// P4 measurement M-P4 (docs/starting-loop-p4-plan.md, decision 0039): the pacing a stocked, staffed starting register sees with
// district rates on the game hour, for each P0 seed and a grid of TEST-ONLY re-tune divisors around GameClock.RetuneDivisor. A
// generated world is created on an isolated save with the real content; for a candidate divisor its stored district rates are
// rescaled in the save (TEST-ONLY) and the world is reopened. The starting register is kept stocked with TEST-ONLY bread that
// does not spoil and worked by a TEST-ONLY player, and the world advances 600 clock s (ten game hours, ten real minutes) in
// one-second steps. Per game hour it records customers created and alive in the city, and at this restaurant the customers
// that chose it, arrived, bought and walked out, its longest queue and busiest seats; plus the first decision, arrival and
// sale. Writes docs/verification/starting-loop-p4-20261007/m-p4-demand.txt. Runs only on request (Temp/starting-loop.flag); it
// judges nothing, so it fails only if a world cannot be made.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using NUnit.Framework;
using SQLite;
using UnityEditor;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class StartingLoopPacingMeasurement
    {
        private const int Seconds = 600;
        // TEST-ONLY candidates; GameClock.RetuneDivisor is what the game uses.
        private static readonly int[] Divisors = { 6, 10, 15 };
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "starting-loop.flag"))))
                Assert.Ignore("The P4 pacing measurement runs on request: create Temp/starting-loop.flag.");
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryStartingLoopPacing", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void PacingAtAStockedStaffedStartingRestaurant()
        {
            var report = new StringBuilder();
            report.AppendLine($"P4 M-P4 pacing, {DateTime.UtcNow:u}, Unity {Application.unityVersion}; {Seconds} clock s (game hours of {GameClock.HourSeconds} s) "
                + $"per run, 1 s steps; game divisor {GameClock.RetuneDivisor}; walk {GoodsWorld.WalkMetresPerSecond} m/s");
            foreach (var seed in new[] { "piece-two", "p0-second" })
            foreach (var divisor in Divisors)
                report.Append(Measure(seed, divisor));
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "starting-loop-p4-20261007"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "m-p4-demand.txt"), report.ToString());
            Debug.Log("[P4] " + report);
        }

        private static EquipmentDefinition Definition(string name) => AssetDatabase.LoadAssetAtPath<EquipmentDefinition>($"Assets/Content/Equipment/{name}.asset");

        private string Measure(string seed, int divisor)
        {
            var folder = Path.Combine(_directory, $"{seed}-{divisor}");
            Directory.CreateDirectory(folder);
            var worldPath = Path.Combine(folder, SessionOptions.WorldFileName);
            var stored = WorldGeneration.PrepareLayout(worldPath, Path.Combine(folder, SessionOptions.LegacyWorldFileName), DevWorld.WorldId, seed);
            GeneratedWorld.LoadOrCreate(worldPath, stored, SessionTestFiles.ContentItems(), Definition("Counter"), Definition("Table"), Definition("Dock"));
            if (divisor != GameClock.RetuneDivisor) Rescale(worldPath, divisor);
            var world = GeneratedWorld.LoadOrCreate(worldPath, stored, SessionTestFiles.ContentItems(), Definition("Counter"), Definition("Table"), Definition("Dock"));
            var sale = AssetDatabase.LoadAssetAtPath<RecipeAsset>("Assets/Content/Recipes/SellBread.asset");
            world.RegisterRecipe(sale.ToDefinition());
            var state = world.Snapshot();
            var site = GeneratedWorld.StartOffer(stored.Layout).SiteId;
            var register = GeneratedWorld.StartCounterId + ":in";
            // TEST-ONLY staff: a player working the starting register.
            world.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("chef"), SiteId = site, Kind = "carried", Capacity = 1 });
            world.Grant("chef", site);
            Assert.That(world.Staff("chef", "staff", GeneratedWorld.StartCounterId, "chef").Accepted, Is.True);
            var restocks = 0;
            void Restock()
            {
                // TEST-ONLY stock: bread that outlasts the run, topped up so the register never runs dry.
                if (world.Snapshot().Lots.Where(x => x.LocationId == register).Sum(x => x.Quantity) >= 10) return;
                world.Bootstrap(new GoodsLot
                {
                    Id = $"test-bread-{restocks++}", ItemId = "bread", OwnerId = site, LocationId = register, Quantity = 10, SpoilAfterSeconds = 1_000_000
                });
            }
            Restock();

            var start = GeneratedWorld.StartOffer(stored.Layout);
            var inRange = state.Districts.Where(d => Math.Abs(d.MapX - start.AccessX) + Math.Abs(d.MapZ - start.AccessZ) <= d.RangeMetres).ToList();
            var walks = inRange.Select(d => (Math.Abs(d.MapX - start.AccessX) + Math.Abs(d.MapZ - start.AccessZ)) / GoodsWorld.WalkMetresPerSecond).OrderBy(x => x).ToList();
            var text = new StringBuilder();
            text.AppendLine();
            text.AppendLine($"seed {seed}, divisor {divisor}: {state.Districts.Count} districts ({state.Districts.Sum(x => x.CustomersPerHour)} customers per game hour in all), "
                + $"{state.Competitors.Count} competitors; {inRange.Count} districts reach this restaurant ({inRange.Sum(x => x.CustomersPerHour)} per game hour), "
                + $"walk from their centres {(walks.Count > 0 ? $"{walks.First()}-{walks.Last()} s" : "none")}; start table seats "
                + state.Equipment.Where(x => x.Id == GeneratedWorld.StartTableId).Sum(x => x.Seats));
            text.AppendLine("game hour | city created | city alive (peak) | chose this | arrived | sales | walk-outs | longest queue | busiest seats");

            var seen = new HashSet<string>(state.Customers.Select(x => x.Id));
            var chose = new HashSet<string>();
            var arrived = new HashSet<string>();
            long? firstChoice = null, firstArrival = null, firstSale = null;
            var clock0 = state.ClockSeconds;
            long totalSales = 0, totalWalked = 0;
            var peakAlive = 0;
            for (var hour = 0; hour < Seconds / GameClock.HourSeconds; hour++)
            {
                int created = 0, chosen = 0, came = 0, alive = 0, queue = 0, seats = 0;
                var before = world.Snapshot();
                var served0 = before.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served);
                var walked0 = before.Diners.Where(x => x.RestaurantId == site).Sum(x => x.WalkedOut);
                for (var second = 0; second < GameClock.HourSeconds; second++)
                {
                    world.Advance(1);
                    Restock();
                    var now = world.Snapshot();
                    var at = now.ClockSeconds - clock0;
                    alive = Math.Max(alive, now.Customers.Count);
                    foreach (var customer in now.Customers)
                    {
                        if (seen.Add(customer.Id)) created++;
                        if (customer.RestaurantId != site) continue;
                        if (chose.Add(customer.Id))
                        {
                            chosen++;
                            firstChoice ??= at;
                        }
                        if (customer.State != CustomerState.Travelling && arrived.Add(customer.Id))
                        {
                            came++;
                            firstArrival ??= at;
                        }
                    }
                    queue = Math.Max(queue, now.Customers.Count(x => x.RestaurantId == site && x.State == CustomerState.Queued));
                    seats = Math.Max(seats, now.Customers.Count(x => x.RestaurantId == site && x.State == CustomerState.Eating));
                    if (firstSale == null && now.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served) > 0) firstSale = at;
                }
                var after = world.Snapshot();
                var sales = after.Diners.Where(x => x.RestaurantId == site).Sum(x => x.Served) - served0;
                var walked = after.Diners.Where(x => x.RestaurantId == site).Sum(x => x.WalkedOut) - walked0;
                totalSales += sales;
                totalWalked += walked;
                peakAlive = Math.Max(peakAlive, alive);
                text.AppendLine($"{hour + 1} | {created} | {alive} | {chosen} | {came} | {sales} | {walked} | {queue} | {seats}");
            }
            string At(long? seconds) => seconds.HasValue ? seconds + " s" : "never";
            text.AppendLine($"first decision for it {At(firstChoice)}, first arrival {At(firstArrival)}, first sale {At(firstSale)}; "
                + $"{totalSales} sales and {totalWalked} walk-outs in {Seconds / GameClock.HourSeconds} game hours "
                + $"({(double)totalSales * GameClock.HourSeconds / Seconds:F1} sales per real minute); city peak {peakAlive} customers alive; "
                + $"{restocks} TEST-ONLY restocks of 10 bread");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(world.Snapshot()));
            return text.ToString();
        }

        // TEST-ONLY: rescales every stored district rate from the game's divisor to the candidate's, as if the save had been
        // re-tuned with it, keeping the checksum valid.
        private static void Rescale(string worldPath, int divisor)
        {
            string payload;
            using (var db = new SQLiteConnection(worldPath, SQLiteOpenFlags.ReadOnly))
                payload = db.ExecuteScalar<string>("SELECT payload FROM snapshots ORDER BY revision DESC LIMIT 1");
            var scaled = Regex.Replace(payload, "\"CustomersPerHour\":(\\d+)",
                m => $"\"CustomersPerHour\":{(int.Parse(m.Groups[1].Value) * GameClock.RetuneDivisor * 2 + divisor) / (2 * divisor)}");
            using var sha = SHA256.Create();
            var digest = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(scaled)));
            using var write = new SQLiteConnection(worldPath, SQLiteOpenFlags.ReadWrite);
            write.Execute("UPDATE snapshots SET payload = ?, sha256 = ? WHERE revision = (SELECT MAX(revision) FROM snapshots)", scaled, digest);
        }
    }
}
