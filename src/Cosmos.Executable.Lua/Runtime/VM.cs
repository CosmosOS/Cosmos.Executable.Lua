// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System;
using System.Collections.Generic;

namespace Cosmos.Executable.Lua
{
	using StringBuilder = System.Text.StringBuilder;

	/*
	** Rounding modes for float->integer coercion
	 */
	internal enum F2Imod
	{
		F2Ieq,		/* no rounding; accepts only integral values */
		F2Ifloor,	/* takes the floor of the number */
		F2Iceil		/* takes the ceil of the number */
	}

	// lvm.c of Lua 5.5: the virtual machine
	internal partial class LuaState
	{
		/* limit for table tag-method chains (to avoid infinite loops) */
		private const int MAXTAGLOOP = 2000;

		/*
		** Try to convert a value from string to a number value.
		** If the value is not a string or is a string not representing
		** a valid numeral, do not modify 'result' and return false.
		*/
		private static bool L_StrToN( ref TValue obj, out TValue result )
		{
			if( !obj.TtIsString() ) // is object not a string?
			{
				result = new TValue();
				return false;
			}
			return O_Str2Num( obj.SValue(), out result );
		}

		/*
		** Try to convert a value to a float.
		*/
		internal static bool V_ToNumber( ref TValue obj, out double n )
		{
			if( obj.TtIsFloat() ) {
				n = obj.FltValue;
				return true;
			}
			if( obj.TtIsInteger() ) {
				n = (double)obj.IValue();
				return true;
			}
			TValue v;
			if( L_StrToN( ref obj, out v ) ) { // string coercible to number?
				n = v.NValue(); // convert result of 'luaO_str2num' to a float
				return true;
			}
			n = 0.0;
			return false; // conversion failed
		}

		// tonumberns: a number as a float, without string coercion
		internal static bool ToNumberNS( ref TValue obj, out double n )
		{
			if( obj.TtIsFloat() ) {
				n = obj.FltValue;
				return true;
			}
			if( obj.TtIsInteger() ) {
				n = (double)obj.IValue();
				return true;
			}
			n = 0.0;
			return false;
		}

		/*
		** try to convert a float to an integer, rounding according to 'mode'.
		*/
		internal static bool FltToInteger( double n, out long p, F2Imod mode )
		{
			double f = Math.Floor( n );
			if( n != f ) { // not an integral value?
				if( mode == F2Imod.F2Ieq ) { p = 0; return false; } // fails if mode demands integral value
				else if( mode == F2Imod.F2Iceil ) // needs ceil?
					f += 1; // convert floor to ceil (remember: n != f)
			}
			return NumberToInteger( f, out p );
		}

		// the same, the mode as in Lua 5.4: 0 integral values only, 1 floor, 2 ceil
		internal static bool FloatToInteger( double n, out long p, int mode )
		{
			return FltToInteger( n, out p, (F2Imod)mode );
		}

		// lua_numbertointeger: an integral float in the range of integers
		internal static bool NumberToInteger( double n, out long p )
		{
			if( n >= (double)LuaConf.LUA_MININTEGER && n < -(double)LuaConf.LUA_MININTEGER ) {
				p = (long)n;
				return true;
			}
			p = 0;
			return false;
		}

		/*
		** try to convert a value to an integer, rounding according to 'mode',
		** without string coercion.
		*/
		internal static bool V_ToIntegerNS( ref TValue obj, out long p, F2Imod mode )
		{
			if( obj.TtIsFloat() )
				return FltToInteger( obj.FltValue, out p, mode );
			else if( obj.TtIsInteger() ) {
				p = obj.IValue();
				return true;
			}
			p = 0;
			return false;
		}

		/*
		** try to convert a value to an integer.
		*/
		internal static bool V_ToInteger( ref TValue obj, out long p, F2Imod mode )
		{
			TValue v;
			if( L_StrToN( ref obj, out v ) ) // does 'obj' point to a numerical string?
				return V_ToIntegerNS( ref v, out p, mode );
			return V_ToIntegerNS( ref obj, out p, mode );
		}

		// the same, the mode as in Lua 5.4: 0 integral values only, 1 floor, 2 ceil
		internal static bool V_ToInteger( ref TValue obj, out long p, int mode )
		{
			return V_ToInteger( ref obj, out p, (F2Imod)mode );
		}

		/*
		** Try to convert a 'for' limit to an integer, preserving the semantics
		** of the loop. Return true if the loop must not run; otherwise, '*p'
		** gets the integer limit.
		** (The following explanation assumes a positive step; it is valid for
		** negative steps mutatis mutandis.)
		** If the limit is an integer or can be converted to an integer,
		** rounding down, that is the limit.
		** Otherwise, check whether the limit can be converted to a float. If
		** the float is too large, clip it to LUA_MAXINTEGER.  If the float
		** is too negative, the loop should not run, because any initial
		** integer value is greater than such limit; so, the function returns
		** true to signal that. (For this latter case, no integer limit would be
		** correct; even a limit of LUA_MININTEGER would run the loop once for
		** an initial value equal to LUA_MININTEGER.)
		*/
		private bool ForLimit( long init, ref TValue lim, out long p, long step )
		{
			if( !V_ToInteger( ref lim, out p, (step < 0 ? F2Imod.F2Iceil : F2Imod.F2Ifloor) ) )
			{
				/* not coercible to in integer */
				double flim; // try to convert to float
				if( !V_ToNumber( ref lim, out flim ) ) // cannot convert to float?
					G_ForError( ref lim, "limit" );
				/* else 'flim' is a float out of integer bounds */
				if( 0 < flim ) { // if it is positive, it is too large
					if( step < 0 ) return true; // initial value must be less than it
					p = LuaConf.LUA_MAXINTEGER; // truncate
				}
				else { // it is less than min integer
					if( step > 0 ) return true; // initial value must be greater than it
					p = LuaConf.LUA_MININTEGER; // truncate
				}
			}
			return (step > 0 ? init > p : init < p); // not to run?
		}

		/*
		** Prepare a numerical for loop (opcode OP_FORPREP).
		** Return true to skip the loop. Otherwise,
		** after preparation, stack will be as follows:
		**   ra     : loop counter (integer loops) or limit (float loops)
		**   ra + 1 : step
		**   ra + 2 : control variable
		*/
		private bool ForPrep( int ra )
		{
			StkId pinit = Stack[ra];
			StkId plimit = Stack[ra + 1];
			StkId pstep = Stack[ra + 2];
			if( pinit.V.TtIsInteger() && pstep.V.TtIsInteger() ) // integer loop?
			{
				long init = pinit.V.IValue();
				long step = pstep.V.IValue();
				long limit;
				if( step == 0 )
					G_RunError( "'for' step is zero" );
				if( ForLimit( init, ref plimit.V, out limit, step ) )
					return true; // skip the loop
				else // prepare loop counter
				{
					ulong count;
					unchecked
					{
						if( step > 0 ) { // ascending loop?
							count = (ulong)limit - (ulong)init;
							if( step != 1 ) // avoid division in the too common case
								count /= (ulong)step;
						}
						else { // step < 0; descending loop
							count = (ulong)init - (ulong)limit;
							/* 'step+1' avoids negating 'mininteger' */
							count /= (ulong)(-(step + 1)) + 1u;
						}
						pinit.V.SetIValue( (long)count ); // change init to count
						plimit.V.SetIValue( step ); // change limit to step
						pstep.V.SetIValue( init ); // change step to init
					}
				}
			}
			else // try making all values floats
			{
				double init; double limit; double step;
				if( !V_ToNumber( ref plimit.V, out limit ) )
					G_ForError( ref plimit.V, "limit" );
				if( !V_ToNumber( ref pstep.V, out step ) )
					G_ForError( ref pstep.V, "step" );
				if( !V_ToNumber( ref pinit.V, out init ) )
					G_ForError( ref pinit.V, "initial value" );
				if( step == 0 )
					G_RunError( "'for' step is zero" );
				if( (0 < step) ? (limit < init) : (init < limit) )
					return true; // skip the loop
				else
				{
					/* make sure all values are floats */
					pinit.V.SetFltValue( limit );
					plimit.V.SetFltValue( step );
					pstep.V.SetFltValue( init ); // control variable
				}
			}
			return false;
		}

