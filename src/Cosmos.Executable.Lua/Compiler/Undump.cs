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

	// lundump.c: loads a precompiled chunk as DumpState writes it
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
				undump.LoadByte(); // number of upvalues of the main function
				return undump.LoadFunction( null );
			}
			catch( UndumpException e )
			{
				var Lua = (LuaState)lua;
				Lua.O_PushString( string.Format(
					"{0}: {1} precompiled chunk", name, e.Why ) );
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
					throw new UndumpException( "truncated" );
				ret[i] = (byte)c;
			}
			return ret;
		}

		private int LoadByte()
		{
			return LoadBlock( 1 )[0];
		}

		private int LoadInt()
		{
			return BitConverter.ToInt32( LoadBlock( DumpState.SIZEOF_INT ), 0 );
		}

		private long LoadInteger()
		{
			return BitConverter.ToInt64( LoadBlock( DumpState.SIZEOF_INTEGER ), 0 );
		}

		private double LoadNumber()
		{
			return BitConverter.ToDouble( LoadBlock( DumpState.SIZEOF_NUMBER ), 0 );
		}

		private string LoadString()
		{
			ulong size = (ulong)LoadByte();
			if( size == 0xFF )
				size = BitConverter.ToUInt64( LoadBlock( DumpState.SIZEOF_SIZET ), 0 );
			if( size == 0 )
				return null;
			if( --size > int.MaxValue )
				throw new UndumpException( "truncated" );
			var bytes = LoadBlock( (int)size );
			var chars = new char[bytes.Length];
			for( int i=0; i<bytes.Length; ++i )
				chars[i] = (char)bytes[i];
			return new string( chars );
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
				throw new UndumpException( tname + " size mismatch in" );
		}

		private void CheckHeader()
		{
			CheckLiteral( LuaConf.LUA_SIGNATURE, "not a" );
			if( LoadByte() != DumpState.LUAC_VERSION )
				throw new UndumpException( "version mismatch in" );
			if( LoadByte() != DumpState.LUAC_FORMAT )
				throw new UndumpException( "format mismatch in" );
			CheckLiteral( DumpState.LUAC_DATA, "corrupted" );
			CheckSize( DumpState.SIZEOF_INT, "int" );
			CheckSize( DumpState.SIZEOF_SIZET, "size_t" );
			CheckSize( DumpState.SIZEOF_INSTRUCTION, "Instruction" );
			CheckSize( DumpState.SIZEOF_INTEGER, "lua_Integer" );
			CheckSize( DumpState.SIZEOF_NUMBER, "lua_Number" );
			if( LoadInteger() != DumpState.LUAC_INT )
				throw new UndumpException( "endianness mismatch in" );
			if( LoadNumber() != DumpState.LUAC_NUM )
				throw new UndumpException( "float format mismatch in" );
		}

		private LuaProto LoadFunction( string psource )
		{
			LuaProto proto = new LuaProto();
			proto.Source = LoadString();
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
					case (int)LuaType.LUA_TNIL:
						v.V.SetNilValue();
						break;
					case (int)LuaType.LUA_TBOOLEAN:
						v.V.SetBValue( LoadByte() != 0 );
						break;
					case TValue.LUA_TNUMFLT:
						v.V.SetFltValue( LoadNumber() );
						break;
					case TValue.LUA_TNUMINT:
						v.V.SetIValue( LoadInteger() );
						break;
					case (int)LuaType.LUA_TSTRING:
					case (int)LuaType.LUA_TSTRING | (1 << 4): // a long string
						v.V.SetSValue( LoadString() );
						break;
					default:
						throw new UndumpException( "bad constant in" );
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
				proto.Upvalues.Add(
					new UpvalDesc()
					{
						Name = null,
						InStack = LoadByte() != 0,
						Index = LoadByte(),
					} );
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
			for( int i=0; i<n; ++i )
				proto.LineInfo.Add( LoadInt() );

			n = LoadInt();
			proto.LocVars.Clear();
			for( int i=0; i<n; ++i )
			{
				proto.LocVars.Add(
					new LocVar()
					{
						VarName = LoadString(),
						StartPc = LoadInt(),
						EndPc   = LoadInt(),
					} );
			}

			n = LoadInt();
			for( int i=0; i<n; ++i )
				proto.Upvalues[i].Name = LoadString();
		}
	}

}
