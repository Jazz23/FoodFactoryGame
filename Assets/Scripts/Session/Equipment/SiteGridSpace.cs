// Presentation mapping between a site's cell grid and scene space: the grid is centred on the scene origin at floor height,
// and each building level (decision 0020) stands one storey higher.
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public static class SiteGridSpace
    {
        // Height of one storey: a level's floor surface stands this far above the one below.
        public const float LevelHeight = 3f;

        public static Vector3 FootprintCenter(SiteLayout layout, int cellX, int cellZ, int width, int depth, int level = 0) => new(
            (cellX + width * 0.5f - layout.Width * 0.5f) * SiteGrid.CellSize, level * LevelHeight,
            (cellZ + depth * 0.5f - layout.Depth * 0.5f) * SiteGrid.CellSize);

        public static Vector3 Center(SiteLayout layout, GoodsEquipment equipment)
        {
            var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
            return FootprintCenter(layout, equipment.CellX, equipment.CellZ, width, depth, equipment.Level);
        }

        public static Quaternion Rotation(int rotation) => Quaternion.Euler(0f, rotation * 90f, 0f);

        // Anchor cell that centres a footprint on a floor point; may lie outside the grid (the placement check decides).
        public static (int X, int Z) AnchorAt(SiteLayout layout, Vector3 point, int width, int depth) => (
            Mathf.RoundToInt(point.x / SiteGrid.CellSize + layout.Width * 0.5f - width * 0.5f),
            Mathf.RoundToInt(point.z / SiteGrid.CellSize + layout.Depth * 0.5f - depth * 0.5f));

        // Level whose floor a point (such as an avatar's feet) stands on.
        public static int LevelAt(float height) => Mathf.Max(0, Mathf.RoundToInt(height / LevelHeight));
    }
}
