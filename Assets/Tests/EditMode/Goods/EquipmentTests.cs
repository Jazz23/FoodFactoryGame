// Verifies equipment placement: stable identity across pickup/place, grid rules, inventory holding, durability and the v2 schema upgrade.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class EquipmentTests
    {
        [Serializable] private sealed class TestEnvelope
        {
            public string Payload;
            public string Sha256;
        }

        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.snapshot");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "goods.snapshot");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryEquipmentTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 6x5-cell restaurant with a 3x3 oven at (0,0) and a 2x1 counter at (4,0),
        // inventories of 10 for chef and sous. Not gameplay content or configuration.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "pantry", SiteId = "restaurant", Kind = "storage", Capacity = 50 });
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new GoodsLocation { Id = "carried:sous", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 6, Depth = 5 });
            world.Bootstrap(new GoodsEquipment { Id = "oven-1", Kind = "oven", SiteId = "restaurant", Width = 3, Depth = 3, InputCapacity = 6, OutputCapacity = 2, OutputRefrigerated = true });
            world.Bootstrap(new GoodsEquipment { Id = "counter", Kind = "counter", SiteId = "restaurant", CellX = 4, Width = 2, Depth = 1, InputCapacity = 1, OutputCapacity = 1 });
            world.Grant("chef", "restaurant");
            world.Grant("sous", "restaurant");
            return world;
        }

        private static GoodsEquipment Oven(GoodsWorld world) => world.Snapshot().Equipment.Single(x => x.Id == "oven-1");

        // Placement-relevant state only; outcomes and revision are expected to change on a recorded rejection.
        private static string Layout(GoodsWorld world)
        {
            var state = world.Snapshot();
            return JsonUtility.ToJson(new GoodsSnapshot
            {
                WorldId = "x", Locations = state.Locations, Lots = state.Lots, Stations = state.Stations, Jobs = state.Jobs, Equipment = state.Equipment
            });
        }

        [Test]
        public void PickupThenPlaceElsewhereKeepsIdentityAndRecreatesStation()
        {
            var picked = _world.PickUp("chef", "pick", "oven-1");
            Assert.That(picked.Accepted, Is.True);
            var held = Oven(_world);
            Assert.That(held.State, Is.EqualTo(EquipmentState.Held));
            Assert.That(held.HolderId, Is.EqualTo("chef"));
            Assert.That(_world.Snapshot().Stations.Any(x => x.Id == "oven-1"), Is.False);
            Assert.That(_world.View("sous", "restaurant").Equipment.Any(x => x.Id == "oven-1" && x.HolderId == "chef"), Is.True,
                "Held equipment stays visible in the site view.");

            var placed = _world.Place("chef", "place", "oven-1", 1, 2, 1);
            Assert.That(placed.Accepted, Is.True);
            Assert.That(placed.Reason, Is.EqualTo("placed"));
            var oven = Oven(_world);
            Assert.That((oven.State, oven.HolderId, oven.CellX, oven.CellZ, oven.Rotation), Is.EqualTo((EquipmentState.Placed, "", 1, 2, 1)));
            var state = _world.Snapshot();
            Assert.That(state.Equipment.Count, Is.EqualTo(2));
            var station = state.Stations.Single(x => x.Id == "oven-1");
            Assert.That((station.Kind, station.InputLocationId, station.OutputLocationId), Is.EqualTo(("oven", "oven-1:in", "oven-1:out")));
            Assert.That(state.Locations.Single(x => x.Id == "oven-1:in").Capacity, Is.EqualTo(6));
            var output = state.Locations.Single(x => x.Id == "oven-1:out");
            Assert.That((output.Capacity, output.Refrigerated), Is.EqualTo((2, true)));
            Assert.That(_world.Place("chef", "place", "oven-1", 0, 0, 0).Accepted, Is.True, "Replay returns the stored outcome.");
            Assert.That(Oven(_world).CellZ, Is.EqualTo(2));
        }

        [Test]
        public void RejectionsChangeNothing()
        {
            var start = Layout(_world);
            Assert.That(_world.PickUp("intruder", "steal", "oven-1").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.Place("chef", "early", "oven-1", 0, 0, 0).Reason, Is.EqualTo("not-held"));
            Assert.That(Layout(_world), Is.EqualTo(start));

            Assert.That(_world.PickUp("chef", "pick", "oven-1").Accepted, Is.True);
            var held = Layout(_world);
            Assert.That(_world.PickUp("chef", "again", "oven-1").Reason, Is.EqualTo("not-placed"));
            Assert.That(_world.PickUp("sous", "grab", "oven-1").Reason, Is.EqualTo("not-placed"));
            Assert.That(_world.Place("sous", "theirs", "oven-1", 0, 0, 0).Reason, Is.EqualTo("not-held"));
            Assert.That(_world.Place("intruder", "theirs", "oven-1", 0, 0, 0).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.Place("chef", "counter", "oven-1", 3, 0, 0).Reason, Is.EqualTo("blocked"));
            Assert.That(_world.Place("chef", "edge", "oven-1", 4, 2, 0).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(_world.Place("chef", "negative", "oven-1", -1, 0, 0).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(_world.Place("chef", "spin", "oven-1", 0, 0, 4).Reason, Is.EqualTo("invalid-rotation"));
            Assert.That(_world.Place("chef", "ghost", "nowhere", 0, 0, 0).Reason, Is.EqualTo("forbidden"));
            Assert.That(Layout(_world), Is.EqualTo(held));
        }

        [Test]
        public void FootprintRotationsSwapWidthAndDepth()
        {
            for (var rotation = 0; rotation < 4; rotation++)
                Assert.That(SiteGrid.Footprint(2, 1, rotation), Is.EqualTo(rotation % 2 == 0 ? (2, 1) : (1, 2)), $"rotation {rotation}");

            // The 2x1 counter picked up and turned upright fits the 1-wide column at x=5 only when rotated.
            Assert.That(_world.PickUp("chef", "pick", "counter").Accepted, Is.True);
            Assert.That(_world.Place("chef", "flat", "counter", 5, 3, 0).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(_world.Place("chef", "flat-3", "counter", 5, 3, 2).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(_world.Place("chef", "upright", "counter", 5, 3, 3).Accepted, Is.True);
            // Rotated counter now covers (5,3)-(5,4); an oven at (3,2) would cover x 3..5, z 2..4.
            Assert.That(_world.PickUp("chef", "pick-oven", "oven-1").Accepted, Is.True);
            Assert.That(_world.Place("chef", "overlap", "oven-1", 3, 2, 0).Reason, Is.EqualTo("blocked"));
            Assert.That(_world.Place("chef", "beside", "oven-1", 2, 2, 0).Accepted, Is.True);
        }

        [Test]
        public void FailedCommitsRollBackPickupAndPlacement()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = Layout(_world);
            Assert.That(_world.PickUpDurably("chef", "pick", "oven-1", BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Layout(_world), Is.EqualTo(before));
            Assert.That(_world.PickUpDurably("chef", "pick", "oven-1", PathForSave).Accepted, Is.True);

            var held = Layout(_world);
            Assert.That(_world.PlaceDurably("chef", "place", "oven-1", 0, 2, 0, BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Layout(_world), Is.EqualTo(held));
            Assert.That(_world.PlaceDurably("chef", "place", "oven-1", 0, 2, 0, PathForSave).Accepted, Is.True);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Equipment.Single(x => x.Id == "oven-1").CellZ, Is.EqualTo(2));
        }

        [Test]
        public void HeldEquipmentSurvivesReloadNeitherPlacedNorLost()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.PickUpDurably("chef", "pick", "oven-1", PathForSave).Accepted, Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            var oven = Oven(_world);
            Assert.That((oven.State, oven.HolderId), Is.EqualTo((EquipmentState.Held, "chef")));
            Assert.That(_world.Snapshot().Stations.Any(x => x.Id == "oven-1"), Is.False);
            Assert.That(_world.PickUpDurably("chef", "pick", "oven-1", PathForSave).Accepted, Is.True, "Replay survives recovery.");
            Assert.That(_world.PlaceDurably("chef", "place", "oven-1", 0, 1, 0, PathForSave).Accepted, Is.True);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Equipment.Count, Is.EqualTo(2));
        }

        [Test]
        public void AdmissionGrantCreatesInventoryOnceAndBackfillsExistingGrants()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.TryGrantDurably("cook", "restaurant", PathForSave, 7), Is.True);
            var inventory = _world.Snapshot().Locations.Single(x => x.Id == "carried:cook");
            Assert.That((inventory.SiteId, inventory.Kind, inventory.Capacity), Is.EqualTo(("restaurant", "carried", 7)));
            var revision = _world.Snapshot().Revision;
            Assert.That(_world.TryGrantDurably("cook", "restaurant", PathForSave, 7), Is.True);
            Assert.That(_world.Snapshot().Revision, Is.EqualTo(revision), "Existing grant and inventory need no write.");

            // A player granted before inventories existed receives one on their next admission.
            _world.Grant("waiter", "restaurant");
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.TryGrantDurably("waiter", "restaurant", BadPath, 7), Is.False);
            Assert.That(_world.Snapshot().Locations.Any(x => x.Id == "carried:waiter"), Is.False);
            Assert.That(_world.TryGrantDurably("waiter", "restaurant", PathForSave, 7), Is.True);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Locations.Any(x => x.Id == "carried:waiter"), Is.True);
        }

        [Test]
        public void StarterGoodsArriveOnlyWithANewInventory()
        {
            // TEST-ONLY starter goods: 4 flour that spoil after 100 s.
            var starter = new[] { new GoodsLot { ItemId = "flour", Quantity = 4, SpoilAfterSeconds = 100 } };
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.TryGrantDurably("cook", "restaurant", PathForSave, 7, starter), Is.True);
            var lot = _world.Snapshot().Lots.Single(x => x.LocationId == "carried:cook");
            Assert.That((lot.Id, lot.ItemId, lot.OwnerId, lot.Quantity, lot.SpoilAfterSeconds),
                Is.EqualTo(("starter:cook:0", "flour", "restaurant", 4, 100L)));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Lots.Any(x => x.Id == "starter:cook:0"), Is.True);

            // Rejoining never grants them again, even after they were used up.
            Assert.That(_world.Transfer("cook", new TransferIntent { RequestId = "stow", LotId = lot.Id, DestinationId = "pantry", Quantity = 4 }).Accepted, Is.True);
            Assert.That(_world.TryGrantDurably("cook", "restaurant", PathForSave, 7, starter), Is.True);
            Assert.That(_world.Snapshot().Lots.Any(x => x.LocationId == "carried:cook"), Is.False);

            // A failed commit leaves neither the inventory nor its goods; oversized starters are refused before any change.
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.TryGrantDurably("waiter", "restaurant", BadPath, 7, starter), Is.False);
            Assert.Throws<ArgumentException>(() => _world.TryGrantDurably("porter", "restaurant", PathForSave, 3, starter));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before));
        }

        [Test]
        public void SchemaV2SaveLoadsAsCurrentWithoutEquipment()
        {
            var legacy = new GoodsWorld("legacy-world");
            legacy.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            var current = JsonUtility.ToJson(legacy.Snapshot());
            var v2 = current.Replace("\"SchemaVersion\":4", "\"SchemaVersion\":2").Replace(",\"Equipment\":[],\"SiteLayouts\":[],\"Belts\":[]", "");
            Assert.That(v2, Does.Not.Contain("Equipment"));
            File.WriteAllText(PathForSave, JsonUtility.ToJson(new TestEnvelope { Payload = v2, Sha256 = Digest(v2) }), new UTF8Encoding(false));

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Snapshot().Equipment, Is.Empty);
            Assert.That(loaded.Snapshot().SiteLayouts, Is.Empty);
            Assert.That(loaded.TryAdvanceDurably(1, PathForSave), Is.True);
            Assert.That(JsonUtility.FromJson<TestEnvelope>(File.ReadAllText(PathForSave)).Payload, Does.Contain("\"SchemaVersion\":4"));
        }

        [Test]
        public void ValidateRejectsInconsistentEquipment()
        {
            Assert.DoesNotThrow(() => GoodsWorld.Restore(_world.Snapshot()));

            GoodsSnapshot Mutate(Action<GoodsSnapshot> change)
            {
                var copy = _world.Snapshot();
                change(copy);
                return copy;
            }

            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment.Single(x => x.Id == "counter").CellX = 2)), "overlapping footprints");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment.Single(x => x.Id == "counter").CellZ = 5)), "out of bounds");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment.Add(s.Equipment[0]))), "duplicate equipment");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment.RemoveAll(x => x.Id == "counter"))), "station without equipment");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s =>
            {
                var oven = s.Equipment.Single(x => x.Id == "oven-1");
                oven.State = EquipmentState.Held;
                oven.HolderId = "chef";
            })), "held equipment that still has a station");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment[0].HolderId = "chef")), "placed equipment with a holder");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.SiteLayouts.Clear())), "equipment without a layout");

            Assert.That(_world.PickUp("chef", "pick", "oven-1").Accepted, Is.True);
            Assert.DoesNotThrow(() => GoodsWorld.Restore(_world.Snapshot()));
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Equipment.Single(x => x.Id == "oven-1").HolderId = "")), "held without a holder");
        }

        private static string Digest(string payload)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
