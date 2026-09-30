// Presentation mapping between a site's cell grid and scene space: the grid is centred on its site origin at floor height, and
// each building level (decision 0020) stands one storey higher. The origin comes from SitePlacement: the scene origin for the
// starting (or dev) site, and each other lot's place on the map in a generated world (decision 0031).
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public static class SiteGridSpace
    {
        // Height of one storey: a level's floor surface stands this far above the one below.
        public const float LevelHeight = 3f;

        // Scene position of the site's grid centre at its ground floor.
        public static Vector3 Origin(SiteLayout layout) => SitePlacement.OriginOf(layout?.SiteId);

        public static Vector3 FootprintCenter(SiteLayout layout, int cellX, int cellZ, int width, int depth, int level = 0) => Origin(layout) + new Vector3(
            (cellX + width * 0.5f - layout.Width * 0.5f) * SiteGrid.CellSize, level * LevelHeight,
            (cellZ + depth * 0.5f - layout.Depth * 0.5f) * SiteGrid.CellSize);

        public static Vector3 Center(SiteLayout layout, GoodsEquipment equipment)
        {
            var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
            return FootprintCenter(layout, equipment.CellX, equipment.CellZ, width, depth, equipment.Level);
        }

        public static Quaternion Rotation(int rotation) => Quaternion.Euler(0f, rotation * 90f, 0f);

        // Anchor cell that centres a footprint on a floor point; may lie outside the grid (the placement check decides).
        public static (int X, int Z) AnchorAt(SiteLayout layout, Vector3 point, int width, int depth)
        {
            var local = point - Origin(layout);
            return (Mathf.RoundToInt(local.x / SiteGrid.CellSize + layout.Width * 0.5f - width * 0.5f),
                Mathf.RoundToInt(local.z / SiteGrid.CellSize + layout.Depth * 0.5f - depth * 0.5f));
        }

        // Level whose floor a point (such as an avatar's feet) stands on, on a site.
        public static int LevelAt(SiteLayout layout, float height) => LevelAt(height - Origin(layout).y);

        // Level for a height measured from the site's ground floor.
        public static int LevelAt(float height) => Mathf.Max(0, Mathf.RoundToInt(height / LevelHeight));

        // Scene height of a level's floor on a site.
        public static float FloorHeight(SiteLayout layout, int level) => Origin(layout).y + level * LevelHeight;
    }
}
