// Owns scene-independent, server-mutated goods, simulation time and replayable terminal command outcomes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("FoodFactoryGame.Goods.EditModeTests")]
[assembly: InternalsVisibleTo("FoodFactoryGame.Session.EditModeTests")]

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GoodsLocation
    {
        public string Id;
        public string SiteId;
        public string Kind;
        public int Capacity;
        public bool Refrigerated;
    }

    [Serializable] public sealed class GoodsLot
    {
        public string Id;
        public string ItemId;
        public string OwnerId;
        public string LocationId;
        public int Quantity;
        public long ExposureSeconds;
        public long SpoilAfterSeconds;
        public bool Spoiled;
        // Path position (BeltRules units) while the lot rides a belt; 0 anywhere else.
        public int BeltPosition;
    }

    [Serializable] public sealed class GoodsReservation
    {
        public string Id;
        public string LotId;
        public string PlayerId;
        public int Quantity;
        public bool Active;
    }

    [Serializable] public sealed class GoodsOutcome
    {
        public string RequestId;
        public string PlayerId;
        public bool Accepted;
        public string Reason;
        public string MovedLotId;
        public string ReservationId;
        public string JobId;
        // Machine created by the request (an equipment purchase, decision 0017); empty otherwise.
        public string EquipmentId;
        public long Revision;
    }

    [Serializable] public sealed class TransferIntent
    {
        public string RequestId;
        public string LotId;
        public string DestinationId;
        public string ReservationId;
        public int Quantity;
    }

    [Serializable] public sealed class GoodsGrant
    {
        public string PlayerId;
        public string SiteId;
    }

    [Serializable] public sealed class GoodsSnapshot
    {
        public const int CurrentSchema = 11;
        public int SchemaVersion = CurrentSchema;
        public string WorldId;
        public long ClockSeconds;
        public long Revision;
        public List<GoodsLocation> Locations = new();
        public List<GoodsLot> Lots = new();
        public List<GoodsReservation> Reservations = new();
        public List<GoodsOutcome> Outcomes = new();
        public List<GoodsGrant> Grants = new();
        public List<GoodsStation> Stations = new();
        public List<StationJob> Jobs = new();
        public List<GoodsEquipment> Equipment = new();
        public List<SiteLayout> SiteLayouts = new();
        public List<GoodsBelt> Belts = new();
        public List<GoodsCompany> Companies = new();
        public List<GoodsBuilding> Buildings = new();
        public List<GoodsEmployee> Employees = new();
        public List<GoodsSite> Sites = new();
        public List<GoodsTruck> Trucks = new();
    }

    public sealed partial class GoodsWorld
    {
        private readonly object _gate = new();
        // Item content, like recipes: registered by the server at start and never saved. Unregistered items stack to 1.
        private readonly Dictionary<string, int> _maxStacks = new();
        private GoodsSnapshot _state;
        // Revision of the last successful save of this world (or of the save it was loaded from); -1 if never saved.
        private long _committedRevision = -1;

        // Bootstrap is server-only: callers supply durable IDs; never expose this method to an RPC.
        public GoodsWorld(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId)) throw new ArgumentException("World ID required.");
            _state = new GoodsSnapshot { WorldId = worldId };
        }

        private GoodsWorld(GoodsSnapshot state) { _state = state; }

        public static GoodsWorld Restore(GoodsSnapshot state)
        {
            Validate(state);
            return new GoodsWorld(JsonUtility.FromJson<GoodsSnapshot>(JsonUtility.ToJson(state)));
        }

        public GoodsSnapshot Snapshot()
        {
            lock (_gate) return JsonUtility.FromJson<GoodsSnapshot>(JsonUtility.ToJson(_state));
        }

        // True while the in-memory world is ahead of its last save (AdvanceUncommitted, or never saved).
        public bool HasUncommittedChanges
        {
            get { lock (_gate) return _state.Revision != _committedRevision; }
        }

        // Called by GoodsSnapshotStore after a revision is committed to, or loaded from, a save.
        internal void MarkCommitted(long revision)
        {
            lock (_gate) _committedRevision = revision;
        }

        public void Bootstrap(GoodsLocation location)
        {
            lock (_gate)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.Id) || string.IsNullOrWhiteSpace(location.SiteId)
                    || string.IsNullOrWhiteSpace(location.Kind) || location.Capacity < 1 || _state.Locations.Any(x => x.Id == location.Id))
                    throw new ArgumentException("Invalid or duplicate location.");
                _state.Locations.Add(JsonUtility.FromJson<GoodsLocation>(JsonUtility.ToJson(location)));
                _state.Revision++;
            }
        }

        public void Bootstrap(GoodsLot lot)
        {
            lock (_gate)
            {
                if (lot == null || string.IsNullOrWhiteSpace(lot.Id) || string.IsNullOrWhiteSpace(lot.ItemId)
                    || string.IsNullOrWhiteSpace(lot.OwnerId) || lot.Quantity < 1 || lot.SpoilAfterSeconds < 1
                    || lot.ExposureSeconds < 0 || lot.Spoiled != (lot.ExposureSeconds >= lot.SpoilAfterSeconds)
                    || _state.Lots.Any(x => x.Id == lot.Id) || !Fits(lot.LocationId, lot.ItemId, lot.Spoiled, lot.Quantity))
                    throw new ArgumentException("Invalid lot or location capacity.");
                _state.Lots.Add(JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(lot)));
                _state.Revision++;
            }
        }

        public void Grant(string playerId, string siteId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || siteId == RoadSiteId || !_state.Locations.Any(x => x.SiteId == siteId))
                    throw new ArgumentException("Invalid grant.");
                if (_state.Grants.All(x => x.PlayerId != playerId || x.SiteId != siteId))
                {
                    _state.Grants.Add(new GoodsGrant { PlayerId = playerId, SiteId = siteId });
                    _state.Revision++;
                }
            }
        }

        // Server-only admission path: an existing grant (and inventory, if requested) succeeds without a write; anything
        // missing is created in one commit before success. Returns false with the prior state restored if the snapshot
        // cannot commit. inventoryCapacity > 0 ensures the player's inventory location on the site with at least that many
        // slots (an older, smaller inventory is enlarged, never shrunk). Starter goods (item, quantity and spoil threshold
        // only) are added, owned by the site, only when that inventory is created, so reconnecting never grants them again.
        public bool TryGrantDurably(string playerId, string siteId, string savePath, int inventoryCapacity = 0,
            IReadOnlyList<GoodsLot> starterGoods = null)
        {
            lock (_gate)
            {
                // Invalid grants throw here, before any mutation, rather than masquerading as I/O failure.
                if (string.IsNullOrWhiteSpace(playerId) || siteId == RoadSiteId || !_state.Locations.Any(x => x.SiteId == siteId))
                    throw new ArgumentException("Invalid grant.");
                starterGoods ??= Array.Empty<GoodsLot>();
                if (starterGoods.Any(x => x == null || string.IsNullOrWhiteSpace(x.ItemId) || x.Quantity < 1 || x.SpoilAfterSeconds < 1)
                    || GoodsSlots.SlotsUsed(starterGoods, MaxStackLocked) > Math.Max(0, inventoryCapacity))
                    throw new ArgumentException("Invalid starter goods or they exceed the inventory capacity.");
                var inventoryId = InventoryLocationId(playerId);
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == inventoryId);
                var needsInventory = inventoryCapacity > 0 && inventory == null;
                var needsSlots = inventory != null && inventory.Capacity < inventoryCapacity;
                if (needsInventory && _state.Lots.Any(x => x.Id.StartsWith($"starter:{playerId}:", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"Starter goods for {playerId} exist without an inventory.");
                if (CanView(playerId, siteId) && !needsInventory && !needsSlots) return true;
                return Durably(savePath, () =>
                {
                    // Re-resolved inside: Durably restores a copied state on failure, so no reference may cross it.
                    var target = _state.Locations.FirstOrDefault(x => x.Id == inventoryId);
                    if (needsSlots)
                    {
                        target.Capacity = inventoryCapacity;
                        _state.Revision++;
                    }
                    if (needsInventory)
                    {
                        Bootstrap(new GoodsLocation { Id = inventoryId, SiteId = siteId, Kind = "carried", Capacity = inventoryCapacity });
                        for (var index = 0; index < starterGoods.Count; index++)
                            Bootstrap(new GoodsLot
                            {
                                Id = $"starter:{playerId}:{index}", ItemId = starterGoods[index].ItemId, OwnerId = siteId,
                                LocationId = inventoryId, Quantity = starterGoods[index].Quantity,
                                SpoilAfterSeconds = starterGoods[index].SpoilAfterSeconds
                            });
                    }
                    Grant(playerId, siteId);
                    return true;
                }, () => false);
            }
        }

        public bool CanView(string playerId, string siteId)
        {
            lock (_gate) return _state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId);
        }

        public GoodsSnapshot View(string playerId, string siteId)
        {
            lock (_gate)
            {
                if (!CanView(playerId, siteId)) return null;
                var view = Snapshot();
                view.Locations = view.Locations.Where(x => x.SiteId == siteId).ToList();
                var ids = new HashSet<string>(view.Locations.Select(x => x.Id));
                view.Lots = view.Lots.Where(x => ids.Contains(x.LocationId)).ToList();
                view.Stations = view.Stations.Where(x => x.SiteId == siteId).ToList();
                var stationIds = new HashSet<string>(view.Stations.Select(x => x.Id));
                view.Jobs = view.Jobs.Where(x => stationIds.Contains(x.StationId)).ToList();
                view.Equipment = view.Equipment.Where(x => x.SiteId == siteId).ToList();
                view.SiteLayouts = view.SiteLayouts.Where(x => x.SiteId == siteId).ToList();
                view.Belts = view.Belts.Where(x => x.SiteId == siteId).ToList();
                view.Buildings = view.Buildings.Where(x => x.SiteId == siteId).ToList();
                view.Companies = view.Companies.Where(x => x.SiteIds.Contains(siteId)).ToList();
                // Scripts reach clients through the employee object, not with every baseline.
                view.Employees = view.Employees.Where(x => x.SiteId == siteId).ToList();
                foreach (var employee in view.Employees) employee.Script = "";
                ViewLogistics(view, siteId);
                view.Reservations.Clear();
                view.Outcomes.Clear();
                view.Grants.Clear();
                return view;
            }
        }

        // Volatile domain primitive for bootstrap/tests. Live ticking uses AdvanceUncommitted plus TryCommitDurably
        // (decision 0016), or TryAdvanceDurably.
        public void Advance(long seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            lock (_gate)
            {
                if (seconds == 0) return;
                checked { _state.ClockSeconds += seconds; }
                // Blocked outputs emit before exposure so they age with this step, like any lot already present.
                EmitBlockedOutputs();
                foreach (var lot in _state.Lots)
                {
                    if (lot.Spoiled || _state.Locations.First(x => x.Id == lot.LocationId).Refrigerated) continue;
                    lot.ExposureSeconds += Math.Min(seconds, lot.SpoilAfterSeconds - lot.ExposureSeconds);
                    lot.Spoiled = lot.ExposureSeconds >= lot.SpoilAfterSeconds;
                }
                ProgressJobs(seconds);
                StartReadyJobs();
                MoveBeltItems(seconds);
                MoveTrucks(seconds);
                _state.Revision++;
            }
        }

        // Volatile domain primitive for bootstrap/tests. Live acknowledgments use ReserveDurably.
        public bool Reserve(string playerId, string reservationId, string lotId, int quantity)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(reservationId) || _state.Reservations.Any(x => x.Id == reservationId)) return false;
                var lot = _state.Lots.FirstOrDefault(x => x.Id == lotId);
                if (lot == null || quantity <= 0 || !Allowed(playerId, lot.LocationId)
                    || quantity > Available(lot)) return false;
                _state.Reservations.Add(new GoodsReservation { Id = reservationId, LotId = lotId, PlayerId = playerId, Quantity = quantity, Active = true });
                _state.Revision++;
                return true;
            }
        }

        // Volatile primitive for tests. Live cancellation uses CancelDurably; committed transfers are final.
        public GoodsOutcome Cancel(string playerId, string requestId, string reservationId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var reservation = _state.Reservations.FirstOrDefault(x => x.Id == reservationId && x.PlayerId == playerId && x.Active);
                if (reservation == null) return Record(requestId, playerId, false, "reservation-unavailable", null);
                reservation.Active = false;
                return Record(requestId, playerId, true, "cancelled", null);
            }
        }

        // Volatile primitive for tests. Live request handlers must call TransferDurably.
        public GoodsOutcome Transfer(string playerId, TransferIntent intent)
        {
            lock (_gate)
            {
                if (intent == null) return new GoodsOutcome { Accepted = false, Reason = "invalid-intent" };
                var replay = Replay(playerId, intent.RequestId);
                if (replay != null) return replay;
                if (string.IsNullOrWhiteSpace(intent.RequestId) || string.IsNullOrWhiteSpace(playerId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var lot = _state.Lots.FirstOrDefault(x => x.Id == intent.LotId);
                var destination = _state.Locations.FirstOrDefault(x => x.Id == intent.DestinationId);
                if (lot == null) return Record(intent.RequestId, playerId, false, "forbidden", null);
                var source = _state.Locations.First(x => x.Id == lot.LocationId);
                if (!Allowed(playerId, source.Id) || lot.OwnerId != source.SiteId)
                    return Record(intent.RequestId, playerId, false, "forbidden", null);
                // Goods ride belts only through PlaceOnBelt and leave them only through TakeFromBelt or with the belt (RemoveBelt).
                // Truck cargo is loaded and unloaded only by the truck at a dock (decision 0022).
                if (destination == null || lot.LocationId == destination.Id || source.Kind == BeltLocationKind
                    || destination.Kind == BeltLocationKind || source.Kind == VehicleLocationKind || destination.Kind == VehicleLocationKind)
                    return Record(intent.RequestId, playerId, false, "invalid-route", null);
                if (source.SiteId != destination.SiteId || !Allowed(playerId, destination.Id))
                    return Record(intent.RequestId, playerId, false, "forbidden", null);
                var reservation = _state.Reservations.FirstOrDefault(x => x.Id == intent.ReservationId && x.Active);
                if (intent.Quantity <= 0 || (!string.IsNullOrEmpty(intent.ReservationId) && reservation == null)
                    || (reservation == null ? intent.Quantity > Available(lot)
                        : reservation.LotId != lot.Id || reservation.PlayerId != playerId || reservation.Quantity != intent.Quantity
                          || intent.Quantity > Available(lot) + reservation.Quantity))
                    return Record(intent.RequestId, playerId, false, "quantity-or-reservation", null);
                if (!Fits(destination.Id, lot.ItemId, lot.Spoiled, intent.Quantity))
                    return Record(intent.RequestId, playerId, false, "capacity", null);

                // All checks precede the single locked mutation. A partial move is a split with a new durable ID.
                var movedId = lot.Id;
                if (intent.Quantity == lot.Quantity)
                    lot.LocationId = destination.Id;
                else
                {
                    movedId = Guid.NewGuid().ToString("N");
                    lot.Quantity -= intent.Quantity;
                    _state.Lots.Add(new GoodsLot
                    {
                        Id = movedId, ItemId = lot.ItemId, OwnerId = lot.OwnerId, LocationId = destination.Id,
                        Quantity = intent.Quantity, ExposureSeconds = lot.ExposureSeconds,
                        SpoilAfterSeconds = lot.SpoilAfterSeconds, Spoiled = lot.Spoiled
                    });
                }
                if (reservation != null) reservation.Active = false;
                StartReadyJobs();
                return Record(intent.RequestId, playerId, true, "transferred", movedId);
            }
        }

        // The network boundary uses this path: commit outcome and snapshot before acknowledging success.
        // A failed disk write restores the entire pre-command state, including reservation claims.
        public GoodsOutcome TransferDurably(string playerId, TransferIntent intent, string savePath)
        {
            return Commit(playerId, intent?.RequestId, savePath, () => Transfer(playerId, intent));
        }

        // Server generates an opaque reservation ID and commits it together with its terminal outcome.
        public GoodsOutcome ReserveDurably(string playerId, string requestId, string lotId, int quantity, string savePath)
        {
            return Commit(playerId, requestId, savePath, () =>
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var lot = _state.Lots.FirstOrDefault(x => x.Id == lotId);
                if (lot == null || !Allowed(playerId, lot.LocationId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (quantity <= 0 || quantity > Available(lot))
                    return Record(requestId, playerId, false, "quantity-or-reservation", null);
                var reservationId = Guid.NewGuid().ToString("N");
                _state.Reservations.Add(new GoodsReservation
                {
                    Id = reservationId, LotId = lotId, PlayerId = playerId, Quantity = quantity, Active = true
                });
                var result = Record(requestId, playerId, true, "reserved", null);
                result.ReservationId = reservationId;
                _state.Outcomes[_state.Outcomes.Count - 1].ReservationId = reservationId;
                return result;
            });
        }

        public GoodsOutcome CancelDurably(string playerId, string requestId, string reservationId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => Cancel(playerId, requestId, reservationId));
        }

        // Returns false without advancing the clock or exposure if the save cannot commit.
        public bool TryAdvanceDurably(long seconds, string savePath)
        {
            lock (_gate)
            {
                if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
                if (seconds == 0) return true;
                return Durably(savePath, () =>
                {
                    Advance(seconds);
                    return true;
                }, () => false);
            }
        }

        // Live clock step between commits (decision 0016): advances in memory only. The next TryCommitDurably, or any
        // durable command (it saves the whole world), persists it; a crash before then loses it together with everything
        // it produced, so goods and cash roll back as one revision. A failure restores the prior state and rethrows.
        public void AdvanceUncommitted(long seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
            lock (_gate)
            {
                if (seconds == 0) return;
                var before = Snapshot();
                try { Advance(seconds); }
                catch
                {
                    _state = before;
                    throw;
                }
            }
        }

        // Saves the world if it is ahead of its last save. Returns false, leaving memory unchanged, if the save cannot
        // commit; nothing pending returns true without touching the disk.
        public bool TryCommitDurably(string savePath)
        {
            lock (_gate)
            {
                if (!HasUncommittedChanges) return true;
                try
                {
                    GoodsSnapshotStore.Save(this, savePath);
                    return true;
                }
                catch (Exception error) when (PersistenceError(error))
                {
                    return false;
                }
            }
        }

        private GoodsOutcome Commit(string playerId, string requestId, string savePath, Func<GoodsOutcome> action)
        {
            lock (_gate)
            {
                var revision = _state.Revision;
                return Durably(savePath, action, () => new GoodsOutcome
                {
                    RequestId = requestId, PlayerId = playerId, Reason = "persistence-unavailable", Revision = revision
                });
            }
        }

        // The one durable boundary: runs the mutation and commits it if the revision changed. Any exception restores the
        // prior state; a persistence failure then returns unavailable(), anything else is rethrown. Validate inputs that
        // should throw (ArgumentException counts as a persistence failure here) before calling this.
        private T Durably<T>(string savePath, Func<T> action, Func<T> unavailable)
        {
            lock (_gate)
            {
                var before = Snapshot();
                try
                {
                    var result = action();
                    if (_state.Revision != before.Revision) GoodsSnapshotStore.Save(this, savePath);
                    return result;
                }
                catch (Exception error)
                {
                    _state = before;
                    if (!PersistenceError(error)) throw;
                    return unavailable();
                }
            }
        }

        private static bool PersistenceError(Exception error) => error is IOException || error is UnauthorizedAccessException
            || error is ArgumentException || error is InvalidOperationException;

        public bool Merge(string firstId, string secondId)
        {
            lock (_gate)
            {
                var first = _state.Lots.FirstOrDefault(x => x.Id == firstId);
                var second = _state.Lots.FirstOrDefault(x => x.Id == secondId);
                if (first == null || second == null || first == second || first.ItemId != second.ItemId
                    || first.OwnerId != second.OwnerId || first.LocationId != second.LocationId
                    || first.ExposureSeconds != second.ExposureSeconds || first.SpoilAfterSeconds != second.SpoilAfterSeconds
                    || first.Spoiled != second.Spoiled || IsBeltLocation(first.LocationId)
                    || _state.Reservations.Any(x => x.Active && (x.LotId == firstId || x.LotId == secondId))) return false;
                checked { first.Quantity += second.Quantity; }
                _state.Lots.Remove(second);
                _state.Revision++;
                return true;
            }
        }

        private GoodsOutcome Replay(string playerId, string id)
        {
            var original = _state.Outcomes.FirstOrDefault(x => x.PlayerId == playerId && x.RequestId == id && !string.IsNullOrWhiteSpace(id));
            if (original == null) return null;
            return JsonUtility.FromJson<GoodsOutcome>(JsonUtility.ToJson(original));
        }

        private GoodsOutcome Record(string id, string player, bool accepted, string reason, string moved)
        {
            _state.Revision++;
            var outcome = new GoodsOutcome { RequestId = id, PlayerId = player, Accepted = accepted, Reason = reason, MovedLotId = moved, Revision = _state.Revision };
            _state.Outcomes.Add(outcome);
            return JsonUtility.FromJson<GoodsOutcome>(JsonUtility.ToJson(outcome));
        }

        private int Available(GoodsLot lot) => lot.Quantity - _state.Reservations.Where(x => x.Active && x.LotId == lot.Id).Sum(x => x.Quantity);
        private bool Allowed(string player, string locationId)
        {
            var location = _state.Locations.First(x => x.Id == locationId);
            return _state.Grants.Any(x => x.PlayerId == player && x.SiteId == location.SiteId);
        }

        // Server content: the largest quantity of an item one slot holds. Register every item before serving requests;
        // an item never registered stacks to 1, so its capacity counts units.
        public void RegisterItem(string itemId, int maxStack)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(itemId) || maxStack < 1 || _maxStacks.ContainsKey(itemId))
                    throw new ArgumentException("Invalid or duplicate item.");
                _maxStacks.Add(itemId, maxStack);
            }
        }

        public int MaxStack(string itemId)
        {
            lock (_gate) return MaxStackLocked(itemId);
        }

        private int MaxStackLocked(string itemId) => itemId != null && _maxStacks.TryGetValue(itemId, out var size) ? size : 1;

        // Capacity is checked only when goods enter a location. Spoiling (a new stack) or a smaller max stack in content can
        // leave a location over its slots; that blocks further entries and never removes goods.
        private bool Fits(string id, string itemId, bool spoiled, int quantity) => quantity > 0
            && GoodsSlots.FreeUnits(_state.Locations.FirstOrDefault(x => x.Id == id), _state.Lots, itemId, spoiled, MaxStackLocked) >= quantity;

        private bool FitsAll(string id, IEnumerable<GoodsLot> incoming)
        {
            var location = _state.Locations.FirstOrDefault(x => x.Id == id);
            return location != null
                && GoodsSlots.SlotsUsed(_state.Lots.Where(x => x.LocationId == id).Concat(incoming), MaxStackLocked) <= location.Capacity;
        }

        public static void Validate(GoodsSnapshot state)
        {
            if (state == null || state.SchemaVersion != GoodsSnapshot.CurrentSchema || string.IsNullOrWhiteSpace(state.WorldId)
                || state.ClockSeconds < 0 || state.Revision < 0 || state.Locations == null || state.Lots == null
                || state.Grants == null || state.Reservations == null || state.Outcomes == null
                || state.Stations == null || state.Jobs == null || state.Equipment == null || state.SiteLayouts == null || state.Belts == null
                || state.Companies == null || state.Buildings == null || state.Employees == null || state.Sites == null || state.Trucks == null)
                throw new InvalidOperationException("Unsupported or invalid goods snapshot schema.");
            if (state.Locations.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.SiteId) || x.Capacity < 1)
                || state.Locations.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Lots.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.ItemId)
                    || string.IsNullOrWhiteSpace(x.OwnerId)
                    || x.Quantity < 1 || x.ExposureSeconds < 0 || x.SpoilAfterSeconds < 1
                    || x.Spoiled != (x.ExposureSeconds >= x.SpoilAfterSeconds)
                    || !state.Locations.Any(y => y.Id == x.LocationId))
                || state.Lots.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Reservations.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || x.Quantity < 1
                    || (x.Active && !state.Lots.Any(y => y.Id == x.LotId)))
                || state.Reservations.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Lots.Any(x => state.Reservations.Where(y => y.Active && y.LotId == x.Id).Sum(y => (long)y.Quantity) > x.Quantity)
                || state.Outcomes.Any(x => x == null || string.IsNullOrWhiteSpace(x.RequestId) || string.IsNullOrWhiteSpace(x.PlayerId))
                || state.Outcomes.GroupBy(x => new { x.PlayerId, x.RequestId }).Any(x => x.Count() != 1)
                || state.Grants.Any(x => x == null || string.IsNullOrWhiteSpace(x.PlayerId)
                    || !state.Locations.Any(y => y.SiteId == x.SiteId)))
                throw new InvalidOperationException("Goods snapshot violates identity, capacity, or reservation invariants.");
            ValidateProduction(state);
            ValidateEquipment(state);
            ValidateBelts(state);
            ValidateCompanies(state);
            ValidateBuildings(state);
            ValidateEmployees(state);
            ValidateTrucks(state);
        }
    }
}
