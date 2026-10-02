// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML

using System;

namespace Cosmos.Executable.Lua
{
	public interface ILoadInfo
	{
		int ReadByte();
		int PeekByte();
	}

	public delegate int CSharpFunctionDelegate(ILuaState state);
	public interface ILuaAPI
	{
		ILuaState NewThread();
		ThreadStatus CloseThread( ILuaState from );

		ThreadStatus Load( ILoadInfo loadinfo, string name, string mode );
		DumpStatus Dump( LuaWriter writeFunc, bool strip );

		ThreadStatus GetContext( out int context );
		void Call( int numArgs, int numResults );
		void CallK( int numArgs, int numResults,
			int context, CSharpFunctionDelegate continueFunc );
		ThreadStatus PCall( int numArgs, int numResults, int errFunc);
		ThreadStatus PCallK( int numArgs, int numResults, int errFunc,
			int context, CSharpFunctionDelegate continueFunc );

		ThreadStatus Resume( ILuaState from, int numArgs, out int numResults );
		int Yield( int numResults );
		int YieldK( int numResults,
			int context, CSharpFunctionDelegate continueFunc );
		bool IsYieldable();

		void Warning( string msg, bool toCont );

		int  AbsIndex( int index );
		int  GetTop();
		void SetTop( int top );

		void Remove( int index );
		void Insert( int index );
		void Replace( int index );
		void Copy( int fromIndex, int toIndex );
		void XMove( ILuaState to, int n );

		bool CheckStack( int size );
		bool GetStack( int level, LuaDebug ar );
		int  Error();

		int  UpvalueIndex( int i );
		string GetUpvalue( int funcIndex, int n );
		string SetUpvalue( int funcIndex, int n );

		void CreateTable( int narray, int nrec );
		void NewTable();
		bool Next( int index );
		LuaType RawGetI( int index, long n );
		void RawSetI( int index, long n );
		LuaType RawGet( int index );
		void RawSet( int index );
		LuaType GetField( int index, string key );
		void SetField( int index, string key );
		LuaType GetTable( int index );
		void SetTable( int index );
		LuaType GetI( int index, long n );
		void SetI( int index, long n );

		void Concat( int n );
		void Arith( LuaOp op );
		void Rotate( int index, int n );
		int StringToNumber( string s );

		void ToClose( int index );
		void CloseSlot( int index );

		LuaType Type( int index );
		string TypeName( LuaType t );
		bool IsNil( int index );
		bool IsNone( int index );
		bool IsNoneOrNil( int index );
		bool IsString( int index );
		bool IsNumber( int index );
		bool IsInteger( int index );
		bool IsTable( int index );
		bool IsFunction( int index );

		bool Compare( int index1, int index2, LuaEq op );
		bool RawEqual( int index1, int index2 );
		int  RawLen( int index );
		void Len( int index );

		void PushNil();
		void PushBoolean( bool b );
		void PushNumber( double n );
		void PushInteger( long n );
		string PushString( string s );
		void PushCSharpFunction( CSharpFunctionDelegate f );
		void PushCSharpClosure( CSharpFunctionDelegate f, int n );
		void PushValue( int index );
		void PushGlobalTable();
		void PushLightUserData( object o );
		void NewUserData( object o );
		void NewUserDataUV( object o, int nuvalue );
		LuaType GetIUserValue( int index, int n );
		bool SetIUserValue( int index, int n );
		bool PushThread();

		void Pop( int n );

		bool GetMetaTable( int index );
		bool SetMetaTable( int index );

		LuaType GetGlobal( string name );
		void SetGlobal( string name );

		string 	ToString( int index );
		double 	ToNumberX( int index, out bool isnum );
		double 	ToNumber( int index );
		long	ToIntegerX( int index, out bool isnum );
		long	ToInteger( int index );
		bool   	ToBoolean( int index );
		object 	ToObject( int index );
		object  ToUserData( int index );
		ILuaState	ToThread( int index );

		ThreadStatus	Status { get; }
	}

	public interface ILuaState : ILuaAPI, ILuaAuxLib
	{
	}

	public static class LuaAPI
	{
		public static ILuaState NewState()
		{
			return LuaState.NewAuxState();
		}
	}

	internal delegate void PFuncDelegate<T>(ref T ud);

	// lapi.c of Lua 5.4: the C# API
	internal partial class LuaState : ILuaState
	{
		/* test for upvalues */
		private static bool IsUpvalue( int i )
		{
			return i < LuaDef.LUA_REGISTRYINDEX;
		}

		/*
		** Convert an acceptable index to a pointer to its respective value.
		** Non-valid indices return false (as the reference returns the
		** 'nilvalue', which is never a valid value).
		*/
		private bool Index2Addr( int index, out StkId addr )
		{
			CallInfo ci = CI;
			if( index > 0 )
			{
				var addrIndex = ci.FuncIndex + index;
				Utl.ApiCheck( index <= ci.TopIndex - (ci.FuncIndex + 1), "unacceptable index" );
				if( addrIndex >= Top.Index ) {
					addr = default(StkId);
					return false;
				}

				addr = Stack[addrIndex];
				return true;
			}
			else if( !IsUpvalue( index ) && index != LuaDef.LUA_REGISTRYINDEX ) // negative index
			{
				Utl.ApiCheck( index != 0 && -index <= Top.Index - (ci.FuncIndex + 1), "invalid index" );
				addr = Stack[Top.Index + index];
				return true;
			}
			else if( index == LuaDef.LUA_REGISTRYINDEX )
			{
				addr = G.Registry;
				return true;
			}
			// upvalues
			else
			{
				index = LuaDef.LUA_REGISTRYINDEX - index;
				Utl.ApiCheck( index <= LuaLimits.MAXUPVAL + 1, "upvalue index too large" );
				var func = Stack[ci.FuncIndex];
				Utl.Assert( func.V.TtIsFunction() );

				if( func.V.ClIsLcsClosure() ) { // light C# function?
					addr = default(StkId);
					return false; // it has no upvalues
				}

				Utl.Assert( func.V.ClIsCsClosure() );
				var clcs = func.V.ClCsValue();
				if( clcs.Upvals == null || index > clcs.Upvals.Length ) {
					addr = default(StkId);
					return false;
				}

				addr = clcs.Upvals[index-1];
				return true;
			}
		}

