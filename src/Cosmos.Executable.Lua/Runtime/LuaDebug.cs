// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Runtime.CompilerServices;

namespace Cosmos.Executable.Lua
{
	public class LuaDebug
	{
		public int			Event;
		public string 		Name;			// (n)
		public string 		NameWhat;		// (n) 'global', 'local', 'field', 'method'
		public int 			ActiveCIIndex;	// active function
		public int			CurrentLine;	// (l)
		public int			NumUps;			// (u) number of upvalues
		public bool			IsVarArg;		// (u)
		public int			NumParams;		// (u) number of parameters
		public bool			IsTailCall;		// (t)
		public int			ExtraArgs;		// (t) number of extra arguments
		public int			FTransfer;		// (r) index of first value transferred
		public int			NTransfer;		// (r) number of transferred values
		public string		Source;			// (S)
		public int			LineDefined;	// (S)
		public int			LastLineDefined;	// (S)
		public string		What;			// (S) 'Lua', 'C', 'main', 'tail'
		public string		ShortSrc;		// (S)
	}

	// ldebug.c of Lua 5.5: debug interface
	internal partial class LuaState
	{
		private const string StrLocal = "local";
		private const string StrUpval = "upvalue";

		/*
		** Mark for entries in 'lineinfo' array that has absolute information in
		** 'abslineinfo' array
		*/
		internal const sbyte ABSLINEINFO = -0x80;

		/*
		** MAXimum number of successive Instructions WiTHout ABSolute line
		** information. (A power of two allows fast divisions.)
		*/
		internal const int MAXIWTHABS = 128;

		private static int CurrentPc( CallInfo ci )
		{
			Utl.Assert( ci.IsLua );
			return ci.SavedPc.Index - 1;
		}

		/*
		** Get a "base line" to find the line corresponding to an instruction.
		** Base lines are regularly placed at MAXIWTHABS intervals, so usually
		** an integer division gets the right place. When the source file has
		** large sequences of empty/comment lines, it may need extra entries,
		** so the original estimate needs a correction.
		** If the original estimate is -1, the initial 'if' ensures that the
		** 'while' will run at least once.
		** The assertion that the estimate is a lower bound for the correct base
		** is valid as long as the debug info has been generated with the same
		** value for MAXIWTHABS or smaller. (Previous releases use a little
		** smaller value.)
		*/
		private static int GetBaseLine( LuaProto f, int pc, out int basepc )
		{
			var abs = f.AbsLineInfo;
			if( abs.Count == 0 || pc < abs[0].Pc )
			{
				basepc = -1; // start from the beginning
				return f.LineDefined;
			}
			else
			{
				int i = (int)((uint)pc / MAXIWTHABS) - 1; // get an estimate
				/* estimate must be a lower bound of the correct base */
				Utl.Assert( i < 0 || (i < abs.Count && abs[i].Pc <= pc) );
				while( i + 1 < abs.Count && pc >= abs[i + 1].Pc )
					i++; // low estimate; adjust it
				basepc = abs[i].Pc;
				return abs[i].Line;
			}
		}

		/*
		** Get the line corresponding to instruction 'pc' in function 'f';
		** first gets a base line and from there does the increments until
		** the desired instruction.
		*/
		internal static int G_GetFuncLine( LuaProto f, int pc )
		{
			if( f.LineInfo.Count == 0 ) // no debug information?
				return -1;
			else
			{
				int basepc;
				int baseline = GetBaseLine( f, pc, out basepc );
				while( basepc++ < pc ) // walk until given instruction
				{
					Utl.Assert( f.LineInfo[basepc] != ABSLINEINFO );
					baseline += f.LineInfo[basepc]; // correct line
				}
				return baseline;
			}
		}

		private int GetCurrentLineOf( CallInfo ci )
		{
			return G_GetFuncLine( Stack[ci.FuncIndex].V.ClLValue().Proto, CurrentPc( ci ) );
		}

		/*
		** Set 'trap' for all active Lua frames.
		*/
		private void SetTraps()
		{
			for( int i = CI.Index; i >= 0; --i )
			{
				if( BaseCI[i].IsLua )
					BaseCI[i].Trap = true;
			}
		}

		// lua_sethook
		internal void SetHook( LuaHookDelegate func, int mask, int count )
		{
			if( func == null || mask == 0 ) // turn off hooks?
			{
				mask = 0;
				func = null;
			}
			Hook = func;
			BaseHookCount = count;
			ResetHookCount();
			HookMask = (byte)mask;
			if( mask != 0 )
				SetTraps(); // to trace inside 'luaV_execute'
		}

