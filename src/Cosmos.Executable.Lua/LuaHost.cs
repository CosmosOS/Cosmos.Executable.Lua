// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Cosmos.Executable.Lua;

/// <summary>
/// What the libraries of one Lua state reach the machine through: the
/// console, the directory relative paths start from, the clock and the
/// random numbers. The coroutines of a state share it; every state has its
/// own, so states on different threads, such as the shells of two console
/// sessions, do not step on each other.
/// </summary>
internal sealed class LuaHost
{
    private Random? _random;

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

    /// <summary>Runs a command for <c>os.execute</c> and returns its exit status; null when there is no shell.</summary>
    public Func<string, int>? ExecuteCommand { get; set; }

    /// <summary>When the state was made, for <c>os.clock</c>.</summary>
    public long StartTimestamp { get; } = Stopwatch.GetTimestamp();

    /// <summary>
    /// The generator behind <c>math.random</c>, seeded from the clock on
    /// first use; <c>math.randomseed</c> replaces it.
    /// </summary>
    public Random Random
    {
        get => _random ??= new Random(unchecked((int)Stopwatch.GetTimestamp()));
        set => _random = value;
    }

    /// <summary>
    /// The code <c>os.exit</c> asked for, while it unwinds the script; see
    /// <see cref="LuaState.D_PropagateExit"/>.
    /// </summary>
    public int? ExitCode { get; set; }

    /// <summary>How many protected calls of the state are running, which os.exit unwinds one by one.</summary>
    public int ProtectedDepth { get; set; }

    /// <summary>
    /// The files of the state that are open, which <see cref="CloseFiles"/>
    /// closes: there is no collector to close those a script forgot, and a
    /// kernel has few file descriptors.
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

    /// <summary>Makes <paramref name="path"/> absolute against <see cref="WorkingDirectory"/>, if set.</summary>
    public string ResolvePath(string path)
    {
        return WorkingDirectory is null || Path.IsPathRooted(path) ? path : Path.Combine(WorkingDirectory, path);
    }
}
