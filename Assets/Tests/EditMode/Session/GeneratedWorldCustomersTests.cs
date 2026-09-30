// Verifies customers in generated worlds (decision 0030) with the real generator and content, on isolated saves: districts and
// competitors derive deterministically from the seed (one district per block sharing its rate by area, one competitor per
// competitor lot at its access point); a new world gets them once with the starting counter and table placed inside the shell
// (room left for an oven, doors kept clear); a world made by piece 2 gains them once and is unchanged on the next load, without
// a second counter when its players already bought one; dev worlds keep only their own district and unlinked competitors; and
// a customer from the map buys at the starting counter.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class GeneratedWorldCustomersTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);
        private string LegacyPath => Path.Combine(_directory, SessionOptions.LegacyWorldFileName);

        private static EquipmentDefinition Counter() => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Counter.asset");
        private static EquipmentDefinition Table() => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Table.asset");
        private static EquipmentDefinition Oven() => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Oven.asset");

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryGeneratedCustomerTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private StoredWorldLayout Layout(string seed = "piece-two") => WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, seed);

        // The seed's layout generated again for a separate world, in its own isolated folder.
        private WorldLayout Elsewhere(string seed)
        {
            var folder = Path.Combine(_directory, "elsewhere-" + seed);
            Directory.CreateDirectory(folder);
            return WorldGeneration.PrepareLayout(Path.Combine(folder, SessionOptions.WorldFileName),
                Path.Combine(folder, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, seed).Layout;
        }

        private GoodsWorld Open(StoredWorldLayout stored, bool equip = true) =>
            GeneratedWorld.LoadOrCreate(WorldPath, stored, SessionTestFiles.ContentItems(), equip ? Counter() : null, equip ? Table() : null);

        [Test]
        public void DistrictsAndCompetitorsDeriveDeterministicallyFromTheSeed()
        {
            var layout = Layout().Layout;
            var districts = WorldLayoutCustomers.Districts(layout);
            var competitors = WorldLayoutCustomers.Competitors(layout);

            // The same seed generated again elsewhere derives byte-identical records.
            var other = Elsewhere("piece-two");
            Assert.That(JsonUtility.ToJson(new Records(WorldLayoutCustomers.Districts(other), WorldLayoutCustomers.Competitors(other))),
                Is.EqualTo(JsonUtility.ToJson(new Records(districts, competitors))));
            var different = Elsewhere("another-seed");
            Assert.That(JsonUtility.ToJson(new Records(WorldLayoutCustomers.Districts(different), WorldLayoutCustomers.Competitors(different))),
                Is.Not.EqualTo(JsonUtility.ToJson(new Records(districts, competitors))));

            Assert.That(districts.Count, Is.EqualTo(layout.Districts.Sum(x => x.Areas.Count)), "One district per block.");
            foreach (var district in layout.Districts)
            {
                var blocks = districts.Where(x => x.Id.StartsWith($"district-{district.Id}-", StringComparison.Ordinal)).ToList();
                Assert.That(blocks.Sum(x => x.CustomersPerHour), Is.EqualTo(district.CustomersPerHour), $"{district.Id} shares its whole rate.");
                for (var block = 0; block < district.Areas.Count; block++)
                {
                    var area = district.Areas[block];
                    var record = blocks.Single(x => x.Id == $"district-{district.Id}-{block}");
                    Assert.That(area.Contains(record.MapX, record.MapZ), Is.True, $"{record.Id} spawns inside its block.");
                    Assert.That(record.LikedCuisines, Is.EqualTo(district.Cuisines.Where(x => x.Weight >= WorldLayoutCustomers.LikedCuisineWeight)
                        .OrderByDescending(x => x.Weight).ThenBy(x => x.Cuisine, StringComparer.Ordinal).Select(x => x.Cuisine)));
                }
            }

            var owned = layout.Buildings.Where(x => x.Ownership == Ownership.Competitor).ToList();
            Assert.That(competitors.Select(x => x.Id), Is.EqualTo(owned.Select(x => "competitor-" + x.Id)));
            Assert.That(competitors.Count, Is.GreaterThan(100), "A generated city has hundreds of competitors.");
            foreach (var competitor in competitors)
            {
                var building = owned.Single(x => "competitor-" + x.Id == competitor.Id);
                var lot = layout.Lots.Single(x => x.BuildingId == building.Id);
                var district = layout.Districts.Single(x => x.Id == building.DistrictId);
                Assert.That((competitor.LotId, competitor.MapX, competitor.MapZ), Is.EqualTo((lot.Id, lot.Access.X, lot.Access.Z)));
                Assert.That(district.Cuisines.Select(x => x.Cuisine), Does.Contain(competitor.Cuisine));
                Assert.That(competitor.Tier, Is.EqualTo(Math.Max(1, district.MinRecipeTier)));
            }
        }

        [Test]
        public void ANewWorldGetsThemOnceWithTheStartingCounterAndTableInside()
        {
            var stored = Layout();
            var start = GeneratedWorld.StartOffer(stored.Layout);
            var created = Open(stored).Snapshot();
            Assert.That(created.Districts.Select(x => x.Id), Is.EqualTo(WorldLayoutCustomers.Districts(stored.Layout).Select(x => x.Id)));
            Assert.That(created.Competitors.Select(x => x.Id), Is.EqualTo(WorldLayoutCustomers.Competitors(stored.Layout).Select(x => x.Id)));
            Assert.That(created.Equipment.Select(x => (x.Id, x.Kind, x.SiteId, x.State)), Is.EquivalentTo(new[]
            {
                (GeneratedWorld.StartCounterId, GoodsWorld.CounterKind, start.SiteId, EquipmentState.Placed),
                (GeneratedWorld.StartTableId, GoodsWorld.TableKind, start.SiteId, EquipmentState.Placed)
            }));
            var shell = created.Buildings.Single();
            foreach (var piece in created.Equipment)
            {
                Assert.That(SiteGrid.InsideInterior(shell, piece.CellX, piece.CellZ, piece.Width, piece.Depth), Is.True, $"{piece.Id} is inside.");
                foreach (var door in shell.Doors)
                {
                    var inwardX = Math.Clamp(door.X, shell.CellX + 1, shell.CellX + shell.Width - 2);
                    var inwardZ = Math.Clamp(door.Z, shell.CellZ + 1, shell.CellZ + shell.Depth - 2);
                    Assert.That(SiteGrid.Overlaps(piece.CellX, piece.CellZ, piece.Width, piece.Depth, inwardX, inwardZ, 1, 1), Is.False,
                        $"{piece.Id} keeps the cell inside door ({door.X}, {door.Z}) clear.");
                }
            }
            var counter = created.Equipment.Single(x => x.Kind == GoodsWorld.CounterKind);
            Assert.That(SiteGrid.IsInterior(shell, counter.CellX, counter.CellZ - 2), Is.True,
                "Two interior rows south of the counter, where customers order (one row closes on the NavMesh).");
            Assert.That(OvenFits(created, shell), Is.True, "The interior still has room for the 3x3 oven.");

            var committed = JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot());
            Assert.That(committed, Is.EqualTo(JsonUtility.ToJson(created)), "Committed before serving.");
            Assert.That(JsonUtility.ToJson(Open(Layout()).Snapshot()), Is.EqualTo(committed), "Reloading adds nothing.");
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(committed), "Reloading writes nothing.");
        }

        [Test]
        public void APieceTwoWorldGainsThemOnceAndIsUnchangedOnTheNextLoad()
        {
            // Piece 2 created the world without districts, competitors, counter or table; its players then bought a counter.
            var stored = Layout();
            var start = GeneratedWorld.StartOffer(stored.Layout);
            var bare = Open(stored, equip: false).Snapshot();
            bare.Districts.Clear();
            bare.Competitors.Clear();
            bare.Revision++;
            var pieceTwo = GoodsWorld.Restore(bare);
            pieceTwo.Bootstrap(Counter().CreatePlaced("bought-counter", start.SiteId, start.BuildingX + 1, start.BuildingZ + 1, 0));
            GoodsSnapshotStore.Save(pieceTwo, WorldPath);

            var upgraded = Open(Layout()).Snapshot();
            Assert.That((upgraded.Districts.Count, upgraded.Competitors.Count),
                Is.EqualTo((WorldLayoutCustomers.Districts(stored.Layout).Count, WorldLayoutCustomers.Competitors(stored.Layout).Count)));
            Assert.That(upgraded.Equipment.Where(x => x.Kind == GoodsWorld.CounterKind).Select(x => x.Id), Is.EqualTo(new[] { "bought-counter" }),
                "A world that already has a counter gets no second one.");
            Assert.That(upgraded.Equipment.Where(x => x.Kind == GoodsWorld.TableKind).Select(x => x.Id), Is.EqualTo(new[] { GeneratedWorld.StartTableId }));
            var committed = JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot());
            Assert.That(committed, Is.EqualTo(JsonUtility.ToJson(upgraded)), "Committed before serving.");

            Assert.That(JsonUtility.ToJson(Open(Layout()).Snapshot()), Is.EqualTo(committed), "Added once, never twice.");
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(committed));
        }

        [Test]
        public void DevWorldsKeepTheirOwnDistrictAndUnlinkedCompetitors()
        {
            var stored = Layout();
            var state = DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems(), table: Table()).Snapshot();
            Assert.That(state.Districts.Select(x => x.Id), Is.EqualTo(new[] { DevWorld.DistrictId }));
            Assert.That(state.Competitors.Select(x => (x.Id, x.LotId)), Is.EquivalentTo(new[] { (DevWorld.CafeId, ""), (DevWorld.NoodlesId, "") }));
            // A format 3 layout beside that dev save still leaves it alone.
            var before = JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot());
            Assert.That(Open(stored), Is.Null);
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(before));
        }

        [Test]
        public void ACustomerFromTheMapBuysAtTheStartingCounter()
        {
            var world = Open(Layout());
            var state = world.Snapshot();
            world.RegisterRecipe(AssetDatabase.LoadAssetAtPath<RecipeAsset>("Assets/Content/Recipes/SellBread.asset").ToDefinition());
            // TEST-ONLY stock: 20 bread that never spoils during the test, placed straight into the counter's input.
            world.Bootstrap(new GoodsLot
            {
                Id = "test-bread", ItemId = "bread", OwnerId = state.Sites.Single().Id, LocationId = GeneratedWorld.StartCounterId + ":in",
                Quantity = 20, SpoilAfterSeconds = 1_000_000
            });
            var cash = state.Companies.Single().Cash;
            var seconds = 0;
            while (world.Snapshot().Companies.Single().Cash == cash && seconds < 6 * 3600)
            {
                world.Advance(60);
                seconds += 60;
            }
            var after = world.Snapshot();
            TestContext.WriteLine($"First sale after {seconds} s; customers {after.Customers.Count}, served " +
                $"{after.Diners.Where(x => x.RestaurantId == state.Sites.Single().Id).Sum(x => x.Served)} here, {after.Diners.Sum(x => x.Served)} in all.");
            Assert.That(after.Companies.Single().Cash, Is.GreaterThan(cash), "A customer from a map district bought bread.");
            Assert.That(after.Customers.Where(x => x.RestaurantId == state.Sites.Single().Id).Select(x => x.DistrictId),
                Is.All.StartsWith("district-"));
            Assert.DoesNotThrow(() => GoodsWorld.Validate(after));
        }

        private static bool OvenFits(GoodsSnapshot state, GoodsBuilding shell)
        {
            var oven = Oven();
            for (var x = shell.CellX + 1; x + oven.Width - 1 <= shell.CellX + shell.Width - 2; x++)
            for (var z = shell.CellZ + 1; z + oven.Depth - 1 <= shell.CellZ + shell.Depth - 2; z++)
                if (state.Equipment.All(e => !SiteGrid.Overlaps(x, z, oven.Width, oven.Depth, e.CellX, e.CellZ, e.Width, e.Depth)))
                    return true;
            return false;
        }

        [Serializable]
        private sealed class Records
        {
            public List<GoodsDistrict> Districts;
            public List<GoodsCompetitor> Competitors;

            public Records(List<GoodsDistrict> districts, List<GoodsCompetitor> competitors)
            {
                Districts = districts;
                Competitors = competitors;
            }
        }
    }
}
