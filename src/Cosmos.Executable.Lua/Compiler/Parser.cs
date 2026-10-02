// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


using System.Collections.Generic;


namespace Cosmos.Executable.Lua
{
	// lparser.h/lparser.c of Lua 5.4: the parser

	/* kinds of variables/expressions */
	internal enum ExpKind
	{
		VVOID,	/* when 'expdesc' describes the last expression of a list,
				   this kind means an empty list (so, no expression) */
		VNIL,	/* constant nil */
		VTRUE,	/* constant true */
		VFALSE,	/* constant false */
		VK,		/* constant in 'k'; info = index of constant in 'k' */
		VKFLT,	/* floating constant; nval = numerical float value */
		VKINT,	/* integer constant; ival = numerical integer value */
		VKSTR,	/* string constant; strval = string value;
				   (string is fixed by the lexer) */
		VNONRELOC,	/* expression has its value in a fixed register;
					   info = result register */
		VLOCAL,	/* local variable; var.ridx = register index;
				   var.vidx = relative index in 'actvar.arr'  */
		VUPVAL,	/* upvalue variable; info = index of upvalue in 'upvalues' */
		VCONST,	/* compile-time <const> variable;
				   info = absolute index in 'actvar.arr'  */
		VINDEXED,	/* indexed variable;
					   ind.t = table register;
					   ind.idx = key's R index */
		VINDEXUP,	/* indexed upvalue;
					   ind.t = table upvalue;
					   ind.idx = key's K index */
		VINDEXI,	/* indexed variable with constant integer;
					   ind.t = table register;
					   ind.idx = key's value */
		VINDEXSTR,	/* indexed variable with literal string;
					   ind.t = table register;
					   ind.idx = key's K index */
		VJMP,	/* expression is a test/comparison;
				   info = pc of corresponding jump instruction */
		VRELOC,	/* expression can put result in any register;
				   info = instruction pc */
		VCALL,	/* expression is a function call; info = instruction pc */
		VVARARG	/* vararg expression; info = instruction pc */
	}

	internal static class ExpKindUtl
	{
		public static bool VKIsVar( ExpKind k )
		{
			return ExpKind.VLOCAL <= k && k <= ExpKind.VINDEXSTR;
		}

		public static bool VKIsIndexed( ExpKind k )
		{
			return ExpKind.VINDEXED <= k && k <= ExpKind.VINDEXSTR;
		}
	}

	// ORDER OPR: the arithmetic and bitwise operators in the order of
	// their opcodes and of LuaOp
	internal enum BinOpr
	{
		/* arithmetic operators */
		ADD, SUB, MUL, MOD, POW,
		DIV, IDIV,
		/* bitwise operators */
		BAND, BOR, BXOR,
		SHL, SHR,
		/* string operator */
		CONCAT,
		/* comparison operators */
		EQ, LT, LE,
		NE, GT, GE,
		/* logical operators */
		AND, OR,
		NOBINOPR,
	}

	internal enum UnOpr
	{
		MINUS,
		BNOT,
		NOT,
		LEN,
		NOUNOPR,
	}

	internal class ExpDesc
	{
		public ExpKind Kind;

		public int Info;	/* for generic use */

		internal struct IndData	/* for indexed variables */
		{
			public int T;	/* table (register or upvalue) */
			public int Idx;	/* index (R or "long" K) */
		}
		public IndData Ind;

		internal struct VarData	/* for local variables */
		{
			public int RIdx;	/* register holding the variable */
			public int VIdx;	/* compiler index (in 'actvar.arr')  */
		}
		public VarData Var;

		public double NumberValue;	/* for VKFLT */
		public long IntValue;		/* for VKINT */
		public string StrValue;		/* for VKSTR */

		public int ExitTrue;	/* patch list of 'exit when true' */
		public int ExitFalse;	/* patch list of 'exit when false' */

		public void CopyFrom( ExpDesc e )
		{
			this.Kind			= e.Kind;
			this.Info			= e.Info;
			this.Ind			= e.Ind;
			this.Var			= e.Var;
			this.NumberValue	= e.NumberValue;
			this.IntValue		= e.IntValue;
			this.StrValue		= e.StrValue;
			this.ExitTrue		= e.ExitTrue;
			this.ExitFalse		= e.ExitFalse;
		}
	}

	/* description of an active local variable */
	internal class VarDesc
	{
		/* kinds of variables */
		public const byte VDKREG		= 0;	/* regular */
		public const byte RDKCONST		= 1;	/* constant */
		public const byte RDKTOCLOSE	= 2;	/* to-be-closed */
		public const byte RDKCTC		= 3;	/* compile-time constant */

		public TValue K;	/* constant value (if it is a compile-time constant) */
		public byte Kind;
		public int RIdx;	/* register holding the variable */
		public int PIdx;	/* index of the variable in the Proto's 'locvars' array */
		public string Name;	/* variable name */
	}

	/* description of pending goto statements and label statements */
	internal class LabelDesc
	{
		public string	Name;		/* label identifier */
		public int		Pc;			/* position in code */
		public int		Line;		/* line where it appeared */
		public int		NActVar;	/* number of active variables in that position */
		public bool		Close;		/* goto that escapes upvalues */
	}

	/* dynamic structures used by the parser */
	internal class Dyndata
	{
		/* list of all active local variables: as in C, entries past 'NActVar'
		   stay readable until they are overwritten */
		public List<VarDesc>	ActVar = new List<VarDesc>();
		public int				NActVar;
		public List<LabelDesc>	Gt = new List<LabelDesc>();		/* list of pending gotos */
		public List<LabelDesc>	Label = new List<LabelDesc>();	/* list of active labels */
	}

	/* control of blocks */
	internal class BlockCnt
	{
		public BlockCnt	Previous;	/* chain */
		public int		FirstLabel;	/* index of first label in this block */
		public int		FirstGoto;	/* index of first pending goto in this block */
		public int		NActVar;	/* # active locals outside the block */
		public bool		Upval;		/* true if some variable in the block is an upvalue */
		public bool		IsLoop;		/* true if 'block' is a loop */
		public bool		InsideTbc;	/* true if inside the scope of a to-be-closed var. */
	}

	/* state needed to generate code for a given function */
	internal class FuncState
	{
		public LuaProto		Proto;		/* current function header */
		public FuncState	Prev;		/* enclosing function */
		public LLex			Lexer;		/* lexical state */
		public LuaState		State;
		public Dyndata		Dyd;
		public BlockCnt		Block;		/* chain of current blocks */

		/* cache of the constants of this function */
		public Dictionary<TValue, int> H = new Dictionary<TValue, int>();

		public int Pc;				/* next position to code (equivalent to 'ncode') */
		public int LastTarget;		/* 'label' of last 'jump label' */
		public int PreviousLine;	/* last line that was saved in 'lineinfo' */
		public int NAbsLineInfo;	/* number of elements in 'abslineinfo' */
		public int FirstLocal;		/* index of first local var (in Dyndata array) */
		public int FirstLabel;		/* index of first label (in 'dyd->label->arr') */
		public int NActVar;			/* number of active local variables */
		public int FreeReg;			/* first free register */
		public int IWthAbs;			/* instructions issued since last absolute line info */
		public bool NeedClose;		/* function needs to close upvalues when returning */

		/*
		** Return the "variable description" (VarDesc) of a given variable.
		** (Unless noted otherwise, all variables are referred to by their
		** compiler indices.)
		*/
		public VarDesc GetLocalVarDesc( int vidx )
		{
			return Dyd.ActVar[FirstLocal + vidx];
		}

		/*
		** Convert 'nvar', a compiler index level, to its corresponding
		** register. For that, search for the highest variable below that level
		** that is in a register and uses its register index ('ridx') plus one.
		*/
		public int RegLevel( int nvar )
		{
			while( nvar-- > 0 )
			{
				var vd = GetLocalVarDesc( nvar ); // get previous variable
				if( vd.Kind != VarDesc.RDKCTC ) // is in a register?
					return vd.RIdx + 1;
			}
			return 0; // no variables in registers
		}

		/*
		** Return the number of variables in the register stack for the given
		** function.
		*/
		public int NVarStack()
		{
			return RegLevel( NActVar );
		}
	}

	internal class Parser
	{
		public static LuaProto Parse(
			ILuaState lua, ILoadInfo loadinfo, string name )
		{
			var parser = new Parser();
			parser.Lua = (LuaState)lua;
			parser.Lexer = new LLex( lua, loadinfo, name );

			var topFuncState = new FuncState();
			topFuncState.Proto = new LuaProto();
			topFuncState.Proto.Source = name;
			parser.MainFunc( topFuncState );
			Utl.Assert( topFuncState.Prev == null && topFuncState.Proto.Upvalues.Count == 1 && parser.CurFunc == null );
			/* all scopes should be correctly finished */
			Utl.Assert( parser.Dyd.NActVar == 0 && parser.Dyd.Gt.Count == 0 && parser.Dyd.Label.Count == 0 );
			return topFuncState.Proto;
		}

