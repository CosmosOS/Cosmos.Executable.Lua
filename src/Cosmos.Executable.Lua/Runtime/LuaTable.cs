// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


// #define DEBUG_DUMMY_TVALUE_MODIFY

using System;
using System.Collections.Generic;

namespace Cosmos.Executable.Lua
{

	internal partial class LuaTable : LuaGCObject {
		public LuaTable MetaTable;
		public uint NoTagMethodFlags;

		public LuaTable(LuaState l) {
			InitLuaTable(l);
		}

		public StkId Get(ref TValue key)
		{
			switch(key.Tt) {
				case (int)LuaType.LUA_TNIL: return TheNilValue;
				case (int)LuaType.LUA_TSTRING: return GetStr(key.SValue());
				case TValue.LUA_TNUMINT: return GetInt(key.IValue());
				case TValue.LUA_TNUMFLT: {
					long k;
					if(LuaState.FloatToInteger(key.FltValue, out k, 0)) // index is an integral float?
						{ return GetInt(k); } // use specialized version
					break;
				}
			}

			var h = key.GetHashCode();
			for(var node = GetHashNode(h); node != null; node = node.Next) {
				if(node.Key.V == key) {
					{ return node.Val; }
				}
			}

			return TheNilValue;
		}

		public StkId GetInt(long key)
		{
			// (1 <= key && key <= ArrayPart.Length)
			if(unchecked((ulong)key - 1UL) < (ulong)ArrayPart.Length)
				{ return ArrayPart[key-1]; }

			for(var node = GetHashNode(IntHash(key)); node != null; node = node.Next) {
				if(node.Key.V.TtIsInteger() && node.Key.V.IValue() == key) {
					return node.Val;
				}
			}

			return TheNilValue;
		}

		public StkId GetStr(string key)
		{
			var h = key.GetHashCode();
			for(var node = GetHashNode(h); node != null; node = node.Next) {
				if(node.Key.V.TtIsString() && node.Key.V.SValue() == key)
					{ return node.Val; }
			}

			return TheNilValue;
		}

		public void Set(ref TValue key, ref TValue val)
		{
			if(key.TtIsString())
				{ NoTagMethodFlags = 0; } // a metamethod may come: forget the misses
			var cell = Get(ref key);
			if(cell == TheNilValue) {
				cell = NewTableKey(ref key);
			}
			cell.V.SetObj(ref val);
		}

		public void SetInt(long key, ref TValue val)
		{
			var cell = GetInt(key);
			if(cell == TheNilValue) {
				var k = new TValue();
				k.SetIValue(key);
				cell = NewTableKey(ref k);
			}
			cell.V.SetObj(ref val);
		}

		/*
		** returns the index of a `key' for table traversals. First goes all
		** elements in the array part, then elements in the hash part. The
		** beginning of a traversal is signaled by -1.
		*/
		private int FindIndex(StkId key)
		{
			if(key.V.TtIsNil())
				{ return -1; }

			// is `key' inside array part?
			int i = ArrayIndex(ref key.V);
			if(0 < i && i <= ArrayPart.Length)
				{ return i-1; }

			var n = GetHashNode(ref key.V);
			// check whether `key' is somewhere in the chain
			for(;;) {
				if(L.V_RawEqualObj(ref n.Key.V, ref key.V) ||
					(n.Key.V.Tt == LuaState.LUA_TDEADKEY && IsDeadKeyOf(ref n.Key.V, ref key.V)))
					{ return ArrayPart.Length + n.Index; }
				n = n.Next;

				// key not found
				if(n == null) { L.G_RunError("invalid key to 'next'"); }
			}
		}

		public bool Next(StkId key, StkId val)
		{
			// find original element
			int i = FindIndex(key);

			// try first array part
			for(i++; i<ArrayPart.Length; ++i) {
				if(!ArrayPart[i].V.TtIsNil()) {
					key.V.SetIValue(i+1);
					val.V.SetObj(ref ArrayPart[i].V);
					return true;
				}
			}

			// then hash part
			for(i-=ArrayPart.Length; i<HashPart.Length; ++i) {
				if(!HashPart[i].Val.V.TtIsNil()) {
					key.V.SetObj(ref HashPart[i].Key.V);
					val.V.SetObj(ref HashPart[i].Val.V);
					return true;
				}
			}
			return false;
		}

