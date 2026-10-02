// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Collections.Generic;

namespace Cosmos.Executable.Lua
{
	using InstructionPtr = Pointer<Instruction>;

	internal struct Pointer<T>
	{
		private List<T> 	List;
		public  int 		Index { get; set; }

		public  T			Value
		{
			get
			{
				return List[Index];
			}
			set
			{
				List[Index] = value;
			}
		}

		public T			ValueInc
		{
			get
			{
				return List[Index++];
			}
			set
			{
				List[Index++] = value;
			}
		}

		public Pointer( List<T> list, int index ) : this()
		{
			List = list;
			Index = index;
		}

		public Pointer( Pointer<T> other ) : this()
		{
			List = other.List;
			Index = other.Index;
		}

		public static Pointer<T> operator +( Pointer<T> lhs, int rhs )
		{
			return new Pointer<T>( lhs.List, lhs.Index + rhs );
		}

		public static Pointer<T> operator -( Pointer<T> lhs, int rhs )
		{
			return new Pointer<T>( lhs.List, lhs.Index - rhs );
		}
	}

	// Bits in CallInfo status (lstate.h of Lua 5.4)
	internal enum CallStatus
	{
		CIST_NONE		= 0,

		CIST_OAH		= (1<<0),	/* original value of 'allowhook' */
		CIST_C			= (1<<1),	/* call is running a C function */
		CIST_FRESH		= (1<<2),	/* call is on a fresh "luaV_execute" frame */
		CIST_HOOKED		= (1<<3),	/* call is running a debug hook */
		CIST_YPCALL		= (1<<4),	/* doing a yieldable protected call */
		CIST_TAIL		= (1<<5),	/* call was tail called */
		CIST_HOOKYIELD	= (1<<6),	/* last hook called yielded */
		CIST_FIN		= (1<<7),	/* function "called" a finalizer */
		CIST_TRAN		= (1<<8),	/* 'ci' has transfer information */
		CIST_CLSRET		= (1<<9),	/* function is closing tbc variables */
		/* Bits 10-12 are used for CIST_RECST (see below) */
		CIST_LEQ		= (1<<13),	/* using __lt for __le */
		// the continuation of the function runs after a yield or an error
		// (what 'lua_getctx' of Lua 5.2 reports, as the C# API keeps it)
		CIST_YIELDED	= (1<<14),
	}

	internal class CallInfo
	{
		public CallInfo[] List;
		public int Index;

		public int FuncIndex;	// 'func'
		public int TopIndex;	// 'top' for this function

		public int NumResults;	// expected number of results from this function
		public CallStatus CallStatus;

		// for Lua functions
		public InstructionPtr SavedPc;
		public bool Trap;		// function is tracing lines/counts
		public int NExtraArgs;	// # of extra arguments in vararg functions

		// for C# functions
		public CSharpFunctionDelegate ContinueFunc;	// continuation in case of yields
		public int OldErrFunc;
		public int Context;		// context info. in case of yields
		public ThreadStatus Status;	// status the continuation gets (lua_getctx)

		// 'u2' of Lua 5.4
		public int FuncIdx;		// called-function index
		public int NYield;		// number of values yielded
		public int NRes;		// number of values returned
		public int FTransfer;	// offset of first value transferred
		public int NTransfer;	// number of values transferred

		public bool IsLua
		{
			get { return (CallStatus & CallStatus.CIST_C) == 0; }
		}

		// isLuacode: a Lua function not running a hook
		public bool IsLuaCode
		{
			get { return (CallStatus & (CallStatus.CIST_C | CallStatus.CIST_HOOKED)) == 0; }
		}

		public CallInfo Previous
		{
			get { return Index > 0 ? List[Index - 1] : null; }
		}

		public int CurrentPc
		{
			get
			{
				Utl.Assert( IsLua );
				return SavedPc.Index - 1;
			}
		}

		/*
		** Field CIST_RECST stores the "recover status", used to keep the error
		** status while closing to-be-closed variables in coroutines, so that
		** Lua can correctly resume after an yield from a __close method called
		** because of an error.  (Three bits are enough for error status.)
		*/
		private const int CIST_RECST = 10;

		public ThreadStatus GetRecSt()
		{
			return (ThreadStatus)(((int)CallStatus >> CIST_RECST) & 7);
		}

		public void SetRecSt( ThreadStatus st )
		{
			CallStatus = (CallStatus)(((int)CallStatus & ~(7 << CIST_RECST))
				| ((int)st << CIST_RECST));
		}
	}

	// lua_WarnFunction: receives the pieces of a warning, 'toCont' while
	// more pieces of the same message are to come
	internal delegate void LuaWarnDelegate( LuaState L, string msg, bool toCont );

	internal class GlobalState
	{
		public StkId		Registry;
		public LuaTable[] 	MetaTables;
		public LuaState		MainThread;

		// lua_setwarnf: the warning function, as lauxlib sets it
		public LuaWarnDelegate WarnF;

		// the collector of the state's objects (see LuaGC.cs)
		public GCState		GC = new GCState();

