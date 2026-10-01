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

	// ldump.c: a function as the precompiled chunk of the reference Lua 5.3
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
		public const int LUAC_VERSION = 5 * 16 + 3;
		public const int LUAC_FORMAT = 0; // this is the official format
		public const long LUAC_INT = 0x5678;
		public const double LUAC_NUM = 370.5;

		// the sizes of int, size_t, Instruction, lua_Integer and lua_Number
		public const int SIZEOF_INT = 4;
		public const int SIZEOF_SIZET = 8;
		public const int SIZEOF_INSTRUCTION = 4;
		public const int SIZEOF_INTEGER = 8;
		public const int SIZEOF_NUMBER = 8;

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
			DumpByte( SIZEOF_INT );
			DumpByte( SIZEOF_SIZET );
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

		private void DumpInt( int value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		private void DumpInteger( long value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		private void DumpNumber( double value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		// a string, one byte per character: its size plus one, in a byte
		// below 0xFF or else after 0xFF as a size_t, then its bytes
		private void DumpString( string value )
		{
			if( value == null )
			{
				DumpByte( 0 );
				return;
			}

			long size = (long)value.Length + 1; // include trailing '\0'
			if( size < 0xFF )
				DumpByte( (int)size );
			else
			{
				DumpByte( 0xFF );
				DumpBlock( BitConverter.GetBytes( (ulong)size ) );
			}
			var bytes = new byte[value.Length];
			for( int i=0; i<value.Length; ++i )
				bytes[i] = (byte)value[i];
			DumpBlock( bytes ); // no need to save '\0'
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
				var t = k.V.Tt;
				DumpByte( t );
				switch( t )
				{
					case (int)LuaType.LUA_TNIL:
						break;
					case (int)LuaType.LUA_TBOOLEAN:
						DumpByte( k.V.BValue() ? 1 : 0 );
						break;
					case TValue.LUA_TNUMFLT:
						DumpNumber( k.V.FltValue );
						break;
					case TValue.LUA_TNUMINT:
						DumpInteger( k.V.IValue() );
						break;
					case (int)LuaType.LUA_TSTRING:
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
			}
		}

		private void DumpDebug( LuaProto proto )
		{
			int n = Strip ? 0 : proto.LineInfo.Count;
			DumpInt( n );
			for( int i=0; i<n; ++i )
				DumpInt( proto.LineInfo[i] );

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
