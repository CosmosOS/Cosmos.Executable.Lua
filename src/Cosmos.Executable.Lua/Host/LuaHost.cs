// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Cosmos.Executable.Lua;

/// <summary>
/// What the libraries of one Lua state reach the machine through: the
/// console, the directory relative paths start from and the clock. The
/// coroutines of a state share it; every state has its own, so states on
/// different threads, such as the shells of two console sessions, do not
/// step on each other.
/// </summary>
internal sealed class LuaHost
{
    /// <summary>The directory relative paths resolve against; null leaves them to <see cref="System.IO"/>.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Where <c>io.read</c> and <c>io.stdin</c> read from; null is <see cref="Console.In"/>.</summary>
    public TextReader? Input { get; set; }

    /// <summary>Where <c>print</c>, <c>io.write</c> and <c>io.stdout</c> write to; null is <see cref="Console.Out"/>.</summary>
    public TextWriter? Output { get; set; }

    /// <summary>Where <c>io.stderr</c> writes to; null is <see cref="Output"/>.</summary>
    public TextWriter? Error { get; set; }

    /// <summary>
    /// The console as it is when the library reads it, not when the state
    /// was made: <see cref="Console"/> follows the session of the thread
    /// that runs the script.
    /// </summary>
    public TextReader In => Input ?? Console.In;

    /// <inheritdoc cref="In"/>
    public TextWriter Out => Output ?? Console.Out;

    /// <inheritdoc cref="In"/>
    public TextWriter Err => Error ?? Out;

    /// <summary>
    /// Writes the Lua string <paramref name="luaString"/> to <see cref="Out"/>,
    /// decoding its bytes from UTF-8 (a sequence may be split between writes).
    /// </summary>
    public void WriteOut(string luaString)
    {
        Write(Out, luaString);
    }

    /// <summary>Writes the Lua string <paramref name="luaString"/> to <see cref="Err"/>; see <see cref="WriteOut"/>.</summary>
    public void WriteErr(string luaString)
    {
        Write(Err, luaString);
    }

    /// <summary>A line of <see cref="In"/>, with its end, as a Lua string; null at the end of the input.</summary>
    public string? ReadInLine()
    {
        string? line = In.ReadLine();
        return line is null ? null : LuaText.Encode(line + "\n");
    }

    /// <summary>The writers the bytes went to last, with the UTF-8 sequences they left unfinished.</summary>
    private TextWriter? _writer1, _writer2;
    private Decoder? _decoder1, _decoder2;

    private void Write(TextWriter writer, string luaString)
    {
        Decoder decoder;
        if (ReferenceEquals(writer, _writer1))
        {
            decoder = _decoder1!;
        }
        else if (ReferenceEquals(writer, _writer2))
        {
            decoder = _decoder2!;
        }
        else
        {
            // A writer the host just set: the older one goes
            _writer2 = _writer1;
            _decoder2 = _decoder1;
            _writer1 = writer;
            _decoder1 = decoder = Encoding.UTF8.GetDecoder();
        }

        byte[] bytes = LuaText.ToBytes(luaString);
        char[] chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length, flush: false)];
        decoder.GetChars(bytes, 0, bytes.Length, chars, 0, flush: false);
        writer.Write(chars);
    }

    /// <summary>Runs a command for <c>os.execute</c> and returns its exit status; null when there is no shell.</summary>
    public Func<string, int>? ExecuteCommand { get; set; }

    /// <summary>When the state was made, for <c>os.clock</c>.</summary>
    public long StartTimestamp { get; } = Stopwatch.GetTimestamp();

    /// <summary>
    /// The code <c>os.exit</c> asked for, while it unwinds the script; see
    /// <see cref="LuaState.D_PropagateExit"/>.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>How many protected calls of the state are running, which os.exit unwinds one by one.</summary>
    public int ProtectedDepth { get; set; }

    /// <summary>
    /// The files of the state that are open, which <see cref="CloseFiles"/>
    /// closes: the collector closes those a script lost, but not those it
    /// keeps until the end, and a kernel has few file descriptors.
    /// </summary>
    public List<LuaFileHandle> OpenFiles { get; } = [];

    /// <summary>Closes the files a script left open.</summary>
    public void CloseFiles()
    {
        while (OpenFiles.Count > 0)
        {
            LuaFileHandle file = OpenFiles[^1];
            try
            {
                file.Close();
            }
            catch (Exception e) when (LuaFile.IsFileError(e))
            {
                OpenFiles.Remove(file); // a disk that failed: the file is gone anyway
            }
        }
    }

    /// <summary>The host of the state <paramref name="lua"/> belongs to.</summary>
    public static LuaHost Of(ILuaState lua)
    {
        return ((LuaState)lua).G.Host;
    }

    /// <summary>
    /// The path of the file a script names: the Lua string
    /// <paramref name="fileName"/> decoded from UTF-8, and made absolute
    /// against <see cref="WorkingDirectory"/>, if set.
    /// </summary>
    public string ResolvePath(string fileName)
    {
        string path = LuaText.Decode(fileName);
        return WorkingDirectory is null || Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path);
    }
}
