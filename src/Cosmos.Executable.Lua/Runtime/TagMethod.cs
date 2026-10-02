// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	/*
	* WARNING: if you change the order of this enumeration,
	* grep "ORDER TM" and "ORDER OP"
	*/
	internal enum TMS
	{
		TM_INDEX,
		TM_NEWINDEX,
		TM_GC,
		TM_MODE,
		TM_LEN,
		TM_EQ,	/* last tag method with fast access */
		TM_ADD,
		TM_SUB,
		TM_MUL,
		TM_MOD,
		TM_POW,
		TM_DIV,
		TM_IDIV,
		TM_BAND,
		TM_BOR,
		TM_BXOR,
		TM_SHL,
		TM_SHR,
		TM_UNM,
		TM_BNOT,
		TM_LT,
		TM_LE,
		TM_CONCAT,
		TM_CALL,
		TM_CLOSE,
		TM_N		/* number of elements in the enum */
	}

	// ltm.c of Lua 5.4: tag methods
	internal partial class LuaState
	{
		// luaT_eventname, ORDER TM
		private static readonly string[] TagMethodNames = {
			"__index", "__newindex",
			"__gc", "__mode", "__len", "__eq",
			"__add", "__sub", "__mul", "__mod", "__pow",
			"__div", "__idiv",
			"__band", "__bor", "__bxor", "__shl", "__shr",
			"__unm", "__bnot", "__lt", "__le",
			"__concat", "__call", "__close",
		};

		internal static string GetTagMethodName( TMS tm )
		{
			return TagMethodNames[(int)tm];
		}

		/*
		** Return the name of the type of an object. For tables and userdata
		** with metatable, use their '__name' metafield, if present.
		*/
		internal string ObjTypeName( ref TValue o )
		{
			LuaTable mt = null;
			if( o.TtIsTable() )
				mt = o.HValue().MetaTable;
			else if( o.Tt == (int)LuaType.LUA_TUSERDATA )
				mt = o.RawUValue().MetaTable;
			if( mt != null )
			{
				var name = mt.GetStr( "__name" );
				if( name.V.TtIsString() ) // is '__name' a string?
					return name.V.SValue(); // use it as type name
			}
			return TypeName( (LuaType)o.BaseTt() ); // else use standard type name
		}

		/*
		** function to be used with macro "fasttm": optimized for absence of
		** tag methods
		*/
		private StkId T_GetTM( LuaTable events, TMS tm )
		{
			var res = events.GetStr( GetTagMethodName( tm ) );
			Utl.Assert( tm <= TMS.TM_EQ );
			if( res.V.TtIsNil() ) // no tag method?
			{
				events.NoTagMethodFlags |= 1u << (int)tm; // cache this fact
				return null;
			}
			else
				return res;
		}

		private StkId FastTM( LuaTable et, TMS tm )
		{
			if( et == null )
				return null;
			if( (et.NoTagMethodFlags & (1u << (int)tm)) != 0u )
				return null;
			return T_GetTM( et, tm );
		}

		private StkId T_GetTMByObj( ref TValue o, TMS tm )
		{
			LuaTable mt;
			switch( o.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
					mt = o.HValue().MetaTable;
					break;
				case (int)LuaType.LUA_TUSERDATA:
					mt = o.RawUValue().MetaTable;
					break;
				default:
					mt = G.MetaTables[o.BaseTt()];
					break;
			}
			return (mt != null)
				 ? mt.GetStr( GetTagMethodName( tm ) )
				 : TheNilValue;
		}

		private void T_CallTM( ref TValue f, ref TValue p1, ref TValue p2, ref TValue p3 )
		{
			StkId func = Top;
			func.V.SetObj( ref f ); // push function (assume EXTRA_STACK)
			Stack[func.Index + 1].V.SetObj( ref p1 ); // 1st argument
			Stack[func.Index + 2].V.SetObj( ref p2 ); // 2nd argument
			Stack[func.Index + 3].V.SetObj( ref p3 ); // 3rd argument
			Top = Stack[func.Index + 4];
			/* metamethod may yield only when called from Lua code */
			if( CI.IsLuaCode )
				D_Call( func, 0 );
			else
				D_CallNoYield( func, 0 );
		}

		private void T_CallTMRes( ref TValue f, ref TValue p1, ref TValue p2, int res )
		{
			StkId func = Top;
			func.V.SetObj( ref f ); // push function (assume EXTRA_STACK)
			Stack[func.Index + 1].V.SetObj( ref p1 ); // 1st argument
			Stack[func.Index + 2].V.SetObj( ref p2 ); // 2nd argument
			Top = Stack[func.Index + 3];
			/* metamethod may yield only when called from Lua code */
			if( CI.IsLuaCode )
				D_Call( func, 1 );
			else
				D_CallNoYield( func, 1 );
			Top = Stack[Top.Index - 1];
			Stack[res].V.SetObj( ref Top.V ); // move result to its place
		}

		private bool CallBinTM( ref TValue p1, ref TValue p2, int res, TMS ev )
		{
			var tm = T_GetTMByObj( ref p1, ev ); // try first operand
			if( tm.V.TtIsNil() )
				tm = T_GetTMByObj( ref p2, ev ); // try second operand
			if( tm.V.TtIsNil() )
				return false;
			T_CallTMRes( ref tm.V, ref p1, ref p2, res );
			return true;
		}

		private void T_TryBinTM( ref TValue p1, ref TValue p2, int res, TMS ev )
		{
			if( !CallBinTM( ref p1, ref p2, res, ev ) )
			{
				switch( ev )
				{
					case TMS.TM_BAND: case TMS.TM_BOR: case TMS.TM_BXOR:
					case TMS.TM_SHL: case TMS.TM_SHR: case TMS.TM_BNOT: {
						if( p1.TtIsNumber() && p2.TtIsNumber() )
							G_ToIntError( ref p1, ref p2 );
						else
							G_OpIntError( ref p1, ref p2, "perform bitwise operation on" );
						break;
					}
					default:
						G_OpIntError( ref p1, ref p2, "perform arithmetic on" );
						break;
				}
			}
		}

		private void T_TryConcatTM()
		{
			StkId top = Top;
			if( !CallBinTM( ref Stack[top.Index - 2].V, ref Stack[top.Index - 1].V,
					top.Index - 2, TMS.TM_CONCAT ) )
				G_ConcatError( ref Stack[top.Index - 2].V, ref Stack[top.Index - 1].V );
		}

		private void T_TryBinAssocTM( ref TValue p1, ref TValue p2, bool flip,
			int res, TMS ev )
		{
			if( flip )
				T_TryBinTM( ref p2, ref p1, res, ev );
			else
				T_TryBinTM( ref p1, ref p2, res, ev );
		}

		private void T_TryBiniTM( ref TValue p1, long i2, bool flip, int res, TMS ev )
		{
			var aux = new TValue();
			aux.SetIValue( i2 );
			T_TryBinAssocTM( ref p1, ref aux, flip, res, ev );
		}

		/*
		** Calls an order tag method.
		** For lessequal, LUA_COMPAT_LT_LE keeps compatibility with old
		** behavior: if there is no '__le', try '__lt', based on l <= r iff
		** !(r < l) (assuming a total order). If the metamethod yields during
		** this substitution, the continuation has to know about it (to negate
		** the result of r<l); bit CIST_LEQ in the call status keeps that
		** information.
		*/
		private bool T_CallOrderTM( ref TValue p1, ref TValue p2, TMS ev )
		{
			if( CallBinTM( ref p1, ref p2, Top.Index, ev ) ) // try original event
				return !IsFalse( ref Top.V );
			else if( ev == TMS.TM_LE )
			{
				/* try '!(p2 < p1)' for '(p1 <= p2)' */
				CI.CallStatus |= CallStatus.CIST_LEQ; // mark it is doing 'lt' for 'le'
				if( CallBinTM( ref p2, ref p1, Top.Index, TMS.TM_LT ) )
				{
					CI.CallStatus ^= CallStatus.CIST_LEQ; // clear mark
					return IsFalse( ref Top.V );
				}
				/* else error will remove this 'ci'; no need to clear mark */
			}
			G_OrderError( ref p1, ref p2 ); // no metamethod found
			return false; // to avoid warnings
		}

		private bool T_CallOrderiTM( ref TValue p1, int v2, bool flip, bool isfloat, TMS ev )
		{
			var aux = new TValue();
			if( isfloat )
				aux.SetFltValue( (double)v2 );
			else
				aux.SetIValue( v2 );
			if( flip ) // arguments were exchanged?
				return T_CallOrderTM( ref aux, ref p1, ev ); // correct them
			else
				return T_CallOrderTM( ref p1, ref aux, ev );
		}

		private void T_AdjustVarargs( int nfixparams, CallInfo ci, LuaProto p )
		{
			int actual = Top.Index - ci.FuncIndex - 1; // number of arguments
			int nextra = actual - nfixparams; // number of extra arguments
			ci.NExtraArgs = nextra;
			D_CheckStack( p.MaxStackSize + 1 );
			/* copy function to the top of the stack */
			StkId.inc( ref Top ).V.SetObj( ref Stack[ci.FuncIndex].V );
			/* move fixed parameters to the top of the stack */
			for( int i = 1; i <= nfixparams; i++ )
			{
				StkId.inc( ref Top ).V.SetObj( ref Stack[ci.FuncIndex + i].V );
				Stack[ci.FuncIndex + i].V.SetNilValue(); // erase original parameter (for GC)
			}
			ci.FuncIndex += actual + 1;
			ci.TopIndex += actual + 1;
			Utl.Assert( Top.Index <= ci.TopIndex && ci.TopIndex <= StackLast );
		}

		private void T_GetVarargs( CallInfo ci, int where, int wanted )
		{
			int i;
			int nextra = ci.NExtraArgs;
			if( wanted < 0 )
			{
				wanted = nextra; // get all extra arguments available
				D_CheckStack( nextra ); // ensure stack space
				Top = Stack[where + nextra]; // next instruction will need top
			}
			for( i = 0; i < wanted && i < nextra; i++ )
				Stack[where + i].V.SetObj( ref Stack[ci.FuncIndex - nextra + i].V );
			for( ; i < wanted; i++ ) // complete required results with nil
				Stack[where + i].V.SetNilValue();
		}

	}

}
