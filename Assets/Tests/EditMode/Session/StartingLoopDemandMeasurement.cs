// P0 measurement M1 (docs/starting-loop-p0-plan.md): the demand rate a basic starting restaurant sees, measured headless. A
// generated world for each P0 seed is created on an isolated save with the real content, its starting register is stocked with
// TEST-ONLY bread that does not spoil during the run and worked by a TEST-ONLY player, and the world is advanced two clock hours
// (7200 clock seconds, the unit districts' CustomersPerHour use). Per clock hour it records customers created in the city, the
// ones that chose this restaurant, its sales and walk-outs, and the first arrival and sale. Writes the numbers to
// docs/verification/starting-loop-baseline-20261006/m1-demand.txt. Runs only on request (Temp/starting-loop.flag), like the
// PlayMode playthrough; it judges nothing, so it fails only if the world cannot be made.
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
    public sealed class StartingLoopDemandMeasurement
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "starting-loop.flag"))))
                Assert.Ignore("The P0 demand measurement runs on request: create Temp/starting-loop.flag.");
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryStartingLoopDemand", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void DemandAtAStockedStaffedStartingRestaurant()
        {
            var report = new StringBuilder();
            report.AppendLine($"P0 M1 demand, {DateTime.UtcNow:u}, Unity {Application.unityVersion}; 7200 clock s per seed, advanced in 60 s steps");
            foreach (var seed in new[] { "piece-two", "p0-second" }) report.Append(Measure(seed));
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "starting-loop-baseline-20261006"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "m1-demand.txt"), report.ToString());
            Debug.Log("[P0] " + report);
        }

        private string Measure(string seed)
        {
            var folder = Path.Combine(_directory, seed);
            Directory.CreateDirectory(folder);
            var worldPath = Path.Combine(folder, SessionOptions.WorldFileName);
            var stored = WorldGeneration.PrepareLayout(worldPath, Path.Combine(folder, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, seed);
            var world = GeneratedWorld.LoadOrCreate(worldPath, stored, SessionTestFiles.ContentItems(),
                AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Counter.asset"),
                AssetDatabase.LoadAssetAtPath<EquipmentDefinition>("Assets/Content/Equipment/Table.asset"));
            var sale = AssetDatabase.LoadAssetAtPath<RecipeAsset>("Assets/Content/Recipes/SellBread.asset");
            world.RegisterRecipe(sale.ToDefinition());
            var state = world.Snapshot();
            var site = state.Sites.Single().Id;
            // TEST-ONLY stock and staff: bread that outlasts the run, and a player working the starting register.
            world.Bootstrap(new GoodsLot
            {
                Id = "test-bread", ItemId = "bread", OwnerId = site, LocationId = GeneratedWorld.StartCounterId + ":in", Quantity = 20, SpoilAfterSeconds = 1_000_000
            });
            world.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("chef"), SiteId = site, Kind = "carried", Capacity = 1 });
            world.Grant("chef", site);
            Assert.That(world.Staff("chef", "staff", GeneratedWorld.StartCounterId, "chef").Accepted, Is.True);

            var start = GeneratedWorld.StartOffer(stored.Layout);
            var inRange = state.Districts.Where(d => Math.Abs(d.MapX - start.AccessX) + Math.Abs(d.MapZ - start.AccessZ) <= d.RangeMetres).ToList();
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine($"seed {seed}: {state.Districts.Count} districts ({state.Districts.Sum(x => x.CustomersPerHour)} customers/h in all), {state.Competitors.Count} competitors; "
                + $"districts whose range reaches this restaurant {inRange.Count} ({inRange.Sum(x => x.CustomersPerHour)} customers/h)");
            text.AppendLine("hour | city customers created | chose this restaurant | sales | walk-outs | stock left");
            var seen = new HashSet<string>(state.Customers.Select(x => x.Id));
            var ours = new HashSet<string>();
            long? firstArrival = null, firstSale = null;
            var clock0 = state.ClockSeconds;
            for (var hour = 0; hour < 2; hour++)
            {
                var created = 0;
                var chose = 0;
                var before = world.Snapshot();
                var served0 = before.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served);
                var walked0 = before.Diners.Where(x => x.RestaurantId == site).Sum(x => x.WalkedOut);
                for (var step = 0; step < 60; step++)
                {
                    world.Advance(60);
                    var now = world.Snapshot();
                    foreach (var customer in now.Customers)
                    {
                        if (seen.Add(customer.Id)) created++;
                        if (customer.RestaurantId == site && ours.Add(customer.Id))
                        {
                            chose++;
                            firstArrival ??= now.ClockSeconds - clock0;
                        }
                    }
                    if (firstSale == null && now.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served) > 0) firstSale = now.ClockSeconds - clock0;
                }
                var after = world.Snapshot();
                text.AppendLine($"{hour + 1} | {created} | {chose} | {after.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served) - served0} | "
                    + $"{after.Diners.Where(x => x.RestaurantId == site).Sum(x => x.WalkedOut) - walked0} | {after.Lots.Where(x => x.LocationId == GeneratedWorld.StartCounterId + ":in").Sum(x => x.Quantity)}");
            }
            text.AppendLine($"first arrival (chose it) at {(firstArrival.HasValue ? firstArrival + " clock s" : "never")}, first sale at {(firstSale.HasValue ? firstSale + " clock s" : "never")} "
                + "(customers already in the city when stocking are counted only if they choose it afterwards; 60 s sampling)");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(world.Snapshot()));
            return text.ToString();
        }
    }
}
