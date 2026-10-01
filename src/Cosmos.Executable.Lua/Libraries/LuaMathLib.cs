// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using Math = System.Math;
	using Double = System.Double;
	using Random = System.Random;

	// lmathlib.c of Lua 5.3, with the functions it keeps for Lua 5.2 code
	// (LUA_COMPAT_MATHLIB)
	internal class LuaMathLib
	{
		public const string LIB_NAME = "math";

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "abs",   		Math_Abs ),
				new NameFuncPair( "acos",  		Math_Acos ),
				new NameFuncPair( "asin",  		Math_Asin ),
				new NameFuncPair( "atan",  		Math_Atan ),
				new NameFuncPair( "ceil",  		Math_Ceil ),
				new NameFuncPair( "cos",   		Math_Cos ),
				new NameFuncPair( "deg",   		Math_Deg ),
				new NameFuncPair( "exp",   		Math_Exp ),
				new NameFuncPair( "tointeger", 	Math_ToInt ),
				new NameFuncPair( "floor", 		Math_Floor ),
				new NameFuncPair( "fmod",  		Math_Fmod ),
				new NameFuncPair( "ult",  		Math_Ult ),
				new NameFuncPair( "log",   		Math_Log ),
				new NameFuncPair( "max",   		Math_Max ),
				new NameFuncPair( "min",   		Math_Min ),
				new NameFuncPair( "modf",  		Math_Modf ),
				new NameFuncPair( "rad",   		Math_Rad ),
				new NameFuncPair( "random",     Math_Random ),
				new NameFuncPair( "randomseed", Math_RandomSeed ),
				new NameFuncPair( "sin",   		Math_Sin ),
				new NameFuncPair( "sqrt",  		Math_Sqrt ),
				new NameFuncPair( "tan",   		Math_Tan ),
				new NameFuncPair( "type",   	Math_Type ),
				// deprecated functions, for compatibility only
				new NameFuncPair( "atan2", 		Math_Atan ),
				new NameFuncPair( "cosh",  		Math_Cosh ),
				new NameFuncPair( "sinh", 		Math_Sinh ),
				new NameFuncPair( "tanh",   	Math_Tanh ),
				new NameFuncPair( "pow",   		Math_Pow ),
				new NameFuncPair( "frexp", 		Math_Frexp ),
				new NameFuncPair( "ldexp", 		Math_Ldexp ),
				new NameFuncPair( "log10", 		Math_Log10 ),
			};

			lua.L_NewLib( define );

			lua.PushNumber( Math.PI );
			lua.SetField( -2, "pi" );

			lua.PushNumber( Double.PositiveInfinity );
			lua.SetField( -2, "huge" );

			lua.PushInteger( LuaConf.LUA_MAXINTEGER );
			lua.SetField( -2, "maxinteger" );

			lua.PushInteger( LuaConf.LUA_MININTEGER );
			lua.SetField( -2, "mininteger" );

			return 1;
		}

		private static int Math_Abs( ILuaState lua )
		{
			if( lua.IsInteger( 1 ) )
			{
				long n = lua.ToInteger( 1 );
				if( n < 0 ) n = unchecked( (long)(0UL - (ulong)n) );
				lua.PushInteger( n );
			}
			else
				lua.PushNumber( Math.Abs( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Sin( ILuaState lua )
		{
			lua.PushNumber( Math.Sin( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Cos( ILuaState lua )
		{
			lua.PushNumber( Math.Cos( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Tan( ILuaState lua )
		{
			lua.PushNumber( Math.Tan( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Asin( ILuaState lua )
		{
			lua.PushNumber( Math.Asin( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Acos( ILuaState lua )
		{
			lua.PushNumber( Math.Acos( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Atan( ILuaState lua )
		{
			double y = lua.L_CheckNumber( 1 );
			double x = lua.L_OptNumber( 2, 1 );
			lua.PushNumber( Math.Atan2( y, x ) );
			return 1;
		}

		private static int Math_ToInt( ILuaState lua )
		{
			bool valid;
			long n = lua.ToIntegerX( 1, out valid );
			if( valid )
				lua.PushInteger( n );
			else
			{
				lua.L_CheckAny( 1 );
				lua.PushNil(); // value is not convertible to integer
			}
			return 1;
		}

		// pushnumint: a float with an integer value as an integer
		private static void PushNumInt( ILuaState lua, double d )
		{
			long n;
			if( LuaState.NumberToInteger( d, out n ) ) // does 'd' fit in an integer?
				lua.PushInteger( n ); // result is integer
			else
				lua.PushNumber( d ); // result is float
		}

		private static int Math_Floor( ILuaState lua )
		{
			if( lua.IsInteger( 1 ) )
				lua.SetTop( 1 ); // integer is its own floor
			else
				PushNumInt( lua, Math.Floor( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Ceil( ILuaState lua )
		{
			if( lua.IsInteger( 1 ) )
				lua.SetTop( 1 ); // integer is its own ceil
			else
				PushNumInt( lua, Math.Ceiling( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Fmod( ILuaState lua )
		{
			if( lua.IsInteger( 1 ) && lua.IsInteger( 2 ) )
			{
				long d = lua.ToInteger( 2 );
				if( unchecked( (ulong)d + 1UL ) <= 1UL ) // special cases: -1 or 0
				{
					lua.L_ArgCheck( d != 0, 2, "zero" );
					lua.PushInteger( 0 ); // avoid overflow with 0x80000... / -1
				}
				else
					lua.PushInteger( lua.ToInteger( 1 ) % d );
			}
			else
				lua.PushNumber( lua.L_CheckNumber( 1 ) % lua.L_CheckNumber( 2 ) ); // fmod
			return 1;
		}

		private static int Math_Modf( ILuaState lua )
		{
			if( lua.IsInteger( 1 ) )
			{
				lua.SetTop( 1 ); // number is its own integer part
				lua.PushNumber( 0 ); // no fractional part
			}
			else
			{
				double n = lua.L_CheckNumber( 1 );
				// integer part (rounds toward zero)
				double ip = (n < 0) ? Math.Ceiling( n ) : Math.Floor( n );
				PushNumInt( lua, ip );
				// fractional part (test needed for inf/-inf)
				lua.PushNumber( (n == ip) ? 0.0 : (n - ip) );
			}
			return 2;
		}

		private static int Math_Sqrt( ILuaState lua )
		{
			lua.PushNumber( Math.Sqrt( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Ult( ILuaState lua )
		{
			long a = lua.L_CheckInteger( 1 );
			long b = lua.L_CheckInteger( 2 );
			lua.PushBoolean( unchecked( (ulong)a < (ulong)b ) );
			return 1;
		}

		private static int Math_Log( ILuaState lua )
		{
			double x = lua.L_CheckNumber( 1 );
			double res;
			if( lua.IsNoneOrNil( 2 ) )
				res = Math.Log( x );
			else
			{
				double logBase = lua.L_CheckNumber( 2 );
				if( logBase == 2.0 )
					res = Math.Log2( x );
				else if( logBase == 10.0 )
					res = Math.Log10( x );
				else
					res = Math.Log( x ) / Math.Log( logBase );
			}
			lua.PushNumber( res );
			return 1;
		}

		private static int Math_Exp( ILuaState lua )
		{
			lua.PushNumber( Math.Exp( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Deg( ILuaState lua )
		{
			lua.PushNumber( lua.L_CheckNumber( 1 ) * (180.0 / Math.PI) );
			return 1;
		}

		private static int Math_Rad( ILuaState lua )
		{
			lua.PushNumber( lua.L_CheckNumber( 1 ) * (Math.PI / 180.0) );
			return 1;
		}

		private static int Math_Min( ILuaState lua )
		{
			int n = lua.GetTop(); // number of arguments
			int imin = 1; // index of current minimum value
			lua.L_ArgCheck( n >= 1, 1, "value expected" );
			for( int i=2; i<=n; ++i )
			{
				if( lua.Compare( i, imin, LuaEq.LUA_OPLT ) )
					imin = i;
			}
			lua.PushValue( imin );
			return 1;
		}

		private static int Math_Max( ILuaState lua )
		{
			int n = lua.GetTop(); // number of arguments
			int imax = 1; // index of current maximum value
			lua.L_ArgCheck( n >= 1, 1, "value expected" );
			for( int i=2; i<=n; ++i )
			{
				if( lua.Compare( imax, i, LuaEq.LUA_OPLT ) )
					imax = i;
			}
			lua.PushValue( imax );
			return 1;
		}

		private static int Math_Random( ILuaState lua )
		{
			long low, up;
			double r = LuaHost.Of( lua ).Random.NextDouble(); // in [0, 1)
			switch( lua.GetTop() ) // check number of arguments
			{
				case 0: // no arguments
					lua.PushNumber( r ); // Number between 0 and 1
					return 1;
				case 1: // only upper limit
					low = 1;
					up = lua.L_CheckInteger( 1 );
					break;
				case 2: // lower and upper limits
					low = lua.L_CheckInteger( 1 );
					up = lua.L_CheckInteger( 2 );
					break;
				default:
					return lua.L_Error( "wrong number of arguments" );
			}
			// random integer in the interval [low, up]
			lua.L_ArgCheck( low <= up, 1, "interval is empty" );
			lua.L_ArgCheck( low >= 0 || up <= LuaConf.LUA_MAXINTEGER + low, 1,
				"interval too large" );
			r *= (double)unchecked( up - low ) + 1.0;
			lua.PushInteger( unchecked( (long)r + low ) );
			return 1;
		}

		private static int Math_RandomSeed( ILuaState lua )
		{
			double n = lua.L_CheckNumber( 1 );
			long seed = Double.IsNaN( n ) ? 0 : (long)Math.Clamp( n, long.MinValue, long.MaxValue );
			LuaHost host = LuaHost.Of( lua );
			host.Random = new Random( unchecked( (int)seed ) );
			host.Random.Next(); // discard first value to avoid undesirable correlations
			return 0;
		}

		private static int Math_Type( ILuaState lua )
		{
			if( lua.Type( 1 ) == LuaType.LUA_TNUMBER )
				lua.PushString( lua.IsInteger( 1 ) ? "integer" : "float" );
			else
			{
				lua.L_CheckAny( 1 );
				lua.PushNil();
			}
			return 1;
		}

		private static int Math_Cosh( ILuaState lua )
		{
			lua.PushNumber( Math.Cosh( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Sinh( ILuaState lua )
		{
			lua.PushNumber( Math.Sinh( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Tanh( ILuaState lua )
		{
			lua.PushNumber( Math.Tanh( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}

		private static int Math_Pow( ILuaState lua )
		{
			double x = lua.L_CheckNumber( 1 );
			double y = lua.L_CheckNumber( 2 );
			lua.PushNumber( Math.Pow( x, y ) );
			return 1;
		}

		private static int Math_Frexp( ILuaState lua )
		{
			double d = lua.L_CheckNumber( 1 );
			int e = 0;
			if( d != 0 && !Double.IsNaN( d ) && !Double.IsInfinity( d ) )
			{
				e = Math.ILogB( d ) + 1;
				d = Math.ScaleB( d, -e ); // in [0.5, 1)
			}
			lua.PushNumber( d );
			lua.PushInteger( e );
			return 2;
		}

		private static int Math_Ldexp( ILuaState lua )
		{
			double x = lua.L_CheckNumber( 1 );
			int ep = unchecked( (int)lua.L_CheckInteger( 2 ) );
			lua.PushNumber( Math.ScaleB( x, ep ) );
			return 1;
		}

		private static int Math_Log10( ILuaState lua )
		{
			lua.PushNumber( Math.Log10( lua.L_CheckNumber( 1 ) ) );
			return 1;
		}
	}

}