		// What the libraries reach the machine through: shared by the
		// coroutines of a state, separate from other states
		internal readonly LuaHost Host = new LuaHost();

		public GlobalState( LuaState state )
		{
			MainThread	= state;
			Registry 	= new StkId();
			MetaTables 	= new LuaTable[(int)LuaType.LUA_NUMTAGS];
		}
	}

	public delegate void LuaHookDelegate(ILuaState lua, LuaDebug ar);

	internal partial class LuaState
	{
		public StkId[]			Stack;
		public StkId			Top;
		public int				StackLast;	// end of the stack (last element + 1)
		public CallInfo 		CI;
		public CallInfo[] 		BaseCI;
		public GlobalState		G;
		// 'nCcalls' of Lua 5.4: the number of nested C# calls (calls of
		// V_Execute, metamethods, parser levels...) and of non-yieldable ones
		public int				NumNonYieldable;
		public int				NumCSharpCalls;
		public int				ErrFunc;
		public ThreadStatus		Status { get; set; }
		public bool				AllowHook;
		public byte				HookMask;
		public int				BaseHookCount;
		public int				HookCount;
		public LuaHookDelegate	Hook;
		public int				OldPc; // last pc traced

		// list of open upvalues, from the top of the stack down
		public LinkedList<LuaUpvalue>	OpenUpval;
		// the to-be-closed variables (their stack indices), the last on top
		public List<int>		TbcList;

		private ILuaAPI 	API;

		static LuaState()
		{
			TheNilValue = new StkId();
			TheNilValue.V.SetNilValue();
		}

		public LuaState( GlobalState g=null )
		{
			API = (ILuaAPI)this;

			NumCSharpCalls  = 0;
			Hook			= null;
			HookMask		= 0;
			BaseHookCount	= 0;
			AllowHook		= true;
			ResetHookCount();
			Status			= ThreadStatus.LUA_OK;

			if( g == null )
			{
				G = new GlobalState(this);
				InitRegistry();
				NumNonYieldable = 1; // main thread is always non yieldable
			}
			else
			{
				G = g;
				NumNonYieldable = 0;
			}
			OpenUpval = new LinkedList<LuaUpvalue>();
			TbcList   = new List<int>();
			ErrFunc   = 0;

			InitStack();
		}

		// whether this thread has no non-yieldable calls in the stack
		internal bool Yieldable()
		{
			return NumNonYieldable == 0;
		}

		private void IncrTop()
		{
			StkId.inc(ref Top);
			D_CheckStack(0);
		}

		private StkId RestoreStack( int index )
		{
			return Stack[index];
		}

		private void ApiIncrTop()
		{
			StkId.inc(ref Top);
			Utl.ApiCheck( Top.Index <= CI.TopIndex, "stack overflow" );
		}

		private void InitStack()
		{
			Stack = new StkId[LuaDef.BASIC_STACK_SIZE + LuaDef.EXTRA_STACK];
			StackLast = LuaDef.BASIC_STACK_SIZE;
			for(int i=0; i<Stack.Length; ++i) {
				var newItem = new StkId();
				Stack[i] = newItem;
				newItem.SetList(Stack);
				newItem.SetIndex(i);
				newItem.V.SetNilValue();
			}
			Top = Stack[0];

			BaseCI = new CallInfo[LuaDef.BASE_CI_SIZE];
			for(int i=0; i<LuaDef.BASE_CI_SIZE; ++i) {
				var newCI = new CallInfo();
				BaseCI[i] = newCI;
				newCI.List = BaseCI;
				newCI.Index = i;
			}
			/* initialize first ci */
			CI = BaseCI[0];
			CI.FuncIndex = Top.Index;
			CI.CallStatus = CallStatus.CIST_C;
			CI.NumResults = 0;
			StkId.inc(ref Top).V.SetNilValue(); // 'function' entry for this 'ci'
			CI.TopIndex = Top.Index + LuaDef.LUA_MINSTACK;
		}

		private void InitRegistry()
		{
			var mt = new TValue();

			G.Registry.V.SetHValue(new LuaTable(this));

			mt.SetThValue(this);
			G.Registry.V.HValue().SetInt(LuaDef.LUA_RIDX_MAINTHREAD, ref mt);

			mt.SetHValue(new LuaTable(this));
			G.Registry.V.HValue().SetInt(LuaDef.LUA_RIDX_GLOBALS, ref mt);
		}

		private void ResetHookCount()
		{
			HookCount = BaseHookCount;
		}

		// luaE_checkcstack: called when 'NumCSharpCalls' reaches the limit
		internal void E_CheckCStack()
		{
			if( NumCSharpCalls == LuaLimits.LUAI_MAXCCALLS ) // possible C stack overflow?
				G_RunError( "C stack overflow" );
			else if( NumCSharpCalls >= (LuaLimits.LUAI_MAXCCALLS / 10 * 11) )
				D_ErrErr(); // error while handling stack error
		}

		// luaE_incCstack
		internal void E_IncCStack()
		{
			NumCSharpCalls++;
			if( NumCSharpCalls >= LuaLimits.LUAI_MAXCCALLS )
				E_CheckCStack();
		}
	}

}
