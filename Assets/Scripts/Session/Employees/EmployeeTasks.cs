// An employee's visual task list (decision 0037) and the Lua program generated from it. The Lua is what actually runs, on
// the server, through the ordinary employee API (EmployeeWorker); the list is only how the player edits it, saved beside the
// script (GoodsEmployee.Tasks). Generation is deterministic, so a client can tell whether a script still matches its list:
// one that does not was edited by hand and is "detached" until the player resets it. The program loops forever, doing one
// step per task each pass (one carrying trip, or one flip of a switch), then waits briefly.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FoodFactoryGame.Session.Employees
{
    public enum EmployeeTaskType
    {
        // Move stuff from A to B.
        Move,
        // Turn a machine on, off, or toggle it.
        Power
    }

    public enum EmployeeTaskPower
    {
        On,
        Off,
        Toggle
    }

    [Serializable] public sealed class EmployeeTask
    {
        public EmployeeTaskType Type;
        // Lua place text picked in the world: "storage", "<id>" or "<id>:out" for a source, "storage" or "<id>:in" for a
        // destination. Empty until picked.
        public string Source = "";
        public string Destination = "";
        // Any item the destination accepts (its recipes' inputs; anything for storage). Otherwise Items is a whitelist, or a
        // blacklist of items never to move (still only what the destination accepts).
        public bool AnyItem = true;
        public bool Whitelist = true;
        public List<string> Items = new();
        // A machine ID picked in the world, and what to do with its switch.
        public string Machine = "";
        public EmployeeTaskPower Power;
    }

    [Serializable] public sealed class EmployeeTaskList
    {
        public const string MoveLabel = "Move stuff from A to B";
        public const string PowerLabel = "Turn machine on";
        public const string Header = "-- Generated from this employee's Tasks tab. Editing it here detaches the employee from its tasks.\n";

        public List<EmployeeTask> Tasks = new();

        public string ToJson() => JsonUtility.ToJson(this);

        // The saved list, or an empty one for empty or unreadable text.
        public static EmployeeTaskList FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new EmployeeTaskList();
            try
            {
                var list = JsonUtility.FromJson<EmployeeTaskList>(json) ?? new EmployeeTaskList();
                list.Tasks ??= new List<EmployeeTask>();
                list.Tasks.RemoveAll(x => x == null);
                foreach (var task in list.Tasks)
                {
                    task.Source ??= "";
                    task.Destination ??= "";
                    task.Machine ??= "";
                    task.Items ??= new List<string>();
                }
                return list;
            }
            catch (ArgumentException)
            {
                return new EmployeeTaskList();
            }
        }

        public EmployeeTaskList Copy() => FromJson(ToJson());

        // The program this list runs as. An unfinished task (nothing picked yet, or an empty whitelist) is a comment.
        public string ToLua()
        {
            var text = new StringBuilder(Header);
            if (Tasks.Count == 0) return text.Append("-- No tasks yet: add one with the + button.\n").ToString();
            if (Tasks.Any(x => x.Type == EmployeeTaskType.Move))
            {
                text.Append('\n');
                text.Append("-- Carries one load from source to destination: only items the filter allows, as many as the destination has\n");
                text.Append("-- room for. Anything that does not fit goes back to the source, so the hands are empty for the next task.\n");
                text.Append("local function move(source, destination, items)\n");
                text.Append("  local space = room(destination, items)\n");
                text.Append("  if space == 0 or count(source, items) == 0 then return 0 end\n");
                text.Append("  if take(source, items, space) == 0 then return 0 end\n");
                text.Append("  local delivered = put(destination, items)\n");
                text.Append("  if carrying() > 0 then put(source) end\n");
                text.Append("  return delivered\n");
                text.Append("end\n");
            }
            text.Append('\n');
            text.Append("while true do\n");
            for (var index = 0; index < Tasks.Count; index++)
            {
                var task = Tasks[index];
                text.Append("  -- ").Append(index + 1).Append(". ").Append(Describe(task)).Append('\n');
                var line = Statement(task);
                text.Append("  ").Append(line ?? "-- (not finished: nothing happens yet)").Append('\n');
            }
            text.Append("  wait(1)\n");
            text.Append("end\n");
            return text.ToString();
        }

        public static string Describe(EmployeeTask task)
        {
            if (task.Type == EmployeeTaskType.Power)
            {
                var machine = Named(task.Machine, "a machine");
                return task.Power switch
                {
                    EmployeeTaskPower.On => $"Turn {machine} on",
                    EmployeeTaskPower.Off => $"Turn {machine} off",
                    _ => $"Toggle {machine}"
                };
            }
            var what = task.AnyItem ? "any valid item"
                : task.Whitelist ? (task.Items.Count == 0 ? "nothing (the whitelist is empty)" : string.Join(", ", task.Items.Select(OneLine)))
                : task.Items.Count == 0 ? "any valid item" : "any valid item except " + string.Join(", ", task.Items.Select(OneLine));
            return $"Move {what} from {Named(task.Source, "?")} to {Named(task.Destination, "?")}";
        }

        private static string Named(string place, string missing) => string.IsNullOrEmpty(place) ? missing : OneLine(place);

        // Text for a comment line: a line break would end the comment.
        private static string OneLine(string text) => (text ?? "").Replace('\n', ' ').Replace('\r', ' ');

        // One task's Lua line, or null while it is unfinished.
        private static string Statement(EmployeeTask task)
        {
            if (task.Type == EmployeeTaskType.Power)
            {
                if (string.IsNullOrEmpty(task.Machine)) return null;
                var machine = Quote(task.Machine);
                return task.Power switch
                {
                    EmployeeTaskPower.On => $"if not is_on({machine}) then turn_on({machine}) end",
                    EmployeeTaskPower.Off => $"if is_on({machine}) then turn_off({machine}) end",
                    _ => $"toggle({machine})"
                };
            }
            if (string.IsNullOrEmpty(task.Source) || string.IsNullOrEmpty(task.Destination)) return null;
            var destination = Quote(task.Destination);
            string items;
            if (task.AnyItem || (!task.Whitelist && task.Items.Count == 0)) items = $"accepts({destination})";
            else if (task.Whitelist)
            {
                if (task.Items.Count == 0) return null;
                items = List(task.Items);
            }
            else items = $"accepts({destination}, {List(task.Items)})";
            return $"move({Quote(task.Source)}, {destination}, {items})";
        }

        private static string List(IEnumerable<string> items) => "{" + string.Join(", ", items.Select(Quote)) + "}";

        public static string Quote(string text) =>
            "\"" + (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}
