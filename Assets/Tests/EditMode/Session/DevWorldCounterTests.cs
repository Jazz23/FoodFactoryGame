// Verifies the dev seed's sell counter (decision 0013): a new world has it placed, an older save without one gains it exactly
// once, a counter the players picked up is never replaced, and seeded bread at it earns the dev company cash. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class DevWorldCounterTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);

        private static EquipmentDefinition Counter() =>
            AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Counter.asset");

        private static RecipeAsset SellBread() => AssetDatabase.LoadAssetAtPath<RecipeAsset>("Assets/Content/Recipes/SellBread.asset");

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

        private GoodsWorld Open(EquipmentDefinition counter) =>
            DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), counter: counter);

        [Test]
        public void NewWorldHasThePlacedCounterAndItSellsForTheDevCompany()
        {
            var world = Open(Counter());
            var counter = world.Snapshot().Equipment.Single(x => x.Kind == DevWorld.CounterKind);
            Assert.That((counter.Id, counter.State, counter.CellX, counter.CellZ),
                Is.EqualTo((DevWorld.CounterId, EquipmentState.Placed, DevWorld.CounterCellX, DevWorld.CounterCellZ)));

            world.RegisterRecipe(SellBread().ToDefinition());
            world.AutomaticJobs = true;
            world.Bootstrap(new GoodsLot { Id = "test-bread", ItemId = "bread", OwnerId = DevWorld.SiteId, LocationId = counter.InputLocationId, Quantity = 2, SpoilAfterSeconds = 3600 });
            Assert.That(world.TryAdvanceDurably(1, WorldPath), Is.True);
            Assert.That(world.TryAdvanceDurably(5, WorldPath), Is.True);
            var saved = GoodsSnapshotStore.Load(WorldPath).Snapshot();
            Assert.That(saved.Companies.Single().Cash, Is.EqualTo(DevWorld.StartingCash + 250), "One bread sold and committed.");
        }

        [Test]
        public void OlderSaveGainsTheCounterOnceAndAHeldCounterIsNeverReplaced()
        {
            Open(null);
            var upgraded = Open(Counter());
            Assert.That(upgraded.Snapshot().Equipment.Count(x => x.Kind == DevWorld.CounterKind), Is.EqualTo(1));
            Assert.That(GoodsSnapshotStore.Load(WorldPath).Snapshot().Equipment.Any(x => x.Id == DevWorld.CounterId), Is.True, "Committed before serving.");

            Assert.That(upgraded.TryGrantDurably("chef", DevWorld.SiteId, WorldPath, DevWorld.InventoryCapacity), Is.True);
            Assert.That(upgraded.PickUpDurably("chef", "take-counter", DevWorld.CounterId, WorldPath).Accepted, Is.True);
            var restarted = Open(Counter());
            var counters = restarted.Snapshot().Equipment.Where(x => x.Kind == DevWorld.CounterKind).ToList();
            Assert.That(counters.Select(x => (x.Id, x.State)), Is.EqualTo(new[] { (DevWorld.CounterId, EquipmentState.Held) }));
        }
    }
}
