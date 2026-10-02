// Verifies the drivable road network and the traffic model (decision 0032): a lot's access cell finds its point on the network,
// the quickest route prefers faster roads and is the same every time, routes on a generated world are contiguous and cross
// the river on bridges, the volume-delay curve and admission rule behave, and traffic lights alternate by axis.
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.World.Tests
{
    public sealed class RoadNetworkTests
    {
        private const ulong KnownSeed = 20260927;

        // TEST-ONLY: a 3 x 2 grid of nodes 100 m apart, local streets except the arterial n1-n4 in the middle.
        //   n3 --r2-- n4 --r3-- n5
        //   |r4       |r5       |r6
        //   n0 --r0-- n1 --r1-- n2
        public static WorldLayout Grid(JunctionControl middle = JunctionControl.None, int trafficPercent = -1)
        {
            var layout = new WorldLayout();
            var spots = new[] { (0, 0), (100, 0), (200, 0), (0, 100), (100, 100), (200, 100) };
            for (var i = 0; i < spots.Length; i++)
                layout.Nodes.Add(new RoadNode { Id = $"n{i}", X = spots[i].Item1, Z = spots[i].Item2, Control = i is 1 or 4 ? middle : JunctionControl.None });
            void Road(string id, string from, string to, RoadKind kind) => layout.Roads.Add(new RoadSegment
            {
                Id = id, FromId = from, ToId = to, Kind = kind, Width = kind == RoadKind.Arterial ? 14 : 10,
                CapacityPerHour = kind == RoadKind.Arterial ? 1800 : 600
            });
            Road("r0", "n0", "n1", RoadKind.Local);
            Road("r1", "n1", "n2", RoadKind.Local);
            Road("r2", "n3", "n4", RoadKind.Local);
            Road("r3", "n4", "n5", RoadKind.Local);
            Road("r4", "n0", "n3", RoadKind.Local);
            Road("r5", "n1", "n4", RoadKind.Arterial);
            Road("r6", "n2", "n5", RoadKind.Local);
            if (trafficPercent >= 0)
                layout.Districts.Add(new WorldDistrict
                {
                    Id = "test-district", TrafficPercent = trafficPercent, Areas = { new WorldRect(-10, -10, 220, 120) }
                });
            return layout;
        }

        private static List<RoadLeg> FreeFlow(RoadNetwork network, RoadPoint from, RoadPoint to) => network.Route(from, to,
            (index, _) => RoadTraffic.DriveMillis(network.Segments[index], network.Segments[index].Length, 100, 0),
            (index, forward) => RoadTraffic.JunctionEstimateMillis(network, index, forward, 0));

        private static string Describe(RoadNetwork network, IEnumerable<RoadLeg> legs) =>
            string.Join(" ", legs.Select(x => $"{network.Segments[x.Segment].Id}:{x.From}-{x.To}"));

        [Test]
        public void AccessCellsLocateTheNearestCentreline()
        {
            var network = new RoadNetwork(Grid());
            var farm = network.Locate(10, 2);
            Assert.That((network.Segments[farm.Value.Segment].Id, farm.Value.Offset), Is.EqualTo(("r0", 10)));
            var shop = network.Locate(190, 98);
            Assert.That((network.Segments[shop.Value.Segment].Id, shop.Value.Offset), Is.EqualTo(("r3", 90)));
            Assert.That(network.Locate(500, 500), Is.Null, "Too far from every road.");
        }

        [Test]
        public void TheQuickestRoutePrefersTheArterialAndIsStable()
        {
            var network = new RoadNetwork(Grid());
            var farm = network.Locate(10, 2).Value;
            var shop = network.Locate(190, 98).Value;
            var route = FreeFlow(network, farm, shop);
            Assert.That(Describe(network, route), Is.EqualTo("r0:10-100 r5:0-100 r3:0-90"));
            Assert.That(Describe(network, FreeFlow(network, farm, shop)), Is.EqualTo(Describe(network, route)), "Deterministic.");
            Assert.That(Describe(network, FreeFlow(network, shop, farm)), Is.EqualTo("r3:90-0 r5:100-0 r0:100-10"));
            Assert.That(Describe(network, FreeFlow(network, farm, new RoadPoint(farm.Segment, 60))), Is.EqualTo("r0:10-60"),
                "Along one segment it drives straight there.");
            Assert.That(FreeFlow(network, farm, farm).Single().Length, Is.Zero, "Start equal to end is one zero-length leg.");
        }

        [Test]
        public void ACostlySegmentIsDrivenAround()
        {
            var network = new RoadNetwork(Grid());
            var farm = network.Locate(10, 2).Value;
            var shop = network.Locate(190, 98).Value;
            var arterial = network.Segments.Single(x => x.Id == "r5").Index;
            var route = network.Route(farm, shop,
                (index, _) => index == arterial ? 1_000_000 : RoadTraffic.DriveMillis(network.Segments[index], network.Segments[index].Length, 100, 0),
                (_, _) => 0);
            Assert.That(route.Any(x => x.Segment == arterial), Is.False);
            Assert.That(route.Sum(x => x.Length), Is.EqualTo(300));
        }

        [Test]
        public void GeneratedRoutesAreContiguousAndCrossTheRiverOnBridges()
        {
            var layout = WorldGenerator.Generate(KnownSeed.ToString(), KnownSeed).Layout;
            var network = RoadNetwork.For(layout);
            Assert.That(RoadNetwork.For(layout), Is.SameAs(network), "Built once per layout.");
            var points = layout.Lots.Select(x => (x.Id, Point: network.Locate(x.Access.X, x.Access.Z))).ToList();
            Assert.That(points.Where(x => x.Point is null).Select(x => x.Id), Is.Empty, "Every lot's access cell is on the network.");
            var bridged = new HashSet<string>(layout.Bridges.Select(x => x.CarriesId));
            var start = points.First(x => layout.Lots.First(y => y.Id == x.Id).BuildingId == layout.StartRestaurantId).Point.Value;
            var crossings = 0;
            foreach (var (id, point) in points.Where((_, i) => i % 7 == 0))
            {
                var route = FreeFlow(network, start, point.Value);
                Assert.That(route, Is.Not.Null, id);
                for (var i = 1; i < route.Count; i++)
                    Assert.That(network.EndNode(route[i - 1].Segment, route[i - 1].Forward),
                        Is.EqualTo(route[i].Forward ? network.Segments[route[i].Segment].From : network.Segments[route[i].Segment].To),
                        $"{id}: leg {i} starts where leg {i - 1} ended.");
                if (route.Any(x => bridged.Contains(network.Segments[x.Segment].Id))) crossings++;
            }
            Assert.That(crossings, Is.GreaterThan(0), "Some routes cross the river, on bridged segments.");
        }

        [Test]
        public void LoadSlowsSegmentsAndFullSegmentsRefuseTrucks()
        {
            var network = new RoadNetwork(Grid(trafficPercent: 90));
            var local = network.Segments.Single(x => x.Id == "r0");
            Assert.That(RoadTraffic.DelayPermille(0), Is.EqualTo(1000));
            Assert.That(RoadTraffic.DelayPermille(100), Is.EqualTo(1150), "BPR: 15% slower at capacity.");
            Assert.That(RoadTraffic.DriveMillis(local, 100, 100, 100), Is.GreaterThan(RoadTraffic.DriveMillis(local, 100, 100, 0)));
            Assert.That(RoadTraffic.DriveMillis(local, 100, 5, 0), Is.EqualTo(20_000), "No faster than the truck itself.");
            var rush = 8 * RoadTraffic.HourSeconds;
            var night = 3 * RoadTraffic.HourSeconds;
            Assert.That(RoadTraffic.BackgroundPercent(local, rush), Is.GreaterThan(RoadTraffic.BackgroundPercent(local, night) * 5));
            Assert.That(RoadTraffic.Admits(RoadTraffic.LoadPercent(local, rush, 2), 1, 0), Is.False, "Rush hour: a second truck waits.");
            Assert.That(RoadTraffic.Admits(RoadTraffic.LoadPercent(local, night, 2), 1, 0), Is.True, "At night it gets on.");
            Assert.That(RoadTraffic.Admits(999, 0, 0), Is.True, "An empty segment always admits one truck.");
            Assert.That(RoadTraffic.Admits(999, 3, RoadTraffic.PatienceSeconds), Is.True, "A truck that waited long enough goes.");
        }

        [Test]
        public void TrafficLightsAlternateByAxis()
        {
            var network = new RoadNetwork(Grid(JunctionControl.TrafficLight));
            var node = network.Nodes.Single(x => x.Id == "n1");
            for (var t = 0; t < RoadTraffic.LightCycleSeconds * 2; t++)
            {
                Assert.That(RoadTraffic.IsGreen(node, true, t) && RoadTraffic.IsGreen(node, false, t), Is.False, $"t={t}");
                var waitX = RoadTraffic.SecondsUntilGreen(node, true, t);
                Assert.That(RoadTraffic.IsGreen(node, true, t + waitX), Is.True, $"t={t}: green after waiting {waitX} s.");
                Assert.That(waitX == 0, Is.EqualTo(RoadTraffic.IsGreen(node, true, t)));
            }
            var r0 = network.Segments.Single(x => x.Id == "r0").Index;
            Assert.That(network.Stops(r0, true), Is.True, "A light stops every approach.");
            Assert.That(network.Stops(r0, false), Is.False, "n0 has no control.");
        }
    }
}
