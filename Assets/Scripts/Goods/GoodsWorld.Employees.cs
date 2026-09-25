// PROTOTYPE employees as saved goods actors: each record lives in the world snapshot beside its site grant and carried
// inventory (carried:<id>), so an employee, the goods in its hands, its last pose and its assigned script commit and recover
// together. The pose changes in memory and is saved with the next commit (tick or command); script changes commit at once.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsEmployee
    {
        public string Id;
        public string SiteId;
        public string Name;
        // Last server-known pose on the site (metres, degrees about up); presentation space, not grid cells.
        public float X;
        public float Y;
        public float Z;
        public float Yaw;
        // The last program a player assigned, and whether it should be running: a running script restarts from its first
        // line after recovery, since interpreter state is not saved.
        public string Script = "";
        public bool ScriptRunning;
    }

    public sealed partial class GoodsWorld
    {
        public const string EmployeePrefix = "employee-";
        public const int MaxEmployeeScriptLength = 16000;

        // Server-only seed/hire path: adds the record, its site grant and its carried inventory (created with handSlots slots,
        // or an existing one reused) in memory. Callers commit. Throws ArgumentException for an invalid or duplicate record.
        public void Bootstrap(GoodsEmployee employee, int handSlots)
        {
            lock (_gate)
            {
                if (employee == null || string.IsNullOrWhiteSpace(employee.Id) || !employee.Id.StartsWith(EmployeePrefix, StringComparison.Ordinal)
                    || _state.Employees.Any(x => x.Id == employee.Id) || _state.Locations.All(x => x.SiteId != employee.SiteId)
                    || handSlots < 1 || !ValidPose(employee) || (employee.Script ?? "").Length > MaxEmployeeScriptLength)
                    throw new ArgumentException("Invalid or duplicate employee, or a site that does not exist.");
                var handsId = InventoryLocationId(employee.Id);
                var hands = _state.Locations.FirstOrDefault(x => x.Id == handsId);
                if (hands != null && hands.SiteId != employee.SiteId)
                    throw new ArgumentException($"{handsId} belongs to another site.");
                if (hands == null)
                    _state.Locations.Add(new GoodsLocation { Id = handsId, SiteId = employee.SiteId, Kind = "carried", Capacity = handSlots });
                if (_state.Grants.All(x => x.PlayerId != employee.Id || x.SiteId != employee.SiteId))
                    _state.Grants.Add(new GoodsGrant { PlayerId = employee.Id, SiteId = employee.SiteId });
                var copy = JsonUtility.FromJson<GoodsEmployee>(JsonUtility.ToJson(employee));
                copy.Script ??= "";
                _state.Employees.Add(copy);
                _state.Revision++;
            }
        }

        // Server-only: copies of every employee record (all sites).
        public List<GoodsEmployee> Employees()
        {
            lock (_gate) return _state.Employees.Select(x => JsonUtility.FromJson<GoodsEmployee>(JsonUtility.ToJson(x))).ToList();
        }

        // Server-only: records where the employee stands. In memory only; it is saved by the next commit. Returns false for an
        // unknown employee or a non-finite pose.
        public bool SetEmployeePose(string employeeId, float x, float y, float z, float yaw)
        {
            lock (_gate)
            {
                var employee = _state.Employees.FirstOrDefault(e => e.Id == employeeId);
                var pose = new GoodsEmployee { X = x, Y = y, Z = z, Yaw = yaw };
                if (employee == null || !ValidPose(pose)) return false;
                if (employee.X == x && employee.Y == y && employee.Z == z && employee.Yaw == yaw) return true;
                employee.X = x;
                employee.Y = y;
                employee.Z = z;
                employee.Yaw = yaw;
                _state.Revision++;
                return true;
            }
        }

        // Server-only: assigns a program (and whether it runs) and commits it before returning, with any pending ticks.
        // Returns null when committed, otherwise unknown-employee, script-too-long or persistence-unavailable (state unchanged).
        public string SetEmployeeScriptDurably(string employeeId, string script, bool running, string savePath)
        {
            lock (_gate)
            {
                script ??= "";
                if (_state.Employees.All(x => x.Id != employeeId)) return "unknown-employee";
                if (script.Length > MaxEmployeeScriptLength) return "script-too-long";
                return Durably(savePath, () =>
                {
                    var employee = _state.Employees.First(x => x.Id == employeeId);
                    if (employee.Script == script && employee.ScriptRunning == running) return (string)null;
                    employee.Script = script;
                    employee.ScriptRunning = running;
                    _state.Revision++;
                    return null;
                }, () => "persistence-unavailable");
            }
        }

        private static bool ValidPose(GoodsEmployee employee) => Finite(employee.X) && Finite(employee.Y) && Finite(employee.Z) && Finite(employee.Yaw);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void ValidateEmployees(GoodsSnapshot state)
        {
            if (state.Employees.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || !x.Id.StartsWith(EmployeePrefix, StringComparison.Ordinal)
                    || !ValidPose(x) || x.Script == null || x.Script.Length > MaxEmployeeScriptLength
                    || !state.Locations.Any(y => y.Id == InventoryLocationId(x.Id) && y.SiteId == x.SiteId)
                    || !state.Grants.Any(y => y.PlayerId == x.Id && y.SiteId == x.SiteId))
                || state.Employees.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot has an invalid employee, or one without its grant and carried inventory.");
        }
    }
}
