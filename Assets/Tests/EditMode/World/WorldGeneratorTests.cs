// Verifies the world generator (decision 0026) without Unity scenes or saves: the same seed gives the same layout (and a pinned
// hash, so an output change without a new generator version fails), validation holds across many seeds, districts sit where
// GDD section 3 puts them, v2 worlds have land relief, a bridged river, level crossings, junction controls, trees and denser
// blocks, format 1 (v1) layouts still read and write unchanged, retries use derived seeds and are reported, the validator
// catches broken layouts, and every generated shell is a valid decision-0019 building.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using NUnit.Framework;

namespace FoodFactoryGame.World.Tests
{
    public sealed class WorldGeneratorTests
    {
        private const ulong KnownSeed = 20260927;
        // WorldLayoutText.Hash of generator v2's layout for KnownSeed (v1's was c8aed6b7…4269). Changing it requires bumping
        // WorldGenerator.Version.
        private const string KnownHash = "6e12b0fd73136c383c74d42545df4f85482c41b9063c5ac2178b25d3c80fb598";
        private const int SeedCount = 120;

        private static WorldLayout Generate(ulong seed) => WorldGenerator.Generate(seed.ToString(), seed).Layout;

        [Test]
        public void SameSeedProducesAnIdenticalLayout()
        {
            var first = WorldLayoutText.Write(Generate(KnownSeed));
            var second = WorldLayoutText.Write(Generate(KnownSeed));
            Assert.That(second, Is.EqualTo(first));
            TestContext.WriteLine($"seed {KnownSeed}: sha256 {WorldLayoutText.Hash(first)}, {first.Length} chars");
            Assert.That(WorldLayoutText.Hash(first), Is.EqualTo(KnownHash), "Generator output changed: bump WorldGenerator.Version and re-pin.");
        }

        [Test]
        public void DifferentSeedsProduceDifferentLayouts()
        {
            Assert.That(WorldLayoutText.Hash(Generate(1)), Is.Not.EqualTo(WorldLayoutText.Hash(Generate(2))));
        }

        [Test]
        public void LayoutTextRoundTrips()
        {
            var layout = WorldGenerator.Generate("Sunny Valley", WorldSeed.FromText("Sunny Valley")).Layout;
            var text = WorldLayoutText.Write(layout);
            var read = WorldLayoutText.Read(text);
            Assert.That(WorldLayoutText.Write(read), Is.EqualTo(text));
            Assert.That(read.RequestedSeed, Is.EqualTo("Sunny Valley"));
            Assert.That(WorldLayoutValidator.Validate(read), Is.Empty);
            Assert.Throws<FormatException>(() => WorldLayoutText.Read(text.Replace("\nend ", "\nfinish ")));
            Assert.Throws<FormatException>(() => WorldLayoutText.Read(text + "extra\n"));
        }

        [Test]
        public void ValidationPassesAcrossManySeeds()
        {
            var retried = 0;
            var buildings = new List<int>();
            for (var seed = 1UL; seed <= SeedCount; seed++)
            {
                var result = WorldGenerator.Generate(seed.ToString(), seed);
                if (result.Attempts.Count > 1) retried++;
                Assert.That(WorldLayoutValidator.Validate(result.Layout), Is.Empty, $"seed {seed}");
                Assert.That(result.Layout.Seed, Is.EqualTo(WorldRandom.DeriveSeed(seed, result.Layout.Attempt)));
                buildings.Add(result.Layout.Buildings.Count);
            }
            TestContext.WriteLine($"{SeedCount} seeds valid; {retried} needed a retry; buildings min {buildings.Min()} max {buildings.Max()}");
        }

