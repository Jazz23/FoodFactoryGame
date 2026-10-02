// Decision 0032 scale check: 100 trucks (the GDD scale table's vehicle count) driving the generated roads of seed 20260927
// at the lunch rush, between 20 restaurant lots with a dock each and ample stock, timed one clock second at a time. Logs one
// [Benchmark] line (truck seconds p99/max, legs entered, queued truck-seconds) and fails if the p99 clock second exceeds one
// 60 FPS frame. Editor Mono timings are not server timings; no save is written.
using System.Diagnostics;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using NUnit.Framework;

namespace FoodFactoryGame.Benchmarks.Tests
{
    public sealed class TruckRoadBenchmarkTests
    {
        private const ulong Seed = 20260927;
        private const int Sites = 20;
        private const int Trucks = 100;
        private const int MeasuredSeconds = 300;
        private const double FrameBudgetMs = 1000.0 / 60.0;

        [Test]
        public void HundredTrucksOnTheGeneratedRoads()
        {
            var layout = WorldGenerator.Generate(Seed.ToString(), Seed).Layout;
            var network = RoadNetwork.For(layout);
            var world = new GoodsWorld("truck-benchmark");
            world.RegisterItem("crate", 50);
            // TEST-ONLY: 20 restaurant lots spread over the city (every n-th in layout order), each a mapped site with a dock.
            var lots = layout.Lots.Where(x => layout.Buildings.First(b => b.Id == x.BuildingId).Category == BuildingCategory.Restaurant).ToList();
            var chosen = Enumerable.Range(0, Sites).Select(i => lots[i * lots.Count / Sites]).ToList();
            foreach (var lot in chosen)
            {
                world.Bootstrap(new GoodsLocation { Id = lot.SiteId + "-storage", SiteId = lot.SiteId, Kind = "storage", Capacity = 1 });
                world.Bootstrap(new SiteLayout { SiteId = lot.SiteId, Width = lot.Width, Depth = lot.Depth });
                world.Bootstrap(new GoodsEquipment
                {
                    Id = lot.SiteId + "-dock", Kind = GoodsWorld.DockKind, SiteId = lot.SiteId, State = EquipmentState.Placed, HolderId = "",
                    Width = 2, Depth = 1, InputCapacity = 80, OutputCapacity = 80
                });
                world.Bootstrap(new GoodsSite { Id = lot.SiteId, Name = lot.SiteId, MapX = lot.Access.X, MapZ = lot.Access.Z });
                world.Bootstrap(new GoodsLot
                {
                    Id = lot.SiteId + "-crates", ItemId = "crate", OwnerId = lot.SiteId, LocationId = lot.SiteId + "-dock:in", Quantity = 4000,
                    SpoilAfterSeconds = 1_000_000
                });
            }
            var company = new GoodsCompany { Id = "co" };
            company.SiteIds.AddRange(chosen.Select(x => x.SiteId));
            world.Bootstrap(company);
            world.RegisterRoads(network);
            foreach (var lot in chosen) world.Grant("boss", lot.SiteId);
            for (var i = 0; i < Sites; i++)
            {
                var from = chosen[i].SiteId;
                var to = chosen[(i + 7) % Sites].SiteId;
                Assert.That(world.CreateRoute("boss", "route-" + i, from + "-dock", to + "-dock", new string[0]).Accepted, Is.True);
            }
            for (var i = 0; i < Trucks; i++)
            {
                var route = i % Sites;
                world.Bootstrap(new GoodsTruck
                {
                    Id = $"truck-{i:000}", CompanyId = "co", Name = "t" + i, CargoSlots = 2, SpeedMetresPerSecond = 15, LoadUnitsPerSecond = 5,
                    State = TruckState.Parked, SiteId = chosen[route].SiteId
                });
                Assert.That(world.AssignTruck("boss", "assign-" + i, $"truck-{i:000}", GoodsWorld.RouteIdFor("boss", "route-" + route)).Accepted, Is.True);
            }
            // The lunch rush, then a minute to get every truck out on the roads.
            world.Advance(12 * RoadTraffic.HourSeconds);
            world.Advance(60);

            var ticks = new double[MeasuredSeconds];
            var shapes = new string[MeasuredSeconds];
            var legs = 0L;
            var queued = 0L;
            // One snapshot a second (outside the timing): the previous second's trucks are this second's before.
            var before = world.Snapshot().Trucks.ToDictionary(x => x.Id, x => (x.LegSegmentId, x.LegFrom));
            for (var second = 0; second < MeasuredSeconds; second++)
            {
                var began = Stopwatch.GetTimestamp();
                world.Advance(1);
                ticks[second] = (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency;
                var after = world.Snapshot().Trucks;
                var entered = after.Count(x => x.OnRoad && before[x.Id] != (x.LegSegmentId, x.LegFrom));
                legs += entered;
                shapes[second] = $"t{second}:{ticks[second]:F1}ms legs={entered} working={after.Count(x => x.State is TruckState.Loading or TruckState.Unloading)} "
                    + $"queued={after.Count(x => x.OnRoad && x.RemainingSeconds == 0)} gc={System.GC.CollectionCount(0)}";
                before = after.ToDictionary(x => x.Id, x => (x.LegSegmentId, x.LegFrom));
                queued += after.Count(x => x.OnRoad && x.RemainingSeconds == 0);
            }
            var state = world.Snapshot();
            var sorted = ticks.OrderBy(x => x).ToArray();
            var p99 = sorted[(int)(MeasuredSeconds * 0.99)];
            var line = $"[Benchmark] trucks on roads: seed {Seed}, {Trucks} trucks, {Sites} sites, hour 12, {MeasuredSeconds} clock seconds: " +
                $"mean={ticks.Average():F2}ms p99={p99:F2}ms max={sorted[^1]:F2}ms | legs entered {legs}, queued truck-seconds {queued}, " +
                $"on the road now {state.Trucks.Count(x => x.OnRoad)}, crates delivered {state.Lots.Where(x => x.LocationId.EndsWith("-dock:out")).Sum(x => x.Quantity)}";
            line += $" | lots {state.Lots.Count} (in docks {state.Lots.Count(x => x.LocationId.EndsWith(":in"))}, out {state.Lots.Count(x => x.LocationId.EndsWith(":out"))}, cargo {state.Lots.Count(x => x.LocationId.EndsWith(":cargo"))}); states "
                + string.Join(",", state.Trucks.GroupBy(x => x.State).Select(x => $"{x.Key}={x.Count()}")) + $" first tick {ticks[0]:F1}ms";
            line += " | slowest: " + string.Join("; ", Enumerable.Range(0, MeasuredSeconds).OrderByDescending(x => ticks[x]).Take(6).Select(x => shapes[x]));
            UnityEngine.Debug.Log(line);
            Assert.That(state.Lots.Sum(x => x.Quantity), Is.EqualTo(Sites * 4000), "Nothing lost or duplicated.");
            Assert.That(legs, Is.GreaterThan(Trucks), "The trucks really drove legs.");
            Assert.That(p99, Is.LessThanOrEqualTo(FrameBudgetMs), line);
        }
    }
}