		public int Length
		{ get {
			uint j = (uint)ArrayPart.Length;
			if(j > 0 && ArrayPart[j-1].V.TtIsNil()) {
				/* there is a boundary in the array part: (binary) search for it */
				uint i = 0;
				while(j - i > 1) {
					uint m = (i+j)/2;
					if(ArrayPart[m-1].V.TtIsNil()) { j = m; }
					else { i = m; }
				}
				return (int)i;
			}
			/* else must find a boundary in hash part */
			else if(HashPart == DummyHashPart)
				return (int)j;
			else return UnboundSearch(j);
		} }

		// luaH_realasize: the size of the array part
		public int ArraySize
		{
			get { return ArrayPart.Length; }
		}

		// luaH_resizearray: a new size for the array part, the hash part kept
		public void ResizeArray(int nasize)
		{
			Resize(nasize, HashPart == DummyHashPart ? 0 : HashPart.Length);
		}

		public void Resize(int nasize, int nhsize)
		{
			int oasize = ArrayPart.Length;
			var oldHashPart = HashPart;
			if(nasize > oasize) // array part must grow?
				SetArraryVector(nasize);

			// create new hash part with appropriate size
			SetNodeVector(nhsize);

			// array part must shrink?
			if(nasize < oasize) {
				var oldArrayPart = ArrayPart;
				ArrayPart = DummyArrayPart;
				// re-insert elements from vanishing slice
				for(int i=nasize; i<oasize; ++i) {
					if(!oldArrayPart[i].V.TtIsNil())
						{ SetInt(i+1, ref oldArrayPart[i].V); }
				}
				// shrink array
				L.C_Alloc((long)LuaGCSize.TValue * (nasize - oasize));
				var newArrayPart = new StkId[nasize];
				for(int i=0; i<nasize; ++i) {
					newArrayPart[i] = oldArrayPart[i];
				}
				ArrayPart = newArrayPart;
			}

			// re-insert elements from hash part
			for(int i=0; i<oldHashPart.Length; ++i) {
				var node = oldHashPart[i];
				if(!node.Val.V.TtIsNil()) {
					Set(ref node.Key.V, ref node.Val.V);
				}
			}
		}

		//-----------------------------------------
		//
		// **** PRIVATE below ****
		//
		//-----------------------------------------

		private class HNode
		{
			public int Index;
			public StkId Key;
			public StkId Val;
			public HNode Next;

			public void CopyFrom(HNode o)
			{
				Key.V.SetObj(ref o.Key.V);
				Val.V.SetObj(ref o.Val.V);
				Next = o.Next;
			}
		}

		private LuaState L;

		private StkId[] ArrayPart;
		private HNode[] HashPart;
		private int LastFree;

		private static StkId TheNilValue;
		private static StkId[] DummyArrayPart;
		private static HNode DummyNode;
		private static HNode[] DummyHashPart;

		private const int MAXBITS = 30;
		private const int MAXASIZE = (1 << MAXBITS);

		static LuaTable()
		{
			TheNilValue = new StkId();
			TheNilValue.V.SetNilValue();
#if DEBUG_DUMMY_TVALUE_MODIFY
			TheNilValue.V.Lock_ = true;
#endif

			DummyArrayPart = new StkId[0];

			DummyNode = new HNode();
			DummyNode.Key = TheNilValue;
			DummyNode.Val = TheNilValue;
			DummyNode.Next = null;

			DummyHashPart = new HNode[1];
			DummyHashPart[0] = DummyNode;
			DummyHashPart[0].Index = 0;
		}

		// UniLua recycled the nodes of a resized or finalized table into a
		// cache shared by every state. Gone: a finalizer on every table, and a
		// cache that only grows, are too much for a kernel's heap and GC.
		private HNode NewHNode()
		{
			HNode ret = new HNode();
			ret.Key = new StkId();
			ret.Val = new StkId();
			return ret;
		}

		private void InitLuaTable(LuaState lua)
		{
			L = lua;
			L.C_Alloc(LuaGCSize.Table);
			ArrayPart = DummyArrayPart;
			SetNodeVector(0);
		}

