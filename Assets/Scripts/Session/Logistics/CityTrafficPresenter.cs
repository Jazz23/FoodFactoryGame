// Presentation only (decision 0032, piece 4b): draws the city cars CityCars places near the camera in a generated world, with
// pooled models picked and tinted per car. CityCars says where the stream has each car; the drawn car drives toward that place
// with limited acceleration and braking instead of jumping or gliding to it, and never reverses. It brakes ahead of where the
// stream will hold it (a stop line before the crosswalk, the queue there), keeps its distance from the drawn car ahead in its
// lane and from trucks in the kerb lane, and slows for anyone on foot in its lane (Pedestrians), stopping short of them; it
// never drives through them, and pulls away smoothly once they have gone. A car left behind its place (held by a person, or the
// hour's flow changed) catches up at a capped speed; one the stream no longer lists drives on by itself, stopping at red lights
// and stop signs, until it reaches the end of its lane. Every car has a solid body (VehicleBody: the local player is blocked by
// it and bumped aside by VehicleBump) and an engine loop (VehicleEngineAudio). Cars never touch the simulation; with no map
// shown nothing is drawn.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
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
        // Further than this from its place in the stream (the camera jumped), a car is put there.
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
        // A car the stream no longer lists is dropped after this many seconds, or when this far beyond the draw radius.
        private const float UnlistedSeconds = 20f;
        private const float DropMargin = 50f;
        // Seconds a drawn car stands at a stop sign's line before going on.
        private const float SignHalt = 1f;
        // A car that drove off the end of its lane is not drawn again for this long, though the stream may still list it.
        private const float FinishedSeconds = 30f;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject[] carPrefabs = new GameObject[0];
        [SerializeField] private Color[] tints = new Color[0];

        // One drawn car and how it is driving: its lane, metres along the direction of travel, speed, and its place in the stream.
        private sealed class Driven
        {
            public uint Key;
            public GameObject Visual;
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
            // Driving by itself (not following the stream): unlisted, or dropped behind its place.
            public bool Free;
            // At the end of its lane: seconds stood at a stop sign's line, and whether it has gone on past the line.
            public float Waited;
            public bool Cleared;
        }

        private readonly Dictionary<uint, Driven> _driven = new();
        private readonly Dictionary<uint, GameObject> _cars = new();
        private readonly Dictionary<int, Stack<GameObject>> _pool = new();
        private readonly Dictionary<(int, bool, int), List<Driven>> _lanes = new();
        private readonly Dictionary<(int, bool), List<float>> _trucks = new();
        private readonly List<float> _peopleInLane = new();
        private readonly List<uint> _drop = new();
        private readonly Dictionary<uint, float> _finished = new();
        private MaterialPropertyBlock _block;
        private GoodsSnapshot _baseline;
        private float _receivedAt;
        private readonly List<Vector3> _people = new();
        private readonly List<Vector2> _peopleOnMap = new();

        public int Shown => _cars.Count;
        public IReadOnlyDictionary<uint, GameObject> Cars => _cars;

        private void Update()
        {
            var placement = SitePlacement.Active;
            var current = session.DrawnSites.Sites.FirstOrDefault(x => x.Current)?.Snapshot;
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
            Pedestrians.Positions(_people);
            _peopleOnMap.Clear();
            foreach (var person in _people) _peopleOnMap.Add(placement.ToMap(person));
            var trucks = current.Trucks.Concat(current.RoadTrucks).ToList();
            var cars = CityCars.Compute(network, seconds, view, Radius, trucks, since, MaxCars, _peopleOnMap);
            TruckObstacles(network, trucks, since);

            foreach (var driven in _driven.Values) driven.Listed = false;
            var spawn = new List<CityCar>();
            foreach (var car in cars)
            {
                if (!_driven.TryGetValue(car.Key, out var driven))
                {
                    if (!_finished.ContainsKey(car.Key)) spawn.Add(car);
                    continue;
                }
                if (driven.Listed) continue;
                driven.Listed = true;
                driven.Unlisted = 0f;
                var error = car.Along - driven.Along;
                if (Mathf.Abs(error) > SnapDistance)
                {
                    Place(driven, car);
                    continue;
                }
                if (error < -DropBehind) driven.Free = true;
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
            foreach (var car in spawn) Spawn(car);
            var dt = Time.deltaTime;
            if (dt > 0f)
                foreach (var ((segmentIndex, forward, lane), list) in _lanes)
                {
                    if (list.Count == 0) continue;
                    list.Sort((a, b) => b.Along.CompareTo(a.Along));
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
                var segment = network.Segments[driven.Segment];
                var (map, heading) = RoadPose.Along(network, driven.Segment, driven.Forward ? driven.Along : segment.Length - driven.Along,
                    driven.Forward, driven.Lane);
                var gone = driven.Along > segment.Length || driven.Along < 0f
                    || (!driven.Listed && (driven.Unlisted > UnlistedSeconds || (map - view).sqrMagnitude > (Radius + DropMargin) * (Radius + DropMargin)));
                if (gone)
                {
                    if (driven.Along > segment.Length) _finished[key] = Time.time;
                    _drop.Add(key);
                    continue;
                }
                driven.Visual.transform.SetPositionAndRotation(placement.OnGround(map.x, map.y),
                    Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)));
            }
            foreach (var key in _drop)
            {
                Release(key, _driven[key].Visual);
                _driven.Remove(key);
                _cars.Remove(key);
            }
            _drop.Clear();
            foreach (var (key, at) in _finished)
                if (Time.time - at > FinishedSeconds) _drop.Add(key);
            foreach (var key in _drop) _finished.Remove(key);
        }

        // Where trucks driving the network are drawn (TruckPresenter: waiting trucks nose to tail), by segment and direction, in
        // metres along the direction of travel.
        private void TruckObstacles(RoadNetwork network, List<GoodsTruck> trucks, float since)
        {
            foreach (var list in _trucks.Values) list.Clear();
            var waiting = new Dictionary<(int, bool, int), int>();
            foreach (var truck in trucks.Where(x => x.Driving && x.OnRoad).OrderBy(x => x.Id, System.StringComparer.Ordinal))
            {
                if (!network.TryGetSegment(truck.LegSegmentId, out var index)) continue;
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
                if (!_trucks.TryGetValue((index, forward), out var list)) _trucks[(index, forward)] = list = new List<float>();
                list.Add(along);
            }
        }

        // Groups the drawn cars by lane.
        private void Lanes()
        {
            foreach (var list in _lanes.Values) list.Clear();
            foreach (var driven in _driven.Values) LaneOf(driven).Add(driven);
        }

        private List<Driven> LaneOf(Driven driven)
        {
            var key = (driven.Segment, driven.Forward, driven.Lane);
            if (!_lanes.TryGetValue(key, out var list)) _lanes[key] = list = new List<Driven>();
            return list;
        }

        // Draws a car the stream lists for the first time at its place, unless a drawn car already stands there.
        private void Spawn(CityCar car)
        {
            var lane = LaneOf(new Driven { Segment = car.Segment, Forward = car.Forward, Lane = car.Lane });
            foreach (var other in lane)
                if (Mathf.Abs(other.Along - car.Along) < other.HalfLength + CityCars.CarHalfLength + BumperGap) return;
            var visual = Take(car.Key);
            var body = visual.GetComponent<VehicleBody>();
            var driven = new Driven
            {
                Key = car.Key, Visual = visual, Segment = car.Segment, Forward = car.Forward, Lane = car.Lane,
                HalfLength = body != null ? body.HalfLength : CityCars.CarHalfLength, Listed = true
            };
            Place(driven, car);
            _driven[car.Key] = driven;
            _cars[car.Key] = visual;
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
        private GameObject Take(uint key)
        {
            var variant = Variant(key);
            GameObject visual = null;
            if (_pool.TryGetValue(variant, out var stack))
                while (stack.Count > 0 && visual == null) visual = stack.Pop();
            if (visual == null)
            {
                visual = Instantiate(carPrefabs[variant], transform);
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) DestroyImmediate(collider);
                VehicleBody.AddTo(visual);
                VehicleEngineAudio.AddTo(visual, 0.18f, Random.Range(1.2f, 1.5f), 22f);
            }
            visual.name = $"Car {key:x8}";
            visual.SetActive(true);
            if (tints.Length > 0)
            {
                _block ??= new MaterialPropertyBlock();
                var tint = tints[(int)((key >> 8) % (uint)tints.Length)];
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    // Only the body material takes the tint (its name starts with the car body prefix).
                    for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var material = renderer.sharedMaterials[i];
                        if (material == null || !material.name.StartsWith(BodyMaterialPrefix)) continue;
                        renderer.GetPropertyBlock(_block, i);
                        _block.SetColor(BaseColor, tint);
                        renderer.SetPropertyBlock(_block, i);
                    }
                }
            }
            return visual;
        }

        public const string BodyMaterialPrefix = "VH_Body";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Release(uint key, GameObject visual)
        {
            if (visual == null) return;
            visual.SetActive(false);
            var variant = Variant(key);
            if (!_pool.TryGetValue(variant, out var stack)) _pool[variant] = stack = new Stack<GameObject>();
            stack.Push(visual);
        }

        private void Clear()
        {
            foreach (var (key, driven) in _driven) Release(key, driven.Visual);
            _driven.Clear();
            _cars.Clear();
            foreach (var list in _lanes.Values) list.Clear();
            _finished.Clear();
            _baseline = null;
        }
    }
}
