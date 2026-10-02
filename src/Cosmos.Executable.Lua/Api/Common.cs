// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	public static class LuaConf
	{
		public const int LUAI_BITSINT			= 32;

#pragma warning disable 0429
		// 200000 slots, not the reference 1000000: a slot is an object here,
		// and a runaway recursion fills the stack before it fails, which costs
		// tens of megabytes of a kernel's heap at a million
		public const int LUAI_MAXSTACK = (LUAI_BITSINT >= 32)
			? 200000
			: 15000
			;
#pragma warning restore 0429

		// reserve some space for error handling
		public const int LUAI_FIRSTPSEUDOIDX	= (-LUAI_MAXSTACK-1000);

		public const string LUA_SIGNATURE = "\u001bLua";

		// lua_Integer is a long, as in the reference build
		public const long LUA_MAXINTEGER = long.MaxValue;
		public const long LUA_MININTEGER = long.MinValue;
		public static string LUA_DIRSEP {
			get { return System.IO.Path.DirectorySeparatorChar.ToString(); }
		}
	}

	public static class LuaLimits
	{
		public const int MAX_INT 	= System.Int32.MaxValue - 2;
		public const int MAXUPVAL 	= System.Byte.MaxValue;
		// as the reference: a nested C# call (a metamethod, a sort comparator,
		// a parser level...) takes up to about 1 KB of the thread's stack, and
		// a Cosmos session thread has 256 KB
		public const int LUAI_MAXCCALLS = 200;
		public const int MAXSTACK	= 250;
	}

	public static class LuaDef
	{
		public const int LUA_MINSTACK 			= 20;
		public const int BASIC_STACK_SIZE		= LUA_MINSTACK * 2;
		public const int EXTRA_STACK			= 5;

		public const int LUA_RIDX_MAINTHREAD 	= 1;
		public const int LUA_RIDX_GLOBALS 		= 2;
		public const int LUA_RIDX_LAST 			= LUA_RIDX_GLOBALS;

		public const int LUA_MULTRET			= -1;

		public const int LUA_REGISTRYINDEX		= LuaConf.LUAI_FIRSTPSEUDOIDX;

		public const int LUA_IDSIZE				= 60;

		public const string LUA_VERSION_MAJOR	= "5";
		public const string LUA_VERSION_MINOR	= "5";
		public const string LUA_VERSION = "Lua " + LUA_VERSION_MAJOR + "." + LUA_VERSION_MINOR;

		public const string LUA_ENV = "_ENV";

		public const int BASE_CI_SIZE = 8;

		// event codes and masks of the debug hooks
		public const int LUA_HOOKCALL		= 0;
		public const int LUA_HOOKRET		= 1;
		public const int LUA_HOOKLINE		= 2;
		public const int LUA_HOOKCOUNT		= 3;
		public const int LUA_HOOKTAILCALL	= 4;

		public const int LUA_MASKCALL		= 1 << LUA_HOOKCALL;
		public const int LUA_MASKRET		= 1 << LUA_HOOKRET;
		public const int LUA_MASKLINE		= 1 << LUA_HOOKLINE;
		public const int LUA_MASKCOUNT		= 1 << LUA_HOOKCOUNT;
	}

	public static class LuaConstants
	{
		public const int LUA_NOREF = -2;
		public const int LUA_REFNIL = -1;
	}

	public enum LuaType
	{
		LUA_TNONE = -1,
		LUA_TNIL = 0,
		LUA_TBOOLEAN = 1,
		LUA_TLIGHTUSERDATA = 2,
		LUA_TNUMBER = 3,
		LUA_TSTRING = 4,
		LUA_TTABLE = 5,
		LUA_TFUNCTION = 6,
		LUA_TUSERDATA = 7,
		LUA_TTHREAD = 8,

		LUA_NUMTAGS = 9,

		LUA_TPROTO,
		LUA_TUPVAL,
		LUA_TDEADKEY,
	}

	internal enum ClosureType
	{
		LUA,
		CSHARP,
	}

	public enum ThreadStatus
	{
		LUA_OK			 = 0,
		LUA_YIELD		 = 1,
		LUA_ERRRUN		 = 2,
		LUA_ERRSYNTAX	 = 3,
		LUA_ERRMEM		 = 4,
		LUA_ERRERR		 = 5,

		LUA_ERRFILE		 = 6,
	}

	/* ORDER TM, ORDER OP */
	public enum LuaOp
	{
		LUA_OPADD	= 0,	/* ORDER TM, ORDER OP */
		LUA_OPSUB	= 1,
		LUA_OPMUL	= 2,
		LUA_OPMOD	= 3,
		LUA_OPPOW	= 4,
		LUA_OPDIV	= 5,
		LUA_OPIDIV	= 6,
		LUA_OPBAND	= 7,
		LUA_OPBOR	= 8,
		LUA_OPBXOR	= 9,
		LUA_OPSHL	= 10,
		LUA_OPSHR	= 11,
		LUA_OPUNM	= 12,
		LUA_OPBNOT	= 13,
	}

	public enum LuaEq
	{
		LUA_OPEQ	= 0,
		LUA_OPLT	= 1,
		LUA_OPLE	= 2,
	}

}

