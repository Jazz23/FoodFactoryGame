// Verifies slot capacity (decision 0009): locations count slots, stacks fill slots up to their item's max stack, and
// content-driven over-fullness blocks entries without losing goods. Test-only sites, items and sizes.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class SlotCapacityTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        [SetUp]
        public void SetUp()
        {
            // TEST-ONLY values: a 50-slot pantry, a 2-slot shelf, dough stacking to 20 and unregistered salt stacking to 1.
            _world = new GoodsWorld("slot-world");
            _world.RegisterItem("dough", 20);
            _world.Bootstrap(new GoodsLocation { Id = "pantry", SiteId = "restaurant", Kind = "storage", Capacity = 50 });
            _world.Bootstrap(new GoodsLocation { Id = "shelf", SiteId = "restaurant", Kind = "storage", Capacity = 2 });
            _world.Bootstrap(new GoodsLot { Id = "dough", ItemId = "dough", OwnerId = "restaurant", LocationId = "pantry", Quantity = 45, SpoilAfterSeconds = 100 });
            _world.Bootstrap(new GoodsLot { Id = "salt", ItemId = "salt", OwnerId = "restaurant", LocationId = "pantry", Quantity = 5, SpoilAfterSeconds = 100 });
            _world.Grant("chef", "restaurant");
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactorySlotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private GoodsOutcome Move(string request, string lotId, int quantity, string destination = "shelf") =>
            _world.Transfer("chef", new TransferIntent { RequestId = request, LotId = lotId, DestinationId = destination, Quantity = quantity });

        private long Free(string itemId, bool spoiled = false) =>
            GoodsSlots.FreeUnits(_world.Snapshot(), "shelf", itemId, spoiled, _world.MaxStack);

        [Test]
        public void StacksFillSlotsUpToTheirMaxStack()
        {
            Assert.That(_world.MaxStack("dough"), Is.EqualTo(20));
            Assert.That(_world.MaxStack("salt"), Is.EqualTo(1), "An unregistered item stacks to 1.");
            Assert.That(Free("dough"), Is.EqualTo(40));
            Assert.That(Move("first", "dough", 25).Accepted, Is.True);
            Assert.That(Free("dough"), Is.EqualTo(15), "The partly filled second slot still takes dough.");
            Assert.That(Move("over", "dough", 16).Reason, Is.EqualTo("capacity"));
            Assert.That(Move("fill", "dough", 15).Accepted, Is.True);
            var state = _world.Snapshot();
            Assert.That(state.Lots.Where(x => x.LocationId == "shelf").Sum(x => x.Quantity), Is.EqualTo(40));
            Assert.That(GoodsSlots.SlotsUsed(state.Lots.Where(x => x.LocationId == "shelf"), _world.MaxStack), Is.EqualTo(2));
            Assert.That(state.Lots.Where(x => x.LocationId == "pantry" && x.ItemId == "dough").Sum(x => x.Quantity), Is.EqualTo(5), "A rejected move changes nothing.");
        }

        [Test]
        public void EachItemNeedsItsOwnSlot()
        {
            Assert.That(Move("dough", "dough", 21).Accepted, Is.True);
            Assert.That(Free("salt"), Is.EqualTo(0), "Two dough slots leave nothing for salt.");
            Assert.That(Move("salt", "salt", 1).Reason, Is.EqualTo("capacity"));
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "back", LotId = _world.Snapshot().Lots.Single(x => x.LocationId == "shelf").Id,
                DestinationId = "pantry", Quantity = 1 }).Accepted, Is.True);
            Assert.That(Move("salt-2", "salt", 1).Accepted, Is.True, "Twenty dough fit one slot, freeing the other.");
            Assert.That(Move("salt-3", "salt", 1).Reason, Is.EqualTo("capacity"), "Salt stacks to 1.");
        }

        [Test]
        public void SpoilingSplitsAStackAndOverfullnessBlocksEntryWithoutLosingGoods()
        {
            _world.Bootstrap(new GoodsLocation { Id = "crate", SiteId = "restaurant", Kind = "storage", Capacity = 1 });
            _world.Bootstrap(new GoodsLot { Id = "old", ItemId = "dough", OwnerId = "restaurant", LocationId = "crate", Quantity = 5, ExposureSeconds = 90, SpoilAfterSeconds = 100 });
            _world.Bootstrap(new GoodsLot { Id = "new", ItemId = "dough", OwnerId = "restaurant", LocationId = "crate", Quantity = 5, SpoilAfterSeconds = 100 });
            _world.Advance(10);
            var state = _world.Snapshot();
            Assert.That(state.Lots.Single(x => x.Id == "old").Spoiled, Is.True);
            Assert.That(GoodsSlots.SlotsUsed(state.Lots.Where(x => x.LocationId == "crate"), _world.MaxStack), Is.EqualTo(2));
            Assert.That(Move("into-crate", "dough", 1, "crate").Reason, Is.EqualTo("capacity"));
            var path = Path.Combine(_saveDirectory, "goods.snapshot");
            GoodsSnapshotStore.Save(_world, path);
            var restored = GoodsSnapshotStore.Load(path).Snapshot();
            Assert.That(restored.Lots.Where(x => x.LocationId == "crate").Sum(x => x.Quantity), Is.EqualTo(10), "Over-full is recoverable state, not corruption.");
        }

        [Test]
        public void GrantEnlargesASmallerInventoryAndNeverShrinksIt()
        {
            var path = Path.Combine(_saveDirectory, "goods.snapshot");
            var inventory = GoodsWorld.InventoryLocationId("sous");
            Assert.That(_world.TryGrantDurably("sous", "restaurant", path, 10), Is.True);
            Assert.That(_world.TryGrantDurably("sous", "restaurant", path, 30), Is.True);
            Assert.That(GoodsSnapshotStore.Load(path).Snapshot().Locations.Single(x => x.Id == inventory).Capacity, Is.EqualTo(30));
            var revision = _world.Snapshot().Revision;
            Assert.That(_world.TryGrantDurably("sous", "restaurant", path, 20), Is.True);
            Assert.That(_world.Snapshot().Locations.Single(x => x.Id == inventory).Capacity, Is.EqualTo(30));
            Assert.That(_world.Snapshot().Revision, Is.EqualTo(revision), "A large enough inventory needs no write.");
        }

        [Test]
        public void StarterGoodsAreCountedInSlots()
        {
            var path = Path.Combine(_saveDirectory, "goods.snapshot");
            var twenty = new[] { new GoodsLot { ItemId = "dough", Quantity = 20, SpoilAfterSeconds = 100 } };
            Assert.That(_world.TryGrantDurably("sous", "restaurant", path, 1, twenty), Is.True);
            var tooMany = new[] { new GoodsLot { ItemId = "dough", Quantity = 21, SpoilAfterSeconds = 100 } };
            Assert.Throws<ArgumentException>(() => _world.TryGrantDurably("baker", "restaurant", path, 1, tooMany));
        }

        [Test]
        public void SavedMachinesTakeContentBufferSlotsWithoutLosingGoods()
        {
            var path = Path.Combine(_saveDirectory, "goods.snapshot");
            _world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            _world.Bootstrap(new GoodsEquipment { Id = "oven-1", Kind = "oven", SiteId = "restaurant", Width = 1, Depth = 1, InputCapacity = 10, OutputCapacity = 4 });
            Assert.That(Move("salt-in", "salt", 3, "oven-1:in").Accepted, Is.True);

            Assert.That(_world.ApplyEquipmentCapacitiesDurably("oven", 1, 1, path), Is.True);
            var saved = GoodsSnapshotStore.Load(path).Snapshot();
            Assert.That((saved.Equipment.Single().InputCapacity, saved.Equipment.Single().OutputCapacity), Is.EqualTo((1, 1)));
            Assert.That(saved.Locations.Single(x => x.Id == "oven-1:in").Capacity, Is.EqualTo(1));
            Assert.That(saved.Locations.Single(x => x.Id == "oven-1:out").Capacity, Is.EqualTo(1));
            Assert.That(saved.Lots.Where(x => x.LocationId == "oven-1:in").Sum(x => x.Quantity), Is.EqualTo(3), "Over-full, but nothing removed.");
            Assert.That(Move("salt-more", "salt", 1, "oven-1:in").Reason, Is.EqualTo("capacity"));
            Assert.That(_world.ApplyEquipmentCapacitiesDurably("oven", 1, 1, path), Is.False, "Already current: no write.");
        }

        [Test]
        public void ItemRegistrationRejectsInvalidOrDuplicateContent()
        {
            Assert.Throws<ArgumentException>(() => _world.RegisterItem("dough", 10));
            Assert.Throws<ArgumentException>(() => _world.RegisterItem("bread", 0));
            Assert.Throws<ArgumentException>(() => _world.RegisterItem(" ", 5));
        }
    }
}
