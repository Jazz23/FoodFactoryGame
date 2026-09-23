// Presentation mapping between a site's cell grid and scene space: the grid is centred on the scene origin at floor height.
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    public static class SiteGridSpace
    {
        public static Vector3 FootprintCenter(SiteLayout layout, int cellX, int cellZ, int width, int depth) => new(
            (cellX + width * 0.5f - layout.Width * 0.5f) * SiteGrid.CellSize, 0f,
            (cellZ + depth * 0.5f - layout.Depth * 0.5f) * SiteGrid.CellSize);

        public static Vector3 Center(SiteLayout layout, GoodsEquipment equipment)
        {
            var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
            return FootprintCenter(layout, equipment.CellX, equipment.CellZ, width, depth);
        }

        public static Quaternion Rotation(int rotation) => Quaternion.Euler(0f, rotation * 90f, 0f);

        // Anchor cell that centres a footprint on a floor point; may lie outside the grid (the placement check decides).
        public static (int X, int Z) AnchorAt(SiteLayout layout, Vector3 point, int width, int depth) => (
            Mathf.RoundToInt(point.x / SiteGrid.CellSize + layout.Width * 0.5f - width * 0.5f),
            Mathf.RoundToInt(point.z / SiteGrid.CellSize + layout.Depth * 0.5f - depth * 0.5f));
    }
}
