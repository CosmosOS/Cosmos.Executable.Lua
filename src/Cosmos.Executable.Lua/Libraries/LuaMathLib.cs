// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using Math = System.Math;
	using Double = System.Double;

	// lmathlib.c of Lua 5.5, without the deprecated functions it keeps for
	// LUA_COMPAT_MATHLIB, which the reference build does not define
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
				new NameFuncPair( "frexp", 		Math_Frexp ),
				new NameFuncPair( "ldexp", 		Math_Ldexp ),
				new NameFuncPair( "ult",  		Math_Ult ),
				new NameFuncPair( "log",   		Math_Log ),
				new NameFuncPair( "max",   		Math_Max ),
				new NameFuncPair( "min",   		Math_Min ),
				new NameFuncPair( "modf",  		Math_Modf ),
				new NameFuncPair( "rad",   		Math_Rad ),
				new NameFuncPair( "sin",   		Math_Sin ),
				new NameFuncPair( "sqrt",  		Math_Sqrt ),
				new NameFuncPair( "tan",   		Math_Tan ),
				new NameFuncPair( "type",   	Math_Type ),
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

			SetRandFunc( lua );
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
				lua.PushNil(); // value is not convertible to integer (luaL_pushfail)
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

		private static int Math_Type( ILuaState lua )
		{
			if( lua.Type( 1 ) == LuaType.LUA_TNUMBER )
				lua.PushString( lua.IsInteger( 1 ) ? "integer" : "float" );
			else
			{
				lua.L_CheckAny( 1 );
				lua.PushNil(); // luaL_pushfail
			}
			return 1;
		}

		// Pseudo-Random Number Generator based on 'xoshiro256**'

		// number of binary digits in the mantissa of a float
		private const int FIGS = 53;

		// rotate left 'x' by 'n' bits
		private static ulong Rotl( ulong x, int n )
		{
			return (x << n) | (x >> (64 - n));
		}

		private static ulong NextRand( ulong[] state )
		{
			ulong state0 = state[0];
			ulong state1 = state[1];
			ulong state2 = state[2] ^ state0;
			ulong state3 = state[3] ^ state1;
			ulong res = unchecked( Rotl( state1 * 5, 7 ) * 9 );
			state[0] = state0 ^ state3;
			state[1] = state1 ^ state2;
			state[2] = state2 ^ (state1 << 17);
			state[3] = Rotl( state3, 45 );
			return res;
		}

		// Convert bits from a random integer into a float in the
		// interval [0,1), getting the higher FIG bits from the
		// random unsigned integer and converting that to a float.

		// must throw out the extra (64 - FIGS) bits
		private const int shift64_FIG = 64 - FIGS;

		// 2^(-FIGS) == 2^-1 / 2^(FIGS-1)
		private const double scaleFIG = 0.5 / (1UL << (FIGS - 1));

		private static double I2d( ulong x )
		{
			long sx = (long)(x >> shift64_FIG);
			double res = (double)sx * scaleFIG;
			if( sx < 0 )
				res += 1.0; // correct the two's complement if negative
			return res;
		}

		// A state uses four 'Rand64' values: the userdata that 'random'
		// and 'randomseed' share as their upvalue
		private sealed class RanState
		{
			public readonly ulong[] s = new ulong[4];
		}

		// Project the random integer 'ran' into the interval [0, n].
		// Because 'ran' has 2^B possible values, the projection can only be
		// uniform when the size of the interval is a power of 2 (exact
		// division). So, to get a uniform projection into [0, n], we
		// first compute 'lim', the smallest Mersenne number not smaller than
		// 'n'. We then project 'ran' into the interval [0, lim].  If the result
		// is inside [0, n], we are done. Otherwise, we try with another 'ran',
		// until we have a result inside the interval.
		private static ulong Project( ulong ran, ulong n, RanState state )
		{
			ulong lim = n; // to compute the Mersenne number
			int sh; // how much to spread bits to the right in 'lim'
			// spread '1' bits in 'lim' until it becomes a Mersenne number
			for( sh = 1; (lim & unchecked( lim + 1 )) != 0; sh *= 2 )
				lim |= (lim >> sh); // spread '1's to the right
			while( (ran &= lim) > n ) // project 'ran' into [0..lim] and test
				ran = NextRand( state.s ); // not inside [0..n]? try again
			return ran;
		}

		private static int Math_Random( ILuaState lua )
		{
			long low, up;
			ulong p;
			RanState state = (RanState)lua.ToUserData( lua.UpvalueIndex( 1 ) );
			ulong rv = NextRand( state.s ); // next pseudo-random value
			switch( lua.GetTop() ) // check number of arguments
			{
				case 0: // no arguments
					lua.PushNumber( I2d( rv ) ); // float between 0 and 1
					return 1;
				case 1: // only upper limit
					low = 1;
					up = lua.L_CheckInteger( 1 );
					if( up == 0 ) // single 0 as argument?
					{
						lua.PushInteger( unchecked( (long)rv ) ); // full random integer
						return 1;
					}
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
			// project random integer into the interval [0, up - low]
			p = Project( rv, unchecked( (ulong)up - (ulong)low ), state );
			lua.PushInteger( unchecked( (long)(p + (ulong)low) ) );
			return 1;
		}

		private static void SetSeed( ILuaState lua, ulong[] state, ulong n1, ulong n2 )
		{
			state[0] = n1;
			state[1] = 0xff; // avoid a zero state
			state[2] = n2;
			state[3] = 0;
			for( int i = 0; i < 16; i++ )
				NextRand( state ); // discard initial values to "spread" seed
			lua.PushInteger( unchecked( (long)n1 ) );
			lua.PushInteger( unchecked( (long)n2 ) );
		}

		private static int Math_RandomSeed( ILuaState lua )
		{
			RanState state = (RanState)lua.ToUserData( lua.UpvalueIndex( 1 ) );
			ulong n1, n2;
			if( lua.IsNone( 1 ) )
			{
				n1 = lua.L_MakeSeed(); // "random" seed
				n2 = NextRand( state.s ); // in case seed is not that random...
			}
			else
			{
				n1 = unchecked( (ulong)lua.L_CheckInteger( 1 ) );
				n2 = unchecked( (ulong)lua.L_OptInteger( 2, 0 ) );
			}
			SetSeed( lua, state.s, n1, n2 );
			return 2; // return seeds
		}

		// Register the random functions and initialize their state.
		private static void SetRandFunc( ILuaState lua )
		{
			NameFuncPair[] randfuncs = new NameFuncPair[]
			{
				new NameFuncPair( "random",     Math_Random ),
				new NameFuncPair( "randomseed", Math_RandomSeed ),
			};
			RanState state = new RanState();
			lua.NewUserDataUV( state, 0 );
			SetSeed( lua, state.s, lua.L_MakeSeed(), 0 ); // initialize with random seed
			lua.Pop( 2 ); // remove pushed seeds
			lua.L_SetFuncs( randfuncs, 1 );
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
	}

}
