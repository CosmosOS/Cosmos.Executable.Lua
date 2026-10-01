// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	// grep `NoTagMethodFlags' if num of TMS >= 32
	internal enum TMS
	{
		TM_INDEX,
		TM_NEWINDEX,
		TM_GC,
		TM_MODE,
		TM_LEN,
		TM_EQ,	/* last tag method with fast access */
		TM_ADD,	/* ORDER OP */
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
		TM_N		/* number of elements in the enum */
	}

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
			"__concat", "__call",
		};

		private string GetTagMethodName( TMS tm )
		{
			return TagMethodNames[(int)tm];
		}

		// luaT_objtypename: the type a message names, the '__name' of the
		// metatable of a table or a userdata if it is a string
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
				if( name.V.TtIsString() )
					return name.V.SValue();
			}
			return TypeName( (LuaType)o.BaseTt() );
		}

		private StkId T_GetTM( LuaTable mt, TMS tm )
		{
			if( mt == null )
				return null;

			var res = mt.GetStr( GetTagMethodName( tm ) );
			if(res.V.TtIsNil()) // no tag method?
			{
				// cache this fact
				mt.NoTagMethodFlags |= 1u << (int)tm;
				return null;
			}
			else
				return res;
		}

		private StkId T_GetTMByObj( ref TValue o, TMS tm )
		{
			LuaTable mt = null;

			switch( o.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
				{
					var tbl = o.HValue();
					mt = tbl.MetaTable;
					break;
				}
				case (int)LuaType.LUA_TUSERDATA:
				{
					var ud = o.RawUValue();
					mt = ud.MetaTable;
					break;
				}
				default:
				{
					mt = G.MetaTables[o.BaseTt()];
					break;
				}
			}
			return (mt != null)
				 ? mt.GetStr( GetTagMethodName( tm ) )
				 : TheNilValue;
		}

	}

}

