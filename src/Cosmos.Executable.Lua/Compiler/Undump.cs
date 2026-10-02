// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System;


namespace Cosmos.Executable.Lua
{
	class UndumpException : Exception
	{
		public string Why;

		public UndumpException( string why )
		{
			Why = why;
		}
	}

	// lundump.c of Lua 5.5: loads a precompiled chunk as DumpState writes it
	internal class Undump
	{
		private ILoadInfo LoadInfo;
		private long Offset; // current position relative to beginning of dump
		// the strings loaded so far, by their indices (1-based)
		private readonly System.Collections.Generic.List<string> Strings =
			new System.Collections.Generic.List<string>();

		public static LuaProto LoadBinary( ILuaState lua,
			ILoadInfo loadinfo, string name )
		{
			if( name.Length > 0 && (name[0] == '@' || name[0] == '=') )
				name = name.Substring( 1 );
			else if( name.Length > 0 && name[0] == LuaConf.LUA_SIGNATURE[0] )
				name = "binary string";

			try
			{
				var undump = new Undump( loadinfo );
				undump.CheckHeader();
				int nupvalues = undump.LoadByte(); // number of upvalues of the main function
				var proto = new LuaProto();
				undump.LoadFunction( proto );
				if( nupvalues != proto.Upvalues.Count )
					throw new UndumpException( "corrupted chunk" );
				return proto;
			}
			catch( UndumpException e )
			{
				var Lua = (LuaState)lua;
				Lua.O_PushString( string.Format(
					"{0}: bad binary format ({1})", name, e.Why ) );
				Lua.D_Throw( ThreadStatus.LUA_ERRSYNTAX );
				return null;
			}
		}

		private Undump( ILoadInfo loadinfo )
		{
			LoadInfo = loadinfo;
		}

		private byte[] LoadBlock( int count )
		{
			var ret = new byte[count];
			for( int i=0; i<count; ++i )
			{
				var c = LoadInfo.ReadByte();
				if( c == -1 )
					throw new UndumpException( "truncated chunk" );
				ret[i] = (byte)c;
			}
			Offset += count;
			return ret;
		}

		private void LoadAlign( int align )
		{
			int padding = align - (int)(Offset % align);
			if( padding < align ) // (padding == align) means no padding
				LoadBlock( padding );
		}

		private int LoadByte()
		{
			var c = LoadInfo.ReadByte();
			if( c == -1 )
				throw new UndumpException( "truncated chunk" );
			Offset++;
			return c;
		}

		private ulong LoadVarint( ulong limit )
		{
			ulong x = 0;
			int b;
			limit >>= 7;
			do {
				b = LoadByte();
				if( x > limit )
					throw new UndumpException( "integer overflow" );
				x = (x << 7) | (uint)(b & 0x7f);
			} while( (b & 0x80) != 0 );
			return x;
		}

		private ulong LoadSize()
		{
			return LoadVarint( long.MaxValue ); // MAX_SIZE
		}

		private int LoadInt()
		{
			return (int)LoadVarint( int.MaxValue );
		}

		private double LoadNumber()
		{
			return BitConverter.ToDouble( LoadBlock( DumpState.SIZEOF_NUMBER ), 0 );
		}

		private long LoadInteger()
		{
			ulong cx = LoadVarint( ulong.MaxValue );
			/* decode unsigned to signed */
			if( (cx & 1) != 0 )
				return unchecked((long)~(cx >> 1));
			else
				return unchecked((long)(cx >> 1));
		}

		/*
		** Load a nullable string.
		*/
		private string LoadString()
		{
			ulong size = LoadSize();
			if( size == 0 ) // previously saved string?
			{
				ulong idx = LoadVarint( ulong.MaxValue ); // get its index
				if( idx == 0 ) // no string?
					return null;
				if( idx > (ulong)Strings.Count )
					throw new UndumpException( "invalid string index" );
				return Strings[(int)idx - 1]; // do not save it again
			}
			if( --size > int.MaxValue )
				throw new UndumpException( "truncated chunk" );
			var bytes = LoadBlock( (int)size + 1 ); // with its ending '\0'
			var chars = new char[size];
			for( int i=0; i<(int)size; ++i )
				chars[i] = (char)bytes[i];
			var ts = new string( chars );
			/* add string to list of saved strings */
			Strings.Add( ts );
			return ts;
		}

		private void CheckLiteral( string s, string msg )
		{
			var bytes = LoadBlock( s.Length );
			for( int i=0; i<s.Length; ++i )
			{
				if( bytes[i] != (byte)s[i] )
					throw new UndumpException( msg );
			}
		}

		private void CheckNumSize( int size, string tname )
		{
			if( size != LoadByte() )
				throw new UndumpException( tname + " size mismatch" );
		}

		private void CheckNumFormat( bool eq, string tname )
		{
			if( !eq )
				throw new UndumpException( tname + " format mismatch" );
		}

