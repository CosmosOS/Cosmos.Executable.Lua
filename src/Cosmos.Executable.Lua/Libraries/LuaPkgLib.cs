// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using StringBuilder = System.Text.StringBuilder;

	// loadlib.c of Lua 5.4: the package library
	internal class LuaPkgLib
	{
		public const string LIB_NAME = "package";

		/*
		** key for table in the registry that keeps handles
		** for all loaded C libraries
		*/
		private const string CLIBS = "_CLIBS";

		private const string LUA_LOADED_TABLE = "_LOADED";
		private const string LUA_PRELOAD_TABLE = "_PRELOAD";

		// relative to the state's working directory, like ./?.lua of the reference lua
		private static readonly string LUA_PATH_DEFAULT = "?.lua;?" + LuaConf.LUA_DIRSEP + "init.lua";
		private const string LUA_CPATH_DEFAULT = "?.so;loadall.so";

		private const string LUA_PATH_SEP	= ";";
		private const string LUA_PATH_MARK	= "?";
		private const string LUA_EXEC_DIR	= "!";
		private const string LUA_IGMARK		= "-";

		/*
		** LUA_CSUBSEP is the character that replaces dots in submodule names
		** when searching for a C loader.
		** LUA_LSUBSEP is the character that replaces dots in submodule names
		** when searching for a Lua loader.
		*/
		private static readonly string LUA_CSUBSEP = LuaConf.LUA_DIRSEP;
		private static readonly string LUA_LSUBSEP = LuaConf.LUA_DIRSEP;

		/* prefix for open functions in C libraries */
		private const string LUA_POF = "luaopen_";

		/* separator for open functions in C libraries */
		private const string LUA_OFSEP = "_";

		/* error codes for 'lookforfunc' */
		private const int ERRLIB = 1;
		private const int ERRFUNC = 2;

		public static int OpenLib( ILuaState lua )
		{
			CreateCLibsTable( lua );
			NameFuncPair[] pk_funcs = new NameFuncPair[]
			{
				new NameFuncPair( "loadlib", 	PKG_LoadLib ),
				new NameFuncPair( "searchpath", PKG_SearchPath ),
				/* placeholders */
				new NameFuncPair( "preload", 	null ),
				new NameFuncPair( "cpath", 		null ),
				new NameFuncPair( "path", 		null ),
				new NameFuncPair( "searchers", 	null ),
				new NameFuncPair( "loaded", 	null ),
			};
			lua.L_NewLib( pk_funcs ); // create 'package' table
			CreateSearchersTable( lua );
			/* set paths */
			SetPath( lua, "path", LUA_PATH_DEFAULT );
			SetPath( lua, "cpath", LUA_CPATH_DEFAULT );
			/* store config information */
			lua.PushString( LuaConf.LUA_DIRSEP + "\n" + LUA_PATH_SEP + "\n" + LUA_PATH_MARK + "\n" +
				LUA_EXEC_DIR + "\n" + LUA_IGMARK + "\n" );
			lua.SetField( -2, "config" );
			/* set field 'loaded' */
			lua.L_GetSubTable( LuaDef.LUA_REGISTRYINDEX, LUA_LOADED_TABLE );
			lua.SetField( -2, "loaded" );
			/* set field 'preload' */
			lua.L_GetSubTable( LuaDef.LUA_REGISTRYINDEX, LUA_PRELOAD_TABLE );
			lua.SetField( -2, "preload" );
			lua.PushGlobalTable();
			lua.PushValue( -2 ); // set 'package' as upvalue for next lib
			NameFuncPair[] ll_funcs = new NameFuncPair[]
			{
				new NameFuncPair( "require", 	LL_Require ),
			};
			lua.L_SetFuncs( ll_funcs, 1 ); // open lib into global table
			lua.Pop( 1 ); // pop global table
			return 1; // return 'package' table
		}

		/*
		** create table CLIBS to keep track of loaded C libraries,
		** setting a finalizer to close all libraries when closing state.
		*/
		private static void CreateCLibsTable( ILuaState lua )
		{
			// TODO: Cosmos loads no dynamic libraries, so the table stays
			// empty and needs no '__gc' to unload them
			lua.L_GetSubTable( LuaDef.LUA_REGISTRYINDEX, CLIBS ); // create CLIBS table
			lua.Pop( 1 ); // pop CLIBS table
		}

		private static void CreateSearchersTable( ILuaState lua )
		{
			CSharpFunctionDelegate[] searchers = new CSharpFunctionDelegate[]
			{
				SearcherPreload,
				SearcherLua,
				SearcherC,
				SearcherCroot,
			};
			/* create 'searchers' table */
			lua.CreateTable( searchers.Length, 0 );
			/* fill it with predefined searchers */
			for( int i=0; i<searchers.Length; ++i )
			{
				lua.PushValue( -2 ); // set 'package' as upvalue for all searchers
				lua.PushCSharpClosure( searchers[i], 1 );
				lua.RawSetI( -2, i+1 );
			}
			lua.SetField( -2, "searchers" ); // put it in field 'searchers'
		}

		/*
		** Set a path: the default one, relative to the working directory of
		** the state (Cosmos has no LUA_PATH_5_4 or LUA_PATH variables)
		*/
		private static void SetPath( ILuaState lua, string fieldName, string dft )
		{
			lua.PushString( dft ); // use default
			lua.SetField( -2, fieldName ); // package[fieldname] = path value
		}

		/*
		** {========================================================
		** The dynamic libraries: Cosmos has none, as the reference Lua
		** built without LUA_USE_DLOPEN nor LUA_DL_DLL
		** =========================================================
		*/

		private const string LIB_FAIL = "absent";
		private const string DLMSG = "dynamic libraries not enabled; check your Lua installation";

		private static object LSysLoad( ILuaState lua, string path, bool seeglb )
		{
			lua.PushString( DLMSG );
			return null;
		}

		private static CSharpFunctionDelegate LSysSym( ILuaState lua, object lib, string sym )
		{
			lua.PushString( DLMSG );
			return null;
		}

		/*
		** return registry.CLIBS[path]
		*/
		private static object CheckCLib( ILuaState lua, string path )
		{
			lua.GetField( LuaDef.LUA_REGISTRYINDEX, CLIBS );
			lua.GetField( -1, path );
			object plib = lua.ToUserData( -1 ); // plib = CLIBS[path]
			lua.Pop( 2 ); // pop CLIBS table and 'plib'
			return plib;
		}

		/*
		** registry.CLIBS[path] = plib        -- for queries
		** registry.CLIBS[#CLIBS + 1] = plib  -- also keep a list of all libraries
		*/
		private static void AddToCLib( ILuaState lua, string path, object plib )
		{
			lua.GetField( LuaDef.LUA_REGISTRYINDEX, CLIBS );
			lua.PushLightUserData( plib );
			lua.PushValue( -1 );
			lua.SetField( -3, path ); // CLIBS[path] = plib
			lua.RawSetI( -2, lua.L_Len( -2 ) + 1 ); // CLIBS[#CLIBS + 1] = plib
			lua.Pop( 1 ); // pop CLIBS table
		}

		/*
		** Look for a C function named 'sym' in a dynamically loaded library
		** 'path'.
		** First, check whether the library is already loaded; if not, try
		** to load it.
		** Then, if 'sym' is '*', return true (as library has been loaded).
		** Otherwise, look for symbol 'sym' in the library and push a
		** C function with that symbol.
		** Return 0 and 'true' or a function in the stack; in case of
		** errors, return an error code and an error message in the stack.
		*/
		private static int LookForFunc( ILuaState lua, string path, string sym )
		{
			object reg = CheckCLib( lua, path ); // check loaded C libraries
			if( reg == null ) // must load library?
			{
				reg = LSysLoad( lua, path, sym == "*" ); // global symbols if 'sym'=='*'
				if( reg == null ) return ERRLIB; // unable to load library
				AddToCLib( lua, path, reg );
			}
			if( sym == "*" ) // loading only library (no function)?
			{
				lua.PushBoolean( true ); // return 'true'
				return 0; // no errors
			}
			else
			{
				var f = LSysSym( lua, reg, sym );
				if( f == null )
					return ERRFUNC; // unable to find function
				lua.PushCSharpFunction( f ); // else create new function
				return 0; // no errors
			}
		}

		private static int PKG_LoadLib( ILuaState lua )
		{
			string path = lua.L_CheckString( 1 );
			string init = lua.L_CheckString( 2 );
			int stat = LookForFunc( lua, path, init );
			if( stat == 0 ) // no errors?
				return 1; // return the loaded function
			else // error; error message is on stack top
			{
				lua.PushNil(); // luaL_pushfail
				lua.Insert( -2 );
				lua.PushString( (stat == ERRLIB) ? LIB_FAIL : "init" );
				return 3; // return fail, error message, and where
			}
		}

		/* }========================================================= */

		/*
		** {======================================================
		** 'require' function
		** =======================================================
		*/

		private static bool Readable( ILuaState lua, string filename )
		{
			return LuaFile.Readable( lua, filename );
		}

		/*
		** Given a path such as ";blabla.so;blublu.so", pushes the string
		**
		** no file 'blabla.so'
		**	no file 'blublu.so'
		*/
		private static void PushErrorNotFound( ILuaState lua, string path )
		{
			lua.PushString( "no file '" + path.Replace( LUA_PATH_SEP, "'\n\tno file '" ) + "'" );
		}

		private static string SearchPath( ILuaState lua,
			string name, string path, string sep, string dirsep )
		{
			/* separator is non-empty and appears in 'name'? */
			if( sep.Length > 0 && name.IndexOf( sep[0] ) >= 0 )
				name = name.Replace( sep, dirsep ); // replace it by 'dirsep'
			/* add path to the buffer, replacing marks ('?') with the file name */
			string pathname = path.Replace( LUA_PATH_MARK, name );
			foreach( string filename in pathname.Split( LUA_PATH_SEP[0] ) )
			{
				if( Readable( lua, filename ) ) // does file exist and is readable?
				{
					lua.PushString( filename ); // save and return name
					return filename;
				}
			}
			PushErrorNotFound( lua, pathname ); // create error message
			return null; // not found
		}

		private static int PKG_SearchPath( ILuaState lua )
		{
			string f = SearchPath( lua, lua.L_CheckString( 1 ), lua.L_CheckString( 2 ),
				lua.L_OptString( 3, "." ), lua.L_OptString( 4, LuaConf.LUA_DIRSEP ) );
			if( f != null ) return 1;
			else // error message is on top of the stack
			{
				lua.PushNil(); // luaL_pushfail
				lua.Insert( -2 );
				return 2; // return fail + error message
			}
		}

		private static string FindFile( ILuaState lua,
			string name, string pname, string dirsep )
		{
			lua.GetField( lua.UpvalueIndex( 1 ), pname );
			string path = lua.ToString( -1 );
			if( path == null )
				lua.L_Error( "'package.{0}' must be a string", pname );
			return SearchPath( lua, name, path, ".", dirsep );
		}

		private static int CheckLoad( ILuaState lua, bool stat, string filename )
		{
			if( stat ) // module loaded successfully?
			{
				lua.PushString( filename ); // will be 2nd argument to module
				return 2; // return open function and file name
			}
			else return lua.L_Error(
				"error loading module '{0}' from file '{1}':\n\t{2}",
				lua.ToString( 1 ), filename, lua.ToString( -1 ) );
		}

		private static int SearcherLua( ILuaState lua )
		{
			string name = lua.L_CheckString( 1 );
			string filename = FindFile( lua, name, "path", LUA_LSUBSEP );
			if( filename == null ) return 1; // module not found in this path
			return CheckLoad( lua, lua.L_LoadFile( filename ) == ThreadStatus.LUA_OK, filename );
		}

		/*
		** Try to find a load function for module 'modname' at file 'filename'.
		** First, change '.' to '_' in 'modname'; then, if 'modname' has
		** the form X-Y (that is, it has an "ignore mark"), build a function
		** name "luaopen_X" and look for it. (For compatibility, if that
		** fails, it also tries "luaopen_Y".) If there is no ignore mark,
		** look for a function named "luaopen_modname".
		*/
		private static int LoadFunc( ILuaState lua, string filename, string modname )
		{
			string openfunc;
			modname = modname.Replace( ".", LUA_OFSEP );
			int mark = modname.IndexOf( LUA_IGMARK[0] );
			if( mark >= 0 )
			{
				openfunc = LUA_POF + modname.Substring( 0, mark );
				int stat = LookForFunc( lua, filename, openfunc );
				if( stat != ERRFUNC ) return stat;
				modname = modname.Substring( mark + 1 ); // else go ahead and try old-style name
			}
			openfunc = LUA_POF + modname;
			return LookForFunc( lua, filename, openfunc );
		}

		private static int SearcherC( ILuaState lua )
		{
			string name = lua.L_CheckString( 1 );
			string filename = FindFile( lua, name, "cpath", LUA_CSUBSEP );
			if( filename == null ) return 1; // module not found in this path
			return CheckLoad( lua, LoadFunc( lua, filename, name ) == 0, filename );
		}

		private static int SearcherCroot( ILuaState lua )
		{
			string name = lua.L_CheckString( 1 );
			int p = name.IndexOf( '.' );
			int stat;
			if( p < 0 ) return 0; // is root
			lua.PushString( name.Substring( 0, p ) );
			string filename = FindFile( lua, lua.ToString( -1 ), "cpath", LUA_CSUBSEP );
			if( filename == null ) return 1; // root not found
			if( (stat = LoadFunc( lua, filename, name )) != 0 )
			{
				if( stat != ERRFUNC )
					return CheckLoad( lua, false, filename ); // real error
				else // open function not found
				{
					lua.PushString( string.Format( "no module '{0}' in file '{1}'", name, filename ) );
					return 1;
				}
			}
			lua.PushString( filename ); // will be 2nd argument to module
			return 2;
		}

		private static int SearcherPreload( ILuaState lua )
		{
			string name = lua.L_CheckString( 1 );
			lua.GetField( LuaDef.LUA_REGISTRYINDEX, LUA_PRELOAD_TABLE );
			if( lua.GetField( -1, name ) == LuaType.LUA_TNIL ) // not found?
			{
				lua.PushString( string.Format( "no field package.preload['{0}']", name ) );
				return 1;
			}
			else
			{
				lua.PushString( ":preload:" );
				return 2;
			}
		}

		private static void FindLoader( ILuaState lua, string name )
		{
			var msg = new StringBuilder(); // to build error message
			/* push 'package.searchers' to index 3 in the stack */
			if( lua.GetField( lua.UpvalueIndex( 1 ), "searchers" ) != LuaType.LUA_TTABLE )
				lua.L_Error( "'package.searchers' must be a table" );
			/*  iterate over available searchers to find a loader */
			for( int i = 1; ; i++ )
			{
				msg.Append( "\n\t" ); // error-message prefix
				if( lua.RawGetI( 3, i ) == LuaType.LUA_TNIL ) // no more searchers?
				{
					lua.Pop( 1 ); // remove nil
					msg.Length -= 2; // remove prefix
					lua.L_Error( "module '{0}' not found:{1}", name, msg.ToString() );
				}
				lua.PushString( name );
				lua.Call( 1, 2 ); // call it
				if( lua.IsFunction( -2 ) ) // did it find a loader?
					return; // module loader found
				else if( lua.IsString( -2 ) ) // searcher returned error message?
				{
					lua.Pop( 1 ); // remove extra return
					msg.Append( lua.ToString( -1 ) ); // concatenate error message
					lua.Pop( 1 );
				}
				else // no error message
				{
					lua.Pop( 2 ); // remove both returns
					msg.Length -= 2; // remove prefix
				}
			}
		}

		private static int LL_Require( ILuaState lua )
		{
			string name = lua.L_CheckString( 1 );
			lua.SetTop( 1 ); // LOADED table will be at index 2
			lua.GetField( LuaDef.LUA_REGISTRYINDEX, LUA_LOADED_TABLE );
			lua.GetField( 2, name ); // LOADED[name]
			if( lua.ToBoolean( -1 ) ) // is it there?
				return 1; // package is already loaded
			/* else must load package */
			lua.Pop( 1 ); // remove 'getfield' result
			FindLoader( lua, name );
			lua.Rotate( -2, 1 ); // function <-> loader data
			lua.PushValue( 1 ); // name is 1st argument to module loader
			lua.PushValue( -3 ); // loader data is 2nd argument
			/* stack: ...; loader data; loader function; mod. name; loader data */
			lua.Call( 2, 1 ); // run loader to load module
			/* stack: ...; loader data; result from loader */
			if( !lua.IsNil( -1 ) ) // non-nil return?
				lua.SetField( 2, name ); // LOADED[name] = returned value
			else
				lua.Pop( 1 ); // pop nil
			if( lua.GetField( 2, name ) == LuaType.LUA_TNIL ) // module set no value?
			{
				lua.PushBoolean( true ); // use true as result
				lua.Copy( -1, -2 ); // replace loader result
				lua.SetField( 2, name ); // LOADED[name] = true
			}
			lua.Rotate( -2, 1 ); // loader data <-> module result
			return 2; // return module result and loader data
		}

		/* }====================================================== */
	}

}
