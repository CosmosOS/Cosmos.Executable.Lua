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
		GLOBAL,
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
        IDIV,
        CONCAT,
        DOTS,
        EQ,
        GE,
        LE,
        NE,
        SHL,
        SHR,
		DBCOLON,
        EOS,
        FLT,
        INT,
        NAME,
        STRING,
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

        public NumberToken( double seminfo ) : base( TK.FLT )
        {
            SemInfo = seminfo;
        }

        public override string ToString()
        {
            return string.Format( "NumberToken: {0}", SemInfo );
        }
    }

    internal class IntegerToken : TypedToken
    {
        public long SemInfo;

        public IntegerToken( long seminfo ) : base( TK.INT )
        {
            SemInfo = seminfo;
        }

        public override string ToString()
        {
            return string.Format( "IntegerToken: {0}", SemInfo );
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
			// LUA_COMPAT_GLOBAL, on in the reference build: "global" is not a
			// reserved word, and the parser reads 'global' statements by
			// looking ahead
		}

		// the strings of the chunk, one copy of each (the scanner table of
		// luaX_newstring): equal literals are one object, as '%p' shows
		private readonly Dictionary<string, string> Strings = new Dictionary<string, string>();

		private string NewString( string s )
		{
			string saved;
			if( Strings.TryGetValue( s, out saved ) ) // string already present?
				return saved; // get saved copy
			Strings.Add( s, s );
			return s;
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

		// luaZ_buffremove: drops the last 'n' saved characters
		private void _RemoveSaved( int n )
		{
			Saved.Length -= n;
		}

		// esccheck: an error in an escape sequence, which shows the string
		// read so far, with the current character
		private void _EscCheck( bool c, string msg )
		{
			if( !c )
			{
				if( Current != EOZ )
					_SaveAndNext(); // add current to buffer for error message
				_LexError( msg, (int)TK.STRING );
			}
		}

		private int _GetHexa()
		{
			_SaveAndNext();
			_EscCheck( _CurrentIsXDigit(), "hexadecimal digit expected" );
			return Utl.HexaValue( Current );
		}

		private int _ReadHexaEsc()
		{
			int r = _GetHexa();
			r = (r << 4) + _GetHexa();
			_RemoveSaved( 2 ); // remove saved chars from buffer
			return r;
		}

		private long _ReadUtf8Esc()
		{
			long r;
			int i = 4; // chars to be removed: '\', 'u', '{', and first digit
			_SaveAndNext(); // skip 'u'
			_EscCheck( Current == '{', "missing '{'" );
			r = _GetHexa(); // must have at least one digit
			while( true )
			{
				_SaveAndNext();
				if( !_CurrentIsXDigit() )
					break;
				i++;
				_EscCheck( r <= (0x7FFFFFFFL >> 4), "UTF-8 value too large" );
				r = (r << 4) + Utl.HexaValue( Current );
			}
			_EscCheck( Current == '}', "missing '}'" );
			_Next(); // skip '}'
			_RemoveSaved( i ); // remove saved chars from buffer
			return r;
		}

		// utf8esc: a code point as its UTF-8 bytes, one per character
		private void _Utf8Esc()
		{
			long x = _ReadUtf8Esc();
			if( x < 0x80 ) // ascii?
			{
				_Save( (char)x );
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
			for( ; n > 0; n-- )
				_Save( buff[8 - n] );
		}

		private int _ReadDecEsc()
		{
			int i;
			int r = 0; // result accumulator
			for( i=0; i<3 && _CurrentIsDigit(); ++i ) // read up to 3 digits
			{
				r = 10*r + Current - '0';
				_SaveAndNext();
			}
			_EscCheck( r <= Byte.MaxValue, "decimal escape too large" );
			_RemoveSaved( i ); // remove read digits from buffer
			return r;
		}

        // keeps the delimiters in the saved text, for error messages
        private string _ReadString()
        {
            var del = Current;
            _SaveAndNext(); // keep delimiter (for error messages)
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

                    case '\\': // escape sequences
                    {
                        int c; // final character to be saved
                        _SaveAndNext(); // keep '\\' for error messages
                        switch( Current )
                        {
                            case 'a': c='\a'; goto read_save;
                            case 'b': c='\b'; goto read_save;
                            case 'f': c='\f'; goto read_save;
                            case 'n': c='\n'; goto read_save;
                            case 'r': c='\r'; goto read_save;
                            case 't': c='\t'; goto read_save;
                            case 'v': c='\v'; goto read_save;
                            case 'x': c=_ReadHexaEsc(); goto read_save;
                            case 'u': _Utf8Esc(); goto no_save;

                            case '\n':
                            case '\r':
                                _IncLineNumber(); c='\n'; goto only_save;

                            case '\\':
                            case '\"':
                            case '\'': c=Current; goto read_save;

                            case EOZ: goto no_save; // will raise an error next loop

                            // zap following span of spaces
                            case 'z': {
                                _RemoveSaved( 1 ); // remove '\\'
                                _Next(); // skip the 'z'
                                while( _CurrentIsSpace() )
                                {
                                    if( _CurrentIsNewLine() )
                                        _IncLineNumber();
                                    else
                                        _Next();
                                }
                                goto no_save;
                            }

                            default:
                            {
                                _EscCheck( _CurrentIsDigit(), "invalid escape sequence" );
                                c = _ReadDecEsc(); // digital escape \ddd
                                goto only_save;
                            }
                        }
                    read_save:
                        _Next();
                    only_save:
                        _RemoveSaved( 1 ); // remove '\\'
                        _Save( (char)c );
                    no_save:
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

        // read_numeral: quite liberal in what it accepts, as O_Str2Num
        // rejects ill-formed numerals
        private Token _ReadNumber()
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
				if( Current == expo[0] || Current == expo[1] ) // exponent mark?
				{
					_SaveAndNext();
					if( Current == '+' || Current == '-' ) // optional exponent sign
						_SaveAndNext();
				}
				else if( _CurrentIsXDigit() || Current == '.' ) // '%x|%.'
					_SaveAndNext();
				else
					break;
            }
			if( _CurrentIsAlpha() || Current == '_' ) // is numeral touching a letter?
				_SaveAndNext(); // force an error

			TValue obj;
			if( !LuaState.O_Str2Num( _GetSavedString(), out obj ) )
			{
                _LexError( "malformed number", (int)TK.FLT );
				return null;
			}
			if( obj.TtIsInteger() )
				return new IntegerToken( obj.IValue() );
			return new NumberToken( obj.FltValue );
        }

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
					: string.Format( "'<\\{0}>'", token );
			switch( (TK)token )
			{
				case TK.IDIV: return "'//'";
				case TK.CONCAT: return "'..'";
				case TK.DOTS: return "'...'";
				case TK.EQ: return "'=='";
				case TK.GE: return "'>='";
				case TK.LE: return "'<='";
				case TK.NE: return "'~='";
				case TK.SHL: return "'<<'";
				case TK.SHR: return "'>>'";
				case TK.DBCOLON: return "'::'";
				case TK.EOS: return "<eof>";
				case TK.FLT: return "<number>";
				case TK.INT: return "<integer>";
				case TK.STRING: return "<string>";
				case TK.NAME: return "<name>";
				default: return "'" + ReservedWords[token - FIRST_RESERVED] + "'";
			}
		}
		private const int FIRST_RESERVED = (int)TK.AND;
		private static readonly string[] ReservedWords = {
			"and", "break", "do", "else", "elseif", "end", "false", "for",
			"function", "global", "goto", "if", "in", "local", "nil", "not", "or",
			"repeat", "return", "then", "true", "until", "while",
		};

		private string _TxtToken( int token )
		{
			switch( token )
			{
				case (int)TK.NAME:
				case (int)TK.STRING:
				case (int)TK.FLT:
				case (int)TK.INT:
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

		// an error about the meaning of the code: no `near' part, and the
		// line of the last token used
		public void SemanticError( string msg )
		{
			LineNumber = LastLine; // back to line of last used token
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
                            return new StringToken( NewString( seminfo ) );
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
                        if( Current == '=' ) { _Next(); return new TypedToken( TK.LE ); }
                        if( Current == '<' ) { _Next(); return new TypedToken( TK.SHL ); }
                        return new LiteralToken('<');
                    }

                    case '>': {
                        _Next();
                        if( Current == '=' ) { _Next(); return new TypedToken( TK.GE ); }
                        if( Current == '>' ) { _Next(); return new TypedToken( TK.SHR ); }
                        return new LiteralToken('>');
                    }

                    case '/': {
                        _Next();
                        if( Current != '/' ) return new LiteralToken('/');
                        _Next();
                        return new TypedToken( TK.IDIV );
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
                        return new StringToken( NewString( _ReadString() ) );
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
                            return _ReadNumber();
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
                            return _ReadNumber();
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
								return new NameToken( NewString( identifier ) );
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