		/* maximum number of local variables per function (must be smaller
		   than 250, due to the bytecode format) */
		private const int MAXVARS = 200;

		private LLex		Lexer;
		private FuncState	CurFunc;
		private Dyndata		Dyd;
		private LuaState	Lua;

		private Parser()
		{
			Dyd = new Dyndata();
			CurFunc = null;
		}

		private static bool HasMultRet( ExpKind k )
		{
			return k == ExpKind.VCALL || k == ExpKind.VVARARG;
		}

		private void ErrorExpected( int token )
		{
			Lexer.SyntaxError( string.Format( "{0} expected", Lexer.Token2Str( token ) ) );
		}

		private void ErrorLimit( FuncState fs, int limit, string what )
		{
			int line = fs.Proto.LineDefined;
			string where = (line == 0)
				? "main function"
				: string.Format( "function at line {0}", line );
			string msg = string.Format( "too many {0} (limit is {1}) in {2}",
				what, limit, where );
			Lexer.SyntaxError( msg );
		}

		private void CheckLimit( FuncState fs, int v, int l, string what )
		{
			if( v > l ) ErrorLimit( fs, l, what );
		}

		/*
		** Test whether next token is 'c'; if so, skip it.
		*/
		private bool TestNext( int c )
		{
			if( Lexer.Token.TokenType == c )
			{
				Lexer.Next();
				return true;
			}
			else return false;
		}

		/*
		** Check that next token is 'c'.
		*/
		private void Check( int c )
		{
			if( Lexer.Token.TokenType != c )
				ErrorExpected( c );
		}

		/*
		** Check that next token is 'c' and skip it.
		*/
		private void CheckNext( int c )
		{
			Check( c );
			Lexer.Next();
		}

		private void CheckCondition( bool c, string msg )
		{
			if( !c ) Lexer.SyntaxError( msg );
		}

		/*
		** Check that next token is 'what' and skip it. In case of error,
		** raise an error that the expected 'what' should match a 'who'
		** in line 'where' (if that is not the current line).
		*/
		private void CheckMatch( int what, int who, int where )
		{
			if( !TestNext( what ) )
			{
				if( where == Lexer.LineNumber ) // all in the same line?
					ErrorExpected( what ); // do not need a complex message
				else
					Lexer.SyntaxError( string.Format(
						"{0} expected (to close {1} at line {2})",
						Lexer.Token2Str( what ), Lexer.Token2Str( who ), where ) );
			}
		}

		private string StrCheckName()
		{
			Check( (int)TK.NAME );
			var ts = ((NameToken)Lexer.Token).SemInfo;
			Lexer.Next();
			return ts;
		}

		private static void InitExp( ExpDesc e, ExpKind k, int i )
		{
			e.ExitFalse = e.ExitTrue = Coder.NO_JUMP;
			e.Kind = k;
			e.Info = i;
		}

		private static void CodeString( ExpDesc e, string s )
		{
			e.ExitFalse = e.ExitTrue = Coder.NO_JUMP;
			e.Kind = ExpKind.VKSTR;
			e.StrValue = s;
		}

		private void CodeName( ExpDesc e )
		{
			CodeString( e, StrCheckName() );
		}

		/*
		** Register a new local variable in the active 'Proto' (for debug
		** information).
		*/
		private int RegisterLocalVar( FuncState fs, string varname )
		{
			var f = fs.Proto;
			if( f.LocVars.Count >= short.MaxValue )
				ErrorLimit( fs, short.MaxValue, "local variables" );
			var v = new LocVar();
			v.VarName = varname;
			v.StartPc = fs.Pc;
			f.LocVars.Add( v );
			return f.LocVars.Count - 1;
		}

		/*
		** Create a new local variable with the given 'name'. Return its index
		** in the function.
		*/
		private int NewLocalVar( string name )
		{
			var fs = CurFunc;
			var dyd = Dyd;
			CheckLimit( fs, dyd.NActVar + 1 - fs.FirstLocal, MAXVARS, "local variables" );
			var v = new VarDesc();
			v.Kind = VarDesc.VDKREG; // default
			v.Name = name;
			if( dyd.NActVar < dyd.ActVar.Count )
				dyd.ActVar[dyd.NActVar] = v;
			else
				dyd.ActVar.Add( v );
			dyd.NActVar++;
			return dyd.NActVar - 1 - fs.FirstLocal;
		}

		/*
		** Get the debug-information entry for current variable 'vidx'.
		*/
		private static LocVar LocalDebugInfo( FuncState fs, int vidx )
		{
			var vd = fs.GetLocalVarDesc( vidx );
			if( vd.Kind == VarDesc.RDKCTC )
				return null; // no debug info. for constants
			else
			{
				int idx = vd.PIdx;
				Utl.Assert( idx < fs.Proto.LocVars.Count );
				return fs.Proto.LocVars[idx];
			}
		}

		/*
		** Create an expression representing variable 'vidx'
		*/
		private static void InitVar( FuncState fs, ExpDesc e, int vidx )
		{
			e.ExitFalse = e.ExitTrue = Coder.NO_JUMP;
			e.Kind = ExpKind.VLOCAL;
			e.Var.VIdx = vidx;
			e.Var.RIdx = fs.GetLocalVarDesc( vidx ).RIdx;
		}

		/*
		** Raises an error if variable described by 'e' is read only
		*/
		private void CheckReadonly( ExpDesc e )
		{
			var fs = CurFunc;
			string varname = null; // to be set if variable is const
			switch( e.Kind )
			{
				case ExpKind.VCONST: {
					varname = Dyd.ActVar[e.Info].Name;
					break;
				}
				case ExpKind.VLOCAL: {
					var vardesc = fs.GetLocalVarDesc( e.Var.VIdx );
					if( vardesc.Kind != VarDesc.VDKREG ) // not a regular variable?
						varname = vardesc.Name;
					break;
				}
				case ExpKind.VUPVAL: {
					var up = fs.Proto.Upvalues[e.Info];
					if( up.Kind != VarDesc.VDKREG )
						varname = up.Name;
					break;
				}
				default:
					return; // other cases cannot be read-only
			}
			if( varname != null )
				Lexer.SemanticError( string.Format(
					"attempt to assign to const variable '{0}'", varname ) );
		}

		/*
		** Start the scope for the last 'nvars' created variables.
		*/
		private void AdjustLocalVars( int nvars )
		{
			var fs = CurFunc;
			int reglevel = fs.NVarStack();
			for( int i = 0; i < nvars; i++ )
			{
				int vidx = fs.NActVar++;
				var v = fs.GetLocalVarDesc( vidx );
				v.RIdx = reglevel++;
				v.PIdx = RegisterLocalVar( fs, v.Name );
			}
		}

		/*
		** Close the scope for all variables up to level 'tolevel'.
		** (debug info.)
		*/
		private void RemoveVars( FuncState fs, int tolevel )
		{
			Dyd.NActVar -= (fs.NActVar - tolevel);
			while( fs.NActVar > tolevel )
			{
				var v = LocalDebugInfo( fs, --fs.NActVar );
				if( v != null ) // does it have debug information?
					v.EndPc = fs.Pc;
			}
		}

		/*
		** Search the upvalues of the function 'fs' for one
		** with the given 'name'.
		*/
		private static int SearchUpvalue( FuncState fs, string name )
		{
			var up = fs.Proto.Upvalues;
			for( int i = 0; i < up.Count; i++ )
			{
				if( up[i].Name == name ) return i;
			}
			return -1; // not found
		}

		private UpvalDesc AllocUpvalue( FuncState fs )
		{
			var f = fs.Proto;
			CheckLimit( fs, f.Upvalues.Count + 1, LuaLimits.MAXUPVAL, "upvalues" );
			var up = new UpvalDesc();
			f.Upvalues.Add( up );
			return up;
		}

		private int NewUpvalue( FuncState fs, string name, ExpDesc v )
		{
			var up = AllocUpvalue( fs );
			var prev = fs.Prev;
			if( v.Kind == ExpKind.VLOCAL )
			{
				up.InStack = true;
				up.Index = v.Var.RIdx;
				up.Kind = prev.GetLocalVarDesc( v.Var.VIdx ).Kind;
				Utl.Assert( name == prev.GetLocalVarDesc( v.Var.VIdx ).Name );
			}
			else
			{
				up.InStack = false;
				up.Index = v.Info;
				up.Kind = prev.Proto.Upvalues[v.Info].Kind;
				Utl.Assert( name == prev.Proto.Upvalues[v.Info].Name );
			}
			up.Name = name;
			return fs.Proto.Upvalues.Count - 1;
		}

		/*
		** Look for an active local variable with the name 'n' in the
		** function 'fs'. If found, initialize 'var' with it and return
		** its expression kind; otherwise return -1.
		*/
		private static int SearchVar( FuncState fs, string n, ExpDesc var )
		{
			for( int i = fs.NActVar - 1; i >= 0; i-- )
			{
				var vd = fs.GetLocalVarDesc( i );
				if( n == vd.Name ) // found?
				{
					if( vd.Kind == VarDesc.RDKCTC ) // compile-time constant?
						InitExp( var, ExpKind.VCONST, fs.FirstLocal + i );
					else // real variable
						InitVar( fs, var, i );
					return (int)var.Kind;
				}
			}
			return -1; // not found
		}

