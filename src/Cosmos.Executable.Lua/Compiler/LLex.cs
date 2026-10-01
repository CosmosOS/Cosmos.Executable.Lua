// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

using NumberStyles = System.Globalization.NumberStyles;

namespace Cosmos.Executable.Lua
{
    internal class LLexException : Exception
    {
        public LLexException( string info ) : base( info ) { }
    }

    internal enum TK
    {
		// reserved words
        AND = 257,
		BREAK,
		DO,
		ELSE,
		ELSEIF,
		END,
		FALSE,
		FOR,
		FUNCTION,
		GOTO,
		IF,
		IN,
		LOCAL,
		NIL,
		NOT,
		OR,
		REPEAT,
		RETURN,
		THEN,
		TRUE,
		UNTIL,
		WHILE,
		// other terminal symbols
        CONCAT,
        DOTS,
        EQ,
        GE,
        LE,
        NE,
		DBCOLON,
        NUMBER,
        STRING,
        NAME,
        EOS,
    }

    internal abstract class Token
    {
        public abstract int TokenType{ get; }

        public bool EqualsToToken( Token other ) {
            return TokenType == other.TokenType;
        }

        public bool EqualsToToken( int other ) {
            return TokenType == other;
        }

        public bool EqualsToToken( TK other ) {
            return TokenType == (int)other;
        }
    }

    internal class LiteralToken : Token
    {
        private int _Literal;

        public LiteralToken( int literal )
        {
            _Literal = literal;
        }

        public override int TokenType
        {
            get { return _Literal; }
        }

        public override string ToString()
        {
            return string.Format( "LiteralToken: {0}", _Literal );
        }
    }

    internal class TypedToken : Token
    {
        private TK _Type;

        public TypedToken( TK type )
        {
            _Type = type;
        }

        public override int TokenType
        {
            get { return (int)_Type; }
        }

        public override string ToString()
        {
            return string.Format( "TypedToken: {0}", _Type );
        }
    }

    internal class StringToken : TypedToken
    {
        public string SemInfo;
        
        public StringToken( string seminfo ) : base( TK.STRING )
        {
            SemInfo = seminfo;
        }

        public override string ToString()
        {
            return string.Format( "StringToken: {0}", SemInfo );
        }
    }

    internal class NameToken : TypedToken
    {
        public string SemInfo;

        public NameToken( string seminfo ) : base( TK.NAME )
        {
            SemInfo = seminfo;
        }

        public override string ToString()
        {
            return string.Format( "NameToken: {0}", SemInfo );
        }
    }

    internal class NumberToken : TypedToken
    {
        public double SemInfo;

        public NumberToken( double seminfo ) : base( TK.NUMBER )
        {
            SemInfo = seminfo;
        }

        public override string ToString()
        {
            return string.Format( "NumberToken: {0}", SemInfo );
        }
    }

    internal class LLex
    {
        public const char EOZ = Char.MaxValue;

		private LuaState Lua;
        private int Current;
        public int LineNumber;
		public int LastLine;
		private ILoadInfo LoadInfo;
		public string Source;

        public Token Token;
        private Token LookAhead;

        private StringBuilder _Saved;
        private StringBuilder Saved
        {
            get {
                if( _Saved == null ) { _Saved = new StringBuilder(); }
                return _Saved;
            }
        }

		private static Dictionary<string, TK> ReservedWordDict;
		static LLex()
		{
			ReservedWordDict = new Dictionary<string, TK>();
			ReservedWordDict.Add("and", TK.AND);
			ReservedWordDict.Add("break", TK.BREAK);
			ReservedWordDict.Add("do", TK.DO);
			ReservedWordDict.Add("else", TK.ELSE);
			ReservedWordDict.Add("elseif", TK.ELSEIF);
			ReservedWordDict.Add("end", TK.END);
			ReservedWordDict.Add("false", TK.FALSE);
			ReservedWordDict.Add("for", TK.FOR);
			ReservedWordDict.Add("function", TK.FUNCTION);
			ReservedWordDict.Add("goto", TK.GOTO);
			ReservedWordDict.Add("if", TK.IF);
			ReservedWordDict.Add("in", TK.IN);
			ReservedWordDict.Add("local", TK.LOCAL);
			ReservedWordDict.Add("nil", TK.NIL);
			ReservedWordDict.Add("not", TK.NOT);
			ReservedWordDict.Add("or", TK.OR);
			ReservedWordDict.Add("repeat", TK.REPEAT);
			ReservedWordDict.Add("return", TK.RETURN);
			ReservedWordDict.Add("then", TK.THEN);
			ReservedWordDict.Add("true", TK.TRUE);
			ReservedWordDict.Add("until", TK.UNTIL);
			ReservedWordDict.Add("while", TK.WHILE);
		}

