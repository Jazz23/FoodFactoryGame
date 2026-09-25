// Conveyor belts: a placed belt is a one-cell, stable-ID site object made from one "belt" item, with one location holding
// the single-unit lots riding on it. Placing consumes a belt item from the player's inventory; removing returns it together
// with every item on the belt, all-or-nothing. Belts move their items on the world clock (MoveBeltItems), so a belt line
// runs whether or not anyone watches. Belts never feed equipment: goods only enter and leave a belt through these commands.
// A conveyor lift (decision 0021) is a belt made from one "lift" item whose front end is one storey up or down: it occupies
// its cell on both levels and hands its items to the belt in front on its exit level.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsBelt
    {
        public string Id;
        public string SiteId;
        public int CellX;
        public int CellZ;
        // Travel direction, 0..3 (BeltRules.Step).
        public int Direction;
        // Floor the belt takes items on (decision 0020); a flat belt links only to belts on the same level.
        public int Level;
        // 0 for a flat belt; +1 or -1 for a conveyor lift, whose front end (and the belt it feeds) is one storey up or down.
        public int Lift;

        public string LocationId => Id + ":items";
        // Level the belt hands its items on at: its own level, or the other end of a lift.
        public int ExitLevel => Level + Lift;
    }

    public sealed partial class GoodsWorld
    {
        // The item a belt is placed from and returned as.
        public const string BeltItemId = "belt";
        // The item a conveyor lift is placed from and returned as.
        public const string LiftItemId = "lift";
        public const string BeltLocationKind = "belt";
        // Belt items do not spoil in practice; lots still need a threshold.
        public const long NonPerishableSeconds = 1_000_000_000_000L;

        private bool IsBeltLocation(string locationId) =>
            _state.Locations.FirstOrDefault(x => x.Id == locationId)?.Kind == BeltLocationKind;

        // Volatile primitive for tests. Live request handlers must call PlaceBeltDurably.
        // An empty cell takes a new belt made from one belt item in the player's inventory; a cell that already holds a
        // belt is turned to the requested direction at no cost (Factorio's drag over an existing belt). Level 0 is the ground.
        public GoodsOutcome PlaceBelt(string playerId, string requestId, string siteId, int cellX, int cellZ, int direction, int level = 0) =>
            PlaceConveyor(playerId, requestId, siteId, cellX, cellZ, direction, level, 0, false);

        public GoodsOutcome PlaceBeltDurably(string playerId, string requestId, string siteId, int cellX, int cellZ, int direction, string savePath,
            int level = 0)
        {
            return Commit(playerId, requestId, savePath, () => PlaceBelt(playerId, requestId, siteId, cellX, cellZ, direction, level));
        }

        // Volatile primitive for tests. Live request handlers must call PlaceLiftDurably.
        // A lift taking items on level and handing them to the belt in front one storey up (lift +1) or down (lift -1). Both
        // of its cells must be free, so the other level must exist there. A lift already there with the same level and lift
        // is turned at no cost, like a belt.
        public GoodsOutcome PlaceLift(string playerId, string requestId, string siteId, int cellX, int cellZ, int direction, int level, int lift) =>
            PlaceConveyor(playerId, requestId, siteId, cellX, cellZ, direction, level, lift, true);

        public GoodsOutcome PlaceLiftDurably(string playerId, string requestId, string siteId, int cellX, int cellZ, int direction, int level,
            int lift, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => PlaceLift(playerId, requestId, siteId, cellX, cellZ, direction, level, lift));
        }

        // A belt (lift 0) or a lift (lift +1 or -1) made from one belt or lift item.
        private GoodsOutcome PlaceConveyor(string playerId, string requestId, string siteId, int cellX, int cellZ, int direction, int level, int lift,
            bool isLift)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                if (!_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (direction < 0 || direction > 3) return Record(requestId, playerId, false, "invalid-rotation", null);
                if (lift < -1 || lift > 1 || isLift != (lift != 0)) return Record(requestId, playerId, false, "invalid-lift", null);
                var existing = _state.Belts.FirstOrDefault(x => x.SiteId == siteId && x.Level == level && x.CellX == cellX && x.CellZ == cellZ
                    && x.Lift == lift);
                if (existing != null)
                {
                    if (existing.Direction == direction) return Record(requestId, playerId, true, "unchanged", null);
                    existing.Direction = direction;
                    return Record(requestId, playerId, true, "rotated", null);
                }
                var problem = SiteGrid.CellProblem(_state, siteId, cellX, cellZ, 1, 1, null, level)
                    ?? (lift == 0 ? null : SiteGrid.CellProblem(_state, siteId, cellX, cellZ, 1, 1, null, level + lift));
                if (problem != null) return Record(requestId, playerId, false, problem, null);
                var itemId = lift == 0 ? BeltItemId : LiftItemId;
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                if (inventory == null || inventory.SiteId != siteId) return Record(requestId, playerId, false, "no-inventory", null);
                var source = _state.Lots
                    .Where(x => x.LocationId == inventory.Id && x.ItemId == itemId && x.OwnerId == siteId && Available(x) > 0)
                    .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (source == null) return Record(requestId, playerId, false, lift == 0 ? "no-belts" : "no-lifts", null);

                // All checks precede this single locked mutation.
                source.Quantity--;
                if (source.Quantity == 0) _state.Lots.Remove(source);
                var belt = new GoodsBelt
                {
                    Id = "belt-" + Guid.NewGuid().ToString("N"), SiteId = siteId, CellX = cellX, CellZ = cellZ, Direction = direction,
                    Level = level, Lift = lift
                };
                _state.Belts.Add(belt);
                _state.Locations.Add(new GoodsLocation
                {
                    Id = belt.LocationId, SiteId = siteId, Kind = BeltLocationKind, Capacity = BeltRules.UnitsPerTile / BeltRules.ItemSpacing
                });
                return Record(requestId, playerId, true, "placed", null);
            }
        }

        // Volatile primitive for tests. Live request handlers must call RemoveBeltDurably.
        // The belt (or lift) item and every item riding the belt go to the player's inventory together, or nothing changes.
        public GoodsOutcome RemoveBelt(string playerId, string requestId, string beltId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var belt = _state.Belts.FirstOrDefault(x => x.Id == beltId);
                if (belt == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == belt.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                if (inventory == null || inventory.SiteId != belt.SiteId) return Record(requestId, playerId, false, "no-inventory", null);
                var riding = _state.Lots.Where(x => x.LocationId == belt.LocationId).ToList();
                if (riding.Any(x => _state.Reservations.Any(y => y.Active && y.LotId == x.Id)))
                    return Record(requestId, playerId, false, "reserved", null);
                var itemId = belt.Lift == 0 ? BeltItemId : LiftItemId;
                var beltLot = new GoodsLot
                {
                    Id = Guid.NewGuid().ToString("N"), ItemId = itemId, OwnerId = belt.SiteId, LocationId = inventory.Id,
                    Quantity = 1, SpoilAfterSeconds = NonPerishableSeconds
                };
                if (!FitsAll(inventory.Id, riding.Append(beltLot))) return Record(requestId, playerId, false, "capacity", null);

                // All checks precede this single locked mutation. Riding lots keep their IDs and exposure.
                foreach (var lot in riding)
                {
                    lot.LocationId = inventory.Id;
                    lot.BeltPosition = 0;
                }
                // Returned belts top up an existing belt lot, so repeated pickups do not pile up single-unit lots.
                var stack = _state.Lots.Where(x => x.LocationId == inventory.Id && x.ItemId == itemId && x.OwnerId == belt.SiteId
                        && !x.Spoiled && !_state.Reservations.Any(y => y.Active && y.LotId == x.Id))
                    .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
                if (stack != null) checked { stack.Quantity++; }
                else _state.Lots.Add(beltLot);
                _state.Locations.RemoveAll(x => x.Id == belt.LocationId);
                _state.Belts.Remove(belt);
                return Record(requestId, playerId, true, "removed", null);
            }
        }

        public GoodsOutcome RemoveBeltDurably(string playerId, string requestId, string beltId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => RemoveBelt(playerId, requestId, beltId));
        }

        // Volatile primitive for tests. Live request handlers must call PlaceOnBeltDurably.
        // Puts exactly one unit of a lot onto a belt at the free position nearest its middle (BeltRules.FreePosition).
        public GoodsOutcome PlaceOnBelt(string playerId, string requestId, string lotId, string beltId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var lot = _state.Lots.FirstOrDefault(x => x.Id == lotId);
                var belt = _state.Belts.FirstOrDefault(x => x.Id == beltId);
                if (lot == null || belt == null) return Record(requestId, playerId, false, "forbidden", null);
                var source = _state.Locations.First(x => x.Id == lot.LocationId);
                if (!Allowed(playerId, source.Id) || lot.OwnerId != source.SiteId || source.SiteId != belt.SiteId
                    || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == belt.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (source.Kind == BeltLocationKind) return Record(requestId, playerId, false, "invalid-route", null);
                if (Available(lot) < 1) return Record(requestId, playerId, false, "quantity-or-reservation", null);
                var position = BeltRules.FreePosition(_state.Lots.Where(x => x.LocationId == belt.LocationId).Select(x => x.BeltPosition));
                if (position < 0) return Record(requestId, playerId, false, "belt-full", null);

                // All checks precede this single locked mutation. One unit splits off under a new ID unless it is the whole lot.
                var movedId = lot.Id;
                if (lot.Quantity == 1)
                {
                    lot.LocationId = belt.LocationId;
                    lot.BeltPosition = position;
                }
                else
                {
                    movedId = Guid.NewGuid().ToString("N");
                    lot.Quantity--;
                    _state.Lots.Add(new GoodsLot
                    {
                        Id = movedId, ItemId = lot.ItemId, OwnerId = lot.OwnerId, LocationId = belt.LocationId, Quantity = 1,
                        ExposureSeconds = lot.ExposureSeconds, SpoilAfterSeconds = lot.SpoilAfterSeconds, Spoiled = lot.Spoiled,
                        BeltPosition = position
                    });
                }
                return Record(requestId, playerId, true, "placed-on-belt", movedId);
            }
        }

        public GoodsOutcome PlaceOnBeltDurably(string playerId, string requestId, string lotId, string beltId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => PlaceOnBelt(playerId, requestId, lotId, beltId));
        }

        // Volatile primitive for tests. Live request handlers must call TakeFromBeltDurably.
        // Takes one riding item (a single-unit lot, wherever it has moved to on a belt) into the player's inventory,
        // keeping its ID and exposure. Refused without change when the inventory has no room.
        public GoodsOutcome TakeFromBelt(string playerId, string requestId, string lotId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var lot = _state.Lots.FirstOrDefault(x => x.Id == lotId);
                var belt = lot == null ? null : _state.Belts.FirstOrDefault(x => x.LocationId == lot.LocationId);
                if (belt == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == belt.SiteId))
                    return Record(requestId, playerId, false, lot == null || belt != null ? "forbidden" : "not-on-belt", null);
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                if (inventory == null || inventory.SiteId != belt.SiteId) return Record(requestId, playerId, false, "no-inventory", null);
                if (_state.Reservations.Any(x => x.Active && x.LotId == lot.Id)) return Record(requestId, playerId, false, "reserved", null);
                if (!Fits(inventory.Id, lot.ItemId, lot.Spoiled, lot.Quantity)) return Record(requestId, playerId, false, "capacity", null);

                // All checks precede this single locked mutation.
                lot.LocationId = inventory.Id;
                lot.BeltPosition = 0;
                return Record(requestId, playerId, true, "taken-from-belt", lot.Id);
            }
        }

        public GoodsOutcome TakeFromBeltDurably(string playerId, string requestId, string lotId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => TakeFromBelt(playerId, requestId, lotId));
        }

        // Moves riding items along their belts, in sub-steps. Belts are handled downstream first (the belt an item enters
        // before the belt it leaves), and each belt's items front first, so an item only ever closes up on items that have
        // already moved and a compressed line stays compressed. Runs inside Advance, under the lock.
        private void MoveBeltItems(long seconds)
        {
            if (_state.Belts.Count == 0) return;
            var riding = _state.Lots.Where(x => IsBeltLocation(x.LocationId)).ToList();
            if (riding.Count == 0) return;
            // Each site is its own belt network; only lifts link one floor to another.
            var cells = new Dictionary<string, Dictionary<(int, int, int), GoodsBelt>>();
            foreach (var site in _state.Belts.GroupBy(x => x.SiteId)) cells[site.Key] = BeltRules.ByCell(site);
            var links = _state.Belts.ToDictionary(x => x.Id, x => BeltRules.Link(cells[x.SiteId], x));
            var order = new List<GoodsBelt>();
            var visited = new HashSet<string>();
            foreach (var belt in _state.Belts.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                // Post-order along the links puts every belt after the one it feeds; a loop is broken where it was entered.
                var chain = new Stack<GoodsBelt>();
                for (var current = belt; current != null && visited.Add(current.Id); current = links[current.Id].Next) chain.Push(current);
                while (chain.Count > 0) order.Add(chain.Pop());
            }
            var byLocation = _state.Belts.ToDictionary(x => x.LocationId, x => x);
            var onBelt = _state.Belts.ToDictionary(x => x.Id, _ => new List<GoodsLot>());
            foreach (var lot in riding) onBelt[byLocation[lot.LocationId].Id].Add(lot);

            var step = BeltRules.UnitsPerSecond / BeltRules.SubStepsPerSecond;
            var moved = new HashSet<GoodsLot>();
            for (long sub = 0; sub < seconds * BeltRules.SubStepsPerSecond; sub++)
            {
                // An item handed across a loop's break point must not move twice in one sub-step.
                moved.Clear();
                foreach (var belt in order)
                {
                    var items = onBelt[belt.Id];
                    if (items.Count == 0) continue;
                    var link = links[belt.Id];
                    foreach (var lot in items.OrderByDescending(x => x.BeltPosition).ToList())
                    {
                        if (!moved.Add(lot)) continue;
                        var ahead = items.Where(x => x != lot && x.BeltPosition > lot.BeltPosition).Select(x => x.BeltPosition)
                            .DefaultIfEmpty(int.MaxValue).Min();
                        int limit;
                        if (ahead != int.MaxValue) limit = ahead - BeltRules.ItemSpacing;
                        else if (link.Next == null) limit = BeltRules.EndRest;
                        else if (link.Entry == 0)
                        {
                            var first = onBelt[link.Next.Id].Select(x => x.BeltPosition).DefaultIfEmpty(int.MaxValue).Min();
                            limit = first == int.MaxValue ? int.MaxValue : BeltRules.UnitsPerTile + first - BeltRules.ItemSpacing;
                        }
                        else
                        {
                            // Side-loading waits at the edge until the entry point on the next belt is clear.
                            var clear = onBelt[link.Next.Id].All(x => Math.Abs(x.BeltPosition - link.Entry) >= BeltRules.ItemSpacing);
                            limit = clear ? int.MaxValue : BeltRules.UnitsPerTile - 1;
                        }
                        var position = Math.Max(lot.BeltPosition, Math.Min(lot.BeltPosition + step, limit));
                        if (position < BeltRules.UnitsPerTile)
                        {
                            lot.BeltPosition = position;
                            continue;
                        }
                        items.Remove(lot);
                        onBelt[link.Next.Id].Add(lot);
                        lot.LocationId = link.Next.LocationId;
                        lot.BeltPosition = link.Entry == 0 ? position - BeltRules.UnitsPerTile : link.Entry;
                    }
                }
            }
        }

        private static void ValidateBelts(GoodsSnapshot state)
        {
            if (state.Belts.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || x.Direction < 0 || x.Direction > 3 || x.Lift < -1 || x.Lift > 1
                    || SiteGrid.CellProblem(state, x.SiteId, x.CellX, x.CellZ, 1, 1, x.Id, x.Level) != null
                    || SiteGrid.CellProblem(state, x.SiteId, x.CellX, x.CellZ, 1, 1, x.Id, x.ExitLevel) != null
                    || !state.Locations.Any(y => y.Id == x.LocationId && y.SiteId == x.SiteId && y.Kind == BeltLocationKind))
                || state.Belts.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Locations.Any(x => x.Kind == BeltLocationKind && !state.Belts.Any(y => y.LocationId == x.Id)))
                throw new InvalidOperationException("Goods snapshot violates belt invariants.");
            var beltLocations = new HashSet<string>(state.Belts.Select(x => x.LocationId));
            if (state.Lots.Any(x => beltLocations.Contains(x.LocationId)
                    && (x.Quantity != 1 || x.BeltPosition < 0 || x.BeltPosition >= BeltRules.UnitsPerTile)))
                throw new InvalidOperationException("A lot on a belt must be one unit at a position on the belt.");
        }
    }
}