		/*
		** Convert a valid actual index (not a pseudo-index) to its address.
		*/
		private StkId Index2Stack( int index )
		{
			CallInfo ci = CI;
			if( index > 0 )
			{
				int o = ci.FuncIndex + index;
				Utl.ApiCheck( o < Top.Index, "invalid index" );
				return Stack[o];
			}
			else // non-positive index
			{
				Utl.ApiCheck( index != 0 && -index <= Top.Index - (ci.FuncIndex + 1), "invalid index" );
				Utl.ApiCheck( !IsUpvalue( index ) && index != LuaDef.LUA_REGISTRYINDEX, "invalid index" );
				return Stack[Top.Index + index];
			}
		}

		bool ILuaAPI.CheckStack( int n )
		{
			bool res;
			CallInfo ci = CI;
			Utl.ApiCheck( n >= 0, "negative 'n'" );
			if( StackLast - Top.Index > n ) // stack large enough?
				res = true; // yes; check is OK
			else // need to grow stack
				res = D_GrowStack( n, false );
			if( res && ci.TopIndex < Top.Index + n )
				ci.TopIndex = Top.Index + n; // adjust frame top
			return res;
		}

		void ILuaAPI.XMove( ILuaState to, int n )
		{
			var toLua = to as LuaState;
			if( (LuaState)this == toLua )
				return;

			Utl.ApiCheckNumElems( this, n );
			Utl.ApiCheck( G == toLua.G, "moving among independent states" );
			Utl.ApiCheck( toLua.CI.TopIndex - toLua.Top.Index >= n, "stack overflow" );

			int index = Top.Index - n;
			Top = Stack[index];
			for( int i=0; i<n; ++i )
				{ StkId.inc( ref toLua.Top ).V.SetObj( ref Stack[index+i].V ); }
		}

		/*
		** basic stack manipulation
		*/

		/*
		** convert an acceptable stack index into an absolute index
		*/
		int ILuaAPI.AbsIndex( int index )
		{
			return (index > 0 || IsUpvalue( index ) || index == LuaDef.LUA_REGISTRYINDEX)
				 ? index
				 : Top.Index - CI.FuncIndex + index;
		}

		int ILuaAPI.GetTop()
		{
			return Top.Index - (CI.FuncIndex + 1);
		}

		void ILuaAPI.SetTop( int index )
		{
			CallInfo ci = CI;
			int func = ci.FuncIndex;
			int diff; // difference for new top
			if( index >= 0 )
			{
				Utl.ApiCheck( index <= ci.TopIndex - (func + 1), "new top too large" );
				diff = ((func + 1) + index) - Top.Index;
				for( ; diff > 0; diff-- )
					StkId.inc( ref Top ).V.SetNilValue(); // clear new slots
			}
			else
			{
				Utl.ApiCheck( -(index+1) <= (Top.Index - (func + 1)), "invalid new top" );
				diff = index + 1; // will "subtract" index (as it is negative)
			}
			int newtop = Top.Index + diff;
			if( diff < 0 && TbcList.Count > 0 && TbcList[TbcList.Count - 1] >= newtop )
			{
				Utl.Assert( HasToCloseCFunc( ci.NumResults ) );
				newtop = F_Close( newtop, CLOSEKTOP, false );
			}
			Top = Stack[newtop]; // correct top only after closing any upvalue
		}

		void ILuaAPI.CloseSlot( int index )
		{
			StkId level = Index2Stack( index );
			Utl.ApiCheck( HasToCloseCFunc( CI.NumResults ) && TbcList.Count > 0 &&
				TbcList[TbcList.Count - 1] == level.Index,
				"no variable to close at given level" );
			int lv = F_Close( level.Index, CLOSEKTOP, false );
			Stack[lv].V.SetNilValue();
		}

		/*
		** Reverse the stack segment from 'from' to 'to'
		** (auxiliary to 'lua_rotate')
		** Note that we move(copy) only the value inside the stack.
		** (We do not move additional fields that may exist.)
		*/
		private void Reverse( int from, int to )
		{
			for( ; from < to; from++, to-- )
			{
				var temp = new TValue();
				temp.SetObj( ref Stack[from].V );
				Stack[from].V.SetObj( ref Stack[to].V );
				Stack[to].V.SetObj( ref temp );
			}
		}

		/*
		** Let x = AB, where A is a prefix of length 'n'. Then,
		** rotate x n == BA. But BA == (A^r . B^r)^r.
		*/
		void ILuaAPI.Rotate( int index, int n )
		{
			int t = Top.Index - 1; // end of stack segment being rotated
			int p = Index2Stack( index ).Index; // start of segment
			Utl.ApiCheck( (n >= 0 ? n : -n) <= (t - p + 1), "invalid 'n'" );
			int m = n >= 0 ? t - n : p - n - 1; // end of prefix
			Reverse( p, m ); // reverse the prefix with length 'n'
			Reverse( m + 1, t ); // reverse the suffix
			Reverse( p, t ); // reverse the entire segment
		}

