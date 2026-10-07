// Verifies the readiness readout (decision 0038) on constructed restaurants: each blocker in priority order (no customer door,
// no register customers reach, none staffed, no edible menu item), each warning (no reachable seat, a dock away from a back
// door, register stock about to spoil), the status read from customers and the ledger, and that "ready" is exactly when the
// customer simulation serves a queued customer. The readout writes nothing. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class RestaurantReadinessTests
    {
        // TEST-ONLY values: a 16x12 site with an 8x6 restaurant shell at (2,2) (interior 3..8 x 3..6), door at (5,2); company "co"
        // with $10,000; a 2x1 register ($50, 5 input slots), a 1x1 two-seat table ($40) and a 2x1 dock ($60); one bread menu item
        // ($2.50) and one oven recipe that is not on the menu. Not gameplay content.
        private GoodsWorld _world;
        private string _saveDirectory;
        private RecipeDefinition[] _recipes;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryReadinessTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            _world = new GoodsWorld("test-world");
            _world.RegisterItem("bread", 10);
            _world.Bootstrap(new SiteLayout { SiteId = "resto", Width = 16, Depth = 12 });
            _world.Bootstrap(new GoodsSite { Id = "resto", Name = "Resto" });
            _world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "resto", Kind = "carried", Capacity = 10 });
            _world.Bootstrap(new GoodsBuilding
            {
                Id = "shop", SiteId = "resto", CellX = 2, CellZ = 2, Width = 8, Depth = 6, Doors = new List<GridCell> { new() { X = 5, Z = 2 } }
            });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1_000_000, SiteIds = { "resto" } });
            _world.Grant("chef", "resto");
            GoodsEquipment Template(string kind, int width, int seats = 0, int input = 1) => new()
            {
                Kind = kind, State = EquipmentState.Held, Width = width, Depth = 1, InputCapacity = input, OutputCapacity = 1, Seats = seats, Layer = ""
            };
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "register", PriceCents = 5000, Equipment = Template(GoodsWorld.CounterKind, 2, input: 5) });
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "table", PriceCents = 4000, Equipment = Template(GoodsWorld.TableKind, 1, seats: 2) });
            _recipes = new[]
            {
                new RecipeDefinition
                {
                    Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 1, Cuisine = "bakery",
                    Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
                },
                new RecipeDefinition
                {
                    Id = "bake", StationKind = "oven", DurationSeconds = 10, Tier = 1, Cuisine = "bakery",
                    Inputs = { new RecipeInput { ItemId = "dough", Quantity = 1 } }, OutputItemId = "bread", OutputQuantity = 1, OutputSpoilAfterSeconds = 3600
                }
            };
            foreach (var recipe in _recipes) _world.RegisterRecipe(recipe);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private const string Register = "buy:chef:register:0";

        private RestaurantReadiness Readout(string site = "resto")
        {
            var before = JsonUtility.ToJson(_world.Snapshot());
            var view = _world.View("chef", site);
            var readout = RestaurantReadiness.Evaluate(view, site, null, _recipes);
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "The readout writes nothing.");
            return readout;
        }

        private void Place(string request, string offer, int x, int z) => Assert.That(_world.BuyAndPlace("chef", request,
            new FurnishOrder { SiteId = "resto", OfferId = offer, Placements = { new GridPlacement { X = x, Z = z } } }).Accepted, Is.True);

        private void Bread(string id, int quantity, long spoilAfter = 9000, long exposure = 0, bool spoiled = false) => _world.Bootstrap(new GoodsLot
        {
            Id = id, ItemId = "bread", OwnerId = "resto", LocationId = Register + ":in", Quantity = quantity, SpoilAfterSeconds = spoilAfter,
            ExposureSeconds = exposure, Spoiled = spoiled
        });

        [Test]
        public void BlockersFollowTheSimulationsOrder()
        {
            Assert.That(Readout().Blocker, Is.EqualTo(RestaurantReadiness.NoReachableRegister), "No register placed.");
            Place("register", "register", 4, 4);
            var unstaffed = Readout();
            Assert.That((unstaffed.Blocker, unstaffed.Registers, unstaffed.ReachableRegisters, unstaffed.StaffedRegisters),
                Is.EqualTo((RestaurantReadiness.NoStaffedRegister, 1, 1, 0)));
            Assert.That(_world.Staff("chef", "staff", Register, "chef").Accepted, Is.True);
            Assert.That(Readout().Blocker, Is.EqualTo(RestaurantReadiness.NoEdibleMenuItem), "Empty input.");
            Bread("stale", 3, spoilAfter: 3600, exposure: 3600, spoiled: true);
            Assert.That(Readout().Blocker, Is.EqualTo(RestaurantReadiness.NoEdibleMenuItem), "Spoiled bread is not edible.");
            Bread("fresh", 2);
            var ready = Readout();
            Assert.That((ready.Ready, ready.Blocker), Is.EqualTo((true, (string)null)));
        }

        [Test]
        public void ARegisterWalledOffFromTheDoorIsNotReachable()
        {
            Place("register", "register", 7, 4);
            Assert.That(_world.Staff("chef", "staff", Register, "chef").Accepted, Is.True);
            Bread("fresh", 2);
            Assert.That(Readout().Ready, Is.True);
            var walls = new ShellOrder
            {
                Kind = ShellOrder.Partition, BuildingId = "shop", Style = "plaster",
                Cells = new[] { (6, 3), (6, 4), (6, 5), (6, 6) }.Select(c => new GridCell { X = c.Item1, Z = c.Item2 }).ToList()
            };
            Assert.That(_world.OrderShell("chef", "walls", walls).Accepted, Is.True);
            var walled = Readout();
            Assert.That((walled.Blocker, walled.Registers, walled.ReachableRegisters), Is.EqualTo((RestaurantReadiness.NoReachableRegister, 1, 0)),
                "Staffed and stocked, but customers cannot reach it.");
        }

        [Test]
        public void ARestaurantWithoutACustomerDoorIsBlockedFirst()
        {
            _world.Bootstrap(new SiteLayout { SiteId = "closed", Width = 12, Depth = 10 });
            _world.Bootstrap(new GoodsSite { Id = "closed", Name = "Closed", MapX = 50 });
            _world.Bootstrap(new GoodsBuilding { Id = "box", SiteId = "closed", CellX = 2, CellZ = 2, Width = 6, Depth = 5 });
            _world.Bootstrap(new GoodsCompany { Id = "other", Cash = 0, SiteIds = { "closed" } });
            _world.Grant("chef", "closed");
            Assert.That(Readout("closed").Blocker, Is.EqualTo(RestaurantReadiness.NoCustomerDoor));
            Assert.That(RestaurantReadiness.Evaluate(_world.Snapshot(), "nowhere", null, _recipes), Is.Null, "Not a restaurant site.");
        }

        [Test]
        public void WarningsNameSeatsDocksAndStockAboutToSpoil()
        {
            Place("register", "register", 4, 4);
            Assert.That(_world.Staff("chef", "staff", Register, "chef").Accepted, Is.True);
            Bread("old", 1, spoilAfter: 3600, exposure: 3100);
            Bread("new", 4, spoilAfter: 3600, exposure: 100);
            var readout = Readout();
            Assert.That(readout.Ready, Is.True);
            Assert.That(readout.Warnings, Is.EqualTo(new[] { RestaurantReadiness.NoReachableSeat, RestaurantReadiness.StockSpoilsSoon }));
            Assert.That((readout.SpoilingItemId, readout.SpoilingSeconds), Is.EqualTo(("bread", 500L)));

            Place("table", "table", 7, 5);
            _world.Bootstrap(new GoodsEquipment
            {
                Id = "old-dock", Kind = GoodsWorld.DockKind, SiteId = "resto", State = EquipmentState.Placed, CellX = 12, CellZ = 9, Width = 2, Depth = 1,
                InputCapacity = 8, OutputCapacity = 8
            });
            readout = Readout();
            Assert.That(readout.ReachableSeats, Is.EqualTo(2));
            Assert.That(readout.Warnings, Is.EqualTo(new[] { RestaurantReadiness.DockNotBesideBackDoor, RestaurantReadiness.StockSpoilsSoon }));
            Assert.That(readout.DocksNotBesideBackDoor, Is.EqualTo(new[] { "old-dock" }));
        }

        [Test]
        public void StatusCountsCustomersAndRecentLedgerEntries()
        {
            Place("register", "register", 4, 4);
            Place("table", "table", 7, 5);
            Assert.That(_world.Staff("chef", "staff", Register, "chef").Accepted, Is.True);
            Bread("bread", 5);
            _world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "Test", MapZ = 20, CustomersPerHour = 0, WealthPercent = 50, AppearanceVariants = 1,
                LikedCuisines = { "bakery" }, DineInPercent = 100, RangeMetres = 100
            });
            GoodsCustomer Queued(string id, long ticket) => new()
            {
                Id = id, DistrictId = "district", DineIn = true, PatienceSeconds = 600, State = CustomerState.Queued, RestaurantId = "resto",
                RecipeId = "sell-bread", Ticket = ticket
            };
            _world.Bootstrap(Queued("c1", 1));
            Assert.That(Readout().Queued, Is.EqualTo(1));
            _world.Advance(10);
            var readout = Readout();
            Assert.That((readout.Queued, readout.Eating, readout.RecentSales, readout.RecentSalesCents, readout.RecentSpendCents),
                Is.EqualTo((0, 1, 1, 250L, 9000L)), "One sale; $50 register + $40 table spent.");
            _world.Advance(RestaurantReadiness.RecentSeconds);
            readout = Readout();
            Assert.That((readout.RecentSales, readout.RecentSpendCents), Is.EqualTo((0, 0L)), "Older than the window.");
        }

        // "Ready" is the same question the simulation asks: a queued customer is served exactly when the readout has no blocker.
        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public void ReadyIsExactlyWhenAQueuedCustomerIsServed(bool placed, bool staffed, bool stocked)
        {
            if (placed) Place("register", "register", 4, 4);
            if (staffed) Assert.That(_world.Staff("chef", "staff", Register, "chef").Accepted, Is.True);
            if (stocked) Bread("bread", 1);
            _world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "Test", MapZ = 20, CustomersPerHour = 0, WealthPercent = 50, AppearanceVariants = 1,
                LikedCuisines = { "bakery" }, DineInPercent = 0, RangeMetres = 100
            });
            _world.Bootstrap(new GoodsCustomer
            {
                Id = "c1", DistrictId = "district", DineIn = false, PatienceSeconds = 600, State = CustomerState.Queued, RestaurantId = "resto",
                RecipeId = "sell-bread", Ticket = 1
            });
            var ready = Readout().Ready;
            _world.Advance(1);
            var served = _world.Snapshot().Customers.Single().State != CustomerState.Queued;
            Assert.That((ready, served), Is.EqualTo((stocked, stocked)));
        }
    }
}