		/*
		** Execute a step of a float numerical for loop, returning
		** true iff the loop must continue. (The integer case is
		** written inline with opcode OP_FORLOOP, for performance.)
		*/
		private bool FloatForLoop( int ra )
		{
			double step = Stack[ra + 1].V.FltValue;
			double limit = Stack[ra].V.FltValue;
			double idx = Stack[ra + 2].V.FltValue; // control variable
			idx = idx + step; // increment index
			if( (0 < step) ? (idx <= limit) : (limit <= idx) )
			{
				Stack[ra + 2].V.SetFltValue( idx ); // update control variable
				return true; // jump back
			}
			else
				return false; // finish the loop
		}

		/*
		** Finish the table access 'val = t[key]'.
		** if 'isTable' is false, 't' is not a table; otherwise, the entry
		** t[k] is empty.
		*/
		internal void V_FinishGet( ref TValue t, ref TValue key, int val, bool isTable )
		{
			for( int loop = 0; loop < MAXTAGLOOP; loop++ )
			{
				StkId tm; // metamethod
				if( !isTable ) // 't' is not a table?
				{
					Utl.Assert( !t.TtIsTable() );
					tm = T_GetTMByObj( ref t, TMS.TM_INDEX );
					if( tm.V.TtIsNil() )
						G_TypeError( ref t, "index" ); // no metamethod
					/* else will try the metamethod */
				}
				else // 't' is a table
				{
					tm = FastTM( t.HValue().MetaTable, TMS.TM_INDEX ); // table's metamethod
					if( tm == null ) // no metamethod?
					{
						Stack[val].V.SetNilValue(); // result is nil
						return;
					}
					/* else will try the metamethod */
				}
				if( tm.V.TtIsFunction() ) // is metamethod a function?
				{
					T_CallTMRes( ref tm.V, ref t, ref key, val ); // call it
					return;
				}
				t = ref tm.V; // else try to access 'tm[key]'
				isTable = t.TtIsTable();
				if( isTable ) // fast track?
				{
					var slot = t.HValue().Get( ref key );
					if( !slot.V.TtIsNil() )
					{
						Stack[val].V.SetObj( ref slot.V ); // done
						return;
					}
				}
				/* else repeat (tail call 'luaV_finishget') */
			}
			G_RunError( "'__index' chain too long; possible loop" );
		}

		/*
		** Finish a table assignment 't[key] = val'.
		** If 'isTable' is false, 't' is not a table. Otherwise, the entry
		** 't[key]' is empty (or absent).
		*/
		internal void V_FinishSet( ref TValue t, ref TValue key, ref TValue val, bool isTable )
		{
			for( int loop = 0; loop < MAXTAGLOOP; loop++ )
			{
				StkId tm; // '__newindex' metamethod
				if( isTable ) // is 't' a table?
				{
					var h = t.HValue(); // save 't' table
					tm = FastTM( h.MetaTable, TMS.TM_NEWINDEX ); // get metamethod
					if( tm == null ) // no metamethod?
					{
						h.Set( ref key, ref val ); // set new value
						return;
					}
					/* else will try the metamethod */
				}
				else // not a table; check metamethod
				{
					tm = T_GetTMByObj( ref t, TMS.TM_NEWINDEX );
					if( tm.V.TtIsNil() )
						G_TypeError( ref t, "index" );
				}
				/* try the metamethod */
				if( tm.V.TtIsFunction() )
				{
					T_CallTM( ref tm.V, ref t, ref key, ref val );
					return;
				}
				t = ref tm.V; // else repeat assignment over 'tm'
				isTable = t.TtIsTable();
				if( isTable )
				{
					var h = t.HValue();
					var slot = h.Get( ref key );
					if( !slot.V.TtIsNil() )
					{
						slot.V.SetObj( ref val );
						return; // done
					}
				}
				/* else 'return luaV_finishset(L, t, key, val, slot)' (loop) */
			}
			G_RunError( "'__newindex' chain too long; possible loop" );
		}

		// The value t[key] of a table 't', nil if absent (luaV_fastget)
		private static StkId FastGet( LuaTable h, ref TValue key )
		{
			return key.TtIsInteger() ? h.GetInt( key.IValue() ) : h.Get( ref key );
		}

		// 'val = t[key]', by its metamethods if need be (luaV_gettable)
		internal void V_GetTable( ref TValue t, ref TValue key, int val )
		{
			if( t.TtIsTable() )
			{
				var slot = FastGet( t.HValue(), ref key );
				if( !slot.V.TtIsNil() )
				{
					Stack[val].V.SetObj( ref slot.V );
					return;
				}
				V_FinishGet( ref t, ref key, val, true );
			}
			else
				V_FinishGet( ref t, ref key, val, false );
		}

		// 't[key] = val', by its metamethods if need be (luaV_settable)
		internal void V_SetTable( ref TValue t, ref TValue key, ref TValue val )
		{
			if( t.TtIsTable() )
			{
				var slot = FastGet( t.HValue(), ref key );
				if( !slot.V.TtIsNil() )
				{
					slot.V.SetObj( ref val ); // luaV_finishfastset
					return;
				}
				V_FinishSet( ref t, ref key, ref val, true );
			}
			else
				V_FinishSet( ref t, ref key, ref val, false );
		}

		/*
		** Compare two strings 'ts1' x 'ts2', returning an integer less-equal-
		** -greater than zero if 'ts1' is less-equal-greater than 'ts2'.
		** (The strings hold bytes, which the C locale compares as such.)
		*/
		private static int L_StrCmp( string ts1, string ts2 )
		{
			return string.CompareOrdinal( ts1, ts2 );
		}

		/*
		** 'l_intfitsf' checks whether a given integer is in the range that
		** can be converted to a float without rounding. Used in comparisons.
		*/
		private const ulong MAXINTFITSF = (ulong)1 << 53;

		private static bool IntFitsFloat( long i )
		{
			return unchecked(MAXINTFITSF + (ulong)i) <= (2 * MAXINTFITSF);
		}

		/*
		** Check whether integer 'i' is less than float 'f'. If 'i' has an
		** exact representation as a float ('l_intfitsf'), compare numbers as
		** floats. Otherwise, use the equivalence 'i < f <=> i < ceil(f)'.
		** If 'ceil(f)' is out of integer range, either 'f' is greater than
		** all integers or less than all integers.
		** (The test with 'l_intfitsf' is only for performance; the else
		** case is correct for all values, but it is slow due to the conversion
		** from float to int.)
		** When 'f' is NaN, comparisons must result in false.
		*/
		private static bool LTIntFloat( long i, double f )
		{
			if( IntFitsFloat( i ) )
				return (double)i < f; // compare them as floats
			else { // i < f <=> i < ceil(f)
				long fi;
				if( FltToInteger( f, out fi, F2Imod.F2Iceil ) ) // fi = ceil(f)
					return i < fi; // compare them as integers
				else // 'f' is either greater or less than all integers
					return f > 0; // greater?
			}
		}

		/*
		** Check whether integer 'i' is less than or equal to float 'f'.
		** See comments on previous function.
		*/
		private static bool LEIntFloat( long i, double f )
		{
			if( IntFitsFloat( i ) )
				return (double)i <= f; // compare them as floats
			else { // i <= f <=> i <= floor(f)
				long fi;
				if( FltToInteger( f, out fi, F2Imod.F2Ifloor ) ) // fi = floor(f)
					return i <= fi; // compare them as integers
				else // 'f' is either greater or less than all integers
					return f > 0; // greater?
			}
		}

		/*
		** Check whether float 'f' is less than integer 'i'.
		** See comments on previous function.
		*/
		private static bool LTFloatInt( double f, long i )
		{
			if( IntFitsFloat( i ) )
				return f < (double)i; // compare them as floats
			else { // f < i <=> floor(f) < i
				long fi;
				if( FltToInteger( f, out fi, F2Imod.F2Ifloor ) ) // fi = floor(f)
					return fi < i; // compare them as integers
				else // 'f' is either greater or less than all integers
					return f < 0; // less?
			}
		}