		void ILuaAPI.Copy( int fromIndex, int toIndex )
		{
			StkId fr;
			if( !Index2Addr( fromIndex, out fr ) )
				fr = TheNilValue;
			StkId to;
			if( !Index2Addr( toIndex, out to ) )
				Utl.InvalidIndex();
			to.V.SetObj( ref fr.V );
		}

		void ILuaAPI.Remove( int index )
		{
			API.Rotate( index, -1 );
			API.Pop( 1 );
		}

		void ILuaAPI.Insert( int index )
		{
			API.Rotate( index, 1 );
		}

		void ILuaAPI.Replace( int index )
		{
			API.Copy( -1, index );
			API.Pop( 1 );
		}

		void ILuaAPI.PushValue( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue; // an index with no value pushes nil, as in C Lua

			Top.V.SetObj( ref addr.V );
			ApiIncrTop();
		}

		/*
		** access functions (stack -> C)
		*/

		LuaType ILuaAPI.Type( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return LuaType.LUA_TNONE;

			return (LuaType)addr.V.BaseTt();
		}

		internal static string TypeName( LuaType t )
		{
			switch( t )
			{
				case LuaType.LUA_TNIL: return "nil";
				case LuaType.LUA_TBOOLEAN: return "boolean";
				case LuaType.LUA_TLIGHTUSERDATA: return "userdata";
				case LuaType.LUA_TNUMBER: return "number";
				case LuaType.LUA_TSTRING: return "string";
				case LuaType.LUA_TTABLE: return "table";
				case LuaType.LUA_TFUNCTION: return "function";
				case LuaType.LUA_TUSERDATA: return "userdata";
				case LuaType.LUA_TTHREAD: return "thread";
				case LuaType.LUA_TPROTO: return "proto";
				case LuaType.LUA_TUPVAL: return "upvalue";
				default: return "no value";
			}
		}

		string ILuaAPI.TypeName( LuaType t )
		{
			return TypeName( t );
		}

		bool ILuaAPI.IsNil( int index )
		{
			return API.Type( index ) == LuaType.LUA_TNIL;
		}

		bool ILuaAPI.IsNone( int index )
		{
			return API.Type( index ) == LuaType.LUA_TNONE;
		}

		bool ILuaAPI.IsNoneOrNil( int index )
		{
			LuaType t = API.Type( index );
			return t == LuaType.LUA_TNONE || t == LuaType.LUA_TNIL;
		}

		bool ILuaAPI.IsString( int index )
		{
			LuaType t = API.Type( index );
			return t == LuaType.LUA_TSTRING || t == LuaType.LUA_TNUMBER;
		}

		bool ILuaAPI.IsNumber( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return false;
			double n;
			return V_ToNumber( ref addr.V, out n );
		}

		bool ILuaAPI.IsInteger( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return false;
			return addr.V.TtIsInteger();
		}

		bool ILuaAPI.IsTable( int index )
		{
			return API.Type( index ) == LuaType.LUA_TTABLE;
		}

		bool ILuaAPI.IsFunction( int index )
		{
			return API.Type( index ) == LuaType.LUA_TFUNCTION;
		}

		bool ILuaAPI.RawEqual( int index1, int index2 )
		{
			StkId addr1, addr2;
			if( !Index2Addr( index1, out addr1 ) || !Index2Addr( index2, out addr2 ) )
				return false;
			return V_RawEqualObj( ref addr1.V, ref addr2.V );
		}

		// luaO_arith: an arithmetic or bitwise operation, through the
		// metamethods when the raw one is not possible
		private void O_Arith( LuaOp op, ref TValue p1, ref TValue p2, int res )
		{
			if( !O_RawArith( this, op, ref p1, ref p2, ref Stack[res].V ) )
			{
				/* could not perform raw operation; try metamethod */
				T_TryBinTM( ref p1, ref p2, res, (TMS)((int)op - (int)LuaOp.LUA_OPADD + (int)TMS.TM_ADD) );
			}
		}

		void ILuaAPI.Arith( LuaOp op )
		{
			if( op != LuaOp.LUA_OPUNM && op != LuaOp.LUA_OPBNOT )
				Utl.ApiCheckNumElems( this, 2 ); // all other operations expect two operands
			else // for unary operations, add fake 2nd operand
			{
				Utl.ApiCheckNumElems( this, 1 );
				Top.V.SetObj( ref Stack[Top.Index-1].V );
				ApiIncrTop();
			}
			/* first operand at top - 2, second at top - 1; result go to top - 2 */
			O_Arith( op, ref Stack[Top.Index-2].V, ref Stack[Top.Index-1].V, Top.Index-2 );
			Top = Stack[Top.Index-1]; // pop second operand
		}

		bool ILuaAPI.Compare( int index1, int index2, LuaEq op )
		{
			// as C Lua: an index with no value is never equal nor less
			StkId addr1, addr2;
			if( !Index2Addr( index1, out addr1 ) || !Index2Addr( index2, out addr2 ) )
				return false;

			switch( op )
			{
				case LuaEq.LUA_OPEQ: return V_EqualObj( ref addr1.V, ref addr2.V, false );
				case LuaEq.LUA_OPLT: return V_LessThan( ref addr1.V, ref addr2.V );
				case LuaEq.LUA_OPLE: return V_LessEqual( ref addr1.V, ref addr2.V );
				default: Utl.ApiCheck( false, "invalid option" ); return false;
			}
		}

