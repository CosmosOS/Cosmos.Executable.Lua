// Part of UniLua (see THIRD-PARTY-NOTICES.txt for its license), adapted for Cosmos.
#nullable disable
#pragma warning disable CS1570, CS1587, CS1591 // UniLua documents its API on its wiki, not in XML


namespace Cosmos.Executable.Lua
{
	using StringBuilder = System.Text.StringBuilder;

	// ltablib.c of Lua 5.3: the functions read and write through
	// metamethods, so that they work on objects that mimic tables
	internal class LuaTableLib
	{
		public const string LIB_NAME = "table";

		public static int OpenLib( ILuaState lua )
		{
			NameFuncPair[] define = new NameFuncPair[]
			{
				new NameFuncPair( "concat", 	TBL_Concat 	),
				new NameFuncPair( "insert", 	TBL_Insert 	),
				new NameFuncPair( "pack", 		TBL_Pack 	),
				new NameFuncPair( "unpack", 	TBL_Unpack 	),
				new NameFuncPair( "remove", 	TBL_Remove 	),
				new NameFuncPair( "move", 		TBL_Move 	),
				new NameFuncPair( "sort", 		TBL_Sort 	),
			};

			lua.L_NewLib( define );
			return 1;
		}

		// operations that an object must define to mimic a table
		private const int TAB_R = 1; // read
		private const int TAB_W = 2; // write
		private const int TAB_L = 4; // length
		private const int TAB_RW = TAB_R | TAB_W; // read/write

		private static bool CheckField( ILuaState lua, string key, int n )
		{
			lua.PushString( key );
			return lua.RawGet( -n ) != LuaType.LUA_TNIL;
		}

		// checktab: 'arg' is a table, or has the metamethods to behave as one
		private static void CheckTab( ILuaState lua, int arg, int what )
		{
			if( lua.Type( arg ) != LuaType.LUA_TTABLE ) // is it not a table?
			{
				int n = 1; // number of elements to pop
				if( lua.GetMetaTable( arg ) && // must have metatable
					((what & TAB_R) == 0 || CheckField( lua, "__index", ++n )) &&
					((what & TAB_W) == 0 || CheckField( lua, "__newindex", ++n )) &&
					((what & TAB_L) == 0 || CheckField( lua, "__len", ++n )) )
				{
					lua.Pop( n ); // pop metatable and tested metamethods
				}
				else
					lua.L_CheckType( arg, LuaType.LUA_TTABLE ); // force an error
			}
		}

		private static long AuxGetN( ILuaState lua, int n, int w )
		{
			CheckTab( lua, n, w | TAB_L );
			return lua.L_Len( n );
		}

		private static int TBL_Insert( ILuaState lua )
		{
			long e = AuxGetN( lua, 1, TAB_RW ) + 1; // first empty element
			long pos; // where to insert new element
			switch( lua.GetTop() )
			{
				case 2: // called with only 2 arguments
					pos = e; // insert new element at the end
					break;
				case 3:
				{
					pos = lua.L_CheckInteger( 2 ); // 2nd argument is the position
					lua.L_ArgCheck( 1 <= pos && pos <= e, 2, "position out of bounds" );
					for( long i = e; i > pos; i-- ) // move up elements
					{
						lua.GetI( 1, i - 1 );
						lua.SetI( 1, i ); // t[i] = t[i - 1]
					}
					break;
				}
				default:
					return lua.L_Error( "wrong number of arguments to 'insert'" );
			}
			lua.SetI( 1, pos ); // t[pos] = v
			return 0;
		}

		private static int TBL_Remove( ILuaState lua )
		{
			long size = AuxGetN( lua, 1, TAB_RW );
			long pos = lua.L_OptInteger( 2, size );
			if( pos != size ) // validate 'pos' if given
				lua.L_ArgCheck( 1 <= pos && pos <= size + 1, 1, "position out of bounds" );
			lua.GetI( 1, pos ); // result = t[pos]
			for( ; pos < size; pos++ )
			{
				lua.GetI( 1, pos + 1 );
				lua.SetI( 1, pos ); // t[pos] = t[pos + 1]
			}
			lua.PushNil();
			lua.SetI( 1, pos ); // t[pos] = nil
			return 1;
		}

