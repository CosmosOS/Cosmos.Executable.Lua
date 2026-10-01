// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System;
using System.IO;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

public class LuaInterpreterTests : LuaTest
{
    [Test]
    public void PrintWritesItsArgumentsSeparatedByTabs()
    {
        Assert.That(Run("print('a', 1, nil, true)"), Is.EqualTo("a\t1\tnil\ttrue\n"));
    }

    [Test]
    public void GlobalsLiveOnBetweenChunks()
    {
        Run("x = 40");
        Assert.That(Eval("x + 2"), Is.EqualTo("42"));
    }

    [Test]
    public void ARuntimeErrorBecomesALuaExceptionWithATraceback()
    {
        LuaException e = Assert.Throws<LuaException>(() => Lua.DoString("local function f() error('boom') end\nf()", "=script"))!;

        Assert.That(e.Message, Is.EqualTo("script:1: boom"));
        Assert.That(e.LuaStackTrace, Does.StartWith("stack traceback:"));
        Assert.That(e.LuaStackTrace, Does.Contain("script:2"));
    }

    [Test]
    public void AnErrorObjectThatIsNotAStringIsDescribed()
    {
        Assert.That(ErrorOf("error({})"), Is.EqualTo("(error object is a table value)"));
        Assert.That(ErrorOf("error(setmetatable({}, { __tostring = function() return 'custom' end }))"), Is.EqualTo("custom"));
    }

    [Test]
    public void ASyntaxErrorHasNoTraceback()
    {
        LuaException e = Assert.Throws<LuaException>(() => Lua.DoString("x = = 1", "=script"))!;

        Assert.That(e.Message, Does.StartWith("script:1:"));
        Assert.That(e.LuaStackTrace, Is.Null);
    }

    [Test]
    public void TheStateIsUsableAfterAnError()
    {
        Assert.Throws<LuaException>(() => Lua.DoString("error('first')"));
        Assert.That(Lua.State.GetTop(), Is.Zero);
        Assert.That(Eval("1 + 1"), Is.EqualTo("2"));
    }

    [Test]
    public void DoFileGivesTheScriptItsArguments()
    {
        File.WriteAllText(Path.Combine(Directory, "args.lua"), "print(arg[0], arg[1], arg[2], select('#', ...), ...)");

        Lua.DoFile("args.lua", "one", "two");

        Assert.That(Output, Is.EqualTo("args.lua\tone\ttwo\t2\tone\ttwo\n"));
    }

    [Test]
    public void DoFileSkipsAShebangLineAndKeepsTheLineNumbers()
    {
        File.WriteAllText(Path.Combine(Directory, "script.lua"), "#!/usr/bin/lua\nprint('ran')\nerror('line 3')");

        LuaException e = Assert.Throws<LuaException>(() => Lua.DoFile("script.lua"))!;

        Assert.That(Output, Is.EqualTo("ran\n"));
        Assert.That(e.Message, Is.EqualTo("script.lua:3: line 3"));
    }

    [Test]
    public void DoFileOfAMissingFileSaysItCannotOpenIt()
    {
        LuaException e = Assert.Throws<LuaException>(() => Lua.DoFile("missing.lua"))!;

        Assert.That(e.Message, Does.StartWith("cannot open missing.lua"));
    }

    [Test]
    public void RequireAndDofileFindFilesFromTheWorkingDirectory()
    {
        System.IO.Directory.CreateDirectory(Path.Combine(Directory, "lib"));
        File.WriteAllText(Path.Combine(Directory, "lib", "greet.lua"), "return { hello = function(n) return 'hello ' .. n end }");
        File.WriteAllText(Path.Combine(Directory, "lib", "init.lua"), "return 'lib package'");
        File.WriteAllText(Path.Combine(Directory, "value.lua"), "return 41 + 1");

        Assert.That(Eval("require('lib.greet').hello('cosmos')"), Is.EqualTo("hello cosmos"));
        Assert.That(Eval("require('lib.greet') == require('lib.greet')"), Is.EqualTo("true"));
        Assert.That(Eval("require('lib')"), Is.EqualTo("lib package"));
        Assert.That(Eval("dofile('value.lua')"), Is.EqualTo("42"));
        Assert.That(Eval("loadfile('value.lua')()"), Is.EqualTo("42"));
        Assert.That(ErrorOf("require('nothing')"), Does.Contain("module 'nothing' not found"));
    }

    [Test]
    public void ANetExceptionInACSharpFunctionIsALuaErrorPcallCatches()
    {
        Lua.State.PushCSharpFunction(static _ => throw new InvalidOperationException("from C#"));
        Lua.State.SetGlobal("fail");

        Assert.That(Run("print(pcall(fail))"), Is.EqualTo("false\tInvalidOperationException: from C#\n"));
        Assert.That(ErrorOf("fail()"), Is.EqualTo("InvalidOperationException: from C#"));
    }

