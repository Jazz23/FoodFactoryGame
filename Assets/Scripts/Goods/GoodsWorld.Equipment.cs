// Equipment is a physical, stable-ID machine that is either placed on a site grid or held in one player's inventory.
// A placed piece owns exactly one station and two buffer locations (<id>:in, <id>:out); pickup empties and removes them,
// placement recreates them under the same IDs. Nothing is ever deleted and recreated under a new equipment ID.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    public enum EquipmentState
    {
        Placed,
        Held
    }

    [Serializable] public sealed class GoodsEquipment
    {
        public string Id;
        public string Kind;
        public string SiteId;
        public EquipmentState State;
        // Player whose inventory holds the piece; empty while placed.
        public string HolderId;
        // Anchor is the footprint's minimum cell; meaningful only while placed.
        public int CellX;
        public int CellZ;
        public int Rotation;
        // Copied from content when the piece is created, like a job's recipe copy, so recovery never needs registered content.
        public int Width;
        public int Depth;
        public int InputCapacity;
        public int OutputCapacity;
        // Goods in a refrigerated buffer do not spoil (their exposure is paused). A fridge is a machine with no recipes and a
        // refrigerated input buffer (decision 0018).
        public bool InputRefrigerated;
        public bool OutputRefrigerated;

        public string InputLocationId => Id + ":in";
        public string OutputLocationId => Id + ":out";
    }

    // Site grid bounds in cells. Server data, not scene authoring.
    [Serializable] public sealed class SiteLayout
    {
        public string SiteId;
        public int Width;
        public int Depth;
    }

    public sealed partial class GoodsWorld
    {
        public static string InventoryLocationId(string playerId) => "carried:" + playerId;

        public void Bootstrap(SiteLayout layout)
        {
            lock (_gate)
            {
                if (layout == null || string.IsNullOrWhiteSpace(layout.SiteId) || layout.Width < 1 || layout.Depth < 1
                    || _state.SiteLayouts.Any(x => x.SiteId == layout.SiteId))
                    throw new ArgumentException("Invalid or duplicate site layout.");
                _state.SiteLayouts.Add(JsonUtility.FromJson<SiteLayout>(JsonUtility.ToJson(layout)));
                _state.Revision++;
            }
        }

        // Server-only, like location bootstrap: creates an already-placed piece with its station and buffers.
        public void Bootstrap(GoodsEquipment equipment)
        {
            lock (_gate)
            {
                var copy = equipment == null ? null : JsonUtility.FromJson<GoodsEquipment>(JsonUtility.ToJson(equipment));
                if (copy == null || string.IsNullOrWhiteSpace(copy.Id) || string.IsNullOrWhiteSpace(copy.Kind)
                    || copy.State != EquipmentState.Placed || !string.IsNullOrEmpty(copy.HolderId)
                    || copy.Width < 1 || copy.Depth < 1 || copy.InputCapacity < 1 || copy.OutputCapacity < 1
                    || _state.Equipment.Any(x => x.Id == copy.Id) || _state.Stations.Any(x => x.Id == copy.Id)
                    || _state.Locations.Any(x => x.Id == copy.InputLocationId || x.Id == copy.OutputLocationId)
                    || SiteGrid.PlacementProblem(_state, copy, copy.CellX, copy.CellZ, copy.Rotation) != null)
                    throw new ArgumentException("Invalid, duplicate or unplaceable equipment.");
                _state.Equipment.Add(copy);
                AddPlacedParts(copy);
                _state.Revision++;
            }
        }

        // Server-only start step (decision 0009): pieces of a kind take the content's current buffer slot counts, placed
        // buffers included, and the change commits before any request is served. Lower counts may leave a buffer over
        // its slots; that only blocks entries and never removes goods. Returns whether anything changed; a failed save
        // restores the prior state and rethrows.
        public bool ApplyEquipmentCapacitiesDurably(string kind, int inputCapacity, int outputCapacity, string savePath)
        {
            if (string.IsNullOrWhiteSpace(kind) || inputCapacity < 1 || outputCapacity < 1)
                throw new ArgumentException("Invalid equipment capacities.");
            lock (_gate)
            {
                var stale = _state.Equipment
                    .Where(x => x.Kind == kind && (x.InputCapacity != inputCapacity || x.OutputCapacity != outputCapacity)).ToList();
                if (stale.Count == 0) return false;
                var before = Snapshot();
                foreach (var equipment in stale)
                {
                    equipment.InputCapacity = inputCapacity;
                    equipment.OutputCapacity = outputCapacity;
                    foreach (var location in _state.Locations.Where(x => x.Id == equipment.InputLocationId)) location.Capacity = inputCapacity;
                    foreach (var location in _state.Locations.Where(x => x.Id == equipment.OutputLocationId)) location.Capacity = outputCapacity;
                }
                _state.Revision++;
                try
                {
                    GoodsSnapshotStore.Save(this, savePath);
                    return true;
                }
                catch
                {
                    _state = before;
                    throw;
                }
            }
        }

        // Volatile primitive for tests. Live request handlers must call PickUpDurably.
        // A running job refunds its inputs, a blocked job hands over its output, and both buffers are swept into the
        // player's inventory location. Everything must fit together or the pickup is refused and nothing changes.
        public GoodsOutcome PickUp(string playerId, string requestId, string equipmentId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var equipment = _state.Equipment.FirstOrDefault(x => x.Id == equipmentId);
                if (equipment == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == equipment.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (equipment.State != EquipmentState.Placed) return Record(requestId, playerId, false, "not-placed", null);
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                if (inventory == null || inventory.SiteId != equipment.SiteId)
                    return Record(requestId, playerId, false, "no-inventory", null);
                var buffered = _state.Lots
                    .Where(x => x.LocationId == equipment.InputLocationId || x.LocationId == equipment.OutputLocationId).ToList();
                if (buffered.Any(x => _state.Reservations.Any(y => y.Active && y.LotId == x.Id)))
                    return Record(requestId, playerId, false, "reserved", null);
                var station = _state.Stations.First(x => x.Id == equipment.Id);
                var job = _state.Jobs.FirstOrDefault(x => x.StationId == station.Id);
                var returned = job == null ? new List<GoodsLot>()
                    : job.State == StationJobState.Blocked ? new List<GoodsLot> { OutputLot(job, station, inventory.Id, 0) }
                    : job.Inputs.Select(x => JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(x))).ToList();
                if (!FitsAll(inventory.Id, buffered.Concat(returned)))
                    return Record(requestId, playerId, false, "capacity", null);

                // All checks precede this single locked mutation. Swept lots keep their IDs and exposure.
                foreach (var lot in buffered) lot.LocationId = inventory.Id;
                foreach (var lot in returned)
                {
                    lot.LocationId = inventory.Id;
                    _state.Lots.Add(lot);
                }
                if (job != null) _state.Jobs.Remove(job);
                _state.Stations.Remove(station);
                _state.Locations.RemoveAll(x => x.Id == equipment.InputLocationId || x.Id == equipment.OutputLocationId);
                equipment.State = EquipmentState.Held;
                equipment.HolderId = playerId;
                equipment.CellX = equipment.CellZ = equipment.Rotation = 0;
                var result = Record(requestId, playerId, true, "picked-up", null);
                result.JobId = job?.Id;
                _state.Outcomes[_state.Outcomes.Count - 1].JobId = job?.Id;
                return result;
            }
        }

        public GoodsOutcome PickUpDurably(string playerId, string requestId, string equipmentId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => PickUp(playerId, requestId, equipmentId));
        }

        // Volatile primitive for tests. Live request handlers must call PlaceDurably.
        // Player position is not checked: it is presentation-only (decision 0005).
        public GoodsOutcome Place(string playerId, string requestId, string equipmentId, int cellX, int cellZ, int rotation)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var equipment = _state.Equipment.FirstOrDefault(x => x.Id == equipmentId);
                if (equipment == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == equipment.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (equipment.State != EquipmentState.Held || equipment.HolderId != playerId)
                    return Record(requestId, playerId, false, "not-held", null);
                var problem = SiteGrid.PlacementProblem(_state, equipment, cellX, cellZ, rotation);
                if (problem != null) return Record(requestId, playerId, false, problem, null);

                equipment.State = EquipmentState.Placed;
                equipment.HolderId = "";
                equipment.CellX = cellX;
                equipment.CellZ = cellZ;
                equipment.Rotation = rotation;
                AddPlacedParts(equipment);
                return Record(requestId, playerId, true, "placed", null);
            }
        }

        public GoodsOutcome PlaceDurably(string playerId, string requestId, string equipmentId, int cellX, int cellZ, int rotation, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => Place(playerId, requestId, equipmentId, cellX, cellZ, rotation));
        }

        private void AddPlacedParts(GoodsEquipment equipment)
        {
            _state.Locations.Add(new GoodsLocation
            {
                Id = equipment.InputLocationId, SiteId = equipment.SiteId, Kind = "machine-buffer", Capacity = equipment.InputCapacity,
                Refrigerated = equipment.InputRefrigerated
            });
            _state.Locations.Add(new GoodsLocation
            {
                Id = equipment.OutputLocationId, SiteId = equipment.SiteId, Kind = "machine-buffer",
                Capacity = equipment.OutputCapacity, Refrigerated = equipment.OutputRefrigerated
            });
            _state.Stations.Add(new GoodsStation
            {
                Id = equipment.Id, SiteId = equipment.SiteId, Kind = equipment.Kind,
                InputLocationId = equipment.InputLocationId, OutputLocationId = equipment.OutputLocationId
            });
        }

        private static void ValidateEquipment(GoodsSnapshot state)
        {
            if (state.SiteLayouts.Any(x => x == null || string.IsNullOrWhiteSpace(x.SiteId) || x.Width < 1 || x.Depth < 1)
                || state.SiteLayouts.GroupBy(x => x.SiteId).Any(x => x.Count() != 1)
                || state.Equipment.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Kind)
                    || !state.SiteLayouts.Any(y => y.SiteId == x.SiteId)
                    || x.Width < 1 || x.Depth < 1 || x.InputCapacity < 1 || x.OutputCapacity < 1
                    || (x.State != EquipmentState.Placed && x.State != EquipmentState.Held))
                || state.Equipment.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates equipment or layout invariants.");
            foreach (var equipment in state.Equipment)
            {
                var station = state.Stations.FirstOrDefault(x => x.Id == equipment.Id);
                var input = state.Locations.FirstOrDefault(x => x.Id == equipment.InputLocationId);
                var output = state.Locations.FirstOrDefault(x => x.Id == equipment.OutputLocationId);
                var valid = equipment.State == EquipmentState.Held
                    ? !string.IsNullOrWhiteSpace(equipment.HolderId) && station == null && input == null && output == null
                    : string.IsNullOrEmpty(equipment.HolderId)
                        && SiteGrid.PlacementProblem(state, equipment, equipment.CellX, equipment.CellZ, equipment.Rotation) == null
                        && station != null && station.SiteId == equipment.SiteId && station.Kind == equipment.Kind
                        && station.InputLocationId == equipment.InputLocationId && station.OutputLocationId == equipment.OutputLocationId
                        && input != null && input.SiteId == equipment.SiteId && input.Capacity == equipment.InputCapacity
                        && input.Refrigerated == equipment.InputRefrigerated
                        && output != null && output.SiteId == equipment.SiteId && output.Capacity == equipment.OutputCapacity
                        && output.Refrigerated == equipment.OutputRefrigerated;
                if (!valid) throw new InvalidOperationException($"Equipment {equipment.Id} is inconsistent with its placement.");
            }
            if (state.Stations.Any(x => !state.Equipment.Any(y => y.Id == x.Id && y.State == EquipmentState.Placed)))
                throw new InvalidOperationException("Every station must belong to placed equipment.");
        }
    }
}
