// A port of lutf8lib.c of Lua 5.3 (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
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

		// pattern to match a single UTF-8 character
		private const string UTF8PATT = "[\0-\x7F\xC2-\xF4][\x80-\xBF]*";

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

		private static bool IsCont( string s, long i )
		{
			return (At( s, i ) & 0xC0) == 0x80;
		}

		// translate a relative string position: negative means back from end
		private static long PosRelative( long pos, int len )
		{
			if( pos >= 0 ) return pos;
			else if( 0u - (ulong)pos > (ulong)len ) return 0;
			else return len + pos + 1;
		}

		// utf8_decode: the code point of the sequence at 'i', and the position
		// after it; -1 if the sequence is invalid
		private static long Decode( string s, long i, out int val )
		{
			int[] limits = { 0xFF, 0x7F, 0x7FF, 0xFFFF };
			int c = At( s, i );
			int res = 0; // final result
			val = 0;
			if( c < 0x80 ) // ascii?
				res = c;
			else
			{
				int count = 0; // to count number of continuation bytes
				while( (c & 0x40) != 0 ) // still have continuation bytes?
				{
					int cc = At( s, i + (++count) ); // read next byte
					if( (cc & 0xC0) != 0x80 ) // not a continuation byte?
						return -1; // invalid byte sequence
					res = (res << 6) | (cc & 0x3F); // add lower 6 bits from cont. byte
					c <<= 1; // to test next bit
				}
				res |= ((c & 0x7F) << (count * 5)); // add first byte
				if( count > 3 || res > MAXUNICODE || res <= limits[count] )
					return -1; // invalid byte sequence
				i += count; // skip continuation bytes read
			}
			val = res;
			return i + 1; // +1 to include first byte
		}

		// utf8.len(s [, i [, j]]): the number of characters that start in
		// [i,j], or nil and the position of the first invalid byte
		private static int UtfLen( ILuaState lua )
		{
			int n = 0;
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			long posi = PosRelative( lua.L_OptInteger( 2, 1 ), len );
			long posj = PosRelative( lua.L_OptInteger( 3, -1 ), len );
			lua.L_ArgCheck( 1 <= posi && --posi <= len, 2, "initial position out of string" );
			lua.L_ArgCheck( --posj < len, 3, "final position out of string" );
			while( posi <= posj )
			{
				int code;
				long s1 = Decode( s, posi, out code );
				if( s1 < 0 ) // conversion error?
				{
					lua.PushNil(); // return nil ...
					lua.PushInteger( posi + 1 ); // ... and current position
					return 2;
				}
				posi = s1;
				n++;
			}
			lua.PushInteger( n );
			return 1;
		}

		// utf8.codepoint(s [, i [, j]]): the code points of the characters
		// that start in [i,j]
		private static int CodePoint( ILuaState lua )
		{
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			long posi = PosRelative( lua.L_OptInteger( 2, 1 ), len );
			long pose = PosRelative( lua.L_OptInteger( 3, posi ), len );
			lua.L_ArgCheck( posi >= 1, 2, "out of range" );
			lua.L_ArgCheck( pose <= len, 3, "out of range" );
			if( posi > pose ) return 0; // empty interval; return no values
			if( pose - posi >= int.MaxValue ) // (lua_Integer -> int) overflow?
				return lua.L_Error( "string slice too long" );
			int n = (int)(pose - posi) + 1;
			lua.L_CheckStack( n, "string slice too long" );
			n = 0;
			for( long i = posi - 1; i < pose; )
			{
				int code;
				i = Decode( s, i, out code );
				if( i < 0 )
					return lua.L_Error( "invalid UTF-8 code" );
				lua.PushInteger( code );
				n++;
			}
			return n;
		}

		// luaO_utf8esc: the UTF-8 bytes of a code point
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
			long code = lua.L_CheckInteger( arg );
			lua.L_ArgCheck( 0 <= code && code <= MAXUNICODE, arg, "value out of range" );
			AppendUtf8( b, code );
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
			lua.L_ArgCheck( 1 <= posi && --posi <= len, 3, "position out of range" );
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
				lua.PushNil();
			return 1;
		}

		private static int IterAux( ILuaState lua )
		{
			string s = lua.L_CheckString( 1 );
			int len = s.Length;
			long n = lua.ToInteger( 2 ) - 1;
			if( n < 0 ) // first iteration?
				n = 0; // start from here
			else if( n < len )
			{
				n++; // skip current byte
				while( IsCont( s, n ) ) n++; // and its continuations
			}
			if( n >= len )
				return 0; // no more codepoints
			int code;
			long next = Decode( s, n, out code );
			if( next < 0 || IsCont( s, next ) )
				return lua.L_Error( "invalid UTF-8 code" );
			lua.PushInteger( n + 1 );
			lua.PushInteger( code );
			return 2;
		}

		// utf8.codes(s): an iterator over the positions and code points of s
		private static int IterCodes( ILuaState lua )
		{
			lua.L_CheckString( 1 );
			lua.PushCSharpFunction( IterAux );
			lua.PushValue( 1 );
			lua.PushInteger( 0 );
			return 3;
		}
	}

}
