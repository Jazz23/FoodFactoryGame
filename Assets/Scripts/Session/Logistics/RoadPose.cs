// Where a vehicle on the generated roads is drawn (decision 0032), from simulation data only: a point along a segment in a
// right-hand lane, and how far along its leg a truck has driven since its last baseline. Presentation only; the truck
// simulation never reads it.
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
    }
}
