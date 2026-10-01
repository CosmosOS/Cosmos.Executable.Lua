// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;

namespace Cosmos.Executable.Lua;

/// <summary>
/// Thrown by <c>os.exit</c>: a script asked to end. It is not a Lua error,
/// so <c>pcall</c> does not catch it; it unwinds to the host, which ends the
/// script there, as the reference <c>lua</c> ends its process.
/// </summary>
/// <remarks>
/// The state is left as it was before the call the host made, so the host
/// may go on using it.
/// </remarks>
public sealed class LuaExitException : Exception
{
    /// <summary>Makes the exception for <c>os.exit(code)</c>.</summary>
    public LuaExitException(int exitCode)
        : base($"The script exited with code {exitCode}.")
    {
        ExitCode = exitCode;
    }

    /// <summary>The code the script exited with: 0 for success, as <c>os.exit()</c> and <c>os.exit(true)</c> give.</summary>
    public int ExitCode { get; }
}