		bool ILuaAPI.GetStack( int level, LuaDebug ar )
		{
			if( level < 0 )
				return false; // invalid (negative) level

			int index;
			for( index = CI.Index; level > 0 && index > 0; --index )
				{ level--; }

			bool status = false;
			if( level == 0 && index > 0 ) { // level found?
				status = true;
				ar.ActiveCIIndex = index;
			}
			return status; // else no such level
		}

		private static string UpvalName( LuaProto p, int uv )
		{
			Utl.Assert( uv < p.Upvalues.Count );
			var name = p.Upvalues[uv].Name;
			return name ?? "?";
		}

		private string FindVararg( CallInfo ci, int n, out StkId pos )
		{
			pos = null;
			if( (Stack[ci.FuncIndex].V.ClLValue().Proto.Flag & LuaProto.PF_VAHID) != 0 )
			{
				int nextra = ci.NExtraArgs;
				if( n >= -nextra ) // 'n' is negative
				{
					pos = Stack[ci.FuncIndex - nextra - (n + 1)];
					return "(vararg)"; // generic name for any vararg
				}
			}
			return null; // no such vararg
		}

		internal string G_FindLocal( CallInfo ci, int n, out StkId pos )
		{
			int stackBase = ci.FuncIndex + 1;
			string name = null;
			pos = null;
			if( ci.IsLua )
			{
				if( n < 0 ) // access to vararg values?
					return FindVararg( ci, n, out pos );
				else
					name = F_GetLocalName( Stack[ci.FuncIndex].V.ClLValue().Proto, n, CurrentPc( ci ) );
			}
			if( name == null ) // no 'standard' name?
			{
				int limit = (ci == CI) ? Top.Index : BaseCI[ci.Index + 1].FuncIndex;
				if( limit - stackBase >= n && n > 0 ) // is 'n' inside 'ci' stack?
				{
					/* generic name for any valid slot */
					name = ci.IsLua ? "(temporary)" : "(C temporary)";
				}
				else
					return null; // no name
			}
			pos = Stack[stackBase + (n - 1)];
			return name;
		}

		// lua_getlocal: with no 'ar', the parameter names of the function on top
		internal string GetLocal( LuaDebug ar, int n )
		{
			string name;
			if( ar == null ) // information about non-active function?
			{
				var f = Stack[Top.Index-1];
				if( !f.V.TtIsFunction() || !f.V.ClIsLuaClosure() ) // not a Lua function?
					name = null;
				else // consider live variables at function start (parameters)
					name = F_GetLocalName( f.V.ClLValue().Proto, n, 0 );
			}
			else // active function; get information through 'ar'
			{
				StkId pos;
				name = G_FindLocal( BaseCI[ar.ActiveCIIndex], n, out pos );
				if( name != null )
				{
					Top.V.SetObj( ref pos.V );
					ApiIncrTop();
				}
			}
			return name;
		}

		// lua_setlocal: pops the value
		internal string SetLocal( LuaDebug ar, int n )
		{
			StkId pos;
			var name = G_FindLocal( BaseCI[ar.ActiveCIIndex], n, out pos );
			if( name != null )
			{
				pos.V.SetObj( ref Stack[Top.Index-1].V );
				Top = Stack[Top.Index-1]; // pop value
			}
			return name;
		}

		private void FuncInfo( LuaDebug ar, StkId func )
		{
			if( !func.V.ClIsLuaClosure() )
			{
				ar.Source = "=[C]";
				ar.LineDefined = -1;
				ar.LastLineDefined = -1;
				ar.What = "C";
			}
			else
			{
				var p = func.V.ClLValue().Proto;
				ar.Source = p.Source ?? "=?";
				ar.LineDefined = p.LineDefined;
				ar.LastLineDefined = p.LastLineDefined;
				ar.What = (ar.LineDefined == 0) ? "main" : "Lua";
			}
			ar.ShortSrc = O_ChunkId( ar.Source );
		}

		private static int NextLine( LuaProto p, int currentline, int pc )
		{
			if( p.LineInfo[pc] != ABSLINEINFO )
				return currentline + p.LineInfo[pc];
			else
				return G_GetFuncLine( p, pc );
		}

