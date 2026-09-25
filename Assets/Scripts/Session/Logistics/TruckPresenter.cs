// Presentation only (decision 0022): a placeholder truck stands behind each placed dock of the local site while a company truck
// loads or unloads there, lengthwise along the dock's back (local +Z, away from its front). A driving truck is between sites
// and has no place in this scene. Visuals are keyed by truck ID, never own state, and are cleared without a baseline.
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
        private GoodsSnapshot _shown;

        public IReadOnlyDictionary<string, GameObject> Visuals => _visuals;

        private void Update()
        {
            var site = session.ClientSite;
            if (ReferenceEquals(site, _shown)) return;
            _shown = site;
            var siteId = session.ClientSubscription?.SiteId;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == siteId);
            var parked = new Dictionary<string, GoodsEquipment>();
            if (layout != null)
                foreach (var truck in site.Trucks.Where(x => x.SiteId == siteId && x.State is TruckState.Loading or TruckState.Unloading))
                {
                    var route = GoodsWorld.RouteOf(site, truck);
                    var dockId = truck.State == TruckState.Loading ? route?.PickupDockId : route?.DropoffDockId;
                    var dock = site.Equipment.FirstOrDefault(x => x.Id == dockId && x.State == EquipmentState.Placed && x.SiteId == siteId);
                    if (dock != null) parked[truck.Id] = dock;
                }
            foreach (var id in _visuals.Keys.Where(x => !parked.ContainsKey(x)).ToList())
            {
                Destroy(_visuals[id]);
                _visuals.Remove(id);
            }
            foreach (var (truckId, dock) in parked)
            {
                if (!_visuals.TryGetValue(truckId, out var visual))
                {
                    if (truckPrefab == null) continue;
                    visual = Instantiate(truckPrefab, transform);
                    visual.name = $"Truck {truckId}";
                    _visuals.Add(truckId, visual);
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
