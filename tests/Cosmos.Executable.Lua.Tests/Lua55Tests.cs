// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// What Lua 5.5 does where Lua 5.4 did otherwise: the language (global
/// declarations, named vararg tables, read-only loop variables), the way
/// floats are written, and the libraries; the results are those of the
/// reference implementation, 5.5.1.
/// </summary>
public class Lua55Tests : LuaTest
{
    // ---- global declarations

    [Test]
    public void AGlobalDeclarationCanInitializeTheGlobal()
    {
        Assert.That(Run("global x = 10 global print print(x)"), Is.EqualTo("10\n"));
        Assert.That(Eval("x, _G.x"), Is.EqualTo("10\t10"));
    }

    [Test]
    public void InTheScopeOfADeclarationEveryGlobalMustBeDeclared()
    {
        Assert.That(ErrorOf("global print; global x = 10 print(x) y = 1"), Does.Contain(":1: variable 'y' not declared"));
        Assert.That(Run("do global x end x = 1 print(x)"), Is.EqualTo("1\n"));
        Assert.That(Run("global *; y = 2 print(y)"), Is.EqualTo("2\n"));
    }

    [Test]
    public void AConstGlobalIsReadOnly()
    {
        Assert.That(ErrorOf("global <const> print; print = 1"), Does.Contain("attempt to assign to const variable 'print'"));
        Assert.That(ErrorOf("global <const> *; x = 1"), Does.Contain("attempt to assign to const variable 'x'"));
        Assert.That(ErrorOf("global <close> x"), Does.Contain("global variables cannot be to-be-closed"));
    }

    [Test]
    public void AnInitializedGlobalMustNotExistYet()
    {
        Assert.That(ErrorOf("x = 1 global x = 2"), Does.Contain("global 'x' already defined"));
        Assert.That(ErrorOf("global function print () end"), Does.Contain("global 'print' already defined"));
    }

    [Test]
    public void GlobalIsAWordOnlyWhereItStartsADeclaration()
    {
        // LUA_COMPAT_GLOBAL, on in the reference build
        Assert.That(Eval("(function () local global = 3 global = global + 1 return global end)()"), Is.EqualTo("4"));
    }

    [Test]
    public void AGotoCannotJumpIntoTheScopeOfADeclaration()
    {
        Assert.That(ErrorOf("goto l; local x; ::l:: print(x)"), Does.Contain("<goto l> at line 1 jumps into the scope of 'x'"));
        Assert.That(ErrorOf("goto l; global *; ::l:: print(x)"), Does.Contain("jumps into the scope of '*'"));
    }

    // ---- varargs and loops

    [Test]
    public void AVarargParameterIsATableOfTheExtraArguments()
    {
        Assert.That(Eval("(function (...t) return t.n, t[1], t[2], t[3] end)(10, nil, 30)"), Is.EqualTo("3\t10\tnil\t30"));
        Assert.That(Eval("(function (a, ...t) return t[2], t.n end)(1, 'x', 'y')"), Is.EqualTo("y\t2"));
        Assert.That(Eval("(function (...t) return select('#', ...), ... end)('a', 'b')"), Is.EqualTo("2\ta\tb"));
    }

    [Test]
    public void TheVarargTableIsARealTableOnceTheFunctionChangesIt()
    {
        Assert.That(Eval("(function (...t) t[1] = 'c' t.n = 1 return ..., t end)('a', 'b') == nil"), Is.EqualTo("false"));
        Assert.That(Eval("(function (...t) t.n = 1 return select('#', ...) end)(2, 3)"), Is.EqualTo("1"));
        Assert.That(ErrorOf("(function (...t) t.n = 'x' return ... end)()"), Does.Contain("vararg table has no proper 'n'"));
    }

    [Test]
    public void TheControlVariablesOfALoopAreReadOnly()
    {
        Assert.That(ErrorOf("for i = 1, 3 do i = i + 1 end"), Does.Contain("attempt to assign to const variable 'i'"));
        Assert.That(ErrorOf("for k, v in pairs({}) do k = 1 end"), Does.Contain("attempt to assign to const variable 'k'"));
        Assert.That(Run("for k, v in pairs({a = 1}) do v = 2 print(k, v) end"), Is.EqualTo("a\t2\n"));
    }

