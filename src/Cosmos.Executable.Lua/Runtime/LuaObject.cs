// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


// #define DEBUG_DUMMY_TVALUE_MODIFY

namespace Cosmos.Executable.Lua
{
	using System;
	using System.Collections.Generic;

	internal struct TValue
	{
		private const UInt64 CLOSURE_LUA = 0; // lua closure
		private const UInt64 CLOSURE_CS = 1; // c# closure
		private const UInt64 CLOSURE_LCS = 2; // light c# closure

		private const UInt64 BOOLEAN_FALSE = 0;
		private const UInt64 BOOLEAN_TRUE = 1;

		// The two variants of numbers, as Lua 5.3 tags them: the type in the
		// low four bits, the variant above
		internal const int LUA_TNUMFLT = (int)LuaType.LUA_TNUMBER | (0 << 4);
		internal const int LUA_TNUMINT = (int)LuaType.LUA_TNUMBER | (1 << 4);

		public int Tt;
		public double FltValue;		// a float
		public UInt64 UInt64Value;	// an integer, a boolean, or the kind of a closure
		public object OValue;
#if DEBUG_DUMMY_TVALUE_MODIFY
		public bool Lock_;
#endif

		public override int GetHashCode()
		{
			return Tt.GetHashCode() ^ FltValue.GetHashCode()
				^ UInt64Value.GetHashCode()
				^ (OValue != null ? OValue.GetHashCode() : 0x12345678);
		}
		public override bool Equals(object o)
		{
			if(!(o is TValue)) return false;
			return Equals((TValue)o);
		}
		public bool Equals(TValue o)
		{
			if(Tt != o.Tt || FltValue != o.FltValue || UInt64Value != o.UInt64Value)
				{ return false; }

			switch(Tt) {
				case (int)LuaType.LUA_TNIL: return true;
				case (int)LuaType.LUA_TBOOLEAN: return BValue() == o.BValue();
				case LUA_TNUMFLT: return FltValue == o.FltValue;
				case LUA_TNUMINT: return UInt64Value == o.UInt64Value;
				case (int)LuaType.LUA_TSTRING: return SValue() == o.SValue();
				default: return SameObject(OValue, o.OValue);
			}
		}

		// C# functions without upvalues are Lua's light C functions: one
		// pushed twice is still one value, so that ipairs{} == ipairs{}
		internal static bool SameObject(object a, object b)
		{
			if(System.Object.ReferenceEquals(a, b))
				return true;
			var ca = a as LuaCsClosureValue;
			var cb = b as LuaCsClosureValue;
			return ca != null && cb != null && ca.IsLight && cb.IsLight && ca.F == cb.F;
		}
		public static bool operator==(TValue lhs, TValue rhs)
		{
			return lhs.Equals(rhs);
		}
		public static bool operator!=(TValue lhs, TValue rhs)
		{
			return !lhs.Equals(rhs);
		}

#if DEBUG_DUMMY_TVALUE_MODIFY
		private void CheckLock() {
			if(Lock_) {
				System.Diagnostics.Debug.WriteLine("changing a lock value");
			}
		}
#endif

		// ttnov: the type, without its variant
		internal int BaseTt() { return Tt & 0x0F; }

		internal bool TtIsNil() { return Tt == (int)LuaType.LUA_TNIL; }
		internal bool TtIsBoolean() { return Tt == (int)LuaType.LUA_TBOOLEAN; }
		internal bool TtIsNumber() { return (Tt & 0x0F) == (int)LuaType.LUA_TNUMBER; }
		internal bool TtIsFloat() { return Tt == LUA_TNUMFLT; }
		internal bool TtIsInteger() { return Tt == LUA_TNUMINT; }
		internal bool TtIsString() { return Tt == (int)LuaType.LUA_TSTRING; }
		internal bool TtIsTable() { return Tt == (int)LuaType.LUA_TTABLE; }
		internal bool TtIsFunction() { return Tt == (int)LuaType.LUA_TFUNCTION; }
		internal bool TtIsThread() { return Tt == (int)LuaType.LUA_TTHREAD; }

