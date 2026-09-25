// The script assistant's generate-check-retry loop against a scripted fake model: a parsing reply is accepted as is, a reply
// that does not parse is answered with the parser's error, and after three bad replies the request fails with that error.
using System.Collections.Generic;
using System.Threading.Tasks;
using FoodFactoryGame.Session.Employees;
using NUnit.Framework;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class ScriptAssistantTests
    {
        private sealed class FakeModel : IScriptModel
        {
            private readonly Queue<string> _replies;
            public readonly List<string> Messages = new();
            public int Resets;

            public FakeModel(params string[] replies) => _replies = new Queue<string>(replies);

            public Task Reset()
            {
                Resets++;
                Messages.Clear();
                return Task.CompletedTask;
            }

            public Task<string> Ask(string message)
            {
                Messages.Add(message);
                return Task.FromResult(_replies.Dequeue());
            }
        }

        private const string Valid = "local n = take(\"storage\", \"dough\", 5)\nput(\"oven\", \"dough\")";
        private const string Broken = "while true do\n  take(\"storage\", \"dough\"\nend";

        private static ScriptAssistantResult Run(FakeModel model, string request = "carry dough to the oven") =>
            new ScriptAssistant(model).Generate(request).GetAwaiter().GetResult();

        [Test]
        public void AcceptsAParsingReplyFromItsCodeBlock()
        {
            var model = new FakeModel("Here you go:\n```lua\n" + Valid + "\n```\nDone.");
            var result = Run(model);
            Assert.That((result.Success, result.Attempts, result.Source), Is.EqualTo((true, 1, Valid + "\n")));
            Assert.That(model.Resets, Is.EqualTo(1));
            Assert.That(model.Messages, Has.Count.EqualTo(1));
            Assert.That(model.Messages[0], Does.Contain("carry dough to the oven"));
        }

        [Test]
        public void FeedsTheSyntaxErrorBackAndAcceptsTheCorrection()
        {
            var model = new FakeModel("```lua\n" + Broken + "\n```", "```lua\n" + Valid + "\n```");
            var result = Run(model);
            Assert.That((result.Success, result.Attempts), Is.EqualTo((true, 2)));
            var expected = EmployeeScript.CheckSyntax(Broken);
            Assert.That(expected, Is.Not.Null.And.Contains("script:"), "The parser reports the line.");
            Assert.That(model.Messages[1], Does.Contain(expected), "The model saw the parser's error.");
        }

        [Test]
        public void FailsWithTheLastErrorAfterThreeBadReplies()
        {
            var model = new FakeModel("```lua\n" + Broken + "\n```", "x = = 1", "no code here, sorry", "```lua\n" + Valid + "\n```");
            var result = Run(model);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Attempts, Is.EqualTo(ScriptAssistant.MaxAttempts));
            Assert.That(model.Messages, Has.Count.EqualTo(3), "No fourth reply is requested.");
            Assert.That(result.Error, Does.Contain("3 attempts").And.Contain(EmployeeScript.CheckSyntax("no code here, sorry")));
            Assert.That(result.Source, Is.Null);
        }

        [Test]
        public void TreatsAnEmptyReplyAsAFailedAttempt()
        {
            var model = new FakeModel("", "```lua\n" + Valid + "\n```");
            var result = Run(model);
            Assert.That((result.Success, result.Attempts), Is.EqualTo((true, 2)));
            Assert.That(model.Messages[1], Does.Contain("no Lua code"));
        }

        [Test]
        public void AnEmptyRequestNeverReachesTheModel()
        {
            var model = new FakeModel();
            var result = Run(model, "   ");
            Assert.That((result.Success, model.Resets, model.Messages.Count), Is.EqualTo((false, 0, 0)));
        }

        // A retry must not depend on the model remembering its reply: the message carries the program, error and request.
        [Test]
        public void RetryMessageCarriesTheProgramTheErrorAndTheRequest()
        {
            var model = new FakeModel("```lua\n" + Broken + "\n```", "```lua\n" + Valid + "\n```");
            Run(model, "walk in a 5m circle");
            Assert.That(model.Messages[1], Does.Contain(Broken).And.Contain(EmployeeScript.CheckSyntax(Broken)).And.Contain("walk in a 5m circle"));
            Assert.That(model.Messages[1], Does.Not.Contain("already sent"));
        }

        // What happened in play (2026-09-25): the model sent the same failing program three times.
        [Test]
        public void RepeatingAFailingProgramIsCalledOut()
        {
            var model = new FakeModel("```lua\n" + Broken + "\n```", "```lua\n" + Broken + "\n```", "```lua\n" + Valid + "\n```");
            var result = Run(model);
            Assert.That((result.Success, result.Attempts), Is.EqualTo((true, 3)));
            Assert.That(model.Messages[2], Does.Contain("already sent this exact program"));
        }

        [Test]
        public void CheckSyntaxAcceptsTheGameApiWithoutRunningIt()
        {
            // Calls to undefined globals parse; only running them would fail.
            Assert.That(EmployeeScript.CheckSyntax(Valid + "\nwhile true do wait(1) end"), Is.Null);
            Assert.That(EmployeeScript.CheckSyntax(new string('a', EmployeeScript.MaxSourceLength + 1)), Does.Contain("at most"));
        }

        // The program the model wrote in play (2026-09-25): it parses, but position() returns one table, so line 6 failed on
        // the server with "attempt to perform arithmetic on a table value".
        private const string CircleWalk =
            "local function move_in_circle(radius, speed)\n    local x, z = position()\n" +
            "    local dx, dz = math.cos(math.rad(360 / radius)), math.sin(math.rad(360 / radius))\n    while true do\n" +
            "        wait(speed)\n        x, z = x + dx, z + dz\n        if not move_to({x, z}) then\n            break\n        end\n" +
            "    end\nend\n\nmove_in_circle(5, 1)\n";

        [Test]
        public void FeedsRuntimeErrorsFromTheDryRunBack()
        {
            Assert.That(EmployeeScript.CheckSyntax(CircleWalk), Is.Null, "It parses.");
            var fixedWalk = CircleWalk.Replace("local x, z = position()", "local p = position()\n    local x, z = p[1], p[2]");
            var model = new FakeModel("```lua\n" + CircleWalk + "```", "```lua\n" + fixedWalk + "```");
            var result = Run(model, "walk in a circle");
            Assert.That((result.Success, result.Attempts), Is.EqualTo((true, 2)));
            Assert.That(model.Messages[1], Does.Contain("arithmetic on a table value").And.Contain("line 6: x, z = x + dx, z + dz"));
        }

        [TestCase("walk_to(\"oven\")", "nil")]
        [TestCase("local p = position()\nsay(p.y + 1)", "arithmetic")]
        [TestCase("local ovens = find(\"oven\")\nput(ovens[1] .. \":in\", \"dough\")\nlocal n = take(\"storage\")\nsay(n .. \" \" .. carrying())\nerror(\"stop\")", "stop")]
        public void DryRunReportsRuntimeErrors(string source, string expected) =>
            Assert.That(ScriptDryRun.Check(source), Does.StartWith("runtime error").And.Contain(expected));

        [Test]
        public void DryRunPassesWorkingProgramsIncludingEndlessLoops()
        {
            Assert.That(ScriptDryRun.Check(Valid), Is.Null);
            Assert.That(ScriptDryRun.Check("while true do\n  if take(\"storage\", \"dough\", 5) > 0 then put(\"oven\", \"dough\") end\n  wait(2)\nend"), Is.Null);
            Assert.That(ScriptDryRun.Check("local p = position()\nmove_to({p[1] + 1, p[2]})\nplace(holding(\"oven\")[1], {3, 4}, 1)\nplace_belt({1, 2}, 0)\npick_up(find(\"oven\")[1])\npath({1, 1}, \"storage\")\nsay(count(\"hands\"))"), Is.Null);
            Assert.That(ScriptDryRun.Check("while true do end"), Is.Null, "A busy loop is legal; the server caps it.");
        }

        // The stand-ins must cover every call EmployeeWorker gives scripts, or the dry run would reject real calls as nil.
        [Test]
        public void DryRunCoversTheWholeEmployeeApi()
        {
            var worker = (EmployeeWorker)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(EmployeeWorker));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var operations = (System.Collections.IDictionary)typeof(EmployeeWorker).GetMethod("Operations", flags).Invoke(worker, null);
            var queries = (System.Collections.IDictionary)typeof(EmployeeWorker).GetMethod("Queries", flags).Invoke(worker, null);
            var real = new List<string>();
            foreach (var key in operations.Keys) real.Add((string)key);
            foreach (var key in queries.Keys) real.Add((string)key);
            Assert.That(ScriptDryRun.ApiNames, Is.EquivalentTo(real));
        }

        [TestCase("```Lua\r\nsay(1)\r\n```", "say(1)")]
        [TestCase("```\nsay(1)\n```", "say(1)")]
        [TestCase("say(1)", "say(1)")]
        public void ExtractsCode(string reply, string code) => Assert.That(ScriptAssistant.ExtractCode(reply), Is.EqualTo(code));
    }
}
