// Verifies trucks on the generated road network (decision 0032): with roads registered a truck drives legs along the quickest
// route and delivers; at rush hour a full segment makes a truck wait without losing anything; step size never changes the
// outcome; a redirected truck finishes its leg and reroutes; parking a driving truck takes it to the nearer route site; a trip
// survives a save and reload; v16 saves keep their abstract trips; other companies' trucks on the road are public in views
// without their route; inconsistent legs are rejected. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class RoadTruckTests
    {
        private GoodsWorld _world;
        private RoadNetwork _network;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private static readonly string RouteId = GoodsWorld.RouteIdFor("boss", "make-route");

        // TEST-ONLY: the 3 x 2 grid of RoadNetworkTests (100 m blocks, an arterial up the middle) inside one district. "co" owns
        // a farm by r0 (10 m from n0), a shop by r3 (90 m from n4) and a depot by r4 (halfway up the west street); "rival" owns
        // a stall. Docks hold 8 out and 8 in. Trucks carry 2 slots of up to 10 crates, drive 20 m/s, move 10 units a second.
        private static WorldLayout Grid(int trafficPercent)
        {
            var layout = new WorldLayout();
            var spots = new[] { (0, 0), (100, 0), (200, 0), (0, 100), (100, 100), (200, 100) };
            for (var i = 0; i < spots.Length; i++) layout.Nodes.Add(new RoadNode { Id = $"n{i}", X = spots[i].Item1, Z = spots[i].Item2 });
            foreach (var (id, from, to) in new[]
                     {
                         ("r0", "n0", "n1"), ("r1", "n1", "n2"), ("r2", "n3", "n4"), ("r3", "n4", "n5"), ("r4", "n0", "n3"),
                         ("r5", "n1", "n4"), ("r6", "n2", "n5")
                     })
            {
                var arterial = id == "r5";
                layout.Roads.Add(new RoadSegment
                {
                    Id = id, FromId = from, ToId = to, Kind = arterial ? RoadKind.Arterial : RoadKind.Local, Width = arterial ? 14 : 10,
                    CapacityPerHour = arterial ? 1800 : 600
                });
            }
            layout.Districts.Add(new WorldDistrict { Id = "test-district", TrafficPercent = trafficPercent, Areas = { new WorldRect(-10, -10, 220, 120) } });
            return layout;
        }

        private void Build(int trafficPercent = 90)
        {
            _world = new GoodsWorld("road-world");
            _world.RegisterItem("crate", 10);
            foreach (var (site, x, z) in new[] { ("farm", 10, 2), ("shop", 190, 98), ("depot", 2, 50), ("stall", 110, 2) })
            {
                _world.Bootstrap(new GoodsLocation { Id = site + "-storage", SiteId = site, Kind = "storage", Capacity = 10 });
                _world.Bootstrap(new SiteLayout { SiteId = site, Width = 6, Depth = 6 });
                _world.Bootstrap(new GoodsEquipment
                {
                    Id = site + "-dock", Kind = GoodsWorld.DockKind, SiteId = site, State = EquipmentState.Placed, HolderId = "",
                    Width = 2, Depth = 1, InputCapacity = 8, OutputCapacity = 8
                });
                _world.Bootstrap(new GoodsSite { Id = site, Name = site, MapX = x, MapZ = z });
            }
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 0, SiteIds = { "farm", "shop", "depot" } });
            _world.Bootstrap(new GoodsCompany { Id = "rival", Cash = 0, SiteIds = { "stall" } });
            _network = new RoadNetwork(Grid(trafficPercent));
            _world.RegisterRoads(_network);
            foreach (var site in new[] { "farm", "shop", "depot" }) Assert.That(_world.TryGrantDurably("boss", site, PathForSave), Is.True);
            Assert.That(_world.TryGrantDurably("vendor", "stall", PathForSave), Is.True);
        }

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryRoadTruckTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            Build();
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private void AddTruck(string id, string company = "co", string site = "farm") => _world.Bootstrap(new GoodsTruck
        {
            Id = id, CompanyId = company, Name = id, CargoSlots = 2, SpeedMetresPerSecond = 20, LoadUnitsPerSecond = 10,
            State = TruckState.Parked, SiteId = site
        });

        private void Stock(string id, int quantity, string site = "farm") => _world.Bootstrap(new GoodsLot
        {
            Id = id, ItemId = "crate", OwnerId = site, LocationId = site + "-dock:in", Quantity = quantity, SpoilAfterSeconds = 100_000
        });

        private void Route(string dropoff = "shop-dock", string request = "make-route") =>
            Assert.That(_world.CreateRouteDurably("boss", request, "farm-dock", dropoff, Array.Empty<string>(), PathForSave).Accepted, Is.True);

        private void Assign(string truck, string route = null) =>
            Assert.That(_world.AssignTruckDurably("boss", "assign-" + truck + "-" + (route ?? "park"), truck, route ?? "", PathForSave).Accepted, Is.True);

        private GoodsTruck Current(string id = "truck") => _world.Snapshot().Trucks.Single(x => x.Id == id);

        private int Units(string location) => _world.Snapshot().Lots.Where(x => x.LocationId == location).Sum(x => x.Quantity);

        // Advances one second at a time until the condition holds; returns the seconds taken.
        private long Until(Func<bool> condition, string what, long limit = 600)
        {
            for (var seconds = 0L; seconds <= limit; seconds++)
            {
                if (condition()) return seconds;
                _world.Advance(1);
            }
            Assert.Fail($"Timed out waiting for {what}.");
            return limit;
        }

        private void ToHour(int hour) => _world.Advance(hour * RoadTraffic.HourSeconds - _world.Snapshot().ClockSeconds % RoadTraffic.DaySeconds);

        [Test]
        public void ATruckDrivesTheQuickestRoadsAndDelivers()
        {
            ToHour(3);
            AddTruck("truck");
            Stock("crates", 15);
            Route();
            Assign("truck", RouteId);
            var segments = new System.Collections.Generic.List<string>();
            var trip = Until(() =>
            {
                var truck = Current();
                if (truck.OnRoad && (segments.Count == 0 || segments[^1] != truck.LegSegmentId)) segments.Add(truck.LegSegmentId);
                return truck.State == TruckState.Unloading;
            }, "arrival");
            Assert.That(segments, Is.EqualTo(new[] { "r0", "r5", "r3" }), "Along its street, up the arterial, along the shop's street.");
            Assert.That(trip, Is.GreaterThan(GoodsWorld.RoadSeconds(_world.Snapshot(), "farm", "shop", 20)),
                "The roads take longer than the abstract straight-grid trip at the truck's top speed.");
            Assert.That((Current().SiteId, Current().LegSegmentId), Is.EqualTo(("shop", "")));
            Until(() => Units("shop-dock:out") == 15, "unloading");
            Assert.That(Units("truck:cargo") + Units("farm-dock:in"), Is.Zero, "Nothing lost.");
        }

        [Test]
        public void AtRushHourAFullSegmentMakesTrucksQueueWithoutLosingGoods()
        {
            ToHour(8);
            foreach (var id in new[] { "truck-a", "truck-b", "truck-c" }) AddTruck(id);
            Stock("crates", 60);
            Route();
            foreach (var id in new[] { "truck-a", "truck-b", "truck-c" }) Assign(id, RouteId);
            var queued = false;
            Until(() =>
            {
                queued |= _world.Snapshot().Trucks.Any(x => x.OnRoad && x.QueuedSeconds > 0);
                return Units("shop-dock:out") >= 40;
            }, "deliveries");
            Assert.That(queued, Is.True, "Rush-hour city traffic plus the first truck fill the farm's street; the others wait.");
            var state = _world.Snapshot();
            Assert.That(state.Lots.Where(x => x.ItemId == "crate").Sum(x => x.Quantity), Is.EqualTo(60), "Nothing lost or duplicated.");
        }

        [Test]
        public void AtNightTheSameTrucksDoNotQueue()
        {
            ToHour(3);
            foreach (var id in new[] { "truck-a", "truck-b" }) AddTruck(id);
            Stock("crates", 40);
            Route();
            foreach (var id in new[] { "truck-a", "truck-b" }) Assign(id, RouteId);
            var queued = false;
            Until(() =>
            {
                queued |= _world.Snapshot().Trucks.Any(x => x.OnRoad && x.QueuedSeconds > 0);
                return Units("shop-dock:out") >= 40;
            }, "deliveries");
            Assert.That(queued, Is.False);
        }

        [Test]
        public void StepSizeDoesNotChangeTheOutcomeOnTheRoads()
        {
            ToHour(8);
            foreach (var id in new[] { "truck-a", "truck-b", "truck-c" }) AddTruck(id);
            Stock("crates", 60);
            Route();
            foreach (var id in new[] { "truck-a", "truck-b", "truck-c" }) Assign(id, RouteId);
            var stepwise = GoodsWorld.Restore(_world.Snapshot());
            stepwise.RegisterItem("crate", 10);
            stepwise.RegisterRoads(_network);
            _world.Advance(173);
            for (var second = 0; second < 173; second++) stepwise.Advance(1);
            string Shape(GoodsSnapshot state) => string.Join(";", state.Lots.OrderBy(x => x.LocationId).ThenBy(x => x.Id)
                .Select(x => $"{x.Id}@{x.LocationId}:{x.Quantity}")) + string.Join(";", state.Trucks.Select(JsonUtility.ToJson));
            Assert.That(Shape(_world.Snapshot()), Is.EqualTo(Shape(stepwise.Snapshot())));
        }

        [Test]
        public void ARedirectedTruckFinishesItsLegAndReroutes()
        {
            ToHour(3);
            AddTruck("truck");
            Stock("crates", 15);
            Route();
            Assign("truck", RouteId);
            Until(() => Current().LegSegmentId == "r5", "the arterial");
            Assert.That(_world.SetRouteDurably("boss", "to-depot", RouteId, "farm-dock", "depot-dock", Array.Empty<string>(), PathForSave).Accepted,
                Is.True);
            Assert.That((Current().LegSegmentId, Current().DestinationSiteId, Current().State),
                Is.EqualTo(("r5", "depot", TruckState.ToDropoff)), "It keeps driving the leg it is on.");
            Until(() => Current().LegSegmentId != "r5", "the next leg");
            Assert.That(Current().LegSegmentId, Is.EqualTo("r2"), "From the top of the arterial, west toward the depot.");
            Until(() => Units("depot-dock:out") == 15, "delivery at the depot");
        }

        [Test]
        public void ParkingADrivingTruckTakesItToTheNearerRouteSite()
        {
            ToHour(3);
            AddTruck("truck");
            Stock("crates", 15);
            Route();
            Assign("truck", RouteId);
            Until(() => Current().LegSegmentId == "r0" && Current().State == TruckState.ToDropoff, "leaving the farm");
            Assign("truck");
            Assert.That((Current().State, Current().DestinationSiteId, Current().RouteId),
                Is.EqualTo((TruckState.ToPark, "farm", "")), "From the end of the farm's street the farm is nearer than the shop.");
            Until(() => Current().State == TruckState.Parked, "parking");
            Assert.That((Current().SiteId, Units("truck:cargo")), Is.EqualTo(("farm", 15)), "Parked with its cargo aboard.");
            _world.Advance(30);
            Assert.That(Current().State, Is.EqualTo(TruckState.Parked));
        }

        [Test]
        public void ATripSurvivesASaveAndReload()
        {
            ToHour(8);
            foreach (var id in new[] { "truck-a", "truck-b" }) AddTruck(id);
            Stock("crates", 40);
            Route();
            foreach (var id in new[] { "truck-a", "truck-b" }) Assign(id, RouteId);
            Until(() => _world.Snapshot().Trucks.All(x => x.OnRoad), "both on the road");
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            loaded.RegisterItem("crate", 10);
            loaded.RegisterRoads(_network);
            Assert.That(loaded.Snapshot().Trucks.Select(JsonUtility.ToJson), Is.EqualTo(_world.Snapshot().Trucks.Select(JsonUtility.ToJson)));
            _world.Advance(90);
            loaded.Advance(90);
            Assert.That(loaded.Snapshot().Trucks.Select(JsonUtility.ToJson), Is.EqualTo(_world.Snapshot().Trucks.Select(JsonUtility.ToJson)));
            Assert.That(loaded.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(40));
        }

        [Test]
        public void SixteenthSchemaTrucksKeepTheirAbstractTrip()
        {
            var plain = new GoodsWorld("old-world");
            plain.RegisterItem("crate", 10);
            foreach (var site in new[] { "farm", "shop" })
            {
                plain.Bootstrap(new GoodsLocation { Id = site + "-storage", SiteId = site, Kind = "storage", Capacity = 1 });
                plain.Bootstrap(new GoodsSite { Id = site, Name = site, MapX = site == "farm" ? 0 : 100 });
            }
            plain.Bootstrap(new GoodsCompany { Id = "co", SiteIds = { "farm", "shop" } });
            plain.Bootstrap(new GoodsTruck
            {
                Id = "old", CompanyId = "co", Name = "old", CargoSlots = 1, SpeedMetresPerSecond = 10, LoadUnitsPerSecond = 1,
                State = TruckState.Parked, SiteId = "farm"
            });
            var path = Path.Combine(_saveDirectory, "old.db");
            GoodsSnapshotStore.Save(plain, path);
            var json = JsonUtility.ToJson(plain.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":16")
                .Replace(",\"RoadTrucks\":[]", "");
            json = System.Text.RegularExpressions.Regex.Replace(json, ",\"(LegSegmentId|LegFrom|LegTo|LegDriveSeconds|LegSeconds|QueuedSeconds|NextSegmentId|NextFrom|NextTo|NextFinal)\":(\"\"|0|false)", "");
            Assert.That(json, Does.Not.Contain("Leg").And.Not.Contain("NextSegmentId"));
            SnapshotDatabase.WritePayload(path, json);
            var upgraded = GoodsSnapshotStore.Load(path).Snapshot();
            Assert.That((upgraded.SchemaVersion, upgraded.Trucks.Single().LegSegmentId, upgraded.RoadTrucks.Count),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, "", 0)));
        }

        [Test]
        public void OtherCompaniesTrucksOnTheRoadArePublicWithoutTheirRoute()
        {
            ToHour(3);
            AddTruck("truck");
            AddTruck("rival-truck", "rival", "stall");
            Stock("crates", 15);
            Route();
            Assign("truck", RouteId);
            Until(() => Current().OnRoad, "on the road");
            var view = _world.View("vendor", "stall");
            var seen = view.RoadTrucks.Single();
            Assert.That((seen.Id, seen.RouteId, seen.LegSegmentId), Is.EqualTo(("truck", "", Current().LegSegmentId)));
            Assert.That(view.Trucks.Select(x => x.Id), Is.EqualTo(new[] { "rival-truck" }), "Its own fleet stays in Trucks.");
            Assert.That(view.Lots.Any(x => x.LocationId == "truck:cargo"), Is.False, "Never another company's cargo.");
            Assert.That(_world.View("boss", "farm").RoadTrucks, Is.Empty, "A company's own trucks are not repeated.");
            Assert.That(_world.Snapshot().RoadTrucks, Is.Empty, "Never stored.");
        }

        [Test]
        public void InconsistentLegsAreRejected()
        {
            AddTruck("truck");
            var state = _world.Snapshot();
            var truck = state.Trucks.Single();
            truck.LegSegmentId = "r0";
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(state), "A parked truck has no leg.");
            truck.State = TruckState.ToPark;
            truck.DestinationSiteId = "farm";
            truck.LegFrom = 10;
            truck.LegTo = 100;
            truck.LegDriveSeconds = 5;
            truck.LegSeconds = 4;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(state), "The whole leg includes its driving.");
            truck.LegSeconds = 5;
            truck.RemainingSeconds = 3;
            var restored = GoodsWorld.Restore(state);
            truck.LegSegmentId = "nowhere";
            var unknown = GoodsWorld.Restore(state);
            Assert.Throws<InvalidOperationException>(() => unknown.RegisterRoads(_network), "A leg must be on the network.");
            Assert.DoesNotThrow(() => restored.RegisterRoads(_network));
            state.RoadTrucks.Add(new GoodsTruck { Id = "seen" });
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(state), "View-only trucks are never stored.");
        }
    }
}
