// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Cosmos.Executable.Lua
{
	/*
	** lgc.c of Lua 5.5: the garbage collector, as far as a script sees it.
	**
	** The .NET collector (the kernel's, on Cosmos) owns the memory: it frees
	** an object once nothing references it. What it cannot do is what Lua
	** promises on top of that: weak tables, '__gc' finalizers called in
	** order with their objects resurrected, and a count of the memory in use
	** by the state. So the state runs the collector of Lua 5.5 over its own
	** objects, as the reference implementation does: it marks what a script
	** can reach from the registry, the main thread and the stacks, clears
	** the weak tables of what it did not reach, and finalizes what it did
	** not reach. Each cycle is a whole one ("stop the world"): the
	** incremental and generational modes, and their parameters, only pace
	** the cycles. Dropped references are then the .NET collector's to free.
	*/

	// What the collector of a state knows of the objects it tracks: the
	// collectable values of Lua (tables, closures, userdata, threads) and
	// the prototypes and upvalues closures hold
	internal abstract class LuaGCObject
	{
		internal uint GCMark;		// the cycle that last reached the object
		internal bool Finalizable;	// in the list of objects with a finalizer
	}

	internal enum LuaGCOption
	{
		LUA_GCSTOP		= 0,
		LUA_GCRESTART	= 1,
		LUA_GCCOLLECT	= 2,
		LUA_GCCOUNT		= 3,
		LUA_GCCOUNTB	= 4,
		LUA_GCSTEP		= 5,
		LUA_GCISRUNNING	= 6,
		LUA_GCGEN		= 7,
		LUA_GCINC		= 8,
		LUA_GCPARAM		= 9,
	}

	// the parameters of the collector (LUA_GCP*), in the order of 'gcparams'
	internal enum LuaGCParam
	{
		LUA_GCPMINORMUL		= 0,	// control minor collections
		LUA_GCPMAJORMINOR	= 1,	// control shift major->minor
		LUA_GCPMINORMAJOR	= 2,	// control shift minor->major
		LUA_GCPPAUSE		= 3,	// size of pause between successive GCs
		LUA_GCPSTEPMUL		= 4,	// GC "speed"
		LUA_GCPSTEPSIZE		= 5,	// GC granularity
	}

	// The part of the global state the collector keeps
	internal class GCState
	{
		// 'gcstp': why the collector is stopped, if it is
		public const int GCSTPUSR = 1;	// by the user
		public const int GCSTPGC = 2;	// by itself, while running (a finalizer)
		public const int GCSTPCLS = 4;	// while closing the state
		public int Stp;

		public bool Generational;
		// 'gcparams': the parameters, as floating-point bytes (luaO_codeparam)
		public byte[] Params =
		{
			CodeParam( 20 ),	// LUAI_GENMINORMUL
			CodeParam( 50 ),	// LUAI_MAJORMINOR
			CodeParam( 70 ),	// LUAI_MINORMAJOR
			CodeParam( 250 ),	// LUAI_GCPAUSE
			CodeParam( 200 ),	// LUAI_GCMUL
			CodeParam( 200 * 48 ),	// LUAI_GCSTEPSIZE: 200 * sizeof(Table)
		};

		// What the state uses, as Lua counts it: the estimate of the last
		// cycle and what was allocated since. A cycle starts once it
		// reaches the threshold.
		public long TotalBytes;
		public long Threshold;

		public uint Epoch;	// the current cycle, which marks what it reaches

		// 'finobj': the objects with a finalizer, oldest first
		public List<LuaGCObject> FinObj = new List<LuaGCObject>();
		// 'tobefnz': those to finalize, in the order of their calls
		public Queue<LuaGCObject> ToBeFnz = new Queue<LuaGCObject>();

		// the work lists of a cycle
		public List<LuaGCObject> Gray = new List<LuaGCObject>();
		public List<LuaTable> Weak = new List<LuaTable>();		// weak values
		public List<LuaTable> Ephemeron = new List<LuaTable>();	// weak keys
		public List<LuaTable> AllWeak = new List<LuaTable>();	// both
		public long MarkedBytes;
		// the long strings the cycle reached, each an object of its own
		public HashSet<string> LongStrings = new HashSet<string>( new StringIdentity() );

		/*
		** luaO_codeparam: encodes 'p'% as a floating-point byte, represented
		** as (eeeexxxx). The exponent is represented using excess-7.
		** Mimicking IEEE 754, the representation normalizes the number when
		** possible, assuming an extra 1 before the mantissa (xxxx) and adding
		** one to the exponent (eeee) to signal that. So, the real value is
		** (1xxxx) * 2^(eeee - 7 - 1) if eeee != 0, and (xxxx) * 2^-7
		** otherwise (subnormal numbers).
		*/
		public static byte CodeParam( uint p )
		{
			if( p >= ((ulong)0x1F << (0xF - 7 - 1)) * 100u ) // overflow?
				return 0xFF; // return maximum value
			else
			{
				p = (uint)(((ulong)p * 128 + 99) / 100); // round up the division
				if( p < 0x10 ) // subnormal number?
				{
					/* exponent bits are already zero; nothing else to do */
					return (byte)p;
				}
				else // p >= 0x10 implies ceil(log2(p + 1)) >= 5
				{
					/* preserve 5 bits in 'p' */
					int log = LuaTable.CeilLog2( (int)p + 1 ) - 5;
					return (byte)(((p >> log) - 0x10) | (uint)((log + 1) << 4));
				}
			}
		}

		/*
		** luaO_applyparam: computes 'p' times 'x', where 'p' is a
		** floating-point byte. Roughly, we have to multiply 'x' by the mantissa
		** and then shift accordingly to the exponent. If the exponent is
		** positive, both the multiplication and the shift increase 'x', so we
		** have to care only about overflows. For negative exponents, however,
		** multiplying before the shift keeps more significant bits, as long as
		** the multiplication does not overflow, so we check which order is
		** best.
		*/
		public static long ApplyParam( byte p, long x )
		{
			int m = p & 0xF; // mantissa
			int e = (p >> 4); // exponent
			if( e > 0 ) // normalized?
			{
				e--; // correct exponent
				m += 0x10; // correct mantissa; maximum value is 0x1F
			}
			e -= 7; // correct excess-7
			if( e >= 0 )
			{
				if( x < (long.MaxValue / 0x1F) >> e ) // no overflow?
					return (x * m) << e; // order doesn't matter here
				else // real overflow
					return long.MaxValue;
			}
			else // negative exponent
			{
				e = -e;
				if( x < long.MaxValue / 0x1F ) // multiplication cannot overflow?
					return (x * m) >> e; // multiplying first gives more precision
				else if( (x >> e) < long.MaxValue / 0x1F ) // cannot overflow after shift?
					return (x >> e) * m;
				else // real overflow
					return long.MaxValue;
			}
		}

		public long Apply( LuaGCParam p, long x ) { return ApplyParam( Params[(int)p], x ); }

		// Strings by identity. Not ReferenceEqualityComparer, an
		// IEqualityComparer<object> that would take variant interface
		// dispatch, which Cosmos's runtime does not resolve.
		private sealed class StringIdentity : IEqualityComparer<string>
		{
			public bool Equals( string a, string b ) { return ReferenceEquals( a, b ); }
			public int GetHashCode( string s ) { return RuntimeHelpers.GetHashCode( s ); }
		}
	}

	// The sizes Lua 5.4 gave its objects on a 64-bit machine, which the
	// count of the memory in use adds up
	internal static class LuaGCSize
	{
		public const int TValue = 16;
		public const int Table = 56;
		public const int Node = 32;
		public const int LClosure = 32;
		public const int CClosure = 32;
		public const int UpVal = 40;
		public const int Udata = 40;
		public const int Thread = 200;
		public const int Proto = 128;
		public const int String = 24;

		// strings up to this size are interned: one copy, not counted per use
		public const int MaxShortLen = 40;

		public static long OfString( string s )
		{
			return String + s.Length + 1;
		}
	}

	internal partial class LuaTable
	{
		// What the table weighs, as Lua counts it
		internal long GCBytes()
		{
			long size = LuaGCSize.Table + (long)LuaGCSize.TValue * ArrayPart.Length;
			if( HashPart != DummyHashPart )
				size += (long)LuaGCSize.Node * HashPart.Length;
			return size;
		}

		// traversestrongtable
		internal void GCTraverseStrong( LuaState L )
		{
			for( int i=0; i<ArrayPart.Length; ++i )
				L.C_MarkValue( ref ArrayPart[i].V );
			for( int i=0; i<HashPart.Length; ++i )
			{
				var n = HashPart[i];
				if( !n.Val.V.TtIsNil() )
				{
					L.C_MarkValue( ref n.Key.V );
					L.C_MarkValue( ref n.Val.V );
				}
			}
		}

		// traverseweakvalue: the keys are strong
		internal void GCTraverseWeakValue( LuaState L )
		{
			for( int i=0; i<HashPart.Length; ++i )
			{
				var n = HashPart[i];
				if( !n.Val.V.TtIsNil() )
					L.C_MarkValue( ref n.Key.V );
			}
		}

		// traverseephemeron: a value is strong while its key is; returns
		// whether it marked any value
		internal bool GCTraverseEphemeron( LuaState L )
		{
			bool marked = false;
			// the keys of the array part are integers: strong
			for( int i=0; i<ArrayPart.Length; ++i )
			{
				if( L.C_IsWhite( ref ArrayPart[i].V ) )
				{
					marked = true;
					L.C_MarkValue( ref ArrayPart[i].V );
				}
			}
			for( int i=0; i<HashPart.Length; ++i )
			{
				var n = HashPart[i];
				if( !n.Val.V.TtIsNil() && !L.C_IsCleared( ref n.Key.V )
					&& L.C_IsWhite( ref n.Val.V ) )
				{
					marked = true;
					L.C_MarkValue( ref n.Val.V );
				}
			}
			return marked;
		}

		// clearbykeys: removes the entries whose keys were not reached
		internal void GCClearByKeys( LuaState L )
		{
			for( int i=0; i<HashPart.Length; ++i )
			{
				var n = HashPart[i];
				if( L.C_IsCleared( ref n.Key.V ) ) // unmarked key?
					n.Val.V.SetNilValue(); // remove entry
				if( n.Val.V.TtIsNil() ) // is entry empty?
					ClearKey( L, n ); // clear its key
			}
		}

		// clearbyvalues: removes the entries whose values were not reached
		internal void GCClearByValues( LuaState L )
		{
			for( int i=0; i<ArrayPart.Length; ++i )
			{
				if( L.C_IsCleared( ref ArrayPart[i].V ) )
					ArrayPart[i].V.SetNilValue(); // remove value
			}
			for( int i=0; i<HashPart.Length; ++i )
			{
				var n = HashPart[i];
				if( L.C_IsCleared( ref n.Val.V ) ) // unmarked value?
					n.Val.V.SetNilValue(); // remove entry
				if( n.Val.V.TtIsNil() ) // is entry empty?
					ClearKey( L, n ); // clear its key
			}
		}

		/*
		** clearkey: the collectable key of an empty entry dies. The entry
		** stays in its chain, and its key matches no lookup, but 'next' can
		** still go on from it (a traversal that removed the entry holds it),
		** so the key is let go only when the script cannot reach it.
		*/
		private static void ClearKey( LuaState L, HNode n )
		{
			ref TValue k = ref n.Key.V;
			if( k.Tt == LuaState.LUA_TDEADKEY || k.OValue == null || !IsCollectable( ref k ) )
				return;
			if( L.C_IsWhite( ref k ) )
				k.OValue = null;
			k.Tt = LuaState.LUA_TDEADKEY;
		}

		private static bool IsCollectable( ref TValue k )
		{
			switch( k.Tt )
			{
				case (int)LuaType.LUA_TSTRING:
				case (int)LuaType.LUA_TTABLE:
				case (int)LuaType.LUA_TFUNCTION:
				case (int)LuaType.LUA_TUSERDATA:
				case (int)LuaType.LUA_TTHREAD:
					return true;
				default:
					return false;
			}
		}

		// whether the dead key of a node is the key 'next' was given
		internal static bool IsDeadKeyOf( ref TValue dead, ref TValue key )
		{
			if( dead.OValue == null )
				return false;
			if( key.TtIsString() )
				return dead.OValue is string s && s == key.SValue();
			return TValue.SameObject( dead.OValue, key.OValue );
		}
	}

	internal partial class LuaState : LuaGCObject
	{
		// the tag of a dead key, which no value has
		internal const int LUA_TDEADKEY = (int)LuaType.LUA_TDEADKEY;

		// luaC_checkGC: a cycle when the memory in use reached the threshold
		internal void C_CheckGC()
		{
			if( G.GC.TotalBytes > G.GC.Threshold )
				C_Step();
		}

		// counts 'n' more bytes in use
		internal void C_Alloc( long n )
		{
			G.GC.TotalBytes += n;
		}

		internal void C_AllocString( string s )
		{
			G.GC.TotalBytes += LuaGCSize.OfString( s );
		}

		// luaC_step
		private void C_Step()
		{
			var gc = G.GC;
			if( gc.Stp != 0 ) // not running?
			{
				gc.Threshold = gc.TotalBytes + 2000; // avoid being called too often
				return;
			}
			C_FullCycle();
			C_CallAllPendingFinalizers();
		}

		// luaC_fullgc
		internal void C_FullGC()
		{
			C_FullCycle();
			C_CallAllPendingFinalizers();
		}

		// setpause: the next cycle starts when the memory in use reaches
		// 'pause' percent of what this one left, and, as each cycle is a
		// whole one, not before the bytes of a step
		private void C_SetPause( long estimate )
		{
			var gc = G.GC;
			long threshold = gc.Apply( LuaGCParam.LUA_GCPPAUSE, estimate );
			long step = estimate + gc.Apply( LuaGCParam.LUA_GCPSTEPSIZE, 100 );
			gc.Threshold = System.Math.Max( threshold, step );
		}

		/*
		** {======================================================
		** Mark functions
		** =======================================================
		*/

		// the value is a collectable object this cycle did not reach yet
		internal bool C_IsWhite( ref TValue v )
		{
			var o = GCObjectOf( ref v );
			return o != null && o.GCMark != G.GC.Epoch;
		}

		/*
		** iscleared: whether a value of a weak table goes. Strings are
		** values, never weak; so are the values that are not collectable,
		** as light C# functions.
		*/
		internal bool C_IsCleared( ref TValue v )
		{
			if( v.Tt == (int)LuaType.LUA_TSTRING )
			{
				C_MarkValue( ref v ); // strings are 'values', so are never weak
				return false;
			}
			return C_IsWhite( ref v );
		}

		private static LuaGCObject GCObjectOf( ref TValue v )
		{
			switch( v.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
				case (int)LuaType.LUA_TUSERDATA:
				case (int)LuaType.LUA_TTHREAD:
					return (LuaGCObject)v.OValue;
				case (int)LuaType.LUA_TFUNCTION:
				{
					if( v.ClIsLcsClosure() )
						return null;
					var cs = v.OValue as LuaCsClosureValue;
					if( cs != null && cs.IsLight )
						return null;
					return (LuaGCObject)v.OValue;
				}
				default:
					return null;
			}
		}

		internal void C_MarkValue( ref TValue v )
		{
			if( v.Tt == (int)LuaType.LUA_TSTRING )
			{
				// a long string is its own object; the short ones are
				// interned, and counted where they are made
				string s = v.SValue();
				if( s.Length > LuaGCSize.MaxShortLen && G.GC.LongStrings.Add( s ) )
					G.GC.MarkedBytes += LuaGCSize.OfString( s );
				return;
			}
			var o = GCObjectOf( ref v );
			if( o != null )
				C_MarkObject( o );
		}

		private void C_MarkObject( LuaGCObject o )
		{
			if( o == null || o.GCMark == G.GC.Epoch )
				return;
			o.GCMark = G.GC.Epoch;
			G.GC.Gray.Add( o );
		}

		// traverses the gray objects until there are no more
		private void C_PropagateAll()
		{
			var gray = G.GC.Gray;
			while( gray.Count > 0 )
			{
				var o = gray[gray.Count - 1];
				gray.RemoveAt( gray.Count - 1 );
				long size = C_Traverse( o ); // (which counts the long strings it marks)
				G.GC.MarkedBytes += size;
			}
		}

		// propagatemark: marks what 'o' references, and returns its size
		private long C_Traverse( LuaGCObject o )
		{
			var t = o as LuaTable;
			if( t != null )
				return C_TraverseTable( t );

			var lcl = o as LuaLClosureValue;
			if( lcl != null )
			{
				C_MarkObject( lcl.Proto );
				for( int i=0; i<lcl.Upvals.Length; ++i )
					C_MarkObject( lcl.Upvals[i] );
				return LuaGCSize.LClosure + 8L * lcl.Upvals.Length;
			}

			var uv = o as LuaUpvalue;
			if( uv != null )
			{
				// open or closed, the value is the one the closures see
				C_MarkValue( ref uv.V.V );
				return LuaGCSize.UpVal;
			}

			var ccl = o as LuaCsClosureValue;
			if( ccl != null )
			{
				int n = (ccl.Upvals != null) ? ccl.Upvals.Length : 0;
				for( int i=0; i<n; ++i )
					C_MarkValue( ref ccl.Upvals[i].V );
				return LuaGCSize.CClosure + (long)LuaGCSize.TValue * n;
			}

			var u = o as LuaUserDataValue;
			if( u != null )
			{
				C_MarkObject( u.MetaTable );
				for( int i=0; i<u.UserValues.Length; ++i )
					C_MarkValue( ref u.UserValues[i] );
				return LuaGCSize.Udata + (long)LuaGCSize.TValue * u.UserValues.Length + u.Length;
			}

			var th = o as LuaState;
			if( th != null )
				return C_TraverseThread( th );

			var p = o as LuaProto;
			if( p != null )
			{
				for( int i=0; i<p.K.Count; ++i )
					C_MarkValue( ref p.K[i].V );
				for( int i=0; i<p.P.Count; ++i )
					C_MarkObject( p.P[i] );
				return LuaGCSize.Proto + 4L * p.Code.Count + (long)LuaGCSize.TValue * p.K.Count
					+ 8L * p.P.Count + p.LineInfo.Count;
			}

			return 0;
		}

		// traversetable: by the weakness its '__mode' gives it
		private long C_TraverseTable( LuaTable h )
		{
			var gc = G.GC;
			C_MarkObject( h.MetaTable );

			bool weakkey = false;
			bool weakvalue = false;
			if( h.MetaTable != null )
			{
				var mode = h.MetaTable.GetStr( GetTagMethodName( TMS.TM_MODE ) );
				if( mode.V.TtIsString() && mode.V.SValue().Length <= LuaGCSize.MaxShortLen )
				{
					string s = mode.V.SValue();
					weakkey = s.IndexOf( 'k' ) >= 0;
					weakvalue = s.IndexOf( 'v' ) >= 0;
				}
			}

			if( !weakkey && !weakvalue ) // not weak
				h.GCTraverseStrong( this );
			else if( !weakkey ) // strong keys?
			{
				h.GCTraverseWeakValue( this );
				gc.Weak.Add( h );
			}
			else if( !weakvalue ) // strong values?
			{
				h.GCTraverseEphemeron( this );
				gc.Ephemeron.Add( h );
			}
			else // all weak: nothing to traverse now
				gc.AllWeak.Add( h );

			return h.GCBytes();
		}

		/*
		** traversethread: the live part of the stack, below its top; what
		** lies above is dead, cleared so that .NET can free it.
		*/
		private long C_TraverseThread( LuaState th )
		{
			int top = th.Top.Index;
			for( int i=0; i<top; ++i )
				C_MarkValue( ref th.Stack[i].V );
			for( int i=top; i<th.Stack.Length; ++i )
				th.Stack[i].V.SetNilValue(); // clear dead stack slice
			return LuaGCSize.Thread + (long)LuaGCSize.TValue * th.Stack.Length;
		}

		/*
		** Traverse all ephemeron tables propagating marks from keys to values,
		** until no more values are marked.
		*/
		private void C_ConvergeEphemerons()
		{
			var ephemeron = G.GC.Ephemeron;
			bool changed;
			do
			{
				changed = false;
				for( int i=0; i<ephemeron.Count; ++i ) // (the list may grow)
				{
					if( ephemeron[i].GCTraverseEphemeron( this ) )
					{
						C_PropagateAll();
						changed = true;
					}
				}
			} while( changed );
		}

		/* }====================================================== */

		// markroot: the main thread, the registry, the metatables of the
		// basic types, and what is left to finalize from the last cycle
		private void C_MarkRoots()
		{
			C_MarkObject( G.MainThread );
			C_MarkObject( this ); // the running thread
			C_MarkValue( ref G.Registry.V );
			for( int i=0; i<G.MetaTables.Length; ++i )
				C_MarkObject( G.MetaTables[i] );
			C_MarkBeingFnz();
		}

		// markbeingfnz: what is to be finalized lives until it is
		private void C_MarkBeingFnz()
		{
			foreach( var o in G.GC.ToBeFnz )
				C_MarkObject( o );
		}

		/*
		** separatetobefnz: moves the objects with a finalizer the cycle did
		** not reach (all of them, when closing the state) to the list of
		** those to finalize, the newest first.
		*/
		private void C_SeparateToBeFnz( bool all )
		{
			var gc = G.GC;
			var kept = new List<LuaGCObject>( gc.FinObj.Count );
			for( int i=gc.FinObj.Count - 1; i>=0; --i )
			{
				var o = gc.FinObj[i];
				if( all || o.GCMark != gc.Epoch )
					gc.ToBeFnz.Enqueue( o );
				else
					kept.Add( o );
			}
			kept.Reverse();
			gc.FinObj = kept;
		}

		/*
		** The atomic phase of a cycle, which is all of it here: marks what
		** a script can reach, clears the weak tables, and separates the
		** objects to finalize, which live until their finalizers ran.
		*/
		private void C_FullCycle()
		{
			var gc = G.GC;
			gc.Epoch++;
			gc.MarkedBytes = 0;
			gc.Gray.Clear();
			gc.Weak.Clear();
			gc.Ephemeron.Clear();
			gc.AllWeak.Clear();
			gc.LongStrings.Clear();

			C_MarkRoots();
			C_PropagateAll();
			C_ConvergeEphemerons();
			// at this point, all strongly accessible objects are marked.
			// Clear values from weak tables, before checking finalizers
			int origWeak = gc.Weak.Count;
			int origAll = gc.AllWeak.Count;
			for( int i=0; i<origWeak; ++i )
				gc.Weak[i].GCClearByValues( this );
			for( int i=0; i<origAll; ++i )
				gc.AllWeak[i].GCClearByValues( this );
			C_SeparateToBeFnz( false ); // separate objects to be finalized
			C_MarkBeingFnz(); // mark objects that will be finalized
			C_PropagateAll(); // remark, to propagate 'resurrection'
			C_ConvergeEphemerons();
			// at this point, all resurrected objects are marked.
			// remove dead objects from weak tables
			for( int i=0; i<gc.Ephemeron.Count; ++i )
				gc.Ephemeron[i].GCClearByKeys( this );
			for( int i=0; i<gc.AllWeak.Count; ++i )
				gc.AllWeak[i].GCClearByKeys( this );
			// clear values from resurrected weak tables
			for( int i=origWeak; i<gc.Weak.Count; ++i )
				gc.Weak[i].GCClearByValues( this );
			for( int i=origAll; i<gc.AllWeak.Count; ++i )
				gc.AllWeak[i].GCClearByValues( this );

			gc.Weak.Clear();
			gc.Ephemeron.Clear();
			gc.AllWeak.Clear();
			gc.LongStrings.Clear();

			gc.TotalBytes = gc.MarkedBytes;
			C_SetPause( gc.MarkedBytes );
		}

		/*
		** {======================================================
		** Finalization
		** =======================================================
		*/

		/*
		** luaC_checkfinalizer: an object whose metatable has a '__gc' field,
		** when it gets it, is finalized once the script cannot reach it
		*/
		internal void C_CheckFinalizer( LuaGCObject o, LuaTable mt )
		{
			var gc = G.GC;
			if( o.Finalizable || // obj. is already marked...
				mt.GetStr( GetTagMethodName( TMS.TM_GC ) ).V.TtIsNil() || // or has no finalizer...
				(gc.Stp & GCState.GCSTPCLS) != 0 ) // or closing state?
				return; // nothing to be done
			o.Finalizable = true;
			gc.FinObj.Add( o );
		}

		private static void DoTheCall( ref LuaState L )
		{
			L.D_CallNoYield( L.Stack[L.Top.Index - 2], 0 );
		}
		private static PFuncDelegate<LuaState> DG_DoTheCall = DoTheCall;

		// GCTM: calls the finalizer of the next object to finalize
		private void C_GCTM()
		{
			var gc = G.GC;
			var o = gc.ToBeFnz.Dequeue();
			o.Finalizable = false; // object is "normal" again

			var v = new TValue();
			var u = o as LuaUserDataValue;
			if( u != null )
				v.SetUValue( u );
			else
				v.SetHValue( (LuaTable)o );

			var tm = T_GetTMByObj( ref v, TMS.TM_GC );
			if( tm.V.TtIsNil() ) // no finalizer any more?
				return;

			bool oldah = AllowHook;
			int oldgcstp = gc.Stp;
			gc.Stp |= GCState.GCSTPGC; // avoid GC steps
			AllowHook = false; // stop debug hooks during GC metamethod
			Top.V.SetObj( ref tm.V ); // push finalizer...
			Stack[Top.Index + 1].V.SetObj( ref v ); // ... and its argument
			Top = Stack[Top.Index + 2];
			CI.CallStatus |= CallStatus.CIST_FIN; // will run a finalizer
			var L = this;
			var status = D_PCall( DG_DoTheCall, ref L, Top.Index - 2, 0 );
			CI.CallStatus &= ~CallStatus.CIST_FIN; // not running a finalizer anymore
			AllowHook = oldah; // restore hooks
			gc.Stp = oldgcstp; // restore state
			if( status != ThreadStatus.LUA_OK ) // error while running __gc?
			{
				E_WarnError( "__gc" );
				Top = Stack[Top.Index - 1]; // pops error object
			}
		}

		private void C_CallAllPendingFinalizers()
		{
			while( G.GC.ToBeFnz.Count > 0 )
				C_GCTM();
		}

		// luaE_warnerror: a warning of an error in a metamethod 'where'
		private void E_WarnError( string where )
		{
			var errobj = Stack[Top.Index - 1];
			string msg = errobj.V.TtIsString()
				? errobj.V.SValue()
				: "error object is not a string";
			// produce warning "error in %s (%s)" (where, msg)
			E_Warning( "error in ", true );
			E_Warning( where, true );
			E_Warning( " metamethod (", true );
			E_Warning( msg, true );
			E_Warning( ")", false );
		}

		/*
		** luaC_freeallobjects, as lua_close calls it: finalizes every object
		** with a finalizer, reached or not, and finalizes no new one.
		*/
		internal void C_FreeAllObjects()
		{
			var gc = G.GC;
			gc.Stp = GCState.GCSTPCLS; // no extra finalizers after here
			C_SeparateToBeFnz( true ); // separate all objects with finalizers
			C_CallAllPendingFinalizers();
		}

		/* }====================================================== */

		// lua_gc: -1 when the collector cannot be asked (inside a finalizer,
		// or closing)
		internal int C_GC( LuaGCOption what, long a = 0, int b = 0 )
		{
			var gc = G.GC;
			if( (gc.Stp & (GCState.GCSTPGC | GCState.GCSTPCLS)) != 0 ) // internal stop?
				return -1; // all options are invalid when stopped
			switch( what )
			{
				case LuaGCOption.LUA_GCSTOP:
					gc.Stp = GCState.GCSTPUSR; // stopped by the user
					return 0;
				case LuaGCOption.LUA_GCRESTART:
					gc.Threshold = gc.TotalBytes;
					gc.Stp = 0;
					return 0;
				case LuaGCOption.LUA_GCCOLLECT:
					C_FullGC();
					// and the .NET collector frees what the cycle let go
					System.GC.Collect();
					return 0;
				case LuaGCOption.LUA_GCCOUNT:
					// GC values are expressed in Kbytes: #bytes/2^10
					return (int)System.Math.Min( gc.TotalBytes >> 10, int.MaxValue );
				case LuaGCOption.LUA_GCCOUNTB:
					return (int)(gc.TotalBytes & 0x3ff);
				case LuaGCOption.LUA_GCSTEP:
				{
					// a step runs a whole cycle, once the debt (what is left
					// before the next cycle) minus the 'a' bytes it adds is due,
					// and at once for a basic step
					long debt = gc.Threshold - gc.TotalBytes;
					long newdebt = (a <= 0) ? 0 : debt - a;
					if( newdebt > 0 )
					{
						gc.Threshold = gc.TotalBytes + newdebt;
						return 0;
					}
					int oldstp = gc.Stp;
					gc.Stp = 0; // allow GC to run (other bits must be zero here)
					C_FullGC();
					gc.Stp = oldstp; // restore previous state
					return 1; // end of cycle
				}
				case LuaGCOption.LUA_GCISRUNNING:
					return gc.Stp == 0 ? 1 : 0;
				case LuaGCOption.LUA_GCGEN:
				{
					int res = gc.Generational ? (int)LuaGCOption.LUA_GCGEN : (int)LuaGCOption.LUA_GCINC;
					gc.Generational = true;
					return res;
				}
				case LuaGCOption.LUA_GCINC:
				{
					int res = gc.Generational ? (int)LuaGCOption.LUA_GCGEN : (int)LuaGCOption.LUA_GCINC;
					gc.Generational = false;
					return res;
				}
				case LuaGCOption.LUA_GCPARAM:
				{
					int param = (int)a;
					int value = b;
					Utl.ApiCheck( 0 <= param && param < gc.Params.Length, "invalid parameter" );
					int res = (int)GCState.ApplyParam( gc.Params[param], 100 );
					if( value >= 0 )
						gc.Params[param] = GCState.CodeParam( (uint)value );
					return res;
				}
				default:
					return -1; // invalid option
			}
		}
	}
}
