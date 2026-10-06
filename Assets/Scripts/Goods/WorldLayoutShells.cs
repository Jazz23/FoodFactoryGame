// Turns a stored world layout's lots into the property catalog (decision 0028): content derived from the layout, never saved.
// A lot's site grid covers exactly the lot, with site cell (0,0) at the lot's minimum world cell. The whole world grid is
// axis-aligned, so the mapping is a translation only and the building keeps its offset from the lot's corner. A restaurant or
// factory shell becomes the site's decision-0019 GoodsBuilding; farms and stations get no shell. Layouts without lots
// (formats 1 and 2) list nothing. A format 4 restaurant also lists its back door and the starter dock that comes with it
// (decision 0037).
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
            var percents = layout.Districts.ToDictionary(x => x.Id, x => x.PricePercent, StringComparer.Ordinal);
            return layout.Lots.Select(x =>
            {
                var building = buildings[x.BuildingId];
                return ToOffer(x, building, percents.TryGetValue(building.DistrictId ?? "", out var percent) && percent > 0 ? percent : 100);
            }).ToList();
        }

        // pricePercent: the lot's district price multiplier (decision 0034 prices structure with it); 100 without a district.
        public static PropertyOffer ToOffer(WorldLot lot, WorldBuilding building, int pricePercent = 100)
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
                BackDoors = building.BackDoor == null ? new List<GridCell>() : new List<GridCell> { new() { X = building.BackDoor.X - lot.X, Z = building.BackDoor.Z - lot.Z } },
                HasDock = building.ServiceDock != null,
                DockX = building.ServiceDock == null ? 0 : building.ServiceDock.X - lot.X,
                DockZ = building.ServiceDock == null ? 0 : building.ServiceDock.Z - lot.Z,
                // The dock content is 2 x 1 along X at rotation 0; a dock rectangle along Z is a quarter turn.
                DockRotation = building.ServiceDock != null && building.ServiceDock.Depth > building.ServiceDock.Width ? 1 : 0,
                Floors = building.IsShell ? building.Floors : 1, PricePercent = pricePercent
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