		private void CollectValidLines( StkId func )
		{
			if( !func.V.ClIsLuaClosure() )
			{
				Top.V.SetNilValue();
				ApiIncrTop();
			}
			else
			{
				var p = func.V.ClLValue().Proto;
				int currentline = p.LineDefined;
				var t = new LuaTable( this ); // new table to store active lines
				Top.V.SetHValue( t ); // push it on stack
				ApiIncrTop();
				if( p.LineInfo.Count != 0 ) // proto with debug information?
				{
					int i;
					var v = new TValue();
					v.SetBValue( true ); // boolean 'true' to be the value of all indices
					if( !p.IsVarArg ) // regular function?
						i = 0; // consider all instructions
					else // vararg function
					{
						Utl.Assert( p.Code[0].GET_OPCODE() == OpCode.OP_VARARGPREP );
						currentline = NextLine( p, currentline, 0 );
						i = 1; // skip first instruction (OP_VARARGPREP)
					}
					for( ; i < p.LineInfo.Count; i++ ) // for each instruction
					{
						currentline = NextLine( p, currentline, i ); // get its line
						t.SetInt( currentline, ref v ); // table[line] = true
					}
				}
			}
		}

		private string GetFuncName( CallInfo ci, out string name )
		{
			/* calling function is a known function? */
			if( ci != null && (ci.CallStatus & CallStatus.CIST_TAIL) == 0 )
				return FuncNameFromCall( ci.Previous, out name );
			name = null;
			return null; // no way to find a name
		}

		private int AuxGetInfo( string what, LuaDebug ar, StkId func, CallInfo ci )
		{
			int status = 1;
			for( int i=0; i<what.Length; ++i )
			{
				switch( what[i] )
				{
					case 'S':
					{
						FuncInfo( ar, func );
						break;
					}
					case 'l':
					{
						ar.CurrentLine = (ci != null && ci.IsLua) ? GetCurrentLineOf( ci ) : -1;
						break;
					}
					case 'u':
					{
						if( !func.V.ClIsLuaClosure() )
						{
							var ccl = func.V.ClCsValue();
							ar.NumUps = ccl.Upvals == null ? 0 : ccl.Upvals.Length;
							ar.IsVarArg = true;
							ar.NumParams = 0;
						}
						else
						{
							var lcl = func.V.ClLValue();
							ar.NumUps = lcl.Upvals.Length;
							ar.IsVarArg = lcl.Proto.IsVarArg;
							ar.NumParams = lcl.Proto.NumParams;
						}
						break;
					}
					case 't':
					{
						if( ci != null )
						{
							ar.IsTailCall = (ci.CallStatus & CallStatus.CIST_TAIL) != 0;
							ar.ExtraArgs = ci.NCallMeta;
						}
						else
						{
							ar.IsTailCall = false;
							ar.ExtraArgs = 0;
						}
						break;
					}
					case 'n':
					{
						ar.NameWhat = GetFuncName( ci, out ar.Name );
						if( ar.NameWhat == null )
						{
							ar.NameWhat = ""; // not found
							ar.Name = null;
						}
						break;
					}
					case 'r':
					{
						if( ci == null || (ci.CallStatus & CallStatus.CIST_HOOKED) == 0 )
							ar.FTransfer = ar.NTransfer = 0;
						else
						{
							ar.FTransfer = FTransfer;
							ar.NTransfer = NTransfer;
						}
						break;
					}
					case 'L':
					case 'f': // handled by lua_getinfo
						break;
					default: status = 0; // invalid option
						break;
				}
			}
			return status;
		}

		// lua_getinfo
		public int GetInfo( string what, LuaDebug ar )
		{
			CallInfo ci;
			StkId func;
			if( what.Length > 0 && what[0] == '>' )
			{
				ci = null;
				func = Stack[Top.Index - 1];
				Utl.ApiCheck( func.V.TtIsFunction(), "function expected" );
				what = what.Substring( 1 ); // skip the '>'
				Top = Stack[Top.Index - 1]; // pop function
			}
			else
			{
				ci = BaseCI[ar.ActiveCIIndex];
				func = Stack[ci.FuncIndex];
				Utl.Assert( func.V.TtIsFunction() );
			}
			int status = AuxGetInfo( what, ar, func, ci );
			if( what.IndexOf( 'f' ) >= 0 )
			{
				Top.V.SetObj( ref func.V );
				ApiIncrTop();
			}
			if( what.IndexOf( 'L' ) >= 0 )
				CollectValidLines( func );
			return status;
		}

