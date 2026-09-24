// Verifies isolated test-only restaurant goods rules, authoritative time, idempotency and crash-safe recovery.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class GoodsWorldTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        [SetUp]
        public void SetUp()
        {
            // TEST-ONLY values: one restaurant, one ingredient, ambient storage 20, cold storage 20,
            // kitchen station 6, spoilage threshold 10 server seconds; not gameplay content/configuration.
            _world = new GoodsWorld("test-world");
            _world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            _world.Bootstrap(new GoodsLocation { Id = "fridge", SiteId = "restaurant", Kind = "storage", Capacity = 20, Refrigerated = true });
            _world.Bootstrap(new GoodsLocation { Id = "kitchen", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 6 });
            _world.Bootstrap(new GoodsLot { Id = "lot-1", ItemId = "ingredient", OwnerId = "restaurant", LocationId = "storage", Quantity = 10, SpoilAfterSeconds = 10 });
            _world.Grant("chef", "restaurant");
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryGoodsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private GoodsOutcome Move(string request, int quantity, string destination = "kitchen", string reservation = "") =>
            _world.Transfer("chef", new TransferIntent { RequestId = request, LotId = "lot-1", DestinationId = destination, Quantity = quantity, ReservationId = reservation });

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [Test]
        public void SplitPreservesHistoryAndCompatibleMergeDoesNotDiscardGoods()
        {
            _world.Advance(4);
            var moved = Move("split", 3);
            Assert.That(moved.Accepted, Is.True);
            var state = _world.Snapshot();
            Assert.That(state.Lots.Sum(x => x.Quantity), Is.EqualTo(10));
            Assert.That(state.Lots.Single(x => x.Id == moved.MovedLotId).ExposureSeconds, Is.EqualTo(4));
            Assert.That(_world.Merge("lot-1", moved.MovedLotId), Is.False, "Different location is not equivalent.");
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "return", LotId = moved.MovedLotId, DestinationId = "storage", Quantity = 3 }).Accepted, Is.True);
            Assert.That(_world.Merge("lot-1", moved.MovedLotId), Is.True);
            Assert.That(_world.Snapshot().Lots.Single().Quantity, Is.EqualTo(10));
        }

        [Test]
        public void DifferentExposureAtSameLocationCannotMerge()
        {
            _world.Advance(3);
            var chilled = Move("chill-split", 2, "fridge");
            _world.Advance(2);
            Assert.That(_world.Transfer("chef", new TransferIntent
            {
                RequestId = "return-chilled", LotId = chilled.MovedLotId,
                DestinationId = "storage", Quantity = 2
            }).Accepted, Is.True);
            Assert.That(_world.Merge("lot-1", chilled.MovedLotId), Is.False);
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void CapacityAndQuantityRejectionAreAtomic()
        {
            Assert.That(Move("over", 7).Reason, Is.EqualTo("capacity"));
            Assert.That(Move("invalid", 11).Reason, Is.EqualTo("quantity-or-reservation"));
            Assert.That(_world.Snapshot().Lots.Single().Quantity, Is.EqualTo(10));
            Assert.That(_world.Snapshot().Lots.Single().LocationId, Is.EqualTo("storage"));
        }

        [Test]
        public void RefrigerationStopsFutureExposureButMovingNeverResetsHistory()
        {
            _world.Advance(6);
            Assert.That(Move("chill", 10, "fridge").Accepted, Is.True);
            _world.Advance(100);
            Assert.That(_world.Snapshot().Lots.Single().ExposureSeconds, Is.EqualTo(6));
            Assert.That(Move("warm", 10, "storage").Accepted, Is.True);
            _world.Advance(4);
            var lot = _world.Snapshot().Lots.Single();
            Assert.That(lot.Spoiled, Is.True);
            Assert.That(lot.ExposureSeconds, Is.EqualTo(10));
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(110));
        }

        [Test]
        public void ReservationPreventsOtherClaimsAndCancellationReleasesWithoutMoving()
        {
            Assert.That(_world.Reserve("chef", "hold", "lot-1", 8), Is.True);
            Assert.That(_world.Reserve("chef", "other", "lot-1", 3), Is.False);
            Assert.That(Move("blocked", 3).Accepted, Is.False);
            Assert.That(Move("stale", 1, "kitchen", "unknown").Accepted, Is.False);
            var cancelled = _world.Cancel("chef", "cancel", "hold");
            Assert.That(cancelled.Accepted, Is.True);
            Assert.That(_world.Cancel("chef", "cancel", "hold").Reason, Is.EqualTo("cancelled"));
            Assert.That(Move("later", 3).Accepted, Is.True);
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void ReservedSplitLeavesOtherReservationsValid()
        {
            Assert.That(_world.Reserve("chef", "a", "lot-1", 4), Is.True);
            Assert.That(_world.Reserve("chef", "b", "lot-1", 6), Is.True);
            Assert.That(Move("move", 4, "kitchen", "a").Accepted, Is.True);
            Assert.That(_world.Snapshot().Reservations.Single(x => x.Id == "b").Active, Is.True);
            Assert.That(_world.Snapshot().Lots.Single(x => x.Id == "lot-1").Quantity, Is.EqualTo(6));
        }

        [Test]
        public void MergeWithConsumedReservationStillRestores()
        {
            var split = Move("split", 3);
            Assert.That(_world.Reserve("chef", "hold-split", split.MovedLotId, 3), Is.True);
            Assert.That(_world.Transfer("chef", new TransferIntent
            {
                RequestId = "return", LotId = split.MovedLotId, DestinationId = "storage", Quantity = 3,
                ReservationId = "hold-split"
            }).Accepted, Is.True);
            Assert.That(_world.Merge("lot-1", split.MovedLotId), Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Lots.Single().Quantity, Is.EqualTo(10));
        }

        [Test]
        public void DuplicateAndUnauthorizedRequestsCannotMutateInventory()
        {
            var first = Move("once", 3);
            Assert.That(first.Accepted, Is.True);
            Assert.That(Move("once", 3).MovedLotId, Is.EqualTo(first.MovedLotId));
            var forged = _world.Transfer("intruder", new TransferIntent { RequestId = "once", LotId = "lot-1", DestinationId = "kitchen", Quantity = 1 });
            Assert.That(forged.Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.Transfer("intruder", new TransferIntent { RequestId = "evil", LotId = "lot-1", DestinationId = "kitchen", Quantity = 1 }).Accepted, Is.False);
            Assert.That(_world.Transfer("intruder", new TransferIntent { RequestId = "unknown", LotId = "not-a-lot", DestinationId = "kitchen", Quantity = 1 }).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.View("intruder", "restaurant"), Is.Null);
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void ForeignRequestIdCannotPoisonAuthorizedActorEvenAfterRecovery()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var intent = new TransferIntent { RequestId = "shared", LotId = "lot-1", DestinationId = "kitchen", Quantity = 2 };
            Assert.That(_world.TransferDurably("intruder", intent, PathForSave).Reason, Is.EqualTo("forbidden"));
            var accepted = _world.TransferDurably("chef", intent, PathForSave);
            Assert.That(accepted.Accepted, Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.TransferDurably("intruder", intent, PathForSave).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.TransferDurably("chef", intent, PathForSave).MovedLotId, Is.EqualTo(accepted.MovedLotId));
            Assert.That(_world.Snapshot().Outcomes.Count(x => x.RequestId == "shared"), Is.EqualTo(2));
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void DurableReservationCancellationAndReplaySurviveRestartWithoutLoss()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var reserved = _world.ReserveDurably("chef", "claim", "lot-1", 8, PathForSave);
            Assert.That(reserved.Accepted, Is.True);
            Assert.That(reserved.ReservationId, Is.Not.Empty);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.ReserveDurably("chef", "claim", "lot-1", 8, PathForSave).ReservationId,
                Is.EqualTo(reserved.ReservationId));
            Assert.That(_world.TransferDurably("chef", new TransferIntent
            {
                RequestId = "blocked", LotId = "lot-1", DestinationId = "kitchen", Quantity = 3
            }, PathForSave).Accepted, Is.False);
            var badPath = Path.Combine(_saveDirectory, "missing", "save");
            Assert.That(_world.CancelDurably("chef", "release", reserved.ReservationId, badPath).Reason,
                Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Reservations.Single().Active, Is.True);
            var released = _world.CancelDurably("chef", "release", reserved.ReservationId, PathForSave);
            Assert.That(released.Accepted, Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.CancelDurably("chef", "release", reserved.ReservationId, PathForSave).Reason,
                Is.EqualTo("cancelled"));
            Assert.That(_world.Snapshot().Reservations.Single().Active, Is.False);
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void FailedReservationCommitCannotAcknowledgeOrLeaveClaim()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var failure = _world.ReserveDurably("chef", "claim", "lot-1", 4,
                Path.Combine(_saveDirectory, "missing", "save"));
            Assert.That(failure.Accepted, Is.False);
            Assert.That(failure.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Reservations, Is.Empty);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.ReserveDurably("chef", "claim", "lot-1", 4, PathForSave).Accepted, Is.True);
        }

        [Test]
        public void DurableClockAndExposureRecoverWithoutClientOrPresentation()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.TryAdvanceDurably(6, PathForSave), Is.True);
            Assert.That(_world.TryAdvanceDurably(3, Path.Combine(_saveDirectory, "missing", "save")), Is.False);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(6));
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().Lots.Single().ExposureSeconds, Is.EqualTo(6));
            var move = new TransferIntent { RequestId = "cold", LotId = "lot-1", DestinationId = "fridge", Quantity = 10 };
            Assert.That(_world.TransferDurably("chef", move, PathForSave).Accepted, Is.True);
            Assert.That(_world.TryAdvanceDurably(100, PathForSave), Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(106));
            Assert.That(_world.Snapshot().Lots.Single().ExposureSeconds, Is.EqualTo(6));
            Assert.That(_world.TransferDurably("chef", new TransferIntent
            {
                RequestId = "warm", LotId = "lot-1", DestinationId = "storage", Quantity = 10
            }, PathForSave).Accepted, Is.True);
            Assert.That(_world.TryAdvanceDurably(4, PathForSave), Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(110));
            Assert.That(_world.Snapshot().Lots.Single().Spoiled, Is.True);
            Assert.That(_world.Snapshot().Lots.Single().ExposureSeconds, Is.EqualTo(10));
        }

        [Test]
        public void SnapshotRestoresStableIdsTerminalOutcomesAndPendingReservation()
        {
            var first = Move("first", 2);
            Assert.That(_world.Reserve("chef", "pending", "lot-1", 5), Is.True);
            var rejection = Move("reject", 8);
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "first", LotId = "lot-1", DestinationId = "kitchen", Quantity = 2 }).MovedLotId, Is.EqualTo(first.MovedLotId));
            Assert.That(Move("reject", 8).Reason, Is.EqualTo(rejection.Reason));
            Assert.That(_world.Cancel("chef", "cancel", "pending").Accepted, Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Cancel("chef", "cancel", "pending").Accepted, Is.True);
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
            Assert.That(_world.Snapshot().Lots.Select(x => x.Id), Does.Contain(first.MovedLotId));
        }

        [Test]
        public void DurableTransferAcknowledgesOnlyPersistedMutationAndRollsBackOnFailure()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var intent = new TransferIntent { RequestId = "durable", LotId = "lot-1", DestinationId = "kitchen", Quantity = 2 };
            var failed = _world.TransferDurably("chef", intent, Path.Combine(_saveDirectory, "missing", "goods.db"));
            Assert.That(failed.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Lots.Single().Quantity, Is.EqualTo(10));
            var committed = _world.TransferDurably("chef", intent, PathForSave);
            Assert.That(committed.Accepted, Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.TransferDurably("chef", intent, PathForSave).MovedLotId, Is.EqualTo(committed.MovedLotId));
            Assert.That(_world.Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
        }

        [Test]
        public void OlderSnapshotCannotOverwriteCommittedTransfer()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var stale = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.TransferDurably("chef", new TransferIntent
            {
                RequestId = "commit", LotId = "lot-1", DestinationId = "kitchen", Quantity = 2
            }, PathForSave).Accepted, Is.True);
            Assert.Throws<IOException>(() => GoodsSnapshotStore.Save(stale, PathForSave));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Outcomes.Single().RequestId, Is.EqualTo("commit"));
        }

        [Test]
        public void CorruptLatestRecoversPreviousAndNewerSchemaIsRejected()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world.Advance(2);
            GoodsSnapshotStore.Save(_world, PathForSave);
            SnapshotDatabase.CorruptLatest(PathForSave);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().ClockSeconds, Is.Zero);
            var recovered = GoodsSnapshotStore.Load(PathForSave);
            recovered.Advance(3);
            GoodsSnapshotStore.Save(recovered, PathForSave);
            Assert.That(SnapshotDatabase.Quarantined(PathForSave), Is.EqualTo(1), "The damaged row is kept aside, not reused.");
            SnapshotDatabase.CorruptLatest(PathForSave, "corrupt-again");
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().ClockSeconds, Is.Zero,
                "The valid backup must survive a save after recovery.");
            var newer = _world.Snapshot();
            newer.SchemaVersion = GoodsSnapshot.CurrentSchema + 1;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(newer));
        }

        [Test]
        public void UnobservedSiteAdvancesWithoutCameraClientOrScene()
        {
            // This test constructs no GameObjects, cameras, subscriptions or connected clients.
            Assert.That(_world.View("intruder", "restaurant"), Is.Null);
            _world.Advance(10);
            Assert.That(_world.Snapshot().Lots.Single().Spoiled, Is.True);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(10));
        }
    }
}