        public LLex( ILuaState lua, ILoadInfo loadinfo, string name )
        {
			Lua			= (LuaState)lua;
            LoadInfo    = loadinfo;
            LineNumber  = 1;
			LastLine	= 1;
            Token       = null;
            LookAhead   = null;
            _Saved      = null;
			Source		= name;

            _Next();
        }

        public void Next()
        {
			LastLine = LineNumber;
            if( LookAhead != null )
            {
                Token = LookAhead;
                LookAhead = null;
            }
            else
            {
                Token = _Lex();
            }
        }

		public Token GetLookAhead()
		{
			Utl.Assert( LookAhead == null );
			LookAhead = _Lex();
			return LookAhead;
		}

        private void _Next()
        {
			var c = LoadInfo.ReadByte();
			Current = (c == -1) ? EOZ : c;
        }

        private void _SaveAndNext()
        {
            Saved.Append( (char)Current );
			_Next();
        }

        private void _Save( char c )
        {
            Saved.Append( c );
        }

        private string _GetSavedString()
        {
            return Saved.ToString();
        }

        private void _ClearSaved()
        {
            _Saved = null;
        }

        private bool _CurrentIsNewLine()
        {
            return Current == '\n' || Current == '\r';
        }

        private bool _CurrentIsDigit()
        {
            return Utl.IsDigit( Current );
        }

		private bool _CurrentIsXDigit()
		{
			return Utl.IsXDigit( Current );
		}

        private bool _CurrentIsSpace()
        {
            return Utl.IsSpace( Current );
        }

        private bool _CurrentIsAlpha()
        {
            return Utl.IsAlpha( Current );
        }

		private bool _IsReserved( string identifier, out TK type )
		{
			return ReservedWordDict.TryGetValue( identifier, out type );
		}

		public bool IsReservedWord( string name )
		{
			return ReservedWordDict.ContainsKey( name );
		}

        private void _IncLineNumber()
        {
            var old = Current;
            _Next();
            if( _CurrentIsNewLine() && Current != old )
                _Next();
            if( ++LineNumber >= Int32.MaxValue )
                _Error( "chunk has too many lines" );
        }

        // a long comment keeps nothing, and returns null
        private string _ReadLongString( int sep, bool isComment )
        {
            _SaveAndNext(); // skip 2nd `['

            if( _CurrentIsNewLine() ) // string starts with a newline?
                _IncLineNumber(); // skip it

            while( true )
            {
                switch( Current )
                {
                    case EOZ:
                        _LexError( isComment ? "unfinished long comment"
							: "unfinished long string", (int)TK.EOS );
                        break;

                    case ']':
                    {
                        if( _SkipSep() == sep )
                        {
                            _SaveAndNext(); // skip 2nd `]'
                            goto endloop;
                        }
                        break;
                    }

                    case '\n':
                    case '\r':
                    {
                        _Save('\n');
                        _IncLineNumber();
                        if( isComment ) _ClearSaved(); // avoid wasting space
                        break;
                    }

                    default:
                    {
                        if( isComment ) _Next();
                        else _SaveAndNext();
                        break;
                    }
                }
            }
            endloop:
            if( isComment )
                return null;
			var r = _GetSavedString();
            return r.Substring( 2+sep, r.Length - 2*(2+sep) );
        }

		// the error shows the escape sequence read so far, as `near '\x4''
		private void _EscapeError( int[] c, int n, string msg )
		{
			_ClearSaved();
			_Save( '\\' );
			for( int i=0; i<n && c[i] != EOZ; ++i )
				_Save( (char)c[i] );
			_LexError( msg, (int)TK.STRING );
		}

		private int _ReadHexEscape()
		{
			int r = 0;
			var c = new int[3] { 'x', 0, 0 };
			// read two hex digits
			for( int i=1; i<3; ++i )
			{
				_Next();
				c[i] = Current;
				if( !_CurrentIsXDigit() )
					_EscapeError( c, i+1, "hexadecimal digit expected" );
				r = (r << 4) + Utl.HexaValue( Current );
			}
			return r;
		}

		private int _ReadDecEscape()
		{
			int r = 0;
			var c = new int[3];
			// read up to 3 digits
			int i = 0;
			for( i=0; i<3 && _CurrentIsDigit(); ++i )
			{
				c[i] = Current;
				r = r*10 + Current - '0';
				_Next();
			}
			if( r > Byte.MaxValue )
				_EscapeError( c, i, "decimal escape too large" );
			return r;
		}