		private static int IntHash(long key)
		{
			return unchecked((int)(key ^ (key >> 32)));
		}

		private HNode GetHashNode(int hashcode)
		{
			uint n = (uint)hashcode;
			return HashPart[n % HashPart.Length];
		}

		// mainposition: the node a key hashes to, an integral float key
		// having been made an integer
		private HNode GetHashNode(ref TValue v)
		{
			if(v.TtIsInteger()) { return GetHashNode(IntHash(v.IValue())); }

			if(v.TtIsString()) { return GetHashNode(v.SValue().GetHashCode()); }

			return GetHashNode(v.GetHashCode());
		}

		private void SetArraryVector(int size)
		{
			Utl.Assert(size >= ArrayPart.Length);

			L.C_Alloc((long)LuaGCSize.TValue * (size - ArrayPart.Length));
			var newArrayPart = new StkId[size];
			int i = 0;
			for( ; i<ArrayPart.Length; ++i) {
				newArrayPart[i] = ArrayPart[i];
			}
			for( ; i<size; ++i) {
				newArrayPart[i] = new StkId();
				newArrayPart[i].V.SetNilValue();
			}
			ArrayPart = newArrayPart;
		}

		private void SetNodeVector(int size)
		{
			if(HashPart != null && HashPart != DummyHashPart)
				L.C_Alloc(-(long)LuaGCSize.Node * HashPart.Length); // the old part goes
			if(size == 0) {
				HashPart = DummyHashPart;
				LastFree = size;
				return;
			}

			int lsize = CeilLog2(size);
			if(lsize > MAXBITS) { L.G_RunError("table overflow"); }

			size = (1 << lsize);
			L.C_Alloc((long)LuaGCSize.Node * size);
			HashPart = new HNode[size];
			for(int i=0; i<size; ++i) {
				HashPart[i] = NewHNode();
				HashPart[i].Index = i;
			}
			LastFree = size;
		}

		private HNode GetFreePos()
		{
			while(LastFree > 0) {
				var node = HashPart[--LastFree];
				if(node.Key.V.TtIsNil()) { return node; }
			}
			return null;
		}

		/*
		** returns the index for `key' if `key' is an appropriate key to live in
		** the array part of the table, -1 otherwise.
		*/
		private int ArrayIndex(ref TValue k)
		{
			if(k.TtIsInteger()) {
				long i = k.IValue();
				if(0 < i && i <= MAXASIZE)
					return (int)i; // 'key' is an appropriate array index
			}
			return -1;
		}

		private static readonly byte[] Log2_ = new byte[] {
			0,1,2,2,3,3,3,3,4,4,4,4,4,4,4,4,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
			6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,6,
			7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
			7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,7,
			8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
			8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
			8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
			8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8
		};
		internal static int CeilLog2(int x)
		{
			Utl.Assert(x > 0);
			int l = 0;
			x--;
			while(x >= 256) { l+=8; x>>=8; }
			return l + Log2_[x];
		}

		private int CountInt(ref TValue key, ref int[] nums)
		{
			int k = ArrayIndex(ref key);
			if(0 < k && k <= MAXASIZE) {
				nums[CeilLog2(k)]++;
				return 1;
			}
			else return 0;
		}

		private int NumUseArray(ref int[] nums)
		{
			int ause = 0;
			int i = 1;
			for(int lg=0, ttlg=1; lg<=MAXBITS; lg++, ttlg*=2) {
				int lc = 0; // counter
				int lim = ttlg;
				if(lim > ArrayPart.Length) {
					lim = ArrayPart.Length;
					if(i > lim) { break; } // no more elements to count
				}

				// count elements in range (2^(lg-1), 2^lg]
				for(; i<=lim; ++i) {
					if(!ArrayPart[i-1].V.TtIsNil()) { lc++; }
				}
				nums[lg] += lc;
				ause += lc;
			}
			return ause;
		}

		private int NumUseHash(ref int[] nums, ref int nasize)
		{
			int totaluse = 0;
			int ause = 0;
			int i = HashPart.Length;
			while(i-- > 0) {
				var n = HashPart[i];
				if(!n.Val.V.TtIsNil()) {
					ause += CountInt(ref n.Key.V, ref nums);
					totaluse++;
				}
			}
			nasize += ause;
			return totaluse;
		}

