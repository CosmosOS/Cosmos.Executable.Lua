// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;

namespace Cosmos.Executable.Lua;

/// <summary>
/// A Lua 5.4 interpreter: a state with the standard libraries open, which
/// runs chunks, files and an interactive prompt, as the reference
/// <c>lua</c> does.
/// </summary>
/// <remarks>
/// <para>The libraries reach the machine through <see cref="System.IO"/>
/// and <see cref="Console"/>: on a Cosmos kernel, the VFS and the console
/// session of the thread that runs the script. An interpreter is not
/// thread-safe; interpreters on different threads are independent.</para>
/// <para><see cref="State"/> is the UniLua API, for what this class does
/// not wrap, such as registering C# functions for scripts to call. Lua
/// strings hold bytes there; this class takes and gives text, which
/// <see cref="LuaText"/> converts to and from UTF-8.</para>
/// <para>Nothing collects the files a script opened and forgot:
/// <see cref="Dispose"/> closes them.</para>
/// </remarks>
public sealed class LuaInterpreter : IDisposable
{
    /// <summary>The prompt of a new statement, and of the next line of an unfinished one.</summary>
    private const string Prompt = "> ";
    private const string ContinuationPrompt = ">> ";

    /// <summary>What a syntax error ends with when the chunk is only unfinished, as the reference lua tests for.</summary>
    private const string EofMark = "<eof>";

    private readonly LuaHost _host;

    /// <summary>The message of the error <see cref="MessageHandler"/> last saw, without the traceback it adds.</summary>
    private string? _errorMessage;

    /// <summary>Makes a state, and opens the standard libraries in it.</summary>
    public LuaInterpreter()
    {
        State = LuaAPI.NewState();
        State.L_OpenLibs();
        _host = LuaHost.Of(State);
    }

    /// <summary>The state the interpreter runs, for the UniLua API.</summary>
    public ILuaState State { get; }

    /// <summary>
    /// The directory that relative file names start from, for
    /// <c>dofile</c>, <c>require</c>, <c>io.open</c>, <c>os.remove</c>...;
    /// null leaves them to <see cref="System.IO"/>, which starts them from
    /// the process's current directory.
    /// </summary>
    public string? WorkingDirectory
    {
        get => _host.WorkingDirectory;
        set => _host.WorkingDirectory = value;
    }

    /// <summary>What <c>io.read</c> and <c>io.stdin</c> read; null, the default, is <see cref="Console.In"/>.</summary>
    public TextReader? Input
    {
        get => _host.Input;
        set => _host.Input = value;
    }

    /// <summary>Where <c>print</c>, <c>io.write</c> and <c>io.stdout</c> write; null, the default, is <see cref="Console.Out"/>.</summary>
    public TextWriter? Output
    {
        get => _host.Output;
        set => _host.Output = value;
    }

    /// <summary>Where <c>io.stderr</c> writes; null, the default, is <see cref="Output"/>.</summary>
    public TextWriter? Error
    {
        get => _host.Error;
        set => _host.Error = value;
    }

    /// <summary>
    /// Runs a command line for <c>os.execute</c>, and returns its exit status
    /// (0 for success). Null, the default, means there is no shell:
    /// <c>os.execute()</c> returns false.
    /// </summary>
    public Func<string, int>? ExecuteCommand
    {
        get => _host.ExecuteCommand;
        set => _host.ExecuteCommand = value;
    }

    /// <summary>Runs <paramref name="chunk"/>.</summary>
    /// <param name="chunk">The Lua code.</param>
    /// <param name="chunkName">
    /// The name errors give the chunk: <c>=name</c> for <c>name</c> as is,
    /// <c>@file</c> for a file; null names it after its first line, as
    /// <c>[string "..."]</c>.
    /// </param>
    /// <exception cref="LuaException">The chunk has a syntax error, or raised an error it did not catch.</exception>
    /// <exception cref="LuaExitException">The chunk called <c>os.exit</c>.</exception>
    public void DoString(string chunk, string? chunkName = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        string code = LuaText.Encode(chunk);
        int top = State.GetTop();
        ThrowIfFailed(State.L_LoadBuffer(code, chunkName is null ? code : LuaText.Encode(chunkName)), top);
        Call(top, 0);
    }