    [Test]
    public void CSharpFunctionsAndUserdataExtendTheLanguage()
    {
        ILuaState state = Lua.State;
        state.L_NewMetaTable("Counter");
        state.PushCSharpFunction(static lua =>
        {
            int[] counter = (int[])lua.L_CheckUData(1, "Counter");
            lua.PushInteger(++counter[0]);
            return 1;
        });
        state.SetField(-2, "__call");
        state.Pop(1);
        state.PushCSharpFunction(static lua =>
        {
            lua.NewUserData(new int[1]);
            lua.L_SetMetaTable("Counter");
            return 1;
        });
        state.SetGlobal("counter");

        Assert.That(Run("local c = counter(); c(); c(); print(c(), type(c))"), Is.EqualTo("3\tuserdata\n"));
        Assert.That(ErrorOf("getmetatable(counter()).__call({})"), Does.Contain("Counter expected, got table"));
    }

    [Test]
    public void ExitEndsTheScriptWithItsCode()
    {
        LuaExitException e = Assert.Throws<LuaExitException>(() => Lua.DoString("print('before') os.exit(3) print('after')"))!;

        Assert.That(e.ExitCode, Is.EqualTo(3));
        Assert.That(Output, Is.EqualTo("before\n"));
    }

    [TestCase("os.exit()", 0)]
    [TestCase("os.exit(true)", 0)]
    [TestCase("os.exit(false)", 1)]
    [TestCase("pcall(os.exit, 4)", 4)]
    [TestCase("pcall(pcall, pcall, os.exit, 5)", 5)]
    [TestCase("xpcall(function() os.exit(6) end, function() print('handler') end)", 6)]
    [TestCase("coroutine.wrap(function() os.exit(7) end)()", 7)]
    [TestCase("coroutine.resume(coroutine.create(function() pcall(os.exit, 8) end))", 8)]
    [TestCase("local t = setmetatable({}, { __index = function() os.exit(9) end }) local x = t.x", 9)]
    [TestCase("table.sort({ 3, 2, 1 }, function() pcall(os.exit, 10) end)", 10)]
    public void NoPcallKeepsExit(string code, int expected)
    {
        LuaExitException e = Assert.Throws<LuaExitException>(() => Lua.DoString(code))!;

        Assert.That(e.ExitCode, Is.EqualTo(expected));
        Assert.That(Output, Is.Empty);
    }

    [Test]
    public void TheStateIsUsableAfterExit()
    {
        Assert.Throws<LuaExitException>(() => Lua.DoString("pcall(function() os.exit(1) end)"));

        Assert.That(Lua.State.GetTop(), Is.Zero);
        Assert.That(Run("print(pcall(error, 'still catches'))"), Is.EqualTo("false\tstill catches\n"));
    }

    [Test]
    public void ExecuteRunsCommandsThroughTheHost()
    {
        Assert.That(Eval("os.execute()"), Is.EqualTo("false"));

        Lua.ExecuteCommand = command => command == "ok" ? 0 : 2;

        Assert.That(Eval("os.execute()"), Is.EqualTo("true"));
        Assert.That(Run("print(os.execute('ok')) print(os.execute('fails'))"), Is.EqualTo("true\texit\t0\nnil\texit\t2\n"));
    }

    [Test]
    public void ThePromptPrintsExpressionsAndRunsStatements()
    {
        Lua.Input = new StringReader("1 + 1\nx = 'set'\nx\n=x .. '!'\n");

        int code = Lua.RunPrompt();

        Assert.That(code, Is.Zero);
        Assert.That(Output, Is.EqualTo("> 2\n> > set\n> set!\n> \n"));
    }

    [Test]
    public void ThePromptReadsTheRestOfAnUnfinishedStatement()
    {
        Lua.Input = new StringReader("for i = 1, 3 do\nprint(i)\nend\n");

        Lua.RunPrompt();

        Assert.That(Output, Is.EqualTo("> >> >> 1\n2\n3\n> \n"));
    }

    [Test]
    public void ThePromptReportsErrorsAndGoesOn()
    {
        Lua.Input = new StringReader("error('oops')\nx = = 1\nprint('still here')\n");

        Lua.RunPrompt();

        Assert.That(Output, Does.Contain("stdin:1: oops\nstack traceback:"));
        Assert.That(Output, Does.Contain("stdin:1: unexpected symbol near '='"));
        Assert.That(Output, Does.EndWith("still here\n> \n"));
    }

    [Test]
    public void ThePromptEndsAtExitWithItsCode()
    {
        Lua.Input = new StringReader("print('one')\nos.exit(5)\nprint('two')\n");

        Assert.That(Lua.RunPrompt(), Is.EqualTo(5));
        Assert.That(Output, Is.EqualTo("> one\n> "));
    }
}