		/*
		** Check whether float 'f' is less than or equal to integer 'i'.
		** See comments on previous function.
		*/
		private static bool LEFloatInt( double f, long i )
		{
			if( IntFitsFloat( i ) )
				return f <= (double)i; // compare them as floats
			else { // f <= i <=> ceil(f) <= i
				long fi;
				if( FltToInteger( f, out fi, F2Imod.F2Iceil ) ) // fi = ceil(f)
					return fi <= i; // compare them as integers
				else // 'f' is either greater or less than all integers
					return f < 0; // less?
			}
		}

		/*
		** Return 'l < r', for numbers.
		*/
		private static bool LTNum( ref TValue l, ref TValue r )
		{
			Utl.Assert( l.TtIsNumber() && r.TtIsNumber() );
			if( l.TtIsInteger() ) {
				long li = l.IValue();
				if( r.TtIsInteger() )
					return li < r.IValue(); // both are integers
				else // 'l' is int and 'r' is float
					return LTIntFloat( li, r.FltValue ); // l < r ?
			}
			else {
				double lf = l.FltValue; // 'l' must be float
				if( r.TtIsFloat() )
					return lf < r.FltValue; // both are float
				else // 'l' is float and 'r' is int
					return LTFloatInt( lf, r.IValue() );
			}
		}

		/*
		** Return 'l <= r', for numbers.
		*/
		private static bool LENum( ref TValue l, ref TValue r )
		{
			Utl.Assert( l.TtIsNumber() && r.TtIsNumber() );
			if( l.TtIsInteger() ) {
				long li = l.IValue();
				if( r.TtIsInteger() )
					return li <= r.IValue(); // both are integers
				else // 'l' is int and 'r' is float
					return LEIntFloat( li, r.FltValue ); // l <= r ?
			}
			else {
				double lf = l.FltValue; // 'l' must be float
				if( r.TtIsFloat() )
					return lf <= r.FltValue; // both are float
				else // 'l' is float and 'r' is int
					return LEFloatInt( lf, r.IValue() );
			}
		}

		/*
		** return 'l < r' for non-numbers.
		*/
		private bool LessThanOthers( ref TValue l, ref TValue r )
		{
			Utl.Assert( !l.TtIsNumber() || !r.TtIsNumber() );
			if( l.TtIsString() && r.TtIsString() ) // both are strings?
				return L_StrCmp( l.SValue(), r.SValue() ) < 0;
			else
				return T_CallOrderTM( ref l, ref r, TMS.TM_LT );
		}

		/*
		** Main operation less than; return 'l < r'.
		*/
		internal bool V_LessThan( ref TValue l, ref TValue r )
		{
			if( l.TtIsNumber() && r.TtIsNumber() ) // both operands are numbers?
				return LTNum( ref l, ref r );
			else return LessThanOthers( ref l, ref r );
		}

		/*
		** return 'l <= r' for non-numbers.
		*/
		private bool LessEqualOthers( ref TValue l, ref TValue r )
		{
			Utl.Assert( !l.TtIsNumber() || !r.TtIsNumber() );
			if( l.TtIsString() && r.TtIsString() ) // both are strings?
				return L_StrCmp( l.SValue(), r.SValue() ) <= 0;
			else
				return T_CallOrderTM( ref l, ref r, TMS.TM_LE );
		}

		/*
		** Main operation less than or equal to; return 'l <= r'.
		*/
		internal bool V_LessEqual( ref TValue l, ref TValue r )
		{
			if( l.TtIsNumber() && r.TtIsNumber() ) // both operands are numbers?
				return LENum( ref l, ref r );
			else return LessEqualOthers( ref l, ref r );
		}

		internal bool V_RawEqualObj( ref TValue t1, ref TValue t2 )
		{
			return V_EqualObj( ref t1, ref t2, true );
		}

		/*
		** Main operation for equality of Lua values; return 't1 == t2'.
		** 'rawEq' means raw equality (no metamethods)
		*/
		internal bool V_EqualObj( ref TValue t1, ref TValue t2, bool rawEq )
		{
			if( t1.Tt != t2.Tt ) // not the same variant?
			{
				if( t1.BaseTt() != t2.BaseTt() || t1.BaseTt() != (int)LuaType.LUA_TNUMBER )
					return false; // only numbers can be equal with different variants
				else { // two numbers with different variants
					/* One of them is an integer. If the other does not have an
					   integer value, they cannot be equal; otherwise, compare their
					   integer values. */
					long i1, i2;
					return V_ToIntegerNS( ref t1, out i1, F2Imod.F2Ieq ) &&
						V_ToIntegerNS( ref t2, out i2, F2Imod.F2Ieq ) &&
						i1 == i2;
				}
			}

			/* values have same type and same variant */
			StkId tm = null;
			switch( t1.Tt )
			{
				case (int)LuaType.LUA_TNIL:
					return true;
				case TValue.LUA_TNUMINT:
					return t1.IValue() == t2.IValue();
				case TValue.LUA_TNUMFLT:
					return t1.FltValue == t2.FltValue;
				case (int)LuaType.LUA_TBOOLEAN:
					return t1.BValue() == t2.BValue();
				case (int)LuaType.LUA_TSTRING:
					return t1.SValue() == t2.SValue();
				case (int)LuaType.LUA_TUSERDATA:
				{
					var ud1 = t1.RawUValue();
					var ud2 = t2.RawUValue();
					if( ud1 == ud2 ) return true;
					else if( rawEq ) return false;
					tm = FastTM( ud1.MetaTable, TMS.TM_EQ );
					if( tm == null )
						tm = FastTM( ud2.MetaTable, TMS.TM_EQ );
					break; // will try TM
				}
				case (int)LuaType.LUA_TTABLE:
				{
					var tbl1 = t1.HValue();
					var tbl2 = t2.HValue();
					if( System.Object.ReferenceEquals( tbl1, tbl2 ) ) return true;
					else if( rawEq ) return false;
					tm = FastTM( tbl1.MetaTable, TMS.TM_EQ );
					if( tm == null )
						tm = FastTM( tbl2.MetaTable, TMS.TM_EQ );
					break; // will try TM
				}
				default:
					return TValue.SameObject( t1.OValue, t2.OValue );
			}
			if( tm == null ) // no TM?
				return false; // objects are different
			else
			{
				T_CallTMRes( ref tm.V, ref t1, ref t2, Top.Index ); // call TM
				return !IsFalse( ref Top.V );
			}
		}

		// a number converted to the string Lua writes for it, in place (luaO_tostring)
		private static bool V_ToString( ref TValue v )
		{
			if( v.TtIsInteger() )
				v.SetSValue( LuaNumber.ToString( v.IValue() ) );
			else if( v.TtIsFloat() )
				v.SetSValue( LuaNumber.ToString( v.FltValue ) );
			else
				return false;
			return true;
		}

		private static bool IsEmptyStr( ref TValue o )
		{
			return o.TtIsString() && o.SValue().Length == 0;
		}

		/*
		** Main operation for concatenation: concat 'total' values in the stack,
		** from 'L->top - total' up to 'L->top - 1'.
		*/
		internal void V_Concat( int total )
		{
			if( total == 1 )
				return; // "all" values already concatenated
			do
			{
				int top = Top.Index;
				int n = 2; // number of elements handled in this pass (at least 2)
				if( !(Stack[top - 2].V.TtIsString() || Stack[top - 2].V.TtIsNumber()) ||
					!ToString( ref Stack[top - 1].V ) )
					T_TryConcatTM(); // may invalidate 'top'
				else if( IsEmptyStr( ref Stack[top - 1].V ) ) // second operand is empty?
					ToString( ref Stack[top - 2].V ); // result is first operand
				else if( IsEmptyStr( ref Stack[top - 2].V ) ) // first operand is empty string?
					Stack[top - 2].V.SetObj( ref Stack[top - 1].V ); // result is second op.
				else
				{
					/* at least two non-empty string values; get as many as possible */
					int tl = Stack[top - 1].V.SValue().Length;
					/* collect total length and number of strings */
					for( n = 1; n < total && ToString( ref Stack[top - n - 1].V ); n++ )
					{
						int l = Stack[top - n - 1].V.SValue().Length;
						if( l >= int.MaxValue - tl ) {
							Top = Stack[top - total]; // pop strings to avoid wasting stack
							G_RunError( "string length overflow" );
						}
						tl += l;
					}
					var sb = new StringBuilder( tl );
					for( int k = n; k >= 1; --k )
						sb.Append( Stack[top - k].V.SValue() );
					string result = sb.ToString();
					C_AllocString( result );
					Stack[top - n].V.SetSValue( result ); // create result
				}
				total -= n - 1; // got 'n' strings to create one new
				Top = Stack[Top.Index - (n - 1)]; // popped 'n' strings and pushed one
			} while( total > 1 ); // repeat until only 1 result left
		}

