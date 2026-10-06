// Pure grid rules shared by the server's placement check and the client's placement preview; the server always re-checks.
// Placed equipment, belts and building walls share the grid: nothing may overlap anything else on the same level. Level 0 is
// the ground; a higher level exists only over the interior of a building with that many floors (decision 0020), and a
// building's elevator cell is kept clear on every one of its floors. A conveyor lift occupies its cell on both of its levels.
// Walls are the perimeter except its doors, plus a restaurant's interior walls (partitions) except those with a door; windows
// sit on wall cells and stay walls (decision 0034). Equipment has an occupancy layer (decision 0034): the object layer (machines,
// tables, registers, docks and most decor) follows the rules above unchanged; floor finishes and rugs lie under it, wall decor
// hangs on wall cells, ceiling decor needs a building's interior, and tabletop props stand on a table. Each of those layers
// overlaps only its own kind, on the ground level.
// A restaurant with free walls (FreeWalls, decision 0036) has no implied perimeter: every wall is a wall record the owner placed,
// its footprint is the walls' bounding box, and its interior is what those walls enclose (cells no four-way walk from outside the
// box reaches without crossing a wall, door or window cell).
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

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

        // The grid direction a piece's front faces: rotation 0 faces +Z, each quarter turn a quarter clockwise seen from above.
        public static (int X, int Z) Facing(int rotation) => (((rotation % 4) + 4) % 4) switch
        {
            0 => (0, 1),
            1 => (1, 0),
            2 => (0, -1),
            _ => (-1, 0)
        };

        // The cells just behind a footprint (width and depth as placed), opposite the way it faces.
        public static IEnumerable<(int X, int Z)> BehindCells(int cellX, int cellZ, int width, int depth, int rotation)
        {
            var (fx, fz) = Facing(rotation);
            if (fz != 0)
                for (var x = cellX; x < cellX + width; x++)
                    yield return (x, fz > 0 ? cellZ - 1 : cellZ + depth);
            else
                for (var z = cellZ; z < cellZ + depth; z++)
                    yield return (fx > 0 ? cellX - 1 : cellX + width, z);
        }

        // True when a wall of any of the site's buildings stands on the cell (ground level).
        public static bool WallAt(GoodsSnapshot state, string siteId, int cellX, int cellZ) =>
            state.Buildings?.Any(x => x.SiteId == siteId && IsWall(x, cellX, cellZ)) == true;

        public static bool OnPerimeter(GoodsBuilding building, int cellX, int cellZ) =>
            !building.FreeWalls && Overlaps(cellX, cellZ, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth)
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

        // A back door (decision 0037): a door whose record has the service role. Customers treat it as a wall.
        public static bool IsServiceDoor(GoodsBuilding building, int cellX, int cellZ) =>
            IsDoor(building, cellX, cellZ) && building.Structures?.Any(x => x != null && x.Kind == GoodsWorld.DoorStructure && x.X == cellX && x.Z == cellZ
                && x.Role == GoodsWorld.ServiceDoorRole) == true;

        // Every back door of a building.
        public static IEnumerable<(int X, int Z)> ServiceDoors(GoodsBuilding building) =>
            (building.Structures ?? new List<GoodsStructure>()).Where(x => x != null && x.Kind == GoodsWorld.DoorStructure && x.Role == GoodsWorld.ServiceDoorRole
                && IsDoor(building, x.X, x.Z)).Select(x => (x.X, x.Z));

        // The doorstep of a door on an outer wall: the one neighbour that is neither wall line nor interior (null for a door between
        // two rooms, or a cell that opens nowhere). Footprint perimeter cells and free walls count as wall line, door cells included.
        public static (int X, int Z)? Doorstep(GoodsBuilding building, int cellX, int cellZ)
        {
            (int X, int Z)? found = null;
            foreach (var (x, z) in new[] { (cellX + 1, cellZ), (cellX - 1, cellZ), (cellX, cellZ + 1), (cellX, cellZ - 1) })
            {
                if (OnPerimeter(building, x, z) || IsPartition(building, x, z) || IsInterior(building, x, z)) continue;
                if (found != null) return null;
                found = (x, z);
            }
            return found;
        }

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
        public static bool IsInterior(GoodsBuilding building, int cellX, int cellZ) => building.FreeWalls
            ? Enclosed(building).Contains((cellX, cellZ))
            : cellX > building.CellX && cellX < building.CellX + building.Width - 1
                && cellZ > building.CellZ && cellZ < building.CellZ + building.Depth - 1;

        public static bool InsideInterior(GoodsBuilding building, int cellX, int cellZ, int width, int depth)
        {
            if (!building.FreeWalls) return IsInterior(building, cellX, cellZ) && IsInterior(building, cellX + width - 1, cellZ + depth - 1);
            for (var x = cellX; x < cellX + width; x++)
            for (var z = cellZ; z < cellZ + depth; z++)
                if (!IsInterior(building, x, z)) return false;
            return true;
        }

        // Every interior cell of a building (the rectangle inside its perimeter, or what free walls enclose).
        public static IEnumerable<(int X, int Z)> InteriorCells(GoodsBuilding building)
        {
            if (building.FreeWalls) return Enclosed(building);
            return Enumerable.Range(building.CellX + 1, System.Math.Max(0, building.Width - 2))
                .SelectMany(x => Enumerable.Range(building.CellZ + 1, System.Math.Max(0, building.Depth - 2)).Select(z => (x, z)));
        }

        // Free walls' enclosed cells, cached per building object and recomputed when its walls or footprint change.
        private sealed class Enclosure
        {
            public int Key;
            public HashSet<(int X, int Z)> Cells;
        }

        private static readonly ConditionalWeakTable<GoodsBuilding, Enclosure> Enclosures = new();

        private static HashSet<(int X, int Z)> Enclosed(GoodsBuilding building)
        {
            var key = EnclosureKey(building);
            lock (Enclosures)
                if (Enclosures.TryGetValue(building, out var cached) && cached.Key == key) return cached.Cells;
            var barriers = new HashSet<(int X, int Z)>(building.Structures?.Where(x => x != null && x.Kind == GoodsWorld.PartitionStructure)
                .Select(x => (x.X, x.Z)) ?? Enumerable.Empty<(int, int)>());
            // Walk from a ring one cell outside the footprint; whatever stays unreached (and is no wall) is inside.
            int minX = building.CellX - 1, minZ = building.CellZ - 1, maxX = building.CellX + building.Width, maxZ = building.CellZ + building.Depth;
            var outside = new HashSet<(int X, int Z)>();
            var queue = new Queue<(int X, int Z)>();
            for (var x = minX; x <= maxX; x++)
            {
                if (outside.Add((x, minZ))) queue.Enqueue((x, minZ));
                if (outside.Add((x, maxZ))) queue.Enqueue((x, maxZ));
            }
            for (var z = minZ; z <= maxZ; z++)
            {
                if (outside.Add((minX, z))) queue.Enqueue((minX, z));
                if (outside.Add((maxX, z))) queue.Enqueue((maxX, z));
            }
            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                foreach (var next in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (next.Item1 >= minX && next.Item1 <= maxX && next.Item2 >= minZ && next.Item2 <= maxZ && !barriers.Contains(next) && outside.Add(next))
                        queue.Enqueue(next);
            }
            var cells = new HashSet<(int X, int Z)>();
            for (var x = building.CellX; x < building.CellX + building.Width; x++)
            for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
                if (!outside.Contains((x, z)) && !barriers.Contains((x, z))) cells.Add((x, z));
            lock (Enclosures)
            {
                Enclosures.Remove(building);
                Enclosures.Add(building, new Enclosure { Key = key, Cells = cells });
            }
            return cells;
        }

        private static int EnclosureKey(GoodsBuilding building)
        {
            unchecked
            {
                var hash = ((building.CellX * 397 ^ building.CellZ) * 397 ^ building.Width) * 397 ^ building.Depth;
                foreach (var piece in building.Structures ?? new List<GoodsStructure>())
                    if (piece != null && piece.Kind == GoodsWorld.PartitionStructure) hash = hash * 31 + (piece.X * 7919 ^ piece.Z);
                return hash;
            }
        }

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
