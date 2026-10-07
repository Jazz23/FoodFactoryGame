// Verifies machine power switches (decision 0037): a manually powered kind starts and advances batches only while switched on,
// switching off pauses a batch where it is, only granted actors can flip a placed switch, picking the machine up switches it
// off, the switch commits durably, kinds without a switch run as before, and v17 saves load with every machine off and no
// employee task lists. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class PowerTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "goods.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryPowerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: one 10x10 site with a manually powered oven and an unpowered fryer, each holding 4 dough, a 5-second
        // bake (2 dough -> 1 bread) and fry (1 dough -> 1 fries), and a chef granted the site. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "site", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "site", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsEquipment { Id = "oven-1", Kind = "oven", SiteId = "site", Width = 1, Depth = 1, InputCapacity = 10, OutputCapacity = 4 });
            world.Bootstrap(new GoodsEquipment { Id = "fryer-1", Kind = "fryer", SiteId = "site", CellX = 2, Width = 1, Depth = 1, InputCapacity = 10, OutputCapacity = 4 });
            world.Bootstrap(new GoodsLot { Id = "oven-dough", ItemId = "dough", OwnerId = "site", LocationId = "oven-1:in", Quantity = 4, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "fryer-dough", ItemId = "dough", OwnerId = "site", LocationId = "fryer-1:in", Quantity = 4, SpoilAfterSeconds = 100 });
            world.Grant("chef", "site");
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "bake", StationKind = "oven", DurationSeconds = 5, Inputs = { new RecipeInput { ItemId = "dough", Quantity = 2 } },
                OutputItemId = "bread", OutputQuantity = 1, OutputSpoilAfterSeconds = 50
            });
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "fry", StationKind = "fryer", DurationSeconds = 5, Inputs = { new RecipeInput { ItemId = "dough", Quantity = 1 } },
                OutputItemId = "fries", OutputQuantity = 1, OutputSpoilAfterSeconds = 50
            });
            world.RegisterManualPower("oven");
            world.AutomaticJobs = true;
            return world;
        }

        private StationJob OvenJob() => _world.Snapshot().Jobs.SingleOrDefault(x => x.StationId == "oven-1");

        private int Output(string location) => _world.Snapshot().Lots.Where(x => x.LocationId == location).Sum(x => x.Quantity);

        [Test]
        public void AnOvenThatIsOffNeverStartsWhileOtherMachinesRun()
        {
            Assert.That(_world.RequiresPower("oven"), Is.True);
            Assert.That(_world.RequiresPower("fryer"), Is.False);
            _world.Advance(1);
            Assert.That(OvenJob(), Is.Null, "An oven that is off starts nothing by itself.");
            Assert.That(_world.Snapshot().Jobs.Any(x => x.StationId == "fryer-1"), Is.True, "A machine without a switch runs as before.");
            Assert.That(_world.StartJob("chef", "manual", "oven-1", "bake").Reason, Is.EqualTo("powered-off"));

            var on = _world.SetPower("chef", "on", "oven-1", true);
            Assert.That((on.Accepted, on.Reason), Is.EqualTo((true, "powered-on")));
            Assert.That(OvenJob()?.RemainingSeconds, Is.EqualTo(5), "Switching on starts a ready batch in the same command.");
            _world.Advance(5);
            Assert.That(Output("oven-1:out"), Is.EqualTo(1));
        }

        [Test]
        public void SwitchingOffPausesABatchWhereItIs()
        {
            Assert.That(_world.SetPower("chef", "on", "oven-1", true).Accepted, Is.True);
            _world.Advance(2);
            Assert.That(OvenJob()?.RemainingSeconds, Is.EqualTo(3));
            Assert.That(_world.SetPower("chef", "off", "oven-1", false).Reason, Is.EqualTo("powered-off"));
            _world.Advance(30);
            Assert.That(OvenJob()?.RemainingSeconds, Is.EqualTo(3), "A paused batch does not advance.");
            Assert.That(Output("oven-1:out"), Is.Zero);

            Assert.That(_world.SetPower("chef", "on-again", "oven-1", true).Accepted, Is.True);
            _world.Advance(3);
            Assert.That(Output("oven-1:out"), Is.EqualTo(1), "The batch resumes where it paused.");
            Assert.That(OvenJob()?.RemainingSeconds, Is.EqualTo(5), "The next batch starts while it stays on.");
        }

        [Test]
        public void OnlyGrantedActorsFlipAPlacedSwitch()
        {
            Assert.That(_world.SetPower("stranger", "s", "oven-1", true).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.SetPower("chef", "f", "fryer-1", true).Reason, Is.EqualTo("no-power-switch"));
            Assert.That(_world.SetPower("chef", "m", "missing", true).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.Snapshot().Equipment.Single(x => x.Id == "oven-1").PoweredOn, Is.False);

            Assert.That(_world.SetPower("chef", "on", "oven-1", true).Accepted, Is.True);
            Assert.That(_world.SetPower("chef", "on", "oven-1", false).Reason, Is.EqualTo("powered-on"), "A replay returns the first outcome.");
            Assert.That(_world.Snapshot().Equipment.Single(x => x.Id == "oven-1").PoweredOn, Is.True);

            Assert.That(_world.PickUp("chef", "pick", "oven-1").Accepted, Is.True);
            var held = _world.Snapshot().Equipment.Single(x => x.Id == "oven-1");
            Assert.That(held.PoweredOn, Is.False, "Picking a machine up switches it off.");
            Assert.That(_world.SetPower("chef", "held", "oven-1", true).Reason, Is.EqualTo("not-placed"));
        }

        [Test]
        public void TheSwitchCommitsDurablyOrNotAtAll()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.SetPowerDurably("chef", "bad", "oven-1", true, BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Equipment.Single(x => x.Id == "oven-1").PoweredOn, Is.False, "A failed commit changes nothing.");
            Assert.That(OvenJob(), Is.Null);

            Assert.That(_world.SetPowerDurably("chef", "good", "oven-1", true, PathForSave).Accepted, Is.True);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.Equipment.Single(x => x.Id == "oven-1").PoweredOn, Is.True);
            Assert.That(loaded.Jobs.Any(x => x.StationId == "oven-1"), Is.True, "The batch it started commits with it.");
        }

        [Test]
        public void SchemaV17RowLoadsWithMachinesOffAndNoTaskLists()
        {
            _world.Bootstrap(new GoodsEmployee { Id = "employee-a", SiteId = "site", Name = "A" }, 2);
            Assert.That(_world.SetPower("chef", "on", "oven-1", true).Accepted, Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var v17 = JsonUtility.ToJson(_world.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":17")
                .Replace(",\"Tasks\":\"\"", "").Replace(",\"PoweredOn\":true", "").Replace(",\"PoweredOn\":false", "");
            Assert.That(v17, Does.Not.Contain("\"Tasks\"").And.Not.Contain("PoweredOn"));
            SnapshotDatabase.WritePayload(PathForSave, v17);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(17), "The row looks like a real v17 commit.");

            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Equipment.All(x => !x.PoweredOn), Is.True, "Every machine loads switched off.");
            Assert.That(loaded.Jobs.Single(x => x.StationId == "oven-1").RemainingSeconds, Is.EqualTo(5), "The batch waits, paused.");
            Assert.That(loaded.Employees.Single().Tasks, Is.EqualTo(""));
        }
    }
}
