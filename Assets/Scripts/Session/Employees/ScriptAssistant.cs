// Turns a player's English description into an employee Lua program with a language model, then checks the reply with
// ScriptDryRun: the server's parser, then a short run against stand-ins for the employee API. A reply that fails either is
// sent back to the model with the error and the failing line for a corrected version, up to MaxAttempts replies in total;
// after that the request fails with the last error. Client-side authoring aid only: it produces draft text, and running it is still the player's server-checked request.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FoodFactoryGame.Session.Employees
{
    // One conversation with a chat model. Reset starts a new conversation; Ask adds a user message and returns the reply.
    public interface IScriptModel
    {
        Task Reset();
        Task<string> Ask(string message);
    }

    public sealed class ScriptAssistantResult
    {
        public bool Success;
        public string Source;
        public string Error;
        public int Attempts;
        // Each reply's code and the check's error (null for the accepted one), for diagnostics.
        public readonly List<(string Source, string Error)> Tries = new();
    }

    public sealed class ScriptAssistant
    {
        public const int MaxAttempts = 3;

        // The model's instructions: the employee API from the script screen's reference plus the game's item and machine names.
        public const string SystemPrompt =
            "You write Lua 5.2 programs that control one employee in a food factory game. Reply with exactly one complete " +
            "program in a single ```lua code block and no other text. Use only the functions below and plain Lua (local, if, " +
            "for, while, functions, tables, string concatenation with ..). There is no require, io, os or load.\n\n" +
            "Blocking calls (the employee walks or waits; they return when done):\n" +
            "- move_to(place): walk next to a place. Returns true, or false and a reason.\n" +
            "- path(place, place, ...): walk through several places in order.\n" +
            "- take(place, item, amount): walk to place and pick up goods. Returns the number taken. item and amount are optional.\n" +
            "- put(place, item, amount): walk to place and drop goods. Returns the number delivered. item and amount are optional.\n" +
            "- wait(seconds): do nothing for a while.\n" +
            "- place(machine, cell, rotation): put down a machine the employee holds; cell is {x, z}; rotation is 0 to 3.\n" +
            "- pick_up(machine): walk to a placed machine and pick it up.\n" +
            "- place_belt(cell, direction): lay a conveyor belt on cell {x, z}; direction is 0 to 3.\n" +
            "Instant calls:\n" +
            "- count(place, item): units of item at place; count(\"hands\") is what the employee carries.\n" +
            "- carrying(): units the employee carries.\n" +
            "- find(kind): table of IDs of placed machines of a kind, nearest first.\n" +
            "- holding(kind): table of IDs of machines the employee holds.\n" +
            "- position(): the employee's cell as ONE table; write local p = position() and use p.x and p.z.\n" +
            "- say(text) or print(text): show a message.\n\n" +
            "The ground is a grid of 1 metre cells, so a distance in metres is a number of cells. A cell is a table {x = ..., z = ...}; " +
            "fractions are rounded. \"Walk\", \"move\" or \"go\" mean moving the employee with move_to or path. Unless the " +
            "request names a place, movement is relative to where the employee starts: local p = position(). For a shape, build " +
            "a table of cells and walk them with path(cells), which does not stop between cells. math.sin, math.cos and math.pi " +
            "work (angles in radians).\n\n" +
            "A place is \"storage\", a machine kind (\"oven\", \"fridge\", \"counter\"), a machine ID from find(), \"<id>:in\" or " +
            "\"<id>:out\" for a machine's input or output, or a ground cell {x, z}. Items: \"dough\", \"bread\". An oven bakes dough " +
            "into bread; a counter sells bread. Do exactly what the request asks, in its order. When the request says forever, " +
            "keep going, repeat or always, put all of the work inside one while true do ... end loop with a wait(seconds) in it.\n\n" +
            "Example request: bring 3 bread from the storage to the counter\n" +
            "```lua\nlocal n = take(\"storage\", \"bread\", 3)\nput(\"counter\", \"bread\", n)\nsay(\"delivered \" .. n .. \" bread\")\n```\n" +
            "Example request: keep the oven stocked with dough and sell whatever it bakes\n" +
            "```lua\nwhile true do\n  if take(\"storage\", \"dough\", 5) > 0 then\n    put(\"oven\", \"dough\")\n  end\n" +
            "  if take(\"oven\", \"bread\") > 0 then\n    put(\"counter\", \"bread\")\n  end\n  wait(2)\nend\n```\n" +
            "Example request: walk around a 4 m square\n" +
            "```lua\nlocal p = position()\nlocal corners = {\n  {x = p.x + 4, z = p.z},\n  {x = p.x + 4, z = p.z + 4},\n" +
            "  {x = p.x, z = p.z + 4},\n  {x = p.x, z = p.z},\n}\npath(corners)\n```";

        private static readonly Regex Fence = new(@"```[ \t]*(?:lua)?[ \t]*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private readonly IScriptModel _model;

        public ScriptAssistant(IScriptModel model) => _model = model;

        // Writes a new program for the request. The player's current draft is deliberately not shown to the model: a small model
        // copies a program it is shown, including a broken one, and then repeats it on every retry. Each retry message carries
        // the failing program, its error and the request, so a correction does not depend on the model's memory of its reply.
        // progress receives a short line for each step, for the screen's status text. The model's own failures (it did not
        // load, generation threw) propagate to the caller; only replies that fail the check are retried.
        public async Task<ScriptAssistantResult> Generate(string request, Action<string> progress = null)
        {
            if (string.IsNullOrWhiteSpace(request))
                return new ScriptAssistantResult { Error = "Describe what the employee should do first." };
            request = request.Trim();
            await _model.Reset();
            var message = $"Request: {request}";
            var result = new ScriptAssistantResult();
            string error = null;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                progress?.Invoke(attempt == 1 ? "Writing the script..." : $"Fixing an error (attempt {attempt} of {MaxAttempts})...");
                var reply = await _model.Ask(message);
                var source = ExtractCode(reply);
                error = string.IsNullOrWhiteSpace(source) ? "the reply contained no Lua code" : ScriptDryRun.Check(source);
                var repeated = error != null && result.Tries.Any(x => x.Source == source);
                result.Tries.Add((source, error));
                result.Attempts = attempt;
                if (error == null)
                {
                    result.Success = true;
                    result.Source = source.TrimEnd() + "\n";
                    return result;
                }
                message = RetryMessage(request, source, error, repeated);
            }
            result.Error = $"The assistant could not write a working script in {MaxAttempts} attempts. Last error: {error}";
            return result;
        }

        // The code inside the reply's first fenced block, or the whole reply when it has no fence.
        public static string ExtractCode(string reply)
        {
            if (string.IsNullOrEmpty(reply)) return "";
            var match = Fence.Match(reply);
            return (match.Success ? match.Groups[1].Value : reply).Trim();
        }

        private static string RetryMessage(string request, string source, string error, bool repeated)
        {
            var failed = string.IsNullOrWhiteSpace(source) ? $"Your reply had no program: {error}." : $"This program is wrong:\n```lua\n{source}\n```\nLua error: {error}";
            var again = repeated ? "\nYou already sent this exact program and it failed the same way. Do not send it again; write it differently." : "";
            return $"{failed}{again}\nFix the mistake on the line the error names. Write the complete corrected program for the request: " +
                   $"{request}\nReply with one ```lua code block and no other text.";
        }
    }
}
