// Shows placed equipment from the latest replicated site baseline: one local visual per placed piece, keyed by equipment ID.
// Visuals never own state; held equipment has no visual, and a missing baseline clears the scene rather than guessing.
// A visual shows "running" exactly while the baseline has a running (not blocked) job on its station.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using UnityEngine;
using UnityEngine.AI;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentPresenter : MonoBehaviour
    {
        [SerializeField] private SessionRoot session;
        [SerializeField] private BuildingPresenter buildings;

        private readonly Dictionary<string, EquipmentVisual> _visuals = new();
        private readonly Dictionary<string, GoodsEquipment> _placed = new();
        private GoodsSnapshot _shown;

        public IReadOnlyDictionary<string, EquipmentVisual> Visuals => _visuals;

        private void Update()
        {
            var site = session.ClientSite;
            if (!ReferenceEquals(site, _shown)) Refresh(site);
            // Machines on a storey the local view hides (decision 0020) are hidden with it, colliders included.
            foreach (var (id, visual) in _visuals)
            {
                var equipment = _placed[id];
                var shown = !buildings.HidesLevel(equipment.CellX, equipment.CellZ, equipment.Level);
                if (visual.gameObject.activeSelf != shown) visual.gameObject.SetActive(shown);
            }
        }

        private void Refresh(GoodsSnapshot site)
        {
            _shown = site;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var placed = layout == null ? new List<GoodsEquipment>()
                : site.Equipment.Where(x => x.State == EquipmentState.Placed).ToList();
            foreach (var id in _visuals.Keys.Where(x => placed.All(y => y.Id != x)).ToList())
            {
                Destroy(_visuals[id].gameObject);
                _visuals.Remove(id);
                _placed.Remove(id);
            }
            foreach (var equipment in placed)
            {
                if (!_visuals.TryGetValue(equipment.Id, out var visual))
                {
                    visual = Create(equipment);
                    if (visual == null) continue;
                    _visuals.Add(equipment.Id, visual);
                }
                _placed[equipment.Id] = equipment;
                visual.transform.SetPositionAndRotation(SiteGridSpace.Center(layout, equipment), SiteGridSpace.Rotation(equipment.Rotation));
                visual.SetRunning(site.Jobs.Any(x => x.StationId == equipment.Id && x.State == StationJobState.Running));
            }
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var visual in _visuals.Values)
                if (visual != null) Destroy(visual.gameObject);
            _visuals.Clear();
            _placed.Clear();
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
            EquipmentModel.Create(definition, root.transform);
            // Ground machines carve the baked NavMesh so walking employees path around them. The box is the unrotated
            // footprint in the root's (rotated) space; upper storeys have no NavMesh yet.
            if (equipment.Level == 0)
            {
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(equipment.Width * SiteGrid.CellSize, 2f, equipment.Depth * SiteGrid.CellSize);
                obstacle.center = Vector3.up;
                obstacle.carving = true;
            }
            var visual = root.AddComponent<EquipmentVisual>();
            visual.Bind(equipment.Id, equipment.Kind);
            return visual;
        }
    }
}
