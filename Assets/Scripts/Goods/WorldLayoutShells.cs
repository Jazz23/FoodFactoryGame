// STUB link between generated shells and the site model (decision 0026). Whether each purchasable building becomes its own
// site with its own SiteGrid is an open owner decision, so nothing calls this at runtime and WorldBuilding.SiteId stays empty.
// It exists to prove that every generated restaurant and factory shell is a valid decision-0019 GoodsBuilding: the shell is
// expressed on a site grid exactly its own size, anchored at the grid's origin.
using System;
using System.Linq;
using FoodFactoryGame.World;

namespace FoodFactoryGame.Goods
{
    public static class WorldLayoutShells
    {
        public static SiteLayout SiteLayoutFor(WorldBuilding building, string siteId) =>
            new() { SiteId = siteId, Width = building.Width, Depth = building.Depth };

        public static GoodsBuilding ToGoodsBuilding(WorldBuilding building, string siteId)
        {
            if (building == null || !building.IsShell) throw new ArgumentException("Only generated restaurant and factory shells convert.");
            return new GoodsBuilding
            {
                Id = building.Id, SiteId = siteId,
                Kind = building.Category == BuildingCategory.Factory ? GoodsWorld.FactoryKind : GoodsWorld.RestaurantKind,
                CellX = 0, CellZ = 0, Width = building.Width, Depth = building.Depth, Floors = building.Floors,
                Doors = building.Doors.Select(x => new GridCell { X = x.X - building.X, Z = x.Z - building.Z }).ToList()
            };
        }
    }
}
