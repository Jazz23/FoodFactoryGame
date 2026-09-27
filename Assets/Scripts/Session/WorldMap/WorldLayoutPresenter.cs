// Client presentation of the replicated world layout (decision 0026) with the ArtSource/World models: tiled roads with
// crosswalks and junction patches, rail track, ground cover, and a model per building fitted to its footprint and facing
// (modular apartments and offices stacked to their storeys, farms as a field plus a barn). It is built only from the layout
// the server sent through WorldLayoutBridge and is rebuilt when that changes; it writes no state, and nothing in the
// simulation reads it or depends on whether it exists. Road and rail tiles are merged into one mesh per material and the
// rest is static-batched, so a ~1,000-building city stays a few hundred draw calls.
using System.Collections.Generic;
using System.Linq;
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
        // where a player sees the city on arrival. Pieces that would land on the dev site are not drawn.
        [SerializeField] private float cityGap = 40f;
        [SerializeField] private Vector2 devSiteClearHalfSize = new(24f, 24f);

        // Authored model sizes (ArtSource/World/build_world_models.py): along the street (X) by depth, in metres, and storey
        // heights of the stacked modules. Road tiles are 10 m long, rail tiles 6 m; both run along local -Z from 0.
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
            "Ground_Quad", "Road_Junction", "Road_Arterial", "Road_Arterial_Crosswalk", "Road_Local", "Road_Local_Crosswalk", "Road_Rural",
            "Rail_Track", "House_Small_a", "House_Small_b", "House_Small_c", "House_Large_a", "House_Large_b", "House_Large_c",
            "Apartment_a_Ground", "Apartment_a_Middle", "Apartment_a_Roof", "Apartment_b_Ground", "Apartment_b_Middle", "Apartment_b_Roof",
            "Office_a_Ground", "Office_a_Middle", "Office_a_Roof", "Office_b_Ground", "Office_b_Middle", "Office_b_Roof",
            "Restaurant_a", "Restaurant_b", "Factory_a", "Factory_b", "Barn_a", "Barn_b", "Station"
        };
        private const float RoadTileLength = 10f;
        private const float RailTileLength = 6f;
        private const float ApartmentStorey = 3f;
        private const float OfficeStorey = 3.5f;
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        // Awning colours: the player's restaurant, restaurants for sale, then competitors by ID.
        private static readonly Color PlayerAwning = new(0.18f, 0.62f, 0.26f);
        private static readonly Color ForSaleAwning = new(0.95f, 0.75f, 0.15f);
        private static readonly Color[] CompetitorAwnings =
        {
            new(0.72f, 0.14f, 0.12f), new(0.16f, 0.3f, 0.6f), new(0.45f, 0.18f, 0.5f), new(0.12f, 0.45f, 0.45f)
        };

        private WorldLayoutBridge _bridge;
        private Transform _root;
        private Vector3 _layoutOrigin;
        private Rect _clear;
        private MaterialPropertyBlock _block;
        private readonly List<Mesh> _meshes = new();
        private readonly Dictionary<Material, List<CombineInstance>> _merged = new();

        public WorldLayout Shown { get; private set; }
        // Where the layout's origin (the city centre) stands in the scene while a layout is shown.
        public Vector3 LayoutOrigin => _layoutOrigin;
        public int BuildingCount { get; private set; }

        private void Update()
        {
            var manager = session.NetworkManager;
            if (_bridge == null && manager != null && manager.IsClientStarted)
                _bridge = manager.ClientManager.Objects.Spawned.Values.Select(x => x.GetComponent<WorldLayoutBridge>()).FirstOrDefault(x => x != null);
            var layout = _bridge != null && _bridge.IsClientStarted ? _bridge.Layout : null;
            if (layout != Shown) Show(layout);
            if (Shown != null) ExtendCameraRange(Shown);
        }

        // PROTOTYPE presentation: the map is about 2 km across and starts beside the dev site, past the player camera's
        // 1 km far plane, so while a layout is shown every local camera draws far enough to see all of it.
        private void ExtendCameraRange(WorldLayout layout)
        {
            var needed = _layoutOrigin.magnitude + layout.MapHalfSize * 1.5f;
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
            BuildingCount = 0;
        }

        private void Show(WorldLayout layout)
        {
            Clear();
            Shown = layout;
            if (layout == null) return;
            _block ??= new MaterialPropertyBlock();
            _root = new GameObject("World Layout").transform;
            _root.SetParent(transform, false);
            _layoutOrigin = new Vector3(0f, 0f, devSiteClearHalfSize.y + cityGap + layout.CityHalfSize);
            _root.position = _layoutOrigin;
            // The dev site's floor in layout-local coordinates.
            _clear = new Rect(-devSiteClearHalfSize.x - _layoutOrigin.x, -devSiteClearHalfSize.y - _layoutOrigin.z,
                2f * devSiteClearHalfSize.x, 2f * devSiteClearHalfSize.y);

            Ground(layout);
            Roads(layout);
            Rails(layout);
            FlushMerged("Roads and rail");
            var districts = layout.Districts.ToDictionary(x => x.Id);
            foreach (var building in layout.Buildings)
            {
                if (Overlaps(building.X, building.Z, building.Width, building.Depth)) continue;
                Building(building, districts.TryGetValue(building.DistrictId ?? "", out var district) ? district.Kind : null);
                BuildingCount++;
            }
            StaticBatchingUtility.Combine(_root.gameObject);
        }

        // ------------------------------------------------------------------ ground

        private void Ground(WorldLayout layout)
        {
            var map = 2f * layout.MapHalfSize;
            var ground = Quad("Ground", art.grass, new Vector3(0f, -0.01f, 0f), new Vector2(map, map), 8f, false);
            // The ground is walkable everywhere; the dev site keeps its own floor on top.
            var collider = ground.AddComponent<BoxCollider>();
            collider.size = new Vector3(1f, 0.02f, 1f);
            collider.center = new Vector3(0f, -0.01f, 0f);
            foreach (var district in layout.Districts)
            {
                var cover = district.Kind switch
                {
                    DistrictKind.Downtown => art.paving,
                    DistrictKind.Industrial => art.yard,
                    _ => null
                };
                if (cover == null) continue;
                foreach (var area in district.Areas)
                    Quad($"Lot {district.Id}", cover, new Vector3(area.X + area.Width / 2f, 0.005f, area.Z + area.Depth / 2f),
                        new Vector2(area.Width, area.Depth), 8f, true);
            }
        }

        // A ground-cover quad of the given size whose texture repeats every `metresPerRepeat`.
        private GameObject Quad(string objectName, Material material, Vector3 centre, Vector2 size, float metresPerRepeat, bool clearOfDevSite)
        {
            if (clearOfDevSite && Overlaps(centre.x - size.x / 2f, centre.z - size.y / 2f, size.x, size.y)) return null;
            var piece = art.Get("Ground_Quad");
            var quad = Place(objectName, piece.mesh, new[] { material }, _root, centre, Quaternion.identity, new Vector3(size.x, 1f, size.y));
            _block.Clear();
            _block.SetVector(BaseMapSt, new Vector4(size.x / metresPerRepeat, size.y / metresPerRepeat, 0f, 0f));
            quad.GetComponent<MeshRenderer>().SetPropertyBlock(_block);
            return quad;
        }

        // ------------------------------------------------------------------ roads and rail

        private void Roads(WorldLayout layout)
        {
            var nodes = layout.Nodes.ToDictionary(x => x.Id);
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
                Merge(junction, new Vector3(node.X, 0f, node.Z), Quaternion.identity, new Vector3(2f * x, 1f, 2f * z), 2f * x, 2f * z);
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
                    Merge(piece, from, Quaternion.LookRotation(-tileDirection), new Vector3(1f, 1f, tile / RoadTileLength),
                        alongZ ? road.Width : tile, alongZ ? tile : road.Width, centre);
                }
            }
        }

        private void Rails(WorldLayout layout)
        {
            var track = art.Get("Rail_Track");
            foreach (var line in layout.Rails)
                for (var index = 0; index + 1 < line.Points.Count; index++)
                {
                    var start = new Vector3(line.Points[index].X, 0f, line.Points[index].Z);
                    var end = new Vector3(line.Points[index + 1].X, 0f, line.Points[index + 1].Z);
                    var direction = (end - start).normalized;
                    var length = Vector3.Distance(start, end);
                    var count = Mathf.Max(1, Mathf.RoundToInt(length / RailTileLength));
                    var tile = length / count;
                    var alongZ = Mathf.Abs(direction.z) > 0.5f;
                    for (var step = 0; step < count; step++)
                    {
                        var from = start + direction * (step * tile);
                        Merge(track, from, Quaternion.LookRotation(-direction), new Vector3(1f, 1f, tile / RailTileLength),
                            alongZ ? line.Width : tile, alongZ ? tile : line.Width, from + direction * (tile / 2f));
                    }
                }
        }

        // Queues a piece into the merged road/rail meshes, one per material, unless it would cover the dev site.
        private void Merge(WorldArtCatalog.Piece piece, Vector3 position, Quaternion rotation, Vector3 scale, float sizeX, float sizeZ,
            Vector3? centre = null)
        {
            var c = centre ?? position;
            if (Overlaps(c.x - sizeX / 2f, c.z - sizeZ / 2f, sizeX, sizeZ)) return;
            var matrix = Matrix4x4.TRS(position, rotation, scale);
            for (var sub = 0; sub < piece.mesh.subMeshCount; sub++)
            {
                var material = piece.materials[sub];
                if (!_merged.TryGetValue(material, out var list)) _merged[material] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = piece.mesh, subMeshIndex = sub, transform = matrix });
            }
        }

        private void FlushMerged(string objectName)
        {
            foreach (var pair in _merged)
            {
                var mesh = new Mesh { name = $"{objectName} {pair.Key.name}", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                _meshes.Add(mesh);
                Place($"{objectName} {pair.Key.name}", mesh, new[] { pair.Key }, _root, Vector3.zero, Quaternion.identity, Vector3.one);
            }
            _merged.Clear();
        }

        // ------------------------------------------------------------------ buildings

        private void Building(WorldBuilding building, DistrictKind? district)
        {
            var centre = new Vector3(building.X + building.Width / 2f, 0f, building.Z + building.Depth / 2f);
            var yaw = Quaternion.Euler(0f, building.YawDegrees, 0f);
            var northSouth = building.Facing == Facing.North || building.Facing == Facing.South;
            var along = northSouth ? building.Width : building.Depth;
            var deep = northSouth ? building.Depth : building.Width;
            var variant = Variant(building);
            switch (building.Category)
            {
                case BuildingCategory.Farm:
                    Farm(building, centre, yaw, variant);
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
            if (building.Category == BuildingCategory.Restaurant) TintAwning(visual.GetComponent<MeshRenderer>(), piece, building);
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

        // A farm is its field over the whole plot and a barn with its silo just inside the entrance, at its authored size.
        private void Farm(WorldBuilding building, Vector3 centre, Quaternion yaw, string variant)
        {
            Quad($"Field {building.Id}", variant == "a" ? art.wheat : art.greens, centre + new Vector3(0f, 0.02f, 0f),
                new Vector2(building.Width, building.Depth), 12f, false);
            var door = building.Doors[0];
            var (stepX, stepZ) = WorldGeometry.Step(building.Facing);
            var barnCentre = new Vector3(door.X + 0.5f - stepX * 9.5f, 0f, door.Z + 0.5f - stepZ * 9.5f);
            var piece = art.Get($"Barn_{variant}");
            var barn = Place($"Barn {building.Id}", piece.mesh, piece.materials, _root, barnCentre, yaw, Vector3.one);
            var collider = barn.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 4.5f, 0f);
            collider.size = new Vector3(12f, 9f, 16f);
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

        private void TintAwning(MeshRenderer renderer, WorldArtCatalog.Piece piece, WorldBuilding building)
        {
            var index = System.Array.FindIndex(piece.materials, x => x != null && x.name == "WG_Awning");
            if (index < 0) return;
            var colour = building.Ownership switch
            {
                Ownership.Player => PlayerAwning,
                Ownership.ForSale => ForSaleAwning,
                _ => CompetitorAwnings[(int)(WorldRandom.Fnv1a(building.Id) % (ulong)CompetitorAwnings.Length)]
            };
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
