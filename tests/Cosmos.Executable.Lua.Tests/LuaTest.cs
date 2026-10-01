// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System.IO;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// A fresh interpreter for every test, writing to a buffer, with a fresh
/// temporary directory as its working directory.
/// </summary>
public abstract class LuaTest
{
    private StringWriter _output = new();
    private LuaInterpreter? _lua;

    protected string Directory { get; private set; } = string.Empty;

    protected LuaInterpreter Lua => _lua!;

    /// <summary>What the scripts printed so far.</summary>
    protected string Output => _output.ToString().Replace("\r\n", "\n");

    [SetUp]
    public void CreateInterpreter()
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("cosmoslua-").FullName;
        _output = new StringWriter();
        _lua = new LuaInterpreter
        {
            Output = _output,
            WorkingDirectory = Directory,
        };
    }

    [TearDown]
    public void DeleteDirectory()
    {
        Lua.Dispose();
        System.IO.Directory.Delete(Directory, recursive: true);
    }

    /// <summary>Runs <paramref name="code"/> as the chunk <c>test</c>, and returns what it printed.</summary>
    protected string Run(string code)
    {
        int before = Output.Length;
        Lua.DoString(code, "=test");
        return Output[before..];
    }

    /// <summary>Runs <c>return expression</c> and returns what <c>tostring</c> makes of the result.</summary>
    protected string Eval(string expression)
    {
        return Run("print(" + expression + ")").TrimEnd('\n');
    }

    /// <summary>The message of the error <paramref name="code"/> raises.</summary>
    protected string ErrorOf(string code)
    {
        LuaException e = Assert.Throws<LuaException>(() => Lua.DoString(code, "=test"))!;
        return e.Message;
    }
}
