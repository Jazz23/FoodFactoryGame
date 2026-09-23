// Loads the committed world save or creates the DEVELOPMENT seed; the seed is placeholder content, not design data.
// The seed is applied only to a brand-new world: an existing save never gains the layout, oven or storage dough retroactively.
using System.Collections.Generic;
using System.IO;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;

namespace FoodFactoryGame.Session
{
    public static class DevWorld
    {
        public const string WorldId = "dev-world";
        public const string SiteId = "dev-site";
        public const string StorageId = "dev-site-storage";
        public const string OvenId = "dev-oven-1";
        // PROTOTYPE values: a 20x20 one-metre grid, the seeded oven's anchor cell, and each player's inventory size.
        public const int GridWidth = 20;
        public const int GridDepth = 20;
        public const int OvenCellX = 12;
        public const int OvenCellZ = 13;
        public const int InventoryCapacity = 10;
        // PROTOTYPE ingredients until something produces dough: stock in the dev storage and a few for each new player.
        public const string DoughItemId = "dough";
        public const int StorageDough = 20;
        public const int StarterDough = 5;
        public const long DoughSpoilAfterSeconds = 7200;

        public static IReadOnlyList<GoodsLot> StarterGoods => new[]
        {
            new GoodsLot { ItemId = DoughItemId, Quantity = StarterDough, SpoilAfterSeconds = DoughSpoilAfterSeconds }
        };

        // Returns a world that matches its committed snapshot, as GoodsNetworkBridge.InitializeServer requires.
        // Without an oven definition the seed has the layout but no equipment.
        public static GoodsWorld LoadOrCreate(string worldPath, EquipmentDefinition oven = null)
        {
            if (File.Exists(worldPath) || File.Exists(worldPath + ".previous")) return GoodsSnapshotStore.Load(worldPath);
            var world = new GoodsWorld(WorldId);
            world.Bootstrap(new GoodsLocation { Id = StorageId, SiteId = SiteId, Kind = "storage", Capacity = 100 });
            world.Bootstrap(new GoodsLot
            {
                Id = "dev-storage-dough", ItemId = DoughItemId, OwnerId = SiteId, LocationId = StorageId,
                Quantity = StorageDough, SpoilAfterSeconds = DoughSpoilAfterSeconds
            });
            world.Bootstrap(new SiteLayout { SiteId = SiteId, Width = GridWidth, Depth = GridDepth });
            if (oven != null) world.Bootstrap(oven.CreatePlaced(OvenId, SiteId, OvenCellX, OvenCellZ, 0));
            GoodsSnapshotStore.Save(world, worldPath);
            return world;
        }
    }
}