    /// <summary>
    /// Runs the script in <paramref name="path"/>, as <c>lua script args...</c>
    /// does: the script gets the arguments as <c>...</c>, and in the global
    /// table <c>arg</c>, where <c>arg[0]</c> is <paramref name="path"/>.
    /// </summary>
    /// <exception cref="LuaException">The file cannot be read, has a syntax error, or raised an error it did not catch.</exception>
    /// <exception cref="LuaExitException">The script called <c>os.exit</c>.</exception>
    public void DoFile(string path, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(arguments);

        string fileName = LuaText.Encode(path);
        State.CreateTable(arguments.Length, 1);
        State.PushString(fileName);
        State.RawSetI(-2, 0);
        for (int i = 0; i < arguments.Length; i++)
        {
            State.PushString(LuaText.Encode(arguments[i]));
            State.RawSetI(-2, i + 1);
        }

        State.SetGlobal("arg");

        int top = State.GetTop();
        ThrowIfFailed(State.L_LoadFile(fileName), top);
        foreach (string argument in arguments)
        {
            State.PushString(LuaText.Encode(argument));
        }

        Call(top, arguments.Length);
    }

    /// <summary>
    /// Runs an interactive prompt on <see cref="Input"/> and
    /// <see cref="Output"/> until the input ends or a statement calls
    /// <c>os.exit</c>. An expression is printed: <c>1 + 1</c> prints 2. A
    /// statement that is not finished at the end of a line, such as
    /// <c>for i = 1, 3 do</c>, goes on on the next one.
    /// </summary>
    /// <returns>The code <c>os.exit</c> was called with, or 0 at the end of the input.</returns>
    public int RunPrompt()
    {
        while (true)
        {
            _host.Out.Write(Prompt);
            string? line = ReadPromptLine();
            if (line is null)
            {
                _host.Out.WriteLine();
                return 0;
            }

            if (line.Length == 0)
            {
                continue;
            }

            int top = State.GetTop();
            if (!LoadPromptLine(line, out string? syntaxError))
            {
                if (syntaxError is null)
                {
                    _host.Out.WriteLine();
                    return 0; // the input ended in the middle of a statement
                }

                _host.WriteErr(syntaxError + "\n");
                continue;
            }

            try
            {
                Call(top, 0, keepResults: true);
            }
            catch (Exception e)
            {
                // One clause that tells the exceptions apart: a Cosmos kernel
                // enters the first typed catch clause whatever the type
                if (e is LuaExitException exit)
                {
                    return exit.ExitCode;
                }

                if (e is not LuaException error)
                {
                    throw;
                }

                _host.Err.WriteLine(error.LuaStackTrace is null ? error.Message : error.Message + "\n" + error.LuaStackTrace);
                continue;
            }

            PrintResults(top);
        }
    }

    /// <summary>
    /// Compiles what is typed at the prompt, reading more lines while the
    /// statement is unfinished. False with the message for a syntax error,
    /// or with null when the input ends first.
    /// </summary>
    private bool LoadPromptLine(string line, out string? syntaxError)
    {
        syntaxError = null;
        int top = State.GetTop();

        // An expression first, so that it is printed; =expr as in Lua 5.1
        string expression = line.StartsWith('=') ? line[1..] : line;
        if (State.L_LoadBuffer("return " + expression, "=stdin") == ThreadStatus.LUA_OK)
        {
            return true;
        }

        State.SetTop(top);
        string chunk = line;
        while (true)
        {
            ThreadStatus status = State.L_LoadBuffer(chunk, "=stdin");
            if (status == ThreadStatus.LUA_OK)
            {
                return true;
            }

            string message = State.ToString(-1) ?? "(error object is not a string)";
            State.SetTop(top);
            if (status != ThreadStatus.LUA_ERRSYNTAX || !message.EndsWith(EofMark, StringComparison.Ordinal))
            {
                syntaxError = message;
                return false;
            }

            _host.Out.Write(ContinuationPrompt);
            string? next = ReadPromptLine();
            if (next is null)
            {
                return false;
            }

            chunk += "\n" + next;
        }
    }

