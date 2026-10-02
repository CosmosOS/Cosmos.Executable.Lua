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

	// lundump.c of Lua 5.4: loads a precompiled chunk as DumpState writes it
	internal class Undump
	{
		private ILoadInfo LoadInfo;

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
				var proto = undump.LoadFunction( null );
				if( nupvalues != proto.Upvalues.Count )
					throw new UndumpException( "upvalues mismatch" );
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
			return ret;
		}

		private int LoadByte()
		{
			var c = LoadInfo.ReadByte();
			if( c == -1 )
				throw new UndumpException( "truncated chunk" );
			return c;
		}

		private ulong LoadUnsigned( ulong limit )
		{
			ulong x = 0;
			int b;
			limit >>= 7;
			do {
				b = LoadByte();
				if( x >= limit )
					throw new UndumpException( "integer overflow" );
				x = (x << 7) | (uint)(b & 0x7f);
			} while( (b & 0x80) == 0 );
			return x;
		}

		private ulong LoadSize()
		{
			return LoadUnsigned( ulong.MaxValue );
		}

		private int LoadInt()
		{
			return (int)LoadUnsigned( int.MaxValue );
		}

		private long LoadInteger()
		{
			return BitConverter.ToInt64( LoadBlock( DumpState.SIZEOF_INTEGER ), 0 );
		}

		private double LoadNumber()
		{
			return BitConverter.ToDouble( LoadBlock( DumpState.SIZEOF_NUMBER ), 0 );
		}

		/*
		** Load a nullable string.
		*/
		private string LoadStringN()
		{
			ulong size = LoadSize();
			if( size == 0 ) // no string?
				return null;
			if( --size > int.MaxValue )
				throw new UndumpException( "truncated chunk" );
			var bytes = LoadBlock( (int)size );
			var chars = new char[bytes.Length];
			for( int i=0; i<bytes.Length; ++i )
				chars[i] = (char)bytes[i];
			return new string( chars );
		}

		/*
		** Load a non-nullable string.
		*/
		private string LoadString()
		{
			string st = LoadStringN();
			if( st == null )
				throw new UndumpException( "bad format for constant string" );
			return st;
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

		private void CheckSize( int size, string tname )
		{
			if( LoadByte() != size )
				throw new UndumpException( tname + " size mismatch" );
		}

		private void CheckHeader()
		{
			CheckLiteral( LuaConf.LUA_SIGNATURE, "not a binary chunk" );
			if( LoadByte() != DumpState.LUAC_VERSION )
				throw new UndumpException( "version mismatch" );
			if( LoadByte() != DumpState.LUAC_FORMAT )
				throw new UndumpException( "format mismatch" );
			CheckLiteral( DumpState.LUAC_DATA, "corrupted chunk" );
			CheckSize( DumpState.SIZEOF_INSTRUCTION, "Instruction" );
			CheckSize( DumpState.SIZEOF_INTEGER, "lua_Integer" );
			CheckSize( DumpState.SIZEOF_NUMBER, "lua_Number" );
			if( LoadInteger() != DumpState.LUAC_INT )
				throw new UndumpException( "integer format mismatch" );
			if( LoadNumber() != DumpState.LUAC_NUM )
				throw new UndumpException( "float format mismatch" );
		}

		private LuaProto LoadFunction( string psource )
		{
			LuaProto proto = new LuaProto();
			proto.Source = LoadStringN();
			if( proto.Source == null ) // no source in dump?
				proto.Source = psource; // reuse parent's source
			proto.LineDefined = LoadInt();
			proto.LastLineDefined = LoadInt();
			proto.NumParams = LoadByte();
			proto.IsVarArg = LoadByte() != 0;
			proto.MaxStackSize = (byte)LoadByte();

			LoadCode( proto );
			LoadConstants( proto );
			LoadUpvalues( proto );
			LoadProtos( proto );
			LoadDebug( proto );
			return proto;
		}

		private void LoadCode( LuaProto proto )
		{
			var n = LoadInt();
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
						v.V.SetSValue( LoadString() );
						break;
					default:
						throw new UndumpException( "bad constant" );
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
				proto.P.Add( LoadFunction( proto.Source ) );
		}

		private void LoadDebug( LuaProto proto )
		{
			int n = LoadInt();
			proto.LineInfo.Clear();
			foreach( var b in LoadBlock( n ) )
				proto.LineInfo.Add( (sbyte)b );

			n = LoadInt();
			proto.AbsLineInfo.Clear();
			for( int i=0; i<n; ++i )
			{
				var abs = new AbsLineInfo();
				abs.Pc = LoadInt();
				abs.Line = LoadInt();
				proto.AbsLineInfo.Add( abs );
			}

			n = LoadInt();
			proto.LocVars.Clear();
			for( int i=0; i<n; ++i )
			{
				var v = new LocVar();
				v.VarName = LoadStringN();
				v.StartPc = LoadInt();
				v.EndPc = LoadInt();
				proto.LocVars.Add( v );
			}

			n = LoadInt();
			if( n != 0 ) // does it have debug information?
				n = proto.Upvalues.Count; // must be this many
			for( int i=0; i<n; ++i )
				proto.Upvalues[i].Name = LoadStringN();
		}
	}

}
