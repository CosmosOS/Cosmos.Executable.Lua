// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	public class LuaDebug
	{
		public int			Event;
		public string 		Name;
		public string 		NameWhat;
		public int 			ActiveCIIndex;
		public int			CurrentLine;
		public int			NumUps;
		public bool			IsVarArg;
		public int			NumParams;
		public bool			IsTailCall;
		public string		Source;
		public int			LineDefined;
		public int			LastLineDefined;
		public string		What;
		public string		ShortSrc;
	}

	internal partial class LuaState
	{
		bool ILuaAPI.GetStack( int level, LuaDebug ar )
		{
			if( level < 0 )
				return false;

			int index;
			for( index = CI.Index; level > 0 && index > 0; --index )
				{ level--; }

			bool status = false;
			if( level == 0 && index > 0 ) {
				status = true;
				ar.ActiveCIIndex = index;
			}
			return status;
		}

		public int GetInfo( string what, LuaDebug ar )
		{
			CallInfo 	ci;
			StkId		func;

			int	pos = 0;
			if( what[pos] == '>' )
			{
				ci = null;
				func = Stack[Top.Index - 1];

				Utl.ApiCheck(func.V.TtIsFunction(), "function expected");
				pos++;

				Top = Stack[Top.Index-1];
			}
			else
			{
				ci = BaseCI[ar.ActiveCIIndex];
				func = Stack[ci.FuncIndex];
				Utl.Assert(Stack[ci.FuncIndex].V.TtIsFunction());
			}

			// var IsClosure( func.Value ) ? func.Value
			int status = AuxGetInfo( what, ar, func, ci );
			if( what.Contains( "f" ) )
			{
				Top.V.SetObj(ref func.V);
				IncrTop();
			}
			if( what.Contains( "L" ) )
			{
				CollectValidLines( func );
			}
			return status;
		}

		internal bool IsLuaFunction( int index )
		{
			StkId addr;
			return Index2Addr( index, out addr )
				&& addr.V.TtIsFunction() && addr.V.ClIsLuaClosure();
		}

		// lua_upvalueid: the closures sharing an upvalue give the same object
		internal object UpvalueId( int funcIndex, int n )
		{
			StkId addr;
			if( !Index2Addr( funcIndex, out addr ) )
				return null;
			if( addr.V.ClIsLuaClosure() )
				return addr.V.ClLValue().Upvals[n-1];
			return addr.V.ClCsValue().Upvals[n-1];
		}

		internal void UpvalueJoin( int funcIndex1, int n1, int funcIndex2, int n2 )
		{
			StkId f1, f2;
			Index2Addr( funcIndex1, out f1 );
			Index2Addr( funcIndex2, out f2 );
			f1.V.ClLValue().Upvals[n1-1] = f2.V.ClLValue().Upvals[n2-1];
		}

		// lua_sethook
		internal void SetHook( LuaHookDelegate func, int mask, int count )
		{
			if( func == null || mask == 0 ) // turn off hooks?
			{
				mask = 0;
				func = null;
			}
			if( CI.IsLua )
				OldPc = CI.SavedPc.Index;
			Hook = func;
			BaseHookCount = count;
			ResetHookCount();
			HookMask = (byte)mask;
		}

		// luaD_hook: runs the hook with the stack and the frame of the event
		internal void D_Hook( int ev, int line )
		{
			var hook = Hook;
			if( hook != null && AllowHook )
			{
				CallInfo ci = CI;
				int top = Top.Index;
				int ciTop = ci.TopIndex;
				LuaDebug ar = new LuaDebug();
				ar.Event = ev;
				ar.CurrentLine = line;
				ar.ActiveCIIndex = ci.Index;
				D_CheckStack( LuaDef.LUA_MINSTACK ); // ensure minimum stack size
				ci.TopIndex = Top.Index + LuaDef.LUA_MINSTACK;
				AllowHook = false; // cannot call hooks inside a hook
				ci.CallStatus |= CallStatus.CIST_HOOKED;
				hook( this, ar );
				AllowHook = true;
				ci.TopIndex = ciTop;
				Top = Stack[top];
				ci.CallStatus &= ~CallStatus.CIST_HOOKED;
			}
		}

		// the call hook of a Lua function, before its first instruction
		private void CallHook( CallInfo ci )
		{
			int hook = LuaDef.LUA_HOOKCALL;
			ci.SavedPc.Index++; // hooks assume 'pc' is already incremented
			var prev = BaseCI[ci.Index-1];
			if( prev.IsLua &&
				(prev.SavedPc - 1).Value.GET_OPCODE() == OpCode.OP_TAILCALL )
			{
				ci.CallStatus |= CallStatus.CIST_TAIL;
				hook = LuaDef.LUA_HOOKTAILCALL;
			}
			D_Hook( hook, -1 );
			ci.SavedPc.Index--; // correct 'pc'
		}

		// the count and line hooks, before an instruction runs
		private void TraceExec( CallInfo ci )
		{
			byte mask = HookMask;
			if( (mask & LuaDef.LUA_MASKCOUNT) != 0 && HookCount == 0 )
			{
				ResetHookCount();
				D_Hook( LuaDef.LUA_HOOKCOUNT, -1 );
			}
			if( (mask & LuaDef.LUA_MASKLINE) != 0 )
			{
				var p = GetCurrentLuaFunc(ci).Proto;
				int npc = ci.SavedPc.Index - 1;
				int newline = p.GetFuncLine( npc );
				if( npc == 0 || // call linehook when enter a new function,
					ci.SavedPc.Index <= OldPc || // when jump back (loop), or when
					newline != p.GetFuncLine( OldPc - 1 ) ) // enter a new line
					D_Hook( LuaDef.LUA_HOOKLINE, newline );
			}
			OldPc = ci.SavedPc.Index;
		}

		private string FindVararg( CallInfo ci, int n, out StkId pos )
		{
			int nparams = Stack[ci.FuncIndex].V.ClLValue().Proto.NumParams;
			pos = null;
			if( n >= ci.BaseIndex - ci.FuncIndex - nparams )
				return null; // no such vararg
			pos = Stack[ci.FuncIndex + nparams + n];
			return "(*vararg)"; // generic name for any vararg
		}

		private string FindLocal( CallInfo ci, int n, out StkId pos )
		{
			string name = null;
			int stackBase;
			pos = null;
			if( ci.IsLua )
			{
				if( n < 0 ) // access to vararg values?
					return FindVararg( ci, -n, out pos );
				stackBase = ci.BaseIndex;
				name = F_GetLocalName( GetCurrentLuaFunc(ci).Proto, n, ci.CurrentPc );
			}
			else stackBase = ci.FuncIndex + 1;
			if( name == null ) // no 'standard' name?
			{
				int limit = (ci == CI) ? Top.Index : BaseCI[ci.Index+1].FuncIndex;
				if( limit - stackBase >= n && n > 0 ) // is 'n' inside 'ci' stack?
					name = "(*temporary)"; // generic name for any valid slot
				else
					return null; // no name
			}
			pos = Stack[stackBase + (n - 1)];
			return name;
		}

		// lua_getlocal: with no `ar', the parameter names of the function on top
		internal string GetLocal( LuaDebug ar, int n )
		{
			if( ar == null )
			{
				var f = Stack[Top.Index-1];
				if( !f.V.TtIsFunction() || !f.V.ClIsLuaClosure() )
					return null;
				return F_GetLocalName( f.V.ClLValue().Proto, n, 0 );
			}
			StkId pos;
			var name = FindLocal( BaseCI[ar.ActiveCIIndex], n, out pos );
			if( name != null )
			{
				Top.V.SetObj(ref pos.V);
				IncrTop();
			}
			return name;
		}

		// lua_setlocal: pops the value
		internal string SetLocal( LuaDebug ar, int n )
		{
			StkId pos;
			var name = FindLocal( BaseCI[ar.ActiveCIIndex], n, out pos );
			if( name != null )
				pos.V.SetObj(ref Stack[Top.Index-1].V);
			Top = Stack[Top.Index-1];
			return name;
		}

		private int AuxGetInfo( string what, LuaDebug ar, StkId func, CallInfo ci )
		{
			int status = 1;
			for( int i=0; i<what.Length; ++i )
			{
				char c = what[i];
				switch( c )
				{
					case 'S':
					{
						FuncInfo( ar, func );
						break;
					}
					case 'l':
					{
						ar.CurrentLine = (ci != null && ci.IsLua) ? GetCurrentLine(ci) : -1;
						break;
					}
					case 'u':
					{
						Utl.Assert(func.V.TtIsFunction());
						if(func.V.ClIsLuaClosure()) {
							var lcl = func.V.ClLValue();
							ar.NumUps = lcl.Upvals.Length;
							ar.IsVarArg = lcl.Proto.IsVarArg;
							ar.NumParams = lcl.Proto.NumParams;
						}
						else if(func.V.ClIsCsClosure()) {
							var ccl = func.V.ClCsValue();
							ar.NumUps = ccl.Upvals == null ? 0 : ccl.Upvals.Length;
							ar.IsVarArg = true;
							ar.NumParams = 0;
						}
						else throw new System.NotImplementedException();
						break;
					}
					case 't':
					{
						ar.IsTailCall = (ci != null)
							? ( (ci.CallStatus & CallStatus.CIST_TAIL) != 0 )
							: false;
						break;
					}
					case 'n':
					{
						if( ci != null
							&& ((ci.CallStatus & CallStatus.CIST_TAIL) == 0)
							&& BaseCI[ci.Index-1].IsLua )
						{
							ar.NameWhat = GetFuncName( BaseCI[ci.Index-1], out ar.Name );
						}
						else
						{
							ar.NameWhat = null;
						}
						if( ar.NameWhat == null )
						{
							ar.NameWhat = ""; // not found
							ar.Name = null;
						}
						break;
					}
					case 'L':
					case 'f': // handled by GetInfo
						break;
					default: status = 0; // invalid option
						break;
				}
			}
			return status;
		}

		private void CollectValidLines( StkId func )
		{
			Utl.Assert(func.V.TtIsFunction());
			if(func.V.ClIsLuaClosure()) {
				var lcl = func.V.ClLValue();
				var p = lcl.Proto;
				var lineinfo = p.LineInfo;
				var t = new LuaTable(this);
				Top.V.SetHValue(t);
				IncrTop();
				var v = new TValue();
				v.SetBValue(true);
				for( int i=0; i<lineinfo.Count; ++i )
					t.SetInt(lineinfo[i], ref v);
			}
			else if(func.V.ClIsCsClosure()) {
				Top.V.SetNilValue();
				IncrTop();
			}
			else throw new System.NotImplementedException();
		}

		private string GetFuncName( CallInfo ci, out string name )
		{
			var proto = GetCurrentLuaFunc(ci).Proto; // calling function
			var pc = ci.CurrentPc; // calling instruction index
			var ins = proto.Code[pc]; // calling instruction

			if( (ci.CallStatus & CallStatus.CIST_HOOKED) != 0 ) { // was it called inside a hook?
				name = "?";
				return "hook";
			}

			TMS tm;
			switch( ins.GET_OPCODE() )
			{
				case OpCode.OP_CALL:
				case OpCode.OP_TAILCALL:  /* get function name */
					return GetObjName(proto, pc, ins.GETARG_A(), out name);

				case OpCode.OP_TFORCALL: {  /* for iterator */
					name = "for iterator";
					return "for iterator";
				}

				/* all other instructions can call only through metamethods */
				case OpCode.OP_SELF:
				case OpCode.OP_GETTABUP:
				case OpCode.OP_GETTABLE: tm = TMS.TM_INDEX; break;

				case OpCode.OP_SETTABUP:
				case OpCode.OP_SETTABLE: tm = TMS.TM_NEWINDEX; break;

				case OpCode.OP_ADD: case OpCode.OP_SUB: case OpCode.OP_MUL: case OpCode.OP_MOD:
				case OpCode.OP_POW: case OpCode.OP_DIV: case OpCode.OP_IDIV: case OpCode.OP_BAND:
				case OpCode.OP_BOR: case OpCode.OP_BXOR: case OpCode.OP_SHL: case OpCode.OP_SHR:
					// ORDER OP, ORDER TM
					tm = (TMS)((int)TMS.TM_ADD + (int)(ins.GET_OPCODE() - OpCode.OP_ADD));
					break;
				case OpCode.OP_UNM: tm = TMS.TM_UNM; break;
				case OpCode.OP_BNOT: tm = TMS.TM_BNOT; break;
				case OpCode.OP_LEN: tm = TMS.TM_LEN; break;
				case OpCode.OP_EQ: tm = TMS.TM_EQ; break;
				case OpCode.OP_LT: tm = TMS.TM_LT; break;
				case OpCode.OP_LE: tm = TMS.TM_LE; break;
				case OpCode.OP_CONCAT: tm = TMS.TM_CONCAT; break;

				default:
					name = null;
					return null;  /* else no useful name can be found */
			}

			name = GetTagMethodName( tm );
			return "metamethod";
		}

		private void FuncInfo( LuaDebug ar, StkId func )
		{
			Utl.Assert(func.V.TtIsFunction());
			if(func.V.ClIsLuaClosure()) {
				var lcl = func.V.ClLValue();
				var p = lcl.Proto;
				ar.Source = p.Source ?? "=?";
				ar.LineDefined = p.LineDefined;
				ar.LastLineDefined = p.LastLineDefined;
				ar.What = (ar.LineDefined == 0) ? "main" : "Lua";
			}
			else if(func.V.ClIsCsClosure()) {
				ar.Source = "=[C]";
				ar.LineDefined = -1;
				ar.LastLineDefined = -1;
				ar.What = "C";
			}
			else throw new System.NotImplementedException();

			ar.ShortSrc = O_ChunkId( ar.Source );
		}

		// luaO_chunkid: a chunk name as messages show it, "file.lua" for
		// "@file.lua", "name" for "=name", [string "..."] for source code
		internal static string O_ChunkId( string source )
		{
			const int bufflen = LuaDef.LUA_IDSIZE;
			if( source == null )
				return "?";
			int l = source.Length;
			if( l > 0 && source[0] == '=' ) // 'literal' source
				return (l <= bufflen) ? source.Substring(1) : source.Substring(1, bufflen-1);
			if( l > 0 && source[0] == '@' ) // file name
				return (l <= bufflen) ? source.Substring(1) : "..." + source.Substring(l-(bufflen-4));
			// string; format as [string "source"]
			int nl = source.IndexOf('\n');
			int max = bufflen - 15; // room for [string "..."] and '\0'
			if( l < max && nl < 0 )
				return "[string \"" + source + "\"]";
			if( nl >= 0 ) l = nl;
			if( l > max ) l = max;
			return "[string \"" + source.Substring(0, l) + "...\"]";
		}

		private void AddInfo( string msg )
		{
			if( CI.IsLua )
			{
				var line = GetCurrentLine(CI);
				var src = GetCurrentLuaFunc(CI).Proto.Source;

				// 不能用 PushString, 因为 PushString 是 API 接口
				// API 接口中的 ApiIncrTop 会检查 Top 是否超过了 CI.Top 导致出错
				// api.PushString( msg );
				O_PushString( string.Format( "{0}:{1}: {2}",
					O_ChunkId( src ), line, msg ) );
			}
			else O_PushString( msg ); // no position outside Lua code, but a message
		}

		internal void G_RunError( string fmt, params object[] args )
		{
			AddInfo( args.Length == 0 ? fmt : string.Format( fmt, args ) );
			G_ErrorMsg();
		}

		private void G_ErrorMsg()
		{
			if( ErrFunc != 0 ) // is there an error handling function?
			{
				StkId errFunc = RestoreStack( ErrFunc );

				if(!errFunc.V.TtIsFunction())
					D_Throw( ThreadStatus.LUA_ERRERR );

				var below = Stack[Top.Index-1];
				Top.V.SetObj(ref below.V);
				below.V.SetObj(ref errFunc.V);
				IncrTop();
				
				D_Call( below, 1, false );
			}

			D_Throw( ThreadStatus.LUA_ERRRUN );
		}

		private string UpvalName( LuaProto p, int uv )
		{
			var name = (uv < p.Upvalues.Count) ? p.Upvalues[uv].Name : null;
			return name ?? "?";
		}

		private string GetUpvalueName( CallInfo ci, StkId o, out string name )
		{
			var func = Stack[ci.FuncIndex];
			Utl.Assert(func.V.TtIsFunction() && func.V.ClIsLuaClosure());
			var lcl = func.V.ClLValue();
			for(int i=0; i<lcl.Upvals.Length; ++i) {
				if( lcl.Upvals[i].V == o ) {
					name = UpvalName( lcl.Proto, i );
					return "upvalue";
				}
			}
			name = default(string);
			return null;
		}

		private void KName( LuaProto proto, int pc, int c, out string name )
		{
			if( Instruction.ISK(c) ) { // is `c' a constant
				var val = proto.K[Instruction.INDEXK(c)];
				if(val.V.TtIsString()) { // literal constant?
					name = val.V.SValue();
					return;
				}
				// else no reasonable name found
			}
			else { // `c' is a register
				string what = GetObjName( proto, pc, c, out name );
				if( what == "constant" ) { // found a constant name
					return; // `name' already filled
				}
				// else no reasonable name found
			}
			name = "?"; // no reasonable name found
		}

		// filterpc: code inside a jump cannot tell who sets the register
		private static int FilterPc( int pc, int jmptarget )
		{
			return pc < jmptarget ? -1 : pc;
		}

		private int FindSetReg( LuaProto proto, int lastpc, int reg )
		{
			var setreg = -1; // keep last instruction that changed `reg'
			var jmptarget = 0; // any code before this address is conditional
			for( int pc=0; pc<lastpc; ++pc ) {
				var ins = proto.Code[pc];
				var op  = ins.GET_OPCODE();
				var a 	= ins.GETARG_A();
				switch( op ) {
					case OpCode.OP_LOADNIL: {
						var b = ins.GETARG_B();
						// set registers from `a' to `a+b'
						if( a <= reg && reg <= a + b )
							setreg = FilterPc( pc, jmptarget );
						break;
					}

					case OpCode.OP_TFORCALL: {
						// affect all regs above its base
						if( reg >= a+2 )
							setreg = FilterPc( pc, jmptarget );
						break;
					}

					case OpCode.OP_CALL:
					case OpCode.OP_TAILCALL: {
						// affect all registers above base
						if( reg >= a )
							setreg = FilterPc( pc, jmptarget );
						break;
					}

					case OpCode.OP_JMP: {
						var b = ins.GETARG_sBx();
						var dest = pc + 1 + b;
						// jump is forward and do not skip `lastpc'?
						if( pc < dest && dest <= lastpc ) {
							if( dest > jmptarget )
								jmptarget = dest; // update 'jmptarget'
						}
						break;
					}

					default: {
						// any instruction that set A
						if( Coder.TestAMode( op ) && reg == a ) {
							setreg = FilterPc( pc, jmptarget );
						}
						break;
					}
				}
			}
			return setreg;
		}

		private string GetObjName( LuaProto proto, int lastpc, int reg,
			out string name )
		{
			name = F_GetLocalName( proto, reg+1, lastpc );
			if( name != null ) // is a local?
				return "local";

			// else try symbolic execution
			var pc = FindSetReg( proto, lastpc, reg );
			if( pc != -1 )
			{
				var ins = proto.Code[pc];
				var op = ins.GET_OPCODE();
				switch( op )
				{
					case OpCode.OP_MOVE: {
						var b = ins.GETARG_B(); // move from `b' to `a'
						if( b < ins.GETARG_A() )
							return GetObjName(proto, pc, b, out name);
						break;
					}
					case OpCode.OP_GETTABUP:
					case OpCode.OP_GETTABLE: {
						var k = ins.GETARG_C();
						var t = ins.GETARG_B();
						var vn = (op == OpCode.OP_GETTABLE)
							? F_GetLocalName( proto, t+1, pc )
							: UpvalName( proto, t );
						KName( proto, pc, k, out name );
						return (vn == LuaDef.LUA_ENV) ? "global" : "field";
					}

					case OpCode.OP_GETUPVAL: {
						name = UpvalName( proto, ins.GETARG_B() );
						return "upvalue";
					}

					case OpCode.OP_LOADK:
					case OpCode.OP_LOADKX: {
						var b = (op == OpCode.OP_LOADK)
							? ins.GETARG_Bx()
							: proto.Code[pc+1].GETARG_Ax();
						var val = proto.K[b];
						if(val.V.TtIsString())
						{
							name = val.V.SValue();
							return "constant";
						}
						break;
					}

					case OpCode.OP_SELF: {
						var k = ins.GETARG_C(); // key index
						KName( proto, pc, k, out name );
						return "method";
					}

					default: break; // go through to return null
				}
			}

			return null; // could not find reasonable name
		}

		private bool IsInStack( CallInfo ci, StkId o )
		{
			// a register of the frame, not a constant or a closed upvalue
			return ci.BaseIndex <= o.Index && o.Index < ci.TopIndex
				&& o.Index < Stack.Length && Stack[o.Index] == o;
		}

		private void G_SimpleTypeError( ref TValue o, string op )
		{
			string t = ObjTypeName( ref o );
			G_RunError( "attempt to {0} a {1} value", op, t );
		}

		// varinfo: " (kind 'name')" for a value with a name in the code
		private string VarInfo( StkId o )
		{
			CallInfo ci = CI;
			string name = null;
			string kind = null;
			if( ci.IsLua )
			{
				kind = GetUpvalueName( ci, o, out name); // check whether 'o' is an upvalue
				if( kind == null && IsInStack( ci, o ) ) // no? try a register
				{
					var lcl = Stack[ci.FuncIndex].V.ClLValue();
					kind = GetObjName( lcl.Proto, ci.CurrentPc,
						(o.Index - ci.BaseIndex), out name );
				}
			}
			return kind != null ? string.Format( " ({0} '{1}')", kind, name ) : "";
		}

		private void G_TypeError( StkId o, string op )
		{
			string t = ObjTypeName(ref o.V);
			G_RunError( "attempt to {0} a {1} value{2}", op, t, VarInfo( o ) );
		}

		// luaG_opinterror: an operation on a value that is not a number
		private void G_OpIntError( StkId p1, StkId p2, string msg )
		{
			double temp;
			if( !V_ToNumber( ref p1.V, out temp ) ) // first operand is wrong?
				{ p2 = p1; } // now second is wrong too

			G_TypeError( p2, msg );
		}

		// luaG_tointerror: a bitwise operation on a float with no integer value
		private void G_ToIntError( StkId p1, StkId p2 )
		{
			long temp;
			if( !V_ToInteger( ref p1.V, out temp, 0 ) )
				{ p2 = p1; }
			G_RunError( "number{0} has no integer representation", VarInfo( p2 ) );
		}

		private void G_OrderError( StkId p1, StkId p2 )
		{
			string t1 = ObjTypeName(ref p1.V);
			string t2 = ObjTypeName(ref p2.V);
			if( t1 == t2 )
				G_RunError( "attempt to compare two {0} values", t1 );
			else
				G_RunError( "attempt to compare {0} with {1}", t1, t2 );
		}

		private void G_ConcatError( StkId p1, StkId p2 )
		{
			if( p1.V.TtIsString() || p1.V.TtIsNumber() )
				p1 = p2;
			G_TypeError( p1, "concatenate" );
		}
	}

}