        // keeps the delimiters in the saved text, for error messages
        private string _ReadString()
        {
            var del = Current;
            _SaveAndNext();
            while( Current != del )
            {
                switch( Current )
                {
                    case EOZ:
                        _LexError( "unfinished string", (int)TK.EOS );
                        continue;

                    case '\n':
                    case '\r':
                        _LexError( "unfinished string", (int)TK.STRING );
                        continue;

                    case '\\':
                    {
                        int c;
                        _Next(); // do not save the `\'
                        switch( Current )
                        {
                            case 'a': c='\a'; break;
                            case 'b': c='\b'; break;
                            case 'f': c='\f'; break;
                            case 'n': c='\n'; break;
                            case 'r': c='\r'; break;
                            case 't': c='\t'; break;
                            case 'v': c='\v'; break;
							case 'x': c=_ReadHexEscape(); break;

                            case '\n':
                            case '\r': _IncLineNumber(); _Save('\n'); continue;

							case '\\':
							case '\"':
							case '\'': c=Current; break;

                            case EOZ: continue; // will raise an error next loop

							// zap following span of spaces
							case 'z': {
								_Next(); // skip `z'
								while( _CurrentIsSpace() )
								{
									if( _CurrentIsNewLine() )
										_IncLineNumber();
									else
										_Next();
								}
								continue;
							}

                            default:
                            {
                                if( !_CurrentIsDigit() )
									_EscapeError( new int[] { Current }, 1,
										"invalid escape sequence" );

								// digital escape \ddd
								c = _ReadDecEscape();
								_Save( (char)c );
								continue;
                                // {
                                //     c = (char)0;
                                //     for(int i=0; i<3 && _CurrentIsDigit(); ++i)
                                //     {
                                //         c = (char)(c*10 + Current - '0');
                                //         _Next();
                                //     }
                                //     _Save( c );
                                // }
                                // continue;
                            }
                        }
                        _Save( (char)c );
                        _Next();
                        continue;
                    }

                    default:
                        _SaveAndNext();
                        continue;
                }
            }
            _SaveAndNext(); // skip delimiter
            var r = _GetSavedString();
            return r.Substring( 1, r.Length - 2 );
        }

        private double _ReadNumber()
        {
			var expo = new char[] { 'E', 'e' };
			Utl.Assert( _CurrentIsDigit() );
			var first = Current;
			_SaveAndNext();
			if( first == '0' && (Current == 'X' || Current == 'x'))
			{
				expo = new char[] { 'P', 'p' };
				_SaveAndNext();
			}
            for(;;)
            {
				if( Current == expo[0] || Current == expo[1] )
				{
					_SaveAndNext();
					if( Current == '+' || Current == '-' )
						_SaveAndNext();
				}
				if( _CurrentIsXDigit() || Current == '.' )
					_SaveAndNext();
				else
					break;
            }

            double ret;
			var str = _GetSavedString();
			if( LuaState.O_Str2Decimal( str, out ret ) )
			{
				return ret;
			}
			else
			{
                _LexError( "malformed number", (int)TK.NUMBER );
				return 0.0;
			}
        }

        // private float _ReadNumber()
        // {
        //     do
        //     {
        //         _SaveAndNext();
        //     } while( _CurrentIsDigit() || Current == '.' );
        //     if( Current == 'E' || Current == 'e' )
        //     {
        //         _SaveAndNext();
        //         if( Current == '+' || Current == '-' )
        //             _SaveAndNext();
        //     }
        //     while( _CurrentIsAlpha() || _CurrentIsDigit() || Current == '_' )
        //         _SaveAndNext();
        //     float ret;
        //     if( !Single.TryParse( _GetSavedString(), out ret ) )
        //         _Error( "malformed number" );
        //     return ret;
        // }

        private void _Error( string error )
        {
			_LexError( error, Token != null ? Token.TokenType : 0 );
        }

		// llex.c's luaX_token2str
		public string Token2Str( int token )
		{
			if( token < FIRST_RESERVED ) // single-byte symbols?
				return Utl.IsPrint( token )
					? string.Format( "'{0}'", (char)token )
					: string.Format( "char({0})", token );
			switch( (TK)token )
			{
				case TK.CONCAT: return "'..'";
				case TK.DOTS: return "'...'";
				case TK.EQ: return "'=='";
				case TK.GE: return "'>='";
				case TK.LE: return "'<='";
				case TK.NE: return "'~='";
				case TK.DBCOLON: return "'::'";
				case TK.EOS: return "<eof>";
				case TK.NUMBER: return "<number>";
				case TK.STRING: return "<string>";
				case TK.NAME: return "<name>";
				default: return "'" + ReservedWords[token - FIRST_RESERVED] + "'";
			}
		}
		private const int FIRST_RESERVED = (int)TK.AND;
		private static readonly string[] ReservedWords = {
			"and", "break", "do", "else", "elseif", "end", "false", "for",
			"function", "goto", "if", "in", "local", "nil", "not", "or",
			"repeat", "return", "then", "true", "until", "while",
		};

		private string _TxtToken( int token )
		{
			switch( token )
			{
				case (int)TK.NAME:
				case (int)TK.STRING:
				case (int)TK.NUMBER:
					return "'" + _GetSavedString() + "'";
				default:
					return Token2Str( token );
			}
		}