		/*
		** Main operation 'ra = #rb'.
		*/
		internal void V_ObjLen( int ra, ref TValue rb )
		{
			StkId tm;
			switch( rb.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
				{
					var h = rb.HValue();
					tm = FastTM( h.MetaTable, TMS.TM_LEN );
					if( tm != null ) break; // metamethod? break switch to call it
					Stack[ra].V.SetIValue( h.Length ); // else primitive len
					return;
				}
				case (int)LuaType.LUA_TSTRING:
				{
					Stack[ra].V.SetIValue( rb.SValue().Length );
					return;
				}
				default: // try metamethod
				{
					tm = T_GetTMByObj( ref rb, TMS.TM_LEN );
					if( tm.V.TtIsNil() ) // no metamethod?
						G_TypeError( ref rb, "get length of" );
					break;
				}
			}
			T_CallTMRes( ref tm.V, ref rb, ref rb, ra );
		}

		/*
		** Integer division; return 'm // n', that is, floor(m/n).
		** C division truncates its result (rounds towards zero).
		** 'floor(q) == trunc(q)' when 'q >= 0' or when 'q' is integer,
		** otherwise 'floor(q) == trunc(q) - 1'.
		*/
		internal static long V_Div( LuaState L, long m, long n )
		{
			if( unchecked((ulong)n + 1UL) <= 1UL ) { // special cases: -1 or 0
				if( n == 0 )
					L.G_RunError( "attempt to divide by zero" );
				return unchecked(0 - m); // n==-1; avoid overflow with 0x80000...//-1
			}
			else {
				long q = m / n; // perform C division
				if( (m ^ n) < 0 && m % n != 0 ) // 'm/n' would be negative non-integer?
					q -= 1; // correct result for different rounding
				return q;
			}
		}

		/*
		** Integer modulus; return 'm % n'. (Assume that C '%' with
		** negative operands follows C99 behavior. See previous comment
		** about luaV_idiv.)
		*/
		internal static long V_Mod( LuaState L, long m, long n )
		{
			if( unchecked((ulong)n + 1UL) <= 1UL ) { // special cases: -1 or 0
				if( n == 0 )
					L.G_RunError( "attempt to perform 'n%0'" );
				return 0; // m % -1 == 0; avoid overflow with 0x80000...%-1
			}
			else {
				long r = m % n;
				if( r != 0 && (r ^ n) < 0 ) // 'm/n' would be non-integer negative?
					r += n; // correct result for different rounding
				return r;
			}
		}

		/* number of bits in an integer */
		private const int NBITS = 64;

		/*
		** Shift left operation. (Shift right just negates 'y'.)
		*/
		internal static long V_ShiftL( long x, long y )
		{
			if( y < 0 ) { // shift right?
				if( y <= -NBITS ) return 0;
				else return (long)((ulong)x >> (int)-y);
			}
			else { // shift left
				if( y >= NBITS ) return 0;
				else return x << (int)y;
			}
		}

		internal static long V_ShiftR( long x, long y )
		{
			return V_ShiftL( x, unchecked(0 - y) );
		}

		// luai_numpow
		private static double NumPow( double a, double b )
		{
			return (b == 2) ? a * a : Math.Pow( a, b );
		}

		/*
		** create a new Lua closure, push it in the stack, and initialize
		** its upvalues.
		*/
		private void PushClosure( LuaProto p, LuaUpvalue[] encup, int stackBase, StkId ra )
		{
			var ncl = new LuaLClosureValue( p );
			C_Alloc( LuaGCSize.LClosure + 8L * p.Upvalues.Count );
			ra.V.SetClLValue( ncl ); // anchor new closure in stack
			for( int i = 0; i < p.Upvalues.Count; ++i ) // fill in its upvalues
			{
				if( p.Upvalues[i].InStack ) // upvalue refers to local variable?
					ncl.Upvals[i] = F_FindUpval( Stack[stackBase + p.Upvalues[i].Index] );
				else // get upvalue from enclosing function
					ncl.Upvals[i] = encup[p.Upvalues[i].Index];
			}
		}

		/*
		** finish execution of an opcode interrupted by a yield
		*/
		private void V_FinishOp()
		{
			CallInfo ci = CI;
			int stackBase = ci.FuncIndex + 1;
			var code = ci.SavedPc;
			Instruction inst = (code - 1).Value; // interrupted instruction
			OpCode op = inst.GET_OPCODE();
			switch( op ) // finish its execution
			{
				case OpCode.OP_MMBIN: case OpCode.OP_MMBINI: case OpCode.OP_MMBINK:
				{
					Top = Stack[Top.Index - 1];
					Stack[stackBase + (code - 2).Value.GETARG_A()].V.SetObj( ref Top.V );
					break;
				}
				case OpCode.OP_UNM: case OpCode.OP_BNOT: case OpCode.OP_LEN:
				case OpCode.OP_GETTABUP: case OpCode.OP_GETTABLE: case OpCode.OP_GETI:
				case OpCode.OP_GETFIELD: case OpCode.OP_SELF:
				{
					Top = Stack[Top.Index - 1];
					Stack[stackBase + inst.GETARG_A()].V.SetObj( ref Top.V );
					break;
				}
				case OpCode.OP_LT: case OpCode.OP_LE:
				case OpCode.OP_LTI: case OpCode.OP_LEI:
				case OpCode.OP_GTI: case OpCode.OP_GEI:
				case OpCode.OP_EQ: // note that 'OP_EQI'/'OP_EQK' cannot yield
				{
					bool res = !IsFalse( ref Stack[Top.Index - 1].V );
					Top = Stack[Top.Index - 1];
					Utl.Assert( ci.SavedPc.Value.GET_OPCODE() == OpCode.OP_JMP );
					if( (res ? 1 : 0) != inst.GETARG_k() ) // condition failed?
						ci.SavedPc.Index++; // skip jump instruction
					break;
				}
				case OpCode.OP_CONCAT:
				{
					int top = Top.Index - 1; // top when 'luaT_tryconcatTM' was called
					int a = inst.GETARG_A(); // first element to concatenate
					int total = top - 1 - (stackBase + a); // yet to concatenate
					Stack[top - 2].V.SetObj( ref Stack[top].V ); // put TM result in proper position
					Top = Stack[top - 1]; // top is one after last element (at top-2)
					V_Concat( total ); // concat them (may yield again)
					break;
				}
				case OpCode.OP_CLOSE: // yielded closing variables
				{
					ci.SavedPc.Index--; // repeat instruction to close other vars.
					break;
				}
				case OpCode.OP_RETURN: // yielded closing variables
				{
					int ra = stackBase + inst.GETARG_A();
					/* adjust top to signal correct number of returns, in case the
					   return is "up to top" ('isIT') */
					Top = Stack[ra + ci.NRes];
					/* repeat instruction to close other vars. and complete the return */
					ci.SavedPc.Index--;
					break;
				}
				default:
				{
					/* only these other opcodes can yield */
					Utl.Assert( op == OpCode.OP_TFORCALL || op == OpCode.OP_CALL ||
						op == OpCode.OP_TAILCALL || op == OpCode.OP_SETTABUP || op == OpCode.OP_SETTABLE ||
						op == OpCode.OP_SETI || op == OpCode.OP_SETFIELD );
					break;
				}
			}
		}

		/*
		** {==================================================================
		** Function 'luaV_execute': main interpreter loop
		** ===================================================================
		*/