		// lua_stringtonumber: pushes the number 's' is the numeral of, and
		// returns the length of 's' plus one, or 0 if it is no numeral
		int ILuaAPI.StringToNumber( string s )
		{
			TValue o;
			if( !O_Str2Num( s, out o ) )
				return 0;
			Top.V.SetObj( ref o );
			ApiIncrTop();
			return s.Length + 1;
		}

		double ILuaAPI.ToNumberX( int index, out bool isnum )
		{
			StkId addr;
			double n = 0.0;
			isnum = Index2Addr( index, out addr ) && V_ToNumber( ref addr.V, out n );
			return isnum ? n : 0.0;
		}

		double ILuaAPI.ToNumber( int index )
		{
			bool isnum;
			return API.ToNumberX( index, out isnum );
		}

		long ILuaAPI.ToIntegerX( int index, out bool isnum )
		{
			StkId addr;
			long n = 0;
			isnum = Index2Addr( index, out addr ) && V_ToInteger( ref addr.V, out n, F2Imod.F2Ieq );
			return isnum ? n : 0;
		}

		long ILuaAPI.ToInteger( int index )
		{
			bool isnum;
			return API.ToIntegerX( index, out isnum );
		}

		bool ILuaAPI.ToBoolean( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return false;
			return !IsFalse( ref addr.V );
		}

		string ILuaAPI.ToString( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return null;

			if( !addr.V.TtIsString() )
			{
				if( !V_ToString( ref addr.V ) ) // not convertible?
					return null;
			}
			return addr.V.SValue();
		}

		int ILuaAPI.RawLen( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return 0;

			switch( addr.V.Tt )
			{
				case (int)LuaType.LUA_TSTRING:
				{
					var s = addr.V.SValue();
					return s == null ? 0 : s.Length;
				}
				case (int)LuaType.LUA_TUSERDATA: return addr.V.RawUValue().Length;
				case (int)LuaType.LUA_TTABLE: return addr.V.HValue().Length;
				default: return 0;
			}
		}

		object ILuaAPI.ToObject( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return null;
			return addr.V.OValue;
		}

		object ILuaAPI.ToUserData( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return null;

			switch( addr.V.Tt ) {
				case (int)LuaType.LUA_TUSERDATA: { return addr.V.RawUValue().Value; }
				case (int)LuaType.LUA_TLIGHTUSERDATA: { return addr.V.OValue; }
				default: return null;
			}
		}

		ILuaState ILuaAPI.ToThread( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return null;
			return addr.V.TtIsThread() ? addr.V.OValue as ILuaState : null;
		}

		/*
		** push functions (C -> stack)
		*/

		void ILuaAPI.PushNil()
		{
			Top.V.SetNilValue();
			ApiIncrTop();
		}

		void ILuaAPI.PushNumber( double n )
		{
			Top.V.SetFltValue( n );
			ApiIncrTop();
		}

		void ILuaAPI.PushInteger( long n )
		{
			Top.V.SetIValue( n );
			ApiIncrTop();
		}

		string ILuaAPI.PushString( string s )
		{
			if( s == null )
			{
				API.PushNil();
				return null;
			}
			else
			{
				Top.V.SetSValue( s );
				ApiIncrTop();
				return s;
			}
		}

		// pushes a string for internal use: no check of the top against the
		// frame of the C# function, as luaO_pushfstring
		internal void O_PushString( string s )
		{
			Top.V.SetSValue( s );
			IncrTop();
		}

		void ILuaAPI.PushCSharpFunction( CSharpFunctionDelegate f )
		{
			API.PushCSharpClosure( f, 0 );
		}

		void ILuaAPI.PushCSharpClosure( CSharpFunctionDelegate f, int n )
		{
			if( n == 0 )
			{
				Top.V.SetClCsValue( new LuaCsClosureValue( f ) );
			}
			else
			{
				Utl.ApiCheckNumElems( this, n );
				Utl.ApiCheck( n <= LuaLimits.MAXUPVAL, "upvalue index too large" );

				LuaCsClosureValue cscl = new LuaCsClosureValue( f, n );
				int index = Top.Index - n;
				Top = Stack[index];
				for( int i=0; i<n; ++i )
					{ cscl.Upvals[i].V.SetObj( ref Stack[index+i].V ); }

				Top.V.SetClCsValue( cscl );
			}
			ApiIncrTop();
		}

		void ILuaAPI.PushGlobalTable()
		{
			API.RawGetI( LuaDef.LUA_REGISTRYINDEX, LuaDef.LUA_RIDX_GLOBALS );
		}

		void ILuaAPI.PushBoolean( bool b )
		{
			Top.V.SetBValue( b );
			ApiIncrTop();
		}

		void ILuaAPI.PushLightUserData( object o )
		{
			Top.V.SetPValue( o );
			ApiIncrTop();
		}

		bool ILuaAPI.PushThread()
		{
			Top.V.SetThValue( this );
			ApiIncrTop();
			return G.MainThread == (LuaState)this;
		}

		/*
		** get functions (Lua -> stack)
		*/

		private LuaType AuxGetStr( ref TValue t, string k )
		{
			Top.V.SetSValue( k );
			ApiIncrTop();
			V_GetTable( ref t, ref Stack[Top.Index-1].V, Top.Index-1 );
			return (LuaType)Stack[Top.Index-1].V.BaseTt();
		}

