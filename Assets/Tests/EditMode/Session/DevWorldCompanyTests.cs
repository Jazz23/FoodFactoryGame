// Verifies the dev seed's company (decision 0012): a new world starts with it, an older save without one gains it exactly once,
// and restarts never add starting cash again. Isolated temporary saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class DevWorldCompanyTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);

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

        private static GoodsCompany Only(GoodsWorld world) => world.Snapshot().Companies.Single();

        [Test]
        public void NewWorldStartsWithTheDevCompany()
        {
            var world = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems());
            var company = Only(world);
            Assert.That((company.Id, company.Cash, company.SiteIds.Single()), Is.EqualTo((DevWorld.CompanyId, DevWorld.StartingCash, DevWorld.SiteId)));
            Assert.That(Only(GoodsSnapshotStore.Load(WorldPath)).Cash, Is.EqualTo(DevWorld.StartingCash), "The seed is committed.");
            var revision = world.Snapshot().Revision;
            var reopened = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems());
            Assert.That((Only(reopened).Cash, reopened.Snapshot().Revision), Is.EqualTo((DevWorld.StartingCash, revision)),
                "Reopening writes nothing and adds no cash.");
        }

        [Test]
        public void OlderSaveGainsTheDevCompanyExactlyOnce()
        {
            // A v4 dev world (before companies), imported through the pre-SQLite path with its dry run. Only the (emptied)
            // Companies entry is removed, so the fixture stays valid whatever fields later schemas add.
            var seeded = DevWorld.LoadOrCreate(Path.Combine(_directory, "seed.db"), items: SessionTestFiles.ContentItems()).Snapshot();
            seeded.Companies.Clear();
            var v4 = JsonUtility.ToJson(seeded).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":4").Replace(",\"Companies\":[]", "");
            Assert.That(v4, Does.Not.Contain("Companies"));
            Assert.That(v4, Does.Contain("\"SchemaVersion\":4"));
            var legacy = Path.Combine(_directory, SessionOptions.LegacyWorldFileName);
            SessionTestFiles.WriteLegacyWorld(legacy, v4);

            var upgraded = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), legacyWorldPath: legacy);
            Assert.That(Only(upgraded).Cash, Is.EqualTo(DevWorld.StartingCash));
            Assert.That(Only(GoodsSnapshotStore.Load(WorldPath)).Id, Is.EqualTo(DevWorld.CompanyId), "Committed before serving.");

            // Spend some, restart: the company is kept as it is and never topped up.
            Assert.That(upgraded.AdjustCashDurably(DevWorld.CompanyId, -100, WorldPath), Is.Null);
            var restarted = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), legacyWorldPath: legacy);
            Assert.That(restarted.Snapshot().Companies.Count, Is.EqualTo(1));
            Assert.That(Only(restarted).Cash, Is.EqualTo(DevWorld.StartingCash - 100));
        }
    }
}
