// Verifies the dev seed's logistics (decisions 0022, 0023): a new world has both sites mapped, the warehouse with dough and a dock,
// the restaurant dock and one truck on the warehouse-to-restaurant route record that delivers warehouse dough; an older save gains
// them exactly once; admission grants the warehouse without an inventory there. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class DevWorldLogisticsTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);

        private static EquipmentDefinition Dock() => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Dock.asset");

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactorySessionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private GoodsWorld Open(EquipmentDefinition dock) => DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), dock: dock);

        [Test]
        public void NewWorldTruckDeliversWarehouseDoughToTheRestaurantDock()
        {
            var world = Open(Dock());
            var state = world.Snapshot();
            Assert.That(state.Sites.Select(x => x.Id), Is.EquivalentTo(new[] { DevWorld.SiteId, DevWorld.WarehouseSiteId }));
            Assert.That(state.Companies.Single().SiteIds, Is.EquivalentTo(new[] { DevWorld.SiteId, DevWorld.WarehouseSiteId }));
            var truck = state.Trucks.Single();
            var route = state.Routes.Single();
            Assert.That((route.Id, route.CompanyId, route.PickupDockId, route.DropoffDockId, route.AllowedItemIds.Count),
                Is.EqualTo((DevWorld.RouteId, DevWorld.CompanyId, DevWorld.WarehouseDockId, DevWorld.DockId, 0)));
            Assert.That((truck.Id, truck.State, truck.RouteId), Is.EqualTo((DevWorld.TruckId, TruckState.Loading, DevWorld.RouteId)));
            Assert.That(GoodsWorld.RoadSeconds(state, DevWorld.WarehouseSiteId, DevWorld.SiteId, truck.SpeedMetresPerSecond), Is.EqualTo(10));

            // Someone stages 20 warehouse dough at the warehouse dock; the truck brings it to the restaurant.
            Assert.That(world.TryGrantDurably("manager", DevWorld.WarehouseSiteId, WorldPath), Is.True);
            var staged = world.TransferDurably("manager", new TransferIntent
            {
                RequestId = "stage", LotId = DevWorld.WarehouseDoughLotId, DestinationId = DevWorld.WarehouseDockId + ":in", Quantity = 20
            }, WorldPath);
            Assert.That(staged.Accepted, Is.True);
            Assert.That(world.TryAdvanceDurably(20, WorldPath), Is.True);
            var delivered = GoodsSnapshotStore.Load(WorldPath).Snapshot().Lots.Where(x => x.LocationId == DevWorld.DockId + ":out").ToList();
            Assert.That((delivered.Sum(x => x.Quantity), delivered.All(x => x.OwnerId == DevWorld.SiteId && x.ItemId == DevWorld.DoughItemId)),
                Is.EqualTo((20, true)), "Loaded in 4 s, 10 s on the road, unloaded in 4 s, and committed.");
        }

        [Test]
        public void OlderSaveGainsTheLogisticsOnce()
        {
            Open(null);
            var upgraded = Open(Dock());
            Assert.That(GoodsSnapshotStore.Load(WorldPath).Snapshot().Trucks.Single().Id, Is.EqualTo(DevWorld.TruckId), "Committed before serving.");
            var revision = upgraded.Snapshot().Revision;
            var restarted = Open(Dock());
            Assert.That((restarted.Snapshot().Revision, restarted.Snapshot().Trucks.Count, restarted.Snapshot().Equipment.Count(x => x.Kind == GoodsWorld.DockKind)),
                Is.EqualTo((revision, 1, 2)), "Reopening adds and writes nothing.");
        }

        [Test]
        public void SavedWarehouseAtAnOlderPositionFollowsTheSeedMap()
        {
            var world = Open(Dock());
            Assert.That(world.MoveSite(DevWorld.WarehouseSiteId, 600, 300), Is.True);
            GoodsSnapshotStore.Save(world, WorldPath);
            var reopened = Open(Dock()).Snapshot();
            var warehouse = reopened.Sites.Single(x => x.Id == DevWorld.WarehouseSiteId);
            Assert.That((warehouse.MapX, warehouse.MapZ), Is.EqualTo((DevWorld.WarehouseMapX, DevWorld.WarehouseMapZ)));
            Assert.That(GoodsWorld.RoadSeconds(GoodsSnapshotStore.Load(WorldPath).Snapshot(), DevWorld.WarehouseSiteId, DevWorld.SiteId,
                DevWorld.TruckSpeedMetresPerSecond), Is.EqualTo(10), "Committed before serving.");
        }

        [Test]
        public void AdmissionGrantsTheWarehouseWithoutAnInventoryThere()
        {
            var world = Open(Dock());
            using var registry = new PlayerRegistry(Path.Combine(_directory, SessionOptions.RegistryFileName));
            var admission = new SessionAdmission(registry, world, DevWorld.SiteId, WorldPath, DevWorld.InventoryCapacity, null, DevWorld.RemoteSiteIds);
            var admitted = admission.Admit("Tester", new string('a', 43));
            Assert.That(admitted.Accepted, Is.True);
            var player = admitted.PlayerId;
            Assert.That((world.CanView(player, DevWorld.SiteId), world.CanView(player, DevWorld.WarehouseSiteId)), Is.EqualTo((true, true)));
            Assert.That(world.Snapshot().Locations.Single(x => x.Id == GoodsWorld.InventoryLocationId(player)).SiteId, Is.EqualTo(DevWorld.SiteId));
        }
    }
}
