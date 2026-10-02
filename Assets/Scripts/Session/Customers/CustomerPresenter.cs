// Draws customers from replicated server records; visuals never change simulation or persistence. Two sources share one bounded
// set of figures keyed by customer ID: the customers of every drawn site, walking local NavMesh paths, and the crowd (decision
// 0033): customers at competitors near this client, walking fixed lines in front of the competitor's door (CompetitorFrontage),
// queueing outside it and hidden while inside. The current site's customers always get figures first; the remaining places go
// to whichever restaurants are nearest the camera. Figures first appear, and figures that lose their place leave, out of the
// local camera's view.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.AI;

namespace FoodFactoryGame.Session.Customers
{
    [DisallowMultipleComponent]
    public sealed class CustomerPresenter : MonoBehaviour
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkRateParameter = Animator.StringToHash("WalkRate");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private const float WalkSpeed = 2f;
        // New figures appear only for travelling customers this close to arriving (the server sends crowds on the same rule).
        private const long ArrivalWindowSeconds = GoodsWorld.CrowdTravelSeconds;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject customerPrefab;
        [SerializeField, Range(1, 200)] private int maxVisible = 100;

        private readonly Dictionary<string, Visual> _visuals = new();
        private readonly Dictionary<string, CompetitorFrontage> _frontages = new();
        private readonly List<string> _remove = new();
        private int _shown = -1;
        private GoodsCrowdView _shownCrowd;
        private SitePlacement _frontagePlacement;
        private float _nextRefresh;
        private DrawnSites _bound;

        private sealed class Visual
        {
            public GameObject Root;
            public Animator Animator;
            public Vector3 Target;
            public Vector3[] Corners = Array.Empty<Vector3>();
            public int Corner;
            public bool Leaving;
            public float LeaveDeadline;
            // The drawn site it belongs to, or null at a competitor.
            public string SiteId;
            // The competitor it belongs to, or null at a site.
            public CompetitorFrontage Frontage;
            // Walking a fixed line (a competitor's frontage) rather than NavMesh corners, so it also follows the line's height.
            public bool Free;
            // Goes in through the competitor's door on arrival.
            public bool HideOnArrival;
            // Inside a competitor: hidden, and not counted against the cap.
            public bool Inside;
        }

        // What one customer would be drawn doing this refresh.
        private sealed class Candidate
        {
            public string Id;
            public string DistrictId;
            public int Appearance;
            // 0 for the current site, 1 for everything else.
            public int Group;
            public float Distance;
            public bool Busy;
            public long Ticket;
            public bool Travelling;
            public long RemainingSeconds;
            public string SiteId;
            public DrawnSite Site;
            public CompetitorFrontage Frontage;
            public Vector3 Target;
            public bool Enters;
        }

        // Figures currently shown (customers inside a competitor are hidden and not counted).
        public int VisibleCount => _visuals.Values.Count(x => !x.Inside);

        // Draws another client connection's replicated sites instead of the session's own, e.g. a second client in one process.
        public void Bind(ClientSiteSubscription subscription)
        {
            _bound = subscription == null ? null : new DrawnSites(subscription);
            Clear();
        }

        private DrawnSites Drawn => _bound ?? session.DrawnSites;

        private void Update()
        {
            if (_bound != null)
            {
                var view = Belts.BeltPresenter.ViewCamera();
                _bound.Tick(view != null ? view.transform.position : (Vector3?)null);
            }
            var drawn = Drawn;
            if (drawn.Version != _shown || !ReferenceEquals(drawn.Crowd, _shownCrowd) || Time.time >= _nextRefresh)
            {
                _shown = drawn.Version;
                _shownCrowd = drawn.Crowd;
                _nextRefresh = Time.time + 0.5f;
                Refresh(drawn);
            }
            _remove.Clear();
            foreach (var (id, visual) in _visuals)
            {
                if (visual.Inside) continue;
                Move(visual);
                var arrived = (visual.Root.transform.position - visual.Target).sqrMagnitude < 0.09f;
                if (visual.Leaving && (arrived || Time.time >= visual.LeaveDeadline))
                {
                    Destroy(visual.Root);
                    _remove.Add(id);
                }
                else if (!visual.Leaving && visual.HideOnArrival && arrived)
                {
                    visual.Inside = true;
                    visual.Root.SetActive(false);
                }
            }
            foreach (var id in _remove) _visuals.Remove(id);
        }

