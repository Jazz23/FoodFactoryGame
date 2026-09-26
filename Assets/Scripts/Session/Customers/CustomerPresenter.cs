// Draws nearby site customers from replicated server records; visuals never change simulation or persistence.
// A bounded set walks along local NavMesh paths, and first appears at a site edge outside the local camera.
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
        private GoodsSnapshot _shown;
        private float _nextRefresh;
        private ClientSiteSubscription _bound;

        private sealed class Visual
        {
            public GameObject Root;
            public Animator Animator;
            public Vector3 Target;
            public Vector3[] Corners = Array.Empty<Vector3>();
            public int Corner;
            public bool Leaving;
            public float LeaveDeadline;
        }

        public int VisibleCount => _visuals.Count;

        // Draws another client connection's replicated site instead of the session's own, e.g. a second client in one process.
        public void Bind(ClientSiteSubscription subscription)
        {
            _bound = subscription;
            Clear();
        }

        private ClientSiteSubscription Subscription => _bound ?? session.ClientSubscription;

        private void Update()
        {
            var site = Subscription?.Latest;
            if (!ReferenceEquals(site, _shown) || Time.time >= _nextRefresh)
            {
                _shown = site;
                _nextRefresh = Time.time + 0.5f;
                Refresh(site);
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

        private void Refresh(GoodsSnapshot site)
        {
            var siteId = Subscription?.SiteId;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null || customerPrefab == null)
            {
                Clear();
                return;
            }
            var customers = site.Customers.Where(x => x.RestaurantId == siteId).ToList();
            var active = new HashSet<string>(customers.Select(x => x.Id));
            // The player rig's camera is untagged in DevSite, so Camera.main alone misses the actual local view.
            var camera = Camera.main != null ? Camera.main : Camera.allCameras.FirstOrDefault(x => x.isActiveAndEnabled);
            foreach (var (id, visual) in _visuals)
            {
                if (active.Contains(id) || visual.Leaving) continue;
                visual.Leaving = true;
                visual.LeaveDeadline = Time.time + 12f;
                if (TryEdge(layout, camera, id, out var exit)) SetTarget(visual, exit);
                else visual.LeaveDeadline = Time.time + 1f;
            }
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
                    || !TryEdge(layout, camera, customer.Id, out var spawn)) continue;
                var root = Instantiate(customerPrefab, spawn, Quaternion.identity, transform);
                root.name = $"Customer {customer.Id}";
                var animator = root.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                Tint(root, customer);
                visual = new Visual { Root = root, Animator = animator, Target = spawn };
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

        private static bool TryEdge(SiteLayout layout, Camera camera, string id, out Vector3 point)
        {
            if (camera == null)
            {
                point = default;
                return false;
            }
            var halfX = (layout.Width * 0.5f - 0.5f) * SiteGrid.CellSize;
            var halfZ = (layout.Depth * 0.5f - 0.5f) * SiteGrid.CellSize;
            var edges = new[]
            {
                new Vector3(0, 0, halfZ), new Vector3(-halfX, 0, halfZ), new Vector3(halfX, 0, halfZ),
                new Vector3(-halfX, 0, 0), new Vector3(halfX, 0, 0),
                new Vector3(0, 0, -halfZ), new Vector3(-halfX, 0, -halfZ), new Vector3(halfX, 0, -halfZ)
            };
            var start = StableHash(id) % edges.Length;
            for (var index = 0; index < edges.Length; index++)
            {
                var candidate = edges[(start + index) % edges.Length];
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
            _shown = null;
        }
    }
}
