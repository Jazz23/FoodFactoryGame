// Acceptance runs of the real script assistant model (Explicit: loads about 1.1 GB and takes seconds per request). Each run
// asks for a program, then replays it against stand-ins that record where the employee is sent, starting from a known
// cell, and checks the walk has the requested shape. Because generation varies, a request is run several times and a pass
// rate is required rather than a single success.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FoodFactoryGame.Session.Employees;
using MoonSharp.Interpreter;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    [Explicit("Loads the local language model")]
    [TestFixture("QwenCoder15B")]
    [TestFixture("Qwen3Instruct4B")]
    public sealed class ScriptAssistantModelTests
    {
        private const int Runs = 5;
        private const int StartX = 20;
        private const int StartZ = 20;
        private readonly ScriptModelFile _file;
        private GameObject _host;

        public ScriptAssistantModelTests(string model) =>
            _file = (ScriptModelFile)typeof(LocalScriptModel).GetField(model).GetValue(null);

        [TearDown]
        public void TearDown()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
        }

        // The cells a program sends the employee to (move_to and path waypoints), starting at (StartX, StartZ).
        private static List<Vector2> Targets(string source, int maxSteps = 4000, int maxTargets = 400)
        {
            var targets = new List<Vector2>();
            void Record(DynValue value)
            {
                if (value.Type != DataType.Table) return;
                var x = value.Table.Get(1).CastToNumber() ?? value.Table.Get("x").CastToNumber();
                var z = value.Table.Get(2).CastToNumber() ?? value.Table.Get("z").CastToNumber();
                if (x != null && z != null) targets.Add(new Vector2((float)Math.Round(x.Value), (float)Math.Round(z.Value)));
                else foreach (var item in value.Table.Values) Record(item);
            }
            IEnumerator Done(EmployeeScript.Result result)
            {
                result.Set(DynValue.True);
                yield break;
            }
            EmployeeScript.Operation Walk() => (args, result) =>
            {
                if (args.Count == 2 && args[0].Type == DataType.Number) Record(EmployeeScript.Cell(new Script(), args[0].Number, args[1].Number));
                else foreach (var arg in args.GetArray()) Record(arg);
                return Done(result);
            };
            EmployeeScript.Operation Other() => (_, result) => Done(result);
            var script = new EmployeeScript(source,
                new Dictionary<string, EmployeeScript.Operation>
                {
                    ["move_to"] = Walk(), ["path"] = Walk(), ["take"] = Other(), ["put"] = Other(), ["wait"] = Other(),
                    ["place"] = Other(), ["pick_up"] = Other(), ["place_belt"] = Other()
                },
                new Dictionary<string, Func<Script, CallbackArguments, DynValue>>
                {
                    ["count"] = (_, _) => DynValue.NewNumber(1), ["carrying"] = (_, _) => DynValue.NewNumber(0),
                    ["find"] = (lua, _) => DynValue.NewTable(new Table(lua)), ["holding"] = (lua, _) => DynValue.NewTable(new Table(lua)),
                    ["position"] = (lua, _) => EmployeeScript.Cell(lua, StartX, StartZ), ["say"] = (_, _) => DynValue.Nil
                },
                _ => { });
            for (var step = 0; step < maxSteps && targets.Count < maxTargets && script.Step(); step++) { }
            return targets;
        }

        // Null when the targets trace a circle around the start whose size is 5 m (as a radius or, the other fair reading of
        // "a 5 m circle", a diameter), otherwise why not.
        private static string CircleProblem(List<Vector2> targets)
        {
            var centre = new Vector2(StartX, StartZ);
            var distinct = targets.Distinct().ToList();
            if (distinct.Count < 8) return $"only {distinct.Count} distinct cells";
            var mean = distinct.Average(x => Vector2.Distance(x, centre));
            var radius = Mathf.Abs(mean - 5f) < Mathf.Abs(mean - 2.5f) ? 5f : 2.5f;
            var onRing = distinct.Count(x => Mathf.Abs(Vector2.Distance(x, centre) - radius) <= 0.8f);
            if (onRing < distinct.Count * 0.85f) return $"{distinct.Count - onRing} of {distinct.Count} cells are off the {radius} m ring (mean {mean:0.0} m)";
            var octants = distinct.Select(x => (int)Mathf.Floor((Mathf.Atan2(x.y - centre.y, x.x - centre.x) + Mathf.PI) / (Mathf.PI / 4)) % 8).Distinct().Count();
            return octants < 7 ? $"the walk covers only {octants} of 8 directions" : null;
        }

        // Answers the first message with a fixed program without asking the model, then passes through: forces the retry path.
        private sealed class FirstReply : IScriptModel
        {
            private readonly IScriptModel _inner;
            private readonly string _first;
            private bool _answered;

            public FirstReply(IScriptModel inner, string first) => (_inner, _first) = (inner, first);

            public Task Reset()
            {
                _answered = false;
                return _inner.Reset();
            }

            public Task<string> Ask(string message)
            {
                if (_answered) return _inner.Ask(message);
                _answered = true;
                return Task.FromResult("```lua\n" + _first + "\n```");
            }
        }

        // The program the model sent three times in play (2026-09-25); line 6 does arithmetic on the table position() returns.
        private const string BrokenCircle =
            "local function move_in_circle(radius, speed)\n    local x, z = position()\n" +
            "    local dx, dz = math.cos(math.rad(360 / radius)), math.sin(math.rad(360 / radius))\n    while true do\n" +
            "        wait(speed)\n        x, z = x + dx, z + dz\n        if not move_to({x, z}) then\n            break\n        end\n" +
            "    end\nend\n\nmove_in_circle(5, 1)";

        private IEnumerator PassRate(string request, Func<List<Vector2>, string> problem, int required, Func<IScriptModel, IScriptModel> wrap = null)
        {
            _host = new GameObject("ScriptAssistantModelTests");
            var model = _host.AddComponent<LocalScriptModel>();
            model.File = _file;
            Assume.That(_file.Installed, $"Model not installed: {_file.File}.");
            var passed = 0;
            var report = new List<string>();
            for (var run = 1; run <= Runs; run++)
            {
                var task = new ScriptAssistant(wrap?.Invoke(model) ?? model).Generate(request);
                while (!task.IsCompleted) yield return null;
                var result = task.Result;
                var why = result.Success ? problem(Targets(result.Source)) : "no program: " + result.Error;
                if (why == null) passed++;
                report.Add($"--- run {run}: {(why ?? "PASS")} ({result.Attempts} attempt(s))\n" +
                           string.Join("\n", result.Tries.Select((x, i) => $"[try {i + 1}] {x.Error ?? "ok"}\n{x.Source}")));
            }
            Debug.Log($"[AssistantEval] {_file.File} \"{request}\": {passed}/{Runs}\n{string.Join("\n", report)}");
            Assert.That(passed, Is.GreaterThanOrEqualTo(required), string.Join("\n", report));
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator MoveInA5mCircle() => PassRate("move in a 5m circle", CircleProblem, Runs);

        [UnityTest, Timeout(900000)]
        public IEnumerator WalkInA5mCircle() => PassRate("walk in a 5m circle", CircleProblem, Runs);

        // The retry path on the real model: the first reply is the broken program, so only a correction can pass.
        [UnityTest, Timeout(900000)]
        public IEnumerator CorrectsTheBrokenCircleFromItsError() =>
            PassRate("walk in a 5m circle", CircleProblem, Runs, model => new FirstReply(model, BrokenCircle));
    }
}
