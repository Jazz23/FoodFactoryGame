// Shows the site's belts and the goods riding them from the latest replicated baseline. Each belt is drawn straight or as a
// left/right corner from the same shape rule the server moves items by (BeltRules.Shape). Every belt shares one runtime
// tread material whose texture offset follows one clock at the simulation's speed, so the arrows of all belts line up and
// move with the items. An item is drawn trailing the server by up to one clock step: it glides along the belt path at belt
// speed toward its latest replicated position, so it never jumps between the whole-second baselines. Presentation only.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.Rendering;

namespace FoodFactoryGame.Session.Belts
{
    [DisallowMultipleComponent]
    public sealed class BeltPresenter : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        [SerializeField] private SessionRoot session;
        [SerializeField] private BuildingPresenter buildings;
        // Tile-centred models travelling +Z: straight, and corners entering from behind and turning left or right.
        [SerializeField] private GameObject straightPrefab;
        [SerializeField] private GameObject leftCornerPrefab;
        [SerializeField] private GameObject rightCornerPrefab;
        // The belt surface material in the prefabs; replaced at runtime by one scrolling instance shared by every belt.
        [SerializeField] private Material treadMaterial;
        // Unlit sprite material for goods riding a belt, drawn as camera-facing icons.
        [SerializeField] private Material itemMaterial;
        [SerializeField] private float itemSize = 0.42f;

        private sealed class BeltView
        {
            public GameObject Root;
            public BeltShape Shape;
            public int Direction;
        }

        private sealed class ItemView
        {
            public SpriteRenderer Renderer;
            public string BeltId;
            public float Position;
        }

        private readonly Dictionary<string, BeltView> _belts = new();
        private readonly Dictionary<string, ItemView> _items = new();
        private readonly Dictionary<string, GoodsBelt> _beltById = new();
        private readonly Dictionary<string, BeltShape> _shapes = new();
        private readonly Dictionary<string, BeltLink> _links = new();
        private readonly Dictionary<string, string> _beltOfLocation = new();
        private Material _tread;
        private GoodsSnapshot _shown;
        private SiteLayout _layout;

        public IReadOnlyDictionary<string, GameObject> Belts => _belts.ToDictionary(x => x.Key, x => x.Value.Root);
        public IReadOnlyCollection<string> ItemIds => _items.Keys;
        public Material Tread => _tread;
        public Material ItemMaterial => itemMaterial;
        public float ItemSize => itemSize;
        public SiteLayout Layout => _layout;

        // Texture scroll in UV per second: the models map half a UV unit to one tile of travel.
        public static float ScrollPerSecond => 0.5f * BeltRules.UnitsPerSecond / BeltRules.UnitsPerTile;

        private void Awake()
        {
            _tread = new Material(treadMaterial) { name = treadMaterial.name + " (scrolling)" };
        }

        private void OnDestroy()
        {
            if (_tread != null) Destroy(_tread);
        }

        private void OnDisable() => Clear();

        private void Update()
        {
            // One offset for every belt: V increases along travel, so subtracting time moves the arrows forward.
            _tread.SetTextureOffset(BaseMap, new Vector2(0f, -Mathf.Repeat(Time.time * ScrollPerSecond, 1f)));
            var site = session.ClientSite;
            if (!ReferenceEquals(site, _shown)) Refresh(site);
            UpdateItems(site);
            // Belts and riding goods on a storey the local view hides (decision 0020) are hidden with it, colliders included.
            foreach (var (id, view) in _belts)
            {
                var shown = !Hidden(_beltById[id]);
                if (view.Root.activeSelf != shown) view.Root.SetActive(shown);
            }
            foreach (var view in _items.Values)
            {
                var shown = !Hidden(_beltById[view.BeltId]);
                if (view.Renderer.enabled != shown) view.Renderer.enabled = shown;
            }
        }

        private bool Hidden(GoodsBelt belt) => buildings.HidesLevel(belt.CellX, belt.CellZ, belt.Level);

