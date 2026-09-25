// Verifies the dev seed's sell counter (decisions 0013, 0024): a new world has it placed, an older save without one gains it
// exactly once, a counter the players picked up is never replaced, and dev customers buy seeded bread there for the dev
// company. Isolated saves only.
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
        public void NewWorldHasThePlacedCounterAndCustomersBuyThereForTheDevCompany()
        {
            // The dock gives the dev site its map record, which customers need to find it (decision 0024).
            var world = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), counter: Counter(),
                dock: AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Dock.asset"),
                table: AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Table.asset"));
            var counter = world.Snapshot().Equipment.Single(x => x.Kind == DevWorld.CounterKind);
            Assert.That((counter.Id, counter.State, counter.CellX, counter.CellZ),
                Is.EqualTo((DevWorld.CounterId, EquipmentState.Placed, DevWorld.CounterCellX, DevWorld.CounterCellZ)));

            world.RegisterRecipe(SellBread().ToDefinition());
            world.AutomaticJobs = true;
            world.Bootstrap(new GoodsLot { Id = "test-bread", ItemId = "bread", OwnerId = DevWorld.SiteId, LocationId = counter.InputLocationId, Quantity = 2, SpoilAfterSeconds = 3600 });
            // The dev district sends a customer every 15 s from a 30 s walk away; ten minutes is ample for one to buy.
            for (var second = 0; second < 600 && world.Snapshot().Companies.Single().Cash == DevWorld.StartingCash; second++)
                Assert.That(world.TryAdvanceDurably(1, WorldPath), Is.True);
            var saved = GoodsSnapshotStore.Load(WorldPath).Snapshot();
            Assert.That(saved.Companies.Single().Cash, Is.EqualTo(DevWorld.StartingCash + 250), "One bread bought and committed.");
            Assert.That(saved.Lots.Where(x => x.ItemId == "bread").Sum(x => x.Quantity), Is.EqualTo(1));
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
