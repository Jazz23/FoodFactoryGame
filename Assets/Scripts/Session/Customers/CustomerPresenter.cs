// Draws customers from replicated server records; visuals never change simulation or persistence. Two sources share one bounded
// set of figures keyed by customer ID: the customers of every drawn site, walking local NavMesh paths, and the crowd (decision
// 0033): customers at competitors near this client, walking fixed lines in front of the competitor's door (CompetitorFrontage),
// queueing outside it and hidden while inside. The current site's customers always get figures first; the remaining places go
// to whichever restaurants are nearest the camera. Figures first appear, and figures that lose their place leave, out of the
// local camera's view. On a drawn site figures walk the site's open cells (SiteWalk), so they come in through a door, queue back
// from the register's service spot along the way in, and only fall back to the NavMesh where no cell path exists; a figure on
// the move opens restaurant doors (DoorOpener), one standing or seated does not. Each figure's character model is picked from
// CharacterFigures by its customer ID. The presenter also drives the ambient street pedestrians (Logistics.AmbientPedestrians),
// which share its figure prefab and never represent any record.
// A site customer's figure appears on the street early enough to walk in by the time the server has it reach the register (the
// server times that walk, GoodsWorld.Diner.EntrySeconds), hurrying when it is late, and appears in view rather than not at all
// when no street point is out of view, so every sale has a figure at the register. A seated customer sits on the seat the server
// gave it (GoodsCustomer.Seat): a seat part of the table's model, a chair beside the table, or a place along its long sides,
// facing the table, in a seated pose posed in code over the idle clip (no sit clip exists).
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
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
        private const float WalkSpeed = 2f;
        // New figures appear only for travelling customers this close to arriving (the server sends crowds on the same rule).
        private const long ArrivalWindowSeconds = GoodsWorld.CrowdTravelSeconds;
        // A figure late for the server's arrival time walks at most this many times WalkSpeed.
        private const float MaxHurry = 2.2f;
        // Metres beyond a table's edge that a generated seat stands (no seat part or chair).
        private const float SeatOut = 0.2f;

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
        private Logistics.AmbientPedestrians _ambient;
        private GoodsSnapshot _clockBaseline;
        private float _clockReceivedAt;

        private sealed class Visual
        {
            public GameObject Root;
            public Animator Animator;
            public DoorOpener Opener;
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
            // Metres per second it walks now (WalkSpeed, or faster when late for the server's arrival time).
            public float Speed = WalkSpeed;
            // Sits facing SeatFacing once at its target (a seated customer); Seated while it does.
            public bool SitWanted;
            public Vector3 SeatFacing;
            public bool Seated;
            // The seated pose: leg bones (thigh, shin, foot per leg), the model's rest local position and how far it drops.
            public Transform[] Legs;
            public Vector3 ModelRest;
            public bool Lowered;
            public float Drop;
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
            // A travelling site customer's figure appears once RemainingSeconds is at most this.
            public long Lead = ArrivalWindowSeconds;
            // Seated at a table: sits facing this way at Target.
            public bool Sits;
            public Vector3 Facing;
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
                // At its seat: turn to the table and sit (LateUpdate poses it).
                visual.Seated = visual.SitWanted && !visual.Leaving && visual.Corner >= visual.Corners.Length;
                if (visual.Seated && visual.SeatFacing.sqrMagnitude > 0.001f)
                    visual.Root.transform.rotation = Quaternion.RotateTowards(visual.Root.transform.rotation,
                        Quaternion.LookRotation(visual.SeatFacing), 360f * Time.deltaTime);
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
            _ambient ??= new Logistics.AmbientPedestrians(transform);
            _ambient.Tick(customerPrefab, Clock(drawn), Belts.BeltPresenter.ViewCamera());
        }

        // After the Animator: seated figures take the seated pose, others drop it.
        private void LateUpdate()
        {
            foreach (var visual in _visuals.Values)
            {
                if (visual.Inside || visual.Root == null || visual.Animator == null) continue;
                if (visual.Seated) Sit(visual);
                else if (visual.Lowered)
                {
                    visual.Animator.transform.localPosition = visual.ModelRest;
                    visual.Lowered = false;
                }
            }
        }

        // Thighs forward, shins down, the model lowered by a thigh's length so the hips rest at seat height. Bones are turned in
        // world space, so it holds whatever axes the armature uses, and it is idempotent on frames the Animator skips (culling).
        private static void Sit(Visual visual)
        {
            var model = visual.Animator.transform;
            if (visual.Legs == null)
            {
                var names = new[] { "thigh.L", "shin.L", "foot.L", "thigh.R", "shin.R", "foot.R" };
                var bones = names.Select(x => FindBone(model, x)).ToArray();
                visual.Legs = bones.Any(x => x == null) ? Array.Empty<Transform>() : bones;
            }
            if (visual.Legs.Length == 0) return;
            if (!visual.Lowered)
            {
                visual.ModelRest = model.localPosition;
                if (visual.Drop <= 0f)
                    visual.Drop = Mathf.Clamp((visual.Legs[0].position.y - visual.Legs[1].position.y + visual.Legs[3].position.y - visual.Legs[4].position.y) * 0.5f, 0.2f, 0.7f);
                visual.Lowered = true;
            }
            var parent = model.parent;
            model.position = (parent != null ? parent.TransformPoint(visual.ModelRest) : visual.ModelRest) + Vector3.down * visual.Drop;
            var forward = visual.Root.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) return;
            forward.Normalize();
            for (var leg = 0; leg < 6; leg += 3)
            {
                var (thigh, shin, foot) = (visual.Legs[leg], visual.Legs[leg + 1], visual.Legs[leg + 2]);
                thigh.rotation = Quaternion.FromToRotation(shin.position - thigh.position, forward + Vector3.down * 0.1f) * thigh.rotation;
                shin.rotation = Quaternion.FromToRotation(foot.position - shin.position, Vector3.down + forward * 0.1f) * shin.rotation;
            }
        }

        private static Transform FindBone(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                var found = FindBone(child, name);
                if (found != null) return found;
            }
            return null;
        }

        // The world clock the drawn city cars run on (the current site's baseline, run on by at most a little over a second),
        // so ambient pedestrians cross with the same traffic lights; null with no current site.
        private double? Clock(DrawnSites drawn)
        {
            GoodsSnapshot current = null;
            var sites = drawn.Sites;
            for (var i = 0; i < sites.Count; i++)
                if (sites[i].Current)
                {
                    current = sites[i].Snapshot;
                    break;
                }
            if (current == null) return null;
            if (!ReferenceEquals(current, _clockBaseline))
            {
                _clockBaseline = current;
                _clockReceivedAt = Time.time;
            }
            return current.ClockSeconds + Mathf.Min(Time.time - _clockReceivedAt, 1.25f);
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
                if (!has && (candidate.Enters || candidate.Travelling && candidate.RemainingSeconds > candidate.Lead)) continue;
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
                var placed = site.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed).ToList();
                var tables = placed.Where(GoodsWorld.IsTable).ToList();
                var customers = site.Customers.Where(x => x.RestaurantId == siteId).ToList();
                var ranks = Ranks(customers.Where(x => x.State == CustomerState.Queued).Select(x => (x.Id, x.Ticket)));
                // Customers still on their way join the end of the line, soonest first.
                var arriving = customers.Where(x => x.State == CustomerState.Travelling).OrderBy(x => x.RemainingSeconds)
                    .ThenBy(x => x.Id, StringComparer.Ordinal).Select((x, index) => (x.Id, Rank: ranks.Count + index))
                    .ToDictionary(x => x.Id, x => x.Rank);
                var distance = drawnSite.Current ? 0f : Flat(eye, SiteGridSpace.Origin(drawnSite.Layout));
                foreach (var customer in customers)
                {
                    if (!TryTarget(customer, drawnSite, counters, tables, placed, ranks, arriving, out var target, out var facing)) continue;
                    var candidate = new Candidate
                    {
                        Id = customer.Id, DistrictId = customer.DistrictId, Appearance = customer.Appearance, Group = drawnSite.Current ? 0 : 1,
                        Distance = distance, Busy = customer.State != CustomerState.Travelling, Ticket = customer.Ticket,
                        Travelling = customer.State == CustomerState.Travelling, RemainingSeconds = customer.RemainingSeconds,
                        SiteId = siteId, Site = drawnSite, Target = target,
                        Sits = customer.State == CustomerState.Eating, Facing = facing
                    };
                    // A figure not drawn yet appears early enough to walk in from the street by the server's arrival time.
                    if (candidate.Travelling && candidate.RemainingSeconds > ArrivalWindowSeconds && !_visuals.ContainsKey(customer.Id))
                        candidate.Lead = Math.Max(ArrivalWindowSeconds, (long)Mathf.Ceil(WalkInMetres(drawnSite, target) / WalkSpeed) + 2);
                    result.Add(candidate);
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
            visual.SitWanted = candidate.Sits;
            visual.SeatFacing = candidate.Facing;
            visual.Speed = WalkSpeed;
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
            if (moved || (visual.Target - candidate.Target).sqrMagnitude > 0.04f)
                SetTarget(visual, candidate.Target, SiteWalk.For(candidate.Site.Snapshot, candidate.Site.Layout));
            // On its way in: hurry when the rest of the walk would end after the server has the customer at the register, and
            // when it is still far from the line or register the server already has it at.
            if (!candidate.Sits && visual.Corner < visual.Corners.Length)
            {
                var left = Length(visual.Root.transform.position, visual.Corners.Skip(visual.Corner).ToArray());
                visual.Speed = candidate.Travelling
                    ? Mathf.Clamp(left / Mathf.Max(0.5f, candidate.RemainingSeconds - 0.5f), WalkSpeed, WalkSpeed * MaxHurry)
                    : left > 3f ? WalkSpeed * 1.5f : WalkSpeed;
            }
        }

        private GameObject Spawn(Candidate candidate, Camera camera)
        {
            Vector3 spawn;
            if (candidate.Frontage != null)
            {
                if (!TryStreet(candidate.Frontage, camera, candidate.Id, out spawn)) return null;
            }
            else if (!TryEdge(candidate.Site.Layout, candidate.Site.Snapshot, camera, candidate.Id, out spawn, inView: true)) return null;
            // The character model is a pure function of the customer ID, so every client draws the same person.
            var variant = CharacterFigures.Pick((uint)StableHash(candidate.Id));
            var root = CharacterFigures.Create(customerPrefab, variant, transform, spawn, Quaternion.identity, out var animator);
            root.name = $"Customer {candidate.Id}";
            Logistics.Pedestrians.Add(root.transform);
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            Tint(root, candidate.DistrictId, candidate.Appearance);
            DistanceCulling.Apply(root);
            var visual = new Visual { Root = root, Animator = animator, Target = spawn, Opener = root.AddComponent<DoorOpener>() };
            visual.Opener.Customer = true;
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
            visual.SitWanted = false;
            visual.Speed = WalkSpeed;
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
            if (from?.Layout != null && TryEdge(from.Layout, from.Snapshot, camera, id, out var exit)) SetTarget(visual, exit, SiteWalk.For(from.Snapshot, from.Layout));
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

        private bool TryTarget(GoodsCustomer customer, DrawnSite site, List<GoodsEquipment> counters, List<GoodsEquipment> tables,
            List<GoodsEquipment> placed, Dictionary<string, int> ranks, Dictionary<string, int> arriving, out Vector3 target, out Vector3 facing)
        {
            target = default;
            facing = default;
            var layout = site.Layout;
            if (customer.State == CustomerState.Eating)
            {
                var table = tables.FirstOrDefault(x => x.Id == customer.TableId);
                if (table == null) return false;
                var seats = Math.Max(1, table.Seats);
                // The server's seat; one seated before seats were assigned picks one by ticket.
                var seat = customer.Seat >= 0 ? customer.Seat : (int)(customer.Ticket % seats);
                var center = SiteGridSpace.Center(layout, table);
                target = SeatPoint(layout, table, placed, seat, seats);
                target.y = center.y;
                facing = center - target;
                facing.y = 0f;
                return true;
            }
            var counter = counters.FirstOrDefault(x => x.Id == customer.CounterId) ?? counters[0];
            var counterCenter = SiteGridSpace.Center(layout, counter);
            var queueRank = customer.State == CustomerState.Ordering ? -1
                : ranks.TryGetValue(customer.Id, out var value) ? value
                : arriving.TryGetValue(customer.Id, out var arrival) ? arrival : ranks.Count;
            var spot = SiteWalk.For(site.Snapshot, layout)?.ServiceSpot(counter, queueRank);
            if (spot.HasValue) target = spot.Value;
            else if (customer.State == CustomerState.Ordering)
                target = counterCenter + new Vector3(0, 0, -0.9f);
            else target = counterCenter + new Vector3(-0.9f - queueRank / 10 * 0.8f, 0, 0.9f + queueRank % 10 * 0.8f);
            return true;
        }

        // Where seat `seat` of a table is: a seat part of the table's own model (a "Seat" node), else a chair, stool, bench or
        // banquette standing beside it (by ID), else a place along the table's long sides, alternating sides.
        private Vector3 SeatPoint(SiteLayout layout, GoodsEquipment table, List<GoodsEquipment> placed, int seat, int seats)
        {
            var parts = SeatParts(table.Id);
            if (parts.Count > 0) return parts[seat % parts.Count];
            var chairs = placed.Where(x => x.Level == table.Level && RestaurantRules.IsSeatFurniture(x) && RestaurantRules.Beside(x, table))
                .OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (seat < chairs.Count) return SiteGridSpace.Center(layout, chairs[seat]);
            var (width, depth) = SiteGrid.Footprint(table.Width, table.Depth, table.Rotation);
            var alongX = width >= depth;
            var length = (alongX ? width : depth) * SiteGrid.CellSize;
            var half = (alongX ? depth : width) * SiteGrid.CellSize * 0.5f + SeatOut;
            var perSide = (seats + 1) / 2;
            var slot = seat / 2 % perSide;
            var side = seat % 2 == 0 ? -1f : 1f;
            var along = ((slot + 0.5f) / perSide - 0.5f) * length;
            return SiteGridSpace.Center(layout, table) + (alongX ? new Vector3(along, 0f, side * half) : new Vector3(side * half, 0f, along));
        }

        private EquipmentPresenter _equipment;
        private readonly Dictionary<string, (Vector3 At, List<Vector3> Seats)> _seatParts = new();

        // The world positions of a table visual's "Seat" nodes (the generic table's built-in chairs), in hierarchy order; empty
        // when the table is not drawn or has none. Cached while the visual stays where it is.
        private List<Vector3> SeatParts(string tableId)
        {
            if (_equipment == null) _equipment = FindAnyObjectByType<EquipmentPresenter>();
            if (_equipment == null || !_equipment.Visuals.TryGetValue(tableId, out var visual) || visual == null) return new List<Vector3>();
            var at = visual.transform.position;
            if (_seatParts.TryGetValue(tableId, out var cached) && (cached.At - at).sqrMagnitude < 0.0001f) return cached.Seats;
            var seats = new List<Vector3>();
            foreach (var part in visual.GetComponentsInChildren<Transform>(true))
            {
                if (part.name != "Seat") continue;
                seats.Add(part.TryGetComponent<Renderer>(out var renderer) ? renderer.bounds.center : part.position);
            }
            _seatParts[tableId] = (at, seats);
            return seats;
        }

        // Metres a figure walks from the street in front of the lot (its middle; a dev site's far edge) to a target on the site.
        private static float WalkInMetres(DrawnSite site, Vector3 target)
        {
            var layout = site.Layout;
            var street = SiteStreet.Outward(layout, site.Snapshot.Buildings);
            var from = street != null
                ? SiteStreet.Points(layout, street.Value)[4]
                : SiteGridSpace.Origin(layout) + new Vector3(0f, 0f, (layout.Depth * 0.5f - 0.5f) * SiteGrid.CellSize);
            var route = SiteWalk.For(site.Snapshot, layout)?.Route(from, target);
            return route != null ? Length(from, route) : Flat(from, target);
        }

        private static bool OnScreen(Camera camera, Vector3 point)
        {
            if (camera == null) return false;
            var view = camera.WorldToViewportPoint(point + Vector3.up);
            return view.z > 0 && view.x > -0.05f && view.x < 1.05f && view.y > -0.05f && view.y < 1.05f;
        }

        // A generated lot's figures come and go along the street in front of it; other sites use their grid's edges.
        // With inView, a point in view is used when none is out of view (a customer must have a figure to walk in).
        private static bool TryEdge(SiteLayout layout, GoodsSnapshot site, Camera camera, string id, out Vector3 point, bool inView = false)
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
            if (inView)
            {
                var first = (street != null ? Vector3.zero : SiteGridSpace.Origin(layout)) + edges[start];
                point = NavMesh.SamplePosition(first, out var hit, 2f, NavMesh.AllAreas) ? hit.position : first;
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

        // Over the site's open cells when it has a route, else the NavMesh. A figure never walks a straight line through walls: an
        // incomplete NavMesh path is walked as far as it goes, then the last short step only.
        private static void SetTarget(Visual visual, Vector3 target, SiteWalk walk)
        {
            visual.Target = target;
            visual.Corner = 0;
            var from = visual.Root.transform.position;
            var route = walk?.Route(from, target);
            if (route != null)
            {
                visual.Corners = route;
                return;
            }
            if (NavMesh.SamplePosition(from, out var origin, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(target, out var destination, 2f, NavMesh.AllAreas))
            {
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(origin.position, destination.position, NavMesh.AllAreas, path) && path.corners.Length > 0)
                {
                    var end = path.corners[path.corners.Length - 1];
                    visual.Corners = path.status == NavMeshPathStatus.PathComplete || Flat(end, target) < 1.5f
                        ? path.corners.Append(target).ToArray() : path.corners;
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
                transform.position = Vector3.MoveTowards(transform.position, next, visual.Speed * Time.deltaTime);
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
            }
            if (visual.Opener != null) visual.Opener.Opens = moving;
            visual.Animator.SetFloat(SpeedParameter, moving ? visual.Speed : 0f, 0.12f, Time.deltaTime);
            visual.Animator.SetFloat(WalkRateParameter, visual.Speed / 1.5f);
        }

        private static bool Reached(Visual visual, Vector3 position, Vector3 corner) => visual.Free
            ? (position - corner).sqrMagnitude < 0.04f
            : new Vector2(position.x - corner.x, position.z - corner.z).sqrMagnitude < 0.04f || (position - corner).sqrMagnitude < 0.04f;

        private static void Tint(GameObject root, string districtId, int appearance)
        {
            var hue = ((StableHash(districtId ?? "") % 100) / 100f + appearance * 0.17f) % 1f;
            CharacterFigures.Tint(CharacterFigures.Tinted(root), Color.HSVToRGB(hue, 0.5f, 0.85f));
        }

        private void OnDisable()
        {
            Clear();
            _ambient?.Clear();
        }

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