		/*
		** Get the global table in the registry.
		*/
		private StkId GetGTable()
		{
			return G.Registry.V.HValue().GetInt( LuaDef.LUA_RIDX_GLOBALS );
		}

		LuaType ILuaAPI.GetGlobal( string name )
		{
			return AuxGetStr( ref GetGTable().V, name );
		}

		LuaType ILuaAPI.GetTable( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;

			V_GetTable( ref addr.V, ref Stack[Top.Index-1].V, Top.Index-1 );
			return (LuaType)Stack[Top.Index-1].V.BaseTt();
		}

		LuaType ILuaAPI.GetField( int index, string key )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;
			return AuxGetStr( ref addr.V, key );
		}

		LuaType ILuaAPI.GetI( int index, long n )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;

			Top.V.SetIValue( n );
			ApiIncrTop();
			V_GetTable( ref addr.V, ref Stack[Top.Index-1].V, Top.Index-1 );
			return (LuaType)Stack[Top.Index-1].V.BaseTt();
		}

		private LuaType FinishRawGet( StkId val )
		{
			if( val.V.TtIsNil() ) // avoid copying empty items to the stack
				Top.V.SetNilValue();
			else
				Top.V.SetObj( ref val.V );
			ApiIncrTop();
			return (LuaType)Stack[Top.Index-1].V.BaseTt();
		}

		private LuaTable GetTableAt( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				Utl.ApiCheck( false, "table expected" );
			Utl.ApiCheck( addr.V.TtIsTable(), "table expected" );
			return addr.V.HValue();
		}

		LuaType ILuaAPI.RawGet( int index )
		{
			var t = GetTableAt( index );
			var val = t.Get( ref Stack[Top.Index-1].V );
			Top = Stack[Top.Index-1]; // remove key
			return FinishRawGet( val );
		}

		LuaType ILuaAPI.RawGetI( int index, long n )
		{
			var t = GetTableAt( index );
			return FinishRawGet( t.GetInt( n ) );
		}

		void ILuaAPI.CreateTable( int narray, int nrec )
		{
			var tbl = new LuaTable( this );
			Top.V.SetHValue( tbl );
			ApiIncrTop();
			if( narray > 0 || nrec > 0 )
				{ tbl.Resize( narray, nrec ); }
		}

		void ILuaAPI.NewTable()
		{
			API.CreateTable( 0, 0 );
		}

		bool ILuaAPI.GetMetaTable( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				return false; // no value, no metatable

			LuaTable mt;
			switch( addr.V.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
					mt = addr.V.HValue().MetaTable;
					break;
				case (int)LuaType.LUA_TUSERDATA:
					mt = addr.V.RawUValue().MetaTable;
					break;
				default:
					mt = G.MetaTables[addr.V.BaseTt()];
					break;
			}
			if( mt == null )
				return false;
			else
			{
				Top.V.SetHValue( mt );
				ApiIncrTop();
				return true;
			}
		}

		LuaType ILuaAPI.GetIUserValue( int index, int n )
		{
			StkId addr;
			LuaType t;
			bool valid = Index2Addr( index, out addr );
			Utl.ApiCheck( valid && addr.V.Tt == (int)LuaType.LUA_TUSERDATA, "full userdata expected" );
			var u = addr.V.RawUValue();
			if( n <= 0 || n > u.UserValues.Length )
			{
				Top.V.SetNilValue();
				t = LuaType.LUA_TNONE;
			}
			else
			{
				Top.V.SetObj( ref u.UserValues[n - 1] );
				t = (LuaType)Top.V.BaseTt();
			}
			ApiIncrTop();
			return t;
		}

		/*
		** set functions (stack -> Lua)
		*/

		/*
		** t[k] = value at the top of the stack (where 'k' is a string)
		*/
		private void AuxSetStr( ref TValue t, string k )
		{
			Utl.ApiCheckNumElems( this, 1 );
			Top.V.SetSValue( k ); // push 'str' (to make it a TValue)
			ApiIncrTop();
			V_SetTable( ref t, ref Stack[Top.Index-1].V, ref Stack[Top.Index-2].V );
			Top = Stack[Top.Index-2]; // pop value and key
		}

		void ILuaAPI.SetGlobal( string name )
		{
			AuxSetStr( ref GetGTable().V, name );
		}

		void ILuaAPI.SetTable( int index )
		{
			Utl.ApiCheckNumElems( this, 2 );
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;
			V_SetTable( ref addr.V, ref Stack[Top.Index-2].V, ref Stack[Top.Index-1].V );
			Top = Stack[Top.Index-2]; // pop index and value
		}

		void ILuaAPI.SetField( int index, string key )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;
			AuxSetStr( ref addr.V, key );
		}

		void ILuaAPI.SetI( int index, long n )
		{
			Utl.ApiCheckNumElems( this, 1 );
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;
			var aux = new TValue();
			aux.SetIValue( n );
			V_SetTable( ref addr.V, ref aux, ref Stack[Top.Index-1].V );
			Top = Stack[Top.Index-1]; // pop value
		}

		void ILuaAPI.RawSet( int index )
		{
			Utl.ApiCheckNumElems( this, 2 );
			var t = GetTableAt( index );
			t.Set( ref Stack[Top.Index-2].V, ref Stack[Top.Index-1].V );
			t.NoTagMethodFlags = 0; // invalidateTMcache
			Top = Stack[Top.Index-2];
		}

		void ILuaAPI.RawSetI( int index, long n )
		{
			Utl.ApiCheckNumElems( this, 1 );
			var t = GetTableAt( index );
			t.SetInt( n, ref Stack[Top.Index-1].V );
			Top = Stack[Top.Index-1];
		}

		bool ILuaAPI.SetMetaTable( int index )
		{
			Utl.ApiCheckNumElems( this, 1 );

			StkId addr;
			if( !Index2Addr( index, out addr ) )
				Utl.InvalidIndex();

			var below = Stack[Top.Index - 1];
			LuaTable mt;
			if( below.V.TtIsNil() )
				mt = null;
			else
			{
				Utl.ApiCheck( below.V.TtIsTable(), "table expected" );
				mt = below.V.HValue();
			}

			// TODO: Cosmos has no finalizers nor weak references: the '__gc'
			// of tables and userdata is never called (luaC_checkfinalizer),
			// and a '__mode' leaves the keys and values of a table strong
			switch( addr.V.Tt )
			{
				case (int)LuaType.LUA_TTABLE:
					addr.V.HValue().MetaTable = mt;
					break;
				case (int)LuaType.LUA_TUSERDATA:
					addr.V.RawUValue().MetaTable = mt;
					break;
				default:
					G.MetaTables[addr.V.BaseTt()] = mt;
					break;
			}
			Top = Stack[Top.Index - 1];
			return true;
		}

		bool ILuaAPI.SetIUserValue( int index, int n )
		{
			Utl.ApiCheckNumElems( this, 1 );
			StkId addr;
			bool valid = Index2Addr( index, out addr );
			Utl.ApiCheck( valid && addr.V.Tt == (int)LuaType.LUA_TUSERDATA, "full userdata expected" );
			var u = addr.V.RawUValue();
			bool res;
			if( !((uint)(n - 1) < (uint)u.UserValues.Length) )
				res = false; // 'n' not in [1, uvalue(o)->nuvalue]
			else
			{
				u.UserValues[n - 1].SetObj( ref Stack[Top.Index-1].V );
				res = true;
			}
			Top = Stack[Top.Index-1];
			return res;
		}

		/*
		** 'load' and 'call' functions (run Lua code)
		*/

		private void CheckResults( int numArgs, int numResults )
		{
			Utl.ApiCheck( numResults == LuaDef.LUA_MULTRET ||
				CI.TopIndex - Top.Index >= numResults - numArgs,
				"results from function overflow current stack size" );
		}

		private void AdjustResults( int numResults )
		{
			if( numResults <= LuaDef.LUA_MULTRET && CI.TopIndex < Top.Index )
				CI.TopIndex = Top.Index;
		}

		void ILuaAPI.Call( int numArgs, int numResults )
		{
			API.CallK( numArgs, numResults, 0, null );
		}

		void ILuaAPI.CallK( int numArgs, int numResults,
			int context, CSharpFunctionDelegate continueFunc )
		{
			Utl.ApiCheck( continueFunc == null || !CI.IsLua,
				"cannot use continuations inside hooks" );
			Utl.ApiCheckNumElems( this, numArgs + 1 );
			Utl.ApiCheck( Status == ThreadStatus.LUA_OK,
				"cannot do calls on non-normal thread" );
			CheckResults( numArgs, numResults );
			var func = Stack[Top.Index - (numArgs+1)];
			if( continueFunc != null && Yieldable() ) // need to prepare continuation?
			{
				CI.ContinueFunc = continueFunc; // save continuation
				CI.Context = context; // save context
				D_Call( func, numResults ); // do the call
			}
			else // no continuation or no yieldable
				D_CallNoYield( func, numResults ); // just do the call
			AdjustResults( numResults );
		}

		/*
		** Execute a protected call.
		*/
		private struct CallS // data to 'F_Call'
		{
			public LuaState L;
			public int FuncIndex;
			public int NumResults;
		}

		private static void F_Call( ref CallS c )
		{
			c.L.D_CallNoYield( c.L.Stack[c.FuncIndex], c.NumResults );
		}
		private static PFuncDelegate<CallS> DG_F_Call = F_Call;

		ThreadStatus ILuaAPI.PCall( int numArgs, int numResults, int errFunc )
		{
			return API.PCallK( numArgs, numResults, errFunc, 0, null );
		}

		ThreadStatus ILuaAPI.PCallK( int numArgs, int numResults, int errFunc,
			int context, CSharpFunctionDelegate continueFunc )
		{
			Utl.ApiCheck( continueFunc == null || !CI.IsLua,
				"cannot use continuations inside hooks" );
			Utl.ApiCheckNumElems( this, numArgs + 1 );
			Utl.ApiCheck( Status == ThreadStatus.LUA_OK,
				"cannot do calls on non-normal thread" );
			CheckResults( numArgs, numResults );

			int func;
			if( errFunc == 0 )
				func = 0;
			else
			{
				StkId o = Index2Stack( errFunc );
				Utl.ApiCheck( o.V.TtIsFunction(), "error handler must be a function" );
				func = o.Index;
			}

			ThreadStatus status;
			var c = new CallS();
			c.L = this;
			c.FuncIndex = Top.Index - (numArgs + 1); // function to be called
			if( continueFunc == null || !Yieldable() ) // no continuation or no yieldable?
			{
				c.NumResults = numResults; // do a 'conventional' protected call
				status = D_PCall( DG_F_Call, ref c, c.FuncIndex, func );
			}
			else // prepare continuation (call is already protected by 'resume')
			{
				CallInfo ci = CI;
				ci.ContinueFunc = continueFunc; // save continuation
				ci.Context = context; // save context
				/* save information for error recovery */
				ci.FuncIdx = c.FuncIndex;
				ci.OldErrFunc = ErrFunc;
				ErrFunc = func;
				if( AllowHook ) // save value of 'allowhook'
					ci.CallStatus |= CallStatus.CIST_OAH;
				else
					ci.CallStatus &= ~CallStatus.CIST_OAH;
				ci.CallStatus |= CallStatus.CIST_YPCALL; // function can do error recovery
				D_Call( Stack[c.FuncIndex], numResults ); // do the call
				ci.CallStatus &= ~CallStatus.CIST_YPCALL;
				ErrFunc = ci.OldErrFunc;
				status = ThreadStatus.LUA_OK; // if it is here, there were no errors
			}
			AdjustResults( numResults );
			return status;
		}

		private struct LoadParameter
		{
			public LuaState		L;
			public ILoadInfo 	LoadInfo;
			public string 		Name;
			public string 		Mode;
		}

		private void CheckMode( string given, string expected )
		{
			if( given != null && given.IndexOf( expected[0] ) == -1 )
			{
				O_PushString( string.Format(
					"attempt to load a {0} chunk (mode is '{1}')",
					expected, given ) );
				D_Throw( ThreadStatus.LUA_ERRSYNTAX );
			}
		}

		// f_parser of ldo.c
		private static void F_Load( ref LoadParameter param )
		{
			var L = param.L;

			LuaProto proto;
			var c = param.LoadInfo.PeekByte();
			if( c == LuaConf.LUA_SIGNATURE[0] )
			{
				L.CheckMode( param.Mode, "binary" );
				proto = Undump.LoadBinary( L, param.LoadInfo, param.Name );
			}
			else
			{
				L.CheckMode( param.Mode, "text" );
				proto = Parser.Parse( L, param.LoadInfo, param.Name );
			}

			var cl = new LuaLClosureValue( proto );
			Utl.Assert( cl.Upvals.Length == cl.Proto.Upvalues.Count );

			L.Top.V.SetClLValue( cl );
			L.IncrTop();
		}
		private static PFuncDelegate<LoadParameter> DG_F_Load = F_Load;

		ThreadStatus ILuaAPI.Load( ILoadInfo loadinfo, string name, string mode )
		{
			if( name == null ) name = "?";
			var param = new LoadParameter();
			param.L = this;
			param.LoadInfo = loadinfo;
			param.Name = name;
			param.Mode = mode;
			// luaD_protectedparser: no message handler
			var status = D_PCall( DG_F_Load, ref param, Top.Index, ErrFunc );

			if( status == ThreadStatus.LUA_OK ) // no errors?
			{
				var cl = Stack[Top.Index-1].V.ClLValue(); // get new function
				if( cl.Upvals.Length >= 1 ) // does it have an upvalue?
				{
					/* get global table from registry */
					var gt = GetGTable();
					/* set global table as 1st upvalue of 'f' (may be LUA_ENV) */
					cl.Upvals[0].V.V.SetObj( ref gt.V );
				}
			}
			return status;
		}

		DumpStatus ILuaAPI.Dump( LuaWriter writeFunc, bool strip )
		{
			Utl.ApiCheckNumElems( this, 1 );

			var below = Stack[Top.Index-1];
			if( !below.V.TtIsFunction() || !below.V.ClIsLuaClosure() )
				return DumpStatus.ERROR;

			var o = below.V.ClLValue();
			if( o == null )
				return DumpStatus.ERROR;

			return DumpState.Dump( o.Proto, writeFunc, strip );
		}

		ThreadStatus ILuaAPI.GetContext( out int context )
		{
			if( (CI.CallStatus & CallStatus.CIST_YIELDED) != 0 )
			{
				context = CI.Context;
				return CI.Status;
			}
			else
			{
				context = default(int);
				return ThreadStatus.LUA_OK;
			}
		}

		ThreadStatus ILuaAPI.Resume( ILuaState from, int numArgs, out int numResults )
		{
			return D_Resume( from as LuaState, numArgs, out numResults );
		}

		bool ILuaAPI.IsYieldable()
		{
			return Yieldable();
		}

		int ILuaAPI.Yield( int numResults )
		{
			return D_Yield( numResults, 0, null );
		}

		int ILuaAPI.YieldK( int numResults,
			int context, CSharpFunctionDelegate continueFunc )
		{
			return D_Yield( numResults, context, continueFunc );
		}

		/*
		** miscellaneous functions
		*/

		int ILuaAPI.Error()
		{
			Utl.ApiCheckNumElems( this, 1 );
			G_ErrorMsg(); // raise a regular error
			return 0;
		}

		bool ILuaAPI.Next( int index )
		{
			var t = GetTableAt( index );
			Utl.ApiCheckNumElems( this, 1 );
			var key = Stack[Top.Index-1];
			if( t.Next( key, Top ) )
			{
				ApiIncrTop();
				return true;
			}
			else // no more elements
			{
				Top = Stack[Top.Index-1]; // remove key
				return false;
			}
		}

		void ILuaAPI.ToClose( int index )
		{
			StkId o = Index2Stack( index );
			int nresults = CI.NumResults;
			Utl.ApiCheck( TbcList.Count == 0 || TbcList[TbcList.Count - 1] < o.Index,
				"given index below or equal a marked one" );
			F_NewTbcUpval( o ); // create new to-be-closed upvalue
			if( !HasToCloseCFunc( nresults ) ) // function not marked yet?
				CI.NumResults = CodeNResults( nresults ); // mark it
			Utl.Assert( HasToCloseCFunc( CI.NumResults ) );
		}

		void ILuaAPI.Concat( int n )
		{
			Utl.ApiCheckNumElems( this, n );
			if( n > 0 )
				V_Concat( n );
			else // nothing to concatenate
			{
				Top.V.SetSValue( "" ); // push empty string
				ApiIncrTop();
			}
		}

		void ILuaAPI.Len( int index )
		{
			StkId addr;
			if( !Index2Addr( index, out addr ) )
				addr = TheNilValue;
			V_ObjLen( Top.Index, ref addr.V );
			ApiIncrTop();
		}

		// lua_newuserdatauv: a C# object as a full userdata with 'nuvalue'
		// user values
		void ILuaAPI.NewUserDataUV( object o, int nuvalue )
		{
			Utl.ApiCheck( 0 <= nuvalue && nuvalue < short.MaxValue, "invalid value" );
			Top.V.SetUValue( new LuaUserDataValue( nuvalue ) { Value = o } );
			ApiIncrTop();
		}

		void ILuaAPI.NewUserData( object o )
		{
			API.NewUserDataUV( o, 1 );
		}

		private string AuxUpvalue( StkId fi, int n, out StkId val )
		{
			val = null;

			if( !fi.V.TtIsFunction() )
				return null; // not a closure

			if( fi.V.ClIsLuaClosure() ) // Lua closure
			{
				var f = fi.V.ClLValue();
				var p = f.Proto;
				if( !((uint)(n - 1) < (uint)p.Upvalues.Count) )
					return null; // 'n' not in [1, p->sizeupvalues]
				val = f.Upvals[n-1].V;
				var name = p.Upvalues[n-1].Name;
				return (name == null) ? "(no name)" : name;
			}
			else if( fi.V.ClIsCsClosure() ) // C# closure
			{
				var f = fi.V.ClCsValue();
				if( f.Upvals == null || !((uint)(n - 1) < (uint)f.Upvals.Length) )
					return null; // 'n' not in [1, f->nupvalues]
				val = f.Upvals[n-1];
				return "";
			}
			else return null; // light C# function
		}

		string ILuaAPI.GetUpvalue( int funcIndex, int n )
		{
			StkId addr;
			if( !Index2Addr( funcIndex, out addr ) )
				return null;

			StkId val;
			var name = AuxUpvalue( addr, n, out val );
			if( name == null )
				return null;

			Top.V.SetObj( ref val.V );
			ApiIncrTop();
			return name;
		}

		string ILuaAPI.SetUpvalue( int funcIndex, int n )
		{
			StkId addr;
			if( !Index2Addr( funcIndex, out addr ) )
				return null;

			Utl.ApiCheckNumElems( this, 1 );

			StkId val;
			var name = AuxUpvalue( addr, n, out val );
			if( name == null )
				return null;

			Top = Stack[Top.Index-1];
			val.V.SetObj( ref Top.V );
			return name;
		}

		int ILuaAPI.UpvalueIndex( int i )
		{
			return LuaDef.LUA_REGISTRYINDEX - i;
		}

		void ILuaAPI.Pop( int n )
		{
			API.SetTop( -n-1 );
		}

		/*
		** {======================================================================
		** threads (lstate.c)
		** =======================================================================
		*/

		ILuaState ILuaAPI.NewThread()
		{
			LuaState L1 = new LuaState( G );
			Top.V.SetThValue( L1 );
			ApiIncrTop();

			L1.HookMask = HookMask;
			L1.BaseHookCount = BaseHookCount;
			L1.Hook = Hook;
			L1.ResetHookCount();

			return L1;
		}

		// luaE_resetthread: unwinds the CallInfo list and closes the pending
		// to-be-closed variables of a thread
		internal ThreadStatus E_ResetThread( ThreadStatus status )
		{
			CallInfo ci = CI = BaseCI[0]; // unwind CallInfo list
			Stack[0].V.SetNilValue(); // 'function' entry for basic 'ci'
			ci.FuncIndex = 0;
			ci.CallStatus = CallStatus.CIST_C;
			if( status == ThreadStatus.LUA_YIELD )
				status = ThreadStatus.LUA_OK;
			Status = ThreadStatus.LUA_OK; // so it can run __close metamethods
			ErrFunc = 0; // stack unwind can "throw away" the error function
			status = D_CloseProtected( 1, status );
			if( status != ThreadStatus.LUA_OK ) // errors?
				D_SetErrorObj( status, Stack[1] );
			else
				Top = Stack[1];
			ci.TopIndex = Top.Index + LuaDef.LUA_MINSTACK;
			if( ci.TopIndex > StackLast )
				D_ReallocStack( ci.TopIndex );
			return status;
		}

		ThreadStatus ILuaAPI.CloseThread( ILuaState from )
		{
			var fromState = from as LuaState;
			NumCSharpCalls = (fromState != null) ? fromState.NumCSharpCalls : 0;
			NumNonYieldable = 0;
			return E_ResetThread( Status );
		}

		// luaE_warning
		internal void E_Warning( string msg, bool toCont )
		{
			var wf = G.WarnF;
			if( wf != null )
				wf( this, msg, toCont );
		}

		// lua_setwarnf
		internal void SetWarnF( LuaWarnDelegate f )
		{
			G.WarnF = f;
		}

		void ILuaAPI.Warning( string msg, bool toCont )
		{
			E_Warning( msg, toCont );
		}

		/* }====================================================================== */
	}

}
