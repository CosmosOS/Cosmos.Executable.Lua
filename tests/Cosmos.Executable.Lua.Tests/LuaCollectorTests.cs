// This code is licensed under the BSD 3-Clause license (see LICENSE.txt for details)

using System.IO;
using NUnit.Framework;

namespace Cosmos.Executable.Lua.Tests;

/// <summary>
/// The collector as a script sees it: weak tables, finalizers, and the
/// count of the memory in use.
/// </summary>
public class LuaCollectorTests : LuaTest
{
    [Test]
    public void WeakKeysGoWhenNothingElseReachesThem()
    {
        Assert.That(Run(@"
            local w = setmetatable({}, {__mode = 'k'})
            local keep = {}
            w[keep] = 1; w[{}] = 2; w['name'] = 3
            collectgarbage()
            local n = 0
            for _ in pairs(w) do n = n + 1 end
            print(n, w[keep], w.name)"), Is.EqualTo("2\t1\t3\n"));
    }

    [Test]
    public void WeakValuesGoButStringsAreValues()
    {
        Assert.That(Run(@"
            local w = setmetatable({}, {__mode = 'v'})
            w[1] = {}; w[2] = string.rep('x', 100); w.f = function () end
            collectgarbage()
            print(w[1], #w[2], w.f)"), Is.EqualTo("nil\t100\tnil\n"));
    }

    [Test]
    public void AnEphemeronKeepsAValueWhileItsKeyLives()
    {
        Assert.That(Run(@"
            local e = setmetatable({}, {__mode = 'k'})
            local k = {}
            e[k] = {k}   -- the value refers to its own key
            e[{}] = {}
            collectgarbage()
            local n = 0
            for _ in pairs(e) do n = n + 1 end
            print(n, e[k][1] == k)"), Is.EqualTo("1\ttrue\n"));
    }

    [Test]
    public void FinalizersRunNewestFirstWithTheirObjectsResurrected()
    {
        Assert.That(Run(@"
            local log, back = {}, nil
            local mt = {__gc = function (o) log[#log + 1] = o.name; back = o end}
            do
              setmetatable({name = 'a'}, mt)
              setmetatable({name = 'b'}, mt)
            end
            collectgarbage()
            print(table.concat(log, ' '), back.name)"), Is.EqualTo("b a\ta\n"));
    }

    [Test]
    public void TheCollectorRunsAsTheScriptAllocates()
    {
        Assert.That(Run(@"
            local done = false
            setmetatable({}, {__gc = function () done = true end})
            local i = 0
            repeat local t = {}; i = i + 1 until done or i > 1000000
            print(done)"), Is.EqualTo("true\n"));
    }

    [Test]
    public void TheCountFollowsWhatTheScriptKeeps()
    {
        Assert.That(Run(@"
            collectgarbage()
            local before = collectgarbage('count')
            local t = {}
            for i = 1, 10000 do t[i] = {} end
            local during = collectgarbage('count')
            t = nil
            collectgarbage()
            local after = collectgarbage('count')
            print(during > before + 100, after < before + 10)"), Is.EqualTo("true\ttrue\n"));
    }

    [Test]
    public void TheOptionsAnswerAsLuaDoes()
    {
        Assert.That(Eval("collectgarbage('isrunning'), collectgarbage('step'), collectgarbage('incremental'), collectgarbage('generational')"),
            Is.EqualTo("true\ttrue\tgenerational\tincremental"));
        Assert.That(Run(@"
            collectgarbage('stop')
            local running = collectgarbage('isrunning')
            collectgarbage('step')   -- a step does not restart it
            print(running, collectgarbage('isrunning'))
            collectgarbage('restart')
            print(collectgarbage('isrunning'))"), Is.EqualTo("false\tfalse\ntrue\n"));
        Assert.That(Run(@"
            local inside
            setmetatable({}, {__gc = function () inside = collectgarbage() end})
            collectgarbage()
            print(inside)   -- the collector is not reentrant"), Is.EqualTo("nil\n"));
        Assert.That(ErrorOf("collectgarbage('nothing')"), Does.Contain("invalid option 'nothing'"));
    }

    [Test]
    public void AnErrorInAFinalizerIsAWarning()
    {
        StringWriter error = new();
        Lua.Error = error;
        Run(@"
            warn('@on')
            setmetatable({}, {__gc = function () error('oops', 0) end})
            collectgarbage()");
        Assert.That(error.ToString(), Does.Contain("Lua warning: error in __gc metamethod (oops)"));
    }

    [Test]
    public void DisposeCallsTheFinalizersLeft()
    {
        Run("keep = setmetatable({}, {__gc = function () print('finalized') end})");
        Assert.That(Output, Is.Empty);
        Lua.Dispose();
        Assert.That(Output, Is.EqualTo("finalized\n"));
    }

    [Test]
    public void TheCollectorClosesAFileTheScriptLost()
    {
        Assert.That(Run(@"
            do
              local f = io.open('lost.txt', 'w')
              f:setvbuf('full')
              f:write('flushed')
            end
            collectgarbage()
            print(io.open('lost.txt'):read('a'))"), Is.EqualTo("flushed\n"));
    }
}