		/*
		** Mark block where variable at given level was defined
		** (to emit close instructions later).
		*/
		private static void MarkUpval( FuncState fs, int level )
		{
			var bl = fs.Block;
			while( bl.NActVar > level )
				bl = bl.Previous;
			bl.Upval = true;
			fs.NeedClose = true;
		}

		/*
		** Mark that current block has a to-be-closed variable.
		*/
		private static void MarkToBeClosed( FuncState fs )
		{
			var bl = fs.Block;
			bl.Upval = true;
			bl.InsideTbc = true;
			fs.NeedClose = true;
		}

		/*
		** Find a variable with the given name 'n'. If it is an upvalue, add
		** this upvalue into all intermediate functions. If it is a global, set
		** 'var' as 'void' as a flag.
		*/
		private void SingleVarAux( FuncState fs, string n, ExpDesc var, bool bas )
		{
			if( fs == null ) // no more levels?
				InitExp( var, ExpKind.VVOID, 0 ); // default is global
			else
			{
				int v = SearchVar( fs, n, var ); // look up locals at current level
				if( v >= 0 ) // found?
				{
					if( v == (int)ExpKind.VLOCAL && !bas )
						MarkUpval( fs, var.Var.VIdx ); // local will be used as an upval
				}
				else // not found as local at current level; try upvalues
				{
					int idx = SearchUpvalue( fs, n ); // try existing upvalues
					if( idx < 0 ) // not found?
					{
						SingleVarAux( fs.Prev, n, var, false ); // try upper levels
						if( var.Kind == ExpKind.VLOCAL || var.Kind == ExpKind.VUPVAL ) // local or upvalue?
							idx = NewUpvalue( fs, n, var ); // will be a new upvalue
						else // it is a global or a constant
							return; // don't need to do anything at this level
					}
					InitExp( var, ExpKind.VUPVAL, idx ); // new or old upvalue
				}
			}
		}

		/*
		** Find a variable with the given name 'n', handling global variables
		** too.
		*/
		private void SingleVar( ExpDesc var )
		{
			string varname = StrCheckName();
			var fs = CurFunc;
			SingleVarAux( fs, varname, var, true );
			if( var.Kind == ExpKind.VVOID ) // global name?
			{
				var key = new ExpDesc();
				SingleVarAux( fs, LuaDef.LUA_ENV, var, true ); // get environment variable
				Utl.Assert( var.Kind != ExpKind.VVOID ); // this one must exist
				Coder.Exp2AnyRegUp( fs, var ); // but could be a constant
				CodeString( key, varname ); // key is variable name
				Coder.Indexed( fs, var, key ); // env[varname]
			}
		}

		/*
		** Adjust the number of results from an expression list 'e' with 'nexps'
		** expressions to 'nvars' values.
		*/
		private void AdjustAssign( int nvars, int nexps, ExpDesc e )
		{
			var fs = CurFunc;
			int needed = nvars - nexps; // extra values needed
			if( HasMultRet( e.Kind ) ) // last expression has multiple returns?
			{
				int extra = needed + 1; // discount last expression itself
				if( extra < 0 )
					extra = 0;
				Coder.SetReturns( fs, e, extra ); // last exp. provides the difference
			}
			else
			{
				if( e.Kind != ExpKind.VVOID ) // at least one expression?
					Coder.Exp2NextReg( fs, e ); // close last expression
				if( needed > 0 ) // missing values?
					Coder.Nil( fs, fs.FreeReg, needed ); // complete with nils
			}
			if( needed > 0 )
				Coder.ReserveRegs( fs, needed ); // registers for extra values
			else // adding 'needed' is actually a subtraction
				fs.FreeReg += needed; // remove extra values
		}

		private void EnterLevel()
		{
			Lua.E_IncCStack();
		}

		private void LeaveLevel()
		{
			Lua.NumCSharpCalls--;
		}

		/*
		** Generates an error that a goto jumps into the scope of some
		** local variable.
		*/
		private void JumpScopeError( LabelDesc gt )
		{
			string varname = CurFunc.GetLocalVarDesc( gt.NActVar ).Name;
			Lexer.SemanticError( string.Format(
				"<goto {0}> at line {1} jumps into the scope of local '{2}'",
				gt.Name, gt.Line, varname ) );
		}

		/*
		** Solves the goto at index 'g' to given 'label' and removes it
		** from the list of pending gotos.
		** If it jumps into the scope of some variable, raises an error.
		*/
		private void SolveGoto( int g, LabelDesc label )
		{
			var gl = Dyd.Gt; // list of gotos
			var gt = gl[g]; // goto to be resolved
			Utl.Assert( gt.Name == label.Name );
			if( gt.NActVar < label.NActVar ) // enter some scope?
				JumpScopeError( gt );
			Coder.PatchList( CurFunc, gt.Pc, label.Pc );
			gl.RemoveAt( g ); // remove goto from pending list
		}

		/*
		** Search for an active label with the given name.
		*/
		private LabelDesc FindLabel( string name )
		{
			/* check labels in current function for a match */
			for( int i = CurFunc.FirstLabel; i < Dyd.Label.Count; i++ )
			{
				var lb = Dyd.Label[i];
				if( lb.Name == name ) // correct label?
					return lb;
			}
			return null; // label not found
		}

		/*
		** Adds a new label/goto in the corresponding list.
		*/
		private int NewLabelEntry( List<LabelDesc> l, string name, int line, int pc )
		{
			var desc = new LabelDesc();
			desc.Name = name;
			desc.Line = line;
			desc.NActVar = CurFunc.NActVar;
			desc.Close = false;
			desc.Pc = pc;
			l.Add( desc );
			return l.Count - 1;
		}

		private int NewGotoEntry( string name, int line, int pc )
		{
			return NewLabelEntry( Dyd.Gt, name, line, pc );
		}

		/*
		** Solves forward jumps. Check whether new label 'lb' matches any
		** pending gotos in current block and solves them. Return true
		** if any of the gotos need to close upvalues.
		*/
		private bool SolveGotos( LabelDesc lb )
		{
			var gl = Dyd.Gt;
			int i = CurFunc.Block.FirstGoto;
			bool needsclose = false;
			while( i < gl.Count )
			{
				if( gl[i].Name == lb.Name )
				{
					needsclose |= gl[i].Close;
					SolveGoto( i, lb ); // will remove 'i' from the list
				}
				else
					i++;
			}
			return needsclose;
		}

		/*
		** Create a new label with the given 'name' at the given 'line'.
		** 'last' tells whether label is the last non-op statement in its
		** block. Solves all pending gotos to this new label and adds
		** a close instruction if necessary.
		** Returns true iff it added a close instruction.
		*/
		private bool CreateLabel( string name, int line, bool last )
		{
			var fs = CurFunc;
			var ll = Dyd.Label;
			int l = NewLabelEntry( ll, name, line, Coder.GetLabel( fs ) );
			if( last ) // label is last no-op statement in the block?
			{
				/* assume that locals are already out of scope */
				ll[l].NActVar = fs.Block.NActVar;
			}
			if( SolveGotos( ll[l] ) ) // need close?
			{
				Coder.CodeABC( fs, OpCode.OP_CLOSE, fs.NVarStack(), 0, 0 );
				return true;
			}
			return false;
		}

		/*
		** Adjust pending gotos to outer level of a block.
		*/
		private void MoveGotosOut( FuncState fs, BlockCnt bl )
		{
			var gl = Dyd.Gt;
			/* correct pending gotos to current block */
			for( int i = bl.FirstGoto; i < gl.Count; i++ ) // for each pending goto
			{
				var gt = gl[i];
				/* leaving a variable scope? */
				if( fs.RegLevel( gt.NActVar ) > fs.RegLevel( bl.NActVar ) )
					gt.Close |= bl.Upval; // jump may need a close
				gt.NActVar = bl.NActVar; // update goto level
			}
		}

		private void EnterBlock( FuncState fs, BlockCnt bl, bool isloop )
		{
			bl.IsLoop = isloop;
			bl.NActVar = fs.NActVar;
			bl.FirstLabel = Dyd.Label.Count;
			bl.FirstGoto = Dyd.Gt.Count;
			bl.Upval = false;
			bl.InsideTbc = (fs.Block != null && fs.Block.InsideTbc);
			bl.Previous = fs.Block;
			fs.Block = bl;
			Utl.Assert( fs.FreeReg == fs.NVarStack() );
		}

		/*
		** generates an error for an undefined 'goto'.
		*/
		private void UndefGoto( LabelDesc gt )
		{
			string msg;
			if( gt.Name == "break" )
				msg = string.Format( "break outside loop at line {0}", gt.Line );
			else
				msg = string.Format( "no visible label '{0}' for <goto> at line {1}",
					gt.Name, gt.Line );
			Lexer.SemanticError( msg );
		}