		internal bool ClIsLuaClosure() { return UInt64Value == CLOSURE_LUA; }
		internal bool ClIsCsClosure() { return UInt64Value == CLOSURE_CS; }
		internal bool ClIsLcsClosure() { return UInt64Value == CLOSURE_LCS; }

		internal bool BValue() { return UInt64Value != BOOLEAN_FALSE; }
		internal long IValue() { return unchecked((long)UInt64Value); }
		// nvalue: a number, an integer converted to a float
		internal double NValue() { return Tt == LUA_TNUMINT ? (double)IValue() : FltValue; }
		internal string SValue() { return (string)OValue; }
		internal LuaTable HValue() { return OValue as LuaTable; }
		internal LuaLClosureValue ClLValue() { return (LuaLClosureValue)OValue; }
		internal LuaCsClosureValue ClCsValue() { return (LuaCsClosureValue)OValue; }
		internal LuaUserDataValue RawUValue() { return OValue as LuaUserDataValue; }

		internal void SetNilValue() {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TNIL;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = null;
		}
		internal void SetBValue(bool v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TBOOLEAN;
			FltValue = 0.0;
			UInt64Value = v ? BOOLEAN_TRUE : BOOLEAN_FALSE;
			OValue = null;
		}
		internal void SetObj(ref TValue v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = v.Tt;
			FltValue = v.FltValue;
			UInt64Value = v.UInt64Value;
			OValue = v.OValue;
		}
		internal void SetFltValue(double v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = LUA_TNUMFLT;
			FltValue = v;
			UInt64Value = 0;
			OValue = null;
		}
		internal void SetIValue(long v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = LUA_TNUMINT;
			FltValue = 0.0;
			UInt64Value = unchecked((UInt64)v);
			OValue = null;
		}
		internal void SetSValue(string v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TSTRING;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = v;
		}
		internal void SetHValue(LuaTable v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TTABLE;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = v;
		}
		internal void SetThValue(LuaState v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TTHREAD;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = v;
		}
		internal void SetPValue(object v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TLIGHTUSERDATA;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = v;
		}
		internal void SetUValue(LuaUserDataValue v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TUSERDATA;
			FltValue = 0.0;
			UInt64Value = 0;
			OValue = v;
		}
		internal void SetClLValue(LuaLClosureValue v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TFUNCTION;
			FltValue = 0.0;
			UInt64Value = CLOSURE_LUA;
			OValue = v;
		}
		internal void SetClCsValue(LuaCsClosureValue v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TFUNCTION;
			FltValue = 0.0;
			UInt64Value = CLOSURE_CS;
			OValue = v;
		}
		internal void SetClLcsValue(CSharpFunctionDelegate v) {
#if DEBUG_DUMMY_TVALUE_MODIFY
			CheckLock();
#endif
			Tt = (int)LuaType.LUA_TFUNCTION;
			FltValue = 0.0;
			UInt64Value = CLOSURE_LCS;
			OValue = v;
		}

		public override string ToString()
		{
			if (TtIsString()) {
				return string.Format("(string, {0})", SValue());
			} else if (TtIsInteger()) {
				return string.Format("(integer, {0})", IValue());
			} else if (TtIsFloat()) {
				return string.Format("(float, {0})", FltValue);
			} else if (TtIsNil()) {
				return "(nil)";
			} else {
				return string.Format("(type:{0})", Tt);
			}
		}
	}

	internal class StkId
	{
		public TValue V;

		private StkId[] List;
		public int Index { get; private set; }

		public void SetList(StkId[] list) { List = list; }
		public void SetIndex(int index) { Index = index; }

		public static StkId inc(ref StkId val)
		{
			var ret = val;
			val = val.List[val.Index+1];
			return ret;
		}

		public override string ToString()
		{
			string detail;
			if(V.TtIsString())
				{ detail = V.SValue().Replace("\n", "»"); }
			else
				{ detail = "..."; }
			return string.Format("StkId - {0} - {1}", LuaState.TypeName((LuaType)V.BaseTt()), detail);
		}
	}

