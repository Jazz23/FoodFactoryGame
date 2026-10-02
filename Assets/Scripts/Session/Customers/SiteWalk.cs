// Presentation only: routes customer figures over a drawn site's walkable cells (RestaurantRules.Walkable: walls, windows and
// placed object-layer pieces block; doors do not), so they come in through a door instead of crossing a wall. One instance per
// replicated snapshot of a site (a new baseline gets a new one). A route walks straight to the lot's nearest open edge cell when
// it starts outside the lot, follows a shortest four-way cell path, and ends with a short step onto the exact target (a seat or a
// queue spot) when that lies on a blocked cell. A register's service spot is the reachable open cell beside it, preferring the
// side it faces; its queue runs from there back along the way customers come in.
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session.Customers
{
    public sealed class SiteWalk
    {
        private static readonly ConditionalWeakTable<GoodsSnapshot, Dictionary<string, SiteWalk>> Cache = new();
        private static readonly (int X, int Z)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        private readonly SiteLayout _layout;
        private readonly bool[,] _open;
        // Cells reached from the lot's edges, and for each the next cell toward the nearest edge (an edge cell maps to itself).
        private readonly Dictionary<(int X, int Z), (int X, int Z)> _outward = new();
        private readonly Dictionary<string, List<(int X, int Z)>> _lines = new();

        private SiteWalk(GoodsSnapshot snapshot, SiteLayout layout)
        {
            _layout = layout;
            _open = RestaurantRules.Walkable(snapshot, layout.SiteId);
            var queue = new Queue<(int X, int Z)>();
            foreach (var cell in RestaurantRules.EdgeCells(layout).Where(Open))
                if (_outward.TryAdd(cell, cell)) queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var (dx, dz) in Steps)
                {
                    var next = (cell.X + dx, cell.Z + dz);
                    if (Open(next) && _outward.TryAdd(next, cell)) queue.Enqueue(next);
                }
            }
        }

        public static SiteWalk For(GoodsSnapshot snapshot, SiteLayout layout)
        {
            if (snapshot == null || layout == null) return null;
            var bySite = Cache.GetOrCreateValue(snapshot);
            if (!bySite.TryGetValue(layout.SiteId, out var walk)) bySite[layout.SiteId] = walk = new SiteWalk(snapshot, layout);
            return walk;
        }

        public bool Open((int X, int Z) cell) => _open != null && cell.X >= 0 && cell.Z >= 0 && cell.X < _layout.Width && cell.Z < _layout.Depth
            && _open[cell.X, cell.Z];

        private bool InLot((int X, int Z) cell) => cell.X >= 0 && cell.Z >= 0 && cell.X < _layout.Width && cell.Z < _layout.Depth;

        private Vector3 Center((int X, int Z) cell) => SiteGridSpace.FootprintCenter(_layout, cell.X, cell.Z, 1, 1);

        // Where a customer orders at a register and where the queue behind them stands (rank 0 first); null when no open cell
        // beside the register can be reached from the lot's edges.
        public Vector3? ServiceSpot(GoodsEquipment register, int rank = -1)
        {
            if (!_lines.TryGetValue(register.Id, out var line)) _lines[register.Id] = line = ServiceLine(register);
            if (line.Count == 0) return null;
            var index = rank + 1;
            if (index < line.Count) return Center(line[index]);
            // A queue longer than the way in bunches up at its end.
            var last = Center(line[line.Count - 1]);
            return last + new Vector3(((index - line.Count) % 3 - 1) * 0.3f, 0f, 0f);
        }

        private List<(int X, int Z)> ServiceLine(GoodsEquipment register)
        {
            var (width, depth) = SiteGrid.Footprint(register.Width, register.Depth, register.Rotation);
            var (fx, fz) = SiteGrid.Facing(register.Rotation);
            var beside = new List<(int X, int Z)>();
            for (var x = register.CellX; x < register.CellX + width; x++)
            {
                beside.Add((x, register.CellZ - 1));
                beside.Add((x, register.CellZ + depth));
            }
            for (var z = register.CellZ; z < register.CellZ + depth; z++)
            {
                beside.Add((register.CellX - 1, z));
                beside.Add((register.CellX + width, z));
            }
            var centerX = register.CellX + (width - 1) * 0.5f;
            var centerZ = register.CellZ + (depth - 1) * 0.5f;
            // In front first (the side the register faces), then nearest its middle.
            var spot = beside.Where(_outward.ContainsKey)
                .OrderByDescending(c => (c.X - centerX) * fx + (c.Z - centerZ) * fz)
                .ThenBy(c => Mathf.Abs(c.X - centerX) + Mathf.Abs(c.Z - centerZ)).Cast<(int X, int Z)?>().FirstOrDefault();
            var line = new List<(int X, int Z)>();
            for (var cell = spot; cell.HasValue && line.Count < 64; )
            {
                line.Add(cell.Value);
                var next = _outward[cell.Value];
                cell = next == cell.Value ? null : next;
            }
            return line;
        }

        // Scene points from one place to another over open cells, ending on the exact target; null when no cell path exists.
        public Vector3[] Route(Vector3 from, Vector3 to)
        {
            var fromCell = SiteGridSpace.AnchorAt(_layout, from, 1, 1);
            var toCell = SiteGridSpace.AnchorAt(_layout, to, 1, 1);
            var points = new List<Vector3>();
            (int X, int Z)? start;
            if (!InLot(fromCell))
            {
                // From the street: walk to the reachable edge cell nearest the figure.
                start = _outward.Where(x => x.Key == x.Value).Select(x => (x.Key.X, x.Key.Z))
                    .OrderBy(c => (Center(c) - from).sqrMagnitude).Cast<(int X, int Z)?>().FirstOrDefault();
            }
            else start = Open(fromCell) ? fromCell : Nearest(fromCell);
            if (start == null) return null;
            System.Func<(int X, int Z), bool> goal;
            if (!InLot(toCell)) goal = c => _outward.TryGetValue(c, out var next) && next == c;
            else if (Open(toCell)) goal = c => c == toCell;
            else goal = c => Mathf.Abs(c.X - toCell.X) + Mathf.Abs(c.Z - toCell.Z) == 1 || Mathf.Abs(c.X - toCell.X) + Mathf.Abs(c.Z - toCell.Z) == 2
                && Mathf.Abs(c.X - toCell.X) == 1;
            var cells = Search(start.Value, goal);
            if (cells == null) return null;
            // Straight runs collapse to their ends.
            for (var index = 0; index < cells.Count; index++)
            {
                if (index > 0 && index < cells.Count - 1)
                {
                    var (a, b, c) = (cells[index - 1], cells[index], cells[index + 1]);
                    if (b.X - a.X == c.X - b.X && b.Z - a.Z == c.Z - b.Z) continue;
                }
                points.Add(Center(cells[index]));
            }
            points.Add(to);
            return points.ToArray();
        }

        private (int X, int Z)? Nearest((int X, int Z) cell)
        {
            for (var radius = 1; radius <= 3; radius++)
                for (var dx = -radius; dx <= radius; dx++)
                for (var dz = -radius; dz <= radius; dz++)
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) == radius && Open((cell.X + dx, cell.Z + dz))) return (cell.X + dx, cell.Z + dz);
            return null;
        }

        private List<(int X, int Z)> Search((int X, int Z) start, System.Func<(int X, int Z), bool> goal)
        {
            var parent = new Dictionary<(int X, int Z), (int X, int Z)> { [start] = start };
            var queue = new Queue<(int X, int Z)>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (goal(cell))
                {
                    var path = new List<(int X, int Z)> { cell };
                    while (path[path.Count - 1] != start) path.Add(parent[path[path.Count - 1]]);
                    path.Reverse();
                    return path;
                }
                foreach (var (dx, dz) in Steps)
                {
                    var next = (cell.X + dx, cell.Z + dz);
                    if (Open(next) && parent.TryAdd(next, cell)) queue.Enqueue(next);
                }
            }
            return null;
        }
    }
}
