// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// What the math, table, os and io libraries do as those of Lua 5.4.9 do,
/// where Lua 5.3 did otherwise; the results are those of the reference
/// implementation.
/// </summary>
public class LuaLibs54Tests : LuaTest
{
    // ---- math

    [Test]
    public void RandomIsXoshiro256StarStar()
    {
        // The first values after seed 1007, which the 5.4 test suite checks
        Assert.That(Eval("(function() math.randomseed(1007) return math.random(0) == 0x7a7040a5a323c9d6 end)()"), Is.EqualTo("true"));
        Assert.That(Eval("(function() math.randomseed(1007, 0) return math.random() * 2^53 == 0x7a7040a5a323c9d6 >> 11 end)()"), Is.EqualTo("true"));
        Assert.That(Eval("(function() math.randomseed(42) return math.random(1, 100), math.random(6), math.random(-5, 5) end)()"), Is.EqualTo("50\t4\t0"));
    }

    [Test]
    public void RandomseedReturnsTheSeeds()
    {
        Assert.That(Eval("math.randomseed(5, 7)"), Is.EqualTo("5\t7"));
        Assert.That(Eval("math.randomseed(5)"), Is.EqualTo("5\t0"));
        Assert.That(Eval("(function() local x, y = math.randomseed() local r = math.random(0) math.randomseed(x, y) return math.type(x), math.type(y), math.random(0) == r end)()"), Is.EqualTo("integer\tinteger\ttrue"));
    }

    [Test]
    public void RandomTakesAnyInterval()
    {
        Assert.That(Eval("math.type(math.random(math.mininteger, math.maxinteger)), math.type(math.random(0)), math.random(3, 3)"), Is.EqualTo("integer\tinteger\t3"));
        Assert.That(Eval("(function() for i = 1, 1000 do local r = math.random(-1, math.maxinteger) if r < -1 then return false end end return true end)()"), Is.EqualTo("true"));
        Assert.That(ErrorOf("math.random(2, 1)"), Does.Contain("(interval is empty)"));
        Assert.That(ErrorOf("math.random(1, 2, 3)"), Does.Contain("wrong number of arguments"));
    }

    [Test]
    public void TointegerTakesWhatConvertsToAnInteger()
    {
        Assert.That(Eval("math.tointeger('10'), math.tointeger(3.0), math.tointeger('34.0'), math.tointeger(' 0x10 ')"), Is.EqualTo("10\t3\t34\t16"));
        Assert.That(Eval("math.tointeger('34.3'), math.tointeger(math.pi), math.tointeger({}), math.tointeger(0/0)"), Is.EqualTo("nil\tnil\tnil\tnil"));
        Assert.That(ErrorOf("math.tointeger()"), Does.Contain("value expected"));
    }

    // ---- table

    [Test]
    public void InsertAndRemoveCheckThePosition()
    {
        Assert.That(ErrorOf("table.insert({1, 2, 3}, 5, 1)"), Does.Contain("#2").And.Contain("(position out of bounds)"));
        Assert.That(ErrorOf("table.insert({1, 2, 3}, 0, 1)"), Does.Contain("#2").And.Contain("(position out of bounds)"));
        Assert.That(ErrorOf("table.remove({1, 2, 3}, 7)"), Does.Contain("#2").And.Contain("(position out of bounds)"));
        Assert.That(Eval("(function() local t = {1, 2, 3} return table.remove(t, 4), table.remove(t, 1), #t end)()"), Is.EqualTo("nil\t1\t2"));
    }

    [Test]
    public void InsertWrapsAroundAtTheLargestLength()
    {
        Assert.That(Eval("(function() local t = setmetatable({}, {__len = function() return math.maxinteger end}) table.insert(t, 20) return rawget(t, math.mininteger) end)()"), Is.EqualTo("20"));
    }

    // ---- os

    [Test]
    public void TimeReadsTheDateFieldsFromTheYear()
    {
        Assert.That(ErrorOf("os.time({ hour = 12 })"), Does.Contain("field 'year' missing in date table"));
        Assert.That(ErrorOf("os.time({ year = 2026 })"), Does.Contain("field 'month' missing in date table"));
        Assert.That(ErrorOf("os.time({ year = 2026, month = 1 })"), Does.Contain("field 'day' missing in date table"));
        Assert.That(ErrorOf("os.time({ year = 1000, month = 1, day = 1, hour = 1.5 })"), Does.Contain("field 'hour' is not an integer"));
    }

