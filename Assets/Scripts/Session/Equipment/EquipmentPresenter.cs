// Shows placed equipment from the latest replicated site baseline: one local visual per placed piece, keyed by equipment ID.
// Visuals never own state; held equipment has no visual, and a missing baseline clears the scene rather than guessing.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentPresenter : MonoBehaviour
    {
        [SerializeField] private SessionRoot session;

        private readonly Dictionary<string, EquipmentVisual> _visuals = new();
        private GoodsSnapshot _shown;

        public IReadOnlyDictionary<string, EquipmentVisual> Visuals => _visuals;

        private void Update()
        {
            var site = session.ClientSite;
            if (ReferenceEquals(site, _shown)) return;
            _shown = site;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var placed = layout == null ? new List<GoodsEquipment>()
                : site.Equipment.Where(x => x.State == EquipmentState.Placed).ToList();
            foreach (var id in _visuals.Keys.Where(x => placed.All(y => y.Id != x)).ToList())
            {
                Destroy(_visuals[id].gameObject);
                _visuals.Remove(id);
            }
            foreach (var equipment in placed)
            {
                if (!_visuals.TryGetValue(equipment.Id, out var visual))
                {
                    visual = Create(equipment);
                    if (visual == null) continue;
                    _visuals.Add(equipment.Id, visual);
                }
                visual.transform.SetPositionAndRotation(SiteGridSpace.Center(layout, equipment), SiteGridSpace.Rotation(equipment.Rotation));
            }
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var visual in _visuals.Values)
                if (visual != null) Destroy(visual.gameObject);
            _visuals.Clear();
            _shown = null;
        }

        private EquipmentVisual Create(GoodsEquipment equipment)
        {
            var definition = session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == equipment.Kind);
            if (definition == null || definition.VisualPrefab == null)
            {
                Debug.LogWarning($"[Equipment] No visual for kind '{equipment.Kind}' ({equipment.Id}).");
                return null;
            }
            var root = new GameObject($"Equipment {equipment.Id}");
            root.transform.SetParent(transform, false);
            var model = Instantiate(definition.VisualPrefab, root.transform, false);
            // Centre the model's rendered bounds on the footprint and stand it on the floor, whatever the asset's pivot.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var item in renderers.Skip(1)) bounds.Encapsulate(item.bounds);
                var center = root.transform.InverseTransformPoint(bounds.center);
                var floor = root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
                model.transform.localPosition -= new Vector3(center.x, floor, center.z);
            }
            var visual = root.AddComponent<EquipmentVisual>();
            visual.Bind(equipment.Id, equipment.Kind);
            return visual;
        }
    }
}
