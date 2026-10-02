// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// Runs the files of the official Lua 5.4 test suite (lua-5.4-tests, from
/// lua-5.4.9-tests), as their authors wrote them, one by one, then all
/// together through all.lua, which loads most of them again from
/// <c>string.dump</c>; with <c>_port</c> set (no tests of the platform of
/// the reference implementation) and the slow tests on. Left out:
/// heavy.lua, which all.lua does not run either; main.lua, which runs the
/// lua program, is there for all.lua and tests nothing with <c>_port</c>
/// set; bwcoercion.lua and tracegc.lua are modules the other files require.
/// </summary>
public class LuaTestSuiteTests
{
    /// <summary>The stack the suite runs on: the reference lua gets the process's, 8 MB on Linux.</summary>
    private const int StackSize = 16 * 1024 * 1024;

    [TestCase("all")]
    [TestCase("api")]
    [TestCase("attrib")]
    [TestCase("big")]
    [TestCase("bitwise")]
    [TestCase("calls")]
    [TestCase("closure")]
    [TestCase("code")]
    [TestCase("constructs")]
    [TestCase("coroutine")]
    [TestCase("cstack")]
    [TestCase("db")]
    [TestCase("errors")]
    [TestCase("events")]
    [TestCase("files")]
    [TestCase("gc")]
    [TestCase("gengc")]
    [TestCase("goto")]
    [TestCase("literals")]
    [TestCase("locals")]
    [TestCase("math")]
    [TestCase("nextvar")]
    [TestCase("pm")]
    [TestCase("sort")]
    [TestCase("strings")]
    [TestCase("tpack")]
    [TestCase("utf8")]
    [TestCase("vararg")]
    [TestCase("verybig")]
    public void Passes(string name)
    {
        // A copy, next to which files.lua writes its files
        string directory = Directory.CreateTempSubdirectory("lua-5.4-tests-").FullName;
        try
        {
            CopySuite(directory);

            Exception? failure = null;
            Thread thread = new(() =>
            {
                try
                {
                    LuaInterpreter lua = new()
                    {
                        Output = TextWriter.Null,
                        Error = TextWriter.Null, // the dots of tracegc.lua, a dot a collection
                        WorkingDirectory = directory,
                    };

                    lua.DoString("_port = true", "=init");
                    if (name == "big")
                    {
                        // As all.lua runs it: it yields between its parts
                        lua.DoString("local f = coroutine.wrap(assert(loadfile('big.lua'))) assert(f() == 'b') assert(f() == 'a')", "=all");
                    }
                    else
                    {
                        lua.DoFile(name + ".lua");
                    }

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
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Copies the suite to <paramref name="directory"/>.</summary>
    private static void CopySuite(string directory)
    {
        string suite = Path.Combine(AppContext.BaseDirectory, "lua-5.4-tests");
        foreach (string file in Directory.GetFiles(suite))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }
    }
}
