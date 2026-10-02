// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Collections.Generic;


namespace Cosmos.Executable.Lua
{
	// lfunc.c of Lua 5.4: upvalues and to-be-closed variables
	internal partial class LuaState
	{
		/* special status to close upvalues preserving the top of the stack */
		internal const ThreadStatus CLOSEKTOP = (ThreadStatus)(-1);

		/*
		** Find and reuse, or create if it does not exist, an upvalue
		** at the given level.
		*/
		private LuaUpvalue F_FindUpval( StkId level )
		{
			var node = OpenUpval.First;
			LinkedListNode<LuaUpvalue> prev = null;
			while( node != null && node.Value.V.Index >= level.Index ) // search for it
			{
				if( node.Value.V == level ) // corresponding upvalue?
					return node.Value; // return it
				prev = node;
				node = node.Next;
			}

			/* not found: create a new upvalue after 'prev' */
			var uv = new LuaUpvalue();
			C_Alloc( LuaGCSize.UpVal );
			uv.V = level; // current value lives in the stack
			if( prev == null )
				OpenUpval.AddFirst( uv );
			else
				OpenUpval.AddAfter( prev, uv );
			return uv;
		}

		/*
		** Call closing method for object 'obj' with error message 'err'. The
		** boolean 'yy' controls whether the call is yieldable.
		** (This function assumes EXTRA_STACK.)
		*/
		private void CallCloseMethod( ref TValue obj, ref TValue err, bool yy )
		{
			StkId top = Top;
			var tm = T_GetTMByObj( ref obj, TMS.TM_CLOSE );
			top.V.SetObj( ref tm.V ); // will call metamethod...
			Stack[top.Index + 1].V.SetObj( ref obj ); // with 'self' as the 1st argument
			Stack[top.Index + 2].V.SetObj( ref err ); // and error msg. as 2nd argument
			Top = Stack[top.Index + 3]; // add function and arguments
			if( yy )
				D_Call( top, 0 );
			else
				D_CallNoYield( top, 0 );
		}

		/*
		** Check whether object at given level has a close metamethod and raise
		** an error if not.
		*/
		private void CheckCloseMth( StkId level )
		{
			var tm = T_GetTMByObj( ref level.V, TMS.TM_CLOSE );
			if( tm.V.TtIsNil() ) // no metamethod?
			{
				int idx = level.Index - CI.FuncIndex; // variable index
				StkId pos;
				string vname = G_FindLocal( CI, idx, out pos );
				if( vname == null ) vname = "?";
				G_RunError( "variable '{0}' got a non-closable value", vname );
			}
		}

		/*
		** Prepare and call a closing method.
		** If status is CLOSEKTOP, the call to the closing method will be pushed
		** at the top of the stack. Otherwise, values can be pushed right after
		** the 'level' of the upvalue being closed, as everything after that
		** won't be used again.
		*/
		private void PrepCallCloseMth( int level, ThreadStatus status, bool yy )
		{
			TValue uv = Stack[level].V; // value being closed
			TValue errobj;
			if( status == CLOSEKTOP )
				errobj = TheNilValue.V; // error object is nil
			else // 'D_SetErrorObj' will set top to level + 2
			{
				D_SetErrorObj( status, Stack[level + 1] ); // set error object
				errobj = Stack[level + 1].V; // error object goes after 'uv'
			}
			CallCloseMethod( ref uv, ref errobj, yy );
		}

		/*
		** Insert a variable in the list of to-be-closed variables.
		*/
		private void F_NewTbcUpval( StkId level )
		{
			Utl.Assert( TbcList.Count == 0 || level.Index > TbcList[TbcList.Count - 1] );
			if( IsFalse( ref level.V ) )
				return; // false doesn't need to be closed
			CheckCloseMth( level ); // value must have a close method
			TbcList.Add( level.Index );
		}

		/*
		** Close all upvalues up to the given stack level.
		*/
		private void F_CloseUpval( int level )
		{
			LinkedListNode<LuaUpvalue> node;
			while( (node = OpenUpval.First) != null && node.Value.V.Index >= level )
			{
				var uv = node.Value;
				OpenUpval.RemoveFirst(); // remove upvalue from 'openupval' list
				uv.Value.V.SetObj( ref uv.V.V ); // move value to upvalue slot
				uv.V = uv.Value; // now current value lives here
			}
		}

		/*
		** Close all upvalues and to-be-closed variables up to the given stack
		** level. Return restored 'level'.
		*/
		private int F_Close( int level, ThreadStatus status, bool yy )
		{
			F_CloseUpval( level ); // first, close the upvalues
			while( TbcList.Count > 0 && TbcList[TbcList.Count - 1] >= level ) // traverse tbc's down to that level
			{
				int tbc = TbcList[TbcList.Count - 1]; // get variable index
				TbcList.RemoveAt( TbcList.Count - 1 ); // remove it from list
				PrepCallCloseMth( tbc, status, yy ); // close variable
			}
			return level;
		}

		/*
		** Look for n-th local variable at line 'line' in function 'func'.
		** Returns null if not found.
		*/
		private static string F_GetLocalName( LuaProto proto, int localNumber, int pc )
		{
			for( int i=0;
				i<proto.LocVars.Count && proto.LocVars[i].StartPc <= pc;
				++i )
			{
				if( pc < proto.LocVars[i].EndPc ) { // is variable active?
					--localNumber;
					if( localNumber == 0 )
						return proto.LocVars[i].VarName;
				}
			}
			return null; // not found
		}

	}

}
