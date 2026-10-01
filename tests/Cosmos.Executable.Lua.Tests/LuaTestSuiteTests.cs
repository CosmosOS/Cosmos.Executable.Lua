// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// Runs files of the official Lua 5.2 test suite (lua-5.2-tests). Left out:
/// closure, coroutine and gc, which test weak tables and <c>__gc</c>; files,
/// which tests byte strings in text files, buffering and <c>io.popen</c>;
/// main, which runs the lua program.
/// </summary>
public class LuaTestSuiteTests
{
    /// <summary>The stack the suite runs on: the reference lua gets the process's, 8 MB on Linux.</summary>
    private const int StackSize = 16 * 1024 * 1024;

    [TestCase("api")]
    [TestCase("attrib")]
    [TestCase("big")]
    [TestCase("bitwise")]
    [TestCase("calls")]
    [TestCase("checktable")]
    [TestCase("code")]
    [TestCase("constructs")]
    [TestCase("db")]
    [TestCase("errors")]
    [TestCase("events")]
    [TestCase("goto")]
    [TestCase("literals")]
    [TestCase("locals")]
    [TestCase("math")]
    [TestCase("nextvar")]
    [TestCase("pm")]
    [TestCase("sort")]
    [TestCase("strings")]
    [TestCase("vararg")]
    [TestCase("verybig")]
    public void Passes(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "lua-5.2-tests");
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                LuaInterpreter lua = new()
                {
                    Output = TextWriter.Null,
                    WorkingDirectory = directory,
                };

                // The suite's own switches: no tests of a particular port, of slow
                // cases, or of numbers wider than 32 bits
                lua.DoString("_port = true _soft = true _no32 = true");
                lua.DoFile(name + ".lua");
                lua.Dispose();
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, StackSize);
        thread.Start();
        thread.Join();

        if (failure is LuaException error)
        {
            Assert.Fail(error.Message + "\n" + error.LuaStackTrace);
        }

        Assert.That(failure, Is.Null);
    }
}
