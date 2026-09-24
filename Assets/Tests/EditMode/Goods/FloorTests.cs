// Verifies factory floors (decision 0020): the add-floor order charges once and fixes the elevator, its rejections change
// nothing, placement on upper floors and around the elevator, belts on separate floors, recovery validation, and the v7
// schema upgrade. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class FloorTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryFloorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 12x10 site with a 6x5 factory at (2,2) (interior 3..6 x 3..5, 12 cells, door (4,2)) and a 3x3
        // restaurant at (8,6); floors cost 100 cents a cell (1200 a floor) up to 3 storeys; the company has 3000 cents; chef
        // holds a 1x1 fridge and 10 belts. Not gameplay content or configuration.
        private const long FloorPrice = 1200;

        private static GoodsWorld CreateWorld(long cash = 3000, bool offer = true)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "plant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "plant", Width = 12, Depth = 10 });
            world.Bootstrap(new GoodsBuilding
            {
                Id = "factory", SiteId = "plant", Kind = GoodsWorld.FactoryKind, CellX = 2, CellZ = 2, Width = 6, Depth = 5,
                Doors = new List<GridCell> { new() { X = 4, Z = 2 } }
            });
            world.Bootstrap(new GoodsBuilding { Id = "diner", SiteId = "plant", CellX = 8, CellZ = 6, Width = 3, Depth = 3 });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = cash, SiteIds = new List<string> { "plant" } });
            world.Bootstrap(new GoodsEquipment { Id = "fridge", Kind = "fridge", SiteId = "plant", CellX = 11, CellZ = 0, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1 });
            world.Bootstrap(new GoodsLot
            {
                Id = "belts", ItemId = GoodsWorld.BeltItemId, OwnerId = "plant", LocationId = "carried:chef", Quantity = 10,
                SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds
            });
            world.Grant("chef", "plant");
            if (offer) world.RegisterFloorOffer(new FloorOffer { CentsPerCell = 100, MaxFloors = 3 });
            Assert.That(world.PickUp("chef", "hold-fridge", "fridge").Accepted, Is.True);
            return world;
        }

        private static GoodsBuilding Factory(GoodsWorld world) => world.Snapshot().Buildings.Single(x => x.Id == "factory");
        private static long Cash(GoodsWorld world) => world.Snapshot().Companies.Single().Cash;

        [Test]
        public void AddingAFloorChargesOnceFixesTheElevatorAndReplays()
        {
            var world = CreateWorld();
            Assert.That(GoodsWorld.FloorPriceCents(Factory(world), new FloorOffer { CentsPerCell = 100 }), Is.EqualTo(FloorPrice));
            var first = world.AddFloor("chef", "floor-1", "factory", 5, 4);
            Assert.That((first.Accepted, first.Reason), Is.EqualTo((true, "floor-added")));
            Assert.That((Factory(world).Floors, Factory(world).ElevatorX, Factory(world).ElevatorZ, Cash(world)), Is.EqualTo((2, 5, 4, 3000 - FloorPrice)));

            var replay = world.AddFloor("chef", "floor-1", "factory", 3, 3);
            Assert.That((replay.Accepted, replay.Revision), Is.EqualTo((true, first.Revision)), "A retried order replays.");
            Assert.That((Factory(world).Floors, Cash(world)), Is.EqualTo((2, 3000 - FloorPrice)), "A retried order never pays twice.");

            Assert.That(world.AddFloor("chef", "floor-2", "factory", 3, 3).Accepted, Is.True);
            Assert.That((Factory(world).Floors, Factory(world).ElevatorX, Factory(world).ElevatorZ, Cash(world)), Is.EqualTo((3, 5, 4, 3000 - 2 * FloorPrice)),
                "Later floors extend the existing shaft and ignore the requested cell.");
            Assert.DoesNotThrow(() => GoodsWorld.Restore(world.Snapshot()));

            var durable = CreateWorld();
            GoodsSnapshotStore.Save(durable, PathForSave);
            Assert.That(durable.AddFloorDurably("chef", "floor-d", "factory", 5, 4, PathForSave).Accepted, Is.True);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((loaded.Buildings.Single(x => x.Id == "factory").Floors, loaded.Companies.Single().Cash), Is.EqualTo((2, 3000 - FloorPrice)),
                "Floor and payment commit together.");
        }

        [Test]
        public void RejectedFloorOrdersChangeNothing()
        {
            var world = CreateWorld(cash: FloorPrice + 100);
            Assert.That(world.Place("chef", "place", "fridge", 3, 3, 0).Accepted, Is.True);
            var before = JsonUtility.ToJson(world.Snapshot());
            Assert.That(world.AddFloor("chef", "diner", "diner", 9, 7).Reason, Is.EqualTo("not-a-factory"));
            Assert.That(world.AddFloor("stranger", "x", "factory", 5, 4).Reason, Is.EqualTo("forbidden"));
            Assert.That(world.AddFloor("chef", "missing", "nowhere", 5, 4).Reason, Is.EqualTo("forbidden"));
            Assert.That(world.AddFloor("chef", "on-wall", "factory", 2, 3).Reason, Is.EqualTo("invalid-elevator"));
            Assert.That(world.AddFloor("chef", "in-door", "factory", 4, 2).Reason, Is.EqualTo("invalid-elevator"));
            Assert.That(world.AddFloor("chef", "on-fridge", "factory", 3, 3).Reason, Is.EqualTo("blocked"));
            Assert.That(JsonUtility.ToJson(world.Snapshot()), Is.EqualTo(before), "Rejections are answered, not recorded.");

            Assert.That(world.AddFloor("chef", "ok", "factory", 5, 4).Accepted, Is.True);
            Assert.That(world.AddFloor("chef", "poor", "factory", 5, 4).Reason, Is.EqualTo("insufficient-funds"));
            var rich = CreateWorld(cash: 10 * FloorPrice);
            Assert.That(rich.AddFloor("chef", "a", "factory", 5, 4).Accepted && rich.AddFloor("chef", "b", "factory", 5, 4).Accepted, Is.True);
            Assert.That(rich.AddFloor("chef", "c", "factory", 5, 4).Reason, Is.EqualTo("max-floors"));
            Assert.That(CreateWorld(offer: false).AddFloor("chef", "a", "factory", 5, 4).Reason, Is.EqualTo("no-floor-offer"));
        }

        [Test]
        public void UpperFloorsHoldEquipmentOnlyOverTheInteriorAndNeverOnTheElevator()
        {
            var world = CreateWorld();
            Assert.That(world.Place("chef", "no-floor-yet", "fridge", 3, 3, 0, 1).Reason, Is.EqualTo("no-floor"));
            Assert.That(world.AddFloor("chef", "floor", "factory", 5, 4).Accepted, Is.True);
            Assert.That(world.Place("chef", "outside", "fridge", 9, 1, 0, 1).Reason, Is.EqualTo("no-floor"), "No floor outside a building.");
            Assert.That(world.Place("chef", "on-wall", "fridge", 2, 3, 0, 1).Reason, Is.EqualTo("no-floor"), "Upper floors cover only the interior.");
            Assert.That(world.Place("chef", "too-high", "fridge", 3, 3, 0, 2).Reason, Is.EqualTo("no-floor"));
            Assert.That(world.Place("chef", "diner-up", "fridge", 9, 7, 0, 1).Reason, Is.EqualTo("no-floor"), "A restaurant has one storey.");
            Assert.That(world.Place("chef", "shaft-up", "fridge", 5, 4, 0, 1).Reason, Is.EqualTo("blocked"));
            Assert.That(world.Place("chef", "shaft-ground", "fridge", 5, 4, 0, 0).Reason, Is.EqualTo("blocked"));
            Assert.That(world.PlaceBelt("chef", "belt-shaft", "plant", 5, 4, 0, 1).Reason, Is.EqualTo("blocked"));

            Assert.That(world.PlaceBelt("chef", "belt-ground", "plant", 3, 3, 0).Accepted, Is.True);
            Assert.That(world.Place("chef", "upstairs", "fridge", 3, 3, 0, 1).Accepted, Is.True, "Levels do not block each other.");
            var placed = world.Snapshot().Equipment.Single();
            Assert.That((placed.CellX, placed.CellZ, placed.Level), Is.EqualTo((3, 3, 1)));
            Assert.DoesNotThrow(() => GoodsWorld.Restore(world.Snapshot()));

            var tampered = world.Snapshot();
            tampered.Equipment.Single().Level = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(tampered), "Recovery rejects a piece above the top floor.");
            var shaftless = world.Snapshot();
            shaftless.Buildings.Single(x => x.Id == "factory").ElevatorX = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(shaftless), "The elevator must be an interior cell.");
            var tallDiner = world.Snapshot();
            tallDiner.Buildings.Single(x => x.Id == "diner").Floors = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(tallDiner), "Only factories have upper floors.");

            Assert.That(world.PickUp("chef", "pick", "fridge").Accepted, Is.True);
            Assert.That(world.Snapshot().Equipment.Single().Level, Is.EqualTo(0), "A held piece has no level.");
        }

        [Test]
        public void BeltsOnDifferentFloorsNeverLink()
        {
            var world = CreateWorld();
            Assert.That(world.AddFloor("chef", "floor", "factory", 6, 5).Accepted, Is.True);
            // Ground belt at (3,3) points east at (4,3), where only an upper-floor belt stands.
            Assert.That(world.PlaceBelt("chef", "ground", "plant", 3, 3, 1).Accepted, Is.True);
            Assert.That(world.PlaceBelt("chef", "upper", "plant", 4, 3, 1, 1).Accepted, Is.True);
            // Belts stack to 1 here, so the dough fits only once two belt items have left the inventory.
            world.Bootstrap(new GoodsLot { Id = "dough", ItemId = "dough", OwnerId = "plant", LocationId = "carried:chef", Quantity = 1, SpoilAfterSeconds = 100000 });
            var ground = world.Snapshot().Belts.Single(x => x.Level == 0);
            Assert.That(world.PlaceOnBelt("chef", "load", "dough", ground.Id).Accepted, Is.True);
            world.Advance(5);
            var lot = world.Snapshot().Lots.Single(x => x.Id == "dough");
            Assert.That((lot.LocationId, lot.BeltPosition), Is.EqualTo((ground.LocationId, BeltRules.EndRest)), "The item stops at the end of its own floor's belt.");
        }

        [Test]
        public void SchemaV7RowLoadsAsOneStoreyRestaurants()
        {
            var legacy = new GoodsWorld("test-world");
            legacy.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "plant", Kind = "storage", Capacity = 1 });
            legacy.Bootstrap(new SiteLayout { SiteId = "plant", Width = 12, Depth = 10 });
            legacy.Bootstrap(new GoodsBuilding { Id = "shop", SiteId = "plant", CellX = 2, CellZ = 2, Width = 4, Depth = 4 });
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v7 = JsonUtility.ToJson(legacy.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":7")
                .Replace("\"Kind\":\"restaurant\",", "").Replace(",\"Floors\":1,\"ElevatorX\":0,\"ElevatorZ\":0", "");
            Assert.That(v7, Does.Not.Contain("Floors").And.Not.Contain("\"restaurant\""), "The row looks like a v7 building.");
            SnapshotDatabase.WritePayload(PathForSave, v7);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(7));

            var building = GoodsSnapshotStore.Load(PathForSave).Snapshot().Buildings.Single();
            Assert.That((building.Kind, building.Floors, building.HasElevator), Is.EqualTo((GoodsWorld.RestaurantKind, 1, false)));
        }
    }
}