		internal bool IsLuaFunction( int index )
		{
			StkId addr;
			return Index2Addr( index, out addr )
				&& addr.V.TtIsFunction() && addr.V.ClIsLuaClosure();
		}

		// lua_upvalueid: the closures sharing an upvalue give the same object;
		// null for an index out of range
		internal object UpvalueId( int funcIndex, int n )
		{
			StkId addr;
			if( !Index2Addr( funcIndex, out addr ) || !addr.V.TtIsFunction() )
				return null;
			if( addr.V.ClIsLuaClosure() )
			{
				var f = addr.V.ClLValue();
				return (1 <= n && n <= f.Upvals.Length) ? f.Upvals[n-1] : null;
			}
			var c = addr.V.ClCsValue();
			if( c.Upvals != null && 1 <= n && n <= c.Upvals.Length )
				return c.Upvals[n-1];
			return null; // light C functions have no upvalues
		}

		internal void UpvalueJoin( int funcIndex1, int n1, int funcIndex2, int n2 )
		{
			StkId f1, f2;
			Index2Addr( funcIndex1, out f1 );
			Index2Addr( funcIndex2, out f2 );
			f1.V.ClLValue().Upvals[n1-1] = f2.V.ClLValue().Upvals[n2-1];
		}

		/*
		** {======================================================
		** Symbolic Execution
		** =======================================================
		*/

		private static int FilterPc( int pc, int jmptarget )
		{
			if( pc < jmptarget ) // is code conditional (inside a jump)?
				return -1; // cannot know who sets that register
			else return pc; // current position sets that register
		}

		/*
		** Try to find last instruction before 'lastpc' that modified register 'reg'.
		*/
		private static int FindSetReg( LuaProto p, int lastpc, int reg )
		{
			int setreg = -1; // keep last instruction that changed 'reg'
			int jmptarget = 0; // any code before this address is conditional
			if( OpCodeInfo.TestMMMode( p.Code[lastpc].GET_OPCODE() ) )
				lastpc--; // previous instruction was not actually executed
			for( int pc = 0; pc < lastpc; pc++ )
			{
				Instruction i = p.Code[pc];
				OpCode op = i.GET_OPCODE();
				int a = i.GETARG_A();
				bool change; // true if current instruction changed 'reg'
				switch( op )
				{
					case OpCode.OP_LOADNIL: // set registers from 'a' to 'a+b'
					{
						int b = i.GETARG_B();
						change = (a <= reg && reg <= a + b);
						break;
					}
					case OpCode.OP_TFORCALL: // affect all regs above its base
					{
						change = (reg >= a + 2);
						break;
					}
					case OpCode.OP_CALL:
					case OpCode.OP_TAILCALL: // affect all registers above base
					{
						change = (reg >= a);
						break;
					}
					case OpCode.OP_JMP: // doesn't change registers, but changes 'jmptarget'
					{
						int b = i.GETARG_sJ();
						int dest = pc + 1 + b;
						/* jump does not skip 'lastpc' and is larger than current one? */
						if( dest <= lastpc && dest > jmptarget )
							jmptarget = dest; // update 'jmptarget'
						change = false;
						break;
					}
					default: // any instruction that sets A
						change = (OpCodeInfo.TestAMode( op ) && reg == a);
						break;
				}
				if( change )
					setreg = FilterPc( pc, jmptarget );
			}
			return setreg;
		}

		/*
		** Find a "name" for the constant 'c'.
		*/
		private static string KName( LuaProto p, int index, out string name )
		{
			var kvalue = p.K[index];
			if( kvalue.V.TtIsString() )
			{
				name = kvalue.V.SValue();
				return "constant";
			}
			else
			{
				name = "?";
				return null;
			}
		}

		private static string BasicGetObjName( LuaProto p, ref int ppc, int reg, out string name )
		{
			int pc = ppc;
			name = F_GetLocalName( p, reg + 1, pc );
			if( name != null ) // is a local?
				return StrLocal;
			/* else try symbolic execution */
			ppc = pc = FindSetReg( p, pc, reg );
			if( pc != -1 ) // could find instruction?
			{
				Instruction i = p.Code[pc];
				OpCode op = i.GET_OPCODE();
				switch( op )
				{
					case OpCode.OP_MOVE:
					{
						int b = i.GETARG_B(); // move from 'b' to 'a'
						if( b < i.GETARG_A() )
							return BasicGetObjName( p, ref ppc, b, out name ); // get name for 'b'
						break;
					}
					case OpCode.OP_GETUPVAL:
					{
						name = UpvalName( p, i.GETARG_B() );
						return StrUpval;
					}
					case OpCode.OP_LOADK: return KName( p, i.GETARG_Bx(), out name );
					case OpCode.OP_LOADKX: return KName( p, p.Code[pc + 1].GETARG_Ax(), out name );
					default: break;
				}
			}
			return null; // could not find reasonable name
		}

