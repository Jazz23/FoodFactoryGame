// A building shell is a server-owned rectangle on a site grid whose perimeter cells are walls, except its ground-floor door
// cells (decision 0019). Walls block equipment and belts through SiteGrid.CellProblem; the interior and doors are ordinary
// cells. Shells are created only by the server and are never moved or removed. A factory can gain floors (decision 0020):
// each upper floor covers the interior at a higher level, walls surround every floor, and one elevator cell, fixed by the
// first added floor, runs through all of them and never holds equipment or belts. Floors are never removed.
// A restaurant's owner may reshape its shell (decision 0034, GoodsWorld.Shell.cs): every piece of structure an order built is a
// GoodsStructure carrying the cents it was charged, so removing it refunds exactly that; structure that came with the bought
// building has no record and refunds nothing. A door record may make its door a back door (decision 0037), which customers never use.
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

    // One piece of a restaurant's structure (decision 0034, schema v16). Interior walls, interior doors and windows exist only as
    // these records. Floor and wall records only remember what an order paid for a footprint cell or a perimeter wall cell, and
    // a perimeter door's record what it cost (the door itself is in GoodsBuilding.Doors); a cell without a record was not paid for.
    [Serializable] public sealed class GoodsStructure
    {
        // GoodsWorld.FloorStructure, WallStructure, PartitionStructure, DoorStructure or WindowStructure.
        public string Kind;
        public int X;
        public int Z;
        // Windows only: a window covers (X, Z) and the next cell along X (0) or Z (1).
        public int Axis;
        // Presentation choice from GoodsWorld's style lists (wall finish of a partition, door leaf, window type); empty otherwise.
        public string Style = "";
        // Whole cents charged when it was built, including any order fee assigned to it; refunded in full when it goes.
        public long ChargedCents;
        // Doors only (decision 0037, schema v18): GoodsWorld.ServiceDoorRole for a back door (staff and goods only), empty for a
        // customer door. A perimeter door without a record is a customer door.
        public string Role = "";
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
        // Restaurants only (decision 0034, v16): the structure orders built, and the perimeter walls' finish (empty is plaster).
        public List<GoodsStructure> Structures = new();
        public string WallStyle = "";
        // Restaurants only (decision 0036, v17): every wall is a partition record the owner placed or removes, Doors is empty, the
        // footprint is the walls' bounding box and the interior is what they enclose. A restaurant gets free walls on its first
        // shell order (GoodsWorld.ToFreeWalls); until then its perimeter is implied by the footprint.
        public bool FreeWalls;

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
        // A door's role (decision 0037): customers enter and leave only by customer doors (empty role); players and employees use
        // either.
        public const string ServiceDoorRole = "service";
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
                || (!building.FreeWalls && (building.Width < MinimumBuildingSize || building.Depth < MinimumBuildingSize))
                || (building.FreeWalls && (building.Kind != RestaurantKind || building.Doors.Count > 0 || !FitsWalls(building)))
                || building.CellX < 0 || building.CellZ < 0
                || building.CellX + building.Width > layout.Width || building.CellZ + building.Depth > layout.Depth)
                return "invalid-building";
            if (building.Floors < 1 || (building.Floors > 1 && building.Kind != FactoryKind)
                || (building.HasElevator && !SiteGrid.IsInterior(building, building.ElevatorX, building.ElevatorZ)))
                return "invalid-floors";
            if (building.Doors.Any(x => x == null || !SiteGrid.IsDoorCell(building, x.X, x.Z))
                || building.Doors.GroupBy(x => (x.X, x.Z)).Any(x => x.Count() != 1))
                return "invalid-door";
            var structureProblem = StructureProblem(building);
            if (structureProblem != null) return structureProblem;
            if (state.Buildings.Any(x => x.Id != building.Id && x.SiteId == building.SiteId
                    && SiteGrid.Overlaps(building.CellX, building.CellZ, building.Width, building.Depth, x.CellX, x.CellZ, x.Width, x.Depth)))
                return "blocked";
            // Decor layers have their own rules (SiteGrid.CellProblem); only the object layer may never stand on a wall.
            foreach (var equipment in state.Equipment.Where(x => x.SiteId == building.SiteId && x.State == EquipmentState.Placed && string.IsNullOrEmpty(x.Layer)))
            {
                var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
                if (SiteGrid.CoversWall(building, equipment.CellX, equipment.CellZ, width, depth)
                    || SiteGrid.CoversShaft(building, equipment.CellX, equipment.CellZ, width, depth, equipment.Level)) return "blocked";
            }
            if (state.Belts.Any(x => x.SiteId == building.SiteId
                    && (SiteGrid.CoversWall(building, x.CellX, x.CellZ, 1, 1) || SiteGrid.CoversShaft(building, x.CellX, x.CellZ, 1, 1, x.Level)
                        || SiteGrid.CoversShaft(building, x.CellX, x.CellZ, 1, 1, x.ExitLevel))))
                return "blocked";
            return null;
        }

        // Null when the structure records are well formed (decision 0034): only restaurants have any; floor records inside the
        // footprint, wall records on the perimeter, partitions strictly inside (free walls, decision 0036: partitions anywhere in the
        // footprint and no floor or wall records); a door record on a perimeter door or a partition;
        // a window on two wall cells of one perimeter side or of partitions, never on a door or another window; known styles.
        private static string StructureProblem(GoodsBuilding building)
        {
            if (building.Structures == null || building.WallStyle == null) return "invalid-structure";
            if (building.Structures.Count == 0 && building.WallStyle == "") return null;
            if (building.Kind != RestaurantKind || (building.WallStyle != "" && !WallStyles.Contains(building.WallStyle))) return "invalid-structure";
            foreach (var piece in building.Structures)
            {
                if (piece == null || piece.ChargedCents < 0 || piece.Style == null || piece.Role == null) return "invalid-structure";
                // A back door is a door on an outer wall: it has a doorstep outside (decision 0037).
                if (piece.Role != "" && (piece.Role != ServiceDoorRole || piece.Kind != DoorStructure || SiteGrid.Doorstep(building, piece.X, piece.Z) == null))
                    return "invalid-structure";
                var valid = piece.Kind switch
                {
                    FloorStructure => !building.FreeWalls
                        && SiteGrid.Overlaps(piece.X, piece.Z, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth),
                    WallStructure => SiteGrid.OnPerimeter(building, piece.X, piece.Z),
                    // Free walls may stand anywhere in the footprint (it is their bounding box); otherwise strictly inside.
                    PartitionStructure => (building.FreeWalls || SiteGrid.IsInterior(building, piece.X, piece.Z)) && WallStyles.Contains(piece.Style),
                    DoorStructure => DoorStyles.Contains(piece.Style) && (building.Doors.Any(x => x.X == piece.X && x.Z == piece.Z)
                        || SiteGrid.IsPartition(building, piece.X, piece.Z)),
                    WindowStructure => WindowStyles.Contains(piece.Style) && (piece.Axis == 0 || piece.Axis == 1) && WindowFits(building, piece),
                    _ => false
                };
                if (!valid) return "invalid-structure";
            }
            if (building.Structures.Where(x => x.Kind != WindowStructure).GroupBy(x => (x.Kind, x.X, x.Z)).Any(x => x.Count() != 1))
                return "invalid-structure";
            var windows = building.Structures.Where(x => x.Kind == WindowStructure).ToList();
            foreach (var window in windows)
                if (windows.Any(other => other != window && (SiteGrid.WindowCovers(other, window.X, window.Z) || SiteGrid.WindowCovers(window, other.X, other.Z)))
                    || building.Structures.Any(x => x.Kind == DoorStructure && SiteGrid.WindowCovers(window, x.X, x.Z)))
                    return "invalid-structure";
            return null;
        }

        // A free-walled shell's footprint is exactly the bounding box of its walls, and it has at least one wall.
        private static bool FitsWalls(GoodsBuilding building)
        {
            var walls = building.Structures?.Where(x => x != null && x.Kind == PartitionStructure).ToList();
            if (walls == null || walls.Count == 0) return false;
            return building.CellX == walls.Min(x => x.X) && building.CellZ == walls.Min(x => x.Z)
                && building.Width == walls.Max(x => x.X) - building.CellX + 1 && building.Depth == walls.Max(x => x.Z) - building.CellZ + 1;
        }

        // Both cells of a window are walls of one perimeter side (non-corner, not doors) or both are interior walls without a door.
        internal static bool WindowFits(GoodsBuilding building, GoodsStructure window)
        {
            var (x2, z2) = window.Axis == 0 ? (window.X + 1, window.Z) : (window.X, window.Z + 1);
            bool PerimeterWall(int x, int z) => SiteGrid.IsDoorCell(building, x, z) && !building.Doors.Any(d => d.X == x && d.Z == z);
            var sameSide = window.Axis == 0
                ? window.Z == building.CellZ || window.Z == building.CellZ + building.Depth - 1
                : window.X == building.CellX || window.X == building.CellX + building.Width - 1;
            if (sameSide && PerimeterWall(window.X, window.Z) && PerimeterWall(x2, z2)) return true;
            return SiteGrid.IsWall(building, window.X, window.Z) && SiteGrid.IsWall(building, x2, z2)
                && SiteGrid.IsPartition(building, window.X, window.Z) && SiteGrid.IsPartition(building, x2, z2);
        }

        private static void ValidateBuildings(GoodsSnapshot state)
        {
            if (state.Buildings.Any(x => x == null || BuildingProblem(state, x) != null)
                || state.Buildings.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates building invariants.");
        }
    }
}