		private void LeaveBlock( FuncState fs )
		{
			var bl = fs.Block;
			bool hasclose = false;
			int stklevel = fs.RegLevel( bl.NActVar ); // level outside the block
			RemoveVars( fs, bl.NActVar ); // remove block locals
			Utl.Assert( bl.NActVar == fs.NActVar ); // back to level on entry
			if( bl.IsLoop ) // has to fix pending breaks?
				hasclose = CreateLabel( "break", 0, false );
			if( !hasclose && bl.Previous != null && bl.Upval ) // still need a 'close'?
				Coder.CodeABC( fs, OpCode.OP_CLOSE, stklevel, 0, 0 );
			fs.FreeReg = stklevel; // free registers
			Dyd.Label.RemoveRange( bl.FirstLabel, Dyd.Label.Count - bl.FirstLabel ); // remove local labels
			fs.Block = bl.Previous; // current block now is previous one
			if( bl.Previous != null ) // was it a nested block?
				MoveGotosOut( fs, bl ); // update pending gotos to enclosing block
			else
			{
				if( bl.FirstGoto < Dyd.Gt.Count ) // still pending gotos?
					UndefGoto( Dyd.Gt[bl.FirstGoto] ); // error
			}
		}

		/*
		** adds a new prototype into list of prototypes
		*/
		private LuaProto AddPrototype()
		{
			var f = CurFunc.Proto; // prototype of current function
			if( f.P.Count >= Instruction.MAXARG_Bx )
				ErrorLimit( CurFunc, Instruction.MAXARG_Bx, "functions" );
			var clp = new LuaProto();
			f.P.Add( clp );
			return clp;
		}

		/*
		** codes instruction to create new closure in parent function.
		** The OP_CLOSURE instruction uses the last available register,
		** so that, if it invokes the GC, the GC knows which registers
		** are in use at that time.
		*/
		private void CodeClosure( ExpDesc v )
		{
			var fs = CurFunc.Prev;
			InitExp( v, ExpKind.VRELOC, Coder.CodeABx( fs, OpCode.OP_CLOSURE, 0,
				(uint)(fs.Proto.P.Count - 1) ) );
			Coder.Exp2NextReg( fs, v ); // fix it at the last register
		}

		private void OpenFunc( FuncState fs, BlockCnt bl )
		{
			var f = fs.Proto;
			fs.Prev = CurFunc; // linked list of funcstates
			fs.Lexer = Lexer;
			fs.State = Lua;
			fs.Dyd = Dyd;
			CurFunc = fs;
			fs.Pc = 0;
			fs.PreviousLine = f.LineDefined;
			fs.IWthAbs = 0;
			fs.LastTarget = 0;
			fs.FreeReg = 0;
			fs.NAbsLineInfo = 0;
			fs.NActVar = 0;
			fs.NeedClose = false;
			fs.FirstLocal = Dyd.NActVar;
			fs.FirstLabel = Dyd.Label.Count;
			fs.Block = null;
			f.Source = Lexer.Source;
			f.MaxStackSize = 2; // registers 0/1 are always valid
			EnterBlock( fs, bl, false );
		}

		// luaM_shrinkvector: drops the entries of 'list' past its first 'n'
		private static void Shrink<T>( List<T> list, int n )
		{
			if( list.Count > n )
				list.RemoveRange( n, list.Count - n );
		}

		private void CloseFunc()
		{
			var fs = CurFunc;
			var f = fs.Proto;
			Coder.Ret( fs, fs.NVarStack(), 0 ); // final return
			LeaveBlock( fs );
			Utl.Assert( fs.Block == null );
			Coder.Finish( fs );
			Shrink( f.Code, fs.Pc );
			Shrink( f.LineInfo, fs.Pc );
			Shrink( f.AbsLineInfo, fs.NAbsLineInfo );
			CurFunc = fs.Prev;
		}

		/*============================================================*/
		/* GRAMMAR RULES */
		/*============================================================*/

		/*
		** check whether current token is in the follow set of a block.
		** 'until' closes syntactical blocks, but do not close scope,
		** so it is handled in separate.
		*/
		private bool BlockFollow( bool withuntil )
		{
			switch( Lexer.Token.TokenType )
			{
				case (int)TK.ELSE: case (int)TK.ELSEIF:
				case (int)TK.END: case (int)TK.EOS:
					return true;
				case (int)TK.UNTIL: return withuntil;
				default: return false;
			}
		}

		private void StatList()
		{
			/* statlist -> { stat [';'] } */
			while( !BlockFollow( true ) )
			{
				if( Lexer.Token.TokenType == (int)TK.RETURN )
				{
					Statement();
					return; // 'return' must be last statement
				}
				Statement();
			}
		}

		private void FieldSel( ExpDesc v )
		{
			/* fieldsel -> ['.' | ':'] NAME */
			var fs = CurFunc;
			var key = new ExpDesc();
			Coder.Exp2AnyRegUp( fs, v );
			Lexer.Next(); // skip the dot or colon
			CodeName( key );
			Coder.Indexed( fs, v, key );
		}

		private void YIndex( ExpDesc v )
		{
			/* index -> '[' expr ']' */
			Lexer.Next(); // skip the '['
			Expr( v );
			Coder.Exp2Val( CurFunc, v );
			CheckNext( (int)']' );
		}

		/*
		** {======================================================================
		** Rules for Constructors
		** =======================================================================
		*/

		private class ConsControl
		{
			public ExpDesc	V = new ExpDesc();	/* last list item read */
			public ExpDesc	T;			/* table descriptor */
			public int		NH;			/* total number of 'record' elements */
			public int		NA;			/* number of array elements already stored */
			public int		ToStore;	/* number of array elements pending to be stored */
		}

		private void RecField( ConsControl cc )
		{
			/* recfield -> (NAME | '['exp']') = exp */
			var fs = CurFunc;
			int reg = CurFunc.FreeReg;
			var tab = new ExpDesc();
			var key = new ExpDesc();
			var val = new ExpDesc();
			if( Lexer.Token.TokenType == (int)TK.NAME )
				CodeName( key );
			else // ls->t.token == '['
				YIndex( key );
			CheckLimit( fs, cc.NH, LuaLimits.MAX_INT, "items in a constructor" );
			cc.NH++;
			CheckNext( (int)'=' );
			tab.CopyFrom( cc.T );
			Coder.Indexed( fs, tab, key );
			Expr( val );
			Coder.StoreVar( fs, tab, val );
			fs.FreeReg = reg; // free registers
		}

		private void CloseListField( FuncState fs, ConsControl cc )
		{
			if( cc.V.Kind == ExpKind.VVOID ) return; // there is no list item
			Coder.Exp2NextReg( fs, cc.V );
			cc.V.Kind = ExpKind.VVOID;
			if( cc.ToStore == LuaDef.LFIELDS_PER_FLUSH )
			{
				Coder.SetList( fs, cc.T.Info, cc.NA, cc.ToStore ); // flush
				cc.NA += cc.ToStore;
				cc.ToStore = 0; // no more items pending
			}
		}

		private void LastListField( FuncState fs, ConsControl cc )
		{
			if( cc.ToStore == 0 ) return;
			if( HasMultRet( cc.V.Kind ) )
			{
				Coder.SetMultRet( fs, cc.V );
				Coder.SetList( fs, cc.T.Info, cc.NA, LuaDef.LUA_MULTRET );
				cc.NA--; // do not count last expression (unknown number of elements)
			}
			else
			{
				if( cc.V.Kind != ExpKind.VVOID )
					Coder.Exp2NextReg( fs, cc.V );
				Coder.SetList( fs, cc.T.Info, cc.NA, cc.ToStore );
			}
			cc.NA += cc.ToStore;
		}

		private void ListField( ConsControl cc )
		{
			/* listfield -> exp */
			Expr( cc.V );
			cc.ToStore++;
		}

		private void Field( ConsControl cc )
		{
			/* field -> listfield | recfield */
			switch( Lexer.Token.TokenType )
			{
				case (int)TK.NAME: { // may be 'listfield' or 'recfield'
					if( Lexer.GetLookAhead().TokenType != (int)'=' ) // expression?
						ListField( cc );
					else
						RecField( cc );
					break;
				}
				case (int)'[': {
					RecField( cc );
					break;
				}
				default: {
					ListField( cc );
					break;
				}
			}
		}

		private void Constructor( ExpDesc t )
		{
			/* constructor -> '{' [ field { sep field } [sep] ] '}'
			   sep -> ',' | ';' */
			var fs = CurFunc;
			int line = Lexer.LineNumber;
			int pc = Coder.CodeABC( fs, OpCode.OP_NEWTABLE, 0, 0, 0 );
			var cc = new ConsControl();
			Coder.Code( fs, new Instruction( 0 ) ); // space for extra arg.
			cc.NA = cc.NH = cc.ToStore = 0;
			cc.T = t;
			InitExp( t, ExpKind.VNONRELOC, fs.FreeReg ); // table will be at stack top
			Coder.ReserveRegs( fs, 1 );
			InitExp( cc.V, ExpKind.VVOID, 0 ); // no value (yet)
			CheckNext( (int)'{' );
			do {
				Utl.Assert( cc.V.Kind == ExpKind.VVOID || cc.ToStore > 0 );
				if( Lexer.Token.TokenType == (int)'}' ) break;
				CloseListField( fs, cc );
				Field( cc );
				CheckLimit( fs, cc.ToStore + cc.NA + cc.NH, int.MaxValue / 2,
					"items in a constructor" );
			} while( TestNext( (int)',' ) || TestNext( (int)';' ) );
			CheckMatch( (int)'}', (int)'{', line );
			LastListField( fs, cc );
			Coder.SetTableSize( fs, pc, t.Info, cc.NA, cc.NH );
		}

