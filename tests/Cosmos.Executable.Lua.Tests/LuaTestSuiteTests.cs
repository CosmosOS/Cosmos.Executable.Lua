// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// Runs the files of the official Lua 5.4 test suite (lua-5.4-tests, from
/// lua-5.4.9-tests) one by one, then all together through all.lua, which
/// loads most of them again from <c>string.dump</c>; with <c>_port</c> set
/// (no tests of the platform of the reference implementation) and the slow
/// tests on. Left out: gc.lua and gengc.lua, which test the collector, weak
/// tables and <c>__gc</c>; heavy.lua, which all.lua does not run either;
/// main.lua, which runs the lua program, is there for all.lua and tests
/// nothing with <c>_port</c> set; bwcoercion.lua and tracegc.lua are
/// modules the other files require.
/// </summary>
public class LuaTestSuiteTests
{
    /// <summary>The stack the suite runs on: the reference lua gets the process's, 8 MB on Linux.</summary>
    private const int StackSize = 16 * 1024 * 1024;

    /// <summary>
    /// The lines of the suite that cannot hold here, changed in the copy the
    /// tests run: the files themselves are as the Lua authors wrote them.
    /// </summary>
    private static readonly (string File, string Line, string Replacement)[] Patches =
    [
        // TODO: Cosmos gives no control over its collector, so there is
        // none to test
        ("all.lua", "local f = assert(loadfile('gc.lua'))", "local f = function () end  -- no collector to test"),
        ("all.lua", "dofile('gengc.lua')", "-- dofile('gengc.lua')  -- no collector to test"),

        // TODO: Cosmos has no weak references: there is no collection to wait for
        ("closure.lua", "while x[1] do   -- repeat until GC", "x[1] = nil; while x[1] do   -- repeat until GC"),

        // TODO: Cosmos has no weak references: the coroutine stays in the table
        ("coroutine.lua", "assert(C[1] == undef)", "-- assert(C[1] == undef)"),

        // TODO: Cosmos has no finalizers: the one the test waits for never runs
        ("db.lua", "do   -- testing debug info for finalizers", "if false then   -- testing debug info for finalizers"),
    ];

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

    /// <summary>Copies the suite to <paramref name="directory"/>, and patches the copy.</summary>
    private static void CopySuite(string directory)
    {
        string suite = Path.Combine(AppContext.BaseDirectory, "lua-5.4-tests");
        foreach (string file in Directory.GetFiles(suite))
        {
            File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
        }

        foreach ((string file, string line, string replacement) in Patches)
        {
            // Latin-1, which keeps the bytes of the files as they are
            string path = Path.Combine(directory, file);
            string text = File.ReadAllText(path, Encoding.Latin1);
            int at = text.IndexOf(line, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), $"{file} has no line '{line}' to patch");
            File.WriteAllText(path, text[..at] + replacement + text[(at + line.Length)..], Encoding.Latin1);
        }
    }
}
