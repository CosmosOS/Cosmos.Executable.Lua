// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	// ldblib.c
	internal class LuaDebugLib
	{
		public const string LIB_NAME = "debug";

		private const string HOOKKEY = "_HKEY";
		private static readonly string[] HookNames =
			{ "call", "return", "line", "count", "tail call" };

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
			lua.L_ArgCheck( t == LuaType.LUA_TNIL || t == LuaType.LUA_TTABLE,
				2, "nil or table expected" );
			lua.SetTop( 2 );
			lua.SetMetaTable( 1 );
			return 1; // return 1st argument
		}

		// userdata have no user value here: always nil
		private static int DBG_GetUserValue( ILuaState lua )
		{
			lua.PushNil();
			return 1;
		}

		private static int DBG_SetUserValue( ILuaState lua )
		{
			if( lua.Type( 1 ) == LuaType.LUA_TLIGHTUSERDATA )
				lua.L_ArgError( 1, "full userdata expected, got light userdata" );
			lua.L_CheckType( 1, LuaType.LUA_TUSERDATA );
			if( !lua.IsNoneOrNil( 2 ) )
				lua.L_CheckType( 2, LuaType.LUA_TTABLE );
			lua.SetTop( 2 );
			lua.Pop( 1 );
			return 1;
		}

		private static ILuaState GetThread( ILuaState lua, out int arg )
		{
			if( lua.Type( 1 ) == LuaType.LUA_TTHREAD )
			{
				arg = 1;
				return lua.ToThread( 1 );
			}
			arg = 0;
			return lua;
		}

		private static void SetTabSS( ILuaState lua, string i, string v )
		{
			lua.PushString( v );
			lua.SetField( -2, i );
		}

		private static void SetTabSI( ILuaState lua, string i, int v )
		{
			lua.PushInteger( v );
			lua.SetField( -2, i );
		}

		private static void SetTabSB( ILuaState lua, string i, bool v )
		{
			lua.PushBoolean( v );
			lua.SetField( -2, i );
		}

		// moves what GetInfo pushed for 'f' or 'L' into the result table
		private static void TreatStackOption( ILuaState lua, ILuaState L1, string fname )
		{
			if( lua == L1 )
			{
				lua.PushValue( -2 );
				lua.Remove( -3 );
			}
			else
				L1.XMove( lua, 1 );
			lua.SetField( -2, fname );
		}

		private static bool ValidOptions( string options )
		{
			for( int i = 0; i < options.Length; ++i )
			{
				if( "SlnutLf".IndexOf( options[i] ) < 0 && !(i == 0 && options[i] == '>') )
					return false;
			}
			return true;
		}

		private static int DBG_GetInfo( ILuaState lua )
		{
			LuaDebug ar = new LuaDebug();
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			string options = lua.L_OptString( arg+2, "flnStu" );
			// checked first: GetInfo pushes the function for 'f' whatever follows
			if( !ValidOptions( options ) || options.StartsWith( ">" ) )
				return lua.L_ArgError( arg+2, "invalid option" );
			if( lua.Type( arg+1 ) == LuaType.LUA_TNUMBER )
			{
				if( !L1.GetStack( ClampInt( lua.ToInteger( arg+1 ) ), ar ) )
				{
					lua.PushNil(); // level out of range
					return 1;
				}
			}
			else if( lua.IsFunction( arg+1 ) )
			{
				options = ">" + options;
				lua.PushValue( arg+1 );
				lua.XMove( L1, 1 );
			}
			else
				return lua.L_ArgError( arg+1, "function or level expected" );

			((LuaState)L1).GetInfo( options, ar );
			lua.CreateTable( 0, 2 );
			if( options.Contains( "S" ) )
			{
				SetTabSS( lua, "source", ar.Source );
				SetTabSS( lua, "short_src", ar.ShortSrc );
				SetTabSI( lua, "linedefined", ar.LineDefined );
				SetTabSI( lua, "lastlinedefined", ar.LastLineDefined );
				SetTabSS( lua, "what", ar.What );
			}
			if( options.Contains( "l" ) )
				SetTabSI( lua, "currentline", ar.CurrentLine );
			if( options.Contains( "u" ) )
			{
				SetTabSI( lua, "nups", ar.NumUps );
				SetTabSI( lua, "nparams", ar.NumParams );
				SetTabSB( lua, "isvararg", ar.IsVarArg );
			}
			if( options.Contains( "n" ) )
			{
				SetTabSS( lua, "name", ar.Name );
				SetTabSS( lua, "namewhat", ar.NameWhat );
			}
			if( options.Contains( "t" ) )
				SetTabSB( lua, "istailcall", ar.IsTailCall );
			if( options.Contains( "L" ) )
				TreatStackOption( lua, L1, "activelines" );
			if( options.Contains( "f" ) )
				TreatStackOption( lua, L1, "func" );
			return 1; // return table
		}

		private static int DBG_GetLocal( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			LuaDebug ar = new LuaDebug();
			int nvar = ClampInt( lua.L_CheckInteger( arg+2 ) ); // local-variable index
			if( lua.IsFunction( arg+1 ) ) // function argument?
			{
				lua.PushValue( arg+1 ); // push function
				lua.PushString( ((LuaState)lua).GetLocal( null, nvar ) ); // push local name
				return 1;
			}
			else // stack-level argument
			{
				if( !L1.GetStack( ClampInt( lua.L_CheckInteger( arg+1 ) ), ar ) ) // out of range?
					return lua.L_ArgError( arg+1, "level out of range" );
				string name = ((LuaState)L1).GetLocal( ar, nvar );
				if( name != null )
				{
					L1.XMove( lua, 1 ); // push local value
					lua.PushString( name ); // push name
					lua.PushValue( -2 ); // re-order
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
			if( !L1.GetStack( ClampInt( lua.L_CheckInteger( arg+1 ) ), ar ) ) // out of range?
				return lua.L_ArgError( arg+1, "level out of range" );
			lua.L_CheckAny( arg+3 );
			int nvar = ClampInt( lua.L_CheckInteger( arg+2 ) );
			lua.SetTop( arg+3 );
			lua.XMove( L1, 1 );
			lua.PushString( ((LuaState)L1).SetLocal( ar, nvar ) );
			return 1;
		}

		// a level or an index as an int, a value beyond int's range being
		// as much out of range as the end of int's range
		private static int ClampInt( long v )
		{
			return (int)System.Math.Clamp( v, int.MinValue, int.MaxValue );
		}

		private static int AuxUpvalue( ILuaState lua, bool get )
		{
			int n = ClampInt( lua.L_CheckInteger( 2 ) );
			lua.L_CheckType( 1, LuaType.LUA_TFUNCTION );
			string name = get ? lua.GetUpvalue( 1, n ) : lua.SetUpvalue( 1, n );
			if( name == null ) return 0;
			lua.PushString( name );
			lua.Insert( get ? -2 : -1 );
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

		private static int CheckUpval( ILuaState lua, int argf, int argnup )
		{
			LuaDebug ar = new LuaDebug();
			int nup = ClampInt( lua.L_CheckInteger( argnup ) );
			lua.L_CheckType( argf, LuaType.LUA_TFUNCTION );
			lua.PushValue( argf );
			((LuaState)lua).GetInfo( ">u", ar );
			lua.L_ArgCheck( 1 <= nup && nup <= ar.NumUps, argnup, "invalid upvalue index" );
			return nup;
		}

		private static int DBG_UpvalueId( ILuaState lua )
		{
			int n = CheckUpval( lua, 1, 2 );
			lua.PushLightUserData( ((LuaState)lua).UpvalueId( 1, n ) );
			return 1;
		}

		private static int DBG_UpvalueJoin( ILuaState lua )
		{
			int n1 = CheckUpval( lua, 1, 2 );
			int n2 = CheckUpval( lua, 3, 4 );
			var L = (LuaState)lua;
			lua.L_ArgCheck( L.IsLuaFunction( 1 ), 1, "Lua function expected" );
			lua.L_ArgCheck( L.IsLuaFunction( 3 ), 3, "Lua function expected" );
			L.UpvalueJoin( 1, n1, 3, n2 );
			return 0;
		}

		// registry._HKEY[thread] is the Lua function hooking that thread
		private static void GetHookTable( ILuaState lua )
		{
			lua.L_GetSubTable( LuaDef.LUA_REGISTRYINDEX, HOOKKEY );
		}

		private static void HookF( ILuaState lua, LuaDebug ar )
		{
			GetHookTable( lua );
			lua.PushThread();
			lua.RawGet( -2 );
			if( lua.IsFunction( -1 ) )
			{
				lua.PushString( HookNames[ar.Event] );
				if( ar.CurrentLine >= 0 )
					lua.PushInteger( ar.CurrentLine );
				else
					lua.PushNil();
				lua.Call( 2, 0 );
			}
		}
		private static readonly LuaHookDelegate DG_HookF = HookF;

		private static int MakeMask( string smask, int count )
		{
			int mask = 0;
			if( smask.Contains( "c" ) ) mask |= LuaDef.LUA_MASKCALL;
			if( smask.Contains( "r" ) ) mask |= LuaDef.LUA_MASKRET;
			if( smask.Contains( "l" ) ) mask |= LuaDef.LUA_MASKLINE;
			if( count > 0 ) mask |= LuaDef.LUA_MASKCOUNT;
			return mask;
		}

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
			if( lua.IsNoneOrNil( arg+1 ) )
			{
				lua.SetTop( arg+1 );
				func = null; mask = 0; count = 0; // turn off hooks
			}
			else
			{
				string smask = lua.L_CheckString( arg+2 );
				lua.L_CheckType( arg+1, LuaType.LUA_TFUNCTION );
				count = lua.L_OptInt( arg+3, 0 );
				func = DG_HookF; mask = MakeMask( smask, count );
			}
			GetHookTable( lua );
			L1.PushThread(); L1.XMove( lua, 1 );
			lua.PushValue( arg+1 );
			lua.RawSet( -3 ); // set new hook
			((LuaState)L1).SetHook( func, mask, count ); // set hooks
			return 0;
		}

		private static int DBG_GetHook( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			int mask = ((LuaState)L1).HookMask;
			LuaHookDelegate hook = ((LuaState)L1).Hook;
			if( hook != null && hook != DG_HookF ) // external hook?
				lua.PushString( "external hook" );
			else
			{
				GetHookTable( lua );
				L1.PushThread(); L1.XMove( lua, 1 );
				lua.RawGet( -2 ); // get hook
				lua.Remove( -2 ); // remove hook table
			}
			lua.PushString( UnmakeMask( mask ) );
			lua.PushInteger( ((LuaState)L1).BaseHookCount );
			return 3;
		}

		// no console to read commands from: a script cannot be paused here
		private static int DBG_Debug( ILuaState lua )
		{
			return 0;
		}

		private static int DBG_Traceback( ILuaState lua )
		{
			int arg;
			ILuaState L1 = GetThread( lua, out arg );
			string msg = lua.ToString( arg+1 );
			if( msg == null && !lua.IsNoneOrNil( arg+1 ) ) // non-string 'msg'?
				lua.PushValue( arg+1 ); // return it untouched
			else
			{
				int level = lua.L_OptInt( arg+2, (lua == L1) ? 1 : 0 );
				lua.L_Traceback( L1, msg, level );
			}
			return 1;
		}
	}
}