        // Belts on one floor, whose shapes and links depend only on each other.
        public static IEnumerable<GoodsBelt> OnLevel(IEnumerable<GoodsBelt> belts, int level) => belts.Where(x => x.Level == level);

        // Shape of a belt that exists, or would exist, at a cell: used by the placement ghost.
        public BeltShape ShapeAt(IEnumerable<GoodsBelt> belts, int cellX, int cellZ, int direction) =>
            BeltRules.Shape(BeltRules.ByCell(belts), cellX, cellZ, direction);

        public GameObject PrefabFor(BeltShape shape) => shape switch
        {
            BeltShape.CurveFromLeft => leftCornerPrefab,
            BeltShape.CurveFromRight => rightCornerPrefab,
            _ => straightPrefab
        };

        public bool IsTread(Material material) => material == treadMaterial;

        private void Refresh(GoodsSnapshot site)
        {
            _shown = site;
            _layout = site?.SiteLayouts.FirstOrDefault(x => x.SiteId == DevWorld.SiteId);
            var belts = _layout == null ? new List<GoodsBelt>() : site.Belts.Where(x => x.SiteId == DevWorld.SiteId).ToList();
            // Each floor is its own belt network (decision 0020).
            var floors = belts.GroupBy(x => x.Level).ToDictionary(x => x.Key, x => BeltRules.ByCell(x));
            _beltById.Clear();
            _shapes.Clear();
            _links.Clear();
            _beltOfLocation.Clear();
            foreach (var belt in belts)
            {
                _beltById[belt.Id] = belt;
                _shapes[belt.Id] = BeltRules.Shape(floors[belt.Level], belt);
                _links[belt.Id] = BeltRules.Link(floors[belt.Level], belt);
                _beltOfLocation[belt.LocationId] = belt.Id;
            }
            foreach (var id in _belts.Keys.Where(x => !_beltById.ContainsKey(x)).ToList())
            {
                Destroy(_belts[id].Root);
                _belts.Remove(id);
            }
            foreach (var belt in belts)
            {
                var shape = _shapes[belt.Id];
                if (_belts.TryGetValue(belt.Id, out var view) && view.Shape != shape)
                {
                    Destroy(view.Root);
                    _belts.Remove(belt.Id);
                    view = null;
                }
                if (view == null)
                {
                    view = new BeltView { Root = CreateBelt(belt, shape), Shape = shape };
                    _belts.Add(belt.Id, view);
                }
                view.Direction = belt.Direction;
                view.Root.transform.SetPositionAndRotation(BeltPath.CellCenter(_layout, belt.CellX, belt.CellZ, belt.Level),
                    BeltPath.ModelRotation(shape, belt.Direction));
            }
        }

