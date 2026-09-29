// Turns a stored world layout's lots into the property catalog (decision 0028): content derived from the layout, never saved.
// A lot's site grid covers exactly the lot, with site cell (0,0) at the lot's minimum world cell. The whole world grid is
// axis-aligned, so the mapping is a translation only and the building keeps its offset from the lot's corner. A restaurant or
// factory shell becomes the site's decision-0019 GoodsBuilding; farms and stations get no shell. Layouts without lots
// (formats 1 and 2) list nothing.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.World;

namespace FoodFactoryGame.Goods
{
    public static class WorldLayoutShells
    {
        public static List<PropertyOffer> PropertyOffers(WorldLayout layout)
        {
            var buildings = layout.Buildings.ToDictionary(x => x.Id, StringComparer.Ordinal);
            return layout.Lots.Select(x => ToOffer(x, buildings[x.BuildingId])).ToList();
        }

        public static PropertyOffer ToOffer(WorldLot lot, WorldBuilding building)
        {
            if (lot == null || building == null || lot.BuildingId != building.Id || !building.HasLot)
                throw new ArgumentException("A lot converts only with its own property building.");
            return new PropertyOffer
            {
                LotId = lot.Id, SiteId = lot.SiteId, BuildingId = building.Id, Category = CategoryOf(building.Category),
                ForSale = building.Ownership == Ownership.ForSale, PriceCents = building.PriceCents,
                LotX = lot.X, LotZ = lot.Z, Width = lot.Width, Depth = lot.Depth, AccessX = lot.Access.X, AccessZ = lot.Access.Z,
                BuildingX = building.X - lot.X, BuildingZ = building.Z - lot.Z, BuildingWidth = building.Width, BuildingDepth = building.Depth,
                Doors = building.Doors.Select(x => new GridCell { X = x.X - lot.X, Z = x.Z - lot.Z }).ToList(),
                Floors = building.IsShell ? building.Floors : 1
            };
        }

        public static SiteLayout SiteLayoutFor(WorldLot lot) => new() { SiteId = lot.SiteId, Width = lot.Width, Depth = lot.Depth };

        public static GoodsBuilding ToGoodsBuilding(WorldBuilding building, WorldLot lot)
        {
            if (building == null || !building.IsShell) throw new ArgumentException("Only generated restaurant and factory shells convert.");
            return GoodsWorld.BuildingOf(ToOffer(lot, building));
        }

        private static string CategoryOf(BuildingCategory category) => category switch
        {
            BuildingCategory.Restaurant => GoodsWorld.RestaurantKind,
            BuildingCategory.Factory => GoodsWorld.FactoryKind,
            BuildingCategory.Farm => GoodsWorld.FarmCategory,
            BuildingCategory.Station => GoodsWorld.StationCategory,
            _ => throw new ArgumentException($"A {category} is never property.")
        };
    }
}
