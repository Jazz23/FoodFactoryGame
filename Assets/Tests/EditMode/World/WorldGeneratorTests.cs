// Verifies the world generator (decision 0026) without Unity scenes or saves: the same seed gives the same layout (and a pinned
// hash, so an output change without a new generator version fails), validation holds across many seeds, districts sit where
// GDD section 3 puts them, v2 worlds have land relief, a bridged river, level crossings, junction controls, trees and denser
// blocks, every property has exactly one lot with a reserved site ID (v3, decision 0028), format 1 and 2 layouts still read
// and write unchanged, retries use derived seeds and are reported, the validator catches broken layouts and lots, and every
// generated shell's lot is a valid decision-0019 building site.
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
        // WorldLayoutText.Hash of generator v6's layout for KnownSeed (v1's was c8aed6b7…4269, v2's 6e12b0fd…b598, the pin before v5
        // 5667d34e…47ec, v5's d0a58a01…8d7f). Changing it requires bumping WorldGenerator.Version.
        private const string KnownHash = "75040b43e7bb5f33b7d01c5d70c59049bd0aaa760ebc495d581fb7e412bd4855";
        private const string KnownHashV5 = "d0a58a011d03fa053ddb5e3df9726dd67115863b6289e56dbdd8bd2f82768d7f";
        private const int SeedCount = 120;

        private static WorldLayout Generate(ulong seed) => WorldGenerator.Generate(seed.ToString(), seed).Layout;

        [Test]
        public void SameSeedProducesAnIdenticalLayout()
        {
            var first = WorldLayoutText.Write(Generate(KnownSeed));
            var second = WorldLayoutText.Write(Generate(KnownSeed));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Does.StartWith("food-factory-world-layout 5\ngenerator 6\n").And.Contains("\nlot lot-").And.Contains("\nservice restaurant-"),
                "the same lots, service yards and IDs, in format 5");
            TestContext.WriteLine($"seed {KnownSeed}: sha256 {WorldLayoutText.Hash(first)}, {first.Length} chars");
            Assert.That(WorldLayoutText.Hash(first), Is.EqualTo(KnownHash), "Generator output changed: bump WorldGenerator.Version and re-pin.");
        }

        // Decision 0039: generator v6 is v5 with district rates per game hour. Written back in format 4 with v5's per-3600 s
        // rates, its layout is byte for byte v5's.
        [Test]
        public void GeneratorV6IsV5WithGameHourRates()
        {
            var layout = Generate(KnownSeed);
            layout.FormatVersion = 4;
            layout.GeneratorVersion = 5;
            foreach (var district in layout.Districts) district.CustomersPerHour *= GameClock.RetuneDivisor;
            Assert.That(WorldLayoutText.Hash(WorldLayoutText.Write(layout)), Is.EqualTo(KnownHashV5));
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
            Assert.That(read.FormatVersion, Is.EqualTo(5));
            Assert.That(read.Buildings.Where(x => x.ServiceYard != null).Select(x => (x.Id, x.ServiceYard.X, x.ServiceYard.Z, x.ServiceYard.Width, x.ServiceYard.Depth, x.BackDoor.X, x.BackDoor.Z, x.ServiceDock.X, x.ServiceDock.Z, x.ServiceDock.Width, x.ServiceDock.Depth)),
                Is.EqualTo(layout.Buildings.Where(x => x.ServiceYard != null).Select(x => (x.Id, x.ServiceYard.X, x.ServiceYard.Z, x.ServiceYard.Width, x.ServiceYard.Depth, x.BackDoor.X, x.BackDoor.Z, x.ServiceDock.X, x.ServiceDock.Z, x.ServiceDock.Width, x.ServiceDock.Depth))).And.Not.Empty,
                "service yards round-trip");
            Assert.That(read.Lots.Select(x => (x.Id, x.BuildingId, x.SiteId, x.X, x.Z, x.Width, x.Depth, x.Access.X, x.Access.Z)),
                Is.EqualTo(layout.Lots.Select(x => (x.Id, x.BuildingId, x.SiteId, x.X, x.Z, x.Width, x.Depth, x.Access.X, x.Access.Z))).And.Not.Empty);
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
                AssertOneLotPerProperty(result.Layout, $"seed {seed}");
                buildings.Add(result.Layout.Buildings.Count);
            }
            TestContext.WriteLine($"{SeedCount} seeds valid; {retried} needed a retry; buildings min {buildings.Min()} max {buildings.Max()}");
        }

        // Exactly one lot per property and none for scenery; the lot is the footprint extended forward by the category's setback
        // (with a restaurant's service yard beside it, generator v5),
        // its IDs are the building's reserved ones, and its access cell lies just past its street edge (validity is Validate's).
        private static void AssertOneLotPerProperty(WorldLayout layout, string context)
        {
            var settings = WorldSettings.Default;
            var lots = layout.Lots.GroupBy(x => x.BuildingId).ToDictionary(x => x.Key, x => x.ToList());
            foreach (var building in layout.Buildings)
            {
                if (!building.HasLot)
                {
                    Assert.That(lots.ContainsKey(building.Id) || building.SiteId.Length > 0, Is.False, $"{context}: scenery {building.Id} has no lot");
                    continue;
                }
                Assert.That(lots.TryGetValue(building.Id, out var found) ? found.Count : 0, Is.EqualTo(1), $"{context}: {building.Id}");
                var lot = found[0];
                Assert.That((lot.Id, lot.SiteId, building.SiteId), Is.EqualTo(("lot-" + building.Id, "site-" + building.Id, "site-" + building.Id)), context);
                var rect = WorldGeometry.LotRect(building, settings.SetbackFor(building.Category));
                Assert.That((lot.X, lot.Z, lot.Width, lot.Depth), Is.EqualTo((rect.X, rect.Z, rect.Width, rect.Depth)), $"{context}: {building.Id}");
                var access = WorldGeometry.AccessCell(rect, building.Facing, building.Doors[0]);
                Assert.That((lot.Access.X, lot.Access.Z), Is.EqualTo((access.X, access.Z)), $"{context}: {building.Id}");
            }
            Assert.That(layout.Lots.Count, Is.EqualTo(layout.Buildings.Count(x => x.HasLot)), context);
            Assert.That(layout.Lots.Any(x => x.BuildingId == layout.StartRestaurantId), context);
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
        public void EveryGeneratedShellsLotIsAValidBuildingSite()
        {
            var layout = Generate(KnownSeed);
            var world = new GoodsWorld("shell-check");
            var lots = layout.Lots.ToDictionary(x => x.BuildingId);
            var shells = layout.Buildings.Where(x => x.IsShell).ToList();
            Assert.That(shells.Count(x => x.Category == BuildingCategory.Factory), Is.GreaterThan(0));
            foreach (var shell in shells)
            {
                var lot = lots[shell.Id];
                world.Bootstrap(WorldLayoutShells.SiteLayoutFor(lot));
                Assert.DoesNotThrow(() => world.Bootstrap(WorldLayoutShells.ToGoodsBuilding(shell, lot)), shell.Id);
            }
            var buildings = world.Snapshot().Buildings;
            Assert.That(buildings.Count, Is.EqualTo(shells.Count));
            // A factory's apron lies in front of its doors: from the door wall to the lot's street edge is the factory setback.
            var factory = shells.First(x => x.Category == BuildingCategory.Factory);
            var placed = buildings.Single(x => x.Id == factory.Id);
            var site = lots[factory.Id];
            var door = placed.Doors[0];
            var apron = factory.Facing switch
            {
                Facing.North => site.Depth - 1 - door.Z,
                Facing.South => door.Z,
                Facing.East => site.Width - 1 - door.X,
                _ => door.X
            };
            Assert.That(apron, Is.EqualTo(WorldSettings.Default.FactorySetback));

            // The whole catalog (shells, farms and stations) registers as valid property offers.
            var offers = WorldLayoutShells.PropertyOffers(layout);
            Assert.That(offers.Count, Is.EqualTo(layout.Lots.Count));
            Assert.That(offers.Where(x => x.ForSale).Select(x => x.Category).Distinct(),
                Is.EquivalentTo(new[] { GoodsWorld.RestaurantKind, GoodsWorld.FactoryKind, GoodsWorld.FarmCategory, GoodsWorld.StationCategory }));
            Assert.That(offers.Single(x => x.BuildingId == layout.StartRestaurantId).ForSale, Is.False, "the starting restaurant is not sold");
            Assert.DoesNotThrow(() => new GoodsWorld("catalog-check").RegisterPropertyOffers(offers));
        }

        [Test]
        public void TheValidatorReportsBrokenLots()
        {
            List<string> ProblemsAfter(Action<WorldLayout> damage)
            {
                var layout = Generate(3);
                damage(layout);
                return WorldLayoutValidator.Validate(layout);
            }
            WorldBuilding BuildingOf(WorldLayout layout, WorldLot lot) => layout.Buildings.Single(x => x.Id == lot.BuildingId);
            WorldLot Restaurant(WorldLayout layout) => layout.Lots.First(x => BuildingOf(layout, x).Category == BuildingCategory.Restaurant);

            Assert.That(ProblemsAfter(x => x.Lots[0].X += 300), Has.Some.Contains("does not contain its building"));
            Assert.That(ProblemsAfter(x =>
            {
                var (a, b) = (x.Lots[0], x.Lots[1]);
                (b.X, b.Z, b.Width, b.Depth) = (a.X, a.Z, a.Width, a.Depth);
            }), Has.Some.Contains($": overlaps lot-"));
            // A lot stretched forward over its street.
            Assert.That(ProblemsAfter(x =>
            {
                var lot = Restaurant(x);
                switch (BuildingOf(x, lot).Facing)
                {
                    case Facing.North: lot.Depth += 4; break;
                    case Facing.South: lot.Z -= 4; lot.Depth += 4; break;
                    case Facing.East: lot.Width += 4; break;
                    default: lot.X -= 4; lot.Width += 4; break;
                }
            }), Has.Some.Contains("overlaps a road"));
            Assert.That(ProblemsAfter(x => x.Lots[0].Access = null), Has.Some.Contains("access is not a cell beside its edge"));
            Assert.That(ProblemsAfter(x => x.Lots[0].Access = new WorldCell(x.Lots[0].X + 1, x.Lots[0].Z + 1)),
                Has.Some.Contains("access is not a cell beside its edge"));
            // The access point moved to the back of the lot, away from any road.
            Assert.That(ProblemsAfter(x =>
            {
                var lot = Restaurant(x);
                var facing = BuildingOf(x, lot).Facing;
                var (dx, dz) = WorldGeometry.Step(facing);
                var length = facing is Facing.North or Facing.South ? lot.Depth : lot.Width;
                lot.Access = new WorldCell(lot.Access.X - dx * (length + 1), lot.Access.Z - dz * (length + 1));
            }), Has.Some.Contains("access is not on a road"));
            Assert.That(ProblemsAfter(x => x.Lots.RemoveAt(0)), Has.Some.Contains("property without a lot"));
            Assert.That(ProblemsAfter(x => x.Lots.Add(new WorldLot
            {
                Id = x.Lots[0].Id, BuildingId = x.Lots[0].BuildingId, SiteId = x.Lots[0].SiteId, X = x.Lots[0].X, Z = x.Lots[0].Z,
                Width = x.Lots[0].Width, Depth = x.Lots[0].Depth, Access = x.Lots[0].Access
            })), Has.Some.Contains("has more than one lot").And.Some.StartsWith("id: "));
            Assert.That(ProblemsAfter(x => x.Lots[0].SiteId = "site-elsewhere"), Has.Some.Contains("IDs not derived from its building"));
            Assert.That(ProblemsAfter(x => x.Buildings.First(b => !b.HasLot).SiteId = "site-house"), Has.Some.Contains("site ID must be its lot's"));
            Assert.That(ProblemsAfter(x => x.Lots.Add(new WorldLot
            {
                Id = "lot-house", BuildingId = x.Buildings.First(b => !b.HasLot).Id, SiteId = "site-house", Width = 1, Depth = 1, Access = new WorldCell(0, 0)
            })), Has.Some.Contains("not the lot of a property"));
        }

        // Decision 0037 (generator v5): every restaurant property has its service yard, a back door opening into it and a starter
        // dock touching that doorstep; the validator catches each piece out of place.
        [Test]
        public void TheValidatorReportsBrokenServiceYards()
        {
            List<string> ProblemsAfter(Action<WorldBuilding, WorldLayout> damage)
            {
                var layout = Generate(3);
                damage(layout.Buildings.First(x => x.Category == BuildingCategory.Restaurant && x.HasLot), layout);
                return WorldLayoutValidator.Validate(layout);
            }
            Assert.That(ProblemsAfter((b, _) => { }), Is.Empty);
            Assert.That(ProblemsAfter((b, _) => b.ServiceYard = null), Has.Some.Contains("restaurant without a service yard"));
            Assert.That(ProblemsAfter((b, _) => b.BackDoor = b.Doors[0]), Has.Some.Contains("back door is not a free non-corner wall cell"));
            Assert.That(ProblemsAfter((b, _) => b.BackDoor = new WorldCell(b.X, b.Z)), Has.Some.Contains("back door is not a free non-corner wall cell"));
            // The back door moved onto the opposite side wall: its doorstep is outside the yard.
            Assert.That(ProblemsAfter((b, _) =>
            {
                var doorstep = WorldGeometry.Doorstep(b.Footprint, b.BackDoor);
                var flip = b.Facing is Facing.North or Facing.South
                    ? new WorldCell(b.BackDoor.X == b.X ? b.X + b.Width - 1 : b.X, b.BackDoor.Z)
                    : new WorldCell(b.BackDoor.X, b.BackDoor.Z == b.Z ? b.Z + b.Depth - 1 : b.Z);
                Assert.That(doorstep, Is.Not.Null);
                b.BackDoor = flip;
            }), Has.Some.Contains("back door does not open into the service yard"));
            Assert.That(ProblemsAfter((b, _) => b.ServiceDock = new WorldRect(b.ServiceDock.X, b.ServiceDock.Z, 2, 2)), Has.Some.Contains("not a 2 x 1 piece"));
            // The dock slid along the yard, away from the doorstep.
            Assert.That(ProblemsAfter((b, _) =>
            {
                var (dx, dz) = WorldGeometry.Step(b.Facing);
                b.ServiceDock = new WorldRect(b.ServiceDock.X + 3 * dx, b.ServiceDock.Z + 3 * dz, b.ServiceDock.Width, b.ServiceDock.Depth);
            }), Has.Some.Contains("does not touch the back door's doorstep"));
            Assert.That(ProblemsAfter((b, layout) => layout.Buildings.First(x => !x.HasLot).ServiceYard = b.ServiceYard),
                Has.Some.Contains("only a format 4 restaurant property has a service yard"));
        }

        // Stored format 2 worlds (generator v2) have no lots: they still read, write back byte-for-byte and validate, and list
        // nothing for sale.
        [Test]
        public void AFormatTwoLayoutHasNoLotsAndRoundTrips()
        {
            var layout = Generate(5);
            layout.FormatVersion = 2;
            layout.GeneratorVersion = 2;
            layout.Lots.Clear();
            foreach (var building in layout.Buildings) building.SiteId = "";
            var text = WorldLayoutText.Write(layout);
            Assert.That(text, Does.StartWith("food-factory-world-layout 2\n").And.Not.Contains("\nlot "));
            var read = WorldLayoutText.Read(text);
            Assert.That(WorldLayoutText.Write(read), Is.EqualTo(text));
            Assert.That(read.Lots, Is.Empty);
            Assert.That(WorldLayoutValidator.Validate(read), Is.Empty);
            Assert.That(WorldLayoutShells.PropertyOffers(read), Is.Empty);
            // Lots are not part of format 2: a format 2 layout carrying them fails validation.
            var withLots = Generate(5);
            withLots.FormatVersion = 2;
            Assert.That(WorldLayoutValidator.Validate(withLots), Has.Some.EqualTo("lot: formats 1 and 2 have no lots"));
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
            Assert.That(layout.Lots, Is.Empty, "format 1 has no lots");
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