    [TestCase("{ year = -(1 << 31) + 1899, month = 1, day = 1 }", "year")]
    [TestCase("{ year = -(1 << 31), month = 1, day = 1 }", "year")]
    [TestCase("{ year = (1 << 31) + 1900, month = 1, day = 1 }", "year")]
    [TestCase("{ year = 0, month = 1, day = 2^32 }", "day")]
    [TestCase("{ year = 0, month = -((1 << 31) + 1), day = 1 }", "month")]
    public void TimeFieldsMustFitAnInt(string date, string field)
    {
        Assert.That(ErrorOf("os.time(" + date + ")"), Does.Contain("field '" + field + "' is out-of-bound"));
    }

    [Test]
    public void TimeAndDateCoverWhatA64BitTimeTCovers()
    {
        Assert.That(Eval("os.date('!%Y-%m-%d %H:%M:%S', 67767976233532799)"), Is.EqualTo("2147483647-12-31 23:59:59"));
        Assert.That(Eval("os.date('!%Y-%m-%d %a', -62198755200)"), Is.EqualTo("-1-01-01 Fri"));
        Assert.That(Eval("os.date('!%Y %C %y', 253402300800)"), Is.EqualTo("10000 100 00"));
        Assert.That(Eval("type(os.time({ year = (1 << 31) + 1899, month = 12, day = 31, hour = 23, min = 59, sec = 59 }))"), Is.EqualTo("number"));
        Assert.That(ErrorOf("os.time({ year = (1 << 31) + 1899, month = 12, day = 31, hour = 23, min = 59, sec = 60 })"), Does.Contain("time result cannot be represented in this installation"));
        Assert.That(ErrorOf("os.date('%Y', 2^60)"), Does.Contain("date result cannot be represented in this installation"));
    }

    [Test]
    public void TimeNormalizesTheFields()
    {
        Assert.That(Eval("""
            (function()
              local t = { year = 2005, month = 1, day = 1, hour = 1, min = 0, sec = -3602 }
              os.time(t)
              return t.year, t.month, t.day, t.hour, t.min, t.sec, t.yday, t.wday
            end)()
            """), Is.EqualTo("2004\t12\t31\t23\t59\t58\t366\t6"));
    }

    [Test]
    public void DateWritesConversionsAsTheCLibrary()
    {
        Assert.That(Eval("os.date('!%C|%y|%G|%g|%V|%U|%W|%u|%w|%j|%F|%D|%r|%Z|%z', 0)"), Is.EqualTo("19|70|1970|70|01|00|00|4|4|001|1970-01-01|01/01/70|12:00:00 AM|GMT|+0000"));
        Assert.That(Eval("os.date('!%Ec|%EY|%Od|%OV', 1700000000)"), Is.EqualTo("Tue Nov 14 22:13:20 2023|2023|14|46"));
        Assert.That(Eval("os.date('\\0\\0') == '\\0\\0', os.date('!\\0\\0') == '\\0\\0', os.date(''), type(os.date('*t\\0x'))"), Is.EqualTo("true\ttrue\t\ttable"));
    }

    [TestCase("%", "'%'")]
    [TestCase("%9", "'%9'")]
    [TestCase("%E", "'%E'")]
    [TestCase("%Ea", "'%Ea'")]
    [TestCase("%Ez more", "'%Ez more'")]
    public void DateNamesTheInvalidConversion(string format, string named)
    {
        Assert.That(ErrorOf("os.date('" + format + "')"), Does.Contain("(invalid conversion specifier " + named + ")"));
    }

    // ---- io

    [Test]
    public void FileMethodsAreInTheirOwnTable()
    {
        Assert.That(Eval("""
            (function()
              local mt = getmetatable(io.stdout)
              return mt.__name, rawequal(mt.__index, mt), mt.__close == mt.__gc, io.stdout.__gc, type(mt.__index.read)
            end)()
            """), Is.EqualTo("FILE*\tfalse\ttrue\tnil\tfunction"));
    }

