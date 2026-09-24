// Verifies the employee Lua host without a scene or server: blocking calls suspend the script until their C# operation
// finishes and return its values, errors carry the script line, and a runaway loop yields instead of hanging a step.
using System;
using System.Collections;
using System.Collections.Generic;
using FoodFactoryGame.Session.Employees;
using MoonSharp.Interpreter;
using NUnit.Framework;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class EmployeeScriptTests
    {
        private readonly List<string> _said = new();

        [SetUp]
        public void SetUp() => _said.Clear();

        // "work(n)" takes n steps and returns n * 10 and "done".
        private static IEnumerator Work(CallbackArguments args, EmployeeScript.Result result)
        {
            var steps = (int)args[0].Number;
            for (var index = 0; index < steps; index++) yield return null;
            result.Set(DynValue.NewNumber(steps * 10), DynValue.NewString("done"));
        }

        private EmployeeScript Create(string source) => new(source,
            new Dictionary<string, EmployeeScript.Operation> { ["work"] = Work },
            new Dictionary<string, Func<Script, CallbackArguments, DynValue>> { ["answer"] = (_, _) => DynValue.NewNumber(42) },
            _said.Add);

        private static int RunToEnd(EmployeeScript script, int limit = 1000)
        {
            var steps = 0;
            while (script.Step() && ++steps < limit) { }
            return steps;
        }

        [Test]
        public void BlockingCallSuspendsUntilItsOperationFinishes()
        {
            var script = Create("local value, word = work(3)\nprint(value .. ' ' .. word .. ' ' .. answer())");
            var steps = RunToEnd(script);
            Assert.AreEqual(EmployeeScriptState.Finished, script.State);
            Assert.GreaterOrEqual(steps, 3, "the three operation steps must each take a server step");
            CollectionAssert.AreEqual(new[] { "30 done 42" }, _said);
        }

        [Test]
        public void HelpersAreHiddenFromScripts()
        {
            var script = Create("print(tostring(__begin) .. tostring(__busy) .. tostring(__result))");
            RunToEnd(script);
            CollectionAssert.AreEqual(new[] { "nilnilnil" }, _said);
        }

        [Test]
        public void SyntaxErrorThrowsWithTheLine()
        {
            var error = Assert.Throws<SyntaxErrorException>(() => Create("work(1\nprint('x')"));
            StringAssert.Contains("script:", error.DecoratedMessage);
        }

        [Test]
        public void RuntimeErrorFailsTheScript()
        {
            var script = Create("work(1)\nerror('broken belt')");
            RunToEnd(script);
            Assert.AreEqual(EmployeeScriptState.Failed, script.State);
            StringAssert.Contains("broken belt", script.Error);
            StringAssert.Contains("script:(2", script.Error);
        }

        [Test]
        public void RunawayLoopYieldsEachStep()
        {
            var script = Create("while true do end");
            for (var index = 0; index < 5; index++) Assert.IsTrue(script.Step());
            Assert.AreEqual(EmployeeScriptState.Running, script.State);
        }

        [Test]
        public void SandboxHasNoFileOrProcessAccess()
        {
            var script = Create("print(tostring(io) .. tostring(os and os.execute) .. tostring(load))");
            RunToEnd(script);
            CollectionAssert.AreEqual(new[] { "nilnilnil" }, _said);
        }

        [Test]
        public void OverlongSourceIsRefused() =>
            Assert.Throws<ArgumentException>(() => Create(new string('-', EmployeeScript.MaxSourceLength + 1)));
    }
}
