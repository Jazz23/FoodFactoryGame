// Shows building shells from the latest replicated site baseline (decision 0019) and drives the local indoor view.
// Walls get colliders so avatars cannot walk through them; the floor, door lintels and roof do not, so they never catch
// aim rays. Visuals never own state. The local avatar's cell decides "indoors": the rig switches to the top-down view
// and that building's roof hides, for this client only.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.Buildings
{
    [DisallowMultipleComponent]
    public sealed class BuildingPresenter : MonoBehaviour
    {
        [SerializeField] private SessionRoot session;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material roofMaterial;
        [SerializeField] private float wallHeight = 3f;
        [SerializeField] private float doorHeight = 2.2f;

        private readonly Dictionary<string, Shell> _shells = new();
        private GoodsSnapshot _shown;

        private sealed class Shell
        {
            public GameObject Root;
            public Renderer Roof;
        }

        public IReadOnlyCollection<string> Shown => _shells.Keys;
        // The building the local avatar stands inside, or null outdoors or without a baseline.
        public string LocalBuildingId { get; private set; }

        public GameObject ShellOf(string buildingId) => _shells.TryGetValue(buildingId, out var shell) ? shell.Root : null;

        public bool RoofVisible(string buildingId) => _shells.TryGetValue(buildingId, out var shell) && shell.Roof.enabled;

        private void Update()
        {
            var site = session.ClientSite;
            var layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            if (!ReferenceEquals(site, _shown))
            {
                _shown = site;
                Rebuild(site, layout);
            }
            var avatar = LocalAvatar();
            LocalBuildingId = null;
            if (avatar != null && layout != null)
            {
                var (x, z) = SiteGridSpace.AnchorAt(layout, avatar.transform.position, 1, 1);
                LocalBuildingId = site.Buildings.FirstOrDefault(b => b.SiteId == layout.SiteId && SiteGrid.IsInterior(b, x, z))?.Id;
            }
            foreach (var (id, shell) in _shells) shell.Roof.enabled = id != LocalBuildingId;
            if (avatar != null) avatar.CameraRig.SetIndoors(LocalBuildingId != null);
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var shell in _shells.Values)
                if (shell.Root != null) Destroy(shell.Root);
            _shells.Clear();
            _shown = null;
            LocalBuildingId = null;
        }

        // Shells never change after creation, so an existing one is kept and only new or vanished IDs are handled.
        private void Rebuild(GoodsSnapshot site, SiteLayout layout)
        {
            var buildings = layout == null ? new List<GoodsBuilding>() : site.Buildings.Where(x => x.SiteId == layout.SiteId).ToList();
            foreach (var id in _shells.Keys.Where(x => buildings.All(y => y.Id != x)).ToList())
            {
                Destroy(_shells[id].Root);
                _shells.Remove(id);
            }
            foreach (var building in buildings.Where(x => !_shells.ContainsKey(x.Id)))
                _shells.Add(building.Id, Create(layout, building));
        }

        private Shell Create(SiteLayout layout, GoodsBuilding building)
        {
            var root = new GameObject($"Building {building.Id}");
            root.transform.SetParent(transform, false);
            var right = building.CellX + building.Width - 1;
            var top = building.CellZ + building.Depth - 1;
            // The south and north walls span the full width; the west and east walls fill in between the corners.
            WallRuns(layout, building, root.transform, Enumerable.Range(building.CellX, building.Width).Select(x => (x, building.CellZ)), true);
            WallRuns(layout, building, root.transform, Enumerable.Range(building.CellX, building.Width).Select(x => (x, top)), true);
            WallRuns(layout, building, root.transform, Enumerable.Range(building.CellZ + 1, building.Depth - 2).Select(z => (building.CellX, z)), false);
            WallRuns(layout, building, root.transform, Enumerable.Range(building.CellZ + 1, building.Depth - 2).Select(z => (right, z)), false);
            foreach (var door in building.Doors)
            {
                var center = SiteGridSpace.FootprintCenter(layout, door.X, door.Z, 1, 1);
                Box(root.transform, $"Lintel {door.X},{door.Z}", wallMaterial, center + Vector3.up * (doorHeight + wallHeight) * 0.5f,
                    new Vector3(SiteGrid.CellSize, wallHeight - doorHeight, SiteGrid.CellSize), false);
            }
            var floorCenter = SiteGridSpace.FootprintCenter(layout, building.CellX + 1, building.CellZ + 1, building.Width - 2, building.Depth - 2);
            var floor = Box(root.transform, "Floor", floorMaterial, floorCenter + Vector3.up * 0.005f,
                new Vector3((building.Width - 2) * SiteGrid.CellSize, 0.01f, (building.Depth - 2) * SiteGrid.CellSize), false);
            floor.shadowCastingMode = ShadowCastingMode.Off;
            var roofCenter = SiteGridSpace.FootprintCenter(layout, building.CellX, building.CellZ, building.Width, building.Depth);
            var roof = Box(root.transform, "Roof", roofMaterial, roofCenter + Vector3.up * (wallHeight + 0.1f),
                new Vector3(building.Width * SiteGrid.CellSize, 0.2f, building.Depth * SiteGrid.CellSize), false);
            return new Shell { Root = root, Roof = roof };
        }

        // One box per contiguous run of wall cells along a line; doors break the runs.
        private void WallRuns(SiteLayout layout, GoodsBuilding building, Transform parent, IEnumerable<(int X, int Z)> cells, bool alongX)
        {
            var run = new List<(int X, int Z)>();
            foreach (var cell in cells.Append((X: -1, Z: -1)))
            {
                if (cell.X >= 0 && SiteGrid.IsWall(building, cell.X, cell.Z))
                {
                    run.Add(cell);
                    continue;
                }
                if (run.Count == 0) continue;
                var width = alongX ? run.Count : 1;
                var depth = alongX ? 1 : run.Count;
                var center = SiteGridSpace.FootprintCenter(layout, run[0].X, run[0].Z, width, depth);
                Box(parent, $"Wall {run[0].X},{run[0].Z}", wallMaterial, center + Vector3.up * wallHeight * 0.5f,
                    new Vector3(width * SiteGrid.CellSize, wallHeight, depth * SiteGrid.CellSize), true);
                run.Clear();
            }
        }

        private static MeshRenderer Box(Transform parent, string name, Material material, Vector3 position, Vector3 scale, bool solid)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            if (!solid) Destroy(box.GetComponent<BoxCollider>());
            box.transform.SetParent(parent, false);
            box.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
            box.transform.localScale = scale;
            var renderer = box.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        private PlayerAvatar LocalAvatar()
        {
            var manager = session.NetworkManager;
            if (manager == null || !manager.IsClientStarted) return null;
            return manager.ClientManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<PlayerAvatar>()).FirstOrDefault(x => x != null && x.IsOwner);
        }
    }
}
