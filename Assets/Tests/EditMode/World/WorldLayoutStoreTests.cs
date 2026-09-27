// Verifies layout persistence in world.db (decision 0026) on isolated temporary databases only: a stored layout reloads
// byte-for-byte, is written once and never replaced, cannot be added to an existing world, is refused (not regenerated) when
// damaged, and a database from before layouts (layout 1) loads unchanged with no layout until its next write upgrades it.
using System;
using System.IO;
using FoodFactoryGame.Goods;
using NUnit.Framework;
using SQLite;
using UnityEngine;

namespace FoodFactoryGame.World.Tests
{
    public sealed class WorldLayoutStoreTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryWorldTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static WorldLayout Layout(ulong seed = 11) => WorldGenerator.Generate(seed.ToString(), seed).Layout;

        private static GoodsWorld World()
        {
            var world = new GoodsWorld("layout-test-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "site", Kind = "storage", Capacity = 4 });
            return world;
        }

        private int UserVersion()
        {
            using var db = new SQLiteConnection(WorldPath, SQLiteOpenFlags.ReadOnly);
            return db.ExecuteScalar<int>("PRAGMA user_version");
        }

        [Test]
        public void AStoredLayoutReloadsExactlyAlongsideTheWorld()
        {
            var layout = Layout();
            var created = WorldLayoutStore.Create(WorldPath, "layout-test-world", layout);
            Assert.That(GoodsSnapshotStore.HasSnapshots(WorldPath), Is.False, "a layout-only database is a world still being created");
            var world = World();
            GoodsSnapshotStore.Save(world, WorldPath);

            var loaded = WorldLayoutStore.Load(WorldPath);
            Assert.That(loaded.Text, Is.EqualTo(WorldLayoutText.Write(layout)));
            Assert.That(loaded.Sha256, Is.EqualTo(WorldLayoutText.Hash(layout)).And.EqualTo(created.Sha256));
            Assert.That(loaded.WorldId, Is.EqualTo("layout-test-world"));
            Assert.That((loaded.Layout.GeneratorVersion, loaded.Layout.Seed, loaded.Layout.RequestedSeed),
                Is.EqualTo((WorldGenerator.Version, layout.Seed, "11")));
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(JsonUtility.ToJson(world.Snapshot())));
            Assert.That(UserVersion(), Is.EqualTo(GoodsSnapshotStore.DatabaseSchema).And.EqualTo(2));
        }

        [Test]
        public void ALayoutIsWrittenOnceAndNeverReplaced()
        {
            var first = WorldLayoutStore.Create(WorldPath, "layout-test-world", Layout(11));
            Assert.Throws<InvalidOperationException>(() => WorldLayoutStore.Create(WorldPath, "layout-test-world", Layout(12)));
            Assert.That(WorldLayoutStore.Load(WorldPath).Sha256, Is.EqualTo(first.Sha256));
        }

        [Test]
        public void AnExistingWorldCannotGainALayout()
        {
            GoodsSnapshotStore.Save(World(), WorldPath);
            Assert.Throws<InvalidOperationException>(() => WorldLayoutStore.Create(WorldPath, "layout-test-world", Layout()));
            Assert.That(WorldLayoutStore.Load(WorldPath), Is.Null);
        }

        [Test]
        public void ADamagedLayoutIsRefusedAndNotRegenerated()
        {
            WorldLayoutStore.Create(WorldPath, "layout-test-world", Layout());
            using (var db = new SQLiteConnection(WorldPath, SQLiteOpenFlags.ReadWrite))
                db.Execute("UPDATE world_layout SET payload = replace(payload, 'bounds 500', 'bounds 400')");
            Assert.Throws<InvalidOperationException>(() => WorldLayoutStore.Load(WorldPath));
            Assert.That(WorldLayoutStore.Load(Path.Combine(_directory, "missing.db")), Is.Null);
        }

        [Test]
        public void AnOldSaveLoadsUnchangedWithNoLayout()
        {
            // A save from before layouts: the same tables at database layout 1, no world_layout table.
            var world = World();
            GoodsSnapshotStore.Save(world, WorldPath);
            using (var db = new SQLiteConnection(WorldPath, SQLiteOpenFlags.ReadWrite))
            {
                db.Execute("DROP TABLE world_layout");
                db.Execute("PRAGMA user_version = 1");
            }
            var saved = JsonUtility.ToJson(world.Snapshot());
            Assert.That(UserVersion(), Is.EqualTo(1));

            Assert.That(WorldLayoutStore.Load(WorldPath), Is.Null);
            Assert.That(GoodsSnapshotStore.HasSnapshots(WorldPath), Is.True);
            var loaded = GoodsSnapshotStore.Load(WorldPath);
            Assert.That(JsonUtility.ToJson(loaded.Snapshot()), Is.EqualTo(saved));
            Assert.That(UserVersion(), Is.EqualTo(1), "reading never upgrades the database");

            // The next commit upgrades the database layout; the world is unchanged and still has no layout.
            GoodsSnapshotStore.Save(loaded, WorldPath);
            Assert.That(UserVersion(), Is.EqualTo(2));
            Assert.That(WorldLayoutStore.Load(WorldPath), Is.Null);
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(saved));
            Assert.Throws<InvalidOperationException>(() => WorldLayoutStore.Create(WorldPath, "layout-test-world", Layout()));
        }
    }
}
