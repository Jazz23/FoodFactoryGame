// Presentation geometry of a belt's item path: a straight belt runs from its back edge to its front edge; a curved belt runs
// a quarter circle of radius 0.5 around the tile corner between its entry side and its front, matching the corner models.
// Positions are the server's BeltRules units, so an item drawn here is where the simulation says it is.
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session.Belts
{
    public static class BeltPath
    {
        // Height of the belt surface above the floor, from the belt models.
        public const float SurfaceHeight = 0.8f;

        // Point on the path of a tile centred on the origin that travels +Z; t runs 0..1 along the path.
        public static Vector3 LocalPoint(BeltShape shape, float t)
        {
            var angle = Mathf.Clamp01(t) * Mathf.PI * 0.5f;
            return shape switch
            {
                BeltShape.CurveFromLeft => new Vector3(-0.5f + 0.5f * Mathf.Sin(angle), SurfaceHeight, 0.5f - 0.5f * Mathf.Cos(angle)),
                BeltShape.CurveFromRight => new Vector3(0.5f - 0.5f * Mathf.Sin(angle), SurfaceHeight, 0.5f - 0.5f * Mathf.Cos(angle)),
                _ => new Vector3(0f, SurfaceHeight, Mathf.Clamp01(t) - 0.5f)
            };
        }

        public static Vector3 CellCenter(SiteLayout layout, int cellX, int cellZ, int level = 0) =>
            SiteGridSpace.FootprintCenter(layout, cellX, cellZ, 1, 1, level);

        public static Vector3 WorldPoint(SiteLayout layout, GoodsBelt belt, BeltShape shape, float position) =>
            CellCenter(layout, belt.CellX, belt.CellZ, belt.Level)
            + SiteGridSpace.Rotation(belt.Direction) * LocalPoint(shape, position / BeltRules.UnitsPerTile) * SiteGrid.CellSize;

        // A curved belt is drawn with the corner model entering along the feeder's travel (see BeltPresenter).
        public static Quaternion ModelRotation(BeltShape shape, int direction) => SiteGridSpace.Rotation(shape switch
        {
            BeltShape.CurveFromLeft => BeltRules.RightOf(direction),
            BeltShape.CurveFromRight => BeltRules.LeftOf(direction),
            _ => direction
        });

        // Grid cell under a floor point.
        public static (int X, int Z) CellAt(SiteLayout layout, Vector3 point) => SiteGridSpace.AnchorAt(layout, point, 1, 1);
    }
}
