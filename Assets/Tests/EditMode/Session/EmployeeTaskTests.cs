// Verifies the visual task list's Lua (decision 0037): the program parses and survives a dry run against the employee API
// stand-ins, is the same text for the same list (so a client can tell a hand-edited script), quotes place and item text,
// leaves unfinished tasks as comments, and the list survives its JSON round trip.
using FoodFactoryGame.Session.Employees;
using NUnit.Framework;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class EmployeeTaskTests
    {
        private static EmployeeTaskList Full() => new()
        {
            Tasks =
            {
                new EmployeeTask { Source = "storage", Destination = "oven-1:in" },
                new EmployeeTask { Source = "oven-1:out", Destination = "fridge-1:in", AnyItem = false, Items = { "bread", "pizza" } },
                new EmployeeTask { Source = "storage", Destination = "storage-2", AnyItem = false, Whitelist = false, Items = { "cheese" } },
                new EmployeeTask { Type = EmployeeTaskType.Power, Machine = "oven-1", Power = EmployeeTaskPower.On },
                new EmployeeTask { Type = EmployeeTaskType.Power, Machine = "oven-1", Power = EmployeeTaskPower.Off },
                new EmployeeTask { Type = EmployeeTaskType.Power, Machine = "oven-1", Power = EmployeeTaskPower.Toggle }
            }
        };

        [Test]
        public void GeneratedLuaRunsAgainstTheApiStandIns()
        {
            var lua = Full().ToLua();
            Assert.That(ScriptDryRun.Check(lua), Is.Null, lua);
            Assert.That(lua, Does.StartWith(EmployeeTaskList.Header));
            Assert.That(lua, Does.Contain("move(\"storage\", \"oven-1:in\", accepts(\"oven-1:in\"))"));
            Assert.That(lua, Does.Contain("move(\"oven-1:out\", \"fridge-1:in\", {\"bread\", \"pizza\"})"));
            Assert.That(lua, Does.Contain("move(\"storage\", \"storage-2\", accepts(\"storage-2\", {\"cheese\"}))"));
            Assert.That(lua, Does.Contain("if not is_on(\"oven-1\") then turn_on(\"oven-1\") end"));
            Assert.That(lua, Does.Contain("if is_on(\"oven-1\") then turn_off(\"oven-1\") end"));
            Assert.That(lua, Does.Contain("toggle(\"oven-1\")"));
            Assert.That(lua, Does.Contain("while true do"));
        }

        [Test]
        public void SameListSameTextAndJsonRoundTrips()
        {
            var list = Full();
            var copy = EmployeeTaskList.FromJson(list.ToJson());
            Assert.That(copy.ToLua(), Is.EqualTo(list.ToLua()));
            Assert.That(copy.Tasks.Count, Is.EqualTo(6));
            Assert.That(copy.Tasks[1].Items, Is.EqualTo(new[] { "bread", "pizza" }));
            Assert.That(copy.Tasks[5].Power, Is.EqualTo(EmployeeTaskPower.Toggle));
            Assert.That(EmployeeTaskList.FromJson("").Tasks, Is.Empty);
            Assert.That(EmployeeTaskList.FromJson("not json").Tasks, Is.Empty);
        }

        [Test]
        public void UnfinishedTasksAreCommentsAndTextIsQuoted()
        {
            var list = new EmployeeTaskList
            {
                Tasks =
                {
                    new EmployeeTask { Source = "storage" },
                    new EmployeeTask { Source = "storage", Destination = "x:in", AnyItem = false, Whitelist = true },
                    new EmployeeTask { Type = EmployeeTaskType.Power },
                    new EmployeeTask { Source = "a\"b", Destination = "c\\d", AnyItem = false, Items = { "e\"f" } }
                }
            };
            var lua = list.ToLua();
            Assert.That(ScriptDryRun.Check(lua), Is.Null, lua);
            Assert.That(lua.Split("-- (not finished: nothing happens yet)").Length - 1, Is.EqualTo(3));
            Assert.That(lua, Does.Contain("move(\"a\\\"b\", \"c\\\\d\", {\"e\\\"f\"})"));
            Assert.That(ScriptDryRun.Check(new EmployeeTaskList().ToLua()), Is.Null, "An empty list is a program that does nothing.");
        }
    }
}
