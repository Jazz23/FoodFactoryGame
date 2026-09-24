// Pure grid rules shared by the server's placement check and the client's placement preview; the server always re-checks.
// Placed equipment and belts share the grid: nothing may overlap anything else.
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
        public static string PlacementProblem(GoodsSnapshot state, GoodsEquipment equipment, int cellX, int cellZ, int rotation)
        {
            if (rotation < 0 || rotation > 3) return "invalid-rotation";
            var (width, depth) = Footprint(equipment.Width, equipment.Depth, rotation);
            return CellProblem(state, equipment.SiteId, cellX, cellZ, width, depth, equipment.Id);
        }

        // Null when a footprint lies inside the site's layout and overlaps no placed equipment or belt other than ignoreId;
        // otherwise "out-of-bounds" or "blocked".
        public static string CellProblem(GoodsSnapshot state, string siteId, int cellX, int cellZ, int width, int depth, string ignoreId)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null || cellX < 0 || cellZ < 0 || cellX + width > layout.Width || cellZ + depth > layout.Depth)
                return "out-of-bounds";
            foreach (var other in state.Equipment.Where(x => x.Id != ignoreId && x.SiteId == siteId && x.State == EquipmentState.Placed))
            {
                var (otherWidth, otherDepth) = Footprint(other.Width, other.Depth, other.Rotation);
                if (Overlaps(cellX, cellZ, width, depth, other.CellX, other.CellZ, otherWidth, otherDepth)) return "blocked";
            }
            if (state.Belts != null && state.Belts.Any(x => x.Id != ignoreId && x.SiteId == siteId
                    && Overlaps(cellX, cellZ, width, depth, x.CellX, x.CellZ, 1, 1)))
                return "blocked";
            return null;
        }
    }
}