    [Test]
    public void PairsGivesAClosingValue()
    {
        Assert.That(Run(@"
            local mt = {__pairs = function (t)
              return next, t, nil, setmetatable({}, {__close = function () print('closed') end})
            end}
            for k in pairs(setmetatable({}, mt)) do end"), Is.EqualTo("closed\n"));
        Assert.That(Eval("select('#', pairs({}))"), Is.EqualTo("4"));
    }

    [Test]
    public void ACloseMethodGetsAnErrorOnlyIfThereIsOne()
    {
        Assert.That(Run(@"
            local t = setmetatable({}, {__close = function (...) print('close', select('#', ...)) end})
            do local x <close> = t end
            pcall(function () local y <close> = t error('e', 0) end)"), Is.EqualTo("close\t1\nclose\t2\n"));
    }

    // ---- calls and errors

    [Test]
    public void AChainOfCallMetamethodsHasAtMost15Objects()
    {
        Assert.That(Eval(@"(function ()
            local f = setmetatable({}, {__call = function (self, ...) return select('#', ...) end})
            for i = 1, 14 do f = setmetatable({}, {__call = f}) end
            local ok = pcall(f)
            f = setmetatable({}, {__call = f})
            return ok, pcall(f)
          end)()"), Is.EqualTo("true\tfalse\t'__call' chain too long"));
    }

    [Test]
    public void GetinfoCountsTheArgumentsCallMetamethodsAdd()
    {
        Assert.That(Eval(@"(function ()
            local function u (...) return debug.getinfo(1, 't').extraargs end
            return u(), setmetatable({}, {__call = setmetatable({}, {__call = u})})()
          end)()"), Is.EqualTo("0\t2"));
    }

    [Test]
    public void ANilErrorBecomesAMessage()
    {
        Assert.That(Eval("pcall(error)"), Is.EqualTo("false\t<no error object>"));
        Assert.That(Eval("pcall(error, nil)"), Is.EqualTo("false\t<no error object>"));
    }

    [Test]
    public void TracebacksNameFunctionsAsTheCodeCallsThem()
    {
        Assert.That(Eval("debug.traceback('hi', 0):find(\"'traceback'\") ~= nil"), Is.EqualTo("true"));
        Assert.That(Eval("debug.traceback('hi'):find(\"'debug.traceback'\") == nil"), Is.EqualTo("true"));
    }

    // ---- numbers

    [TestCase("0.1", "0.1")]
    [TestCase("1/3", "0.33333333333333331")]
    [TestCase("-1/3", "-0.33333333333333331")]
    [TestCase("2^53", "9007199254740992.0")]
    [TestCase("123.456", "123.456")]
    [TestCase("1e300 * 10", "1e+301")]
    public void AFloatIsWrittenWithTheDigitsToReadItBack(string expression, string expected)
    {
        Assert.That(Eval(expression), Is.EqualTo(expected));
        Assert.That(Eval("tonumber(tostring(" + expression + ")) == " + expression), Is.EqualTo("true"));
    }

    [Test]
    public void WriteWritesFloatsAsTostringDoes()
    {
        Assert.That(Run("io.write(1.0, ' ', 1/3, ' ', 2)"), Is.EqualTo("1.0 0.33333333333333331 2"));
    }

    // ---- libraries

    [Test]
    public void TableCreatePreallocatesATable()
    {
        Assert.That(Eval("type(table.create(10, 5)), #table.create(3)"), Is.EqualTo("table\t0"));
        Assert.That(ErrorOf("table.create(-1)"), Does.Contain("bad argument #1 to 'create' (out of range)"));
    }

    [Test]
    public void Utf8OffsetGivesTheEndOfTheCharacterToo()
    {
        Assert.That(Eval("utf8.offset('a\\u{F1}b', 2)"), Is.EqualTo("2\t3"));
        Assert.That(Eval("utf8.offset('a\\u{F1}b', 3)"), Is.EqualTo("4\t4"));
        Assert.That(Eval("utf8.offset('a\\u{F1}b', 4)"), Is.EqualTo("5\t5"));
        Assert.That(Eval("utf8.offset('a\\u{F1}b', 5)"), Is.EqualTo("nil"));
    }

    [Test]
    public void TheDeprecatedMathFunctionsAreGone()
    {
        Assert.That(Eval("math.pow, math.log10, math.cosh, math.atan2"), Is.EqualTo("nil\tnil\tnil\tnil"));
        Assert.That(Eval("math.frexp(8)"), Is.EqualTo("0.5\t4"));
        Assert.That(Eval("math.ldexp(0.5, 4)"), Is.EqualTo("8.0"));
    }

    [Test]
    public void TheCollectorTakesItsParametersByName()
    {
        Assert.That(Eval("collectgarbage('param', 'pause'), collectgarbage('param', 'stepmul'), collectgarbage('param', 'stepsize')"), Is.EqualTo("250\t200\t9600"));
        Assert.That(Eval("collectgarbage('param', 'minormul'), collectgarbage('param', 'majorminor'), collectgarbage('param', 'minormajor')"), Is.EqualTo("20\t50\t68"));
        Assert.That(Eval("collectgarbage('param', 'pause', 123), collectgarbage('param', 'pause')"), Is.EqualTo("250\t118"));
        Assert.That(ErrorOf("collectgarbage('setpause')"), Does.Contain("invalid option 'setpause'"));
    }

    [Test]
    public void ARunningCoroutineCanCloseItself()
    {
        Assert.That(Run(@"
            local co = coroutine.wrap(function ()
              local x <close> = setmetatable({}, {__close = function () print('closed') end})
              coroutine.close()
              print('not reached')
            end)
            print(pcall(co))"), Is.EqualTo("closed\ntrue\n"));
        Assert.That(Eval("pcall(coroutine.close, coroutine.running())"), Is.EqualTo("false\tcannot close main thread"));
    }

    [Test]
    public void OnlyLuaFunctionsAndTextOrBinaryModesLoad()
    {
        Assert.That(ErrorOf("string.dump(print)"), Does.Contain("bad argument #1 to 'dump' (Lua function expected)"));
        Assert.That(ErrorOf("load('', '', 'B')"), Does.Contain("bad argument #3 to 'load' (invalid mode)"));
    }

    [Test]
    public void ADumpedFunctionLoadsBack()
    {
        Assert.That(Eval(@"load(string.dump(function (a, ...t)
            local s = 'a long string, longer than forty characters, used twice'
            return a + t.n, s == 'a long string, longer than forty characters, used twice', -7, 1.5
          end))(2, 'x', 'y')"), Is.EqualTo("4\ttrue\t-7\t1.5"));
    }
}