		private int ComputeSizes(ref int[] nums, ref int nasize)
		{
			int a = 0;
			int na = 0;
			int n = 0;
			for(int i=0, tti=1; tti/2<nasize; ++i, tti*=2) {
				if(nums[i] > 0) {
					a += nums[i];
					if(a > tti/2) {
						n = tti;
						na = a;
					}
				}
				if(a == nasize) { break; } // all elements already conted
			}
			nasize = n;
			Utl.Assert(nasize/2 <= na && na <= nasize);
			return na;
		}

		private void Rehash(ref TValue k)
		{
			// Per call, not static: states on other threads rehash at the same time
			int[] Nums = new int[MAXBITS + 1];

			int nasize = NumUseArray(ref Nums);
			int totaluse = nasize;
			totaluse += NumUseHash(ref Nums, ref nasize);
			nasize += CountInt(ref k, ref Nums);
			totaluse++;
			int na = ComputeSizes(ref Nums, ref nasize);
			Resize(nasize, totaluse-na);
		}

		private void DumpParts()
		{
			System.Diagnostics.Debug.WriteLine("------------------ [DumpParts] enter -----------------------");
			System.Diagnostics.Debug.WriteLine("<< Array Part >>");
			for(var i=0; i<ArrayPart.Length; ++i) {
				var n = ArrayPart[i];
				System.Diagnostics.Debug.WriteLine(string.Format("i:{0} val:{1}", i, n.V));
			}
			System.Diagnostics.Debug.WriteLine("<< Hash Part >>");
			for(var i=0; i<HashPart.Length; ++i) {
				var n = HashPart[i];
				var next = (n.Next == null) ? -1 : n.Next.Index;
				System.Diagnostics.Debug.WriteLine(string.Format("i:{0} index:{1} key:{2} val:{3} next:{4}", i, n.Index, n.Key.V, n.Val.V, next));
			}
			System.Diagnostics.Debug.WriteLine("++++++++++++++++++ [DumpParts] leave +++++++++++++++++++++++");
		}

		private StkId NewTableKey(ref TValue key)
		{
			if(key.TtIsNil()) { L.G_RunError("index is nil"); }

			// an integral float key is the integer it equals
			var k = key;
			if(k.TtIsFloat()) {
				long i;
				if(LuaState.FloatToInteger(k.FltValue, out i, 0))
					{ k.SetIValue(i); }
				else if(System.Double.IsNaN(k.FltValue))
					{ L.G_RunError("index is NaN"); }
			}

			var mp = GetHashNode(ref k);

			// if main position is taken
			if(!mp.Val.V.TtIsNil() || mp == DummyNode) {
				var n = GetFreePos();
				if(n == null) {
					Rehash(ref k);
					var cell = Get(ref k);
					if(cell != TheNilValue) { return cell; }
					return NewTableKey(ref k);
				}

				Utl.Assert(n != DummyNode);
				var othern = GetHashNode(ref mp.Key.V);
				// is colliding node out of its main position?
				if(othern != mp) {
					while(othern.Next != mp) { othern = othern.Next; }
					othern.Next = n;
					n.CopyFrom(mp);
					mp.Next = null;
					mp.Val.V.SetNilValue();
				}
				// colliding node is in its own main position
				else {
					n.Next = mp.Next;
					mp.Next = n;
					mp = n;
				}
			}

			mp.Key.V.SetObj(ref k);
			Utl.Assert(mp.Val.V.TtIsNil());
			return mp.Val;
		}

		private int UnboundSearch(uint j)
		{
			long i = j;
			long jj = (long)j + 1;
			while(!GetInt(jj).V.TtIsNil()) {
				i = jj;
				jj *= 2;

				// overflow?
				if(jj > LuaLimits.MAX_INT) {
					/* table was built with bad purposes: resort to linear search */
					i = 1;
					while(!GetInt(i).V.TtIsNil()) { i++; }
					return (int)(i-1);
				}
			}
			/* now do a binary search between them */
			while(jj - i > 1) {
				long m = (i + jj) / 2;
				if(GetInt(m).V.TtIsNil()) { jj = m; }
				else { i = m; }
			}
			return (int)i;
		}
	}
}
