// Pure conveyor rules shared by the server simulation and client previews: which belt a belt feeds, the shape a belt takes
// from its feeders (Factorio-style: a belt fed only from one side curves), where an item enters the next belt, and where a
// new item fits. A belt is one grid cell; its items travel one lane, measured in integer units along its path. A conveyor
// lift (decision 0021) is a straight belt whose front end is one storey up or down: it takes items on its own level and
// hands them to the belt in front on its exit level, so the belt grid is keyed by cell and level, a lift at both of its ends.
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public enum BeltShape
    {
        Straight,
        // Fed only from its left or right side: the path enters on that side edge and turns to the front edge.
        CurveFromLeft,
        CurveFromRight
    }

    // Where a belt hands its items on: the next belt and the path position they enter at (0, or the midpoint when side-loading).
    public readonly struct BeltLink
    {
        public readonly GoodsBelt Next;
        public readonly int Entry;

        public BeltLink(GoodsBelt next, int entry)
        {
            Next = next;
            Entry = entry;
        }
    }

    public static class BeltRules
    {
        // Path length of one belt, whatever its shape. Items keep at least ItemSpacing units apart (four per belt).
        public const int UnitsPerTile = 240;
        public const int ItemSpacing = 60;
        // One tile per second: the texture scroll (0.5 UV per tile) is matched to this on the client.
        public const int UnitsPerSecond = 240;
        // The clock steps whole seconds; items move in smaller sub-steps so a compressed line stays compressed.
        public const int SubStepsPerSecond = 8;
        // An item at the end of a belt that feeds nothing rests here, so it does not overhang the belt.
        public const int EndRest = UnitsPerTile - ItemSpacing / 2;
        public const int Middle = UnitsPerTile / 2;

        // Direction 0..3 is the same quarter turn as equipment rotation: 0 = +Z, 1 = +X, 2 = -Z, 3 = -X.
        public static (int X, int Z) Step(int direction) => (direction & 3) switch
        {
            0 => (0, 1),
            1 => (1, 0),
            2 => (0, -1),
            _ => (-1, 0)
        };

        public static int Opposite(int direction) => (direction + 2) & 3;
        public static int RightOf(int direction) => (direction + 1) & 3;
        public static int LeftOf(int direction) => (direction + 3) & 3;

        // Every belt of one site by cell and level; a lift is found at its cell on both the level it takes items on and its
        // exit level.
        public static Dictionary<(int X, int Z, int Level), GoodsBelt> ByCell(IEnumerable<GoodsBelt> belts)
        {
            var cells = new Dictionary<(int, int, int), GoodsBelt>();
            foreach (var belt in belts)
            {
                cells[(belt.CellX, belt.CellZ, belt.Level)] = belt;
                if (belt.Lift != 0) cells[(belt.CellX, belt.CellZ, belt.ExitLevel)] = belt;
            }
            return cells;
        }

        private static GoodsBelt At(Dictionary<(int, int, int), GoodsBelt> cells, int x, int z, int level, int direction)
        {
            var (dx, dz) = Step(direction);
            return cells.TryGetValue((x + dx, z + dz, level), out var belt) ? belt : null;
        }

        // True when a belt next to a cell on this level points into it and hands its items on at this level (a lift's
        // entry end hands them to another level, so it feeds nothing here).
        private static bool Feeds(GoodsBelt neighbour, int level, int direction) =>
            neighbour != null && neighbour.ExitLevel == level && neighbour.Direction == direction;

        // Straight when fed from behind, from both sides or from nowhere; curved when fed from exactly one side. A lift is
        // always straight.
        public static BeltShape Shape(Dictionary<(int, int, int), GoodsBelt> cells, int x, int z, int level, int direction, int lift = 0)
        {
            if (lift != 0) return BeltShape.Straight;
            if (Feeds(At(cells, x, z, level, Opposite(direction)), level, direction)) return BeltShape.Straight;
            var fromLeft = Feeds(At(cells, x, z, level, LeftOf(direction)), level, RightOf(direction));
            var fromRight = Feeds(At(cells, x, z, level, RightOf(direction)), level, LeftOf(direction));
            return fromLeft == fromRight ? BeltShape.Straight : fromLeft ? BeltShape.CurveFromLeft : BeltShape.CurveFromRight;
        }

        public static BeltShape Shape(Dictionary<(int, int, int), GoodsBelt> cells, GoodsBelt belt) =>
            Shape(cells, belt.CellX, belt.CellZ, belt.Level, belt.Direction, belt.Lift);

        // The belt in front on this belt's exit level, unless it faces back into this one or it is the exit end of a lift.
        // Entering its back or its curve starts at 0; entering the side of a belt that does not curve from this side side-loads
        // at its midpoint.
        public static BeltLink Link(Dictionary<(int, int, int), GoodsBelt> cells, GoodsBelt belt)
        {
            var next = At(cells, belt.CellX, belt.CellZ, belt.ExitLevel, belt.Direction);
            if (next == null || next.Level != belt.ExitLevel || next.Direction == Opposite(belt.Direction)) return default;
            if (next.Direction == belt.Direction) return new BeltLink(next, 0);
            var shape = Shape(cells, next);
            // A belt turning left into the next one sits on that belt's left side.
            var curvesFromHere = next.Direction == LeftOf(belt.Direction) ? shape == BeltShape.CurveFromLeft : shape == BeltShape.CurveFromRight;
            return new BeltLink(next, curvesFromHere ? 0 : Middle);
        }

        // Nearest free position to the middle of a belt for a new item, or -1 when none keeps the spacing.
        public static int FreePosition(IEnumerable<int> occupied)
        {
            var taken = occupied.ToList();
            for (var offset = 0; offset <= Middle - ItemSpacing / 2; offset += 10)
                foreach (var position in new[] { Middle + offset, Middle - offset })
                    if (taken.All(x => System.Math.Abs(x - position) >= ItemSpacing)) return position;
            return -1;
        }
    }
}