        private void Refresh(DrawnSites drawn)
        {
            if (customerPrefab == null)
            {
                Clear();
                return;
            }
            // The player rig's camera is untagged in DevSite, so Camera.main alone misses the actual local view.
            var camera = Camera.main != null ? Camera.main : Camera.allCameras.FirstOrDefault(x => x.isActiveAndEnabled);
            var candidates = Candidates(drawn, camera);
            var byId = new Dictionary<string, Candidate>();
            foreach (var candidate in candidates) byId.TryAdd(candidate.Id, candidate);

            // Owner decision 4 (decision 0033): the current site first, then nearest the camera; customers using a restaurant
            // before those still arriving, then queue order. Figures hidden inside a competitor take no place.
            var wanted = new HashSet<string>();
            var places = 0;
            foreach (var candidate in byId.Values.OrderBy(x => x.Group).ThenBy(x => x.Distance).ThenBy(x => x.Busy ? 0 : 1)
                         .ThenBy(x => x.Ticket).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var has = _visuals.TryGetValue(candidate.Id, out var visual);
                if (has && visual.Inside && candidate.Enters) continue;
                if (!has && (candidate.Enters || candidate.Travelling && candidate.RemainingSeconds > ArrivalWindowSeconds)) continue;
                if (places >= maxVisible) continue;
                places++;
                wanted.Add(candidate.Id);
            }

            foreach (var (id, visual) in _visuals.ToList())
            {
                if (wanted.Contains(id) || visual.Leaving) continue;
                if (visual.Inside && byId.TryGetValue(id, out var still) && still.Enters && still.Frontage == visual.Frontage) continue;
                // Gone from the records, or displaced by a nearer figure: leave out of view. A displaced figure the camera cannot
                // see is simply removed.
                if (byId.ContainsKey(id) && !visual.Inside && !OnScreen(camera, visual.Root.transform.position))
                {
                    Destroy(visual.Root);
                    _visuals.Remove(id);
                    continue;
                }
                Leave(visual, id, drawn, camera);
            }

            var shown = VisibleCount;
            foreach (var candidate in byId.Values.Where(x => wanted.Contains(x.Id)).OrderBy(x => x.Group).ThenBy(x => x.Distance)
                         .ThenBy(x => x.Busy ? 0 : 1).ThenBy(x => x.Ticket).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (_visuals.TryGetValue(candidate.Id, out var visual))
                {
                    Follow(visual, candidate);
                    continue;
                }
                if (shown >= maxVisible) continue;
                var spawned = Spawn(candidate, camera);
                if (spawned != null) shown++;
            }
        }

