// City traffic as a flow model (decision 0032, piece 4, option C). Each segment and direction carries a background flow of
// city cars that follows its district's traffic level and the time of day; trucks on it add to the load. Load sets how long a
// segment takes (a volume-delay curve) and whether a truck may enter it. Traffic lights run a fixed cycle per node. Every
// value is a pure function of the layout, the world clock and the trucks on the road, so the server's truck simulation and
// every client's drawn cars agree without replicating any car. All numbers are PROTOTYPE.
using System;

namespace FoodFactoryGame.World
{
    public static class RoadTraffic
    {
        // PROTOTYPE: one game hour lasts a real minute, so a day of rush hours passes in 24 minutes.
        public const int HourSeconds = 60;
        public const int DaySeconds = 24 * HourSeconds;
        // Background load never passes this percent of capacity, so city cars alone never close a road.
        public const int MaxBackgroundPercent = 90;
        // Above this load a segment admits no more trucks, unless none is on it or a truck has waited PatienceSeconds.
        public const int QueuePercent = 120;
        public const int PatienceSeconds = 30;
        // A truck counts as this many cars (percent).
        public const int TruckPcePercent = 250;
        // Spacing of moving cars at capacity flow, in metres per lane.
        public const int CapacitySpacingMetres = 20;
        // Traffic lights: each axis is green for GreenSeconds, then amber for the rest of its half of the cycle.
        public const int LightCycleSeconds = 30;
        public const int GreenSeconds = 13;
        // A truck held at a red light needs a moment to pull away.
        public const int StartUpSeconds = 1;

        // Percent of the busiest hour, by hour of the day: a morning, lunch and evening rush and quiet nights.
        private static readonly int[] HourPercent =
        {
            8, 5, 5, 5, 8, 20, 45, 90, 100, 70, 55, 65, 85, 75, 55, 55, 65, 85, 100, 80, 55, 40, 25, 15
        };

        public static int Hour(long clockSeconds) => (int)(((clockSeconds % DaySeconds) + DaySeconds) % DaySeconds / HourSeconds);

        public static int HourLoadPercent(long clockSeconds) => HourPercent[Hour(clockSeconds)];

        // Free-flow speed by road kind, in metres per second (about 50, 36 and 72 km/h).
        public static int SpeedMetresPerSecond(RoadKind kind) => kind switch
        {
            RoadKind.Arterial => 14,
            RoadKind.Local => 10,
            _ => 20
        };

        // Share of capacity city cars use at the busiest hour, before the district's level.
        private static int BasePercent(RoadKind kind) => kind switch
        {
            RoadKind.Arterial => 55,
            RoadKind.Local => 35,
            _ => 15
        };

        // City cars on one direction of a segment, in percent of its capacity.
        public static int BackgroundPercent(RoadNetwork.Segment segment, long clockSeconds) => Math.Min(MaxBackgroundPercent,
            BasePercent(segment.Kind) * (50 + segment.TrafficPercent) / 100 * HourLoadPercent(clockSeconds) / 100);

        // Moving vehicles one direction of a segment holds at capacity flow.
        public static int VehiclesAtCapacity(RoadNetwork.Segment segment) =>
            Math.Max(1, segment.LanesPerDirection * segment.Length / CapacitySpacingMetres);

        // Load of one direction with this many trucks on it, in percent of capacity.
        public static int LoadPercent(RoadNetwork.Segment segment, long clockSeconds, int trucks) =>
            BackgroundPercent(segment, clockSeconds) + trucks * TruckPcePercent / VehiclesAtCapacity(segment);

        // The volume-delay curve (BPR: 1 + 0.15 (v/c)^4), in thousandths of the free-flow time.
        public static long DelayPermille(int loadPercent)
        {
            long x = Math.Clamp(loadPercent, 0, 1000);
            return 1000 + 150 * x * x * x * x / 100_000_000;
        }

        // Milliseconds to drive this many metres of a segment at a load, no faster than the vehicle's own top speed.
        public static long DriveMillis(RoadNetwork.Segment segment, int metres, int vehicleSpeed, int loadPercent)
        {
            var speed = Math.Max(1, Math.Min(SpeedMetresPerSecond(segment.Kind), vehicleSpeed));
            return metres * 1000L * DelayPermille(loadPercent) / 1000 / speed;
        }

        // Whether a truck may enter a direction whose load, counting it, would be loadWithTruck.
        public static bool Admits(int loadWithTruck, int trucksAlready, long queuedSeconds) =>
            trucksAlready == 0 || loadWithTruck <= QueuePercent || queuedSeconds >= PatienceSeconds;

        // The busiest cross street's background load at a segment's end node, in percent.
        public static int CrossPercent(RoadNetwork network, int segment, bool forward, long clockSeconds)
        {
            var node = network.Nodes[network.EndNode(segment, forward)];
            var cross = 0;
            foreach (var index in node.Segments)
                if (index != segment) cross = Math.Max(cross, BackgroundPercent(network.Segments[index], clockSeconds));
            return cross;
        }

        // Seconds a vehicle arriving at a segment's end node at this time waits there: until its light turns green, or a stop
        // that grows with the cross traffic. Nothing where it does not stop.
        public static long JunctionWaitSeconds(RoadNetwork network, int segment, bool forward, long arrivalSeconds)
        {
            if (!network.Stops(segment, forward)) return 0;
            var node = network.Nodes[network.EndNode(segment, forward)];
            if (node.Control == JunctionControl.TrafficLight)
            {
                var wait = SecondsUntilGreen(node, network.Segments[segment].Dx != 0, arrivalSeconds);
                return wait == 0 ? 0 : wait + StartUpSeconds;
            }
            return 2 + CrossPercent(network, segment, forward, arrivalSeconds) / 30;
        }

        // What route planning expects a junction to cost, in milliseconds: the average red wait at a light, or the stop.
        public static long JunctionEstimateMillis(RoadNetwork network, int segment, bool forward, long clockSeconds)
        {
            if (!network.Stops(segment, forward)) return 0;
            if (network.Nodes[network.EndNode(segment, forward)].Control == JunctionControl.TrafficLight) return 6000;
            return 2000 + CrossPercent(network, segment, forward, clockSeconds) * 1000 / 30;
        }

        // Light phase: a stable offset per node so neighbouring lights do not switch together.
        public static int LightOffset(string nodeId)
        {
            var hash = 2166136261u;
            foreach (var c in nodeId ?? "")
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)(hash % LightCycleSeconds);
        }

        // Seconds into the node's cycle at a time: traffic along X has green from 0, along Z from half the cycle.
        public static double LightPhase(RoadNetwork.Node node, double seconds)
        {
            var phase = (seconds + LightOffset(node.Id)) % LightCycleSeconds;
            return phase < 0 ? phase + LightCycleSeconds : phase;
        }

        public static bool IsGreen(RoadNetwork.Node node, bool alongX, double seconds)
        {
            var start = alongX ? 0 : LightCycleSeconds / 2;
            var phase = LightPhase(node, seconds);
            return phase >= start && phase < start + GreenSeconds;
        }

        // Whole seconds from an arrival until the approach's light is green (0 if it already is).
        public static long SecondsUntilGreen(RoadNetwork.Node node, bool alongX, long arrivalSeconds)
        {
            var start = alongX ? 0 : LightCycleSeconds / 2;
            var phase = (long)LightPhase(node, arrivalSeconds);
            if (phase >= start && phase < start + GreenSeconds) return 0;
            return ((start - phase) % LightCycleSeconds + LightCycleSeconds) % LightCycleSeconds;
        }
    }
}
