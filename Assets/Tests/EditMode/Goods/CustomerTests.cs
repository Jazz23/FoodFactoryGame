// Verifies customers (decision 0024): districts spawn at their rate and customers only choose restaurants in range; a purchase
// takes one edible item from a counter and pays the company exactly once, even across a failed commit or a restart; dine-in
// customers wait for a free seat while takeaway customers behind them are served; spoiled food is never served; walking out
// costs reputation and sends the customer elsewhere; occupied tables and serving counters cannot be picked up; one long clock
// step matches many one-second steps; and v12 saves upgrade. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class CustomerTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryCustomerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 10x10 restaurant on the map origin owned by "co" (1000 cents), with a 2x1 counter (5 input slots)
        // and a two-seat table, a chef inventory, and one menu item: 1 bread for 250 cents, served in 4 s. No district: tests add
        // what they need. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsSite { Id = "restaurant", Name = "Restaurant" });
            world.Bootstrap(new GoodsEquipment { Id = "counter-1", Kind = GoodsWorld.CounterKind, SiteId = "restaurant", Width = 2, Depth = 1, InputCapacity = 5, OutputCapacity = 1 });
            world.Bootstrap(new GoodsEquipment
            {
                Id = "table-1", Kind = GoodsWorld.TableKind, SiteId = "restaurant", CellZ = 3, Width = 2, Depth = 1, InputCapacity = 1,
                OutputCapacity = 1, Seats = 2
            });
            world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1000, SiteIds = { "restaurant" } });
            world.Grant("chef", "restaurant");
            world.RegisterRecipe(SellBread());
            world.AutomaticJobs = true;
            return world;
        }

        private static RecipeDefinition SellBread() => new()
        {
            Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 1, Cuisine = "bakery",
            Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
        };

        private static GoodsDistrict District(int customersPerHour = 3600, int mapZ = 20, int range = 100) => new()
        {
            Id = "district", Name = "Test", MapZ = mapZ, CustomersPerHour = customersPerHour, WealthPercent = 50,
            AppearanceVariants = 3, LikedCuisines = { "bakery" }, DineInPercent = 50, RangeMetres = range
        };

        private static GoodsCompetitor Competitor(string id, int mapX) => new()
        {
            Id = id, Name = id, MapX = mapX, Cuisine = "bakery", Tier = 1, PriceCents = 300, Servers = 1, ServiceSeconds = 5, Seats = 4
        };

        private static GoodsCustomer Queued(string id, bool dineIn, long ticket, long patience = 100, string restaurant = "restaurant") => new()
        {
            Id = id, DistrictId = "district", DineIn = dineIn, PatienceSeconds = patience, State = CustomerState.Queued,
            RestaurantId = restaurant, RecipeId = restaurant == "restaurant" ? "sell-bread" : "", Ticket = ticket
        };

        private void Stock(string id, int quantity, long exposure = 0) =>
            _world.Bootstrap(new GoodsLot
            {
                Id = id, ItemId = "bread", OwnerId = "restaurant", LocationId = "counter-1:in", Quantity = quantity,
                ExposureSeconds = exposure, SpoilAfterSeconds = 1000, Spoiled = exposure >= 1000
            });

        private long Cash() => _world.Snapshot().Companies.Single().Cash;
        private int Bread() => _world.Snapshot().Lots.Where(x => x.ItemId == "bread").Sum(x => x.Quantity);
        private GoodsCustomer Customer(string id) => _world.Snapshot().Customers.SingleOrDefault(x => x.Id == id);

        // Revision counts mutations, which differ between one long step and many short ones; everything else must match.
        private static string State(GoodsWorld world)
        {
            var state = world.Snapshot();
            state.Revision = 0;
            return JsonUtility.ToJson(state);
        }

        [Test]
        public void DistrictSpawnsAtItsRateAndCustomersOnlyChooseRestaurantsInRange()
        {
            _world.Bootstrap(District());
            _world.Bootstrap(Competitor("near", 30));
            _world.Bootstrap(Competitor("far", 500));
            _world.Advance(120);
            var state = _world.Snapshot();
            Assert.That(state.NextCustomerNumber, Is.GreaterThanOrEqualTo(120), "One customer a second (plus queue tickets).");
            Assert.That(state.Customers, Is.Not.Empty);
            Assert.That(state.Customers.Select(x => x.RestaurantId).Distinct(), Is.SubsetOf(new[] { "restaurant", "near" }));
            Assert.That(state.Customers.Select(x => x.Appearance), Is.All.InRange(0, 2));
            Assert.That(state.Districts.Single().SpawnProgress, Is.Zero, "120 s at 3600 an hour leaves nothing over.");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(state));
        }

        [Test]
        public void QueuedCustomerBuysOneEdibleItemPaysOnceTakesASeatAndLeaves()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 2);
            _world.Bootstrap(Queued("c1", true, 1));
            _world.Advance(1);
            var customer = Customer("c1");
            Assert.That((customer.State, customer.CounterId, customer.TableId, customer.PaidCents),
                Is.EqualTo((CustomerState.Ordering, "counter-1", "table-1", 250L)));
            Assert.That((Bread(), Cash()), Is.EqualTo((1, 1250L)), "The bread leaves the world and the company is paid at once.");
            var diner = _world.Snapshot().Diners.Single(x => x.RestaurantId == "restaurant");
            Assert.That((diner.Served, diner.Reputation > 0), Is.EqualTo((1L, true)), "A prompt sale raises reputation.");
            _world.Advance(4);
            Assert.That((Customer("c1").State, Customer("c1").CounterId), Is.EqualTo((CustomerState.Eating, "")), "The counter is free again.");
            _world.Advance(400);
            Assert.That(Customer("c1"), Is.Null, "Done eating, the customer leaves and frees the seat.");
            Assert.That((Bread(), Cash()), Is.EqualTo((1, 1250L)), "Paid exactly once.");
        }

        [Test]
        public void SpoiledFoodIsNeverServedAndWalkingOutCostsReputation()
        {
            _world.Bootstrap(District(0));
            Stock("stale", 3, exposure: 1000);
            _world.Bootstrap(Queued("c1", false, 1, patience: 30));
            _world.Advance(29);
            Assert.That((Customer("c1").State, Cash(), Bread()), Is.EqualTo((CustomerState.Queued, 1000L, 3)));
            _world.Advance(1);
            Assert.That(Customer("c1"), Is.Null, "Out of patience with nowhere else in range: gone home.");
            var diner = _world.Snapshot().Diners.Single(x => x.RestaurantId == "restaurant");
            Assert.That((diner.WalkedOut, diner.Reputation < 0), Is.EqualTo((1L, true)));
        }

        [Test]
        public void DineInWaitsForAFreeSeatWhileTakeawayBehindIsServed()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 5);
            foreach (var id in new[] { "eating-1", "eating-2" })
                _world.Bootstrap(new GoodsCustomer
                {
                    Id = id, DistrictId = "district", DineIn = true, PatienceSeconds = 100, State = CustomerState.Eating,
                    RestaurantId = "restaurant", RecipeId = "sell-bread", RemainingSeconds = 20, TableId = "table-1", PaidCents = 250
                });
            _world.Bootstrap(Queued("dine-in", true, 1));
            _world.Bootstrap(Queued("takeaway", false, 2));
            _world.Advance(1);
            Assert.That(Customer("dine-in").State, Is.EqualTo(CustomerState.Queued), "Both seats are taken.");
            Assert.That((Customer("takeaway").State, Customer("takeaway").TableId), Is.EqualTo((CustomerState.Ordering, "")));
            _world.Advance(4);
            var published = _world.Snapshot().Diners.Single(x => x.RestaurantId == "restaurant");
            Assert.That((published.PublishedWaitSeconds, published.PublishedFreeSeats), Is.EqualTo((4L, 0)),
                "Published queue and seat counts include current customer transitions.");
            _world.Advance(16);
            Assert.That(Customer("takeaway"), Is.Null, "Takeaway leaves after being served.");
            Assert.That((Customer("dine-in").State, Customer("dine-in").TableId), Is.EqualTo((CustomerState.Ordering, "table-1")),
                "A seat freed, so the dine-in customer bought.");
            Assert.That(Cash(), Is.EqualTo(1500));
        }

        [Test]
        public void AddedTableIsAvailableToAlreadyQueuedCustomers()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 2);
            foreach (var id in new[] { "eating-1", "eating-2" })
                _world.Bootstrap(new GoodsCustomer
                {
                    Id = id, DistrictId = "district", DineIn = true, PatienceSeconds = 100, State = CustomerState.Eating,
                    RestaurantId = "restaurant", RecipeId = "sell-bread", RemainingSeconds = 20, TableId = "table-1", PaidCents = 250
                });
            _world.Bootstrap(Queued("waiting", true, 1));
            _world.Advance(1);
            Assert.That(Customer("waiting").State, Is.EqualTo(CustomerState.Queued));

            _world.Bootstrap(new GoodsEquipment
            {
                Id = "table-2", Kind = GoodsWorld.TableKind, SiteId = "restaurant", CellX = 4, CellZ = 3, Width = 2, Depth = 1,
                InputCapacity = 1, OutputCapacity = 1, Seats = 2
            });
            _world.Advance(1);
            Assert.That((Customer("waiting").State, Customer("waiting").TableId),
                Is.EqualTo((CustomerState.Ordering, "table-2")));
        }

        [Test]
        public void EqualQueueTicketsKeepSnapshotOrderWhenATravellerArrives()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 1);
            _world.Bootstrap(new GoodsCustomer
            {
                Id = "traveller", DistrictId = "district", PatienceSeconds = 100, State = CustomerState.Travelling,
                RestaurantId = "restaurant", RecipeId = "sell-bread", RemainingSeconds = 1
            });
            _world.Bootstrap(Queued("already-queued", false, 0));

            _world.Advance(1);
            Assert.That(Customer("traveller").State, Is.EqualTo(CustomerState.Ordering),
                "Equal tickets retain customer order in the snapshot.");
            Assert.That(Customer("already-queued").State, Is.EqualTo(CustomerState.Queued));
        }

        [Test]
        public void WalkingOutSendsTheCustomerToAnotherRestaurantNeverBack()
        {
            _world.Bootstrap(District(0));
            _world.Bootstrap(Competitor("near", 30));
            for (var index = 0; index < 10; index++) _world.Bootstrap(Queued($"c{index}", true, index, patience: 10));
            _world.Advance(10);
            var customers = _world.Snapshot().Customers;
            Assert.That(customers, Is.Not.Empty, "Most walk on to the competitor rather than going home.");
            Assert.That(customers.Select(x => (x.State, x.RestaurantId, x.LeftRestaurantId)).Distinct(),
                Is.EqualTo(new[] { (CustomerState.Travelling, "near", "restaurant") }));
            Assert.That(_world.Snapshot().Diners.Single(x => x.RestaurantId == "restaurant").WalkedOut, Is.EqualTo(10));
            _world.Advance(200);
            Assert.That(_world.Snapshot().Customers.Select(x => x.RestaurantId), Has.None.EqualTo("restaurant"));
        }

        [Test]
        public void OccupiedTablesAndServingCountersCannotBePickedUp()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 1);
            _world.Bootstrap(Queued("c1", true, 1));
            _world.Advance(1);
            Assert.That(_world.PickUp("chef", "pick-counter", "counter-1").Reason, Is.EqualTo("occupied"));
            Assert.That(_world.PickUp("chef", "pick-table", "table-1").Reason, Is.EqualTo("occupied"));
            _world.Advance(4);
            Assert.That(_world.PickUp("chef", "pick-counter-2", "counter-1").Accepted, Is.True, "Nobody is served there any more.");
            Assert.That(_world.PickUp("chef", "pick-table-2", "table-1").Reason, Is.EqualTo("occupied"), "The customer still eats.");
        }

        [Test]
        public void FailedTickCommitRollsBackThePurchaseAndItHappensOnceLater()
        {
            _world.Bootstrap(District(0));
            Stock("bread-a", 1);
            _world.Bootstrap(Queued("c1", false, 1));
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.TryAdvanceDurably(1, BadPath), Is.False);
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "Neither the sale, the payment nor the customer changed.");
            Assert.That(_world.TryAdvanceDurably(1, PathForSave), Is.True);
            Assert.That(_world.TryAdvanceDurably(10, PathForSave), Is.True);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.Companies.Single().Cash, saved.Lots.Any(x => x.ItemId == "bread"), saved.Customers.Count),
                Is.EqualTo((1250L, false, 0)));
        }

        [Test]
        public void RestoredWorldContinuesExactlyLikeOneThatNeverStopped()
        {
            _world.Bootstrap(District(1800));
            _world.Bootstrap(Competitor("near", 30));
            Stock("bread-a", 5);
            _world.Advance(90);
            Assert.That(_world.Snapshot().Customers, Is.Not.Empty, "Some customers are mid-way when saved.");
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            loaded.RegisterRecipe(SellBread());
            loaded.AutomaticJobs = true;
            _world.Advance(300);
            loaded.Advance(300);
            Assert.That(State(loaded), Is.EqualTo(State(_world)), "Travelling and queued customers resume; choices repeat.");
        }

        [Test]
        public void OneLongStepMatchesManyOneSecondSteps()
        {
            var steps = CreateWorld();
            foreach (var world in new[] { _world, steps })
            {
                world.Bootstrap(District(1800));
                world.Bootstrap(Competitor("near", 30));
                world.Bootstrap(new GoodsLot { Id = "stock", ItemId = "bread", OwnerId = "restaurant", LocationId = "counter-1:in", Quantity = 5, SpoilAfterSeconds = 1000 });
            }
            _world.Advance(240);
            for (var second = 0; second < 240; second++) steps.Advance(1);
            Assert.That(State(steps), Is.EqualTo(State(_world)), "A server hitch must not change what customers do.");
            Assert.That(_world.Snapshot().Companies.Single().Cash, Is.GreaterThan(1000), "Some bread sold.");
        }

        [Test]
        public void ValidateRejectsInconsistentCustomers()
        {
            _world.Bootstrap(District(0));
            _world.Bootstrap(Competitor("near", 30));
            Stock("bread-a", 1);
            _world.Bootstrap(Queued("c1", true, 1));
            _world.Advance(1);
            GoodsSnapshot Mutate(Action<GoodsSnapshot> change)
            {
                var state = _world.Snapshot();
                change(state);
                return state;
            }
            Assert.DoesNotThrow(() => GoodsWorld.Validate(_world.Snapshot()));
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Customers[0].PaidCents = 0)), "a customer being served has paid");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Customers[0].TableId = "")), "dine-in needs a seat");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Customers[0].CounterId = "table-1")), "served at a counter");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Customers[0].DistrictId = "nowhere")), "known district");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s =>
            {
                var twin = JsonUtility.FromJson<GoodsCustomer>(JsonUtility.ToJson(s.Customers[0]));
                twin.Id = "twin";
                s.Customers.Add(twin);
            })), "one customer per counter");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s =>
            {
                for (var index = 0; index < 2; index++)
                    s.Customers.Add(new GoodsCustomer
                    {
                        Id = $"eating-{index}", DistrictId = "district", DineIn = true, PatienceSeconds = 100,
                        State = CustomerState.Eating, RestaurantId = "restaurant", RemainingSeconds = 10,
                        TableId = "table-1", PaidCents = 250
                    });
            })), "table occupancy cannot exceed its seats");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s =>
            {
                s.Customers.Clear();
                for (var index = 0; index < 2; index++)
                    s.Customers.Add(new GoodsCustomer
                    {
                        Id = $"ordering-{index}", DistrictId = "district", PatienceSeconds = 100,
                        State = CustomerState.Ordering, RestaurantId = "near", RemainingSeconds = 10, PaidCents = 300
                    });
            })), "competitor ordering count cannot exceed its servers");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Competitors[0].Id = "restaurant")), "competitor IDs are not sites");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(new GoodsEquipment
            {
                Id = "seatless", Kind = GoodsWorld.TableKind, SiteId = "restaurant", CellZ = 6, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1
            }), "a table has seats");
        }

        [Test]
        public void SchemaV12RowLoadsAsCurrentWithoutCustomers()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var v12 = JsonUtility.ToJson(_world.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":12");
            SnapshotDatabase.WritePayload(PathForSave, v12);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That((loaded.Districts.Count, loaded.Customers.Count), Is.EqualTo((0, 0)));
        }
    }
}
