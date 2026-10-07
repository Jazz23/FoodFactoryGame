// Verifies the company cash ledger (decision 0038): an accepted purchase writes one entry with its IDs in the same commit as the
// cash; a rejected request, a replayed request and a failed commit write nothing; retention keeps opening + carried + kept entries
// equal to cash; the ledger survives a save, restore and restart; a v19 save upgrades with an empty ledger whose carried total
// holds its cash; a view carries only the viewed site's company's entries; Validate refuses a ledger that does not balance.
// Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class LedgerTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        // TEST-ONLY values: restaurant "restaurant" owned by "co" (1000 cents), a rival site owned by "rival-co" (500 cents), the
        // chef's 3-slot inventory and an offer of 5 dough for 250 cents. Not gameplay content.
        [SetUp]
        public void SetUp()
        {
            _world = new GoodsWorld("test-world");
            _world.RegisterItem("dough", 10);
            _world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 10 });
            _world.Bootstrap(new GoodsLocation { Id = "rival-storage", SiteId = "rival", Kind = "storage", Capacity = 10 });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1000, SiteIds = { "restaurant" } });
            _world.Bootstrap(new GoodsCompany { Id = "rival-co", Cash = 500, SiteIds = { "rival" } });
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryLedgerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            Assert.That(_world.TryGrantDurably("chef", "restaurant", PathForSave, 3), Is.True);
            Assert.That(_world.TryGrantDurably("rival-chef", "rival", PathForSave, 3), Is.True);
            _world.RegisterOffer(new PurchaseOffer { Id = "dough-5", ItemId = "dough", Quantity = 5, PriceCents = 250, SpoilAfterSeconds = 100 });
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private static void AssertBalanced(GoodsSnapshot state)
        {
            foreach (var company in state.Companies)
                Assert.That(GoodsWorld.LedgerBalances(state, company), Is.True, $"{company.Id}: opening + carried + entries == cash");
        }

        [Test]
        public void ANewCompanyOpensItsLedgerWithItsStartingCash()
        {
            var state = _world.Snapshot();
            var co = state.Companies.Single(x => x.Id == "co");
            Assert.That((co.OpeningCents, co.LedgerCarriedCents, co.LedgerNextNumber, state.Ledger.Count), Is.EqualTo((1000L, 0L, 0L, 0)));
            AssertBalanced(state);
        }

        [Test]
        public void AnAcceptedPurchaseWritesOneEntryInTheSameCommit()
        {
            _world.Advance(7);
            var outcome = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave);
            Assert.That(outcome.Accepted, Is.True);
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var entry = saved.Ledger.Single();
            Assert.That((entry.Id, entry.CompanyId, entry.SiteId, entry.Kind, entry.Cents, entry.ClockSeconds, entry.RequestId, entry.OfferId, entry.EquipmentId, entry.CustomerId),
                Is.EqualTo(("co:1", "co", "restaurant", GoodsWorld.LedgerSupplierGoods, -250L, 7L, "buy-1", "dough-5", "", "")));
            Assert.That(entry.Revision, Is.LessThanOrEqualTo(outcome.Revision).And.GreaterThan(0));
            Assert.That(saved.Companies.Single(x => x.Id == "co").Cash, Is.EqualTo(750), "The cash and its entry were committed together.");
            AssertBalanced(saved);
        }

        [Test]
        public void RejectedReplayedAndFailedRequestsWriteNothing()
        {
            // Rejected: no grant on the site, an unknown offer, more than the inventory holds.
            Assert.That(_world.BuyDurably("rival-chef", "r1", "restaurant", "dough-5", PathForSave).Accepted, Is.False);
            Assert.That(_world.BuyDurably("chef", "r2", "restaurant", "nothing", PathForSave).Accepted, Is.False);
            Assert.That(_world.Snapshot().Ledger, Is.Empty);

            // Replayed: the duplicate request ID answers from the first outcome and writes no second entry, also after a restart.
            Assert.That(_world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            Assert.That(_world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            Assert.That(_world.Snapshot().Ledger.Count, Is.EqualTo(1));
            var reloaded = GoodsSnapshotStore.Load(PathForSave);
            reloaded.RegisterItem("dough", 10);
            reloaded.RegisterOffer(new PurchaseOffer { Id = "dough-5", ItemId = "dough", Quantity = 5, PriceCents = 250, SpoilAfterSeconds = 100 });
            Assert.That(reloaded.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            Assert.That(reloaded.Snapshot().Ledger.Count, Is.EqualTo(1));

            // Failed commit: cash, goods and ledger roll back together.
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.BuyDurably("chef", "buy-2", "restaurant", "dough-5", BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before));
            Assert.That(_world.AdjustCashDurably("co", 5, BadPath), Is.EqualTo("persistence-unavailable"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before));
            // A refused adjustment (more than the balance) writes nothing either.
            Assert.That(_world.AdjustCashDurably("co", -100_000, PathForSave), Is.EqualTo("insufficient-funds"));
            Assert.That(_world.Snapshot().Ledger.Count, Is.EqualTo(1));
            AssertBalanced(_world.Snapshot());
        }

        [Test]
        public void RetentionCarriesDroppedEntriesSoTheLedgerStillBalances()
        {
            var extra = 7;
            for (var index = 1; index <= GoodsWorld.LedgerEntriesKept + extra; index++)
            {
                Assert.That(_world.AdjustCashDurably("co", index % 3 == 0 ? -index : index, PathForSave), Is.Null);
                AssertBalanced(_world.Snapshot());
            }
            Assert.That(_world.AdjustCashDurably("rival-co", 11, PathForSave), Is.Null);
            var state = _world.Snapshot();
            var co = state.Companies.Single(x => x.Id == "co");
            var kept = state.Ledger.Where(x => x.CompanyId == "co").ToList();
            Assert.That(kept.Count, Is.EqualTo(GoodsWorld.LedgerEntriesKept));
            Assert.That(kept.First().Id, Is.EqualTo($"co:{extra + 1}"), "The oldest entries went first.");
            Assert.That(kept.Last().Id, Is.EqualTo($"co:{GoodsWorld.LedgerEntriesKept + extra}"));
            var dropped = Enumerable.Range(1, extra).Sum(x => x % 3 == 0 ? -x : (long)x);
            Assert.That(co.LedgerCarriedCents, Is.EqualTo(dropped));
            Assert.That(state.Ledger.Single(x => x.CompanyId == "rival-co").Id, Is.EqualTo("rival-co:1"), "Each company keeps its own entries.");
            Assert.That(GoodsWorld.LedgerOf(state, "co").First().Id, Is.EqualTo(kept.Last().Id), "Newest first.");
            AssertBalanced(GoodsSnapshotStore.Load(PathForSave).Snapshot());
        }

        [Test]
        public void TheLedgerSurvivesSaveRestoreAndRestart()
        {
            Assert.That(_world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            Assert.That(_world.AdjustCashDurably("co", 40, PathForSave), Is.Null);
            var live = _world.Snapshot();
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(JsonUtility.ToJson(loaded.Snapshot().Ledger), Is.EqualTo(JsonUtility.ToJson(live.Ledger)));
            Assert.That(JsonUtility.ToJson(GoodsWorld.Restore(live).Snapshot().Ledger), Is.EqualTo(JsonUtility.ToJson(live.Ledger)));
            // Numbering continues after the restart.
            Assert.That(loaded.AdjustCashDurably("co", 1, PathForSave), Is.Null);
            Assert.That(loaded.Snapshot().Ledger.Last().Id, Is.EqualTo("co:3"));
        }

        [Test]
        public void AV19SaveUpgradesWithAnEmptyLedgerAndItsCashCarried()
        {
            Assert.That(_world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            var cash = _world.Snapshot().Companies.Single(x => x.Id == "co").Cash;
            // The same save shaped as v19: no ledger list and no ledger fields on the companies.
            var payload = SnapshotDatabase.LatestPayload(PathForSave);
            var v19 = System.Text.RegularExpressions.Regex.Replace(payload, ",\"OpeningCents\":-?\\d+,\"LedgerCarriedCents\":-?\\d+,\"LedgerNextNumber\":\\d+", "");
            v19 = System.Text.RegularExpressions.Regex.Replace(v19, ",\"Ledger\":\\[.*?\\]\\}$", "}")
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":19");
            Assert.That(v19, Does.Not.Contain("Ledger").And.Not.Contain("OpeningCents"), "The payload is shaped like a v19 save.");
            SnapshotDatabase.WritePayload(PathForSave, v19);
            var upgraded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var co = upgraded.Companies.Single(x => x.Id == "co");
            Assert.That((upgraded.SchemaVersion, upgraded.Ledger.Count, co.Cash, co.OpeningCents, co.LedgerCarriedCents, co.LedgerNextNumber),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, 0, cash, 0L, cash, 0L)));
            AssertBalanced(upgraded);
            Assert.That(SnapshotDatabase.LatestPayload(PathForSave), Is.EqualTo(v19), "Loading upgrades in memory and writes nothing.");
        }

        [Test]
        public void AViewCarriesOnlyItsCompanysEntries()
        {
            Assert.That(_world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).Accepted, Is.True);
            Assert.That(_world.AdjustCashDurably("rival-co", 3, PathForSave), Is.Null);
            Assert.That(_world.View("chef", "restaurant").Ledger.Select(x => x.CompanyId).Distinct(), Is.EqualTo(new[] { "co" }));
            Assert.That(_world.View("rival-chef", "rival").Ledger.Select(x => x.CompanyId).Distinct(), Is.EqualTo(new[] { "rival-co" }));
        }

        [Test]
        public void ValidateRefusesALedgerThatDoesNotBalance()
        {
            Assert.That(_world.AdjustCashDurably("co", 10, PathForSave), Is.Null);
            Assert.DoesNotThrow(() => GoodsWorld.Validate(_world.Snapshot()));
            GoodsSnapshot Mutate(Action<GoodsSnapshot> change)
            {
                var state = _world.Snapshot();
                change(state);
                return state;
            }
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Companies[0].Cash += 1)), "cash without an entry");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Ledger.Clear())), "a lost entry");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Ledger.Add(JsonUtility.FromJson<GoodsLedgerEntry>(JsonUtility.ToJson(s.Ledger[0]))))),
                "a duplicated entry");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Validate(Mutate(s => s.Ledger[0].Kind = "")), "an entry without a kind");
        }
    }
}
