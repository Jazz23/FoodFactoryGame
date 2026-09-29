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

        public static long FloorDiv(long value, long divisor) => value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

        // Smallest n with n * n >= value.
        public static long CeilSqrt(long value)
        {
            if (value <= 0) return 0;
            var root = (long)Math.Sqrt(value);
            while (root * root > value) root--;
            while (root * root < value) root++;
            return root;
        }

        // Whether point p lies within `radius` of segment a-b (inclusive), exactly, in whatever unit the arguments share.
        public static bool WithinDistance(long px, long pz, long ax, long az, long bx, long bz, long radius)
        {
            var dx = bx - ax;
            var dz = bz - az;
            var qx = px - ax;
            var qz = pz - az;
            var along = qx * dx + qz * dz;
            var lengthSquared = dx * dx + dz * dz;
            if (along <= 0 || lengthSquared == 0) return qx * qx + qz * qz <= radius * radius;
            if (along >= lengthSquared) return (px - bx) * (px - bx) + (pz - bz) * (pz - bz) <= radius * radius;
            var cross = dx * qz - dz * qx;
            return cross * cross <= radius * radius * lengthSquared;
        }

        public static bool WithinDistance(IReadOnlyList<WorldCell> line, long px, long pz, long radius)
        {
            for (var index = 0; index + 1 < line.Count; index++)
                if (WithinDistance(px, pz, line[index].X, line[index].Z, line[index + 1].X, line[index + 1].Z, radius)) return true;
            return false;
        }

        // Where a polyline crosses the axis line (vertical: x = fixedAt, else z = fixedAt) strictly between from and to, as the
        // along coordinate rounded down, with the direction of the polyline piece that crosses. Pieces lying on the line are skipped.
        public static List<(int Along, int Dx, int Dz)> Crossings(IReadOnlyList<WorldCell> line, bool vertical, int fixedAt, int from, int to)
        {
            var result = new List<(int, int, int)>();
            for (var index = 0; index + 1 < line.Count; index++)
            {
                var a = line[index];
                var b = line[index + 1];
                long a0 = vertical ? a.X : a.Z, b0 = vertical ? b.X : b.Z;
                long a1 = vertical ? a.Z : a.X, b1 = vertical ? b.Z : b.X;
                if (a0 == b0) continue;
                // Half-open on the far end so a crossing exactly at a shared point counts once.
                var low = Math.Min(a0, b0);
                var high = Math.Max(a0, b0);
                if (fixedAt < low || fixedAt >= high) continue;
                var along = a1 + FloorDiv((b1 - a1) * (fixedAt - a0), b0 - a0);
                if (along <= from || along >= to) continue;
                result.Add(((int)along, b.X - a.X, b.Z - a.Z));
            }
            return result;
        }

        // Boxes (doubled units) covering a polyline widened by `radius` metres each side, one per piece; conservative.
        public static IEnumerable<Box> Corridor(IReadOnlyList<WorldCell> line, int radius)
        {
            for (var index = 0; index + 1 < line.Count; index++)
            {
                var a = line[index];
                var b = line[index + 1];
                yield return new Box(2 * (Math.Min(a.X, b.X) - radius), 2 * (Math.Min(a.Z, b.Z) - radius),
                    2 * (Math.Max(a.X, b.X) + radius), 2 * (Math.Max(a.Z, b.Z) + radius));
            }
        }
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
