// Where a vehicle on the generated roads is drawn (decision 0032), from simulation data only: a point along a segment in a
// right-hand lane, how far along its leg a truck has driven since its last baseline, and where vehicles halt before a
// junction: at the painted stop line ahead of the crosswalk, not in it. Presentation only; the truck simulation never reads it.
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    public static class RoadPose
    {
        // Kerb and sidewalk margin inside a road's width before the first lane, in metres.
        public const float KerbMetres = 1f;
        // A baseline arrives every clock second; positions run on at most this far past one before waiting for the next.
        public const float MaxExtrapolationSeconds = 1.25f;

        // Distance of a lane's centre to the right of the centreline (right-hand traffic); lane 0 is the kerb lane.
        public static float LaneOffset(RoadNetwork.Segment segment, int lane) =>
            Mathf.Max(0.75f, segment.Width / 2f - KerbMetres - RoadNetwork.LaneWidth * (lane + 0.5f));

        public static int Lanes(RoadNetwork.Segment segment) => segment.LanesPerDirection;

        // Map position (metres) and unit heading of a point `offset` metres along a segment, driven forward (toward its To node)
        // or back, in a lane, moved `back` metres against the direction of travel.
        public static (Vector2 Position, Vector2 Heading) Along(RoadNetwork network, int segment, float offset, bool forward, int lane,
            float back = 0f)
        {
            var s = network.Segments[segment];
            var heading = new Vector2(s.Dx, s.Dz) * (forward ? 1f : -1f);
            var (x, z) = network.Position(segment, offset);
            var right = new Vector2(heading.y, -heading.x);
            return (new Vector2((float)x, (float)z) + right * LaneOffset(s, lane) - heading * back, heading);
        }

        // Metres from its segment's From node a truck on a road leg has reached, `since` seconds after the baseline that
        // reported it: it drives the leg's length in LegDriveSeconds, then waits at the leg's end (a junction or a queue).
        public static float LegOffset(GoodsTruck truck, float since)
        {
            if (truck.LegDriveSeconds <= 0 || truck.RemainingSeconds <= 0) return truck.LegTo;
            var elapsed = truck.LegSeconds - truck.RemainingSeconds + Mathf.Clamp(since, 0f, MaxExtrapolationSeconds);
            return Mathf.Lerp(truck.LegFrom, truck.LegTo, Mathf.Clamp01(elapsed / truck.LegDriveSeconds));
        }

        // True when a truck stands at the end of its leg (waiting at a junction or to enter the next segment).
        public static bool AtLegEnd(GoodsTruck truck, float since) => Mathf.Approximately(LegOffset(truck, since), truck.LegTo);

        // Road tile paint (WorldLayoutPresenter, ArtSource/World/build_world_textures.py): city road tiles are 10 m long and the
        // crosswalk tile at a junction carries the zebra 0.8-3.8 m and the stop line 4.2-4.5 m in from the junction's edge,
        // scaled with the tile. Rural roads have no crosswalk; their stop sign stands 2 m back from the crossing road.
        private const float RoadTileLength = 10f;
        private const float CrosswalkStopLine = 4.5f;
        // How far behind the line (or the rural junction edge) a vehicle's front bumper halts, in metres.
        private const float StopLineMargin = 0.3f;
        private const float RuralStopBack = 1.5f;

        // Half the width of the widest road crossing (perpendicular to) a segment at one of its nodes; 0 when none crosses.
        // The drawn road is trimmed by exactly this much at that node, so it is where the junction patch begins.
        public static float CrossHalfWidth(RoadNetwork network, int segment, int node)
        {
            var alongX = network.Segments[segment].Dx != 0;
            var half = 0f;
            foreach (var index in network.Nodes[node].Segments)
                if ((network.Segments[index].Dx != 0) != alongX) half = Mathf.Max(half, network.Segments[index].Width / 2f);
            return half;
        }

        // Metres back from the end node of a segment driven in this direction to where a stopping vehicle's front bumper halts:
        // just behind the painted stop line, which lies before the crosswalk.
        public static float StopBack(RoadNetwork network, int segment, bool forward)
        {
            var s = network.Segments[segment];
            var near = CrossHalfWidth(network, segment, network.EndNode(segment, forward));
            if (s.Kind == RoadKind.Rural || near <= 0f) return near + RuralStopBack;
            var far = CrossHalfWidth(network, segment, network.EndNode(segment, !forward));
            var paved = s.Length - near - far;
            if (paved < 0.5f) return near + StopLineMargin;
            // The presenter fits a whole number of tiles between the junction patches, stretching each a little.
            var tile = paved / Mathf.Max(1, Mathf.RoundToInt(paved / RoadTileLength));
            return near + (CrosswalkStopLine + StopLineMargin) * tile / RoadTileLength;
        }

        // Where a truck on a road leg is drawn, in metres from its segment's From node. A truck that will wait at the leg's end
        // node (a junction wait the simulation added to the leg, or held there to enter the next segment) drives to the stop
        // line instead (or the edge of the junction where it does not stop), its front `halfLength` behind it, so it never
        // waits in the crosswalk or the middle of the junction. A truck passing straight through drives to the node.
        public static float TruckOffset(RoadNetwork network, int segment, GoodsTruck truck, float since, float halfLength)
        {
            var offset = LegOffset(truck, since);
            var s = network.Segments[segment];
            if (truck.LegFrom == truck.LegTo || (truck.LegTo != 0 && truck.LegTo != s.Length)) return offset;
            var waits = truck.LegSeconds > truck.LegDriveSeconds || truck.RemainingSeconds <= 0;
            if (!waits) return offset;
            var forward = truck.LegTo >= truck.LegFrom;
            var back = (network.Stops(segment, forward) ? StopBack(network, segment, forward)
                : CrossHalfWidth(network, segment, network.EndNode(segment, forward))) + halfLength;
            // Never behind where the leg began (a leg that starts within the stop distance stays put).
            var stop = forward ? Mathf.Max(truck.LegFrom, s.Length - back) : Mathf.Min(truck.LegFrom, back);
            if (truck.LegDriveSeconds <= 0 || truck.RemainingSeconds <= 0) return stop;
            var elapsed = truck.LegSeconds - truck.RemainingSeconds + Mathf.Clamp(since, 0f, MaxExtrapolationSeconds);
            return Mathf.Lerp(truck.LegFrom, stop, BrakeToStop(Mathf.Clamp01(elapsed / truck.LegDriveSeconds)));
        }

        // Share of the way covered at a share of the time when a vehicle drives at an even speed and then brakes evenly to a
        // stop over the last BrakingShare of the time (in the same total time, so a little faster before braking).
        private const float BrakingShare = 0.2f;

        private static float BrakeToStop(float time)
        {
            const float cruiseUntil = 1f - BrakingShare;
            var speed = 2f / (1f + cruiseUntil);
            if (time <= cruiseUntil) return speed * time;
            var braking = time - cruiseUntil;
            return speed * cruiseUntil + speed * braking - speed * braking * braking / (2f * BrakingShare);
        }
    }
}
