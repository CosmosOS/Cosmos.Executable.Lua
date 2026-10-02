// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	// ldblib.c of Lua 5.5: the debug library
	internal class LuaDebugLib
	{
		public const string LIB_NAME = "debug";

		/*
		** The hook table at registry[HOOKKEY] maps threads to their current
		** hook function.
		*/
		private const string HOOKKEY = "_HOOKKEY";

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "debug", 			DBG_Debug			),
				new NameFuncPair( "getuservalue", 	DBG_GetUserValue	),
				new NameFuncPair( "gethook", 		DBG_GetHook			),
				new NameFuncPair( "getinfo", 		DBG_GetInfo			),
				new NameFuncPair( "getlocal", 		DBG_GetLocal		),
				new NameFuncPair( "getregistry", 	DBG_GetRegistry		),
				new NameFuncPair( "getmetatable", 	DBG_GetMetaTable	),
				new NameFuncPair( "getupvalue", 	DBG_GetUpvalue		),
				new NameFuncPair( "upvaluejoin", 	DBG_UpvalueJoin		),
				new NameFuncPair( "upvalueid", 		DBG_UpvalueId		),
				new NameFuncPair( "setuservalue", 	DBG_SetUserValue	),
				new NameFuncPair( "sethook", 		DBG_SetHook			),
				new NameFuncPair( "setlocal", 		DBG_SetLocal		),
				new NameFuncPair( "setmetatable", 	DBG_SetMetaTable	),
				new NameFuncPair( "setupvalue", 	DBG_SetUpvalue		),
				new NameFuncPair( "traceback", 		DBG_Traceback		),
			};

			lua.L_NewLib( define );
			return 1;
		}

		/*
		** If L1 != L, L1 can be in any state, and therefore there are no
		** guarantees about its stack space; any push in L1 must be
		** checked.
		*/
		private static void CheckStack( ILuaState lua, ILuaState L1, int n )
		{
			if( lua != L1 && !L1.CheckStack( n ) )
				lua.L_Error( "stack overflow" );
		}

		private static int DBG_GetRegistry( ILuaState lua )
		{
			lua.PushValue( LuaDef.LUA_REGISTRYINDEX );
			return 1;
		}

		private static int DBG_GetMetaTable( ILuaState lua )
		{
			lua.L_CheckAny( 1 );
			if( !lua.GetMetaTable( 1 ) )
				lua.PushNil(); // no metatable
			return 1;
		}

		private static int DBG_SetMetaTable( ILuaState lua )
		{
			LuaType t = lua.Type( 2 );
			lua.L_ArgExpected( t == LuaType.LUA_TNIL || t == LuaType.LUA_TTABLE, 2, "nil or table" );
			lua.SetTop( 2 );
			lua.SetMetaTable( 1 );
			return 1; // return 1st argument
		}

		private static int DBG_GetUserValue( ILuaState lua )
		{
			int n = (int)lua.L_OptInteger( 2, 1 );
			if( lua.Type( 1 ) != LuaType.LUA_TUSERDATA )
				lua.PushNil(); // luaL_pushfail
			else if( lua.GetIUserValue( 1, n ) != LuaType.LUA_TNONE )
			{
				lua.PushBoolean( true );
				return 2;
			}
			return 1;
		}

		private static int DBG_SetUserValue( ILuaState lua )
		{
			int n = (int)lua.L_OptInteger( 3, 1 );
			lua.L_CheckType( 1, LuaType.LUA_TUSERDATA );
			lua.L_CheckAny( 2 );
			lua.SetTop( 2 );
			if( !lua.SetIUserValue( 1, n ) )
				lua.PushNil(); // luaL_pushfail
			return 1;
		}

		/*
		** Auxiliary function used by several library functions: check for
		** an optional thread as function's first argument and set 'arg' with
		** 1 if this argument is present (so that functions can skip it to
		** access their other arguments)
		*/
		private static ILuaState GetThread( ILuaState lua, out int arg )
		{
			if( lua.Type( 1 ) == LuaType.LUA_TTHREAD )
			{
				arg = 1;
				return lua.ToThread( 1 );
			}
			else
			{
				arg = 0;
				return lua; // function will operate over current thread
			}
		}

		/*
		** Variations of 'lua_settable', used by 'db_getinfo' to put results
		** from 'lua_getinfo' into result table. Key is always a string;
		** value can be a string, an int, or a boolean.
		*/
		private static void SetTabSS( ILuaState lua, string k, string v )
		{
			lua.PushString( v );
			lua.SetField( -2, k );
		}

		private static void SetTabSI( ILuaState lua, string k, int v )
		{
			lua.PushInteger( v );
			lua.SetField( -2, k );
		}

		private static void SetTabSB( ILuaState lua, string k, bool v )
		{
			lua.PushBoolean( v );
			lua.SetField( -2, k );
		}

		/*
		** In function 'db_getinfo', the call to 'lua_getinfo' may push
		** results on the stack; later it creates the result table to put
		** these objects. Function 'treatstackoption' puts the result from
		** 'lua_getinfo' on top of the result table so that it can call
		** 'lua_setfield'.
		*/
		private static void TreatStackOption( ILuaState lua, ILuaState L1, string fname )
		{
			if( lua == L1 )
				lua.Rotate( -2, 1 ); // exchange object and table
			else
				L1.XMove( lua, 1 ); // move object to the "main" stack
			lua.SetField( -2, fname ); // put object into table
		}

		/*
		** Calls 'lua_getinfo' and collects all results in a new table.
		** L1 needs stack space for an optional input (function) plus
		** two optional outputs (function and line table) from function
		** 'lua_getinfo'.
		*/
		private static int DBG_GetInfo( ILuaState lua )
		{
			LuaDebug ar = new LuaDebug();
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			string options = lua.L_OptString( arg+2, "flnSrtu" );
			CheckStack( lua, L1, 3 );
			lua.L_ArgCheck( options.Length == 0 || options[0] != '>', arg + 2, "invalid option '>'" );
			if( lua.IsFunction( arg + 1 ) ) // info about a function?
			{
				options = ">" + options; // add '>' to 'options'
				lua.PushValue( arg + 1 ); // move function to 'L1' stack
				lua.XMove( L1, 1 );
			}
			else // stack level
			{
				if( !L1.GetStack( (int)lua.L_CheckInteger( arg + 1 ), ar ) )
				{
					lua.PushNil(); // level out of range
					return 1;
				}
			}
			if( ((LuaState)L1).GetInfo( options, ar ) == 0 )
				return lua.L_ArgError( arg+2, "invalid option" );
			lua.NewTable(); // table to collect results
			if( options.IndexOf( 'S' ) >= 0 )
			{
				SetTabSS( lua, "source", ar.Source );
				SetTabSS( lua, "short_src", ar.ShortSrc );
				SetTabSI( lua, "linedefined", ar.LineDefined );
				SetTabSI( lua, "lastlinedefined", ar.LastLineDefined );
				SetTabSS( lua, "what", ar.What );
			}
			if( options.IndexOf( 'l' ) >= 0 )
				SetTabSI( lua, "currentline", ar.CurrentLine );
			if( options.IndexOf( 'u' ) >= 0 )
			{
				SetTabSI( lua, "nups", ar.NumUps );
				SetTabSI( lua, "nparams", ar.NumParams );
				SetTabSB( lua, "isvararg", ar.IsVarArg );
			}
			if( options.IndexOf( 'n' ) >= 0 )
			{
				SetTabSS( lua, "name", ar.Name );
				SetTabSS( lua, "namewhat", ar.NameWhat );
			}
			if( options.IndexOf( 'r' ) >= 0 )
			{
				SetTabSI( lua, "ftransfer", ar.FTransfer );
				SetTabSI( lua, "ntransfer", ar.NTransfer );
			}
			if( options.IndexOf( 't' ) >= 0 )
			{
				SetTabSB( lua, "istailcall", ar.IsTailCall );
				SetTabSI( lua, "extraargs", ar.ExtraArgs );
			}
			if( options.IndexOf( 'L' ) >= 0 )
				TreatStackOption( lua, L1, "activelines" );
			if( options.IndexOf( 'f' ) >= 0 )
				TreatStackOption( lua, L1, "func" );
			return 1; // return table
		}

		private static int DBG_GetLocal( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			int nvar = (int)lua.L_CheckInteger( arg + 2 ); // local-variable index
			if( lua.IsFunction( arg + 1 ) ) // function argument?
			{
				lua.PushValue( arg + 1 ); // push function
				lua.PushString( ((LuaState)lua).GetLocal( null, nvar ) ); // push local name
				return 1; // return only name (there is no value)
			}
			else // stack-level argument
			{
				LuaDebug ar = new LuaDebug();
				int level = (int)lua.L_CheckInteger( arg + 1 );
				if( !L1.GetStack( level, ar ) ) // out of range?
					return lua.L_ArgError( arg+1, "level out of range" );
				CheckStack( lua, L1, 1 );
				string name = ((LuaState)L1).GetLocal( ar, nvar );
				if( name != null )
				{
					L1.XMove( lua, 1 ); // move local value
					lua.PushString( name ); // push name
					lua.Rotate( -2, 1 ); // re-order
					return 2;
				}
				else
				{
					lua.PushNil(); // no name (nor value)
					return 1;
				}
			}
		}

		private static int DBG_SetLocal( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			LuaDebug ar = new LuaDebug();
			int level = (int)lua.L_CheckInteger( arg + 1 );
			int nvar = (int)lua.L_CheckInteger( arg + 2 );
			if( !L1.GetStack( level, ar ) ) // out of range?
				return lua.L_ArgError( arg+1, "level out of range" );
			lua.L_CheckAny( arg+3 );
			lua.SetTop( arg+3 );
			CheckStack( lua, L1, 1 );
			lua.XMove( L1, 1 );
			string name = ((LuaState)L1).SetLocal( ar, nvar );
			if( name == null )
				L1.Pop( 1 ); // pop value (if not popped by 'lua_setlocal')
			lua.PushString( name );
			return 1;
		}

		/*
		** get (if 'get' is true) or set an upvalue from a closure
		*/
		private static int AuxUpvalue( ILuaState lua, bool get )
		{
			int n = (int)lua.L_CheckInteger( 2 ); // upvalue index
			lua.L_CheckType( 1, LuaType.LUA_TFUNCTION ); // closure
			string name = get ? lua.GetUpvalue( 1, n ) : lua.SetUpvalue( 1, n );
			if( name == null ) return 0;
			lua.PushString( name );
			lua.Insert( get ? -2 : -1 ); // no-op if get is false
			return get ? 2 : 1;
		}

		private static int DBG_GetUpvalue( ILuaState lua )
		{
			return AuxUpvalue( lua, true );
		}

		private static int DBG_SetUpvalue( ILuaState lua )
		{
			lua.L_CheckAny( 3 );
			return AuxUpvalue( lua, false );
		}

		/*
		** Check whether a given upvalue from a given closure exists and
		** returns its index
		*/
		private static object CheckUpval( ILuaState lua, int argf, int argnup, out int pnup, bool check )
		{
			int nup = (int)lua.L_CheckInteger( argnup ); // upvalue index
			lua.L_CheckType( argf, LuaType.LUA_TFUNCTION ); // closure
			object id = ((LuaState)lua).UpvalueId( argf, nup );
			if( check )
				lua.L_ArgCheck( id != null, argnup, "invalid upvalue index" );
			pnup = nup;
			return id;
		}

		private static int DBG_UpvalueId( ILuaState lua )
		{
			int n;
			object id = CheckUpval( lua, 1, 2, out n, false );
			if( id != null )
				lua.PushLightUserData( id );
			else
				lua.PushNil(); // luaL_pushfail
			return 1;
		}

		private static int DBG_UpvalueJoin( ILuaState lua )
		{
			int n1, n2;
			CheckUpval( lua, 1, 2, out n1, true );
			CheckUpval( lua, 3, 4, out n2, true );
			var L = (LuaState)lua;
			lua.L_ArgCheck( L.IsLuaFunction( 1 ), 1, "Lua function expected" );
			lua.L_ArgCheck( L.IsLuaFunction( 3 ), 3, "Lua function expected" );
			L.UpvalueJoin( 1, n1, 3, n2 );
			return 0;
		}

		/*
		** Call hook function registered at hook table for the current
		** thread (if there is one)
		*/
		private static readonly string[] HookNames =
			{ "call", "return", "line", "count", "tail call" };

		private static void HookF( ILuaState lua, LuaDebug ar )
		{
			lua.GetField( LuaDef.LUA_REGISTRYINDEX, HOOKKEY );
			lua.PushThread();
			if( lua.RawGet( -2 ) == LuaType.LUA_TFUNCTION ) // is there a hook function?
			{
				lua.PushString( HookNames[ar.Event] ); // push event name
				if( ar.CurrentLine >= 0 )
					lua.PushInteger( ar.CurrentLine ); // push current line
				else lua.PushNil();
				lua.Call( 2, 0 ); // call hook function
			}
		}
		private static readonly LuaHookDelegate DG_HookF = HookF;

		/*
		** Convert a string mask (for 'sethook') into a bit mask
		*/
		private static int MakeMask( string smask, int count )
		{
			int mask = 0;
			if( smask.IndexOf( 'c' ) >= 0 ) mask |= LuaDef.LUA_MASKCALL;
			if( smask.IndexOf( 'r' ) >= 0 ) mask |= LuaDef.LUA_MASKRET;
			if( smask.IndexOf( 'l' ) >= 0 ) mask |= LuaDef.LUA_MASKLINE;
			if( count > 0 ) mask |= LuaDef.LUA_MASKCOUNT;
			return mask;
		}

		/*
		** Convert a bit mask (for 'gethook') into a string mask
		*/
		private static string UnmakeMask( int mask )
		{
			string smask = "";
			if( (mask & LuaDef.LUA_MASKCALL) != 0 ) smask += "c";
			if( (mask & LuaDef.LUA_MASKRET) != 0 ) smask += "r";
			if( (mask & LuaDef.LUA_MASKLINE) != 0 ) smask += "l";
			return smask;
		}

		private static int DBG_SetHook( ILuaState lua )
		{
			int arg, mask, count;
			LuaHookDelegate func;
			ILuaState L1 = GetThread( lua, out arg );
			if( lua.IsNoneOrNil( arg+1 ) ) // no hook?
			{
				lua.SetTop( arg+1 );
				func = null; mask = 0; count = 0; // turn off hooks
			}
			else
			{
				string smask = lua.L_CheckString( arg+2 );
				lua.L_CheckType( arg+1, LuaType.LUA_TFUNCTION );
				count = (int)lua.L_OptInteger( arg + 3, 0 );
				func = DG_HookF; mask = MakeMask( smask, count );
			}
			if( lua.L_GetSubTable( LuaDef.LUA_REGISTRYINDEX, HOOKKEY ) == 0 )
			{
				/* table just created; initialize it */
				lua.PushString( "k" );
				lua.SetField( -2, "__mode" ); // hooktable.__mode = "k"
				lua.PushValue( -1 );
				lua.SetMetaTable( -2 ); // metatable(hooktable) = hooktable
			}
			CheckStack( lua, L1, 1 );
			L1.PushThread(); L1.XMove( lua, 1 ); // key (thread)
			lua.PushValue( arg + 1 ); // value (hook function)
			lua.RawSet( -3 ); // hooktable[L1] = new Lua hook
			((LuaState)L1).SetHook( func, mask, count );
			return 0;
		}

		private static int DBG_GetHook( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			int mask = ((LuaState)L1).HookMask;
			LuaHookDelegate hook = ((LuaState)L1).Hook;
			if( hook == null ) // no hook?
			{
				lua.PushNil(); // luaL_pushfail
				return 1;
			}
			else if( hook != DG_HookF ) // external hook?
				lua.PushString( "external hook" );
			else // hook table must exist
			{
				lua.GetField( LuaDef.LUA_REGISTRYINDEX, HOOKKEY );
				CheckStack( lua, L1, 1 );
				L1.PushThread(); L1.XMove( lua, 1 );
				lua.RawGet( -2 ); // 1st result = hooktable[L1]
				lua.Remove( -2 ); // remove hook table
			}
			lua.PushString( UnmakeMask( mask ) ); // 2nd result = mask
			lua.PushInteger( ((LuaState)L1).BaseHookCount ); // 3rd result = count
			return 3;
		}

		private static int DBG_Debug( ILuaState lua )
		{
			var host = LuaHost.Of( lua );
			for( ;; )
			{
				host.WriteErr( "lua_debug> " );
				string buffer = host.ReadInLine();
				if( buffer == null || buffer == "cont\n" )
					return 0;
				if( lua.L_LoadBufferX( buffer, "=(debug command)", "t" ) != ThreadStatus.LUA_OK ||
					lua.PCall( 0, 0, 0 ) != ThreadStatus.LUA_OK )
					host.WriteErr( lua.L_ToString( -1 ) + "\n" );
				lua.SetTop( 0 ); // remove eventual returns
			}
		}

		private static int DBG_Traceback( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			string msg = lua.ToString( arg + 1 );
			if( msg == null && !lua.IsNoneOrNil( arg + 1 ) ) // non-string 'msg'?
				lua.PushValue( arg + 1 ); // return it untouched
			else
			{
				int level = (int)lua.L_OptInteger( arg + 2, (lua == L1) ? 1 : 0 );
				lua.L_Traceback( L1, msg, level );
			}
			return 1;
		}
	}
}
