// Client presentation of the replicated world layout (decision 0026) with the ArtSource/World models: the land as a chunked
// terrain mesh (the river's channel and banks cut into it), water, road and rail tiles draped over the land, bridges with
// decks, parapets and piers, level crossings with signals, traffic lights and stop signs at controlled junctions, trees, and
// a model per building fitted to its footprint and facing at its recorded elevation, on a foundation where the land falls
// away. It is built only from the layout the server sent through WorldLayoutBridge and is rebuilt when that changes; it
// writes no state, and nothing in the simulation reads it or depends on whether it exists. Tiles, furniture and trees are
// merged into one mesh per material per 250 m chunk and the buildings are static-batched, so a ~1,800-building city with
// ~6,000 trees stays a few hundred draw calls. Format 1 layouts (generator v1) show as flat land with no river or trees.
// Generated worlds (format 3, decisions 0028, 0031) are drawn by SitePlacement, which this presenter activates: the starting
// lot's grid centre stands at the scene origin with its building's ground floor at y = 0, and every lot stands where
// SiteGridSpace puts its site. The land over every lot is levelled to its building's ground floor, so any lot can be drawn
// from site data without terrain showing through, and a restaurant or factory model is hidden while BuildingPresenter draws
// its site's shell (DrawnSites), and shown again when that site stops being drawn.
// Every lot gets a pavement surface and every purchasable building a PropertyMarker, and restaurant awnings are tinted by the
// replicated ownership records (GoodsProperty) before the layout's own for-sale or competitor state. Older formats keep the
// PROTOTYPE placement beside the dev site, whose floor and NavMesh (devSiteOnly) are shown only then.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.WorldMap
{
    [DisallowMultipleComponent]
    public sealed class WorldLayoutPresenter : MonoBehaviour
    {
        [SerializeField] private SessionRoot session;
        [SerializeField] private WorldArtCatalog art;
        // PROTOTYPE placement. How the map relates to the dev site is part of the open site question (decision 0026), so the
        // dev site (DevSite's 40 m floor at the scene origin) sits in the farmland with the city's edge this far north of it,
        // where a player sees the city on arrival. Pieces that would land on the dev site are not drawn, the land around it
        // is levelled to its floor, and the whole map is lowered so that floor stays at y = 0.
        [SerializeField] private float cityGap = 40f;
        [SerializeField] private Vector2 devSiteClearHalfSize = new(24f, 24f);
        [SerializeField] private float devSiteBlend = 30f;
        // The dev site's own ground (DevSite's floor, landmarks and baked NavMesh), shown unless a generated world is.
        [SerializeField] private GameObject[] devSiteOnly = System.Array.Empty<GameObject>();

        // Authored model sizes (ArtSource/World/build_world_models.py): along the street (X) by depth, in metres, and storey
        // heights of the stacked modules. Road and bridge tiles are 10 m long, rail tiles 6 m; all run along local -Z from 0.
        private static readonly Dictionary<string, Vector2> Footprints = new()
        {
            ["House_Small"] = new Vector2(10f, 9f), ["House_Large"] = new Vector2(15f, 12f), ["Apartment"] = new Vector2(18f, 15f),
            ["Office"] = new Vector2(22f, 19f), ["Restaurant"] = new Vector2(12f, 11f), ["Factory"] = new Vector2(30f, 26f),
            ["Station"] = new Vector2(10f, 30f)
        };
        private static readonly Dictionary<string, float> Heights = new()
        {
            ["House_Small"] = 6.4f, ["House_Large"] = 8.6f, ["Restaurant"] = 5.3f, ["Factory"] = 10.5f, ["Station"] = 1f
        };
        // Every catalog piece this presenter can ask for (checked by an authoring test).
        public static readonly string[] RequiredPieces =
        {
            "Road_Junction", "Road_Arterial", "Road_Arterial_Crosswalk", "Road_Local", "Road_Local_Crosswalk", "Road_Rural",
            "Rail_Track", "Rail_Crossing", "Rail_Signal", "Stop_Sign", "Traffic_Light_Arterial", "Traffic_Light_Local",
            "Bridge_Railing", "Bridge_Deck", "Bridge_Pier", "Foundation",
            "Tree_Broadleaf_a", "Tree_Broadleaf_b", "Tree_Conifer", "Tree_Poplar", "Tree_Bush",
            "House_Small_a", "House_Small_b", "House_Small_c", "House_Large_a", "House_Large_b", "House_Large_c",
            "Apartment_a_Ground", "Apartment_a_Middle", "Apartment_a_Roof", "Apartment_b_Ground", "Apartment_b_Middle", "Apartment_b_Roof",
            "Office_a_Ground", "Office_a_Middle", "Office_a_Roof", "Office_b_Ground", "Office_b_Middle", "Office_b_Roof",
            "Restaurant_a", "Restaurant_b", "Factory_a", "Factory_b", "Barn_a", "Barn_b", "Station"
        };
        private const float RoadTileLength = 10f;
        private const float RailTileLength = 6f;
        private const float BridgeTileLength = 10f;
        private const float ApartmentStorey = 3f;
        private const float OfficeStorey = 3.5f;
        // Terrain mesh resolution and chunking; merged pieces are chunked the same way for culling.
        private const float TerrainCell = 5f;
        private const int TerrainChunkCells = 76;
        private const float ChunkSize = 250f;
        // The land is lowered this far under roads and rails so it never shows through them; their skirts hide the step.
        private const float RoadCarve = 0.15f;
        // River channel: bed depth below the water surface and the width of the sloping bank beyond the water's edge.
        private const float RiverBedBelowSurface = 1.6f;
        private const float RiverBankWidth = 10f;
        private const float TextureMetres = 8f;
        private const float FieldMetres = 12f;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Awning colours: the player's restaurant, restaurants for sale, then competitors by ID.
        private static readonly Color PlayerAwning = new(0.18f, 0.62f, 0.26f);
        private static readonly Color ForSaleAwning = new(0.95f, 0.75f, 0.15f);
        private static readonly Color[] CompetitorAwnings =
        {
            new(0.72f, 0.14f, 0.12f), new(0.16f, 0.3f, 0.6f), new(0.45f, 0.18f, 0.5f), new(0.12f, 0.45f, 0.45f)
        };

        private enum Drape
        {
            // Vertices keep the given height.
            None,
            // The whole piece moves up by the land height at its origin (signs, trees).
            Rigid,
            // Every vertex moves up by the land height under it (tiles, bridge parts).
            Surface
        }

        private sealed class MeshBuffer
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Triangles = new();
        }

        private sealed class PieceData
        {
            public Vector3[] Vertices;
            public Vector2[] Uvs;
            public int[][] Triangles;
        }

        private WorldLayoutBridge _bridge;
        private Transform _root;
        private Vector3 _layoutOrigin;
        private Rect _clear;
        private MaterialPropertyBlock _block;
        private WorldTerrain _terrain;
        private float _devHeight;
        private bool _generated;
        // PROTOTYPE (decision 0031): the land around every lot of a generated world blends back to the terrain over this many
        // metres; lots are indexed in square map cells of LotBucket metres.
        private const float LotBlend = 8f;
        private const float LotBucket = 64f;
        private readonly Dictionary<(int X, int Z), List<(Rect Rect, float Elevation)>> _lotBuckets = new();
        // Restaurant and factory models by building ID; each is hidden while its site is drawn from site data.
        private readonly Dictionary<string, GameObject> _shellModels = new();
        private int _hiddenFor = -1;
        // Restaurant awnings by building ID, re-tinted when a new baseline brings ownership records.
        private readonly Dictionary<string, (MeshRenderer Renderer, int Index, WorldBuilding Building)> _awnings = new();
        private readonly Dictionary<string, Color> _awningColours = new();
        private GoodsSnapshot _tintedFrom;
        private Dictionary<string, PropertyOffer> _offers = new();
        private readonly List<Mesh> _meshes = new();
        private readonly Dictionary<(Material Material, int X, int Z, bool Collide), MeshBuffer> _buffers = new();
        private readonly Dictionary<Mesh, PieceData> _pieceData = new();

        public WorldLayout Shown { get; private set; }
        // Where the layout's origin (the city centre, at land height) stands in the scene while a layout is shown.
        public Vector3 LayoutOrigin => _layoutOrigin;
        public int BuildingCount { get; private set; }
        // True while a generated world's layout (with lots) is shown around the starting lot.
        public bool Generated => Shown != null && _generated;
        // The property catalog derived from the shown layout (the same WorldLayoutShells call the server makes), by lot ID.
        public IReadOnlyDictionary<string, PropertyOffer> Offers => _offers;

        // The scene position of a layout point (metres) at the given layout-local height.
        public Vector3 ScenePoint(float x, float z, float height = 0f) => _layoutOrigin + new Vector3(x, height, z);

        // The awning colour currently applied to a restaurant's model; null when it has none drawn.
        public Color? AwningColour(string buildingId) => _awningColours.TryGetValue(buildingId, out var colour) ? colour : null;

        private void Update()
        {
            var manager = session.NetworkManager;
            if (_bridge == null && manager != null && manager.IsClientStarted)
                _bridge = manager.ClientManager.Objects.Spawned.Values.Select(x => x.GetComponent<WorldLayoutBridge>()).FirstOrDefault(x => x != null);
            var layout = _bridge != null && _bridge.IsClientStarted ? _bridge.Layout : null;
            if (layout != Shown) Show(layout);
            if (Shown != null) ExtendCameraRange(Shown);
            var site = session.ClientSite;
            if (Shown != null && !ReferenceEquals(site, _tintedFrom))
            {
                _tintedFrom = site;
                foreach (var awning in _awnings.Values) TintAwning(awning.Renderer, awning.Index, awning.Building, site);
            }
            var drawn = session.DrawnSites;
            if (Shown != null && drawn.Version != _hiddenFor)
            {
                _hiddenFor = drawn.Version;
                // A drawn site's shell comes from its site data (BuildingPresenter), so the map's own model steps aside; it
                // returns when the site stops being drawn.
                var fromSites = new HashSet<string>(drawn.Sites.SelectMany(x => x.Snapshot.Buildings.Where(b => b.SiteId == x.SiteId).Select(b => b.Id)));
                foreach (var (id, model) in _shellModels)
                    if (model.activeSelf == fromSites.Contains(id)) model.SetActive(!fromSites.Contains(id));
            }
        }

        // True while the map's own model of a restaurant or factory is drawn (false while its site draws the shell instead).
        public bool ModelShown(string buildingId) => _shellModels.TryGetValue(buildingId, out var model) && model.activeSelf;

        // PROTOTYPE presentation: the map is about 2 km across and starts beside the dev site, past the player camera's
        // 1 km far plane, so while a layout is shown every local camera draws far enough to see all of it.
        private void ExtendCameraRange(WorldLayout layout)
        {
            var needed = new Vector2(_layoutOrigin.x, _layoutOrigin.z).magnitude + layout.MapHalfSize * 1.5f;
            foreach (var camera in Camera.allCameras)
                if (camera.farClipPlane < needed) camera.farClipPlane = needed;
        }

        private void OnDestroy() => Clear();

        private void Clear()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            foreach (var mesh in _meshes) Destroy(mesh);
            _meshes.Clear();
            _buffers.Clear();
            _pieceData.Clear();
            _awnings.Clear();
            _awningColours.Clear();
            _tintedFrom = null;
            _lotBuckets.Clear();
            _shellModels.Clear();
            _hiddenFor = -1;
            SitePlacement.Use(null);
            BuildingCount = 0;
        }

        private void Show(WorldLayout layout)
        {
            Clear();
            Shown = layout;
            var start = layout?.Lots.FirstOrDefault(x => x.BuildingId == layout.StartRestaurantId);
            _generated = layout != null && layout.FormatVersion >= GeneratedWorld.FirstFormat && start != null;
            foreach (var item in devSiteOnly)
                if (item != null && item.activeSelf == _generated) item.SetActive(!_generated);
            _offers = layout == null ? new Dictionary<string, PropertyOffer>()
                : WorldLayoutShells.PropertyOffers(layout).ToDictionary(x => x.LotId);
            if (layout == null) return;
            _block ??= new MaterialPropertyBlock();
            _terrain = layout.Terrain ?? WorldTerrain.Flat();
            if (_generated)
            {
                // The starting lot's grid centre goes to the scene origin and its building's ground floor to y = 0, and every
                // other lot stands where SitePlacement puts its site (decision 0031). Every lot is levelled to its building.
                var placement = SitePlacement.For(layout);
                SitePlacement.Use(placement);
                _layoutOrigin = placement.LayoutOrigin;
                IndexLots(layout);
                placement.Ground = Land;
            }
            else
            {
                var planar = new Vector3(0f, 0f, devSiteClearHalfSize.y + cityGap + layout.CityHalfSize);
                // The dev site's floor in layout-local coordinates, and the land height the map is levelled to there.
                _clear = new Rect(-devSiteClearHalfSize.x - planar.x, -devSiteClearHalfSize.y - planar.z, 2f * devSiteClearHalfSize.x, 2f * devSiteClearHalfSize.y);
                _devHeight = _terrain.Height(_clear.center.x, _clear.center.y);
                _layoutOrigin = planar + Vector3.down * _devHeight;
            }
            _root = new GameObject("World Layout").transform;
            _root.SetParent(transform, false);
            _root.position = _layoutOrigin;

            Terrain(layout);
            Water(layout);
            Pavement(layout);
            Fields(layout);
            var nodes = layout.Nodes.ToDictionary(x => x.Id);
            Roads(layout, nodes);
            Bridges(layout, nodes);
            Rails(layout, nodes);
            Junctions(layout, nodes);
            Trees(layout);
            var districts = layout.Districts.ToDictionary(x => x.Id);
            foreach (var building in layout.Buildings)
            {
                if (!_generated && Overlaps(building.X, building.Z, building.Width, building.Depth)) continue;
                Building(building, districts.TryGetValue(building.DistrictId ?? "", out var district) ? district.Kind : null);
                BuildingCount++;
            }
            Flush();
            StaticBatchingUtility.Combine(_root.gameObject);
        }

        // ------------------------------------------------------------------ land

        // Every lot of a generated world by LotBucket-sized map cell, reaching LotBlend past each lot, for Land and LotAt.
        private void IndexLots(WorldLayout layout)
        {
            var elevations = layout.Buildings.ToDictionary(x => x.Id, x => x.ElevationCm / 100f);
            foreach (var lot in layout.Lots)
            {
                var entry = (Rect: new Rect(lot.X, lot.Z, lot.Width, lot.Depth), Elevation: elevations[lot.BuildingId]);
                for (var i = Mathf.FloorToInt((lot.X - LotBlend) / LotBucket); i <= Mathf.FloorToInt((lot.X + lot.Width + LotBlend) / LotBucket); i++)
                for (var j = Mathf.FloorToInt((lot.Z - LotBlend) / LotBucket); j <= Mathf.FloorToInt((lot.Z + lot.Depth + LotBlend) / LotBucket); j++)
                {
                    if (!_lotBuckets.TryGetValue((i, j), out var list)) _lotBuckets[(i, j)] = list = new List<(Rect, float)>();
                    list.Add(entry);
                }
            }
        }

        // The nearest lot's elevation and how far outside it a point lies (0 inside); false when none is within LotBlend.
        private bool NearestLot(float x, float z, out float elevation, out float outside)
        {
            elevation = 0f;
            outside = float.MaxValue;
            if (!_lotBuckets.TryGetValue((Mathf.FloorToInt(x / LotBucket), Mathf.FloorToInt(z / LotBucket)), out var list)) return false;
            foreach (var (rect, height) in list)
            {
                var distance = Mathf.Max(Mathf.Max(0f, Mathf.Max(rect.xMin - x, x - rect.xMax)), Mathf.Max(0f, Mathf.Max(rect.yMin - z, z - rect.yMax)));
                if (distance >= outside) continue;
                outside = distance;
                elevation = height;
            }
            return outside < LotBlend;
        }

        private bool OnAnyLot(Rect footprint)
        {
            if (!_lotBuckets.TryGetValue((Mathf.FloorToInt(footprint.center.x / LotBucket), Mathf.FloorToInt(footprint.center.y / LotBucket)), out var list))
                return false;
            return list.Any(x => x.Rect.Overlaps(footprint));
        }

        // Land height (layout-local metres): in a generated world every lot is levelled to its building's ground floor, blending
        // back to the terrain within LotBlend; otherwise the ground around the dev site is levelled to its floor.
        private float Land(float x, float z)
        {
            var raw = _terrain.Height(x, z);
            if (_generated)
            {
                if (!NearestLot(x, z, out var elevation, out var fromLot)) return raw;
                return Mathf.Lerp(elevation, raw, Mathf.SmoothStep(0f, 1f, fromLot / LotBlend));
            }
            var dx = Mathf.Max(0f, Mathf.Abs(x - _clear.center.x) - _clear.width / 2f);
            var dz = Mathf.Max(0f, Mathf.Abs(z - _clear.center.y) - _clear.height / 2f);
            var outside = Mathf.Max(dx, dz);
            if (outside >= devSiteBlend) return raw;
            var t = Mathf.SmoothStep(0f, 1f, outside / devSiteBlend);
            return Mathf.Lerp(_devHeight, raw, t);
        }

        // Distance from a point to the nearest river's centreline and that river (null when there is none).
        private (float Distance, WorldRiver River) NearestRiver(WorldLayout layout, float x, float z)
        {
            var best = float.MaxValue;
            WorldRiver found = null;
            foreach (var river in layout.Rivers)
            {
                var reach = river.Width / 2f + RiverBankWidth + 20f;
                for (var index = 0; index + 1 < river.Points.Count; index++)
                {
                    var a = river.Points[index];
                    var b = river.Points[index + 1];
                    if (x < Mathf.Min(a.X, b.X) - reach || x > Mathf.Max(a.X, b.X) + reach || z < Mathf.Min(a.Z, b.Z) - reach
                        || z > Mathf.Max(a.Z, b.Z) + reach) continue;
                    var distance = SegmentDistance(new Vector2(x, z), new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));
                    if (distance >= best) continue;
                    best = distance;
                    found = river;
                }
            }
            return (best, found);
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        // How far the river's channel cuts below the land at a point.
        private float Carve(WorldLayout layout, float x, float z)
        {
            var (distance, river) = NearestRiver(layout, x, z);
            if (river == null) return 0f;
            var half = river.Width / 2f;
            var bed = river.SurfaceDropCm / 100f + RiverBedBelowSurface;
            if (distance <= half) return bed;
            if (distance >= half + RiverBankWidth) return 0f;
            return bed * (1f - Mathf.SmoothStep(0f, 1f, (distance - half) / RiverBankWidth));
        }

        // The land as a grid of TerrainCell squares in chunks: grass, downtown paving, industrial yard and river bank by cell,
        // cut down under roads and rails and into the river's channel. Chunks carry mesh colliders, so the land is walkable.
        private void Terrain(WorldLayout layout)
        {
            var half = layout.MapHalfSize;
            var cells = Mathf.RoundToInt(2f * half / TerrainCell);
            var size = cells + 1;
            var heights = new float[size * size];
            var carved = new bool[size * size];
            // Roads and rails (with a margin) carve the land a little so it never shows through them.
            var nodes = layout.Nodes.ToDictionary(x => x.Id);
            void Mark(float x0, float z0, float x1, float z1)
            {
                var i0 = Mathf.Max(0, Mathf.CeilToInt((x0 + half) / TerrainCell));
                var i1 = Mathf.Min(cells, Mathf.FloorToInt((x1 + half) / TerrainCell));
                var j0 = Mathf.Max(0, Mathf.CeilToInt((z0 + half) / TerrainCell));
                var j1 = Mathf.Min(cells, Mathf.FloorToInt((z1 + half) / TerrainCell));
                for (var j = j0; j <= j1; j++)
                for (var i = i0; i <= i1; i++)
                    carved[j * size + i] = true;
            }
            foreach (var road in layout.Roads)
            {
                var a = nodes[road.FromId];
                var b = nodes[road.ToId];
                var w = road.Width / 2f + 0.8f;
                Mark(Mathf.Min(a.X, b.X) - w, Mathf.Min(a.Z, b.Z) - w, Mathf.Max(a.X, b.X) + w, Mathf.Max(a.Z, b.Z) + w);
            }
            foreach (var line in layout.Rails)
                for (var index = 0; index + 1 < line.Points.Count; index++)
                {
                    var a = line.Points[index];
                    var b = line.Points[index + 1];
                    const float w = 2.8f;
                    Mark(Mathf.Min(a.X, b.X) - w, Mathf.Min(a.Z, b.Z) - w, Mathf.Max(a.X, b.X) + w, Mathf.Max(a.Z, b.Z) + w);
                }
            for (var j = 0; j < size; j++)
            for (var i = 0; i < size; i++)
            {
                var x = -half + i * TerrainCell;
                var z = -half + j * TerrainCell;
                var h = Land(x, z) - Carve(layout, x, z);
                if (carved[j * size + i]) h -= RoadCarve;
                // Keep the land just under each lot's floor in a generated world, or the dev site's own floor.
                if (_generated)
                {
                    if (NearestLot(x, z, out var floor, out var outside) && outside == 0f) h = floor - 0.05f;
                }
                else if (_clear.Contains(new Vector2(x, z))) h = _devHeight - 0.05f;
                heights[j * size + i] = h;
            }
            // Cover per cell, by the cell's centre.
            var covers = new Material[cells * cells];
            var districtCover = layout.Districts
                .Select(d => (Areas: d.Areas, Cover: d.Kind == DistrictKind.Downtown ? art.paving : d.Kind == DistrictKind.Industrial ? art.yard : null))
                .Where(x => x.Cover != null).ToList();
            for (var j = 0; j < cells; j++)
            for (var i = 0; i < cells; i++)
            {
                var x = -half + (i + 0.5f) * TerrainCell;
                var z = -half + (j + 0.5f) * TerrainCell;
                var cover = art.grass;
                foreach (var (areas, material) in districtCover)
                    if (areas.Any(a => x >= a.X && x < a.X + a.Width && z >= a.Z && z < a.Z + a.Depth)) cover = material;
                if (art.bank != null)
                {
                    var (distance, river) = NearestRiver(layout, x, z);
                    if (river != null && distance < river.Width / 2f + RiverBankWidth + 1f) cover = art.bank;
                }
                covers[j * cells + i] = cover;
            }
            for (var cj = 0; cj < cells; cj += TerrainChunkCells)
            for (var ci = 0; ci < cells; ci += TerrainChunkCells)
                TerrainChunk(half, size, heights, covers, cells, ci, cj, Mathf.Min(TerrainChunkCells, cells - ci), Mathf.Min(TerrainChunkCells, cells - cj));
        }

        private void TerrainChunk(float half, int size, float[] heights, Material[] covers, int cells, int ci, int cj, int ni, int nj)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            for (var j = 0; j <= nj; j++)
            for (var i = 0; i <= ni; i++)
            {
                var x = -half + (ci + i) * TerrainCell;
                var z = -half + (cj + j) * TerrainCell;
                vertices.Add(new Vector3(x, heights[(cj + j) * size + ci + i], z));
                uvs.Add(new Vector2(x / TextureMetres, z / TextureMetres));
            }
            var bySubmesh = new Dictionary<Material, List<int>>();
            for (var j = 0; j < nj; j++)
            for (var i = 0; i < ni; i++)
            {
                var cover = covers[(cj + j) * cells + ci + i];
                if (!bySubmesh.TryGetValue(cover, out var list)) bySubmesh[cover] = list = new List<int>();
                var a = j * (ni + 1) + i;
                var b = a + 1;
                var c = a + ni + 1;
                var d = c + 1;
                list.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = $"Land {ci}-{cj}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            var materials = bySubmesh.Keys.ToArray();
            mesh.subMeshCount = materials.Length;
            for (var sub = 0; sub < materials.Length; sub++) mesh.SetTriangles(bySubmesh[materials[sub]], sub);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);
            var land = Place($"Land {ci}-{cj}", mesh, materials, _root, Vector3.zero, Quaternion.identity, Vector3.one);
            land.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // Each river's water: a ribbon along its centreline at the surface level, reaching under the banks.
        private void Water(WorldLayout layout)
        {
            if (art.water == null) return;
            foreach (var river in layout.Rivers)
            {
                var points = river.Points.Select(p => new Vector2(p.X, p.Z)).ToList();
                var reach = river.Width / 2f + RiverBankWidth * 0.7f;
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                var along = 0f;
                for (var index = 0; index < points.Count; index++)
                {
                    var previous = points[Mathf.Max(0, index - 1)];
                    var next = points[Mathf.Min(points.Count - 1, index + 1)];
                    var tangent = (next - previous).normalized;
                    var side = new Vector2(-tangent.y, tangent.x) * reach;
                    if (index > 0) along += Vector2.Distance(points[index - 1], points[index]);
                    var level = Land(points[index].x, points[index].y) - river.SurfaceDropCm / 100f;
                    var left = points[index] + side;
                    var right = points[index] - side;
                    vertices.Add(new Vector3(left.x, level, left.y));
                    vertices.Add(new Vector3(right.x, level, right.y));
                    uvs.Add(new Vector2(0f, along / TextureMetres));
                    uvs.Add(new Vector2(2f * reach / TextureMetres, along / TextureMetres));
                    if (index == 0) continue;
                    var a = 2 * (index - 1);
                    triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
                var mesh = new Mesh { name = $"Water {river.Id}", indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                // Wound either way depending on the river's direction: face the water up.
                if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
                {
                    for (var t = 0; t < triangles.Count; t += 3) (triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);
                    mesh.SetTriangles(triangles, 0);
                    mesh.RecalculateNormals();
                }
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                _meshes.Add(mesh);
                Place($"Water {river.Id}", mesh, new[] { art.water }, _root, Vector3.zero, Quaternion.identity, Vector3.one);
            }
        }

        // A pavement surface over every lot (format 3), draped on the land just above it and under farm fields.
        private void Pavement(WorldLayout layout)
        {
            if (art.paving == null) return;
            foreach (var lot in layout.Lots)
            {
                var xs = Grid(lot.X, lot.X + lot.Width);
                var zs = Grid(lot.Z, lot.Z + lot.Depth);
                // A lot's pavement stays just under its site's floor (its building's ground floor), below floor tints and ghosts.
                const float lift = -0.02f;
                var buffer = Buffer(art.paving, lot.X + lot.Width / 2f, lot.Z + lot.Depth / 2f, false);
                var first = buffer.Vertices.Count;
                foreach (var z in zs)
                foreach (var x in xs)
                {
                    buffer.Vertices.Add(new Vector3(x, Land(x, z) + lift, z));
                    buffer.Uvs.Add(new Vector2(x / TextureMetres, z / TextureMetres));
                }
                for (var j = 0; j + 1 < zs.Count; j++)
                for (var i = 0; i + 1 < xs.Count; i++)
                {
                    var a = first + j * xs.Count + i;
                    var c = a + xs.Count;
                    buffer.Triangles.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
                }
            }
        }

        // Crop fields over every farm plot, draped on the land just above it.
        private void Fields(WorldLayout layout)
        {
            foreach (var farm in layout.Buildings.Where(x => x.Category == BuildingCategory.Farm))
            {
                if (!_generated && Overlaps(farm.X, farm.Z, farm.Width, farm.Depth)) continue;
                var material = Variant(farm) == "a" ? art.wheat : art.greens;
                var xs = Grid(farm.X, farm.X + farm.Width);
                var zs = Grid(farm.Z, farm.Z + farm.Depth);
                var buffer = Buffer(material, farm.X + farm.Width / 2f, farm.Z + farm.Depth / 2f, false);
                var first = buffer.Vertices.Count;
                foreach (var z in zs)
                foreach (var x in xs)
                {
                    buffer.Vertices.Add(new Vector3(x, Land(x, z) + 0.05f, z));
                    buffer.Uvs.Add(new Vector2(x / FieldMetres, z / FieldMetres));
                }
                for (var j = 0; j + 1 < zs.Count; j++)
                for (var i = 0; i + 1 < xs.Count; i++)
                {
                    var a = first + j * xs.Count + i;
                    var c = a + xs.Count;
                    buffer.Triangles.AddRange(new[] { a, c, a + 1, a + 1, c, c + 1 });
                }
            }
        }

        // Coordinates from `from` to `to` through every terrain grid line between them, so draped patches meet the land exactly.
        private static List<float> Grid(float from, float to)
        {
            var result = new List<float> { from };
            for (var value = Mathf.Floor(from / TerrainCell + 1f) * TerrainCell; value < to - 0.01f; value += TerrainCell)
                if (value > from + 0.01f) result.Add(value);
            result.Add(to);
            return result;
        }

        // ------------------------------------------------------------------ roads, bridges and rail

        private void Roads(WorldLayout layout, Dictionary<string, RoadNode> nodes)
        {
            // Half widths of the roads meeting at each node in each direction: a road along X is trimmed at its ends by the
            // half width of the roads along Z there, and the junction patch covers the crossing of both.
            var alongZHalf = new Dictionary<string, float>();
            var alongXHalf = new Dictionary<string, float>();
            foreach (var road in layout.Roads)
            {
                var alongZ = nodes[road.FromId].X == nodes[road.ToId].X;
                var halves = alongZ ? alongZHalf : alongXHalf;
                foreach (var id in new[] { road.FromId, road.ToId })
                    halves[id] = Mathf.Max(halves.TryGetValue(id, out var half) ? half : 0f, road.Width / 2f);
            }
            var junction = art.Get("Road_Junction");
            foreach (var node in layout.Nodes)
            {
                if (!alongZHalf.TryGetValue(node.Id, out var x) || !alongXHalf.TryGetValue(node.Id, out var z)) continue;
                Merge(junction, Matrix4x4.TRS(new Vector3(node.X, 0f, node.Z), Quaternion.identity, new Vector3(2f * x, 1f, 2f * z)),
                    Drape.Surface, true, new Rect(node.X - x, node.Z - z, 2f * x, 2f * z));
            }
            foreach (var road in layout.Roads)
            {
                var a = nodes[road.FromId];
                var b = nodes[road.ToId];
                var alongZ = a.X == b.X;
                var crossing = alongZ ? alongXHalf : alongZHalf;
                var trimA = crossing.TryGetValue(a.Id, out var ta) ? ta : 0f;
                var trimB = crossing.TryGetValue(b.Id, out var tb) ? tb : 0f;
                var start = new Vector3(a.X, 0f, a.Z);
                var end = new Vector3(b.X, 0f, b.Z);
                var direction = (end - start).normalized;
                var length = Vector3.Distance(start, end) - trimA - trimB;
                if (length < 0.5f) continue;
                var kind = road.Kind.ToString();
                var plain = art.Get($"Road_{kind}");
                var city = road.Kind != RoadKind.Rural;
                var crosswalk = city ? art.Get($"Road_{kind}_Crosswalk") : null;
                var count = Mathf.Max(1, Mathf.RoundToInt(length / RoadTileLength));
                var tile = length / count;
                for (var index = 0; index < count; index++)
                {
                    var from = start + direction * (trimA + index * tile);
                    var tileDirection = direction;
                    var piece = plain;
                    // The zebra end of a crosswalk tile (its start) meets the junction.
                    if (city && index == 0 && trimA > 0f) piece = crosswalk;
                    else if (city && index == count - 1 && trimB > 0f)
                    {
                        piece = crosswalk;
                        from += direction * tile;
                        tileDirection = -direction;
                    }
                    var centre = from + tileDirection * (tile / 2f);
                    Merge(piece, Matrix4x4.TRS(from, Quaternion.LookRotation(-tileDirection), new Vector3(1f, 1f, tile / RoadTileLength)),
                        Drape.Surface, true, Footprint(centre, alongZ ? road.Width : tile, alongZ ? tile : road.Width));
                }
            }
        }

        // Every bridge: a deck slab under the carried way, parapets along both edges, and a pier at each water crossing.
        private void Bridges(WorldLayout layout, Dictionary<string, RoadNode> nodes)
        {
            var widths = layout.Roads.ToDictionary(x => x.Id, x => (float)x.Width);
            foreach (var line in layout.Rails) widths[line.Id] = line.Width - 3f;
            var deck = art.Get("Bridge_Deck");
            var railing = art.Get("Bridge_Railing");
            var pier = art.Get("Bridge_Pier");
            var rivers = layout.Rivers.ToDictionary(x => x.Id);
            foreach (var bridge in layout.Bridges)
            {
                if (!widths.TryGetValue(bridge.CarriesId, out var width)) continue;
                var start = new Vector3(bridge.From.X, 0f, bridge.From.Z);
                var end = new Vector3(bridge.To.X, 0f, bridge.To.Z);
                var direction = (end - start).normalized;
                var length = Vector3.Distance(start, end);
                if (length < 1f) continue;
                var side = Vector3.Cross(Vector3.up, direction);
                var rotation = Quaternion.LookRotation(-direction);
                var count = Mathf.Max(1, Mathf.RoundToInt(length / BridgeTileLength));
                var tile = length / count;
                for (var index = 0; index < count; index++)
                {
                    var from = start + direction * (index * tile);
                    var footprint = Footprint(from + direction * (tile / 2f), Mathf.Abs(direction.x) > 0.5f ? tile : width, Mathf.Abs(direction.x) > 0.5f ? width : tile);
                    Merge(deck, Matrix4x4.TRS(from, rotation, new Vector3(width, 1f, tile / BridgeTileLength)), Drape.Surface, true, footprint);
                    foreach (var sign in new[] { -1f, 1f })
                        Merge(railing, Matrix4x4.TRS(from + side * (sign * (width / 2f + 0.15f)), rotation, new Vector3(1f, 1f, tile / BridgeTileLength)),
                            Drape.Surface, true, footprint);
                }
                // Piers stand where the carried way crosses the water, down to the river bed.
                if (!rivers.ContainsKey(bridge.RiverId)) continue;
                var middle = (start + end) / 2f;
                var top = Land(middle.x, middle.z) - 1.3f;
                var bottom = Land(middle.x, middle.z) - Carve(layout, middle.x, middle.z) - 0.5f;
                if (top - bottom < 0.5f) continue;
                Merge(pier, Matrix4x4.TRS(new Vector3(middle.x, top, middle.z), Quaternion.LookRotation(direction), new Vector3(width, top - bottom, 1f)),
                    Drape.None, false, Footprint(middle, 2f, 2f));
            }
        }

        // Rail track, with level crossing panels where a road crosses at grade (and no ballast on the road there).
        private void Rails(WorldLayout layout, Dictionary<string, RoadNode> nodes)
        {
            var track = art.Get("Rail_Track");
            var panel = art.Get("Rail_Crossing");
            var roads = layout.Roads.ToDictionary(x => x.Id);
            var crossings = layout.Crossings.Where(x => roads.ContainsKey(x.RoadId)).ToList();
            foreach (var line in layout.Rails)
                for (var index = 0; index + 1 < line.Points.Count; index++)
                {
                    var start = new Vector3(line.Points[index].X, 0f, line.Points[index].Z);
                    var end = new Vector3(line.Points[index + 1].X, 0f, line.Points[index + 1].Z);
                    var direction = (end - start).normalized;
                    var length = Vector3.Distance(start, end);
                    var alongZ = Mathf.Abs(direction.z) > 0.5f;
                    // Crossings on this piece as (distance along it, road half width), in order; track is tiled in the gaps.
                    var here = crossings.Where(x => x.RailId == line.Id)
                        .Select(x => (Crossing: x, At: Vector3.Dot(new Vector3(x.X, 0f, x.Z) - start, direction), Half: roads[x.RoadId].Width / 2f + 0.2f))
                        .Where(x => x.At > 0f && x.At < length).OrderBy(x => x.At).ToList();
                    var gapStart = 0f;
                    for (var k = 0; k <= here.Count; k++)
                    {
                        var gapEnd = k < here.Count ? here[k].At - here[k].Half : length;
                        var gap = gapEnd - gapStart;
                        if (gap > 0.5f)
                        {
                            var count = Mathf.Max(1, Mathf.RoundToInt(gap / RailTileLength));
                            var tile = gap / count;
                            for (var step = 0; step < count; step++)
                            {
                                var from = start + direction * (gapStart + step * tile);
                                Merge(track, Matrix4x4.TRS(from, Quaternion.LookRotation(-direction), new Vector3(1f, 1f, tile / RailTileLength)),
                                    Drape.Surface, true, Footprint(from + direction * (tile / 2f), alongZ ? line.Width : tile, alongZ ? tile : line.Width));
                            }
                        }
                        if (k < here.Count) gapStart = here[k].At + here[k].Half;
                    }
                    // The panel spans the road's full width, sidewalks included.
                    foreach (var (crossing, at, half) in here)
                    {
                        var from = start + direction * (at - half);
                        Merge(panel, Matrix4x4.TRS(from, Quaternion.LookRotation(-direction), new Vector3(1f, 1f, 2f * half / RoadTileLength)),
                            Drape.Surface, true, Footprint(start + direction * at, 4f, 4f));
                        CrossingSignals(crossing, roads[crossing.RoadId], nodes);
                    }
                }
        }

        // A crossing signal on the right of each road approach, facing the traffic, clear of the track.
        private void CrossingSignals(LevelCrossing crossing, RoadSegment road, Dictionary<string, RoadNode> nodes)
        {
            var signal = art.Get("Rail_Signal");
            var a = nodes[road.FromId];
            var b = nodes[road.ToId];
            var roadDirection = new Vector3(b.X - a.X, 0f, b.Z - a.Z).normalized;
            var point = new Vector3(crossing.X, 0f, crossing.Z);
            var kerb = road.Kind == RoadKind.Rural ? road.Width / 2f + 0.9f : road.Width / 2f - 0.7f;
            foreach (var d in new[] { roadDirection, -roadDirection })
            {
                var right = Vector3.Cross(Vector3.up, d);
                var position = point - d * 4.5f + right * kerb;
                Merge(signal, Matrix4x4.TRS(position, Quaternion.LookRotation(-d), Vector3.one), Drape.Rigid, false, Footprint(position, 1f, 1f));
            }
        }

        // Traffic lights and stop signs on the right of each controlled approach, facing the traffic, before the junction.
        private void Junctions(WorldLayout layout, Dictionary<string, RoadNode> nodes)
        {
            var bySegment = new Dictionary<string, List<RoadSegment>>();
            foreach (var road in layout.Roads)
                foreach (var id in new[] { road.FromId, road.ToId })
                {
                    if (!bySegment.TryGetValue(id, out var list)) bySegment[id] = list = new List<RoadSegment>();
                    list.Add(road);
                }
            var stop = art.Get("Stop_Sign");
            var arterialLight = art.Get("Traffic_Light_Arterial");
            var localLight = art.Get("Traffic_Light_Local");
            foreach (var node in layout.Nodes)
            {
                if (node.Control == JunctionControl.None || !bySegment.TryGetValue(node.Id, out var segments)) continue;
                var kinds = segments.Select(x => x.Kind).ToList();
                var here = new Vector3(node.X, 0f, node.Z);
                foreach (var road in segments)
                {
                    var far = nodes[road.FromId == node.Id ? road.ToId : road.FromId];
                    var d = (here - new Vector3(far.X, 0f, far.Z)).normalized;
                    var right = Vector3.Cross(Vector3.up, d);
                    // Half the width of the widest road crossing this approach.
                    var cross = segments.Where(x => x != road && Mathf.Abs(Vector3.Dot(Direction(x, nodes), d)) < 0.5f).Select(x => x.Width / 2f).DefaultIfEmpty(4f).Max();
                    var kerb = road.Kind == RoadKind.Rural ? road.Width / 2f + 0.9f : road.Width / 2f - 0.7f;
                    WorldArtCatalog.Piece piece;
                    float back;
                    if (node.Control == JunctionControl.TrafficLight)
                    {
                        piece = road.Kind == RoadKind.Arterial ? arterialLight : localLight;
                        back = cross + 1.2f;
                    }
                    else if (WorldJunctions.Stops(node.Control, road.Kind, kinds))
                    {
                        piece = stop;
                        back = cross + (road.Kind == RoadKind.Rural ? 2f : 5f);
                    }
                    else continue;
                    var position = here - d * back + right * kerb;
                    Merge(piece, Matrix4x4.TRS(position, Quaternion.LookRotation(-d), Vector3.one), Drape.Rigid, false, Footprint(position, 1f, 1f));
                }
            }
        }

        private static Vector3 Direction(RoadSegment road, Dictionary<string, RoadNode> nodes)
        {
            var a = nodes[road.FromId];
            var b = nodes[road.ToId];
            return new Vector3(b.X - a.X, 0f, b.Z - a.Z).normalized;
        }

        // ------------------------------------------------------------------ trees

        private void Trees(WorldLayout layout)
        {
            var pieces = new Dictionary<TreeKind, WorldArtCatalog.Piece[]>
            {
                [TreeKind.Broadleaf] = new[] { art.Get("Tree_Broadleaf_a"), art.Get("Tree_Broadleaf_b") },
                [TreeKind.Conifer] = new[] { art.Get("Tree_Conifer") },
                [TreeKind.Poplar] = new[] { art.Get("Tree_Poplar") },
                [TreeKind.Bush] = new[] { art.Get("Tree_Bush") }
            };
            foreach (var tree in layout.Trees)
            {
                var hash = WorldRandom.Mix((ulong)(uint)tree.X << 32 ^ (uint)tree.Z);
                var options = pieces[tree.Kind];
                var piece = options[(int)(hash % (ulong)options.Length)];
                var yaw = (hash >> 8) % 360;
                var position = new Vector3(tree.X + 0.5f, 0f, tree.Z + 0.5f);
                Merge(piece, Matrix4x4.TRS(position, Quaternion.Euler(0f, yaw, 0f), Vector3.one * (tree.Scale / 100f)), Drape.Rigid, false,
                    Footprint(position, 1f, 1f));
            }
        }

        // ------------------------------------------------------------------ merged meshes

        private static Rect Footprint(Vector3 centre, float sizeX, float sizeZ) => new(centre.x - sizeX / 2f, centre.z - sizeZ / 2f, sizeX, sizeZ);

        private MeshBuffer Buffer(Material material, float x, float z, bool collide)
        {
            var key = (material, Mathf.FloorToInt(x / ChunkSize), Mathf.FloorToInt(z / ChunkSize), collide);
            if (!_buffers.TryGetValue(key, out var buffer)) _buffers[key] = buffer = new MeshBuffer();
            return buffer;
        }

        private PieceData Data(Mesh mesh)
        {
            if (_pieceData.TryGetValue(mesh, out var data)) return data;
            data = new PieceData { Vertices = mesh.vertices, Uvs = mesh.uv, Triangles = new int[mesh.subMeshCount][] };
            for (var sub = 0; sub < mesh.subMeshCount; sub++) data.Triangles[sub] = mesh.GetTriangles(sub);
            _pieceData[mesh] = data;
            return data;
        }

        // Queues a piece into the merged meshes (one per material and chunk), unless it would cover the dev site.
        private void Merge(WorldArtCatalog.Piece piece, Matrix4x4 matrix, Drape drape, bool collide, Rect footprint)
        {
            // Generated worlds keep trees and signs off every lot (roads and tiles merely touch lots); older ones keep everything off
            // the dev site.
            if (_generated ? drape == Drape.Rigid && OnAnyLot(footprint) : Overlaps(footprint.x, footprint.y, footprint.width, footprint.height)) return;
            var origin = matrix.GetColumn(3);
            var lift = drape == Drape.Rigid ? Land(origin.x, origin.z) : 0f;
            var data = Data(piece.mesh);
            for (var sub = 0; sub < data.Triangles.Length; sub++)
            {
                var buffer = Buffer(piece.materials[sub], origin.x, origin.z, collide);
                // Each submesh copies only the vertices its triangles use.
                var remap = new Dictionary<int, int>();
                foreach (var index in data.Triangles[sub])
                {
                    if (!remap.TryGetValue(index, out var mapped))
                    {
                        var point = matrix.MultiplyPoint3x4(data.Vertices[index]);
                        point.y += drape == Drape.Surface ? Land(point.x, point.z) : lift;
                        mapped = buffer.Vertices.Count;
                        buffer.Vertices.Add(point);
                        buffer.Uvs.Add(data.Uvs.Length > index ? data.Uvs[index] : Vector2.zero);
                        remap[index] = mapped;
                    }
                    buffer.Triangles.Add(mapped);
                }
            }
        }

        private void Flush()
        {
            foreach (var pair in _buffers.OrderBy(x => x.Key.Material.name).ThenBy(x => x.Key.X).ThenBy(x => x.Key.Z))
            {
                if (pair.Value.Triangles.Count == 0) continue;
                var (material, x, z, collide) = pair.Key;
                var objectName = $"{material.name} {x},{z}";
                var mesh = new Mesh { name = objectName, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(pair.Value.Vertices);
                mesh.SetUVs(0, pair.Value.Uvs);
                mesh.SetTriangles(pair.Value.Triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                _meshes.Add(mesh);
                var go = Place(objectName, mesh, new[] { material }, _root, Vector3.zero, Quaternion.identity, Vector3.one);
                if (collide) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            _buffers.Clear();
        }

        // ------------------------------------------------------------------ buildings

        // Where a building's ground floor stands: its recorded elevation, levelled like the land near the dev site.
        private float Base(WorldBuilding building)
        {
            // A generated lot is levelled to its building's ground floor, where its site stands (SitePlacement).
            if (building.Doors.Count == 0 || _generated && building.HasLot) return building.ElevationCm / 100f;
            var door = building.Doors[0];
            return building.ElevationCm / 100f + Land(door.X + 0.5f, door.Z + 0.5f) - _terrain.Height(door.X + 0.5f, door.Z + 0.5f);
        }

        // A concrete plinth under a footprint from its floor down past the lowest land beneath it.
        private void Foundation(float x, float z, float width, float depth, float floor)
        {
            var lowest = new[] { Land(x, z), Land(x + width, z), Land(x, z + depth), Land(x + width, z + depth), Land(x + width / 2f, z + depth / 2f) }.Min();
            var drop = floor - lowest + 0.4f;
            if (drop < 0.45f) return;
            Merge(art.Get("Foundation"), Matrix4x4.TRS(new Vector3(x + width / 2f, floor, z + depth / 2f), Quaternion.identity, new Vector3(width, drop, depth)),
                Drape.None, false, new Rect(x, z, width, depth));
        }

        private void Building(WorldBuilding building, DistrictKind? district)
        {
            var floor = Base(building);
            var centre = new Vector3(building.X + building.Width / 2f, floor, building.Z + building.Depth / 2f);
            var yaw = Quaternion.Euler(0f, building.YawDegrees, 0f);
            var northSouth = building.Facing == Facing.North || building.Facing == Facing.South;
            var along = northSouth ? building.Width : building.Depth;
            var deep = northSouth ? building.Depth : building.Width;
            var variant = Variant(building);
            if (building.Category != BuildingCategory.Farm) Foundation(building.X, building.Z, building.Width, building.Depth, floor);
            switch (building.Category)
            {
                case BuildingCategory.Farm:
                    Farm(building, yaw, variant);
                    return;
                case BuildingCategory.Apartment:
                    Stack(building, centre, yaw, along, deep, $"Apartment_{variant}", "Apartment", ApartmentStorey);
                    return;
                case BuildingCategory.Office:
                    Stack(building, centre, yaw, along, deep, $"Office_{variant}", "Office", OfficeStorey);
                    return;
            }
            var large = building.ModelKey.StartsWith("house/large");
            var (pieceName, sizeKey) = building.Category switch
            {
                BuildingCategory.Restaurant => ($"Restaurant_{variant}", "Restaurant"),
                BuildingCategory.Factory => ($"Factory_{variant}", "Factory"),
                BuildingCategory.Station => ("Station", "Station"),
                _ => (large ? $"House_Large_{variant}" : $"House_Small_{variant}", large ? "House_Large" : "House_Small")
            };
            var size = Footprints[sizeKey];
            var holder = Holder(building, centre, yaw, new Vector3(along / size.x, 1f, deep / size.y));
            var piece = art.Get(pieceName);
            var visual = Place(pieceName, piece.mesh, piece.materials, holder, Vector3.zero, Quaternion.identity, Vector3.one);
            AddCollider(holder, size, Heights[sizeKey]);
            if (building.HasLot) Mark(holder.gameObject, building);
            if (_generated && building.IsShell) _shellModels[building.Id] = holder.gameObject;
            var awning = System.Array.FindIndex(piece.materials, x => x != null && x.name == "WG_Awning");
            if (building.Category != BuildingCategory.Restaurant || awning < 0) return;
            var renderer = visual.GetComponent<MeshRenderer>();
            _awnings[building.Id] = (renderer, awning, building);
            TintAwning(renderer, awning, building, session.ClientSite);
        }

        // Purchasable buildings carry their lot so aiming at one can open the buy panel (EquipmentInteraction).
        private void Mark(GameObject target, WorldBuilding building)
        {
            var lotId = WorldLot.IdFor(building.Id);
            if (_offers.ContainsKey(lotId)) target.AddComponent<PropertyMarker>().Configure(lotId, building.Id);
        }

        // Apartments and offices: a ground storey, middle storeys, and a roof cap, all fitted to the footprint.
        private void Stack(WorldBuilding building, Vector3 centre, Quaternion yaw, float along, float deep, string prefix, string sizeKey, float storey)
        {
            var size = Footprints[sizeKey];
            var holder = Holder(building, centre, yaw, new Vector3(along / size.x, 1f, deep / size.y));
            var floors = Mathf.Max(2, building.Floors);
            var ground = art.Get($"{prefix}_Ground");
            var middle = art.Get($"{prefix}_Middle");
            var roof = art.Get($"{prefix}_Roof");
            Place(ground.name, ground.mesh, ground.materials, holder, Vector3.zero, Quaternion.identity, Vector3.one);
            for (var level = 1; level < floors; level++)
                Place(middle.name, middle.mesh, middle.materials, holder, new Vector3(0f, level * storey, 0f), Quaternion.identity, Vector3.one);
            Place(roof.name, roof.mesh, roof.materials, holder, new Vector3(0f, floors * storey, 0f), Quaternion.identity, Vector3.one);
            AddCollider(holder, size, floors * storey + 1f);
        }

        // A farm is its field (see Fields) and a barn with its silo just inside the entrance, at its authored size and on a
        // plinth levelled to the land's highest point under it.
        private void Farm(WorldBuilding building, Quaternion yaw, string variant)
        {
            var door = building.Doors[0];
            var (stepX, stepZ) = WorldGeometry.Step(building.Facing);
            var x = door.X + 0.5f - stepX * 9.5f;
            var z = door.Z + 0.5f - stepZ * 9.5f;
            var floor = new[] { Land(x - 8f, z - 8f), Land(x + 8f, z - 8f), Land(x - 8f, z + 8f), Land(x + 8f, z + 8f) }.Max() + 0.1f;
            Foundation(x - 8f, z - 8f, 16f, 16f, floor);
            var piece = art.Get($"Barn_{variant}");
            var barn = Place($"Barn {building.Id}", piece.mesh, piece.materials, _root, new Vector3(x, floor, z), yaw, Vector3.one);
            var collider = barn.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 4.5f, 0f);
            collider.size = new Vector3(12f, 9f, 16f);
            Mark(barn, building);
        }

        private Transform Holder(WorldBuilding building, Vector3 centre, Quaternion yaw, Vector3 scale)
        {
            var holder = new GameObject($"{building.Category} {building.Id}").transform;
            holder.SetParent(_root, false);
            holder.localPosition = centre;
            holder.localRotation = yaw;
            holder.localScale = scale;
            return holder;
        }

        private static void AddCollider(Transform holder, Vector2 size, float height)
        {
            var collider = holder.gameObject.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, height / 2f, 0f);
            collider.size = new Vector3(size.x, height, size.y);
        }

        // Ownership records first (decision 0028): this client's company green, any other company a competitor colour; a lot
        // nobody has bought shows the layout's own state.
        private void TintAwning(MeshRenderer renderer, int index, WorldBuilding building, GoodsSnapshot site)
        {
            if (renderer == null) return;
            var competitor = CompetitorAwnings[(int)(WorldRandom.Fnv1a(building.Id) % (ulong)CompetitorAwnings.Length)];
            var lotId = WorldLot.IdFor(building.Id);
            var owner = site?.Properties.FirstOrDefault(x => x.LotId == lotId)?.CompanyId;
            var mine = site?.Companies.FirstOrDefault(x => x.SiteIds.Contains(session.ClientSiteId))?.Id;
            var colour = owner != null ? (owner == mine ? PlayerAwning : competitor) : building.Ownership switch
            {
                Ownership.Player => PlayerAwning,
                Ownership.ForSale => ForSaleAwning,
                _ => competitor
            };
            if (_awningColours.TryGetValue(building.Id, out var shown) && shown != colour)
                Debug.Log($"[World] {building.Id} awning re-tinted: {(owner == null ? "unowned" : owner == mine ? "owned by this company" : "owned by another company")}.");
            _awningColours[building.Id] = colour;
            _block.Clear();
            _block.SetColor(BaseColor, colour);
            renderer.SetPropertyBlock(_block, index);
        }

        // The model variant letter: from the premade model key, or chosen per ID for generated shells and stations.
        private static string Variant(WorldBuilding building)
        {
            var key = building.ModelKey ?? "";
            var dash = key.LastIndexOf('-');
            if (dash >= 0 && dash + 1 < key.Length) return key.Substring(dash + 1);
            return WorldRandom.Fnv1a(building.Id) % 2 == 0 ? "a" : "b";
        }

        // ------------------------------------------------------------------ helpers

        private bool Overlaps(float x, float z, float width, float depth) => _clear.Overlaps(new Rect(x, z, width, depth));

        private static GameObject Place(string objectName, Mesh mesh, Material[] materials, Transform parent, Vector3 localPosition,
            Quaternion localRotation, Vector3 localScale)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }
    }
}
