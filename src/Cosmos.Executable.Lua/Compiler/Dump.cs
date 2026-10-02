// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System;
using System.Collections.Generic;

namespace Cosmos.Executable.Lua
{
	public enum DumpStatus
	{
		OK,
		ERROR,
	}

	public delegate DumpStatus LuaWriter( byte[] bytes, int start, int length );

	// ldump.c: a function as the precompiled chunk of the reference Lua 5.4
	// writes on a 64-bit machine, little endian
	internal class DumpState
	{
		public static DumpStatus Dump(
			LuaProto proto, LuaWriter writer, bool strip )
		{
			var d = new DumpState();
			d.Writer 	= writer;
			d.Strip 	= strip;
			d.Status	= DumpStatus.OK;

			d.DumpHeader();
			d.DumpByte( proto.Upvalues.Count );
			d.DumpFunction( proto, null );

			return d.Status;
		}

		private LuaWriter 	Writer;
		private bool		Strip;
		private DumpStatus	Status;

		public const string LUAC_DATA = "\u0019\u0093\r\n\u001a\n";
		public const int LUAC_VERSION = 5 * 16 + 4;
		public const int LUAC_FORMAT = 0; // this is the official format
		public const long LUAC_INT = 0x5678;
		public const double LUAC_NUM = 370.5;

		// the sizes of Instruction, lua_Integer and lua_Number
		public const int SIZEOF_INSTRUCTION = 4;
		public const int SIZEOF_INTEGER = 8;
		public const int SIZEOF_NUMBER = 8;

		// the type tags (with variants) of the constants in a binary chunk
		public const int LUA_VNIL = 0;
		public const int LUA_VFALSE = 1;
		public const int LUA_VTRUE = 1 | (1 << 4);
		public const int LUA_VNUMINT = 3;
		public const int LUA_VNUMFLT = 3 | (1 << 4);
		public const int LUA_VSHRSTR = 4;
		public const int LUA_VLNGSTR = 4 | (1 << 4);

		/* maximum size of a short string (LUAI_MAXSHORTLEN) */
		private const int MAXSHORTLEN = 40;

		private DumpState()
		{
		}

		private void DumpLiteral( string s )
		{
			var bytes = new byte[s.Length];
			for( int i=0; i<s.Length; ++i )
				bytes[i] = (byte)s[i];
			DumpBlock( bytes );
		}

		private void DumpHeader()
		{
			DumpLiteral( LuaConf.LUA_SIGNATURE );
			DumpByte( LUAC_VERSION );
			DumpByte( LUAC_FORMAT );
			DumpLiteral( LUAC_DATA );
			DumpByte( SIZEOF_INSTRUCTION );
			DumpByte( SIZEOF_INTEGER );
			DumpByte( SIZEOF_NUMBER );
			DumpInteger( LUAC_INT );
			DumpNumber( LUAC_NUM );
		}

		private void DumpByte( int value )
		{
			DumpBlock( new byte[] { (byte)value } );
		}

		/* dumpSize: 7 bits per byte, most significant first, the last byte
		   marked with its high bit */
		private void DumpSize( ulong x )
		{
			var buff = new byte[10];
			int n = 0;
			do {
				buff[buff.Length - (++n)] = (byte)(x & 0x7f); // fill buffer in reverse order
				x >>= 7;
			} while( x != 0 );
			buff[buff.Length - 1] |= 0x80; // mark last byte
			var bytes = new byte[n];
			Array.Copy( buff, buff.Length - n, bytes, 0, n );
			DumpBlock( bytes );
		}

		private void DumpInt( int value )
		{
			DumpSize( (ulong)value );
		}

