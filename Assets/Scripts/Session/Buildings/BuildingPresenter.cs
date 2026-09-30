// Shows building shells from the latest replicated baselines of every drawn site (decisions 0019, 0020, 0031) and drives the local indoor view.
// Each storey is its own object: the ground storey has the doorway, floor tint and lintels; every upper storey has a solid
// slab over the interior (so avatars stand on it) and a closed ring of walls. The elevator cell is marked on every storey.
// Walls and slabs get colliders; the floor tint, lintels, markers and roof do not, so they never catch aim rays. Visuals
// never own state. The local avatar's cell and height on any drawn site decide "indoors" and its level: the rig switches to the top-down view,
// and that building's roof and every storey above the avatar's level are hidden (colliders too), for this client only.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.Buildings
{
    [DisallowMultipleComponent]
    public sealed class BuildingPresenter : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private const float SlabThickness = 0.2f;

        [SerializeField] private SessionRoot session;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material roofMaterial;
        [SerializeField] private float doorHeight = 2.2f;
        [SerializeField] private Color elevatorColor = new(0.95f, 0.75f, 0.2f, 1f);

        private readonly Dictionary<string, Shell> _shells = new();
        private int _shown = -1;

        private sealed class Shell
        {
            public GameObject Root;
            public Renderer Roof;
            // Index is the level.
            public List<GameObject> Storeys = new();
            // What the visual was built from; a shell is rebuilt when its floors or elevator change.
            public (int Floors, int ElevatorX, int ElevatorZ) Built;
            public GoodsBuilding Building;
        }

        public IReadOnlyCollection<string> Shown => _shells.Keys;
        // The building the local avatar stands inside (on any drawn site), or null outdoors or without a baseline.
        public string LocalBuildingId { get; private set; }
        // Level the local avatar stands on (0 outdoors), and its cell on the site it stands on.
        public int LocalLevel { get; private set; }
        public (int X, int Z) LocalCell { get; private set; }
        public PlayerAvatar LocalAvatar { get; private set; }
        // The drawn site whose building the avatar is inside, or else the current site.
        public DrawnSite LocalSite { get; private set; }

        public GameObject ShellOf(string buildingId) => _shells.TryGetValue(buildingId, out var shell) ? shell.Root : null;

        public GameObject StoreyOf(string buildingId, int level) =>
            _shells.TryGetValue(buildingId, out var shell) && level >= 0 && level < shell.Storeys.Count ? shell.Storeys[level] : null;

        public bool RoofVisible(string buildingId) => _shells.TryGetValue(buildingId, out var shell) && shell.Roof.enabled;

        public GoodsBuilding LocalBuilding => LocalBuildingId != null && _shells.TryGetValue(LocalBuildingId, out var shell) ? shell.Building : null;
        public SiteLayout Layout => LocalSite?.Layout;

        // True when something at this cell and level of a site is inside the local avatar's building above its level, so this
        // client hides it with the storeys it stands on.
        public bool HidesLevel(string siteId, int cellX, int cellZ, int level)
        {
            var building = LocalBuilding;
            return building != null && level > LocalLevel && building.SiteId == siteId
                && SiteGrid.Overlaps(cellX, cellZ, 1, 1, building.CellX, building.CellZ, building.Width, building.Depth);
        }

        private void Update()
        {
            var drawn = session.DrawnSites;
            if (drawn.Version != _shown)
            {
                _shown = drawn.Version;
                Rebuild(drawn);
            }
            LocalAvatar = FindLocalAvatar();
            LocalBuildingId = null;
            LocalLevel = 0;
            LocalSite = drawn.Sites.FirstOrDefault(x => x.Current);
            if (LocalAvatar != null)
            {
                // Indoors in any drawn shell (decision 0031), not only the current site's; outdoors the current site's grid.
                var position = LocalAvatar.transform.position;
                foreach (var site in drawn.Sites.Where(x => x.Layout != null))
                {
                    var (x, z) = SiteGridSpace.AnchorAt(site.Layout, position, 1, 1);
                    var level = SiteGridSpace.LevelAt(site.Layout, position.y);
                    var inside = site.Snapshot.Buildings.FirstOrDefault(b => b.SiteId == site.SiteId && SiteGrid.IsInterior(b, x, z) && level < b.Floors);
                    if (inside == null && !(site.Current && LocalBuildingId == null)) continue;
                    LocalSite = site;
                    LocalCell = (x, z);
                    LocalLevel = level;
                    LocalBuildingId = inside?.Id;
                    if (inside != null) break;
                }
            }
            foreach (var (id, shell) in _shells)
            {
                var local = id == LocalBuildingId;
                shell.Roof.enabled = !local;
                for (var level = 0; level < shell.Storeys.Count; level++)
                {
                    var shown = !local || level <= LocalLevel;
                    if (shell.Storeys[level].activeSelf != shown) shell.Storeys[level].SetActive(shown);
                }
            }
            if (LocalAvatar != null) LocalAvatar.CameraRig.SetIndoors(LocalBuildingId != null);
            // Other players on a hidden storey are hidden with it.
            var localSite = LocalBuilding == null ? null : drawn.Find(LocalBuilding.SiteId);
            foreach (var avatar in Avatars().Where(x => !x.IsOwner))
            {
                var position = avatar.transform.position;
                var hidden = false;
                if (localSite?.Layout != null)
                {
                    var (x, z) = SiteGridSpace.AnchorAt(localSite.Layout, position, 1, 1);
                    hidden = HidesLevel(localSite.SiteId, x, z, SiteGridSpace.LevelAt(localSite.Layout, position.y));
                }
                avatar.SetHidden(hidden);
            }
        }

        private void OnDisable() => Clear();

        private void Clear()
        {
            foreach (var shell in _shells.Values)
                if (shell.Root != null) Destroy(shell.Root);
            _shells.Clear();
            _shown = -1;
            LocalBuildingId = null;
            LocalLevel = 0;
            LocalSite = null;
        }

        // Every drawn site's shells, each at its site's place in the scene (decision 0031). A shell is kept while its floors and
        // elevator are unchanged, and rebuilt when a floor is added.
        private void Rebuild(DrawnSites drawn)
        {
            var buildings = drawn.Sites.Where(x => x.Layout != null)
                .SelectMany(site => site.Snapshot.Buildings.Where(x => x.SiteId == site.SiteId).Select(x => (Site: site, Building: x))).ToList();
            foreach (var id in _shells.Keys.Where(x => buildings.All(y => y.Building.Id != x || y.Building.Floors != _shells[x].Built.Floors
                         || y.Building.ElevatorX != _shells[x].Built.ElevatorX || y.Building.ElevatorZ != _shells[x].Built.ElevatorZ)).ToList())
            {
                Destroy(_shells[id].Root);
                _shells.Remove(id);
            }
            foreach (var (site, building) in buildings)
            {
                if (!_shells.TryGetValue(building.Id, out var shell)) _shells.Add(building.Id, shell = Create(site.Layout, building));
                shell.Building = building;
            }
        }

        private Shell Create(SiteLayout layout, GoodsBuilding building)
        {
            var root = new GameObject($"Building {building.Id}");
            root.transform.SetParent(transform, false);
            var shell = new Shell { Root = root, Built = (building.Floors, building.ElevatorX, building.ElevatorZ) };
            var interiorCenter = SiteGridSpace.FootprintCenter(layout, building.CellX + 1, building.CellZ + 1, building.Width - 2, building.Depth - 2);
            var interiorSize = new Vector3((building.Width - 2) * SiteGrid.CellSize, 0f, (building.Depth - 2) * SiteGrid.CellSize);
            for (var level = 0; level < building.Floors; level++)
            {
                var storey = new GameObject($"Storey {level}");
                storey.transform.SetParent(root.transform, false);
                var baseHeight = Vector3.up * (level * SiteGridSpace.LevelHeight);
                Walls(layout, building, storey.transform, level);
                if (level == 0)
                {
                    foreach (var door in building.Doors)
                    {
                        var center = SiteGridSpace.FootprintCenter(layout, door.X, door.Z, 1, 1);
                        Box(storey.transform, $"Lintel {door.X},{door.Z}", wallMaterial, center + Vector3.up * (doorHeight + SiteGridSpace.LevelHeight) * 0.5f,
                            new Vector3(SiteGrid.CellSize, SiteGridSpace.LevelHeight - doorHeight, SiteGrid.CellSize), false);
                    }
                    var floor = Box(storey.transform, "Floor", floorMaterial, interiorCenter + Vector3.up * 0.005f, interiorSize + Vector3.up * 0.01f, false);
                    floor.shadowCastingMode = ShadowCastingMode.Off;
                }
                else
                {
                    // The slab's top is this level's floor surface.
                    Box(storey.transform, "Slab", floorMaterial, interiorCenter + baseHeight - Vector3.up * SlabThickness * 0.5f,
                        interiorSize + Vector3.up * SlabThickness, true);
                }
                if (building.HasElevator)
                {
                    var pad = Box(storey.transform, "Elevator", floorMaterial,
                        SiteGridSpace.FootprintCenter(layout, building.ElevatorX, building.ElevatorZ, 1, 1, level) + Vector3.up * 0.02f,
                        new Vector3(SiteGrid.CellSize * 0.9f, 0.02f, SiteGrid.CellSize * 0.9f), false);
                    pad.shadowCastingMode = ShadowCastingMode.Off;
                    var block = new MaterialPropertyBlock();
                    pad.GetPropertyBlock(block);
                    block.SetColor(BaseColor, elevatorColor);
                    pad.SetPropertyBlock(block);
                }
                shell.Storeys.Add(storey);
            }
            var roofCenter = SiteGridSpace.FootprintCenter(layout, building.CellX, building.CellZ, building.Width, building.Depth, building.Floors);
            shell.Roof = Box(root.transform, "Roof", roofMaterial, roofCenter + Vector3.up * 0.1f,
                new Vector3(building.Width * SiteGrid.CellSize, 0.2f, building.Depth * SiteGrid.CellSize), false);
            return shell;
        }

        // The south and north walls span the full width; the west and east walls fill in between the corners.
        private void Walls(SiteLayout layout, GoodsBuilding building, Transform parent, int level)
        {
            var right = building.CellX + building.Width - 1;
            var top = building.CellZ + building.Depth - 1;
            WallRuns(layout, building, parent, level, Enumerable.Range(building.CellX, building.Width).Select(x => (x, building.CellZ)), true);
            WallRuns(layout, building, parent, level, Enumerable.Range(building.CellX, building.Width).Select(x => (x, top)), true);
            WallRuns(layout, building, parent, level, Enumerable.Range(building.CellZ + 1, building.Depth - 2).Select(z => (building.CellX, z)), false);
            WallRuns(layout, building, parent, level, Enumerable.Range(building.CellZ + 1, building.Depth - 2).Select(z => (right, z)), false);
        }

        // One box per contiguous run of wall cells along a line; ground-floor doors break the runs.
        private void WallRuns(SiteLayout layout, GoodsBuilding building, Transform parent, int level, IEnumerable<(int X, int Z)> cells, bool alongX)
        {
            var run = new List<(int X, int Z)>();
            foreach (var cell in cells.Append((X: -1, Z: -1)))
            {
                if (cell.X >= 0 && SiteGrid.IsWall(building, cell.X, cell.Z, level))
                {
                    run.Add(cell);
                    continue;
                }
                if (run.Count == 0) continue;
                var width = alongX ? run.Count : 1;
                var depth = alongX ? 1 : run.Count;
                var center = SiteGridSpace.FootprintCenter(layout, run[0].X, run[0].Z, width, depth, level);
                var wall = Box(parent, $"Wall {run[0].X},{run[0].Z}", wallMaterial, center + Vector3.up * SiteGridSpace.LevelHeight * 0.5f,
                    new Vector3(width * SiteGrid.CellSize, SiteGridSpace.LevelHeight, depth * SiteGrid.CellSize), true);
                // Ground walls carve the baked NavMesh so walking employees use the doorways (the unit cube's scale sizes it).
                if (level == 0) wall.gameObject.AddComponent<NavMeshObstacle>().carving = true;
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

        private PlayerAvatar FindLocalAvatar() => Avatars().FirstOrDefault(x => x.IsOwner);

        private IEnumerable<PlayerAvatar> Avatars()
        {
            var manager = session.NetworkManager;
            if (manager == null || !manager.IsClientStarted) return Enumerable.Empty<PlayerAvatar>();
            return manager.ClientManager.Objects.Spawned.Values.Select(x => x.GetComponent<PlayerAvatar>()).Where(x => x != null);
        }
    }
}
