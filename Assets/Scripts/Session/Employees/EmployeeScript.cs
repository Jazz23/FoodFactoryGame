// Runs one employee's Lua program (MoonSharp, soft sandbox: no io, os.execute or load) as a coroutine the server steps once
// per frame. Blocking calls (walking, waiting, moving goods) are C# operations: the Lua wrapper starts one, yields until it
// finishes and returns its result, so a script reads top to bottom while the world keeps running. Every resume is capped by
// an instruction budget, so a runaway loop only slows its own employee instead of freezing the server.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter;

namespace FoodFactoryGame.Session.Employees
{
    public enum EmployeeScriptState
    {
        Running,
        Finished,
        Failed
    }

    public sealed class EmployeeScript
    {
        // Where a blocking operation leaves the values the Lua call returns.
        public sealed class Result
        {
            public DynValue Value = DynValue.Nil;
            public void Set(params DynValue[] values) => Value = values.Length == 1 ? values[0] : DynValue.NewTuple(values);
        }

        public delegate IEnumerator Operation(CallbackArguments args, Result result);

        public const int MaxSourceLength = 16000;
        // Lua VM instructions per resume before a forced yield.
        private const long InstructionBudget = 20000;

        private readonly Script _lua;
        private readonly DynValue _coroutine;
        private readonly IReadOnlyDictionary<string, Operation> _operations;
        private IEnumerator _operation;
        private Result _result;

        public EmployeeScriptState State { get; private set; } = EmployeeScriptState.Running;
        public string Error { get; private set; }

        // Throws ArgumentException for an over-long program and SyntaxErrorException (with the line) for one that does not parse.
        public EmployeeScript(string source, IReadOnlyDictionary<string, Operation> operations,
            IReadOnlyDictionary<string, Func<Script, CallbackArguments, DynValue>> queries, Action<string> print)
        {
            if (source == null || source.Length > MaxSourceLength)
                throw new ArgumentException($"A script is at most {MaxSourceLength} characters.");
            _operations = operations;
            _lua = new Script(CoreModules.Preset_SoftSandbox);
            _lua.Options.DebugPrint = print;
            foreach (var query in queries)
            {
                var call = query.Value;
                _lua.Globals[query.Key] = DynValue.NewCallback((context, args) => call(context.GetScript(), args), query.Key);
            }
            _lua.Globals["__begin"] = DynValue.NewCallback(Begin, "__begin");
            _lua.Globals["__busy"] = DynValue.NewCallback((_, _) => DynValue.NewBoolean(_operation != null), "__busy");
            _lua.Globals["__result"] = DynValue.NewCallback((_, _) => _result?.Value ?? DynValue.Nil, "__result");
            _lua.DoString(Prelude(operations.Keys), null, "prelude");
            var main = _lua.LoadString(source, null, "script");
            _coroutine = _lua.CreateCoroutine(main);
            _coroutine.Coroutine.AutoYieldCounter = InstructionBudget;
        }

        // A cell as scripts see it: {x, z} that also answers .x and .z, since scripts (and the script assistant) use both.
        public static DynValue Cell(Script lua, double x, double z)
        {
            var table = new Table(lua, DynValue.NewNumber(x), DynValue.NewNumber(z));
            table["x"] = x;
            table["z"] = z;
            return DynValue.NewTable(table);
        }

        // Parses a program without running it, with the same limits as the constructor. Returns null when it would load, or the
        // error the constructor would throw (with the line). Undefined globals are not errors: Lua resolves them at run time.
        public static string CheckSyntax(string source)
        {
            if (source == null || source.Length > MaxSourceLength) return $"A script is at most {MaxSourceLength} characters.";
            try
            {
                new Script(CoreModules.None).LoadString(source, null, "script");
                return null;
            }
            catch (SyntaxErrorException error)
            {
                return error.DecoratedMessage ?? error.Message;
            }
        }

        // Advances the running operation, or resumes Lua once when none is running. Returns false once the script has ended.
        public bool Step()
        {
            if (State != EmployeeScriptState.Running) return false;
            try
            {
                if (_operation != null)
                {
                    if (_operation.MoveNext()) return true;
                    _operation = null;
                }
                _coroutine.Coroutine.Resume();
                if (_coroutine.Coroutine.State == CoroutineState.Dead) State = EmployeeScriptState.Finished;
            }
            catch (InterpreterException error)
            {
                Fail(error.DecoratedMessage ?? error.Message);
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException)
            {
                Fail(error.Message);
            }
            return State == EmployeeScriptState.Running;
        }

        private void Fail(string message)
        {
            State = EmployeeScriptState.Failed;
            Error = message;
            _operation = null;
        }

        private DynValue Begin(ScriptExecutionContext context, CallbackArguments args)
        {
            var name = args.AsType(0, "__begin", DataType.String).String;
            if (!_operations.TryGetValue(name, out var operation)) throw new ScriptRuntimeException($"unknown operation '{name}'");
            var rest = args.GetArray(1);
            _result = new Result();
            _operation = operation(new CallbackArguments(rest, false), _result);
            return DynValue.Nil;
        }

        // Captures the helpers as locals and removes them from the globals, so scripts see only the public calls.
        private static string Prelude(IEnumerable<string> operations)
        {
            var text = new StringBuilder();
            text.AppendLine("local begin, busy, result, yield = __begin, __busy, __result, coroutine.yield");
            text.AppendLine("__begin, __busy, __result = nil, nil, nil");
            text.AppendLine("local function await(name, ...) begin(name, ...) while busy() do yield() end return result() end");
            foreach (var name in operations.OrderBy(x => x, StringComparer.Ordinal))
                text.AppendLine($"function {name}(...) return await(\"{name}\", ...) end");
            return text.ToString();
        }
    }
}
