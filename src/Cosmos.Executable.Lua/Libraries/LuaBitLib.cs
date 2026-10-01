// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	// lbitlib.c of Lua 5.3, which keeps bit32 for Lua 5.2 code
	// (LUA_COMPAT_BITLIB): 32-bit operations on integers
	internal class LuaBitLib
	{
		public const string LIB_NAME = "bit32";

		private const int LUA_NBITS = 32;

		private const ulong ALLONES = ~(((~(ulong)0) << (LUA_NBITS - 1)) << 1);

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "arshift", 	B_ArithShift 	),
				new NameFuncPair( "band", 		B_And 			),
				new NameFuncPair( "bnot", 		B_Not 			),
				new NameFuncPair( "bor", 		B_Or 			),
				new NameFuncPair( "bxor", 		B_Xor 			),
				new NameFuncPair( "btest", 		B_Test 			),
				new NameFuncPair( "extract", 	B_Extract 		),
				new NameFuncPair( "lrotate", 	B_LeftRotate 	),
				new NameFuncPair( "lshift", 	B_LeftShift 	),
				new NameFuncPair( "replace", 	B_Replace 		),
				new NameFuncPair( "rrotate", 	B_RightRotate 	),
				new NameFuncPair( "rshift", 	B_RightShift 	),
			};

			lua.L_NewLib( define );
			return 1;
		}

		private static ulong Trim( ulong x ) { return x & ALLONES; }

		private static ulong Mask( int n ) { return ~((ALLONES << 1) << (n - 1)); }

		private static ulong CheckUnsigned( ILuaState lua, int i )
		{
			return unchecked( (ulong)lua.L_CheckInteger( i ) );
		}

		private static void PushUnsigned( ILuaState lua, ulong n )
		{
			lua.PushInteger( unchecked( (long)n ) );
		}

		private static ulong AndAux( ILuaState lua )
		{
			int n = lua.GetTop();
			ulong r = ~(ulong)0;
			for( int i=1; i<=n; ++i )
				r &= CheckUnsigned( lua, i );
			return Trim( r );
		}

		private static int B_And( ILuaState lua )
		{
			PushUnsigned( lua, AndAux( lua ) );
			return 1;
		}

		private static int B_Test( ILuaState lua )
		{
			lua.PushBoolean( AndAux( lua ) != 0 );
			return 1;
		}

		private static int B_Or( ILuaState lua )
		{
			int n = lua.GetTop();
			ulong r = 0;
			for( int i=1; i<=n; ++i )
				r |= CheckUnsigned( lua, i );
			PushUnsigned( lua, Trim( r ) );
			return 1;
		}

		private static int B_Xor( ILuaState lua )
		{
			int n = lua.GetTop();
			ulong r = 0;
			for( int i=1; i<=n; ++i )
				r ^= CheckUnsigned( lua, i );
			PushUnsigned( lua, Trim( r ) );
			return 1;
		}

		private static int B_Not( ILuaState lua )
		{
			PushUnsigned( lua, Trim( ~CheckUnsigned( lua, 1 ) ) );
			return 1;
		}

		private static int B_Shift( ILuaState lua, ulong r, long i )
		{
			if( i < 0 ) // shift right?
			{
				i = -i;
				r = Trim( r );
				if( i >= LUA_NBITS ) r = 0;
				else r >>= (int)i;
			}
			else // shift left
			{
				if( i >= LUA_NBITS ) r = 0;
				else r <<= (int)i;
				r = Trim( r );
			}
			PushUnsigned( lua, r );
			return 1;
		}

		private static int B_LeftShift( ILuaState lua )
		{
			return B_Shift( lua, CheckUnsigned( lua, 1 ), lua.L_CheckInteger( 2 ) );
		}

		private static int B_RightShift( ILuaState lua )
		{
			return B_Shift( lua, CheckUnsigned( lua, 1 ), unchecked( -lua.L_CheckInteger( 2 ) ) );
		}

		private static int B_ArithShift( ILuaState lua )
		{
			ulong r = CheckUnsigned( lua, 1 );
			long i = lua.L_CheckInteger( 2 );
			if( i < 0 || (r & ((ulong)1 << (LUA_NBITS - 1))) == 0 )
				return B_Shift( lua, r, unchecked( -i ) );
			else // arithmetic shift for 'negative' number
			{
				if( i >= LUA_NBITS ) r = ALLONES;
				else
					r = Trim( (r >> (int)i) | ~(Trim( ~(ulong)0 ) >> (int)i) ); // add signal bit
				PushUnsigned( lua, r );
				return 1;
			}
		}

		private static int B_Rotate( ILuaState lua, long d )
		{
			ulong r = CheckUnsigned( lua, 1 );
			int i = (int)(d & (LUA_NBITS - 1)); // i = d % NBITS
			r = Trim( r );
			if( i != 0 ) // avoid undefined shift of LUA_NBITS when i == 0
				r = (r << i) | (r >> (LUA_NBITS - i));
			PushUnsigned( lua, Trim( r ) );
			return 1;
		}

		private static int B_LeftRotate( ILuaState lua )
		{
			return B_Rotate( lua, lua.L_CheckInteger( 2 ) );
		}

		private static int B_RightRotate( ILuaState lua )
		{
			return B_Rotate( lua, unchecked( -lua.L_CheckInteger( 2 ) ) );
		}

		private static int FieldArgs( ILuaState lua, int farg, out int width )
		{
			long f = lua.L_CheckInteger( farg );
			long w = lua.L_OptInteger( farg + 1, 1 );
			lua.L_ArgCheck( 0 <= f, farg, "field cannot be negative" );
			lua.L_ArgCheck( 0 < w, farg + 1, "width must be positive" );
			if( f + w > LUA_NBITS )
				lua.L_Error( "trying to access non-existent bits" );
			width = (int)w;
			return (int)f;
		}

		private static int B_Extract( ILuaState lua )
		{
			int w;
			ulong r = Trim( CheckUnsigned( lua, 1 ) );
			int f = FieldArgs( lua, 2, out w );
			r = (r >> f) & Mask( w );
			PushUnsigned( lua, r );
			return 1;
		}

		private static int B_Replace( ILuaState lua )
		{
			int w;
			ulong r = Trim( CheckUnsigned( lua, 1 ) );
			ulong v = Trim( CheckUnsigned( lua, 2 ) );
			int f = FieldArgs( lua, 3, out w );
			ulong m = Mask( w );
			r = (r & ~(m << f)) | ((v & m) << f);
			PushUnsigned( lua, r );
			return 1;
		}
	}

}