    /// <summary>A line typed at the prompt, as a Lua string; null at the end of the input.</summary>
    private string? ReadPromptLine()
    {
        string? line = _host.In.ReadLine();
        return line is null ? null : LuaText.Encode(line);
    }

    /// <summary>Prints what a statement at the prompt returned, with print, as the reference lua does.</summary>
    private void PrintResults(int top)
    {
        int count = State.GetTop() - top;
        if (count == 0)
        {
            return;
        }

        State.GetGlobal("print");
        State.Insert(top + 1);
        if (State.PCall(count, 0, 0) != ThreadStatus.LUA_OK)
        {
            _host.WriteErr("error calling 'print' (" + State.ToString(-1) + ")\n");
        }

        State.SetTop(top);
    }

    /// <summary>
    /// Calls the function at <paramref name="top"/> + 1 with the
    /// <paramref name="argumentCount"/> values above it, with a traceback
    /// for its errors.
    /// </summary>
    private void Call(int top, int argumentCount, bool keepResults = false)
    {
        int function = top + 1;
        State.PushCSharpFunction(MessageHandler);
        State.Insert(function); // below the function
        _errorMessage = null;

        ThreadStatus status;
        try
        {
            status = State.PCall(argumentCount, keepResults ? LuaDef.LUA_MULTRET : 0, function);
        }
        catch (Exception)
        {
            // os.exit: what it left on the stack goes
            State.SetTop(top);
            throw;
        }

        State.Remove(function);
        if (status != ThreadStatus.LUA_OK)
        {
            // The handler ran for a Lua error, and left its message and the
            // traceback; a .NET exception, which became an error, has only
            // its message (and the handler may have run for an error a
            // 'load' returned since)
            string error = State.ToString(-1) ?? "(error object is not a string)";
            State.SetTop(top);
            if (_errorMessage is null || !error.StartsWith(_errorMessage + "\n", StringComparison.Ordinal))
            {
                throw new LuaException(LuaText.Decode(error));
            }

            throw new LuaException(LuaText.Decode(_errorMessage),
                LuaText.Decode(error.Substring(_errorMessage.Length + 1)));
        }

        if (!keepResults)
        {
            State.SetTop(top);
        }
    }

    /// <summary>Closes the files the scripts left open. The state can still run code, which may open others.</summary>
    public void Dispose()
    {
        _host.CloseFiles();
    }

    /// <summary>
    /// Remembers the error's message, and returns it with the traceback of
    /// where it was raised, as the message handler of the reference lua does
    /// (a 'load' whose reader function fails returns what it gives).
    /// </summary>
    private int MessageHandler(ILuaState lua)
    {
        string? message = lua.ToString(1);
        if (message is null)
        {
            // An error object that is not a string: its __tostring, or its type
            message = lua.L_CallMeta(1, "__tostring") && lua.Type(-1) == LuaType.LUA_TSTRING
                ? lua.ToString(-1)
                : "(error object is a " + lua.L_TypeName(1) + " value)";
        }

        _errorMessage = message;
        lua.L_Traceback(lua, message, 1);
        return 1;
    }

    /// <summary>Throws the error a load left on the stack, if it failed.</summary>
    private void ThrowIfFailed(ThreadStatus status, int top)
    {
        if (status == ThreadStatus.LUA_OK)
        {
            return;
        }

        string message = State.ToString(-1) ?? "(error object is not a string)";
        State.SetTop(top);
        throw new LuaException(LuaText.Decode(message));
    }
}
