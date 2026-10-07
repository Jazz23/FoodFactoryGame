// Shows placed equipment from the latest replicated baselines of every drawn site (DrawnSites): one local visual per placed
// piece, keyed by equipment ID (unique across sites). Visuals never own state; held equipment has no visual, and a missing
// baseline clears the scene rather than guessing.
// A visual shows "running" exactly while the baseline has a running (not blocked) job on its station.
// Decor (decision 0034) stands where its mount says: on a wall face, under the ceiling, on the table top under it or (a sink)
// back to the wall behind it; pieces
// marked hide-from-above (ceiling panels) hide their renderers, not their lights, while this client looks down on the room
// it stands in. Only ground object-layer pieces carve the NavMesh.
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
        // Renderers of pieces hidden from above, by equipment ID.
        private readonly Dictionary<string, Renderer[]> _fromAbove = new();
        private int _shown = -1;

        public IReadOnlyDictionary<string, EquipmentVisual> Visuals => _visuals;

        private void Update()
        {
            var drawn = session.DrawnSites;
            if (drawn.Version != _shown) Refresh(drawn);
            // Machines on a storey the local view hides (decision 0020) are hidden with it, colliders included.
            foreach (var (id, visual) in _visuals)
            {
                var equipment = _placed[id];
                var shown = !buildings.HidesLevel(equipment.SiteId, equipment.CellX, equipment.CellZ, equipment.Level);
                if (visual.gameObject.activeSelf != shown) visual.gameObject.SetActive(shown);
            }
            var avatar = buildings.LocalAvatar;
            var inside = buildings.LocalBuilding;
            var lookingDown = avatar != null && avatar.CameraRig != null && avatar.CameraRig.TopDown && inside != null;
            foreach (var (id, renderers) in _fromAbove)
            {
                var equipment = _placed[id];
                var hide = (lookingDown && inside.SiteId == equipment.SiteId
                    && SiteGrid.Overlaps(equipment.CellX, equipment.CellZ, 1, 1, inside.CellX, inside.CellZ, inside.Width, inside.Depth))
                    || buildings.EditedSiteId == equipment.SiteId;
                foreach (var renderer in renderers)
                    if (renderer != null && renderer.enabled == hide) renderer.enabled = !hide;
            }
        }

        // Every drawn site's placed machines, each at its own site's place in the scene (decision 0031).
        private void Refresh(DrawnSites drawn)
        {
            _shown = drawn.Version;
            var placed = drawn.Sites.Where(x => x.Layout != null)
                .SelectMany(site => site.Snapshot.Equipment.Where(x => x.State == EquipmentState.Placed && x.SiteId == site.SiteId)
                    .Select(x => (Site: site, Equipment: x))).ToList();
            var ids = new HashSet<string>(placed.Select(x => x.Equipment.Id));
            foreach (var id in _visuals.Keys.Where(x => !ids.Contains(x)).ToList())
            {
                Destroy(_visuals[id].gameObject);
                _visuals.Remove(id);
                _placed.Remove(id);
                _fromAbove.Remove(id);
            }
            foreach (var (site, equipment) in placed.OrderBy(x => x.Equipment.Layer == SiteGrid.TabletopLayer))
            {
                if (!_visuals.TryGetValue(equipment.Id, out var visual))
                {
                    visual = Create(equipment);
                    if (visual == null) continue;
                    _visuals.Add(equipment.Id, visual);
                }
                _placed[equipment.Id] = equipment;
                var rotation = SiteGridSpace.Rotation(equipment.Rotation);
                var definition = Definition(equipment.Kind);
                var againstWall = EquipmentModel.BackedAgainstWall(definition, site.Snapshot, equipment.SiteId, equipment.CellX, equipment.CellZ, equipment.Rotation);
                var position = SiteGridSpace.Center(site.Layout, equipment) + (definition == null ? Vector3.zero : EquipmentModel.MountOffset(definition, rotation, againstWall));
                if (definition != null && definition.Mount == EquipmentMount.Tabletop) position.y = TableTop(site, equipment, position.y);
                visual.transform.SetPositionAndRotation(position, rotation);
                // A machine with a power switch (decision 0037) shows whether it is switched on; others whether a batch runs.
                visual.SetRunning(definition != null && definition.ManualPower ? equipment.PoweredOn
                    : site.Snapshot.Jobs.Any(x => x.StationId == equipment.Id && x.State == StationJobState.Running));
            }
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var visual in _visuals.Values)
                if (visual != null) Destroy(visual.gameObject);
            _visuals.Clear();
            _placed.Clear();
            _fromAbove.Clear();
            _shown = -1;
        }

        private EquipmentDefinition Definition(string kind) => session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind);

        // The height of the top of the placed table under a tabletop piece (from its drawn model), or the floor without one.
        private float TableTop(DrawnSite site, GoodsEquipment piece, float floor)
        {
            var table = site.Snapshot.Equipment.FirstOrDefault(x => x.SiteId == piece.SiteId && x.State == EquipmentState.Placed && GoodsWorld.IsTable(x)
                && string.IsNullOrEmpty(x.Layer) && SiteGrid.Contains(x, piece.CellX, piece.CellZ, 1, 1));
            if (table == null || !_visuals.TryGetValue(table.Id, out var visual)) return floor;
            var renderers = visual.GetComponentsInChildren<Renderer>();
            return renderers.Length == 0 ? floor : renderers.Max(x => x.bounds.max.y);
        }

        private EquipmentVisual Create(GoodsEquipment equipment)
        {
            var definition = Definition(equipment.Kind);
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
            if (definition.HideFromAbove) _fromAbove[equipment.Id] = root.GetComponentsInChildren<Renderer>(true);
            if (equipment.Level == 0 && string.IsNullOrEmpty(equipment.Layer))
            {
                var obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = new Vector3(equipment.Width * SiteGrid.CellSize, 2f, equipment.Depth * SiteGrid.CellSize);
                obstacle.center = Vector3.up;
                obstacle.carving = true;
            }
            var visual = root.AddComponent<EquipmentVisual>();
            visual.Bind(equipment.Id, equipment.Kind);
            // Far away it is not drawn; the machine keeps running on the server.
            DistanceCulling.Apply(root);
            return visual;
        }
    }
}
