// Verifies saved employee records: a record brings its grant and carried inventory, pose and script survive a save and load,
// a failed script commit changes nothing, recovery rejects records without their grant or inventory, views hide scripts,
// and v8 saves load with no employees. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class EmployeeTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");
        private string MissingPath => Path.Combine(_saveDirectory, "missing", "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryEmployeeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: one site with a 5-slot storage holding 3 dough, and a worker with 2 hand slots.
        private static GoodsWorld CreateWorld(bool withEmployee = true)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "site", Kind = "storage", Capacity = 5 });
            world.Bootstrap(new GoodsLot { Id = "dough", ItemId = "dough", OwnerId = "site", LocationId = "storage", Quantity = 3, SpoilAfterSeconds = 100 });
            if (withEmployee) world.Bootstrap(Worker(), 2);
            return world;
        }

        private static GoodsEmployee Worker() => new() { Id = "employee-a", SiteId = "site", Name = "A", X = 1f, Z = 2f, Yaw = 90f };

        [Test]
        public void RecordBringsItsGrantAndHandsSoItCanCarryGoods()
        {
            var world = CreateWorld();
            var hands = GoodsWorld.InventoryLocationId("employee-a");
            Assert.That(world.CanView("employee-a", "site"), Is.True);
            Assert.That(world.Snapshot().Locations.Single(x => x.Id == hands).Capacity, Is.EqualTo(2));
            var moved = world.Transfer("employee-a", new TransferIntent { RequestId = "r1", LotId = "dough", DestinationId = hands, Quantity = 2 });
            Assert.That(moved.Accepted, Is.True, moved.Reason);
        }

        [Test]
        public void ExistingGrantAndHandsAreReused()
        {
            var world = CreateWorld(false);
            var hands = GoodsWorld.InventoryLocationId("employee-a");
            world.Bootstrap(new GoodsLocation { Id = hands, SiteId = "site", Kind = "carried", Capacity = 4 });
            world.Grant("employee-a", "site");
            world.Bootstrap(Worker(), 2);
            var state = world.Snapshot();
            Assert.That(state.Locations.Single(x => x.Id == hands).Capacity, Is.EqualTo(4), "An existing inventory is kept.");
            Assert.That(state.Grants.Count(x => x.PlayerId == "employee-a"), Is.EqualTo(1));
        }

        [Test]
        public void InvalidOrDuplicateRecordsAreRefused()
        {
            var world = CreateWorld();
            Assert.Throws<ArgumentException>(() => world.Bootstrap(Worker(), 2), "Duplicate ID.");
            Assert.Throws<ArgumentException>(() => world.Bootstrap(new GoodsEmployee { Id = "player-x", SiteId = "site" }, 2), "Not an employee ID.");
            Assert.Throws<ArgumentException>(() => world.Bootstrap(new GoodsEmployee { Id = "employee-b", SiteId = "nowhere" }, 2), "Unknown site.");
            Assert.Throws<ArgumentException>(() => world.Bootstrap(new GoodsEmployee { Id = "employee-c", SiteId = "site", X = float.NaN }, 2), "Pose.");
        }

        [Test]
        public void PoseAndScriptSurviveSaveAndLoad()
        {
            var world = CreateWorld();
            GoodsSnapshotStore.Save(world, PathForSave);
            var revision = world.Snapshot().Revision;
            Assert.That(world.SetEmployeePose("employee-a", 4f, 0.1f, -3f, 180f), Is.True);
            Assert.That(world.HasUncommittedChanges, Is.True, "A pose change waits for the next commit.");
            Assert.That(world.SetEmployeeScriptDurably("employee-a", "wait(1)", true, PathForSave), Is.Null);
            Assert.That(world.HasUncommittedChanges, Is.False, "The script commit saved the pose with it.");
            Assert.That(world.Snapshot().Revision, Is.GreaterThan(revision));

            var loaded = GoodsSnapshotStore.Load(PathForSave).Employees().Single();
            Assert.That((loaded.X, loaded.Y, loaded.Z, loaded.Yaw), Is.EqualTo((4f, 0.1f, -3f, 180f)));
            Assert.That((loaded.Script, loaded.ScriptRunning, loaded.Name), Is.EqualTo(("wait(1)", true, "A")));
        }

        [Test]
        public void FailedScriptCommitChangesNothing()
        {
            var world = CreateWorld();
            GoodsSnapshotStore.Save(world, PathForSave);
            var revision = world.Snapshot().Revision;
            Assert.That(world.SetEmployeeScriptDurably("employee-a", "wait(1)", true, MissingPath), Is.EqualTo("persistence-unavailable"));
            var employee = world.Employees().Single();
            Assert.That((employee.Script, employee.ScriptRunning, world.Snapshot().Revision), Is.EqualTo(("", false, revision)));
            Assert.That(world.SetEmployeeScriptDurably("employee-z", "", false, PathForSave), Is.EqualTo("unknown-employee"));
            Assert.That(world.SetEmployeeScriptDurably("employee-a", new string('-', GoodsWorld.MaxEmployeeScriptLength + 1), false, PathForSave),
                Is.EqualTo("script-too-long"));
            Assert.That(world.SetEmployeePose("employee-a", float.PositiveInfinity, 0f, 0f, 0f), Is.False);
        }

        [Test]
        public void RecoveryRejectsAnEmployeeWithoutItsHandsOrGrant()
        {
            var state = CreateWorld().Snapshot();
            var withoutHands = JsonUtility.FromJson<GoodsSnapshot>(JsonUtility.ToJson(state));
            withoutHands.Locations.RemoveAll(x => x.Id == GoodsWorld.InventoryLocationId("employee-a"));
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(withoutHands));
            var withoutGrant = JsonUtility.FromJson<GoodsSnapshot>(JsonUtility.ToJson(state));
            withoutGrant.Grants.Clear();
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(withoutGrant));
            var duplicate = JsonUtility.FromJson<GoodsSnapshot>(JsonUtility.ToJson(state));
            duplicate.Employees.Add(duplicate.Employees[0]);
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(duplicate));
        }

        [Test]
        public void ViewListsTheSitesEmployeesWithoutScripts()
        {
            var world = CreateWorld();
            world.Grant("chef", "site");
            Assert.That(world.SetEmployeeScriptDurably("employee-a", "wait(1)", true, PathForSave), Is.Null);
            var seen = world.View("chef", "site").Employees.Single();
            Assert.That((seen.Id, seen.Script, seen.ScriptRunning), Is.EqualTo(("employee-a", "", true)));
        }

        [Test]
        public void SchemaV8RowLoadsAsCurrentWithoutEmployees()
        {
            var legacy = CreateWorld(false);
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v8 = JsonUtility.ToJson(legacy.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":8").Replace(",\"Employees\":[]", "");
            Assert.That(v8, Does.Not.Contain("Employees"));
            SnapshotDatabase.WritePayload(PathForSave, v8);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(8), "The row looks like a real v8 commit.");

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Employees(), Is.Empty);
            loaded.Bootstrap(Worker(), 2);
            GoodsSnapshotStore.Save(loaded, PathForSave);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Employees().Single().Id, Is.EqualTo("employee-a"));
        }
    }
}