    [Test]
    public void CloseMetamethodClosesTheFile()
    {
        Assert.That(Eval("""
            (function()
              local f = assert(io.open('closed.txt', 'w'))
              getmetatable(f).__close(f, nil)
              getmetatable(io.stdout).__close(io.stdout, nil)
              return io.type(f), tostring(f), io.type(io.stdout)
            end)()
            """), Is.EqualTo("closed file\tfile (closed)\tfile"));
    }

    [Test]
    public void LinesOfAFileNameReturnTheFileToClose()
    {
        Assert.That(Eval("""
            (function()
              local f = assert(io.open('lines.txt', 'w')) f:write('a\nb\n') f:close()
              local it, state, control, file = io.lines('lines.txt')
              local before = io.type(file)
              local n = 0
              for l in it do n = n + 1 end
              f = assert(io.open('lines.txt'))
              local count = select('#', f:lines())
              f:close()
              return select('#', io.lines('lines.txt')), state, control, before, n, io.type(file), count
            end)()
            """), Is.EqualTo("4\tnil\tnil\tfile\t2\tclosed file\t1"));
        Assert.That(ErrorOf("local it = io.lines('lines.txt') while it() do end it()"), Does.Contain("file is already closed"));
    }

    [Test]
    public void LinesChecksItsFormatsWhenItReads()
    {
        Run("local f = assert(io.open('formats.txt', 'w')) f:write('1 2\\n') f:close()");
        Assert.That(ErrorOf("local it = io.lines('formats.txt', 'x') it()"), Does.Contain("(invalid format)"));
        Assert.That(Eval("(function() local it = io.lines('formats.txt', 'n', 'n') return it() end)()"), Is.EqualTo("1\t2"));
        Run("local f = assert(io.open('many.txt', 'w')) f:write(string.rep('a', 300)) f:close()");
        Assert.That(Eval("(function() local t = {} for i = 1, 250 do t[i] = 1 end return #{io.lines('many.txt', table.unpack(t))()} end)()"), Is.EqualTo("250"));
        Assert.That(ErrorOf("local t = {} for i = 1, 251 do t[i] = 'l' end io.lines('formats.txt', table.unpack(t))"), Does.Contain("(too many arguments)"));
    }

    [Test]
    public void ClosedDefaultFilesAreTheDefaultOnes()
    {
        Assert.That(ErrorOf("io.output('out.txt') io.close() io.write('x')"), Does.Contain("default output file is closed"));
        Assert.That(ErrorOf("io.output(io.stdout) io.input('out.txt') io.close(io.input()) io.read()"), Does.Contain("default input file is closed"));
        Assert.That(ErrorOf("io.lines()"), Does.Contain("attempt to use a closed file"));
    }

    [Test]
    public void OpenChecksTheMode()
    {
        Run("assert(io.open('mode.txt', 'w')):close()");
        foreach (string mode in new[] { "rw", "rb+", "r+bk", "", "+", "b", "x" })
        {
            Assert.That(ErrorOf("io.open('mode.txt', '" + mode + "')"), Does.Contain("(invalid mode)"), mode);
        }

        Assert.That(Eval("io.type(io.open('mode.txt', 'r+b')), io.type(io.open('mode.txt', 'rbb')), io.type(io.open('mode.txt', 'r\\0x'))"), Is.EqualTo("file\tfile\tfile"));
        Assert.That(ErrorOf("io.popen('ls', 'r+')"), Does.Contain("(invalid mode)"));
        Assert.That(ErrorOf("io.popen('ls')"), Does.Contain("'popen' not supported"));
    }

    [Test]
    public void FailedReadsAndWritesGiveTheErrno()
    {
        Assert.That(Eval("(function() local f = assert(io.open('ebadf.txt', 'w')) local a, b, c = f:read() f:close() return a, b, c end)()"), Is.EqualTo("nil\tBad file descriptor\t9"));
        Assert.That(Eval("(function() local f = assert(io.open('ebadf.txt')) local a, b, c = f:write('x') f:close() return a, b, c end)()"), Is.EqualTo("nil\tBad file descriptor\t9"));
        Assert.That(Eval("io.stdout:read()"), Is.EqualTo("nil\tBad file descriptor\t9"));
    }
}