		private void CheckHeader()
		{
			/* (the 1st char was only peeked at) */
			CheckLiteral( LuaConf.LUA_SIGNATURE, "not a binary chunk" );
			if( LoadByte() != DumpState.LUAC_VERSION )
				throw new UndumpException( "version mismatch" );
			if( LoadByte() != DumpState.LUAC_FORMAT )
				throw new UndumpException( "format mismatch" );
			CheckLiteral( DumpState.LUAC_DATA, "corrupted chunk" );
			CheckNumSize( DumpState.SIZEOF_INT, "int" );
			CheckNumFormat( BitConverter.ToInt32( LoadBlock( DumpState.SIZEOF_INT ), 0 ) == DumpState.LUAC_INT, "int" );
			CheckNumSize( DumpState.SIZEOF_INSTRUCTION, "instruction" );
			CheckNumFormat( BitConverter.ToUInt32( LoadBlock( DumpState.SIZEOF_INSTRUCTION ), 0 ) == DumpState.LUAC_INST, "instruction" );
			CheckNumSize( DumpState.SIZEOF_INTEGER, "Lua integer" );
			CheckNumFormat( BitConverter.ToInt64( LoadBlock( DumpState.SIZEOF_INTEGER ), 0 ) == DumpState.LUAC_INT, "Lua integer" );
			CheckNumSize( DumpState.SIZEOF_NUMBER, "Lua number" );
			CheckNumFormat( LoadNumber() == DumpState.LUAC_NUM, "Lua number" );
		}

		private void LoadFunction( LuaProto proto )
		{
			proto.LineDefined = LoadInt();
			proto.LastLineDefined = LoadInt();
			proto.NumParams = LoadByte();
			/* get only the meaningful flags */
			proto.Flag = (byte)(LoadByte() & (LuaProto.PF_VAHID | LuaProto.PF_VATAB));
			proto.MaxStackSize = (byte)LoadByte();
			LoadCode( proto );
			LoadConstants( proto );
			LoadUpvalues( proto );
			LoadProtos( proto );
			proto.Source = LoadString();
			LoadDebug( proto );
		}

		private void LoadCode( LuaProto proto )
		{
			var n = LoadInt();
			LoadAlign( DumpState.SIZEOF_INSTRUCTION );
			proto.Code.Clear();
			for( int i=0; i<n; ++i )
				proto.Code.Add( (Instruction)BitConverter.ToUInt32( LoadBlock( 4 ), 0 ) );
		}

		private void LoadConstants( LuaProto proto )
		{
			var n = LoadInt();
			proto.K.Clear();
			for( int i=0; i<n; ++i )
			{
				int t = LoadByte();
				var v = new StkId();
				switch( t )
				{
					case DumpState.LUA_VNIL:
						v.V.SetNilValue();
						break;
					case DumpState.LUA_VFALSE:
						v.V.SetBValue( false );
						break;
					case DumpState.LUA_VTRUE:
						v.V.SetBValue( true );
						break;
					case DumpState.LUA_VNUMFLT:
						v.V.SetFltValue( LoadNumber() );
						break;
					case DumpState.LUA_VNUMINT:
						v.V.SetIValue( LoadInteger() );
						break;
					case DumpState.LUA_VSHRSTR:
					case DumpState.LUA_VLNGSTR:
					{
						string ts = LoadString();
						if( ts == null )
							throw new UndumpException( "bad format for constant string" );
						v.V.SetSValue( ts );
						break;
					}
					default:
						throw new UndumpException( "invalid constant" );
				}
				proto.K.Add( v );
			}
		}

		private void LoadUpvalues( LuaProto proto )
		{
			var n = LoadInt();
			proto.Upvalues.Clear();
			for( int i=0; i<n; ++i )
			{
				var up = new UpvalDesc();
				up.InStack = LoadByte() != 0;
				up.Index = LoadByte();
				up.Kind = (byte)LoadByte();
				proto.Upvalues.Add( up );
			}
		}

		private void LoadProtos( LuaProto proto )
		{
			var n = LoadInt();
			proto.P.Clear();
			for( int i=0; i<n; ++i )
			{
				var p = new LuaProto();
				proto.P.Add( p );
				LoadFunction( p );
			}
		}

		private void LoadDebug( LuaProto proto )
		{
			int n = LoadInt();
			proto.LineInfo.Clear();
			foreach( var b in LoadBlock( n ) )
				proto.LineInfo.Add( (sbyte)b );

			n = LoadInt();
			proto.AbsLineInfo.Clear();
			if( n > 0 )
			{
				LoadAlign( DumpState.SIZEOF_INT );
				for( int i=0; i<n; ++i )
				{
					var abs = new AbsLineInfo();
					abs.Pc = BitConverter.ToInt32( LoadBlock( DumpState.SIZEOF_INT ), 0 );
					abs.Line = BitConverter.ToInt32( LoadBlock( DumpState.SIZEOF_INT ), 0 );
					proto.AbsLineInfo.Add( abs );
				}
			}

			n = LoadInt();
			proto.LocVars.Clear();
			for( int i=0; i<n; ++i )
			{
				var v = new LocVar();
				v.VarName = LoadString();
				v.StartPc = LoadInt();
				v.EndPc = LoadInt();
				proto.LocVars.Add( v );
			}

			n = LoadInt();
			if( n != 0 ) // does it have debug information?
				n = proto.Upvalues.Count; // must be this many
			for( int i=0; i<n; ++i )
				proto.Upvalues[i].Name = LoadString();
		}
	}

}
