// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Collections.Generic;

namespace Cosmos.Executable.Lua
{
	// lcode.c of Lua 5.5: code generator
	internal static class Coder
	{
		public const int NO_JUMP = -1;

		/* limit for difference between lines in relative line info. */
		private const int LIMLINEDIFF = 0x80;

		/* maximum length of a short string (LUAI_MAXSHORTLEN) */
		private const int MAXSHORTLEN = 40;

		/* (note that expressions VJMP also have jumps.) */
		private static bool HasJumps( ExpDesc e )
		{
			return e.ExitTrue != e.ExitFalse;
		}

		/*
		** If expression is a numeric constant, fills 'v' with its value
		** and returns true. Otherwise, returns false.
		*/
		private static bool ToNumeral( ExpDesc e, out TValue v )
		{
			v = new TValue();
			if( HasJumps( e ) )
				return false; // not a numeral
			switch( e.Kind )
			{
				case ExpKind.VKINT:
					v.SetIValue( e.IntValue );
					return true;
				case ExpKind.VKFLT:
					v.SetFltValue( e.NumberValue );
					return true;
				default: return false;
			}
		}

		private static bool IsNumeral( ExpDesc e )
		{
			TValue v;
			return ToNumeral( e, out v );
		}

		/*
		** Get the constant value from a constant expression
		*/
		private static VarDesc Const2Val( FuncState fs, ExpDesc e )
		{
			Utl.Assert( e.Kind == ExpKind.VCONST );
			return fs.Dyd.ActVar[e.Info];
		}

		/*
		** If expression is a constant, fills 'v' with its value
		** and returns true. Otherwise, returns false.
		*/
		public static bool Exp2Const( FuncState fs, ExpDesc e, ref TValue v )
		{
			if( HasJumps( e ) )
				return false; // not a constant
			switch( e.Kind )
			{
				case ExpKind.VFALSE:
					v.SetBValue( false );
					return true;
				case ExpKind.VTRUE:
					v.SetBValue( true );
					return true;
				case ExpKind.VNIL:
					v.SetNilValue();
					return true;
				case ExpKind.VKSTR:
					v.SetSValue( e.StrValue );
					return true;
				case ExpKind.VCONST:
					v.SetObj( ref Const2Val( fs, e ).K );
					return true;
				default: {
					TValue n;
					if( !ToNumeral( e, out n ) )
						return false;
					v.SetObj( ref n );
					return true;
				}
			}
		}

		/*
		** Return the index of the previous instruction of the current code.
		** If there may be a jump target between the current instruction and
		** the previous one, return -1 (an invalid instruction, to avoid wrong
		** optimizations).
		*/
		private static int PreviousInstruction( FuncState fs )
		{
			if( fs.Pc > fs.LastTarget )
				return fs.Pc - 1; // previous instruction
			else
				return -1;
		}

		/*
		** Create a OP_LOADNIL instruction, but try to optimize: if the previous
		** instruction is also OP_LOADNIL and ranges are compatible, adjust
		** range of previous instruction instead of emitting a new one. (For
		** instance, 'local a; local b' will generate a single opcode.)
		*/
		public static void Nil( FuncState fs, int from, int n )
		{
			int l = from + n - 1; // last register to set nil
			int prev = PreviousInstruction( fs );
			if( prev >= 0 && fs.Proto.Code[prev].GET_OPCODE() == OpCode.OP_LOADNIL ) // previous is LOADNIL?
			{
				var previous = fs.Proto.Code[prev];
				int pfrom = previous.GETARG_A(); // get previous range
				int pl = pfrom + previous.GETARG_B();
				if( (pfrom <= from && from <= pl + 1) ||
					(from <= pfrom && pfrom <= l + 1) ) // can connect both?
				{
					if( pfrom < from ) from = pfrom; // from = min(from, pfrom)
					if( pl > l ) l = pl; // l = max(l, pl)
					previous.SETARG_A( from );
					previous.SETARG_B( l - from );
					fs.Proto.Code[prev] = previous;
					return;
				} // else go through
			}
			CodeABC( fs, OpCode.OP_LOADNIL, from, n - 1, 0 ); // else no optimization
		}

		/*
		** Gets the destination address of a jump instruction. Used to traverse
		** a list of jumps.
		*/
		private static int GetJump( FuncState fs, int pc )
		{
			int offset = fs.Proto.Code[pc].GETARG_sJ();
			if( offset == NO_JUMP ) // point to itself represents end of list
				return NO_JUMP; // end of list
			else
				return (pc + 1) + offset; // turn offset into absolute position
		}

		/*
		** Fix jump instruction at position 'pc' to jump to 'dest'.
		** (Jump addresses are relative in Lua)
		*/
		private static void FixJump( FuncState fs, int pc, int dest )
		{
			var jmp = fs.Proto.Code[pc];
			int offset = dest - (pc + 1);
			Utl.Assert( dest != NO_JUMP );
			if( !(-Instruction.OFFSET_sJ <= offset &&
				  offset <= Instruction.MAXARG_sJ - Instruction.OFFSET_sJ) )
				fs.Lexer.SyntaxError( "control structure too long" );
			Utl.Assert( jmp.GET_OPCODE() == OpCode.OP_JMP );
			jmp.SETARG_sJ( offset );
			fs.Proto.Code[pc] = jmp;
		}

		/*
		** Concatenate jump-list 'l2' into jump-list 'l1'
		*/
		public static void Concat( FuncState fs, ref int l1, int l2 )
		{
			if( l2 == NO_JUMP ) return; // nothing to concatenate?
			else if( l1 == NO_JUMP ) // no original list?
				l1 = l2; // 'l1' points to 'l2'
			else
			{
				int list = l1;
				int next;
				while( (next = GetJump( fs, list )) != NO_JUMP ) // find last element
					list = next;
				FixJump( fs, list, l2 ); // last element links to 'l2'
			}
		}

		/*
		** Create a jump instruction and return its position, so its destination
		** can be fixed later (with 'FixJump').
		*/
		public static int Jump( FuncState fs )
		{
			return CodesJ( fs, OpCode.OP_JMP, NO_JUMP, 0 );
		}

		/*
		** Code a 'return' instruction
		*/
		public static void Ret( FuncState fs, int first, int nret )
		{
			OpCode op;
			switch( nret )
			{
				case 0: op = OpCode.OP_RETURN0; break;
				case 1: op = OpCode.OP_RETURN1; break;
				default: op = OpCode.OP_RETURN; break;
			}
			Parser.CheckLimit( fs, nret + 1, Instruction.MAXARG_B, "returns" );
			CodeABC( fs, op, first, nret + 1, 0 );
		}

		/*
		** Code a "conditional jump", that is, a test or comparison opcode
		** followed by a jump. Return jump position.
		*/
		private static int CondJump( FuncState fs, OpCode op, int a, int b, int c, int k )
		{
			CodeABCk( fs, op, a, b, c, k );
			return Jump( fs );
		}

		/*
		** returns current 'pc' and marks it as a jump target (to avoid wrong
		** optimizations with consecutive instructions not in the same basic block).
		*/
		public static int GetLabel( FuncState fs )
		{
			fs.LastTarget = fs.Pc;
			return fs.Pc;
		}

		/*
		** Returns the position of the instruction "controlling" a given
		** jump (that is, its condition), or the jump itself if it is
		** unconditional.
		*/
		private static int GetJumpControl( FuncState fs, int pc )
		{
			if( pc >= 1 && OpCodeInfo.TestTMode( fs.Proto.Code[pc - 1].GET_OPCODE() ) )
				return pc - 1;
			else
				return pc;
		}

		/*
		** Patch destination register for a TESTSET instruction.
		** If instruction in position 'node' is not a TESTSET, return false
		** ("fails"). Otherwise, if 'reg' is not 'NO_REG', set it as the
		** destination register. Otherwise, change instruction to a simple
		** 'TEST' (produces no register value)
		*/
		private static bool PatchTestReg( FuncState fs, int node, int reg )
		{
			int ipc = GetJumpControl( fs, node );
			var i = fs.Proto.Code[ipc];
			if( i.GET_OPCODE() != OpCode.OP_TESTSET )
				return false; // cannot patch other instructions
			if( reg != Instruction.NO_REG && reg != i.GETARG_B() )
				i.SETARG_A( reg );
			else
			{
				/* no register to put value or register already has the value;
				   change instruction to simple test */
				i = Instruction.CreateABCk( OpCode.OP_TEST, i.GETARG_B(), 0, 0, i.GETARG_k() );
			}
			fs.Proto.Code[ipc] = i;
			return true;
		}

		/*
		** Traverse a list of tests ensuring no one produces a value
		*/
		private static void RemoveValues( FuncState fs, int list )
		{
			for( ; list != NO_JUMP; list = GetJump( fs, list ) )
				PatchTestReg( fs, list, Instruction.NO_REG );
		}

		/*
		** Traverse a list of tests, patching their destination address and
		** registers: tests producing values jump to 'vtarget' (and put their
		** values in 'reg'), other tests jump to 'dtarget'.
		*/
		private static void PatchListAux( FuncState fs, int list, int vtarget,
			int reg, int dtarget )
		{
			while( list != NO_JUMP )
			{
				int next = GetJump( fs, list );
				if( PatchTestReg( fs, list, reg ) )
					FixJump( fs, list, vtarget );
				else
					FixJump( fs, list, dtarget ); // jump to default target
				list = next;
			}
		}