	internal class LuaLClosureValue
	{
		public LuaProto 		Proto;
		public LuaUpvalue[]		Upvals;

		public LuaLClosureValue(LuaProto p) {
			Proto = p;

			// luaF_initupvals: fill the closure with new closed upvalues
			Upvals = new LuaUpvalue[p.Upvalues.Count];
			for(int i=0; i<p.Upvalues.Count; ++i)
				{ Upvals[i] = new LuaUpvalue(); }
		}
	}
	
	internal class LuaUserDataValue
	{
		public object Value;
		public LuaTable MetaTable;
		public int Length;
		public TValue[] UserValues;	// the 'nuvalue' user values (5.4)

		public LuaUserDataValue( int nuvalue = 1 )
		{
			UserValues = new TValue[nuvalue];
			for( int i=0; i<nuvalue; ++i )
				UserValues[i].SetNilValue();
		}
	}

	internal class LocVar
	{
		public string VarName;
		public int StartPc;	// first point where variable is active
		public int EndPc;	// first point where variable is dead
	}

	// Description of an upvalue for function prototypes
	internal class UpvalDesc
	{
		public string Name;		// upvalue name (for debug information)
		public int Index;		// index of upvalue (in stack or in outer function's list)
		public bool InStack;	// whether it is in stack (register)
		public byte Kind;		// kind of corresponding variable
	}

	/*
	** Associates the absolute line source for a given instruction ('pc').
	** The array 'lineinfo' gives, for each instruction, the difference in
	** lines from the previous instruction. When that difference does not
	** fit into a byte, Lua saves the absolute line for that instruction.
	** (Lua also saves the absolute line periodically, to speed up the
	** computation of a line number: we can use binary search in the
	** absolute-line array, but we must traverse the 'lineinfo' array
	** linearly to compute a line.)
	*/
	internal struct AbsLineInfo
	{
		public int Pc;
		public int Line;
	}

	internal class LuaProto
	{
		public List<Instruction> 	Code;
		public List<StkId>			K;
		public List<LuaProto>		P;
		public List<UpvalDesc>		Upvalues;

		public int					LineDefined;
		public int					LastLineDefined;

		public int					NumParams;	// number of fixed (named) parameters
		public bool					IsVarArg;
		public byte					MaxStackSize;	// number of registers needed by this function

		public string				Source;
		public List<sbyte>			LineInfo;	// information about source lines (debug information)
		public List<AbsLineInfo>	AbsLineInfo;	// idem
		public List<LocVar>			LocVars;	// information about local variables (debug information)

		public LuaProto()
		{
			Code = new List<Instruction>();
			K = new List<StkId>();
			P = new List<LuaProto>();
			Upvalues = new List<UpvalDesc>();
			LineInfo = new List<sbyte>();
			AbsLineInfo = new List<AbsLineInfo>();
			LocVars = new List<LocVar>();
		}
	}
	
	internal class LuaUpvalue
	{
		public StkId			V;		// points to stack or to its own value
		public StkId			Value;	// the value (when closed)

		public LuaUpvalue()
		{
			Value = new StkId();
			Value.V.SetNilValue();

			V = Value;
		}
	}

	internal class LuaCsClosureValue
	{
		public CSharpFunctionDelegate 	F;
		public StkId[]					Upvals;

		public LuaCsClosureValue( CSharpFunctionDelegate f )
		{
			F = f;
		}

		public bool IsLight
		{
			get { return Upvals == null || Upvals.Length == 0; }
		}

		// equal light functions hash alike, as table keys (see TValue.SameObject)
		public override int GetHashCode()
		{
			return IsLight ? F.GetHashCode() : base.GetHashCode();
		}

		public LuaCsClosureValue( CSharpFunctionDelegate f, int numUpvalues )
		{
			F = f;
			Upvals = new StkId[numUpvalues];
			for(int i=0; i<numUpvalues; ++i) {
				var newItem = new StkId();
				Upvals[i] = newItem;
				newItem.SetList(Upvals);
				newItem.SetIndex(i);
			}
		}
	}