		// tmove: copies a1[f..e] into tt[t..], in increasing order when possible
		private static int TBL_Move( ILuaState lua )
		{
			long f = lua.L_CheckInteger( 2 );
			long e = lua.L_CheckInteger( 3 );
			long t = lua.L_CheckInteger( 4 );
			int tt = !lua.IsNoneOrNil( 5 ) ? 5 : 1; // destination table
			CheckTab( lua, 1, TAB_R );
			CheckTab( lua, tt, TAB_W );
			if( e >= f ) // otherwise, nothing to move
			{
				lua.L_ArgCheck( f > 0 || e < LuaConf.LUA_MAXINTEGER + f, 3,
					"too many elements to move" );
				long n = e - f + 1; // number of elements to move
				lua.L_ArgCheck( t <= LuaConf.LUA_MAXINTEGER - n + 1, 4,
					"destination wrap around" );
				if( t > e || t <= f || (tt != 1 && !lua.Compare( 1, tt, LuaEq.LUA_OPEQ )) )
				{
					for( long i = 0; i < n; i++ )
					{
						lua.GetI( 1, f + i );
						lua.SetI( tt, t + i );
					}
				}
				else
				{
					for( long i = n - 1; i >= 0; i-- )
					{
						lua.GetI( 1, f + i );
						lua.SetI( tt, t + i );
					}
				}
			}
			lua.PushValue( tt ); // return destination table
			return 1;
		}

		private static void AddField( ILuaState lua, StringBuilder sb, long i )
		{
			lua.GetI( 1, i );
			if( !lua.IsString( -1 ) )
				lua.L_Error( "invalid value ({0}) at index {1} in table for 'concat'",
					lua.L_TypeName( -1 ), i );
			sb.Append( lua.ToString( -1 ) );
			lua.Pop( 1 );
		}

		private static int TBL_Concat( ILuaState lua )
		{
			long last = AuxGetN( lua, 1, TAB_R );
			string sep = lua.L_OptString( 2, "" );
			long i = lua.L_OptInteger( 3, 1 );
			last = lua.L_OptInteger( 4, last );

			var sb = new StringBuilder();
			for( ; i < last; ++i )
			{
				AddField( lua, sb, i );
				sb.Append( sep );
			}
			if( i == last ) // add last value (if interval was not empty)
				AddField( lua, sb, i );
			lua.PushString( sb.ToString() );
			return 1;
		}

		private static int TBL_Pack( ILuaState lua )
		{
			int n = lua.GetTop(); // number of elements to pack
			lua.CreateTable( n, 1 ); // create result table
			lua.Insert( 1 ); // put it at index 1
			for( int i = n; i >= 1; i-- ) // assign elements
				lua.SetI( 1, i );
			lua.PushInteger( n );
			lua.SetField( 1, "n" ); // t.n = number of elements
			return 1; // return table
		}

		private static int TBL_Unpack( ILuaState lua )
		{
			long i = lua.L_OptInteger( 2, 1 );
			long e = lua.IsNoneOrNil( 3 ) ? lua.L_Len( 1 ) : lua.L_CheckInteger( 3 );
			if( i > e ) return 0; // empty range
			ulong n = unchecked( (ulong)e - (ulong)i ); // number of elements minus 1 (avoid overflows)
			if( n >= (ulong)int.MaxValue || !lua.CheckStack( (int)(++n) ) )
				return lua.L_Error( "too many results to unpack" );
			for( ; i < e; i++ ) // push arg[i..e - 1] (to avoid overflows)
				lua.GetI( 1, i );
			lua.GetI( 1, e ); // push last element
			return (int)n;
		}

		// quicksort (based on 'Algorithms in MODULA-3', Robert Sedgewick;
		// Addison-Wesley, 1993.)

		// arrays larger than 'RANLIMIT' may use randomized pivots
		private const uint RANLIMIT = 100u;

		private static uint RandomizePivot()
		{
			return unchecked( (uint)System.Environment.TickCount + (uint)System.DateTime.UtcNow.Ticks );
		}

		private static void Set2( ILuaState lua, uint i, uint j )
		{
			lua.SetI( 1, i );
			lua.SetI( 1, j );
		}

		// whether the value at 'a' is less than the value at 'b', by the
		// order of the sort
		private static bool SortComp( ILuaState lua, int a, int b )
		{
			if( lua.IsNil( 2 ) ) // no function?
				return lua.Compare( a, b, LuaEq.LUA_OPLT ); // a < b
			// function
			lua.PushValue( 2 ); // push function
			lua.PushValue( a - 1 ); // -1 to compensate function
			lua.PushValue( b - 2 ); // -2 to compensate function and 'a'
			lua.Call( 2, 1 ); // call function
			bool res = lua.ToBoolean( -1 ); // get result
			lua.Pop( 1 ); // pop result
			return res;
		}

