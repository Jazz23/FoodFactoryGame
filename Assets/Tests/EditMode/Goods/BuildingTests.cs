// Verifies building shells (decision 0019): shell validation at bootstrap, walls blocking equipment and belts while the
// interior and doors stay usable, recovery validation, the site view, and the v6 schema upgrade. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class BuildingTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryBuildingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: a 12x10 site with a 6x5 shell at (2,2) (interior 3..6 x 3..5) and one door at (4,6) in its north
        // wall; chef holds a 1x1 fridge and 10 belts. Not gameplay content or configuration.
        private static GoodsBuilding Shell(string id = "shop", int x = 2, int z = 2, int width = 6, int depth = 5, params (int X, int Z)[] doors) => new()
        {
            Id = id, SiteId = "restaurant", CellX = x, CellZ = z, Width = width, Depth = depth,
            Doors = (doors.Length == 0 ? new[] { (4, 6) } : doors).Select(d => new GridCell { X = d.Item1, Z = d.Item2 }).ToList()
        };

        private static GoodsWorld CreateWorld(bool withShell = true)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 12, Depth = 10 });
            world.Bootstrap(new GoodsLocation { Id = "annex-storage", SiteId = "annex", Kind = "storage", Capacity = 1 });
            world.Bootstrap(new SiteLayout { SiteId = "annex", Width = 12, Depth = 10 });
            if (withShell) world.Bootstrap(Shell());
            world.Bootstrap(new GoodsEquipment { Id = "fridge", Kind = "fridge", SiteId = "restaurant", CellX = 10, CellZ = 9, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1 });
            world.Bootstrap(new GoodsLot
            {
                Id = "belts", ItemId = GoodsWorld.BeltItemId, OwnerId = "restaurant", LocationId = "carried:chef", Quantity = 10,
                SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds
            });
            world.Grant("chef", "restaurant");
            Assert.That(world.PickUp("chef", "hold-fridge", "fridge").Accepted, Is.True);
            return world;
        }

        [Test]
        public void WallsBlockEquipmentAndBeltsWhileTheInteriorAndDoorsStayUsable()
        {
            var world = CreateWorld();
            Assert.That(world.Place("chef", "on-wall", "fridge", 2, 3, 0).Reason, Is.EqualTo("blocked"), "West wall.");
            Assert.That(world.Place("chef", "on-corner", "fridge", 7, 6, 0).Reason, Is.EqualTo("blocked"), "North-east corner.");
            Assert.That(world.PlaceBelt("chef", "belt-on-wall", "restaurant", 5, 2, 0).Reason, Is.EqualTo("blocked"), "South wall.");
            Assert.That(world.PlaceBelt("chef", "belt-in-door", "restaurant", 4, 6, 0).Accepted, Is.True, "A belt may run through the door.");
            Assert.That(world.Place("chef", "inside", "fridge", 3, 3, 0).Accepted, Is.True, "Interior cells are ordinary floor.");
            var state = world.Snapshot();
            Assert.That((state.Equipment.Single().CellX, state.Equipment.Single().CellZ), Is.EqualTo((3, 3)));
            Assert.That(SiteGrid.CellProblem(state, "restaurant", 1, 1, 2, 2, null), Is.EqualTo("blocked"), "A footprint overlapping one wall corner.");
            Assert.That(SiteGrid.CellProblem(state, "restaurant", 4, 4, 3, 2, null), Is.Null, "A footprint wholly inside.");
            Assert.That(SiteGrid.CellProblem(state, "annex", 2, 3, 1, 1, null), Is.Null, "Walls block only their own site.");
            Assert.That(GoodsWorld.Restore(state), Is.Not.Null, "The result is a valid snapshot.");
        }

        [Test]
        public void GridHelpersSeparateWallsDoorsAndInterior()
        {
            var shell = Shell();
            Assert.That((SiteGrid.IsWall(shell, 2, 2), SiteGrid.IsWall(shell, 4, 6), SiteGrid.IsWall(shell, 5, 6), SiteGrid.IsWall(shell, 3, 3)),
                Is.EqualTo((true, false, true, false)));
            Assert.That((SiteGrid.IsInterior(shell, 3, 3), SiteGrid.IsInterior(shell, 6, 5), SiteGrid.IsInterior(shell, 4, 6), SiteGrid.IsInterior(shell, 7, 4)),
                Is.EqualTo((true, true, false, false)), "Door cells are thresholds, not interior.");
            Assert.That((SiteGrid.IsDoorCell(shell, 4, 2), SiteGrid.IsDoorCell(shell, 2, 2), SiteGrid.IsDoorCell(shell, 4, 4)), Is.EqualTo((true, false, false)));
        }

        [Test]
        public void BootstrapRejectsMalformedOrBlockedShells()
        {
            var world = CreateWorld();
            var before = JsonUtility.ToJson(world.Snapshot());
            var invalid = new List<GoodsBuilding>
            {
                null,
                Shell(id: " "),
                Shell(id: "shop"),
                Shell(id: "tiny", x: 9, z: 0, width: 2, depth: 5, doors: (9, 1)),
                Shell(id: "outside", x: 8, z: 7, width: 5, depth: 3, doors: (9, 9)),
                Shell(id: "corner-door", x: 8, z: 0, width: 4, depth: 5, doors: (8, 0)),
                Shell(id: "inner-door", x: 8, z: 0, width: 4, depth: 5, doors: (9, 2)),
                Shell(id: "twice", x: 8, z: 0, width: 4, depth: 5, doors: new[] { (9, 0), (9, 0) }),
                Shell(id: "overlap", x: 6, z: 0, width: 4, depth: 4, doors: (7, 0)),
                new() { Id = "nowhere", SiteId = "missing", Width = 3, Depth = 3 }
            };
            foreach (var building in invalid)
                Assert.Throws<ArgumentException>(() => world.Bootstrap(building), building?.Id ?? "null");
            Assert.That(JsonUtility.ToJson(world.Snapshot()), Is.EqualTo(before), "A rejected shell changes nothing.");

            Assert.That(world.Place("chef", "place", "fridge", 9, 1, 0).Accepted, Is.True);
            Assert.Throws<ArgumentException>(() => world.Bootstrap(Shell(id: "over-fridge", x: 9, z: 1, width: 3, depth: 4, doors: (10, 4))),
                "Walls may not cover placed equipment.");
            Assert.That(world.PlaceBelt("chef", "belt", "restaurant", 10, 8, 0).Accepted, Is.True);
            Assert.Throws<ArgumentException>(() => world.Bootstrap(Shell(id: "over-belt", x: 8, z: 6, width: 4, depth: 3, doors: (9, 6))),
                "Walls may not cover belts.");
            Assert.DoesNotThrow(() => world.Bootstrap(Shell(id: "around", x: 8, z: 0, width: 4, depth: 4, doors: (10, 3))),
                "Equipment inside the walls is fine.");
        }

        [Test]
        public void RecoveryRejectsEquipmentOnAWallAndBadShells()
        {
            var world = CreateWorld();
            Assert.That(world.Place("chef", "place", "fridge", 3, 3, 0).Accepted, Is.True);
            var good = world.Snapshot();
            Assert.DoesNotThrow(() => GoodsWorld.Restore(good));

            var onWall = world.Snapshot();
            onWall.Equipment.Single().CellX = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(onWall));
            var duplicate = world.Snapshot();
            duplicate.Buildings.Add(duplicate.Buildings[0]);
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(duplicate));
            var cornerDoor = world.Snapshot();
            cornerDoor.Buildings[0].Doors[0].X = 2;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(cornerDoor));
            var missingList = world.Snapshot();
            missingList.Buildings = null;
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(missingList));
        }

        [Test]
        public void ShellsSurviveSaveAndAppearOnlyInTheirSitesView()
        {
            var world = CreateWorld();
            world.Bootstrap(new GoodsBuilding
            {
                Id = "annex-shed", SiteId = "annex", CellX = 0, CellZ = 0, Width = 3, Depth = 3, Doors = new List<GridCell> { new() { X = 1, Z = 0 } }
            });
            world.Grant("chef", "annex");
            GoodsSnapshotStore.Save(world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(JsonUtility.ToJson(loaded.Buildings), Is.EqualTo(JsonUtility.ToJson(world.Snapshot().Buildings)));
            Assert.That(world.View("chef", "restaurant").Buildings.Select(x => x.Id), Is.EqualTo(new[] { "shop" }));
            Assert.That(world.View("chef", "annex").Buildings.Select(x => x.Id), Is.EqualTo(new[] { "annex-shed" }));
        }

        [Test]
        public void SchemaV6RowLoadsAsCurrentWithoutBuildings()
        {
            var legacy = CreateWorld(false);
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v6 = JsonUtility.ToJson(legacy.Snapshot()).Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":6").Replace(",\"Buildings\":[]", "");
            Assert.That(v6, Does.Not.Contain("Buildings"));
            SnapshotDatabase.WritePayload(PathForSave, v6);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(6), "The row looks like a real v6 commit.");

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Snapshot().Buildings, Is.Empty);
            loaded.Bootstrap(Shell());
            GoodsSnapshotStore.Save(loaded, PathForSave);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Buildings.Single().Id, Is.EqualTo("shop"));
        }
    }
}