		// "chunk:line: msg near token"; no `near' part for token 0
		private void _LexError( string msg, int token )
		{
			msg = string.Format( "{0}:{1}: {2}",
				LuaState.O_ChunkId( Source ), LineNumber, msg );
			if( token != 0 )
				msg = string.Format( "{0} near {1}", msg, _TxtToken( token ) );
			Lua.O_PushString( msg );
			Lua.D_Throw( ThreadStatus.LUA_ERRSYNTAX );
		}

		public void SyntaxError( string msg )
		{
			_LexError( msg, Token.TokenType );
		}

		// an error about the meaning of the code: no `near' part
		public void SemanticError( string msg )
		{
			_LexError( msg, 0 );
		}

        private int _SkipSep()
        {
            int count = 0;
            var boundary = Current;
            _SaveAndNext();
            while( Current == '=' ) {
                _SaveAndNext();
                count++;
            }
            return ( Current == boundary ? count : (-count)-1 );
        }

        private Token _Lex()
        {
            _ClearSaved();
            while( true )
            {
                switch( Current )
                {
                    case '\n':
                    case '\r': {
                        _IncLineNumber();
                        continue;
                    }

                    case '-': {
                        _Next();
                        if( Current != '-' ) return new LiteralToken('-');

                        // else is a comment
                        _Next();
                        if( Current == '[' ) // long comment?
                        {
                            int sep = _SkipSep();
                            _ClearSaved(); // `_SkipSep' may dirty the buffer
                            if( sep >= 0 )
                            {
                                _ReadLongString( sep, true );
                                _ClearSaved();
                                continue;
                            }
                        }

                        // else is a short comment
                        while( !_CurrentIsNewLine() && Current != EOZ )
                            _Next();
                        continue;
                    }

                    case '[': {
                        int sep = _SkipSep();
                        if( sep >= 0 ) {
                            string seminfo = _ReadLongString( sep, false );
                            return new StringToken( seminfo );
                        }
                        else if( sep == -1 ) return new LiteralToken('[');
                        else _LexError("invalid long string delimiter", (int)TK.STRING);
                        continue;
                    }

                    case '=': {
                        _Next();
                        if( Current != '=' ) return new LiteralToken('=');
                        _Next();
                        return new TypedToken( TK.EQ );
                    }

                    case '<': {
                        _Next();
                        if( Current != '=' ) return new LiteralToken('<');
                        _Next();
                        return new TypedToken( TK.LE );
                    }

                    case '>': {
                        _Next();
                        if( Current != '=' ) return new LiteralToken('>');
                        _Next();
                        return new TypedToken( TK.GE );
                    }

                    case '~': {
                        _Next();
                        if( Current != '=' ) return new LiteralToken('~');
                        _Next();
                        return new TypedToken( TK.NE );
                    }

					case ':': {
						_Next();
						if( Current != ':' ) return new LiteralToken(':');
						_Next();
						return new TypedToken( TK.DBCOLON ); // new in 5.2 ?
					}

                    case '"':
                    case '\'': {
                        return new StringToken( _ReadString() );
                    }

                    case '.': {
                        _SaveAndNext();
                        if( Current == '.' )
                        {
                            _SaveAndNext();
                            if( Current == '.' )
                            {
                                _SaveAndNext();
                                return new TypedToken( TK.DOTS );
                            }
                            else
                            {
                                return new TypedToken( TK.CONCAT );
                            }
                        }
                        else if( !_CurrentIsDigit() )
                            return new LiteralToken('.');
                        else
                            return new NumberToken( _ReadNumber() );
                    }

                    case EOZ: {
                        return new TypedToken( TK.EOS );
                    }

                    case ' ':
                    case '\f':
                    case '\t':
                    case '\v': {
                        _Next();
                        continue;
                    }

                    default: {
                        if( _CurrentIsDigit() )
                        {
                            return new NumberToken( _ReadNumber() );
                        }
                        else if( _CurrentIsAlpha() || Current == '_' )
                        {
                            do {
                                _SaveAndNext();
                            } while( _CurrentIsAlpha() ||
									 _CurrentIsDigit() ||
									 Current == '_' );

                            string identifier = _GetSavedString();
							TK type;
                            if( _IsReserved( identifier, out type ) )
							{
								return new TypedToken( type );
							}
							else
							{
								return new NameToken( identifier );
							}
                        }
                        else
                        {
                            var c = Current;
                            // A character above the bytes C Lua reads would
                            // take the number of a reserved word as a token
                            if( c >= FIRST_RESERVED )
                                _LexError( string.Format(
									"unexpected symbol near char({0})", c ), 0 );
                            _Next();
                            return new LiteralToken(c);
                        }
                    }
                }
            }
        }

    }

}

