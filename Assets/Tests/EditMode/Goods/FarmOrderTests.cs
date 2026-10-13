// Verifies farm orders (decision 0047): farms take their types in lot order; a delivery pays goods and the flat fee once and a
// supplier truck brings the goods fresh from the farm's gate to the dock and leaves; a pickup sends a parked company truck that
// loads at the gate and parks at the dock's site; owning the farm halves the goods price; a dock that cannot take the goods
// times out and refunds exactly what was not delivered; rejections change nothing; recurring orders run on the game hour and
// stop when cancelled; open orders survive a reload and a v25 save upgrades; sites are renamed only by their company's players.
// Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class FarmOrderTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        // TEST-ONLY values, not gameplay content: "co" owns "shop" (map origin) with $500.00 and a 2x1 dock whose incoming buffer has
        // 2 slots; milk and beef stack to 10. Milk is $10.00 for 5 (lasts 600 s), beef $30.00 for 5. Two farms 100 m east: farm-a is
        // the dairy (first in lot order), farm-b the cattle farm. The company truck has 2 cargo slots and drives 10 m/s.
        private const long StartCash = 50_000;
        private const long MilkPack = 1000;

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryFarmOrderTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            _world = CreateWorld();
            Assert.That(_world.TryGrantDurably("boss", "shop", PathForSave), Is.True);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            Register(world);
            world.Bootstrap(new GoodsLocation { Id = "shop-storage", SiteId = "shop", Kind = "storage", Capacity = 4 });
            world.Bootstrap(new SiteLayout { SiteId = "shop", Width = 6, Depth = 6 });
            world.Bootstrap(new GoodsEquipment
            {
                Id = "shop-dock", Kind = GoodsWorld.DockKind, SiteId = "shop", State = EquipmentState.Placed, HolderId = "", Width = 2, Depth = 1,
                InputCapacity = 2, OutputCapacity = 2
            });
            world.Bootstrap(new GoodsSite { Id = "shop", Name = "Shop" });
            world.Bootstrap(new GoodsCompany { Id = "co", Cash = StartCash, SiteIds = { "shop" } });
            world.Bootstrap(new GoodsTruck
            {
                Id = "truck", CompanyId = "co", Name = "Truck 1", CargoSlots = 2, SpeedMetresPerSecond = 10, LoadUnitsPerSecond = 4,
                State = TruckState.Parked, SiteId = "shop"
            });
            return world;
        }

        // Content the server registers at every start.
        private static void Register(GoodsWorld world)
        {
            world.RegisterItem("milk", 10);
            world.RegisterItem("beef", 10);
            world.RegisterOffer(new PurchaseOffer { Id = "supplier-milk-5", ItemId = "milk", Quantity = 5, PriceCents = MilkPack, SpoilAfterSeconds = 600 });
            world.RegisterOffer(new PurchaseOffer { Id = "supplier-beef-5", ItemId = "beef", Quantity = 5, PriceCents = 3000, SpoilAfterSeconds = 600 });
            world.RegisterPropertyOffers(new[] { Farm("farm-b", 120), Farm("farm-a", 100) });
        }

        private static PropertyOffer Farm(string name, int x) => new()
        {
            LotId = "lot-" + name, SiteId = "site-" + name, BuildingId = name, Category = GoodsWorld.FarmCategory, ForSale = true, PriceCents = 1000,
            LotX = x, LotZ = 10, Width = 10, Depth = 10, AccessX = x, AccessZ = 0, BuildingX = 0, BuildingZ = 0, BuildingWidth = 10, BuildingDepth = 6,
            Doors = { new GridCell { X = 5, Z = 5 } }
        };

        private GoodsOutcome Order(string request, int packs = 1, string truck = "", string offer = "supplier-milk-5", string farm = "lot-farm-a",
            string dock = "shop-dock", string player = "boss") =>
            _world.OrderFromFarmDurably(player, request, farm, offer, packs, dock, truck, PathForSave);

        private long Cash() => _world.Snapshot().Companies.Single().Cash;
        private int Units(string location) => _world.Snapshot().Lots.Where(x => x.LocationId == location).Sum(x => x.Quantity);

        // Steps a second at a time until the condition holds (or fails after max seconds).
        private void AdvanceUntil(Func<GoodsSnapshot, bool> done, int max = 600)
        {
            for (var second = 0; second < max; second++)
            {
                if (done(_world.Snapshot())) return;
                _world.Advance(1);
            }
            Assert.Fail("The condition never held.");
        }

        [Test]
        public void FarmsTakeTheTypesInTurnInLotOrder()
        {
            var offers = Enumerable.Range(0, 8).Select(x => Farm($"f{x}", x * 20)).Reverse().ToList();
            var kinds = FarmCatalog.KindsOf(offers);
            Assert.That(kinds["lot-f0"], Is.EqualTo(FarmKind.Dairy));
            Assert.That(kinds["lot-f5"], Is.EqualTo(FarmKind.Fishery));
            Assert.That(kinds["lot-f6"], Is.EqualTo(FarmKind.Dairy), "The types repeat after the sixth farm.");
            Assert.That(FarmCatalog.Kinds.SelectMany(FarmCatalog.Items).Distinct().Count(), Is.EqualTo(11), "Every raw ingredient comes from some farm.");
        }

        [Test]
        public void ADeliveryPaysOnceAndASupplierTruckBringsTheGoodsFreshAndLeaves()
        {
            var placed = Order("milk", packs: 2);
            Assert.That((placed.Accepted, placed.Reason, placed.Cents), Is.EqualTo((true, "ordered", 2 * MilkPack + GoodsWorld.FarmDeliveryFeeCents)));
            Assert.That(Order("milk", packs: 2).Revision, Is.EqualTo(placed.Revision), "A retried request replays and never pays twice.");
            Assert.That(Cash(), Is.EqualTo(StartCash - 2 * MilkPack - GoodsWorld.FarmDeliveryFeeCents));
            var state = _world.Snapshot();
            Assert.That(state.Ledger.Select(x => (x.Kind, x.Cents)), Is.EqualTo(new[]
            {
                (GoodsWorld.LedgerFarmGoods, -2 * MilkPack), (GoodsWorld.LedgerFarmDelivery, -GoodsWorld.FarmDeliveryFeeCents)
            }));
            var truck = state.Trucks.Single(x => x.Supplier);
            Assert.That((truck.State, truck.SiteId, truck.DestinationSiteId, truck.Name),
                Is.EqualTo((TruckState.ToDropoff, FarmCatalog.GateSiteId("lot-farm-a"), "shop", GoodsWorld.SupplierTruckName)));
            Assert.That(state.Locations.Single(x => x.Id == truck.CargoLocationId).Refrigerated, Is.True, "Supplier goods do not age on the truck.");
            Assert.That(Units(truck.CargoLocationId), Is.EqualTo(10));
            Assert.That(_world.HasSite("site-farm-a"), Is.False, "The gate is not the farm's own site, so the farm can still be bought.");

            AdvanceUntil(x => x.FarmOrders.Count == 0);
            var after = _world.Snapshot();
            var delivered = after.Lots.Where(x => x.LocationId == "shop-dock:out").ToList();
            Assert.That((delivered.Sum(x => x.Quantity), delivered.All(x => x.OwnerId == "shop")), Is.EqualTo((10, true)));
            Assert.That(delivered.Max(x => x.ExposureSeconds), Is.LessThan(30), "The goods age only from unloading on.");
            Assert.That(after.Trucks.Select(x => x.Id), Is.EqualTo(new[] { "truck" }), "The supplier truck leaves the world at its farm.");
            Assert.That(after.Locations.Any(x => x.SiteId == GoodsWorld.RoadSiteId && x.Id != "truck:cargo"), Is.False);
            Assert.That(Cash(), Is.EqualTo(StartCash - 2 * MilkPack - GoodsWorld.FarmDeliveryFeeCents), "Nothing is refunded for a full delivery.");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(after));
        }

        [Test]
        public void APickupSendsTheParkedTruckWhichParksAtTheDockSite()
        {
            var placed = Order("pickup", packs: 2, truck: "truck");
            Assert.That((placed.Accepted, placed.Cents), Is.EqualTo((true, 2 * MilkPack)), "A pickup pays no delivery fee.");
            var truck = _world.Snapshot().Trucks.Single();
            Assert.That((truck.State, truck.OrderId, truck.DestinationSiteId), Is.EqualTo((TruckState.ToPickup, "order:boss:pickup", FarmCatalog.GateSiteId("lot-farm-a"))));
            Assert.That(_world.AssignTruckDurably("boss", "reroute", "truck", "", PathForSave).Reason, Is.EqualTo("on-order"), "It finishes the order first.");
            Assert.That(Order("again", truck: "truck").Reason, Is.EqualTo("truck-busy"));

            AdvanceUntil(x => x.Trucks.Single().State == TruckState.ToDropoff);
            Assert.That(Units("truck:cargo"), Is.EqualTo(10), "The goods are made at the farm when the truck loads.");
            AdvanceUntil(x => x.FarmOrders.Count == 0);
            var after = _world.Snapshot().Trucks.Single();
            Assert.That((after.State, after.SiteId, after.OrderId, after.Docked), Is.EqualTo((TruckState.Parked, "shop", "", false)));
            Assert.That(Units("shop-dock:out"), Is.EqualTo(10));
            Assert.DoesNotThrow(() => GoodsWorld.Validate(_world.Snapshot()));
        }

        [Test]
        public void OwningTheFarmHalvesTheGoodsPrice()
        {
            Assert.That(_world.BuyPropertyDurably("boss", "buy-farm", "shop", "lot-farm-a", PathForSave).Accepted, Is.True);
            var cash = Cash();
            var placed = Order("owned", packs: 2);
            Assert.That(placed.Cents, Is.EqualTo(MilkPack + GoodsWorld.FarmDeliveryFeeCents), "Half the goods, the whole fee.");
            Assert.That(Cash(), Is.EqualTo(cash - placed.Cents));
        }

        [Test]
        public void AFullDockTimesOutAndRefundsWhatWasNotDelivered()
        {
            // Both incoming slots hold beef, so the milk cannot be unloaded.
            _world.Bootstrap(new GoodsLot { Id = "beef-a", ItemId = "beef", OwnerId = "shop", LocationId = "shop-dock:out", Quantity = 10, SpoilAfterSeconds = 600 });
            _world.Bootstrap(new GoodsLot { Id = "beef-b", ItemId = "beef", OwnerId = "shop", LocationId = "shop-dock:out", Quantity = 1, SpoilAfterSeconds = 600, ExposureSeconds = 5 });
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(Order("blocked").Accepted, Is.True);
            AdvanceUntil(x => x.Trucks.Any(t => t.Supplier && t.State == TruckState.Unloading));
            AdvanceUntil(x => x.Trucks.Any(t => t.Supplier && t.State == TruckState.ToPark), (int)GoodsWorld.FarmDockTimeoutSeconds + 5);
            Assert.That(_world.Snapshot().FarmOrders.Single().Returning, Is.True);
            AdvanceUntil(x => x.FarmOrders.Count == 0);
            Assert.That(Cash(), Is.EqualTo(StartCash), "Goods and fee are refunded when nothing was delivered.");
            Assert.That(_world.Snapshot().Lots.Count(x => x.ItemId == "milk"), Is.Zero, "The milk went back to the farm, refunded.");
            Assert.That(_world.Snapshot().Ledger.Last().Kind, Is.EqualTo(GoodsWorld.LedgerFarmRefund));
            Assert.DoesNotThrow(() => GoodsWorld.Validate(_world.Snapshot()));
        }

        [Test]
        public void EveryRejectionLeavesTheStateUnchanged()
        {
            var before = JsonUtility.ToJson(_world.Snapshot());
            void Rejected(string reason, GoodsOutcome outcome)
            {
                Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, reason)));
                Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), $"{reason} is answered, not recorded.");
            }

            Rejected("unknown-farm", Order("a", farm: "lot-nowhere"));
            Rejected("not-sold-here", Order("b", offer: "supplier-beef-5"));
            Rejected("invalid-quantity", Order("c", packs: GoodsWorld.MaxFarmOrderPacks + 1));
            Rejected("invalid-dock", Order("d", dock: "shop-storage"));
            Rejected("forbidden", Order("e", player: "stranger"));
            Rejected("invalid-truck", Order("f", truck: "no-truck"));
            Rejected("capacity", Order("g", packs: 5, truck: "truck"));
            Rejected("insufficient-funds", Order("h", packs: GoodsWorld.MaxFarmOrderPacks, offer: "supplier-beef-5", farm: "lot-farm-b"));
            Rejected("invalid-interval", _world.ScheduleFarmOrderDurably("boss", "i", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "", 0, PathForSave));
        }

        [Test]
        public void ARecurringOrderRunsEachGameHourUntilCancelled()
        {
            var scheduled = _world.ScheduleFarmOrderDurably("boss", "daily", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "", 2, PathForSave);
            Assert.That(scheduled.Accepted, Is.True);
            var id = _world.Snapshot().FarmSchedules.Single().Id;
            Assert.That(_world.Snapshot().FarmOrders, Is.Empty, "The first order waits for the next game hour.");
            _world.Advance(GoodsWorld.GameHourSeconds);
            Assert.That(_world.Snapshot().FarmOrders.Single().Id, Is.EqualTo($"order:{id}:1"));
            Assert.That(Cash(), Is.EqualTo(StartCash - MilkPack - GoodsWorld.FarmDeliveryFeeCents));
            Assert.That(_world.Snapshot().FarmSchedules.Single().NextHour, Is.EqualTo(3));

            Assert.That(_world.CancelFarmScheduleDurably("stranger", "x", id, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.CancelFarmScheduleDurably("boss", "stop", id, PathForSave).Accepted, Is.True);
            _world.Advance(4 * GoodsWorld.GameHourSeconds);
            Assert.That(_world.Snapshot().FarmSchedules, Is.Empty);
            Assert.That(_world.Snapshot().Ledger.Count(x => x.Kind == GoodsWorld.LedgerFarmGoods), Is.EqualTo(1), "Nothing more is ordered.");
        }

        [Test]
        public void AnOpenOrderSurvivesAReloadAndAV25SaveUpgrades()
        {
            Assert.That(Order("reload", packs: 1).Accepted, Is.True);
            _world.Advance(2);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Register(loaded);
            Assert.That(loaded.Snapshot().FarmOrders.Single().Id, Is.EqualTo("order:boss:reload"));
            _world = loaded;
            AdvanceUntil(x => x.FarmOrders.Count == 0);
            Assert.That(Units("shop-dock:out"), Is.EqualTo(5));

            // The same world shaped as v25: no farm order lists, no order fields on trucks.
            GoodsSnapshotStore.Save(_world, PathForSave);
            var v25 = SnapshotDatabase.LatestPayload(PathForSave).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":25");
            v25 = Regex.Replace(v25, ",\"FarmOrders\":\\[[^\\]]*\\],\"FarmSchedules\":\\[[^\\]]*\\]", "");
            v25 = Regex.Replace(v25, ",\"OrderId\":\"[^\"]*\",\"Supplier\":(true|false)", "");
            Assert.That(v25, Does.Contain("\"SchemaVersion\":25").And.Not.Contain("FarmOrders").And.Not.Contain("OrderId"), "The payload is shaped like a v25 save.");
            SnapshotDatabase.WritePayload(PathForSave, v25);
            var upgraded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((upgraded.SchemaVersion, upgraded.FarmOrders.Count, upgraded.Trucks.Single().OrderId), Is.EqualTo((GoodsSnapshot.CurrentSchema, 0, "")));
        }

        [Test]
        public void ASiteIsRenamedOnlyByItsCompanysPlayers()
        {
            Assert.That(_world.RenameSiteDurably("boss", "name", "shop", "  Corner Bistro ", PathForSave).Accepted, Is.True);
            Assert.That(_world.Snapshot().Sites.Single(x => x.Id == "shop").Name, Is.EqualTo("Corner Bistro"));
            Assert.That(_world.RenameSiteDurably("stranger", "x", "shop", "Mine", PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.RenameSiteDurably("boss", "y", "shop", " ", PathForSave).Reason, Is.EqualTo("invalid-name"));
            Assert.That(_world.RenameSiteDurably("boss", "z", "shop", new string('a', GoodsWorld.MaxSiteNameLength + 1), PathForSave).Reason,
                Is.EqualTo("invalid-name"));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Sites.Single(x => x.Id == "shop").Name, Is.EqualTo("Corner Bistro"));
        }
    }
}
