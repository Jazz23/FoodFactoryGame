// Integer geometry shared by the generator and the validator. Boxes are in half-metre units (doubled coordinates), so a road
// of even width around an integer centreline and a cell's centre are both exact; nothing uses floating point.
using System;
using System.Collections.Generic;

namespace FoodFactoryGame.World
{
    public readonly struct Box
    {
        public readonly int X0, Z0, X1, Z1;

        public Box(int x0, int z0, int x1, int z1)
        {
            X0 = Math.Min(x0, x1);
            Z0 = Math.Min(z0, z1);
            X1 = Math.Max(x0, x1);
            Z1 = Math.Max(z0, z1);
        }

        // Interiors intersect; touching edges do not count.
        public bool Overlaps(Box other) => X0 < other.X1 && other.X0 < X1 && Z0 < other.Z1 && other.Z0 < Z1;

        public bool ContainsDoubled(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;
    }

    public static class WorldGeometry
    {
        public static Box Of(WorldRect rect) => new(2 * rect.X, 2 * rect.Z, 2 * (rect.X + rect.Width), 2 * (rect.Z + rect.Depth));

        public static Box Of(WorldBuilding building) => Of(building.Footprint);

        // The paved area of an axis-aligned centreline from a to b, including half the width past each end.
        public static Box Strip(int ax, int az, int bx, int bz, int width) =>
            new(2 * Math.Min(ax, bx) - width, 2 * Math.Min(az, bz) - width, 2 * Math.Max(ax, bx) + width, 2 * Math.Max(az, bz) + width);

        public static (int X, int Z) Step(Facing facing) => facing switch
        {
            Facing.North => (0, 1),
            Facing.East => (1, 0),
            Facing.South => (0, -1),
            _ => (-1, 0)
        };

        // Perimeter cells, never corners (decision 0019), on the building's street side.
        public static bool IsDoorOnFacing(WorldBuilding building, WorldCell door)
        {
            int along, alongMin, alongLength;
            bool onSide;
            switch (building.Facing)
            {
                case Facing.North: onSide = door.Z == building.Z + building.Depth - 1; along = door.X; alongMin = building.X; alongLength = building.Width; break;
                case Facing.South: onSide = door.Z == building.Z; along = door.X; alongMin = building.X; alongLength = building.Width; break;
                case Facing.East: onSide = door.X == building.X + building.Width - 1; along = door.Z; alongMin = building.Z; alongLength = building.Depth; break;
                default: onSide = door.X == building.X; along = door.Z; alongMin = building.Z; alongLength = building.Depth; break;
            }
            return onSide && along > alongMin && along < alongMin + alongLength - 1;
        }

        // Door cells centred on the street wall: count cells starting at the middle of a wall of the given length.
        public static List<WorldCell> Doors(WorldRect rect, Facing facing, int count)
        {
            var doors = new List<WorldCell>();
            var length = facing == Facing.North || facing == Facing.South ? rect.Width : rect.Depth;
            var first = (length - count) / 2;
            for (var index = 0; index < count; index++)
            {
                var offset = first + index;
                doors.Add(facing switch
                {
                    Facing.North => new WorldCell(rect.X + offset, rect.Z + rect.Depth - 1),
                    Facing.South => new WorldCell(rect.X + offset, rect.Z),
                    Facing.East => new WorldCell(rect.X + rect.Width - 1, rect.Z + offset),
                    _ => new WorldCell(rect.X, rect.Z + offset)
                });
            }
            return doors;
        }

        // A quarter turn clockwise seen from above about the origin: +Z (north) becomes +X (east).
        public static (int X, int Z) TurnPoint(int x, int z) => (z, -x);

        public static WorldCell TurnCell(WorldCell cell) => new(cell.Z, -cell.X - 1);

        public static WorldRect TurnRect(WorldRect rect) => new(rect.Z, -(rect.X + rect.Width), rect.Depth, rect.Width);

        public static Facing TurnFacing(Facing facing) => (Facing)(((int)facing + 1) % 4);
    }

    // Bucketed boxes for overlap and point queries. Query results come back in insertion order, never bucket order.
    internal sealed class BoxIndex<T>
    {
        private const int Bucket = 128;
        private readonly Dictionary<long, List<int>> _buckets = new();
        private readonly List<(Box Box, T Item)> _items = new();

        public void Add(Box box, T item)
        {
            var index = _items.Count;
            _items.Add((box, item));
            ForBuckets(box, key =>
            {
                if (!_buckets.TryGetValue(key, out var list)) _buckets[key] = list = new List<int>();
                list.Add(index);
            });
        }

        public List<T> Overlapping(Box box)
        {
            var hits = new SortedSet<int>();
            ForBuckets(box, key =>
            {
                if (!_buckets.TryGetValue(key, out var list)) return;
                foreach (var index in list)
                    if (_items[index].Box.Overlaps(box)) hits.Add(index);
            });
            var result = new List<T>();
            foreach (var index in hits) result.Add(_items[index].Item);
            return result;
        }

        public bool Any(Box box) => Overlapping(box).Count > 0;

        // First item (in insertion order) whose box contains the doubled point.
        public bool TryFind(int x, int z, out T item)
        {
            item = default;
            if (!_buckets.TryGetValue(Key(Floor(x), Floor(z)), out var list)) return false;
            var best = int.MaxValue;
            foreach (var index in list)
                if (index < best && _items[index].Box.ContainsDoubled(x, z)) best = index;
            if (best == int.MaxValue) return false;
            item = _items[best].Item;
            return true;
        }

        private static void ForBuckets(Box box, Action<long> action)
        {
            for (var bx = Floor(box.X0); bx <= Floor(box.X1); bx++)
            for (var bz = Floor(box.Z0); bz <= Floor(box.Z1); bz++)
                action(Key(bx, bz));
        }

        private static int Floor(int value) => value >= 0 ? value / Bucket : -((-value + Bucket - 1) / Bucket);

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
    }
}
