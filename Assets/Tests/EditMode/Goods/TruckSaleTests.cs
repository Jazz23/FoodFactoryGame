// Verifies selling trucks (PROTOTYPE proposal, GoodsWorld.SellTruck): an empty fleet truck sells for TruckResalePercent of its
// model's list price, credited once with a sell-back ledger entry and replayed, never paid twice; it and its cargo location
// leave the world and the sale survives a reload; a truck carrying goods or serving a farm order is refused and nothing changes,
// so goods are never deleted; an empty truck driving its route can be sold and the route stays; repeating farm orders that
// name it are cancelled; strangers and supplier trucks are refused; a failed commit changes nothing. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class TruckSaleTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");
        private static readonly string RouteId = GoodsWorld.RouteIdFor("boss", "make-route");
        private static readonly string VanId = "buy:boss:buy-van";

        // TEST-ONLY values, not gameplay content: "co" owns a farm site at the map origin and a shop 30 m away with $500.00; "rival"
        // owns a stall. Each site has a 6x6 grid and a 2x1 dock (3 outgoing, 2 incoming slots). Crates and milk stack to 10. The
        // seeded "truck" (2 slots, 10 m/s, 4 units/s) matches no offer; the van offer costs $250.01 (3 slots, 5 m/s, 2 units/s).
        // A dairy farm lot 100 m east sells milk packs of 5 for $10.00.
        private const long StartCash = 50_000;
        private const long VanPrice = 25_001;

        [SetUp]
        public void SetUp()
        {
            _world = new GoodsWorld("test-world");
            _world.RegisterItem("crate", 10);
            _world.RegisterItem("milk", 10);
            _world.RegisterOffer(new PurchaseOffer { Id = "supplier-milk-5", ItemId = "milk", Quantity = 5, PriceCents = 1000, SpoilAfterSeconds = 600 });
            _world.RegisterPropertyOffers(new[]
            {
                new PropertyOffer
                {
                    LotId = "lot-farm-a", SiteId = "site-farm-a", BuildingId = "farm-a", Category = GoodsWorld.FarmCategory, ForSale = true,
                    PriceCents = 1000, LotX = 100, LotZ = 10, Width = 10, Depth = 10, AccessX = 100, AccessZ = 0, BuildingX = 0, BuildingZ = 0,
                    BuildingWidth = 10, BuildingDepth = 6, Doors = { new GridCell { X = 5, Z = 5 } }
                }
            });
            foreach (var site in new[] { "farm", "shop", "stall" })
            {
                _world.Bootstrap(new GoodsLocation { Id = site + "-storage", SiteId = site, Kind = "storage", Capacity = 10 });
                _world.Bootstrap(new SiteLayout { SiteId = site, Width = 6, Depth = 6 });
                _world.Bootstrap(new GoodsEquipment
                {
                    Id = site + "-dock", Kind = GoodsWorld.DockKind, SiteId = site, State = EquipmentState.Placed, HolderId = "", Width = 2, Depth = 1,
                    InputCapacity = 3, OutputCapacity = 2
                });
            }
            _world.Bootstrap(new GoodsSite { Id = "farm", Name = "Farm" });
            _world.Bootstrap(new GoodsSite { Id = "shop", Name = "Shop", MapX = 20, MapZ = 10 });
            _world.Bootstrap(new GoodsSite { Id = "stall", Name = "Stall", MapX = -5 });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = StartCash, SiteIds = { "farm", "shop" } });
            _world.Bootstrap(new GoodsCompany { Id = "rival", Cash = 0, SiteIds = { "stall" } });
            _world.Bootstrap(new GoodsTruck
            {
                Id = "truck", CompanyId = "co", Name = "truck", CargoSlots = 2, SpeedMetresPerSecond = 10, LoadUnitsPerSecond = 4,
                State = TruckState.Parked, SiteId = "farm"
            });
            _world.RegisterTruckOffer(new TruckOffer
            {
                Id = "van-offer", PriceCents = VanPrice, Name = "Van", CargoSlots = 3, SpeedMetresPerSecond = 5, LoadUnitsPerSecond = 2
            });
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryTruckSaleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            foreach (var site in new[] { "farm", "shop" }) Assert.That(_world.TryGrantDurably("boss", site, PathForSave), Is.True);
            Assert.That(_world.TryGrantDurably("vendor", "stall", PathForSave), Is.True);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private GoodsOutcome Sell(string request, string truck, string player = "boss") => _world.SellTruckDurably(player, request, truck, PathForSave);
        private long Cash() => _world.Snapshot().Companies.Single(x => x.Id == "co").Cash;
        private GoodsTruck Find(string id) => _world.Snapshot().Trucks.SingleOrDefault(x => x.Id == id);
        private int Units(string location) => _world.Snapshot().Lots.Where(x => x.LocationId == location).Sum(x => x.Quantity);

        private void BuyVan()
        {
            var bought = _world.BuyDurably("boss", "buy-van", "farm", "van-offer", PathForSave);
            Assert.That((bought.Accepted, bought.EquipmentId), Is.EqualTo((true, VanId)));
            Assert.That(Find(VanId).Name, Is.EqualTo("Van 2"));
        }

        // The farm-to-shop route with "truck" on it, and crates waiting at the farm dock.
        private void Routed(int crates)
        {
            _world.Bootstrap(new GoodsLot { Id = "crates", ItemId = "crate", OwnerId = "farm", LocationId = "farm-dock:in", Quantity = crates, SpoilAfterSeconds = 1000 });
            Assert.That(_world.CreateRouteDurably("boss", "make-route", "farm-dock", "shop-dock", null, PathForSave).Accepted, Is.True);
            Assert.That(_world.AssignTruckDurably("boss", "put-truck", "truck", RouteId, PathForSave).Accepted, Is.True);
        }

        [Test]
        public void ResaleIsAShareOfTheMatchingOffersPriceRoundedDown()
        {
            var offers = new[]
            {
                new TruckOffer { Id = "a", PriceCents = 10_001, Name = "Truck", CargoSlots = 4, SpeedMetresPerSecond = 15, LoadUnitsPerSecond = 5 },
                new TruckOffer { Id = "b", PriceCents = 30_000, Name = "Truck XL", CargoSlots = 4, SpeedMetresPerSecond = 15, LoadUnitsPerSecond = 5 }
            };
            GoodsTruck Truck(string name, int slots = 4) => new() { Name = name, CargoSlots = slots, SpeedMetresPerSecond = 15, LoadUnitsPerSecond = 5 };
            Assert.That(GoodsWorld.TruckOfferOf(Truck("Truck 1"), offers).Id, Is.EqualTo("a"));
            Assert.That(GoodsWorld.TruckOfferOf(Truck("Truck XL 3"), offers).Id, Is.EqualTo("b"), "The longest matching model name wins.");
            Assert.That(GoodsWorld.TruckOfferOf(Truck("Truck 1", slots: 9), offers), Is.Null, "Different stats: not that model.");
            Assert.That(GoodsWorld.TruckOfferOf(Truck("Trucker 1"), offers), Is.Null);
            Assert.That(GoodsWorld.TruckResaleCents(Truck("Truck 1"), offers), Is.EqualTo(10_001L * GoodsWorld.TruckResalePercent / 100));
            Assert.That(GoodsWorld.TruckResaleCents(Truck("Bus 1"), offers), Is.Zero);
        }

        [Test]
        public void AParkedTruckSellsOnceForItsResaleValueAndLeavesTheWorld()
        {
            BuyVan();
            Assert.That(Cash(), Is.EqualTo(StartCash - VanPrice));
            var resale = VanPrice * GoodsWorld.TruckResalePercent / 100;
            var sold = Sell("sell-van", VanId);
            Assert.That((sold.Accepted, sold.Reason, sold.Cents, sold.EquipmentId), Is.EqualTo((true, "truck-sold", -resale, VanId)));
            Assert.That(Cash(), Is.EqualTo(StartCash - VanPrice + resale));
            var state = _world.Snapshot();
            Assert.That(state.Trucks.Select(x => x.Id), Is.EqualTo(new[] { "truck" }));
            Assert.That(state.Locations.Any(x => x.Id == VanId + ":cargo"), Is.False, "Its cargo location goes with it.");
            var entry = state.Ledger.Last();
            Assert.That((entry.Kind, entry.Cents, entry.RequestId, entry.EquipmentId, entry.OfferId, entry.SiteId),
                Is.EqualTo((GoodsWorld.LedgerSellBack, resale, "sell-van", VanId, "van-offer", "farm")));
            Assert.That(GoodsWorld.LedgerBalances(state, state.Companies.Single(x => x.Id == "co")), Is.True);

            var replay = Sell("sell-van", VanId);
            Assert.That((replay.Accepted, replay.Revision, replay.Cents), Is.EqualTo((true, sold.Revision, -resale)), "A retried request replays.");
            Assert.That(Cash(), Is.EqualTo(StartCash - VanPrice + resale), "Never paid twice.");
            Assert.That(Sell("sell-again", VanId).Reason, Is.EqualTo("forbidden"), "A sold truck is gone.");

            var reloaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((reloaded.Trucks.Count, reloaded.Companies.Single(x => x.Id == "co").Cash), Is.EqualTo((1, StartCash - VanPrice + resale)));
        }

        [Test]
        public void ATruckOfNoOfferedModelSellsForNothing()
        {
            var before = _world.Snapshot().Ledger.Count;
            var sold = Sell("scrap", "truck");
            Assert.That((sold.Accepted, sold.Cents), Is.EqualTo((true, 0L)));
            Assert.That((Cash(), _world.Snapshot().Ledger.Count, _world.Snapshot().Trucks.Count), Is.EqualTo((StartCash, before, 0)),
                "No cash change, so no ledger entry.");
        }

        [Test]
        public void ATruckCarryingGoodsIsRefusedUntilItHasUnloadedThenSellsMidRoute()
        {
            Routed(6);
            _world.Advance(1);
            Assert.That(Units("truck:cargo"), Is.EqualTo(4));
            var revision = _world.Snapshot().Revision;
            var refused = Sell("too-soon", "truck");
            Assert.That((refused.Accepted, refused.Reason), Is.EqualTo((false, "has-cargo")));
            Assert.That((_world.Snapshot().Revision, Units("truck:cargo"), Find("truck") != null), Is.EqualTo((revision, 4, true)),
                "A refusal changes nothing; goods are never deleted.");

            // Loads the rest, drives 3 s, unloads 6 into the shop dock and heads back to the farm, empty.
            _world.Advance(7);
            Assert.That((Find("truck").State, Units("truck:cargo"), Units("shop-dock:out")), Is.EqualTo((TruckState.ToPickup, 0, 6)));
            var sold = Sell("now", "truck");
            Assert.That((sold.Accepted, sold.Reason), Is.EqualTo((true, "truck-sold")));
            var state = _world.Snapshot();
            Assert.That((state.Trucks.Count, state.Routes.Single().Id), Is.EqualTo((0, RouteId)), "The route is its own record and stays.");
            Assert.That(Units("shop-dock:out") + Units("farm-dock:in"), Is.EqualTo(6), "Every crate is where the truck left it.");
            _world.Advance(10);
            Assert.That(Units("shop-dock:out"), Is.EqualTo(6), "The world runs on without the truck.");
        }

        [Test]
        public void ATruckServingAFarmOrderIsRefused()
        {
            var ordered = _world.OrderFromFarmDurably("boss", "pickup", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "truck", PathForSave);
            Assert.That(ordered.Accepted, Is.True, ordered.Reason);
            var refused = Sell("sell", "truck");
            Assert.That((refused.Accepted, refused.Reason), Is.EqualTo((false, "on-order")));
            Assert.That(Find("truck").OrderId, Is.Not.Empty);
            var supplier = _world.OrderFromFarmDurably("boss", "delivery", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "", PathForSave);
            Assert.That(supplier.Accepted, Is.True, supplier.Reason);
            var supplierTruck = _world.Snapshot().Trucks.Single(x => x.Supplier);
            Assert.That(Sell("sell-supplier", supplierTruck.Id).Reason, Is.EqualTo("forbidden"), "A farm's truck is not the company's to sell.");
        }

        [Test]
        public void SellingCancelsTheRepeatingOrdersThatPickUpWithIt()
        {
            Assert.That(_world.ScheduleFarmOrderDurably("boss", "with-truck", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "truck", 2, PathForSave).Accepted, Is.True);
            Assert.That(_world.ScheduleFarmOrderDurably("boss", "delivered", "lot-farm-a", "supplier-milk-5", 1, "shop-dock", "", 2, PathForSave).Accepted, Is.True);
            Assert.That(Sell("sell", "truck").Accepted, Is.True);
            Assert.That(_world.Snapshot().FarmSchedules.Select(x => x.Id), Is.EqualTo(new[] { "schedule:boss:delivered" }),
                "Only the schedule that named the sold truck is cancelled.");
        }

        [Test]
        public void OnlyTheTrucksCompanyMaySellIt()
        {
            BuyVan();
            Assert.That(Sell("steal", VanId, "vendor").Reason, Is.EqualTo("forbidden"));
            Assert.That(Sell("ghost", "no-such-truck").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.SellTruck("", "x", VanId).Reason, Is.EqualTo("invalid-identity"));
            Assert.That((Find(VanId) != null, Cash()), Is.EqualTo((true, StartCash - VanPrice)));
        }

        [Test]
        public void FailedCommitKeepsTheTruckAndTheCash()
        {
            BuyVan();
            var failed = _world.SellTruckDurably("boss", "sell", VanId, BadPath);
            Assert.That((failed.Accepted, failed.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That((Find(VanId) != null, Cash()), Is.EqualTo((true, StartCash - VanPrice)));
            Assert.That(Sell("sell", VanId).Accepted, Is.True, "The same request can still succeed once the save works.");
        }
    }
}
