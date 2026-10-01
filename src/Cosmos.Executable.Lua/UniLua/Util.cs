// Part of UniLua (see LICENSE.txt in this directory), adapted for Cosmos.
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


		private static double ReadHexa( string s, ref int pos, double r, out int count )
		{
			count = 0;
			while( pos < s.Length && IsXDigit( s[pos] ) )
			{
				r = (r * 16.0) + HexaValue( s[pos] );
				++pos;
				++count;
			}
			return r;
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

		// following C99 specification for 'strtod'
		public static double StrX2Number( string s, ref int curpos )
		{
			int pos = curpos;
			while( pos < s.Length && IsSpace( s[pos] )) ++pos;
			bool negative = IsNegative( s, ref pos );

			// check `0x'
			if( pos >= s.Length || !(s[pos] == '0' && (s[pos+1] == 'x' || s[pos+1] == 'X')) )
				return 0.0;

			pos += 2; // skip `0x'

			double r = 0.0;
			int i = 0;
			int e = 0;
			r = ReadHexa( s, ref pos, r, out i );
			if( pos < s.Length && s[pos] == '.' )
			{
				++pos; // skip `.'
				r = ReadHexa( s, ref pos, r, out e );
			}
			if( i == 0 && e == 0 )
				return 0.0; // invalid format (no digit)

			// each fractional digit divides value by 2^-4
			e *= -4;
			curpos = pos;

			// exponent part
			if( pos < s.Length && (s[pos] == 'p' || s[pos] == 'P') )
			{
				++pos; // skip `p'
				bool expNegative = IsNegative( s, ref pos );
				if( pos >= s.Length || !IsDigit( s[pos] ) )
					goto ret;

				int exp1 = 0;
				while( pos < s.Length && IsDigit( s[pos] ) )
				{
					exp1 = exp1 * 10 + (s[pos] - '0');
					++pos;
				}
				if( expNegative )
					exp1 = -exp1;
				e += exp1;
			}
			curpos = pos;

ret:
			if( negative ) r = -r;

			return r * Math.Pow(2.0, e);
		}

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

