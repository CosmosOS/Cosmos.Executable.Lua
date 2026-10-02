// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using StringBuilder = System.Text.StringBuilder;
	using Convert = System.Convert;
	using Math = System.Math;
	using BitConverter = System.BitConverter;
	using StringComparison = System.StringComparison;
	using CultureInfo = System.Globalization.CultureInfo;
	using RuntimeHelpers = System.Runtime.CompilerServices.RuntimeHelpers;

	// lstrlib.c of Lua 5.4. A string holds bytes, one per character, and
	// character classes are those of the "C" locale (ASCII).
	internal static class LuaStrLib
	{
		public const string LIB_NAME = "string";

		private const int CAP_UNFINISHED 	= -1;
		private const int CAP_POSITION		= -2;
		private const int LUA_MAXCAPTURES 	= 32;
		private const int MAXCCALLS			= 200; // maximum recursion depth for 'match'
		private const char L_ESC 			= '%';
		private static readonly char[] SPECIALS;

		// what a string can hold at most here
		private const int MAXSIZE			= 0x3FFFFFDF;

		static LuaStrLib()
		{
			SPECIALS = "^$*+?.([%-".ToCharArray();
		}

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "byte", 		Str_Byte ),
				new NameFuncPair( "char", 		Str_Char ),
				new NameFuncPair( "dump", 		Str_Dump ),
				new NameFuncPair( "find", 		Str_Find ),
				new NameFuncPair( "format", 	Str_Format ),
				new NameFuncPair( "gmatch", 	Str_Gmatch ),
				new NameFuncPair( "gsub", 		Str_Gsub ),
				new NameFuncPair( "len", 		Str_Len ),
				new NameFuncPair( "lower", 		Str_Lower ),
				new NameFuncPair( "match", 		Str_Match ),
				new NameFuncPair( "rep", 		Str_Rep ),
				new NameFuncPair( "pack", 		Str_Pack ),
				new NameFuncPair( "packsize", 	Str_PackSize ),
				new NameFuncPair( "unpack", 	Str_Unpack ),
				new NameFuncPair( "reverse", 	Str_Reverse ),
				new NameFuncPair( "sub", 		Str_Sub ),
				new NameFuncPair( "upper", 		Str_Upper ),
			};

			lua.L_NewLib( define );
			CreateMetaTable( lua );

			return 1;
		}

		private static void CreateMetaTable( ILuaState lua )
		{
			// table to be metatable for strings ('__index' is set below)
			lua.L_NewLibTable( StringMetaMethods );
			lua.L_SetFuncs( StringMetaMethods, 0 );
			lua.PushString( "" ); // dummy string
			lua.PushValue( -2 ); // copy table
			lua.SetMetaTable( -2 ); // set table as metatable for strings
			lua.Pop( 1 );
			lua.PushValue( -2 ); // get string library
			lua.SetField( -2, "__index" ); // metatable.__index = string
			lua.Pop( 1 ); // pop metatable
		}

		// posrelatI: translate a relative initial string position (negative
		// means back from end): clip result to [1, inf). The inverted
		// comparison avoids a possible overflow computing '-pos'.
		private static long PosRelatI( long pos, int len )
		{
			if( pos > 0 )
				return pos;
			else if( pos == 0 )
				return 1;
			else if( pos < -(long)len ) // inverted comparison
				return 1; // clip to 1
			else return len + pos + 1;
		}

		// getendpos: an optional ending string position from argument 'arg',
		// with default value 'def'. Negative means back from end: clip result
		// to [0, len]
		private static long GetEndPos( ILuaState lua, int arg, long def, int len )
		{
			long pos = lua.L_OptInteger( arg, def );
			if( pos > len )
				return len;
			else if( pos >= 0 )
				return pos;
			else if( pos < -(long)len )
				return 0;
			else return len + pos + 1;
		}

		private static int Str_Len( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			lua.PushInteger( s.Length );
			return 1;
		}

		private static int Str_Sub( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			long start = PosRelatI( lua.L_CheckInteger(2), s.Length );
			long end = GetEndPos( lua, 3, -1, s.Length );
			if( start <= end )
				lua.PushString( s.Substring((int)start-1, (int)(end-start)+1) );
			else
				lua.PushString( "" );
			return 1;
		}

		private static int Str_Reverse( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			StringBuilder sb = new StringBuilder(s.Length);
			for( int i=s.Length-1; i>=0; --i )
				sb.Append( s[i] );
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int Str_Lower( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			StringBuilder sb = new StringBuilder(s.Length);
			for( int i=0; i<s.Length; ++i )
				sb.Append( Utl.IsUpper( s[i] ) ? (char)(s[i] + ('a' - 'A')) : s[i] );
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int Str_Upper( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			StringBuilder sb = new StringBuilder(s.Length);
			for( int i=0; i<s.Length; ++i )
				sb.Append( Utl.IsLower( s[i] ) ? (char)(s[i] - ('a' - 'A')) : s[i] );
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int Str_Rep( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			long n = lua.L_CheckInteger(2);
			string sep = lua.L_OptString(3, "");
			if( n <= 0 )
				lua.PushString( "" );
			else if( (long)s.Length + sep.Length > MAXSIZE / n ) // may overflow?
				return lua.L_Error( "resulting string too large" );
			else
			{
				StringBuilder sb = new StringBuilder( (int)(s.Length * n + sep.Length * (n - 1)) );
				while( n-- > 1 ) // first n-1 copies (followed by separator)
				{
					sb.Append( s );
					sb.Append( sep );
				}
				sb.Append( s ); // last copy (not followed by separator)
				lua.PushString( sb.ToString() );
			}
			return 1;
		}

		private static int Str_Byte( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			long pi = lua.L_OptInteger(2, 1);
			long posi = PosRelatI( pi, s.Length );
			long pose = GetEndPos( lua, 3, pi, s.Length );
			if( posi > pose ) return 0; // empty interval; return no values
			if( pose - posi >= int.MaxValue ) // arithmetic overflow?
				return lua.L_Error( "string slice too long" );
			int n = (int)(pose - posi) + 1;
			lua.L_CheckStack(n, "string slice too long");
			for( int i=0; i<n; ++i )
				lua.PushInteger( s[(int)posi+i-1] );
			return n;
		}

		private static int Str_Char( ILuaState lua )
		{
			int n = lua.GetTop();
			StringBuilder sb = new StringBuilder(n);
			for( int i=1; i<=n; ++i )
			{
				long c = lua.L_CheckInteger(i);
				lua.L_ArgCheck( (ulong)c <= byte.MaxValue, i, "value out of range" );
				sb.Append( (char)c );
			}
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int Str_Dump( ILuaState lua )
		{
			bool strip = lua.ToBoolean( 2 );
			lua.L_CheckType( 1, LuaType.LUA_TFUNCTION );
			lua.SetTop( 1 );
			var bsb = new ByteStringBuilder();
			LuaWriter writeFunc =
				delegate(byte[] bytes, int start, int length)
			{
				bsb.Append(bytes, start, length);
				return DumpStatus.OK;
			};
			if( lua.Dump( writeFunc, strip ) != DumpStatus.OK )
				return lua.L_Error( "unable to dump given function" );
			lua.PushString( bsb.ToString() );
			return 1;
		}

		// ======================================================
		// METAMETHODS
		// ======================================================

		private static readonly NameFuncPair[] StringMetaMethods = new NameFuncPair[]
		{
			new NameFuncPair( "__add", 		Arith_Add ),
			new NameFuncPair( "__sub", 		Arith_Sub ),
			new NameFuncPair( "__mul", 		Arith_Mul ),
			new NameFuncPair( "__mod", 		Arith_Mod ),
			new NameFuncPair( "__pow", 		Arith_Pow ),
			new NameFuncPair( "__div", 		Arith_Div ),
			new NameFuncPair( "__idiv", 	Arith_IDiv ),
			new NameFuncPair( "__unm", 		Arith_Unm ),
		};

		// tonum: pushes the number the argument is, or the one its numerical
		// string converts to; false if it is neither
		private static bool ToNum( ILuaState lua, int arg )
		{
			if( lua.Type( arg ) == LuaType.LUA_TNUMBER ) // already a number?
			{
				lua.PushValue( arg );
				return true;
			}
			else // check whether it is a numerical string
			{
				string s = lua.ToString( arg );
				return s != null && lua.StringToNumber( s ) == s.Length + 1;
			}
		}

		private static void TryMt( ILuaState lua, string mtname )
		{
			lua.SetTop( 2 ); // back to the original arguments
			if( lua.Type( 2 ) == LuaType.LUA_TSTRING || !lua.L_GetMetaField( 2, mtname ) )
				lua.L_Error( "attempt to {0} a '{1}' with a '{2}'", mtname.Substring( 2 ),
					lua.L_TypeName( -2 ), lua.L_TypeName( -1 ) );
			lua.Insert( -3 ); // put metamethod before arguments
			lua.Call( 2, 1 ); // call metamethod
		}

		private static int Arith( ILuaState lua, LuaOp op, string mtname )
		{
			if( ToNum( lua, 1 ) && ToNum( lua, 2 ) )
				lua.Arith( op ); // result will be on the top
			else
				TryMt( lua, mtname );
			return 1;
		}

		private static int Arith_Add( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPADD, "__add" );
		}

		private static int Arith_Sub( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPSUB, "__sub" );
		}

		private static int Arith_Mul( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPMUL, "__mul" );
		}

		private static int Arith_Mod( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPMOD, "__mod" );
		}

		private static int Arith_Pow( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPPOW, "__pow" );
		}

		private static int Arith_Div( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPDIV, "__div" );
		}

		private static int Arith_IDiv( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPIDIV, "__idiv" );
		}

		private static int Arith_Unm( ILuaState lua )
		{
			return Arith( lua, LuaOp.LUA_OPUNM, "__unm" );
		}

		// ======================================================
		// PATTERN MATCHING
		// ======================================================

		class CaptureInfo
		{
			public int Len;
			public int Init;
		}

		class MatchState
		{
			public ILuaState 		Lua;
			public int				MatchDepth; // control for recursive depth
			public int				Level; // total number of captures (finished or unfinished)
			public string			Src;
			public int				SrcInit;
			public int				SrcEnd;
			public string			Pattern;
			public int				PatternEnd;
			public CaptureInfo[]	Capture;

			public MatchState()
			{
				Capture = new CaptureInfo[LUA_MAXCAPTURES];
				for(int i =0; i < LUA_MAXCAPTURES; ++i)
					Capture[i] = new CaptureInfo();
			}

			// C reads the '\0' that ends a string: so does this, past the end
			public char P( int p )
			{
				return p < PatternEnd ? Pattern[p] : '\0';
			}

			public char S( int s )
			{
				return s < SrcEnd ? Src[s] : '\0';
			}
		}

		private static int CheckCapture( MatchState ms, int l )
		{
			l -= '1';
			if( l < 0 || l >= ms.Level || ms.Capture[l].Len == CAP_UNFINISHED )
				return ms.Lua.L_Error( "invalid capture index %{0}", l+1 );
			return l;
		}

		private static int CaptureToClose( MatchState ms )
		{
			int level = ms.Level;
			for( level--; level>=0; level-- )
			{
				if( ms.Capture[level].Len == CAP_UNFINISHED )
					return level;
			}
			return ms.Lua.L_Error( "invalid pattern capture" );
		}

		private static int ClassEnd( MatchState ms, int p )
		{
			switch( ms.P(p++) )
			{
				case L_ESC:
				{
					if( p == ms.PatternEnd )
						ms.Lua.L_Error( "malformed pattern (ends with '%')" );
					return p+1;
				}
				case '[':
				{
					if( ms.P(p) == '^' ) p++;
					do { // look for a `]'
						if( p == ms.PatternEnd )
							ms.Lua.L_Error( "malformed pattern (missing ']')" );
						if( ms.P(p++) == L_ESC && p < ms.PatternEnd )
							p++; // skip escapes (e.g. `%]')
					} while( ms.P(p) != ']' );
					return p+1;
				}
				default: return p;
			}
		}

		private static bool MatchClass( int c, int cl )
		{
			bool res;
			switch( Utl.IsUpper( cl ) ? cl + ('a' - 'A') : cl )
			{
				case 'a': res = Utl.IsAlpha(c); break;
				case 'c': res = Utl.IsCntrl(c); break;
				case 'd': res = Utl.IsDigit(c); break;
				case 'g': res = Utl.IsGraph(c); break;
				case 'l': res = Utl.IsLower(c); break;
				case 'p': res = Utl.IsPunct(c); break;
				case 's': res = Utl.IsSpace(c); break;
				case 'u': res = Utl.IsUpper(c); break;
				case 'w': res = Utl.IsAlnum(c); break;
				case 'x': res = Utl.IsXDigit(c); break;
				case 'z': res = (c == 0); break;  /* deprecated option */
				default: return (cl == c);
			}
			return Utl.IsLower( cl ) ? res : !res;
		}

		private static bool MatchBracketClass( MatchState ms, int c, int p, int ec )
		{
			bool sig = true;
			if( ms.P(p+1) == '^' )
			{
				sig = false;
				p++; // skip the `^'
			}
			while( ++p < ec )
			{
				if( ms.P(p) == L_ESC )
				{
					p++;
					if( MatchClass( c, ms.P(p) ) )
						return sig;
				}
				else if( ms.P(p+1) == '-' && (p+2 < ec) )
				{
					p += 2;
					if( ms.P(p-2) <= c && c <= ms.P(p) )
						return sig;
				}
				else if( ms.P(p) == c ) return sig;
			}
			return !sig;
		}

		private static bool SingleMatch( MatchState ms, int s, int p, int ep )
		{
			if( s >= ms.SrcEnd )
				return false;
			int c = ms.Src[s];
			switch( ms.P(p) )
			{
				case '.': 	return true; // matches any char
				case L_ESC: return MatchClass( c, ms.P(p+1) );
				case '[': 	return MatchBracketClass( ms, c, p, ep-1 );
				default: 	return ms.P(p) == c;
			}
		}

		private static int MatchBalance( MatchState ms, int s, int p )
		{
			if( p >= ms.PatternEnd - 1 )
				ms.Lua.L_Error( "malformed pattern (missing arguments to '%b')" );
			if( ms.S(s) != ms.P(p) ) return -1;
			else
			{
				char b = ms.P(p);
				char e = ms.P(p+1);
				int cont = 1;
				while( ++s < ms.SrcEnd )
				{
					if( ms.Src[s] == e )
					{
						if( --cont == 0 ) return s+1;
					}
					else if( ms.Src[s] == b ) cont++;
				}
			}
			return -1; // string ends out of balance
		}

		private static int MaxExpand( MatchState ms, int s, int p, int ep )
		{
			int i = 0; // counts maximum expand for item
			while( SingleMatch( ms, s+i, p, ep ) )
				i++;
			// keeps trying to match with the maximum repetitions
			while( i >= 0 )
			{
				int res = Match( ms, s+i, ep+1 );
				if( res != -1 ) return res;
				i--; // else didn't match; reduce 1 repetition to try again
			}
			return -1;
		}

		private static int MinExpand( MatchState ms, int s, int p, int ep )
		{
			for(;;)
			{
				int res = Match( ms, s, ep+1 );
				if( res != -1 )
					return res;
				else if( SingleMatch( ms, s, p, ep ) )
					s++; // try with one more repetition
				else return -1;
			}
		}

		private static int StartCapture( MatchState ms, int s, int p, int what )
		{
			int level = ms.Level;
			if( level >= LUA_MAXCAPTURES )
				ms.Lua.L_Error( "too many captures" );
			ms.Capture[level].Init = s;
			ms.Capture[level].Len = what;
			ms.Level = level + 1;
			int res = Match( ms, s, p );
			if( res == -1 ) // match failed?
				ms.Level--; // undo capture
			return res;
		}

		private static int EndCapture( MatchState ms, int s, int p )
		{
			int l = CaptureToClose( ms );
			ms.Capture[l].Len = s - ms.Capture[l].Init; // close capture
			int res = Match( ms, s, p );
			if( res == -1 ) // match failed?
				ms.Capture[l].Len = CAP_UNFINISHED; // undo capture
			return res;
		}

		private static int MatchCapture( MatchState ms, int s, int l )
		{
			l = CheckCapture( ms, l );
			int len = ms.Capture[l].Len;
			if( ms.SrcEnd - s >= len &&
				string.CompareOrdinal( ms.Src, ms.Capture[l].Init, ms.Src, s, len ) == 0 )
				return s + len;
			else
				return -1;
		}

		// the end of the match of the pattern at `p' with the source at `s', or -1
		private static int Match( MatchState ms, int s, int p )
		{
			if( ms.MatchDepth-- == 0 )
				ms.Lua.L_Error( "pattern too complex" );
			init: // using goto's to optimize tail recursion
			if( p != ms.PatternEnd ) // end of pattern?
			{
				switch( ms.P(p) )
				{
					case '(': // start capture
					{
						if( ms.P(p+1) == ')' ) // position capture?
							s = StartCapture( ms, s, p+2, CAP_POSITION );
						else
							s = StartCapture( ms, s, p+1, CAP_UNFINISHED );
						break;
					}
					case ')': // end capture
					{
						s = EndCapture( ms, s, p+1 );
						break;
					}
					case '$':
					{
						if( p+1 != ms.PatternEnd ) // is the `$' the last char in pattern?
							goto dflt; // no; go to default
						s = (s == ms.SrcEnd) ? s : -1; // check end of string
						break;
					}
					case L_ESC: // escaped sequences not in the format class[*+?-]?
					{
						switch( ms.P(p+1) )
						{
							case 'b': // balanced string?
							{
								s = MatchBalance( ms, s, p+2 );
								if( s != -1 )
								{
									p += 4; goto init; // return match(ms, s, p+4);
								} // else fail (s == -1)
								break;
							}
							case 'f': // frontier?
							{
								p += 2;
								if( ms.P(p) != '[' )
									ms.Lua.L_Error( "missing '[' after '%f' in pattern" );
								int ep = ClassEnd( ms, p ); // points to what is next
								char previous = (s == ms.SrcInit) ? '\0' : ms.Src[s-1];
								if( !MatchBracketClass( ms, previous, p, ep-1 ) &&
									MatchBracketClass( ms, ms.S(s), p, ep-1 ) )
								{
									p = ep; goto init; // return match( ms, s, ep );
								}
								s = -1; // match failed
								break;
							}
							case '0': case '1': case '2': case '3':
							case '4': case '5': case '6': case '7':
							case '8': case '9': // capture results (%0-%9)?
							{
								s = MatchCapture( ms, s, ms.P(p+1) );
								if( s != -1 )
								{
									p += 2; goto init; // return match(ms, s, p+2)
								}
								break;
							}
							default: goto dflt;
						}
						break;
					}
					default: dflt: // pattern class plus optional suffix
					{
						int ep = ClassEnd( ms, p ); // points to optional suffix
						// does not match at least once?
						if( !SingleMatch( ms, s, p, ep ) )
						{
							char epc = ms.P(ep);
							if( epc == '*' || epc == '?' || epc == '-' ) // accept empty?
							{
								p = ep+1; goto init; // return match(ms, s, ep + 1);
							}
							else // '+' or no suffix
								s = -1; // fail
						}
						else // matched once
						{
							switch( ms.P(ep) ) // handle optional suffix
							{
								case '?': // optional
								{
									int res = Match( ms, s+1, ep+1 );
									if( res != -1 )
										s = res;
									else
									{
										p = ep+1; goto init; // else return match(ms, s, ep + 1);
									}
									break;
								}
								case '+': // 1 or more repetitions
									s = MaxExpand( ms, s+1, p, ep ); // 1 match already done
									break;
								case '*': // 0 or more repetitions
									s = MaxExpand( ms, s, p, ep );
									break;
								case '-': // 0 or more repetitions (minimum)
									s = MinExpand( ms, s, p, ep );
									break;
								default: // no suffix
									s++; p = ep; goto init; // return match(ms, s + 1, ep);
							}
						}
						break;
					}
				}
			}
			ms.MatchDepth++;
			return s;
		}

		// get_onecapture: information about the i-th capture. If there are no
		// captures and 'i==0', the whole match, which is the range 's'..'e'.
		// If the capture is a string, returns its length and puts its position
		// in 'cap'. If it is an integer (a position), pushes it on the stack
		// and returns CAP_POSITION.
		private static int GetOneCapture( MatchState ms, int i, int s, int e, out int cap )
		{
			if( i >= ms.Level )
			{
				if( i != 0 )
					ms.Lua.L_Error( "invalid capture index %{0}", i + 1 );
				cap = s;
				return e - s;
			}
			else
			{
				int capl = ms.Capture[i].Len;
				cap = ms.Capture[i].Init;
				if( capl == CAP_UNFINISHED )
					ms.Lua.L_Error( "unfinished capture" );
				else if( capl == CAP_POSITION )
					ms.Lua.PushInteger( ms.Capture[i].Init - ms.SrcInit + 1 );
				return capl;
			}
		}

		// push_onecapture: push the i-th capture on the stack
		private static void PushOneCapture( MatchState ms, int i, int s, int e )
		{
			int cap;
			int l = GetOneCapture( ms, i, s, e, out cap );
			if( l != CAP_POSITION )
				ms.Lua.PushString( ms.Src.Substring( cap, l ) );
			// else position was already pushed
		}

		// `s' is -1 when only the captures are wanted, not the whole match
		private static int PushCaptures( MatchState ms, int s, int e )
		{
			int nLevels = (ms.Level == 0 && s >= 0) ? 1 : ms.Level;
			ms.Lua.L_CheckStack(nLevels, "too many captures");
			for( int i=0; i<nLevels; ++i )
				PushOneCapture( ms, i, s, e );
			return nLevels; // number of strings pushed
		}

		// check whether pattern has no special characters
		private static bool NoSpecials( string pattern )
		{
			return pattern.IndexOfAny( SPECIALS ) == -1;
		}

		private static MatchState PrepState( ILuaState lua, string s, string p )
		{
			MatchState ms = new MatchState();
			ms.Lua = lua;
			ms.Src = s;
			ms.SrcInit = 0;
			ms.SrcEnd = s.Length;
			ms.Pattern = p;
			ms.PatternEnd = p.Length;
			return ms;
		}

		private static void RePrepState( MatchState ms )
		{
			ms.MatchDepth = MAXCCALLS;
			ms.Level = 0;
		}

		private static int StrFindAux( ILuaState lua, bool find )
		{
			string s = lua.L_CheckString( 1 );
			string p = lua.L_CheckString( 2 );
			long linit = PosRelatI( lua.L_OptInteger(3, 1), s.Length ) - 1;
			if( linit > s.Length ) // start after string's end?
			{
				lua.PushNil(); // cannot find anything
				return 1;
			}
			int init = (int)linit;
			// explicit request or no special characters?
			if( find && (lua.ToBoolean(4) || NoSpecials(p)) )
			{
				// do a plain search
				int pos = s.IndexOf( p, init, StringComparison.Ordinal );
				if( pos >= 0 )
				{
					lua.PushInteger( pos+1 );
					lua.PushInteger( pos+p.Length );
					return 2;
				}
			}
			else
			{
				int s1 = init;
				int ppos = 0;
				bool anchor = p.Length > 0 && p[0] == '^';
				if( anchor )
					ppos++; // skip anchor character

				MatchState ms = PrepState( lua, s, p );
				do
				{
					RePrepState( ms );
					int res = Match( ms, s1, ppos );
					if( res != -1 )
					{
						if(find)
						{
							lua.PushInteger( s1+1 ); // start
							lua.PushInteger( res );  // end
							return PushCaptures( ms, -1, 0 ) + 2;
						}
						else return PushCaptures( ms, s1, res );
					}
				} while( s1++ < ms.SrcEnd && !anchor );
			}
			lua.PushNil(); // not found
			return 1;
		}

		private static int Str_Find( ILuaState lua )
		{
			return StrFindAux( lua, true );
		}

		private static int Str_Match( ILuaState lua )
		{
			return StrFindAux( lua, false );
		}

		// GMatchState: where the iteration is, where the last match ended (a
		// match may not end where the last one did), and the match state
		private sealed class GMatchState
		{
			public int Src; // current position
			public int P; // pattern
			public int LastMatch; // end of last match
			public MatchState Ms; // match state
		}

		private static int GmatchAux( ILuaState lua )
		{
			var gm = (GMatchState)lua.ToUserData( lua.UpvalueIndex(3) );
			gm.Ms.Lua = lua;
			for( int src = gm.Src; src <= gm.Ms.SrcEnd; src++ )
			{
				RePrepState( gm.Ms );
				int e = Match( gm.Ms, src, gm.P );
				if( e != -1 && e != gm.LastMatch )
				{
					gm.Src = gm.LastMatch = e;
					return PushCaptures( gm.Ms, src, e );
				}
			}
			return 0; // not found
		}

		private static int Str_Gmatch( ILuaState lua )
		{
			string s = lua.L_CheckString(1);
			string p = lua.L_CheckString(2);
			long init = PosRelatI( lua.L_OptInteger(3, 1), s.Length ) - 1;
			lua.SetTop(2); // keep strings on closure to avoid being collected
			var gm = new GMatchState();
			lua.NewUserDataUV( gm, 0 );
			if( init > s.Length ) // start after string's end?
				init = s.Length + 1; // avoid overflows in 's + init'
			gm.Ms = PrepState( lua, s, p );
			gm.Src = (int)init; gm.P = 0; gm.LastMatch = -1;
			lua.PushCSharpClosure( GmatchAux, 3 );
			return 1;
		}

		private static void Add_S( MatchState ms, StringBuilder b, int s, int e )
		{
			ILuaState lua = ms.Lua;
			string news = lua.ToString(3);
			int l = news.Length;
			int i = 0; // start of what is left of 'news'
			int p;
			while( (p = news.IndexOf( L_ESC, i )) != -1 )
			{
				b.Append( news, i, p - i );
				p++; // skip ESC
				char c = p < l ? news[p] : '\0';
				if( c == L_ESC ) // '%%'
					b.Append( c );
				else if( c == '0' ) // '%0'
					b.Append( ms.Src, s, e - s );
				else if( Utl.IsDigit( c ) ) // '%n'
				{
					int cap;
					int resl = GetOneCapture( ms, c - '1', s, e, out cap );
					if( resl == CAP_POSITION )
					{
						b.Append( lua.ToString(-1) ); // add position to accumulated result
						lua.Pop( 1 );
					}
					else
						b.Append( ms.Src, cap, resl );
				}
				else
					lua.L_Error( "invalid use of '%' in replacement string" );
				i = p + 1;
			}
			b.Append( news, i, l - i );
		}

		// add_value: adds the replacement value to the buffer 'b'; true if the
		// original string was changed (function calls and table indexing
		// resulting in nil or false do not change the subject)
		private static bool Add_Value( MatchState ms, StringBuilder b, int s, int e, LuaType tr )
		{
			ILuaState lua = ms.Lua;
			switch( tr )
			{
				case LuaType.LUA_TFUNCTION: // call the function
				{
					lua.PushValue(3); // push the function
					int n = PushCaptures( ms, s, e ); // all captures as arguments
					lua.Call(n, 1); // call it
					break;
				}
				case LuaType.LUA_TTABLE: // index the table
				{
					PushOneCapture( ms, 0, s, e ); // first capture is the index
					lua.GetTable(3);
					break;
				}
				default: // LUA_TNUMBER or LUA_TSTRING
				{
					Add_S( ms, b, s, e ); // add value to the buffer
					return true; // something changed
				}
			}
			if( !lua.ToBoolean(-1) ) // nil or false?
			{
				lua.Pop(1); // remove value
				b.Append( ms.Src, s, e - s ); // keep original text
				return false; // no changes
			}
			else if( !lua.IsString(-1) )
				return lua.L_Error( "invalid replacement value (a {0})", lua.L_TypeName(-1) ) != 0;
			else
			{
				b.Append( lua.ToString(-1) ); // add result to accumulator
				lua.Pop(1);
				return true; // something changed
			}
		}

		private static int Str_Gsub( ILuaState lua )
		{
			string src = lua.L_CheckString(1); // subject
			string p = lua.L_CheckString(2); // pattern
			int lastmatch = -1; // end of last match
			LuaType tr = lua.Type(3); // replacement type
			long max_s = lua.L_OptInteger(4, src.Length + 1); // max replacements
			int ppos = 0;
			bool anchor = p.Length > 0 && p[0] == '^';
			long n = 0; // replacement count
			bool changed = false; // change flag
			ArgExpected( lua, tr == LuaType.LUA_TNUMBER || tr == LuaType.LUA_TSTRING ||
				tr == LuaType.LUA_TFUNCTION || tr == LuaType.LUA_TTABLE, 3,
				"string/function/table" );
			StringBuilder b = new StringBuilder( src.Length );
			if( anchor )
				ppos++; // skip anchor character
			MatchState ms = PrepState( lua, src, p );
			int s = 0;
			while( n < max_s )
			{
				RePrepState( ms ); // (re)prepare state for new match
				int e = Match( ms, s, ppos );
				if( e != -1 && e != lastmatch ) // match?
				{
					n++;
					changed = Add_Value( ms, b, s, e, tr ) | changed;
					s = lastmatch = e;
				}
				else if( s < ms.SrcEnd ) // otherwise, skip one character
					b.Append( src[s++] );
				else break; // end of subject
				if( anchor ) break;
			}
			if( !changed ) // no changes?
				lua.PushValue(1); // return original string
			else // something changed
			{
				b.Append( src, s, ms.SrcEnd - s );
				lua.PushString( b.ToString() ); // create and return new string
			}
			lua.PushInteger( n ); // number of substitutions
			return 2;
		}

		// luaL_argexpected: the argument must be of the type 'tname'
		private static void ArgExpected( ILuaState lua, bool cond, int arg, string tname )
		{
			if( !cond )
				TypeError( lua, arg, tname );
		}

		// luaL_typeerror
		private static int TypeError( ILuaState lua, int arg, string tname )
		{
			string typearg; // name for the type of the actual argument
			if( lua.L_GetMetaField( arg, "__name" ) && lua.Type( -1 ) == LuaType.LUA_TSTRING )
				typearg = lua.ToString( -1 ); // use the given type name
			else if( lua.Type( arg ) == LuaType.LUA_TLIGHTUSERDATA )
				typearg = "light userdata"; // special name for messages
			else
				typearg = lua.L_TypeName( arg ); // standard name
			string msg = string.Format( "{0} expected, got {1}", tname, typearg );
			lua.PushString( msg );
			return lua.L_ArgError( arg, msg );
		}

		// ======================================================
		// STRING FORMAT
		// ======================================================

		// valid flags for a, A, e, E, f, F, g, and G conversions
		private const string L_FMTFLAGSF	= "-+#0 ";
		// valid flags for o, x, and X conversions
		private const string L_FMTFLAGSX	= "-#0";
		// valid flags for d and i conversions
		private const string L_FMTFLAGSI	= "-+0 ";
		// valid flags for u conversions
		private const string L_FMTFLAGSU	= "-0";
		// valid flags for c, p, and s conversions
		private const string L_FMTFLAGSC	= "-";

		// maximum size of each format specification (such as "%-099.99d"):
		// initial '%', flags (up to 5), width (2), period, precision (2),
		// length modifier (8), conversion specifier, and final '\0', plus
		// some extra
		private const int MAX_FORMAT = 32;

		// a conversion specification of C's printf: %[flags][width][.precision]
		private struct FormatSpec
		{
			public bool Left, Plus, Space, Alt, Zero;
			public int Width; // -1: none
			public int Precision; // -1: none
		}

		private static char At( string s, int i )
		{
			return i < s.Length ? s[i] : '\0';
		}

		private static int Get2Digits( string s, int p )
		{
			if( Utl.IsDigit( At( s, p ) ) )
			{
				p++;
				if( Utl.IsDigit( At( s, p ) ) ) p++; // (2 digits at most)
			}
			return p;
		}

		// checkformat: whether a conversion specification is valid. Its first
		// character must be '%' and its last a valid conversion specifier.
		// 'flags' are the accepted flags; 'precision' signals whether to
		// accept a precision.
		private static void CheckFormat( ILuaState lua, string form, string flags, bool precision )
		{
			int spec = 1; // skip '%'
			while( flags.IndexOf( At( form, spec ) ) != -1 ) spec++; // skip flags
			if( At( form, spec ) != '0' ) // a width cannot start with '0'
			{
				spec = Get2Digits( form, spec ); // skip width
				if( At( form, spec ) == '.' && precision )
				{
					spec++;
					spec = Get2Digits( form, spec ); // skip precision
				}
			}
			if( !Utl.IsAlpha( At( form, spec ) ) ) // did not go to the end?
				lua.L_Error( "invalid conversion specification: '{0}'", form );
		}

		// getformat: gets the conversion specification after the '%' at 's' in
		// 'form', which is a C string (it ends before a '\0' specifier).
		// Returns the position of its last character.
		private static int GetFormat( ILuaState lua, string strfrmt, int s, out string form )
		{
			// spans flags, width, and precision ('0' is included as a flag)
			int len = 0;
			while( (L_FMTFLAGSF + "123456789.").IndexOf( At( strfrmt, s + len ) ) != -1 )
				len++;
			len++; // adds following character (should be the specifier)
			// still needs space for '%', '\0', plus a length modifier
			if( len >= MAX_FORMAT - 10 )
				lua.L_Error( "invalid format (too long)" );
			form = "%" + strfrmt.Substring( s, len - 1 );
			if( At( strfrmt, s + len - 1 ) != '\0' )
				form += strfrmt[s + len - 1];
			return s + len - 1;
		}

		// the flags, width and precision of a valid conversion specification
		private static FormatSpec ScanSpec( string form )
		{
			var spec = new FormatSpec();
			spec.Width = -1;
			spec.Precision = -1;
			int p = 1; // skip '%'
			for( char c; (c = form[p]) == '-' || c == '+' || c == ' ' || c == '#' || c == '0'; p++ )
			{
				switch( c )
				{
					case '-': spec.Left = true; break;
					case '+': spec.Plus = true; break;
					case ' ': spec.Space = true; break;
					case '#': spec.Alt = true; break;
					case '0': spec.Zero = true; break;
				}
			}
			int start = p;
			while( Utl.IsDigit( form[p] ) ) p++; // width
			if( p > start )
				spec.Width = int.Parse( form.Substring( start, p - start ), CultureInfo.InvariantCulture );
			if( form[p] == '.' )
			{
				start = ++p;
				while( Utl.IsDigit( form[p] ) ) p++; // precision
				spec.Precision = (p > start)
					? int.Parse( form.Substring( start, p - start ), CultureInfo.InvariantCulture )
					: 0;
			}
			return spec;
		}

		// pads to the width: `prefix' (a sign, "0x") stays before zeros
		private static void AddPadded( StringBuilder sb, FormatSpec spec,
			string prefix, string body, bool zeros )
		{
			int fill = spec.Width - prefix.Length - body.Length;
			if( fill <= 0 )
				sb.Append( prefix ).Append( body );
			else if( spec.Left )
				sb.Append( prefix ).Append( body ).Append( ' ', fill );
			else if( spec.Zero && zeros )
				sb.Append( prefix ).Append( '0', fill ).Append( body );
			else
				sb.Append( ' ', fill ).Append( prefix ).Append( body );
		}

		private static string Sign( FormatSpec spec, bool negative )
		{
			return negative ? "-" : spec.Plus ? "+" : spec.Space ? " " : "";
		}

		// the digits of an integer, with at least `precision' of them
		private static string IntDigits( string digits, FormatSpec spec, bool isZero )
		{
			if( spec.Precision == 0 && isZero )
				return "";
			if( digits.Length < spec.Precision )
				return new string( '0', spec.Precision - digits.Length ) + digits;
			return digits;
		}

		private static void FormatInteger( StringBuilder sb, FormatSpec spec, long ni )
		{
			bool negative = ni < 0;
			ulong mag = negative ? (ulong)(-(ni + 1)) + 1 : (ulong)ni;
			string digits = IntDigits( mag.ToString( CultureInfo.InvariantCulture ), spec, mag == 0 );
			AddPadded( sb, spec, Sign( spec, negative ), digits, spec.Precision < 0 );
		}

		private static void FormatUnsigned( StringBuilder sb, FormatSpec spec, long n, char conv )
		{
			// the bits of the integer, as C prints a long long with %llx
			ulong ni = unchecked( (ulong)n );
			string digits;
			switch( conv )
			{
				case 'o': digits = Convert.ToString( unchecked((long)ni), 8 ); break;
				case 'x': digits = ni.ToString( "x", CultureInfo.InvariantCulture ); break;
				case 'X': digits = ni.ToString( "X", CultureInfo.InvariantCulture ); break;
				default: digits = ni.ToString( CultureInfo.InvariantCulture ); break;
			}
			digits = IntDigits( digits, spec, ni == 0 );
			if( spec.Alt && conv == 'o' && (digits.Length == 0 || digits[0] != '0') )
				digits = "0" + digits;
			string prefix = (spec.Alt && ni != 0 && (conv == 'x' || conv == 'X'))
				? (conv == 'x' ? "0x" : "0X")
				: "";
			AddPadded( sb, spec, prefix, digits, spec.Precision < 0 );
		}

		// %e: a mantissa, and an exponent of 2 digits at least
		private static string FormatExp( double a, int precision, bool alt )
		{
			string s = a.ToString( "E" + precision, CultureInfo.InvariantCulture );
			int mark = s.IndexOf( 'E' );
			int exponent = int.Parse( s.Substring( mark + 1 ), CultureInfo.InvariantCulture );
			string mantissa = s.Substring( 0, mark );
			if( alt && precision == 0 )
				mantissa += ".";
			return mantissa + LuaNumber.FormatExponent( exponent );
		}

		// %a: hexadecimal digits of the mantissa, and a binary exponent
		private static string FormatHexFloat( double a, int precision, bool alt )
		{
			long bits = BitConverter.DoubleToInt64Bits( a );
			int exponent = (int)((bits >> 52) & 0x7FF);
			ulong mantissa = (ulong)bits & 0xFFFFFFFFFFFFFUL;
			int lead = 1;
			if( exponent == 0 ) // zero or subnormal
			{
				lead = 0;
				exponent = (mantissa == 0) ? 0 : -1022;
			}
			else exponent -= 1023;

			string digits;
			if( precision < 0 ) // as many digits as needed
				digits = mantissa.ToString( "x13", CultureInfo.InvariantCulture ).TrimEnd( '0' );
			else if( precision < 13 ) // rounded, to even
			{
				int shift = (13 - precision) * 4;
				ulong full = ((ulong)lead << 52) | mantissa;
				ulong q = full >> shift;
				ulong rem = full & ((1UL << shift) - 1);
				ulong half = 1UL << (shift - 1);
				if( rem > half || (rem == half && (q & 1) != 0) )
					q++;
				lead = (int)(q >> (precision * 4));
				ulong frac = q & ((1UL << (precision * 4)) - 1);
				digits = precision == 0 ? "" : frac.ToString( "x" + precision, CultureInfo.InvariantCulture );
			}
			else
				digits = mantissa.ToString( "x13", CultureInfo.InvariantCulture ) + new string( '0', precision - 13 );

			string s = lead.ToString( CultureInfo.InvariantCulture );
			if( digits.Length > 0 || alt )
				s += "." + digits;
			return s + "p" + (exponent < 0 ? "-" : "+")
				+ Math.Abs( exponent ).ToString( CultureInfo.InvariantCulture );
		}

		private static void FormatFloat( StringBuilder sb, FormatSpec spec, double v, char conv )
		{
			bool negative = double.IsNegative( v );
			double a = Math.Abs( v );
			bool finite = !double.IsNaN( v ) && !double.IsInfinity( v );
			bool upper = conv == 'E' || conv == 'G' || conv == 'A';
			string prefix = Sign( spec, negative );
			string body;
			if( !finite )
				body = double.IsNaN( v ) ? "nan" : "inf";
			else switch( conv )
			{
				case 'f':
					body = a.ToString( "F" + (spec.Precision < 0 ? 6 : spec.Precision),
						CultureInfo.InvariantCulture );
					if( spec.Alt && spec.Precision == 0 )
						body += ".";
					break;
				case 'e': case 'E':
					body = FormatExp( a, spec.Precision < 0 ? 6 : spec.Precision, spec.Alt );
					break;
				case 'a': case 'A':
					body = FormatHexFloat( a, spec.Precision, spec.Alt );
					prefix += "0x";
					break;
				default: // 'g', 'G'
					body = LuaNumber.Format( a, spec.Precision < 0 ? 6 : spec.Precision, spec.Alt );
					break;
			}
			if( upper )
			{
				prefix = prefix.ToUpperInvariant();
				body = body.ToUpperInvariant();
			}
			AddPadded( sb, spec, prefix, body, finite );
		}

		// quotefloat: a float as Lua scans it back. Hexadecimal for "common"
		// numbers (to preserve precision); inf, -inf, and NaN are handled
		// separately (NaN cannot be expressed as a numeral, so it is written
		// '(0/0)')
		private static string QuoteFloat( double n )
		{
			if( n == double.PositiveInfinity ) // inf?
				return "1e9999";
			else if( n == double.NegativeInfinity ) // -inf?
				return "-1e9999";
			else if( double.IsNaN( n ) ) // NaN?
				return "(0/0)";
			else // format number as hexadecimal ('%a')
				return (double.IsNegative( n ) ? "-0x" : "0x") + FormatHexFloat( Math.Abs( n ), -1, false );
		}

		// addliteral: a value as Lua code reads it back
		private static void AddLiteral( ILuaState lua, StringBuilder sb, int arg )
		{
			switch( lua.Type( arg ) )
			{
				case LuaType.LUA_TSTRING:
					AddQuoted( lua.ToString( arg ), sb );
					break;
				case LuaType.LUA_TNUMBER:
					if( !lua.IsInteger( arg ) ) // float?
						sb.Append( QuoteFloat( lua.ToNumber( arg ) ) );
					else // integers
					{
						long n = lua.ToInteger( arg );
						sb.Append( n == long.MinValue // corner case?
							? "0x8000000000000000" // use hex
							: n.ToString( CultureInfo.InvariantCulture ) );
					}
					break;
				case LuaType.LUA_TNIL:
				case LuaType.LUA_TBOOLEAN:
					sb.Append( lua.L_ToString( arg ) );
					lua.Pop( 1 );
					break;
				default:
					lua.L_ArgError( arg, "value has no literal form" );
					break;
			}
		}

		private static void AddQuoted( string s, StringBuilder sb )
		{
			sb.Append('"');
			for( var i=0; i<s.Length; ++i )
			{
				var c = s[i];
				if( c == '"' || c == '\\' || c == '\n' )
				{
					sb.Append('\\').Append(c);
				}
				else if( Utl.IsCntrl(c) )
				{
					if( i+1 >= s.Length || !Utl.IsDigit(s[i+1]) )
						sb.Append('\\').Append( ((int)c).ToString( CultureInfo.InvariantCulture ) );
					else
						sb.Append('\\').Append( ((int)c).ToString( "000", CultureInfo.InvariantCulture ) );
				}
				else
				{
					sb.Append(c);
				}
			}
			sb.Append('"');
		}

		// the length of a short string, which Lua internalizes
		private const int LUAI_MAXSHORTLEN = 40;

		// lua_topointer, as the address of '%p': none (null) for the values
		// that are not objects. Lua internalizes short strings, so equal ones
		// have one address: here it comes from their contents. The other
		// objects have the address 'tostring' shows.
		private static string ToPointer( ILuaState lua, int arg )
		{
			int address;
			switch( lua.Type( arg ) )
			{
				case LuaType.LUA_TSTRING:
				{
					string s = lua.ToString( arg );
					if( s.Length <= LUAI_MAXSHORTLEN ) // short string?
					{
						uint h = 2166136261; // FNV-1a hash of its bytes
						for( int i = 0; i < s.Length; i++ )
							h = unchecked( (h ^ (s[i] & 0xFFu)) * 16777619 );
						address = unchecked( (int)h );
					}
					else
						address = RuntimeHelpers.GetHashCode( s );
					break;
				}
				case LuaType.LUA_TLIGHTUSERDATA:
				{
					object o = lua.ToUserData( arg );
					if( o == null ) // a NULL pointer?
						return null;
					address = RuntimeHelpers.GetHashCode( o );
					break;
				}
				case LuaType.LUA_TTABLE: case LuaType.LUA_TFUNCTION:
				case LuaType.LUA_TUSERDATA: case LuaType.LUA_TTHREAD:
				{
					// the same light function shows one address (see TValue.SameObject)
					object o = lua.ToObject( arg );
					var cscl = o as LuaCsClosureValue;
					if( cscl != null && cscl.IsLight )
						o = cscl.F;
					address = RuntimeHelpers.GetHashCode( o );
					break;
				}
				default: // nil, booleans and numbers
					return null;
			}
			return string.Format( "0x{0:x8}", address );
		}

		private static int Str_Format( ILuaState lua )
		{
			int top = lua.GetTop();
			StringBuilder sb = new StringBuilder();
			int arg = 1;
			string strfrmt = lua.L_CheckString( arg );
			int s = 0;
			int e = strfrmt.Length;
			string flags;
			while( s < e )
			{
				if( strfrmt[s] != L_ESC )
					sb.Append( strfrmt[s++] );
				else if( At( strfrmt, ++s ) == L_ESC )
					sb.Append( strfrmt[s++] ); // %%
				else // format item
				{
					string form; // to store the format ('%...')
					if( ++arg > top )
						return lua.L_ArgError( arg, "no value" );
					s = GetFormat( lua, strfrmt, s, out form );
					char conv = At( strfrmt, s++ );
					switch( conv )
					{
						case 'c':
						{
							CheckFormat( lua, form, L_FMTFLAGSC, false );
							// the byte C takes of the int
							long c = lua.L_CheckInteger( arg );
							AddPadded( sb, ScanSpec( form ), "", ((char)(c & 0xFF)).ToString(), false );
							break;
						}
						case 'd': case 'i':
							flags = L_FMTFLAGSI;
							goto intcase;
						case 'u':
							flags = L_FMTFLAGSU;
							goto intcase;
						case 'o': case 'x': case 'X':
							flags = L_FMTFLAGSX;
						intcase:
						{
							long n = lua.L_CheckInteger( arg );
							CheckFormat( lua, form, flags, true );
							if( conv == 'd' || conv == 'i' )
								FormatInteger( sb, ScanSpec( form ), n );
							else
								FormatUnsigned( sb, ScanSpec( form ), n, conv );
							break;
						}
						case 'a': case 'A':
						{
							CheckFormat( lua, form, L_FMTFLAGSF, true );
							FormatFloat( sb, ScanSpec( form ), lua.L_CheckNumber( arg ), conv );
							break;
						}
						case 'f':
						case 'e': case 'E': case 'g': case 'G':
						{
							double n = lua.L_CheckNumber( arg );
							CheckFormat( lua, form, L_FMTFLAGSF, true );
							FormatFloat( sb, ScanSpec( form ), n, conv );
							break;
						}
						case 'p':
						{
							string p = ToPointer( lua, arg );
							CheckFormat( lua, form, L_FMTFLAGSC, false );
							if( p == null ) // avoid calling 'printf' with argument NULL
								p = "(null)"; // result, formatted as a string
							AddPadded( sb, ScanSpec( form ), "", p, false );
							break;
						}
						case 'q':
						{
							if( form.Length > 2 ) // modifiers?
								return lua.L_Error( "specifier '%q' cannot have modifiers" );
							AddLiteral( lua, sb, arg );
							break;
						}
						case 's':
						{
							string str = lua.L_ToString( arg );
							if( form.Length == 2 ) // no modifiers?
								sb.Append( str ); // keep entire string
							else
							{
								lua.L_ArgCheck( str.IndexOf( '\0' ) < 0, arg, "string contains zeros" );
								CheckFormat( lua, form, L_FMTFLAGSC, true );
								if( form.IndexOf( '.' ) < 0 && str.Length >= 100 )
								{
									// no precision and string is too long to be formatted
									sb.Append( str ); // keep entire string
								}
								else // format the string
								{
									FormatSpec spec = ScanSpec( form );
									if( spec.Precision >= 0 && str.Length > spec.Precision )
										str = str.Substring( 0, spec.Precision );
									AddPadded( sb, spec, "", str, false );
								}
							}
							lua.Pop( 1 ); // remove result from 'luaL_tolstring'
							break;
						}
						default: // also treat cases 'pnLlh'
						{
							return lua.L_Error( "invalid conversion '{0}' to 'format'", form );
						}
					}
				}
			}
			lua.PushString( sb.ToString() );
			return 1;
		}


		// ======================================================
		// PACK/UNPACK
		// ======================================================

		private const char LUAL_PACKPADBYTE = '\0';
		private const int MAXINTSIZE = 16; // maximum size for the binary representation of an integer
		private const int NB = 8; // number of bits in a character
		private const int MC = (1 << NB) - 1; // mask for one character (NB 1's)
		private const int SZINT = 8; // size of a lua_Integer
		private const int MAXALIGN = 8; // native alignment requirements

		// the MAXSIZE of C, for the sizes formats give: a string here holds
		// less, as .NET's do
		private const int PACK_MAXSIZE = int.MaxValue;

		// the sizes of C's types on the 64-bit machines the reference runs on
		private const int SIZEOF_SHORT = 2;
		private const int SIZEOF_INT = 4;
		private const int SIZEOF_LONG = 8;
		private const int SIZEOF_SIZET = 8;

		// information to pack/unpack stuff
		private sealed class PackHeader
		{
			public ILuaState L;
			public bool IsLittle = true; // little endian, as the machine
			public int MaxAlign = 1;
			public string Fmt;
			public int Pos;

			public char Current { get { return Pos < Fmt.Length ? Fmt[Pos] : '\0'; } }
		}

		// options for pack/unpack
		private enum KOption
		{
			Kint,		// signed integers
			Kuint,		// unsigned integers
			Kfloat,		// single-precision floating-point numbers
			Knumber,	// Lua "native" floating-point numbers
			Kdouble,	// double-precision floating-point numbers
			Kchar,		// fixed-length strings
			Kstring,	// strings with prefixed length
			Kzstr,		// zero-terminated strings
			Kpadding,	// padding
			Kpaddalign,	// padding for alignment
			Knop		// no-op (configuration or spaces)
		}

		// getnum: an integer numeral from the format, or 'df' if there is none
		private static int GetNum( PackHeader h, int df )
		{
			if( !Utl.IsDigit( h.Current ) ) // no number?
				return df; // return default value
			int a = 0;
			do {
				a = a*10 + (h.Fmt[h.Pos++] - '0');
			} while( Utl.IsDigit( h.Current ) && a <= (PACK_MAXSIZE - 9)/10 );
			return a;
		}

		// getnumlimit: a numeral, which must be a size of integers
		private static int GetNumLimit( PackHeader h, int df )
		{
			int sz = GetNum( h, df );
			if( sz > MAXINTSIZE || sz <= 0 )
				return h.L.L_Error( "integral size ({0}) out of limits [1,{1}]", sz, MAXINTSIZE );
			return sz;
		}

		// getoption: reads and classifies the next option, and its size
		private static KOption GetOption( PackHeader h, out int size )
		{
			char opt = h.Fmt[h.Pos++];
			size = 0; // default
			switch( opt )
			{
				case 'b': size = 1; return KOption.Kint;
				case 'B': size = 1; return KOption.Kuint;
				case 'h': size = SIZEOF_SHORT; return KOption.Kint;
				case 'H': size = SIZEOF_SHORT; return KOption.Kuint;
				case 'l': size = SIZEOF_LONG; return KOption.Kint;
				case 'L': size = SIZEOF_LONG; return KOption.Kuint;
				case 'j': size = SZINT; return KOption.Kint;
				case 'J': size = SZINT; return KOption.Kuint;
				case 'T': size = SIZEOF_SIZET; return KOption.Kuint;
				case 'f': size = 4; return KOption.Kfloat;
				case 'n': size = 8; return KOption.Knumber;
				case 'd': size = 8; return KOption.Kdouble;
				case 'i': size = GetNumLimit( h, SIZEOF_INT ); return KOption.Kint;
				case 'I': size = GetNumLimit( h, SIZEOF_INT ); return KOption.Kuint;
				case 's': size = GetNumLimit( h, SIZEOF_SIZET ); return KOption.Kstring;
				case 'c':
					size = GetNum( h, -1 );
					if( size == -1 )
						h.L.L_Error( "missing size for format option 'c'" );
					return KOption.Kchar;
				case 'z': return KOption.Kzstr;
				case 'x': size = 1; return KOption.Kpadding;
				case 'X': return KOption.Kpaddalign;
				case ' ': break;
				case '<': h.IsLittle = true; break;
				case '>': h.IsLittle = false; break;
				case '=': h.IsLittle = true; break; // native endianness
				case '!': h.MaxAlign = GetNumLimit( h, MAXALIGN ); break;
				default: h.L.L_Error( "invalid format option '{0}'", opt ); break;
			}
			return KOption.Knop;
		}

		// getdetails: the next option, its size and the padding it needs to
		// be aligned
		private static KOption GetDetails( PackHeader h, long totalsize, out int size, out int ntoalign )
		{
			KOption opt = GetOption( h, out size );
			int align = size; // usually, alignment follows size
			if( opt == KOption.Kpaddalign ) // 'X' gets alignment from following option
			{
				if( h.Current == '\0' || GetOption( h, out align ) == KOption.Kchar || align == 0 )
					h.L.L_ArgError( 1, "invalid next option for option 'X'" );
			}
			if( align <= 1 || opt == KOption.Kchar ) // need no alignment?
				ntoalign = 0;
			else
			{
				if( align > h.MaxAlign ) // enforce maximum alignment
					align = h.MaxAlign;
				if( (align & (align - 1)) != 0 ) // is 'align' not a power of 2?
					h.L.L_ArgError( 1, "format asks for alignment not power of 2" );
				ntoalign = (align - (int)(totalsize & (align - 1))) & (align - 1);
			}
			return opt;
		}

		// packint: an integer in 'size' bytes, sign-extended past a Lua integer
		private static void PackInt( StringBuilder b, ulong n, bool islittle, int size, bool neg )
		{
			var buff = new char[size];
			buff[islittle ? 0 : size - 1] = (char)(n & MC); // first byte
			for( int i = 1; i < size; i++ )
			{
				n >>= NB;
				buff[islittle ? i : size - 1 - i] = (char)(n & MC);
			}
			if( neg && size > SZINT ) // negative number need sign extension?
			{
				for( int i = SZINT; i < size; i++ ) // correct extra bytes
					buff[islittle ? i : size - 1 - i] = (char)MC;
			}
			b.Append( buff ); // add result to buffer
		}

		// copywithendian: adds the bytes of 'src' to 'b', correcting endianness
		// if 'islittle' is different from native endianness
		private static void CopyWithEndian( StringBuilder b, byte[] src, bool islittle )
		{
			if( islittle == BitConverter.IsLittleEndian )
				foreach( byte x in src )
					b.Append( (char)x );
			else
				for( int i = src.Length - 1; i >= 0; i-- )
					b.Append( (char)src[i] );
		}

		// copywithendian, to read: the 'size' bytes at 'pos' of 'data'
		private static byte[] CopyWithEndian( string data, int pos, int size, bool islittle )
		{
			var dest = new byte[size];
			for( int i = 0; i < size; i++ )
				dest[islittle == BitConverter.IsLittleEndian ? i : size - 1 - i] = (byte)data[pos + i];
			return dest;
		}

		private static int Str_Pack( ILuaState lua )
		{
			var b = new StringBuilder();
			var h = new PackHeader { L = lua, Fmt = lua.L_CheckString( 1 ) }; // format string
			int arg = 1; // current argument to pack
			long totalsize = 0; // accumulate total size of result
			while( h.Current != '\0' )
			{
				int size, ntoalign;
				KOption opt = GetDetails( h, totalsize, out size, out ntoalign );
				totalsize += ntoalign + size;
				while( ntoalign-- > 0 )
					b.Append( LUAL_PACKPADBYTE ); // fill alignment
				arg++;
				switch( opt )
				{
					case KOption.Kint: // signed integers
					{
						long n = lua.L_CheckInteger( arg );
						if( size < SZINT ) // need overflow check?
						{
							long lim = 1L << ((size * NB) - 1);
							lua.L_ArgCheck( -lim <= n && n < lim, arg, "integer overflow" );
						}
						PackInt( b, unchecked( (ulong)n ), h.IsLittle, size, n < 0 );
						break;
					}
					case KOption.Kuint: // unsigned integers
					{
						long n = lua.L_CheckInteger( arg );
						if( size < SZINT ) // need overflow check?
							lua.L_ArgCheck( unchecked( (ulong)n ) < (1UL << (size * NB)),
								arg, "unsigned overflow" );
						PackInt( b, unchecked( (ulong)n ), h.IsLittle, size, false );
						break;
					}
					case KOption.Kfloat: // C float
					{
						float f = (float)lua.L_CheckNumber( arg ); // get argument
						// move 'f' to final result, correcting endianness if needed
						CopyWithEndian( b, BitConverter.GetBytes( f ), h.IsLittle );
						break;
					}
					case KOption.Knumber: // Lua float
					{
						double f = lua.L_CheckNumber( arg ); // get argument
						// move 'f' to final result, correcting endianness if needed
						CopyWithEndian( b, BitConverter.GetBytes( f ), h.IsLittle );
						break;
					}
					case KOption.Kdouble: // C double
					{
						double f = (double)lua.L_CheckNumber( arg ); // get argument
						// move 'f' to final result, correcting endianness if needed
						CopyWithEndian( b, BitConverter.GetBytes( f ), h.IsLittle );
						break;
					}
					case KOption.Kchar: // fixed-size string
					{
						string str = lua.L_CheckString( arg );
						int len = str.Length;
						lua.L_ArgCheck( len <= size, arg, "string longer than given size" );
						b.Append( str ); // add string
						while( len++ < size ) // pad extra space
							b.Append( LUAL_PACKPADBYTE );
						break;
					}
					case KOption.Kstring: // strings with length count
					{
						string str = lua.L_CheckString( arg );
						int len = str.Length;
						lua.L_ArgCheck( size >= SIZEOF_SIZET || (ulong)len < (1UL << (size * NB)),
							arg, "string length does not fit in given size" );
						PackInt( b, (ulong)len, h.IsLittle, size, false ); // pack length
						b.Append( str );
						totalsize += len;
						break;
					}
					case KOption.Kzstr: // zero-terminated string
					{
						string str = lua.L_CheckString( arg );
						lua.L_ArgCheck( str.IndexOf( '\0' ) < 0, arg, "string contains zeros" );
						b.Append( str );
						b.Append( '\0' ); // add zero at the end
						totalsize += str.Length + 1;
						break;
					}
					case KOption.Kpadding:
						b.Append( LUAL_PACKPADBYTE );
						arg--; // undo increment
						break;
					case KOption.Kpaddalign: case KOption.Knop:
						arg--; // undo increment
						break;
				}
			}
			lua.PushString( b.ToString() );
			return 1;
		}

		private static int Str_PackSize( ILuaState lua )
		{
			var h = new PackHeader { L = lua, Fmt = lua.L_CheckString( 1 ) }; // format string
			long totalsize = 0; // accumulate total size of result
			while( h.Current != '\0' )
			{
				int size, ntoalign;
				KOption opt = GetDetails( h, totalsize, out size, out ntoalign );
				lua.L_ArgCheck( opt != KOption.Kstring && opt != KOption.Kzstr, 1,
					"variable-length format" );
				size += ntoalign; // total space used by option
				lua.L_ArgCheck( totalsize <= PACK_MAXSIZE - size, 1, "format result too large" );
				totalsize += size;
			}
			lua.PushInteger( totalsize );
			return 1;
		}

		// unpackint: an integer of 'size' bytes, which must fit in a Lua integer
		private static long UnpackInt( ILuaState lua, string str, int pos, bool islittle, int size, bool issigned )
		{
			ulong res = 0;
			int limit = (size <= SZINT) ? size : SZINT;
			for( int i = limit - 1; i >= 0; i-- )
			{
				res <<= NB;
				res |= (uint)(str[pos + (islittle ? i : size - 1 - i)] & MC);
			}
			if( size < SZINT ) // real size smaller than lua_Integer?
			{
				if( issigned ) // needs sign extension?
				{
					ulong mask = 1UL << (size*NB - 1);
					res = unchecked( (res ^ mask) - mask ); // do sign extension
				}
			}
			else if( size > SZINT ) // must check unread bytes
			{
				int mask = (!issigned || unchecked( (long)res ) >= 0) ? 0 : MC;
				for( int i = limit; i < size; i++ )
				{
					if( (str[pos + (islittle ? i : size - 1 - i)] & MC) != mask )
						lua.L_Error( "{0}-byte integer does not fit into Lua Integer", size );
				}
			}
			return unchecked( (long)res );
		}

		private static int Str_Unpack( ILuaState lua )
		{
			var h = new PackHeader { L = lua, Fmt = lua.L_CheckString( 1 ) };
			string data = lua.L_CheckString( 2 );
			int ld = data.Length;
			long lpos = PosRelatI( lua.L_OptInteger( 3, 1 ), ld ) - 1;
			int n = 0; // number of results
			lua.L_ArgCheck( lpos <= ld, 3, "initial position out of string" );
			int pos = (int)lpos;
			while( h.Current != '\0' )
			{
				int size, ntoalign;
				KOption opt = GetDetails( h, pos, out size, out ntoalign );
				lua.L_ArgCheck( (long)ntoalign + size <= ld - pos, 2,
					"data string too short" );
				pos += ntoalign; // skip alignment
				// stack space for item + next position
				lua.L_CheckStack( 2, "too many results" );
				n++;
				switch( opt )
				{
					case KOption.Kint:
					case KOption.Kuint:
						lua.PushInteger( UnpackInt( lua, data, pos, h.IsLittle, size, opt == KOption.Kint ) );
						break;
					case KOption.Kfloat:
					{
						float f = BitConverter.ToSingle( CopyWithEndian( data, pos, 4, h.IsLittle ), 0 );
						lua.PushNumber( (double)f );
						break;
					}
					case KOption.Knumber:
					{
						double f = BitConverter.ToDouble( CopyWithEndian( data, pos, 8, h.IsLittle ), 0 );
						lua.PushNumber( f );
						break;
					}
					case KOption.Kdouble:
					{
						double f = BitConverter.ToDouble( CopyWithEndian( data, pos, 8, h.IsLittle ), 0 );
						lua.PushNumber( (double)f );
						break;
					}
					case KOption.Kchar:
						lua.PushString( data.Substring( pos, size ) );
						break;
					case KOption.Kstring:
					{
						ulong len = unchecked( (ulong)UnpackInt( lua, data, pos, h.IsLittle, size, false ) );
						lua.L_ArgCheck( len <= (ulong)(ld - pos - size), 2, "data string too short" );
						lua.PushString( data.Substring( pos + size, (int)len ) );
						pos += (int)len; // skip string
						break;
					}
					case KOption.Kzstr:
					{
						int end = data.IndexOf( '\0', pos );
						int len = (end < 0 ? ld : end) - pos; // strlen
						lua.L_ArgCheck( pos + len < ld, 2,
							"unfinished string for format 'z'" );
						lua.PushString( data.Substring( pos, len ) );
						pos += len + 1; // skip string plus final '\0'
						break;
					}
					case KOption.Kpaddalign: case KOption.Kpadding: case KOption.Knop:
						n--; // undo increment
						break;
				}
				pos += size;
			}
			lua.PushInteger( (long)pos + 1 ); // next position
			return n + 1;
		}

	}

}