		/* }====================================================================== */

		private void SetVararg( FuncState fs, int nparams )
		{
			fs.Proto.IsVarArg = true;
			Coder.CodeABC( fs, OpCode.OP_VARARGPREP, nparams, 0, 0 );
		}

		private void ParList()
		{
			/* parlist -> [ {NAME ','} (NAME | '...') ] */
			var fs = CurFunc;
			var f = fs.Proto;
			int nparams = 0;
			bool isvararg = false;
			if( Lexer.Token.TokenType != (int)')' ) // is 'parlist' not empty?
			{
				do {
					switch( Lexer.Token.TokenType )
					{
						case (int)TK.NAME: {
							NewLocalVar( StrCheckName() );
							nparams++;
							break;
						}
						case (int)TK.DOTS: {
							Lexer.Next();
							isvararg = true;
							break;
						}
						default: Lexer.SyntaxError( "<name> or '...' expected" ); break;
					}
				} while( !isvararg && TestNext( (int)',' ) );
			}
			AdjustLocalVars( nparams );
			f.NumParams = fs.NActVar;
			if( isvararg )
				SetVararg( fs, f.NumParams ); // declared vararg
			Coder.ReserveRegs( fs, fs.NActVar ); // reserve registers for parameters
		}

		private void Body( ExpDesc e, bool ismethod, int line )
		{
			/* body ->  '(' parlist ')' block END */
			var newFs = new FuncState();
			var bl = new BlockCnt();
			newFs.Proto = AddPrototype();
			newFs.Proto.LineDefined = line;
			OpenFunc( newFs, bl );
			CheckNext( (int)'(' );
			if( ismethod )
			{
				NewLocalVar( "self" ); // create 'self' parameter
				AdjustLocalVars( 1 );
			}
			ParList();
			CheckNext( (int)')' );
			StatList();
			newFs.Proto.LastLineDefined = Lexer.LineNumber;
			CheckMatch( (int)TK.END, (int)TK.FUNCTION, line );
			CodeClosure( e );
			CloseFunc();
		}

		private int ExpList( ExpDesc v )
		{
			/* explist -> expr { ',' expr } */
			int n = 1; // at least one expression
			Expr( v );
			while( TestNext( (int)',' ) )
			{
				Coder.Exp2NextReg( CurFunc, v );
				Expr( v );
				n++;
			}
			return n;
		}

		private void FuncArgs( ExpDesc f )
		{
			var fs = CurFunc;
			var args = new ExpDesc();
			int bas, nparams;
			int line = Lexer.LineNumber;
			switch( Lexer.Token.TokenType )
			{
				case (int)'(': { // funcargs -> '(' [ explist ] ')'
					Lexer.Next();
					if( Lexer.Token.TokenType == (int)')' ) // arg list is empty?
						args.Kind = ExpKind.VVOID;
					else
					{
						ExpList( args );
						if( HasMultRet( args.Kind ) )
							Coder.SetMultRet( fs, args );
					}
					CheckMatch( (int)')', (int)'(', line );
					break;
				}
				case (int)'{': { // funcargs -> constructor
					Constructor( args );
					break;
				}
				case (int)TK.STRING: { // funcargs -> STRING
					CodeString( args, ((StringToken)Lexer.Token).SemInfo );
					Lexer.Next(); // must use 'seminfo' before 'next'
					break;
				}
				default: {
					Lexer.SyntaxError( "function arguments expected" );
					break;
				}
			}
			Utl.Assert( f.Kind == ExpKind.VNONRELOC );
			bas = f.Info; // base register for call
			if( HasMultRet( args.Kind ) )
				nparams = LuaDef.LUA_MULTRET; // open call
			else
			{
				if( args.Kind != ExpKind.VVOID )
					Coder.Exp2NextReg( fs, args ); // close last argument
				nparams = fs.FreeReg - (bas + 1);
			}
			InitExp( f, ExpKind.VCALL, Coder.CodeABC( fs, OpCode.OP_CALL, bas, nparams + 1, 2 ) );
			Coder.FixLine( fs, line );
			fs.FreeReg = bas + 1; /* call removes function and arguments and leaves
									 one result (unless changed later) */
		}

		/*
		** {======================================================================
		** Expression parsing
		** =======================================================================
		*/

		private void PrimaryExp( ExpDesc v )
		{
			/* primaryexp -> NAME | '(' expr ')' */
			switch( Lexer.Token.TokenType )
			{
				case (int)'(': {
					int line = Lexer.LineNumber;
					Lexer.Next();
					Expr( v );
					CheckMatch( (int)')', (int)'(', line );
					Coder.DischargeVars( CurFunc, v );
					return;
				}
				case (int)TK.NAME: {
					SingleVar( v );
					return;
				}
				default: {
					Lexer.SyntaxError( "unexpected symbol" );
					return;
				}
			}
		}

		private void SuffixedExp( ExpDesc v )
		{
			/* suffixedexp ->
				 primaryexp { '.' NAME | '[' exp ']' | ':' NAME funcargs | funcargs } */
			var fs = CurFunc;
			PrimaryExp( v );
			for( ;; )
			{
				switch( Lexer.Token.TokenType )
				{
					case (int)'.': { // fieldsel
						FieldSel( v );
						break;
					}
					case (int)'[': { // '[' exp ']'
						var key = new ExpDesc();
						Coder.Exp2AnyRegUp( fs, v );
						YIndex( key );
						Coder.Indexed( fs, v, key );
						break;
					}
					case (int)':': { // ':' NAME funcargs
						var key = new ExpDesc();
						Lexer.Next();
						CodeName( key );
						Coder.Self( fs, v, key );
						FuncArgs( v );
						break;
					}
					case (int)'(': case (int)TK.STRING: case (int)'{': { // funcargs
						Coder.Exp2NextReg( fs, v );
						FuncArgs( v );
						break;
					}
					default: return;
				}
			}
		}

		private void SimpleExp( ExpDesc v )
		{
			/* simpleexp -> FLT | INT | STRING | NIL | TRUE | FALSE | ... |
							constructor | FUNCTION body | suffixedexp */
			switch( Lexer.Token.TokenType )
			{
				case (int)TK.FLT: {
					InitExp( v, ExpKind.VKFLT, 0 );
					v.NumberValue = ((NumberToken)Lexer.Token).SemInfo;
					break;
				}
				case (int)TK.INT: {
					InitExp( v, ExpKind.VKINT, 0 );
					v.IntValue = ((IntegerToken)Lexer.Token).SemInfo;
					break;
				}
				case (int)TK.STRING: {
					CodeString( v, ((StringToken)Lexer.Token).SemInfo );
					break;
				}
				case (int)TK.NIL: {
					InitExp( v, ExpKind.VNIL, 0 );
					break;
				}
				case (int)TK.TRUE: {
					InitExp( v, ExpKind.VTRUE, 0 );
					break;
				}
				case (int)TK.FALSE: {
					InitExp( v, ExpKind.VFALSE, 0 );
					break;
				}
				case (int)TK.DOTS: { // vararg
					var fs = CurFunc;
					CheckCondition( fs.Proto.IsVarArg,
						"cannot use '...' outside a vararg function" );
					InitExp( v, ExpKind.VVARARG, Coder.CodeABC( fs, OpCode.OP_VARARG, 0, 0, 1 ) );
					break;
				}
				case (int)'{': { // constructor
					Constructor( v );
					return;
				}
				case (int)TK.FUNCTION: {
					Lexer.Next();
					Body( v, false, Lexer.LineNumber );
					return;
				}
				default: {
					SuffixedExp( v );
					return;
				}
			}
			Lexer.Next();
		}

		private static UnOpr GetUnOpr( int op )
		{
			switch( op )
			{
				case (int)TK.NOT: return UnOpr.NOT;
				case (int)'-': return UnOpr.MINUS;
				case (int)'~': return UnOpr.BNOT;
				case (int)'#': return UnOpr.LEN;
				default: return UnOpr.NOUNOPR;
			}
		}

