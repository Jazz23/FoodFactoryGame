// Verifies WAL saves and the held connection a served world commits through: durable per tick, readable while held,
// recovered after a refused commit, and fully closed on release.
using System;
using System.IO;
using NUnit.Framework;
using SQLite;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class HeldSaveTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryHeldSaveTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(PathForSave);
            Directory.Delete(_saveDirectory, true);
        }

        [Test]
        public void SavesUseWriteAheadLogging()
        {
            GoodsSnapshotStore.Save(new GoodsWorld("test-world"), PathForSave);

            using var db = new SQLiteConnection(PathForSave, SQLiteOpenFlags.ReadOnly);
            Assert.That(db.ExecuteScalar<string>("PRAGMA journal_mode"), Is.EqualTo("wal"));
        }

        [Test]
        public void HeldSaveCommitsEveryTickAndClosesOnRelease()
        {
            var world = new GoodsWorld("test-world");
            GoodsSnapshotStore.Save(world, PathForSave);
            GoodsSnapshotStore.Hold(PathForSave);
            GoodsSnapshotStore.Hold(PathForSave);

            for (var tick = 0; tick < 5; tick++) Assert.That(world.TryAdvanceDurably(1, PathForSave), Is.True);

            // Another connection reads each commit while the held one stays open.
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Revision, Is.EqualTo(world.Snapshot().Revision));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().ClockSeconds, Is.EqualTo(5));

            GoodsSnapshotStore.Release(PathForSave);
            Assert.That(File.Exists(PathForSave + "-wal"), Is.False, "The last close checkpoints and removes the WAL.");
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Revision, Is.EqualTo(world.Snapshot().Revision));
        }

        [Test]
        public void HeldSaveRecoversAfterARefusedCommit()
        {
            var world = new GoodsWorld("test-world");
            GoodsSnapshotStore.Save(world, PathForSave);
            var stale = GoodsSnapshotStore.Load(PathForSave);
            GoodsSnapshotStore.Hold(PathForSave);
            Assert.That(world.TryAdvanceDurably(2, PathForSave), Is.True);

            Assert.Throws<IOException>(() => GoodsSnapshotStore.Save(stale, PathForSave));

            Assert.That(world.TryAdvanceDurably(1, PathForSave), Is.True);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.Revision, Is.EqualTo(world.Snapshot().Revision));
            Assert.That(loaded.ClockSeconds, Is.EqualTo(3));
        }

        [Test]
        public void HoldNeedsAnExistingDatabaseAndReleaseIgnoresUnheldPaths()
        {
            Assert.Throws<FileNotFoundException>(() => GoodsSnapshotStore.Hold(PathForSave));
            Assert.DoesNotThrow(() => GoodsSnapshotStore.Release(PathForSave));
            Assert.DoesNotThrow(() => GoodsSnapshotStore.Release(null));
        }
    }
}
