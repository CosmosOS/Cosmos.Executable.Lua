// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using InstructionPtr = Pointer<Instruction>;
	using Exception = System.Exception;

	internal class LuaRuntimeException : Exception
	{
		public ThreadStatus ErrCode { get; private set; }

		public LuaRuntimeException( ThreadStatus errCode )
		{
			ErrCode = errCode;
		}
	}

	// ldo.c of Lua 5.4: stack and call structure
	internal partial class LuaState
	{
		private static bool ErrorStatus( ThreadStatus s )
		{
			return s > ThreadStatus.LUA_YIELD;
		}

		/*
		** {======================================================
		** Error-recovery functions
		** =======================================================
		*/

		internal void D_SetErrorObj( ThreadStatus errCode, StkId oldTop )
		{
			switch( errCode )
			{
				case ThreadStatus.LUA_ERRMEM: // memory error?
					oldTop.V.SetSValue("not enough memory");
					break;

				case ThreadStatus.LUA_OK: // special case only for closing upvalues
					oldTop.V.SetNilValue(); // no error message
					break;

				default: // error message on current top
					Utl.Assert( ErrorStatus( errCode ) );
					oldTop.V.SetObj(ref Stack[Top.Index-1].V);
					break;
			}
			Top = Stack[oldTop.Index+1];
		}

		internal void D_Throw( ThreadStatus errCode )
		{
			throw new LuaRuntimeException( errCode );
		}

		private ThreadStatus D_RawRunProtected<T>( PFuncDelegate<T> func, ref T ud )
		{
			int oldNumCSharpCalls = NumCSharpCalls;
			int oldNumNonYieldable = NumNonYieldable;
			ThreadStatus res = ThreadStatus.LUA_OK;
			try
			{
				func(ref ud);
			}
			catch( Exception e )
			{
				// One clause that tells the exceptions apart: Cosmos kernels up
				// to 3.0.89 enter the first typed clause whatever the type
				NumCSharpCalls = oldNumCSharpCalls;
				NumNonYieldable = oldNumNonYieldable;
				if( e is LuaRuntimeException error )
				{
					res = error.ErrCode;
				}
				else
				{
					// Any other .NET exception, from a C# function such as a
					// host's or from a file operation, is a Lua error with its
					// message: pcall catches it, and it does not tear down the host.
					D_CheckStack( 1 );
					Top.V.SetSValue( LuaText.Encode( e.GetType().Name + ": " + e.Message ) );
					StkId.inc( ref Top );
					res = ThreadStatus.LUA_ERRRUN;
				}
			}
			NumCSharpCalls = oldNumCSharpCalls;
			NumNonYieldable = oldNumNonYieldable;
			return res;
		}

		// os.exit is a Lua error that no pcall keeps: every protected call it
		// reaches gives the state back as the call found it, then raises it
		// again, and the outermost one throws LuaExitException to the host.
		// A new throw at every level rather than one exception to the host:
		// a Cosmos kernel finds the catch of a throw within a few dozen frames
		// only, and rethrows a few times only.
		internal void D_PropagateExit( ThreadStatus status )
		{
			if( G.Host.ProtectedDepth > 0 )
				D_Throw( status );

			int code = G.Host.ExitCode.Value;
			G.Host.ExitCode = null;
			throw new LuaExitException( code );
		}

		/* }====================================================== */

		/*
		** {==================================================================
		** Stack reallocation
		** ===================================================================
		*/

		/* some space for error handling */
		private const int ERRORSTACKSIZE = LuaConf.LUAI_MAXSTACK + 200;

		/* raise an error while running the message handler */
		internal void D_ErrErr()
		{
			Top.V.SetSValue( "error in error handling" );
			StkId.inc( ref Top ); // assume EXTRA_STACK
			D_Throw( ThreadStatus.LUA_ERRERR );
		}

		private int StackSize
		{
			get { return StackLast; }
		}

		/*
		** Reallocate the stack to a new size. The slots keep their indices,
		** which all references to the stack use.
		*/
		private void D_ReallocStack( int newsize )
		{
			Utl.Assert( newsize <= LuaConf.LUAI_MAXSTACK || newsize == ERRORSTACKSIZE );
			int size = newsize + LuaDef.EXTRA_STACK;
			var newStack = new StkId[size];
			int i = 0;
			for( ; i<Stack.Length && i<size; ++i) {
				newStack[i] = Stack[i];
				newStack[i].SetList(newStack);
			}
			for( ; i<size; ++i) {
				newStack[i] = new StkId();
				newStack[i].SetList(newStack);
				newStack[i].SetIndex(i);
				newStack[i].V.SetNilValue(); // erase new segment
			}
			Top = newStack[Top.Index];
			Stack = newStack;
			StackLast = newsize;
		}

		/*
		** Try to grow the stack by at least 'n' elements. When 'raiseerror'
		** is true, raises any error; otherwise, return false in case of errors.
		*/
		private bool D_GrowStack( int n, bool raiseError )
		{
			int size = StackSize;
			if( size > LuaConf.LUAI_MAXSTACK )
			{
				/* if stack is larger than maximum, thread is already using the
				   extra space reserved for errors, that is, thread is handling
				   a stack error; cannot grow further than that. */
				Utl.Assert( StackSize == ERRORSTACKSIZE );
				if( raiseError )
					D_ErrErr(); // error inside message handler
				return false; // if not 'raiseerror', just signal it
			}
			else if( n < LuaConf.LUAI_MAXSTACK ) // avoids arithmetic overflows
			{
				int newsize = 2 * size; // tentative new size
				int needed = Top.Index + n;
				if( newsize > LuaConf.LUAI_MAXSTACK ) // cannot cross the limit
					newsize = LuaConf.LUAI_MAXSTACK;
				if( newsize < needed ) // but must respect what was asked for
					newsize = needed;
				if( newsize <= LuaConf.LUAI_MAXSTACK )
				{
					D_ReallocStack( newsize );
					return true;
				}
			}
			/* else stack overflow */
			/* add extra size to be able to handle the error message */
			D_ReallocStack( ERRORSTACKSIZE );
			if( raiseError )
				G_RunError( "stack overflow" );
			return false;
		}

		private void D_CheckStack( int n )
		{
			if( StackLast - Top.Index <= n )
				D_GrowStack( n, true );
		}

		/*
		** Compute how much of the stack is being used, by computing the
		** maximum top of all call frames in the stack and the current top.
		*/
		private int StackInUse()
		{
			int lim = Top.Index;
			for( int i=0; i<=CI.Index; ++i )
			{
				if( lim < BaseCI[i].TopIndex )
					lim = BaseCI[i].TopIndex;
			}
			int res = lim + 1; // part of stack in use
			if( res < LuaDef.LUA_MINSTACK )
				res = LuaDef.LUA_MINSTACK; // ensure a minimum size
			return res;
		}

		/*
		** If stack size is more than 3 times the current use, reduce that size
		** to twice the current use. (So, the final stack size is at most 2/3 the
		** previous size, and half of its entries are empty.)
		** As a particular case, if stack was handling a stack overflow and now
		** it is not, 'max' (limited by LUAI_MAXSTACK) will be smaller than
		** stacksize (equal to ERRORSTACKSIZE in this case), and so the stack
		** will be reduced to a "regular" size.
		*/
		private void D_ShrinkStack()
		{
			int inuse = StackInUse();
			int max = (inuse > LuaConf.LUAI_MAXSTACK / 3) ? LuaConf.LUAI_MAXSTACK : inuse * 3;
			/* if thread is currently not handling a stack overflow and its
			   size is larger than maximum "reasonable" size, shrink it */
			if( inuse <= LuaConf.LUAI_MAXSTACK && StackSize > max )
			{
				int nsize = (inuse > LuaConf.LUAI_MAXSTACK / 2) ? LuaConf.LUAI_MAXSTACK : inuse * 2;
				D_ReallocStack( nsize );
			}
		}

		internal void D_IncTop()
		{
			D_CheckStack( 1 );
			StkId.inc( ref Top );
		}

		/* }================================================================== */

		/*
		** Call a hook for the given event. Make sure there is a hook to be
		** called. (Both 'L->hook' and 'L->hookmask', which trigger this
		** function, can be changed asynchronously by signals.)
		*/
		internal void D_Hook( int ev, int line, int ftransfer, int ntransfer )
		{
			var hook = Hook;
			if( hook != null && AllowHook ) // make sure there is a hook
			{
				CallStatus mask = CallStatus.CIST_HOOKED;
				CallInfo ci = CI;
				int top = Top.Index; // preserve original 'top'
				int ciTop = ci.TopIndex; // idem for 'ci->top'
				var ar = new LuaDebug();
				ar.Event = ev;
				ar.CurrentLine = line;
				ar.ActiveCIIndex = ci.Index;
				if( ntransfer != 0 )
				{
					mask |= CallStatus.CIST_TRAN; // 'ci' has transfer information
					ci.FTransfer = ftransfer;
					ci.NTransfer = ntransfer;
				}
				if( ci.IsLua && Top.Index < ci.TopIndex )
					Top = Stack[ci.TopIndex]; // protect entire activation register
				D_CheckStack( LuaDef.LUA_MINSTACK ); // ensure minimum stack size
				if( ci.TopIndex < Top.Index + LuaDef.LUA_MINSTACK )
					ci.TopIndex = Top.Index + LuaDef.LUA_MINSTACK;
				AllowHook = false; // cannot call hooks inside a hook
				ci.CallStatus |= mask;
				hook( this, ar );
				Utl.Assert( !AllowHook );
				AllowHook = true;
				ci.TopIndex = ciTop;
				Top = Stack[top];
				ci.CallStatus &= ~mask;
			}
		}

		/*
		** Executes a call hook for Lua functions. This function is called
		** whenever 'hookmask' is not zero, so it checks whether call hooks are
		** active.
		*/
		private void D_HookCall( CallInfo ci )
		{
			OldPc = 0; // set 'oldpc' for new function
			if( (HookMask & LuaDef.LUA_MASKCALL) != 0 ) // is call hook on?
			{
				int ev = (ci.CallStatus & CallStatus.CIST_TAIL) != 0
					? LuaDef.LUA_HOOKTAILCALL
					: LuaDef.LUA_HOOKCALL;
				LuaProto p = Stack[ci.FuncIndex].V.ClLValue().Proto;
				ci.SavedPc.Index++; // hooks assume 'pc' is already incremented
				D_Hook( ev, -1, 1, p.NumParams );
				ci.SavedPc.Index--; // correct 'pc'
			}
		}

		/*
		** Executes a return hook for Lua and C functions and sets/corrects
		** 'oldpc'. (Note that this correction is needed by the line hook, so it
		** is done even when return hooks are off.)
		*/
		private void RetHook( CallInfo ci, int nres )
		{
			if( (HookMask & LuaDef.LUA_MASKRET) != 0 ) // is return hook on?
			{
				int firstres = Top.Index - nres; // index of first result
				int delta = 0; // correction for vararg functions
				if( ci.IsLua )
				{
					LuaProto p = Stack[ci.FuncIndex].V.ClLValue().Proto;
					if( p.IsVarArg )
						delta = ci.NExtraArgs + p.NumParams + 1;
				}
				ci.FuncIndex += delta; // if vararg, back to virtual 'func'
				int ftransfer = firstres - ci.FuncIndex;
				D_Hook( LuaDef.LUA_HOOKRET, -1, ftransfer, nres ); // call it
				ci.FuncIndex -= delta;
			}
			ci = ci.Previous;
			if( ci.IsLua )
				OldPc = ci.SavedPc.Index - 1; // set 'oldpc'
		}

		/*
		** Check whether 'func' has a '__call' metafield. If so, put it in the
		** stack, below original 'func', so that 'luaD_precall' can call it. Raise
		** an error if there is no '__call' metafield.
		*/
		private StkId TryFuncTM( StkId func )
		{
			int funcIndex = func.Index;
			D_CheckStack( 1 ); // space for metamethod
			func = Stack[funcIndex];
			var tm = T_GetTMByObj( ref func.V, TMS.TM_CALL );
			if( tm.V.TtIsNil() )
				G_CallError( func ); // nothing to call
			for( int p = Top.Index; p > funcIndex; p-- ) // open space for metamethod
				Stack[p].V.SetObj( ref Stack[p-1].V );
			StkId.inc( ref Top ); // stack space pre-allocated by the caller
			func.V.SetObj( ref tm.V ); // metamethod is the new function to be called
			return func;
		}

		/*
		** Given 'nres' results at 'firstResult', move 'wanted' of them to 'res'.
		** Handle most typical cases (zero results for commands, one result for
		** expressions, multiple results for tail calls/single parameters)
		** separated.
		*/
		private void MoveResults( int res, int nres, int wanted )
		{
			switch( wanted ) // handle typical cases separately
			{
				case 0: // no values needed
					Top = Stack[res];
					return;
				case 1: // one value needed
					if( nres == 0 ) // no results?
						Stack[res].V.SetNilValue(); // adjust with nil
					else // at least one result
						Stack[res].V.SetObj( ref Stack[Top.Index - nres].V ); // move it to proper place
					Top = Stack[res + 1];
					return;
				case LuaDef.LUA_MULTRET:
					wanted = nres; // we want all results
					break;
				default: // two/more results and/or to-be-closed variables
					if( HasToCloseCFunc( wanted ) ) // to-be-closed variables?
					{
						CI.CallStatus |= CallStatus.CIST_CLSRET; // in case of yields
						CI.NRes = nres;
						res = F_Close( res, CLOSEKTOP, true );
						CI.CallStatus &= ~CallStatus.CIST_CLSRET;
						if( HookMask != 0 ) // if needed, call hook after '__close's
							RetHook( CI, nres );
						wanted = DecodeNResults( wanted );
						if( wanted == LuaDef.LUA_MULTRET )
							wanted = nres; // we want all results
					}
					break;
			}
			/* generic case */
			int firstresult = Top.Index - nres; // index of first result
			if( nres > wanted ) // extra results?
				nres = wanted; // don't need them
			int i;
			for( i = 0; i < nres; i++ ) // move all results to correct place
				Stack[res + i].V.SetObj( ref Stack[firstresult + i].V );
			for( ; i < wanted; i++ ) // complete wanted number of results
				Stack[res + i].V.SetNilValue();
			Top = Stack[res + wanted]; // top points after the last result
		}

		/*
		** Encode the results of a C function that has to-be-closed variables
		** (see 'lua_settop' and 'luaD_poscall')
		*/
		internal static bool HasToCloseCFunc( int n ) { return n < LuaDef.LUA_MULTRET; }
		internal static int CodeNResults( int n ) { return -n - 3; }
		internal static int DecodeNResults( int n ) { return -n - 3; }

		/*
		** Finishes a function call: calls hook if necessary, moves current
		** number of results to proper place, and returns to previous call
		** info. If function has to close variables, hook must be called after
		** that.
		*/
		internal void D_PosCall( CallInfo ci, int nres )
		{
			int wanted = ci.NumResults;
			if( HookMask != 0 && !HasToCloseCFunc( wanted ) )
				RetHook( ci, nres );
			/* move results to proper place */
			MoveResults( ci.FuncIndex, nres, wanted );
			/* function cannot be in any of these cases when returning */
			Utl.Assert( (ci.CallStatus & (CallStatus.CIST_HOOKED | CallStatus.CIST_YPCALL
				| CallStatus.CIST_FIN | CallStatus.CIST_TRAN | CallStatus.CIST_CLSRET)) == 0 );
			CI = ci.Previous; // back to caller (after closing variables)
		}

		private CallInfo NextCI()
		{
			int newIndex = CI.Index + 1;
			if( newIndex >= BaseCI.Length )
			{
				int newLength = BaseCI.Length * 2;
				var newBaseCI = new CallInfo[newLength];
				int i = 0;
				while( i < BaseCI.Length ) {
					newBaseCI[i] = BaseCI[i];
					newBaseCI[i].List = newBaseCI;
					++i;
				}
				while( i < newLength ) {
					var newCI = new CallInfo();
					newBaseCI[i] = newCI;
					newCI.List = newBaseCI;
					newCI.Index = i;
					++i;
				}
				BaseCI = newBaseCI;
				CI = newBaseCI[CI.Index];
			}
			return BaseCI[newIndex];
		}

		private CallInfo PrepCallInfo( int func, int nret, CallStatus mask, int top )
		{
			CallInfo ci = CI = NextCI(); // new frame
			ci.FuncIndex = func;
			ci.NumResults = nret;
			ci.CallStatus = mask;
			ci.TopIndex = top;
			return ci;
		}

		/*
		** precall for C functions
		*/
		private int PreCallC( int func, int nresults, CSharpFunctionDelegate f )
		{
			D_CheckStack( LuaDef.LUA_MINSTACK ); // ensure minimum stack size
			CallInfo ci = PrepCallInfo( func, nresults, CallStatus.CIST_C,
				Top.Index + LuaDef.LUA_MINSTACK );
			Utl.Assert( ci.TopIndex <= StackLast );
			if( (HookMask & LuaDef.LUA_MASKCALL) != 0 )
			{
				int narg = Top.Index - func - 1;
				D_Hook( LuaDef.LUA_HOOKCALL, -1, 1, narg );
			}
			int n = f( this ); // do the actual call
			Utl.ApiCheckNumElems( this, n );
			D_PosCall( ci, n );
			return n;
		}

		private static CSharpFunctionDelegate CSharpFunctionOf( ref TValue v )
		{
			return v.ClIsLcsClosure()
				? (CSharpFunctionDelegate)v.OValue
				: v.ClCsValue().F;
		}

		/*
		** Prepare a function for a tail call, building its call info on top
		** of the current call info. 'narg1' is the number of arguments plus 1
		** (so that it includes the function itself). Return the number of
		** results, if it was a C function, or -1 for a Lua function.
		*/
		private int D_PreTailCall( CallInfo ci, StkId func, int narg1, int delta )
		{
			for( ;; )
			{
				if( func.V.TtIsFunction() )
				{
					if( !func.V.ClIsLuaClosure() ) // C# function
						return PreCallC( func.Index, LuaDef.LUA_MULTRET, CSharpFunctionOf( ref func.V ) );

					/* Lua function */
					LuaProto p = func.V.ClLValue().Proto;
					int fsize = p.MaxStackSize; // frame size
					int nfixparams = p.NumParams;
					int funcIndex = func.Index;
					D_CheckStack( fsize - delta );
					ci.FuncIndex -= delta; // restore 'func' (if vararg)
					for( int i = 0; i < narg1; i++ ) // move down function and arguments
						Stack[ci.FuncIndex + i].V.SetObj( ref Stack[funcIndex + i].V );
					int f = ci.FuncIndex; // moved-down function
					for( ; narg1 <= nfixparams; narg1++ )
						Stack[f + narg1].V.SetNilValue(); // complete missing arguments
					ci.TopIndex = f + 1 + fsize; // top for new function
					Utl.Assert( ci.TopIndex <= StackLast );
					ci.SavedPc = new InstructionPtr( p.Code, 0 ); // starting point
					ci.CallStatus |= CallStatus.CIST_TAIL;
					Top = Stack[f + narg1]; // set top
					return -1;
				}
				/* not a function */
				func = TryFuncTM( func ); // try to get '__call' metamethod
				narg1++;
			}
		}

		/*
		** Prepares the call to a function (C or Lua). For C functions, also do
		** the call. The function to be called is at '*func'.  The arguments
		** are on the stack, right after the function.  Returns the CallInfo
		** to be executed, if it was a Lua function. Otherwise (a C function)
		** returns null, with all the results on the stack, starting at the
		** original function position.
		*/
		private CallInfo D_PreCall( StkId func, int nresults )
		{
			for( ;; )
			{
				if( func.V.TtIsFunction() )
				{
					if( !func.V.ClIsLuaClosure() ) // C# function
					{
						PreCallC( func.Index, nresults, CSharpFunctionOf( ref func.V ) );
						return null;
					}

					/* Lua function */
					LuaProto p = func.V.ClLValue().Proto;
					int narg = Top.Index - func.Index - 1; // number of real arguments
					int nfixparams = p.NumParams;
					int fsize = p.MaxStackSize; // frame size
					int funcIndex = func.Index;
					D_CheckStack( fsize );
					CallInfo ci = PrepCallInfo( funcIndex, nresults, CallStatus.CIST_NONE,
						funcIndex + 1 + fsize );
					ci.SavedPc = new InstructionPtr( p.Code, 0 ); // starting point
					for( ; narg < nfixparams; narg++ )
						StkId.inc( ref Top ).V.SetNilValue(); // complete missing arguments
					Utl.Assert( ci.TopIndex <= StackLast );
					return ci;
				}
				/* not a function */
				func = TryFuncTM( func ); // try to get '__call' metamethod
			}
		}

		/*
		** Call a function (C or Lua) through C. 'inc' can be 1 (increment
		** number of recursive invocations in the C stack) or 0 (lua_resume
		** counted it already); 'nonYieldable' also counts the call as a
		** non-yieldable one.
		** This function can be called with some use of EXTRA_STACK, so it should
		** check the stack before doing anything else. 'luaD_precall' already
		** does that.
		*/
		private void CCall( StkId func, int nResults, int inc, bool nonYieldable )
		{
			int funcIndex = func.Index;
			NumCSharpCalls += inc;
			if( nonYieldable )
				NumNonYieldable++;
			if( NumCSharpCalls >= LuaLimits.LUAI_MAXCCALLS )
			{
				D_CheckStack( 0 ); // free any use of EXTRA_STACK
				E_CheckCStack();
			}
			CallInfo ci = D_PreCall( Stack[funcIndex], nResults );
			if( ci != null ) // Lua function?
			{
				ci.CallStatus = CallStatus.CIST_FRESH; // mark that it is a "fresh" execute
				V_Execute( ci ); // call it
			}
			if( nonYieldable )
				NumNonYieldable--;
			NumCSharpCalls -= inc;
		}

		/*
		** External interface for 'ccall'
		*/
		internal void D_Call( StkId func, int nResults )
		{
			CCall( func, nResults, 1, false );
		}

		/*
		** Similar to 'luaD_call', but does not allow yields during the call.
		*/
		internal void D_CallNoYield( StkId func, int nResults )
		{
			CCall( func, nResults, 1, true );
		}

		/*
		** Finish the job of 'lua_pcallk' after it was interrupted by an yield.
		** (The caller, 'finishCcall', does the final call to 'adjustresults'.)
		** The main job is to complete the 'luaD_pcall' called by 'lua_pcallk'.
		** If a '__close' method yields here, eventually control will be back
		** to 'finishCcall' (when that '__close' method finally returns) and
		** 'finishpcallk' will run again and close any still pending '__close'
		** methods. Similarly, if a '__close' method errs, 'precover' calls
		** 'unroll' which calls ''finishCcall' and we are back here again, to
		** close any pending '__close' methods.
		** Note that, up to the call to 'luaF_close', the corresponding
		** 'CallInfo' is not modified, so that this repeated run works like the
		** first one (except that it has at least one less '__close' to do). In
		** particular, field CIST_RECST preserves the error status across these
		** multiple runs, changing only if there is a new error.
		*/
		private ThreadStatus FinishPCallK( CallInfo ci )
		{
			ThreadStatus status = ci.GetRecSt(); // get original status
			if( status == ThreadStatus.LUA_OK ) // no error?
				status = ThreadStatus.LUA_YIELD; // was interrupted by an yield
			else // error
			{
				int func = ci.FuncIdx;
				AllowHook = (ci.CallStatus & CallStatus.CIST_OAH) != 0; // restore 'allowhook'
				func = F_Close( func, status, true ); // can yield or raise an error
				D_SetErrorObj( status, Stack[func] );
				D_ShrinkStack(); // restore stack size in case of overflow
				ci.SetRecSt( ThreadStatus.LUA_OK ); // clear original status
			}
			ci.CallStatus &= ~CallStatus.CIST_YPCALL;
			ErrFunc = ci.OldErrFunc;
			/* if it is here, there were errors or yields; unlike 'lua_pcallk',
			   do not change status */
			return status;
		}

		/*
		** Completes the execution of a C function interrupted by an yield.
		** The interruption must have happened while the function was either
		** closing its tbc variables in 'moveresults' or executing
		** 'lua_callk'/'lua_pcallk'. In the first case, it just redoes
		** 'luaD_poscall'. In the second case, the call to 'finishpcallk'
		** finishes the interrupted execution of 'lua_pcallk'.  After that, it
		** calls the continuation of the interrupted function and finally it
		** completes the job of the 'luaD_call' that called the function.  In
		** the call to 'adjustresults', we do not know the number of results
		** of the function called by 'lua_callk'/'lua_pcallk', so we are
		** conservative and use LUA_MULTRET (always adjust).
		*/
		private void FinishCCall( CallInfo ci )
		{
			int n; // actual number of results from C function
			if( (ci.CallStatus & CallStatus.CIST_CLSRET) != 0 ) // was returning?
			{
				Utl.Assert( HasToCloseCFunc( ci.NumResults ) );
				n = ci.NRes; // just redo 'luaD_poscall'
				/* don't need to reset CIST_CLSRET, as it will be set again anyway */
			}
			else
			{
				ThreadStatus status = ThreadStatus.LUA_YIELD; // default if there were no errors
				/* must have a continuation and must be able to call it */
				Utl.Assert( ci.ContinueFunc != null && Yieldable() );
				if( (ci.CallStatus & CallStatus.CIST_YPCALL) != 0 ) // was inside a 'lua_pcallk'?
					status = FinishPCallK( ci ); // finish it
				AdjustResults( LuaDef.LUA_MULTRET ); // finish 'lua_callk'
				n = CallContinuation( ci, status ); // call continuation
				Utl.ApiCheckNumElems( this, n );
			}
			D_PosCall( ci, n ); // finish 'luaD_call'
		}

		// The continuation of a C# function, which learns the status and the
		// context through 'GetContext'
		private int CallContinuation( CallInfo ci, ThreadStatus status )
		{
			ci.Status = status;
			ci.CallStatus |= CallStatus.CIST_YIELDED;
			return ci.ContinueFunc( this );
		}

		/*
		** Executes "full continuation" (everything in the stack) of a
		** previously interrupted coroutine until the stack is empty (or another
		** interruption long-jumps out of the loop).
		*/
		private void Unroll()
		{
			CallInfo ci;
			while( (ci = CI).Index != 0 ) // something in the stack
			{
				if( !ci.IsLua ) // C function?
					FinishCCall( ci ); // complete its execution
				else // Lua function
				{
					V_FinishOp(); // finish interrupted instruction
					V_Execute( ci ); // execute down to higher C 'boundary'
				}
			}
		}
		private struct UnrollParam
		{
			public LuaState L;
		}
		private static void UnrollWrap( ref UnrollParam param )
		{
			param.L.Unroll();
		}
		private static PFuncDelegate<UnrollParam> DG_Unroll = UnrollWrap;

		/*
		** Try to find a suspended protected call (a "recover point") for the
		** given thread.
		*/
		private CallInfo FindPCall()
		{
			for( int i = CI.Index; i >= 0; --i ) // search for a pcall
			{
				var ci = BaseCI[i];
				if( (ci.CallStatus & CallStatus.CIST_YPCALL) != 0 )
					return ci;
			}
			return null; // no pending pcall
		}

		/*
		** Signal an error in the call to 'lua_resume', not in the execution
		** of the coroutine itself. (Such errors should not be handled by any
		** coroutine error handler and should not kill the coroutine.)
		*/
		private ThreadStatus ResumeError( string msg, int narg )
		{
			Top = Stack[Top.Index - narg]; // remove args from the stack
			Top.V.SetSValue( msg ); // push error message
			ApiIncrTop();
			return ThreadStatus.LUA_ERRRUN;
		}

		/*
		** Do the work for 'lua_resume' in protected mode. Most of the work
		** depends on the status of the coroutine: initial state, suspended
		** inside a hook, or regularly suspended (optionally with a continuation
		** function), plus erroneous cases: non-suspended coroutine or dead
		** coroutine.
		*/
		private void Resume( int n )
		{
			int firstArg = Top.Index - n; // first argument
			CallInfo ci = CI;
			if( Status == ThreadStatus.LUA_OK ) // starting a coroutine?
				CCall( Stack[firstArg - 1], LuaDef.LUA_MULTRET, 0, false ); // just call its body
			else // resuming from previous yield
			{
				Utl.Assert( Status == ThreadStatus.LUA_YIELD );
				Status = ThreadStatus.LUA_OK; // mark that it is running (again)
				if( ci.IsLua ) // yielded inside a hook?
				{
					/* undo increment made by 'luaG_traceexec': instruction was not
					   executed yet */
					Utl.Assert( (ci.CallStatus & CallStatus.CIST_HOOKYIELD) != 0 );
					ci.SavedPc.Index--;
					Top = Stack[firstArg]; // discard arguments
					V_Execute( ci ); // just continue running Lua code
				}
				else // 'common' yield
				{
					if( ci.ContinueFunc != null ) // does it have a continuation function?
					{
						n = CallContinuation( ci, ThreadStatus.LUA_YIELD ); // call continuation
						Utl.ApiCheckNumElems( this, n );
					}
					D_PosCall( ci, n ); // finish 'luaD_call'
				}
				Unroll(); // run continuation
			}
		}
		private struct ResumeParam
		{
			public LuaState L;
			public int NArgs;
		}
		private static void ResumeWrap( ref ResumeParam param )
		{
			param.L.Resume( param.NArgs );
		}
		private static PFuncDelegate<ResumeParam> DG_Resume = ResumeWrap;

		/*
		** Unrolls a coroutine in protected mode while there are recoverable
		** errors, that is, errors inside a protected call. (Any error
		** interrupts 'unroll', and this loop protects it again so it can
		** continue.) Stops with a normal end (status == LUA_OK), an yield
		** (status == LUA_YIELD), or an unprotected error ('findpcall' doesn't
		** find a recover point).
		*/
		private ThreadStatus PRecover( ThreadStatus status )
		{
			CallInfo ci;
			// (no recover point for os.exit, which ends the script)
			while( ErrorStatus( status ) && !G.Host.ExitCode.HasValue && (ci = FindPCall()) != null )
			{
				CI = ci; // go down to recovery functions
				ci.SetRecSt( status ); // status to finish 'pcall'
				var param = new UnrollParam();
				param.L = this;
				status = D_RawRunProtected( DG_Unroll, ref param );
			}
			return status;
		}

		internal ThreadStatus D_Resume( LuaState from, int nargs, out int nresults )
		{
			nresults = 0;
			if( Status == ThreadStatus.LUA_OK ) // may be starting a coroutine
			{
				if( CI.Index != 0 ) // not in base level?
					return ResumeError( "cannot resume non-suspended coroutine", nargs );
				else if( Top.Index - (CI.FuncIndex + 1) == nargs ) // no function?
					return ResumeError( "cannot resume dead coroutine", nargs );
			}
			else if( Status != ThreadStatus.LUA_YIELD ) // ended with errors?
				return ResumeError( "cannot resume dead coroutine", nargs );
			NumCSharpCalls = (from != null) ? from.NumCSharpCalls : 0;
			NumNonYieldable = 0; // a coroutine starts yieldable, whatever 'from' is
			if( NumCSharpCalls >= LuaLimits.LUAI_MAXCCALLS )
				return ResumeError( "C stack overflow", nargs );
			NumCSharpCalls++;
			Utl.ApiCheckNumElems( this, (Status == ThreadStatus.LUA_OK) ? nargs + 1 : nargs );
			var param = new ResumeParam();
			param.L = this;
			param.NArgs = nargs;
			ThreadStatus status = D_RawRunProtected( DG_Resume, ref param );
			/* continue running after recoverable errors */
			status = PRecover( status );
			if( !ErrorStatus( status ) )
				Utl.Assert( status == Status ); // normal end or yield
			else // unrecoverable error
			{
				Status = status; // mark thread as 'dead'
				D_SetErrorObj( status, Top ); // push error message
				CI.TopIndex = Top.Index;
			}
			nresults = (status == ThreadStatus.LUA_YIELD) ? CI.NYield
				: Top.Index - (CI.FuncIndex + 1);
			return status;
		}

		internal int D_Yield( int nresults, int context, CSharpFunctionDelegate k )
		{
			CallInfo ci = CI;
			Utl.ApiCheckNumElems( this, nresults );
			if( !Yieldable() )
			{
				if( this != G.MainThread )
					G_RunError( "attempt to yield across a C-call boundary" );
				else
					G_RunError( "attempt to yield from outside a coroutine" );
			}
			Status = ThreadStatus.LUA_YIELD;
			ci.NYield = nresults; // save number of results
			if( ci.IsLua ) // inside a hook?
			{
				Utl.Assert( !ci.IsLuaCode );
				Utl.ApiCheck( nresults == 0, "hooks cannot yield values" );
				Utl.ApiCheck( k == null, "hooks cannot continue after yielding" );
			}
			else
			{
				if( (ci.ContinueFunc = k) != null ) // is there a continuation?
					ci.Context = context; // save context
				D_Throw( ThreadStatus.LUA_YIELD );
			}
			Utl.Assert( (ci.CallStatus & CallStatus.CIST_HOOKED) != 0 ); // must be inside a hook
			return 0; // return to 'luaD_hook'
		}

		/*
		** Auxiliary structure to call 'luaF_close' in protected mode.
		*/
		private struct CloseP
		{
			public LuaState L;
			public int Level;
			public ThreadStatus Status;
		}

		/*
		** Auxiliary function to call 'luaF_close' in protected mode.
		*/
		private static void ClosePAux( ref CloseP pcl )
		{
			pcl.L.F_Close( pcl.Level, pcl.Status, false );
		}
		private static PFuncDelegate<CloseP> DG_ClosePAux = ClosePAux;

		/*
		** Calls 'luaF_close' in protected mode. Return the original status
		** or, in case of errors, the new status.
		*/
		internal ThreadStatus D_CloseProtected( int level, ThreadStatus status )
		{
			int oldCIIndex = CI.Index;
			bool oldAllowHook = AllowHook;
			for( ;; ) // keep closing upvalues until no more errors
			{
				if( G.Host.ExitCode.HasValue )
				{
					// os.exit leaves without running '__close' methods, as
					// the C exit of the reference implementation does
					F_CloseUpval( level );
					while( TbcList.Count > 0 && TbcList[TbcList.Count - 1] >= level )
						TbcList.RemoveAt( TbcList.Count - 1 );
					return status;
				}
				var pcl = new CloseP();
				pcl.L = this;
				pcl.Level = level;
				pcl.Status = status;
				ThreadStatus st = D_RawRunProtected( DG_ClosePAux, ref pcl );
				if( st == ThreadStatus.LUA_OK ) // no more errors?
					return pcl.Status;
				else // an error occurred; restore saved state and repeat
				{
					CI = BaseCI[oldCIIndex];
					AllowHook = oldAllowHook;
					status = st;
				}
			}
		}

		/*
		** Call the C function 'func' in protected mode, restoring basic
		** thread information ('allowhook', etc.) and in particular
		** its stack level in case of errors.
		*/
		private ThreadStatus D_PCall<T>( PFuncDelegate<T> func, ref T ud,
			int oldTopIndex, int ef )
		{
			int oldCIIndex = CI.Index;
			bool oldAllowHook = AllowHook;
			int oldErrFunc = ErrFunc;
			ErrFunc = ef;
			G.Host.ProtectedDepth++;
			ThreadStatus status = D_RawRunProtected<T>( func, ref ud );
			G.Host.ProtectedDepth--;
			if( status != ThreadStatus.LUA_OK ) // an error occurred?
			{
				CI = BaseCI[oldCIIndex];
				AllowHook = oldAllowHook;
				status = D_CloseProtected( oldTopIndex, status );
				D_SetErrorObj( status, Stack[oldTopIndex] );
				D_ShrinkStack(); // restore stack size in case of overflow
			}
			ErrFunc = oldErrFunc;
			if( status != ThreadStatus.LUA_OK && G.Host.ExitCode.HasValue )
				D_PropagateExit( status );
			return status;
		}
	}

}
