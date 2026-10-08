// Hiring, firing and wages (decision 0039). The game clock is the world clock read in game hours: wages are charged to each
// employee's site company at every game-hour boundary, inside the clock step, so a charge commits with the cash it moves and
// recovery can neither repeat nor skip one (WagesPaidHour). Cash never goes below zero: an employee whose wage cannot be paid
// is marked unpaid and does no work (its register serves nobody) until a later step finds the cash and charges it an hour
// again. Employees are paid in hire order (the record order), so the most recently hired stop first.
using System;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public sealed partial class GoodsWorld
    {
        // PROTOTYPE values (decision 0039): one game hour is one real minute of the world clock, an employee costs $10 per game
        // hour, a site allows one employee per 25 floor cells over all its storeys (at least one), and new hires have 4 hand slots.
        public const long GameHourSeconds = 60;
        public const long WageCentsPerHour = 1000;
        public const int FloorCellsPerEmployee = 25;
        public const int HiredHandSlots = 4;
        // Presentation only: clock 0 reads as day 1, 08:00.
        public const long GameClockStartHour = 8;

        // Day (from 1), hour and minute of the game clock at a world clock reading.
        public static (long Day, int Hour, int Minute) GameTime(long clockSeconds)
        {
            var minutes = Math.Max(0, clockSeconds) + GameClockStartHour * GameHourSeconds;
            var hours = minutes / GameHourSeconds;
            return (hours / 24 + 1, (int)(hours % 24), (int)(minutes % GameHourSeconds));
        }

        // World clock second of the next wage charge after a snapshot's clock.
        public static long NextWageSeconds(GoodsSnapshot state) => (state.WagesPaidHour + 1) * GameHourSeconds;

        // How many employees a site allows: one per FloorCellsPerEmployee floor cells of its buildings (each building's interior
        // cells times its storeys), and at least one. Works on a site view.
        public static int EmployeeCap(GoodsSnapshot state, string siteId)
        {
            var cells = state.Buildings.Where(x => x.SiteId == siteId)
                .Sum(x => (long)SiteGrid.InteriorCells(x).Count() * Math.Max(1, x.Floors));
            return (int)Math.Max(1, Math.Min(int.MaxValue, cells / FloorCellsPerEmployee));
        }

        // Wages per game hour of every employee of the company that owns the site; 0 when no company owns it.
        public long CompanyWageCentsPerHour(string siteId)
        {
            lock (_gate) return CompanyWagesLocked(_state, siteId);
        }

        private static long CompanyWagesLocked(GoodsSnapshot state, string siteId)
        {
            var company = state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId));
            return company == null ? 0 : state.Employees.Count(x => company.SiteIds.Contains(x.SiteId)) * WageCentsPerHour;
        }

        // Whether an employee is waiting to be paid (and so not working). False for anyone else.
        public bool IsUnpaid(string employeeId)
        {
            lock (_gate) return _state.Employees.Any(x => x.Id == employeeId && x.Unpaid);
        }

        // Hires an employee for the site the player stands on (where their inventory is), standing at the given scene pose. The
        // site's company must own it and it must be under its cap. The new ID is in the outcome's EquipmentId; a replay returns
        // the first outcome, so a retried request never hires twice.
        public GoodsOutcome Hire(string playerId, string requestId, float x, float y, float z, float yaw)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                var siteId = _state.Locations.FirstOrDefault(l => l.Id == InventoryLocationId(playerId))?.SiteId;
                if (siteId is null || !_state.Grants.Any(g => g.PlayerId == playerId && g.SiteId == siteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (CompanyOfSiteLocked(siteId) is null) return Record(requestId, playerId, false, "no-company", null);
                if (_state.Employees.Count(e => e.SiteId == siteId) >= EmployeeCap(_state, siteId))
                    return Record(requestId, playerId, false, "employee-cap", null);
                var pose = new GoodsEmployee { X = x, Y = y, Z = z, Yaw = yaw };
                if (!ValidPose(pose)) return Record(requestId, playerId, false, "invalid-position", null);
                string id;
                do id = EmployeePrefix + ++_state.NextEmployeeNumber;
                while (_state.Employees.Any(e => e.Id == id) || _state.Grants.Any(g => g.PlayerId == id)
                    || _state.Locations.Any(l => l.Id == InventoryLocationId(id)));
                Bootstrap(new GoodsEmployee
                {
                    Id = id, SiteId = siteId, Name = $"Employee {_state.NextEmployeeNumber}", X = x, Y = y, Z = z, Yaw = yaw
                }, HiredHandSlots);
                var outcome = Record(requestId, playerId, true, "hired", null);
                outcome.EquipmentId = id;
                _state.Outcomes[_state.Outcomes.Count - 1].EquipmentId = id;
                return outcome;
            }
        }

        public GoodsOutcome HireDurably(string playerId, string requestId, float x, float y, float z, float yaw, string savePath) =>
            Commit(playerId, requestId, savePath, () => Hire(playerId, requestId, x, y, z, yaw));

        // Fires an employee of a site the player is granted. Refused while its hands hold goods or a machine, so firing never
        // loses anything; its register (if any) is left unstaffed, and its grant and empty hands go with it.
        public GoodsOutcome Fire(string playerId, string requestId, string employeeId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                var employee = _state.Employees.FirstOrDefault(e => e.Id == employeeId);
                if (employee is null || !_state.Grants.Any(g => g.PlayerId == playerId && g.SiteId == employee.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                var handsId = InventoryLocationId(employee.Id);
                if (_state.Lots.Any(l => l.LocationId == handsId) || _state.Equipment.Any(e => e.State == EquipmentState.Held && e.HolderId == employee.Id))
                    return Record(requestId, playerId, false, "holding", null);
                if (_state.Reservations.Any(r => r.Active && r.PlayerId == employee.Id))
                    return Record(requestId, playerId, false, "busy", null);
                ReleaseStaffLocked(employee.Id);
                _state.Employees.Remove(employee);
                _state.Grants.RemoveAll(g => g.PlayerId == employee.Id);
                _state.Locations.RemoveAll(l => l.Id == handsId);
                InvalidateDiners();
                var outcome = Record(requestId, playerId, true, "fired", null);
                outcome.EquipmentId = employee.Id;
                _state.Outcomes[_state.Outcomes.Count - 1].EquipmentId = employee.Id;
                return outcome;
            }
        }

        public GoodsOutcome FireDurably(string playerId, string requestId, string employeeId, string savePath) =>
            Commit(playerId, requestId, savePath, () => Fire(playerId, requestId, employeeId));

        // Call only under _gate, inside a clock step. Charges every game hour the clock has passed (hire order; one that cannot be
        // paid goes unpaid and is not charged), then pays any unpaid employee the cash now covers for one hour so it goes back to
        // work. Employees of a site no company owns cost nothing and are never unpaid.
        private void PayWages()
        {
            var hour = _state.ClockSeconds / GameHourSeconds;
            var changed = false;
            while (_state.WagesPaidHour < hour)
            {
                _state.WagesPaidHour++;
                foreach (var employee in _state.Employees.Where(x => !x.Unpaid))
                {
                    var company = CompanyOfSiteLocked(employee.SiteId);
                    if (company is null || TryDebit(company, WageCentsPerHour)) continue;
                    employee.Unpaid = true;
                    changed = true;
                }
            }
            foreach (var employee in _state.Employees.Where(x => x.Unpaid))
            {
                var company = CompanyOfSiteLocked(employee.SiteId);
                if (company is not null && !TryDebit(company, WageCentsPerHour)) continue;
                employee.Unpaid = false;
                changed = true;
            }
            // A register worked by an unpaid employee serves nobody (Diners).
            if (changed) InvalidateDiners();
        }

        private bool UnpaidLocked(string actorId) =>
            !string.IsNullOrEmpty(actorId) && actorId.StartsWith(EmployeePrefix, StringComparison.Ordinal)
            && _state.Employees.Any(x => x.Id == actorId && x.Unpaid);
    }
}
