// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// The string and utf8 libraries as Lua 5.4 has them: the results and the
/// messages are those of the reference lua 5.4.9.
/// </summary>
public class LuaStrLib54Tests : LuaTest
{
    [Test]
    public void PositionsAreClippedAsIn54()
    {
        Assert.That(Eval("string.sub('123456789', math.mininteger, -4), string.sub('123456789', -100, 3), string.byte('hi', -3), string.byte('abc', 0)"),
            Is.EqualTo("123456\t123\tnil"));
        Assert.That(Eval("string.find('abc', 'c', -1), string.find('abc', '', 4), string.find('abc', '', 5), string.match('abc', '()', 10)"),
            Is.EqualTo("3\t4\tnil\tnil"));
        Assert.That(Eval("string.unpack('B', 'abc', -10), string.unpack('i2', 'abcd', -2), string.unpack('c0', 'abc', 0)"),
            Is.EqualTo("97\t25699\t\t1"));
        Assert.That(ErrorOf("string.unpack('c0', 'abc', 5)"), Is.EqualTo("test:1: bad argument #3 to 'unpack' (initial position out of string)"));
    }

    [Test]
    public void GmatchTakesAnInitialPosition()
    {
        Assert.That(Eval("string.gmatch('10 20 30', '%d+', 3)(), string.gmatch('11 21 31', '%d+', -4)()"), Is.EqualTo("20\t1"));
        Assert.That(Run("local n = 0 for k in string.gmatch('11 21 31', '%w*', 9) do n = n + 1 end print(n)"), Is.EqualTo("1\n"));
        Assert.That(Run("local n = 0 for k in string.gmatch('11 21 31', '%w*', 10) do n = n + 1 end print(n)"), Is.EqualTo("0\n"));
    }

