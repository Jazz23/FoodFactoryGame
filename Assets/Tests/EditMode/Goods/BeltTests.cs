// Verifies conveyor belts: placement from inventory and free re-orientation, grid sharing with equipment, belt shapes and
// links (curves, side-loading), item movement, spacing and queuing, loops, taking single items off, removal returning
// everything, and schema v4.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class BeltTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;
        private int _request;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryBeltTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: an 8x6-cell site with a 1x1 counter at (7,5); the chef carries 10 belts and 6 dough in 4 slots,
        // the sous has an empty 4-slot inventory. Belts stack to 100 and dough to 20 here. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.RegisterItem(GoodsWorld.BeltItemId, 100);
            world.RegisterItem("dough", 20);
            world.Bootstrap(new GoodsLocation { Id = "pantry", SiteId = "site", Kind = "storage", Capacity = 10 });
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "site", Kind = "carried", Capacity = 4 });
            world.Bootstrap(new GoodsLocation { Id = "carried:sous", SiteId = "site", Kind = "carried", Capacity = 4 });
            world.Bootstrap(new GoodsLot { Id = "belts", ItemId = GoodsWorld.BeltItemId, OwnerId = "site", LocationId = "carried:chef", Quantity = 10, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds });
            world.Bootstrap(new GoodsLot { Id = "dough", ItemId = "dough", OwnerId = "site", LocationId = "carried:chef", Quantity = 6, SpoilAfterSeconds = 100000 });
            world.Bootstrap(new SiteLayout { SiteId = "site", Width = 8, Depth = 6 });
            world.Bootstrap(new GoodsEquipment { Id = "counter", Kind = "counter", SiteId = "site", CellX = 7, CellZ = 5, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1 });
            world.Grant("chef", "site");
            world.Grant("sous", "site");
            return world;
        }

        private string Next() => "r" + _request++;

        private GoodsOutcome Belt(int x, int z, int direction, string player = "chef") =>
            _world.PlaceBelt(player, Next(), "site", x, z, direction);

        private GoodsBelt BeltAt(int x, int z) => _world.Snapshot().Belts.SingleOrDefault(b => b.CellX == x && b.CellZ == z);

        private int Carried(string itemId, string player = "chef") =>
            _world.Snapshot().Lots.Where(x => x.LocationId == "carried:" + player && x.ItemId == itemId).Sum(x => x.Quantity);

        private GoodsLot[] Riding() => _world.Snapshot().Lots.Where(x => x.LocationId.EndsWith(":items", StringComparison.Ordinal)).ToArray();

        private GoodsOutcome Drop(int x, int z) => _world.PlaceOnBelt("chef", Next(), _world.Snapshot().Lots
            .Where(l => l.LocationId == "carried:chef" && l.ItemId == "dough").OrderBy(l => l.Id).First().Id, BeltAt(x, z).Id);

        [Test]
        public void PlacingConsumesOneBeltAndDraggingOverABeltOnlyTurnsIt()
        {
            var placed = Belt(0, 0, 1);
            Assert.That((placed.Accepted, placed.Reason), Is.EqualTo((true, "placed")));
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(9));
            var belt = BeltAt(0, 0);
            Assert.That(belt.Direction, Is.EqualTo(1));
            Assert.That(_world.Snapshot().Locations.Single(x => x.Id == belt.LocationId).Kind, Is.EqualTo(GoodsWorld.BeltLocationKind));

            Assert.That(Belt(0, 0, 1).Reason, Is.EqualTo("unchanged"));
            Assert.That(Belt(0, 0, 2).Reason, Is.EqualTo("rotated"));
            Assert.That((BeltAt(0, 0).Id, BeltAt(0, 0).Direction), Is.EqualTo((belt.Id, 2)), "Turning keeps the belt's identity.");
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(9), "Turning an existing belt costs nothing.");
            Assert.That(_world.View("sous", "site").Belts.Single().Id, Is.EqualTo(belt.Id));
        }

        [Test]
        public void RejectedPlacementsChangeNothing()
        {
            var before = JsonUtility.ToJson(_world.Snapshot().Lots) + JsonUtility.ToJson(_world.Snapshot().Belts);
            Assert.That(Belt(-1, 0, 0).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(Belt(8, 0, 0).Reason, Is.EqualTo("out-of-bounds"));
            Assert.That(Belt(7, 5, 0).Reason, Is.EqualTo("blocked"), "Equipment blocks belts.");
            Assert.That(Belt(0, 0, 4).Reason, Is.EqualTo("invalid-rotation"));
            Assert.That(Belt(0, 0, 0, "intruder").Reason, Is.EqualTo("forbidden"));
            Assert.That(Belt(0, 0, 0, "sous").Reason, Is.EqualTo("no-belts"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot().Lots) + JsonUtility.ToJson(_world.Snapshot().Belts), Is.EqualTo(before));

            // Belts block equipment in turn.
            Assert.That(Belt(6, 5, 0).Accepted, Is.True);
            Assert.That(_world.PickUp("chef", Next(), "counter").Accepted, Is.True);
            Assert.That(_world.Place("chef", Next(), "counter", 6, 5, 0).Reason, Is.EqualTo("blocked"));
            Assert.That(_world.Place("chef", Next(), "counter", 5, 5, 0).Accepted, Is.True);
        }

        [Test]
        public void ShapesAndLinksFollowFactorioRules()
        {
            // A (0,1) east feeds B (1,1) north from B's left: B curves and A enters it at 0. E (1,2) is in front of B.
            Belt(0, 1, 1);
            Belt(1, 1, 0);
            Belt(1, 2, 0);
            var cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(1, 1)), Is.EqualTo(BeltShape.CurveFromLeft));
            var link = BeltRules.Link(cells, BeltAt(0, 1));
            Assert.That((link.Next.Id, link.Entry), Is.EqualTo((BeltAt(1, 1).Id, 0)));
            Assert.That(BeltRules.Link(cells, BeltAt(1, 1)).Next.Id, Is.EqualTo(BeltAt(1, 2).Id));

            // C (1,0) north behind B straightens it; A now side-loads at B's middle.
            Belt(1, 0, 0);
            cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(1, 1)), Is.EqualTo(BeltShape.Straight));
            Assert.That(BeltRules.Link(cells, BeltAt(0, 1)).Entry, Is.EqualTo(BeltRules.Middle));

            // Without C, and with D (2,1) west on B's right, B is fed from both sides and stays straight.
            Assert.That(_world.RemoveBelt("chef", Next(), BeltAt(1, 0).Id).Accepted, Is.True);
            Belt(2, 1, 3);
            cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(1, 1)), Is.EqualTo(BeltShape.Straight), "Fed from both sides.");

            // A turned away leaves B fed from the right only: it curves the other way and D enters it at 0.
            Belt(0, 1, 2);
            cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(1, 1)), Is.EqualTo(BeltShape.CurveFromRight));
            Assert.That(BeltRules.Link(cells, BeltAt(2, 1)).Entry, Is.EqualTo(0));

            // Head-on belts never link.
            Belt(4, 0, 1);
            Belt(5, 0, 3);
            cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Link(cells, BeltAt(4, 0)).Next, Is.Null);
        }

        [Test]
        public void SideLoadingEntersAtTheMiddle()
        {
            Belt(1, 0, 0);
            Belt(1, 1, 0);
            Belt(1, 2, 0);
            Belt(0, 1, 1);
            var cells = BeltRules.ByCell(_world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(1, 1)), Is.EqualTo(BeltShape.Straight));
            Assert.That(BeltRules.Link(cells, BeltAt(0, 1)).Entry, Is.EqualTo(BeltRules.Middle));
            Assert.That(Drop(0, 1).Accepted, Is.True);
            // Half a tile to the edge, then in at (1,1)'s middle and half a tile on: the start of (1,2). Entering at 0
            // instead would have left it in the middle of (1,1).
            _world.Advance(1);
            var item = Riding().Single();
            Assert.That((item.LocationId, item.BeltPosition), Is.EqualTo((BeltAt(1, 2).LocationId, 0)));
        }

        [Test]
        public void ItemsTravelOneTilePerSecondAndQueueAtTheEnd()
        {
            for (var x = 0; x < 4; x++) Belt(x, 0, 1);
            var placed = Drop(0, 0);
            Assert.That(placed.Reason, Is.EqualTo("placed-on-belt"));
            Assert.That(Carried("dough"), Is.EqualTo(5), "Exactly one unit leaves the inventory.");
            var item = Riding().Single();
            Assert.That((item.Id, item.Quantity, item.BeltPosition), Is.EqualTo((placed.MovedLotId, 1, BeltRules.Middle)));

            _world.Advance(1);
            item = Riding().Single();
            Assert.That((item.LocationId, item.BeltPosition), Is.EqualTo((BeltAt(1, 0).LocationId, BeltRules.Middle)));

            // Five more drops, one per second, then plenty of time: the line compresses at the last belt, nothing is lost.
            for (var index = 0; index < 5; index++)
            {
                Assert.That(Drop(0, 0).Accepted, Is.True);
                _world.Advance(1);
            }
            _world.Advance(30);
            var riding = Riding();
            Assert.That(riding.Length, Is.EqualTo(6));
            Assert.That(Carried("dough"), Is.EqualTo(0));
            var last = riding.Where(x => x.LocationId == BeltAt(3, 0).LocationId).Select(x => x.BeltPosition).OrderByDescending(x => x).ToArray();
            Assert.That(last, Is.EqualTo(new[] { BeltRules.EndRest, 150, 90, 30 }));
            var before = riding.Where(x => x.LocationId == BeltAt(2, 0).LocationId).Select(x => x.BeltPosition).OrderByDescending(x => x).ToArray();
            Assert.That(before, Is.EqualTo(new[] { 210, 150 }), "Spacing holds across the belt boundary.");
        }

        [Test]
        public void OneLongStepMatchesManyShortOnes()
        {
            for (var x = 0; x < 5; x++) Belt(x, 0, 1);
            Belt(5, 0, 0);
            Belt(5, 1, 0);
            Drop(0, 0);
            Drop(0, 0);
            Drop(0, 0);
            var other = GoodsWorld.Restore(_world.Snapshot());
            _world.Advance(7);
            for (var index = 0; index < 7; index++) other.Advance(1);
            Assert.That(JsonUtility.ToJson(other.Snapshot().Lots), Is.EqualTo(JsonUtility.ToJson(_world.Snapshot().Lots)));
        }

        [Test]
        public void ALoopKeepsCirculating()
        {
            Belt(0, 0, 1);
            Belt(1, 0, 0);
            Belt(1, 1, 3);
            Belt(0, 1, 2);
            for (var index = 0; index < 4; index++)
            {
                Assert.That(Drop(index % 2, 0).Accepted, Is.True);
                _world.Advance(1);
            }
            var start = Riding().ToDictionary(x => x.Id, x => (x.LocationId, x.BeltPosition));
            _world.Advance(3);
            var after = Riding();
            Assert.That(after.Length, Is.EqualTo(4));
            Assert.That(after.All(x => start[x.Id] != (x.LocationId, x.BeltPosition)), Is.True, "Every item moved.");
        }

        [Test]
        public void GoodsOnlyEnterAndLeaveBeltsThroughBeltCommands()
        {
            Belt(0, 0, 1);
            var belt = BeltAt(0, 0);
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = Next(), LotId = "dough", DestinationId = belt.LocationId, Quantity = 1 }).Reason,
                Is.EqualTo("invalid-route"));
            var moved = Drop(0, 0).MovedLotId;
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = Next(), LotId = moved, DestinationId = "pantry", Quantity = 1 }).Reason,
                Is.EqualTo("invalid-route"));
            Assert.That(_world.PlaceOnBelt("chef", Next(), moved, belt.Id).Reason, Is.EqualTo("invalid-route"));
            Assert.That(_world.PlaceOnBelt("intruder", Next(), "dough", belt.Id).Reason, Is.EqualTo("forbidden"));
            Assert.That(Drop(0, 0).Accepted, Is.True);
            Assert.That(Drop(0, 0).Accepted, Is.True);
            Assert.That(Drop(0, 0).Reason, Is.EqualTo("belt-full"));
            Assert.That(Riding().Select(x => x.BeltPosition).OrderBy(x => x), Is.EqualTo(new[] { 60, 120, 180 }));
        }

        [Test]
        public void TakingAnItemOffABeltReturnsThatUnitOrNothing()
        {
            Belt(0, 0, 1);
            Belt(1, 0, 1);
            var riding = Drop(0, 0).MovedLotId;
            _world.Advance(1);
            Assert.That(Riding().Single().LocationId, Is.EqualTo(BeltAt(1, 0).LocationId), "It moved on before being taken.");

            Assert.That(_world.TakeFromBelt("intruder", Next(), riding).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.TakeFromBelt("chef", Next(), "dough").Reason, Is.EqualTo("not-on-belt"));
            Assert.That(_world.TakeFromBelt("chef", Next(), "no-such-lot").Reason, Is.EqualTo("forbidden"));

            // A failed commit and a full inventory leave the item riding.
            GoodsSnapshotStore.Save(_world, PathForSave);
            var bad = Path.Combine(_saveDirectory, "missing", "goods.db");
            Assert.That(_world.TakeFromBeltDurably("chef", "take-bad", riding, bad).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Riding().Single().Id, Is.EqualTo(riding));
            Assert.That(_world.TakeFromBelt("sous", Next(), riding).Reason, Is.EqualTo("taken-from-belt"), "Any granted player may take it.");
            Assert.That(Carried("dough", "sous"), Is.EqualTo(1));
            Assert.That(Riding(), Is.Empty);

            Drop(1, 0);
            var second = Riding().Single().Id;
            foreach (var item in new[] { "flour", "salt", "sugar" })
                _world.Bootstrap(new GoodsLot { Id = item, ItemId = item, OwnerId = "site", LocationId = "carried:sous", Quantity = 1, SpoilAfterSeconds = 10 });
            Assert.That(Carried("dough", "sous"), Is.EqualTo(1), "The sous's four slots are full: dough, flour, salt, sugar.");
            Assert.That(_world.TakeFromBelt("sous", Next(), second).Accepted, Is.True, "Dough tops up the sous's dough slot.");
            Assert.That(Drop(1, 0).Accepted, Is.True);
            var third = Riding().Single().Id;
            Assert.That(_world.TakeFromBeltDurably("chef", "take", third, PathForSave).Reason, Is.EqualTo("taken-from-belt"));
            var taken = _world.Snapshot().Lots.Single(x => x.Id == third);
            Assert.That((taken.LocationId, taken.BeltPosition, taken.Quantity), Is.EqualTo(("carried:chef", 0, 1)), "Same ID, off the belt.");
            Assert.That(_world.TakeFromBeltDurably("chef", "take", third, PathForSave).Accepted, Is.True, "Replay returns the stored outcome.");
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Lots.Single(x => x.Id == third).LocationId, Is.EqualTo("carried:chef"));
        }

        [Test]
        public void TakingFromABeltIntoAFullInventoryChangesNothing()
        {
            Belt(0, 0, 1);
            // The chef keeps only belts, then fills the other three slots; the bread riding the belt would need a fifth.
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = Next(), LotId = "dough", DestinationId = "pantry", Quantity = 6 }).Accepted, Is.True);
            _world.Bootstrap(new GoodsLot { Id = "ride", ItemId = "bread", OwnerId = "site", LocationId = "pantry", Quantity = 1, SpoilAfterSeconds = 100 });
            Assert.That(_world.PlaceOnBelt("chef", Next(), "ride", BeltAt(0, 0).Id).Accepted, Is.True);
            foreach (var item in new[] { "flour", "salt", "sugar" })
                _world.Bootstrap(new GoodsLot { Id = item, ItemId = item, OwnerId = "site", LocationId = "carried:chef", Quantity = 1, SpoilAfterSeconds = 10 });
            var before = JsonUtility.ToJson(_world.Snapshot().Lots);
            Assert.That(_world.TakeFromBelt("chef", Next(), "ride").Reason, Is.EqualTo("capacity"), "Bread needs a fifth slot.");
            Assert.That(JsonUtility.ToJson(_world.Snapshot().Lots), Is.EqualTo(before));
        }

        [Test]
        public void RemovingABeltReturnsItAndItsItemsOrNothing()
        {
            Belt(0, 0, 1);
            Drop(0, 0);
            Drop(0, 0);
            var beltId = BeltAt(0, 0).Id;
            var riding = Riding().Select(x => x.Id).ToArray();

            // Stow the remaining dough and fill the chef's four slots with belts, flour, salt and sugar, so the returning
            // dough needs a slot that is not there.
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = Next(), LotId = "dough", DestinationId = "pantry", Quantity = 4 }).Accepted, Is.True);
            foreach (var item in new[] { "flour", "salt", "sugar" })
                _world.Bootstrap(new GoodsLot { Id = item, ItemId = item, OwnerId = "site", LocationId = "carried:chef", Quantity = 1, SpoilAfterSeconds = 10 });
            var before = JsonUtility.ToJson(_world.Snapshot().Lots) + JsonUtility.ToJson(_world.Snapshot().Belts);
            Assert.That(_world.RemoveBelt("chef", Next(), beltId).Reason, Is.EqualTo("capacity"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot().Lots) + JsonUtility.ToJson(_world.Snapshot().Belts), Is.EqualTo(before));

            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = Next(), LotId = "salt", DestinationId = "pantry", Quantity = 1 }).Accepted, Is.True);
            Assert.That(_world.RemoveBelt("intruder", Next(), beltId).Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.RemoveBelt("chef", Next(), beltId).Reason, Is.EqualTo("removed"));
            var state = _world.Snapshot();
            Assert.That(state.Belts, Is.Empty);
            Assert.That(state.Locations.Any(x => x.Kind == GoodsWorld.BeltLocationKind), Is.False);
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(10), "The belt item comes back.");
            Assert.That(riding.All(id => state.Lots.Single(x => x.Id == id).LocationId == "carried:chef"), Is.True, "Riding items keep their IDs.");
        }

        [Test]
        public void BeltsAndRidingItemsSurviveSaveAndFailedCommits()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var bad = Path.Combine(_saveDirectory, "missing", "goods.db");
            Assert.That(_world.PlaceBeltDurably("chef", "place", "site", 0, 0, 1, bad).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Belts, Is.Empty);
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(10));
            Assert.That(_world.PlaceBeltDurably("chef", "place", "site", 0, 0, 1, PathForSave).Accepted, Is.True);
            Assert.That(_world.PlaceBeltDurably("chef", "place2", "site", 1, 0, 1, PathForSave).Accepted, Is.True);
            var lotId = _world.Snapshot().Lots.First(x => x.ItemId == "dough").Id;
            Assert.That(_world.PlaceOnBeltDurably("chef", "drop", lotId, BeltAt(0, 0).Id, PathForSave).Accepted, Is.True);
            Assert.That(_world.TryAdvanceDurably(1, PathForSave), Is.True);

            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.Belts.Count, Is.EqualTo(2));
            var item = loaded.Lots.Single(x => x.LocationId.EndsWith(":items", StringComparison.Ordinal));
            Assert.That((item.LocationId, item.BeltPosition), Is.EqualTo((BeltAt(1, 0).LocationId, BeltRules.Middle)));
            Assert.That(_world.RemoveBeltDurably("chef", "remove", BeltAt(1, 0).Id, bad).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_world.Snapshot().Belts.Count, Is.EqualTo(2));
        }

        [Test]
        public void SchemaV3SaveLoadsAsV4WithoutBelts()
        {
            var current = JsonUtility.ToJson(_world.Snapshot());
            var v3 = current.Replace("\"SchemaVersion\":4", "\"SchemaVersion\":3").Replace(",\"BeltPosition\":0", "").Replace(",\"Belts\":[]", "");
            Assert.That(v3, Does.Not.Contain("Belt\""));
            var legacyPath = Path.Combine(_saveDirectory, "legacy.snapshot");
            SnapshotDatabase.WriteLegacy(legacyPath, v3);
            GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, true);
            Assert.That(File.Exists(PathForSave), Is.False, "A dry run writes nothing.");
            GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, false);
            Assert.Throws<IOException>(() => GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, false));
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(4));
            Assert.That(loaded.Snapshot().Belts, Is.Empty);
            Assert.That(loaded.TryAdvanceDurably(1, PathForSave), Is.True);
            Assert.That(SnapshotDatabase.LatestPayload(PathForSave), Does.Contain("\"SchemaVersion\":4"));
        }

        [Test]
        public void ValidateRejectsInconsistentBelts()
        {
            Belt(0, 0, 1);
            Drop(0, 0);
            Assert.DoesNotThrow(() => GoodsWorld.Restore(_world.Snapshot()));

            GoodsSnapshot Mutate(Action<GoodsSnapshot> change)
            {
                var copy = _world.Snapshot();
                change(copy);
                return copy;
            }

            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => { s.Belts[0].CellX = 7; s.Belts[0].CellZ = 5; })), "belt on equipment");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Belts[0].CellX = 8)), "belt out of bounds");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Belts[0].Direction = 4)), "bad direction");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Belts.Add(JsonUtility.FromJson<GoodsBelt>(JsonUtility.ToJson(s.Belts[0]))))), "duplicate belt");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Belts.Clear())), "belt location without a belt");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Lots.Single(x => x.BeltPosition > 0).Quantity = 2)), "stacked riding lot");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Lots.Single(x => x.BeltPosition > 0).BeltPosition = BeltRules.UnitsPerTile)), "off the belt");
        }
    }
}
