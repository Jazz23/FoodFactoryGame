// Pure grid rules shared by the server's placement check and the client's placement preview; the server always re-checks.
// Placed equipment, belts and building walls share the grid: nothing may overlap anything else on the same level. Level 0 is
// the ground; a higher level exists only over the interior of a building with that many floors (decision 0020), and a
// building's elevator cell is kept clear on every one of its floors. A conveyor lift occupies its cell on both of its levels.
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public static class SiteGrid
    {
        public const float CellSize = 1f;

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
            return CellProblem(state, equipment.SiteId, cellX, cellZ, width, depth, equipment.Id, level);
        }

        // Null when a footprint lies inside the site's layout on an existing floor and overlaps no placed equipment or belt on
        // that level, building wall or elevator shaft other than ignoreId; otherwise "out-of-bounds", "no-floor" or "blocked".
        public static string CellProblem(GoodsSnapshot state, string siteId, int cellX, int cellZ, int width, int depth, string ignoreId, int level = 0)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null || level < 0 || cellX < 0 || cellZ < 0 || cellX + width > layout.Width || cellZ + depth > layout.Depth)
                return "out-of-bounds";
            if (level > 0 && state.Buildings?.Any(x => x.SiteId == siteId && x.Floors > level && InsideInterior(x, cellX, cellZ, width, depth)) != true)
                return "no-floor";
            foreach (var other in state.Equipment.Where(x => x.Id != ignoreId && x.SiteId == siteId && x.State == EquipmentState.Placed && x.Level == level))
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

        public static bool OnPerimeter(GoodsBuilding building, int cellX, int cellZ) =>
            Overlaps(cellX, cellZ, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth)
            && (cellX == building.CellX || cellX == building.CellX + building.Width - 1
                || cellZ == building.CellZ || cellZ == building.CellZ + building.Depth - 1);

        // A door may be any perimeter cell except a corner, so every wall run stays attached at the corners.
        public static bool IsDoorCell(GoodsBuilding building, int cellX, int cellZ) =>
            OnPerimeter(building, cellX, cellZ)
            && !((cellX == building.CellX || cellX == building.CellX + building.Width - 1)
                && (cellZ == building.CellZ || cellZ == building.CellZ + building.Depth - 1));

        // Ground floor: doors are openings. Upper floors have no doors, so their whole perimeter is wall.
        public static bool IsWall(GoodsBuilding building, int cellX, int cellZ, int level = 0) =>
            OnPerimeter(building, cellX, cellZ) && (level > 0 || !building.Doors.Any(x => x.X == cellX && x.Z == cellZ));

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
