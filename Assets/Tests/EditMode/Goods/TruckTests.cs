// Verifies trucks (decision 0022): a routed truck loads from the pickup dock's outgoing buffer at its rate, drives the road
// time, unloads into the dropoff dock's incoming buffer re-owned by that site, and returns; cargo ages in transit; a full
// buffer or a missing dock makes it wait without losing goods; nobody reaches cargo except the truck; route changes are
// validated, replayed and rolled back on a failed commit; step size never changes the outcome; trucks survive a reload and
// a v10 save upgrades. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class TruckTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        // TEST-ONLY values: "co" owns a farm at the map origin and a shop 30 m away by road (20 east, 10 north); "rival" owns a
        // stall. Each site has a 6x6 grid and a 2x1 dock with 3 outgoing and 2 incoming slots. Crates stack to 10. The truck
        // has 2 cargo slots, drives 10 m/s (3 s a trip) and moves 4 units a second. Not gameplay content.
        [SetUp]
        public void SetUp()
        {
            _world = new GoodsWorld("test-world");
            _world.RegisterItem("crate", 10);
            _world.RegisterItem("apple", 10);
            foreach (var site in new[] { "farm", "shop", "stall" })
            {
                _world.Bootstrap(new GoodsLocation { Id = site + "-storage", SiteId = site, Kind = "storage", Capacity = 10 });
                _world.Bootstrap(new SiteLayout { SiteId = site, Width = 6, Depth = 6 });
                _world.Bootstrap(Dock(site + "-dock", site));
            }
            _world.Bootstrap(new GoodsSite { Id = "farm", Name = "Farm" });
            _world.Bootstrap(new GoodsSite { Id = "shop", Name = "Shop", MapX = 20, MapZ = 10 });
            _world.Bootstrap(new GoodsSite { Id = "stall", Name = "Stall", MapX = -5 });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 0, SiteIds = { "farm", "shop" } });
            _world.Bootstrap(new GoodsCompany { Id = "rival", Cash = 0, SiteIds = { "stall" } });
            _world.Bootstrap(new GoodsTruck
            {
                Id = "truck", CompanyId = "co", Name = "Truck", CargoSlots = 2, SpeedMetresPerSecond = 10, LoadUnitsPerSecond = 4,
                State = TruckState.Parked, SiteId = "farm"
            });
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryTruckTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            foreach (var site in new[] { "farm", "shop" }) Assert.That(_world.TryGrantDurably("boss", site, PathForSave), Is.True);
            Assert.That(_world.TryGrantDurably("farmer", "farm", PathForSave), Is.True);
            Assert.That(_world.TryGrantDurably("vendor", "stall", PathForSave), Is.True);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private static GoodsEquipment Dock(string id, string site) => new()
        {
            Id = id, Kind = GoodsWorld.DockKind, SiteId = site, State = EquipmentState.Placed, HolderId = "", Width = 2, Depth = 1,
            InputCapacity = 3, OutputCapacity = 2
        };

        private void Stock(string id, string site, string item, int quantity, long exposure = 0, long spoilAfter = 1000) =>
            _world.Bootstrap(new GoodsLot
            {
                Id = id, ItemId = item, OwnerId = site, LocationId = site + "-dock:in", Quantity = quantity, ExposureSeconds = exposure,
                SpoilAfterSeconds = spoilAfter
            });

        private GoodsOutcome Route(string request, string pickup = "farm-dock", string dropoff = "shop-dock", string player = "boss",
            params string[] items) => _world.SetTruckRouteDurably(player, request, "truck", pickup, dropoff, items, PathForSave);

        private GoodsTruck Truck() => _world.Snapshot().Trucks.Single();
        private int Units(string location, string item = null) =>
            _world.Snapshot().Lots.Where(x => x.LocationId == location && (item == null || x.ItemId == item)).Sum(x => x.Quantity);

        [Test]
        public void RoadTimeIsTheManhattanDistanceOverSpeed()
        {
            var state = _world.Snapshot();
            Assert.That(GoodsWorld.RoadMetres(state, "farm", "shop"), Is.EqualTo(30));
            Assert.That(GoodsWorld.RoadSeconds(state, "farm", "shop", 10), Is.EqualTo(3));
            Assert.That(GoodsWorld.RoadSeconds(state, "farm", "shop", 7), Is.EqualTo(5), "Rounded up.");
            Assert.That(GoodsWorld.RoadSeconds(state, "farm", "farm", 7), Is.Zero);
            Assert.That(GoodsWorld.RoadMetres(state, "farm", "nowhere"), Is.Null);
        }

        [Test]
        public void RoutedTruckCarriesGoodsFromDockToDockAndReturns()
        {
            Stock("crates", "farm", "crate", 6);
            var outcome = Route("route-1");
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((true, "route-set")));
            Assert.That((Truck().State, Truck().SiteId), Is.EqualTo((TruckState.Loading, "farm")), "Already at the pickup site.");

            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Units("farm-dock:in")), Is.EqualTo((4, 2)), "Loads its rate each second.");
            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Truck().State), Is.EqualTo((6, TruckState.Loading)));
            _world.Advance(1);
            Assert.That((Truck().State, Truck().DestinationSiteId, Truck().RemainingSeconds), Is.EqualTo((TruckState.ToDropoff, "shop", 3L)),
                "Nothing more to load: it leaves with its cargo.");
            _world.Advance(3);
            Assert.That((Truck().State, Truck().SiteId), Is.EqualTo((TruckState.Unloading, "shop")));
            _world.Advance(2);
            Assert.That((Units("truck:cargo"), Units("shop-dock:out")), Is.EqualTo((0, 6)));
            Assert.That(_world.Snapshot().Lots.Where(x => x.LocationId == "shop-dock:out").All(x => x.OwnerId == "shop"), Is.True,
                "Unloaded goods belong to the dropoff site.");
            Assert.That((Truck().State, Truck().DestinationSiteId), Is.EqualTo((TruckState.ToPickup, "farm")), "Empty: back to the pickup.");
            _world.Advance(3);
            Assert.That(Truck().State, Is.EqualTo(TruckState.Loading));
            _world.Advance(5);
            Assert.That(Truck().State, Is.EqualTo(TruckState.Loading), "An empty truck waits at an empty dock.");
        }

        [Test]
        public void FullCargoLeavesTheRestAtTheDock()
        {
            // Crates are older, so they load first (most exposed first) and fill both slots.
            Stock("crates", "farm", "crate", 20, exposure: 10);
            Stock("apples", "farm", "apple", 5);
            Route("route-1");
            _world.Advance(10);
            Assert.That(Units("truck:cargo") + Units("shop-dock:out"), Is.EqualTo(20), "Two slots of ten: the truck took what fits.");
            Assert.That(Units("farm-dock:in"), Is.EqualTo(5));
        }

        [Test]
        public void CargoFilterLoadsOnlyAllowedItems()
        {
            Stock("crates", "farm", "crate", 3);
            Stock("apples", "farm", "apple", 3);
            Route("route-1", items: "apple");
            _world.Advance(1);
            Assert.That((Units("truck:cargo", "apple"), Units("truck:cargo", "crate")), Is.EqualTo((3, 0)));
        }

        [Test]
        public void GoodsAgeInTransitAndCanSpoil()
        {
            Stock("apples", "farm", "apple", 2, exposure: 995, spoilAfter: 1000);
            Route("route-1");
            _world.Advance(10);
            var apples = _world.Snapshot().Lots.Single(x => x.ItemId == "apple");
            Assert.That((apples.LocationId, apples.Spoiled, apples.Id), Is.EqualTo(("shop-dock:out", true, "apples")),
                "Trucks are not refrigerated; the lot keeps its ID when it moves whole.");
        }

        [Test]
        public void FullDropoffOrMissingDockMakesTheTruckWaitWithoutLosingGoods()
        {
            Stock("crates", "farm", "crate", 10);
            _world.Bootstrap(new GoodsLot { Id = "blocker", ItemId = "apple", OwnerId = "shop", LocationId = "shop-dock:out", Quantity = 20, SpoilAfterSeconds = 1000 });
            Route("route-1");
            _world.Advance(30);
            Assert.That((Truck().State, Units("truck:cargo")), Is.EqualTo((TruckState.Unloading, 10)), "The incoming buffer is full.");
            Assert.That(_world.TransferDurably("boss", new TransferIntent { RequestId = "clear", LotId = "blocker", DestinationId = "shop-storage", Quantity = 20 },
                PathForSave).Accepted, Is.True);
            // With the dock picked up, the truck waits for it to come back.
            Assert.That(_world.TryGrantDurably("porter", "shop", PathForSave, 5), Is.True);
            Assert.That(_world.PickUpDurably("porter", "lift-dock", "shop-dock", PathForSave).Accepted, Is.True);
            _world.Advance(5);
            Assert.That((Truck().State, Units("truck:cargo")), Is.EqualTo((TruckState.Unloading, 10)), "No dock: it waits, cargo aboard.");
            Assert.That(_world.PlaceDurably("porter", "put-dock", "shop-dock", 0, 0, 0, PathForSave).Accepted, Is.True);
            _world.Advance(3);
            Assert.That((Units("truck:cargo"), Units("shop-dock:out")), Is.EqualTo((0, 10)));
            Assert.That(_world.Snapshot().Lots.Where(x => x.ItemId == "crate").Sum(x => x.Quantity), Is.EqualTo(10), "Nothing lost or duplicated.");
        }

        [Test]
        public void NobodyButTheTruckReachesItsCargo()
        {
            Stock("crates", "farm", "crate", 4);
            Route("route-1");
            _world.Advance(1);
            var cargo = _world.Snapshot().Lots.Single(x => x.LocationId == "truck:cargo");
            Assert.That(_world.TransferDurably("boss", new TransferIntent { RequestId = "steal", LotId = cargo.Id, DestinationId = "farm-storage", Quantity = 1 },
                PathForSave).Reason, Is.EqualTo("forbidden"));
            _world.Bootstrap(new GoodsLot { Id = "extra", ItemId = "crate", OwnerId = "farm", LocationId = "farm-storage", Quantity = 1, SpoilAfterSeconds = 1000 });
            Assert.That(_world.TransferDurably("boss", new TransferIntent { RequestId = "stow", LotId = "extra", DestinationId = "truck:cargo", Quantity = 1 },
                PathForSave).Reason, Is.EqualTo("invalid-route"));
            Assert.Throws<ArgumentException>(() => _world.Grant("boss", GoodsWorld.RoadSiteId), "The road is never granted.");
        }

        [Test]
        public void RouteChangesAreValidatedAndChangeNothingWhenRefused()
        {
            var revision = _world.Snapshot().Revision;
            Assert.That(_world.SetTruckRouteDurably("boss", "r0", "ghost", "farm-dock", "shop-dock", null, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(Route("r1", pickup: "farm-storage").Reason, Is.EqualTo("invalid-dock"));
            Assert.That(Route("r2", player: "farmer").Reason, Is.EqualTo("forbidden"), "Needs a grant on both sites.");
            Assert.That(Route("r3", dropoff: "stall-dock", player: "vendor").Reason, Is.EqualTo("forbidden"));
            Assert.That(Route("r4", dropoff: "farm-dock").Reason, Is.EqualTo("same-site"));
            Assert.That(Route("r5", items: new[] { "apple", "apple" }).Reason, Is.EqualTo("invalid-cargo"));
            Assert.That(Route("r6", items: new[] { " " }).Reason, Is.EqualTo("invalid-cargo"));
            Assert.That((_world.Snapshot().Revision, Truck().State), Is.EqualTo((revision, TruckState.Parked)), "Rejections are not recorded.");
        }

        [Test]
        public void AcceptedRouteReplaysAndSurvivesReload()
        {
            var first = Route("route-1", items: "crate");
            var again = Route("route-1", pickup: "shop-dock", dropoff: "farm-dock");
            Assert.That((again.Accepted, again.Revision), Is.EqualTo((true, first.Revision)), "Replayed, not applied again.");
            Assert.That(Truck().PickupDockId, Is.EqualTo("farm-dock"));
            var reloaded = GoodsSnapshotStore.Load(PathForSave).Snapshot().Trucks.Single();
            Assert.That((reloaded.PickupDockId, reloaded.DropoffDockId, reloaded.AllowedItemIds.Single(), reloaded.State),
                Is.EqualTo(("farm-dock", "shop-dock", "crate", TruckState.Loading)));
        }

        [Test]
        public void FailedCommitLeavesTheRouteUnchanged()
        {
            var outcome = _world.SetTruckRouteDurably("boss", "route-1", "truck", "farm-dock", "shop-dock", null, BadPath);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That((Truck().State, Truck().PickupDockId), Is.EqualTo((TruckState.Parked, "")));
        }

        [Test]
        public void LoadedTruckRedirectedDeliversToItsNewDropoff()
        {
            Stock("crates", "farm", "crate", 4);
            Route("route-1");
            _world.Advance(1);
            Assert.That(Route("route-2", pickup: "shop-dock", dropoff: "farm-dock").Accepted, Is.True);
            Assert.That((Truck().State, Truck().SiteId), Is.EqualTo((TruckState.Unloading, "farm")),
                "It carries cargo, so it goes to the new dropoff, where it already stands.");
            _world.Advance(1);
            Assert.That(Units("farm-dock:out"), Is.EqualTo(4));
        }

        [Test]
        public void StepSizeDoesNotChangeTheOutcome()
        {
            Stock("crates", "farm", "crate", 20);
            Stock("apples", "farm", "apple", 7);
            Route("route-1");
            var stepwise = GoodsWorld.Restore(_world.Snapshot());
            stepwise.RegisterItem("crate", 10);
            stepwise.RegisterItem("apple", 10);
            _world.Advance(47);
            for (var second = 0; second < 47; second++) stepwise.Advance(1);
            string Shape(GoodsSnapshot state) => string.Join(";", state.Lots.OrderBy(x => x.LocationId).ThenBy(x => x.ItemId)
                .GroupBy(x => (x.LocationId, x.ItemId)).Select(x => $"{x.Key}:{x.Sum(y => y.Quantity)}"))
                + JsonUtility.ToJson(state.Trucks.Single());
            Assert.That(Shape(_world.Snapshot()), Is.EqualTo(Shape(stepwise.Snapshot())));
        }

        [Test]
        public void SiteViewCarriesTheCompanyFleetAndItsCargo()
        {
            Stock("crates", "farm", "crate", 4);
            Route("route-1");
            _world.Advance(1);
            var view = _world.View("boss", "shop");
            Assert.That(view.Locations[0].SiteId, Is.EqualTo("shop"), "The baseline still names its site first.");
            Assert.That(view.Trucks.Single().Id, Is.EqualTo("truck"));
            Assert.That(view.Lots.Where(x => x.LocationId == "truck:cargo").Sum(x => x.Quantity), Is.EqualTo(4));
            Assert.That(view.Sites.Select(x => x.Id), Is.EquivalentTo(new[] { "farm", "shop" }));
            Assert.That(_world.View("vendor", "stall").Trucks, Is.Empty, "Another company sees none of them.");
        }

        [Test]
        public void TenthSchemaSaveUpgradesWithoutSitesOrTrucks()
        {
            var plain = new GoodsWorld("old-world");
            plain.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "farm", Kind = "storage", Capacity = 1 });
            var oldPath = Path.Combine(_saveDirectory, "old.db");
            GoodsSnapshotStore.Save(plain, oldPath);
            var json = JsonUtility.ToJson(plain.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":10")
                .Replace(",\"Sites\":[]", "").Replace(",\"Trucks\":[]", "");
            Assert.That(json, Does.Not.Contain("Trucks"));
            SnapshotDatabase.WritePayload(oldPath, json);
            var upgraded = GoodsSnapshotStore.Load(oldPath).Snapshot();
            Assert.That((upgraded.SchemaVersion, upgraded.Sites.Count, upgraded.Trucks.Count), Is.EqualTo((GoodsSnapshot.CurrentSchema, 0, 0)));
        }

        [Test]
        public void ValidationRejectsInconsistentTrucks()
        {
            var state = _world.Snapshot();
            state.Trucks.Single().State = TruckState.Loading;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "Loading without a route.");
            state = _world.Snapshot();
            state.Locations.RemoveAll(x => x.Id == "truck:cargo");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "No cargo location.");
            state = _world.Snapshot();
            state.Grants.Add(new GoodsGrant { PlayerId = "boss", SiteId = GoodsWorld.RoadSiteId });
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "A grant on the road.");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(new GoodsTruck
            {
                Id = "bad", CompanyId = "co", Name = "Bad", CargoSlots = 1, SpeedMetresPerSecond = 1, LoadUnitsPerSecond = 1,
                State = TruckState.Parked, SiteId = "nowhere"
            }), "Unmapped site.");
            Assert.That(_world.Snapshot().Trucks.Count, Is.EqualTo(1), "A refused bootstrap changes nothing.");
        }
    }
}
