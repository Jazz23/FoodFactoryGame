// The city cars drawn on the generated roads (decision 0032, piece 4b): a deterministic function of the layout, the world clock
// and the trucks on the road, so every client shows the same cars in the same places without any car being replicated. Each
// lane of each direction of a segment is a stream of cars entering at its start node with the headway the server's background
// flow implies for the hour (RoadTraffic), at the speed that load allows. Where the segment ends at a stop sign each car halts
// at the painted stop line before the crosswalk (RoadPose.StopBack); at a traffic light cars arriving on red queue nose to tail
// and leave one by one on green. Cars also queue behind trucks in the kerb lane, and stop short of anyone on foot standing in
// their lane (players and drawn customers, given as map points). Each car carries the stream's speed for it and where the
// stream will next hold it, so the presenter can drive it there smoothly. Cars never affect the simulation; they only show
// the traffic it already counts.
using System;
using System.Collections.Generic;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    public readonly struct CityCar
    {
        public readonly int Segment;
        public readonly bool Forward;
        public readonly int Lane;
        // Metres along the direction of travel from the start node.
        public readonly float Along;
        // Stable per car: picks the model and colour.
        public readonly uint Key;
        // The stream's speed for this car now, in metres per second (0 while held).
        public readonly float Speed;
        // Metres along where the stream holds this car next (a stop line, the queue before it, a truck or a person);
        // positive infinity when it drives on to the end of the segment.
        public readonly float Stop;

        public CityCar(int segment, bool forward, int lane, float along, uint key, float speed = 0f, float stop = float.PositiveInfinity)
        {
            Segment = segment;
            Forward = forward;
            Lane = lane;
            Along = along;
            Key = key;
            Speed = speed;
            Stop = stop;
        }
    }

    public static class CityCars
    {
        // Bumper-to-bumper spacing in a queue, in metres.
        public const float Gap = 7f;
        // Half an ordinary car's length: a car's position is its centre, so it halts this far behind a stop line.
        public const float CarHalfLength = 2.3f;
        // A truck's half length where only the stream knows of it (the drawn model is measured by TruckPresenter).
        public const float TruckHalfLength = 3.5f;
        // Seconds between queued cars leaving a stop line, and the halt at a stop sign.
        public const float DischargeSeconds = 2f;
        public const float StopSeconds = 2f;
        // How far back in time cars are followed so the queue at a light has settled.
        private const float LookbackSeconds = 90f;
        // Segments shorter than this carry no drawn cars (junction to junction is nearly all crossing).
        private const int MinLength = 15;
        // Headways longer than this draw no cars at all on the lane.
        private const float MaxHeadwaySeconds = 600f;
        // A person on foot this close to a lane's centre (metres, sideways) is in that lane; cars stop this far short of them.
        public const float PedestrianReach = RoadNetwork.LaneWidth / 2f + 0.1f;
        public const float PedestrianStop = 4.5f;

        // Every car on segments within `radius` metres of `view` at clock time `seconds`, nearest first, at most `cap`.
        // `trucks` are the trucks on the road with the seconds since their baseline, so cars wait behind them.
        public static List<CityCar> Compute(RoadNetwork network, double seconds, Vector2 view, float radius, IEnumerable<GoodsTruck> trucks,
            float since, int cap, IReadOnlyList<Vector2> pedestrians = null)
        {
            var obstacles = new Dictionary<(int, bool), List<float>>();
            foreach (var truck in trucks)
            {
                if (!truck.Driving || !truck.OnRoad || !network.TryGetSegment(truck.LegSegmentId, out var index)) continue;
                var forward = truck.LegTo >= truck.LegFrom;
                var offset = RoadPose.TruckOffset(network, index, truck, since, TruckHalfLength);
                var along = forward ? offset : network.Segments[index].Length - offset;
                if (!obstacles.TryGetValue((index, forward), out var list)) obstacles[(index, forward)] = list = new List<float>();
                list.Add(along);
            }
            var found = new List<(float Distance, CityCar Car)>();
            var lane = new List<LaneCar>();
            foreach (var segment in network.Segments)
            {
                if (segment.Length < MinLength || Distance(network, segment, view) > radius) continue;
                var clock = (long)Math.Floor(seconds);
                var background = RoadTraffic.BackgroundPercent(segment, clock);
                var speed = LaneSpeed(segment, seconds);
                var flowPerLane = background / 100f * segment.CapacityPerHour / 3600f / segment.LanesPerDirection;
                if (flowPerLane <= 0f) continue;
                var headway = 1f / flowPerLane;
                if (headway > MaxHeadwaySeconds) continue;
                foreach (var forward in new[] { true, false })
                for (var l = 0; l < segment.LanesPerDirection; l++)
                {
                    Lane(network, segment, forward, l, seconds, speed, headway, lane);
                    if (l == 0 && obstacles.TryGetValue((segment.Index, forward), out var blocking)) BehindTrucks(lane, blocking);
                    if (pedestrians != null && pedestrians.Count > 0) BeforePedestrians(network, segment, forward, l, lane, pedestrians);
                    foreach (var car in lane)
                    {
                        var (position, _) = RoadPose.Along(network, segment.Index, forward ? car.Along : segment.Length - car.Along, forward, l);
                        found.Add(((position - view).magnitude, new CityCar(segment.Index, forward, l, car.Along, car.Key, car.Speed, car.Stop)));
                    }
                }
            }
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var result = new List<CityCar>(Math.Min(cap, found.Count));
            for (var i = 0; i < found.Count && i < cap; i++) result.Add(found[i].Car);
            return result;
        }

        // Free-flow speed of the city cars on a segment at a clock time, slowed by the background load, in metres per second.
        public static float LaneSpeed(RoadNetwork.Segment segment, double seconds) =>
            RoadTraffic.SpeedMetresPerSecond(segment.Kind) * 1000f
            / RoadTraffic.DelayPermille(RoadTraffic.BackgroundPercent(segment, (long)Math.Floor(seconds)));

        // Whether a map point stands in a lane (within PedestrianReach of its centre, sideways) and, if so, how many metres
        // along the direction of travel it is from the lane's start; points beyond either end of the segment are not in it.
        public static bool InLane(RoadNetwork network, RoadNetwork.Segment segment, bool forward, int laneIndex, Vector2 point, out float along)
        {
            var start = network.Nodes[segment.From];
            var direction = new Vector2(segment.Dx, segment.Dz);
            var right = new Vector2(segment.Dz, -segment.Dx);
            // The lane's centre, sideways from the centreline along `right` (driving back puts it on the other side).
            var centre = RoadPose.LaneOffset(segment, laneIndex) * (forward ? 1f : -1f);
            var relative = point - new Vector2(start.X, start.Z);
            var offset = Vector2.Dot(relative, direction);
            along = forward ? offset : segment.Length - offset;
            return offset >= 0f && offset <= segment.Length && Mathf.Abs(Vector2.Dot(relative, right) - centre) <= PedestrianReach;
        }

        private readonly struct LaneCar
        {
            public readonly float Along;
            public readonly uint Key;
            public readonly float Speed;
            public readonly float Stop;

            public LaneCar(float along, uint key, float speed, float stop)
            {
                Along = along;
                Key = key;
                Speed = speed;
                Stop = stop;
            }

            // Held at `at`: standing there, and the stream stops it there at the latest.
            public LaneCar HeldAt(float at) => new(at, Key, 0f, Mathf.Min(Stop, at));
        }

        // The cars of one lane at a time, front first: positions along the direction of travel in metres.
        private static void Lane(RoadNetwork network, RoadNetwork.Segment segment, bool forward, int laneIndex, double seconds, float speed,
            float headway, List<LaneCar> result)
        {
            result.Clear();
            var seed = Hash(segment.Id, forward ? 1 : 2, laneIndex);
            var phase = seed % 10_000 / 10_000f * headway;
            var length = segment.Length;
            var node = network.Nodes[network.EndNode(segment.Index, forward)];
            var stops = network.Stops(segment.Index, forward);
            var light = stops && node.Control == JunctionControl.TrafficLight;
            var alongX = segment.Dx != 0;
            // A car's position is its centre: it halts with its front bumper at the stop line, before the crosswalk.
            var stopLine = stops ? Mathf.Max(length * 0.5f, length - RoadPose.StopBack(network, segment.Index, forward) - CarHalfLength) : length;
            // Car k enters at t0 = k * headway + phase. Follow cars from the lookback on, so their departures are settled.
            var first = (long)Math.Floor((seconds - LookbackSeconds - length / speed - phase) / headway);
            var last = (long)Math.Floor((seconds - phase) / headway);
            var previousDeparture = double.NegativeInfinity;
            var cars = new List<(long K, double Arrival, double Departure)>();
            for (var k = first; k <= last; k++)
            {
                var entered = k * (double)headway + phase;
                var arrival = entered + (double)stopLine / speed;
                var departure = arrival;
                if (stops)
                {
                    departure = Math.Max(arrival, previousDeparture + DischargeSeconds);
                    if (light) departure = NextGreen(node, alongX, departure);
                    else departure = Math.Max(departure, arrival + StopSeconds);
                }
                previousDeparture = departure;
                cars.Add((k, arrival, departure));
            }
            // Front first: the oldest car still on the lane.
            var waitingAhead = 0;
            for (var i = 0; i < cars.Count; i++)
            {
                var (k, arrival, departure) = cars[i];
                var entered = k * (double)headway + phase;
                double along;
                var moving = speed;
                var stop = float.PositiveInfinity;
                if (seconds >= departure) along = stopLine + speed * (seconds - departure);
                else
                {
                    var free = speed * (seconds - entered);
                    var hold = stopLine - waitingAhead * Gap;
                    along = Math.Min(free, hold);
                    if (free >= hold) moving = 0f;
                    // Only a car that will really stand (it reaches its place in the queue before it may go) is held there;
                    // one arriving on green with nobody ahead drives straight through.
                    if (stops && entered + hold / speed < departure - 0.05) stop = hold;
                    waitingAhead++;
                }
                if (along > length || along < 0) continue;
                result.Add(new LaneCar((float)along, seed ^ (uint)k * 2654435761u, moving, stop));
            }
            KeepGaps(result);
        }

        // Front first: no car closer than a gap to the one ahead; any pushed off the start of the lane is not drawn.
        private static void KeepGaps(List<LaneCar> lane)
        {
            for (var i = 1; i < lane.Count; i++)
            {
                var ahead = lane[i - 1];
                var car = lane[i];
                if (car.Along > ahead.Along - Gap)
                    lane[i] = new LaneCar(ahead.Along - Gap, car.Key, Mathf.Min(car.Speed, ahead.Speed), Mathf.Min(car.Stop, ahead.Stop - Gap));
                else if (ahead.Stop - Gap < car.Stop) lane[i] = new LaneCar(car.Along, car.Key, car.Speed, ahead.Stop - Gap);
            }
            lane.RemoveAll(x => x.Along < 0f);
        }

        // Kerb-lane cars that would overlap a truck (the truck drives slower than the stream, or waits) stop a truck's length
        // behind it; cars clearly ahead of it drive on.
        private static void BehindTrucks(List<LaneCar> lane, List<float> trucks)
        {
            for (var i = 0; i < lane.Count; i++)
                foreach (var truck in trucks)
                    if (lane[i].Along > truck - Gap * 1.5f && lane[i].Along <= truck + Gap)
                        lane[i] = lane[i].HeldAt(truck - Gap * 1.5f);
            lane.Sort((a, b) => b.Along.CompareTo(a.Along));
            KeepGaps(lane);
        }

        // Cars that would reach a person standing in their lane stop short of them; cars already past drive on.
        private static void BeforePedestrians(RoadNetwork network, RoadNetwork.Segment segment, bool forward, int laneIndex,
            List<LaneCar> lane, IReadOnlyList<Vector2> pedestrians)
        {
            if (lane.Count == 0) return;
            var moved = false;
            foreach (var person in pedestrians)
            {
                if (!InLane(network, segment, forward, laneIndex, person, out var at)) continue;
                for (var i = 0; i < lane.Count; i++)
                    if (lane[i].Along > at - PedestrianStop && lane[i].Along <= at + 1f)
                    {
                        lane[i] = lane[i].HeldAt(at - PedestrianStop);
                        moved = true;
                    }
            }
            if (!moved) return;
            lane.Sort((a, b) => b.Along.CompareTo(a.Along));
            KeepGaps(lane);
        }

        private static double NextGreen(RoadNetwork.Node node, bool alongX, double time)
        {
            var start = alongX ? 0 : RoadTraffic.LightCycleSeconds / 2;
            var phase = RoadTraffic.LightPhase(node, time);
            if (phase >= start && phase < start + RoadTraffic.GreenSeconds) return time;
            var wait = (start - phase) % RoadTraffic.LightCycleSeconds;
            if (wait < 0) wait += RoadTraffic.LightCycleSeconds;
            return time + wait;
        }

        private static float Distance(RoadNetwork network, RoadNetwork.Segment segment, Vector2 view)
        {
            var a = network.Nodes[segment.From];
            var b = network.Nodes[segment.To];
            var x = Mathf.Clamp(view.x, Math.Min(a.X, b.X), Math.Max(a.X, b.X));
            var z = Mathf.Clamp(view.y, Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z));
            return (new Vector2(x, z) - view).magnitude;
        }

        private static uint Hash(string id, int direction, int lane)
        {
            var hash = 2166136261u;
            foreach (var c in id)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            hash ^= (uint)direction * 31u + (uint)lane * 977u;
            hash *= 16777619u;
            return hash;
        }
    }
}