		private void V_Execute( CallInfo ci )
		{
			LuaLClosureValue cl;
			List<StkId> k;
			List<Instruction> code;
			int stackBase;
			bool trap;

		startfunc:
			trap = HookMask != 0;
		returning: // trap already set
			cl = Stack[ci.FuncIndex].V.ClLValue();
			k = cl.Proto.K;
			code = cl.Proto.Code;
			if( trap )
				trap = G_TraceCall();
			stackBase = ci.FuncIndex + 1;

			/* main loop of interpreter */
			for( ;; )
			{
				if( trap ) // stack reallocation or hooks?
				{
					trap = G_TraceExec( ci ); // handle hooks
					stackBase = ci.FuncIndex + 1; // correct stack
				}
				Instruction i = code[ci.SavedPc.Index++];
				OpCode opcode = i.GET_OPCODE();
				// the A field of an 'isJ' instruction is part of its jump
				StkId ra = (opcode != OpCode.OP_JMP) ? Stack[stackBase + i.GETARG_A()] : null;

				switch( opcode )
				{
					case OpCode.OP_MOVE:
					{
						ra.V.SetObj( ref Stack[stackBase + i.GETARG_B()].V );
						break;
					}
					case OpCode.OP_LOADI:
					{
						ra.V.SetIValue( i.GETARG_sBx() );
						break;
					}
					case OpCode.OP_LOADF:
					{
						ra.V.SetFltValue( (double)i.GETARG_sBx() );
						break;
					}
					case OpCode.OP_LOADK:
					{
						ra.V.SetObj( ref k[i.GETARG_Bx()].V );
						break;
					}
					case OpCode.OP_LOADKX:
					{
						ra.V.SetObj( ref k[code[ci.SavedPc.Index].GETARG_Ax()].V );
						ci.SavedPc.Index++;
						break;
					}
					case OpCode.OP_LOADFALSE:
					{
						ra.V.SetBValue( false );
						break;
					}
					case OpCode.OP_LFALSESKIP:
					{
						ra.V.SetBValue( false );
						ci.SavedPc.Index++; // skip next instruction
						break;
					}
					case OpCode.OP_LOADTRUE:
					{
						ra.V.SetBValue( true );
						break;
					}
					case OpCode.OP_LOADNIL:
					{
						int b = i.GETARG_B();
						int r = ra.Index;
						do {
							Stack[r++].V.SetNilValue();
						} while( b-- > 0 );
						break;
					}
					case OpCode.OP_GETUPVAL:
					{
						ra.V.SetObj( ref cl.Upvals[i.GETARG_B()].V.V );
						break;
					}
					case OpCode.OP_SETUPVAL:
					{
						var uv = cl.Upvals[i.GETARG_B()];
						uv.V.V.SetObj( ref ra.V );
						break;
					}
					case OpCode.OP_GETTABUP:
					{
						StkId upval = cl.Upvals[i.GETARG_B()].V;
						StkId rc = k[i.GETARG_C()]; // key must be a short string
						if( upval.V.TtIsTable() )
						{
							var slot = upval.V.HValue().GetStr( rc.V.SValue() );
							if( !slot.V.TtIsNil() ) {
								ra.V.SetObj( ref slot.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishGet( ref upval.V, ref rc.V, ra.Index, upval.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_GETTABLE:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						StkId rc = Stack[stackBase + i.GETARG_C()];
						if( rb.V.TtIsTable() )
						{
							var slot = FastGet( rb.V.HValue(), ref rc.V );
							if( !slot.V.TtIsNil() ) {
								ra.V.SetObj( ref slot.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishGet( ref rb.V, ref rc.V, ra.Index, rb.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_GETI:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						int c = i.GETARG_C();
						if( rb.V.TtIsTable() )
						{
							var slot = rb.V.HValue().GetInt( c );
							if( !slot.V.TtIsNil() ) {
								ra.V.SetObj( ref slot.V );
								break;
							}
						}
						var key = new TValue();
						key.SetIValue( c );
						Top = Stack[ci.TopIndex];
						V_FinishGet( ref rb.V, ref key, ra.Index, rb.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_GETFIELD:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						StkId rc = k[i.GETARG_C()]; // key must be a short string
						if( rb.V.TtIsTable() )
						{
							var slot = rb.V.HValue().GetStr( rc.V.SValue() );
							if( !slot.V.TtIsNil() ) {
								ra.V.SetObj( ref slot.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishGet( ref rb.V, ref rc.V, ra.Index, rb.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_SETTABUP:
					{
						StkId upval = cl.Upvals[i.GETARG_A()].V;
						StkId rb = k[i.GETARG_B()]; // key must be a short string
						StkId rc = i.TESTARG_k() ? k[i.GETARG_C()] : Stack[stackBase + i.GETARG_C()];
						if( upval.V.TtIsTable() )
						{
							var slot = upval.V.HValue().GetStr( rb.V.SValue() );
							if( !slot.V.TtIsNil() ) {
								slot.V.SetObj( ref rc.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishSet( ref upval.V, ref rb.V, ref rc.V, upval.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_SETTABLE:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()]; // key (table is in 'ra')
						StkId rc = i.TESTARG_k() ? k[i.GETARG_C()] : Stack[stackBase + i.GETARG_C()]; // value
						if( ra.V.TtIsTable() )
						{
							var slot = FastGet( ra.V.HValue(), ref rb.V );
							if( !slot.V.TtIsNil() ) {
								slot.V.SetObj( ref rc.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishSet( ref ra.V, ref rb.V, ref rc.V, ra.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_SETI:
					{
						int c = i.GETARG_B();
						StkId rc = i.TESTARG_k() ? k[i.GETARG_C()] : Stack[stackBase + i.GETARG_C()];
						if( ra.V.TtIsTable() )
						{
							var slot = ra.V.HValue().GetInt( c );
							if( !slot.V.TtIsNil() ) {
								slot.V.SetObj( ref rc.V );
								break;
							}
						}
						var key = new TValue();
						key.SetIValue( c );
						Top = Stack[ci.TopIndex];
						V_FinishSet( ref ra.V, ref key, ref rc.V, ra.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_SETFIELD:
					{
						StkId rb = k[i.GETARG_B()]; // key must be a short string
						StkId rc = i.TESTARG_k() ? k[i.GETARG_C()] : Stack[stackBase + i.GETARG_C()];
						if( ra.V.TtIsTable() )
						{
							var slot = ra.V.HValue().GetStr( rb.V.SValue() );
							if( !slot.V.TtIsNil() ) {
								slot.V.SetObj( ref rc.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishSet( ref ra.V, ref rb.V, ref rc.V, ra.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_NEWTABLE:
					{
						int b = i.GETARG_vB(); // log2(hash size) + 1
						int c = i.GETARG_vC(); // array size
						if( b > 0 )
							b = 1 << (b - 1); // hash size is 2^(b - 1)
						if( i.TESTARG_k() ) // non-zero extra argument?
						{
							Utl.Assert( code[ci.SavedPc.Index].GETARG_Ax() != 0 );
							/* add it to array size */
							c += code[ci.SavedPc.Index].GETARG_Ax() * (Instruction.MAXARG_vC + 1);
						}
						ci.SavedPc.Index++; // skip extra argument
						Top = Stack[ra.Index + 1]; // correct top in case of emergency GC
						var t = new LuaTable( this );
						ra.V.SetHValue( t );
						if( b != 0 || c != 0 )
							t.Resize( c, b );
						C_CheckGC();
						break;
					}
					case OpCode.OP_SELF:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						StkId rc = k[i.GETARG_C()]; // key must be a short string
						Stack[ra.Index + 1].V.SetObj( ref rb.V );
						if( rb.V.TtIsTable() )
						{
							var slot = rb.V.HValue().GetStr( rc.V.SValue() ); // key must be a string
							if( !slot.V.TtIsNil() ) {
								ra.V.SetObj( ref slot.V );
								break;
							}
						}
						Top = Stack[ci.TopIndex];
						V_FinishGet( ref rb.V, ref rc.V, ra.Index, rb.V.TtIsTable() );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_ADDI:
					{
						StkId v1 = Stack[stackBase + i.GETARG_B()];
						int imm = i.GETARG_sC();
						if( v1.V.TtIsInteger() ) {
							ci.SavedPc.Index++; ra.V.SetIValue( unchecked(v1.V.IValue() + imm) );
						}
						else if( v1.V.TtIsFloat() ) {
							ci.SavedPc.Index++; ra.V.SetFltValue( v1.V.FltValue + (double)imm );
						}
						break;
					}
					case OpCode.OP_ADDK: case OpCode.OP_SUBK: case OpCode.OP_MULK:
					case OpCode.OP_MODK: case OpCode.OP_IDIVK:
					{
						if( ArithOp( ci, i.GET_OPCODE() - OpCode.OP_ADDK + OpCode.OP_ADD, ra,
								ref Stack[stackBase + i.GETARG_B()].V, ref k[i.GETARG_C()].V ) )
							ci.SavedPc.Index++;
						break;
					}
					case OpCode.OP_POWK: case OpCode.OP_DIVK:
					{
						double n1, n2;
						if( ToNumberNS( ref Stack[stackBase + i.GETARG_B()].V, out n1 ) &&
							ToNumberNS( ref k[i.GETARG_C()].V, out n2 ) )
						{
							ci.SavedPc.Index++;
							ra.V.SetFltValue( i.GET_OPCODE() == OpCode.OP_POWK ? NumPow( n1, n2 ) : n1 / n2 );
						}
						break;
					}
					case OpCode.OP_BANDK: case OpCode.OP_BORK: case OpCode.OP_BXORK:
					{
						long i1;
						long i2 = k[i.GETARG_C()].V.IValue();
						if( V_ToIntegerNS( ref Stack[stackBase + i.GETARG_B()].V, out i1, F2Imod.F2Ieq ) )
						{
							ci.SavedPc.Index++;
							switch( i.GET_OPCODE() )
							{
								case OpCode.OP_BANDK: ra.V.SetIValue( i1 & i2 ); break;
								case OpCode.OP_BORK: ra.V.SetIValue( i1 | i2 ); break;
								default: ra.V.SetIValue( i1 ^ i2 ); break;
							}
						}
						break;
					}
					case OpCode.OP_SHLI:
					{
						int ic = i.GETARG_sC();
						long ib;
						if( V_ToIntegerNS( ref Stack[stackBase + i.GETARG_B()].V, out ib, F2Imod.F2Ieq ) ) {
							ci.SavedPc.Index++; ra.V.SetIValue( V_ShiftL( ic, ib ) );
						}
						break;
					}
					case OpCode.OP_SHRI:
					{
						int ic = i.GETARG_sC();
						long ib;
						if( V_ToIntegerNS( ref Stack[stackBase + i.GETARG_B()].V, out ib, F2Imod.F2Ieq ) ) {
							ci.SavedPc.Index++; ra.V.SetIValue( V_ShiftL( ib, -ic ) );
						}
						break;
					}
					case OpCode.OP_ADD: case OpCode.OP_SUB: case OpCode.OP_MUL:
					case OpCode.OP_MOD: case OpCode.OP_IDIV:
					{
						if( ArithOp( ci, i.GET_OPCODE(), ra,
								ref Stack[stackBase + i.GETARG_B()].V, ref Stack[stackBase + i.GETARG_C()].V ) )
							ci.SavedPc.Index++;
						break;
					}
					case OpCode.OP_POW: case OpCode.OP_DIV:
					{
						double n1, n2;
						if( ToNumberNS( ref Stack[stackBase + i.GETARG_B()].V, out n1 ) &&
							ToNumberNS( ref Stack[stackBase + i.GETARG_C()].V, out n2 ) )
						{
							ci.SavedPc.Index++;
							ra.V.SetFltValue( i.GET_OPCODE() == OpCode.OP_POW ? NumPow( n1, n2 ) : n1 / n2 );
						}
						break;
					}
					case OpCode.OP_BAND: case OpCode.OP_BOR: case OpCode.OP_BXOR:
					case OpCode.OP_SHL: case OpCode.OP_SHR:
					{
						long i1, i2;
						if( V_ToIntegerNS( ref Stack[stackBase + i.GETARG_B()].V, out i1, F2Imod.F2Ieq ) &&
							V_ToIntegerNS( ref Stack[stackBase + i.GETARG_C()].V, out i2, F2Imod.F2Ieq ) )
						{
							ci.SavedPc.Index++;
							switch( i.GET_OPCODE() )
							{
								case OpCode.OP_BAND: ra.V.SetIValue( i1 & i2 ); break;
								case OpCode.OP_BOR: ra.V.SetIValue( i1 | i2 ); break;
								case OpCode.OP_BXOR: ra.V.SetIValue( i1 ^ i2 ); break;
								case OpCode.OP_SHL: ra.V.SetIValue( V_ShiftL( i1, i2 ) ); break;
								default: ra.V.SetIValue( V_ShiftR( i1, i2 ) ); break;
							}
						}
						break;
					}
					case OpCode.OP_MMBIN:
					{
						Instruction pi = code[ci.SavedPc.Index - 2]; // original arith. expression
						StkId rb = Stack[stackBase + i.GETARG_B()];
						TMS tm = (TMS)i.GETARG_C();
						int result = stackBase + pi.GETARG_A();
						Utl.Assert( OpCode.OP_ADD <= pi.GET_OPCODE() && pi.GET_OPCODE() <= OpCode.OP_SHR );
						Top = Stack[ci.TopIndex];
						T_TryBinTM( ref ra.V, ref rb.V, result, tm );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_MMBINI:
					{
						Instruction pi = code[ci.SavedPc.Index - 2]; // original arith. expression
						int imm = i.GETARG_sB();
						TMS tm = (TMS)i.GETARG_C();
						bool flip = i.GETARG_k() != 0;
						int result = stackBase + pi.GETARG_A();
						Top = Stack[ci.TopIndex];
						T_TryBiniTM( ref ra.V, imm, flip, result, tm );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_MMBINK:
					{
						Instruction pi = code[ci.SavedPc.Index - 2]; // original arith. expression
						StkId imm = k[i.GETARG_B()];
						TMS tm = (TMS)i.GETARG_C();
						bool flip = i.GETARG_k() != 0;
						int result = stackBase + pi.GETARG_A();
						Top = Stack[ci.TopIndex];
						T_TryBinAssocTM( ref ra.V, ref imm.V, flip, result, tm );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_UNM:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						double nb;
						if( rb.V.TtIsInteger() )
							ra.V.SetIValue( unchecked(0 - rb.V.IValue()) );
						else if( ToNumberNS( ref rb.V, out nb ) )
							ra.V.SetFltValue( -nb );
						else {
							Top = Stack[ci.TopIndex];
							T_TryBinTM( ref rb.V, ref rb.V, ra.Index, TMS.TM_UNM );
							trap = ci.Trap;
						}
						break;
					}
					case OpCode.OP_BNOT:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						long ib;
						if( V_ToIntegerNS( ref rb.V, out ib, F2Imod.F2Ieq ) )
							ra.V.SetIValue( ~ib );
						else {
							Top = Stack[ci.TopIndex];
							T_TryBinTM( ref rb.V, ref rb.V, ra.Index, TMS.TM_BNOT );
							trap = ci.Trap;
						}
						break;
					}
					case OpCode.OP_NOT:
					{
						ra.V.SetBValue( IsFalse( ref Stack[stackBase + i.GETARG_B()].V ) );
						break;
					}
					case OpCode.OP_LEN:
					{
						Top = Stack[ci.TopIndex];
						V_ObjLen( ra.Index, ref Stack[stackBase + i.GETARG_B()].V );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_CONCAT:
					{
						int n = i.GETARG_B(); // number of elements to concatenate
						Top = Stack[ra.Index + n]; // mark the end of concat operands
						V_Concat( n );
						C_CheckGC(); // 'V_Concat' ensures correct top
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_CLOSE:
					{
						Utl.Assert( i.GETARG_B() == 0 ); // 'close must be alive
						Top = Stack[ci.TopIndex];
						F_Close( ra.Index, ThreadStatus.LUA_OK, true );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_TBC:
					{
						/* create new to-be-closed upvalue */
						Top = Stack[ci.TopIndex];
						F_NewTbcUpval( ra );
						break;
					}
					case OpCode.OP_JMP:
					{
						ci.SavedPc.Index += i.GETARG_sJ();
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_EQ:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						Top = Stack[ci.TopIndex];
						bool cond = V_EqualObj( ref ra.V, ref rb.V, false );
						trap = ci.Trap;
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_LT:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						bool cond;
						if( ra.V.TtIsInteger() && rb.V.TtIsInteger() )
							cond = ra.V.IValue() < rb.V.IValue();
						else if( ra.V.TtIsNumber() && rb.V.TtIsNumber() )
							cond = LTNum( ref ra.V, ref rb.V );
						else {
							Top = Stack[ci.TopIndex];
							cond = LessThanOthers( ref ra.V, ref rb.V );
							trap = ci.Trap;
						}
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_LE:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						bool cond;
						if( ra.V.TtIsInteger() && rb.V.TtIsInteger() )
							cond = ra.V.IValue() <= rb.V.IValue();
						else if( ra.V.TtIsNumber() && rb.V.TtIsNumber() )
							cond = LENum( ref ra.V, ref rb.V );
						else {
							Top = Stack[ci.TopIndex];
							cond = LessEqualOthers( ref ra.V, ref rb.V );
							trap = ci.Trap;
						}
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_EQK:
					{
						/* basic types do not use '__eq'; we can use raw equality */
						bool cond = V_RawEqualObj( ref ra.V, ref k[i.GETARG_B()].V );
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_EQI:
					{
						bool cond;
						int im = i.GETARG_sB();
						if( ra.V.TtIsInteger() )
							cond = ra.V.IValue() == im;
						else if( ra.V.TtIsFloat() )
							cond = ra.V.FltValue == (double)im;
						else
							cond = false; // other types cannot be equal to a number
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_LTI: case OpCode.OP_LEI:
					case OpCode.OP_GTI: case OpCode.OP_GEI:
					{
						bool cond;
						int im = i.GETARG_sB();
						OpCode op = i.GET_OPCODE();
						if( ra.V.TtIsInteger() )
							cond = OrderI( op, ra.V.IValue(), im );
						else if( ra.V.TtIsFloat() )
							cond = OrderF( op, ra.V.FltValue, (double)im );
						else {
							bool isf = i.GETARG_C() != 0;
							bool inv = op == OpCode.OP_GTI || op == OpCode.OP_GEI;
							TMS tm = (op == OpCode.OP_LTI || op == OpCode.OP_GTI) ? TMS.TM_LT : TMS.TM_LE;
							Top = Stack[ci.TopIndex];
							cond = T_CallOrderiTM( ref ra.V, im, inv, isf, tm );
							trap = ci.Trap;
						}
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_TEST:
					{
						bool cond = !IsFalse( ref ra.V );
						DoCondJump( ci, code, i, cond, ref trap );
						break;
					}
					case OpCode.OP_TESTSET:
					{
						StkId rb = Stack[stackBase + i.GETARG_B()];
						if( IsFalse( ref rb.V ) == (i.GETARG_k() != 0) )
							ci.SavedPc.Index++;
						else {
							ra.V.SetObj( ref rb.V );
							DoNextJump( ci, code, ref trap );
						}
						break;
					}
					case OpCode.OP_CALL:
					{
						CallInfo newci;
						int b = i.GETARG_B();
						int nresults = i.GETARG_C() - 1;
						if( b != 0 ) // fixed number of arguments?
							Top = Stack[ra.Index + b]; // top signals number of arguments
						/* else previous instruction set top */
						if( (newci = D_PreCall( ra, nresults )) == null )
							trap = ci.Trap; // C call; nothing else to be done
						else { // Lua call: run function in this same C frame
							ci = newci;
							goto startfunc;
						}
						break;
					}
					case OpCode.OP_TAILCALL:
					{
						int b = i.GETARG_B(); // number of arguments + 1 (function)
						int n; // number of results when calling a C function
						int nparams1 = i.GETARG_C();
						/* delta is virtual 'func' - real 'func' (vararg functions) */
						int delta = (nparams1 != 0) ? ci.NExtraArgs + nparams1 : 0;
						if( b != 0 )
							Top = Stack[ra.Index + b];
						else // previous instruction set top
							b = Top.Index - ra.Index;
						if( i.TESTARG_k() )
						{
							F_CloseUpval( stackBase ); // close upvalues from current call
							Utl.Assert( TbcList.Count == 0 || TbcList[TbcList.Count - 1] < stackBase ); // no pending tbc variables
							Utl.Assert( stackBase == ci.FuncIndex + 1 );
						}
						if( (n = D_PreTailCall( ci, ra, b, delta )) < 0 ) // Lua function?
							goto startfunc; // execute the callee
						else // C function?
						{
							ci.FuncIndex -= delta; // restore 'func' (if vararg)
							D_PosCall( ci, n ); // finish caller
							trap = ci.Trap; // 'luaD_poscall' can change hooks
							goto ret; // caller returns after the tail call
						}
					}
					case OpCode.OP_RETURN:
					{
						int n = i.GETARG_B() - 1; // number of results
						int nparams1 = i.GETARG_C();
						if( n < 0 ) // not fixed?
							n = Top.Index - ra.Index; // get what is available
						if( i.TESTARG_k() ) // may there be open upvalues?
						{
							ci.NRes = n; // save number of returns
							if( Top.Index < ci.TopIndex )
								Top = Stack[ci.TopIndex];
							F_Close( stackBase, CLOSEKTOP, true );
							trap = ci.Trap;
						}
						if( nparams1 != 0 ) // vararg function?
							ci.FuncIndex -= ci.NExtraArgs + nparams1;
						Top = Stack[ra.Index + n]; // set call for 'luaD_poscall'
						D_PosCall( ci, n );
						trap = ci.Trap; // 'luaD_poscall' can change hooks
						goto ret;
					}
					case OpCode.OP_RETURN0:
					{
						if( HookMask != 0 )
						{
							Top = ra;
							D_PosCall( ci, 0 ); // no hurry...
							trap = true;
						}
						else // do the 'poscall' here
						{
							CI = ci.Previous; // back to caller
							Top = Stack[stackBase - 1];
							for( int nres = ci.NumResults; nres > 0; nres-- )
								StkId.inc( ref Top ).V.SetNilValue(); // all results are nil
						}
						goto ret;
					}
					case OpCode.OP_RETURN1:
					{
						if( HookMask != 0 )
						{
							Top = Stack[ra.Index + 1];
							D_PosCall( ci, 1 ); // no hurry...
							trap = true;
						}
						else // do the 'poscall' here
						{
							int nres = ci.NumResults;
							CI = ci.Previous; // back to caller
							if( nres == 0 )
								Top = Stack[stackBase - 1]; // asked for no results
							else
							{
								Stack[stackBase - 1].V.SetObj( ref ra.V ); // at least this result
								Top = Stack[stackBase];
								for( ; nres > 1; nres-- )
									StkId.inc( ref Top ).V.SetNilValue(); // complete missing results
							}
						}
						goto ret;
					}
					case OpCode.OP_FORLOOP:
					{
						if( Stack[ra.Index + 1].V.TtIsInteger() ) // integer loop?
						{
							ulong count = unchecked((ulong)ra.V.IValue());
							if( count > 0 ) // still more iterations?
							{
								long step = Stack[ra.Index + 1].V.IValue();
								long idx = Stack[ra.Index + 2].V.IValue(); // control variable
								ra.V.SetIValue( unchecked((long)(count - 1)) ); // update counter
								idx = unchecked(idx + step); // add step to index
								Stack[ra.Index + 2].V.SetIValue( idx ); // update control variable
								ci.SavedPc.Index -= i.GETARG_Bx(); // jump back
							}
						}
						else if( FloatForLoop( ra.Index ) ) // float loop
							ci.SavedPc.Index -= i.GETARG_Bx(); // jump back
						trap = ci.Trap; // allows a signal to break the loop
						break;
					}
					case OpCode.OP_FORPREP:
					{
						Top = Stack[ci.TopIndex]; // in case of errors
						if( ForPrep( ra.Index ) )
							ci.SavedPc.Index += i.GETARG_Bx() + 1; // skip the loop
						break;
					}
					case OpCode.OP_TFORPREP:
					{
						/* before: 'ra' has the iterator function, 'ra + 1' has the state,
						   'ra + 2' has the initial value for the control variable, and
						   'ra + 3' has the closing variable. This opcode then swaps the
						   control and the closing variables and marks the closing variable
						   as to-be-closed.
						*/
						var temp = new TValue(); // to swap control and closing variables
						temp.SetObj( ref Stack[ra.Index + 3].V );
						Stack[ra.Index + 3].V.SetObj( ref Stack[ra.Index + 2].V );
						Stack[ra.Index + 2].V.SetObj( ref temp );
						/* create to-be-closed upvalue (if closing var. is not nil) */
						Top = Stack[ci.TopIndex];
						F_NewTbcUpval( Stack[ra.Index + 2] );
						ci.SavedPc.Index += i.GETARG_Bx(); // go to end of the loop
						i = code[ci.SavedPc.Index++]; // fetch next instruction
						Utl.Assert( i.GET_OPCODE() == OpCode.OP_TFORCALL && ra.Index == stackBase + i.GETARG_A() );
						TForCall( ci, code, i, ra );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_TFORCALL:
					{
						TForCall( ci, code, i, ra );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_TFORLOOP:
					{
						if( !Stack[ra.Index + 3].V.TtIsNil() ) // continue loop?
							ci.SavedPc.Index -= i.GETARG_Bx(); // jump back
						break;
					}
					case OpCode.OP_SETLIST:
					{
						int n = i.GETARG_vB();
						long last = i.GETARG_vC();
						var h = ra.V.HValue();
						if( n == 0 )
							n = Top.Index - ra.Index - 1; // get up to the top
						else
							Top = Stack[ci.TopIndex]; // correct top in case of emergency GC
						last += n;
						if( i.TESTARG_k() )
						{
							last += (long)code[ci.SavedPc.Index].GETARG_Ax() * (Instruction.MAXARG_vC + 1);
							ci.SavedPc.Index++;
						}
						if( last > h.ArraySize ) // needs more space?
							h.ResizeArray( (int)last ); // preallocate it at once
						for( ; n > 0; n-- )
						{
							h.SetInt( last, ref Stack[ra.Index + n].V );
							last--;
						}
						break;
					}
					case OpCode.OP_CLOSURE:
					{
						LuaProto p = cl.Proto.P[i.GETARG_Bx()];
						Top = Stack[ci.TopIndex];
						PushClosure( p, cl.Upvals, stackBase, ra );
						Top = Stack[ra.Index + 1];
						C_CheckGC();
						break;
					}
					case OpCode.OP_VARARG:
					{
						int n = i.GETARG_C() - 1; // required results (-1 means all)
						int vatab = i.GETARG_k() != 0 ? i.GETARG_B() : -1;
						Top = Stack[ci.TopIndex];
						T_GetVarargs( ci, ra.Index, n, vatab );
						trap = ci.Trap;
						break;
					}
					case OpCode.OP_GETVARG:
					{
						T_GetVararg( ci, ra, ref Stack[stackBase + i.GETARG_C()].V );
						break;
					}
					case OpCode.OP_ERRNNIL:
					{
						if( !ra.V.TtIsNil() )
						{
							Top = Stack[ci.TopIndex];
							G_ErrNNil( cl, i.GETARG_Bx() );
						}
						break;
					}
					case OpCode.OP_VARARGPREP:
					{
						T_AdjustVarargs( ci, cl.Proto );
						trap = ci.Trap;
						if( trap ) // previous "Protect" updated trap
						{
							D_HookCall( ci );
							OldPc = 1; // next opcode will be seen as a "new" line
						}
						stackBase = ci.FuncIndex + 1; // function has new base after adjustment
						break;
					}
					case OpCode.OP_EXTRAARG:
					{
						Utl.Assert( false );
						break;
					}
				}
				continue;

			ret: // return from a Lua function
				if( (ci.CallStatus & CallStatus.CIST_FRESH) != 0 )
					return; // end this frame
				else
				{
					ci = ci.Previous;
					goto returning; // continue running caller in this frame
				}
			}
		}

		// The arithmetic operations over integers and floats: false when an
		// operand is not a number, for the next OP_MMBIN to do
		private bool ArithOp( CallInfo ci, OpCode op, StkId ra, ref TValue v1, ref TValue v2 )
		{
			if( v1.TtIsInteger() && v2.TtIsInteger() )
			{
				long i1 = v1.IValue(); long i2 = v2.IValue();
				switch( op )
				{
					case OpCode.OP_ADD: ra.V.SetIValue( unchecked(i1 + i2) ); break;
					case OpCode.OP_SUB: ra.V.SetIValue( unchecked(i1 - i2) ); break;
					case OpCode.OP_MUL: ra.V.SetIValue( unchecked(i1 * i2) ); break;
					case OpCode.OP_MOD:
						Top = Stack[ci.TopIndex]; // in case of division by 0
						ra.V.SetIValue( V_Mod( this, i1, i2 ) );
						break;
					default:
						Top = Stack[ci.TopIndex]; // in case of division by 0
						ra.V.SetIValue( V_Div( this, i1, i2 ) );
						break;
				}
				return true;
			}
			double n1, n2;
			if( ToNumberNS( ref v1, out n1 ) && ToNumberNS( ref v2, out n2 ) )
			{
				switch( op )
				{
					case OpCode.OP_ADD: ra.V.SetFltValue( n1 + n2 ); break;
					case OpCode.OP_SUB: ra.V.SetFltValue( n1 - n2 ); break;
					case OpCode.OP_MUL: ra.V.SetFltValue( n1 * n2 ); break;
					case OpCode.OP_MOD: ra.V.SetFltValue( NumMod( n1, n2 ) ); break;
					default: ra.V.SetFltValue( Math.Floor( n1 / n2 ) ); break;
				}
				return true;
			}
			return false;
		}

		private static bool OrderI( OpCode op, long a, long b )
		{
			switch( op )
			{
				case OpCode.OP_LTI: return a < b;
				case OpCode.OP_LEI: return a <= b;
				case OpCode.OP_GTI: return a > b;
				default: return a >= b;
			}
		}

		private static bool OrderF( OpCode op, double a, double b )
		{
			switch( op )
			{
				case OpCode.OP_LTI: return a < b;
				case OpCode.OP_LEI: return a <= b;
				case OpCode.OP_GTI: return a > b;
				default: return a >= b;
			}
		}

		/*
		** do a conditional jump: skip next instruction if 'cond' is not what
		** was expected (parameter 'k'), else do next instruction, which must
		** be a jump.
		*/
		private static void DoCondJump( CallInfo ci, List<Instruction> code, Instruction i,
			bool cond, ref bool trap )
		{
			if( (cond ? 1 : 0) != i.GETARG_k() )
				ci.SavedPc.Index++;
			else
				DoNextJump( ci, code, ref trap );
		}

		/* for test instructions, execute the jump instruction that follows it */
		private static void DoNextJump( CallInfo ci, List<Instruction> code, ref bool trap )
		{
			Instruction ni = code[ci.SavedPc.Index];
			ci.SavedPc.Index += ni.GETARG_sJ() + 1;
			trap = ci.Trap;
		}

		// OP_TFORCALL, and the OP_TFORLOOP that follows it
		private void TForCall( CallInfo ci, List<Instruction> code, Instruction i, StkId ra )
		{
			/* 'ra' has the iterator function, 'ra + 1' has the state,
			   'ra + 2' has the closing variable, and 'ra + 3' has the control
			   variable. The call will use the stack starting at 'ra + 3',
			   so that it preserves the first three values, and the first
			   return will be the new value for the control variable.
			*/
			int r = ra.Index;
			Stack[r + 5].V.SetObj( ref Stack[r + 3].V ); // copy the control variable
			Stack[r + 4].V.SetObj( ref Stack[r + 1].V ); // copy state
			Stack[r + 3].V.SetObj( ref Stack[r].V ); // copy function
			Top = Stack[r + 3 + 3];
			D_Call( Stack[r + 3], i.GETARG_C() ); // do the call
			i = code[ci.SavedPc.Index++]; // go to next instruction
			Utl.Assert( i.GET_OPCODE() == OpCode.OP_TFORLOOP && r == ci.FuncIndex + 1 + i.GETARG_A() );
			/* OP_TFORLOOP */
			if( !Stack[r + 3].V.TtIsNil() ) // continue loop?
				ci.SavedPc.Index -= i.GETARG_Bx(); // jump back
		}

	}

}
