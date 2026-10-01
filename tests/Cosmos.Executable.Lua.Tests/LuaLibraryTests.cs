// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

public class LuaLibraryTests : LuaTest
{
    [TestCase("1/3", "0.33333333333333")]
    [TestCase("2^53", "9.007199254741e+15")]
    [TestCase("100", "100")]
    [TestCase("-7.25", "-7.25")]
    [TestCase("1e15", "1e+15")]
    [TestCase("1e100", "1e+100")]
    [TestCase("0.1 + 0.2", "0.3")]
    [TestCase("123456789012", "123456789012")]
    [TestCase("1e-5", "1e-05")]
    [TestCase("-0.0", "-0")]
    [TestCase("math.huge", "inf")]
    [TestCase("-math.huge", "-inf")]
    [TestCase("0x10", "16")]
    [TestCase("10 .. ''", "10")]
    [TestCase("1.5 .. 'x'", "1.5x")]
    public void NumbersAreWrittenAsLuaWritesThem(string expression, string expected)
    {
        Assert.That(Eval(expression), Is.EqualTo(expected));
    }

    [Test]
    public void NumbersDoNotDependOnTheCulture()
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.That(Eval("0.5, tonumber('2.5'), string.format('%.2f', 1.5)"), Is.EqualTo("0.5\t2.5\t1.50"));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Test]
    public void DecimalLiteralsAreRoundedOnce()
    {
        Assert.That(Eval("0.3 == 3/10, 0.1 * 3 == 0.3, 123.456e-2 == 1.23456"), Is.EqualTo("true\tfalse\ttrue"));
    }

    [Test]
    public void TheLanguageWorks()
    {
        string output = Run("""
            local function fib(n) if n < 2 then return n end return fib(n - 1) + fib(n - 2) end
            local t = {}
            for i = 1, 10 do t[#t + 1] = fib(i) end
            print(table.concat(t, ','))

            local counter = (function() local n = 0 return function() n = n + 1 return n end end)()
            counter() counter()
            print(counter())

            local v = setmetatable({ 1, 2 }, { __add = function(a, b) return a[1] + b[1] end, __len = function() return 99 end })
            print(v + v, #v)

            local co = coroutine.create(function(a) local b = coroutine.yield(a + 1) return b * 2 end)
            print(coroutine.resume(co, 1))
            print(coroutine.resume(co, 10))
            print(coroutine.status(co))

            for i = 1, 3 do
              if i == 2 then goto continue end
              io.write(i, ' ')
              ::continue::
            end
            print()

            print(select('#', nil, nil), select(2, 'a', 'b', 'c'))
            print(string.format('%5.1f|%-3d|%s|%q', 3.14159, 7, 'x', 'a"b'))
            print(('hello world'):gsub('o', '0'), ('key=value'):match('(%w+)=(%w+)'))
            """);

        Assert.That(output, Is.EqualTo(
            "1,1,2,3,5,8,13,21,34,55\n" +
            "3\n" +
            "2\t99\n" +
            "true\t2\n" +
            "true\t20\n" +
            "dead\n" +
            "1 3 \n" +
            "2\tb\tc\n" +
            "  3.1|7  |x|\"a\\\"b\"\n" +
            "hell0 w0rld\tkey\tvalue\n"));
    }

    [Test]
    public void ErrorsCarryThePositionAndPcallCatchesThem()
    {
        Assert.That(Run("print(pcall(error, 'msg', 0))"), Is.EqualTo("false\tmsg\n"));
        Assert.That(Run("print(pcall(function() local x = nil; return x.field end))"), Does.StartWith("false\ttest:1: attempt to index"));
        Assert.That(Run("print(select(2, pcall(function() return math.floor() end)))"), Is.EqualTo("test:1: bad argument #1 to 'floor' (number expected, got no value)\n"));
    }

    [Test]
    public void MathRandomFollowsItsSeed()
    {
        string first = Run("math.randomseed(42) print(math.random(1, 100), math.random(1, 100), math.random())");
        string second = Run("math.randomseed(42) print(math.random(1, 100), math.random(1, 100), math.random())");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(Eval("math.random(5, 5), math.huge == 1/0"), Is.EqualTo("5\ttrue"));
    }

    // ---- io

    [Test]
    public void FilesAreWrittenAndReadBack()
    {
        string output = Run("""
            local f = assert(io.open('data.txt', 'w'))
            print(io.type(f), f:write('first line\n', 42, ' ', 1.5, '\n', 'héllo\n') == f)
            f:close()
            print(io.type(f), tostring(f))

            f = assert(io.open('data.txt'))
            print(f:read('*l'))
            print(f:read('*n', '*n'))
            print(f:read('*L'))
            print(f:read('*l'))
            print(f:read('*l'), f:read('*a'), f:read(0))
            f:close()

            for line in io.lines('data.txt') do io.write('[', line, ']') end
            print()
            """);

        Assert.That(output, Is.EqualTo(
            "file\ttrue\n" +
            "closed file\tfile (closed)\n" +
            "first line\n" +
            "42\t1.5\n" +
            "\n\n" +
            "héllo\n" +
            "nil\t\tnil\n" +
            "[first line][42 1.5][héllo]\n"));
        Assert.That(File.ReadAllText(Path.Combine(Directory, "data.txt"), Encoding.UTF8), Is.EqualTo("first line\n42 1.5\nhéllo\n"));
    }

    [Test]
    public void SeekAppendAndReadCounts()
    {
        string output = Run("""
            local f = assert(io.open('seek.txt', 'w+'))
            f:write('0123456789')
            print(f:seek('set', 2), f:read(3), f:seek(), f:seek('end'))
            f:close()

            f = assert(io.open('seek.txt', 'a'))
            f:write('AB')
            f:close()
            f = assert(io.open('seek.txt', 'r'))
            print(f:read('*a'))
            f:close()
            """);

        Assert.That(output, Is.EqualTo("2\t234\t5\t10\n0123456789AB\n"));
    }

    [Test]
    public void BinaryFilesKeepEveryByte()
    {
        Run("""
            local f = assert(io.open('bytes.bin', 'wb'))
            for i = 0, 255 do f:write(string.char(i)) end
            f:close()
            f = assert(io.open('bytes.bin', 'rb'))
            local all = f:read('*a')
            f:close()
            assert(#all == 256 and all:byte(1) == 0 and all:byte(256) == 255)
            """);

        byte[] bytes = File.ReadAllBytes(Path.Combine(Directory, "bytes.bin"));
        Assert.That(bytes, Has.Length.EqualTo(256));
        Assert.That(bytes[200], Is.EqualTo(200));
    }

    [Test]
    public void FileErrorsAreResultsNotErrors()
    {
        Assert.That(Run("print(io.open('missing.txt'))"), Is.EqualTo("nil\tmissing.txt: No such file or directory\t2\n"));
        Assert.That(ErrorOf("io.open('x', 'z')"), Does.Contain("invalid mode"));
        Assert.That(ErrorOf("io.lines('missing.txt')"), Does.Contain("cannot open file 'missing.txt' (No such file or directory)"));
        Assert.That(ErrorOf("local f = io.open('closed.txt', 'w') f:close() f:write('x')"), Does.Contain("attempt to use a closed file"));
        Assert.That(Run("print(io.stdout:close())"), Is.EqualTo("nil\tcannot close standard file\n"));
    }

    [Test]
    public void TheDefaultFilesAreTheConsole()
    {
        Lua.Input = new StringReader("a line\n12 13\nrest\nof it\n");

        string output = Run("""
            print(io.read())
            print(io.read('*n', '*n'))
            print(io.read('*L'))
            print(io.read('*a'))
            print(io.read(), io.type(io.stdin), io.input() == io.stdin)
            io.write('no newline', 1, '\n')
            io.stderr:write('to stderr\n')
            """);

        Assert.That(output, Is.EqualTo("a line\n12\t13\n\n\nrest\nof it\n\nnil\tfile\ttrue\nno newline1\nto stderr\n"));
    }

    [Test]
    public void TheDefaultOutputCanBeAFile()
    {
        Run("io.output('out.txt') io.write('to the file') io.close() io.output(io.stdout) print('back')");

        Assert.That(File.ReadAllText(Path.Combine(Directory, "out.txt")), Is.EqualTo("to the file"));
        Assert.That(Output, Is.EqualTo("back\n"));
    }

    [Test]
    public void WritesReachTheFileBeforeItIsClosed()
    {
        Run("leaked = io.open('leak.txt', 'w') leaked:write('written')");

        Assert.That(File.ReadAllText(Path.Combine(Directory, "leak.txt")), Is.EqualTo("written"));
    }

    [Test]
    public void DisposeClosesTheFilesScriptsLeftOpen()
    {
        Run("leaked = io.open('leak.txt', 'w') lines = io.lines('leak.txt')");

        Lua.Dispose();

        Assert.That(Eval("io.type(leaked)"), Is.EqualTo("closed file"));
        File.Delete(Path.Combine(Directory, "leak.txt")); // no handle holds it
    }

    [Test]
    public void TmpfileLivesInMemory()
    {
        Assert.That(Eval("(function() local f = io.tmpfile() f:write('kept') f:seek('set') return f:read('*a') end)()"), Is.EqualTo("kept"));
    }

    // ---- os

    [Test]
    public void TimeAndDateAgree()
    {
        string output = Run("""
            local t = os.time({ year = 2026, month = 10, day = 1, hour = 21, min = 46, sec = 5 })
            local d = os.date('*t', t)
            print(d.year, d.month, d.day, d.hour, d.min, d.sec, d.wday, d.yday, d.isdst)
            print(os.date('%Y-%m-%d %H:%M:%S %a %b %j %p %%', t))
            print(os.time({ year = 2026, month = 13, day = 0, hour = 0 }) == os.time({ year = 2026, month = 12, day = 31, hour = 0 }))
            print(os.date('!%c', 0), os.date('!%x %X %y', 86400 * 365))
            print(os.difftime(t + 60, t), type(os.time()), os.clock() >= 0)
            """);

        Assert.That(output, Is.EqualTo(
            "2026\t10\t1\t21\t46\t5\t5\t274\tfalse\n" +
            "2026-10-01 21:46:05 Thu Oct 274 PM %\n" +
            "true\n" +
            "Thu Jan  1 00:00:00 1970\t01/01/71 00:00:00 71\n" +
            "60\tnumber\ttrue\n"));
    }

    [Test]
    public void DateRejectsUnknownConversions()
    {
        Assert.That(ErrorOf("os.date('%Q')"), Does.Contain("invalid conversion specifier '%Q'"));
        Assert.That(ErrorOf("os.time({ year = 2026 })"), Does.Contain("field 'day' missing in date table"));
    }

    [Test]
    public void RemoveAndRenameFiles()
    {
        File.WriteAllText(Path.Combine(Directory, "old.txt"), "content");

        string output = Run("""
            print(os.rename('old.txt', 'new.txt'))
            print(io.open('old.txt'), io.open('new.txt') ~= nil)
            print(os.remove('new.txt'))
            print(os.remove('new.txt'))
            print(os.getenv('COSMOS_LUA_NO_SUCH_VARIABLE'), os.setlocale(), os.setlocale('fr_FR'))
            """);

        Assert.That(output, Is.EqualTo(
            "true\n" +
            "nil\ttrue\n" +
            "true\n" +
            "nil\tnew.txt: No such file or directory\t2\n" +
            "nil\tC\tnil\n"));
    }

    // ---- states on several threads

    [Test]
    public void InterpretersOnDifferentThreadsDoNotInterfere()
    {
        const string Script = """
            local t = {}
            for i = 1, 20000 do t['k' .. i] = i end
            local sum = 0
            for k, v in pairs(t) do sum = sum + v end
            math.randomseed(1)
            return sum, math.random(1, 1000)
            """;

        string[] results = new string[4];
        Thread[] threads = new Thread[results.Length];
        for (int i = 0; i < threads.Length; i++)
        {
            int index = i;
            threads[i] = new Thread(() =>
            {
                StringWriter output = new();
                LuaInterpreter lua = new() { Output = output };
                lua.DoString("print((function() " + Script + " end)())");
                results[index] = output.ToString();
            });
            threads[i].Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }

        Assert.That(results, Is.All.EqualTo(results[0]));
        Assert.That(results[0], Does.StartWith("200010000\t"));
    }
}
