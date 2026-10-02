// The generated road graph as something vehicles can drive (decision 0032): nodes and axis-aligned two-way segments with
// lengths, the point on the network nearest a lot's access cell, and the quickest route between two points under a caller's
// costs. Everything is integer and ordered by layout index, so a route is the same on every run; the server's truck
// simulation and every client's presentation build the same network from the same stored layout.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FoodFactoryGame.World
{
    // A place on the network: a segment and whole metres along it from its From node (0..Length).
    public readonly struct RoadPoint : IEquatable<RoadPoint>
    {
        public readonly int Segment;
        public readonly int Offset;

        public RoadPoint(int segment, int offset)
        {
            Segment = segment;
            Offset = offset;
        }

        public bool Equals(RoadPoint other) => Segment == other.Segment && Offset == other.Offset;
        public override bool Equals(object obj) => obj is RoadPoint other && Equals(other);
        public override int GetHashCode() => Segment * 100_003 + Offset;
    }

    // One straight piece of a route: along a segment from one offset to another. Forward means toward the To node.
    public readonly struct RoadLeg
    {
        public readonly int Segment;
        public readonly int From;
        public readonly int To;

        public RoadLeg(int segment, int from, int to)
        {
            Segment = segment;
            From = from;
            To = to;
        }

        public bool Forward => To >= From;
        public int Length => Math.Abs(To - From);
    }

    public sealed class RoadNetwork
    {
        public sealed class Node
        {
            public string Id;
            public int X;
            public int Z;
            public JunctionControl Control;
            // Segment indices meeting here, in layout order.
            public readonly List<int> Segments = new();
        }

        public sealed class Segment
        {
            public string Id;
            public int Index;
            public int From;
            public int To;
            public RoadKind Kind;
            public int Width;
            public int CapacityPerHour;
            public int Length;
            // The district's traffic level at the segment's midpoint; RuralTrafficPercent outside every district.
            public int TrafficPercent;
            public int LanesPerDirection;
            // Unit direction from From to To (axis-aligned).
            public int Dx;
            public int Dz;
        }

        // PROTOTYPE: the traffic level of roads outside every district (farms, the ring road).
        public const int RuralTrafficPercent = 10;
        // PROTOTYPE: a lot's access cell further than this from every centreline is not on the network.
        public const int MaxAccessDistance = 20;
        // PROTOTYPE: metres of road width per lane; a metre of each half is kerb.
        public const int LaneWidth = 3;

        private static readonly ConditionalWeakTable<WorldLayout, RoadNetwork> Cache = new();

        private readonly List<Node> _nodes = new();
        private readonly List<Segment> _segments = new();
        private readonly Dictionary<string, int> _segmentIndex = new(StringComparer.Ordinal);

        public IReadOnlyList<Node> Nodes => _nodes;
        public IReadOnlyList<Segment> Segments => _segments;

        // The network of a stored layout, built once per layout object.
        public static RoadNetwork For(WorldLayout layout)
        {
            if (layout is null) throw new ArgumentNullException(nameof(layout));
            return Cache.GetValue(layout, x => new RoadNetwork(x));
        }

        public RoadNetwork(WorldLayout layout)
        {
            var nodeIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in layout.Nodes)
            {
                nodeIndex[node.Id] = _nodes.Count;
                _nodes.Add(new Node { Id = node.Id, X = node.X, Z = node.Z, Control = node.Control });
            }
            foreach (var road in layout.Roads)
            {
                if (!nodeIndex.TryGetValue(road.FromId, out var from) || !nodeIndex.TryGetValue(road.ToId, out var to) || from == to)
                    throw new ArgumentException($"Road {road.Id} does not join two nodes.");
                var a = _nodes[from];
                var b = _nodes[to];
                if (a.X != b.X && a.Z != b.Z) throw new ArgumentException($"Road {road.Id} is not axis-aligned.");
                var length = Math.Abs(b.X - a.X) + Math.Abs(b.Z - a.Z);
                var segment = new Segment
                {
                    Id = road.Id, Index = _segments.Count, From = from, To = to, Kind = road.Kind, Width = road.Width,
                    CapacityPerHour = Math.Max(1, road.CapacityPerHour), Length = length,
                    LanesPerDirection = Math.Max(1, (road.Width / 2 - 1) / LaneWidth),
                    Dx = Math.Sign(b.X - a.X), Dz = Math.Sign(b.Z - a.Z),
                    TrafficPercent = DistrictTraffic(layout, (a.X + b.X) / 2, (a.Z + b.Z) / 2)
                };
                _segmentIndex[road.Id] = segment.Index;
                _segments.Add(segment);
                a.Segments.Add(segment.Index);
                b.Segments.Add(segment.Index);
            }
        }

        private static int DistrictTraffic(WorldLayout layout, int x, int z)
        {
            foreach (var district in layout.Districts)
            foreach (var area in district.Areas)
                if (x >= area.X && x <= area.X + area.Width && z >= area.Z && z <= area.Z + area.Depth)
                    return district.TrafficPercent;
            return RuralTrafficPercent;
        }

        public bool TryGetSegment(string id, out int index) => _segmentIndex.TryGetValue(id ?? "", out index);

        public int EndNode(int segment, bool forward) => forward ? _segments[segment].To : _segments[segment].From;

        // The point on the network nearest a cell (a lot's access cell), or null when every centreline is further than
        // MaxAccessDistance. Ties go to the lower segment index.
        public RoadPoint? Locate(int x, int z)
        {
            RoadPoint? best = null;
            var bestDistance = int.MaxValue;
            foreach (var segment in _segments)
            {
                var a = _nodes[segment.From];
                var along = segment.Dx != 0 ? (x - a.X) * segment.Dx : (z - a.Z) * segment.Dz;
                var offset = Math.Clamp(along, 0, segment.Length);
                var px = a.X + segment.Dx * offset;
                var pz = a.Z + segment.Dz * offset;
                var distance = Math.Abs(px - x) + Math.Abs(pz - z);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = new RoadPoint(segment.Index, offset);
            }
            return bestDistance <= MaxAccessDistance ? best : null;
        }

        // Map position of a point along a segment (centreline).
        public (double X, double Z) Position(int segment, double offset)
        {
            var s = _segments[segment];
            var a = _nodes[s.From];
            return (a.X + s.Dx * offset, a.Z + s.Dz * offset);
        }

        // Whether traffic arriving at the end node of a segment driven in this direction must stop there.
        public bool Stops(int segment, bool forward)
        {
            var node = _nodes[EndNode(segment, forward)];
            if (node.Control == JunctionControl.None) return false;
            if (node.Control == JunctionControl.TrafficLight) return true;
            var kinds = new List<RoadKind>(node.Segments.Count);
            foreach (var index in node.Segments) kinds.Add(_segments[index].Kind);
            return WorldJunctions.Stops(node.Control, _segments[segment].Kind, kinds);
        }

        // The quickest route from start to end, as legs in driving order. driveMillis(segment, forward) is the time to drive
        // a whole segment (a partial leg costs its share); junctionMillis(segment, forward) is the wait at its end node, paid
        // only when a leg ends there and the route goes on. A start or end at a node (offset 0 or Length) begins or ends
        // there. Start equal to end gives one zero-length leg. Null if no route exists.
        public List<RoadLeg> Route(RoadPoint start, RoadPoint end, Func<int, bool, long> driveMillis, Func<int, bool, long> junctionMillis)
        {
            if (start.Equals(end) || SameNode(start, end)) return new List<RoadLeg> { new(start.Segment, start.Offset, start.Offset) };
            long Part(int segment, bool forward, int metres) =>
                metres <= 0 ? 0 : Math.Max(1, driveMillis(segment, forward) * metres / Math.Max(1, _segments[segment].Length));

            // A* toward the end point: no vehicle beats the fastest road, so straight-grid distance at that speed never
            // overestimates (and is consistent: every segment costs at least its length at that speed).
            var (targetX, targetZ) = Position(end.Segment, end.Offset);
            long Estimate(int node) => (long)((Math.Abs(_nodes[node].X - targetX) + Math.Abs(_nodes[node].Z - targetZ)) * 1000 / FastestMetresPerSecond);
            var distance = new long[_nodes.Count];
            var previousNode = new int[_nodes.Count];
            var previousSegment = new int[_nodes.Count];
            var settled = new bool[_nodes.Count];
            Array.Fill(distance, long.MaxValue);
            Array.Fill(previousNode, -1);
            Array.Fill(previousSegment, -1);
            var queue = new MinHeap();
            void Reach(int node, long cost, int fromNode, int viaSegment)
            {
                if (cost >= distance[node]) return;
                distance[node] = cost;
                previousNode[node] = fromNode;
                previousSegment[node] = viaSegment;
                queue.Push(cost + Estimate(node), node, cost);
            }

            // Leaving the start: along its segment to either end, or from the node it stands on.
            var startSegment = _segments[start.Segment];
            var startNode = AtNode(start);
            if (startNode >= 0) Reach(startNode, 0, -1, -1);
            else
            {
                Reach(startSegment.To, Part(start.Segment, true, startSegment.Length - start.Offset) + junctionMillis(start.Segment, true), -1, start.Segment);
                Reach(startSegment.From, Part(start.Segment, false, start.Offset) + junctionMillis(start.Segment, false), -1, start.Segment);
            }
            var endNode = AtNode(end);
            var endSegmentIndex = end.Segment;
            var reachedEnd = long.MaxValue;
            while (queue.Count > 0)
            {
                var (estimate, node, cost) = queue.Pop();
                if (settled[node] || cost != distance[node]) continue;
                if (estimate >= reachedEnd) break;
                settled[node] = true;
                if (node == endNode) break;
                if (endNode < 0 && (node == _segments[endSegmentIndex].From || node == _segments[endSegmentIndex].To))
                {
                    var fromStart = node == _segments[endSegmentIndex].From;
                    reachedEnd = Math.Min(reachedEnd, cost + Part(endSegmentIndex, fromStart, fromStart ? end.Offset : _segments[endSegmentIndex].Length - end.Offset));
                }
                foreach (var index in _nodes[node].Segments)
                {
                    var segment = _segments[index];
                    var forward = segment.From == node;
                    var next = forward ? segment.To : segment.From;
                    Reach(next, cost + driveMillis(index, forward) + junctionMillis(index, forward), node, index);
                }
            }

            // Arriving at the end: at its node, or along its segment from either end; on the start's own segment, directly.
            var bestCost = long.MaxValue;
            var bestNode = -1;
            var direct = false;
            if (endNode >= 0)
            {
                bestCost = distance[endNode];
                bestNode = endNode;
            }
            else
            {
                var endSegment = _segments[end.Segment];
                foreach (var (node, forward, metres) in new[]
                         {
                             (endSegment.From, true, end.Offset),
                             (endSegment.To, false, endSegment.Length - end.Offset)
                         })
                {
                    if (distance[node] == long.MaxValue) continue;
                    var cost = distance[node] + Part(end.Segment, forward, metres);
                    if (cost < bestCost || (cost == bestCost && node < bestNode))
                    {
                        bestCost = cost;
                        bestNode = node;
                    }
                }
                if (startNode < 0 && start.Segment == end.Segment)
                {
                    var forward = end.Offset > start.Offset;
                    var cost = Part(start.Segment, forward, Math.Abs(end.Offset - start.Offset));
                    if (cost <= bestCost)
                    {
                        bestCost = cost;
                        direct = true;
                    }
                }
            }
            if (direct) return new List<RoadLeg> { new(start.Segment, start.Offset, end.Offset) };
            if (bestNode < 0 || bestCost == long.MaxValue) return null;

            var legs = new List<RoadLeg>();
            if (endNode < 0)
            {
                var endSegment = _segments[end.Segment];
                legs.Add(new RoadLeg(end.Segment, bestNode == endSegment.From ? 0 : endSegment.Length, end.Offset));
            }
            var at = bestNode;
            while (previousNode[at] >= 0)
            {
                var index = previousSegment[at];
                var segment = _segments[index];
                legs.Add(segment.To == at ? new RoadLeg(index, 0, segment.Length) : new RoadLeg(index, segment.Length, 0));
                at = previousNode[at];
            }
            if (startNode < 0)
                legs.Add(new RoadLeg(start.Segment, start.Offset, _segments[start.Segment].To == at ? _segments[start.Segment].Length : 0));
            legs.Reverse();
            legs.RemoveAll(x => x.Length == 0);
            return legs;
        }

        // The fastest any vehicle drives (RoadTraffic's top road speed), for the route search's estimate.
        private const int FastestMetresPerSecond = 20;

        // A binary min-heap of (estimate, node, cost) ordered by estimate then node index, so equal estimates pop in the same
        // order on every run. Stale entries are skipped by the caller.
        private sealed class MinHeap
        {
            private readonly List<(long Estimate, int Node, long Cost)> _items = new();

            public int Count => _items.Count;

            private static bool Less((long Estimate, int Node, long Cost) a, (long Estimate, int Node, long Cost) b) =>
                a.Estimate < b.Estimate || (a.Estimate == b.Estimate && a.Node < b.Node);

            public void Push(long estimate, int node, long cost)
            {
                _items.Add((estimate, node, cost));
                var i = _items.Count - 1;
                while (i > 0)
                {
                    var parent = (i - 1) / 2;
                    if (!Less(_items[i], _items[parent])) break;
                    (_items[i], _items[parent]) = (_items[parent], _items[i]);
                    i = parent;
                }
            }

            public (long Estimate, int Node, long Cost) Pop()
            {
                var top = _items[0];
                var last = _items[^1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return top;
                _items[0] = last;
                var i = 0;
                while (true)
                {
                    var left = 2 * i + 1;
                    var right = left + 1;
                    var smallest = i;
                    if (left < _items.Count && Less(_items[left], _items[smallest])) smallest = left;
                    if (right < _items.Count && Less(_items[right], _items[smallest])) smallest = right;
                    if (smallest == i) break;
                    (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                    i = smallest;
                }
                return top;
            }
        }

        // The node a point stands on (offset 0 or Length), or -1.
        public int AtNode(RoadPoint point)
        {
            var segment = _segments[point.Segment];
            return point.Offset <= 0 ? segment.From : point.Offset >= segment.Length ? segment.To : -1;
        }

        private bool SameNode(RoadPoint a, RoadPoint b) => AtNode(a) >= 0 && AtNode(a) == AtNode(b);
    }
}
