// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


#define API_CHECK
#define UNILUA_ASSERT

using System;

namespace Cosmos.Executable.Lua
{
	using DebugS = System.Diagnostics.Debug;
	using NumberStyles = System.Globalization.NumberStyles;

	internal static class Utl
	{
		private static void Throw( params string[] msgs )
		{
			throw new Exception(String.Join("", msgs));
		}

		public static void Assert( bool condition )
		{
#if UNILUA_ASSERT
			if( !condition )
				Throw("assert failed!" );
			DebugS.Assert( condition );
#endif
		}

		public static void Assert( bool condition, string message )
		{
#if UNILUA_ASSERT
			if( !condition )
				Throw( "assert failed! ", message );
			DebugS.Assert( condition, message );
#endif
		}

		public static void Assert( bool condition, string message, string detailMessage )
		{
#if UNILUA_ASSERT
			if( !condition )
				Throw( "assert failed! ", message, "\n", detailMessage );
			DebugS.Assert( condition, message, detailMessage );
#endif
		}

		public static void ApiCheck( bool condition, string message )
		{
#if UNILUA_ASSERT
#if API_CHECK
			Assert( condition, message );
#endif
#endif
		}

		public static void ApiCheckNumElems( LuaState lua, int n )
		{
#if UNILUA_ASSERT
			Assert( n < (lua.Top.Index - lua.CI.FuncIndex), "not enough elements in the stack" );
#endif
		}

		public static void InvalidIndex()
		{
#if UNILUA_ASSERT
			Assert( false, "invalid index" );
#endif
		}

		private static bool IsNegative( string s, ref int pos )
		{
			if( pos >= s.Length )
				return false;

			char c = s[pos];
			if( c == '-' )
			{
				++pos;
				return true;
			}
			else if( c == '+' )
			{
				++pos;
			}
			return false;
		}


		private static double ReadDecimal( string s, ref int pos, double r, out int count )
		{
			count = 0;
			while( pos < s.Length && IsDigit( s[pos] ) )
			{
				r = (r * 10.0) + (s[pos] - '0');
				++pos;
				++count;
			}
			return r;
		}

		// lua_strx2number: a hexadecimal numeral, its digits past the first
		// MAXSIGDIG significant ones counted in the exponent only, so that a
		// numeral of any length does not overflow the accumulator
		public static double StrX2Number( string s, ref int curpos )
		{
			const int MAXSIGDIG = 30;
			int pos = curpos;
			while( pos < s.Length && IsSpace( s[pos] )) ++pos;
			bool negative = IsNegative( s, ref pos );

			// check `0x'
			if( pos + 1 >= s.Length || !(s[pos] == '0' && (s[pos+1] == 'x' || s[pos+1] == 'X')) )
				return 0.0; // invalid format (no '0x')

			double r = 0.0; // result (accumulator)
			int sigdig = 0; // number of significant digits
			int nosigdig = 0; // number of non-significant digits
			int e = 0; // exponent correction
			bool hasdot = false; // true after seen a dot
			for( pos += 2; pos < s.Length; ++pos ) // skip '0x' and read numeral
			{
				char c = s[pos];
				if( c == '.' )
				{
					if( hasdot ) break; // second dot? stop loop
					hasdot = true;
				}
				else if( IsXDigit( c ) )
				{
					if( sigdig == 0 && c == '0' ) // non-significant digit (zero)?
						nosigdig++;
					else if( ++sigdig <= MAXSIGDIG ) // can read it without overflow?
						r = (r * 16.0) + HexaValue( c );
					else e++; // too many digits; ignore, but still count for exponent
					if( hasdot ) e--; // decimal digit? correct exponent
				}
				else break; // neither a dot nor a digit
			}
			if( nosigdig + sigdig == 0 ) // no digits?
				return 0.0; // invalid format
			curpos = pos; // valid up to here
			e *= 4; // each digit multiplies/divides value by 2^4

			if( pos < s.Length && (s[pos] == 'p' || s[pos] == 'P') ) // exponent part?
			{
				++pos; // skip 'p'
				bool expNegative = IsNegative( s, ref pos );
				if( pos >= s.Length || !IsDigit( s[pos] ) )
					return 0.0; // invalid; must have at least one digit
				int exp1 = 0; // exponent value
				while( pos < s.Length && IsDigit( s[pos] ) ) // read exponent
				{
					exp1 = Math.Min( exp1 * 10 + (s[pos] - '0'), 1 << 24 ); // way past any double
					++pos;
				}
				if( expNegative )
					exp1 = -exp1;
				e += exp1;
				curpos = pos; // valid up to here
			}

			if( negative ) r = -r;
			return Math.ScaleB( r, e );
		}

