// Presentation only: the figures on foot in this client's scene (player avatars, drawn customers and ambient pedestrians), so
// the drawn city cars can stop for anyone in their lane. Destroyed figures drop out on the next read; hidden ones are skipped.
// AmbientPedestrians fills the city sidewalks near the camera with pooled walkers that represent no record and never touch the
// simulation: they walk the sidewalk on either side of every non-rural road, turn at junction corners and use the crosswalks,
// crossing on the traffic light's walk phase (the cars' own green along the same axis) or, at other junctions, when no drawn car
// is close. Walkers are recycled once they are far from the camera.
using System.Collections.Generic;
using FoodFactoryGame.Session.Customers;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    public static class Pedestrians
    {
        private static readonly List<Transform> Figures = new();

        public static void Add(Transform figure)
        {
            if (figure != null && !Figures.Contains(figure)) Figures.Add(figure);
        }

        public static void Remove(Transform figure) => Figures.Remove(figure);

        // Scene positions of every live, shown figure.
        public static void Positions(List<Vector3> result)
        {
            result.Clear();
            Figures.RemoveAll(x => x == null);
            foreach (var figure in Figures)
                if (figure.gameObject.activeInHierarchy) result.Add(figure.position);
        }
    }

    // Ticked by its owner (CustomerPresenter, which supplies the figure prefab and the world clock); walkers live under a child
    // of the owner's transform. Without a generated world, a current site or a camera nothing is drawn.
    public sealed class AmbientPedestrians
    {
        // PROTOTYPE: at most this many walkers, about one per DensityMetres of sidewalk near the camera.
        public const int MaxWalkers = 36;
        private const float DensityMetres = 10f;
        private const float Radius = 90f;
        private const float DespawnRadius = 115f;
        // New walkers appear out of view, or anywhere this far away (too small to notice popping in).
        private const float HiddenDistance = 45f;
        private const int SpawnsPerFrame = 2;
        private const int FillSpawnsPerFrame = 8;
        // The sidewalk's centre line, in from the road's edge (the outer metre of a road's width is kerb and sidewalk).
        private const float Inset = 0.5f;
        // Distance past the junction box of the line walkers cross an arm on (the zebra at the start of the crosswalk tile).
        private const float CrosswalkOut = 1.5f;
        // Street trees stand on the sidewalk's centre line: walkers step this far toward the lots to pass one.
        private const float TreeStep = 0.55f;
        private const float TreeClear = 1.1f;
        private const float MaxWaitSeconds = 35f;
        // At a junction without lights a walker crosses only with no drawn car this close to the crossing's middle.
        private const float CarClearance = 16f;
        private const float WalkClipSpeed = 1.5f;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkRateParameter = Animator.StringToHash("WalkRate");

        private readonly Transform _owner;
        private readonly List<Walker> _walkers = new();
        private readonly List<int> _nearby = new();
        private readonly System.Random _random = new(20261014);
        private Transform _container;
        private Stack<Walker>[] _pools;
        private Streets _streets;
        private float _nearbyLength;
        private float _nextNearby;
        private uint _serial;
        private CityTrafficPresenter _traffic;
        private float _nextTrafficLookup;
        // Until this time (just after the street starts being drawn) walkers may appear in view, filling it at once.
        private float _fillUntil;
        private bool _fresh = true;

        public AmbientPedestrians(Transform owner) => _owner = owner;

        // Walkers currently shown.
        public int Count => _walkers.Count;

        private sealed class Walker
        {
            public GameObject Root;
            public Transform Transform;
            public Animator Animator;
            public Renderer[] Tinted;
            public int Variant;
            public float Speed;
            // Map position (metres) on the walked line, before stepping around trees.
            public Vector2 Position;
            public readonly Vector2[] Path = new Vector2[4];
            public int Count;
            public int Index;
            // The path ends at this corner of this node.
            public int Node;
            public int Sx;
            public int Sz;
            // The move that would undo the last one (not chosen again unless it is the only one).
            public int Back;
            // While heading for Path[LegPoint] the walker is on Segment's sidewalk, on side Side (+1/-1 along the other axis).
            public int LegPoint;
            public int Segment;
            public int Side;
            // A crossing waiting for its light or a gap in the traffic: the move, since when, next check.
            public int Pending = -1;
            public float WaitingSince;
            public float NextCheck;
        }

        // Per-layout sidewalk geometry.
        private sealed class Streets
        {
            public SitePlacement Placement;
            public RoadNetwork Roads;
            // Per node: half the junction box across X (the widest road running along Z) and across Z.
            public float[] SpanX;
            public float[] SpanZ;
            // Per node and direction (0 +X, 1 -X, 2 +Z, 3 -Z): the city segment leaving that way, or -1.
            public int[] Arms;
            public bool[] City;
            // Per segment * 2 + (side > 0 ? 1 : 0): street-tree coordinates along the segment's axis, sorted.
            public float[][] Trees;
        }

        public void Tick(GameObject prefab, double? clock, Camera camera)
        {
            var placement = SitePlacement.Active;
            if (prefab == null || clock == null || camera == null || placement == null)
            {
                Clear();
                return;
            }
            if (_streets == null || !ReferenceEquals(_streets.Placement, placement))
            {
                Clear();
                _streets = Build(placement);
                _nextNearby = 0f;
            }
            if (_container == null)
            {
                _container = new GameObject("Ambient Pedestrians").transform;
                _container.SetParent(_owner, false);
            }
            _pools ??= new Stack<Walker>[CharacterFigures.Count];
            if (_fresh)
            {
                _fresh = false;
                _fillUntil = Time.time + 1f;
            }
            var view = placement.ToMap(camera.transform.position);
            if (Time.time >= _nextNearby)
            {
                _nextNearby = Time.time + 1f;
                Nearby(view);
            }
            for (var i = _walkers.Count - 1; i >= 0; i--)
            {
                var walker = _walkers[i];
                var distance = (walker.Position - view).magnitude;
                if (distance > DespawnRadius || distance > Radius && !OnScreen(camera, walker.Transform.position))
                {
                    Release(walker);
                    _walkers.RemoveAt(i);
                    continue;
                }
                Step(walker, clock.Value, placement);
            }
            var target = Mathf.Min(MaxWalkers, (int)(_nearbyLength / DensityMetres));
            // The first fill may appear in view (the street is just being shown); later walkers come in out of sight.
            var fill = Time.time < _fillUntil;
            var budget = fill ? FillSpawnsPerFrame : SpawnsPerFrame;
            for (var attempt = 0; attempt < budget * 3 && budget > 0 && _walkers.Count < target && _nearby.Count > 0; attempt++)
                if (TrySpawn(prefab, placement, camera, view, fill))
                    budget--;
        }

        // Hides every walker (they stay pooled for reuse).
        public void Clear()
        {
            foreach (var walker in _walkers) Release(walker);
            _walkers.Clear();
            _fresh = true;
        }

        private static Streets Build(SitePlacement placement)
        {
            var roads = placement.Roads;
            var nodes = roads.Nodes.Count;
            var segments = roads.Segments.Count;
            var streets = new Streets
            {
                Placement = placement, Roads = roads, SpanX = new float[nodes], SpanZ = new float[nodes], Arms = new int[nodes * 4],
                City = new bool[segments], Trees = new float[segments * 2][]
            };
            for (var i = 0; i < streets.Arms.Length; i++) streets.Arms[i] = -1;
            foreach (var segment in roads.Segments)
            {
                if (segment.Kind == RoadKind.Rural) continue;
                streets.City[segment.Index] = true;
                var half = segment.Width / 2f;
                foreach (var node in new[] { segment.From, segment.To })
                    if (segment.Dx != 0) streets.SpanZ[node] = Mathf.Max(streets.SpanZ[node], half);
                    else streets.SpanX[node] = Mathf.Max(streets.SpanX[node], half);
                streets.Arms[segment.From * 4 + Direction(segment.Dx, segment.Dz)] = segment.Index;
                streets.Arms[segment.To * 4 + Direction(-segment.Dx, -segment.Dz)] = segment.Index;
            }
            // Only street trees matter; the countryside's woodland lies outside the city streets' bounds and is skipped cheaply.
            var bounds = new Rect(float.MaxValue, float.MaxValue, 0f, 0f);
            var minX = float.MaxValue;
            var minZ = float.MaxValue;
            var maxX = float.MinValue;
            var maxZ = float.MinValue;
            foreach (var segment in roads.Segments)
            {
                if (!streets.City[segment.Index]) continue;
                foreach (var node in new[] { roads.Nodes[segment.From], roads.Nodes[segment.To] })
                {
                    minX = Mathf.Min(minX, node.X - segment.Width);
                    minZ = Mathf.Min(minZ, node.Z - segment.Width);
                    maxX = Mathf.Max(maxX, node.X + segment.Width);
                    maxZ = Mathf.Max(maxZ, node.Z + segment.Width);
                }
            }
            if (minX <= maxX) bounds = Rect.MinMaxRect(minX, minZ, maxX, maxZ);
            var trees = new List<float>[segments * 2];
            foreach (var tree in placement.Layout.Trees)
            {
                var tx = tree.X + 0.5f;
                var tz = tree.Z + 0.5f;
                if (!bounds.Contains(new Vector2(tx, tz))) continue;
                foreach (var segment in roads.Segments)
                {
                    if (!streets.City[segment.Index]) continue;
                    var a = roads.Nodes[segment.From];
                    var b = roads.Nodes[segment.To];
                    var alongX = segment.Dx != 0;
                    var across = alongX ? tz - a.Z : tx - a.X;
                    var along = alongX ? tx : tz;
                    var min = alongX ? Mathf.Min(a.X, b.X) : Mathf.Min(a.Z, b.Z);
                    var max = alongX ? Mathf.Max(a.X, b.X) : Mathf.Max(a.Z, b.Z);
                    if (along < min || along > max || Mathf.Abs(Mathf.Abs(across) - (segment.Width / 2f - Inset)) > 0.6f) continue;
                    var key = segment.Index * 2 + (across > 0 ? 1 : 0);
                    (trees[key] ??= new List<float>()).Add(along);
                }
            }
            for (var i = 0; i < trees.Length; i++)
            {
                if (trees[i] == null) continue;
                trees[i].Sort();
                streets.Trees[i] = trees[i].ToArray();
            }
            return streets;
        }

        private static int Direction(int dx, int dz) => dx > 0 ? 0 : dx < 0 ? 1 : dz > 0 ? 2 : 3;

        // City segments within the radius, and their total sidewalk length (both sides).
        private void Nearby(Vector2 view)
        {
            _nearby.Clear();
            _nearbyLength = 0f;
            var roads = _streets.Roads;
            foreach (var segment in roads.Segments)
            {
                if (!_streets.City[segment.Index] || segment.Length < 6) continue;
                var a = roads.Nodes[segment.From];
                var b = roads.Nodes[segment.To];
                var x = Mathf.Clamp(view.x, Mathf.Min(a.X, b.X), Mathf.Max(a.X, b.X));
                var z = Mathf.Clamp(view.y, Mathf.Min(a.Z, b.Z), Mathf.Max(a.Z, b.Z));
                if ((new Vector2(x, z) - view).magnitude > Radius) continue;
                _nearby.Add(segment.Index);
                _nearbyLength += 2f * Mathf.Min(segment.Length, 2f * Radius);
            }
        }

        private Vector2 Corner(int node, int sx, int sz)
        {
            var n = _streets.Roads.Nodes[node];
            return new Vector2(n.X + sx * Mathf.Max(_streets.SpanX[node] - Inset, 0f), n.Z + sz * Mathf.Max(_streets.SpanZ[node] - Inset, 0f));
        }

        private bool TrySpawn(GameObject prefab, SitePlacement placement, Camera camera, Vector2 view, bool anywhere)
        {
            var roads = _streets.Roads;
            var segment = roads.Segments[_nearby[_random.Next(_nearby.Count)]];
            var forward = _random.Next(2) == 0;
            var side = _random.Next(2) == 0 ? 1 : -1;
            var from = forward ? segment.From : segment.To;
            var to = forward ? segment.To : segment.From;
            var dx = forward ? segment.Dx : -segment.Dx;
            var dz = forward ? segment.Dz : -segment.Dz;
            // The leg of sidewalk from `from` to `to`, then on to the corner it ends at.
            var start = Leg(segment.Index, from, dx, dz, side);
            var end = Leg(segment.Index, to, -dx, -dz, side);
            if ((end - start).magnitude < 2f) return false;
            var position = Vector2.Lerp(start, end, (float)_random.NextDouble());
            if ((position - view).magnitude > Radius) return false;
            var scene = placement.OnGround(position.x, position.y);
            if (!anywhere && (position - view).magnitude < HiddenDistance && OnScreen(camera, scene)) return false;
            var walker = Take(prefab);
            walker.Position = position;
            walker.Speed = Mathf.Lerp(1.15f, 1.6f, (float)_random.NextDouble());
            walker.Count = 0;
            walker.Index = 0;
            walker.Path[walker.Count++] = end;
            ArriveAt(walker, to, dx, dz, side);
            walker.LegPoint = 0;
            walker.Segment = segment.Index;
            walker.Side = side;
            walker.Back = Direction(-dx, -dz);
            walker.Pending = -1;
            var heading = end - position;
            walker.Transform.SetPositionAndRotation(scene, heading.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)) : Quaternion.identity);
            _walkers.Add(walker);
            return true;
        }

        // The end at `node` of a segment's sidewalk leg on `side`, where (dx, dz) points from the node into the segment: clear
        // of the junction box, on the segment's own sidewalk line.
        private Vector2 Leg(int segmentIndex, int node, int dx, int dz, int side)
        {
            var segment = _streets.Roads.Segments[segmentIndex];
            var n = _streets.Roads.Nodes[node];
            var lateral = segment.Width / 2f - Inset;
            return dx != 0
                ? new Vector2(n.X + dx * Mathf.Max(_streets.SpanX[node] - Inset, 0f), n.Z + side * lateral)
                : new Vector2(n.X + side * lateral, n.Z + dz * Mathf.Max(_streets.SpanZ[node] - Inset, 0f));
        }

        // Ends the walker's path at the corner of `node` it reaches walking in (dx, dz) on `side`.
        private void ArriveAt(Walker walker, int node, int dx, int dz, int side)
        {
            walker.Node = node;
            if (dx != 0)
            {
                walker.Sx = -dx;
                walker.Sz = side;
            }
            else
            {
                walker.Sx = side;
                walker.Sz = -dz;
            }
            walker.Path[walker.Count++] = Corner(node, walker.Sx, walker.Sz);
        }

        private void Step(Walker walker, double clock, SitePlacement placement)
        {
            var moving = false;
            if (walker.Index >= walker.Count) Choose(walker, clock);
            if (walker.Index < walker.Count)
            {
                var target = walker.Path[walker.Index];
                var offset = target - walker.Position;
                var step = walker.Speed * Time.deltaTime;
                if (offset.magnitude <= step)
                {
                    walker.Position = target;
                    walker.Index++;
                }
                else walker.Position += offset.normalized * step;
                moving = true;
            }
            var shown = walker.Position;
            if (walker.Index == walker.LegPoint && walker.Segment >= 0) shown += AroundTrees(walker);
            var scene = placement.OnGround(shown.x, shown.y);
            var transform = walker.Transform;
            var direction = scene - transform.position;
            direction.y = 0f;
            if (moving && direction.sqrMagnitude > 0.000001f)
                transform.SetPositionAndRotation(scene,
                    Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 360f * Time.deltaTime));
            else transform.position = scene;
            walker.Animator.SetFloat(SpeedParameter, moving ? walker.Speed : 0f, 0.12f, Time.deltaTime);
            walker.Animator.SetFloat(WalkRateParameter, walker.Speed / WalkClipSpeed);
        }

        // Sideways step (map metres) toward the lots past the nearest street tree on the walker's side of its segment.
        private Vector2 AroundTrees(Walker walker)
        {
            var trees = _streets.Trees[walker.Segment * 2 + (walker.Side > 0 ? 1 : 0)];
            if (trees == null) return Vector2.zero;
            var alongX = _streets.Roads.Segments[walker.Segment].Dx != 0;
            var along = alongX ? walker.Position.x : walker.Position.y;
            var index = System.Array.BinarySearch(trees, along);
            if (index < 0) index = ~index;
            var nearest = float.MaxValue;
            if (index < trees.Length) nearest = Mathf.Abs(trees[index] - along);
            if (index > 0) nearest = Mathf.Min(nearest, Mathf.Abs(trees[index - 1] - along));
            if (nearest >= TreeClear) return Vector2.zero;
            var amount = TreeStep * Mathf.SmoothStep(0f, 1f, 1f - nearest / TreeClear);
            return alongX ? new Vector2(0f, walker.Side * amount) : new Vector2(walker.Side * amount, 0f);
        }

        // At a corner: picks the next move (walk on along an arm, turn the corner, or cross) and builds its path. False while
        // waiting to cross.
        private bool Choose(Walker walker, double clock)
        {
            var node = walker.Node;
            if (walker.Pending >= 0)
            {
                if (Time.time < walker.NextCheck) return false;
                walker.NextCheck = Time.time + 0.4f;
                if (MayCross(walker, walker.Pending, clock))
                {
                    Begin(walker, walker.Pending);
                    return true;
                }
                if (Time.time - walker.WaitingSince < MaxWaitSeconds) return false;
                // Waited long enough: give up on this crossing and walk on another way.
                walker.Back = walker.Pending;
                walker.Pending = -1;
            }
            // Moves: 0-3 walk the arm that way, 4 to the corner across X (-sx), 5 to the corner across Z (-sz).
            var armX = walker.Sx > 0 ? 0 : 1;
            var armZ = walker.Sz > 0 ? 2 : 3;
            Span(walker, out var w4, out var w5);
            var wx = _streets.Arms[node * 4 + armX] >= 0 ? 3f : 0f;
            var wz = _streets.Arms[node * 4 + armZ] >= 0 ? 3f : 0f;
            if (walker.Back == armX && (wz + w4 + w5) > 0f) wx = 0f;
            if (walker.Back == armZ && (wx + w4 + w5) > 0f) wz = 0f;
            if (walker.Back == 4 && (wx + wz + w5) > 0f) w4 = 0f;
            if (walker.Back == 5 && (wx + wz + w4) > 0f) w5 = 0f;
            var total = wx + wz + w4 + w5;
            if (total <= 0f)
            {
                // Nowhere to go (an isolated corner): stand still.
                walker.Count = 0;
                walker.Index = 0;
                return false;
            }
            var roll = (float)_random.NextDouble() * total;
            var move = roll < wx ? armX : roll < wx + wz ? armZ : roll < wx + wz + w4 ? 4 : 5;
            if (move >= 4 && Crosses(walker, move) && !MayCross(walker, move, clock))
            {
                walker.Pending = move;
                walker.WaitingSince = Time.time;
                walker.NextCheck = Time.time + 0.4f;
                return false;
            }
            Begin(walker, move);
            return true;
        }

        // Weights of the two corner-to-corner moves: none where both corners coincide, a little less for a road crossing.
        private void Span(Walker walker, out float w4, out float w5)
        {
            var node = walker.Node;
            w4 = _streets.SpanX[node] - Inset > 0.05f ? Crosses(walker, 4) ? 1.2f : 1f : 0f;
            w5 = _streets.SpanZ[node] - Inset > 0.05f ? Crosses(walker, 5) ? 1.2f : 1f : 0f;
            // A crossing needs a crosswalk: the arm crossed must meet a road the other way (the junction box has depth).
            if (w4 > 0f && Crosses(walker, 4) && _streets.SpanZ[node] <= 0f) w4 = 0f;
            if (w5 > 0f && Crosses(walker, 5) && _streets.SpanX[node] <= 0f) w5 = 0f;
        }

        // Whether a corner-to-corner move crosses a road arm (else it walks the junction's edge on the sidewalk).
        private bool Crosses(Walker walker, int move) => move == 4
            ? _streets.Arms[walker.Node * 4 + (walker.Sz > 0 ? 2 : 3)] >= 0
            : _streets.Arms[walker.Node * 4 + (walker.Sx > 0 ? 0 : 1)] >= 0;

        private bool MayCross(Walker walker, int move, double clock)
        {
            var roads = _streets.Roads;
            var node = roads.Nodes[walker.Node];
            // Move 4 walks along X across a Z arm: it goes with the X traffic's green, while Z traffic waits.
            var walkAlongX = move == 4;
            var width = walkAlongX ? 2f * _streets.SpanX[walker.Node] : 2f * _streets.SpanZ[walker.Node];
            if (node.Control == JunctionControl.TrafficLight)
            {
                // The crossed traffic waits for the whole of this axis's half cycle (its green, then its amber): start on the
                // green, with time to reach the far side before that half ends.
                var into = RoadTraffic.LightPhase(node, clock) - (walkAlongX ? 0 : RoadTraffic.LightCycleSeconds / 2);
                return into >= 0 && into < RoadTraffic.GreenSeconds && into + width / walker.Speed + 1f <= RoadTraffic.LightCycleSeconds / 2f;
            }
            var placement = _streets.Placement;
            if (_traffic == null && Time.time >= _nextTrafficLookup)
            {
                _nextTrafficLookup = Time.time + 5f;
                _traffic = Object.FindAnyObjectByType<CityTrafficPresenter>();
            }
            if (_traffic == null) return true;
            var middle = move == 4
                ? new Vector2(node.X, node.Z + walker.Sz * (_streets.SpanZ[walker.Node] + CrosswalkOut))
                : new Vector2(node.X + walker.Sx * (_streets.SpanX[walker.Node] + CrosswalkOut), node.Z);
            foreach (var car in _traffic.Cars)
                if (car.Value != null && (placement.ToMap(car.Value.transform.position) - middle).sqrMagnitude < CarClearance * CarClearance)
                    return false;
            return true;
        }

        // Builds the path of a move from the walker's corner.
        private void Begin(Walker walker, int move)
        {
            var roads = _streets.Roads;
            var node = walker.Node;
            var n = roads.Nodes[node];
            walker.Pending = -1;
            walker.Count = 0;
            walker.Index = 0;
            walker.LegPoint = -1;
            walker.Segment = -1;
            if (move < 4)
            {
                var segmentIndex = _streets.Arms[node * 4 + move];
                var segment = roads.Segments[segmentIndex];
                var dx = move == 0 ? 1 : move == 1 ? -1 : 0;
                var dz = move == 2 ? 1 : move == 3 ? -1 : 0;
                var side = dx != 0 ? walker.Sz : walker.Sx;
                var other = segment.From == node ? segment.To : segment.From;
                walker.Path[walker.Count++] = Leg(segmentIndex, node, dx, dz, side);
                walker.Path[walker.Count++] = Leg(segmentIndex, other, -dx, -dz, side);
                walker.LegPoint = 1;
                walker.Segment = segmentIndex;
                walker.Side = side;
                ArriveAt(walker, other, dx, dz, side);
                walker.Back = move ^ 1;
                return;
            }
            var cx = Mathf.Max(_streets.SpanX[node] - Inset, 0f);
            var cz = Mathf.Max(_streets.SpanZ[node] - Inset, 0f);
            if (move == 4)
            {
                if (Crosses(walker, 4))
                {
                    var z = n.Z + walker.Sz * (_streets.SpanZ[node] + CrosswalkOut);
                    walker.Path[walker.Count++] = new Vector2(n.X + walker.Sx * cx, z);
                    walker.Path[walker.Count++] = new Vector2(n.X - walker.Sx * cx, z);
                }
                walker.Sx = -walker.Sx;
            }
            else
            {
                if (Crosses(walker, 5))
                {
                    var x = n.X + walker.Sx * (_streets.SpanX[node] + CrosswalkOut);
                    walker.Path[walker.Count++] = new Vector2(x, n.Z + walker.Sz * cz);
                    walker.Path[walker.Count++] = new Vector2(x, n.Z - walker.Sz * cz);
                }
                walker.Sz = -walker.Sz;
            }
            walker.Path[walker.Count++] = Corner(node, walker.Sx, walker.Sz);
            walker.Back = move;
        }

        private Walker Take(GameObject prefab)
        {
            var key = ++_serial;
            var variant = CharacterFigures.Pick(key * 2654435761u);
            var pool = _pools[variant] ??= new Stack<Walker>();
            var walker = pool.Count > 0 ? pool.Pop() : null;
            if (walker == null || walker.Root == null)
            {
                var root = CharacterFigures.Create(prefab, variant, _container, Vector3.zero, Quaternion.identity, out var animator);
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                walker = new Walker { Root = root, Transform = root.transform, Animator = animator, Tinted = CharacterFigures.Tinted(root), Variant = variant };
                DistanceCulling.Apply(root);
                Pedestrians.Add(walker.Transform);
            }
            walker.Root.name = $"Pedestrian {key}";
            CharacterFigures.Tint(walker.Tinted, Color.HSVToRGB((float)_random.NextDouble(), Mathf.Lerp(0.35f, 0.6f, (float)_random.NextDouble()),
                Mathf.Lerp(0.55f, 0.9f, (float)_random.NextDouble())));
            walker.Root.SetActive(true);
            return walker;
        }

        private void Release(Walker walker)
        {
            if (walker.Root == null) return;
            walker.Root.SetActive(false);
            if (_pools != null && walker.Variant < _pools.Length) (_pools[walker.Variant] ??= new Stack<Walker>()).Push(walker);
        }

        private static bool OnScreen(Camera camera, Vector3 point)
        {
            var view = camera.WorldToViewportPoint(point + Vector3.up);
            return view.z > 0 && view.x > -0.05f && view.x < 1.05f && view.y > -0.05f && view.y < 1.05f;
        }
    }
}