		private void DumpInteger( long value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		private void DumpNumber( double value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		// a string, one byte per character: its size plus one, then its bytes
		private void DumpString( string value )
		{
			if( value == null )
			{
				DumpSize( 0 );
				return;
			}

			DumpSize( (ulong)value.Length + 1 );
			var bytes = new byte[value.Length];
			for( int i=0; i<value.Length; ++i )
				bytes[i] = (byte)value[i];
			DumpBlock( bytes );
		}

		private void DumpCode( LuaProto proto )
		{
			DumpInt( proto.Code.Count );
			foreach( var ins in proto.Code )
				DumpBlock( BitConverter.GetBytes( (uint)ins ) );
		}

		private void DumpConstants( LuaProto proto )
		{
			DumpInt( proto.K.Count );
			foreach( var k in proto.K )
			{
				switch( k.V.Tt )
				{
					case (int)LuaType.LUA_TNIL:
						DumpByte( LUA_VNIL );
						break;
					case (int)LuaType.LUA_TBOOLEAN:
						DumpByte( k.V.BValue() ? LUA_VTRUE : LUA_VFALSE );
						break;
					case TValue.LUA_TNUMFLT:
						DumpByte( LUA_VNUMFLT );
						DumpNumber( k.V.FltValue );
						break;
					case TValue.LUA_TNUMINT:
						DumpByte( LUA_VNUMINT );
						DumpInteger( k.V.IValue() );
						break;
					case (int)LuaType.LUA_TSTRING:
						DumpByte( k.V.SValue().Length <= MAXSHORTLEN ? LUA_VSHRSTR : LUA_VLNGSTR );
						DumpString( k.V.SValue() );
						break;
					default:
						Utl.Assert(false);
						break;
				}
			}
		}

		private void DumpProtos( LuaProto proto )
		{
			DumpInt( proto.P.Count );
			foreach( var p in proto.P )
				DumpFunction( p, proto.Source );
		}

		private void DumpUpvalues( LuaProto proto )
		{
			DumpInt( proto.Upvalues.Count );
			foreach( var upval in proto.Upvalues )
			{
				DumpByte( upval.InStack ? 1 : 0 );
				DumpByte( upval.Index );
				DumpByte( upval.Kind );
			}
		}

		private void DumpDebug( LuaProto proto )
		{
			int n = Strip ? 0 : proto.LineInfo.Count;
			DumpInt( n );
			if( n > 0 )
			{
				var bytes = new byte[n];
				for( int i=0; i<n; ++i )
					bytes[i] = (byte)proto.LineInfo[i];
				DumpBlock( bytes );
			}

			n = Strip ? 0 : proto.AbsLineInfo.Count;
			DumpInt( n );
			for( int i=0; i<n; ++i )
			{
				DumpInt( proto.AbsLineInfo[i].Pc );
				DumpInt( proto.AbsLineInfo[i].Line );
			}

			n = Strip ? 0 : proto.LocVars.Count;
			DumpInt( n );
			for( int i=0; i<n; ++i )
			{
				DumpString( proto.LocVars[i].VarName );
				DumpInt( proto.LocVars[i].StartPc );
				DumpInt( proto.LocVars[i].EndPc );
			}

			n = Strip ? 0 : proto.Upvalues.Count;
			DumpInt( n );
			for( int i=0; i<n; ++i )
				DumpString( proto.Upvalues[i].Name );
		}

		private void DumpFunction( LuaProto proto, string psource )
		{
			if( Strip || proto.Source == psource )
				DumpString( null ); // no debug info or same source as its parent
			else
				DumpString( proto.Source );
			DumpInt( proto.LineDefined );
			DumpInt( proto.LastLineDefined );
			DumpByte( proto.NumParams );
			DumpByte( proto.IsVarArg ? 1 : 0 );
			DumpByte( proto.MaxStackSize );
			DumpCode( proto );
			DumpConstants( proto );
			DumpUpvalues( proto );
			DumpProtos( proto );
			DumpDebug( proto );
		}

		private void DumpBlock( byte[] bytes )
		{
			if( Status == DumpStatus.OK && bytes.Length > 0 )
			{
				Status = Writer(bytes, 0, bytes.Length);
			}
		}
	}
}
