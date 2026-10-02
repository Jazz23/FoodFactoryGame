// Presentation only (decision 0022): a placeholder truck stands behind each placed dock of every drawn site while a company truck
// loads or unloads there, lengthwise along the dock's back (local +Z, away from its front). A driving truck is between sites
// and has no place in this scene. Visuals are keyed by truck ID, never own state, and are cleared without a baseline.
// A restaurant's dock (decision 0034) may stand anywhere in the lot, so its truck stands on the street in front of the lot,
// level with the dock, the way it arrived; there is no driving animation.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class TruckPresenter : MonoBehaviour
    {
        // Gap between the dock's back edge and the truck's side, in metres.
        private const float Clearance = 0.3f;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject truckPrefab;

        private readonly Dictionary<string, GameObject> _visuals = new();
        private int _shown = -1;

        public IReadOnlyDictionary<string, GameObject> Visuals => _visuals;

        private void Update()
        {
            var drawn = session.DrawnSites;
            if (drawn.Version == _shown) return;
            _shown = drawn.Version;
            var parked = new Dictionary<string, (GoodsEquipment Dock, SiteLayout Layout, Vector2Int? Street)>();
            foreach (var drawnSite in drawn.Sites.Where(x => x.Layout != null))
            {
                var site = drawnSite.Snapshot;
                var siteId = drawnSite.SiteId;
                foreach (var truck in site.Trucks.Where(x => x.SiteId == siteId && x.State is TruckState.Loading or TruckState.Unloading))
                {
                    var route = GoodsWorld.RouteOf(site, truck);
                    var dockId = truck.State == TruckState.Loading ? route?.PickupDockId : route?.DropoffDockId;
                    var dock = site.Equipment.FirstOrDefault(x => x.Id == dockId && x.State == EquipmentState.Placed && x.SiteId == siteId);
                    var street = RestaurantRules.IsRestaurantSite(site, siteId) ? Customers.SiteStreet.Outward(drawnSite.Layout, site.Buildings) : null;
                    if (dock != null) parked[truck.Id] = (dock, drawnSite.Layout, street);
                }
            }
            foreach (var id in _visuals.Keys.Where(x => !parked.ContainsKey(x)).ToList())
            {
                Destroy(_visuals[id]);
                _visuals.Remove(id);
            }
            foreach (var (truckId, (dock, layout, street)) in parked)
            {
                if (!_visuals.TryGetValue(truckId, out var visual))
                {
                    if (truckPrefab == null) continue;
                    visual = Instantiate(truckPrefab, transform);
                    visual.name = $"Truck {truckId}";
                    _visuals.Add(truckId, visual);
                }
                if (street is { } outward)
                {
                    // Kerbside: half a lane beyond the lot's street edge, lengthwise along the street, level with the dock.
                    var dockCenter = SiteGridSpace.Center(layout, dock);
                    var origin = SiteGridSpace.Origin(layout);
                    var edge = outward.x == 0 ? layout.Depth * 0.5f : layout.Width * 0.5f;
                    var out3 = new Vector3(outward.x, 0f, outward.y);
                    var kerb = (edge + Clearance) * SiteGrid.CellSize + TruckHalfWidth(visual);
                    var position = outward.x == 0 ? new Vector3(dockCenter.x, origin.y, origin.z + outward.y * kerb)
                        : new Vector3(origin.x + outward.x * kerb, origin.y, dockCenter.z);
                    visual.transform.SetPositionAndRotation(position, Quaternion.LookRotation(out3));
                    continue;
                }
                var rotation = SiteGridSpace.Rotation(dock.Rotation);
                var behind = dock.Depth * 0.5f * SiteGrid.CellSize + Clearance + TruckHalfWidth(visual);
                visual.transform.SetPositionAndRotation(SiteGridSpace.Center(layout, dock) + rotation * new Vector3(0f, 0f, behind), rotation);
            }
        }

        // Half the truck's rendered depth (its local Z), so it parks clear of the dock whatever the placeholder's size.
        private static float TruckHalfWidth(GameObject visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 1f;
            var rotation = visual.transform.rotation;
            visual.transform.rotation = Quaternion.identity;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            visual.transform.rotation = rotation;
            return bounds.extents.z;
        }
    }
}
