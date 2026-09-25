// Verifies trucks and routes (decisions 0022, 0023): a truck on a route loads from the pickup dock's outgoing buffer at its
// rate, drives the road time, unloads into the dropoff dock's incoming buffer re-owned by that site, and returns; several
// trucks share a route and its docks; cargo ages in transit; a full buffer or a missing dock makes a truck wait without losing
// goods; nobody reaches cargo except the truck; route commands are validated, replayed and rolled back on a failed commit;
// deleting a route or parking a truck keeps its cargo; trucks are bought once; step size never changes the outcome; routes and
// trucks survive a reload, and v10 and v11 saves upgrade. Isolated saves only.
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
        private static readonly string RouteId = GoodsWorld.RouteIdFor("boss", "make-route");

        // TEST-ONLY values: "co" owns a farm at the map origin and a shop 30 m away by road (20 east, 10 north) and $500.00;
        // "rival" owns a stall. Each site has a 6x6 grid and a 2x1 dock with 3 outgoing and 2 incoming slots. Crates stack to
        // 10. The truck has 2 cargo slots, drives 10 m/s (3 s a trip) and moves 4 units a second; the offered van costs
        // $250.00. Not gameplay content.
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
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 50_000, SiteIds = { "farm", "shop" } });
            _world.Bootstrap(new GoodsCompany { Id = "rival", Cash = 0, SiteIds = { "stall" } });
            _world.Bootstrap(Truck("truck"));
            _world.RegisterTruckOffer(new TruckOffer
            {
                Id = "van-offer", PriceCents = 25_000, Name = "Van", CargoSlots = 3, SpeedMetresPerSecond = 5, LoadUnitsPerSecond = 2
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

        private static GoodsTruck Truck(string id, string site = "farm") => new()
        {
            Id = id, CompanyId = "co", Name = id, CargoSlots = 2, SpeedMetresPerSecond = 10, LoadUnitsPerSecond = 4,
            State = TruckState.Parked, SiteId = site
        };

        private void Stock(string id, string site, string item, int quantity, long exposure = 0, long spoilAfter = 1000) =>
            _world.Bootstrap(new GoodsLot
            {
                Id = id, ItemId = item, OwnerId = site, LocationId = site + "-dock:in", Quantity = quantity, ExposureSeconds = exposure,
                SpoilAfterSeconds = spoilAfter
            });

        private GoodsOutcome Create(string request = "make-route", string pickup = "farm-dock", string dropoff = "shop-dock",
            string player = "boss", params string[] items) => _world.CreateRouteDurably(player, request, pickup, dropoff, items, PathForSave);

        private GoodsOutcome Assign(string request, string route, string truck = "truck", string player = "boss") =>
            _world.AssignTruckDurably(player, request, truck, route, PathForSave);

        // The farm-to-shop route RouteId with "truck" on it.
        private void Routed(params string[] items)
        {
            Assert.That(Create(items: items).Accepted, Is.True);
            Assert.That(Assign("put-truck", RouteId).Accepted, Is.True);
        }

        private GoodsTruck Current(string id = "truck") => _world.Snapshot().Trucks.Single(x => x.Id == id);
        private long Cash() => _world.Snapshot().Companies.Single(x => x.Id == "co").Cash;
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
            var created = Create();
            Assert.That((created.Accepted, created.Reason), Is.EqualTo((true, "route-created")));
            Assert.That(_world.Snapshot().Routes.Single().Id, Is.EqualTo(RouteId));
            Assert.That(Current().State, Is.EqualTo(TruckState.Parked), "A new route has no trucks.");
            var assigned = Assign("put-truck", RouteId);
            Assert.That((assigned.Accepted, assigned.Reason), Is.EqualTo((true, "assigned")));
            Assert.That((Current().State, Current().SiteId), Is.EqualTo((TruckState.Loading, "farm")), "Already at the pickup site.");

            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Units("farm-dock:in")), Is.EqualTo((4, 2)), "Loads its rate each second.");
            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Current().State), Is.EqualTo((6, TruckState.Loading)));
            _world.Advance(1);
            Assert.That((Current().State, Current().DestinationSiteId, Current().RemainingSeconds),
                Is.EqualTo((TruckState.ToDropoff, "shop", 3L)), "Nothing more to load: it leaves with its cargo.");
            _world.Advance(3);
            Assert.That((Current().State, Current().SiteId), Is.EqualTo((TruckState.Unloading, "shop")));
            _world.Advance(2);
            Assert.That((Units("truck:cargo"), Units("shop-dock:out")), Is.EqualTo((0, 6)));
            Assert.That(_world.Snapshot().Lots.Where(x => x.LocationId == "shop-dock:out").All(x => x.OwnerId == "shop"), Is.True,
                "Unloaded goods belong to the dropoff site.");
            Assert.That((Current().State, Current().DestinationSiteId), Is.EqualTo((TruckState.ToPickup, "farm")), "Empty: back to the pickup.");
            _world.Advance(3);
            Assert.That(Current().State, Is.EqualTo(TruckState.Loading));
            _world.Advance(5);
            Assert.That(Current().State, Is.EqualTo(TruckState.Loading), "An empty truck waits at an empty dock.");
        }

        [Test]
        public void SeveralTrucksShareARouteAndItsDocks()
        {
            _world.Bootstrap(Truck("truck-2", "shop"));
            Stock("crates", "farm", "crate", 16);
            Routed();
            Assert.That(Assign("put-truck-2", RouteId, "truck-2").Accepted, Is.True);
            Assert.That((Current("truck-2").State, Current("truck-2").DestinationSiteId), Is.EqualTo((TruckState.ToPickup, "farm")),
                "Empty, so it drives to the pickup.");
            _world.Advance(1);
            Assert.That(Units("truck:cargo"), Is.EqualTo(4));
            _world.Advance(2);
            Assert.That((Units("truck:cargo"), Current("truck-2").State), Is.EqualTo((12, TruckState.Loading)),
                "The first truck loaded 4 a second; the second has arrived.");
            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Units("truck-2:cargo")), Is.EqualTo((16, 0)), "The first truck, earlier by ID, took the last units.");
            _world.Advance(30);
            Assert.That((Units("shop-dock:out"), Units("truck:cargo") + Units("truck-2:cargo") + Units("farm-dock:in")), Is.EqualTo((16, 0)));
            Assert.That(_world.Snapshot().Routes.Single().Id, Is.EqualTo(RouteId), "One route serves both.");
        }

        [Test]
        public void FullCargoLeavesTheRestAtTheDock()
        {
            // Crates are older, so they load first (most exposed first) and fill both slots.
            Stock("crates", "farm", "crate", 20, exposure: 10);
            Stock("apples", "farm", "apple", 5);
            Routed();
            _world.Advance(10);
            Assert.That(Units("truck:cargo") + Units("shop-dock:out"), Is.EqualTo(20), "Two slots of ten: the truck took what fits.");
            Assert.That(Units("farm-dock:in"), Is.EqualTo(5));
        }

        [Test]
        public void CargoFilterLoadsOnlyAllowedItems()
        {
            Stock("crates", "farm", "crate", 3);
            Stock("apples", "farm", "apple", 3);
            Routed("apple");
            _world.Advance(1);
            Assert.That((Units("truck:cargo", "apple"), Units("truck:cargo", "crate")), Is.EqualTo((3, 0)));
        }

        [Test]
        public void GoodsAgeInTransitAndCanSpoil()
        {
            Stock("apples", "farm", "apple", 2, exposure: 995, spoilAfter: 1000);
            Routed();
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
            Routed();
            _world.Advance(30);
            Assert.That((Current().State, Units("truck:cargo")), Is.EqualTo((TruckState.Unloading, 10)), "The incoming buffer is full.");
            Assert.That(_world.TransferDurably("boss", new TransferIntent { RequestId = "clear", LotId = "blocker", DestinationId = "shop-storage", Quantity = 20 },
                PathForSave).Accepted, Is.True);
            // With the dock picked up, the truck waits for it to come back.
            Assert.That(_world.TryGrantDurably("porter", "shop", PathForSave, 5), Is.True);
            Assert.That(_world.PickUpDurably("porter", "lift-dock", "shop-dock", PathForSave).Accepted, Is.True);
            _world.Advance(5);
            Assert.That((Current().State, Units("truck:cargo")), Is.EqualTo((TruckState.Unloading, 10)), "No dock: it waits, cargo aboard.");
            Assert.That(_world.PlaceDurably("porter", "put-dock", "shop-dock", 0, 0, 0, PathForSave).Accepted, Is.True);
            _world.Advance(3);
            Assert.That((Units("truck:cargo"), Units("shop-dock:out")), Is.EqualTo((0, 10)));
            Assert.That(_world.Snapshot().Lots.Where(x => x.ItemId == "crate").Sum(x => x.Quantity), Is.EqualTo(10), "Nothing lost or duplicated.");
        }

        [Test]
        public void NobodyButTheTruckReachesItsCargo()
        {
            Stock("crates", "farm", "crate", 4);
            Routed();
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
        public void RouteCommandsAreValidatedAndChangeNothingWhenRefused()
        {
            Assert.That(Create().Accepted, Is.True);
            var revision = _world.Snapshot().Revision;
            Assert.That(Create("r1", pickup: "farm-storage").Reason, Is.EqualTo("invalid-dock"));
            Assert.That(Create("r2", player: "farmer").Reason, Is.EqualTo("forbidden"), "Needs a grant on both sites.");
            Assert.That(Create("r3", pickup: "stall-dock", player: "vendor").Reason, Is.EqualTo("forbidden"), "No grant on the shop.");
            Assert.That(Create("r4", dropoff: "farm-dock").Reason, Is.EqualTo("same-site"));
            Assert.That(Create("r5", items: new[] { "apple", "apple" }).Reason, Is.EqualTo("invalid-cargo"));
            Assert.That(Create("r6", items: new[] { " " }).Reason, Is.EqualTo("invalid-cargo"));
            Assert.That(_world.SetRouteDurably("boss", "r7", "ghost", "farm-dock", "shop-dock", null, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.SetRouteDurably("boss", "r8", RouteId, "farm-dock", "stall-dock", null, PathForSave).Reason, Is.EqualTo("forbidden"),
                "Needs a grant on the stall.");
            Assert.That(_world.SetRouteDurably("farmer", "r9", RouteId, "shop-dock", "farm-dock", null, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.DeleteRouteDurably("farmer", "r10", RouteId, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.DeleteRouteDurably("boss", "r11", "ghost", PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(Assign("r12", RouteId, "ghost").Reason, Is.EqualTo("forbidden"));
            Assert.That(Assign("r13", "ghost").Reason, Is.EqualTo("forbidden"));
            Assert.That(Assign("r14", RouteId, player: "farmer").Reason, Is.EqualTo("forbidden"), "Needs a grant on both dock sites.");
            Assert.That(Assign("r15", "", player: "vendor").Reason, Is.EqualTo("forbidden"), "Another company's truck.");
            Assert.That((_world.Snapshot().Revision, Current().State), Is.EqualTo((revision, TruckState.Parked)), "Rejections are not recorded.");
        }

        [Test]
        public void AcceptedRouteCommandsReplayAndSurviveReload()
        {
            var first = Create(items: "crate");
            var again = Create(pickup: "shop-dock", dropoff: "farm-dock");
            Assert.That((again.Accepted, again.Revision), Is.EqualTo((true, first.Revision)), "Replayed, not applied again.");
            Assert.That(_world.Snapshot().Routes.Single().PickupDockId, Is.EqualTo("farm-dock"));
            Assert.That(Assign("put-truck", RouteId).Accepted, Is.True);
            var reloaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var route = reloaded.Routes.Single();
            Assert.That((route.Id, route.CompanyId, route.PickupDockId, route.DropoffDockId, route.AllowedItemIds.Single()),
                Is.EqualTo((RouteId, "co", "farm-dock", "shop-dock", "crate")));
            Assert.That((reloaded.Trucks.Single().RouteId, reloaded.Trucks.Single().State), Is.EqualTo((RouteId, TruckState.Loading)));
        }

        [Test]
        public void FailedCommitLeavesRoutesAndTrucksUnchanged()
        {
            var outcome = _world.CreateRouteDurably("boss", "make-route", "farm-dock", "shop-dock", null, BadPath);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That(_world.Snapshot().Routes, Is.Empty);
            Routed();
            Assert.That(_world.AssignTruckDurably("boss", "park", "truck", "", BadPath).Accepted, Is.False);
            Assert.That(_world.DeleteRouteDurably("boss", "drop", RouteId, BadPath).Accepted, Is.False);
            Assert.That((Current().RouteId, _world.Snapshot().Routes.Count), Is.EqualTo((RouteId, 1)));
        }

        [Test]
        public void EditedRouteRedirectsItsLoadedTruck()
        {
            Stock("crates", "farm", "crate", 4);
            Routed();
            _world.Advance(1);
            var edited = _world.SetRouteDurably("boss", "reverse", RouteId, "shop-dock", "farm-dock", null, PathForSave);
            Assert.That((edited.Accepted, edited.Reason), Is.EqualTo((true, "route-set")));
            Assert.That((Current().State, Current().SiteId), Is.EqualTo((TruckState.Unloading, "farm")),
                "It carries cargo, so it goes to the new dropoff, where it already stands.");
            _world.Advance(1);
            Assert.That(Units("farm-dock:out"), Is.EqualTo(4));
        }

        [Test]
        public void DeletingARouteParksItsTrucksWithTheirCargo()
        {
            Stock("crates", "farm", "crate", 4);
            Routed();
            _world.Advance(1);
            var deleted = _world.DeleteRouteDurably("boss", "drop", RouteId, PathForSave);
            Assert.That((deleted.Accepted, deleted.Reason), Is.EqualTo((true, "route-deleted")));
            Assert.That((_world.Snapshot().Routes.Count, Current().State, Current().RouteId, Units("truck:cargo")),
                Is.EqualTo((0, TruckState.Parked, "", 4)), "Parked with its cargo aboard.");
            _world.Advance(10);
            Assert.That(Units("truck:cargo"), Is.EqualTo(4), "A parked truck does nothing.");

            // On a new route it delivers what it carries first.
            Assert.That(Create("back", "shop-dock", "farm-dock").Accepted, Is.True);
            Assert.That(Assign("put-back", GoodsWorld.RouteIdFor("boss", "back")).Accepted, Is.True);
            _world.Advance(1);
            Assert.That((Units("truck:cargo"), Units("farm-dock:out")), Is.EqualTo((0, 4)));
        }

        [Test]
        public void ParkingADrivingTruckLeavesItAtTheSiteItLeft()
        {
            Stock("crates", "farm", "crate", 4);
            Routed();
            _world.Advance(2);
            Assert.That(Current().State, Is.EqualTo(TruckState.ToDropoff));
            var remaining = Current().RemainingSeconds;
            Assert.That(Assign("same", RouteId).Accepted, Is.True);
            Assert.That((Current().State, Current().RemainingSeconds), Is.EqualTo((TruckState.ToDropoff, remaining)),
                "Assigning the route it already has does not restart it.");
            var parked = Assign("park", "", player: "farmer");
            Assert.That((parked.Accepted, parked.Reason), Is.EqualTo((true, "parked")), "Any grant on a company site may park it.");
            Assert.That((Current().State, Current().SiteId, Current().DestinationSiteId, Current().RemainingSeconds, Units("truck:cargo")),
                Is.EqualTo((TruckState.Parked, "farm", "", 0L, 4)));
            Assert.That(_world.Snapshot().Routes.Single().Id, Is.EqualTo(RouteId), "The route stays.");
        }

        [Test]
        public void BuyingATruckChargesOnceAndParksItAtTheSite()
        {
            var bought = _world.BuyDurably("boss", "buy-1", "shop", "van-offer", PathForSave);
            Assert.That((bought.Accepted, bought.Reason, bought.EquipmentId), Is.EqualTo((true, "bought", "buy:boss:buy-1")));
            var van = Current("buy:boss:buy-1");
            Assert.That((van.Name, van.CompanyId, van.State, van.SiteId, van.CargoSlots, van.SpeedMetresPerSecond, van.LoadUnitsPerSecond),
                Is.EqualTo(("Van 2", "co", TruckState.Parked, "shop", 3, 5, 2)));
            Assert.That(_world.Snapshot().Locations.Single(x => x.Id == van.CargoLocationId).Capacity, Is.EqualTo(3));
            Assert.That(Cash(), Is.EqualTo(25_000));
            Assert.That(_world.BuyDurably("boss", "buy-1", "shop", "van-offer", PathForSave).Revision, Is.EqualTo(bought.Revision), "Replayed.");
            Assert.That(Cash(), Is.EqualTo(25_000), "Charged once.");

            // It works like any other truck.
            Assert.That(Create().Accepted, Is.True);
            Assert.That(Assign("put-van", RouteId, van.Id).Accepted, Is.True);
            Assert.That(Current(van.Id).State, Is.EqualTo(TruckState.ToPickup));

            Assert.That(_world.BuyDurably("boss", "buy-2", "farm", "van-offer", PathForSave).Accepted, Is.True);
            Assert.That(_world.BuyDurably("boss", "buy-3", "farm", "van-offer", PathForSave).Reason, Is.EqualTo("insufficient-funds"));
            _world.Bootstrap(new GoodsLocation { Id = "yard-storage", SiteId = "yard", Kind = "storage", Capacity = 1 });
            _world.AddCompanySite("co", "yard");
            Assert.That(_world.TryGrantDurably("boss", "yard", PathForSave), Is.True);
            Assert.That(_world.BuyDurably("boss", "buy-4", "yard", "van-offer", PathForSave).Reason, Is.EqualTo("no-road"), "An unmapped site.");
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Trucks.Count, Is.EqualTo(3));
        }

        [Test]
        public void StepSizeDoesNotChangeTheOutcome()
        {
            _world.Bootstrap(Truck("truck-2", "shop"));
            Stock("crates", "farm", "crate", 20);
            Stock("apples", "farm", "apple", 7);
            Routed();
            Assert.That(Assign("put-truck-2", RouteId, "truck-2").Accepted, Is.True);
            var stepwise = GoodsWorld.Restore(_world.Snapshot());
            stepwise.RegisterItem("crate", 10);
            stepwise.RegisterItem("apple", 10);
            _world.Advance(47);
            for (var second = 0; second < 47; second++) stepwise.Advance(1);
            string Shape(GoodsSnapshot state) => string.Join(";", state.Lots.OrderBy(x => x.LocationId).ThenBy(x => x.ItemId)
                .GroupBy(x => (x.LocationId, x.ItemId)).Select(x => $"{x.Key}:{x.Sum(y => y.Quantity)}"))
                + string.Join(";", state.Trucks.Select(JsonUtility.ToJson));
            Assert.That(Shape(_world.Snapshot()), Is.EqualTo(Shape(stepwise.Snapshot())));
        }

        [Test]
        public void SiteViewCarriesTheCompanyRoutesFleetAndCargo()
        {
            Stock("crates", "farm", "crate", 4);
            Routed();
            _world.Advance(1);
            var view = _world.View("boss", "shop");
            Assert.That(view.Locations[0].SiteId, Is.EqualTo("shop"), "The baseline still names its site first.");
            Assert.That(view.Trucks.Single().Id, Is.EqualTo("truck"));
            Assert.That(view.Routes.Single().Id, Is.EqualTo(RouteId));
            Assert.That(view.Lots.Where(x => x.LocationId == "truck:cargo").Sum(x => x.Quantity), Is.EqualTo(4));
            Assert.That(view.Sites.Select(x => x.Id), Is.EquivalentTo(new[] { "farm", "shop" }));
            var rival = _world.View("vendor", "stall");
            Assert.That((rival.Trucks.Count, rival.Routes.Count), Is.EqualTo((0, 0)), "Another company sees none of them.");
        }

        [Test]
        public void TenthSchemaSaveUpgradesWithoutSitesOrTrucks()
        {
            var plain = new GoodsWorld("old-world");
            plain.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "farm", Kind = "storage", Capacity = 1 });
            var oldPath = Path.Combine(_saveDirectory, "old.db");
            GoodsSnapshotStore.Save(plain, oldPath);
            var json = JsonUtility.ToJson(plain.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":10")
                .Replace(",\"Sites\":[]", "").Replace(",\"Trucks\":[]", "").Replace(",\"Routes\":[]", "");
            Assert.That(json, Does.Not.Contain("Trucks"));
            SnapshotDatabase.WritePayload(oldPath, json);
            var upgraded = GoodsSnapshotStore.Load(oldPath).Snapshot();
            Assert.That((upgraded.SchemaVersion, upgraded.Sites.Count, upgraded.Trucks.Count, upgraded.Routes.Count),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, 0, 0, 0)));
        }

        [Test]
        public void EleventhSchemaTruckRoutesBecomeRouteRecords()
        {
            _world.Bootstrap(Truck("truck-2", "shop"));
            GoodsSnapshotStore.Save(_world, PathForSave);
            // A v11 payload: the loading truck carries its own route; the other is parked.
            var json = JsonUtility.ToJson(_world.Snapshot());
            var parkedAtFarm = $"\"RouteId\":\"\",\"State\":{(int)TruckState.Parked},\"SiteId\":\"farm\"";
            Assert.That(json, Does.Contain(parkedAtFarm));
            json = json.Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":11").Replace(",\"Routes\":[]", "")
                .Replace(parkedAtFarm, "\"PickupDockId\":\"farm-dock\",\"DropoffDockId\":\"shop-dock\",\"AllowedItemIds\":[\"crate\"],"
                    + $"\"State\":{(int)TruckState.Loading},\"SiteId\":\"farm\"")
                .Replace("\"RouteId\":\"\",", "\"PickupDockId\":\"\",\"DropoffDockId\":\"\",\"AllowedItemIds\":[],");
            Assert.That(json, Does.Not.Contain("RouteId"));
            SnapshotDatabase.WritePayload(PathForSave, json);
            var upgraded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var route = upgraded.Routes.Single();
            Assert.That((route.Id, route.CompanyId, route.PickupDockId, route.DropoffDockId, route.AllowedItemIds.Single()),
                Is.EqualTo(("route:truck", "co", "farm-dock", "shop-dock", "crate")));
            Assert.That((upgraded.Trucks.Single(x => x.Id == "truck").RouteId, upgraded.Trucks.Single(x => x.Id == "truck").State),
                Is.EqualTo(("route:truck", TruckState.Loading)));
            Assert.That((upgraded.Trucks.Single(x => x.Id == "truck-2").RouteId, upgraded.Trucks.Single(x => x.Id == "truck-2").State),
                Is.EqualTo(("", TruckState.Parked)));
        }

        [Test]
        public void ValidationRejectsInconsistentRoutesAndTrucks()
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
            Routed();
            state = _world.Snapshot();
            state.Routes.Single().CompanyId = "rival";
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "A route of a company that owns neither dock.");
            state = _world.Snapshot();
            state.Trucks.Single().RouteId = "ghost";
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "A truck on a missing route.");
            state = _world.Snapshot();
            state.Trucks.Single().State = TruckState.Parked;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(state), "Parked on a route.");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(new GoodsTruck
            {
                Id = "bad", CompanyId = "co", Name = "Bad", CargoSlots = 1, SpeedMetresPerSecond = 1, LoadUnitsPerSecond = 1,
                State = TruckState.Parked, SiteId = "nowhere"
            }), "Unmapped site.");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(new GoodsRoute
            {
                Id = "bad", CompanyId = "co", PickupDockId = "farm-dock", DropoffDockId = "farm-dock"
            }), "Same site.");
            Assert.That((_world.Snapshot().Trucks.Count, _world.Snapshot().Routes.Count), Is.EqualTo((1, 1)), "A refused bootstrap changes nothing.");
        }
    }
}