		/*
		** Find a "name" for the register 'c'.
		*/
		private static void RName( LuaProto p, int pc, int c, out string name )
		{
			string what = BasicGetObjName( p, ref pc, c, out name ); // search for 'c'
			if( !(what != null && what[0] == 'c') ) // did not find a constant name?
				name = "?";
		}

		/*
		** Check whether table being indexed by instruction 'i' is the
		** environment '_ENV'
		*/
		private static string IsEnv( LuaProto p, int pc, Instruction i, bool isup )
		{
			int t = i.GETARG_B(); // table index
			string name; // name of indexed variable
			if( isup ) // is 't' an upvalue?
				name = UpvalName( p, t );
			else // 't' is a register
			{
				string what = BasicGetObjName( p, ref pc, t, out name );
				/* 'name' must be the name of a local variable (at the current
				   level or an upvalue) */
				if( (object)what != (object)StrLocal && (object)what != (object)StrUpval )
					name = null; // cannot be the variable _ENV
			}
			return (name != null && name == LuaDef.LUA_ENV) ? "global" : "field";
		}

		/*
		** Extend 'basicgetobjname' to handle table accesses
		*/
		private static string GetObjName( LuaProto p, int lastpc, int reg, out string name )
		{
			string kind = BasicGetObjName( p, ref lastpc, reg, out name );
			if( kind != null )
				return kind;
			else if( lastpc != -1 ) // could find instruction?
			{
				Instruction i = p.Code[lastpc];
				OpCode op = i.GET_OPCODE();
				switch( op )
				{
					case OpCode.OP_GETTABUP:
					{
						int k = i.GETARG_C(); // key index
						KName( p, k, out name );
						return IsEnv( p, lastpc, i, true );
					}
					case OpCode.OP_GETTABLE: case OpCode.OP_GETVARG:
					{
						int k = i.GETARG_C(); // key index
						RName( p, lastpc, k, out name );
						return IsEnv( p, lastpc, i, false );
					}
					case OpCode.OP_GETI:
					{
						name = "integer index";
						return "field";
					}
					case OpCode.OP_GETFIELD:
					{
						int k = i.GETARG_C(); // key index
						KName( p, k, out name );
						return IsEnv( p, lastpc, i, false );
					}
					case OpCode.OP_SELF:
					{
						int k = i.GETARG_C(); // key index
						KName( p, k, out name );
						return "method";
					}
					default: break; // go through to return NULL
				}
			}
			return null; // could not find reasonable name
		}

		/*
		** Try to find a name for a function based on the code that called it.
		** (Only works when function was called by a Lua function.)
		** Returns what the name is (e.g., "for iterator", "method",
		** "metamethod") and sets '*name' to point to the name.
		*/
		private static string FuncNameFromCode( LuaProto p, int pc, out string name )
		{
			TMS tm;
			Instruction i = p.Code[pc]; // calling instruction
			switch( i.GET_OPCODE() )
			{
				case OpCode.OP_CALL:
				case OpCode.OP_TAILCALL:
					return GetObjName( p, pc, i.GETARG_A(), out name ); // get function name
				case OpCode.OP_TFORCALL: // for iterator
				{
					name = "for iterator";
					return "for iterator";
				}
				/* other instructions can do calls through metamethods */
				case OpCode.OP_SELF: case OpCode.OP_GETTABUP: case OpCode.OP_GETTABLE:
				case OpCode.OP_GETI: case OpCode.OP_GETFIELD:
					tm = TMS.TM_INDEX;
					break;
				case OpCode.OP_SETTABUP: case OpCode.OP_SETTABLE: case OpCode.OP_SETI: case OpCode.OP_SETFIELD:
					tm = TMS.TM_NEWINDEX;
					break;
				case OpCode.OP_MMBIN: case OpCode.OP_MMBINI: case OpCode.OP_MMBINK:
					tm = (TMS)i.GETARG_C();
					break;
				case OpCode.OP_UNM: tm = TMS.TM_UNM; break;
				case OpCode.OP_BNOT: tm = TMS.TM_BNOT; break;
				case OpCode.OP_LEN: tm = TMS.TM_LEN; break;
				case OpCode.OP_CONCAT: tm = TMS.TM_CONCAT; break;
				case OpCode.OP_EQ: tm = TMS.TM_EQ; break;
				/* no cases for OP_EQI and OP_EQK, as they don't call metamethods */
				case OpCode.OP_LT: case OpCode.OP_LTI: case OpCode.OP_GTI: tm = TMS.TM_LT; break;
				case OpCode.OP_LE: case OpCode.OP_LEI: case OpCode.OP_GEI: tm = TMS.TM_LE; break;
				case OpCode.OP_CLOSE: case OpCode.OP_RETURN: tm = TMS.TM_CLOSE; break;
				default:
					name = null;
					return null; // cannot find a reasonable name
			}
			name = GetTagMethodName( tm ).Substring( 2 );
			return "metamethod";
		}

