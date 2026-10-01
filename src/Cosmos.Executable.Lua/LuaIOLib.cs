// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// The <c>io</c> library of Lua 5.2 on <see cref="System.IO"/>, where UniLua
/// had stubs. Files are userdata with the <c>FILE*</c> metatable; the
/// standard ones are the host's console, so in a console session they are
/// the session's terminal. Relative names start from the state's working
/// directory.
/// </summary>
/// <remarks>
/// Not here: <c>io.popen</c>, which needs processes, and the closing of a
/// forgotten file by the garbage collector (<c>__gc</c>), which UniLua does
/// not run: close what you open, or read it to the end with
/// <c>io.lines(name)</c>.
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

        // The metatable of files, whose __index holds their methods
        lua.L_NewMetaTable(FileType);
        lua.PushValue(-1);
        lua.SetField(-2, "__index");
        NameFuncPair[] methods =
        [
            new("close", F_Close),
            new("flush", F_Flush),
            new("lines", F_Lines),
            new("read", F_Read),
            new("seek", F_Seek),
            new("setvbuf", F_Setvbuf),
            new("write", F_Write),
            new("__gc", F_Gc),
            new("__tostring", F_ToString),
        ];
        lua.L_SetFuncs(methods, 0);
        lua.Pop(1);

        LuaHost host = LuaHost.Of(lua);
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Input), InputKey, "stdin");
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Output), OutputKey, "stdout");
        CreateStandardFile(lua, new LuaFileHandle(host, LuaFileHandle.StandardFile.Error), null, "stderr");
        return 1;
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
        lua.NewUserData(file);
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
            _ => error.Message,
        };
    }

    /// <summary>The errno of a failure, for the common ones: ENOENT, EACCES, or else EIO.</summary>
    private static int ErrorNumber(Exception error)
    {
        return error switch
        {
            FileNotFoundException or DirectoryNotFoundException => 2,
            UnauthorizedAccessException => 13,
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
            FileStream stream = new(LuaHost.Of(lua).ResolvePath(fileName), fileMode, access, FileShare.ReadWrite);
            error = null;
            return new LuaFileHandle(LuaHost.Of(lua), stream, binary, append);
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

    /// <summary>Parses an <c>fopen</c> mode: <c>r</c>, <c>w</c> or <c>a</c>, then an optional <c>+</c>, then an optional <c>b</c>.</summary>
    private static bool TryParseMode(string mode, out FileMode fileMode, out FileAccess access, out bool append, out bool binary)
    {
        fileMode = FileMode.Open;
        access = FileAccess.Read;
        append = false;
        binary = false;
        if (mode.Length == 0)
        {
            return false;
        }

        int i = 1;
        bool update = i < mode.Length && mode[i] == '+';
        if (update)
        {
            i++;
        }

        binary = i < mode.Length && mode[i] == 'b';
        if (binary)
        {
            i++;
        }

        if (i != mode.Length)
        {
            return false;
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
            if (lua.Type(1) == LuaType.LUA_TSTRING)
            {
                PushFile(lua, OpenOrRaise(lua, lua.ToString(1), mode));
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

    private static LuaFileHandle DefaultFile(ILuaState lua, string key)
    {
        lua.GetField(LuaDef.LUA_REGISTRYINDEX, key);
        LuaFileHandle file = (LuaFileHandle)lua.ToUserData(-1);
        lua.Pop(1);
        if (file.IsClosed)
        {
            lua.L_Error("standard {0} file is closed", key == InputKey ? "input" : "output");
        }

        return file;
    }

    private static int IO_Lines(ILuaState lua)
    {
        if (lua.IsNoneOrNil(1))
        {
            // Lines of the default input, which stays open
            return PushLinesIterator(lua, DefaultFile(lua, InputKey), 2, close: false);
        }

        LuaFileHandle file = OpenOrRaise(lua, lua.L_CheckString(1), "r");
        return PushLinesIterator(lua, file, 2, close: true);
    }

    private static int IO_Read(ILuaState lua)
    {
        return Read(lua, DefaultFile(lua, InputKey), 1);
    }

    private static int IO_Write(ILuaState lua)
    {
        lua.GetField(LuaDef.LUA_REGISTRYINDEX, OutputKey);
        LuaFileHandle file = (LuaFileHandle)lua.ToUserData(-1);
        lua.Pop(1);
        if (file.IsClosed)
        {
            return lua.L_Error("standard output file is closed");
        }

        int results = Write(lua, file, 1);
        if (results == 1)
        {
            lua.Pop(1);
            lua.GetField(LuaDef.LUA_REGISTRYINDEX, OutputKey); // io.write returns the file
        }

        return results;
    }

    private static int IO_Popen(ILuaState lua)
    {
        return lua.L_Error("'popen' not supported");
    }

    private static int IO_Tmpfile(ILuaState lua)
    {
        // A file that lives in memory, as long as the script holds it
        PushFile(lua, new LuaFileHandle(LuaHost.Of(lua), new MemoryStream(), binary: true, append: false));
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
        return PushLinesIterator(lua, ToFile(lua, 1), 2, close: false);
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
        double offset = lua.L_Opt(lua.L_CheckNumber, 3, 0.0);
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
            lua.PushNumber(file.Seek(origin, (long)offset));
            return 1;
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return PushResult(lua, e, null);
        }
    }

    private static int F_Setvbuf(ILuaState lua)
    {
        // Buffering is the stream's business: accepted, and ignored
        ToFile(lua, 1);
        lua.PushBoolean(true);
        return 1;
    }

    private static int F_Gc(ILuaState lua)
    {
        // As in the reference implementation, which the collector calls: here only a script does
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

    private static int PushLinesIterator(ILuaState lua, LuaFileHandle file, int firstFormat, bool close)
    {
        // The formats lines() was given, read again on every call
        int formatCount = Math.Max(0, lua.GetTop() - firstFormat + 1);
        string[] formats = new string[formatCount];
        double[] counts = new double[formatCount];
        for (int i = 0; i < formatCount; i++)
        {
            if (lua.Type(firstFormat + i) == LuaType.LUA_TNUMBER)
            {
                counts[i] = lua.ToNumber(firstFormat + i);
            }
            else
            {
                formats[i] = lua.L_CheckString(firstFormat + i);
            }
        }

        lua.PushCSharpFunction(iterator =>
        {
            if (file.IsClosed)
            {
                return iterator.L_Error("file is already closed");
            }

            int top = iterator.GetTop();
            for (int i = 0; i < formatCount; i++)
            {
                if (formats[i] is null)
                {
                    iterator.PushNumber(counts[i]);
                }
                else
                {
                    iterator.PushString(formats[i]);
                }
            }

            int results = Read(iterator, file, top + 1);
            if (!iterator.IsNil(-results))
            {
                return results;
            }

            if (results > 1)
            {
                // nil and the reason: not the end of the file, but a failure
                return iterator.L_Error("{0}", iterator.ToString(-results + 1));
            }

            // The end of the file: io.lines(name) closes it
            if (close)
            {
                file.Close();
            }

            return 0;
        });
        return 1;
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
                success = ReadCount(lua, file, (long)lua.ToNumber(n));
            }
            else
            {
                string format = lua.L_CheckString(n);
                char kind = format.Length > 1 && format[0] == '*' ? format[1] : format.Length > 0 ? format[0] : '\0';
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

        int c = 0;
        while (text.Length < count && (c = file.ReadChar()) != -1)
        {
            text.Append((char)c);
            if (char.IsHighSurrogate((char)c))
            {
                // the pair is one character: not split between two reads
                int low = file.ReadChar();
                if (low != -1)
                {
                    text.Append((char)low);
                }
            }
        }

        lua.PushString(text.ToString());
        return text.Length > 0;
    }

    private static bool ReadNumber(ILuaState lua, LuaFileHandle file)
    {
        // Like fscanf("%lf"): skip white space, then take what can be part of a number
        int c;
        while ((c = file.PeekByte()) != -1 && char.IsWhiteSpace((char)c))
        {
            file.SkipByte();
        }

        StringBuilder number = new();
        while (number.Length < MaxNumberLength && (c = file.PeekByte()) != -1 && IsNumberChar((char)c))
        {
            number.Append((char)c);
            file.SkipByte();
        }

        if (LuaState.O_Str2Decimal(number.ToString(), out double value))
        {
            lua.PushNumber(value);
            return true;
        }

        lua.PushNil();
        return false;
    }

    private static bool IsNumberChar(char c)
    {
        return char.IsAsciiHexDigit(c) || c is '.' or '+' or '-' or 'x' or 'X' or 'p' or 'P';
    }

    private static int Write(ILuaState lua, LuaFileHandle file, int first)
    {
        int last = lua.GetTop();
        try
        {
            for (int i = first; i <= last; i++)
            {
                file.Write(lua.L_CheckString(i));
            }
        }
        catch (Exception e) when (LuaFile.IsFileError(e))
        {
            return PushResult(lua, e, null);
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
