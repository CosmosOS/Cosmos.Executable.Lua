// Part of UniLua (see LICENSE.txt in this directory), adapted for Cosmos.
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

	// lstrlib.c of Lua 5.2. Characters stand for bytes: a string holds the
	// UTF-16 code units of its text, and character classes are those of the
	// "C" locale (ASCII).
	internal static class LuaStrLib
	{
		public const string LIB_NAME = "string";

		private const int CAP_UNFINISHED 	= -1;
		private const int CAP_POSITION		= -2;
		private const int LUA_MAXCAPTURES 	= 32;
		private const int MAXCCALLS			= 200; // maximum recursion depth for 'match'
		private const char L_ESC 			= '%';
		private const string FLAGS			= "-+ #0";
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
			lua.CreateTable(0, 1); // table to be metatable for strings
			lua.PushString( "" ); // dummy string
			lua.PushValue( -2 ); // copy table
			lua.SetMetaTable( -2 ); // set table as metatable for strings
			lua.Pop( 1 );
			lua.PushValue( -2 ); // get string library
			lua.SetField( -2, "__index" ); // metatable.__index = string
			lua.Pop( 1 ); // pop metatable
		}

		// translate a relative string position: negative means back from end
		private static int PosRelative( int pos, int len )
		{
			if( pos >= 0 ) return pos;
			else if( -(long)pos > len ) return 0;
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
			int start = PosRelative( lua.L_CheckInteger(2), s.Length );
			int end = PosRelative( lua.L_OptInt(3, -1), s.Length );
			if( start < 1 ) start = 1;
			if( end > s.Length ) end = s.Length;
			if( start <= end )
				lua.PushString( s.Substring(start-1, end-start+1) );
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
			int n = lua.L_CheckInteger(2);
			string sep = lua.L_OptString(3, "");
			if( n <= 0 )
				lua.PushString( "" );
			else if( (long)s.Length * n + (long)sep.Length * (n - 1) > MAXSIZE )
				return lua.L_Error( "resulting string too large" );
			else
			{
				StringBuilder sb = new StringBuilder( s.Length * n + sep.Length * (n - 1) );
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
			int posi = PosRelative( lua.L_OptInt(2, 1), s.Length );
			int pose = PosRelative( lua.L_OptInt(3, posi), s.Length );
			if( posi < 1 ) posi = 1;
			if( pose > s.Length ) pose = s.Length;
			if( posi > pose ) return 0; // empty interval; return no values
			int n = pose - posi + 1;
			lua.L_CheckStack(n, "string slice too long");
			for( int i=0; i<n; ++i )
				lua.PushInteger( s[posi+i-1] );
			return n;
		}

		// any UTF-16 code unit, so that string.char(s:byte(1, -1)) == s
		private static int Str_Char( ILuaState lua )
		{
			int n = lua.GetTop();
			StringBuilder sb = new StringBuilder(n);
			for( int i=1; i<=n; ++i )
			{
				int c = lua.L_CheckInteger(i);
				lua.L_ArgCheck( (char)c == c, i, "value out of range" );
				sb.Append( (char)c );
			}
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int Str_Dump( ILuaState lua )
		{
			lua.L_CheckType( 1, LuaType.LUA_TFUNCTION );
			lua.SetTop( 1 );
			var bsb = new ByteStringBuilder();
			LuaWriter writeFunc =
				delegate(byte[] bytes, int start, int length)
			{
				bsb.Append(bytes, start, length);
				return DumpStatus.OK;
			};
			if( lua.Dump( writeFunc ) != DumpStatus.OK )
				return lua.L_Error( "unable to dump given function" );
			lua.PushString( bsb.ToString() );
			return 1;
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

		private static void PushOneCapture( MatchState ms, int i, int s, int e )
		{
			var lua = ms.Lua;
			if( i >= ms.Level )
			{
				if( i == 0 ) // ms.Level == 0, too
					lua.PushString( ms.Src.Substring( s, e-s ) ); // add whole match
				else
					lua.L_Error( "invalid capture index" );
			}
			else
			{
				int l = ms.Capture[i].Len;
				if( l == CAP_UNFINISHED )
					lua.L_Error( "unfinished capture" );
				if( l == CAP_POSITION )
					lua.PushInteger( ms.Capture[i].Init - ms.SrcInit + 1 );
				else
					lua.PushString( ms.Src.Substring( ms.Capture[i].Init, l ) );
			}
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

		private static MatchState NewMatchState( ILuaState lua, string s, string p )
		{
			MatchState ms = new MatchState();
			ms.Lua = lua;
			ms.MatchDepth = MAXCCALLS;
			ms.Src = s;
			ms.SrcInit = 0;
			ms.SrcEnd = s.Length;
			ms.Pattern = p;
			ms.PatternEnd = p.Length;
			return ms;
		}

		private static int StrFindAux( ILuaState lua, bool find )
		{
			string s = lua.L_CheckString( 1 );
			string p = lua.L_CheckString( 2 );
			int init = PosRelative( lua.L_OptInt(3, 1), s.Length );
			if( init < 1 ) init = 1;
			else if( init > s.Length + 1 ) // start after string's end?
			{
				lua.PushNil(); // cannot find anything
				return 1;
			}
			// explicit request or no special characters?
			if( find && (lua.ToBoolean(4) || NoSpecials(p)) )
			{
				// do a plain search
				int pos = s.IndexOf( p, init-1, StringComparison.Ordinal );
				if( pos >= 0 )
				{
					lua.PushInteger( pos+1 );
					lua.PushInteger( pos+p.Length );
					return 2;
				}
			}
			else
			{
				int s1 = init-1;
				int ppos = 0;
				bool anchor = p.Length > 0 && p[0] == '^';
				if( anchor )
					ppos++; // skip anchor character

				MatchState ms = NewMatchState( lua, s, p );
				do
				{
					ms.Level = 0;
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

		private static int GmatchAux( ILuaState lua )
		{
			string src = lua.ToString( lua.UpvalueIndex(1) );
			string pattern = lua.ToString( lua.UpvalueIndex(2) );
			MatchState ms = NewMatchState( lua, src, pattern );
			for( int s = lua.ToInteger( lua.UpvalueIndex(3) )
			   ; s <= ms.SrcEnd
			   ; s++ )
			{
				ms.Level = 0;
				int e = Match( ms, s, 0 );
				if( e != -1 )
				{
					int newStart = e;
					if( e == s ) newStart++; // empty match? go at least one position
					lua.PushInteger( newStart );
					lua.Replace( lua.UpvalueIndex(3) );
					return PushCaptures( ms, s, e );
				}
			}
			return 0; // not found
		}

		private static int Str_Gmatch( ILuaState lua )
		{
			lua.L_CheckString(1);
			lua.L_CheckString(2);
			lua.SetTop(2);
			lua.PushInteger(0);
			lua.PushCSharpClosure( GmatchAux, 3 );
			return 1;
		}

		private static void Add_S( MatchState ms, StringBuilder b, int s, int e )
		{
			string news = ms.Lua.ToString(3);
			for( int i = 0; i < news.Length; i++ )
			{
				if( news[i] != L_ESC )
					b.Append( news[i] );
				else
				{
					i++; // skip ESC
					char c = i < news.Length ? news[i] : '\0';
					if( !Utl.IsDigit( c ) )
					{
						if( c != L_ESC )
							ms.Lua.L_Error( "invalid use of '%' in replacement string" );
						b.Append( c );
					}
					else if( c == '0' )
						b.Append( ms.Src, s, e - s );
					else
					{
						PushOneCapture( ms, c - '1', s, e );
						b.Append( ms.Lua.ToString(-1) ); // add capture to accumulated result
						ms.Lua.Pop( 1 );
					}
				}
			}
		}

		private static void Add_Value( MatchState ms, StringBuilder b, int s, int e, LuaType tr )
		{
			ILuaState lua = ms.Lua;
			switch( tr )
			{
				case LuaType.LUA_TFUNCTION:
				{
					lua.PushValue(3);
					int n = PushCaptures( ms, s, e );
					lua.Call(n, 1);
					break;
				}
				case LuaType.LUA_TTABLE:
				{
					PushOneCapture( ms, 0, s, e );
					lua.GetTable(3);
					break;
				}
				default: // LUA_TNUMBER or LUA_TSTRING
				{
					Add_S( ms, b, s, e );
					return;
				}
			}
			if( !lua.ToBoolean(-1) ) // nil or false?
				b.Append( ms.Src, s, e - s ); // keep original text
			else if( !lua.IsString(-1) )
				lua.L_Error( "invalid replacement value (a {0})", lua.L_TypeName(-1) );
			else
				b.Append( lua.ToString(-1) ); // add result to accumulator
			lua.Pop(1);
		}

		private static int Str_Gsub( ILuaState lua )
		{
			string src = lua.L_CheckString(1);
			string p = lua.L_CheckString(2);
			LuaType tr = lua.Type(3);
			// a negative maximum is no maximum, as for C's size_t
			uint max_s = (uint)lua.L_OptInt(4, src.Length + 1);
			int ppos = 0;
			bool anchor = p.Length > 0 && p[0] == '^';
			uint n = 0;
			lua.L_ArgCheck( tr == LuaType.LUA_TNUMBER || tr == LuaType.LUA_TSTRING ||
				tr == LuaType.LUA_TFUNCTION || tr == LuaType.LUA_TTABLE, 3,
				"string/function/table expected" );
			StringBuilder b = new StringBuilder( src.Length );
			if( anchor )
				ppos++; // skip anchor character
			MatchState ms = NewMatchState( lua, src, p );
			int s = 0;
			while( n < max_s )
			{
				ms.Level = 0;
				int e = Match( ms, s, ppos );
				if( e != -1 )
				{
					n++;
					Add_Value( ms, b, s, e, tr );
				}
				if( e != -1 && e > s ) // non empty match?
					s = e; // skip it
				else if( s < ms.SrcEnd )
					b.Append( src[s++] );
				else break;
				if( anchor ) break;
			}
			b.Append( src, s, ms.SrcEnd - s );
			lua.PushString( b.ToString() );
			lua.PushNumber( n ); // number of substitutions
			return 2;
		}

		// ======================================================
		// STRING FORMAT
		// ======================================================

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

		private static int ScanFormat( ILuaState lua, string format, int s, out FormatSpec spec )
		{
			spec = new FormatSpec();
			spec.Width = -1;
			spec.Precision = -1;
			int p = s;
			for( char c; (c = At( format, p )) != '\0' && FLAGS.IndexOf( c ) != -1; p++ ) // skip flags
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
			if( p - s >= FLAGS.Length + 1 )
				lua.L_Error( "invalid format (repeated flags)" );
			int start = p;
			if( Utl.IsDigit( At( format, p ) ) ) p++; // skip width
			if( Utl.IsDigit( At( format, p ) ) ) p++; // (2 digits at most)
			if( p > start )
				spec.Width = int.Parse( format.Substring( start, p - start ), CultureInfo.InvariantCulture );
			if( At( format, p ) == '.' )
			{
				p++;
				start = p;
				if( Utl.IsDigit( At( format, p ) ) ) p++; // skip precision
				if( Utl.IsDigit( At( format, p ) ) ) p++; // (2 digits at most)
				spec.Precision = (p > start)
					? int.Parse( format.Substring( start, p - start ), CultureInfo.InvariantCulture )
					: 0;
			}
			if( Utl.IsDigit( At( format, p ) ) )
				lua.L_Error( "invalid format (width or precision too long)" );
			return p;
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

		private static void FormatInteger( ILuaState lua, StringBuilder sb, FormatSpec spec, int arg )
		{
			double n = lua.L_CheckNumber( arg );
			// the range of C's long long, which Lua 5.2 formats with
			lua.L_ArgCheck( n >= -9223372036854775808.0 && n < 9223372036854775808.0, arg,
				"not a number in proper range" );
			long ni = (long)n;
			bool negative = ni < 0;
			ulong mag = negative ? (ulong)(-(ni + 1)) + 1 : (ulong)ni;
			string digits = IntDigits( mag.ToString( CultureInfo.InvariantCulture ), spec, mag == 0 );
			AddPadded( sb, spec, Sign( spec, negative ), digits, spec.Precision < 0 );
		}

		private static void FormatUnsigned( ILuaState lua, StringBuilder sb, FormatSpec spec,
			int arg, char conv )
		{
			double n = lua.L_CheckNumber( arg );
			lua.L_ArgCheck( n > -1.0 && n < 18446744073709551616.0, arg,
				"not a non-negative number in proper range" );
			ulong ni = n <= 0 ? 0 : (ulong)n;
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

		private static void FormatFloat( ILuaState lua, StringBuilder sb, FormatSpec spec,
			int arg, char conv )
		{
			double v = lua.L_CheckNumber( arg );
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

		private static void AddQuoted( ILuaState lua, StringBuilder sb, int arg )
		{
			var s = lua.L_CheckString(arg);
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

		private static int Str_Format( ILuaState lua )
		{
			int top = lua.GetTop();
			StringBuilder sb = new StringBuilder();
			int arg = 1;
			string format = lua.L_CheckString( arg );
			int s = 0;
			int e = format.Length;
			while( s < e )
			{
				if( format[s] != L_ESC )
				{
					sb.Append( format[s++] );
					continue;
				}

				if( At( format, ++s ) == L_ESC )
				{
					sb.Append( format[s++] ); // %%
					continue;
				}

				// else format item
				if( ++arg > top )
					lua.L_ArgError( arg, "no value" );

				FormatSpec spec;
				s = ScanFormat( lua, format, s, out spec );
				char conv = At( format, s++ );
				switch( conv )
				{
					case 'c':
					{
						// a UTF-16 code unit, or the byte C would take of other values
						int c = lua.L_CheckInteger(arg);
						AddPadded( sb, spec, "", ((char)((char)c == c ? c : c & 0xFF)).ToString(), false );
						break;
					}
					case 'd': case 'i':
					{
						FormatInteger( lua, sb, spec, arg );
						break;
					}
					case 'o': case 'u': case 'x': case 'X':
					{
						FormatUnsigned( lua, sb, spec, arg, conv );
						break;
					}
					case 'e': case 'E': case 'f':
					case 'a': case 'A':
					case 'g': case 'G':
					{
						FormatFloat( lua, sb, spec, arg, conv );
						break;
					}
					case 'q':
					{
						AddQuoted( lua, sb, arg );
						break;
					}
					case 's':
					{
						string str = lua.L_ToString( arg );
						lua.Pop( 1 );
						if( spec.Precision < 0 && str.Length >= 100 )
						{
							// no precision and string is too long to be formatted;
							// keep original string
							sb.Append( str );
						}
						else
						{
							if( spec.Precision >= 0 && str.Length > spec.Precision )
								str = str.Substring( 0, spec.Precision );
							AddPadded( sb, spec, "", str, false );
						}
						break;
					}
					default: // also treat cases `pnLlh'
					{
						return lua.L_Error( "invalid option '%{0}' to 'format'", conv );
					}
				}
			}
			lua.PushString( sb.ToString() );
			return 1;
		}

	}

}