		/*
		** Try to find a name for a function based on how it was called.
		*/
		private string FuncNameFromCall( CallInfo ci, out string name )
		{
			if( (ci.CallStatus & CallStatus.CIST_HOOKED) != 0 ) // was it called inside a hook?
			{
				name = "?";
				return "hook";
			}
			else if( (ci.CallStatus & CallStatus.CIST_FIN) != 0 ) // was it called as a finalizer?
			{
				name = "__gc";
				return "metamethod"; // report it as such
			}
			else if( ci.IsLua )
				return FuncNameFromCode( Stack[ci.FuncIndex].V.ClLValue().Proto, CurrentPc( ci ), out name );
			name = null;
			return null;
		}

		/* }====================================================== */

		/*
		** Check whether pointer 'o' points to some value in the stack frame of
		** the current function and, if so, returns its index.
		*/
		private int InStack( CallInfo ci, ref TValue o )
		{
			int stackBase = ci.FuncIndex + 1;
			for( int pos = 0; stackBase + pos < ci.TopIndex; pos++ )
			{
				if( Unsafe.AreSame( ref o, ref Stack[stackBase + pos].V ) )
					return pos;
			}
			return -1; // not found
		}

		/*
		** Checks whether value 'o' came from an upvalue. (That can only happen
		** with instructions OP_GETTABUP/OP_SETTABUP, which operate directly on
		** upvalues.)
		*/
		private string GetUpvalName( CallInfo ci, ref TValue o, out string name )
		{
			var c = Stack[ci.FuncIndex].V.ClLValue();
			for( int i = 0; i < c.Upvals.Length; i++ )
			{
				if( Unsafe.AreSame( ref c.Upvals[i].V.V, ref o ) )
				{
					name = UpvalName( c.Proto, i );
					return StrUpval;
				}
			}
			name = null;
			return null;
		}

		private static string FormatVarInfo( string kind, string name )
		{
			if( kind == null )
				return ""; // no information
			else
				return string.Format( " ({0} '{1}')", kind, name );
		}

		/*
		** Build a string with a "description" for the value 'o', such as
		** "variable 'x'" or "upvalue 'y'".
		*/
		private string VarInfo( ref TValue o )
		{
			CallInfo ci = CI;
			string name = null; // to avoid warnings
			string kind = null;
			if( ci.IsLua )
			{
				kind = GetUpvalName( ci, ref o, out name ); // check whether 'o' is an upvalue
				if( kind == null ) // not an upvalue?
				{
					int reg = InStack( ci, ref o ); // try a register
					if( reg >= 0 ) // is 'o' a register?
						kind = GetObjName( Stack[ci.FuncIndex].V.ClLValue().Proto, CurrentPc( ci ), reg, out name );
				}
			}
			return FormatVarInfo( kind, name );
		}

		/*
		** Raise a type error
		*/
		private void TypeError( ref TValue o, string op, string extra )
		{
			string t = ObjTypeName( ref o );
			G_RunError( "attempt to {0} a {1} value{2}", op, t, extra );
		}

		/*
		** Raise a type error with "standard" information about the faulty
		** object 'o' (using 'varinfo').
		*/
		internal void G_TypeError( ref TValue o, string op )
		{
			TypeError( ref o, op, VarInfo( ref o ) );
		}

		/*
		** Raise an error for calling a non-callable object. Try to find a name
		** for the object based on how it was called ('funcnamefromcall'); if it
		** cannot get a name there, try 'varinfo'.
		*/
		private void G_CallError( StkId o )
		{
			CallInfo ci = CI;
			string name;
			string kind = FuncNameFromCall( ci, out name );
			string extra = (kind != null) ? FormatVarInfo( kind, name ) : VarInfo( ref o.V );
			TypeError( ref o.V, "call", extra );
		}

