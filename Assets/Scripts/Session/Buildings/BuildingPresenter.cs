// Shows building shells from the latest replicated baselines of every drawn site (decisions 0019, 0020, 0031) and drives the local indoor view.
// Each storey is its own object: the ground storey has the doorway, floor tint and lintels; every upper storey has a solid
// slab over the interior (so avatars stand on it) and a closed ring of walls. The elevator cell is marked on every storey.
// Walls and slabs get colliders; the floor tint, lintels, markers and roof do not, so they never catch aim rays. Visuals
// never own state. The local avatar's cell and height on any drawn site decide "indoors" and its level: the rig switches to the top-down view,
// and that building's roof and every storey above the avatar's level are hidden (colliders too), for this client only.
// A restaurant's ground storey (decision 0034) is drawn with the restaurant art kit (RestaurantShellModel) when a style catalog
// is set, with one invisible collider and NavMesh obstacle per wall cell; a shell is rebuilt whenever its data changes. A shell
// with free walls (decision 0036) gets its floor tint, roof and ceiling over the cells its walls enclose, not its bounding box.
// Every ground storey has a ceiling under its roof, shown only to a local avatar inside who is not using the top-down view, so
// the room keeps a ceiling in the third-person view while the top-down view still looks in from above.
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
        // Art-kit models for restaurant walls, doors and windows (decision 0034); boxes are drawn without it.
        [SerializeField] private RestaurantStyleCatalog restaurantStyles;

        private readonly Dictionary<string, Shell> _shells = new();
        private int _shown = -1;

        private sealed class Shell
        {
            public GameObject Root;
            // One box, or one per row of a free-walled roof.
            public List<Renderer> Roof = new();
            // Under the ground storey's top; null for multi-storey shells, whose upper storeys are slabs.
            public GameObject Ceiling;
            // Index is the level.
            public List<GameObject> Storeys = new();
            // What the visual was built from; a shell is rebuilt when any of its data changes (floors, elevator, or a restaurant
            // order's walls, doors and windows).
            public string Built;
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

        public bool RoofVisible(string buildingId) => _shells.TryGetValue(buildingId, out var shell) && shell.Roof.Any(x => x.enabled);
        public bool CeilingVisible(string buildingId) => _shells.TryGetValue(buildingId, out var shell) && shell.Ceiling != null && shell.Ceiling.activeSelf;
        // Height of the ceiling over the local avatar's storey, or null outdoors (the camera rig stays under it).
        public float? LocalCeiling { get; private set; }
        public RestaurantStyleCatalog RestaurantStyles => restaurantStyles;
        // Set by build mode (decision 0034) to the site it edits: that site's roofs hide and all its storeys show, so the
        // interior can be seen from above; null otherwise.
        public string EditedSiteId { get; set; }

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
            var topDown = LocalAvatar != null && LocalAvatar.CameraRig.TopDown;
            LocalCeiling = null;
            foreach (var (id, shell) in _shells)
            {
                var local = id == LocalBuildingId;
                var edited = EditedSiteId != null && shell.Building.SiteId == EditedSiteId;
                foreach (var roof in shell.Roof) roof.enabled = !local && !edited;
                if (shell.Ceiling != null)
                {
                    var ceiling = local && !edited && !topDown;
                    if (shell.Ceiling.activeSelf != ceiling) shell.Ceiling.SetActive(ceiling);
                }
                if (local && LocalSite?.Layout != null)
                    LocalCeiling = SiteGridSpace.FloorHeight(LocalSite.Layout, LocalLevel) + SiteGridSpace.LevelHeight;
                for (var level = 0; level < shell.Storeys.Count; level++)
                {
                    var shown = !local || level <= LocalLevel;
                    if (shell.Storeys[level].activeSelf != shown) shell.Storeys[level].SetActive(shown);
                }
            }
            if (LocalAvatar != null) LocalAvatar.CameraRig.SetIndoors(LocalBuildingId != null, LocalCeiling);
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
            foreach (var id in _shells.Keys.Where(x => buildings.All(y => y.Building.Id != x || JsonUtility.ToJson(y.Building) != _shells[x].Built)).ToList())
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
            var shell = new Shell { Root = root, Built = JsonUtility.ToJson(building) };
            var kit = restaurantStyles != null && building.Kind == GoodsWorld.RestaurantKind;
            var interiorCenter = SiteGridSpace.FootprintCenter(layout, building.CellX + 1, building.CellZ + 1, building.Width - 2, building.Depth - 2);
            var interiorSize = new Vector3((building.Width - 2) * SiteGrid.CellSize, 0f, (building.Depth - 2) * SiteGrid.CellSize);
            for (var level = 0; level < building.Floors; level++)
            {
                var storey = new GameObject($"Storey {level}");
                storey.transform.SetParent(root.transform, false);
                var baseHeight = Vector3.up * (level * SiteGridSpace.LevelHeight);
                if (kit && level == 0) KitWalls(layout, building, storey.transform);
                else Walls(layout, building, storey.transform, level);
                if (level == 0)
                {
                    foreach (var door in kit ? new List<GridCell>() : building.Doors)
                    {
                        var center = SiteGridSpace.FootprintCenter(layout, door.X, door.Z, 1, 1);
                        Box(storey.transform, $"Lintel {door.X},{door.Z}", wallMaterial, center + Vector3.up * (doorHeight + SiteGridSpace.LevelHeight) * 0.5f,
                            new Vector3(SiteGrid.CellSize, SiteGridSpace.LevelHeight - doorHeight, SiteGrid.CellSize), false);
                    }
                    if (building.FreeWalls)
                        foreach (var floor in Cover(layout, storey.transform, "Floor", floorMaterial, SiteGrid.InteriorCells(building), 0.005f, 0.01f))
                            floor.shadowCastingMode = ShadowCastingMode.Off;
                    else
                    {
                        var floor = Box(storey.transform, "Floor", floorMaterial, interiorCenter + Vector3.up * 0.005f, interiorSize + Vector3.up * 0.01f, false);
                        floor.shadowCastingMode = ShadowCastingMode.Off;
                    }
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
            if (building.FreeWalls)
            {
                // The roof spans the enclosed cells and the walls around them.
                shell.Roof.AddRange(Cover(layout, root.transform, "Roof", roofMaterial, RoofCells(building), SiteGridSpace.LevelHeight + 0.1f, 0.2f));
            }
            else
            {
                var roofCenter = SiteGridSpace.FootprintCenter(layout, building.CellX, building.CellZ, building.Width, building.Depth, building.Floors);
                shell.Roof.Add(Box(root.transform, "Roof", roofMaterial, roofCenter + Vector3.up * 0.1f,
                    new Vector3(building.Width * SiteGrid.CellSize, 0.2f, building.Depth * SiteGrid.CellSize), false));
            }
            if (building.Floors == 1)
            {
                // A plaster ceiling just under the top of the walls, over the room and the walls around it (thin kit walls stand on
                // their cells' centrelines, so the room's edge cells reach past them).
                shell.Ceiling = new GameObject("Ceiling");
                shell.Ceiling.transform.SetParent(root.transform, false);
                var ceilingCells = building.FreeWalls ? RoofCells(building)
                    : Enumerable.Range(building.CellX, building.Width).SelectMany(x => Enumerable.Range(building.CellZ, building.Depth).Select(z => (X: x, Z: z)));
                Cover(layout, shell.Ceiling.transform, "Ceiling", wallMaterial, ceilingCells, SiteGridSpace.LevelHeight + 0.023f, 0.05f);
                shell.Ceiling.SetActive(false);
            }
            DistanceCulling.Apply(root, DistanceCulling.Shells);
            return shell;
        }

        // Cells a free-walled roof covers: the enclosed cells and every wall cell touching one (diagonals too).
        private static IEnumerable<(int X, int Z)> RoofCells(GoodsBuilding building)
        {
            var inside = new HashSet<(int X, int Z)>(SiteGrid.InteriorCells(building));
            var cells = new HashSet<(int X, int Z)>(inside);
            foreach (var (x, z) in inside)
                for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                    if (SiteGrid.IsPartition(building, x + dx, z + dz)) cells.Add((x + dx, z + dz));
            return cells;
        }

        // Flat boxes over a set of cells, one per run of cells along each row (X), top at height + thickness / 2 above the floor.
        private static List<MeshRenderer> Cover(SiteLayout layout, Transform parent, string name, Material material, IEnumerable<(int X, int Z)> cells,
            float height, float thickness)
        {
            var boxes = new List<MeshRenderer>();
            foreach (var row in cells.GroupBy(c => c.Z))
            {
                var xs = row.Select(c => c.X).OrderBy(x => x).ToList();
                var start = 0;
                for (var index = 1; index <= xs.Count; index++)
                {
                    if (index < xs.Count && xs[index] == xs[index - 1] + 1) continue;
                    var width = xs[index - 1] - xs[start] + 1;
                    var center = SiteGridSpace.FootprintCenter(layout, xs[start], row.Key, width, 1);
                    boxes.Add(Box(parent, $"{name} {xs[start]},{row.Key}", material, center + Vector3.up * height,
                        new Vector3(width * SiteGrid.CellSize, thickness, SiteGrid.CellSize), false));
                    start = index;
                }
            }
            return boxes;
        }

        // Art-kit visuals plus one invisible box collider (a solid wall for avatars and aim rays) and carving NavMesh obstacle per
        // wall cell, interior walls and window cells included; doors stay open.
        private void KitWalls(SiteLayout layout, GoodsBuilding building, Transform parent)
        {
            RestaurantShellModel.Build(parent, layout, building, restaurantStyles);
            var cells = new HashSet<(int X, int Z)>(building.Structures.Where(x => x.Kind == GoodsWorld.PartitionStructure).Select(x => (x.X, x.Z)));
            for (var x = building.CellX; x < building.CellX + building.Width; x++)
            for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
                if (SiteGrid.OnPerimeter(building, x, z)) cells.Add((x, z));
            foreach (var (x, z) in cells.Where(c => SiteGrid.IsWall(building, c.X, c.Z)))
            {
                var center = SiteGridSpace.FootprintCenter(layout, x, z, 1, 1);
                var solid = Box(parent, $"Wall {x},{z}", wallMaterial, center + Vector3.up * SiteGridSpace.LevelHeight * 0.5f,
                    new Vector3(SiteGrid.CellSize, SiteGridSpace.LevelHeight, SiteGrid.CellSize), true);
                solid.enabled = false;
                solid.gameObject.AddComponent<NavMeshObstacle>().carving = true;
            }
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