		/*
		** Path all jumps in 'list' to jump to 'target'.
		** (The assert means that we cannot fix a jump to a forward address
		** because we only know addresses once code is generated.)
		*/
		public static void PatchList( FuncState fs, int list, int target )
		{
			Utl.Assert( target <= fs.Pc );
			PatchListAux( fs, list, target, Instruction.NO_REG, target );
		}

		public static void PatchToHere( FuncState fs, int list )
		{
			int hr = GetLabel( fs ); // mark "here" as a jump target
			PatchList( fs, list, hr );
		}

		public static void JumpTo( FuncState fs, int target )
		{
			PatchList( fs, Jump( fs ), target );
		}

		// stores 'v' at 'idx' of 'list', growing it by one when 'idx' is its end
		private static void SetAt<T>( List<T> list, int idx, T v )
		{
			if( idx < list.Count )
				list[idx] = v;
			else
			{
				Utl.Assert( idx == list.Count );
				list.Add( v );
			}
		}

		/*
		** Save line info for a new instruction. If difference from last line
		** does not fit in a byte, of after that many instructions, save a new
		** absolute line info; (in that case, the special value 'ABSLINEINFO'
		** in 'lineinfo' signals the existence of this absolute information.)
		** Otherwise, store the difference from last line in 'lineinfo'.
		*/
		private static void SaveLineInfo( FuncState fs, LuaProto f, int line )
		{
			int linedif = line - fs.PreviousLine;
			int pc = fs.Pc - 1; // last instruction coded
			if( System.Math.Abs( linedif ) >= LIMLINEDIFF || fs.IWthAbs++ >= LuaState.MAXIWTHABS )
			{
				var abs = new AbsLineInfo();
				abs.Pc = pc;
				abs.Line = line;
				SetAt( f.AbsLineInfo, fs.NAbsLineInfo++, abs );
				linedif = LuaState.ABSLINEINFO; // signal that there is absolute information
				fs.IWthAbs = 1; // restart counter
			}
			SetAt( f.LineInfo, pc, (sbyte)linedif );
			fs.PreviousLine = line; // last line saved
		}

		/*
		** Remove line information from the last instruction.
		** If line information for that instruction is absolute, set 'iwthabs'
		** above its max to force the new (replacing) instruction to have
		** absolute line info, too.
		*/
		private static void RemoveLastLineInfo( FuncState fs )
		{
			var f = fs.Proto;
			int pc = fs.Pc - 1; // last instruction coded
			if( f.LineInfo[pc] != LuaState.ABSLINEINFO ) // relative line info?
			{
				fs.PreviousLine -= f.LineInfo[pc]; // correct last line saved
				fs.IWthAbs--; // undo previous increment
			}
			else // absolute line information
			{
				Utl.Assert( f.AbsLineInfo[fs.NAbsLineInfo - 1].Pc == pc );
				fs.NAbsLineInfo--; // remove it
				fs.IWthAbs = LuaState.MAXIWTHABS + 1; // force next line info to be absolute
			}
		}

		/*
		** Remove the last instruction created, correcting line information
		** accordingly.
		*/
		private static void RemoveLastInstruction( FuncState fs )
		{
			RemoveLastLineInfo( fs );
			fs.Pc--;
		}

		/*
		** Emit instruction 'i', checking for array sizes and saving also its
		** line information. Return 'i' position.
		*/
		public static int Code( FuncState fs, Instruction i )
		{
			var f = fs.Proto;
			/* put new instruction in code array */
			SetAt( f.Code, fs.Pc++, i );
			SaveLineInfo( fs, f, fs.Lexer.LastLine );
			return fs.Pc - 1; // index of new instruction
		}

		/*
		** Format and emit an 'iABC' instruction. (Assertions check consistency
		** of parameters versus opcode.)
		*/
		public static int CodeABCk( FuncState fs, OpCode o, int a, int b, int c, int k )
		{
			Utl.Assert( OpCodeInfo.GetOpMode( o ) == OpMode.iABC );
			Utl.Assert( a <= Instruction.MAXARG_A && b <= Instruction.MAXARG_B &&
						c <= Instruction.MAXARG_C && (k & ~1) == 0 );
			return Code( fs, Instruction.CreateABCk( o, a, b, c, k ) );
		}

		public static int CodeABC( FuncState fs, OpCode o, int a, int b, int c )
		{
			return CodeABCk( fs, o, a, b, c, 0 );
		}

		public static int CodevABCk( FuncState fs, OpCode o, int a, int b, int c, int k )
		{
			Utl.Assert( OpCodeInfo.GetOpMode( o ) == OpMode.ivABC );
			Utl.Assert( a <= Instruction.MAXARG_A && b <= Instruction.MAXARG_vB &&
						c <= Instruction.MAXARG_vC && (k & ~1) == 0 );
			return Code( fs, Instruction.CreatevABCk( o, a, b, c, k ) );
		}

		/*
		** Format and emit an 'iABx' instruction.
		*/
		public static int CodeABx( FuncState fs, OpCode o, int a, uint bc )
		{
			Utl.Assert( OpCodeInfo.GetOpMode( o ) == OpMode.iABx );
			Utl.Assert( a <= Instruction.MAXARG_A && bc <= Instruction.MAXARG_Bx );
			return Code( fs, Instruction.CreateABx( o, a, bc ) );
		}

		/*
		** Format and emit an 'iAsBx' instruction.
		*/
		private static int CodeAsBx( FuncState fs, OpCode o, int a, int bc )
		{
			uint b = (uint)(bc + Instruction.OFFSET_sBx);
			Utl.Assert( OpCodeInfo.GetOpMode( o ) == OpMode.iAsBx );
			Utl.Assert( a <= Instruction.MAXARG_A && b <= Instruction.MAXARG_Bx );
			return Code( fs, Instruction.CreateABx( o, a, b ) );
		}

		/*
		** Format and emit an 'isJ' instruction.
		*/
		private static int CodesJ( FuncState fs, OpCode o, int sj, int k )
		{
			uint j = (uint)(sj + Instruction.OFFSET_sJ);
			Utl.Assert( OpCodeInfo.GetOpMode( o ) == OpMode.isJ );
			Utl.Assert( j <= Instruction.MAXARG_sJ && (k & ~1) == 0 );
			return Code( fs, Instruction.CreatesJ( o, j, k ) );
		}

		/*
		** Emit an "extra argument" instruction (format 'iAx')
		*/
		private static int CodeExtraArg( FuncState fs, int a )
		{
			Utl.Assert( a <= Instruction.MAXARG_Ax );
			return Code( fs, Instruction.CreateAx( OpCode.OP_EXTRAARG, a ) );
		}

		/*
		** Emit a "load constant" instruction, using either 'OP_LOADK'
		** (if constant index 'k' fits in 18 bits) or an 'OP_LOADKX'
		** instruction with "extra argument".
		*/
		private static int CodeK( FuncState fs, int reg, int k )
		{
			if( k <= Instruction.MAXARG_Bx )
				return CodeABx( fs, OpCode.OP_LOADK, reg, (uint)k );
			else
			{
				int p = CodeABx( fs, OpCode.OP_LOADKX, reg, 0 );
				CodeExtraArg( fs, k );
				return p;
			}
		}

		/*
		** Check register-stack level, keeping track of its maximum size
		** in field 'maxstacksize'
		*/
		public static void CheckStack( FuncState fs, int n )
		{
			int newstack = fs.FreeReg + n;
			if( newstack > fs.Proto.MaxStackSize )
			{
				Parser.CheckLimit( fs, newstack, Instruction.MAX_FSTACK, "registers" );
				fs.Proto.MaxStackSize = (byte)newstack;
			}
		}

		/*
		** Reserve 'n' registers in register stack
		*/
		public static void ReserveRegs( FuncState fs, int n )
		{
			CheckStack( fs, n );
			fs.FreeReg += n;
		}

		/*
		** Free register 'reg', if it is neither a constant index nor
		** a local variable.
		*/
		private static void FreeReg( FuncState fs, int reg )
		{
			if( reg >= fs.NVarStack() )
			{
				fs.FreeReg--;
				Utl.Assert( reg == fs.FreeReg );
			}
		}

		/*
		** Free two registers in proper order
		*/
		private static void FreeRegs( FuncState fs, int r1, int r2 )
		{
			if( r1 > r2 )
			{
				FreeReg( fs, r1 );
				FreeReg( fs, r2 );
			}
			else
			{
				FreeReg( fs, r2 );
				FreeReg( fs, r1 );
			}
		}

		/*
		** Free register used by expression 'e' (if any)
		*/
		private static void FreeExp( FuncState fs, ExpDesc e )
		{
			if( e.Kind == ExpKind.VNONRELOC )
				FreeReg( fs, e.Info );
		}

		/*
		** Free registers used by expressions 'e1' and 'e2' (if any) in proper
		** order.
		*/
		private static void FreeExps( FuncState fs, ExpDesc e1, ExpDesc e2 )
		{
			int r1 = (e1.Kind == ExpKind.VNONRELOC) ? e1.Info : -1;
			int r2 = (e2.Kind == ExpKind.VNONRELOC) ? e2.Info : -1;
			FreeRegs( fs, r1, r2 );
		}

