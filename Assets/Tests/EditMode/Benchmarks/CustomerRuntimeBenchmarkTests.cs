// Measures the RUNTIME customer code (GoodsWorld, decision 0024) at the GDD section 28 target of about 1,000 concurrent
// customers and 20 restaurants, driven like the live server: AdvanceUncommitted(1) every clock second, one accepted
// player transfer at >=1,000 customers, and a durable commit every 10 s (decision 0016).
// Reports save phases; thresholds: provisional one-frame tick (16.7 ms), decision 0012's commit signals (50 ms, 1 MB).
// Editor Mono timings, isolated temp save. The synthetic world below is test data, not gameplay content.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FoodFactoryGame.Benchmarks.Tests
{
    [Category("Benchmark")]
    public sealed class CustomerRuntimeBenchmarkTests
    {
        private const double FrameBudgetMs = 1000.0 / 60.0;
        private const int PlayerSites = 10;
        private const int Competitors = 10;
        private const int TargetCustomers = 1000;
        private const int MeasuredSeconds = 300;
        private const int CommitEverySeconds = 10;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCustomerRuntimeBenchmark", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(Path.Combine(_directory, "world.db"));
            Directory.Delete(_directory, true);
        }

        // TEST data: 10 player restaurants on a 2 km map, each a company site with 2 counters (200 bread each) and 5 four-seat
        // tables; 10 competitors (3 servers, 20 seats); 5 districts sending 3,000 customers an hour each, with a 700 m range.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("benchmark-world");
            world.RegisterItem("bread", 20);
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 5, Tier = 1, Cuisine = "bakery",
                Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
            });
            for (var site = 0; site < PlayerSites; site++)
            {
                var id = $"site-{site}";
                world.Bootstrap(new GoodsLocation { Id = $"{id}-storage", SiteId = id, Kind = "storage", Capacity = 1 });
                world.Bootstrap(new SiteLayout { SiteId = id, Width = 20, Depth = 20 });
                world.Bootstrap(new GoodsSite { Id = id, Name = id, MapX = site % 5 * 400, MapZ = site / 5 * 800 });
                world.Bootstrap(new GoodsCompany { Id = $"co-{site}", SiteIds = { id } });
                for (var counter = 0; counter < 2; counter++)
                {
                    world.Bootstrap(new GoodsEquipment
                    {
                        Id = $"{id}-counter-{counter}", Kind = GoodsWorld.CounterKind, SiteId = id, CellX = counter * 3, Width = 2, Depth = 1,
                        InputCapacity = 10, OutputCapacity = 1
                    });
                    world.Bootstrap(new GoodsLot
                    {
                        Id = $"{id}-bread-{counter}", ItemId = "bread", OwnerId = id, LocationId = $"{id}-counter-{counter}:in",
                        Quantity = 200, SpoilAfterSeconds = 100000
                    });
                }
                for (var table = 0; table < 5; table++)
                    world.Bootstrap(new GoodsEquipment
                    {
                        Id = $"{id}-table-{table}", Kind = GoodsWorld.TableKind, SiteId = id, CellX = table * 3, CellZ = 5, Width = 2, Depth = 1,
                        InputCapacity = 1, OutputCapacity = 1, Seats = 4
                    });
            }
            for (var index = 0; index < Competitors; index++)
                world.Bootstrap(new GoodsCompetitor
                {
                    Id = $"competitor-{index}", Name = $"Competitor {index}", MapX = index % 5 * 400 + 200, MapZ = index / 5 * 800 + 400,
                    Cuisine = index % 2 == 0 ? "bakery" : "noodles", Tier = 1 + index % 3, PriceCents = 300 + 50 * index, Servers = 3,
                    ServiceSeconds = 10, Seats = 20
                });
            for (var index = 0; index < 5; index++)
                world.Bootstrap(new GoodsDistrict
                {
                    Id = $"district-{index}", Name = $"District {index}", MapX = index * 400, MapZ = 400, CustomersPerHour = 3000,
                    WealthPercent = 20 * index, AppearanceVariants = 4, LikedCuisines = { index % 2 == 0 ? "bakery" : "noodles" },
                    DineInPercent = 60, RangeMetres = 700
                });
            world.Grant("benchmark-player", "site-0");
            return world;
        }

        [Test]
        public void ThousandCustomersFitAFrameAndCommitUnderTheWarningSignals()
        {
            var world = CreateWorld();
            var path = Path.Combine(_directory, "world.db");
            GoodsSnapshotStore.Save(world, path);
            GoodsSnapshotStore.Hold(path);

            // Warm up (and JIT) until the population is near the target, the way a running world reaches steady state.
            var warmup = 0;
            while (world.Snapshot().Customers.Count < TargetCustomers && warmup < 1800)
            {
                for (var second = 0; second < 30; second++) world.AdvanceUncommitted(1);
                warmup += 30;
            }
            Assert.That(world.TryCommitDurably(path), Is.True);
            var start = world.Snapshot();
            Assert.That(start.Customers.Count, Is.GreaterThanOrEqualTo(TargetCustomers),
                $"The synthetic districts must reach the target population (reached {start.Customers.Count} after {warmup} s).");

            // One accepted player command at the target population, including Durably's rollback copy and the save.
            GoodsSnapshotStore.Stats.Reset();
            var commandBegan = Stopwatch.GetTimestamp();
            var outcome = world.TransferDurably("benchmark-player", new TransferIntent
            {
                RequestId = "benchmark-transfer", LotId = "site-0-bread-0", DestinationId = "site-0-storage", Quantity = 1
            }, path);
            var commandMs = (Stopwatch.GetTimestamp() - commandBegan) * 1000.0 / Stopwatch.Frequency;
            Assert.That(outcome.Accepted, Is.True, outcome.Reason);
            Assert.That(GoodsSnapshotStore.Stats.Commits, Is.EqualTo(1));
            var commandSave = GoodsSnapshotStore.Stats.LastTimings;
            var commandLine = $"[Benchmark] player transfer: customers={start.Customers.Count} total={commandMs:F2}ms " +
                $"save={commandSave.TotalMilliseconds:F2}ms wrapper/action={commandMs - commandSave.TotalMilliseconds:F2}ms " +
                $"save phases copy={commandSave.CopyMilliseconds:F2} validate={commandSave.ValidationMilliseconds:F2} " +
                $"json={commandSave.JsonMilliseconds:F2} transaction={commandSave.TransactionMilliseconds:F2} " +
                $"commit+sync={commandSave.CommitAndSyncMilliseconds:F2}ms";
            TestContext.WriteLine(commandLine);
            Debug.Log(commandLine);

            GoodsSnapshotStore.Stats.Reset();
            var ticks = new double[MeasuredSeconds];
            var savePhases = new GoodsSaveTimings[MeasuredSeconds / CommitEverySeconds];
            var commitIndex = 0;
            for (var second = 0; second < MeasuredSeconds; second++)
            {
                var began = Stopwatch.GetTimestamp();
                world.AdvanceUncommitted(1);
                ticks[second] = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                if ((second + 1) % CommitEverySeconds == 0)
                {
                    Assert.That(world.TryCommitDurably(path), Is.True);
                    savePhases[commitIndex++] = GoodsSnapshotStore.Stats.LastTimings;
                }
            }
            Assert.That(commitIndex, Is.EqualTo(savePhases.Length));

            // The raw simulation step for comparison with the measured uncommitted tick (which now has no rollback copy).
            const int samples = 30;
            var began2 = Stopwatch.GetTimestamp();
            for (var index = 0; index < samples; index++) world.Advance(1);
            var stepMs = (Stopwatch.GetTimestamp() - began2) * 1000.0 / Stopwatch.Frequency / samples;

            var end = world.Snapshot();
            var mean = ticks.Average();
            Array.Sort(ticks);
            var p99 = ticks[(int)(MeasuredSeconds * 0.99)];
            var stats = GoodsSnapshotStore.Stats;
            var line = $"[Benchmark] runtime customers: warmup={warmup}s customers {start.Customers.Count}->{end.Customers.Count} " +
                $"({string.Join(",", end.Customers.GroupBy(x => x.State).Select(x => $"{x.Key}={x.Count()}"))}) " +
                $"served={end.Diners.Sum(x => x.Served)} walkedOut={end.Diners.Sum(x => x.WalkedOut)} | tick mean={mean:F2}ms " +
                $"p99={p99:F2}ms max={ticks[MeasuredSeconds - 1]:F2}ms over {MeasuredSeconds} ticks (step {stepMs:F2}ms) | commits={stats.Commits} " +
                $"avg={stats.AverageMilliseconds:F1}ms max={stats.MaxMilliseconds:F1}ms payload={stats.LastPayloadBytes / 1024.0:F0}KB " +
                $"| cpu=\"{SystemInfo.processorType}\" unity={Application.unityVersion} editor-mono";
            TestContext.WriteLine(line);
            Debug.Log(line);
            var phasesLine = $"[Benchmark] save phases over {commitIndex} tick commits (mean/max ms): " +
                $"copy={Mean(savePhases, x => x.CopyMilliseconds):F2}/{Max(savePhases, x => x.CopyMilliseconds):F2} " +
                $"validate={Mean(savePhases, x => x.ValidationMilliseconds):F2}/{Max(savePhases, x => x.ValidationMilliseconds):F2} " +
                $"json={Mean(savePhases, x => x.JsonMilliseconds):F2}/{Max(savePhases, x => x.JsonMilliseconds):F2} " +
                $"transaction={Mean(savePhases, x => x.TransactionMilliseconds):F2}/{Max(savePhases, x => x.TransactionMilliseconds):F2} " +
                $"commit+sync={Mean(savePhases, x => x.CommitAndSyncMilliseconds):F2}/{Max(savePhases, x => x.CommitAndSyncMilliseconds):F2}";
            TestContext.WriteLine(phasesLine);
            Debug.Log(phasesLine);

            // Every budget is reported, not just the first one missed.
            var missed = new System.Collections.Generic.List<string>();
            if (p99 > FrameBudgetMs) missed.Add($"p99 clock tick {p99:F2} ms exceeds one 60 FPS frame ({FrameBudgetMs:F1} ms)");
            if (stats.MaxMilliseconds > GoodsCommitStats.SlowCommitMilliseconds)
                missed.Add($"max commit {stats.MaxMilliseconds:F1} ms exceeds the decision 0012 signal ({GoodsCommitStats.SlowCommitMilliseconds} ms)");
            if (stats.LastPayloadBytes > GoodsCommitStats.LargePayloadBytes)
                missed.Add($"payload {stats.LastPayloadBytes / 1024} KB exceeds the decision 0012 signal (1024 KB)");
            Assert.That(missed, Is.Empty);
        }

        private static double Mean(GoodsSaveTimings[] timings, Func<GoodsSaveTimings, double> phase) => timings.Average(phase);
        private static double Max(GoodsSaveTimings[] timings, Func<GoodsSaveTimings, double> phase) => timings.Max(phase);
    }
}