        [Test]
        public void DistrictsArePlacedAsTheDesignSays()
        {
            var sides = new HashSet<Facing>();
            for (var seed = 1UL; seed <= 40; seed++)
            {
                var layout = Generate(seed);
                var c = layout.CityHalfSize;
                var side = WorldLayoutValidator.IndustrialSide(layout);
                Assert.That(side, Is.Not.Null, $"seed {seed}: industrial on one city edge");
                sides.Add(side.Value);
                var district = layout.Districts.ToDictionary(x => x.Id, x => x.Kind);
                var farms = layout.Buildings.Where(x => x.Category == BuildingCategory.Farm).ToList();
                Assert.That(farms, Is.Not.Empty);
                // Farms lie wholly outside the 1 km city square, and some lie beyond the industrial edge.
                Assert.That(farms.All(x => x.X >= c || x.Z >= c || x.X + x.Width <= -c || x.Z + x.Depth <= -c), $"seed {seed}");
                Assert.That(farms.Any(x => WorldLayoutValidator.Beyond(x.Footprint, side.Value, c)), $"seed {seed}");
                Assert.That(layout.Buildings.Where(x => x.Category == BuildingCategory.Factory).All(x => district[x.DistrictId] == DistrictKind.Industrial),
                    $"seed {seed}: factories only in industrial");
                Assert.That(layout.Districts.Single(x => x.Kind == DistrictKind.Downtown).Areas.Any(x => x.Contains(0, 0)), $"seed {seed}");
                var start = layout.Buildings.Single(x => x.Id == layout.StartRestaurantId);
                Assert.That((start.Category, district[start.DistrictId], start.Ownership),
                    Is.EqualTo((BuildingCategory.Restaurant, DistrictKind.Residential, Ownership.Player)));
                var residential = layout.Buildings.Where(x => x.Category == BuildingCategory.Restaurant && x.DistrictId == start.DistrictId).ToList();
                Assert.That(start.Width * start.Depth, Is.EqualTo(residential.Min(x => x.Width * x.Depth)), "start is the smallest residential shell");
                var restaurants = layout.Buildings.Where(x => x.Category == BuildingCategory.Restaurant).ToList();
                Assert.That(restaurants.Any(x => x.Ownership == Ownership.ForSale) && restaurants.Any(x => x.Ownership == Ownership.Competitor));
                Assert.That(layout.Buildings.Where(x => x.Category is BuildingCategory.Factory or BuildingCategory.Farm or BuildingCategory.Station)
                    .All(x => x.Ownership == Ownership.ForSale));
                Assert.That(layout.Buildings.Where(x => x.Category is BuildingCategory.House or BuildingCategory.Apartment or BuildingCategory.Office)
                    .All(x => x.Ownership == Ownership.Scenery && x.ModelKey.Length > 0 && x.PriceCents == 0));
                Assert.That(layout.Buildings.Any(x => x.Category == BuildingCategory.Station && x.LineId == "rail-farm"), $"seed {seed}");
            }
            Assert.That(sides.Count, Is.GreaterThan(1), "the industrial edge varies with the seed");
        }

        [Test]
        public void AFailedAttemptRetriesWithADerivedSeedAndIsReported()
        {
            var result = WorldGenerator.Generate("7", 7, extraRule: x => x.Attempt == 0 ? new[] { "forced failure" } : Array.Empty<string>());
            Assert.That(result.Attempts.Select(x => (x.Attempt, x.Seed)), Is.EqualTo(new[] { (0, 7UL), (1, WorldRandom.DeriveSeed(7, 1)) }));
            Assert.That(result.Attempts[0].Problems, Is.EqualTo(new[] { "forced failure" }));
            Assert.That(result.Attempts[1].Problems, Is.Empty);
            Assert.That((result.Layout.Attempt, result.Layout.Seed, result.Layout.RequestedSeed), Is.EqualTo((1, WorldRandom.DeriveSeed(7, 1), "7")));
            Assert.That(WorldRandom.DeriveSeed(7, 1), Is.Not.EqualTo(7UL));

            var error = Assert.Throws<WorldGenerationException>(() => WorldGenerator.Generate("7", 7, extraRule: _ => new[] { "never" }));
            Assert.That(error.Attempts.Count, Is.EqualTo(WorldSettings.Default.MaxAttempts));
        }

