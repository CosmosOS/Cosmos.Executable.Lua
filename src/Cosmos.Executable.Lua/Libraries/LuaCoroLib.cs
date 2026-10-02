// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{

	// lcorolib.c of Lua 5.4: the coroutine library
	internal class LuaCoroLib
	{
		public const string LIB_NAME = "coroutine";

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "create", 		CO_Create		),
				new NameFuncPair( "resume", 		CO_Resume		),
				new NameFuncPair( "running", 		CO_Running		),
				new NameFuncPair( "status", 		CO_Status		),
				new NameFuncPair( "wrap", 			CO_Wrap			),
				new NameFuncPair( "yield", 			CO_Yield		),
				new NameFuncPair( "isyieldable", 	CO_IsYieldable	),
				new NameFuncPair( "close", 			CO_Close		),
			};

			lua.L_NewLib( define );
			return 1;
		}

		private static ILuaState GetCo( ILuaState lua )
		{
			ILuaState co = lua.ToThread( 1 );
			lua.L_ArgExpected( co != null, 1, "thread" );
			return co;
		}

		/*
		** Resumes a coroutine. Returns the number of results for non-error
		** cases or -1 for errors.
		*/
		private static int AuxResume( ILuaState lua, ILuaState co, int narg )
		{
			int nres;
			if( !co.CheckStack( narg ) )
			{
				lua.PushString( "too many arguments to resume" );
				return -1; // error flag
			}
			lua.XMove( co, narg );
			ThreadStatus status = co.Resume( lua, narg, out nres );
			if( status == ThreadStatus.LUA_OK || status == ThreadStatus.LUA_YIELD )
			{
				if( !lua.CheckStack( nres + 1 ) )
				{
					co.Pop( nres ); // remove results anyway
					lua.PushString( "too many results to resume" );
					return -1; // error flag
				}
				co.XMove( lua, nres ); // move yielded values
				return nres;
			}
			else
			{
				co.XMove( lua, 1 ); // move error message
				if( LuaHost.Of( lua ).ExitCode.HasValue ) // os.exit in the coroutine?
					((LuaState)lua).D_PropagateExit( status );
				return -1; // error flag
			}
		}

		private static int CO_Resume( ILuaState lua )
		{
			ILuaState co = GetCo( lua );
			int r = AuxResume( lua, co, lua.GetTop() - 1 );
			if( r < 0 )
			{
				lua.PushBoolean( false );
				lua.Insert( -2 );
				return 2; // return false + error message
			}
			else
			{
				lua.PushBoolean( true );
				lua.Insert( -(r + 1) );
				return r + 1; // return true + 'resume' returns
			}
		}

		private static int CO_AuxWrap( ILuaState lua )
		{
			ILuaState co = lua.ToThread( lua.UpvalueIndex( 1 ) );
			int r = AuxResume( lua, co, lua.GetTop() );
			if( r < 0 ) // error?
			{
				ThreadStatus stat = co.Status;
				if( stat != ThreadStatus.LUA_OK && stat != ThreadStatus.LUA_YIELD ) // error in the coroutine?
				{
					stat = co.CloseThread( lua ); // close its tbc variables
					Utl.Assert( stat != ThreadStatus.LUA_OK );
					co.XMove( lua, 1 ); // move error message to the caller
				}
				if( stat != ThreadStatus.LUA_ERRMEM && // not a memory error and ...
					lua.Type( -1 ) == LuaType.LUA_TSTRING ) // ... error object is a string?
				{
					lua.L_Where( 1 ); // add extra info, if available
					lua.Insert( -2 );
					lua.Concat( 2 );
				}
				return lua.Error(); // propagate error
			}
			return r;
		}

		private static int CO_Create( ILuaState lua )
		{
			lua.L_CheckType( 1, LuaType.LUA_TFUNCTION );
			ILuaState newLua = lua.NewThread();
			lua.PushValue( 1 ); // move function to top
			lua.XMove( newLua, 1 ); // move function from lua to newLua
			return 1;
		}

		private static int CO_Wrap( ILuaState lua )
		{
			CO_Create( lua );
			lua.PushCSharpClosure( CO_AuxWrap, 1 );
			return 1;
		}

		private static int CO_Yield( ILuaState lua )
		{
			return lua.Yield( lua.GetTop() );
		}

		private const int COS_RUN	= 0;
		private const int COS_DEAD	= 1;
		private const int COS_YIELD	= 2;
		private const int COS_NORM	= 3;

		private static readonly string[] StatName =
			{ "running", "dead", "suspended", "normal" };

		private static int AuxStatus( ILuaState lua, ILuaState co )
		{
			if( (LuaState)lua == (LuaState)co ) return COS_RUN;
			else
			{
				switch( co.Status )
				{
					case ThreadStatus.LUA_YIELD:
						return COS_YIELD;
					case ThreadStatus.LUA_OK:
					{
						LuaDebug ar = new LuaDebug();
						if( co.GetStack( 0, ar ) ) // does it have frames?
							return COS_NORM; // it is running
						else if( co.GetTop() == 0 )
							return COS_DEAD;
						else
							return COS_YIELD; // initial state
					}
					default: // some error occurred
						return COS_DEAD;
				}
			}
		}

		private static int CO_Status( ILuaState lua )
		{
			ILuaState co = GetCo( lua );
			lua.PushString( StatName[AuxStatus( lua, co )] );
			return 1;
		}

		private static int CO_IsYieldable( ILuaState lua )
		{
			ILuaState co = lua.IsNone( 1 ) ? lua : GetCo( lua );
			lua.PushBoolean( co.IsYieldable() );
			return 1;
		}

		private static int CO_Running( ILuaState lua )
		{
			bool isMain = lua.PushThread();
			lua.PushBoolean( isMain );
			return 2;
		}

		private static int CO_Close( ILuaState lua )
		{
			ILuaState co = GetCo( lua );
			int status = AuxStatus( lua, co );
			switch( status )
			{
				case COS_DEAD: case COS_YIELD:
				{
					ThreadStatus st = co.CloseThread( lua );
					if( st == ThreadStatus.LUA_OK )
					{
						lua.PushBoolean( true );
						return 1;
					}
					else
					{
						lua.PushBoolean( false );
						co.XMove( lua, 1 ); // move error message
						return 2;
					}
				}
				default: // normal or running coroutine
					return lua.L_Error( "cannot close a {0} coroutine", StatName[status] );
			}
		}

	}

}
