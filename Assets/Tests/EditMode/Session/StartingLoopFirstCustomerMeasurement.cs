// Acceptance measurement for starting-loop decision 28 (customers spawn by the street): how long the first customer takes at a
// stocked, staffed starting register in a new generated world, on both P0 seeds. For each seed a new world is created on an
// isolated save with the real content and run unattended for 0, 60, 120, 180 or 240 clock s (customers go to competitors
// meanwhile); then the starting register is stocked with TEST-ONLY bread that does not spoil and worked by a TEST-ONLY player, and
// the world advances in one-second steps until the first sale or 300 s. Each opening time is a fresh world, so the five runs per
// seed are separate draws of the deterministic customer stream. Writes docs/verification/start-rules-20261012/first-customer.txt
// and fails if the mean first arrival over the ten runs exceeds 120 s, or a run has none in 300 s (owner decision 30: about one to
// two minutes is accepted). Runs only on request (Temp/starting-loop.flag), like the P4 measurements.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class StartingLoopFirstCustomerMeasurement
    {
        private const int LimitSeconds = 300;
        // PROTOTYPE (owner decision 30): the mean first arrival is about one to two minutes.
        private const int TargetMeanSeconds = 120;
        private static readonly int[] OpenAfter = { 0, 60, 120, 180, 240 };
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "starting-loop.flag"))))
                Assert.Ignore("The first-customer measurement runs on request: create Temp/starting-loop.flag.");
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryFirstCustomer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void TheFirstCustomerComesInAboutOneToTwoMinutes()
        {
            var report = new StringBuilder();
            report.AppendLine($"First customer at a stocked, staffed starting register, {DateTime.UtcNow:u}, Unity {Application.unityVersion}; generator "
                + $"v{WorldGenerator.Version}, street spawn {WorldLayoutCustomers.StreetSpawnMetres} m, walk {GoodsWorld.WalkMetresPerSecond} m/s, "
                + $"game hour {GameClock.HourSeconds} clock s; 1 s steps; target: mean first arrival at most {TargetMeanSeconds} s");
            report.AppendLine("seed | opened at | districts in range (per game hour) | first decision | first arrival | first sale");
            var arrivals = new List<(string Seed, int Open, long? Arrival)>();
            foreach (var seed in new[] { "piece-two", "p0-second" })
            foreach (var open in OpenAfter)
            {
                var (line, arrival) = Measure(seed, open);
                report.AppendLine(line);
                arrivals.Add((seed, open, arrival));
            }
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "start-rules-20261012"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "first-customer.txt"), report.ToString());
            Debug.Log("[Start rules] " + report);
            var times = arrivals.Where(x => x.Arrival.HasValue).Select(x => x.Arrival.Value).OrderBy(x => x).ToList();
            var summary = times.Count == 0 ? "no arrivals"
                : $"mean {times.Average():F0} s, median {(times[(times.Count - 1) / 2] + times[times.Count / 2]) / 2.0:F0} s, min {times.First()} s, max {times.Last()} s, "
                  + $"{times.Count(x => x < 60)} of {arrivals.Count} under 60 s";
            File.AppendAllText(Path.Combine(output, "first-customer.txt"), summary + Environment.NewLine);
            Debug.Log("[Start rules] " + summary);
            Assert.That(arrivals.Where(x => x.Arrival is null).Select(x => $"{x.Seed} opened at {x.Open} s"), Is.Empty, "Every run has a first customer.");
            Assert.That(times.Average(), Is.LessThanOrEqualTo(TargetMeanSeconds), summary);
        }

        private static EquipmentDefinition Definition(string name) => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>($"Assets/Content/Equipment/{name}.asset");

        private (string Line, long? Arrival) Measure(string seed, int openAfter)
        {
            var folder = Path.Combine(_directory, $"{seed}-{openAfter}");
            Directory.CreateDirectory(folder);
            var worldPath = Path.Combine(folder, SessionOptions.WorldFileName);
            var stored = WorldGeneration.PrepareLayout(worldPath, Path.Combine(folder, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, seed);
            var world = GeneratedWorld.LoadOrCreate(worldPath, stored, SessionTestFiles.ContentItems(), Definition("Counter"), Definition("Table"), Definition("Dock"));
            world.RegisterRecipe(AssetDatabase.LoadAssetAtPath<FoodBalance>("Assets/Content/FoodBalance.asset").Menu()
                .Single(x => x.Id == "counter-sell-bread").ToDefinition());
            var start = GeneratedWorld.StartOffer(stored.Layout);
            var site = start.SiteId;
            for (var second = 0; second < openAfter; second++) world.Advance(1);

            // TEST-ONLY staff and stock: a player working the starting register, and bread that outlasts the run.
            world.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("chef"), SiteId = site, Kind = "carried", Capacity = 1 });
            world.Grant("chef", site);
            Assert.That(world.Staff("chef", "staff", GeneratedWorld.StartCounterId, "chef").Accepted, Is.True);
            world.Bootstrap(new GoodsLot
            {
                Id = "test-bread", ItemId = "bread", OwnerId = site, LocationId = GeneratedWorld.StartCounterId + ":in", Quantity = 10,
                SpoilAfterSeconds = 1_000_000
            });

            var state = world.Snapshot();
            var inRange = state.Districts.Where(d => Math.Abs(d.MapX - start.AccessX) + Math.Abs(d.MapZ - start.AccessZ) <= d.RangeMetres).ToList();
            var clock0 = state.ClockSeconds;
            long? firstChoice = null, firstArrival = null, firstSale = null;
            for (var second = 0; second < LimitSeconds && firstSale == null; second++)
            {
                world.Advance(1);
                var now = world.Snapshot();
                var at = now.ClockSeconds - clock0;
                foreach (var customer in now.Customers.Where(x => x.RestaurantId == site))
                {
                    firstChoice ??= at;
                    if (customer.State != CustomerState.Travelling) firstArrival ??= at;
                }
                if (now.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served) > 0) firstSale = at;
            }
            Assert.DoesNotThrow(() => GoodsWorld.Validate(world.Snapshot()));
            Assert.That(state.Districts.Select(x => x.SpawnWalkMetres).Distinct(), Is.EqualTo(new[] { WorldLayoutCustomers.StreetSpawnMetres }));
            string At(long? seconds) => seconds.HasValue ? seconds + " s" : $"none in {LimitSeconds} s";
            return ($"{seed} | {openAfter} s | {inRange.Count} ({inRange.Sum(x => x.CustomersPerHour)}) | {At(firstChoice)} | {At(firstArrival)} | {At(firstSale)}",
                firstArrival);
        }
    }
}
