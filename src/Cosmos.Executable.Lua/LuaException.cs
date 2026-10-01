// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;

namespace Cosmos.Executable.Lua;

/// <summary>
/// A Lua error that reached the host: a syntax error in a chunk, or a
/// runtime error no <c>pcall</c> caught.
/// </summary>
public sealed class LuaException : Exception
{
    /// <summary>Makes the exception for an error with <paramref name="message"/>, as Lua wrote it.</summary>
    public LuaException(string message, string? luaStackTrace = null)
        : base(message)
    {
        LuaStackTrace = luaStackTrace;
    }

    /// <summary>
    /// Where the script was when the error was raised, as the reference
    /// <c>lua</c> writes it: <c>stack traceback:</c> and one line per call.
    /// Null for a syntax error.
    /// </summary>
    public string? LuaStackTrace { get; }
}
