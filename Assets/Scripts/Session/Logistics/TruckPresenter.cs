// Presentation only (decisions 0022, 0032): draws trucks from site baselines. Visuals are keyed by truck ID, never own state,
// and are cleared without a baseline. The truck model's length runs along its local Z, cab forward; its cab paint (the VH_Body
// material) is the company colour: this client's company green, like its awnings, any other company a colour from its ID.
// At a dock: a truck loading or unloading at a placed dock of a drawn site stands lengthwise behind the dock (its local +Z side,
// away from the front). A restaurant's dock (decision 0034) may stand anywhere in the lot, so its truck stands at the kerb in
// front of the lot, level with the dock, the way it arrived.
// On the roads (generated worlds, decision 0032): every truck driving the network near the camera, the company's own and other
// companies' (public, from RoadTrucks), is drawn where the simulation has it on its leg, in the right-hand kerb lane, carried
// on between baselines at the leg's speed; trucks waiting at the same leg end stand nose to tail. A parked truck stands at the
// kerb by its drawn site. Visuals ease toward these poses, so turns at junctions and pulling in to a dock are smoothed; a jump
// too long to ease is snapped. In a dev world (no map) only docked trucks are drawn, as before.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class TruckPresenter : MonoBehaviour
    {
        // Gap between the dock's back edge and the truck's side, in metres.
        private const float Clearance = 0.3f;
        // PROTOTYPE: trucks on the roads are drawn within this distance of the camera (decision 0031's draw radius).
        private const float RoadDrawRadius = DrawnSites.DrawRadius;
        // Bumper-to-bumper spacing of trucks waiting at one leg end, and of parked trucks, in metres.
        private const float QueueSpacing = 9f;
        // Easing rate toward the target pose (per second), and the jump beyond which a visual snaps instead.
        private const float EaseRate = 6f;
        private const float SnapDistance = 40f;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject truckPrefab;

        private readonly Dictionary<string, GameObject> _visuals = new();
        private int _shown = -1;
        private GoodsSnapshot _baseline;
        private float _receivedAt;
        private readonly Dictionary<string, string> _companyOf = new();
        private string _ownCompany;
        private MaterialPropertyBlock _block;

        private static readonly Color OwnPaint = new(0.18f, 0.62f, 0.26f);
        private static readonly Color[] OtherPaints =
        {
            new(0.75f, 0.15f, 0.12f), new(0.15f, 0.35f, 0.75f), new(0.55f, 0.2f, 0.6f), new(0.9f, 0.45f, 0.1f), new(0.2f, 0.55f, 0.6f)
        };
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public IReadOnlyDictionary<string, GameObject> Visuals => _visuals;

        private void Update()
        {
            var drawn = session.DrawnSites;
            var placement = SitePlacement.Active;
            if (placement == null)
            {
                // Dev worlds: poses change only with a baseline.
                if (drawn.Version == _shown) return;
                _shown = drawn.Version;
                Apply(DockPoses(drawn), false);
                return;
            }
            _shown = -1;
            var current = drawn.Sites.FirstOrDefault(x => x.Current)?.Snapshot;
            if (!ReferenceEquals(current, _baseline))
            {
                _baseline = current;
                _receivedAt = Time.time;
                _companyOf.Clear();
                if (current != null)
                {
                    _ownCompany = current.Companies.FirstOrDefault()?.Id;
                    foreach (var truck in current.Trucks.Concat(current.RoadTrucks)) _companyOf[truck.Id] = truck.CompanyId;
                }
            }
            var poses = DockPoses(drawn);
            if (current != null) RoadPoses(placement, current, drawn, Time.time - _receivedAt, poses);
            Apply(poses, true);
        }

        // Creates, moves and removes visuals so exactly the posed trucks are drawn.
        private void Apply(Dictionary<string, Pose> poses, bool ease)
        {
            foreach (var id in _visuals.Keys.Where(x => !poses.ContainsKey(x)).ToList())
            {
                Destroy(_visuals[id]);
                _visuals.Remove(id);
            }
            var k = 1f - Mathf.Exp(-EaseRate * Time.deltaTime);
            foreach (var (truckId, pose) in poses)
            {
                if (!_visuals.TryGetValue(truckId, out var visual))
                {
                    if (truckPrefab == null) continue;
                    visual = Instantiate(truckPrefab, transform);
                    visual.name = $"Truck {truckId}";
                    DistanceCulling.Apply(visual);
                    _visuals.Add(truckId, visual);
                    visual.transform.SetPositionAndRotation(pose.position, pose.rotation);
                    Paint(visual, truckId);
                    continue;
                }
                var shown = visual.transform;
                if (!ease || (shown.position - pose.position).sqrMagnitude > SnapDistance * SnapDistance)
                    shown.SetPositionAndRotation(pose.position, pose.rotation);
                else
                    shown.SetPositionAndRotation(Vector3.Lerp(shown.position, pose.position, k), Quaternion.Slerp(shown.rotation, pose.rotation, k));
            }
        }

        // Trucks loading or unloading at a placed dock of a drawn site.
        private Dictionary<string, Pose> DockPoses(DrawnSites drawn)
        {
            var poses = new Dictionary<string, Pose>();
            foreach (var drawnSite in drawn.Sites.Where(x => x.Layout != null))
            {
                var site = drawnSite.Snapshot;
                var siteId = drawnSite.SiteId;
                foreach (var truck in site.Trucks.Where(x => x.SiteId == siteId && x.State is TruckState.Loading or TruckState.Unloading))
                {
                    var route = GoodsWorld.RouteOf(site, truck);
                    var dockId = truck.State == TruckState.Loading ? route?.PickupDockId : route?.DropoffDockId;
                    var dock = site.Equipment.FirstOrDefault(x => x.Id == dockId && x.State == EquipmentState.Placed && x.SiteId == siteId);
                    if (dock == null) continue;
                    var street = RestaurantRules.IsRestaurantSite(site, siteId) ? Customers.SiteStreet.Outward(drawnSite.Layout, site.Buildings) : null;
                    poses[truck.Id] = DockPose(dock, drawnSite.Layout, street);
                }
            }
            return poses;
        }

        private Pose DockPose(GoodsEquipment dock, SiteLayout layout, Vector2Int? street)
        {
            var halfWidth = TruckHalfWidth();
            if (street is { } outward)
            {
                // Kerbside: half a lane beyond the lot's street edge, lengthwise along the street in the near lane's direction
                // of travel (right-hand traffic keeps the lot on its right), level with the dock.
                var dockCenter = SiteGridSpace.Center(layout, dock);
                var origin = SiteGridSpace.Origin(layout);
                var edge = outward.x == 0 ? layout.Depth * 0.5f : layout.Width * 0.5f;
                var kerb = (edge + Clearance) * SiteGrid.CellSize + halfWidth;
                var position = outward.x == 0 ? new Vector3(dockCenter.x, origin.y, origin.z + outward.y * kerb)
                    : new Vector3(origin.x + outward.x * kerb, origin.y, dockCenter.z);
                return new Pose(position, Quaternion.LookRotation(new Vector3(outward.y, 0f, -outward.x)));
            }
            var rotation = SiteGridSpace.Rotation(dock.Rotation);
            var behind = dock.Depth * 0.5f * SiteGrid.CellSize + Clearance + halfWidth;
            // Lengthwise along the dock's back: the model's length (local Z) along the dock's local X.
            return new Pose(SiteGridSpace.Center(layout, dock) + rotation * new Vector3(0f, 0f, behind), rotation * Quaternion.Euler(0f, 90f, 0f));
        }

        // Trucks driving the network, and parked trucks at drawn sites, from the current site's baseline.
        private void RoadPoses(SitePlacement placement, GoodsSnapshot current, DrawnSites drawn, float since, Dictionary<string, Pose> poses)
        {
            var network = placement.Roads;
            var camera = Belts.BeltPresenter.ViewCamera();
            var view = camera != null ? placement.ToMap(camera.transform.position) : (Vector2?)null;
            var waiting = new Dictionary<(int, bool, int), int>();
            foreach (var truck in current.Trucks.Concat(current.RoadTrucks).Where(x => x.Driving && x.OnRoad && !poses.ContainsKey(x.Id))
                         .OrderBy(x => x.Id, System.StringComparer.Ordinal))
            {
                if (!network.TryGetSegment(truck.LegSegmentId, out var segment)) continue;
                var forward = truck.LegTo >= truck.LegFrom;
                var back = 0f;
                if (RoadPose.AtLegEnd(truck, since))
                {
                    var key = (segment, forward, truck.LegTo);
                    var ahead = waiting.GetValueOrDefault(key);
                    waiting[key] = ahead + 1;
                    back = ahead * QueueSpacing;
                }
                var (map, heading) = RoadPose.Along(network, segment, RoadPose.LegOffset(truck, since), forward, 0, back);
                if (view.HasValue && (map - view.Value).sqrMagnitude > RoadDrawRadius * RoadDrawRadius) continue;
                poses[truck.Id] = new Pose(placement.OnGround(map.x, map.y), Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)));
            }
            // Parked trucks line the kerb in front of their drawn site, nose to tail.
            foreach (var drawnSite in drawn.Sites)
            {
                var site = current.Sites.FirstOrDefault(x => x.Id == drawnSite.SiteId);
                var point = site == null ? null : network.Locate(site.MapX, site.MapZ);
                if (point == null) continue;
                var index = 0;
                foreach (var truck in current.Trucks.Where(x => x.State == TruckState.Parked && x.SiteId == drawnSite.SiteId)
                             .OrderBy(x => x.Id, System.StringComparer.Ordinal))
                {
                    var (map, heading) = RoadPose.Along(network, point.Value.Segment, point.Value.Offset, true, 0, index++ * QueueSpacing);
                    var segment = network.Segments[point.Value.Segment];
                    var right = new Vector2(heading.y, -heading.x);
                    map += right * (segment.Width / 2f - RoadPose.KerbMetres - 1.2f - RoadPose.LaneOffset(segment, 0));
                    poses[truck.Id] = new Pose(placement.OnGround(map.x, map.y), Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y)));
                }
            }
        }

        // Half the truck model's width (its local X), measured once on a spare instance, so it parks clear of the dock
        // whatever the model's size.
        private float TruckHalfWidth()
        {
            if (_halfWidth > 0f || truckPrefab == null) return _halfWidth > 0f ? _halfWidth : 1f;
            var probe = Instantiate(truckPrefab);
            probe.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = probe.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                _halfWidth = bounds.extents.x;
            }
            Destroy(probe);
            return _halfWidth > 0f ? _halfWidth : 1f;
        }

        private float _halfWidth;

        private void Paint(GameObject visual, string truckId)
        {
            var company = _companyOf.GetValueOrDefault(truckId);
            var colour = company == null || company == _ownCompany ? OwnPaint
                : OtherPaints[(int)(WorldRandom.Fnv1a(company) % (ulong)OtherPaints.Length)];
            _block ??= new MaterialPropertyBlock();
            foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                {
                    var material = renderer.sharedMaterials[i];
                    if (material == null || !material.name.StartsWith(CityTrafficPresenter.BodyMaterialPrefix)) continue;
                    renderer.GetPropertyBlock(_block, i);
                    _block.SetColor(BaseColor, colour);
                    renderer.SetPropertyBlock(_block, i);
                }
        }
    }
}
