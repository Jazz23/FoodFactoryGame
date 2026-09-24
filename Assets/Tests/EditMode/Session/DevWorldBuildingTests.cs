// Verifies the dev seed's restaurant shell (decision 0019): a new world has it clear of the seeded oven and counter and the
// spawn points, an older save without a building gains it exactly once, and one with something on its wall cells is left
// alone with a warning. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class DevWorldBuildingTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);

        private static EquipmentDefinition Definition(string name) =>
            AssetDatabase.LoadAssetAtPath<EquipmentDefinition>($"Assets/Content/Equipment/{name}.asset");

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

        private GoodsWorld Open() =>
            DevWorld.LoadOrCreate(WorldPath, Definition("Oven"), SessionTestFiles.ContentItems(), counter: Definition("Counter"));

        // An older dev save: the dev site's layout and storage but no building, committed at the current schema.
        private void SaveWithoutBuilding(GoodsEquipment onSite = null)
        {
            var world = new GoodsWorld(DevWorld.WorldId);
            world.Bootstrap(new GoodsLocation { Id = DevWorld.StorageId, SiteId = DevWorld.SiteId, Kind = "storage", Capacity = DevWorld.StorageCapacity });
            world.Bootstrap(new SiteLayout { SiteId = DevWorld.SiteId, Width = DevWorld.GridWidth, Depth = DevWorld.GridDepth });
            if (onSite != null) world.Bootstrap(onSite);
            GoodsSnapshotStore.Save(world, WorldPath);
        }

        [Test]
        public void NewWorldHasTheRestaurantShellClearOfTheSeedAndTheSpawnPoints()
        {
            var state = Open().Snapshot();
            var shell = state.Buildings.Single();
            Assert.That((shell.Id, shell.CellX, shell.CellZ, shell.Width, shell.Depth),
                Is.EqualTo((DevWorld.RestaurantId, DevWorld.RestaurantCellX, DevWorld.RestaurantCellZ, DevWorld.RestaurantWidth, DevWorld.RestaurantDepth)));
            Assert.That(shell.Doors.Select(x => (x.X, x.Z)), Is.EqualTo(new[] { (DevWorld.RestaurantDoorX, 8), (DevWorld.RestaurantDoorX + 1, 8) }));
            Assert.That(state.Equipment.Select(x => x.Kind), Is.EquivalentTo(new[] { "oven", DevWorld.CounterKind }));

            // The DevSite spawn points stand at x = -3, -1, 1, 3 m on z = 0; none may be inside a wall or the shell.
            var layout = state.SiteLayouts.Single();
            foreach (var x in new[] { -3f, -1f, 1f, 3f })
            {
                var (cellX, cellZ) = SiteGridSpace.AnchorAt(layout, new Vector3(x, 0f, 0.05f), 1, 1);
                Assert.That(SiteGrid.OnPerimeter(shell, cellX, cellZ) || SiteGrid.IsInterior(shell, cellX, cellZ), Is.False, $"Spawn at x = {x} m.");
            }
        }

        [Test]
        public void OlderSaveGainsTheShellOnce()
        {
            SaveWithoutBuilding();
            var upgraded = Open();
            Assert.That(upgraded.Snapshot().Buildings.Select(x => x.Id), Is.EqualTo(new[] { DevWorld.RestaurantId }));
            Assert.That(GoodsSnapshotStore.Load(WorldPath).Snapshot().Buildings.Count, Is.EqualTo(1), "Committed before serving.");
            Assert.That(Open().Snapshot().Buildings.Count, Is.EqualTo(1), "A restart adds nothing.");
        }

        [Test]
        public void OlderSaveWithSomethingOnTheWallsIsLeftWithoutAShell()
        {
            SaveWithoutBuilding(new GoodsEquipment
            {
                Id = "old-fridge", Kind = "fridge", SiteId = DevWorld.SiteId, CellX = DevWorld.RestaurantCellX, CellZ = 4,
                Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1
            });
            LogAssert.Expect(LogType.Warning, new Regex("dev restaurant's walls"));
            var world = Open();
            Assert.That(world.Snapshot().Buildings, Is.Empty);
            Assert.That(world.Snapshot().Equipment.Single(x => x.Id == "old-fridge").CellX, Is.EqualTo(DevWorld.RestaurantCellX), "Nothing is moved.");
        }
    }
}