        [Test]
        public void TheValidatorReportsBrokenLayouts()
        {
            WorldLayout Fresh() => Generate(3);
            List<string> ProblemsAfter(Action<WorldLayout> damage)
            {
                var layout = Fresh();
                damage(layout);
                return WorldLayoutValidator.Validate(layout);
            }

            Assert.That(WorldLayoutValidator.Validate(Fresh()), Is.Empty);
            Assert.That(ProblemsAfter(x => x.Buildings.RemoveAll(b => b.Id == x.StartRestaurantId)), Has.Some.StartsWith("start: no starting restaurant"));
            Assert.That(ProblemsAfter(x => x.Buildings.RemoveAll(b => b.Category == BuildingCategory.Farm)), Has.Some.EqualTo("farms: none"));
            Assert.That(ProblemsAfter(x => x.Buildings.RemoveAll(b => b.Category == BuildingCategory.Station)), Has.Some.EqualTo("stations: none"));
            // A house moved onto the centre of a road segment.
            Assert.That(ProblemsAfter(x =>
            {
                var road = x.Roads.First(r => r.Kind == RoadKind.Local);
                var node = x.Nodes.First(n => n.Id == road.FromId);
                var house = x.Buildings.First(b => b.Category == BuildingCategory.House);
                house.X = node.X - 1;
                house.Z = node.Z - 1;
            }), Has.Some.Contains("overlaps a road"));
            // Cutting every road at a farm's gate leaves the farm with no road at its door.
            Assert.That(ProblemsAfter(x =>
            {
                var farm = x.Buildings.First(b => b.Category == BuildingCategory.Farm);
                var nodes = x.Nodes.ToDictionary(n => n.Id);
                var door = farm.Doors[0];
                x.Roads.RemoveAll(r => Math.Abs(nodes[r.FromId].X - door.X) + Math.Abs(nodes[r.FromId].Z - door.Z) < 400
                    || Math.Abs(nodes[r.ToId].X - door.X) + Math.Abs(nodes[r.ToId].Z - door.Z) < 400);
            }), Has.Some.Contains("has no road at its door"));
            // A farm moved into the city.
            Assert.That(ProblemsAfter(x =>
            {
                var farm = x.Buildings.First(b => b.Category == BuildingCategory.Farm);
                farm.X = -farm.Width / 2;
                farm.Z = -farm.Depth / 2;
            }), Has.Some.Contains("farms lie outside the city"));
            Assert.That(ProblemsAfter(x => x.Buildings[1].Id = x.Buildings[0].Id), Has.Some.StartsWith("id: "));
            Assert.That(ProblemsAfter(x => x.Buildings.First(b => b.IsShell).Doors[0].X += 100), Has.Some.Contains("door not a non-corner cell"));
            Assert.That(ProblemsAfter(x => x.Districts.Single(d => d.Kind == DistrictKind.Industrial).Areas.RemoveAt(0)),
                Has.Some.StartsWith("district: "));
        }

        [Test]
        public void EveryGeneratedShellIsAValidBuildingShell()
        {
            var layout = Generate(KnownSeed);
            var world = new GoodsWorld("shell-check");
            var shells = layout.Buildings.Where(x => x.IsShell).ToList();
            Assert.That(shells.Count(x => x.Category == BuildingCategory.Factory), Is.GreaterThan(0));
            foreach (var shell in shells)
            {
                var site = "site-" + shell.Id;
                world.Bootstrap(WorldLayoutShells.SiteLayoutFor(shell, site));
                Assert.DoesNotThrow(() => world.Bootstrap(WorldLayoutShells.ToGoodsBuilding(shell, site)), shell.Id);
                Assert.That(shell.SiteId, Is.Empty, "the site link is undecided");
            }
            Assert.That(world.Snapshot().Buildings.Count, Is.EqualTo(shells.Count));
        }

