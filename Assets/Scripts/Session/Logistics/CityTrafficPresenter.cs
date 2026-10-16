// Presentation only (decision 0032, piece 4b): draws the city cars CityCars places near the camera in a generated world, with
// pooled models picked and tinted per car. CityCars says where the stream has each car; the drawn car drives toward that place
// with limited acceleration and braking instead of jumping or gliding to it, and never reverses. It brakes ahead of where the
// stream will hold it (a stop line before the crosswalk, the queue there), keeps its distance from the drawn car ahead in its
// lane and from trucks in the kerb lane, and slows for anyone on foot in its lane (Pedestrians), stopping short of them; it
// never drives through them, and pulls away smoothly once they have gone. A car left behind its place (held by a person, or the
// hour's flow changed) catches up at a capped speed; one the stream no longer lists drives on by itself, stopping at red lights
// and stop signs. Every car has a solid body (VehicleBody: the local player is blocked by it and bumped aside by VehicleBump)
// and an engine loop (VehicleEngineAudio). Cars never touch the simulation; with no map shown nothing is drawn.
// Cars appear and vanish only out of sight (ViewCulling): a new car is drawn only where no camera sees it (or while a street
// is first shown), and a car that reaches the end of its lane in sight turns or drives straight on into the next segment as a
// car on its own instead of vanishing, blending into its new lane; it goes once out of sight. A car nobody can see (and that
// is not right by a camera) is not drawn and its engine is off, though it keeps driving.
using System.Collections.Generic;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Audio;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class CityTrafficPresenter : MonoBehaviour
    {
        // PROTOTYPE: at most this many cars are drawn, the nearest first, within the draw radius of the camera.
        public const int MaxCars = 150;
        private const float Radius = DrawnSites.DrawRadius;
        // Further than this from its place in the stream (the camera jumped), a car out of sight is put there.
        private const float SnapDistance = 200f;
        // Further than this behind its place, a car leaves the stream and drives on by itself.
        private const float DropBehind = 25f;
        // Driving (metres per second, and per second squared): pulling away, ordinary braking, and the hardest braking.
        private const float Acceleration = 2.5f;
        private const float ComfortBraking = 3.5f;
        private const float MaxBraking = 9f;
        // How strongly a car closes the distance to its place (per second), and how much faster than the lane's speed it may
        // drive to catch up (a factor and an extra in metres per second).
        private const float FollowGain = 0.8f;
        private const float CatchUpFactor = 1.3f;
        private const float CatchUpExtra = 3f;
        // Bumper to bumper, a car never comes closer than this to the car ahead or a truck, in metres.
        private const float BumperGap = 1f;
        // A person on foot is about this wide around their position, in metres.
        private const float PersonRadius = 0.5f;
        // A car the stream no longer lists is dropped once out of sight for this many seconds, or when this far beyond the
        // draw radius and out of sight.
        private const float UnlistedSeconds = 5f;
        private const float DropMargin = 50f;
        // Seconds a drawn car stands at a stop sign's line before going on.
        private const float SignHalt = 1f;
        // A car that drove off the end of its lane is not drawn again for this long, though the stream may still list it.
        private const float FinishedSeconds = 30f;
        // After the cars were cleared, or the camera jumped further than JumpDistance (map metres) in a frame, cars may appear
        // in sight for this long: the street is being shown for the first time.
        private const float FillSeconds = 0.5f;
        private const float JumpDistance = 40f;
        // Within this distance of a camera a car is always drawn and heard, and never removed (a car right behind you).
        private const float AlwaysDrawn = 35f;
        // Seconds a car turning into the next segment takes to blend from the old lane's pose into the new one's.
        private const float TurnSeconds = 0.8f;
        // Share of cars (percent) going straight on at a junction where they can; the rest turn.
        private const uint StraightPercent = 60;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject[] carPrefabs = new GameObject[0];
        [SerializeField] private Color[] tints = new Color[0];

        // A pooled car model: its renderers, the body material slots that take the tint, and its engine.
        private sealed class CarVisual
        {
            public GameObject Root;
            public Transform Transform;
            public Renderer[] Renderers;
            public readonly List<(Renderer Renderer, int Slot)> Body = new();
            public VehicleEngineAudio Engine;
            public float HalfLength;
            public bool Drawn = true;
        }

        // One drawn car and how it is driving: its lane, metres along the direction of travel, speed, and its place in the stream.
        private sealed class Driven
        {
            public uint Key;
            public CarVisual Visual;
            public int Segment;
            public bool Forward;
            public int Lane;
            public float Along;
            public float Speed;
            public float HalfLength;
            public float Target;
            public float TargetSpeed;
            public float Stop;
            public bool Listed;
            public float Unlisted;
            // Driving by itself (not following the stream): unlisted, dropped behind its place, or turned into another segment.
            public bool Free;
            // At the end of its lane: seconds stood at a stop sign's line, and whether it has gone on past the line.
            public float Waited;
            public bool Cleared;
            // Where it was drawn last (map metres), and whether any camera could see it.
            public Vector2 Map;
            public bool Seen;
            // Turning into a new segment: the drawn pose eases from the old one by this offset and turn over Blend seconds.
            public bool BlendPending;
            public float Blend;
            public Vector3 BlendOffset;
            public Quaternion BlendTurn = Quaternion.identity;
        }

        private readonly Dictionary<uint, Driven> _driven = new();
        private readonly Stack<Driven> _spareDriven = new();
        private readonly Dictionary<uint, GameObject> _cars = new();
        private readonly Dictionary<int, Stack<CarVisual>> _pool = new();
        // Only lanes holding drawn cars are kept (emptied lists go back to a pool), so the per-frame work does not grow with
        // every lane the camera has ever passed.
        private readonly Dictionary<(int, bool, int), List<Driven>> _lanes = new();
        private readonly Stack<List<Driven>> _laneLists = new();
        private readonly Dictionary<(int, bool), List<float>> _trucks = new();
        private readonly Stack<List<float>> _truckLists = new();
        private readonly List<float> _peopleInLane = new();
        private readonly List<uint> _drop = new();
        private readonly Dictionary<uint, float> _finished = new();
        private readonly List<(int Segment, bool Forward)> _choices = new();
        private MaterialPropertyBlock _block;
        private GoodsSnapshot _baseline;
        private float _receivedAt;
        private bool _hasView;
        private Vector2 _lastView;
        private float _fillUntil;
        private readonly List<Vector3> _people = new();
        private readonly List<Vector2> _peopleOnMap = new();
        private readonly List<GoodsTruck> _allTrucks = new();
        private readonly List<GoodsTruck> _drivingTrucks = new();
        private readonly List<CityCar> _computed = new();
        private readonly List<CityCar> _spawn = new();
        private readonly Dictionary<(int, bool, int), int> _waiting = new();
        // Truck order is by ID (ordinal), so queues at a leg's end form the same way on every client.
        private static readonly System.Comparison<GoodsTruck> ById = (a, b) => string.CompareOrdinal(a.Id, b.Id);
        private static readonly System.Comparison<Driven> FrontFirst = (a, b) => b.Along.CompareTo(a.Along);

        public int Shown => _cars.Count;
        public IReadOnlyDictionary<uint, GameObject> Cars => _cars;

        // Whether a drawn car stands within `distance` map metres of a map point (where it was drawn this frame).
        public bool AnyCarWithin(Vector2 map, float distance)
        {
            foreach (var driven in _driven.Values)
                if ((driven.Map - map).sqrMagnitude < distance * distance) return true;
            return false;
        }

        private void Update()
        {
            var placement = SitePlacement.Active;
            GoodsSnapshot current = null;
            foreach (var site in session.DrawnSites.Sites)
                if (site.Current)
                {
                    current = site.Snapshot;
                    break;
                }
            var camera = Belts.BeltPresenter.ViewCamera();
            if (placement == null || current == null || camera == null || carPrefabs.Length == 0)
            {
                Clear();
                return;
            }
            if (!ReferenceEquals(current, _baseline))
            {
                _baseline = current;
                _receivedAt = Time.time;
            }
            var since = Mathf.Min(Time.time - _receivedAt, RoadPose.MaxExtrapolationSeconds);
            var seconds = current.ClockSeconds + since;
            var network = placement.Roads;
            var view = placement.ToMap(camera.transform.position);
            if (!_hasView || (view - _lastView).sqrMagnitude > JumpDistance * JumpDistance) _fillUntil = Time.time + FillSeconds;
            _hasView = true;
            _lastView = view;
            var filling = Time.time < _fillUntil;
            Pedestrians.Positions(_people);
            _peopleOnMap.Clear();
            foreach (var person in _people) _peopleOnMap.Add(placement.ToMap(person));
            // Everything below reuses its buffers: this runs every frame, and its garbage was setting off periodic GC stalls.
            var trucks = _allTrucks;
            trucks.Clear();
            trucks.AddRange(current.Trucks);
            trucks.AddRange(current.RoadTrucks);
            var cars = _computed;
            CityCars.Compute(network, seconds, view, Radius, trucks, since, MaxCars, _peopleOnMap, cars);
            TruckObstacles(network, trucks, since, view);

            foreach (var driven in _driven.Values) driven.Listed = false;
            var spawn = _spawn;
            spawn.Clear();
            foreach (var car in cars)
            {
                if (!_driven.TryGetValue(car.Key, out var driven))
                {
                    if (!_finished.ContainsKey(car.Key)) spawn.Add(car);
                    continue;
                }
                // A car that turned into another segment keeps its key; the stream's car of that key elsewhere is not it.
                if (driven.Listed || driven.Segment != car.Segment || driven.Forward != car.Forward || driven.Lane != car.Lane) continue;
                driven.Listed = true;
                driven.Unlisted = 0f;
                var error = car.Along - driven.Along;
                if (Mathf.Abs(error) > SnapDistance)
                {
                    // Out of sight it is put at its place; in sight it drives on by itself rather than jump.
                    if (!driven.Seen)
                    {
                        Place(driven, car);
                        continue;
                    }
                    driven.Free = true;
                }
                else if (error < -DropBehind) driven.Free = true;
                // A car driving by itself rejoins the stream once its place has come back to it.
                else if (driven.Free && error > -2f && error < 2f * CityCars.Gap)
                {
                    driven.Free = false;
                    driven.Cleared = false;
                    driven.Waited = 0f;
                }
                driven.Target = car.Along;
                driven.TargetSpeed = car.Speed;
                driven.Stop = car.Stop;
            }

            Lanes();
            foreach (var car in spawn)
            {
                if (_driven.Count >= MaxCars) break;
                Spawn(network, placement, car, filling);
            }
            var dt = Time.deltaTime;
            if (dt > 0f)
                foreach (var ((segmentIndex, forward, lane), list) in _lanes)
                {
                    if (list.Count == 0) continue;
                    list.Sort(FrontFirst);
                    var segment = network.Segments[segmentIndex];
                    _peopleInLane.Clear();
                    foreach (var person in _peopleOnMap)
                        if (CityCars.InLane(network, segment, forward, lane, person, out var at)) _peopleInLane.Add(at);
                    var trucksAhead = lane == 0 ? _trucks.GetValueOrDefault((segmentIndex, forward)) : null;
                    for (var i = 0; i < list.Count; i++)
                        Drive(network, segment, list[i], i > 0 ? list[i - 1] : null, trucksAhead, seconds, dt);
                }

            _drop.Clear();
            foreach (var (key, driven) in _driven)
            {
                var length = network.Segments[driven.Segment].Length;
                if (driven.Along > length)
                {
                    // At the end of its lane: out of sight it goes; in sight it drives on into the next segment, or waits there
                    // for room.
                    var (_, end, _) = PoseOf(network, placement, driven.Segment, driven.Forward, driven.Lane, length);
                    if (Hidden(end, driven.HalfLength))
                    {
                        _finished[key] = Time.time;
                        _drop.Add(key);
                        continue;
                    }
                    if (!Transfer(network, driven))
                    {
                        driven.Along = length;
                        driven.Speed = 0f;
                    }
                }
                var (map, position, rotation) = PoseOf(network, placement, driven.Segment, driven.Forward, driven.Lane, driven.Along);
                var shown = driven.Visual.Transform;
                if (driven.BlendPending)
                {
                    driven.BlendPending = false;
                    driven.BlendOffset = shown.position - position;
                    driven.BlendTurn = shown.rotation * Quaternion.Inverse(rotation);
                    driven.Blend = TurnSeconds;
                }
                if (driven.Blend > 0f)
                {
                    driven.Blend = Mathf.Max(0f, driven.Blend - dt);
                    var share = Mathf.SmoothStep(0f, 1f, driven.Blend / TurnSeconds);
                    position += driven.BlendOffset * share;
                    rotation = Quaternion.Slerp(Quaternion.identity, driven.BlendTurn, share) * rotation;
                }
                driven.Map = map;
                driven.Seen = Visible(position, driven.HalfLength);
                var near = Near(position);
                if (!driven.Listed && !driven.Seen && !near
                    && (driven.Unlisted > UnlistedSeconds || (map - view).sqrMagnitude > (Radius + DropMargin) * (Radius + DropMargin)))
                {
                    _drop.Add(key);
                    continue;
                }
                shown.SetPositionAndRotation(position, rotation);
                Draw(driven.Visual, driven.Seen || near);
            }
            foreach (var key in _drop)
            {
                var driven = _driven[key];
                Release(key, driven.Visual);
                _driven.Remove(key);
                _cars.Remove(key);
                driven.Visual = null;
                _spareDriven.Push(driven);
            }
            _drop.Clear();
            foreach (var (key, at) in _finished)
                if (Time.time - at > FinishedSeconds) _drop.Add(key);
            foreach (var key in _drop) _finished.Remove(key);
        }

        // Map point, scene position and rotation of a car `along` metres into a lane in its direction of travel.
        private static (Vector2 Map, Vector3 Position, Quaternion Rotation) PoseOf(RoadNetwork network, SitePlacement placement, int segment,
            bool forward, int lane, float along)
        {
            var length = network.Segments[segment].Length;
            var (map, heading) = RoadPose.Along(network, segment, forward ? along : length - along, forward, lane);
            return (map, placement.OnGround(map.x, map.y), Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)));
        }

        private static bool Visible(Vector3 position, float halfLength) =>
            ViewCulling.Seen(position + Vector3.up, halfLength + 1f, 2f * halfLength);

        // Right by a camera or the audio listener (seen or heard if it popped).
        private static bool Near(Vector3 position)
        {
            if (ViewCulling.NearestView(position) < AlwaysDrawn) return true;
            var listener = GameAudio.Listener;
            return listener != null && (listener.position - position).sqrMagnitude < AlwaysDrawn * AlwaysDrawn;
        }

        // Out of sight and not right by a camera: a car may appear or vanish here.
        private static bool Hidden(Vector3 position, float halfLength) => !Visible(position, halfLength) && !Near(position);

        // Where trucks driving the network near the view are drawn (TruckPresenter: waiting trucks nose to tail), by segment
        // and direction, in metres along the direction of travel.
        private void TruckObstacles(RoadNetwork network, List<GoodsTruck> trucks, float since, Vector2 view)
        {
            foreach (var list in _trucks.Values)
            {
                list.Clear();
                _truckLists.Push(list);
            }
            _trucks.Clear();
            var waiting = _waiting;
            waiting.Clear();
            var driving = _drivingTrucks;
            driving.Clear();
            foreach (var truck in trucks)
                if (truck.Driving && truck.OnRoad) driving.Add(truck);
            driving.Sort(ById);
            foreach (var truck in driving)
            {
                if (!network.TryGetSegment(truck.LegSegmentId, out var index)) continue;
                // A whole segment is in or out, so the queue order at its end is unchanged.
                if (CityCars.SegmentDistance(network, network.Segments[index], view) > Radius + DropMargin) continue;
                var forward = truck.LegTo >= truck.LegFrom;
                var along = RoadPose.TruckOffset(network, index, truck, since, CityCars.TruckHalfLength);
                if (!forward) along = network.Segments[index].Length - along;
                if (RoadPose.AtLegEnd(truck, since))
                {
                    var key = (index, forward, truck.LegTo);
                    var ahead = waiting.GetValueOrDefault(key);
                    waiting[key] = ahead + 1;
                    along -= ahead * TruckPresenter.QueueSpacing;
                }
                if (!_trucks.TryGetValue((index, forward), out var list))
                    _trucks[(index, forward)] = list = _truckLists.Count > 0 ? _truckLists.Pop() : new List<float>();
                list.Add(along);
            }
        }

        // Groups the drawn cars by lane.
        private void Lanes()
        {
            foreach (var list in _lanes.Values)
            {
                list.Clear();
                _laneLists.Push(list);
            }
            _lanes.Clear();
            foreach (var driven in _driven.Values) LaneOf(driven.Segment, driven.Forward, driven.Lane).Add(driven);
        }

        private List<Driven> LaneOf(int segment, bool forward, int lane)
        {
            var key = (segment, forward, lane);
            if (!_lanes.TryGetValue(key, out var list)) _lanes[key] = list = _laneLists.Count > 0 ? _laneLists.Pop() : new List<Driven>();
            return list;
        }

        // Draws a car the stream lists for the first time at its place, unless a drawn car already stands there or (outside a
        // fill) a camera could see it appear.
        private void Spawn(RoadNetwork network, SitePlacement placement, CityCar car, bool anywhere)
        {
            var lane = LaneOf(car.Segment, car.Forward, car.Lane);
            foreach (var other in lane)
                if (Mathf.Abs(other.Along - car.Along) < other.HalfLength + CityCars.CarHalfLength + BumperGap) return;
            var (map, position, rotation) = PoseOf(network, placement, car.Segment, car.Forward, car.Lane, car.Along);
            if (!anywhere && !Hidden(position, CityCars.CarHalfLength)) return;
            var visual = Take(car.Key);
            var driven = _spareDriven.Count > 0 ? _spareDriven.Pop() : new Driven();
            driven.Key = car.Key;
            driven.Visual = visual;
            driven.Segment = car.Segment;
            driven.Forward = car.Forward;
            driven.Lane = car.Lane;
            driven.HalfLength = visual.HalfLength;
            driven.Listed = true;
            driven.Unlisted = 0f;
            driven.Map = map;
            driven.Seen = anywhere && Visible(position, visual.HalfLength);
            driven.BlendPending = false;
            driven.Blend = 0f;
            Place(driven, car);
            visual.Transform.SetPositionAndRotation(position, rotation);
            _driven[car.Key] = driven;
            _cars[car.Key] = visual.Root;
            lane.Add(driven);
        }

        private static void Place(Driven driven, CityCar car)
        {
            driven.Along = car.Along;
            driven.Speed = car.Speed;
            driven.Target = car.Along;
            driven.TargetSpeed = car.Speed;
            driven.Stop = car.Stop;
            driven.Free = false;
            driven.Cleared = false;
            driven.Waited = 0f;
        }

        // A car at the end of its lane in sight drives on into a segment leaving the end node (straight on for most cars where
        // a road goes on, else a turn picked by its key; never back), as a car on its own, starting at that node. False when no
        // road leaves the node or the new lane has no room at its start.
        private bool Transfer(RoadNetwork network, Driven driven)
        {
            var segment = network.Segments[driven.Segment];
            var node = network.EndNode(driven.Segment, driven.Forward);
            var dx = driven.Forward ? segment.Dx : -segment.Dx;
            var dz = driven.Forward ? segment.Dz : -segment.Dz;
            var choices = _choices;
            choices.Clear();
            var straight = -1;
            foreach (var index in network.Nodes[node].Segments)
            {
                if (index == driven.Segment) continue;
                var next = network.Segments[index];
                var forward = next.From == node;
                var nextDx = forward ? next.Dx : -next.Dx;
                var nextDz = forward ? next.Dz : -next.Dz;
                if (nextDx == -dx && nextDz == -dz) continue;
                if (nextDx == dx && nextDz == dz) straight = choices.Count;
                choices.Add((index, forward));
            }
            if (choices.Count == 0) return false;
            var roll = (driven.Key ^ (uint)driven.Segment * 2654435761u) * 2246822519u;
            var pick = straight >= 0 && roll % 100u < StraightPercent ? straight : (int)((roll >> 8) % (uint)choices.Count);
            var (nextIndex, nextForward) = choices[pick];
            var nextSegment = network.Segments[nextIndex];
            var nextLane = Mathf.Min(driven.Lane, nextSegment.LanesPerDirection - 1);
            var along = Mathf.Clamp(driven.Along - segment.Length, 0f, nextSegment.Length);
            var lane = LaneOf(nextIndex, nextForward, nextLane);
            foreach (var other in lane)
                if (Mathf.Abs(other.Along - along) < other.HalfLength + driven.HalfLength + BumperGap) return false;
            driven.Segment = nextIndex;
            driven.Forward = nextForward;
            driven.Lane = nextLane;
            driven.Along = along;
            driven.Target = along;
            driven.Stop = float.PositiveInfinity;
            driven.Free = true;
            driven.Listed = false;
            driven.Unlisted = 0f;
            driven.Cleared = false;
            driven.Waited = 0f;
            driven.BlendPending = true;
            lane.Add(driven);
            return true;
        }

        // One frame of driving: the speed the car wants (toward its place, or the lane's speed when on its own), capped by what
        // lets it stop in comfort before everything ahead of it, then reached with limited acceleration and braking; it never
        // moves past the nearest thing it must not touch.
        private void Drive(RoadNetwork network, RoadNetwork.Segment segment, Driven car, Driven ahead, List<float> trucks, double seconds, float dt)
        {
            if (!car.Listed)
            {
                car.Unlisted += dt;
                car.Free = true;
            }
            var cruise = CityCars.LaneSpeed(segment, seconds);
            float desired;
            var stop = float.PositiveInfinity;
            if (car.Free) desired = cruise;
            else
            {
                var error = car.Target - car.Along;
                desired = Mathf.Min(car.TargetSpeed + FollowGain * error,
                    Mathf.Sqrt(car.TargetSpeed * car.TargetSpeed + 2f * ComfortBraking * Mathf.Max(0f, error)));
                stop = car.Stop;
            }
            desired = Mathf.Clamp(desired, 0f, cruise * CatchUpFactor + CatchUpExtra);
            // The stream's positions are for an ordinary car; a longer model halts with its front at the same place.
            var longer = car.HalfLength - CityCars.CarHalfLength;
            // A hold the drawn car is already past (it ran a little ahead of its place) is not slammed on; what is physically
            // ahead of it still stops it.
            if (stop - longer < car.Along - 0.5f) stop = float.PositiveInfinity;
            // Whatever the stream says, a drawn car halts at a stop sign and does not run a red light it can still stop for.
            stop = Mathf.Min(stop, JunctionStop(network, segment, car, seconds, dt));
            var hard = float.PositiveInfinity;
            if (!float.IsPositiveInfinity(stop))
            {
                desired = Mathf.Min(desired, Braking(stop - longer - car.Along, 0f));
                hard = Mathf.Min(hard, stop - longer + 1f);
            }
            if (ahead != null)
            {
                var touching = ahead.HalfLength + car.HalfLength;
                desired = Mathf.Min(desired, Braking(ahead.Along - Mathf.Max(CityCars.Gap, touching + 2.5f) - car.Along, ahead.Speed));
                hard = Mathf.Min(hard, ahead.Along - touching - BumperGap);
            }
            foreach (var at in _peopleInLane)
            {
                // Someone at or ahead of the front bumper. Someone beside the body is bumped aside (VehicleBump) rather than
                // holding the car where it stands.
                if (at < car.Along + car.HalfLength - 0.5f) continue;
                desired = Mathf.Min(desired, Braking(at - car.HalfLength - (CityCars.PedestrianStop - CityCars.CarHalfLength) - car.Along, 0f));
                hard = Mathf.Min(hard, at - car.HalfLength - PersonRadius);
            }
            if (trucks != null)
                foreach (var at in trucks)
                {
                    if (at < car.Along) continue;
                    var touching = CityCars.TruckHalfLength + car.HalfLength;
                    desired = Mathf.Min(desired, Braking(at - touching - 3f - car.Along, 0f));
                    hard = Mathf.Min(hard, at - touching - BumperGap);
                }
            car.Speed = desired > car.Speed ? Mathf.Min(desired, car.Speed + Acceleration * dt) : Mathf.Max(desired, car.Speed - MaxBraking * dt);
            var next = car.Along + car.Speed * dt;
            if (next > hard)
            {
                next = Mathf.Max(car.Along, hard);
                car.Speed = 0f;
            }
            car.Along = next;
        }

        // The highest speed from which a car stops in comfort within `room` metres, arriving at `speedThere`.
        private static float Braking(float room, float speedThere) =>
            room <= 0f ? 0f : Mathf.Sqrt(speedThere * speedThere + 2f * ComfortBraking * room);

        // Where a drawn car must stop at the end of its lane: the stop line while the light is not green (unless it is too
        // close to stop in comfort), or at a stop sign until it has stood there a moment; nowhere once past the line.
        private static float JunctionStop(RoadNetwork network, RoadNetwork.Segment segment, Driven car, double seconds, float dt)
        {
            if (car.Cleared || !network.Stops(segment.Index, car.Forward)) return float.PositiveInfinity;
            var line = Mathf.Max(segment.Length * 0.5f, segment.Length - RoadPose.StopBack(network, segment.Index, car.Forward) - CityCars.CarHalfLength);
            var longer = car.HalfLength - CityCars.CarHalfLength;
            if (car.Along > line - longer + 0.5f)
            {
                car.Cleared = true;
                return float.PositiveInfinity;
            }
            var node = network.Nodes[network.EndNode(segment.Index, car.Forward)];
            if (node.Control == JunctionControl.TrafficLight)
            {
                if (RoadTraffic.IsGreen(node, segment.Dx != 0, seconds)) return float.PositiveInfinity;
                // Too close to stop in comfort as the light changes: it goes on through.
                if (car.Speed * car.Speed > 2f * ComfortBraking * 1.5f * Mathf.Max(0f, line - longer - car.Along))
                {
                    car.Cleared = true;
                    return float.PositiveInfinity;
                }
                return line;
            }
            if (car.Along >= line - longer - 0.5f && car.Speed < 0.1f)
            {
                car.Waited += dt;
                if (car.Waited >= SignHalt)
                {
                    car.Cleared = true;
                    return float.PositiveInfinity;
                }
            }
            return line;
        }

        private int Variant(uint key) => (int)(key % (uint)carPrefabs.Length);

        // A pooled car of the key's model, tinted with the key's colour.
        private CarVisual Take(uint key)
        {
            var variant = Variant(key);
            CarVisual visual = null;
            if (_pool.TryGetValue(variant, out var stack))
                while (stack.Count > 0 && (visual == null || visual.Root == null)) visual = stack.Pop();
            if (visual == null || visual.Root == null) visual = Create(variant);
            visual.Root.SetActive(true);
            if (tints.Length > 0 && visual.Body.Count > 0)
            {
                _block ??= new MaterialPropertyBlock();
                var tint = tints[(int)((key >> 8) % (uint)tints.Length)];
                foreach (var (renderer, slot) in visual.Body)
                {
                    renderer.GetPropertyBlock(_block, slot);
                    _block.SetColor(BaseColor, tint);
                    renderer.SetPropertyBlock(_block, slot);
                }
            }
            return visual;
        }

        // A new model of a variant: no prefab colliders (VehicleBody adds the solid box), an engine, distance culling, and the
        // body material slots found once (reading sharedMaterials allocates).
        private CarVisual Create(int variant)
        {
            var root = Instantiate(carPrefabs[variant], transform);
            root.name = $"Car {variant}";
            foreach (var collider in root.GetComponentsInChildren<Collider>()) DestroyImmediate(collider);
            var body = VehicleBody.AddTo(root);
            var engine = VehicleEngineAudio.AddTo(root, 0.18f, Random.Range(1.2f, 1.5f), 22f);
            DistanceCulling.Apply(root);
            var visual = new CarVisual
            {
                Root = root, Transform = root.transform, Renderers = root.GetComponentsInChildren<Renderer>(true), Engine = engine,
                HalfLength = body != null ? body.HalfLength : CityCars.CarHalfLength
            };
            foreach (var renderer in visual.Renderers)
            {
                // Only the body material takes the tint (its name starts with the car body prefix).
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].name.StartsWith(BodyMaterialPrefix)) visual.Body.Add((renderer, i));
            }
            return visual;
        }

        // Draws or hides a car; a hidden one's engine is off too (it is out of earshot: AlwaysDrawn covers hearing distance).
        private static void Draw(CarVisual visual, bool drawn)
        {
            if (visual.Drawn == drawn) return;
            visual.Drawn = drawn;
            foreach (var renderer in visual.Renderers)
                if (renderer != null) renderer.forceRenderingOff = !drawn;
            if (visual.Engine != null) visual.Engine.enabled = drawn;
        }

        public const string BodyMaterialPrefix = "VH_Body";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Release(uint key, CarVisual visual)
        {
            if (visual == null || visual.Root == null) return;
            visual.Root.SetActive(false);
            var variant = Variant(key);
            if (!_pool.TryGetValue(variant, out var stack)) _pool[variant] = stack = new Stack<CarVisual>();
            stack.Push(visual);
        }

        private void Clear()
        {
            foreach (var (key, driven) in _driven)
            {
                Release(key, driven.Visual);
                driven.Visual = null;
                _spareDriven.Push(driven);
            }
            _driven.Clear();
            _cars.Clear();
            foreach (var list in _lanes.Values)
            {
                list.Clear();
                _laneLists.Push(list);
            }
            _lanes.Clear();
            _finished.Clear();
            _baseline = null;
            _hasView = false;
        }
    }
}