        private List<Candidate> Candidates(DrawnSites drawn, Camera camera)
        {
            var result = new List<Candidate>();
            var eye = camera != null ? camera.transform.position : Vector3.zero;
            foreach (var drawnSite in drawn.Sites.Where(x => x.Layout != null))
            {
                var site = drawnSite.Snapshot;
                var siteId = drawnSite.SiteId;
                var counters = site.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && x.Kind == GoodsWorld.CounterKind).ToList();
                if (counters.Count == 0) continue;
                var tables = site.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && GoodsWorld.IsTable(x)).ToList();
                var customers = site.Customers.Where(x => x.RestaurantId == siteId).ToList();
                var ranks = Ranks(customers.Where(x => x.State == CustomerState.Queued).Select(x => (x.Id, x.Ticket)));
                var distance = drawnSite.Current ? 0f : Flat(eye, SiteGridSpace.Origin(drawnSite.Layout));
                foreach (var customer in customers)
                {
                    if (!TryTarget(customer, drawnSite.Layout, counters, tables, ranks, out var target)) continue;
                    result.Add(new Candidate
                    {
                        Id = customer.Id, DistrictId = customer.DistrictId, Appearance = customer.Appearance, Group = drawnSite.Current ? 0 : 1,
                        Distance = distance, Busy = customer.State != CustomerState.Travelling, Ticket = customer.Ticket,
                        Travelling = customer.State == CustomerState.Travelling, RemainingSeconds = customer.RemainingSeconds,
                        SiteId = siteId, Site = drawnSite, Target = target
                    });
                }
            }
            var crowd = drawn.Crowd;
            var placement = SitePlacement.Active;
            if (crowd == null || placement == null) return result;
            if (!ReferenceEquals(placement, _frontagePlacement))
            {
                _frontages.Clear();
                _frontagePlacement = placement;
            }
            foreach (var restaurant in crowd.Restaurants)
            {
                if (!_frontages.TryGetValue(restaurant.LotId, out var frontage))
                    _frontages[restaurant.LotId] = frontage = CompetitorFrontage.For(placement, restaurant.LotId);
                if (frontage == null) continue;
                var ranks = Ranks(restaurant.Customers.Where(x => x.State == CustomerState.Queued).Select(x => (x.Id, x.Ticket)));
                var distance = Flat(eye, frontage.Anchor);
                foreach (var customer in restaurant.Customers)
                {
                    var enters = customer.State is CustomerState.Ordering or CustomerState.Eating;
                    Vector3 target;
                    if (customer.State == CustomerState.Queued)
                    {
                        var rank = ranks.TryGetValue(customer.Id, out var value) ? value : 0;
                        if (rank >= CompetitorFrontage.QueueSpots) continue;
                        target = frontage.QueueSpot(rank);
                    }
                    else target = enters ? frontage.Door : frontage.Apron;
                    result.Add(new Candidate
                    {
                        Id = customer.Id, DistrictId = customer.DistrictId, Appearance = customer.Appearance, Group = 1, Distance = distance,
                        Busy = customer.State != CustomerState.Travelling, Ticket = customer.Ticket,
                        Travelling = customer.State == CustomerState.Travelling, RemainingSeconds = customer.RemainingSeconds,
                        Frontage = frontage, Target = target, Enters = enters
                    });
                }
            }
            return result;
        }

        private static Dictionary<string, int> Ranks(IEnumerable<(string Id, long Ticket)> queued) =>
            queued.OrderBy(x => x.Ticket).Select((customer, rank) => (customer.Id, rank)).ToDictionary(x => x.Id, x => x.rank);

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // Keeps an existing figure on its customer's current target, switching between a site's NavMesh and a frontage's line.
        private static void Follow(Visual visual, Candidate candidate)
        {
            var wasLeaving = visual.Leaving;
            visual.Leaving = false;
            if (visual.Inside)
            {
                // Came back out of a competitor (it now belongs elsewhere): reappear at the door it went in through.
                visual.Inside = false;
                visual.Root.transform.position = visual.Frontage?.Door ?? visual.Root.transform.position;
                visual.Root.SetActive(true);
            }
            visual.HideOnArrival = candidate.Enters;
            if (candidate.Frontage != null)
            {
                var arriving = visual.Frontage != candidate.Frontage || wasLeaving;
                if (!arriving && (visual.Target - candidate.Target).sqrMagnitude <= 0.04f) return;
                visual.SiteId = null;
                visual.Frontage = candidate.Frontage;
                visual.Free = true;
                SetPath(visual, arriving ? candidate.Frontage.Arrive(candidate.Target) : new[] { candidate.Target });
                return;
            }
            var moved = visual.Free || visual.SiteId != candidate.SiteId;
            // From a competitor's line onto a site's NavMesh, which keeps the figure's height: start at the site's floor (a lot
            // at another elevation pops once, on a rare walk-out between them).
            if (visual.Free)
            {
                var position = visual.Root.transform.position;
                visual.Root.transform.position = new Vector3(position.x, candidate.Target.y, position.z);
            }
            visual.SiteId = candidate.SiteId;
            visual.Frontage = null;
            visual.Free = false;
            if (moved || (visual.Target - candidate.Target).sqrMagnitude > 0.04f) SetTarget(visual, candidate.Target);
        }

        private GameObject Spawn(Candidate candidate, Camera camera)
        {
            Vector3 spawn;
            if (candidate.Frontage != null)
            {
                if (!TryStreet(candidate.Frontage, camera, candidate.Id, out spawn)) return null;
            }
            else if (!TryEdge(candidate.Site.Layout, candidate.Site.Snapshot, camera, candidate.Id, out spawn)) return null;
            var root = Instantiate(customerPrefab, spawn, Quaternion.identity, transform);
            root.name = $"Customer {candidate.Id}";
            var animator = root.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            Tint(root, candidate.DistrictId, candidate.Appearance);
            var visual = new Visual { Root = root, Animator = animator, Target = spawn };
            _visuals.Add(candidate.Id, visual);
            Follow(visual, candidate);
            return root;
        }

        // Walks a figure off out of view: a site's figure to its grid edge or street, a competitor's back out of the door (if it
        // was inside) and along the street.
        private void Leave(Visual visual, string id, DrawnSites drawn, Camera camera)
        {
            visual.Leaving = true;
            visual.HideOnArrival = false;
            visual.LeaveDeadline = Time.time + 12f;
            if (visual.Frontage != null)
            {
                if (visual.Inside)
                {
                    visual.Inside = false;
                    visual.Root.transform.position = visual.Frontage.Door;
                    visual.Root.SetActive(true);
                }
                if (TryStreet(visual.Frontage, camera, id, out var street))
                {
                    var path = visual.Frontage.Depart(street);
                    SetPath(visual, path);
                    visual.LeaveDeadline = Time.time + 2f + Length(visual.Root.transform.position, path) / WalkSpeed;
                }
                else visual.LeaveDeadline = Time.time + 1f;
                return;
            }
            var from = visual.SiteId == null ? null : drawn.Find(visual.SiteId);
            if (from?.Layout != null && TryEdge(from.Layout, from.Snapshot, camera, id, out var exit)) SetTarget(visual, exit);
            else visual.LeaveDeadline = Time.time + 1f;
        }

        private static float Length(Vector3 from, Vector3[] path)
        {
            var total = 0f;
            foreach (var point in path)
            {
                total += Vector3.Distance(from, point);
                from = point;
            }
            return total;
        }

        private static bool TryTarget(GoodsCustomer customer, SiteLayout layout, List<GoodsEquipment> counters,
            List<GoodsEquipment> tables, Dictionary<string, int> ranks, out Vector3 target)
        {
            target = default;
            if (customer.State == CustomerState.Eating)
            {
                var table = tables.FirstOrDefault(x => x.Id == customer.TableId);
                if (table == null) return false;
                var side = (int)(customer.Ticket % 4);
                target = SiteGridSpace.Center(layout, table) + new Vector3(side < 2 ? -0.7f : 0.7f, 0,
                    side % 2 == 0 ? -0.55f : 0.55f);
                return true;
            }
            var counter = counters.FirstOrDefault(x => x.Id == customer.CounterId) ?? counters[0];
            var center = SiteGridSpace.Center(layout, counter);
            if (customer.State == CustomerState.Ordering)
                target = center + new Vector3(0, 0, -0.9f);
            else
            {
                var rank = ranks.TryGetValue(customer.Id, out var value) ? value : 0;
                target = center + new Vector3(-0.9f - rank / 10 * 0.8f, 0, 0.9f + rank % 10 * 0.8f);
            }
            return true;
        }

        private static bool OnScreen(Camera camera, Vector3 point)
        {
            if (camera == null) return false;
            var view = camera.WorldToViewportPoint(point + Vector3.up);
            return view.z > 0 && view.x > -0.05f && view.x < 1.05f && view.y > -0.05f && view.y < 1.05f;
        }

        // A generated lot's figures come and go along the street in front of it; other sites use their grid's edges.
        private static bool TryEdge(SiteLayout layout, GoodsSnapshot site, Camera camera, string id, out Vector3 point)
        {
            if (camera == null)
            {
                point = default;
                return false;
            }
            var halfX = (layout.Width * 0.5f - 0.5f) * SiteGrid.CellSize;
            var halfZ = (layout.Depth * 0.5f - 0.5f) * SiteGrid.CellSize;
            var street = SiteStreet.Outward(layout, site.Buildings);
            var edges = street != null ? SiteStreet.Points(layout, street.Value).ToArray() : new[]
            {
                new Vector3(0, 0, halfZ), new Vector3(-halfX, 0, halfZ), new Vector3(halfX, 0, halfZ),
                new Vector3(-halfX, 0, 0), new Vector3(halfX, 0, 0),
                new Vector3(0, 0, -halfZ), new Vector3(-halfX, 0, -halfZ), new Vector3(halfX, 0, -halfZ)
            };
            var start = StableHash(id) % edges.Length;
            for (var index = 0; index < edges.Length; index++)
            {
                var candidate = (street != null ? Vector3.zero : SiteGridSpace.Origin(layout)) + edges[(start + index) % edges.Length];
                point = NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas) ? hit.position : candidate;
                if (OnScreen(camera, point)) continue;
                return true;
            }
            point = default;
            return false;
        }

        // A competitor's figures come and go at street points in front of its lot, out of view.
        private static bool TryStreet(CompetitorFrontage frontage, Camera camera, string id, out Vector3 point)
        {
            point = default;
            if (camera == null) return false;
            var start = StableHash(id) % frontage.Street.Count;
            for (var index = 0; index < frontage.Street.Count; index++)
            {
                point = frontage.Street[(start + index) % frontage.Street.Count];
                if (!OnScreen(camera, point)) return true;
            }
            return false;
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                var hash = 17;
                foreach (var character in text) hash = hash * 31 + character;
                return hash & int.MaxValue;
            }
        }

        private static void SetPath(Visual visual, Vector3[] corners)
        {
            visual.Corners = corners;
            visual.Corner = 0;
            visual.Target = corners[corners.Length - 1];
        }

        private static void SetTarget(Visual visual, Vector3 target)
        {
            visual.Target = target;
            visual.Corner = 0;
            var from = visual.Root.transform.position;
            if (NavMesh.SamplePosition(from, out var origin, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(target, out var destination, 2f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(origin.position, destination.position, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                {
                    visual.Corners = path.corners;
                    return;
                }
            }
            visual.Corners = new[] { target };
        }

        private static void Move(Visual visual)
        {
            var transform = visual.Root.transform;
            while (visual.Corner < visual.Corners.Length && Reached(visual, transform.position, visual.Corners[visual.Corner]))
                visual.Corner++;
            var moving = visual.Corner < visual.Corners.Length;
            if (moving)
            {
                var corner = visual.Corners[visual.Corner];
                var direction = corner - transform.position;
                direction.y = 0;
                // NavMesh corners keep the figure's height; a frontage line follows its own (a competitor's ground floor).
                var next = visual.Free ? corner : new Vector3(corner.x, transform.position.y, corner.z);
                transform.position = Vector3.MoveTowards(transform.position, next, WalkSpeed * Time.deltaTime);
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
            }
            visual.Animator.SetFloat(SpeedParameter, moving ? WalkSpeed : 0f, 0.12f, Time.deltaTime);
            visual.Animator.SetFloat(WalkRateParameter, WalkSpeed / 1.5f);
        }

        private static bool Reached(Visual visual, Vector3 position, Vector3 corner) => visual.Free
            ? (position - corner).sqrMagnitude < 0.04f
            : new Vector2(position.x - corner.x, position.z - corner.z).sqrMagnitude < 0.04f || (position - corner).sqrMagnitude < 0.04f;

        private static void Tint(GameObject root, string districtId, int appearance)
        {
            var hue = ((StableHash(districtId ?? "") % 100) / 100f + appearance * 0.17f) % 1f;
            var color = Color.HSVToRGB(hue, 0.5f, 0.85f);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, color);
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.Contains("Torso") || renderer.name.Contains("UpperArm") || renderer.name.Contains("Thigh")
                    || renderer.name.Contains("Shin") || renderer.name.Contains("Pelvis"))
                    renderer.SetPropertyBlock(block);
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var visual in _visuals.Values)
                if (visual.Root != null) Destroy(visual.Root);
            _visuals.Clear();
            _frontages.Clear();
            _frontagePlacement = null;
            _shown = -1;
            _shownCrowd = null;
        }
    }
}
