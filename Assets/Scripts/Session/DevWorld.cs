// Loads the committed world save or creates the DEVELOPMENT seed; the seed is placeholder content, not design data.
using System.IO;
using FoodFactoryGame.Goods;

namespace FoodFactoryGame.Session
{
    public static class DevWorld
    {
        public const string WorldId = "dev-world";
        public const string SiteId = "dev-site";
        public const string StorageId = "dev-site-storage";

        // Returns a world that matches its committed snapshot, as GoodsNetworkBridge.InitializeServer requires.
        public static GoodsWorld LoadOrCreate(string worldPath)
        {
            if (File.Exists(worldPath) || File.Exists(worldPath + ".previous")) return GoodsSnapshotStore.Load(worldPath);
            var world = new GoodsWorld(WorldId);
            world.Bootstrap(new GoodsLocation { Id = StorageId, SiteId = SiteId, Kind = "storage", Capacity = 100 });
            GoodsSnapshotStore.Save(world, worldPath);
            return world;
        }
    }
}
