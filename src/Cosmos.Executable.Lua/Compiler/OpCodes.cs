// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	// lopcodes.h of Lua 5.4: R[x] is a register, K[x] a constant,
	// RK(x) is K[x] if k(i) else R[x]
	internal enum OpCode
	{
		/*----------------------------------------------------------------------
		name		args	description
		------------------------------------------------------------------------*/
		OP_MOVE,/*	A B	R[A] := R[B]					*/
		OP_LOADI,/*	A sBx	R[A] := sBx					*/
		OP_LOADF,/*	A sBx	R[A] := (lua_Number)sBx				*/
		OP_LOADK,/*	A Bx	R[A] := K[Bx]					*/
		OP_LOADKX,/*	A	R[A] := K[extra arg]				*/
		OP_LOADFALSE,/*	A	R[A] := false					*/
		OP_LFALSESKIP,/*A	R[A] := false; pc++	(*)			*/
		OP_LOADTRUE,/*	A	R[A] := true					*/
		OP_LOADNIL,/*	A B	R[A], R[A+1], ..., R[A+B] := nil		*/
		OP_GETUPVAL,/*	A B	R[A] := UpValue[B]				*/
		OP_SETUPVAL,/*	A B	UpValue[B] := R[A]				*/

		OP_GETTABUP,/*	A B C	R[A] := UpValue[B][K[C]:shortstring]		*/
		OP_GETTABLE,/*	A B C	R[A] := R[B][R[C]]				*/
		OP_GETI,/*	A B C	R[A] := R[B][C]					*/
		OP_GETFIELD,/*	A B C	R[A] := R[B][K[C]:shortstring]			*/

		OP_SETTABUP,/*	A B C	UpValue[A][K[B]:shortstring] := RK(C)		*/
		OP_SETTABLE,/*	A B C	R[A][R[B]] := RK(C)				*/
		OP_SETI,/*	A B C	R[A][B] := RK(C)				*/
		OP_SETFIELD,/*	A B C	R[A][K[B]:shortstring] := RK(C)			*/

		OP_NEWTABLE,/*	A B C k	R[A] := {}					*/

		OP_SELF,/*	A B C	R[A+1] := R[B]; R[A] := R[B][RK(C):string]	*/

		OP_ADDI,/*	A B sC	R[A] := R[B] + sC				*/

		OP_ADDK,/*	A B C	R[A] := R[B] + K[C]:number			*/
		OP_SUBK,/*	A B C	R[A] := R[B] - K[C]:number			*/
		OP_MULK,/*	A B C	R[A] := R[B] * K[C]:number			*/
		OP_MODK,/*	A B C	R[A] := R[B] % K[C]:number			*/
		OP_POWK,/*	A B C	R[A] := R[B] ^ K[C]:number			*/
		OP_DIVK,/*	A B C	R[A] := R[B] / K[C]:number			*/
		OP_IDIVK,/*	A B C	R[A] := R[B] // K[C]:number			*/

		OP_BANDK,/*	A B C	R[A] := R[B] & K[C]:integer			*/
		OP_BORK,/*	A B C	R[A] := R[B] | K[C]:integer			*/
		OP_BXORK,/*	A B C	R[A] := R[B] ~ K[C]:integer			*/

		OP_SHRI,/*	A B sC	R[A] := R[B] >> sC				*/
		OP_SHLI,/*	A B sC	R[A] := sC << R[B]				*/

		OP_ADD,/*	A B C	R[A] := R[B] + R[C]				*/
		OP_SUB,/*	A B C	R[A] := R[B] - R[C]				*/
		OP_MUL,/*	A B C	R[A] := R[B] * R[C]				*/
		OP_MOD,/*	A B C	R[A] := R[B] % R[C]				*/
		OP_POW,/*	A B C	R[A] := R[B] ^ R[C]				*/
		OP_DIV,/*	A B C	R[A] := R[B] / R[C]				*/
		OP_IDIV,/*	A B C	R[A] := R[B] // R[C]				*/

		OP_BAND,/*	A B C	R[A] := R[B] & R[C]				*/
		OP_BOR,/*	A B C	R[A] := R[B] | R[C]				*/
		OP_BXOR,/*	A B C	R[A] := R[B] ~ R[C]				*/
		OP_SHL,/*	A B C	R[A] := R[B] << R[C]				*/
		OP_SHR,/*	A B C	R[A] := R[B] >> R[C]				*/

		OP_MMBIN,/*	A B C	call C metamethod over R[A] and R[B]	(*)	*/
		OP_MMBINI,/*	A sB C k	call C metamethod over R[A] and sB	*/
		OP_MMBINK,/*	A B C k		call C metamethod over R[A] and K[B]	*/

		OP_UNM,/*	A B	R[A] := -R[B]					*/
		OP_BNOT,/*	A B	R[A] := ~R[B]					*/
		OP_NOT,/*	A B	R[A] := not R[B]				*/
		OP_LEN,/*	A B	R[A] := #R[B] (length operator)			*/

		OP_CONCAT,/*	A B	R[A] := R[A].. ... ..R[A + B - 1]		*/

		OP_CLOSE,/*	A	close all upvalues >= R[A]			*/
		OP_TBC,/*	A	mark variable A "to be closed"			*/
		OP_JMP,/*	sJ	pc += sJ					*/
		OP_EQ,/*	A B k	if ((R[A] == R[B]) ~= k) then pc++		*/
		OP_LT,/*	A B k	if ((R[A] <  R[B]) ~= k) then pc++		*/
		OP_LE,/*	A B k	if ((R[A] <= R[B]) ~= k) then pc++		*/

		OP_EQK,/*	A B k	if ((R[A] == K[B]) ~= k) then pc++		*/
		OP_EQI,/*	A sB k	if ((R[A] == sB) ~= k) then pc++		*/
		OP_LTI,/*	A sB k	if ((R[A] < sB) ~= k) then pc++			*/
		OP_LEI,/*	A sB k	if ((R[A] <= sB) ~= k) then pc++		*/
		OP_GTI,/*	A sB k	if ((R[A] > sB) ~= k) then pc++			*/
		OP_GEI,/*	A sB k	if ((R[A] >= sB) ~= k) then pc++		*/

		OP_TEST,/*	A k	if (not R[A] == k) then pc++			*/
		OP_TESTSET,/*	A B k	if (not R[B] == k) then pc++ else R[A] := R[B] (*) */

		OP_CALL,/*	A B C	R[A], ... ,R[A+C-2] := R[A](R[A+1], ... ,R[A+B-1]) */
		OP_TAILCALL,/*	A B C k	return R[A](R[A+1], ... ,R[A+B-1])		*/

		OP_RETURN,/*	A B C k	return R[A], ... ,R[A+B-2]	(see note)	*/
		OP_RETURN0,/*		return						*/
		OP_RETURN1,/*	A	return R[A]					*/

		OP_FORLOOP,/*	A Bx	update counters; if loop continues then pc-=Bx; */
		OP_FORPREP,/*	A Bx	<check values and prepare counters>;
		                        if not to run then pc+=Bx+1;			*/

		OP_TFORPREP,/*	A Bx	create upvalue for R[A + 3]; pc+=Bx		*/
		OP_TFORCALL,/*	A C	R[A+4], ... ,R[A+3+C] := R[A](R[A+1], R[A+2]);	*/
		OP_TFORLOOP,/*	A Bx	if R[A+2] ~= nil then { R[A]=R[A+2]; pc -= Bx }	*/

		OP_SETLIST,/*	A B C k	R[A][C+i] := R[A+i], 1 <= i <= B		*/

		OP_CLOSURE,/*	A Bx	R[A] := closure(KPROTO[Bx])			*/

		OP_VARARG,/*	A C	R[A], R[A+1], ..., R[A+C-2] = vararg		*/

		OP_VARARGPREP,/*A	(adjust vararg parameters)			*/

		OP_EXTRAARG/*	Ax	extra (larger) argument for previous opcode	*/
	}

	/*===========================================================================
	  Notes:

	  (*) Opcode OP_LFALSESKIP is used to convert a condition to a boolean
	  value, in a code equivalent to (not cond ? false : true).  (It
	  produces false and skips the next instruction producing true.)

	  (*) Opcodes OP_MMBIN and variants follow each arithmetic and
	  bitwise opcode. If the operation succeeds, it skips this next
	  opcode. Otherwise, this opcode calls the corresponding metamethod.

	  (*) Opcode OP_TESTSET is used in short-circuit expressions that need
	  both to jump and to produce a value, such as (a = b or c).

	  (*) In OP_CALL, if (B == 0) then B = top - A. If (C == 0), then
	  'top' is set to last_result+1, so next open instruction (OP_CALL,
	  OP_RETURN*, OP_SETLIST) may use 'top'.

	  (*) In OP_VARARG, if (C == 0) then use actual number of varargs and
	  set top (like in OP_CALL with C == 0).

	  (*) In OP_RETURN, if (B == 0) then return up to 'top'.

	  (*) In OP_LOADKX and OP_NEWTABLE, the next instruction is always
	  OP_EXTRAARG.

	  (*) In OP_SETLIST, if (B == 0) then real B = 'top'; if k, then
	  real C = EXTRAARG _ C (the bits of EXTRAARG concatenated with the
	  bits of C).

	  (*) In OP_NEWTABLE, B is log2 of the hash size (which is always a
	  power of 2) plus 1, or zero for size zero. If not k, the array size
	  is C. Otherwise, the array size is EXTRAARG _ C.

	  (*) For comparisons, k specifies what condition the test should accept
	  (true or false).

	  (*) In OP_MMBINI/OP_MMBINK, k means the arguments were flipped
	   (the constant is the first operand).

	  (*) All 'skips' (pc++) assume that next instruction is a jump.

	  (*) In instructions OP_RETURN/OP_TAILCALL, 'k' specifies that the
	  function builds upvalues, which may need to be closed. C > 0 means
	  the function is vararg, so that its 'func' must be corrected before
	  returning; in this case, (C - 1) is its number of fixed parameters.

	  (*) In comparisons with an immediate operand, C signals whether the
	  original operand was a float. (It must be corrected in case of
	  metamethods.)

	===========================================================================*/

	/// <summary>
	/// basic instruction formats
	/// </summary>
	internal enum OpMode
	{
		iABC,
		iABx,
		iAsBx,
		iAx,
		isJ,
	}

	internal static class OpCodeInfo
	{
		/*
		** masks for instruction properties. The format is:
		** bits 0-2: op mode
		** bit 3: instruction set register A
		** bit 4: operator is a test (next instruction must be a jump)
		** bit 5: instruction uses 'L->top' set by previous instruction (when B == 0)
		** bit 6: instruction sets 'L->top' for next instruction (when C == 0)
		** bit 7: instruction is an MM instruction (call a metamethod)
		*/
		private static readonly byte[] OpModes =
		{
		/*	 MM OT IT T  A  mode		   opcode  */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_MOVE */
			M(0, 0, 0, 0, 1, OpMode.iAsBx),		/* OP_LOADI */
			M(0, 0, 0, 0, 1, OpMode.iAsBx),		/* OP_LOADF */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_LOADK */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_LOADKX */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_LOADFALSE */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_LFALSESKIP */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_LOADTRUE */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_LOADNIL */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_GETUPVAL */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_SETUPVAL */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_GETTABUP */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_GETTABLE */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_GETI */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_GETFIELD */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_SETTABUP */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_SETTABLE */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_SETI */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_SETFIELD */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_NEWTABLE */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SELF */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_ADDI */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_ADDK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SUBK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_MULK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_MODK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_POWK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_DIVK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_IDIVK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BANDK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BORK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BXORK */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SHRI */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SHLI */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_ADD */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SUB */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_MUL */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_MOD */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_POW */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_DIV */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_IDIV */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BAND */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BOR */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BXOR */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SHL */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_SHR */
			M(1, 0, 0, 0, 0, OpMode.iABC),		/* OP_MMBIN */
			M(1, 0, 0, 0, 0, OpMode.iABC),		/* OP_MMBINI*/
			M(1, 0, 0, 0, 0, OpMode.iABC),		/* OP_MMBINK*/
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_UNM */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_BNOT */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_NOT */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_LEN */
			M(0, 0, 0, 0, 1, OpMode.iABC),		/* OP_CONCAT */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_CLOSE */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_TBC */
			M(0, 0, 0, 0, 0, OpMode.isJ),		/* OP_JMP */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_EQ */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_LT */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_LE */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_EQK */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_EQI */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_LTI */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_LEI */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_GTI */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_GEI */
			M(0, 0, 0, 1, 0, OpMode.iABC),		/* OP_TEST */
			M(0, 0, 0, 1, 1, OpMode.iABC),		/* OP_TESTSET */
			M(0, 1, 1, 0, 1, OpMode.iABC),		/* OP_CALL */
			M(0, 1, 1, 0, 1, OpMode.iABC),		/* OP_TAILCALL */
			M(0, 0, 1, 0, 0, OpMode.iABC),		/* OP_RETURN */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_RETURN0 */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_RETURN1 */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_FORLOOP */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_FORPREP */
			M(0, 0, 0, 0, 0, OpMode.iABx),		/* OP_TFORPREP */
			M(0, 0, 0, 0, 0, OpMode.iABC),		/* OP_TFORCALL */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_TFORLOOP */
			M(0, 0, 1, 0, 0, OpMode.iABC),		/* OP_SETLIST */
			M(0, 0, 0, 0, 1, OpMode.iABx),		/* OP_CLOSURE */
			M(0, 1, 0, 0, 1, OpMode.iABC),		/* OP_VARARG */
			M(0, 0, 1, 0, 1, OpMode.iABC),		/* OP_VARARGPREP */
			M(0, 0, 0, 0, 0, OpMode.iAx),		/* OP_EXTRAARG */
		};

		public const int NUM_OPCODES = (int)OpCode.OP_EXTRAARG + 1;

		private static byte M( int mm, int ot, int it, int t, int a, OpMode m )
		{
			return (byte)((mm << 7) | (ot << 6) | (it << 5) | (t << 4) | (a << 3) | (int)m);
		}

		public static OpMode GetOpMode( OpCode m ) { return (OpMode)(OpModes[(int)m] & 7); }
		public static bool TestAMode( OpCode m ) { return (OpModes[(int)m] & (1 << 3)) != 0; }
		public static bool TestTMode( OpCode m ) { return (OpModes[(int)m] & (1 << 4)) != 0; }
		public static bool TestITMode( OpCode m ) { return (OpModes[(int)m] & (1 << 5)) != 0; }
		public static bool TestOTMode( OpCode m ) { return (OpModes[(int)m] & (1 << 6)) != 0; }
		public static bool TestMMMode( OpCode m ) { return (OpModes[(int)m] & (1 << 7)) != 0; }

		/* "out top" (set top for next instruction) */
		public static bool IsOT( Instruction i )
		{
			return (TestOTMode( i.GET_OPCODE() ) && i.GETARG_C() == 0) ||
				i.GET_OPCODE() == OpCode.OP_TAILCALL;
		}

		/* "in top" (uses top from previous instruction) */
		public static bool IsIT( Instruction i )
		{
			return TestITMode( i.GET_OPCODE() ) && i.GETARG_B() == 0;
		}
	}

	/*===========================================================================
	  We assume that instructions are unsigned 32-bit integers.
	  All instructions have an opcode in the first 7 bits.
	  Instructions can have the following formats:

	        3 3 2 2 2 2 2 2 2 2 2 2 1 1 1 1 1 1 1 1 1 1 0 0 0 0 0 0 0 0 0 0
	        1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0 9 8 7 6 5 4 3 2 1 0
	iABC          C(8)     |      B(8)     |k|     A(8)      |   Op(7)     |
	iABx                Bx(17)               |     A(8)      |   Op(7)     |
	iAsBx              sBx (signed)(17)      |     A(8)      |   Op(7)     |
	iAx                           Ax(25)                     |   Op(7)     |
	isJ                           sJ (signed)(25)            |   Op(7)     |

	  A signed argument is represented in excess K: the represented value is
	  the written unsigned value minus K, where K is half the maximum for the
	  corresponding unsigned argument.
	===========================================================================*/
	internal struct Instruction
	{
		public uint Value;

		public Instruction( uint val )
		{
			Value = val;
		}

		public static explicit operator Instruction( uint val )
		{
			return new Instruction(val);
		}

		public static explicit operator uint( Instruction i )
		{
			return i.Value;
		}

		public override string ToString()
		{
			var op = GET_OPCODE();
			switch( OpCodeInfo.GetOpMode( op ) )
			{
				case OpMode.iABC:
					return string.Format( "{0,-12} {1} {2} {3}{4}", op,
						GETARG_A(), GETARG_B(), GETARG_C(), GETARG_k() != 0 ? "k" : "" );
				case OpMode.iABx:
					return string.Format( "{0,-12} {1} {2}", op, GETARG_A(), GETARG_Bx() );
				case OpMode.iAsBx:
					return string.Format( "{0,-12} {1} {2}", op, GETARG_A(), GETARG_sBx() );
				case OpMode.iAx:
					return string.Format( "{0,-12} {1}", op, GETARG_Ax() );
				default:
					return string.Format( "{0,-12} {1}", op, GETARG_sJ() );
			}
		}

		/*
		** size and position of opcode arguments.
		*/
		public const int SIZE_C		= 8;
		public const int SIZE_B		= 8;
		public const int SIZE_Bx	= (SIZE_C + SIZE_B + 1);
		public const int SIZE_A		= 8;
		public const int SIZE_Ax	= (SIZE_Bx + SIZE_A);
		public const int SIZE_sJ	= (SIZE_Bx + SIZE_A);

		public const int SIZE_OP	= 7;

		public const int POS_OP		= 0;

		public const int POS_A		= (POS_OP + SIZE_OP);
		public const int POS_k		= (POS_A + SIZE_A);
		public const int POS_B		= (POS_k + 1);
		public const int POS_C		= (POS_B + SIZE_B);

		public const int POS_Bx		= POS_k;

		public const int POS_Ax		= POS_A;

		public const int POS_sJ		= POS_A;

		/*
		** limits for opcode arguments.
		*/
		public const int MAXARG_Bx	= ((1<<SIZE_Bx)-1);
		public const int OFFSET_sBx	= (MAXARG_Bx>>1);	/* 'sBx' is signed */

		public const int MAXARG_Ax	= ((1<<SIZE_Ax)-1);

		public const int MAXARG_sJ	= ((1 << SIZE_sJ) - 1);
		public const int OFFSET_sJ	= (MAXARG_sJ >> 1);

		public const int MAXARG_A	= ((1<<SIZE_A)-1);
		public const int MAXARG_B	= ((1<<SIZE_B)-1);
		public const int MAXARG_C	= ((1<<SIZE_C)-1);
		public const int OFFSET_sC	= (MAXARG_C >> 1);

		public static int Int2sC( int i ) { return i + OFFSET_sC; }
		public static int SC2Int( int i ) { return i - OFFSET_sC; }

		public const int MAXINDEXRK	= MAXARG_B;

		/*
		** invalid register that fits in 8 bits
		*/
		public const int NO_REG		= MAXARG_A;

		/* creates a mask with 'n' 1 bits at position 'p' */
		public static uint MASK1( int n, int p )
		{
			return ((~((~((uint)0)) << n)) << p);
		}

		/* creates a mask with 'n' 0 bits at position 'p' */
		public static uint MASK0( int n, int p )
		{
			return (~MASK1(n, p));
		}

		public OpCode GET_OPCODE()
		{
			return (OpCode)( (Value >> POS_OP) & MASK1(SIZE_OP, 0) );
		}

		public Instruction SET_OPCODE( OpCode op )
		{
			Value = (Value & MASK0(SIZE_OP, POS_OP)) |
				((((uint)op) << POS_OP) & MASK1(SIZE_OP, POS_OP));
			return this;
		}

		public int GETARG( int pos, int size )
		{
			return (int)( (Value >> pos) & MASK1(size, 0) );
		}

		public Instruction SETARG( int value, int pos, int size )
		{
			Value = ((Value & MASK0(size, pos)) |
				(((uint)value << pos) & MASK1(size, pos)));
			return this;
		}

		public int GETARG_A() { return GETARG( POS_A, SIZE_A ); }
		public Instruction SETARG_A( int v ) { return SETARG( v, POS_A, SIZE_A ); }

		public int GETARG_B() { return GETARG( POS_B, SIZE_B ); }
		public int GETARG_sB() { return SC2Int( GETARG_B() ); }
		public Instruction SETARG_B( int v ) { return SETARG( v, POS_B, SIZE_B ); }

		public int GETARG_C() { return GETARG( POS_C, SIZE_C ); }
		public int GETARG_sC() { return SC2Int( GETARG_C() ); }
		public Instruction SETARG_C( int v ) { return SETARG( v, POS_C, SIZE_C ); }

		public bool TESTARG_k() { return (Value & (1u << POS_k)) != 0; }
		public int GETARG_k() { return GETARG( POS_k, 1 ); }
		public Instruction SETARG_k( int v ) { return SETARG( v, POS_k, 1 ); }

		public int GETARG_Bx() { return GETARG( POS_Bx, SIZE_Bx ); }
		public Instruction SETARG_Bx( int v ) { return SETARG( v, POS_Bx, SIZE_Bx ); }

		public int GETARG_Ax() { return GETARG( POS_Ax, SIZE_Ax ); }
		public Instruction SETARG_Ax( int v ) { return SETARG( v, POS_Ax, SIZE_Ax ); }

		public int GETARG_sBx() { return GETARG( POS_Bx, SIZE_Bx ) - OFFSET_sBx; }
		public Instruction SETARG_sBx( int b ) { return SETARG_Bx( b + OFFSET_sBx ); }

		public int GETARG_sJ() { return GETARG( POS_sJ, SIZE_sJ ) - OFFSET_sJ; }
		public Instruction SETARG_sJ( int j ) { return SETARG( j + OFFSET_sJ, POS_sJ, SIZE_sJ ); }

		public static Instruction CreateABCk( OpCode o, int a, int b, int c, int k )
		{
			return (Instruction)( (((uint)o) << POS_OP)
				| ((uint)a << POS_A)
				| ((uint)b << POS_B)
				| ((uint)c << POS_C)
				| ((uint)k << POS_k));
		}

		public static Instruction CreateABx( OpCode o, int a, uint bc )
		{
			return (Instruction)( (((uint)o) << POS_OP)
				| ((uint)a << POS_A)
				| (bc << POS_Bx));
		}

		public static Instruction CreateAx( OpCode o, int a )
		{
			return (Instruction)( (((uint)o) << POS_OP)
				| ((uint)a << POS_Ax));
		}

		public static Instruction CreatesJ( OpCode o, uint j, int k )
		{
			return (Instruction)( (((uint)o) << POS_OP)
				| (j << POS_sJ)
				| ((uint)k << POS_k));
		}
	}

}
