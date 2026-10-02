// Pure grid rules shared by the server's placement check and the client's placement preview; the server always re-checks.
// Placed equipment, belts and building walls share the grid: nothing may overlap anything else on the same level. Level 0 is
// the ground; a higher level exists only over the interior of a building with that many floors (decision 0020), and a
// building's elevator cell is kept clear on every one of its floors. A conveyor lift occupies its cell on both of its levels.
// Walls are the perimeter except its doors, plus a restaurant's interior walls (partitions) except those with a door; windows
// sit on wall cells and stay walls (decision 0034). Equipment has an occupancy layer (decision 0034): the object layer (machines,
// tables, registers, docks and most decor) follows the rules above unchanged; floor finishes and rugs lie under it, wall decor
// hangs on wall cells, ceiling decor needs a building's interior, and tabletop props stand on a table. Each of those layers
// overlaps only its own kind, on the ground level.
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public static class SiteGrid
    {
        public const float CellSize = 1f;

        // Equipment occupancy layers (GoodsEquipment.Layer). Empty is the object layer every machine uses.
        public const string ObjectLayer = "";
        public const string FloorLayer = "floor";
        public const string WallLayer = "wall";
        public const string CeilingLayer = "ceiling";
        public const string TabletopLayer = "tabletop";

        public static bool IsLayer(string layer) => string.IsNullOrEmpty(layer) || layer == FloorLayer || layer == WallLayer
            || layer == CeilingLayer || layer == TabletopLayer;

        // Odd quarter turns swap the footprint's width and depth.
        public static (int Width, int Depth) Footprint(int width, int depth, int rotation) =>
            rotation % 2 == 0 ? (width, depth) : (depth, width);

        public static bool Overlaps(int ax, int az, int aw, int ad, int bx, int bz, int bw, int bd) =>
            ax < bx + bw && bx < ax + aw && az < bz + bd && bz < az + ad;

        // Returns null when the equipment can occupy the anchor cell; otherwise the rejection reason the server records.
        // The equipment itself is ignored, so a held piece never blocks its own placement.
        public static string PlacementProblem(GoodsSnapshot state, GoodsEquipment equipment, int cellX, int cellZ, int rotation, int level = 0)
        {
            if (rotation < 0 || rotation > 3) return "invalid-rotation";
            var (width, depth) = Footprint(equipment.Width, equipment.Depth, rotation);
            return CellProblem(state, equipment.SiteId, cellX, cellZ, width, depth, equipment.Id, level, equipment.Layer);
        }

        // Null when a footprint lies inside the site's layout on an existing floor and overlaps no placed equipment or belt on
        // that level, building wall or elevator shaft other than ignoreId; otherwise "out-of-bounds", "no-floor" or "blocked".
        // Other layers (decision 0034) answer LayerProblem's reasons instead.
        public static string CellProblem(GoodsSnapshot state, string siteId, int cellX, int cellZ, int width, int depth, string ignoreId, int level = 0,
            string layer = ObjectLayer)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null || level < 0 || cellX < 0 || cellZ < 0 || cellX + width > layout.Width || cellZ + depth > layout.Depth)
                return "out-of-bounds";
            if (!string.IsNullOrEmpty(layer)) return LayerProblem(state, siteId, cellX, cellZ, width, depth, ignoreId, level, layer);
            if (level > 0 && state.Buildings?.Any(x => x.SiteId == siteId && x.Floors > level && InsideInterior(x, cellX, cellZ, width, depth)) != true)
                return "no-floor";
            foreach (var other in state.Equipment.Where(x => x.Id != ignoreId && x.SiteId == siteId && x.State == EquipmentState.Placed && x.Level == level
                         && string.IsNullOrEmpty(x.Layer)))
            {
                var (otherWidth, otherDepth) = Footprint(other.Width, other.Depth, other.Rotation);
                if (Overlaps(cellX, cellZ, width, depth, other.CellX, other.CellZ, otherWidth, otherDepth)) return "blocked";
            }
            // A lift stands on both of its levels.
            if (state.Belts != null && state.Belts.Any(x => x.Id != ignoreId && x.SiteId == siteId && (x.Level == level || x.ExitLevel == level)
                    && Overlaps(cellX, cellZ, width, depth, x.CellX, x.CellZ, 1, 1)))
                return "blocked";
            if (state.Buildings != null && state.Buildings.Any(x => x.SiteId == siteId
                    && (CoversWall(x, cellX, cellZ, width, depth) || CoversShaft(x, cellX, cellZ, width, depth, level))))
                return "blocked";
            return null;
        }

        // Decor layers (decision 0034), ground level only: a floor piece may not lie on a wall; a wall piece must hang on wall cells
        // (not doors or windows); a ceiling piece needs a building's interior that is not a wall; a tabletop prop must stand wholly
        // on one placed table. Each overlaps only pieces of its own layer.
        private static string LayerProblem(GoodsSnapshot state, string siteId, int cellX, int cellZ, int width, int depth, string ignoreId, int level,
            string layer)
        {
            if (!IsLayer(layer)) return "invalid-layer";
            if (level != 0) return "no-floor";
            var buildings = state.Buildings?.Where(x => x.SiteId == siteId).ToList() ?? new System.Collections.Generic.List<GoodsBuilding>();
            for (var x = cellX; x < cellX + width; x++)
            for (var z = cellZ; z < cellZ + depth; z++)
            {
                var wall = buildings.Any(b => IsWall(b, x, z));
                switch (layer)
                {
                    case FloorLayer when wall:
                        return "blocked";
                    case WallLayer when !wall || buildings.Any(b => WindowAt(b, x, z) != null):
                        return "no-wall";
                    case CeilingLayer when wall || !buildings.Any(b => IsInterior(b, x, z)):
                        return "no-ceiling";
                }
            }
            if (layer == TabletopLayer && !state.Equipment.Any(t => t.SiteId == siteId && t.State == EquipmentState.Placed && GoodsWorld.IsTable(t)
                    && string.IsNullOrEmpty(t.Layer) && t.Level == 0 && Contains(t, cellX, cellZ, width, depth)))
                return "no-table";
            foreach (var other in state.Equipment.Where(x => x.Id != ignoreId && x.SiteId == siteId && x.State == EquipmentState.Placed && x.Layer == layer))
            {
                var (otherWidth, otherDepth) = Footprint(other.Width, other.Depth, other.Rotation);
                if (Overlaps(cellX, cellZ, width, depth, other.CellX, other.CellZ, otherWidth, otherDepth)) return "blocked";
            }
            return null;
        }

        // True when the placed piece's footprint wholly contains the given footprint.
        public static bool Contains(GoodsEquipment piece, int cellX, int cellZ, int width, int depth)
        {
            var (pieceWidth, pieceDepth) = Footprint(piece.Width, piece.Depth, piece.Rotation);
            return cellX >= piece.CellX && cellZ >= piece.CellZ && cellX + width <= piece.CellX + pieceWidth && cellZ + depth <= piece.CellZ + pieceDepth;
        }

        public static bool OnPerimeter(GoodsBuilding building, int cellX, int cellZ) =>
            Overlaps(cellX, cellZ, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth)
            && (cellX == building.CellX || cellX == building.CellX + building.Width - 1
                || cellZ == building.CellZ || cellZ == building.CellZ + building.Depth - 1);

        // A door may be any perimeter cell except a corner, so every wall run stays attached at the corners.
        public static bool IsDoorCell(GoodsBuilding building, int cellX, int cellZ) =>
            OnPerimeter(building, cellX, cellZ)
            && !((cellX == building.CellX || cellX == building.CellX + building.Width - 1)
                && (cellZ == building.CellZ || cellZ == building.CellZ + building.Depth - 1));

        // Ground floor: doors are openings. Upper floors have no doors, so their whole perimeter is wall. Interior walls
        // (partitions, decision 0034) stand on the ground floor; one with a door is an opening.
        public static bool IsWall(GoodsBuilding building, int cellX, int cellZ, int level = 0) =>
            (OnPerimeter(building, cellX, cellZ) && (level > 0 || !building.Doors.Any(x => x.X == cellX && x.Z == cellZ)))
            || (level == 0 && IsPartition(building, cellX, cellZ) && !HasStructure(building, GoodsWorld.DoorStructure, cellX, cellZ));

        // A walkable opening: a perimeter door or a door in an interior wall.
        public static bool IsDoor(GoodsBuilding building, int cellX, int cellZ) =>
            (OnPerimeter(building, cellX, cellZ) && building.Doors.Any(x => x.X == cellX && x.Z == cellZ))
            || (IsPartition(building, cellX, cellZ) && HasStructure(building, GoodsWorld.DoorStructure, cellX, cellZ));

        public static bool IsPartition(GoodsBuilding building, int cellX, int cellZ) =>
            HasStructure(building, GoodsWorld.PartitionStructure, cellX, cellZ);

        public static bool HasStructure(GoodsBuilding building, string kind, int cellX, int cellZ) =>
            building.Structures?.Any(x => x != null && x.Kind == kind && x.X == cellX && x.Z == cellZ) == true;

        // The window covering a cell (a window spans its cell and the next one along its axis), or null.
        public static GoodsStructure WindowAt(GoodsBuilding building, int cellX, int cellZ) =>
            building.Structures?.FirstOrDefault(x => x != null && x.Kind == GoodsWorld.WindowStructure && WindowCovers(x, cellX, cellZ));

        public static bool WindowCovers(GoodsStructure window, int cellX, int cellZ) =>
            (window.X == cellX && window.Z == cellZ)
            || (window.Axis == 0 ? window.X + 1 == cellX && window.Z == cellZ : window.X == cellX && window.Z + 1 == cellZ);

        // Strictly inside the walls; door cells are not interior.
        public static bool IsInterior(GoodsBuilding building, int cellX, int cellZ) =>
            cellX > building.CellX && cellX < building.CellX + building.Width - 1
            && cellZ > building.CellZ && cellZ < building.CellZ + building.Depth - 1;

        public static bool InsideInterior(GoodsBuilding building, int cellX, int cellZ, int width, int depth) =>
            IsInterior(building, cellX, cellZ) && IsInterior(building, cellX + width - 1, cellZ + depth - 1);

        public static bool IsShaft(GoodsBuilding building, int cellX, int cellZ) =>
            building.HasElevator && building.ElevatorX == cellX && building.ElevatorZ == cellZ;

        // True when the footprint on that level covers the building's elevator cell on one of its floors.
        public static bool CoversShaft(GoodsBuilding building, int cellX, int cellZ, int width, int depth, int level) =>
            building.HasElevator && level < building.Floors
            && Overlaps(cellX, cellZ, width, depth, building.ElevatorX, building.ElevatorZ, 1, 1);

        public static bool CoversWall(GoodsBuilding building, int cellX, int cellZ, int width, int depth)
        {
            if (!Overlaps(cellX, cellZ, width, depth, building.CellX, building.CellZ, building.Width, building.Depth)) return false;
            for (var x = cellX; x < cellX + width; x++)
            for (var z = cellZ; z < cellZ + depth; z++)
                if (IsWall(building, x, z)) return true;
            return false;
        }
    }
}
