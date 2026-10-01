// Measures the RUNTIME customer code at a generated city's scale (decision 0030's scale gate): the real generator's layout for
// seed "piece-two", every map district and competitor derived by WorldLayoutCustomers, and one player restaurant (the starting
// lot) with a counter and table. Driven like the live server: AdvanceUncommitted(1) every clock second with a durable commit
// every 10 s (decision 0016). Reports tick mean/p99/max, two decision spikes (100 customers deciding in one second, and a
// 60-second catch-up step) and save phases, payload and commit time against decision 0025's signals (16.7 ms tick p99, 50 ms
// commit, 1 MB payload). Editor Mono timings, isolated temp save. The stock and burst district are test data, not content.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FoodFactoryGame.Benchmarks.Tests
{
    [Category("Benchmark")]
    public sealed class CityCustomerBenchmarkTests
    {
        private const double FrameBudgetMs = 1000.0 / 60.0;
        private const int WarmupSeconds = 1800;
        private const int MeasuredSeconds = 300;
        private const int CommitEverySeconds = 10;
        private const int BurstCustomers = 100;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCityCustomerBenchmark", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(Path.Combine(_directory, "world.db"));
            Directory.Delete(_directory, true);
        }

        private static double Ms(long began) => (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;

        // The generated city with its customers, plus TEST data: the starting lot owned by one company, a 2x1 counter with 400
        // bread that does not spoil during the run and a four-seat table inside its shell, and the real bread sale recipe values.
        private static (GoodsWorld World, string SiteId) CreateCity()
        {
            var (requested, seed) = WorldSeed.Resolve("piece-two");
            var layout = WorldGenerator.Generate(requested, seed).Layout;
            var world = new GoodsWorld("city-benchmark");
            world.RegisterItem("bread", 20);
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 5, Tier = 1, Cuisine = "bakery",
                Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
            });
            world.RegisterPropertyOffers(WorldLayoutShells.PropertyOffers(layout));
            var start = WorldLayoutShells.PropertyOffers(layout).Single(x => x.BuildingId == layout.StartRestaurantId);
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = 100_000_000 });
            world.Bootstrap(start, "company");
            world.Bootstrap(new GoodsEquipment
            {
                Id = "counter", Kind = GoodsWorld.CounterKind, SiteId = start.SiteId, CellX = start.BuildingX + 2, CellZ = start.BuildingZ + 2,
                Width = 2, Depth = 1, InputCapacity = 20, OutputCapacity = 1
            });
            world.Bootstrap(new GoodsEquipment
            {
                Id = "table", Kind = GoodsWorld.TableKind, SiteId = start.SiteId, CellX = start.BuildingX + 2, CellZ = start.BuildingZ + 4,
                Width = 2, Depth = 1, InputCapacity = 1, OutputCapacity = 1, Seats = 4
            });
            world.Bootstrap(new GoodsLot
            {
                Id = "bread", ItemId = "bread", OwnerId = start.SiteId, LocationId = "counter:in", Quantity = 400, SpoilAfterSeconds = 10_000_000
            });
            foreach (var district in WorldLayoutCustomers.Districts(layout)) world.Bootstrap(district);
            foreach (var competitor in WorldLayoutCustomers.Competitors(layout)) world.Bootstrap(competitor);
            return (world, start.SiteId);
        }

        [Test]
        public void AGeneratedCityFitsAFrameAndCommitsUnderTheWarningSignals()
        {
            var (world, siteId) = CreateCity();
            var path = Path.Combine(_directory, "world.db");
            GoodsSnapshotStore.Save(world, path);
            GoodsSnapshotStore.Hold(path);
            var setup = world.Snapshot();

            // Warm up (and JIT) to steady state: customers travel, queue, eat and leave across the city.
            for (var second = 0; second < WarmupSeconds; second++) world.AdvanceUncommitted(1);
            Assert.That(world.TryCommitDurably(path), Is.True);
            var start = world.Snapshot();

            GoodsSnapshotStore.Stats.Reset();
            var ticks = new double[MeasuredSeconds];
            var savePhases = new GoodsSaveTimings[MeasuredSeconds / CommitEverySeconds];
            var commitIndex = 0;
            for (var second = 0; second < MeasuredSeconds; second++)
            {
                var began = Stopwatch.GetTimestamp();
                world.AdvanceUncommitted(1);
                ticks[second] = Ms(began);
                if ((second + 1) % CommitEverySeconds == 0)
                {
                    Assert.That(world.TryCommitDurably(path), Is.True);
                    savePhases[commitIndex++] = GoodsSnapshotStore.Stats.LastTimings;
                }
            }
            var stats = GoodsSnapshotStore.Stats;
            var end = world.Snapshot();

            // Spike 1: a copy of the world where BurstCustomers appear in one second at the player's own block (the densest
            // candidate area), so every one of them scores every restaurant within 450 m.
            var burstWorld = GoodsWorld.Restore(end);
            burstWorld.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 5, Tier = 1, Cuisine = "bakery",
                Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
            });
            var site = end.Sites.Single(x => x.Id == siteId);
            burstWorld.Bootstrap(new GoodsDistrict
            {
                Id = "burst", Name = "Burst", MapX = site.MapX, MapZ = site.MapZ, CustomersPerHour = BurstCustomers * 3600, WealthPercent = 50,
                AppearanceVariants = 1, LikedCuisines = { "bakery" }, DineInPercent = 50, RangeMetres = 450
            });
            var burstCandidates = end.Competitors.Count(x => Math.Abs(x.MapX - site.MapX) + Math.Abs(x.MapZ - site.MapZ) <= 450) + 1;
            GoodsSnapshotStore.Save(burstWorld, Path.Combine(_directory, "burst.db"));
            burstWorld.AdvanceUncommitted(1);
            var burstBegan = Stopwatch.GetTimestamp();
            burstWorld.AdvanceUncommitted(1);
            var burstMs = Ms(burstBegan);

            // Spike 2: a 60-second catch-up step (a server hitch), every decision of that minute in one call.
            var catchUpBegan = Stopwatch.GetTimestamp();
            world.AdvanceUncommitted(60);
            var catchUpMs = Ms(catchUpBegan);

            var mean = ticks.Average();
            Array.Sort(ticks);
            var p99 = ticks[(int)(MeasuredSeconds * 0.99)];
            var here = end.Diners.FirstOrDefault(x => x.RestaurantId == siteId);
            var line = $"[Benchmark] city customers: districts={setup.Districts.Count} competitors={setup.Competitors.Count} " +
                $"warmup={WarmupSeconds}s customers {start.Customers.Count}->{end.Customers.Count} " +
                $"({string.Join(",", end.Customers.GroupBy(x => x.State).Select(x => $"{x.Key}={x.Count()}"))}) diners={end.Diners.Count} " +
                $"served={end.Diners.Sum(x => x.Served)} (player {here?.Served ?? 0}) walkedOut={end.Diners.Sum(x => x.WalkedOut)} | " +
                $"tick mean={mean:F3}ms p99={p99:F3}ms max={ticks[MeasuredSeconds - 1]:F3}ms over {MeasuredSeconds} ticks | " +
                $"burst {BurstCustomers} decisions x {burstCandidates} candidates={burstMs:F2}ms catch-up 60s={catchUpMs:F2}ms | " +
                $"commits={stats.Commits} avg={stats.AverageMilliseconds:F1}ms max={stats.MaxMilliseconds:F1}ms " +
                $"payload={stats.LastPayloadBytes / 1024.0:F0}KB | cpu=\"{SystemInfo.processorType}\" unity={Application.unityVersion} editor-mono";
            TestContext.WriteLine(line);
            Debug.Log(line);
            var phasesLine = $"[Benchmark] city save phases over {commitIndex} tick commits (mean/max ms): " +
                $"validate={savePhases.Average(x => x.ValidationMilliseconds):F2}/{savePhases.Max(x => x.ValidationMilliseconds):F2} " +
                $"json={savePhases.Average(x => x.JsonMilliseconds):F2}/{savePhases.Max(x => x.JsonMilliseconds):F2} " +
                $"transaction={savePhases.Average(x => x.TransactionMilliseconds):F2}/{savePhases.Max(x => x.TransactionMilliseconds):F2} " +
                $"commit+sync={savePhases.Average(x => x.CommitAndSyncMilliseconds):F2}/{savePhases.Max(x => x.CommitAndSyncMilliseconds):F2}";
            TestContext.WriteLine(phasesLine);
            Debug.Log(phasesLine);

            Assert.That(setup.Competitors.Count, Is.GreaterThan(250), "The benchmark runs at a generated city's competitor count.");
            Assert.That(end.Customers.Count, Is.GreaterThan(0));
            var missed = new List<string>();
            if (p99 > FrameBudgetMs) missed.Add($"p99 clock tick {p99:F2} ms exceeds one 60 FPS frame ({FrameBudgetMs:F1} ms)");
            if (burstMs > FrameBudgetMs) missed.Add($"{BurstCustomers}-customer burst tick {burstMs:F2} ms exceeds one frame");
            if (stats.MaxMilliseconds > GoodsCommitStats.SlowCommitMilliseconds)
                missed.Add($"max commit {stats.MaxMilliseconds:F1} ms exceeds the decision 0012 signal ({GoodsCommitStats.SlowCommitMilliseconds} ms)");
            if (stats.LastPayloadBytes > GoodsCommitStats.LargePayloadBytes)
                missed.Add($"payload {stats.LastPayloadBytes / 1024} KB exceeds the decision 0012 signal (1024 KB)");
            Assert.That(missed, Is.Empty);
        }

        // Decision 0033's crowd budget: after the same warm-up (volatile Advance, nothing saved), every clock second builds one
        // crowd view per connection. Eight
        // connections stand at the starting lot and at the seven competitors nearest it (the densest rival rows), measured over
        // MeasuredSeconds seconds. Reports the cost per view and the JSON a client would receive per send.
        [Test]
        public void CrowdViewsForEightConnectionsStayUnderTheirBudget()
        {
            const double ViewBudgetMs = 0.5;
            const int Connections = 8;
            var (world, siteId) = CreateCity();
            for (var second = 0; second < WarmupSeconds; second++) world.Advance(1);
            var snapshot = world.Snapshot();
            var site = snapshot.Sites.Single(x => x.Id == siteId);
            var points = new[] { (X: (float)site.MapX, Z: (float)site.MapZ) }.Concat(snapshot.Competitors.Where(x => x.LotId != "")
                    .OrderBy(x => Math.Abs(x.MapX - site.MapX) + Math.Abs(x.MapZ - site.MapZ)).ThenBy(x => x.Id, StringComparer.Ordinal)
                    .Take(Connections - 1).Select(x => (X: (float)x.MapX, Z: (float)x.MapZ))).ToArray();
            for (var index = 0; index < 50; index++) world.CrowdNear(points[0].X, points[0].Z);

            var views = new List<double>();
            var bytes = new List<int>();
            var restaurants = new List<int>();
            var customers = new List<int>();
            for (var second = 0; second < MeasuredSeconds; second++)
            {
                world.Advance(1);
                foreach (var (x, z) in points)
                {
                    var began = Stopwatch.GetTimestamp();
                    var crowd = world.CrowdNear(x, z);
                    views.Add(Ms(began));
                    bytes.Add(JsonUtility.ToJson(crowd).Length);
                    restaurants.Add(crowd.Restaurants.Count);
                    customers.Add(crowd.Restaurants.Sum(r => r.Customers.Count));
                }
            }
            var sorted = views.OrderBy(x => x).ToList();
            var p99 = sorted[(int)(sorted.Count * 0.99)];
            var line = $"[Benchmark] crowd views: {Connections} connections x {MeasuredSeconds} s after {WarmupSeconds} s warm-up, " +
                $"radius {GoodsWorld.CrowdRadiusMetres} m | view mean={views.Average():F3}ms p99={p99:F3}ms max={sorted.Last():F3}ms " +
                $"per second (all connections) mean={views.Average() * Connections:F3}ms | competitors with customers mean={restaurants.Average():F1} " +
                $"max={restaurants.Max()} | customers sent mean={customers.Average():F1} max={customers.Max()} | " +
                $"json mean={bytes.Average():F0}B max={bytes.Max()}B | cpu=\"{SystemInfo.processorType}\" unity={Application.unityVersion} editor-mono";
            TestContext.WriteLine(line);
            Debug.Log(line);
            Assert.That(restaurants.Max(), Is.GreaterThan(0), "Customers stood at competitors within range of the measured points.");
            Assert.That(p99, Is.LessThanOrEqualTo(ViewBudgetMs), $"A crowd view's p99 exceeds its {ViewBudgetMs} ms budget.");
        }
    }
}