	internal partial class LuaState
	{
		internal static StkId TheNilValue;

		// l_str2int: a decimal or hexadecimal integer numeral, with optional
		// spaces around it; a hexadecimal one wraps around, a decimal one
		// that overflows is no integer (but a float)
		private static bool O_Str2Int( string s, out long result )
		{
			const ulong maxby10 = (ulong)LuaConf.LUA_MAXINTEGER / 10;
			const int maxlastd = (int)((ulong)LuaConf.LUA_MAXINTEGER % 10);
			ulong a = 0;
			bool empty = true;
			int pos = 0;
			result = 0;
			while( pos < s.Length && Utl.IsSpace( s[pos] ) ) ++pos;
			bool neg = false;
			if( pos < s.Length && s[pos] == '-' ) { ++pos; neg = true; }
			else if( pos < s.Length && s[pos] == '+' ) ++pos;
			if( pos + 1 < s.Length && s[pos] == '0' && (s[pos+1] == 'x' || s[pos+1] == 'X') )
			{
				for( pos += 2; pos < s.Length && Utl.IsXDigit( s[pos] ); ++pos )
				{
					a = unchecked( a * 16 + (ulong)Utl.HexaValue( s[pos] ) );
					empty = false;
				}
			}
			else
			{
				for( ; pos < s.Length && Utl.IsDigit( s[pos] ); ++pos )
				{
					int d = s[pos] - '0';
					if( a >= maxby10 && (a > maxby10 || d > maxlastd + (neg ? 1 : 0)) )
						return false; // overflow: not accepted as an integer
					a = a * 10 + (ulong)d;
					empty = false;
				}
			}
			while( pos < s.Length && Utl.IsSpace( s[pos] ) ) ++pos;
			if( empty || pos != s.Length )
				return false;
			result = unchecked( (long)(neg ? 0UL - a : a) );
			return true;
		}

		// l_str2d: a float numeral, decimal or hexadecimal, with optional
		// spaces around it; not 'inf' nor 'nan'
		public static bool O_Str2Decimal( string s, out double result )
		{
			result = 0.0;

			int mark = s.IndexOfAny( new char[] { '.', 'x', 'X', 'n', 'N' } );
			char mode = mark < 0 ? '\0' : char.ToLowerInvariant( s[mark] );
			if( mode == 'n' ) // reject 'inf' and 'nan'
				return false;

			int pos = 0;
			if( mode == 'x' )
				result = Utl.StrX2Number( s, ref pos );
			else
				result = Utl.Str2Number( s, ref pos );

			if( pos == 0 )
				return false; // nothing recognized

			while( pos < s.Length && Utl.IsSpace( s[pos] ) ) ++pos;
			return pos == s.Length; // OK if no trailing characters
		}

		// luaO_str2num: the number a whole string is the numeral of, an
		// integer if it is one, else a float
		public static bool O_Str2Num( string s, out TValue o )
		{
			o = new TValue();
			long i;
			double n;
			if( O_Str2Int( s, out i ) )
				o.SetIValue( i );
			else if( O_Str2Decimal( s, out n ) )
				o.SetFltValue( n );
			else
				return false;
			return true;
		}

