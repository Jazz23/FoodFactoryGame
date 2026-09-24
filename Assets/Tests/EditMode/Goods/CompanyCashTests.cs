// Verifies company cash rules (decision 0012): ownership invariants, whole-cent never-negative balances, rollback with the
// world snapshot, the v4 -> v5 payload upgrade in world.db, per-site visibility, and commit-cost measurement. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class CompanyCashTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string MissingPath => Path.Combine(_saveDirectory, "missing", "world.db");

        [SetUp]
        public void SetUp()
        {
            // TEST-ONLY values: two restaurants, one company owning the first with 1000 cents; not gameplay content.
            _world = new GoodsWorld("test-world");
            _world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            _world.Bootstrap(new GoodsLocation { Id = "rival-storage", SiteId = "rival", Kind = "storage", Capacity = 20 });
            _world.Bootstrap(Company("co", 1000, "restaurant"));
            _world.Grant("chef", "restaurant");
            _world.Grant("other", "rival");
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryGoodsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private static GoodsCompany Company(string id, long cash, params string[] sites) =>
            new() { Id = id, Cash = cash, SiteIds = sites.ToList() };

        private long Cash(string id = "co") => _world.Snapshot().Companies.Single(x => x.Id == id).Cash;

        [Test]
        public void BootstrapRejectsInvalidCompaniesWithoutChangingTheWorld()
        {
            var revision = _world.Snapshot().Revision;
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company(" ", 0, "rival")), "blank ID");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company("co", 0, "rival")), "duplicate ID");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company("new", -1, "rival")), "negative cash");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company("new", 0, "nowhere")), "unknown site");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company("new", 0, "restaurant")), "site already owned");
            Assert.Throws<ArgumentException>(() => _world.Bootstrap(Company("new", 0, "rival", "rival")), "site listed twice");
            Assert.That(_world.Snapshot().Revision, Is.EqualTo(revision));
            Assert.That(_world.Snapshot().Companies.Count, Is.EqualTo(1));
            _world.Bootstrap(Company("rival-co", 0, "rival"));
            Assert.That(_world.CompanyOfSite("rival"), Is.EqualTo("rival-co"));
            Assert.That(_world.CompanyOfSite("restaurant"), Is.EqualTo("co"));
        }

        [Test]
        public void CompaniesAndCashSurviveWorldDatabaseRoundTrip()
        {
            Assert.That(_world.AdjustCashDurably("co", 234, PathForSave), Is.Null);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            var company = loaded.Companies.Single();
            Assert.That((company.Id, company.Cash, company.SiteIds.Single()), Is.EqualTo(("co", 1234L, "restaurant")));
        }

        [Test]
        public void SchemaV4RowInWorldDatabaseLoadsAsCurrentWithoutCompanies()
        {
            var legacy = new GoodsWorld("legacy-world");
            legacy.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v4 = JsonUtility.ToJson(legacy.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":4").Replace(",\"Companies\":[]", "");
            Assert.That(v4, Does.Not.Contain("Companies"));
            SnapshotDatabase.WritePayload(PathForSave, v4);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(4), "The row looks like a real v4 commit.");

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Snapshot().Companies, Is.Empty);
            loaded.Bootstrap(Company("co", 50, "restaurant"));
            GoodsSnapshotStore.Save(loaded, PathForSave);
            Assert.That(SnapshotDatabase.LatestPayload(PathForSave), Does.Contain($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}"));
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Companies.Single().Cash, Is.EqualTo(50));
        }

        [Test]
        public void InvalidCompanyRowsAreRejectedAndQuarantined()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            _world.Advance(1);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var good = JsonUtility.ToJson(_world.Snapshot());

            var negative = good.Replace("\"Cash\":1000", "\"Cash\":-5");
            Assert.That(negative, Is.Not.EqualTo(good));
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(JsonUtility.FromJson<GoodsSnapshot>(negative)));
            var doubleOwned = JsonUtility.FromJson<GoodsSnapshot>(good);
            doubleOwned.Companies.Add(Company("thief", 0, "restaurant"));
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(doubleOwned));

            SnapshotDatabase.WritePayload(PathForSave, negative);
            var recovered = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(recovered.Snapshot().ClockSeconds, Is.EqualTo(0), "An invalid latest row falls back to the previous commit.");
            Assert.That(recovered.Snapshot().Companies.Single().Cash, Is.EqualTo(1000));
            GoodsSnapshotStore.Save(recovered, PathForSave);
            Assert.That(SnapshotDatabase.Quarantined(PathForSave), Is.EqualTo(1), "The invalid row is kept aside, not reused.");
        }

        [Test]
        public void SiteViewShowsOnlyTheOwningCompany()
        {
            _world.Bootstrap(Company("rival-co", 777, "rival"));
            Assert.That(_world.View("chef", "restaurant").Companies.Select(x => (x.Id, x.Cash)), Is.EqualTo(new[] { ("co", 1000L) }));
            Assert.That(_world.View("other", "rival").Companies.Select(x => (x.Id, x.Cash)), Is.EqualTo(new[] { ("rival-co", 777L) }));
            Assert.That(_world.View("chef", "rival"), Is.Null, "No grant, no view.");
        }

        [Test]
        public void CashNeverGoesNegativeOrOverflows()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.AdjustCashDurably("co", -1001, PathForSave), Is.EqualTo("insufficient-funds"));
            Assert.That(_world.AdjustCashDurably("co", 0, PathForSave), Is.EqualTo("invalid-amount"));
            Assert.That(_world.AdjustCashDurably("co", long.MinValue, PathForSave), Is.EqualTo("invalid-amount"));
            Assert.That(_world.AdjustCashDurably("nobody", 5, PathForSave), Is.EqualTo("unknown-company"));
            Assert.That(Cash(), Is.EqualTo(1000));
            Assert.That(_world.AdjustCashDurably("co", -1000, PathForSave), Is.Null);
            Assert.That(Cash(), Is.EqualTo(0));

            Assert.That(_world.AdjustCashDurably("co", long.MaxValue, PathForSave), Is.Null);
            var revision = _world.Snapshot().Revision;
            Assert.Throws<OverflowException>(() => _world.AdjustCashDurably("co", 1, PathForSave));
            Assert.That((Cash(), _world.Snapshot().Revision), Is.EqualTo((long.MaxValue, revision)), "Overflow restores the prior state.");
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Companies.Single().Cash, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void FailedCommitRestoresCash()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var revision = _world.Snapshot().Revision;
            Assert.That(_world.AdjustCashDurably("co", -400, MissingPath), Is.EqualTo("persistence-unavailable"));
            Assert.That((Cash(), _world.Snapshot().Revision), Is.EqualTo((1000L, revision)));
            Assert.That(_world.AdjustCashDurably("co", 400, MissingPath), Is.EqualTo("persistence-unavailable"));
            Assert.That(Cash(), Is.EqualTo(1000));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Companies.Single().Cash, Is.EqualTo(1000));
        }

        [Test]
        public void CommitStatsCountOnlyWrittenSavesAndReset()
        {
            var stats = GoodsSnapshotStore.Stats;
            var before = stats.Commits;
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(stats.Commits, Is.EqualTo(before + 1));
            Assert.That(stats.LastPayloadBytes, Is.EqualTo(Encoding.UTF8.GetByteCount(SnapshotDatabase.LatestPayload(PathForSave))));
            Assert.That(stats.LastMilliseconds, Is.GreaterThanOrEqualTo(0));
            Assert.That(stats.MaxMilliseconds, Is.GreaterThanOrEqualTo(stats.LastMilliseconds));
            Assert.That(stats.AverageMilliseconds, Is.GreaterThanOrEqualTo(0));

            Assert.That(_world.AdjustCashDurably("co", 1, MissingPath), Is.EqualTo("persistence-unavailable"));
            Assert.That(stats.Commits, Is.EqualTo(before + 1), "A failed save is not a commit.");
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(stats.Commits, Is.EqualTo(before + 1), "Saving an already stored revision writes nothing.");
            Assert.That(stats.Summary(), Does.StartWith("[Goods] commits="));

            stats.Reset();
            Assert.That((stats.Commits, stats.MaxMilliseconds, stats.LastPayloadBytes), Is.EqualTo((0L, 0d, 0)));
            Assert.That(_world.AdjustCashDurably("co", 1, PathForSave), Is.Null);
            Assert.That(stats.Commits, Is.EqualTo(1), "Counting restarts for the served world.");
        }
    }
}
