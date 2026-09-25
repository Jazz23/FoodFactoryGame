// Checks a program the script assistant wrote: first that it parses, then that it survives a short run against stand-ins for
// the employee API (EmployeeWorker's Lua calls). Stand-ins finish at once and return typical values: true for walking and
// placing, 1 for take/put/count, one ID from find/holding, {0, 0} from position(). No goods, machines or world state are
// touched, so the run can happen on the client. It catches what parsing cannot, such as arithmetic on the table position()
// returns or a call to a function the API does not have. Branches the stand-in values do not take go unchecked.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MoonSharp.Interpreter;

namespace FoodFactoryGame.Session.Employees
{
    public static class ScriptDryRun
    {
        // Server steps to run for; a program still running then (a loop with wait()) passes.
        public const int MaxSteps = 400;

        private static readonly Regex Line = new(@"script:\((\d+),");

        public static IEnumerable<string> ApiNames => Operations().Keys.Concat(Queries().Keys);

        // Returns null when the program parses and the dry run did not fail, otherwise the error with the failing line's text.
        public static string Check(string source)
        {
            var error = EmployeeScript.CheckSyntax(source);
            if (error != null) return WithLine(error, source);
            var script = new EmployeeScript(source, Operations(), Queries(), _ => { });
            for (var step = 0; step < MaxSteps && script.Step(); step++) { }
            return script.State == EmployeeScriptState.Failed ? "runtime error: " + WithLine(script.Error, source) : null;
        }

        // Appends the source line an error names, so a small model can find its mistake.
        private static string WithLine(string error, string source)
        {
            var match = Line.Match(error ?? "");
            if (!match.Success) return error;
            var lines = source.Replace("\r\n", "\n").Split('\n');
            var index = int.Parse(match.Groups[1].Value) - 1;
            return index >= 0 && index < lines.Length ? $"{error} (line {index + 1}: {lines[index].Trim()})" : error;
        }

        private static Dictionary<string, EmployeeScript.Operation> Operations() => new()
        {
            ["move_to"] = Returns(DynValue.True),
            ["path"] = Returns(DynValue.True),
            ["take"] = Returns(DynValue.NewNumber(1)),
            ["put"] = Returns(DynValue.NewNumber(1)),
            ["wait"] = Returns(DynValue.True),
            ["place"] = Returns(DynValue.True),
            ["pick_up"] = Returns(DynValue.True),
            ["place_belt"] = Returns(DynValue.True)
        };

        private static Dictionary<string, Func<Script, CallbackArguments, DynValue>> Queries() => new()
        {
            ["count"] = (_, _) => DynValue.NewNumber(1),
            ["find"] = (lua, _) => DynValue.NewTable(new Table(lua, DynValue.NewString("machine-1"))),
            ["carrying"] = (_, _) => DynValue.NewNumber(1),
            ["holding"] = (lua, _) => DynValue.NewTable(new Table(lua, DynValue.NewString("machine-2"))),
            ["position"] = (lua, _) => EmployeeScript.Cell(lua, 0, 0),
            ["say"] = (_, _) => DynValue.Nil
        };

        private static EmployeeScript.Operation Returns(DynValue value) => (_, result) => Finish(result, value);

        private static IEnumerator Finish(EmployeeScript.Result result, DynValue value)
        {
            result.Set(value);
            yield break;
        }
    }
}