		// partition: pivot P is at the top of the stack;
		// precondition: a[lo] <= P == a[up-1] <= a[up]
		private static uint Partition( ILuaState lua, uint lo, uint up )
		{
			uint i = lo; // will be incremented before first use
			uint j = up - 1; // will be decremented before first use
			// loop invariant: a[lo .. i] <= P <= a[j .. up]
			for(;;)
			{
				// next loop: repeat ++i while a[i] < P
				while( true )
				{
					lua.GetI( 1, ++i );
					if( !SortComp( lua, -1, -2 ) )
						break;
					if( i == up - 1 ) // a[i] < P  but a[up - 1] == P  ??
						lua.L_Error( "invalid order function for sorting" );
					lua.Pop( 1 ); // remove a[i]
				}
				// after the loop, a[i] >= P and a[lo .. i - 1] < P
				// next loop: repeat --j while P < a[j]
				while( true )
				{
					lua.GetI( 1, --j );
					if( !SortComp( lua, -3, -1 ) )
						break;
					if( j < i ) // j < i  but  a[j] > P ??
						lua.L_Error( "invalid order function for sorting" );
					lua.Pop( 1 ); // remove a[j]
				}
				// after the loop, a[j] <= P and a[j + 1 .. up] >= P
				if( j < i ) // no elements out of place?
				{
					// a[lo .. i - 1] <= P <= a[j + 1 .. i .. up]
					lua.Pop( 1 ); // pop a[j]
					// swap pivot (a[up - 1]) with a[i] to satisfy pos-condition
					Set2( lua, up - 1, i );
					return i;
				}
				// otherwise, swap a[i] - a[j] to restore invariant and repeat
				Set2( lua, i, j );
			}
		}

		// an element in the middle (2nd-3th quarters) of [lo,up],
		// "randomized" by 'rnd'
		private static uint ChoosePivot( uint lo, uint up, uint rnd )
		{
			uint r4 = (up - lo) / 4; // range/4
			return rnd % (r4 * 2) + (lo + r4);
		}

		private static void AuxSort( ILuaState lua, uint lo, uint up, uint rnd )
		{
			while( lo < up ) // loop for tail recursion
			{
				uint p; // Pivot index
				uint n; // to be used later
				// sort elements 'lo', 'p', and 'up'
				lua.GetI( 1, lo );
				lua.GetI( 1, up );
				if( SortComp( lua, -1, -2 ) ) // a[up] < a[lo]?
					Set2( lua, lo, up ); // swap a[lo] - a[up]
				else
					lua.Pop( 2 ); // remove both values
				if( up - lo == 1 ) // only 2 elements?
					return; // already sorted
				if( up - lo < RANLIMIT || rnd == 0 ) // small interval or no randomize?
					p = (lo + up) / 2; // middle element is a good pivot
				else // for larger intervals, it is worth a random pivot
					p = ChoosePivot( lo, up, rnd );
				lua.GetI( 1, p );
				lua.GetI( 1, lo );
				if( SortComp( lua, -2, -1 ) ) // a[p] < a[lo]?
					Set2( lua, p, lo ); // swap a[p] - a[lo]
				else
				{
					lua.Pop( 1 ); // remove a[lo]
					lua.GetI( 1, up );
					if( SortComp( lua, -1, -2 ) ) // a[up] < a[p]?
						Set2( lua, p, up ); // swap a[up] - a[p]
					else
						lua.Pop( 2 );
				}
				if( up - lo == 2 ) // only 3 elements?
					return; // already sorted
				lua.GetI( 1, p ); // get middle element (Pivot)
				lua.PushValue( -1 ); // push Pivot
				lua.GetI( 1, up - 1 ); // push a[up - 1]
				Set2( lua, p, up - 1 ); // swap Pivot (a[p]) with a[up - 1]
				p = Partition( lua, lo, up );
				// a[lo .. p - 1] <= a[p] == P <= a[p + 1 .. up]
				if( p - lo < up - p ) // lower interval is smaller?
				{
					AuxSort( lua, lo, p - 1, rnd ); // call recursively for lower interval
					n = p - lo; // size of smaller interval
					lo = p + 1; // tail call for [p + 1 .. up] (upper interval)
				}
				else
				{
					AuxSort( lua, p + 1, up, rnd ); // call recursively for upper interval
					n = up - p; // size of smaller interval
					up = p - 1; // tail call for [lo .. p - 1]  (lower interval)
				}
				if( (up - lo) / 128 > n ) // partition too imbalanced?
					rnd = RandomizePivot(); // try a new randomization
			}
		}

		private static int TBL_Sort( ILuaState lua )
		{
			long n = AuxGetN( lua, 1, TAB_RW );
			if( n > 1 ) // non-trivial interval?
			{
				lua.L_ArgCheck( n < int.MaxValue, 1, "array too big" );
				if( !lua.IsNoneOrNil( 2 ) ) // is there a 2nd argument?
					lua.L_CheckType( 2, LuaType.LUA_TFUNCTION ); // must be a function
				lua.SetTop( 2 ); // make sure there are two arguments
				AuxSort( lua, 1, (uint)n, 0 );
			}
			return 0;
		}
	}

}