        [Test]
        public void WorldsHaveLandRiverBridgesCrossingsJunctionsAndTrees()
        {
            for (var seed = 1UL; seed <= 20; seed++)
            {
                var layout = Generate(seed);
                var heights = layout.Terrain.HeightsCm;
                Assert.That(heights.Max() - heights.Min(), Is.GreaterThan(400), $"seed {seed}: land has relief");
                Assert.That(layout.Rivers, Has.Count.EqualTo(1));
                Assert.That(layout.Bridges.Count(x => x.CarriesId.StartsWith("road-")), Is.GreaterThanOrEqualTo(6), $"seed {seed}: road bridges");
                Assert.That(layout.Bridges.Any(x => x.CarriesId == "rail-city"), $"seed {seed}: the city line bridges the river");
                Assert.That(layout.Crossings.Count, Is.GreaterThanOrEqualTo(5), $"seed {seed}: level crossings");
                Assert.That(layout.Nodes.Count(x => x.Control == JunctionControl.TrafficLight), Is.GreaterThan(10), $"seed {seed}: lights");
                Assert.That(layout.Nodes.Count(x => x.Control == JunctionControl.StopSign), Is.GreaterThan(100), $"seed {seed}: stop signs");
                Assert.That(layout.Trees.Count, Is.GreaterThan(2000), $"seed {seed}: trees");
                Assert.That(Enum.GetValues(typeof(TreeKind)).Cast<TreeKind>().All(k => layout.Trees.Any(t => t.Kind == k)), $"seed {seed}: every tree kind");
                Assert.That(layout.Buildings.Count, Is.GreaterThan(1400), $"seed {seed}: density");
                Assert.That(layout.Buildings.Select(x => x.ElevationCm).Distinct().Count(), Is.GreaterThan(50), $"seed {seed}: buildings follow the land");
            }
        }

        [Test]
        public void StopSignsStopTheMinorRoadOrEveryApproach()
        {
            var minor = new[] { RoadKind.Arterial, RoadKind.Arterial, RoadKind.Local };
            Assert.That(WorldJunctions.Stops(JunctionControl.StopSign, RoadKind.Local, minor), Is.True);
            Assert.That(WorldJunctions.Stops(JunctionControl.StopSign, RoadKind.Arterial, minor), Is.False);
            Assert.That(WorldJunctions.Stops(JunctionControl.StopSign, RoadKind.Local, new[] { RoadKind.Local, RoadKind.Local, RoadKind.Local }), Is.True);
            Assert.That(WorldJunctions.Stops(JunctionControl.TrafficLight, RoadKind.Local, minor), Is.False);
            Assert.That(WorldJunctions.Stops(JunctionControl.None, RoadKind.Local, minor), Is.False);
        }

        [Test]
        public void LandHeightIsTheSameAfterAQuarterTurn()
        {
            var terrain = Generate(KnownSeed).Terrain;
            var turned = terrain.Turned();
            foreach (var (x, z) in new[] { (0, 0), (137, -411), (-940, 12), (333, 777), (-1, -1) })
            {
                var (tx, tz) = WorldGeometry.TurnPoint(x, z);
                Assert.That(turned.HeightCm(2 * tx, 2 * tz), Is.EqualTo(terrain.HeightCm(2 * x, 2 * z)), $"{x},{z}");
                Assert.That(terrain.Height(x, z), Is.EqualTo(terrain.HeightCm(2 * x, 2 * z) / 100f).Within(0.011f), $"{x},{z} float sampler");
            }
        }

        // Worlds created by generator v1 are stored as format 1 text; they must still read, and write back byte-for-byte.
        [Test]
        public void AFormatOneLayoutStillReadsAndWritesUnchanged()
        {
            const string v1 = "food-factory-world-layout 1\ngenerator 1\nseed 7 7 0\nbounds 500 950\nstart restaurant-0001\n"
                              + "district downtown Downtown 1 900 90 250 fast-food:40 -100,-100,200,200\n"
                              + "node node-0001 0 0\nnode node-0002 0 10\nroad road-0001 node-0001 node-0002 Local 10 600\n"
                              + "rail rail-city 8 20,-100;20,100\n"
                              + "building restaurant-0001 Restaurant downtown 8 0 10 9 West 8,4;8,5 % 1 Player 150000 % %\n"
                              + "end 1 2 1 1 1\n";
            var layout = WorldLayoutText.Read(v1);
            Assert.That(layout.FormatVersion, Is.EqualTo(1));
            Assert.That(layout.Terrain.IsFlat && layout.Terrain.HeightCm(123, 456) == 0);
            Assert.That(layout.Nodes.All(x => x.Control == JunctionControl.None));
            Assert.That(layout.Rivers.Count + layout.Bridges.Count + layout.Crossings.Count + layout.Trees.Count, Is.Zero);
            Assert.That(layout.Buildings.Single().ElevationCm, Is.Zero);
            Assert.That(WorldLayoutText.Write(layout), Is.EqualTo(v1));
            Assert.That(WorldLayoutText.Hash(layout), Is.EqualTo(WorldLayoutText.Hash(v1)));
        }

