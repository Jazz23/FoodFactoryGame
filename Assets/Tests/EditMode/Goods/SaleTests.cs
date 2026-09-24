// Verifies sale recipes (decision 0013): a counter sells edible goods one at a time on the server clock and pays the site's
// company in the same commit; spoiled goods never sell; no company means no sale; pickup mid-sale refunds and pays nothing;
// a failed tick commit rolls back goods and cash together; a sale saved mid-way completes exactly once. Isolated saves only.
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
        // and a sale of 1 bread every 4 s for 250 cents; bread spoils after 100 s. Not gameplay content.
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
        }

        [Test]
        public void CounterSellsOneBreadPerServiceTimeAndPaysTheCompany()
        {
            Stock("bread-a", 3);
            _world.Advance(1);
            Assert.That(_world.Snapshot().Jobs.Single().IsSale, Is.True, "A customer is being served.");
            Assert.That((Bread(), Cash()), Is.EqualTo((2, 1000L)), "The bread is taken at the start, paid for at the end.");
            _world.Advance(4);
            Assert.That(Cash(), Is.EqualTo(1250));
            _world.Advance(4);
            _world.Advance(4);
            _world.Advance(4);
            Assert.That((Bread(), Cash()), Is.EqualTo((0, 1750L)));
            Assert.That(_world.Snapshot().Jobs, Is.Empty, "The counter is idle once sold out.");
            Assert.That(_world.Snapshot().Lots.Any(x => x.ItemId == "bread"), Is.False, "Sold goods leave the world.");
        }

        [Test]
        public void SpoiledGoodsNeverSell()
        {
            Stock("stale", 2, exposure: 100);
            _world.Advance(10);
            Assert.That((Bread(), Cash(), _world.Snapshot().Jobs.Count), Is.EqualTo((2, 1000L, 0)));
            Stock("fresh", 1);
            _world.Advance(1);
            _world.Advance(4);
            Assert.That(Cash(), Is.EqualTo(1250), "Fresh bread sells even beside spoiled bread.");
            Assert.That(_world.Snapshot().Lots.Single(x => x.Id == "stale").Quantity, Is.EqualTo(2));
        }

        [Test]
        public void SiteWithoutACompanyDoesNotSell()
        {
            var world = new GoodsWorld("no-company");
            world.Bootstrap(new SiteLayout { SiteId = "stall", Width = 4, Depth = 4 });
            world.Bootstrap(new GoodsEquipment { Id = "counter-2", Kind = "counter", SiteId = "stall", Width = 2, Depth = 1, InputCapacity = 5, OutputCapacity = 1 });
            world.Bootstrap(new GoodsLot { Id = "b", ItemId = "bread", OwnerId = "stall", LocationId = "counter-2:in", Quantity = 1, SpoilAfterSeconds = 100 });
            world.Grant("chef", "stall");
            world.RegisterRecipe(SellBread());
            world.AutomaticJobs = true;
            world.Advance(10);
            Assert.That(world.Snapshot().Jobs, Is.Empty);
            Assert.That(world.StartJob("chef", "sell", "counter-2", "sell-bread").Reason, Is.EqualTo("no-company"));
            Assert.That(world.Snapshot().Lots.Single().Quantity, Is.EqualTo(1));

            Stock("x", 1);
            _world.Advance(1);
            var selling = _world.Snapshot();
            Assert.That(selling.Jobs.Single().IsSale, Is.True);
            Assert.DoesNotThrow(() => GoodsWorld.Validate(selling));
            selling.Companies.Clear();
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(selling), "A sale in progress needs a company.");
        }

        [Test]
        public void PickingUpMidSaleRefundsTheGoodsAndPaysNothing()
        {
            Stock("bread-a", 2);
            _world.Advance(2);
            var outcome = _world.PickUp("chef", "pick", "counter-1");
            Assert.That(outcome.Accepted, Is.True, outcome.Reason);
            Assert.That((Bread("carried:chef"), Cash()), Is.EqualTo((2, 1000L)), "The bread being sold comes back unsold.");
            Assert.That(_world.Snapshot().Jobs, Is.Empty);
        }

        [Test]
        public void FailedTickCommitRollsBackGoodsAndCashTogether()
        {
            Stock("bread-a", 1);
            _world.Advance(1);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.TryAdvanceDurably(4, BadPath), Is.False);
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "Neither the sale nor the payment happened.");
            Assert.That(_world.TryAdvanceDurably(4, PathForSave), Is.True);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.Companies.Single().Cash, saved.Jobs.Count, saved.Lots.Any(x => x.ItemId == "bread")), Is.EqualTo((1250L, 0, false)));
        }

        [Test]
        public void SaleSavedMidWayCompletesExactlyOnceAfterReload()
        {
            Stock("bread-a", 1);
            _world.Advance(2);
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