		// following C99 specification for 'strtod'
		public static double Str2Number( string s, ref int curpos )
		{
			int pos = curpos;
			while( pos < s.Length && IsSpace( s[pos] )) ++pos;
			int start = pos;
			bool negative = IsNegative( s, ref pos );

			double r = 0.0;
			int i = 0;
			int f = 0;
			r = ReadDecimal( s, ref pos, r, out i );
			if( pos < s.Length && s[pos] == '.' )
			{
				++pos;
				r = ReadDecimal( s, ref pos, r, out f );
			}
			if( i == 0 && f == 0 )
				return 0.0;

			f = -f;
			curpos = pos;

			// exponent part
			double e = 0.0;
			if( pos < s.Length && (s[pos] == 'e' || s[pos] == 'E') )
			{
				++pos;
				bool expNegative = IsNegative( s, ref pos );
				if( pos >= s.Length || !IsDigit( s[pos] ) )
					goto ret;

				int n;
				e = ReadDecimal( s, ref pos, e, out n );
				if( expNegative )
					e = -e;
				f += (int)e;
			}
			curpos = pos;

ret:
			// What was scanned, rounded once by the BCL: digits * 10^f is off by
			// rounding errors, so that 0.3 came out as 0.30000000000000004
			return Double.Parse( s.AsSpan( start, curpos - start ),
				NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture );
		}

		// <ctype.h> in the "C" locale, as Lua uses it: ASCII only, so that
		// chars above 127 are neither letters nor spaces nor punctuation
		public static bool IsAlpha( int c ) { return ('a' <= c && c <= 'z') || ('A' <= c && c <= 'Z'); }
		public static bool IsDigit( int c ) { return '0' <= c && c <= '9'; }
		public static bool IsAlnum( int c ) { return IsAlpha( c ) || IsDigit( c ); }
		public static bool IsSpace( int c ) { return c == ' ' || ('\t' <= c && c <= '\r'); }
		public static bool IsCntrl( int c ) { return (0 <= c && c < 32) || c == 127; }
		public static bool IsGraph( int c ) { return 32 < c && c < 127; }
		public static bool IsPrint( int c ) { return 32 <= c && c < 127; }
		public static bool IsPunct( int c ) { return IsGraph( c ) && !IsAlnum( c ); }
		public static bool IsLower( int c ) { return 'a' <= c && c <= 'z'; }
		public static bool IsUpper( int c ) { return 'A' <= c && c <= 'Z'; }
		public static bool IsXDigit( int c ) { return IsDigit( c ) || ('a' <= c && c <= 'f') || ('A' <= c && c <= 'F'); }
		public static int HexaValue( int c ) { return IsDigit( c ) ? c - '0' : (c | 0x20) - 'a' + 10; }

		public static string TrimWhiteSpace( string str )
		{
			int s = 0;
			int e = str.Length;

			while( s < str.Length && Char.IsWhiteSpace( str[s] ) ) ++s;
			if( s >= e )
				return "";

			while( e >= 0 && Char.IsWhiteSpace( str[e-1] ) ) --e;
			return str.Substring( s, e-s );
		}
	}

}

