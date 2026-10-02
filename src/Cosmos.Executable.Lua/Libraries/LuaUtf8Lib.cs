// A port of lutf8lib.c of Lua 5.4 (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using StringBuilder = System.Text.StringBuilder;

	// The utf8 library: strings hold bytes, one per character, and these
	// functions read and write their UTF-8 sequences
	internal static class LuaUtf8Lib
	{
		public const string LIB_NAME = "utf8";

		private const long MAXUNICODE = 0x10FFFF;

		private const long MAXUTF = 0x7FFFFFFF;

		private const string MSGInvalid = "invalid UTF-8 code";

		// pattern to match a single UTF-8 character
		private const string UTF8PATT = "[\0-\x7F\xC2-\xFD][\x80-\xBF]*";

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "offset", 	ByteOffset ),
				new NameFuncPair( "codepoint", 	CodePoint ),
				new NameFuncPair( "char", 		UtfChar ),
				new NameFuncPair( "len", 		UtfLen ),
				new NameFuncPair( "codes", 		IterCodes ),
			};

			lua.L_NewLib( define );
			lua.PushString( UTF8PATT );
			lua.SetField( -2, "charpattern" );
			return 1;
		}

		// the byte at 'i', and the '\0' C finds after the end of a string
		private static int At( string s, long i )
		{
			return i < s.Length ? s[(int)i] & 0xFF : 0;
		}

		private static bool IsCont( int c )
		{
			return (c & 0xC0) == 0x80;
		}

		// iscontp: whether the byte at 'i' is a continuation byte
		private static bool IsCont( string s, long i )
		{
			return IsCont( At( s, i ) );
		}

		// translate a relative string position: negative means back from end
		private static long PosRelative( long pos, int len )
		{
			if( pos >= 0 ) return pos;
			else if( 0u - (ulong)pos > (ulong)len ) return 0;
			else return len + pos + 1;
		}

		// the minimum value for each sequence length, to check for overlong
		// representations: the first entry forces an error for non-ascii
		// bytes with no continuation bytes (count == 0)
		private static readonly long[] Limits =
			{ ~0L, 0x80, 0x800, 0x10000, 0x200000, 0x4000000 };

		// utf8_decode: the code point of the sequence at 'i', and the position
		// after it; -1 if the sequence is invalid. 'strict' rejects the code
		// points too large or surrogates
		private static long Decode( string s, long i, out long val, bool strict )
		{
			int c = At( s, i );
			long res = 0; // final result
			val = 0;
			if( c < 0x80 ) // ascii?
				res = c;
			else if( c >= 0xFE ) // c >= 1111 1110b ?
				return -1; // would need six or more continuation bytes
			else
			{
				int count = 0; // to count number of continuation bytes
				for( ; (c & 0x40) != 0; c <<= 1 ) // while it needs continuation bytes...
				{
					int cc = At( s, i + (++count) ); // read next byte
					if( !IsCont( cc ) ) // not a continuation byte?
						return -1; // invalid byte sequence
					res = (res << 6) | (long)(cc & 0x3F); // add lower 6 bits from cont. byte
				}
				res |= ((long)(c & 0x7F) << (count * 5)); // add first byte
				if( res > MAXUTF || (ulong)res < (ulong)Limits[count] )
					return -1; // invalid byte sequence
				i += count; // skip continuation bytes read
			}
			if( strict )
			{
				// check for invalid code points; too large or surrogates
				if( res > MAXUNICODE || (0xD800 <= res && res <= 0xDFFF) )
					return -1;
			}
			val = res;
			return i + 1; // +1 to include first byte
		}

		// utf8.len(s [, i [, j [, lax]]]): the number of characters that start
		// in [i,j], or fail and the position of the first invalid byte
		private static int UtfLen( ILuaState lua )
		{
			long n = 0; // counter for the number of characters
			string s = lua.L_CheckString( 1 );
			int len = s.Length; // string length in bytes
			long posi = PosRelative( lua.L_OptInteger( 2, 1 ), len );
			long posj = PosRelative( lua.L_OptInteger( 3, -1 ), len );
			bool lax = lua.ToBoolean( 4 );
			lua.L_ArgCheck( 1 <= posi && --posi <= len, 2, "initial position out of bounds" );
			lua.L_ArgCheck( --posj < len, 3, "final position out of bounds" );
			while( posi <= posj )
			{
				long code;
				long s1 = Decode( s, posi, out code, !lax );
				if( s1 < 0 ) // conversion error?
				{
					lua.PushNil(); // return fail ...
					lua.PushInteger( posi + 1 ); // ... and current position
					return 2;
				}
				posi = s1;
				n++;
			}
			lua.PushInteger( n );
			return 1;
		}

		// utf8.codepoint(s [, i [, j [, lax]]]): the code points of the
		// characters that start in [i,j]
		private static int CodePoint( ILuaState lua )
		{
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			long posi = PosRelative( lua.L_OptInteger( 2, 1 ), len );
			long pose = PosRelative( lua.L_OptInteger( 3, posi ), len );
			bool lax = lua.ToBoolean( 4 );
			lua.L_ArgCheck( posi >= 1, 2, "out of bounds" );
			lua.L_ArgCheck( pose <= len, 3, "out of bounds" );
			if( posi > pose ) return 0; // empty interval; return no values
			if( pose - posi >= int.MaxValue ) // (lua_Integer -> int) overflow?
				return lua.L_Error( "string slice too long" );
			int n = (int)(pose - posi) + 1; // upper bound for number of returns
			lua.L_CheckStack( n, "string slice too long" );
			n = 0; // count the number of returns
			for( long i = posi - 1; i < pose; ) // 'pose' is the string end
			{
				long code;
				i = Decode( s, i, out code, !lax );
				if( i < 0 )
					return lua.L_Error( MSGInvalid );
				lua.PushInteger( code );
				n++;
			}
			return n;
		}

		// luaO_utf8esc: the UTF-8 bytes of a code point (up to 0x7FFFFFFF)
		internal static void AppendUtf8( StringBuilder b, long x )
		{
			if( x < 0x80 ) // ascii?
			{
				b.Append( (char)x );
				return;
			}
			var buff = new char[8];
			int n = 1; // number of bytes put in buffer (backwards)
			long mfb = 0x3f; // maximum that fits in first byte
			do { // add continuation bytes
				buff[8 - (n++)] = (char)(0x80 | (x & 0x3f));
				x >>= 6; // remove added bits
				mfb >>= 1; // now there is one less bit available in first byte
			} while( x > mfb ); // still needs continuation byte?
			buff[8 - n] = (char)(((~mfb << 1) | x) & 0xFF); // add first byte
			b.Append( buff, 8 - n, n );
		}

		private static void AddUtfChar( ILuaState lua, StringBuilder b, int arg )
		{
			ulong code = unchecked( (ulong)lua.L_CheckInteger( arg ) );
			lua.L_ArgCheck( code <= MAXUTF, arg, "value out of range" );
			AppendUtf8( b, (long)code );
		}

		// utf8.char(n1, n2, ...): the characters of those code points
		private static int UtfChar( ILuaState lua )
		{
			int n = lua.GetTop(); // number of arguments
			var b = new StringBuilder();
			for( int i = 1; i <= n; i++ )
				AddUtfChar( lua, b, i );
			lua.PushString( b.ToString() );
			return 1;
		}

		// utf8.offset(s, n [, i]): the position where the n-th character
		// counting from position 'i' starts; 0 means character at 'i'
		private static int ByteOffset( ILuaState lua )
		{
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			long n = lua.L_CheckInteger( 2 );
			long posi = (n >= 0) ? 1 : len + 1;
			posi = PosRelative( lua.L_OptInteger( 3, posi ), len );
			lua.L_ArgCheck( 1 <= posi && --posi <= len, 3, "position out of bounds" );
			if( n == 0 )
			{
				// find beginning of current byte sequence
				while( posi > 0 && IsCont( s, posi ) ) posi--;
			}
			else
			{
				if( IsCont( s, posi ) )
					return lua.L_Error( "initial position is a continuation byte" );
				if( n < 0 )
				{
					while( n < 0 && posi > 0 ) // move back
					{
						do { // find beginning of previous character
							posi--;
						} while( posi > 0 && IsCont( s, posi ) );
						n++;
					}
				}
				else
				{
					n--; // do not move for 1st character
					while( n > 0 && posi < len )
					{
						do { // find beginning of next character
							posi++;
						} while( IsCont( s, posi ) ); // (cannot pass final '\0')
						n--;
					}
				}
			}
			if( n == 0 ) // did it find given character?
				lua.PushInteger( posi + 1 );
			else // no such character
				lua.PushNil(); // fail
			return 1;
		}

		private static int IterAux( ILuaState lua, bool strict )
		{
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			ulong n = unchecked( (ulong)lua.ToInteger( 2 ) );
			if( n < (ulong)len )
			{
				while( IsCont( s, (long)n ) ) n++; // go to next character
			}
			if( n >= (ulong)len ) // (also handles original 'n' being negative)
				return 0; // no more codepoints
			else
			{
				long code;
				long next = Decode( s, (long)n, out code, strict );
				if( next < 0 || IsCont( s, next ) )
					return lua.L_Error( MSGInvalid );
				lua.PushInteger( (long)n + 1 );
				lua.PushInteger( code );
				return 2;
			}
		}

		private static int IterAuxStrict( ILuaState lua )
		{
			return IterAux( lua, true );
		}

		private static int IterAuxLax( ILuaState lua )
		{
			return IterAux( lua, false );
		}

		// utf8.codes(s [, lax]): an iterator over the positions and code
		// points of s
		private static int IterCodes( ILuaState lua )
		{
			bool lax = lua.ToBoolean( 2 );
			string s = lua.L_CheckString( 1 );
			lua.L_ArgCheck( !IsCont( s, 0 ), 1, MSGInvalid );
			lua.PushCSharpFunction( lax ? (CSharpFunctionDelegate)IterAuxLax : IterAuxStrict );
			lua.PushValue( 1 );
			lua.PushInteger( 0 );
			return 3;
		}
	}

}
