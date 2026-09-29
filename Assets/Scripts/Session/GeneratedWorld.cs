// Loads or creates a generated world's goods save (decision 0028, piece 2): the layout's property catalog is registered first,
// then a new world gets one company with the PROTOTYPE starting cash, which is given the starting restaurant's lot (its site,
// layout and shell) without charge, and the first save is committed. Nothing else is seeded: no storage, belts, machines,
// employees, warehouse or customers. A save made in this scene before piece 2 (a format 3 layout beside a dev-world snapshot,
// which never got its starting restaurant) is left untouched and not opened as a generated world.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public static class GeneratedWorld
    {
        // PROTOTYPE starting capital of the one company every player joins (owner, 2026-09-29: $5,000), in whole cents.
        public const string CompanyId = "company-1";
        public const long StartingCash = 500000;
        // Layout format that has lots (decision 0028); older formats keep the dev site beside the map.
        public const int FirstFormat = 3;

        public static bool Supports(StoredWorldLayout stored) => stored?.Layout != null && stored.Layout.FormatVersion >= FirstFormat;

        // The listed lot of the layout's starting restaurant.
        public static PropertyOffer StartOffer(WorldLayout layout)
        {
            var lot = layout.Lots.FirstOrDefault(x => x.BuildingId == layout.StartRestaurantId)
                ?? throw new System.ArgumentException("The layout has no lot for its starting restaurant.");
            return WorldLayoutShells.ToOffer(lot, layout.Buildings.First(x => x.Id == lot.BuildingId));
        }

        // Returns a world that matches its committed snapshot (GoodsNetworkBridge.InitializeServer requires it), with the
        // layout's property catalog registered; null for a pre-piece-2 save that must keep the dev world. Item max stacks
        // (content) are registered before anything is counted in slots.
        public static GoodsWorld LoadOrCreate(string worldPath, StoredWorldLayout stored, IEnumerable<ItemDefinition> items = null)
        {
            if (!Supports(stored)) throw new System.ArgumentException("A generated world needs a layout with lots.");
            var start = StartOffer(stored.Layout);
            var catalog = WorldLayoutShells.PropertyOffers(stored.Layout);
            if (GoodsSnapshotStore.HasSnapshots(worldPath))
            {
                var loaded = GoodsSnapshotStore.Load(worldPath);
                if (loaded.Snapshot().Properties.All(x => x.LotId != start.LotId))
                {
                    Debug.Log("[Session] This world was saved before generated worlds had their own restaurant; it keeps the dev site.");
                    return null;
                }
                Register(loaded, items);
                loaded.RegisterPropertyOffers(catalog);
                return loaded;
            }
            var world = new GoodsWorld(stored.WorldId);
            Register(world, items);
            world.RegisterPropertyOffers(catalog);
            world.Bootstrap(new GoodsCompany { Id = CompanyId, Cash = StartingCash });
            world.Bootstrap(start, CompanyId);
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log($"[Session] Created generated world {stored.WorldId}: {CompanyId} owns {start.BuildingId} (site {start.SiteId}).");
            return world;
        }

        private static void Register(GoodsWorld world, IEnumerable<ItemDefinition> items)
        {
            foreach (var item in items ?? Enumerable.Empty<ItemDefinition>())
                if (item != null) world.RegisterItem(item.Id, item.MaxStack);
        }
    }
}