		/*
		** Add constant 'v' to prototype's list of constants (field 'k').
		** Use the function's table to cache position of constants in constant
		** list and try to reuse constants. Each function has its own table (the
		** reference shares one among all functions of a chunk), and its keys
		** tell integers, floats and strings apart by their type tags.
		*/
		private static int AddK( FuncState fs, ref TValue key, ref TValue v )
		{
			int idx;
			if( fs.H.TryGetValue( key, out idx ) )
				return idx; // reuse index

			/* constant not found; create a new entry */
			idx = fs.Proto.K.Count;
			if( idx > Instruction.MAXARG_Ax )
				fs.Lexer.SyntaxError( "too many constants" );
			fs.H.Add( key, idx );

			var newItem = new StkId();
			newItem.V.SetObj( ref v );
			fs.Proto.K.Add( newItem );
			return idx;
		}

		/*
		** Add a string to list of constants and return its index.
		*/
		public static int StringK( FuncState fs, string s )
		{
			var o = new TValue();
			o.SetSValue( s );
			return AddK( fs, ref o, ref o ); // use string itself as key
		}

		/*
		** Add an integer to list of constants and return its index.
		*/
		private static int IntK( FuncState fs, long n )
		{
			var o = new TValue();
			o.SetIValue( n );
			return AddK( fs, ref o, ref o ); // use integer itself as key
		}

		/*
		** Add a float to list of constants and return its index. (The table
		** keys tell floats from integers apart by their type tags, so integral
		** floats need no alternative key here.)
		*/
		private static int NumberK( FuncState fs, double r )
		{
			var o = new TValue();
			o.SetFltValue( r );
			return AddK( fs, ref o, ref o ); // use number itself as key
		}

		/*
		** Add a false to list of constants and return its index.
		*/
		private static int BoolF( FuncState fs )
		{
			var o = new TValue();
			o.SetBValue( false );
			return AddK( fs, ref o, ref o ); // use boolean itself as key
		}

		/*
		** Add a true to list of constants and return its index.
		*/
		private static int BoolT( FuncState fs )
		{
			var o = new TValue();
			o.SetBValue( true );
			return AddK( fs, ref o, ref o ); // use boolean itself as key
		}

		/*
		** Add nil to list of constants and return its index.
		*/
		private static int NilK( FuncState fs )
		{
			var o = new TValue();
			o.SetNilValue();
			return AddK( fs, ref o, ref o );
		}

		/*
		** Check whether 'i' can be stored in an 'sC' operand. Equivalent to
		** (0 <= int2sC(i) && int2sC(i) <= MAXARG_C) but without risk of
		** overflows in the hidden addition inside 'int2sC'.
		*/
		private static bool FitsC( long i )
		{
			return unchecked((ulong)i + Instruction.OFFSET_sC) <= (ulong)Instruction.MAXARG_C;
		}

		/*
		** Check whether 'i' can be stored in an 'sBx' operand.
		*/
		private static bool FitsBx( long i )
		{
			return -Instruction.OFFSET_sBx <= i && i <= Instruction.MAXARG_Bx - Instruction.OFFSET_sBx;
		}

		public static void Int( FuncState fs, int reg, long i )
		{
			if( FitsBx( i ) )
				CodeAsBx( fs, OpCode.OP_LOADI, reg, (int)i );
			else
				CodeK( fs, reg, IntK( fs, i ) );
		}

		private static void Float( FuncState fs, int reg, double f )
		{
			long fi;
			if( LuaState.FltToInteger( f, out fi, F2Imod.F2Ieq ) && FitsBx( fi ) )
				CodeAsBx( fs, OpCode.OP_LOADF, reg, (int)fi );
			else
				CodeK( fs, reg, NumberK( fs, f ) );
		}

		/*
		** Get the value of 'var' in a register and generate an opcode to check
		** whether that register is nil. 'k' is the index of the variable name
		** in the list of constants. If its value cannot be encoded in Bx, a 0
		** will use '?' for the name.
		*/
		public static void CodeCheckGlobal( FuncState fs, ExpDesc var, int k, int line )
		{
			Exp2AnyReg( fs, var );
			FixLine( fs, line );
			k = (k >= Instruction.MAXARG_Bx) ? 0 : k + 1;
			CodeABx( fs, OpCode.OP_ERRNNIL, var.Info, (uint)k );
			FixLine( fs, line );
			FreeExp( fs, var );
		}

		/*
		** Convert a constant in 'v' into an expression description 'e'
		*/
		private static void Const2Exp( ref TValue v, ExpDesc e )
		{
			switch( v.Tt )
			{
				case TValue.LUA_TNUMINT:
					e.Kind = ExpKind.VKINT; e.IntValue = v.IValue();
					break;
				case TValue.LUA_TNUMFLT:
					e.Kind = ExpKind.VKFLT; e.NumberValue = v.FltValue;
					break;
				case (int)LuaType.LUA_TBOOLEAN:
					e.Kind = v.BValue() ? ExpKind.VTRUE : ExpKind.VFALSE;
					break;
				case (int)LuaType.LUA_TNIL:
					e.Kind = ExpKind.VNIL;
					break;
				case (int)LuaType.LUA_TSTRING:
					e.Kind = ExpKind.VKSTR; e.StrValue = v.SValue();
					break;
				default: Utl.Assert( false ); break;
			}
		}

		/*
		** Fix an expression to return the number of results 'nresults'.
		** 'e' must be a multi-ret expression (function call or vararg).
		*/
		public static void SetReturns( FuncState fs, ExpDesc e, int nresults )
		{
			var pc = fs.Proto.Code[e.Info];
			Parser.CheckLimit( fs, nresults + 1, Instruction.MAXARG_C, "multiple results" );
			if( e.Kind == ExpKind.VCALL ) // expression is an open function call?
				pc.SETARG_C( nresults + 1 );
			else
			{
				Utl.Assert( e.Kind == ExpKind.VVARARG );
				pc.SETARG_C( nresults + 1 );
				pc.SETARG_A( fs.FreeReg );
			}
			fs.Proto.Code[e.Info] = pc;
			if( e.Kind == ExpKind.VVARARG )
				ReserveRegs( fs, 1 );
		}

		public static void SetMultRet( FuncState fs, ExpDesc e )
		{
			SetReturns( fs, e, LuaDef.LUA_MULTRET );
		}

		/*
		** Convert a VKSTR to a VK
		*/
		private static int Str2K( FuncState fs, ExpDesc e )
		{
			Utl.Assert( e.Kind == ExpKind.VKSTR );
			e.Info = StringK( fs, e.StrValue );
			e.Kind = ExpKind.VK;
			return e.Info;
		}

		/*
		** Fix an expression to return one result.
		** If expression is not a multi-ret expression (function call or
		** vararg), it already returns one result, so nothing needs to be done.
		** Function calls become VNONRELOC expressions (as its result comes
		** fixed in the base register of the call), while vararg expressions
		** become VRELOC (as OP_VARARG puts its results where it wants).
		** (Calls are created returning one result, so that does not need
		** to be fixed.)
		*/
		public static void SetOneRet( FuncState fs, ExpDesc e )
		{
			if( e.Kind == ExpKind.VCALL ) // expression is an open function call?
			{
				/* already returns 1 value */
				Utl.Assert( fs.Proto.Code[e.Info].GETARG_C() == 2 );
				e.Kind = ExpKind.VNONRELOC; // result has fixed position
				e.Info = fs.Proto.Code[e.Info].GETARG_A();
			}
			else if( e.Kind == ExpKind.VVARARG )
			{
				var pc = fs.Proto.Code[e.Info];
				pc.SETARG_C( 2 );
				fs.Proto.Code[e.Info] = pc;
				e.Kind = ExpKind.VRELOC; // can relocate its simple result
			}
		}

		/*
		** Change a vararg parameter into a regular local variable
		*/
		public static void VaPar2Local( FuncState fs, ExpDesc var )
		{
			fs.Proto.NeedVaTab(); // function will need a vararg table
			/* now a vararg parameter is equivalent to a regular local variable */
			var.Kind = ExpKind.VLOCAL;
		}

