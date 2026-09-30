// Draws the customers of every drawn site from replicated server records; visuals never change simulation or persistence.
// A bounded set walks along local NavMesh paths, and first appears at a site edge (a generated lot's street, SiteStreet)
// outside the local camera.
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

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject customerPrefab;
        [SerializeField, Range(1, 200)] private int maxVisible = 100;

        private readonly Dictionary<string, Visual> _visuals = new();
        private readonly List<string> _remove = new();
        private int _shown = -1;
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
            public string SiteId;
        }

        public int VisibleCount => _visuals.Count;

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
            if (drawn.Version != _shown || Time.time >= _nextRefresh)
            {
                _shown = drawn.Version;
                _nextRefresh = Time.time + 0.5f;
                Refresh(drawn);
            }
            _remove.Clear();
            foreach (var (id, visual) in _visuals)
            {
                Move(visual);
                if (visual.Leaving && ((visual.Root.transform.position - visual.Target).sqrMagnitude < 0.09f
                    || Time.time >= visual.LeaveDeadline))
                {
                    Destroy(visual.Root);
                    _remove.Add(id);
                }
            }
            foreach (var id in _remove) _visuals.Remove(id);
        }

        // Every drawn restaurant's customers, at its site's place in the scene (decision 0031); visibility is capped overall.
        private void Refresh(DrawnSites drawn)
        {
            var sites = drawn.Sites.Where(x => x.Layout != null).ToList();
            if (sites.Count == 0 || customerPrefab == null)
            {
                Clear();
                return;
            }
            var bySite = sites.ToDictionary(x => x.SiteId);
            var active = new HashSet<string>(sites.SelectMany(site => site.Snapshot.Customers.Where(x => x.RestaurantId == site.SiteId).Select(x => x.Id)));
            // The player rig's camera is untagged in DevSite, so Camera.main alone misses the actual local view.
            var camera = Camera.main != null ? Camera.main : Camera.allCameras.FirstOrDefault(x => x.isActiveAndEnabled);
            foreach (var (id, visual) in _visuals)
            {
                if (active.Contains(id) || visual.Leaving) continue;
                visual.Leaving = true;
                visual.LeaveDeadline = Time.time + 12f;
                if (bySite.TryGetValue(visual.SiteId, out var from) && TryEdge(from.Layout, from.Snapshot, camera, id, out var exit)) SetTarget(visual, exit);
                else visual.LeaveDeadline = Time.time + 1f;
            }
            foreach (var drawnSite in sites) RefreshSite(drawnSite, camera);
        }

        private void RefreshSite(DrawnSite drawnSite, Camera camera)
        {
            var site = drawnSite.Snapshot;
            var siteId = drawnSite.SiteId;
            var layout = drawnSite.Layout;
            var customers = site.Customers.Where(x => x.RestaurantId == siteId).ToList();
            var counters = site.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && x.Kind == GoodsWorld.CounterKind).ToList();
            var tables = site.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && x.Kind == GoodsWorld.TableKind).ToList();
            if (counters.Count == 0) return;
            var ranks = customers.Where(x => x.State == CustomerState.Queued).OrderBy(x => x.Ticket)
                .Select((customer, rank) => (customer.Id, rank)).ToDictionary(x => x.Id, x => x.rank);
            // Prioritize customers currently using the site over those still travelling toward it.
            foreach (var customer in customers.OrderBy(x => x.State == CustomerState.Travelling ? 1 : 0).ThenBy(x => x.Ticket))
            {
                if (!TryTarget(customer, layout, counters, tables, ranks, out var target)) continue;
                if (_visuals.TryGetValue(customer.Id, out var visual))
                {
                    visual.Leaving = false;
                    if ((visual.Target - target).sqrMagnitude > 0.04f) SetTarget(visual, target);
                    continue;
                }
                if (_visuals.Count >= maxVisible || customer.State == CustomerState.Travelling && customer.RemainingSeconds > 12
                    || !TryEdge(layout, site, camera, customer.Id, out var spawn)) continue;
                var root = Instantiate(customerPrefab, spawn, Quaternion.identity, transform);
                root.name = $"Customer {customer.Id}";
                var animator = root.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                Tint(root, customer);
                visual = new Visual { Root = root, Animator = animator, Target = spawn, SiteId = siteId };
                _visuals.Add(customer.Id, visual);
                SetTarget(visual, target);
            }
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
                var view = camera.WorldToViewportPoint(point + Vector3.up);
                if (view.z > 0 && view.x > -0.05f && view.x < 1.05f && view.y > -0.05f && view.y < 1.05f) continue;
                return true;
            }
            point = default;
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
            while (visual.Corner < visual.Corners.Length && (transform.position - visual.Corners[visual.Corner]).sqrMagnitude < 0.04f)
                visual.Corner++;
            var moving = visual.Corner < visual.Corners.Length;
            if (moving)
            {
                var direction = visual.Corners[visual.Corner] - transform.position;
                direction.y = 0;
                transform.position = Vector3.MoveTowards(transform.position, new Vector3(visual.Corners[visual.Corner].x,
                    transform.position.y, visual.Corners[visual.Corner].z), WalkSpeed * Time.deltaTime);
                if (direction.sqrMagnitude > 0.001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), 540f * Time.deltaTime);
            }
            visual.Animator.SetFloat(SpeedParameter, moving ? WalkSpeed : 0f, 0.12f, Time.deltaTime);
            visual.Animator.SetFloat(WalkRateParameter, WalkSpeed / 1.5f);
        }

        private static void Tint(GameObject root, GoodsCustomer customer)
        {
            var hue = ((StableHash(customer.DistrictId) % 100) / 100f + customer.Appearance * 0.17f) % 1f;
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
            _shown = -1;
        }
    }
}
