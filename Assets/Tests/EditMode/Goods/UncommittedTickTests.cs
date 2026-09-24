// Verifies decision-0016 clock ticks: they run in memory, reach the save only through a commit (periodic or a durable
// command), are lost together on a crash, and survive a failed commit in memory for a later retry.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class UncommittedTickTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        [SetUp]
        public void SetUp()
        {
            // TEST-ONLY values: one ambient storage and kitchen, 10 units that spoil after 100 s; not gameplay content.
            _world = new GoodsWorld("test-world");
            _world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            _world.Bootstrap(new GoodsLocation { Id = "kitchen", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 20 });
            _world.Bootstrap(new GoodsLot { Id = "lot-1", ItemId = "ingredient", OwnerId = "restaurant", LocationId = "storage", Quantity = 10, SpoilAfterSeconds = 100 });
            _world.Grant("chef", "restaurant");
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryUncommittedTickTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        [Test]
        public void TicksReachTheSaveOnlyWhenCommittedAndACrashLosesThemTogether()
        {
            Assert.That(_world.HasUncommittedChanges, Is.True, "A world that was never saved is ahead of its save.");
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.HasUncommittedChanges, Is.False);

            _world.AdvanceUncommitted(3);
            Assert.That(_world.HasUncommittedChanges, Is.True);
            var crashed = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((crashed.ClockSeconds, crashed.Lots.Single().ExposureSeconds), Is.EqualTo((0L, 0L)),
                "A crash before the commit loses the clock and everything it did.");

            Assert.That(_world.TryCommitDurably(PathForSave), Is.True);
            Assert.That(_world.HasUncommittedChanges, Is.False);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.ClockSeconds, saved.Lots.Single().ExposureSeconds, saved.Revision),
                Is.EqualTo((3L, 3L, _world.Snapshot().Revision)));
        }

        [Test]
        public void NothingPendingCommitsWithoutTouchingTheDisk()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.HasUncommittedChanges, Is.False, "A loaded world matches its save.");
            Assert.That(_world.TryCommitDurably(BadPath), Is.True, "Nothing pending, so the missing save is never opened.");
        }

        [Test]
        public void ADurableCommandSavesThePendingTicks()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world.AdvanceUncommitted(2);

            var outcome = _world.TransferDurably("chef", new TransferIntent { RequestId = "move", LotId = "lot-1", DestinationId = "kitchen", Quantity = 10 }, PathForSave);

            Assert.That(outcome.Accepted, Is.True);
            Assert.That(_world.HasUncommittedChanges, Is.False);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.ClockSeconds, saved.Lots.Single().LocationId), Is.EqualTo((2L, "kitchen")));
        }

        [Test]
        public void AFailedCommitKeepsTheTicksInMemoryForARetry()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world.AdvanceUncommitted(4);
            var before = _world.Snapshot();

            Assert.That(_world.TryCommitDurably(BadPath), Is.False);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(before.ClockSeconds), "Memory is not rolled back.");
            Assert.That(_world.HasUncommittedChanges, Is.True);

            Assert.That(_world.TryCommitDurably(PathForSave), Is.True);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().ClockSeconds, Is.EqualTo(4));
        }
    }
}