		/*
		** Ensure that expression 'e' is not a variable (nor a <const>).
		** (Expression still may have jump lists.)
		*/
		public static void DischargeVars( FuncState fs, ExpDesc e )
		{
			switch( e.Kind )
			{
				case ExpKind.VCONST: {
					Const2Exp( ref Const2Val( fs, e ).K, e );
					break;
				}
				case ExpKind.VVARGVAR: {
					VaPar2Local( fs, e ); // turn it into a local variable
					goto case ExpKind.VLOCAL;
				}
				case ExpKind.VLOCAL: { // already in a register
					e.Info = e.Var.RIdx;
					e.Kind = ExpKind.VNONRELOC; // becomes a non-relocatable value
					break;
				}
				case ExpKind.VUPVAL: { // move value to some (pending) register
					e.Info = CodeABC( fs, OpCode.OP_GETUPVAL, 0, e.Info, 0 );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VINDEXUP: {
					e.Info = CodeABC( fs, OpCode.OP_GETTABUP, 0, e.Ind.T, e.Ind.Idx );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VINDEXI: {
					FreeReg( fs, e.Ind.T );
					e.Info = CodeABC( fs, OpCode.OP_GETI, 0, e.Ind.T, e.Ind.Idx );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VINDEXSTR: {
					FreeReg( fs, e.Ind.T );
					e.Info = CodeABC( fs, OpCode.OP_GETFIELD, 0, e.Ind.T, e.Ind.Idx );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VINDEXED: {
					FreeRegs( fs, e.Ind.T, e.Ind.Idx );
					e.Info = CodeABC( fs, OpCode.OP_GETTABLE, 0, e.Ind.T, e.Ind.Idx );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VVARGIND: {
					FreeRegs( fs, e.Ind.T, e.Ind.Idx );
					e.Info = CodeABC( fs, OpCode.OP_GETVARG, 0, e.Ind.T, e.Ind.Idx );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				case ExpKind.VVARARG: case ExpKind.VCALL: {
					SetOneRet( fs, e );
					break;
				}
				default: break; // there is one value available (somewhere)
			}
		}

		/*
		** Ensure expression value is in register 'reg', making 'e' a
		** non-relocatable expression.
		** (Expression still may have jump lists.)
		*/
		private static void Discharge2Reg( FuncState fs, ExpDesc e, int reg )
		{
			DischargeVars( fs, e );
			switch( e.Kind )
			{
				case ExpKind.VNIL: {
					Nil( fs, reg, 1 );
					break;
				}
				case ExpKind.VFALSE: {
					CodeABC( fs, OpCode.OP_LOADFALSE, reg, 0, 0 );
					break;
				}
				case ExpKind.VTRUE: {
					CodeABC( fs, OpCode.OP_LOADTRUE, reg, 0, 0 );
					break;
				}
				case ExpKind.VKSTR: {
					Str2K( fs, e );
					CodeK( fs, reg, e.Info );
					break;
				}
				case ExpKind.VK: {
					CodeK( fs, reg, e.Info );
					break;
				}
				case ExpKind.VKFLT: {
					Float( fs, reg, e.NumberValue );
					break;
				}
				case ExpKind.VKINT: {
					Int( fs, reg, e.IntValue );
					break;
				}
				case ExpKind.VRELOC: {
					var pc = fs.Proto.Code[e.Info];
					pc.SETARG_A( reg ); // instruction will put result in 'reg'
					fs.Proto.Code[e.Info] = pc;
					break;
				}
				case ExpKind.VNONRELOC: {
					if( reg != e.Info )
						CodeABC( fs, OpCode.OP_MOVE, reg, e.Info, 0 );
					break;
				}
				default: {
					Utl.Assert( e.Kind == ExpKind.VJMP );
					return; // nothing to do...
				}
			}
			e.Info = reg;
			e.Kind = ExpKind.VNONRELOC;
		}

		/*
		** Ensure expression value is in a register, making 'e' a
		** non-relocatable expression.
		** (Expression still may have jump lists.)
		*/
		private static void Discharge2AnyReg( FuncState fs, ExpDesc e )
		{
			if( e.Kind != ExpKind.VNONRELOC ) // no fixed register yet?
			{
				ReserveRegs( fs, 1 ); // get a register
				Discharge2Reg( fs, e, fs.FreeReg - 1 ); // put value there
			}
		}

		private static int CodeLoadBool( FuncState fs, int a, OpCode op )
		{
			GetLabel( fs ); // those instructions may be jump targets
			return CodeABC( fs, op, a, 0, 0 );
		}

		/*
		** check whether list has any jump that do not produce a value
		** or produce an inverted value
		*/
		private static bool NeedValue( FuncState fs, int list )
		{
			for( ; list != NO_JUMP; list = GetJump( fs, list ) )
			{
				var i = fs.Proto.Code[GetJumpControl( fs, list )];
				if( i.GET_OPCODE() != OpCode.OP_TESTSET ) return true;
			}
			return false; // not found
		}

		/*
		** Ensures final expression result (which includes results from its
		** jump lists) is in register 'reg'.
		** If expression has jumps, need to patch these jumps either to
		** its final position or to "load" instructions (for those tests
		** that do not produce values).
		*/
		private static void Exp2Reg( FuncState fs, ExpDesc e, int reg )
		{
			Discharge2Reg( fs, e, reg );
			if( e.Kind == ExpKind.VJMP ) // expression itself is a test?
				Concat( fs, ref e.ExitTrue, e.Info ); // put this jump in 't' list
			if( HasJumps( e ) )
			{
				int final; // position after whole expression
				int p_f = NO_JUMP; // position of an eventual LOAD false
				int p_t = NO_JUMP; // position of an eventual LOAD true
				if( NeedValue( fs, e.ExitTrue ) || NeedValue( fs, e.ExitFalse ) )
				{
					int fj = (e.Kind == ExpKind.VJMP) ? NO_JUMP : Jump( fs );
					p_f = CodeLoadBool( fs, reg, OpCode.OP_LFALSESKIP ); // skip next inst.
					p_t = CodeLoadBool( fs, reg, OpCode.OP_LOADTRUE );
					/* jump around these booleans if 'e' is not a test */
					PatchToHere( fs, fj );
				}
				final = GetLabel( fs );
				PatchListAux( fs, e.ExitFalse, final, reg, p_f );
				PatchListAux( fs, e.ExitTrue, final, reg, p_t );
			}
			e.ExitFalse = e.ExitTrue = NO_JUMP;
			e.Info = reg;
			e.Kind = ExpKind.VNONRELOC;
		}

		/*
		** Ensures final expression result is in next available register.
		*/
		public static void Exp2NextReg( FuncState fs, ExpDesc e )
		{
			DischargeVars( fs, e );
			FreeExp( fs, e );
			ReserveRegs( fs, 1 );
			Exp2Reg( fs, e, fs.FreeReg - 1 );
		}

		/*
		** Ensures final expression result is in some (any) register
		** and return that register.
		*/
		public static int Exp2AnyReg( FuncState fs, ExpDesc e )
		{
			DischargeVars( fs, e );
			if( e.Kind == ExpKind.VNONRELOC ) // expression already has a register?
			{
				if( !HasJumps( e ) ) // no jumps?
					return e.Info; // result is already in a register
				if( e.Info >= fs.NVarStack() ) // reg. is not a local?
				{
					Exp2Reg( fs, e, e.Info ); // put final result in it
					return e.Info;
				}
				/* else expression has jumps and cannot change its register
				   to hold the jump values, because it is a local variable.
				   Go through to the default case. */
			}
			Exp2NextReg( fs, e ); // default: use next available register
			return e.Info;
		}

		/*
		** Ensures final expression result is either in a register,
		** in an upvalue, or it is the vararg parameter.
		*/
		public static void Exp2AnyRegUp( FuncState fs, ExpDesc e )
		{
			if( (e.Kind != ExpKind.VUPVAL && e.Kind != ExpKind.VVARGVAR) || HasJumps( e ) )
				Exp2AnyReg( fs, e );
		}

		/*
		** Ensures final expression result is either in a register
		** or it is a constant.
		*/
		public static void Exp2Val( FuncState fs, ExpDesc e )
		{
			if( e.Kind == ExpKind.VJMP || HasJumps( e ) )
				Exp2AnyReg( fs, e );
			else
				DischargeVars( fs, e );
		}

		/*
		** Try to make 'e' a K expression with an index in the range of R/K
		** indices. Return true iff succeeded.
		*/
		public static bool Exp2K( FuncState fs, ExpDesc e )
		{
			if( !HasJumps( e ) )
			{
				int info;
				switch( e.Kind ) // move constants to 'k'
				{
					case ExpKind.VTRUE: info = BoolT( fs ); break;
					case ExpKind.VFALSE: info = BoolF( fs ); break;
					case ExpKind.VNIL: info = NilK( fs ); break;
					case ExpKind.VKINT: info = IntK( fs, e.IntValue ); break;
					case ExpKind.VKFLT: info = NumberK( fs, e.NumberValue ); break;
					case ExpKind.VKSTR: info = StringK( fs, e.StrValue ); break;
					case ExpKind.VK: info = e.Info; break;
					default: return false; // not a constant
				}
				if( info <= Instruction.MAXINDEXRK ) // does constant fit in 'argC'?
				{
					e.Kind = ExpKind.VK; // make expression a 'K' expression
					e.Info = info;
					return true;
				}
			}
			/* else, expression doesn't fit; leave it unchanged */
			return false;
		}

		/*
		** Ensures final expression result is in a valid R/K index
		** (that is, it is either in a register or in 'k' with an index
		** in the range of R/K indices).
		** Returns true iff expression is K.
		*/
		private static bool Exp2RK( FuncState fs, ExpDesc e )
		{
			if( Exp2K( fs, e ) )
				return true;
			else // not a constant in the right range: put it in a register
			{
				Exp2AnyReg( fs, e );
				return false;
			}
		}

		private static void CodeABRK( FuncState fs, OpCode o, int a, int b, ExpDesc ec )
		{
			bool k = Exp2RK( fs, ec );
			CodeABCk( fs, o, a, b, ec.Info, k ? 1 : 0 );
		}

		/*
		** Generate code to store result of expression 'ex' into variable 'var'.
		*/
		public static void StoreVar( FuncState fs, ExpDesc var, ExpDesc ex )
		{
			switch( var.Kind )
			{
				case ExpKind.VLOCAL: {
					FreeExp( fs, ex );
					Exp2Reg( fs, ex, var.Var.RIdx ); // compute 'ex' into proper place
					return;
				}
				case ExpKind.VUPVAL: {
					int e = Exp2AnyReg( fs, ex );
					CodeABC( fs, OpCode.OP_SETUPVAL, e, var.Info, 0 );
					break;
				}
				case ExpKind.VINDEXUP: {
					CodeABRK( fs, OpCode.OP_SETTABUP, var.Ind.T, var.Ind.Idx, ex );
					break;
				}
				case ExpKind.VINDEXI: {
					CodeABRK( fs, OpCode.OP_SETI, var.Ind.T, var.Ind.Idx, ex );
					break;
				}
				case ExpKind.VINDEXSTR: {
					CodeABRK( fs, OpCode.OP_SETFIELD, var.Ind.T, var.Ind.Idx, ex );
					break;
				}
				case ExpKind.VVARGIND: {
					fs.Proto.NeedVaTab(); // function will need a vararg table
					/* now, assignment is to a regular table */
					goto case ExpKind.VINDEXED;
				}
				case ExpKind.VINDEXED: {
					CodeABRK( fs, OpCode.OP_SETTABLE, var.Ind.T, var.Ind.Idx, ex );
					break;
				}
				default: Utl.Assert( false ); break; // invalid var kind to store
			}
			FreeExp( fs, ex );
		}

		/*
		** Negate condition 'e' (where 'e' is a comparison).
		*/
		private static void NegateCondition( FuncState fs, ExpDesc e )
		{
			int ipc = GetJumpControl( fs, e.Info );
			var pc = fs.Proto.Code[ipc];
			Utl.Assert( OpCodeInfo.TestTMode( pc.GET_OPCODE() ) &&
						pc.GET_OPCODE() != OpCode.OP_TESTSET &&
						pc.GET_OPCODE() != OpCode.OP_TEST );
			pc.SETARG_k( pc.GETARG_k() ^ 1 );
			fs.Proto.Code[ipc] = pc;
		}

		/*
		** Emit instruction to jump if 'e' is 'cond' (that is, if 'cond'
		** is true, code will jump if 'e' is true.) Return jump position.
		** Optimize when 'e' is 'not' something, inverting the condition
		** and removing the 'not'.
		*/
		private static int JumpOnCond( FuncState fs, ExpDesc e, int cond )
		{
			if( e.Kind == ExpKind.VRELOC )
			{
				var ie = fs.Proto.Code[e.Info];
				if( ie.GET_OPCODE() == OpCode.OP_NOT )
				{
					RemoveLastInstruction( fs ); // remove previous OP_NOT
					return CondJump( fs, OpCode.OP_TEST, ie.GETARG_B(), 0, 0, cond ^ 1 );
				}
				/* else go through */
			}
			Discharge2AnyReg( fs, e );
			FreeExp( fs, e );
			return CondJump( fs, OpCode.OP_TESTSET, Instruction.NO_REG, e.Info, 0, cond );
		}

		/*
		** Emit code to go through if 'e' is true, jump otherwise.
		*/
		public static void GoIfTrue( FuncState fs, ExpDesc e )
		{
			int pc; // pc of new jump
			DischargeVars( fs, e );
			switch( e.Kind )
			{
				case ExpKind.VJMP: { // condition?
					NegateCondition( fs, e ); // jump when it is false
					pc = e.Info; // save jump position
					break;
				}
				case ExpKind.VK: case ExpKind.VKFLT: case ExpKind.VKINT:
				case ExpKind.VKSTR: case ExpKind.VTRUE: {
					pc = NO_JUMP; // always true; do nothing
					break;
				}
				default: {
					pc = JumpOnCond( fs, e, 0 ); // jump when false
					break;
				}
			}
			Concat( fs, ref e.ExitFalse, pc ); // insert new jump in false list
			PatchToHere( fs, e.ExitTrue ); // true list jumps to here (to go through)
			e.ExitTrue = NO_JUMP;
		}

		/*
		** Emit code to go through if 'e' is false, jump otherwise.
		*/
		private static void GoIfFalse( FuncState fs, ExpDesc e )
		{
			int pc; // pc of new jump
			DischargeVars( fs, e );
			switch( e.Kind )
			{
				case ExpKind.VJMP: {
					pc = e.Info; // already jump if true
					break;
				}
				case ExpKind.VNIL: case ExpKind.VFALSE: {
					pc = NO_JUMP; // always false; do nothing
					break;
				}
				default: {
					pc = JumpOnCond( fs, e, 1 ); // jump if true
					break;
				}
			}
			Concat( fs, ref e.ExitTrue, pc ); // insert new jump in 't' list
			PatchToHere( fs, e.ExitFalse ); // false list jumps to here (to go through)
			e.ExitFalse = NO_JUMP;
		}

		/*
		** Code 'not e', doing constant folding.
		*/
		private static void CodeNot( FuncState fs, ExpDesc e )
		{
			switch( e.Kind )
			{
				case ExpKind.VNIL: case ExpKind.VFALSE: {
					e.Kind = ExpKind.VTRUE; // true == not nil == not false
					break;
				}
				case ExpKind.VK: case ExpKind.VKFLT: case ExpKind.VKINT:
				case ExpKind.VKSTR: case ExpKind.VTRUE: {
					e.Kind = ExpKind.VFALSE; // false == not "x" == not 0.5 == not 1 == not true
					break;
				}
				case ExpKind.VJMP: {
					NegateCondition( fs, e );
					break;
				}
				case ExpKind.VRELOC:
				case ExpKind.VNONRELOC: {
					Discharge2AnyReg( fs, e );
					FreeExp( fs, e );
					e.Info = CodeABC( fs, OpCode.OP_NOT, 0, e.Info, 0 );
					e.Kind = ExpKind.VRELOC;
					break;
				}
				default: Utl.Assert( false ); break; // cannot happen
			}
			/* interchange true and false lists */
			{ int temp = e.ExitFalse; e.ExitFalse = e.ExitTrue; e.ExitTrue = temp; }
			RemoveValues( fs, e.ExitFalse ); // values are useless when negated
			RemoveValues( fs, e.ExitTrue );
		}

		/*
		** Check whether expression 'e' is a short literal string
		*/
		private static bool IsKstr( FuncState fs, ExpDesc e )
		{
			return e.Kind == ExpKind.VK && !HasJumps( e ) && e.Info <= Instruction.MAXINDEXRK &&
				fs.Proto.K[e.Info].V.TtIsString() &&
				fs.Proto.K[e.Info].V.SValue().Length <= MAXSHORTLEN;
		}

		/*
		** Check whether expression 'e' is a literal integer.
		*/
		private static bool IsKint( ExpDesc e )
		{
			return e.Kind == ExpKind.VKINT && !HasJumps( e );
		}

		/*
		** Check whether expression 'e' is a literal integer in
		** proper range to fit in register C
		*/
		private static bool IsCint( ExpDesc e )
		{
			return IsKint( e ) && unchecked((ulong)e.IntValue) <= (ulong)Instruction.MAXARG_C;
		}

		/*
		** Check whether expression 'e' is a literal integer in
		** proper range to fit in register sC
		*/
		private static bool IsSCint( ExpDesc e )
		{
			return IsKint( e ) && FitsC( e.IntValue );
		}

		/*
		** Check whether expression 'e' is a literal integer or float in
		** proper range to fit in a register (sB or sC).
		*/
		private static bool IsSCnumber( ExpDesc e, out int pi, ref int isfloat )
		{
			long i;
			pi = 0;
			if( e.Kind == ExpKind.VKINT )
				i = e.IntValue;
			else if( e.Kind == ExpKind.VKFLT && LuaState.FltToInteger( e.NumberValue, out i, F2Imod.F2Ieq ) )
				isfloat = 1;
			else
				return false; // not a number
			if( !HasJumps( e ) && FitsC( i ) )
			{
				pi = Instruction.Int2sC( (int)i );
				return true;
			}
			else
				return false;
		}

		/*
		** Emit SELF instruction or equivalent: the code will convert
		** expression 'e' into 'e.key(e,'.
		*/
		public static void Self( FuncState fs, ExpDesc e, ExpDesc key )
		{
			int ereg, bas;
			Exp2AnyReg( fs, e );
			ereg = e.Info; // register where 'e' (the receiver) was placed
			FreeExp( fs, e );
			bas = e.Info = fs.FreeReg; // base register for op_self
			e.Kind = ExpKind.VNONRELOC; // self expression has a fixed register
			ReserveRegs( fs, 2 ); // method and 'self' produced by op_self
			Utl.Assert( key.Kind == ExpKind.VKSTR );
			/* is method name a short string in a valid K index? */
			if( key.StrValue.Length <= MAXSHORTLEN && Exp2K( fs, key ) )
			{
				/* can use 'self' opcode */
				CodeABCk( fs, OpCode.OP_SELF, bas, ereg, key.Info, 0 );
			}
			else // cannot use 'self' opcode; use move+gettable
			{
				Exp2AnyReg( fs, key ); // put method name in a register
				CodeABC( fs, OpCode.OP_MOVE, bas + 1, ereg, 0 ); // copy self to base+1
				CodeABC( fs, OpCode.OP_GETTABLE, bas, ereg, key.Info ); // get method
			}
			FreeExp( fs, key );
		}

		/*
		** Create expression 't[k]'. 't' must have its final result already in a
		** register or upvalue. Upvalues can only be indexed by literal strings.
		** Keys can be literal strings in the constant table or arbitrary
		** values in registers.
		*/
		public static void Indexed( FuncState fs, ExpDesc t, ExpDesc k )
		{
			int keystr = -1;
			if( k.Kind == ExpKind.VKSTR )
				keystr = Str2K( fs, k );
			Utl.Assert( !HasJumps( t ) &&
				(t.Kind == ExpKind.VLOCAL || t.Kind == ExpKind.VVARGVAR ||
				 t.Kind == ExpKind.VNONRELOC || t.Kind == ExpKind.VUPVAL) );
			if( t.Kind == ExpKind.VUPVAL && !IsKstr( fs, k ) ) // upvalue indexed by non 'Kstr'?
				Exp2AnyReg( fs, t ); // put it in a register
			if( t.Kind == ExpKind.VUPVAL )
			{
				Utl.Assert( IsKstr( fs, k ) );
				t.Ind.T = t.Info; // upvalue index
				t.Ind.Idx = k.Info; // literal short string
				t.Kind = ExpKind.VINDEXUP;
			}
			else if( t.Kind == ExpKind.VVARGVAR ) // indexing the vararg parameter?
			{
				int kreg = Exp2AnyReg( fs, k ); // put key in some register
				int vreg = t.Var.RIdx; // register with vararg param.
				Utl.Assert( vreg == fs.Proto.NumParams );
				t.Ind.T = vreg;
				t.Ind.Idx = kreg;
				t.Kind = ExpKind.VVARGIND; // 't' represents 'vararg[k]'
			}
			else
			{
				/* register index of the table */
				t.Ind.T = (t.Kind == ExpKind.VLOCAL) ? t.Var.RIdx : t.Info;
				if( IsKstr( fs, k ) )
				{
					t.Ind.Idx = k.Info; // literal short string
					t.Kind = ExpKind.VINDEXSTR;
				}
				else if( IsCint( k ) )
				{
					t.Ind.Idx = (int)k.IntValue; // int. constant in proper range
					t.Kind = ExpKind.VINDEXI;
				}
				else
				{
					t.Ind.Idx = Exp2AnyReg( fs, k ); // register
					t.Kind = ExpKind.VINDEXED;
				}
			}
			t.Ind.KeyStr = keystr; // string index in 'k'
			t.Ind.Ro = false; // by default, not read-only
		}

		/*
		** Return false if folding can raise an error.
		** Bitwise operations need operands convertible to integers; division
		** operations cannot have 0 as divisor.
		*/
		private static bool ValidOp( LuaOp op, ref TValue v1, ref TValue v2 )
		{
			switch( op )
			{
				case LuaOp.LUA_OPBAND: case LuaOp.LUA_OPBOR: case LuaOp.LUA_OPBXOR:
				case LuaOp.LUA_OPSHL: case LuaOp.LUA_OPSHR: case LuaOp.LUA_OPBNOT: { // conversion errors
					long i;
					return LuaState.V_ToIntegerNS( ref v1, out i, F2Imod.F2Ieq ) &&
						   LuaState.V_ToIntegerNS( ref v2, out i, F2Imod.F2Ieq );
				}
				case LuaOp.LUA_OPDIV: case LuaOp.LUA_OPIDIV: case LuaOp.LUA_OPMOD: // division by 0
					return v2.NValue() != 0;
				default: return true; // everything else is valid
			}
		}

		/*
		** Try to "constant-fold" an operation; return true iff successful.
		** (In this case, 'e1' has the final result.)
		*/
		private static bool ConstFolding( FuncState fs, LuaOp op, ExpDesc e1, ExpDesc e2 )
		{
			TValue v1, v2;
			var res = new TValue();
			if( !ToNumeral( e1, out v1 ) || !ToNumeral( e2, out v2 ) || !ValidOp( op, ref v1, ref v2 ) )
				return false; // non-numeric operands or not safe to fold
			LuaState.O_RawArith( fs.State, op, ref v1, ref v2, ref res ); // does operation
			if( res.TtIsInteger() )
			{
				e1.Kind = ExpKind.VKINT;
				e1.IntValue = res.IValue();
			}
			else // folds neither NaN nor 0.0 (to avoid problems with -0.0)
			{
				double n = res.FltValue;
				if( double.IsNaN( n ) || n == 0 )
					return false;
				e1.Kind = ExpKind.VKFLT;
				e1.NumberValue = n;
			}
			return true;
		}

		/*
		** Convert a BinOpr to an OpCode  (ORDER OPR - ORDER OP)
		*/
		private static OpCode BinOpr2Op( BinOpr opr, BinOpr baser, OpCode bas )
		{
			Utl.Assert( baser <= opr &&
				((baser == BinOpr.ADD && opr <= BinOpr.SHR) ||
				 (baser == BinOpr.LT && opr <= BinOpr.LE)) );
			return (OpCode)(((int)opr - (int)baser) + (int)bas);
		}

		/*
		** Convert a UnOpr to an OpCode  (ORDER OPR - ORDER OP)
		*/
		private static OpCode UnOpr2Op( UnOpr opr )
		{
			return (OpCode)(((int)opr - (int)UnOpr.MINUS) + (int)OpCode.OP_UNM);
		}

		/*
		** Convert a BinOpr to a tag method  (ORDER OPR - ORDER TM)
		*/
		private static TMS BinOpr2TM( BinOpr opr )
		{
			Utl.Assert( BinOpr.ADD <= opr && opr <= BinOpr.SHR );
			return (TMS)(((int)opr - (int)BinOpr.ADD) + (int)TMS.TM_ADD);
		}

		/*
		** Emit code for unary expressions that "produce values"
		** (everything but 'not').
		** Expression to produce final result will be encoded in 'e'.
		*/
		private static void CodeUnExpVal( FuncState fs, OpCode op, ExpDesc e, int line )
		{
			int r = Exp2AnyReg( fs, e ); // opcodes operate only on registers
			FreeExp( fs, e );
			e.Info = CodeABC( fs, op, 0, r, 0 ); // generate opcode
			e.Kind = ExpKind.VRELOC; // all those operations are relocatable
			FixLine( fs, line );
		}

		/*
		** Emit code for binary expressions that "produce values"
		** (everything but logical operators 'and'/'or' and comparison
		** operators).
		** Expression to produce final result will be encoded in 'e1'.
		*/
		private static void FinishBinExpVal( FuncState fs, ExpDesc e1, ExpDesc e2,
			OpCode op, int v2, int flip, int line, OpCode mmop, TMS ev )
		{
			int v1 = Exp2AnyReg( fs, e1 );
			int pc = CodeABCk( fs, op, 0, v1, v2, 0 );
			FreeExps( fs, e1, e2 );
			e1.Info = pc;
			e1.Kind = ExpKind.VRELOC; // all those operations are relocatable
			FixLine( fs, line );
			CodeABCk( fs, mmop, v1, v2, (int)ev, flip ); // to call metamethod
			FixLine( fs, line );
		}

		/*
		** Emit code for binary expressions that "produce values" over
		** two registers.
		*/
		private static void CodeBinExpVal( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int line )
		{
			OpCode op = BinOpr2Op( opr, BinOpr.ADD, OpCode.OP_ADD );
			int v2 = Exp2AnyReg( fs, e2 ); // make sure 'e2' is in a register
			/* 'e1' must be already in a register or it is a constant */
			Utl.Assert( (ExpKind.VNIL <= e1.Kind && e1.Kind <= ExpKind.VKSTR) ||
						e1.Kind == ExpKind.VNONRELOC || e1.Kind == ExpKind.VRELOC );
			Utl.Assert( OpCode.OP_ADD <= op && op <= OpCode.OP_SHR );
			FinishBinExpVal( fs, e1, e2, op, v2, 0, line, OpCode.OP_MMBIN, BinOpr2TM( opr ) );
		}

		/*
		** Code binary operators with immediate operands.
		*/
		private static void CodeBinI( FuncState fs, OpCode op,
			ExpDesc e1, ExpDesc e2, int flip, int line, TMS ev )
		{
			int v2 = Instruction.Int2sC( (int)e2.IntValue ); // immediate operand
			Utl.Assert( e2.Kind == ExpKind.VKINT );
			FinishBinExpVal( fs, e1, e2, op, v2, flip, line, OpCode.OP_MMBINI, ev );
		}

		/*
		** Code binary operators with K operand.
		*/
		private static void CodeBinK( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int flip, int line )
		{
			TMS ev = BinOpr2TM( opr );
			int v2 = e2.Info; // K index
			OpCode op = BinOpr2Op( opr, BinOpr.ADD, OpCode.OP_ADDK );
			FinishBinExpVal( fs, e1, e2, op, v2, flip, line, OpCode.OP_MMBINK, ev );
		}

		/* Try to code a binary operator negating its second operand.
		** For the metamethod, 2nd operand must keep its original value.
		*/
		private static bool FinishBinExpNeg( FuncState fs, ExpDesc e1, ExpDesc e2,
			OpCode op, int line, TMS ev )
		{
			if( !IsKint( e2 ) )
				return false; // not an integer constant
			else
			{
				long i2 = e2.IntValue;
				if( !(FitsC( i2 ) && FitsC( -i2 )) )
					return false; // not in the proper range
				else // operating a small integer constant
				{
					int v2 = (int)i2;
					FinishBinExpVal( fs, e1, e2, op, Instruction.Int2sC( -v2 ), 0, line, OpCode.OP_MMBINI, ev );
					/* correct metamethod argument */
					var mm = fs.Proto.Code[fs.Pc - 1];
					mm.SETARG_B( Instruction.Int2sC( v2 ) );
					fs.Proto.Code[fs.Pc - 1] = mm;
					return true; // successfully coded
				}
			}
		}

		private static void SwapExps( ExpDesc e1, ExpDesc e2 )
		{
			var temp = new ExpDesc();
			temp.CopyFrom( e1 ); e1.CopyFrom( e2 ); e2.CopyFrom( temp ); // swap 'e1' and 'e2'
		}

		/*
		** Code binary operators with no constant operand.
		*/
		private static void CodeBinNoK( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int flip, int line )
		{
			if( flip != 0 )
				SwapExps( e1, e2 ); // back to original order
			CodeBinExpVal( fs, opr, e1, e2, line ); // use standard operators
		}

		/*
		** Code arithmetic operators ('+', '-', ...). If second operand is a
		** constant in the proper range, use variant opcodes with K operands.
		*/
		private static void CodeArith( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int flip, int line )
		{
			if( IsNumeral( e2 ) && Exp2K( fs, e2 ) ) // K operand?
				CodeBinK( fs, opr, e1, e2, flip, line );
			else // 'e2' is neither an immediate nor a K operand
				CodeBinNoK( fs, opr, e1, e2, flip, line );
		}

		/*
		** Code commutative operators ('+', '*'). If first operand is a
		** numeric constant, change order of operands to try to use an
		** immediate or K operator.
		*/
		private static void CodeCommutative( FuncState fs, BinOpr op,
			ExpDesc e1, ExpDesc e2, int line )
		{
			int flip = 0;
			if( IsNumeral( e1 ) ) // is first operand a numeric constant?
			{
				SwapExps( e1, e2 ); // change order
				flip = 1;
			}
			if( op == BinOpr.ADD && IsSCint( e2 ) ) // immediate operand?
				CodeBinI( fs, OpCode.OP_ADDI, e1, e2, flip, line, TMS.TM_ADD );
			else
				CodeArith( fs, op, e1, e2, flip, line );
		}

		/*
		** Code bitwise operations; they are all commutative, so the function
		** tries to put an integer constant as the 2nd operand (a K operand).
		*/
		private static void CodeBitwise( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int line )
		{
			int flip = 0;
			if( e1.Kind == ExpKind.VKINT )
			{
				SwapExps( e1, e2 ); // 'e2' will be the constant operand
				flip = 1;
			}
			if( e2.Kind == ExpKind.VKINT && Exp2K( fs, e2 ) ) // K operand?
				CodeBinK( fs, opr, e1, e2, flip, line );
			else // no constants
				CodeBinNoK( fs, opr, e1, e2, flip, line );
		}

		/*
		** Emit code for order comparisons. When using an immediate operand,
		** 'isfloat' tells whether the original value was a float.
		*/
		private static void CodeOrder( FuncState fs, BinOpr opr, ExpDesc e1, ExpDesc e2 )
		{
			int r1, r2;
			int im;
			int isfloat = 0;
			OpCode op;
			if( IsSCnumber( e2, out im, ref isfloat ) )
			{
				/* use immediate operand */
				r1 = Exp2AnyReg( fs, e1 );
				r2 = im;
				op = BinOpr2Op( opr, BinOpr.LT, OpCode.OP_LTI );
			}
			else if( IsSCnumber( e1, out im, ref isfloat ) )
			{
				/* transform (A < B) to (B > A) and (A <= B) to (B >= A) */
				r1 = Exp2AnyReg( fs, e2 );
				r2 = im;
				op = BinOpr2Op( opr, BinOpr.LT, OpCode.OP_GTI );
			}
			else // regular case, compare two registers
			{
				r1 = Exp2AnyReg( fs, e1 );
				r2 = Exp2AnyReg( fs, e2 );
				op = BinOpr2Op( opr, BinOpr.LT, OpCode.OP_LT );
			}
			FreeExps( fs, e1, e2 );
			e1.Info = CondJump( fs, op, r1, r2, isfloat, 1 );
			e1.Kind = ExpKind.VJMP;
		}

		/*
		** Emit code for equality comparisons ('==', '~=').
		** 'e1' was already put as RK by 'Infix'.
		*/
		private static void CodeEq( FuncState fs, BinOpr opr, ExpDesc e1, ExpDesc e2 )
		{
			int r1, r2;
			int im;
			int isfloat = 0; // not needed here, but kept for symmetry
			OpCode op;
			if( e1.Kind != ExpKind.VNONRELOC )
			{
				Utl.Assert( e1.Kind == ExpKind.VK || e1.Kind == ExpKind.VKINT || e1.Kind == ExpKind.VKFLT );
				SwapExps( e1, e2 );
			}
			r1 = Exp2AnyReg( fs, e1 ); // 1st expression must be in register
			if( IsSCnumber( e2, out im, ref isfloat ) )
			{
				op = OpCode.OP_EQI;
				r2 = im; // immediate operand
			}
			else if( Exp2RK( fs, e2 ) ) // 2nd expression is constant?
			{
				op = OpCode.OP_EQK;
				r2 = e2.Info; // constant index
			}
			else
			{
				op = OpCode.OP_EQ; // will compare two registers
				r2 = Exp2AnyReg( fs, e2 );
			}
			FreeExps( fs, e1, e2 );
			e1.Info = CondJump( fs, op, r1, r2, isfloat, (opr == BinOpr.EQ) ? 1 : 0 );
			e1.Kind = ExpKind.VJMP;
		}

		/*
		** Apply prefix operation 'op' to expression 'e'.
		*/
		public static void Prefix( FuncState fs, UnOpr opr, ExpDesc e, int line )
		{
			var ef = new ExpDesc(); // fake 2nd operand
			ef.Kind = ExpKind.VKINT;
			ef.IntValue = 0;
			ef.ExitTrue = ef.ExitFalse = NO_JUMP;
			DischargeVars( fs, e );
			switch( opr )
			{
				case UnOpr.MINUS: case UnOpr.BNOT: // use 'ef' as fake 2nd operand
					if( ConstFolding( fs, (LuaOp)((int)opr + (int)LuaOp.LUA_OPUNM), e, ef ) )
						break;
					/* else */
					CodeUnExpVal( fs, UnOpr2Op( opr ), e, line );
					break;
				case UnOpr.LEN:
					CodeUnExpVal( fs, UnOpr2Op( opr ), e, line );
					break;
				case UnOpr.NOT: CodeNot( fs, e ); break;
				default: Utl.Assert( false ); break;
			}
		}

		/*
		** Process 1st operand 'v' of binary operation 'op' before reading
		** 2nd operand.
		*/
		public static void Infix( FuncState fs, BinOpr op, ExpDesc v )
		{
			DischargeVars( fs, v );
			switch( op )
			{
				case BinOpr.AND: {
					GoIfTrue( fs, v ); // go ahead only if 'v' is true
					break;
				}
				case BinOpr.OR: {
					GoIfFalse( fs, v ); // go ahead only if 'v' is false
					break;
				}
				case BinOpr.CONCAT: {
					Exp2NextReg( fs, v ); // operand must be on the stack
					break;
				}
				case BinOpr.ADD: case BinOpr.SUB:
				case BinOpr.MUL: case BinOpr.DIV: case BinOpr.IDIV:
				case BinOpr.MOD: case BinOpr.POW:
				case BinOpr.BAND: case BinOpr.BOR: case BinOpr.BXOR:
				case BinOpr.SHL: case BinOpr.SHR: {
					if( !IsNumeral( v ) )
						Exp2AnyReg( fs, v );
					/* else keep numeral, which may be folded or used as an immediate
					   operand */
					break;
				}
				case BinOpr.EQ: case BinOpr.NE: {
					if( !IsNumeral( v ) )
						Exp2RK( fs, v );
					/* else keep numeral, which may be an immediate operand */
					break;
				}
				case BinOpr.LT: case BinOpr.LE:
				case BinOpr.GT: case BinOpr.GE: {
					int dummy, dummy2 = 0;
					if( !IsSCnumber( v, out dummy, ref dummy2 ) )
						Exp2AnyReg( fs, v );
					/* else keep numeral, which may be an immediate operand */
					break;
				}
				default: Utl.Assert( false ); break;
			}
		}

		/*
		** Create code for '(e1 .. e2)'.
		** For '(e1 .. e2.1 .. e2.2)' (which is '(e1 .. (e2.1 .. e2.2))',
		** because concatenation is right associative), merge both CONCATs.
		*/
		private static void CodeConcat( FuncState fs, ExpDesc e1, ExpDesc e2, int line )
		{
			int prev = PreviousInstruction( fs );
			if( prev >= 0 && fs.Proto.Code[prev].GET_OPCODE() == OpCode.OP_CONCAT ) // is 'e2' a concatenation?
			{
				var ie2 = fs.Proto.Code[prev];
				int n = ie2.GETARG_B(); // # of elements concatenated in 'e2'
				Utl.Assert( e1.Info + 1 == ie2.GETARG_A() );
				FreeExp( fs, e2 );
				ie2.SETARG_A( e1.Info ); // correct first element ('e1')
				ie2.SETARG_B( n + 1 ); // will concatenate one more element
				fs.Proto.Code[prev] = ie2;
			}
			else // 'e2' is not a concatenation
			{
				CodeABC( fs, OpCode.OP_CONCAT, e1.Info, 2, 0 ); // new concat opcode
				FreeExp( fs, e2 );
				FixLine( fs, line );
			}
		}

		/*
		** Finalize code for binary operation, after reading 2nd operand.
		*/
		public static void Posfix( FuncState fs, BinOpr opr,
			ExpDesc e1, ExpDesc e2, int line )
		{
			DischargeVars( fs, e2 );
			if( opr <= BinOpr.SHR && ConstFolding( fs, (LuaOp)((int)opr + (int)LuaOp.LUA_OPADD), e1, e2 ) )
				return; // done by folding
			switch( opr )
			{
				case BinOpr.AND: {
					Utl.Assert( e1.ExitTrue == NO_JUMP ); // list closed by 'Infix'
					Concat( fs, ref e2.ExitFalse, e1.ExitFalse );
					e1.CopyFrom( e2 );
					break;
				}
				case BinOpr.OR: {
					Utl.Assert( e1.ExitFalse == NO_JUMP ); // list closed by 'Infix'
					Concat( fs, ref e2.ExitTrue, e1.ExitTrue );
					e1.CopyFrom( e2 );
					break;
				}
				case BinOpr.CONCAT: { // e1 .. e2
					Exp2NextReg( fs, e2 );
					CodeConcat( fs, e1, e2, line );
					break;
				}
				case BinOpr.ADD: case BinOpr.MUL: {
					CodeCommutative( fs, opr, e1, e2, line );
					break;
				}
				case BinOpr.SUB: {
					if( FinishBinExpNeg( fs, e1, e2, OpCode.OP_ADDI, line, TMS.TM_SUB ) )
						break; // coded as (r1 + -I)
					/* ELSE */
					CodeArith( fs, opr, e1, e2, 0, line );
					break;
				}
				case BinOpr.DIV: case BinOpr.IDIV: case BinOpr.MOD: case BinOpr.POW: {
					CodeArith( fs, opr, e1, e2, 0, line );
					break;
				}
				case BinOpr.BAND: case BinOpr.BOR: case BinOpr.BXOR: {
					CodeBitwise( fs, opr, e1, e2, line );
					break;
				}
				case BinOpr.SHL: {
					if( IsSCint( e1 ) )
					{
						SwapExps( e1, e2 );
						CodeBinI( fs, OpCode.OP_SHLI, e1, e2, 1, line, TMS.TM_SHL ); // I << r2
					}
					else if( FinishBinExpNeg( fs, e1, e2, OpCode.OP_SHRI, line, TMS.TM_SHL ) )
					{
						/* coded as (r1 >> -I) */
					}
					else // regular case (two registers)
						CodeBinExpVal( fs, opr, e1, e2, line );
					break;
				}
				case BinOpr.SHR: {
					if( IsSCint( e2 ) )
						CodeBinI( fs, OpCode.OP_SHRI, e1, e2, 0, line, TMS.TM_SHR ); // r1 >> I
					else // regular case (two registers)
						CodeBinExpVal( fs, opr, e1, e2, line );
					break;
				}
				case BinOpr.EQ: case BinOpr.NE: {
					CodeEq( fs, opr, e1, e2 );
					break;
				}
				case BinOpr.GT: case BinOpr.GE: {
					/* '(a > b)' <=> '(b < a)';  '(a >= b)' <=> '(b <= a)' */
					SwapExps( e1, e2 );
					opr = (BinOpr)((opr - BinOpr.GT) + BinOpr.LT);
					CodeOrder( fs, opr, e1, e2 );
					break;
				}
				case BinOpr.LT: case BinOpr.LE: {
					CodeOrder( fs, opr, e1, e2 );
					break;
				}
				default: Utl.Assert( false ); break;
			}
		}

		/*
		** Change line information associated with current position, by removing
		** previous info and adding it again with new line.
		*/
		public static void FixLine( FuncState fs, int line )
		{
			RemoveLastLineInfo( fs );
			SaveLineInfo( fs, fs.Proto, line );
		}

		public static void SetTableSize( FuncState fs, int pc, int ra, int asize, int hsize )
		{
			int extra = asize / (Instruction.MAXARG_vC + 1); // higher bits of array size
			int rc = asize % (Instruction.MAXARG_vC + 1); // lower bits of array size
			int k = (extra > 0) ? 1 : 0; // true iff needs extra argument
			hsize = (hsize != 0) ? LuaTable.CeilLog2( hsize ) + 1 : 0;
			fs.Proto.Code[pc] = Instruction.CreatevABCk( OpCode.OP_NEWTABLE, ra, hsize, rc, k );
			fs.Proto.Code[pc + 1] = Instruction.CreateAx( OpCode.OP_EXTRAARG, extra );
		}

		/*
		** Emit a SETLIST instruction.
		** 'base' is register that keeps table;
		** 'nelems' is #table plus those to be stored now;
		** 'tostore' is number of values (in registers 'base + 1',...) to add to
		** table (or LUA_MULTRET to add up to stack top).
		*/
		public static void SetList( FuncState fs, int bas, int nelems, int tostore )
		{
			Utl.Assert( tostore != 0 );
			if( tostore == LuaDef.LUA_MULTRET )
				tostore = 0;
			if( nelems <= Instruction.MAXARG_vC )
				CodevABCk( fs, OpCode.OP_SETLIST, bas, tostore, nelems, 0 );
			else
			{
				int extra = nelems / (Instruction.MAXARG_vC + 1);
				nelems %= (Instruction.MAXARG_vC + 1);
				CodevABCk( fs, OpCode.OP_SETLIST, bas, tostore, nelems, 1 );
				CodeExtraArg( fs, extra );
			}
			fs.FreeReg = bas + 1; // free registers with list values
		}

		/*
		** return the final target of a jump (skipping jumps to jumps)
		*/
		private static int FinalTarget( List<Instruction> code, int i )
		{
			int count;
			for( count = 0; count < 100; count++ ) // avoid infinite loops
			{
				var pc = code[i];
				if( pc.GET_OPCODE() != OpCode.OP_JMP )
					break;
				else
					i += pc.GETARG_sJ() + 1;
			}
			return i;
		}

		/*
		** Do a final pass over the code of a function, doing small peephole
		** optimizations and adjustments.
		*/
		public static void Finish( FuncState fs )
		{
			int i;
			var p = fs.Proto;
			if( (p.Flag & LuaProto.PF_VATAB) != 0 ) // will it use a vararg table?
				p.Flag &= unchecked((byte)~LuaProto.PF_VAHID); // then it will not use hidden args.
			for( i = 0; i < fs.Pc; i++ )
			{
				var pc = p.Code[i];
				Utl.Assert( i == 0 || OpCodeInfo.IsOT( p.Code[i - 1] ) == OpCodeInfo.IsIT( pc ) );
				switch( pc.GET_OPCODE() )
				{
					case OpCode.OP_RETURN0: case OpCode.OP_RETURN1:
					case OpCode.OP_RETURN: case OpCode.OP_TAILCALL: {
						if( pc.GET_OPCODE() == OpCode.OP_RETURN0 || pc.GET_OPCODE() == OpCode.OP_RETURN1 )
						{
							if( !(fs.NeedClose || (p.Flag & LuaProto.PF_VAHID) != 0) )
								break; // no extra work
							/* else use OP_RETURN to do the extra work */
							pc.SET_OPCODE( OpCode.OP_RETURN );
						}
						if( fs.NeedClose )
							pc.SETARG_k( 1 ); // signal that it needs to close
						if( (p.Flag & LuaProto.PF_VAHID) != 0 ) // does it use hidden arguments?
							pc.SETARG_C( p.NumParams + 1 ); // signal that
						p.Code[i] = pc;
						break;
					}
					case OpCode.OP_GETVARG: {
						if( (p.Flag & LuaProto.PF_VATAB) != 0 ) // function has a vararg table?
						{
							pc.SET_OPCODE( OpCode.OP_GETTABLE ); // must get vararg there
							p.Code[i] = pc;
						}
						break;
					}
					case OpCode.OP_VARARG: {
						if( (p.Flag & LuaProto.PF_VATAB) != 0 ) // function has a vararg table?
						{
							pc.SETARG_k( 1 ); // must get vararg there
							p.Code[i] = pc;
						}
						break;
					}
					case OpCode.OP_JMP: { // to optimize jumps to jumps
						int target = FinalTarget( p.Code, i );
						FixJump( fs, i, target ); // jump directly to final target
						break;
					}
					default: break;
				}
			}
		}
	}

}
