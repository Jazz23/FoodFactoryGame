// Verifies conveyor lifts (decision 0021): a lift is made from one lift item, stands on its cell on both of its levels, needs
// the other floor to exist there, carries items up or down onto the belt in front on its exit level, is never fed from its
// exit end, returns its item and riding goods when removed, survives save and recovery, and a v9 row loads with flat belts.
// Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class LiftTests
    {
        private string _saveDirectory;
        private int _request;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryLiftTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 12x10 site with a 6x5 factory at (2,2) (interior 3..6 x 3..5, door (4,2)); floors cost 100 cents
        // a cell up to 3 storeys; the chef carries 10 belts, 4 lifts and 3 dough in 10 slots. Belts and lifts stack to 100,
        // dough to 20. Not gameplay content or configuration.
        private static GoodsWorld CreateWorld(int floorsToAdd = 1)
        {
            var world = new GoodsWorld("test-world");
            world.RegisterItem(GoodsWorld.BeltItemId, 100);
            world.RegisterItem(GoodsWorld.LiftItemId, 100);
            world.RegisterItem("dough", 20);
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "plant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "plant", Width = 12, Depth = 10 });
            world.Bootstrap(new GoodsBuilding
            {
                Id = "factory", SiteId = "plant", Kind = GoodsWorld.FactoryKind, CellX = 2, CellZ = 2, Width = 6, Depth = 5,
                Doors = new List<GridCell> { new() { X = 4, Z = 2 } }
            });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = 100000, SiteIds = new List<string> { "plant" } });
            world.Bootstrap(Lot("belts", GoodsWorld.BeltItemId, 10, GoodsWorld.NonPerishableSeconds));
            world.Bootstrap(Lot("lifts", GoodsWorld.LiftItemId, 4, GoodsWorld.NonPerishableSeconds));
            world.Bootstrap(Lot("dough", "dough", 3, 100000));
            world.Grant("chef", "plant");
            world.RegisterFloorOffer(new FloorOffer { CentsPerCell = 100, MaxFloors = 3 });
            for (var floor = 0; floor < floorsToAdd; floor++)
                Assert.That(world.AddFloor("chef", "floor-" + floor, "factory", 6, 5).Accepted, Is.True);
            return world;
        }

        private static GoodsLot Lot(string id, string itemId, int quantity, long spoilAfter) => new()
        {
            Id = id, ItemId = itemId, OwnerId = "plant", LocationId = "carried:chef", Quantity = quantity, SpoilAfterSeconds = spoilAfter
        };

        private string Next() => "r" + _request++;

        private static int Carried(GoodsWorld world, string itemId) =>
            world.Snapshot().Lots.Where(x => x.LocationId == "carried:chef" && x.ItemId == itemId).Sum(x => x.Quantity);

        private static GoodsBelt At(GoodsWorld world, int x, int z, int level) =>
            world.Snapshot().Belts.Single(b => b.CellX == x && b.CellZ == z && b.Level == level);

        [Test]
        public void ALiftTakesALiftItemAndStandsOnBothLevels()
        {
            var world = CreateWorld();
            var placed = world.PlaceLift("chef", "lift", "plant", 4, 3, 1, 0, 1);
            Assert.That((placed.Accepted, placed.Reason), Is.EqualTo((true, "placed")));
            var lift = world.Snapshot().Belts.Single();
            Assert.That((lift.Level, lift.Lift, lift.ExitLevel), Is.EqualTo((0, 1, 1)));
            Assert.That((Carried(world, GoodsWorld.LiftItemId), Carried(world, GoodsWorld.BeltItemId)), Is.EqualTo((3, 10)),
                "A lift costs one lift item and no belt.");

            Assert.That(world.PlaceBelt("chef", Next(), "plant", 4, 3, 1, 1).Reason, Is.EqualTo("blocked"), "The lift stands upstairs too.");
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 4, 3, 1).Reason, Is.EqualTo("blocked"),
                "A belt over the lift's cell never turns it into a belt.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 1, -1).Reason, Is.EqualTo("blocked"), "No down-lift over its top.");

            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 1).Reason, Is.EqualTo("unchanged"));
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 0, 0, 1).Reason, Is.EqualTo("rotated"));
            Assert.That(world.Snapshot().Belts.Single().Id, Is.EqualTo(lift.Id), "Turning keeps the lift's identity.");
            Assert.That(Carried(world, GoodsWorld.LiftItemId), Is.EqualTo(3), "Turning is free.");
        }

        [Test]
        public void ALiftNeedsTheOtherFloorAndAValidDirection()
        {
            var world = CreateWorld();
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 1, 1).Reason, Is.EqualTo("no-floor"), "No third storey yet.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 9, 3, 1, 0, 1).Reason, Is.EqualTo("no-floor"), "Outside the factory.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, -1).Reason, Is.EqualTo("out-of-bounds"), "Nothing below the ground.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 6, 5, 1, 0, 1).Reason, Is.EqualTo("blocked"), "The elevator shaft stays clear.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 2).Reason, Is.EqualTo("invalid-lift"));
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 0).Reason, Is.EqualTo("invalid-lift"), "A lift moves between floors.");
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 4, 4, 0, 1).Accepted, Is.True);
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 4, 0, 0, 1).Reason, Is.EqualTo("blocked"), "A belt upstairs blocks its top.");
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 4, 0, 1, -1).Reason, Is.EqualTo("blocked"), "A belt blocks its bottom.");
            Assert.That(world.Snapshot().Belts.Count, Is.EqualTo(1));
            Assert.That(Carried(world, GoodsWorld.LiftItemId), Is.EqualTo(4), "Refused lifts cost nothing.");

            var empty = CreateWorld();
            empty.Bootstrap(new GoodsLocation { Id = "carried:sous", SiteId = "plant", Kind = "carried", Capacity = 4 });
            empty.Grant("sous", "plant");
            Assert.That(empty.PlaceLift("sous", Next(), "plant", 4, 3, 1, 0, 1).Reason, Is.EqualTo("no-lifts"));
        }

        [Test]
        public void ItemsRideUpAndDownBetweenFloors()
        {
            var world = CreateWorld();
            // Ground belt east into an up-lift at (4,3), which hands to the upper belt at (5,3).
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 3, 3, 1).Accepted, Is.True);
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 1).Accepted, Is.True);
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 5, 3, 1, 1).Accepted, Is.True);
            // Upper belt west at (5,4) into a down-lift at (4,4), which hands to the ground belt at (3,4).
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 5, 4, 3, 1).Accepted, Is.True);
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 4, 3, 1, -1).Accepted, Is.True);
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 3, 4, 3).Accepted, Is.True);

            var cells = BeltRules.ByCell(world.Snapshot().Belts);
            var up = At(world, 4, 3, 0);
            Assert.That(BeltRules.Link(cells, At(world, 3, 3, 0)).Next.Id, Is.EqualTo(up.Id));
            Assert.That((BeltRules.Link(cells, up).Next.Id, BeltRules.Link(cells, up).Entry), Is.EqualTo((At(world, 5, 3, 1).Id, 0)));
            Assert.That(BeltRules.Link(cells, At(world, 4, 4, 1)).Next.Id, Is.EqualTo(At(world, 3, 4, 0).Id));

            var riding = world.PlaceOnBelt("chef", Next(), "dough", At(world, 3, 3, 0).Id);
            var falling = world.PlaceOnBelt("chef", Next(), "dough", At(world, 5, 4, 1).Id);
            Assert.That((riding.Accepted, falling.Accepted), Is.EqualTo((true, true)));
            world.Advance(5);
            var top = world.Snapshot().Lots.Single(x => x.Id == riding.MovedLotId);
            Assert.That((top.LocationId, top.BeltPosition), Is.EqualTo((At(world, 5, 3, 1).LocationId, BeltRules.EndRest)), "Carried up a floor.");
            var bottom = world.Snapshot().Lots.Single(x => x.Id == falling.MovedLotId);
            Assert.That((bottom.LocationId, bottom.BeltPosition), Is.EqualTo((At(world, 3, 4, 0).LocationId, BeltRules.EndRest)), "Carried down a floor.");
        }

        [Test]
        public void ALiftShapesItsExitBeltAndIsNeverFedFromItsExitEnd()
        {
            var world = CreateWorld();
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 1).Accepted, Is.True);
            // Upstairs, the belt in front of the lift runs north: the lift feeds it from its left side, so it curves.
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 5, 3, 0, 1).Accepted, Is.True);
            // Upstairs, a belt west of the lift's top points east into it.
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 3, 3, 1, 1).Accepted, Is.True);
            var cells = BeltRules.ByCell(world.Snapshot().Belts);
            var lift = At(world, 4, 3, 0);
            Assert.That(BeltRules.Shape(cells, At(world, 5, 3, 1)), Is.EqualTo(BeltShape.CurveFromLeft));
            Assert.That(BeltRules.Link(cells, lift).Entry, Is.EqualTo(0), "Entering the curve starts at 0.");
            Assert.That(BeltRules.Link(cells, At(world, 3, 3, 1)).Next, Is.Null, "A lift's top takes nothing in.");
            Assert.That(BeltRules.Shape(cells, lift), Is.EqualTo(BeltShape.Straight));

            // Downstairs, a belt north of the lift pointing south side-loads it at its middle; the lift stays straight.
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 4, 4, 2).Accepted, Is.True);
            cells = BeltRules.ByCell(world.Snapshot().Belts);
            Assert.That(BeltRules.Shape(cells, lift), Is.EqualTo(BeltShape.Straight));
            Assert.That(BeltRules.Link(cells, At(world, 4, 4, 0)).Entry, Is.EqualTo(BeltRules.Middle));
            // An up-lift's bottom end hands nothing to the ground belt in front of it, so that belt does not curve from it.
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 5, 3, 0).Accepted, Is.True);
            cells = BeltRules.ByCell(world.Snapshot().Belts);
            Assert.That(BeltRules.Link(cells, lift).Next.Level, Is.EqualTo(1));
            Assert.That(BeltRules.Shape(cells, At(world, 5, 3, 0)), Is.EqualTo(BeltShape.Straight), "The lift does not feed the ground belt.");
        }

        [Test]
        public void RemovingALiftReturnsItsItemAndRidingGoods()
        {
            var world = CreateWorld();
            Assert.That(world.PlaceLift("chef", Next(), "plant", 4, 3, 1, 0, 1).Accepted, Is.True);
            var lift = world.Snapshot().Belts.Single();
            var placed = world.PlaceOnBelt("chef", Next(), "dough", lift.Id);
            Assert.That(placed.Accepted, Is.True);
            var removed = world.RemoveBelt("chef", Next(), lift.Id);
            Assert.That((removed.Accepted, removed.Reason), Is.EqualTo((true, "removed")));
            Assert.That(world.Snapshot().Belts, Is.Empty);
            Assert.That((Carried(world, GoodsWorld.LiftItemId), Carried(world, GoodsWorld.BeltItemId), Carried(world, "dough")), Is.EqualTo((4, 10, 3)));
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 4, 3, 1, 1).Accepted, Is.True, "The upper cell is free again.");
        }

        [Test]
        public void LiftsSurviveSaveAndRecoveryRejectsBrokenLifts()
        {
            var world = CreateWorld(2);
            GoodsSnapshotStore.Save(world, PathForSave);
            Assert.That(world.PlaceLiftDurably("chef", "up", "plant", 4, 3, 1, 1, 1, PathForSave).Accepted, Is.True, "Level 1 to the third storey.");
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot().Belts.Single();
            Assert.That((loaded.CellX, loaded.CellZ, loaded.Level, loaded.Lift, loaded.Direction), Is.EqualTo((4, 3, 1, 1, 1)));

            var steep = world.Snapshot();
            steep.Belts.Single().Lift = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(steep), "A lift spans one storey.");
            var roofless = world.Snapshot();
            roofless.Belts.Single().Level = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(roofless), "Its top must be on a floor.");
            var crowded = world.Snapshot();
            crowded.Belts.Add(new GoodsBelt { Id = "belt-x", SiteId = "plant", CellX = 4, CellZ = 3, Direction = 0, Level = 2 });
            crowded.Locations.Add(new GoodsLocation { Id = "belt-x:items", SiteId = "plant", Kind = GoodsWorld.BeltLocationKind, Capacity = 4 });
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(crowded), "Nothing may share the lift's top cell.");
        }

        [Test]
        public void SchemaV9RowLoadsWithFlatBelts()
        {
            var world = CreateWorld();
            Assert.That(world.PlaceBelt("chef", Next(), "plant", 3, 3, 1).Accepted, Is.True);
            GoodsSnapshotStore.Save(world, PathForSave);
            var v9 = JsonUtility.ToJson(world.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":9").Replace(",\"Lift\":0", "");
            Assert.That(v9, Does.Not.Contain("Lift\""), "The row looks like a v9 belt.");
            SnapshotDatabase.WritePayload(PathForSave, v9);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(9));

            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Belts.Single().Lift, Is.EqualTo(0));
        }
    }
}
