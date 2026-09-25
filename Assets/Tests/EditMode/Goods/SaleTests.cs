// Verifies sale recipes after decision 0024: a sale recipe is a menu item that only customers buy (CustomerTests), so stations
// never start one, by themselves or on request. A sale job saved by an older build (decision 0013) still completes and pays
// its company exactly once: it waits unpaid rather than overflowing the balance, pickup mid-sale refunds and pays nothing, a
// failed tick commit rolls back goods and cash together, and it needs a company. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class SaleTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactorySaleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 10x10 restaurant owned by "co" (1000 cents) with a 2x1 counter (input 5 slots), a chef inventory,
        // and a sale of 1 bread for 250 cents served in 4 s; bread spoils after 100 s. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsEquipment { Id = "counter-1", Kind = "counter", SiteId = "restaurant", Width = 2, Depth = 1, InputCapacity = 5, OutputCapacity = 1 });
            world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1000, SiteIds = { "restaurant" } });
            world.Grant("chef", "restaurant");
            world.RegisterRecipe(SellBread());
            world.AutomaticJobs = true;
            return world;
        }

        private static RecipeDefinition SellBread() => new()
        {
            Id = "sell-bread", StationKind = "counter", DurationSeconds = 4,
            Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
        };

        private void Stock(string id, int quantity, long exposure = 0, string location = "counter-1:in") =>
            _world.Bootstrap(new GoodsLot
            {
                Id = id, ItemId = "bread", OwnerId = "restaurant", LocationId = location, Quantity = quantity,
                ExposureSeconds = exposure, SpoilAfterSeconds = 100, Spoiled = exposure >= 100
            });

        // Replaces the world with its own state plus a running sale job, as an older build saved it: one bread already taken,
        // remainingSeconds of service left.
        private void WithLegacySale(long remainingSeconds = 2)
        {
            var state = _world.Snapshot();
            state.Jobs.Add(new StationJob
            {
                Id = "legacy", StationId = "counter-1", RecipeId = "sell-bread", StartedBy = GoodsWorld.AutomaticStarter,
                DurationSeconds = 4, RemainingSeconds = remainingSeconds, State = StationJobState.Running, OutputItemId = "",
                SaleCents = 250,
                Inputs = { new GoodsLot { Id = "legacy:in:0", ItemId = "bread", OwnerId = "restaurant", LocationId = "", Quantity = 1, SpoilAfterSeconds = 100 } }
            });
            _world = GoodsWorld.Restore(state);
            _world.RegisterRecipe(SellBread());
            _world.AutomaticJobs = true;
        }

        private long Cash() => _world.Snapshot().Companies.Single().Cash;
        private int Bread(string location = "counter-1:in") =>
            _world.Snapshot().Lots.Where(x => x.LocationId == location && x.ItemId == "bread").Sum(x => x.Quantity);

        [Test]
        public void SaleRecipesMustHaveExactlyOneResult()
        {
            var both = SellBread();
            both.Id = "both";
            both.OutputItemId = "crumbs";
            both.OutputQuantity = 1;
            both.OutputSpoilAfterSeconds = 10;
            Assert.Throws<ArgumentException>(() => _world.RegisterRecipe(both), "goods and cash");
            var negative = SellBread();
            negative.Id = "negative";
            negative.SaleCents = -1;
            Assert.Throws<ArgumentException>(() => _world.RegisterRecipe(negative), "negative price without goods");
            var tierless = SellBread();
            tierless.Id = "tierless";
            tierless.Tier = 0;
            Assert.Throws<ArgumentException>(() => _world.RegisterRecipe(tierless), "a menu tier starts at 1");
        }

        [Test]
        public void StationsNeverStartMenuItems()
        {
            Stock("bread-a", 3);
            _world.Advance(20);
            Assert.That((_world.Snapshot().Jobs.Count, Bread(), Cash()), Is.EqualTo((0, 3, 1000L)), "Only customers buy.");
            Assert.That(_world.StartJob("chef", "sell", "counter-1", "sell-bread").Reason, Is.EqualTo("customers-only"));
            Assert.That(Bread(), Is.EqualTo(3));
        }

        [Test]
        public void LegacySaleJobCompletesAndPaysExactlyOnce()
        {
            Stock("bread-a", 2);
            WithLegacySale();
            _world.Advance(1);
            Assert.That(Cash(), Is.EqualTo(1000));
            _world.Advance(1);
            Assert.That((Cash(), _world.Snapshot().Jobs.Count), Is.EqualTo((1250L, 0)));
            _world.Advance(20);
            Assert.That((Cash(), Bread()), Is.EqualTo((1250L, 2)), "No new sale starts; the stock stays for customers.");
        }

        [Test]
        public void LegacySaleThatWouldOverflowTheBalanceWaitsWithoutStoppingTheClock()
        {
            WithLegacySale();
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.AdjustCashDurably("co", long.MaxValue - 1000 - 100, PathForSave), Is.Null);
            Assert.DoesNotThrow(() => _world.Advance(10));
            var state = _world.Snapshot();
            Assert.That((state.Companies.Single().Cash, state.Jobs.Single().RemainingSeconds), Is.EqualTo((long.MaxValue - 100, 0L)),
                "The sale waits unpaid; the bread is not lost.");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(state));
            Assert.That(_world.TryAdvanceDurably(1, PathForSave), Is.True, "The world keeps ticking and committing.");
            Assert.That(_world.AdjustCashDurably("co", -1000, PathForSave), Is.Null);
            _world.Advance(1);
            Assert.That((_world.Snapshot().Companies.Single().Cash, _world.Snapshot().Jobs.Count), Is.EqualTo((long.MaxValue - 850, 0)),
                "Paid once there is room.");
        }

        [Test]
        public void LegacySaleNeedsACompany()
        {
            WithLegacySale();
            var selling = _world.Snapshot();
            Assert.DoesNotThrow(() => GoodsWorld.Validate(selling));
            selling.Companies.Clear();
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(selling), "A sale in progress needs a company.");
        }

        [Test]
        public void PickingUpMidLegacySaleRefundsTheGoodsAndPaysNothing()
        {
            Stock("bread-a", 1);
            WithLegacySale();
            var outcome = _world.PickUp("chef", "pick", "counter-1");
            Assert.That(outcome.Accepted, Is.True, outcome.Reason);
            Assert.That((Bread("carried:chef"), Cash()), Is.EqualTo((2, 1000L)), "The bread being sold comes back unsold.");
            Assert.That(_world.Snapshot().Jobs, Is.Empty);
        }

        [Test]
        public void FailedTickCommitRollsBackGoodsAndCashTogether()
        {
            WithLegacySale();
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.TryAdvanceDurably(4, BadPath), Is.False);
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "Neither the sale nor the payment happened.");
            Assert.That(_world.TryAdvanceDurably(4, PathForSave), Is.True);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.Companies.Single().Cash, saved.Jobs.Count), Is.EqualTo((1250L, 0)));
        }

        [Test]
        public void LegacySaleSavedMidWayCompletesExactlyOnceAfterReload()
        {
            WithLegacySale(3);
            _world.Advance(1);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            loaded.RegisterRecipe(SellBread());
            loaded.AutomaticJobs = true;
            Assert.That(loaded.Snapshot().Jobs.Single().SaleCents, Is.EqualTo(250), "The job keeps its own price.");
            loaded.Advance(2);
            loaded.Advance(10);
            Assert.That(loaded.Snapshot().Companies.Single().Cash, Is.EqualTo(1250));
        }

        [Test]
        public void SchemaV5RowLoadsAsCurrent()
        {
            _world.Advance(1);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var v5 = JsonUtility.ToJson(_world.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":5");
            SnapshotDatabase.WritePayload(PathForSave, v5);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Companies.Single().Cash, Is.EqualTo(1000));
        }
    }
}