    [Test]
    public void GsubKeepsTheSubjectWhenNothingChanges()
    {
        Assert.That(Eval("string.gsub('hello', '%w+', {x = 1}), string.gsub('hello', 'l', '%0%0')"), Is.EqualTo("hello\thellllo\t2"));
        Assert.That(Run(@"
            local s = string.rep('a', 100)
            local same = string.gsub(s, '.', function () end)
            local copy = string.gsub(s, '.', '%0')
            print(string.format('%p', s) == string.format('%p', same), string.format('%p', s) == string.format('%p', copy), copy == s)"),
            Is.EqualTo("true\tfalse\ttrue\n"));
        Assert.That(Eval("string.gsub('hello world', '()o', '%1')"), Is.EqualTo("hell5 w8rld\t2"));
        Assert.That(ErrorOf("string.gsub('abc', 'b', true)"), Is.EqualTo("test:1: bad argument #3 to 'gsub' (string/function/table expected, got boolean)"));
        Assert.That(ErrorOf("string.gsub('abc', 'b')"), Is.EqualTo("test:1: bad argument #3 to 'gsub' (string/function/table expected, got no value)"));
        Assert.That(ErrorOf("string.gsub('abc', 'b', '%')"), Is.EqualTo("test:1: invalid use of '%' in replacement string"));
    }

    [Test]
    public void PatternsHaveAMatchDepth()
    {
        Assert.That(Eval("#string.match(string.rep('a', 199), string.rep('.?', 199))"), Is.EqualTo("199"));
        Assert.That(ErrorOf("string.match(string.rep('a', 300), string.rep('.?', 300))"), Is.EqualTo("test:1: pattern too complex"));
    }

    [Test]
    public void StringsHaveArithmeticMetamethods()
    {
        Assert.That(Eval("getmetatable('').__add('10', 1), getmetatable('').__mul('3', '4'), getmetatable('').__div('1', '2'), getmetatable('').__unm('2'), getmetatable('').__idiv('7', 2.0)"),
            Is.EqualTo("11\t12\t0.5\t-2\t3.0"));
        Assert.That(Eval("getmetatable('').__mod('7', ' 0x3 '), getmetatable('').__pow('2', '3'), getmetatable('').__sub('1e1', 1)"), Is.EqualTo("1\t8.0\t9.0"));
        Assert.That(Eval("math.type(getmetatable('').__add('1', '2')), math.type(getmetatable('').__add('1.0', '2')), getmetatable('').__band, getmetatable('').__index == string"),
            Is.EqualTo("integer\tfloat\tnil\ttrue"));
        Assert.That(Eval("getmetatable('').__add('1', setmetatable({}, {__add = function (a, b) return 'other' end}))"), Is.EqualTo("other"));
        Assert.That(ErrorOf("getmetatable('').__add('abc', 1)"), Is.EqualTo("test:1: attempt to add a 'string' with a 'number'"));
        Assert.That(ErrorOf("getmetatable('').__sub({}, '1')"), Is.EqualTo("test:1: attempt to sub a 'table' with a 'string'"));
        Assert.That(ErrorOf("getmetatable('').__unm('x')"), Is.EqualTo("test:1: attempt to unm a 'string' with a 'nil'"));
        Assert.That(ErrorOf("getmetatable('').__idiv('1', '0')"), Does.Contain("attempt to divide by zero"));
    }

    [Test]
    public void FormatChecksItsConversions()
    {
        Assert.That(Eval("string.format('%5s|%-5s|%.2s', 'ab', 'ab', 'abc'), string.format('%#x %#o %+d % d %05d %.3d', 255, 8, 5, 5, -42, 7)"),
            Is.EqualTo("   ab|ab   |ab\t0xff 010 +5  5 -0042 007"));
        Assert.That(ErrorOf("string.format('%10q', 'x')"), Is.EqualTo("test:1: specifier '%q' cannot have modifiers"));
        Assert.That(ErrorOf("string.format('%t', 1)"), Is.EqualTo("test:1: invalid conversion '%t' to 'format'"));
        Assert.That(ErrorOf("string.format('%F', 1)"), Is.EqualTo("test:1: invalid conversion '%F' to 'format'"));
        Assert.That(ErrorOf("string.format('%', 1)"), Is.EqualTo("test:1: invalid conversion '%' to 'format'"));
        Assert.That(ErrorOf("string.format('%#i', 1)"), Is.EqualTo("test:1: invalid conversion specification: '%#i'"));
        Assert.That(ErrorOf("string.format('%05s', 'x')"), Is.EqualTo("test:1: invalid conversion specification: '%05s'"));
        Assert.That(ErrorOf("string.format('%.10c', 65)"), Is.EqualTo("test:1: invalid conversion specification: '%.10c'"));
        Assert.That(ErrorOf("string.format('%100d', 1)"), Is.EqualTo("test:1: invalid conversion specification: '%100d'"));
        Assert.That(ErrorOf("string.format('%1.100d', 1)"), Is.EqualTo("test:1: invalid conversion specification: '%1.100d'"));
        Assert.That(ErrorOf("string.format('%' .. string.rep('0', 30) .. 'd', 1)"), Is.EqualTo("test:1: invalid format (too long)"));
        Assert.That(ErrorOf("string.format('%d %d', 1)"), Is.EqualTo("test:1: bad argument #3 to 'format' (no value)"));
        Assert.That(ErrorOf("string.format('%10s', 'a\\0')"), Is.EqualTo("test:1: bad argument #2 to 'format' (string contains zeros)"));
    }

    [Test]
    public void FormatQuotesEveryFloat()
    {
        Assert.That(Eval("string.format('%q %q %q %q %q', 1/0, -1/0, 0/0, 1.5, math.mininteger)"),
            Is.EqualTo("1e9999 -1e9999 (0/0) 0x1.8p+0 0x8000000000000000"));
        Assert.That(Eval("load('return ' .. string.format('%q', -1/0))() == -1/0, load('return ' .. string.format('%q', 0.1))() == 0.1"),
            Is.EqualTo("true\ttrue"));
    }

    [Test]
    public void FormatWritesPointers()
    {
        Assert.That(Eval("string.format('%p', 1), string.format('%10p', false), string.format('%-8p|', nil)"), Is.EqualTo("(null)\t    (null)\t(null)  |"));
        Assert.That(Eval("#string.format('%90p', {}), string.format('%p', print) == string.format('%p', print), string.format('%p', {}) == string.format('%p', {})"),
            Is.EqualTo("90\ttrue\tfalse"));
        Assert.That(Eval("string.format('%p', string.rep('a', 10)) == string.format('%p', string.rep('aa', 5)), string.format('%p', string.rep('a', 300)) == string.format('%p', string.rep('a', 300))"),
            Is.EqualTo("true\tfalse"));
        Assert.That(Run("local t = {} print(tostring(t) == 'table: ' .. string.format('%p', t))"), Is.EqualTo("true\n"));
        Assert.That(ErrorOf("string.format('%.2p', {})"), Is.EqualTo("test:1: invalid conversion specification: '%.2p'"));
    }

    [Test]
    public void PackKnowsThe54Formats()
    {
        Assert.That(Eval("string.unpack('>n', string.pack('>n', 0.1)) == 0.1, string.unpack('<d', string.pack('<d', -2.5)), string.unpack('f', string.pack('f', 0.5)), string.packsize('fdn')"),
            Is.EqualTo("true\t-2.5\t0.5\t20"));
        Assert.That(ErrorOf("string.unpack('z', 'abc')"), Is.EqualTo("test:1: bad argument #2 to 'unpack' (unfinished string for format 'z')"));
        Assert.That(ErrorOf("string.packsize('s')"), Is.EqualTo("test:1: bad argument #1 to 'packsize' (variable-length format)"));
        Assert.That(ErrorOf("string.packsize('i17')"), Is.EqualTo("test:1: integral size (17) out of limits [1,16]"));
        Assert.That(ErrorOf("string.unpack('i16', string.rep('\\3', 16))"), Is.EqualTo("test:1: 16-byte integer does not fit into Lua Integer"));
        Assert.That(ErrorOf("string.unpack('c5', 'abcd')"), Is.EqualTo("test:1: bad argument #2 to 'unpack' (data string too short)"));
    }

    [Test]
    public void Utf8HasALaxMode()
    {
        Assert.That(Eval("utf8.char(0x7FFFFFFF):byte(1, -1)"), Is.EqualTo("253\t191\t191\t191\t191\t191"));
        Assert.That(Eval("utf8.len('\\xF4\\x90\\x80\\x80'), utf8.len('\\xF4\\x90\\x80\\x80', 1, -1, true), utf8.codepoint('\\xED\\xA0\\x80', 1, 1, true)"),
            Is.EqualTo("nil\t1\t55296"));
        Assert.That(Run("for p, c in utf8.codes('a\\xFD\\xBF\\xBF\\xBF\\xBF\\xBF', true) do io.write(p, ':', c, ' ') end"), Is.EqualTo("1:97 2:2147483647 "));
        Assert.That(Eval("utf8.charpattern == '[\\0-\\x7F\\xC2-\\xFD][\\x80-\\xBF]*'"), Is.EqualTo("true"));
        Assert.That(ErrorOf("for _ in utf8.codes('ab\\xFF') do end"), Is.EqualTo("test:1: invalid UTF-8 code"));
        Assert.That(ErrorOf("for _ in utf8.codes('\\u{D800}') do end"), Is.EqualTo("test:1: invalid UTF-8 code"));
        Assert.That(ErrorOf("utf8.codepoint('\\xED\\xA0\\x80')"), Is.EqualTo("test:1: invalid UTF-8 code"));
    }

    [Test]
    public void Utf8MessagesSayOutOfBounds()
    {
        Assert.That(ErrorOf("utf8.char(0x80000000)"), Is.EqualTo("test:1: bad argument #1 to 'char' (value out of range)"));
        Assert.That(ErrorOf("utf8.len('abc', 0)"), Is.EqualTo("test:1: bad argument #2 to 'len' (initial position out of bounds)"));
        Assert.That(ErrorOf("utf8.len('abc', 1, 4)"), Is.EqualTo("test:1: bad argument #3 to 'len' (final position out of bounds)"));
        Assert.That(ErrorOf("utf8.codepoint('abc', 4)"), Is.EqualTo("test:1: bad argument #3 to 'codepoint' (out of bounds)"));
        Assert.That(ErrorOf("utf8.offset('abc', 1, 5)"), Is.EqualTo("test:1: bad argument #3 to 'offset' (position out of bounds)"));
        Assert.That(ErrorOf("utf8.codes('\\x80')"), Is.EqualTo("test:1: bad argument #1 to 'codes' (invalid UTF-8 code)"));
        Assert.That(ErrorOf("utf8.offset('\\x80', 1)"), Is.EqualTo("test:1: initial position is a continuation byte"));
    }
}
