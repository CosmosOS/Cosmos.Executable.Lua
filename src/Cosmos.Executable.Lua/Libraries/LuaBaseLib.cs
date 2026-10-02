// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using System.Collections.Generic;
	using StringBuilder = System.Text.StringBuilder;
	using Char = System.Char;
	using Int32 = System.Int32;

	internal static class LuaBaseLib
	{
		internal static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "assert", 		LuaBaseLib.B_Assert ),
				new NameFuncPair( "collectgarbage", LuaBaseLib.B_CollectGarbage ),
				new NameFuncPair( "dofile", 		LuaBaseLib.B_DoFile ),
				new NameFuncPair( "error", 			LuaBaseLib.B_Error ),
				new NameFuncPair( "ipairs", 		LuaBaseLib.B_Ipairs ),
				new NameFuncPair( "loadfile", 		LuaBaseLib.B_LoadFile ),
				new NameFuncPair( "load", 			LuaBaseLib.B_Load ),
				new NameFuncPair( "next", 			LuaBaseLib.B_Next ),
				new NameFuncPair( "pairs", 			LuaBaseLib.B_Pairs ),
				new NameFuncPair( "pcall", 			LuaBaseLib.B_PCall ),
				new NameFuncPair( "print", 			LuaBaseLib.B_Print ),
				new NameFuncPair( "rawequal", 		LuaBaseLib.B_RawEqual ),
				new NameFuncPair( "rawlen", 		LuaBaseLib.B_RawLen ),
				new NameFuncPair( "rawget", 		LuaBaseLib.B_RawGet ),
				new NameFuncPair( "rawset", 		LuaBaseLib.B_RawSet ),
				new NameFuncPair( "select", 		LuaBaseLib.B_Select ),
				new NameFuncPair( "getmetatable", 	LuaBaseLib.B_GetMetaTable ),
				new NameFuncPair( "setmetatable", 	LuaBaseLib.B_SetMetaTable ),
				new NameFuncPair( "tonumber", 		LuaBaseLib.B_ToNumber ),
				new NameFuncPair( "tostring", 		LuaBaseLib.B_ToString ),
				new NameFuncPair( "type", 			LuaBaseLib.B_Type ),
				new NameFuncPair( "xpcall", 		LuaBaseLib.B_XPCall ),
				new NameFuncPair( "warn", 			LuaBaseLib.B_Warn ),
			};

			// set global _G
			lua.PushGlobalTable();
			lua.PushGlobalTable();
			lua.SetField( -2, "_G" );

			// open lib into global lib
			lua.L_SetFuncs( define, 0 );
			// lua.RegisterGlobalFunc( "type", 	LuaBaseLib.B_Type );
			// lua.RegisterGlobalFunc( "pairs", 	LuaBaseLib.B_Pairs );
			// lua.RegisterGlobalFunc( "ipairs", 	LuaBaseLib.B_Ipairs );
			// lua.RegisterGlobalFunc( "print",	LuaBaseLib.B_Print );
			// lua.RegisterGlobalFunc( "tostring",	LuaBaseLib.B_ToString );

			lua.PushString( LuaDef.LUA_VERSION );
			lua.SetField( -2, "_VERSION" );

			return 1;
		}

		public static int B_Assert( ILuaState lua )
		{
			if( lua.ToBoolean( 1 ) ) // condition is true?
				return lua.GetTop(); // return all arguments

			// error
			lua.L_CheckAny( 1 ); // there must be a condition
			lua.Remove( 1 ); // remove it
			lua.PushString( "assertion failed!" ); // default message
			lua.SetTop( 1 ); // leave only message (default if no other one)
			return B_Error( lua ); // call 'error'
		}

		/*
		** Creates a warning with all given arguments.
		** Check first for errors; otherwise an error may interrupt
		** the composition of a warning, leaving it unfinished.
		*/
		public static int B_Warn( ILuaState lua )
		{
			int n = lua.GetTop(); // number of arguments
			lua.L_CheckString( 1 ); // at least one argument
			for( int i = 2; i <= n; i++ )
				lua.L_CheckString( i ); // make sure all arguments are strings
			for( int i = 1; i < n; i++ ) // compose warning
				lua.Warning( lua.ToString( i ), true );
			lua.Warning( lua.ToString( n ), false ); // close warning
			return 0;
		}

		private static int PushMode( ILuaState lua, int oldmode )
		{
			if( oldmode == -1 )
				lua.PushNil(); // invalid call to 'lua_gc'
			else
				lua.PushString( (oldmode == (int)LuaGCOption.LUA_GCINC) ? "incremental" : "generational" );
			return 1;
		}

		private static readonly string[] GCOptionNames = new string[] { "stop", "restart", "collect",
			"count", "step", "isrunning", "generational", "incremental",
			"param" };
		private static readonly LuaGCOption[] GCOptions = new LuaGCOption[] { LuaGCOption.LUA_GCSTOP,
			LuaGCOption.LUA_GCRESTART, LuaGCOption.LUA_GCCOLLECT,
			LuaGCOption.LUA_GCCOUNT, LuaGCOption.LUA_GCSTEP, LuaGCOption.LUA_GCISRUNNING,
			LuaGCOption.LUA_GCGEN, LuaGCOption.LUA_GCINC, LuaGCOption.LUA_GCPARAM };
		private static readonly string[] GCParamNames = new string[] {
			"minormul", "majorminor", "minormajor",
			"pause", "stepmul", "stepsize" };
		private static readonly LuaGCParam[] GCParams = new LuaGCParam[] {
			LuaGCParam.LUA_GCPMINORMUL, LuaGCParam.LUA_GCPMAJORMINOR, LuaGCParam.LUA_GCPMINORMAJOR,
			LuaGCParam.LUA_GCPPAUSE, LuaGCParam.LUA_GCPSTEPMUL, LuaGCParam.LUA_GCPSTEPSIZE };

		public static int B_CollectGarbage( ILuaState lua )
		{
			var L = (LuaState)lua;
			var o = GCOptions[lua.L_CheckOption( 1, "collect", GCOptionNames )];
			switch( o )
			{
				case LuaGCOption.LUA_GCCOUNT:
				{
					int k = L.C_GC( o );
					int b = L.C_GC( LuaGCOption.LUA_GCCOUNTB );
					if( k == -1 ) break;
					lua.PushNumber( (double)k + ((double)b / 1024) );
					return 1;
				}
				case LuaGCOption.LUA_GCSTEP:
				{
					long n = lua.L_OptInteger( 2, 0 );
					int res = L.C_GC( o, n );
					if( res == -1 ) break;
					lua.PushBoolean( res != 0 );
					return 1;
				}
				case LuaGCOption.LUA_GCISRUNNING:
				{
					int res = L.C_GC( o );
					if( res == -1 ) break;
					lua.PushBoolean( res != 0 );
					return 1;
				}
				case LuaGCOption.LUA_GCGEN:
				case LuaGCOption.LUA_GCINC:
				{
					return PushMode( lua, L.C_GC( o ) );
				}
				case LuaGCOption.LUA_GCPARAM:
				{
					var p = GCParams[lua.L_CheckOption( 2, null, GCParamNames )];
					long value = lua.L_OptInteger( 3, -1 );
					int res = L.C_GC( o, (long)p, (int)value );
					if( res == -1 ) break;
					lua.PushInteger( res );
					return 1;
				}
				default:
				{
					int res = L.C_GC( o );
					if( res == -1 ) break;
					lua.PushInteger( res );
					return 1;
				}
			}
			lua.PushNil(); // invalid call (inside a finalizer)
			return 1;
		}

		private static int DoFileContinuation( ILuaState lua )
		{
			return lua.GetTop() - 1;
		}

		public static int B_DoFile( ILuaState lua )
		{
			string filename = lua.L_OptString( 1, null );
			lua.SetTop( 1 );
			if( lua.L_LoadFileX( filename, "bt" ) != ThreadStatus.LUA_OK )
				lua.Error();
			lua.CallK( 0, LuaDef.LUA_MULTRET, 0, DoFileContinuation );
			return DoFileContinuation( lua );
		}

		public static int B_Error( ILuaState lua )
		{
			int level = lua.L_OptInt( 2, 1 );
			lua.SetTop( 1 );
			if( lua.Type( 1 ) == LuaType.LUA_TSTRING && level > 0 )
			{
				lua.L_Where( level );
				lua.PushValue( 1 );
				lua.Concat( 2 );
			}
			return lua.Error();
		}

		private static int LoadAux( ILuaState lua, ThreadStatus status, int envidx )
		{
			if( status == ThreadStatus.LUA_OK )
			{
				if( envidx != 0 ) // `env' parameter?
				{
					lua.PushValue(envidx); // push `env' on stack
					if( lua.SetUpvalue(-2, 1) == null ) // set `env' as 1st upvalue of loaded function
					{
						lua.Pop(1); // remove `env' if not used by previous call
					}
				}
				return 1;
			}
			else // error (message is on top of the stack)
			{
				lua.PushNil(); // luaL_pushfail
				lua.Insert(-2); // put before error message
				return 2; // return fail plus error message
			}
		}

		private static string GetMode( ILuaState lua, int idx )
		{
			string mode = lua.L_OptString( idx, null );
			if( mode != null && mode.IndexOf( 'B' ) >= 0 )
			{
				/* Lua code cannot use fixed buffers */
				lua.L_ArgError( idx, "invalid mode" );
			}
			return mode;
		}

		public static int B_LoadFile( ILuaState lua )
		{
			string fname = lua.L_OptString( 1, null );
			string mode  = GetMode( lua, 2 );
			int env = (!lua.IsNone(3) ? 3 : 0); // `env' index or 0 if no `env'
			var status = lua.L_LoadFileX( fname, mode );
			return LoadAux(lua, status, env);
		}

		private const int RESERVEDSLOT = 5;

		// the chunk as the reader function, at index 1, gives it in pieces
		private class ReaderLoadInfo : ILoadInfo
		{
			private ILuaState Lua;
			private string Piece = "";
			private int Pos = 0;
			private bool Done = false;

			public ReaderLoadInfo( ILuaState lua )
			{
				Lua = lua;
			}

			private bool Fill()
			{
				while( !Done && Pos >= Piece.Length )
				{
					Lua.L_CheckStack( 2, "too many nested functions" );
					Lua.PushValue( 1 ); // get function
					Lua.Call( 0, 1 ); // call it
					if( Lua.IsNil( -1 ) )
					{
						Lua.Pop( 1 );
						Done = true;
					}
					else if( !Lua.IsString( -1 ) )
						Lua.L_Error( "reader function must return a string" );
					else
					{
						Piece = Lua.ToString( -1 );
						Pos = 0;
						Lua.Replace( RESERVEDSLOT ); // save string in reserved slot
						Done = Piece.Length == 0; // an empty piece ends the chunk
					}
				}
				return Pos < Piece.Length;
			}

			public int ReadByte()
			{
				return Fill() ? Piece[Pos++] : -1;
			}

			public int PeekByte()
			{
				return Fill() ? Piece[Pos] : -1;
			}
		}

		public static int B_Load( ILuaState lua )
		{
			ThreadStatus status;
			string s = lua.ToString(1);
			string mode = GetMode( lua, 3 );
			int env = (! lua.IsNone(4) ? 4 : 0); // `env' index or 0 if no `env'
			if( s != null )
			{
				string chunkName = lua.L_OptString(2, s);
				status = lua.L_LoadBufferX( s, chunkName, mode );
			}
			else // loading from a reader function
			{
				string chunkName = lua.L_OptString(2, "=(load)");
				lua.L_CheckType(1, LuaType.LUA_TFUNCTION);
				lua.SetTop(RESERVEDSLOT); // create reserved slot
				status = lua.Load( new ReaderLoadInfo( lua ), chunkName, mode );
			}
			return LoadAux( lua, status, env );
		}

		/*
		** Continuation function for 'pcall' and 'xpcall'. Both functions
		** already pushed a 'true' before doing the call, so in case of success
		** 'finishpcall' only has to return everything in the stack minus
		** 'extra' values (where 'extra' is exactly the number of items to be
		** ignored).
		*/
		private static int FinishPCall( ILuaState lua, ThreadStatus status, int extra )
		{
			if( status != ThreadStatus.LUA_OK && status != ThreadStatus.LUA_YIELD ) // error?
			{
				lua.PushBoolean( false ); // first result (false)
				lua.PushValue( -2 ); // error message
				return 2; // return false, msg
			}
			else
				return lua.GetTop() - extra; // return all results
		}

		private static int PCallContinuation( ILuaState lua )
		{
			int extra;
			ThreadStatus status = lua.GetContext( out extra );
			return FinishPCall( lua, status, extra );
		}
		private static CSharpFunctionDelegate DG_PCallContinuation = PCallContinuation;

		public static int B_PCall( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			lua.PushBoolean( true ); // first result if no errors
			lua.Insert( 1 ); // put it in place
			ThreadStatus status = lua.PCallK( lua.GetTop() - 2,
				LuaDef.LUA_MULTRET, 0, 0, DG_PCallContinuation );
			return FinishPCall( lua, status, 0 );
		}

		/*
		** Do a protected call with error handling. After 'lua_rotate', the
		** stack will have <f, err, true, f, [args...]>; so, the function passes
		** 2 to 'finishpcall' to skip the 2 first values when returning results.
		*/
		public static int B_XPCall( ILuaState lua )
		{
			int n = lua.GetTop();
			lua.L_CheckType( 2, LuaType.LUA_TFUNCTION ); // check error function
			lua.PushBoolean( true ); // first result
			lua.PushValue( 1 ); // function
			lua.Rotate( 3, 2 ); // move them below function's arguments
			ThreadStatus status = lua.PCallK( n - 2, LuaDef.LUA_MULTRET,
				2, 2, DG_PCallContinuation );
			return FinishPCall( lua, status, 2 );
		}

		public static int B_RawEqual( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			lua.L_CheckAny( 2 );
			lua.PushBoolean( lua.RawEqual( 1, 2 ) );
			return 1;
		}

		public static int B_RawLen( ILuaState lua )
		{
			LuaType t = lua.Type( 1 );
			lua.L_ArgExpected( t == LuaType.LUA_TTABLE || t == LuaType.LUA_TSTRING,
				1, "table or string" );
			lua.PushInteger( lua.RawLen( 1 ) );
			return 1;
		}

		public static int B_RawGet( ILuaState lua )
		{
			lua.L_CheckType( 1, LuaType.LUA_TTABLE );
			lua.L_CheckAny( 2 );
			lua.SetTop( 2 );
			lua.RawGet( 1 );
			return 1;
		}

		public static int B_RawSet( ILuaState lua )
		{
			lua.L_CheckType( 1, LuaType.LUA_TTABLE );
			lua.L_CheckAny( 2 );
			lua.L_CheckAny( 3 );
			lua.SetTop( 3 );
			lua.RawSet( 1 );
			return 1;
		}

		public static int B_Select( ILuaState lua )
		{
			int n = lua.GetTop();
			if( lua.Type( 1 ) == LuaType.LUA_TSTRING &&
				lua.ToString( 1 ).StartsWith( '#' ) )
			{
				lua.PushInteger( n-1 );
				return 1;
			}
			else
			{
				long i = lua.L_CheckInteger( 1 );
				if( i < 0 ) i = n + i;
				else if( i > n ) i = n;
				lua.L_ArgCheck( 1 <= i, 1, "index out of range" );
				return n - (int)i;
			}
		}

		public static int B_GetMetaTable( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			if( !lua.GetMetaTable( 1 ) )
			{
				lua.PushNil();
				return 1; // no metatable
			}
			lua.L_GetMetaField( 1, "__metatable" );
			return 1;
		}

		public static int B_SetMetaTable( ILuaState lua )
		{
			LuaType t = lua.Type( 2 );
			lua.L_CheckType( 1, LuaType.LUA_TTABLE );
			lua.L_ArgExpected( t == LuaType.LUA_TNIL || t == LuaType.LUA_TTABLE,
				2, "nil or table" );
			if( lua.L_GetMetaField( 1, "__metatable" ) )
				return lua.L_Error( "cannot change a protected metatable" );
			lua.SetTop( 2 );
			lua.SetMetaTable( 1 );
			return 1;
		}

		// b_str2int: a numeral in 'numBase', which wraps around
		private static bool StrToInt( string s, int numBase, out long result )
		{
			ulong n = 0;
			bool neg = false;
			int pos = 0;
			result = 0;
			while( pos < s.Length && Utl.IsSpace( s[pos] ) ) pos++; // skip initial spaces
			if( pos < s.Length && s[pos] == '-' ) { pos++; neg = true; } // handle sign
			else if( pos < s.Length && s[pos] == '+' ) pos++;
			if( pos >= s.Length || !Utl.IsAlnum( s[pos] ) ) // no digit?
				return false;
			do
			{
				int digit = Utl.IsDigit( s[pos] )
					? s[pos] - '0'
					: (s[pos] | 0x20) - 'a' + 10;
				if( digit >= numBase )
					return false; // invalid numeral
				n = unchecked( n * (ulong)numBase + (ulong)digit );
				pos++;
			} while( pos < s.Length && Utl.IsAlnum( s[pos] ) );
			while( pos < s.Length && Utl.IsSpace( s[pos] ) ) pos++; // skip trailing spaces
			result = unchecked( (long)(neg ? 0UL - n : n) );
			return pos == s.Length;
		}

		public static int B_ToNumber( ILuaState lua )
		{
			if( lua.IsNoneOrNil( 2 ) ) // standard conversion?
			{
				if( lua.Type( 1 ) == LuaType.LUA_TNUMBER ) // already a number?
				{
					lua.SetTop( 1 ); // yes; return it
					return 1;
				}
				string s = lua.ToString( 1 );
				if( s != null && lua.StringToNumber( s ) == s.Length + 1 )
					return 1; // successful conversion to number
				// else not a number
				lua.L_CheckAny( 1 ); // (but there must be some parameter)
			}
			else
			{
				long numBase = lua.L_CheckInteger( 2 );
				lua.L_CheckType( 1, LuaType.LUA_TSTRING ); // no numbers as strings
				string s = lua.ToString( 1 );
				lua.L_ArgCheck( 2 <= numBase && numBase <= 36, 2, "base out of range" );
				long n;
				if( StrToInt( s, (int)numBase, out n ) )
				{
					lua.PushInteger( n );
					return 1;
				} // else not a number
			}
			lua.PushNil(); // not a number (luaL_pushfail)
			return 1;
		}

		public static int B_Type( ILuaState lua )
		{
			var t = lua.Type( 1 );
			lua.L_ArgCheck( t != LuaType.LUA_TNONE, 1, "value expected" );
			var tname = lua.TypeName( t );
			lua.PushString( tname );
			return 1;
		}

		public static int B_Next( ILuaState lua )
		{
			lua.L_CheckType( 1, LuaType.LUA_TTABLE );
			lua.SetTop( 2 );
			if( lua.Next(1) )
			{
				return 2;
			}
			else
			{
				lua.PushNil();
				return 1;
			}
		}
		static CSharpFunctionDelegate DG_B_Next = B_Next;

		private static int PairsCont( ILuaState lua )
		{
			return 4; // __pairs did all the work, just return its results
		}
		static CSharpFunctionDelegate DG_PairsCont = PairsCont;

		public static int B_Pairs( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			if( !lua.L_GetMetaField( 1, "__pairs" ) ) // no metamethod?
			{
				lua.PushCSharpFunction( DG_B_Next ); // will return generator and
				lua.PushValue( 1 ); // state
				lua.PushNil(); // initial value
				lua.PushNil(); // to-be-closed object
			}
			else
			{
				lua.PushValue( 1 ); // argument 'self' to metamethod
				lua.CallK( 1, 4, 0, DG_PairsCont ); // get 4 values from metamethod
			}
			return 4;
		}

		/*
		** Traversal function for 'ipairs'
		*/
		private static int IpairsAux( ILuaState lua )
		{
			long i = lua.L_CheckInteger( 2 );
			i = unchecked( i + 1 ); // luaL_intop
			lua.PushInteger( i );
			return lua.GetI( 1, i ) == LuaType.LUA_TNIL ? 1 : 2;
		}
		static CSharpFunctionDelegate DG_IpairsAux = IpairsAux;

		/*
		** 'ipairs' function. Returns 'ipairsaux', given "table", 0.
		** (The given "table" may not be a table.)
		*/
		public static int B_Ipairs( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			lua.PushCSharpFunction( DG_IpairsAux ); // iteration function
			lua.PushValue( 1 ); // state
			lua.PushInteger( 0 ); // initial value
			return 3;
		}

		public static int B_Print( ILuaState lua )
		{
			var host = LuaHost.Of( lua );
			int n = lua.GetTop(); // number of arguments
			for( int i = 1; i <= n; i++ ) // for each argument
			{
				string s = lua.L_ToString( i ); // convert it to string
				if( i > 1 ) // not the first element?
					host.WriteOut( "\t" ); // add a tab before it
				host.WriteOut( s ); // print it
				lua.Pop( 1 ); // pop result
			}
			host.WriteOut( "\n" );
			return 0;
		}

		public static int B_ToString( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			lua.L_ToString( 1 );
			return 1;
		}

	}

}