		private static BinOpr GetBinOpr( int op )
		{
			switch( op )
			{
				case (int)'+': return BinOpr.ADD;
				case (int)'-': return BinOpr.SUB;
				case (int)'*': return BinOpr.MUL;
				case (int)'%': return BinOpr.MOD;
				case (int)'^': return BinOpr.POW;
				case (int)'/': return BinOpr.DIV;
				case (int)TK.IDIV: return BinOpr.IDIV;
				case (int)'&': return BinOpr.BAND;
				case (int)'|': return BinOpr.BOR;
				case (int)'~': return BinOpr.BXOR;
				case (int)TK.SHL: return BinOpr.SHL;
				case (int)TK.SHR: return BinOpr.SHR;
				case (int)TK.CONCAT: return BinOpr.CONCAT;
				case (int)TK.NE: return BinOpr.NE;
				case (int)TK.EQ: return BinOpr.EQ;
				case (int)'<': return BinOpr.LT;
				case (int)TK.LE: return BinOpr.LE;
				case (int)'>': return BinOpr.GT;
				case (int)TK.GE: return BinOpr.GE;
				case (int)TK.AND: return BinOpr.AND;
				case (int)TK.OR: return BinOpr.OR;
				default: return BinOpr.NOBINOPR;
			}
		}

		/*
		** Priority table for binary operators.
		*/
		private static readonly int[,] Priority = { /* ORDER OPR */
			{10, 10}, {10, 10},		/* '+' '-' */
			{11, 11}, {11, 11},		/* '*' '%' */
			{14, 13},				/* '^' (right associative) */
			{11, 11}, {11, 11},		/* '/' '//' */
			{6, 6}, {4, 4}, {5, 5},	/* '&' '|' '~' */
			{7, 7}, {7, 7},			/* '<<' '>>' */
			{9, 8},					/* '..' (right associative) */
			{3, 3}, {3, 3}, {3, 3},	/* ==, <, <= */
			{3, 3}, {3, 3}, {3, 3},	/* ~=, >, >= */
			{2, 2}, {1, 1}			/* and, or */
		};

		private const int UNARY_PRIORITY = 12; // priority for unary operators

		/*
		** subexpr -> (simpleexp | unop subexpr) { binop subexpr }
		** where 'binop' is any binary operator with a priority higher than 'limit'
		*/
		private BinOpr SubExpr( ExpDesc v, int limit )
		{
			BinOpr op;
			UnOpr uop;
			EnterLevel();
			uop = GetUnOpr( Lexer.Token.TokenType );
			if( uop != UnOpr.NOUNOPR ) // prefix (unary) operator?
			{
				int line = Lexer.LineNumber;
				Lexer.Next(); // skip operator
				SubExpr( v, UNARY_PRIORITY );
				Coder.Prefix( CurFunc, uop, v, line );
			}
			else SimpleExp( v );
			/* expand while operators have priorities higher than 'limit' */
			op = GetBinOpr( Lexer.Token.TokenType );
			while( op != BinOpr.NOBINOPR && Priority[(int)op, 0] > limit )
			{
				var v2 = new ExpDesc();
				BinOpr nextop;
				int line = Lexer.LineNumber;
				Lexer.Next(); // skip operator
				Coder.Infix( CurFunc, op, v );
				/* read sub-expression with higher priority */
				nextop = SubExpr( v2, Priority[(int)op, 1] );
				Coder.Posfix( CurFunc, op, v, v2, line );
				op = nextop;
			}
			LeaveLevel();
			return op; // return first untreated operator
		}

		private void Expr( ExpDesc v )
		{
			SubExpr( v, 0 );
		}

		/* }==================================================================== */

		/*
		** {======================================================================
		** Rules for Statements
		** =======================================================================
		*/

		private void Block()
		{
			/* block -> statlist */
			var fs = CurFunc;
			var bl = new BlockCnt();
			EnterBlock( fs, bl, false );
			StatList();
			LeaveBlock( fs );
		}

		/*
		** structure to chain all variables in the left-hand side of an
		** assignment
		*/
		private class LHSAssign
		{
			public LHSAssign	Prev;
			public ExpDesc		V = new ExpDesc();	/* variable (global, local, upvalue, or indexed) */
		}

		/*
		** check whether, in an assignment to an upvalue/local variable, the
		** upvalue/local variable is begin used in a previous assignment to a
		** table. If so, save original upvalue/local value in a safe place and
		** use this safe copy in the previous assignment.
		*/
		private void CheckConflict( LHSAssign lh, ExpDesc v )
		{
			var fs = CurFunc;
			int extra = fs.FreeReg; // eventual position to save local variable
			bool conflict = false;
			for( ; lh != null; lh = lh.Prev ) // check all previous assignments
			{
				if( ExpKindUtl.VKIsIndexed( lh.V.Kind ) ) // assignment to table field?
				{
					if( lh.V.Kind == ExpKind.VINDEXUP ) // is table an upvalue?
					{
						if( v.Kind == ExpKind.VUPVAL && lh.V.Ind.T == v.Info )
						{
							conflict = true; // table is the upvalue being assigned now
							lh.V.Kind = ExpKind.VINDEXSTR;
							lh.V.Ind.T = extra; // assignment will use safe copy
						}
					}
					else // table is a register
					{
						if( v.Kind == ExpKind.VLOCAL && lh.V.Ind.T == v.Var.RIdx )
						{
							conflict = true; // table is the local being assigned now
							lh.V.Ind.T = extra; // assignment will use safe copy
						}
						/* is index the local being assigned? */
						if( lh.V.Kind == ExpKind.VINDEXED && v.Kind == ExpKind.VLOCAL &&
							lh.V.Ind.Idx == v.Var.RIdx )
						{
							conflict = true;
							lh.V.Ind.Idx = extra; // previous assignment will use safe copy
						}
					}
				}
			}
			if( conflict )
			{
				/* copy upvalue/local value to a temporary (in position 'extra') */
				if( v.Kind == ExpKind.VLOCAL )
					Coder.CodeABC( fs, OpCode.OP_MOVE, extra, v.Var.RIdx, 0 );
				else
					Coder.CodeABC( fs, OpCode.OP_GETUPVAL, extra, v.Info, 0 );
				Coder.ReserveRegs( fs, 1 );
			}
		}

		/*
		** Parse and compile a multiple assignment. The first "variable"
		** (a 'suffixedexp') was already read by the caller.
		**
		** assignment -> suffixedexp restassign
		** restassign -> ',' suffixedexp restassign | '=' explist
		*/
		private void RestAssign( LHSAssign lh, int nvars )
		{
			var e = new ExpDesc();
			CheckCondition( ExpKindUtl.VKIsVar( lh.V.Kind ), "syntax error" );
			CheckReadonly( lh.V );
			if( TestNext( (int)',' ) ) // restassign -> ',' suffixedexp restassign
			{
				var nv = new LHSAssign();
				nv.Prev = lh;
				SuffixedExp( nv.V );
				if( !ExpKindUtl.VKIsIndexed( nv.V.Kind ) )
					CheckConflict( lh, nv.V );
				EnterLevel(); // control recursion depth
				RestAssign( nv, nvars + 1 );
				LeaveLevel();
			}
			else // restassign -> '=' explist
			{
				int nexps;
				CheckNext( (int)'=' );
				nexps = ExpList( e );
				if( nexps != nvars )
					AdjustAssign( nvars, nexps, e );
				else
				{
					Coder.SetOneRet( CurFunc, e ); // close last expression
					Coder.StoreVar( CurFunc, lh.V, e );
					return; // avoid default
				}
			}
			InitExp( e, ExpKind.VNONRELOC, CurFunc.FreeReg - 1 ); // default assignment
			Coder.StoreVar( CurFunc, lh.V, e );
		}

		private int Cond()
		{
			/* cond -> exp */
			var v = new ExpDesc();
			Expr( v ); // read condition
			if( v.Kind == ExpKind.VNIL ) v.Kind = ExpKind.VFALSE; // 'falses' are all equal here
			Coder.GoIfTrue( CurFunc, v );
			return v.ExitFalse;
		}

		private void GotoStat()
		{
			var fs = CurFunc;
			int line = Lexer.LineNumber;
			string name = StrCheckName(); // label's name
			var lb = FindLabel( name );
			if( lb == null ) // no label?
				/* forward jump; will be resolved when the label is declared */
				NewGotoEntry( name, line, Coder.Jump( fs ) );
			else // found a label
			{
				/* backward jump; will be resolved here */
				int lblevel = fs.RegLevel( lb.NActVar ); // label level
				if( fs.NVarStack() > lblevel ) // leaving the scope of a variable?
					Coder.CodeABC( fs, OpCode.OP_CLOSE, lblevel, 0, 0 );
				/* create jump and link it to the label */
				Coder.PatchList( fs, Coder.Jump( fs ), lb.Pc );
			}
		}

		/*
		** Break statement. Semantically equivalent to "goto break".
		*/
		private void BreakStat()
		{
			int line = Lexer.LineNumber;
			Lexer.Next(); // skip break
			NewGotoEntry( "break", line, Coder.Jump( CurFunc ) );
		}

		/*
		** Check whether there is already a label with the given 'name'.
		*/
		private void CheckRepeated( string name )
		{
			var lb = FindLabel( name );
			if( lb != null ) // already defined?
				Lexer.SemanticError( string.Format(
					"label '{0}' already defined on line {1}", name, lb.Line ) );
		}

