// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	/*
	* WARNING: if you change the order of this enumeration,
	* grep "ORDER TM" and "ORDER OP"
	*/
	internal enum TMS
	{
		TM_INDEX,
		TM_NEWINDEX,
		TM_GC,
		TM_MODE,
		TM_LEN,
		TM_EQ,	/* last tag method with fast access */
		TM_ADD,
		TM_SUB,
		TM_MUL,
		TM_MOD,
		TM_POW,
		TM_DIV,
		TM_IDIV,
		TM_BAND,
		TM_BOR,
		TM_BXOR,
		TM_SHL,
		TM_SHR,
		TM_UNM,
		TM_BNOT,
		TM_LT,
		TM_LE,
		TM_CONCAT,
		TM_CALL,
		TM_CLOSE,
		TM_N		/* number of elements in the enum */
	}

	// ltm.c of Lua 5.5: tag methods
	internal partial class LuaState
	{
		// luaT_eventname, ORDER TM
		private static readonly string[] TagMethodNames = {
			"__index", "__newindex",
			"__gc", "__mode", "__len", "__eq",
			"__add", "__sub", "__mul", "__mod", "__pow",
			"__div", "__idiv",
			"__band", "__bor", "__bxor", "__shl", "__shr",
			"__unm", "__bnot", "__lt", "__le",
			"__concat", "__call", "__close",
		};

		internal static string GetTagMethodName( TMS tm )
		{
			return TagMethodNames[(int)tm];
		}

		/*
		** Return the name of the type of an object. For tables and userdata
		** with metatable, use their '__name' metafield, if present.
		*/
		internal string ObjTypeName( ref TValue o )
		{
			LuaTable mt = null;
			if( o.TtIsTable() )
				mt = o.HValue().MetaTable;
			else if( o.Tt == (int)LuaType.LUA_TUSERDATA )
				mt = o.RawUValue().MetaTable;
			if( mt != null )
			{
				var name = mt.GetStr( "__name" );
				if( name.V.TtIsString() ) // is '__name' a string?
					return name.V.SValue(); // use it as type name
			}
			return TypeName( (LuaType)o.BaseTt() ); // else use standard type name
		}

		/*
		** function to be used with macro "fasttm": optimized for absence of
		** tag methods
		*/
		private StkId T_GetTM( LuaTable events, TMS tm )
		{
			var res = events.GetStr( GetTagMethodName( tm ) );
			Utl.Assert( tm <= TMS.TM_EQ );
			if( res.V.TtIsNil() ) // no tag method?
			{
				events.NoTagMethodFlags |= 1u << (int)tm; // cache this fact
				return null;
			}
			else
				return res;
		}

		private StkId FastTM( LuaTable et, TMS tm )
		{
			if( et == null )
				return null;
			if( (et.NoTagMethodFlags & (1u << (int)tm)) != 0u )
				return null;
			return T_GetTM( et, tm );
		}

		private StkId T_GetTMByObj( ref TValue o, TMS tm )
		{
			LuaTable mt;
			switch( o.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
					mt = o.HValue().MetaTable;
					break;
				case (int)LuaType.LUA_TUSERDATA:
					mt = o.RawUValue().MetaTable;
					break;
				default:
					mt = G.MetaTables[o.BaseTt()];
					break;
			}
			return (mt != null)
				 ? mt.GetStr( GetTagMethodName( tm ) )
				 : TheNilValue;
		}

		private void T_CallTM( ref TValue f, ref TValue p1, ref TValue p2, ref TValue p3 )
		{
			StkId func = Top;
			func.V.SetObj( ref f ); // push function (assume EXTRA_STACK)
			Stack[func.Index + 1].V.SetObj( ref p1 ); // 1st argument
			Stack[func.Index + 2].V.SetObj( ref p2 ); // 2nd argument
			Stack[func.Index + 3].V.SetObj( ref p3 ); // 3rd argument
			Top = Stack[func.Index + 4];
			/* metamethod may yield only when called from Lua code */
			if( CI.IsLuaCode )
				D_Call( func, 0 );
			else
				D_CallNoYield( func, 0 );
		}

		private void T_CallTMRes( ref TValue f, ref TValue p1, ref TValue p2, int res )
		{
			StkId func = Top;
			func.V.SetObj( ref f ); // push function (assume EXTRA_STACK)
			Stack[func.Index + 1].V.SetObj( ref p1 ); // 1st argument
			Stack[func.Index + 2].V.SetObj( ref p2 ); // 2nd argument
			Top = Stack[func.Index + 3];
			/* metamethod may yield only when called from Lua code */
			if( CI.IsLuaCode )
				D_Call( func, 1 );
			else
				D_CallNoYield( func, 1 );
			Top = Stack[Top.Index - 1];
			Stack[res].V.SetObj( ref Top.V ); // move result to its place
		}

		private bool CallBinTM( ref TValue p1, ref TValue p2, int res, TMS ev )
		{
			var tm = T_GetTMByObj( ref p1, ev ); // try first operand
			if( tm.V.TtIsNil() )
				tm = T_GetTMByObj( ref p2, ev ); // try second operand
			if( tm.V.TtIsNil() )
				return false;
			T_CallTMRes( ref tm.V, ref p1, ref p2, res );
			return true;
		}

		private void T_TryBinTM( ref TValue p1, ref TValue p2, int res, TMS ev )
		{
			if( !CallBinTM( ref p1, ref p2, res, ev ) )
			{
				switch( ev )
				{
					case TMS.TM_BAND: case TMS.TM_BOR: case TMS.TM_BXOR:
					case TMS.TM_SHL: case TMS.TM_SHR: case TMS.TM_BNOT: {
						if( p1.TtIsNumber() && p2.TtIsNumber() )
							G_ToIntError( ref p1, ref p2 );
						else
							G_OpIntError( ref p1, ref p2, "perform bitwise operation on" );
						break;
					}
					default:
						G_OpIntError( ref p1, ref p2, "perform arithmetic on" );
						break;
				}
			}
		}

		private void T_TryConcatTM()
		{
			StkId top = Top;
			if( !CallBinTM( ref Stack[top.Index - 2].V, ref Stack[top.Index - 1].V,
					top.Index - 2, TMS.TM_CONCAT ) )
				G_ConcatError( ref Stack[top.Index - 2].V, ref Stack[top.Index - 1].V );
		}

		private void T_TryBinAssocTM( ref TValue p1, ref TValue p2, bool flip,
			int res, TMS ev )
		{
			if( flip )
				T_TryBinTM( ref p2, ref p1, res, ev );
			else
				T_TryBinTM( ref p1, ref p2, res, ev );
		}

		private void T_TryBiniTM( ref TValue p1, long i2, bool flip, int res, TMS ev )
		{
			var aux = new TValue();
			aux.SetIValue( i2 );
			T_TryBinAssocTM( ref p1, ref aux, flip, res, ev );
		}

		/*
		** Calls an order tag method.
		*/
		private bool T_CallOrderTM( ref TValue p1, ref TValue p2, TMS ev )
		{
			if( CallBinTM( ref p1, ref p2, Top.Index, ev ) ) // try original event
				return !IsFalse( ref Top.V );
			G_OrderError( ref p1, ref p2 ); // no metamethod found
			return false; // to avoid warnings
		}

		private bool T_CallOrderiTM( ref TValue p1, int v2, bool flip, bool isfloat, TMS ev )
		{
			var aux = new TValue();
			if( isfloat )
				aux.SetFltValue( (double)v2 );
			else
				aux.SetIValue( v2 );
			if( flip ) // arguments were exchanged?
				return T_CallOrderTM( ref aux, ref p1, ev ); // correct them
			else
				return T_CallOrderTM( ref p1, ref aux, ev );
		}

		/*
		** Create a vararg table at the top of the stack, with 'n' elements
		** starting at 'f'.
		*/
		private void CreateVarargTab( int f, int n )
		{
			var t = new LuaTable( this );
			Top.V.SetHValue( t );
			StkId.inc( ref Top );
			t.Resize( n, 1 );
			var value = new TValue();
			value.SetIValue( n ); // value is n
			var key = new TValue();
			key.SetSValue( "n" ); // key is "n"
			t.Set( ref key, ref value ); // t.n = n
			for( int i = 0; i < n; i++ )
				t.SetInt( i + 1, ref Stack[f + i].V );
			C_CheckGC();
		}

		/*
		** initial stack:  func arg1 ... argn extra1 ...
		**                 ^ ci->func                    ^ L->top
		** final stack: func nil ... nil extra1 ... func arg1 ... argn
		**                                          ^ ci->func
		*/
		private void BuildHiddenArgs( CallInfo ci, LuaProto p,
			int totalargs, int nfixparams, int nextra )
		{
			ci.NExtraArgs = nextra;
			D_CheckStack( p.MaxStackSize + 1 );
			/* copy function to the top of the stack, after extra arguments */
			StkId.inc( ref Top ).V.SetObj( ref Stack[ci.FuncIndex].V );
			/* move fixed parameters to after the copied function */
			for( int i = 1; i <= nfixparams; i++ )
			{
				StkId.inc( ref Top ).V.SetObj( ref Stack[ci.FuncIndex + i].V );
				Stack[ci.FuncIndex + i].V.SetNilValue(); // erase original parameter (for GC)
			}
			ci.FuncIndex += totalargs + 1; // 'func' now lives after hidden arguments
			ci.TopIndex += totalargs + 1;
		}

		private void T_AdjustVarargs( CallInfo ci, LuaProto p )
		{
			int totalargs = Top.Index - ci.FuncIndex - 1;
			int nfixparams = p.NumParams;
			int nextra = totalargs - nfixparams; // number of extra arguments
			if( (p.Flag & LuaProto.PF_VATAB) != 0 ) // does it need a vararg table?
			{
				Utl.Assert( (p.Flag & LuaProto.PF_VAHID) == 0 );
				CreateVarargTab( ci.FuncIndex + nfixparams + 1, nextra );
				/* move table to proper place (last parameter) */
				Stack[ci.FuncIndex + nfixparams + 1].V.SetObj( ref Stack[Top.Index - 1].V );
			}
			else // no table
			{
				Utl.Assert( (p.Flag & LuaProto.PF_VAHID) != 0 );
				BuildHiddenArgs( ci, p, totalargs, nfixparams, nextra );
				/* set vararg parameter to nil */
				Stack[ci.FuncIndex + nfixparams + 1].V.SetNilValue();
				Utl.Assert( Top.Index <= ci.TopIndex && ci.TopIndex <= StackLast );
			}
		}

		private void T_GetVararg( CallInfo ci, StkId ra, ref TValue rc )
		{
			int nextra = ci.NExtraArgs;
			long n;
			if( V_ToIntegerNS( ref rc, out n, F2Imod.F2Ieq ) ) // integral value?
			{
				if( unchecked((ulong)n - 1) < (ulong)nextra )
				{
					ra.V.SetObj( ref Stack[ci.FuncIndex - nextra + (int)n - 1].V );
					return;
				}
			}
			else if( rc.TtIsString() ) // string value?
			{
				if( rc.SValue() == "n" ) // key is "n"?
				{
					ra.V.SetIValue( nextra );
					return;
				}
			}
			ra.V.SetNilValue(); // else produce nil
		}

		/*
		** Get the number of extra arguments in a vararg function. If vararg
		** table has been optimized away, that number is in the call info.
		** Otherwise, get the field 'n' from the vararg table and check that it
		** has a proper value (non-negative integer not larger than the stack
		** limit).
		*/
		private int GetNumArgs( CallInfo ci, LuaTable h )
		{
			if( h == null ) // no vararg table?
				return ci.NExtraArgs;
			else
			{
				StkId res = h.GetStr( "n" );
				if( res.V.Tt != TValue.LUA_TNUMINT ||
					unchecked((ulong)res.V.IValue()) > (ulong)(int.MaxValue / 2) )
					G_RunError( "vararg table has no proper 'n'" );
				return (int)res.V.IValue();
			}
		}

		/*
		** Get 'wanted' vararg arguments and put them in 'where'. 'vatab' is
		** the register of the vararg table or -1 if there is no vararg table.
		*/
		private void T_GetVarargs( CallInfo ci, int where, int wanted, int vatab )
		{
			LuaTable h = (vatab < 0) ? null : Stack[ci.FuncIndex + vatab + 1].V.HValue();
			int nargs = GetNumArgs( ci, h ); // number of available vararg args.
			int i, touse; // 'touse' is minimum between 'wanted' and 'nargs'
			if( wanted < 0 )
			{
				touse = wanted = nargs; // get all extra arguments available
				D_CheckStack( nargs ); // ensure stack space
				Top = Stack[where + nargs]; // next instruction will need top
			}
			else
				touse = (nargs > wanted) ? wanted : nargs;
			if( h == null ) // no vararg table?
			{
				for( i = 0; i < touse; i++ ) // get vararg values from the stack
					Stack[where + i].V.SetObj( ref Stack[ci.FuncIndex - nargs + i].V );
			}
			else // get vararg values from vararg table
			{
				for( i = 0; i < touse; i++ )
					Stack[where + i].V.SetObj( ref h.GetInt( i + 1 ).V );
			}
			for( ; i < wanted; i++ ) // complete required results with nil
				Stack[where + i].V.SetNilValue();
		}

	}

}