        [Test]
        public void TheValidatorReportsBrokenLandRiversCrossingsJunctionsAndTrees()
        {
            List<string> ProblemsAfter(Action<WorldLayout> damage)
            {
                var layout = Generate(3);
                damage(layout);
                return WorldLayoutValidator.Validate(layout);
            }

            Assert.That(ProblemsAfter(x => x.Bridges.RemoveAll(b => b.CarriesId == "rail-city")), Has.Some.Contains("rail-city crosses it without a bridge"));
            Assert.That(ProblemsAfter(x => x.Bridges.RemoveAt(0)), Has.Some.Contains("without a bridge"));
            Assert.That(ProblemsAfter(x => x.Crossings.RemoveAt(0)), Has.Some.Contains("without a level crossing"));
            Assert.That(ProblemsAfter(x => x.Crossings[0].X += 3), Has.Some.StartsWith("crossing crossing-0001"));
            Assert.That(ProblemsAfter(x => x.Nodes.First(n => n.Control == JunctionControl.StopSign).Control = JunctionControl.None),
                Has.Some.Contains(": control None at "));
            Assert.That(ProblemsAfter(x => x.Buildings.First(b => b.Category == BuildingCategory.House).ElevationCm += 50),
                Has.Some.Contains("elevation is not the land height"));
            Assert.That(ProblemsAfter(x =>
            {
                var house = x.Buildings.First(b => b.Category == BuildingCategory.House);
                x.Trees[0].X = house.X + house.Width / 2;
                x.Trees[0].Z = house.Z + house.Depth / 2;
            }), Has.Some.StartsWith("trees: 1 misplaced"));
            Assert.That(ProblemsAfter(x =>
            {
                var point = x.Rivers[0].Points[x.Rivers[0].Points.Count / 2];
                x.Trees[0].X = point.X;
                x.Trees[0].Z = point.Z;
            }), Has.Some.StartsWith("trees: 1 misplaced"));
            Assert.That(ProblemsAfter(x =>
            {
                var house = x.Buildings.First(b => b.Category == BuildingCategory.House);
                var point = x.Rivers[0].Points[x.Rivers[0].Points.Count / 2];
                house.X = point.X - house.Width / 2;
                house.Z = point.Z - house.Depth / 2;
            }), Has.Some.Contains("stands in river"));
            Assert.That(ProblemsAfter(x => x.Terrain = WorldTerrain.Flat()), Has.Some.EqualTo("land: the terrain must cover the map"));
        }

        [Test]
        public void SeedTextIsRepeatable()
        {
            Assert.That(WorldSeed.FromText("42"), Is.EqualTo(42UL));
            Assert.That(WorldSeed.FromText(" 42 "), Is.EqualTo(42UL));
            Assert.That(WorldSeed.FromText("-1"), Is.EqualTo(ulong.MaxValue));
            Assert.That(WorldSeed.FromText("Sunny Valley"), Is.EqualTo(WorldSeed.FromText(" Sunny Valley ")));
            Assert.That(WorldSeed.FromText("Sunny Valley"), Is.Not.EqualTo(WorldSeed.FromText("Rainy Valley")));
            var (requested, seed) = WorldSeed.Resolve("");
            Assert.That(requested, Is.EqualTo(seed.ToString()));
        }
    }
}
