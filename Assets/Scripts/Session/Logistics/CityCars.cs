// The city cars drawn on the generated roads (decision 0032, piece 4b): a deterministic function of the layout, the world clock
// and the trucks on the road, so every client shows the same cars in the same places without any car being replicated. Each
// lane of each direction of a segment is a stream of cars entering at its start node with the headway the server's background
// flow implies for the hour (RoadTraffic), at the speed that load allows. Where the segment ends at a stop sign each car halts
// at the stop line; at a traffic light cars arriving on red queue nose to tail and leave one by one on green. Cars also queue
// behind trucks in the kerb lane. Cars never affect the simulation; they only show the traffic it already counts.
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

        public CityCar(int segment, bool forward, int lane, float along, uint key)
        {
            Segment = segment;
            Forward = forward;
            Lane = lane;
            Along = along;
            Key = key;
        }
    }

    public static class CityCars
    {
        // Bumper-to-bumper spacing in a queue, in metres.
        public const float Gap = 7f;
        // Seconds between queued cars leaving a stop line, and the halt at a stop sign.
        public const float DischargeSeconds = 2f;
        public const float StopSeconds = 2f;
        // How far back in time cars are followed so the queue at a light has settled.
        private const float LookbackSeconds = 90f;
        // Segments shorter than this carry no drawn cars (junction to junction is nearly all crossing).
        private const int MinLength = 15;
        // Headways longer than this draw no cars at all on the lane.
        private const float MaxHeadwaySeconds = 600f;

        // Every car on segments within `radius` metres of `view` at clock time `seconds`, nearest first, at most `cap`.
        // `trucks` are the trucks on the road with the seconds since their baseline, so cars wait behind them.
        public static List<CityCar> Compute(RoadNetwork network, double seconds, Vector2 view, float radius, IEnumerable<GoodsTruck> trucks,
            float since, int cap)
        {
            var obstacles = new Dictionary<(int, bool), List<float>>();
            foreach (var truck in trucks)
            {
                if (!truck.Driving || !truck.OnRoad || !network.TryGetSegment(truck.LegSegmentId, out var index)) continue;
                var forward = truck.LegTo >= truck.LegFrom;
                var offset = RoadPose.LegOffset(truck, since);
                var along = forward ? offset : network.Segments[index].Length - offset;
                if (!obstacles.TryGetValue((index, forward), out var list)) obstacles[(index, forward)] = list = new List<float>();
                list.Add(along);
            }
            var found = new List<(float Distance, CityCar Car)>();
            var lane = new List<(float Along, uint Key)>();
            foreach (var segment in network.Segments)
            {
                if (segment.Length < MinLength || Distance(network, segment, view) > radius) continue;
                var clock = (long)Math.Floor(seconds);
                var background = RoadTraffic.BackgroundPercent(segment, clock);
                var speed = RoadTraffic.SpeedMetresPerSecond(segment.Kind) * 1000f / RoadTraffic.DelayPermille(background);
                var flowPerLane = background / 100f * segment.CapacityPerHour / 3600f / segment.LanesPerDirection;
                if (flowPerLane <= 0f) continue;
                var headway = 1f / flowPerLane;
                if (headway > MaxHeadwaySeconds) continue;
                foreach (var forward in new[] { true, false })
                for (var l = 0; l < segment.LanesPerDirection; l++)
                {
                    Lane(network, segment, forward, l, seconds, speed, headway, lane);
                    if (l == 0 && obstacles.TryGetValue((segment.Index, forward), out var blocking)) BehindTrucks(lane, blocking);
                    foreach (var (along, key) in lane)
                    {
                        var (position, _) = RoadPose.Along(network, segment.Index, forward ? along : segment.Length - along, forward, l);
                        found.Add(((position - view).magnitude, new CityCar(segment.Index, forward, l, along, key)));
                    }
                }
            }
            found.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            var result = new List<CityCar>(Math.Min(cap, found.Count));
            for (var i = 0; i < found.Count && i < cap; i++) result.Add(found[i].Car);
            return result;
        }

        // The cars of one lane at a time, front first: positions along the direction of travel in metres.
        private static void Lane(RoadNetwork network, RoadNetwork.Segment segment, bool forward, int laneIndex, double seconds, float speed,
            float headway, List<(float Along, uint Key)> result)
        {
            result.Clear();
            var seed = Hash(segment.Id, forward ? 1 : 2, laneIndex);
            var phase = seed % 10_000 / 10_000f * headway;
            var length = segment.Length;
            var node = network.Nodes[network.EndNode(segment.Index, forward)];
            var stops = network.Stops(segment.Index, forward);
            var light = stops && node.Control == JunctionControl.TrafficLight;
            var alongX = segment.Dx != 0;
            var stopLine = stops ? Mathf.Max(length * 0.5f, length - StopBack(network, segment, node)) : length;
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
                if (seconds >= departure) along = stopLine + speed * (seconds - departure);
                else
                {
                    var free = speed * (seconds - entered);
                    along = Math.Min(free, stopLine - waitingAhead * Gap);
                    waitingAhead++;
                }
                if (along > length || along < 0) continue;
                result.Add(((float)along, seed ^ (uint)k * 2654435761u));
            }
            KeepGaps(result);
        }

        // Front first: no car closer than a gap to the one ahead; any pushed off the start of the lane is not drawn.
        private static void KeepGaps(List<(float Along, uint Key)> lane)
        {
            for (var i = 1; i < lane.Count; i++)
                if (lane[i].Along > lane[i - 1].Along - Gap) lane[i] = (lane[i - 1].Along - Gap, lane[i].Key);
            lane.RemoveAll(x => x.Along < 0f);
        }

        // Kerb-lane cars that would overlap a truck (the truck drives slower than the stream, or waits) stop a truck's length
        // behind it; cars clearly ahead of it drive on.
        private static void BehindTrucks(List<(float Along, uint Key)> lane, List<float> trucks)
        {
            for (var i = 0; i < lane.Count; i++)
                foreach (var truck in trucks)
                    if (lane[i].Along > truck - Gap * 1.5f && lane[i].Along <= truck + Gap)
                        lane[i] = (truck - Gap * 1.5f, lane[i].Key);
            lane.Sort((a, b) => b.Along.CompareTo(a.Along));
            KeepGaps(lane);
        }

        // Where traffic stops before a node: half the widest crossing road plus a crosswalk.
        private static float StopBack(RoadNetwork network, RoadNetwork.Segment segment, RoadNetwork.Node node)
        {
            var widest = 0;
            foreach (var index in node.Segments)
                if (index != segment.Index) widest = Math.Max(widest, network.Segments[index].Width);
            return widest / 2f + 2f;
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
