// Verifies the dev seed's customers (decision 0024): a new world has the dev district, both competitors and the placed table;
// an older save without them gains each exactly once, committed before serving; a table the players picked up is never
// replaced. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEditor;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class DevWorldCustomersTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);

        private static EquipmentDefinition Table() => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Table.asset");

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

        private GoodsWorld Open(EquipmentDefinition table) => DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), table: table);

        [Test]
        public void NewWorldHasTheDistrictCompetitorsAndPlacedTable()
        {
            var state = Open(Table()).Snapshot();
            Assert.That(state.Districts.Select(x => x.Id), Is.EqualTo(new[] { DevWorld.DistrictId }));
            Assert.That(state.Competitors.Select(x => x.Id), Is.EquivalentTo(new[] { DevWorld.CafeId, DevWorld.NoodlesId }));
            var table = state.Equipment.Single(x => x.Kind == DevWorld.TableKind);
            Assert.That((table.Id, table.State, table.CellX, table.CellZ, table.Seats),
                Is.EqualTo((DevWorld.TableId, EquipmentState.Placed, DevWorld.TableCellX, DevWorld.TableCellZ, 4)));
            Assert.That(state.Buildings.Single(x => x.Id == DevWorld.RestaurantId).CellX, Is.LessThan(DevWorld.TableCellX), "Inside the restaurant.");
        }

        [Test]
        public void OlderSaveGainsCustomersOnceAndAHeldTableIsNeverReplaced()
        {
            // A world written without a table definition stands in for a save from before customers: strip what it seeded.
            Open(null);
            var saved = GoodsSnapshotStore.Load(WorldPath);
            var bare = saved.Snapshot();
            bare.Districts.Clear();
            bare.Competitors.Clear();
            bare.Revision++;
            File.Delete(WorldPath);
            GoodsSnapshotStore.Save(GoodsWorld.Restore(bare), WorldPath);

            var upgraded = Open(Table());
            var committed = GoodsSnapshotStore.Load(WorldPath).Snapshot();
            Assert.That((committed.Districts.Count, committed.Competitors.Count, committed.Equipment.Count(x => x.Kind == DevWorld.TableKind)),
                Is.EqualTo((1, 2, 1)), "Committed before serving.");

            Assert.That(upgraded.TryGrantDurably("chef", DevWorld.SiteId, WorldPath, DevWorld.InventoryCapacity), Is.True);
            Assert.That(upgraded.PickUpDurably("chef", "take-table", DevWorld.TableId, WorldPath).Accepted, Is.True);
            var restarted = Open(Table()).Snapshot();
            Assert.That(restarted.Equipment.Where(x => x.Kind == DevWorld.TableKind).Select(x => (x.Id, x.State)),
                Is.EqualTo(new[] { (DevWorld.TableId, EquipmentState.Held) }));
            Assert.That((restarted.Districts.Count, restarted.Competitors.Count), Is.EqualTo((1, 2)), "Added once, never twice.");
        }
    }
}
