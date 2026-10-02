// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// The <c>io</c> library of Lua 5.5 on <see cref="System.IO"/>, where UniLua
/// had stubs. Files are userdata with the <c>FILE*</c> metatable; the
/// standard ones are the host's console, so in a console session they are
/// the session's terminal. Relative names start from the state's working
/// directory.
/// </summary>
/// <remarks>
/// Not here: <c>io.popen</c>, which needs processes.
/// </remarks>
internal static class LuaIOLib
{
    public const string LIB_NAME = "io";

    /// <summary>The registry name of the metatable of files, as in the reference implementation.</summary>
    private const string FileType = "FILE*";

    /// <summary>The registry keys of the default input and output files.</summary>
    private const string InputKey = "_IO_input";
    private const string OutputKey = "_IO_output";

    /// <summary>The most numbers <c>read("*n")</c> takes in, as the reference implementation's buffer.</summary>
    private const int MaxNumberLength = 200;

    /// <summary>MAXARGLINE: how many formats lines() takes at most.</summary>
    private const int MaxLinesFormats = 250;

    public static int OpenLib(ILuaState lua)
    {
        NameFuncPair[] library =
        [
            new("close", IO_Close),
            new("flush", IO_Flush),
            new("input", IO_Input),
            new("lines", IO_Lines),
            new("open", IO_Open),
            new("output", IO_Output),
            new("popen", IO_Popen),
            new("read", IO_Read),
            new("tmpfile", IO_Tmpfile),
            new("type", IO_Type),
            new("write", IO_Write),
        ];
        lua.L_NewLib(library);
        CreateMeta(lua);

        LuaHost host = LuaHost.Of(lua);
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Input), InputKey, "stdin");
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Output), OutputKey, "stdout");
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Error), null, "stderr");
        return 1;
    }

    /// <summary>
    /// createmeta: the metatable of files, with their metamethods, and an
    /// __index that holds their methods.
    /// </summary>
    private static void CreateMeta(ILuaState lua)
    {
        NameFuncPair[] methods =
        [
            new("read", F_Read),
            new("write", F_Write),
            new("lines", F_Lines),
            new("flush", F_Flush),
            new("seek", F_Seek),
            new("close", F_Close),
            new("setvbuf", F_Setvbuf),
        ];
        NameFuncPair[] metamethods =
        [
            new("__gc", F_Gc),
            new("__close", F_Gc),
            new("__tostring", F_ToString),
        ];
        lua.L_NewMetaTable(FileType); // metatable for file handles
        lua.L_SetFuncs(metamethods, 0); // add metamethods to new metatable
        lua.L_NewLibTable(methods); // create method table
        lua.L_SetFuncs(methods, 0); // add file methods to method table
        lua.SetField(-2, "__index"); // metatable.__index = method table
        lua.Pop(1); // pop metatable
    }

    private static void CreateStandardFile(ILuaState lua, LuaFileHandle file, string? registryKey, string name)
    {
        PushFile(lua, file);
        if (registryKey is not null)
        {
            lua.PushValue(-1);
            lua.SetField(LuaDef.LUA_REGISTRYINDEX, registryKey);
        }

        lua.SetField(-2, name);
    }

    private static void PushFile(ILuaState lua, LuaFileHandle file)
    {
        lua.NewUserDataUV(file, 0);
        lua.L_SetMetaTable(FileType);
    }

    /// <summary>The open file at <paramref name="index"/>; a Lua error for anything else.</summary>
    private static LuaFileHandle ToFile(ILuaState lua, int index)
    {
        LuaFileHandle file = (LuaFileHandle)lua.L_CheckUData(index, FileType);
        if (file.IsClosed)
        {
            lua.L_Error("attempt to use a closed file");
        }

        return file;
    }

    /// <summary>
    /// The result of an operation that can fail, as luaL_fileresult gives it:
    /// <c>true</c>, or <c>nil</c>, a message such as
    /// <c>name: No such file or directory</c>, and the errno.
    /// </summary>
    internal static int PushResult(ILuaState lua, Exception? error, string? fileName)
    {
        if (error is null)
        {
            lua.PushBoolean(true);
            return 1;
        }

        lua.PushNil();
        lua.PushString(fileName is null ? Describe(error) : fileName + ": " + Describe(error));
        lua.PushInteger(ErrorNumber(error));
        return 3;
    }

    /// <summary>The C library's words for the common failures, which scripts may test for; the exception's otherwise.</summary>
    internal static string Describe(Exception error)
    {
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => "No such file or directory",
            UnauthorizedAccessException => "Permission denied",
            NotSupportedException => "Bad file descriptor", // a read from a file not open for reading, or a write
            _ => LuaText.Encode(error.Message),
        };
    }

    /// <summary>The errno of a failure, for the common ones: ENOENT, EACCES, EBADF, or else EIO.</summary>
    private static int ErrorNumber(Exception error)
    {
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => 2,
            UnauthorizedAccessException => 13,
            NotSupportedException => 9,
            _ => 5,
        };
    }

    // ---- io functions

    private static int IO_Open(ILuaState lua)
    {
        string fileName = lua.L_CheckString(1);
        string mode = lua.L_OptString(2, "r");
        if (!TryParseMode(mode, out FileMode fileMode, out FileAccess access, out bool append, out bool binary))
        {
            return lua.L_ArgError(2, "invalid mode");
        }

        LuaFileHandle? file = Open(lua, fileName, fileMode, access, append, binary, out Exception? error);
        if (file is null)
        {
            return PushResult(lua, error, fileName);
        }

        PushFile(lua, file);
        return 1;
    }

    private static LuaFileHandle? Open(ILuaState lua, string fileName, FileMode fileMode, FileAccess access, bool append, bool binary, out Exception? error)
    {
        try
        {
            // no buffer of the stream's own: the file keeps one (see LuaFileHandle),
            // which forgets what a failed flush could not write, as C's does
            FileStream stream = new(LuaHost.Of(lua).ResolvePath(fileName), fileMode, access, FileShare.ReadWrite, bufferSize: 1);
            error = null;
            return new LuaFileHandle(LuaHost.Of(lua), stream, append);
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            error = e;
            return null;
        }
    }

    /// <summary>Opens a file for io.input, io.output and io.lines, which raise an error when they cannot.</summary>
    private static LuaFileHandle OpenOrRaise(ILuaState lua, string fileName, string mode)
    {
        TryParseMode(mode, out FileMode fileMode, out FileAccess access, out bool append, out bool binary);
        LuaFileHandle? file = Open(lua, fileName, fileMode, access, append, binary, out Exception? error);
        if (file is null)
        {
            lua.L_Error("cannot open file '{0}' ({1})", fileName, Describe(error!));
        }

        return file!;
    }

    /// <summary>
    /// Parses an <c>fopen</c> mode as l_checkmode checks it,
    /// <c>[rwa]%+?b*</c>: <c>r</c>, <c>w</c> or <c>a</c>, then an optional
    /// <c>+</c>, then any number of <c>b</c>; a C string, which ends at its
    /// first zero.
    /// </summary>
    private static bool TryParseMode(string mode, out FileMode fileMode, out FileAccess access, out bool append, out bool binary)
    {
        fileMode = FileMode.Open;
        access = FileAccess.Read;
        append = false;
        binary = false;
        int end = mode.IndexOf('\0');
        if (end >= 0)
        {
            mode = mode[..end];
        }

        if (mode.Length == 0)
        {
            return false;
        }

        int i = 1;
        bool update = i < mode.Length && mode[i] == '+';
        if (update)
        {
            i++; // skip if char is '+'
        }

        binary = i < mode.Length;
        for (; i < mode.Length; i++)
        {
            if (mode[i] != 'b') // check extensions
            {
                return false;
            }
        }

        switch (mode[0])
        {
            case 'r':
                fileMode = FileMode.Open;
                access = update ? FileAccess.ReadWrite : FileAccess.Read;
                return true;
            case 'w':
                fileMode = FileMode.Create;
                access = update ? FileAccess.ReadWrite : FileAccess.Write;
                return true;
            case 'a':
                // Every write goes to the end; a+ also reads from anywhere
                fileMode = FileMode.OpenOrCreate;
                access = update ? FileAccess.ReadWrite : FileAccess.Write;
                append = true;
                return true;
            default:
                return false;
        }
    }

    private static int IO_Close(ILuaState lua)
    {
        if (lua.IsNone(1))
        {
            lua.GetField(LuaDef.LUA_REGISTRYINDEX, OutputKey);
        }

        return F_Close(lua);
    }

    private static int IO_Flush(ILuaState lua)
    {
        LuaFileHandle file = DefaultFile(lua, OutputKey);
        return PushResult(lua, Try(file.Flush), null);
    }

    private static int IO_Input(ILuaState lua)
    {
        return DefaultFileAccessor(lua, InputKey, "r");
    }

    private static int IO_Output(ILuaState lua)
    {
        return DefaultFileAccessor(lua, OutputKey, "w");
    }

    /// <summary>io.input and io.output: set the default file from a name or a file, and return it.</summary>
    private static int DefaultFileAccessor(ILuaState lua, string key, string mode)
    {
        if (!lua.IsNoneOrNil(1))
        {
            string? fileName = lua.ToString(1); // a string, or a number as one
            if (fileName is not null)
            {
                PushFile(lua, OpenOrRaise(lua, fileName, mode));
            }
            else
            {
                ToFile(lua, 1); // check that it is a valid file
                lua.PushValue(1);
            }

            lua.SetField(LuaDef.LUA_REGISTRYINDEX, key);
        }

        lua.GetField(LuaDef.LUA_REGISTRYINDEX, key);
        return 1;
    }

    /// <summary>getiofile: the default input or output file, which must be open.</summary>
    private static LuaFileHandle DefaultFile(ILuaState lua, string key)
    {
        lua.GetField(LuaDef.LUA_REGISTRYINDEX, key);
        LuaFileHandle file = (LuaFileHandle)lua.ToUserData(-1);
        lua.Pop(1);
        if (file.IsClosed)
        {
            lua.L_Error("default {0} file is closed", key == InputKey ? "input" : "output");
        }

        return file;
    }

    /// <summary>
    /// The iterator of the lines of a file or of the default input. For a
    /// file it opened, it also returns the file as the fourth result, the
    /// to-be-closed variable of a generic for, which closes the file when
    /// the loop ends.
    /// </summary>
    private static int IO_Lines(ILuaState lua)
    {
        bool toClose;
        if (lua.IsNone(1))
        {
            lua.PushNil(); // at least one argument
        }

        if (lua.IsNil(1))
        {
            // no file name: the default input, which stays open
            lua.GetField(LuaDef.LUA_REGISTRYINDEX, InputKey);
            lua.Replace(1); // put it at index 1
            ToFile(lua, 1); // check that it's a valid file handle
            toClose = false;
        }
        else
        {
            // open a new file
            string fileName = lua.L_CheckString(1);
            PushFile(lua, OpenOrRaise(lua, fileName, "r"));
            lua.Replace(1); // put file at index 1
            toClose = true; // close it after iteration
        }

        AuxLines(lua, toClose); // push iteration function
        if (toClose)
        {
            lua.PushNil(); // state
            lua.PushNil(); // control
            lua.PushValue(1); // file is the to-be-closed variable (4th result)
            return 4;
        }

        return 1;
    }

    private static int IO_Read(ILuaState lua)
    {
        return Read(lua, DefaultFile(lua, InputKey), 1);
    }

    private static int IO_Write(ILuaState lua)
    {
        LuaFileHandle file = DefaultFile(lua, OutputKey);
        int results = Write(lua, file, 1);
        if (results == 1)
        {
            lua.Pop(1);
            lua.GetField(LuaDef.LUA_REGISTRYINDEX, OutputKey); // io.write returns the file
        }

        return results;
    }

    /// <summary>io.popen, as the reference implementation is where there are no processes.</summary>
    private static int IO_Popen(ILuaState lua)
    {
        lua.L_CheckString(1);
        string mode = lua.L_OptString(2, "r");
        // l_checkmodep: only "r" or "w" (a C string, which ends at its first zero)
        bool valid = mode.Length > 0 && (mode[0] == 'r' || mode[0] == 'w') && (mode.Length == 1 || mode[1] == '\0');
        lua.L_ArgCheck(valid, 2, "invalid mode");
        return lua.L_Error("'popen' not supported");
    }

    private static int IO_Tmpfile(ILuaState lua)
    {
        // A file that lives in memory, as long as the script holds it
        PushFile(lua, new LuaFileHandle(LuaHost.Of(lua), new MemoryStream(), append: false));
        return 1;
    }

    private static int IO_Type(ILuaState lua)
    {
        lua.L_CheckAny(1);
        if (lua.L_TestUData(1, FileType) is not LuaFileHandle file)
        {
            lua.PushNil();
        }
        else
        {
            lua.PushString(file.IsClosed ? "closed file" : "file");
        }

        return 1;
    }

    // ---- file methods

    private static int F_Close(ILuaState lua)
    {
        LuaFileHandle file = ToFile(lua, 1);
        if (file.IsStandard)
        {
            lua.PushNil();
            lua.PushString("cannot close standard file");
            return 2;
        }

        return PushResult(lua, Try(file.Close), null);
    }

    private static int F_Flush(ILuaState lua)
    {
        return PushResult(lua, Try(ToFile(lua, 1).Flush), null);
    }

    private static int F_Lines(ILuaState lua)
    {
        ToFile(lua, 1); // check that it's a valid file handle
        AuxLines(lua, false);
        return 1;
    }

    private static int F_Read(ILuaState lua)
    {
        return Read(lua, ToFile(lua, 1), 2);
    }

    private static int F_Write(ILuaState lua)
    {
        LuaFileHandle file = ToFile(lua, 1);
        int results = Write(lua, file, 2);
        if (results == 1)
        {
            lua.Pop(1);
            lua.PushValue(1); // file:write returns the file
        }

        return results;
    }

    private static int F_Seek(ILuaState lua)
    {
        LuaFileHandle file = ToFile(lua, 1);
        string whence = lua.L_OptString(2, "cur");
        long offset = lua.L_OptInteger(3, 0);
        SeekOrigin origin;
        switch (whence)
        {
            case "set":
                origin = SeekOrigin.Begin;
                break;
            case "cur":
                origin = SeekOrigin.Current;
                break;
            case "end":
                origin = SeekOrigin.End;
                break;
            default:
                return lua.L_ArgError(2, "invalid option '" + whence + "'");
        }

        try
        {
            lua.PushInteger(file.Seek(origin, offset));
            return 1;
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return PushResult(lua, e, null);
        }
    }

    private static int F_Setvbuf(ILuaState lua)
    {
        LuaFileHandle file = ToFile(lua, 1);
        LuaFileHandle.BufferMode mode;
        string option = lua.L_CheckString(2);
        switch (option)
        {
            case "no":
                mode = LuaFileHandle.BufferMode.No;
                break;
            case "full":
                mode = LuaFileHandle.BufferMode.Full;
                break;
            case "line":
                mode = LuaFileHandle.BufferMode.Line;
                break;
            default:
                return lua.L_ArgError(2, "invalid option '" + option + "'");
        }

        long size = lua.L_OptInteger(3, LuaFileHandle.DefaultBufferSize);
        try
        {
            file.SetBuffering(mode, (int)Math.Clamp(size, 1, int.MaxValue));
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return PushResult(lua, e, null);
        }

        lua.PushBoolean(true);
        return 1;
    }

    /// <summary>
    /// f_gc, which is also __close: the collector calls __gc on a file a
    /// script lost, and the end of the scope of a to-be-closed variable
    /// calls __close.
    /// </summary>
    private static int F_Gc(ILuaState lua)
    {
        LuaFileHandle file = (LuaFileHandle)lua.L_CheckUData(1, FileType);
        if (!file.IsClosed && !file.IsStandard)
        {
            Try(file.Close);
        }

        return 0;
    }

    private static int F_ToString(ILuaState lua)
    {
        LuaFileHandle file = (LuaFileHandle)lua.L_CheckUData(1, FileType);
        lua.PushString(file.IsClosed
            ? "file (closed)"
            : "file (0x" + RuntimeHelpers.GetHashCode(file).ToString("x8") + ")");
        return 1;
    }

    // ---- reading and writing

    /// <summary>
    /// aux_lines: the iteration function of lines(), a closure over
    /// <see cref="IO_ReadLine"/> with these upvalues: the file being read
    /// (at index 1), the number of formats, whether to close the file at
    /// its end, and the formats (the rest of the stack).
    /// </summary>
    private static void AuxLines(ILuaState lua, bool toClose)
    {
        int n = lua.GetTop() - 1; // number of arguments to read
        lua.L_ArgCheck(n <= MaxLinesFormats, MaxLinesFormats + 2, "too many arguments");
        lua.PushValue(1); // file
        lua.PushInteger(n); // number of arguments to read
        lua.PushBoolean(toClose); // close/not close file when finished
        lua.Rotate(2, 3); // move the three values to their positions
        lua.PushCSharpClosure(IO_ReadLine, 3 + n);
    }

    /// <summary>io_readline: the iteration function for lines().</summary>
    private static int IO_ReadLine(ILuaState lua)
    {
        LuaFileHandle file = (LuaFileHandle)lua.ToUserData(lua.UpvalueIndex(1));
        int n = (int)lua.ToInteger(lua.UpvalueIndex(2));
        if (file.IsClosed) // file is already closed?
        {
            return lua.L_Error("file is already closed");
        }

        lua.SetTop(1);
        lua.L_CheckStack(n, "too many arguments");
        for (int i = 1; i <= n; i++)
        {
            lua.PushValue(lua.UpvalueIndex(3 + i)); // push arguments to 'g_read'
        }

        n = Read(lua, file, 2); // 'n' is number of results
        if (lua.ToBoolean(-n)) // read at least one value?
        {
            return n; // return them
        }

        // first result is false: EOF or error
        if (n > 1)
        {
            // is there error information? 2nd result is error message
            return lua.L_Error("{0}", lua.ToString(-n + 1));
        }

        if (lua.ToBoolean(lua.UpvalueIndex(3))) // generator created file?
        {
            Try(file.Close); // close it (aux_close, whose results are dropped)
        }

        return 0;
    }

    /// <summary>Reads what the formats from <paramref name="first"/> on ask for; the first that fails gives nil and stops.</summary>
    private static int Read(ILuaState lua, LuaFileHandle file, int first)
    {
        int top = lua.GetTop();
        try
        {
            return ReadFormats(lua, file, first);
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            // A file not open for reading, or a disk that failed
            lua.SetTop(top);
            return PushResult(lua, e, null);
        }
    }

    private static int ReadFormats(ILuaState lua, LuaFileHandle file, int first)
    {
        int last = lua.GetTop();
        if (last < first)
        {
            // no format: a line
            return ReadLine(lua, file, keepNewline: false) ? 1 : PushNil(lua);
        }

        lua.L_CheckStack(last - first + LuaDef.LUA_MINSTACK, "too many arguments");
        int n = first;
        for (; n <= last; n++)
        {
            bool success;
            if (lua.Type(n) == LuaType.LUA_TNUMBER)
            {
                success = ReadCount(lua, file, lua.L_CheckInteger(n));
            }
            else
            {
                string format = lua.L_CheckString(n);
                int p = format.Length > 0 && format[0] == '*' ? 1 : 0; // skip optional '*' (for compatibility)
                char kind = p < format.Length ? format[p] : '\0';
                switch (kind)
                {
                    case 'n':
                        success = ReadNumber(lua, file);
                        break;
                    case 'l':
                        success = ReadLine(lua, file, keepNewline: false);
                        break;
                    case 'L':
                        success = ReadLine(lua, file, keepNewline: true);
                        break;
                    case 'a':
                        ReadAll(lua, file);
                        success = true;
                        break;
                    default:
                        return lua.L_ArgError(n, "invalid format");
                }
            }

            if (!success)
            {
                lua.Pop(1);
                lua.PushNil();
                n++;
                break;
            }
        }

        return n - first;
    }

    private static int PushNil(ILuaState lua)
    {
        lua.PushNil();
        return 1;
    }

    private static bool ReadLine(ILuaState lua, LuaFileHandle file, bool keepNewline)
    {
        StringBuilder line = new();
        int c;
        while ((c = file.ReadChar()) != -1 && c != '\n')
        {
            line.Append((char)c);
        }

        if (c == '\n' && keepNewline)
        {
            line.Append('\n');
        }

        lua.PushString(line.ToString());
        return c == '\n' || line.Length > 0;
    }

    private static void ReadAll(ILuaState lua, LuaFileHandle file)
    {
        StringBuilder text = new();
        int c;
        while ((c = file.ReadChar()) != -1)
        {
            text.Append((char)c);
        }

        lua.PushString(text.ToString());
    }

    private static bool ReadCount(ILuaState lua, LuaFileHandle file, long count)
    {
        StringBuilder text = new();
        if (count == 0)
        {
            // read(0) tests for the end of the file
            lua.PushString(string.Empty);
            return file.PeekByte() != -1;
        }

        int c;
        while (text.Length < count && (c = file.ReadChar()) != -1)
        {
            text.Append((char)c);
        }

        lua.PushString(text.ToString());
        return text.Length > 0;
    }

    /// <summary>
    /// read_number: reads what follows the lexer's rules for a numeral, at
    /// most 200 characters, and converts it as tonumber does; a numeral
    /// that is not valid reads as a failure.
    /// </summary>
    private static bool ReadNumber(ILuaState lua, LuaFileHandle file)
    {
        NumeralReader rn = new(file);
        int count = 0;
        bool hex = false;
        do
        {
            rn.Current = file.ReadChar();
        }
        while (Utl.IsSpace(rn.Current)); // skip spaces

        rn.Test2("-+"); // optional signal
        if (rn.Test2("00"))
        {
            if (rn.Test2("xX"))
            {
                hex = true; // numeral is hexadecimal
            }
            else
            {
                count = 1; // count initial '0' as a valid digit
            }
        }

        count += rn.ReadDigits(hex); // integral part
        if (rn.Test2(".."))
        {
            count += rn.ReadDigits(hex); // fractional part
        }

        if (count > 0 && rn.Test2(hex ? "pP" : "eE")) // exponent mark?
        {
            rn.Test2("-+"); // exponent signal
            rn.ReadDigits(false); // exponent digits
        }

        if (rn.Current != -1)
        {
            file.Unread(rn.Current); // unread look-ahead char
        }

        string numeral = rn.Overflowed ? string.Empty : rn.Buffer.ToString();
        if (lua.StringToNumber(numeral) != 0)
        {
            return true; // ok
        }

        lua.PushNil(); // "result" to be removed
        return false; // read fails
    }

    /// <summary>The state of <see cref="ReadNumber"/>: the characters read, and the one looked at.</summary>
    private sealed class NumeralReader(LuaFileHandle file)
    {
        public StringBuilder Buffer { get; } = new();

        public int Current { get; set; }

        public bool Overflowed { get; private set; }

        /// <summary>nextc: keeps the current character, and reads the next one.</summary>
        public bool Next()
        {
            if (Buffer.Length >= MaxNumberLength) // buffer overflow?
            {
                Overflowed = true; // invalidate result
                return false; // fail
            }

            Buffer.Append((char)Current); // save current char
            Current = file.ReadChar(); // read next one
            return true;
        }

        public bool Test2(string set)
        {
            return (Current == set[0] || Current == set[1]) && Next();
        }

        public int ReadDigits(bool hex)
        {
            int count = 0;
            while ((hex ? Utl.IsXDigit(Current) : Utl.IsDigit(Current)) && Next())
            {
                count++;
            }

            return count;
        }
    }

    private static int Write(ILuaState lua, LuaFileHandle file, int first)
    {
        int last = lua.GetTop();
        long totalbytes = 0; // total number of bytes written
        try
        {
            for (int i = first; i <= last; i++)
            {
                // a number as lua_numbertocstring writes it: as tostring does
                string text = lua.Type(i) != LuaType.LUA_TNUMBER ? lua.L_CheckString(i)
                    : lua.IsInteger(i) ? LuaNumber.ToString(lua.ToInteger(i))
                    : LuaNumber.ToString(lua.ToNumber(i));
                file.Write(text);
                totalbytes += text.Length;
            }
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            // fail, error message, error code, and the bytes written
            int n = PushResult(lua, e, null);
            lua.PushInteger(totalbytes);
            return n + 1;
        }

        lua.PushBoolean(true);
        return 1;
    }

    /// <summary>Runs a file operation; the failure it reports, if any.</summary>
    private static Exception? Try(Action operation)
    {
        try
        {
            operation();
            return null;
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return e;
        }
    }
}