		private void LabelStat( string name, int line )
		{
			/* label -> '::' NAME '::' */
			CheckNext( (int)TK.DBCOLON ); // skip double colon
			while( Lexer.Token.TokenType == (int)';' || Lexer.Token.TokenType == (int)TK.DBCOLON )
				Statement(); // skip other no-op statements
			CheckRepeated( name ); // check for repeated labels
			CreateLabel( name, line, BlockFollow( false ) );
		}

		private void WhileStat( int line )
		{
			/* whilestat -> WHILE cond DO block END */
			var fs = CurFunc;
			int whileinit;
			int condexit;
			var bl = new BlockCnt();
			Lexer.Next(); // skip WHILE
			whileinit = Coder.GetLabel( fs );
			condexit = Cond();
			EnterBlock( fs, bl, true );
			CheckNext( (int)TK.DO );
			Block();
			Coder.JumpTo( fs, whileinit );
			CheckMatch( (int)TK.END, (int)TK.WHILE, line );
			LeaveBlock( fs );
			Coder.PatchToHere( fs, condexit ); // false conditions finish the loop
		}

		private void RepeatStat( int line )
		{
			/* repeatstat -> REPEAT block UNTIL cond */
			int condexit;
			var fs = CurFunc;
			int repeat_init = Coder.GetLabel( fs );
			var bl1 = new BlockCnt();
			var bl2 = new BlockCnt();
			EnterBlock( fs, bl1, true ); // loop block
			EnterBlock( fs, bl2, false ); // scope block
			Lexer.Next(); // skip REPEAT
			StatList();
			CheckMatch( (int)TK.UNTIL, (int)TK.REPEAT, line );
			condexit = Cond(); // read condition (inside scope block)
			LeaveBlock( fs ); // finish scope
			if( bl2.Upval ) // upvalues?
			{
				int exit = Coder.Jump( fs ); // normal exit must jump over fix
				Coder.PatchToHere( fs, condexit ); // repetition must close upvalues
				Coder.CodeABC( fs, OpCode.OP_CLOSE, fs.RegLevel( bl2.NActVar ), 0, 0 );
				condexit = Coder.Jump( fs ); // repeat after closing upvalues
				Coder.PatchToHere( fs, exit ); // normal exit comes to here
			}
			Coder.PatchList( fs, condexit, repeat_init ); // close the loop
			LeaveBlock( fs ); // finish loop
		}

		/*
		** Read an expression and generate code to put its results in next
		** stack slot.
		**
		*/
		private void Exp1()
		{
			var e = new ExpDesc();
			Expr( e );
			Coder.Exp2NextReg( CurFunc, e );
			Utl.Assert( e.Kind == ExpKind.VNONRELOC );
		}

		/*
		** Fix for instruction at position 'pc' to jump to 'dest'.
		** (Jump addresses are relative in Lua). 'back' true means
		** a back jump.
		*/
		private void FixForJump( FuncState fs, int pc, int dest, bool back )
		{
			var jmp = fs.Proto.Code[pc];
			int offset = dest - (pc + 1);
			if( back )
				offset = -offset;
			if( offset > Instruction.MAXARG_Bx )
				Lexer.SyntaxError( "control structure too long" );
			jmp.SETARG_Bx( offset );
			fs.Proto.Code[pc] = jmp;
		}

		/*
		** Generate code for a 'for' loop.
		*/
		private void ForBody( int bas, int line, int nvars, bool isgen )
		{
			/* forbody -> DO block */
			var bl = new BlockCnt();
			var fs = CurFunc;
			int prep, endfor;
			CheckNext( (int)TK.DO );
			prep = Coder.CodeABx( fs, isgen ? OpCode.OP_TFORPREP : OpCode.OP_FORPREP, bas, 0 );
			EnterBlock( fs, bl, false ); // scope for declared variables
			AdjustLocalVars( nvars );
			Coder.ReserveRegs( fs, nvars );
			Block();
			LeaveBlock( fs ); // end of scope for declared variables
			FixForJump( fs, prep, Coder.GetLabel( fs ), false );
			if( isgen ) // generic for?
			{
				Coder.CodeABC( fs, OpCode.OP_TFORCALL, bas, 0, nvars );
				Coder.FixLine( fs, line );
			}
			endfor = Coder.CodeABx( fs, isgen ? OpCode.OP_TFORLOOP : OpCode.OP_FORLOOP, bas, 0 );
			FixForJump( fs, endfor, prep + 1, true );
			Coder.FixLine( fs, line );
		}

		private void ForNum( string varname, int line )
		{
			/* fornum -> NAME = exp,exp[,exp] forbody */
			var fs = CurFunc;
			int bas = fs.FreeReg;
			NewLocalVar( "(for state)" );
			NewLocalVar( "(for state)" );
			NewLocalVar( "(for state)" );
			NewLocalVar( varname );
			CheckNext( (int)'=' );
			Exp1(); // initial value
			CheckNext( (int)',' );
			Exp1(); // limit
			if( TestNext( (int)',' ) )
				Exp1(); // optional step
			else // default step = 1
			{
				Coder.Int( fs, fs.FreeReg, 1 );
				Coder.ReserveRegs( fs, 1 );
			}
			AdjustLocalVars( 3 ); // control variables
			ForBody( bas, line, 1, false );
		}

		private void ForList( string indexname )
		{
			/* forlist -> NAME {,NAME} IN explist forbody */
			var fs = CurFunc;
			var e = new ExpDesc();
			int nvars = 5; // gen, state, control, toclose, 'indexname'
			int line;
			int bas = fs.FreeReg;
			/* create control variables */
			NewLocalVar( "(for state)" );
			NewLocalVar( "(for state)" );
			NewLocalVar( "(for state)" );
			NewLocalVar( "(for state)" );
			/* create declared variables */
			NewLocalVar( indexname );
			while( TestNext( (int)',' ) )
			{
				NewLocalVar( StrCheckName() );
				nvars++;
			}
			CheckNext( (int)TK.IN );
			line = Lexer.LineNumber;
			AdjustAssign( 4, ExpList( e ), e );
			AdjustLocalVars( 4 ); // control variables
			MarkToBeClosed( fs ); // last control var. must be closed
			Coder.CheckStack( fs, 3 ); // extra space to call generator
			ForBody( bas, line, nvars - 4, true );
		}

		private void ForStat( int line )
		{
			/* forstat -> FOR (fornum | forlist) END */
			var fs = CurFunc;
			string varname;
			var bl = new BlockCnt();
			EnterBlock( fs, bl, true ); // scope for loop and control variables
			Lexer.Next(); // skip 'for'
			varname = StrCheckName(); // first variable name
			switch( Lexer.Token.TokenType )
			{
				case (int)'=': ForNum( varname, line ); break;
				case (int)',': case (int)TK.IN: ForList( varname ); break;
				default: Lexer.SyntaxError( "'=' or 'in' expected" ); break;
			}
			CheckMatch( (int)TK.END, (int)TK.FOR, line );
			LeaveBlock( fs ); // loop scope ('break' jumps to this point)
		}

		private void TestThenBlock( ref int escapelist )
		{
			/* test_then_block -> [IF | ELSEIF] cond THEN block */
			var bl = new BlockCnt();
			var fs = CurFunc;
			var v = new ExpDesc();
			int jf; // instruction to skip 'then' code (if condition is false)
			Lexer.Next(); // skip IF or ELSEIF
			Expr( v ); // read condition
			CheckNext( (int)TK.THEN );
			if( Lexer.Token.TokenType == (int)TK.BREAK ) // 'if x then break' ?
			{
				int line = Lexer.LineNumber;
				Coder.GoIfFalse( fs, v ); // will jump if condition is true
				Lexer.Next(); // skip 'break'
				EnterBlock( fs, bl, false ); // must enter block before 'goto'
				NewGotoEntry( "break", line, v.ExitTrue );
				while( TestNext( (int)';' ) ) { } // skip semicolons
				if( BlockFollow( false ) ) // jump is the entire block?
				{
					LeaveBlock( fs );
					return; // and that is it
				}
				else // must skip over 'then' part if condition is false
					jf = Coder.Jump( fs );
			}
			else // regular case (not a break)
			{
				Coder.GoIfTrue( fs, v ); // skip over block if condition is false
				EnterBlock( fs, bl, false );
				jf = v.ExitFalse;
			}
			StatList(); // 'then' part
			LeaveBlock( fs );
			if( Lexer.Token.TokenType == (int)TK.ELSE ||
				Lexer.Token.TokenType == (int)TK.ELSEIF ) // followed by 'else'/'elseif'?
				Coder.Concat( fs, ref escapelist, Coder.Jump( fs ) ); // must jump over it
			Coder.PatchToHere( fs, jf );
		}

		private void IfStat( int line )
		{
			/* ifstat -> IF cond THEN block {ELSEIF cond THEN block} [ELSE block] END */
			var fs = CurFunc;
			int escapelist = Coder.NO_JUMP; // exit list for finished parts
			TestThenBlock( ref escapelist ); // IF cond THEN block
			while( Lexer.Token.TokenType == (int)TK.ELSEIF )
				TestThenBlock( ref escapelist ); // ELSEIF cond THEN block
			if( TestNext( (int)TK.ELSE ) )
				Block(); // 'else' part
			CheckMatch( (int)TK.END, (int)TK.IF, line );
			Coder.PatchToHere( fs, escapelist ); // patch escape list to 'if' end
		}