        private GameObject CreateBelt(GoodsBelt belt, BeltShape shape)
        {
            var root = Instantiate(PrefabFor(shape), transform, false);
            root.name = $"Belt {belt.Id} ({shape})";
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                if (!materials.Any(IsTread)) continue;
                renderer.sharedMaterials = materials.Select(x => IsTread(x) ? _tread : x).ToArray();
            }
            if (!root.TryGetComponent<BeltVisual>(out var visual)) visual = root.AddComponent<BeltVisual>();
            visual.Bind(belt.Id);
            return root;
        }

        private void UpdateItems(GoodsSnapshot site)
        {
            var riding = _layout == null ? new List<GoodsLot>()
                : site.Lots.Where(x => _beltOfLocation.ContainsKey(x.LocationId)).ToList();
            foreach (var id in _items.Keys.Where(x => riding.All(y => y.Id != x)).ToList())
            {
                Destroy(_items[id].Renderer.gameObject);
                _items.Remove(id);
            }
            var camera = ViewCamera();
            foreach (var lot in riding)
            {
                var beltId = _beltOfLocation[lot.LocationId];
                if (!_items.TryGetValue(lot.Id, out var view))
                {
                    view = new ItemView { Renderer = CreateItem(lot), BeltId = beltId, Position = lot.BeltPosition };
                    _items.Add(lot.Id, view);
                }
                Follow(view, beltId, lot.BeltPosition);
                var belt = _beltById[view.BeltId];
                var transformItem = view.Renderer.transform;
                transformItem.position = BeltPath.WorldPoint(_layout, belt, _shapes[belt.Id], view.Position) + Vector3.up * (itemSize * 0.5f);
                if (camera != null) transformItem.rotation = camera.transform.rotation;
            }
        }

        // Glides a drawn item along the belt path toward the server's position at belt speed (faster when far behind);
        // snaps when the target cannot be reached forward within a few belts (placed, removed or re-routed).
        private void Follow(ItemView view, string beltId, int target)
        {
            if (!_beltById.ContainsKey(view.BeltId))
            {
                view.BeltId = beltId;
                view.Position = target;
                return;
            }
            var distance = Distance(view.BeltId, view.Position, beltId, target);
            if (distance < 0f)
            {
                view.BeltId = beltId;
                view.Position = target;
                return;
            }
            var speed = BeltRules.UnitsPerSecond * (distance > BeltRules.UnitsPerTile * 1.5f ? 2f : 1f);
            var move = Mathf.Min(distance, speed * Time.deltaTime);
            if (move >= distance)
            {
                view.BeltId = beltId;
                view.Position = target;
                return;
            }
            var position = view.Position + move;
            while (position >= BeltRules.UnitsPerTile)
            {
                var link = _links[view.BeltId];
                if (link.Next == null)
                {
                    position = BeltRules.UnitsPerTile - 1;
                    break;
                }
                position = link.Entry == 0 ? position - BeltRules.UnitsPerTile : link.Entry;
                view.BeltId = link.Next.Id;
            }
            view.Position = position;
        }

        // Path distance forward from one belt position to another, following links; -1 when not reachable within 4 belts.
        private float Distance(string fromBelt, float from, string toBelt, float to)
        {
            var total = 0f;
            for (var hops = 0; hops < 4; hops++)
            {
                if (fromBelt == toBelt && to >= from - 0.01f) return total + Mathf.Max(0f, to - from);
                if (!_links.TryGetValue(fromBelt, out var link) || link.Next == null) return -1f;
                total += BeltRules.UnitsPerTile - from;
                fromBelt = link.Next.Id;
                from = link.Entry;
            }
            return -1f;
        }

        private SpriteRenderer CreateItem(GoodsLot lot)
        {
            var item = session.Items.FirstOrDefault(x => x != null && x.Id == lot.ItemId);
            var instance = new GameObject($"Riding {lot.ItemId} {lot.Id}");
            instance.transform.SetParent(transform, false);
            var renderer = instance.AddComponent<SpriteRenderer>();
            ConfigureItemSprite(renderer, item?.Icon, itemMaterial, itemSize);
            renderer.color = lot.Spoiled ? new Color(0.6f, 0.75f, 0.35f, 1f) : Color.white;
            return renderer;
        }

        // Shared with the placement ghost: an icon sprite scaled to a world size, never casting shadows.
        public static void ConfigureItemSprite(SpriteRenderer renderer, Sprite sprite, Material material, float size)
        {
            renderer.sprite = sprite;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var extent = sprite == null ? 1f : Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
            renderer.transform.localScale = Vector3.one * (size / Mathf.Max(0.0001f, extent));
        }

        // The local player's rig camera is the only enabled camera in a client (DevSite has no scene camera).
        public static Camera ViewCamera()
        {
            var camera = Camera.main;
            if (camera != null && camera.isActiveAndEnabled) return camera;
            return Array.Find(Camera.allCameras, x => x.isActiveAndEnabled);
        }

        private void Clear()
        {
            foreach (var view in _belts.Values)
                if (view.Root != null) Destroy(view.Root);
            foreach (var view in _items.Values)
                if (view.Renderer != null) Destroy(view.Renderer.gameObject);
            _belts.Clear();
            _items.Clear();
            _shown = null;
        }
    }
}