		private void G_ForError( ref TValue o, string what )
		{
			G_RunError( "bad 'for' {0} (number expected, got {1})", what, ObjTypeName( ref o ) );
		}

		private void G_ConcatError( ref TValue p1, ref TValue p2 )
		{
			if( p1.TtIsString() || p1.TtIsNumber() ) p1 = ref p2;
			G_TypeError( ref p1, "concatenate" );
		}

		private void G_OpIntError( ref TValue p1, ref TValue p2, string msg )
		{
			if( !p1.TtIsNumber() ) // first operand is wrong?
				p2 = ref p1; // now second is wrong
			G_TypeError( ref p2, msg );
		}

		/*
		** Error when both values are convertible to numbers, but not to integers
		*/
		private void G_ToIntError( ref TValue p1, ref TValue p2 )
		{
			long temp;
			if( !V_ToIntegerNS( ref p1, out temp, F2Imod.F2Ieq ) )
				p2 = ref p1;
			G_RunError( "number{0} has no integer representation", VarInfo( ref p2 ) );
		}

		private void G_OrderError( ref TValue p1, ref TValue p2 )
		{
			string t1 = ObjTypeName( ref p1 );
			string t2 = ObjTypeName( ref p2 );
			if( t1 == t2 )
				G_RunError( "attempt to compare two {0} values", t1 );
			else
				G_RunError( "attempt to compare {0} with {1}", t1, t2 );
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

		internal void G_ErrNNil( LuaLClosureValue cl, int k )
		{
			string globalname = "?"; // default name if k == 0
			if( k > 0 )
				KName( cl.Proto, k - 1, out globalname );
			G_RunError( "global '{0}' already defined", globalname );
		}

		/* add src:line information to 'msg' */
		internal string G_AddInfo( string msg, string src, int line )
		{
			if( src == null ) // no debug information?
				return string.Format( "?:?: {0}", msg );
			else
				return string.Format( "{0}:{1}: {2}", O_ChunkId( src ), line, msg );
		}

		internal void G_ErrorMsg()
		{
			if( ErrFunc != 0 ) // is there an error handling function?
			{
				StkId errFunc = RestoreStack( ErrFunc );
				Utl.Assert( errFunc.V.TtIsFunction() );
				Top.V.SetObj( ref Stack[Top.Index-1].V ); // move argument
				Stack[Top.Index-1].V.SetObj( ref errFunc.V ); // push function
				StkId.inc( ref Top ); // assume EXTRA_STACK
				D_CallNoYield( Stack[Top.Index-2], 1 ); // call it
			}
			if( Stack[Top.Index-1].V.TtIsNil() ) // error object is nil?
			{
				/* change it to a proper message */
				Stack[Top.Index-1].V.SetSValue( "<no error object>" );
			}
			D_Throw( ThreadStatus.LUA_ERRRUN );
		}

		internal void G_RunError( string fmt, params object[] args )
		{
			CallInfo ci = CI;
			string msg = args.Length == 0 ? fmt : string.Format( fmt, args ); // format message
			if( ci.IsLua ) // if Lua function, add source:line information
				msg = G_AddInfo( msg, Stack[ci.FuncIndex].V.ClLValue().Proto.Source, GetCurrentLineOf( ci ) );
			O_PushString( msg );
			G_ErrorMsg();
		}

		/*
		** Check whether new instruction 'newpc' is in a different line from
		** previous instruction 'oldpc'. More often than not, 'newpc' is only
		** one or a few instructions after 'oldpc' (it must be after, see
		** caller), so try to avoid calling 'luaG_getfuncline'. If they are
		** too far apart, there is a good chance of a ABSLINEINFO in the way,
		** so it goes directly to 'luaG_getfuncline'.
		*/
		private static bool ChangedLine( LuaProto p, int oldpc, int newpc )
		{
			if( p.LineInfo.Count == 0 ) // no debug information?
				return false;
			if( newpc - oldpc < MAXIWTHABS / 2 ) // not too far apart?
			{
				int delta = 0; // line difference
				int pc = oldpc;
				for( ;; )
				{
					int lineinfo = p.LineInfo[++pc];
					if( lineinfo == ABSLINEINFO )
						break; // cannot compute delta; fall through
					delta += lineinfo;
					if( pc == newpc )
						return (delta != 0); // delta computed successfully
				}
			}
			/* either instructions are too far apart or there is an absolute line
			   info in the way; compute line difference explicitly */
			return G_GetFuncLine( p, oldpc ) != G_GetFuncLine( p, newpc );
		}

		/*
		** Traces Lua calls. If code is running the first instruction of a function,
		** and function is not vararg, and it is not coming from an yield,
		** calls 'luaD_hookcall'. (Vararg functions will call 'luaD_hookcall'
		** after adjusting its variable arguments; otherwise, they could call
		** a line/count hook before the call hook. Functions coming from
		** an yield already called 'luaD_hookcall' before yielding.)
		*/
		private bool G_TraceCall()
		{
			CallInfo ci = CI;
			LuaProto p = Stack[ci.FuncIndex].V.ClLValue().Proto;
			ci.Trap = true; // ensure hooks will be checked
			if( ci.SavedPc.Index == 0 ) // first instruction (not resuming)?
			{
				if( p.IsVarArg )
					return false; // hooks will start at VARARGPREP instruction
				else if( (ci.CallStatus & CallStatus.CIST_HOOKYIELD) == 0 ) // not yieded?
					D_HookCall( ci ); // check 'call' hook
			}
			return true; // keep 'trap' on
		}

		/*
		** Traces the execution of a Lua function. Called before the execution
		** of each opcode, when debug is on. 'L->oldpc' stores the last
		** instruction traced, to detect line changes. When entering a new
		** function, 'npci' will be zero and will test as a new line whatever
		** the value of 'oldpc'.  Some exceptional conditions may return to
		** a function without setting 'oldpc'. In that case, 'oldpc' may be
		** invalid; if so, use zero as a valid value. (A wrong but valid 'oldpc'
		** at most causes an extra call to a line hook.)
		** 'ci.SavedPc' points to the instruction about to run, before and
		** after; the hooks see it as already fetched, as Lua's do.
		*/
		private bool G_TraceExec( CallInfo ci )
		{
			byte mask = HookMask;
			LuaProto p = Stack[ci.FuncIndex].V.ClLValue().Proto;
			if( (mask & (LuaDef.LUA_MASKLINE | LuaDef.LUA_MASKCOUNT)) == 0 ) // no hooks?
			{
				ci.Trap = false; // don't need to stop again
				return false; // turn off 'trap'
			}
			ci.SavedPc.Index++; // reference is always next instruction
			bool counthook = (mask & LuaDef.LUA_MASKCOUNT) != 0 && (--HookCount == 0);
			if( counthook )
				ResetHookCount(); // reset count
			else if( (mask & LuaDef.LUA_MASKLINE) == 0 )
			{
				ci.SavedPc.Index--;
				return true; // no line hook and count != 0; nothing to be done now
			}
			if( (ci.CallStatus & CallStatus.CIST_HOOKYIELD) != 0 ) // hook yielded last time?
			{
				ci.CallStatus &= ~CallStatus.CIST_HOOKYIELD; // erase mark
				ci.SavedPc.Index--;
				return true; // do not call hook again (VM yielded, so it did not move)
			}
			if( !OpCodeInfo.IsIT( p.Code[ci.SavedPc.Index - 1] ) ) // top not being used?
				Top = Stack[ci.TopIndex]; // correct top
			if( counthook )
				D_Hook( LuaDef.LUA_HOOKCOUNT, -1, 0, 0 ); // call count hook
			if( (mask & LuaDef.LUA_MASKLINE) != 0 )
			{
				/* 'L->oldpc' may be invalid; use zero in this case */
				int oldpc = (OldPc < p.Code.Count) ? OldPc : 0;
				int npci = ci.SavedPc.Index - 1;
				if( npci <= oldpc || // call hook when jump back (loop),
					ChangedLine( p, oldpc, npci ) ) // or when enter new line
				{
					int newline = G_GetFuncLine( p, npci );
					D_Hook( LuaDef.LUA_HOOKLINE, newline, 0, 0 ); // call line hook
				}
				OldPc = npci; // 'pc' of last call to line hook
			}
			if( Status == ThreadStatus.LUA_YIELD ) // did hook yield?
			{
				if( counthook )
					HookCount = 1; // undo decrement to zero
				ci.CallStatus |= CallStatus.CIST_HOOKYIELD; // mark that it yielded
				D_Throw( ThreadStatus.LUA_YIELD );
			}
			ci.SavedPc.Index--;
			return true; // keep 'trap' on
		}

	}

}
