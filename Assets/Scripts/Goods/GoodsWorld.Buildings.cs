// A building shell is a server-owned rectangle on a site grid whose perimeter cells are walls, except its ground-floor door
// cells (decision 0019). Walls block equipment and belts through SiteGrid.CellProblem; the interior and doors are ordinary
// cells. Shells are created only by the server and are never moved or removed. A factory can gain floors (decision 0020):
// each upper floor covers the interior at a higher level, walls surround every floor, and one elevator cell, fixed by the
// first added floor, runs through all of them and never holds equipment or belts. Floors are never removed.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GridCell
    {
        public int X;
        public int Z;
    }

    [Serializable] public sealed class GoodsBuilding
    {
        public string Id;
        public string SiteId;
        // GoodsWorld.RestaurantKind or FactoryKind; only a factory can add floors (GDD section 5).
        public string Kind = GoodsWorld.RestaurantKind;
        // Anchor is the footprint's minimum cell; the footprint includes the walls.
        public int CellX;
        public int CellZ;
        public int Width;
        public int Depth;
        // Perimeter cells, never corners, that are ground-floor openings instead of walls.
        public List<GridCell> Doors = new();
        // Storeys including the ground floor (level 0); floor n is level n - 1.
        public int Floors = 1;
        // Elevator shaft cell, an interior cell; meaningful only while Floors > 1.
        public int ElevatorX;
        public int ElevatorZ;

        public bool HasElevator => Floors > 1;
    }

    // Content, not state: what a floor costs and how tall a factory may grow. Registered by the server, never saved.
    [Serializable] public sealed class FloorOffer
    {
        // Whole cents per interior cell of the new floor.
        public long CentsPerCell;
        // Most storeys a factory may have, ground floor included.
        public int MaxFloors;
    }

    public sealed partial class GoodsWorld
    {
        public const string RestaurantKind = "restaurant";
        public const string FactoryKind = "factory";
        // Smallest shell with an interior: one cell inside a ring of walls.
        public const int MinimumBuildingSize = 3;

        private FloorOffer _floorOffer;

        // Price of the next floor of a building: one charge per interior cell. Shared with the client's HUD preview.
        public static long FloorPriceCents(GoodsBuilding building, FloorOffer offer) =>
            offer == null ? 0 : checked((long)(building.Width - 2) * (building.Depth - 2) * offer.CentsPerCell);

        public void RegisterFloorOffer(FloorOffer offer)
        {
            lock (_gate)
            {
                if (offer is null || offer.CentsPerCell < 1 || offer.MaxFloors < 2 || _floorOffer is not null)
                    throw new ArgumentException("Invalid or duplicate floor offer.");
                _floorOffer = JsonUtility.FromJson<FloorOffer>(JsonUtility.ToJson(offer));
            }
        }

        // Server-only, like layout bootstrap: the shell must fit the site, overlap no other shell, and its walls must not
        // cover placed equipment or belts.
        public void Bootstrap(GoodsBuilding building)
        {
            lock (_gate)
            {
                var copy = building == null ? null : JsonUtility.FromJson<GoodsBuilding>(JsonUtility.ToJson(building));
                if (copy == null || _state.Buildings.Any(x => x.Id == copy.Id) || BuildingProblem(_state, copy) != null)
                    throw new ArgumentException("Invalid, duplicate or blocked building.");
                _state.Buildings.Add(copy);
                _state.Revision++;
            }
        }

        // Volatile primitive for tests. Live request handlers must call AddFloorDurably.
        // Outsourced construction (GDD section 5), instant for now: the site's company pays and the factory gains a floor in
        // one mutation. The first added floor puts the elevator at (elevatorX, elevatorZ), which must be a clear interior
        // cell; later floors extend the existing shaft and ignore the cell. Like a purchase, only the accepted order (the one
        // that charges) is recorded, so a retried request replays it and never pays twice.
        public GoodsOutcome AddFloor(string playerId, string requestId, string buildingId, int elevatorX, int elevatorZ)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                var building = _state.Buildings.FirstOrDefault(x => x.Id == buildingId);
                if (building is null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == building.SiteId))
                    return Reject("forbidden");
                if (building.Kind != FactoryKind) return Reject("not-a-factory");
                if (_floorOffer is null) return Reject("no-floor-offer");
                if (building.Floors >= _floorOffer.MaxFloors) return Reject("max-floors");
                var company = CompanyOfSiteLocked(building.SiteId);
                if (company is null) return Reject("no-company");
                if (!building.HasElevator)
                {
                    if (!SiteGrid.IsInterior(building, elevatorX, elevatorZ)) return Reject("invalid-elevator");
                    var problem = SiteGrid.CellProblem(_state, building.SiteId, elevatorX, elevatorZ, 1, 1, null);
                    if (problem is not null) return Reject(problem);
                }
                var price = FloorPriceCents(building, _floorOffer);
                if (_state.Companies.First(x => x.Id == company).Cash < price) return Reject("insufficient-funds");

                // All checks precede this single locked mutation.
                TryDebit(company, price);
                if (!building.HasElevator)
                {
                    building.ElevatorX = elevatorX;
                    building.ElevatorZ = elevatorZ;
                }
                building.Floors++;
                return Record(requestId, playerId, true, "floor-added", null);
            }
        }

        public GoodsOutcome AddFloorDurably(string playerId, string requestId, string buildingId, int elevatorX, int elevatorZ, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => AddFloor(playerId, requestId, buildingId, elevatorX, elevatorZ));
        }

        // Null when the shell is well formed and its walls and elevator are clear; checks everything except ID uniqueness.
        private static string BuildingProblem(GoodsSnapshot state, GoodsBuilding building)
        {
            var layout = state.SiteLayouts.FirstOrDefault(x => x.SiteId == building.SiteId);
            if (string.IsNullOrWhiteSpace(building.Id) || layout == null || building.Doors == null
                || (building.Kind != RestaurantKind && building.Kind != FactoryKind)
                || building.Width < MinimumBuildingSize || building.Depth < MinimumBuildingSize
                || building.CellX < 0 || building.CellZ < 0
                || building.CellX + building.Width > layout.Width || building.CellZ + building.Depth > layout.Depth)
                return "invalid-building";
            if (building.Floors < 1 || (building.Floors > 1 && building.Kind != FactoryKind)
                || (building.HasElevator && !SiteGrid.IsInterior(building, building.ElevatorX, building.ElevatorZ)))
                return "invalid-floors";
            if (building.Doors.Any(x => x == null || !SiteGrid.IsDoorCell(building, x.X, x.Z))
                || building.Doors.GroupBy(x => (x.X, x.Z)).Any(x => x.Count() != 1))
                return "invalid-door";
            if (state.Buildings.Any(x => x.Id != building.Id && x.SiteId == building.SiteId
                    && SiteGrid.Overlaps(building.CellX, building.CellZ, building.Width, building.Depth, x.CellX, x.CellZ, x.Width, x.Depth)))
                return "blocked";
            foreach (var equipment in state.Equipment.Where(x => x.SiteId == building.SiteId && x.State == EquipmentState.Placed))
            {
                var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
                if (SiteGrid.CoversWall(building, equipment.CellX, equipment.CellZ, width, depth)
                    || SiteGrid.CoversShaft(building, equipment.CellX, equipment.CellZ, width, depth, equipment.Level)) return "blocked";
            }
            if (state.Belts.Any(x => x.SiteId == building.SiteId
                    && (SiteGrid.CoversWall(building, x.CellX, x.CellZ, 1, 1) || SiteGrid.CoversShaft(building, x.CellX, x.CellZ, 1, 1, x.Level))))
                return "blocked";
            return null;
        }

        private static void ValidateBuildings(GoodsSnapshot state)
        {
            if (state.Buildings.Any(x => x == null || BuildingProblem(state, x) != null)
                || state.Buildings.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates building invariants.");
        }
    }
}
