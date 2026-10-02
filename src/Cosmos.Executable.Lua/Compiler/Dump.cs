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

	// ldump.c: a function as the precompiled chunk of the reference Lua 5.5
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
			d.DumpFunction( proto );

			return d.Status;
		}

		private LuaWriter 	Writer;
		private bool		Strip;
		private DumpStatus	Status;
		private long		Offset;	// current position relative to beginning of dump
		// the strings already saved, with their indices (the strings are
		// equal by content, as the keys of the reference's table are)
		private readonly Dictionary<string, ulong> Strings = new Dictionary<string, ulong>();
		private ulong		NStr;	// counter for counting saved strings

		public const string LUAC_DATA = "\u0019\u0093\r\n\u001a\n";
		public const int LUAC_VERSION = 5 * 16 + 5;
		public const int LUAC_FORMAT = 0; // this is the official format
		public const int LUAC_INT = -0x5678;
		public const uint LUAC_INST = 0x12345678;
		public const double LUAC_NUM = -370.5;

		// the sizes of int, Instruction, lua_Integer and lua_Number
		public const int SIZEOF_INT = 4;
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
		public const int MAXSHORTLEN = 40;

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

		/*
		** Dump enough zeros to ensure that current position is a multiple of
		** 'align'.
		*/
		private void DumpAlign( int align )
		{
			int padding = align - (int)(Offset % align);
			if( padding < align ) // padding == align means no padding
				DumpBlock( new byte[padding] );
			Utl.Assert( Status != DumpStatus.OK || Offset % align == 0 );
		}

		private void DumpHeader()
		{
			DumpLiteral( LuaConf.LUA_SIGNATURE );
			DumpByte( LUAC_VERSION );
			DumpByte( LUAC_FORMAT );
			DumpLiteral( LUAC_DATA );
			DumpByte( SIZEOF_INT );
			DumpBlock( BitConverter.GetBytes( LUAC_INT ) );
			DumpByte( SIZEOF_INSTRUCTION );
			DumpBlock( BitConverter.GetBytes( LUAC_INST ) );
			DumpByte( SIZEOF_INTEGER );
			DumpBlock( BitConverter.GetBytes( (long)LUAC_INT ) );
			DumpByte( SIZEOF_NUMBER );
			DumpNumber( LUAC_NUM );
		}

		private void DumpByte( int value )
		{
			DumpBlock( new byte[] { (byte)value } );
		}

		/*
		** Dumps an unsigned integer using the MSB Varint encoding
		*/
		private void DumpVarint( ulong x )
		{
			var buff = new byte[10];
			int n = 1;
			buff[buff.Length - 1] = (byte)(x & 0x7f); // fill least-significant byte
			while( (x >>= 7) != 0 ) // fill other bytes in reverse order
				buff[buff.Length - (++n)] = (byte)((x & 0x7f) | 0x80);
			var bytes = new byte[n];
			Array.Copy( buff, buff.Length - n, bytes, 0, n );
			DumpBlock( bytes );
		}

		private void DumpSize( ulong sz )
		{
			DumpVarint( sz );
		}

		private void DumpInt( int x )
		{
			Utl.Assert( x >= 0 );
			DumpVarint( (ulong)x );
		}

		private void DumpNumber( double value )
		{
			DumpBlock( BitConverter.GetBytes( value ) );
		}

		/*
		** Signed integers are coded to keep small values small. (Coding -1 as
		** 0xfff...fff would use too many bytes to save a quite common value.)
		** A non-negative x is coded as 2x; a negative x is coded as -2x - 1.
		** (0 => 0; -1 => 1; 1 => 2; -2 => 3; 2 => 4; ...)
		*/
		private void DumpInteger( long x )
		{
			ulong cx = unchecked((x >= 0) ? 2u * (ulong)x : (2u * ~(ulong)x) + 1);
			DumpVarint( cx );
		}

		/*
		** Dump a String. First dump its "size":
		** size==0 is followed by an index and means "reuse saved string with
		** that index"; index==0 means NULL.
		** size>=1 is followed by the string contents with real size==size-1 and
		** means that string, which will be saved with the next available index.
		** The real size does not include the ending '\0' (which is not dumped),
		** so adding 1 to it cannot overflow a size_t.
		*/
		private void DumpString( string ts )
		{
			if( ts == null )
			{
				DumpVarint( 0 ); // will "reuse" NULL
				DumpVarint( 0 ); // special index for NULL
			}
			else
			{
				ulong idx;
				if( Strings.TryGetValue( ts, out idx ) ) // string already saved?
				{
					DumpVarint( 0 ); // reuse a saved string
					DumpVarint( idx ); // index of saved string
				}
				else // must write and save the string
				{
					DumpSize( (ulong)ts.Length + 1 );
					var bytes = new byte[ts.Length + 1]; // include ending '\0'
					for( int i=0; i<ts.Length; ++i )
						bytes[i] = (byte)ts[i];
					DumpBlock( bytes );
					NStr++; // one more saved string
					Strings.Add( ts, NStr ); // h[ts] = nstr
				}
			}
		}

		private void DumpCode( LuaProto proto )
		{
			DumpInt( proto.Code.Count );
			DumpAlign( SIZEOF_INSTRUCTION );
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
				DumpFunction( p );
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
			if( n > 0 )
			{
				/* 'abslineinfo' is an array of structures of int's */
				DumpAlign( SIZEOF_INT );
				for( int i=0; i<n; ++i )
				{
					DumpBlock( BitConverter.GetBytes( proto.AbsLineInfo[i].Pc ) );
					DumpBlock( BitConverter.GetBytes( proto.AbsLineInfo[i].Line ) );
				}
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

		private void DumpFunction( LuaProto proto )
		{
			DumpInt( proto.LineDefined );
			DumpInt( proto.LastLineDefined );
			DumpByte( proto.NumParams );
			DumpByte( proto.Flag );
			DumpByte( proto.MaxStackSize );
			DumpCode( proto );
			DumpConstants( proto );
			DumpUpvalues( proto );
			DumpProtos( proto );
			DumpString( Strip ? null : proto.Source );
			DumpDebug( proto );
		}

		private void DumpBlock( byte[] bytes )
		{
			if( Status == DumpStatus.OK ) // do not write anything after an error
			{
				if( bytes.Length > 0 )
					Status = Writer(bytes, 0, bytes.Length);
				Offset += bytes.Length;
			}
		}
	}
}
