// Verifies supplier purchases (decisions 0014, 0017): the company pays and fresh goods, or a new machine held by the buyer,
// arrive in the buyer's inventory in one commit; a retried request is never charged twice; every rejection (grant, offer,
// company, inventory, grid, room, funds) changes nothing, not even the revision; a failed commit rolls back cash and goods
// together; a purchase survives reload. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class PurchaseTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "world.db");

        // TEST-ONLY values: a restaurant owned by "co" (1000 cents), chef's inventory of 3 slots (dough stacks to 10), an
        // ownerless stall, and offers of 5 dough for 250 cents, 1 oven-sized crate for 2000 cents and 1 oven machine for 600
        // cents. Not gameplay content.
        [SetUp]
        public void SetUp()
        {
            _world = new GoodsWorld("test-world");
            _world.RegisterItem("dough", 10);
            _world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 10 });
            _world.Bootstrap(new GoodsLocation { Id = "stall-storage", SiteId = "stall", Kind = "storage", Capacity = 10 });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1000, SiteIds = { "restaurant" } });
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryPurchaseTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            Assert.That(_world.TryGrantDurably("chef", "restaurant", PathForSave, 3), Is.True);
            Assert.That(_world.TryGrantDurably("vendor", "stall", PathForSave, 3), Is.True);
            _world.RegisterOffer(new PurchaseOffer { Id = "dough-5", ItemId = "dough", Quantity = 5, PriceCents = 250, SpoilAfterSeconds = 100 });
            _world.RegisterOffer(new PurchaseOffer { Id = "crate", ItemId = "crate", Quantity = 1, PriceCents = 2000, SpoilAfterSeconds = 100 });
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "oven", PriceCents = 600, Equipment = OvenTemplate() });
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private long Cash() => _world.Snapshot().Companies.Single().Cash;
        private int Dough() => _world.Snapshot().Lots.Where(x => x.LocationId == "carried:chef" && x.ItemId == "dough").Sum(x => x.Quantity);

        [Test]
        public void OffersMustBeValidContent()
        {
            Assert.Throws<ArgumentException>(() => _world.RegisterOffer(new PurchaseOffer { Id = "dough-5", ItemId = "dough", Quantity = 1, PriceCents = 1, SpoilAfterSeconds = 1 }), "duplicate");
            Assert.Throws<ArgumentException>(() => _world.RegisterOffer(new PurchaseOffer { Id = "free", ItemId = "dough", Quantity = 1, PriceCents = 0, SpoilAfterSeconds = 1 }), "free");
            Assert.Throws<ArgumentException>(() => _world.RegisterOffer(new PurchaseOffer { Id = "none", ItemId = "dough", Quantity = 0, PriceCents = 1, SpoilAfterSeconds = 1 }), "empty pack");
        }

        [Test]
        public void BuyingPaysAndDeliversFreshGoodsToTheBuyer()
        {
            var outcome = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((true, "bought")));
            var lot = _world.Snapshot().Lots.Single(x => x.Id == outcome.MovedLotId);
            Assert.That((lot.ItemId, lot.Quantity, lot.LocationId, lot.OwnerId, lot.ExposureSeconds, lot.Spoiled),
                Is.EqualTo(("dough", 5, "carried:chef", "restaurant", 0L, false)));
            Assert.That(Cash(), Is.EqualTo(750));
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.Companies.Single().Cash, saved.Lots.Any(x => x.Id == outcome.MovedLotId)), Is.EqualTo((750L, true)), "Committed together.");
        }

        [Test]
        public void RetriedRequestIsNeverChargedTwice()
        {
            var first = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave);
            var retry = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave);
            Assert.That((retry.Accepted, retry.MovedLotId, retry.Revision), Is.EqualTo((true, first.MovedLotId, first.Revision)));
            Assert.That((Cash(), Dough()), Is.EqualTo((750L, 5)));
            var reloaded = GoodsSnapshotStore.Load(PathForSave);
            reloaded.RegisterItem("dough", 10);
            reloaded.RegisterOffer(new PurchaseOffer { Id = "dough-5", ItemId = "dough", Quantity = 5, PriceCents = 250, SpoilAfterSeconds = 100 });
            Assert.That(reloaded.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave).MovedLotId, Is.EqualTo(first.MovedLotId),
                "The replay survives a restart.");
            Assert.That(reloaded.Snapshot().Companies.Single().Cash, Is.EqualTo(750));
        }

        [Test]
        public void RejectionsChangeNothing()
        {
            _world.Grant("guest", "restaurant");
            // The whole world, revision and outcomes included: a rejection records nothing, so it costs no commit.
            var before = JsonUtility.ToJson(_world.Snapshot());
            string Reason(string player, string request, string site, string offer) => _world.Buy(player, request, site, offer).Reason;
            Assert.That(Reason("chef", "a", "stall", "dough-5"), Is.EqualTo("forbidden"), "no grant for the site");
            Assert.That(Reason("chef", "b", "restaurant", "caviar"), Is.EqualTo("invalid-offer"));
            Assert.That(Reason("vendor", "c", "stall", "dough-5"), Is.EqualTo("no-company"));
            Assert.That(Reason("guest", "g", "restaurant", "dough-5"), Is.EqualTo("no-inventory"), "granted, but no inventory on the site");
            Assert.That(Reason("chef", "d", "restaurant", "crate"), Is.EqualTo("insufficient-funds"));
            Assert.That(Reason("", "e", "restaurant", "dough-5"), Is.EqualTo("invalid-identity"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before));

            // Three slots of 10 dough: after six packs (30 dough) a seventh does not fit. Enough cash for all seven first.
            Assert.That(_world.AdjustCashDurably("co", 1000, PathForSave), Is.Null);
            for (var index = 0; index < 6; index++) Assert.That(_world.Buy("chef", $"fill-{index}", "restaurant", "dough-5").Accepted, Is.True);
            var full = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(Reason("chef", "full", "restaurant", "dough-5"), Is.EqualTo("capacity"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(full));
            Assert.That(Dough(), Is.EqualTo(30));
        }

        [Test]
        public void RejectedRequestRetriedLaterIsChargedAtMostOnce()
        {
            Assert.That(_world.BuyDurably("chef", "crate-1", "restaurant", "crate", PathForSave).Reason, Is.EqualTo("insufficient-funds"));
            Assert.That(_world.AdjustCashDurably("co", 1000, PathForSave), Is.Null);
            Assert.That(_world.BuyDurably("chef", "crate-1", "restaurant", "crate", PathForSave).Reason, Is.EqualTo("bought"),
                "A rejection was not recorded, so the retry is evaluated afresh.");
            Assert.That(_world.BuyDurably("chef", "crate-1", "restaurant", "crate", PathForSave).Reason, Is.EqualTo("bought"));
            Assert.That(Cash(), Is.EqualTo(0), "Charged once.");
        }

        [Test]
        public void FailedCommitRollsBackCashAndGoods()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var failed = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", BadPath);
            Assert.That(failed.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That((Cash(), Dough()), Is.EqualTo((1000L, 0)));
            var retried = _world.BuyDurably("chef", "buy-1", "restaurant", "dough-5", PathForSave);
            Assert.That(retried.Accepted, Is.True, "A failed commit records nothing, so the same request can be retried.");
            Assert.That((Cash(), Dough()), Is.EqualTo((750L, 5)));
        }

        // TEST-ONLY machine: 3x3 cells, one input and one output slot; the restaurant has no grid until a test adds one.
        private static GoodsEquipment OvenTemplate() => new() { Kind = "oven", Width = 3, Depth = 3, InputCapacity = 1, OutputCapacity = 1 };
        private void AddLayout() => _world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });

        [Test]
        public void EquipmentOffersMustBeValidContent()
        {
            Assert.Throws<ArgumentException>(() => _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "dough-5", PriceCents = 1, Equipment = OvenTemplate() }),
                "goods and equipment offers share one ID space");
            Assert.Throws<ArgumentException>(() => _world.RegisterOffer(new PurchaseOffer { Id = "oven", ItemId = "dough", Quantity = 1, PriceCents = 1, SpoilAfterSeconds = 1 }),
                "the other way round too");
            Assert.Throws<ArgumentException>(() => _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "free", PriceCents = 0, Equipment = OvenTemplate() }), "free");
            Assert.Throws<ArgumentException>(() => _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "nothing", PriceCents = 1 }), "no machine");
            var flat = OvenTemplate();
            flat.Width = 0;
            Assert.Throws<ArgumentException>(() => _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "flat", PriceCents = 1, Equipment = flat }), "no footprint");
        }

        [Test]
        public void BuyingEquipmentPaysAndDeliversAHeldMachineThatCanBePlaced()
        {
            AddLayout();
            var outcome = _world.BuyDurably("chef", "oven-1", "restaurant", "oven", PathForSave);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((true, "bought")));
            Assert.That(outcome.MovedLotId, Is.Null.Or.Empty, "No goods move.");
            var machine = _world.Snapshot().Equipment.Single(x => x.Id == outcome.EquipmentId);
            Assert.That((machine.Kind, machine.SiteId, machine.State, machine.HolderId, machine.Width, machine.Depth),
                Is.EqualTo(("oven", "restaurant", EquipmentState.Held, "chef", 3, 3)));
            Assert.That(_world.Snapshot().Stations, Is.Empty, "A held machine has no station until it is placed.");
            Assert.That(Cash(), Is.EqualTo(400));
            var saved = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((saved.Companies.Single().Cash, saved.Equipment.Any(x => x.Id == outcome.EquipmentId)), Is.EqualTo((400L, true)), "Committed together.");

            // The template is copied, not shared: a second machine is a separate piece with its own ID.
            Assert.That(_world.AdjustCashDurably("co", 600, PathForSave), Is.Null);
            var second = _world.BuyDurably("chef", "oven-2", "restaurant", "oven", PathForSave);
            Assert.That((second.Accepted, _world.Snapshot().Equipment.Count), Is.EqualTo((true, 2)));
            Assert.That(second.EquipmentId, Is.Not.EqualTo(outcome.EquipmentId));

            var placed = _world.PlaceDurably("chef", "place-1", outcome.EquipmentId, 0, 0, 0, PathForSave);
            Assert.That(placed.Reason, Is.EqualTo("placed"));
            Assert.That(_world.Snapshot().Stations.Single().Id, Is.EqualTo(outcome.EquipmentId), "Placed like any picked-up machine.");
        }

        [Test]
        public void RetriedEquipmentRequestIsNeverChargedTwice()
        {
            AddLayout();
            var first = _world.BuyDurably("chef", "oven-1", "restaurant", "oven", PathForSave);
            var retry = _world.BuyDurably("chef", "oven-1", "restaurant", "oven", PathForSave);
            Assert.That((retry.Accepted, retry.EquipmentId, retry.Revision), Is.EqualTo((true, first.EquipmentId, first.Revision)));
            Assert.That((Cash(), _world.Snapshot().Equipment.Count), Is.EqualTo((400L, 1)));
            var reloaded = GoodsSnapshotStore.Load(PathForSave);
            reloaded.RegisterEquipmentOffer(new EquipmentOffer { Id = "oven", PriceCents = 600, Equipment = OvenTemplate() });
            Assert.That(reloaded.BuyDurably("chef", "oven-1", "restaurant", "oven", PathForSave).EquipmentId, Is.EqualTo(first.EquipmentId),
                "The replay survives a restart.");
            Assert.That((reloaded.Snapshot().Companies.Single().Cash, reloaded.Snapshot().Equipment.Count), Is.EqualTo((400L, 1)));
        }

        [Test]
        public void EquipmentRejectionsChangeNothing()
        {
            _world.Grant("guest", "restaurant");
            var before = JsonUtility.ToJson(_world.Snapshot());
            string Reason(string player, string request, string site) => _world.Buy(player, request, site, "oven").Reason;
            Assert.That(Reason("chef", "a", "restaurant"), Is.EqualTo("no-layout"), "nowhere to place it");
            Assert.That(Reason("chef", "b", "stall"), Is.EqualTo("forbidden"));
            Assert.That(Reason("vendor", "c", "stall"), Is.EqualTo("no-company"));
            Assert.That(Reason("guest", "d", "restaurant"), Is.EqualTo("no-inventory"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before));

            AddLayout();
            Assert.That(_world.Buy("chef", "e", "restaurant", "oven").Accepted, Is.True);
            var afterOne = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(Reason("chef", "f", "restaurant"), Is.EqualTo("insufficient-funds"), "400 cents left for a 600-cent oven");
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(afterOne));
        }

        [Test]
        public void HeldMachinesNeedNoInventoryRoom()
        {
            AddLayout();
            // Three slots of 10 dough fill the inventory; a machine still arrives (decision 0006: held machines are uncapped).
            Assert.That(_world.AdjustCashDurably("co", 2000, PathForSave), Is.Null);
            for (var index = 0; index < 6; index++) Assert.That(_world.Buy("chef", $"fill-{index}", "restaurant", "dough-5").Accepted, Is.True);
            Assert.That(_world.Buy("chef", "full", "restaurant", "dough-5").Reason, Is.EqualTo("capacity"));
            Assert.That(_world.Buy("chef", "oven-1", "restaurant", "oven").Reason, Is.EqualTo("bought"));
        }

        [Test]
        public void FailedEquipmentCommitRollsBackCashAndMachine()
        {
            AddLayout();
            GoodsSnapshotStore.Save(_world, PathForSave);
            var failed = _world.BuyDurably("chef", "oven-1", "restaurant", "oven", BadPath);
            Assert.That(failed.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That((Cash(), _world.Snapshot().Equipment.Count), Is.EqualTo((1000L, 0)));
            Assert.That(_world.BuyDurably("chef", "oven-1", "restaurant", "oven", PathForSave).Accepted, Is.True, "The same request can be retried.");
            Assert.That((Cash(), _world.Snapshot().Equipment.Count), Is.EqualTo((400L, 1)));
        }
    }
}
