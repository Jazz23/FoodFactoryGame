// Presentation geometry of a generated lot (decision 0028): the lot is its building's footprint extended forward to the street,
// so a site whose building spans the lot on every side but one faces the street on that side. Dev sites (a shell standing
// inside a larger grid) have no street side and keep their own presentation.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Customers
{
    public static class SiteStreet
    {
        // Metres of street in front of the lot that customer figures walk on, and how far past the lot's sides it reaches.
        public const float Band = 6f;
        public const float Reach = 12f;

        // The outward unit direction (site axes) of the lot's street edge, or null when the site is not a generated lot.
        public static Vector2Int? Outward(SiteLayout layout, IEnumerable<GoodsBuilding> buildings)
        {
            if (layout == null) return null;
            var shell = buildings?.FirstOrDefault(x => x.SiteId == layout.SiteId);
            if (shell == null) return null;
            var spansX = shell.CellX == 0 && shell.Width == layout.Width;
            var spansZ = shell.CellZ == 0 && shell.Depth == layout.Depth;
            if (spansX && shell.CellZ == 0 && shell.Depth < layout.Depth) return new Vector2Int(0, 1);
            if (spansX && shell.CellZ > 0 && shell.CellZ + shell.Depth == layout.Depth) return new Vector2Int(0, -1);
            if (spansZ && shell.CellX == 0 && shell.Width < layout.Width) return new Vector2Int(1, 0);
            if (spansZ && shell.CellX > 0 && shell.CellX + shell.Width == layout.Width) return new Vector2Int(-1, 0);
            return null;
        }

        // Scene points on the street in front of the lot (the grid is centred on the scene origin), from past one side of
        // the lot to past the other, Band / 2 out from its street edge.
        public static List<Vector3> Points(SiteLayout layout, Vector2Int outward, int count = 9)
        {
            var halfX = layout.Width * 0.5f * SiteGrid.CellSize;
            var halfZ = layout.Depth * 0.5f * SiteGrid.CellSize;
            var along = outward.x == 0 ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
            var edge = new Vector3(outward.x * (halfX + Band * 0.5f), 0, outward.y * (halfZ + Band * 0.5f));
            var reach = (outward.x == 0 ? halfX : halfZ) + Reach - 1f;
            return Enumerable.Range(0, count).Select(i => edge + along * Mathf.Lerp(-reach, reach, i / (count - 1f))).ToList();
        }
    }
}