		// luaO_rawarith: an arithmetic or bitwise operation on numbers (strings
		// go through the metamethods of the string library); false if an
		// operand is not a number
		internal static bool O_RawArith( LuaState L, LuaOp op, ref TValue p1, ref TValue p2, ref TValue res )
		{
			switch( op )
			{
				case LuaOp.LUA_OPBAND: case LuaOp.LUA_OPBOR: case LuaOp.LUA_OPBXOR:
				case LuaOp.LUA_OPSHL: case LuaOp.LUA_OPSHR:
				case LuaOp.LUA_OPBNOT: { // operate only on integers
					long i1, i2;
					if( V_ToIntegerNS( ref p1, out i1, F2Imod.F2Ieq ) && V_ToIntegerNS( ref p2, out i2, F2Imod.F2Ieq ) )
					{
						res.SetIValue( IntArith( L, op, i1, i2 ) );
						return true;
					}
					return false;
				}
				case LuaOp.LUA_OPDIV: case LuaOp.LUA_OPPOW: { // operate only on floats
					double n1, n2;
					if( ToNumberNS( ref p1, out n1 ) && ToNumberNS( ref p2, out n2 ) )
					{
						res.SetFltValue( NumArith( op, n1, n2 ) );
						return true;
					}
					return false;
				}
				default: { // other operations
					double n1, n2;
					if( p1.TtIsInteger() && p2.TtIsInteger() )
					{
						res.SetIValue( IntArith( L, op, p1.IValue(), p2.IValue() ) );
						return true;
					}
					if( ToNumberNS( ref p1, out n1 ) && ToNumberNS( ref p2, out n2 ) )
					{
						res.SetFltValue( NumArith( op, n1, n2 ) );
						return true;
					}
					return false;
				}
			}
		}

		internal static long IntArith( LuaState L, LuaOp op, long v1, long v2 )
		{
			unchecked
			{
				switch( op )
				{
					case LuaOp.LUA_OPADD: return v1 + v2;
					case LuaOp.LUA_OPSUB: return v1 - v2;
					case LuaOp.LUA_OPMUL: return v1 * v2;
					case LuaOp.LUA_OPMOD: return V_Mod( L, v1, v2 );
					case LuaOp.LUA_OPIDIV: return V_Div( L, v1, v2 );
					case LuaOp.LUA_OPBAND: return v1 & v2;
					case LuaOp.LUA_OPBOR: return v1 | v2;
					case LuaOp.LUA_OPBXOR: return v1 ^ v2;
					case LuaOp.LUA_OPSHL: return V_ShiftL( v1, v2 );
					case LuaOp.LUA_OPSHR: return V_ShiftL( v1, 0 - v2 );
					case LuaOp.LUA_OPUNM: return 0 - v1;
					case LuaOp.LUA_OPBNOT: return ~v1;
					default: throw new System.NotImplementedException();
				}
			}
		}

		internal static double NumArith( LuaOp op, double v1, double v2 )
		{
			switch( op )
			{
				case LuaOp.LUA_OPADD: return v1+v2;
				case LuaOp.LUA_OPSUB: return v1-v2;
				case LuaOp.LUA_OPMUL: return v1*v2;
				case LuaOp.LUA_OPDIV: return v1/v2;
				case LuaOp.LUA_OPPOW: return NumPow(v1, v2);
				case LuaOp.LUA_OPIDIV: return Math.Floor(v1/v2);
				case LuaOp.LUA_OPUNM: return -v1;
				case LuaOp.LUA_OPMOD: return NumMod(v1, v2);
				default: throw new System.NotImplementedException();
			}
		}

		// luai_nummod: 'a - floor(a/b)*b', from fmod (which C#'s % is)
		internal static double NumMod( double a, double b )
		{
			double m = a % b;
			if( (m > 0) ? b < 0 : (m < 0 && b > 0) )
				m += b;
			return m;
		}

		private bool IsFalse(ref TValue v)
		{
			if( v.TtIsNil() )
				return true;
				
			if((v.TtIsBoolean() && v.BValue() == false))
				return true;

			return false;
		}

		// tostring: whether 'o' is a string, after converting a number to one
		private static bool ToString(ref TValue o)
		{
			if(o.TtIsString()) { return true; }
			return V_ToString(ref o);
		}

		internal LuaLClosureValue GetCurrentLuaFunc(CallInfo ci)
		{
			if(ci.IsLua) {
				return Stack[ci.FuncIndex].V.ClLValue();
			}
			else return null;
		}

		internal int GetCurrentLine(CallInfo ci)
		{
			Utl.Assert(ci.IsLua);
			var cl = Stack[ci.FuncIndex].V.ClLValue();
			return G_GetFuncLine(cl.Proto, ci.CurrentPc);
		}
	}

}