		private void LocalFunc()
		{
			var b = new ExpDesc();
			var fs = CurFunc;
			int fvar = fs.NActVar; // function's variable index
			NewLocalVar( StrCheckName() ); // new local variable
			AdjustLocalVars( 1 ); // enter its scope
			Body( b, false, Lexer.LineNumber ); // function created in next register
			/* debug information will only see the variable after this point! */
			LocalDebugInfo( fs, fvar ).StartPc = fs.Pc;
		}

		private byte GetLocalAttribute()
		{
			/* ATTRIB -> ['<' Name '>'] */
			if( TestNext( (int)'<' ) )
			{
				string attr = StrCheckName();
				CheckNext( (int)'>' );
				if( attr == "const" )
					return VarDesc.RDKCONST; // read-only variable
				else if( attr == "close" )
					return VarDesc.RDKTOCLOSE; // to-be-closed variable
				else
					Lexer.SemanticError( string.Format( "unknown attribute '{0}'", attr ) );
			}
			return VarDesc.VDKREG; // regular variable
		}

		private void CheckToClose( FuncState fs, int level )
		{
			if( level != -1 ) // is there a to-be-closed variable?
			{
				MarkToBeClosed( fs );
				Coder.CodeABC( fs, OpCode.OP_TBC, fs.RegLevel( level ), 0, 0 );
			}
		}

		private void LocalStat()
		{
			/* stat -> LOCAL NAME ATTRIB { ',' NAME ATTRIB } ['=' explist] */
			var fs = CurFunc;
			int toclose = -1; // index of to-be-closed variable (if any)
			VarDesc var; // last variable
			int vidx; // index of last variable
			byte kind; // kind of last variable
			int nvars = 0;
			int nexps;
			var e = new ExpDesc();
			do {
				vidx = NewLocalVar( StrCheckName() );
				kind = GetLocalAttribute();
				fs.GetLocalVarDesc( vidx ).Kind = kind;
				if( kind == VarDesc.RDKTOCLOSE ) // to-be-closed?
				{
					if( toclose != -1 ) // one already present?
						Lexer.SemanticError( "multiple to-be-closed variables in local list" );
					toclose = fs.NActVar + nvars;
				}
				nvars++;
			} while( TestNext( (int)',' ) );
			if( TestNext( (int)'=' ) )
				nexps = ExpList( e );
			else
			{
				e.Kind = ExpKind.VVOID;
				nexps = 0;
			}
			var = fs.GetLocalVarDesc( vidx ); // get last variable
			if( nvars == nexps && // no adjustments?
				var.Kind == VarDesc.RDKCONST && // last variable is const?
				Coder.Exp2Const( fs, e, ref var.K ) ) // compile-time constant?
			{
				var.Kind = VarDesc.RDKCTC; // variable is a compile-time constant
				AdjustLocalVars( nvars - 1 ); // exclude last variable
				fs.NActVar++; // but count it
			}
			else
			{
				AdjustAssign( nvars, nexps, e );
				AdjustLocalVars( nvars );
			}
			CheckToClose( fs, toclose );
		}

		private bool FuncName( ExpDesc v )
		{
			/* funcname -> NAME {fieldsel} [':' NAME] */
			bool ismethod = false;
			SingleVar( v );
			while( Lexer.Token.TokenType == (int)'.' )
				FieldSel( v );
			if( Lexer.Token.TokenType == (int)':' )
			{
				ismethod = true;
				FieldSel( v );
			}
			return ismethod;
		}

		private void FuncStat( int line )
		{
			/* funcstat -> FUNCTION funcname body */
			bool ismethod;
			var v = new ExpDesc();
			var b = new ExpDesc();
			Lexer.Next(); // skip FUNCTION
			ismethod = FuncName( v );
			Body( b, ismethod, line );
			CheckReadonly( v );
			Coder.StoreVar( CurFunc, v, b );
			Coder.FixLine( CurFunc, line ); // definition "happens" in the first line
		}

		private void ExprStat()
		{
			/* stat -> func | assignment */
			var fs = CurFunc;
			var v = new LHSAssign();
			SuffixedExp( v.V );
			if( Lexer.Token.TokenType == (int)'=' || Lexer.Token.TokenType == (int)',' ) // stat -> assignment ?
			{
				v.Prev = null;
				RestAssign( v, 1 );
			}
			else // stat -> func
			{
				CheckCondition( v.V.Kind == ExpKind.VCALL, "syntax error" );
				var inst = fs.Proto.Code[v.V.Info];
				inst.SETARG_C( 1 ); // call statement uses no results
				fs.Proto.Code[v.V.Info] = inst;
			}
		}

		private void RetStat()
		{
			/* stat -> RETURN [explist] [';'] */
			var fs = CurFunc;
			var e = new ExpDesc();
			int nret; // number of values being returned
			int first = fs.NVarStack(); // first slot to be returned
			if( BlockFollow( true ) || Lexer.Token.TokenType == (int)';' )
				nret = 0; // return no values
			else
			{
				nret = ExpList( e ); // optional return values
				if( HasMultRet( e.Kind ) )
				{
					Coder.SetMultRet( fs, e );
					if( e.Kind == ExpKind.VCALL && nret == 1 && !fs.Block.InsideTbc ) // tail call?
					{
						var inst = fs.Proto.Code[e.Info];
						inst.SET_OPCODE( OpCode.OP_TAILCALL );
						fs.Proto.Code[e.Info] = inst;
						Utl.Assert( inst.GETARG_A() == fs.NVarStack() );
					}
					nret = LuaDef.LUA_MULTRET; // return all values
				}
				else
				{
					if( nret == 1 ) // only one single value?
						first = Coder.Exp2AnyReg( fs, e ); // can use original slot
					else // values must go to the top of the stack
					{
						Coder.Exp2NextReg( fs, e );
						Utl.Assert( nret == fs.FreeReg - first );
					}
				}
			}
			Coder.Ret( fs, first, nret );
			TestNext( (int)';' ); // skip optional semicolon
		}

		private void Statement()
		{
			int line = Lexer.LineNumber; // may be needed for error messages
			EnterLevel();
			switch( Lexer.Token.TokenType )
			{
				case (int)';': { // stat -> ';' (empty statement)
					Lexer.Next(); // skip ';'
					break;
				}
				case (int)TK.IF: { // stat -> ifstat
					IfStat( line );
					break;
				}
				case (int)TK.WHILE: { // stat -> whilestat
					WhileStat( line );
					break;
				}
				case (int)TK.DO: { // stat -> DO block END
					Lexer.Next(); // skip DO
					Block();
					CheckMatch( (int)TK.END, (int)TK.DO, line );
					break;
				}
				case (int)TK.FOR: { // stat -> forstat
					ForStat( line );
					break;
				}
				case (int)TK.REPEAT: { // stat -> repeatstat
					RepeatStat( line );
					break;
				}
				case (int)TK.FUNCTION: { // stat -> funcstat
					FuncStat( line );
					break;
				}
				case (int)TK.LOCAL: { // stat -> localstat
					Lexer.Next(); // skip LOCAL
					if( TestNext( (int)TK.FUNCTION ) ) // local function?
						LocalFunc();
					else
						LocalStat();
					break;
				}
				case (int)TK.DBCOLON: { // stat -> label
					Lexer.Next(); // skip double colon
					LabelStat( StrCheckName(), line );
					break;
				}
				case (int)TK.RETURN: { // stat -> retstat
					Lexer.Next(); // skip RETURN
					RetStat();
					break;
				}
				case (int)TK.BREAK: { // stat -> breakstat
					BreakStat();
					break;
				}
				case (int)TK.GOTO: { // stat -> 'goto' NAME
					Lexer.Next(); // skip 'goto'
					GotoStat();
					break;
				}
				default: { // stat -> func | assignment
					ExprStat();
					break;
				}
			}
			Utl.Assert( CurFunc.Proto.MaxStackSize >= CurFunc.FreeReg &&
						CurFunc.FreeReg >= CurFunc.NVarStack() );
			CurFunc.FreeReg = CurFunc.NVarStack(); // free registers
			LeaveLevel();
		}

		/* }====================================================================== */

		/*
		** compiles the main function, which is a regular vararg function with an
		** upvalue named LUA_ENV
		*/
		private void MainFunc( FuncState fs )
		{
			var bl = new BlockCnt();
			OpenFunc( fs, bl );
			SetVararg( fs, 0 ); // main function is always declared vararg
			var env = AllocUpvalue( fs ); // ...set environment upvalue
			env.InStack = true;
			env.Index = 0;
			env.Kind = VarDesc.VDKREG;
			env.Name = LuaDef.LUA_ENV;
			Lexer.Next(); // read first token
			StatList(); // parse main body
			Check( (int)TK.EOS );
			CloseFunc();
		}
	}

}
