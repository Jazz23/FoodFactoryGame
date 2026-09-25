// Shows the site's belts and the goods riding them from the latest replicated baseline. Each belt is drawn straight or as a
// left/right corner from the same shape rule the server moves items by (BeltRules.Shape). Every belt shares one runtime
// tread material whose texture offset follows one clock at the simulation's speed, so the arrows of all belts line up and
// move with the items. An item is drawn trailing the server by up to one clock step: it glides along the belt path at belt
// speed toward its latest replicated position, so it never jumps between the whole-second baselines. A conveyor lift
// (decision 0021) is drawn from the straight model: half a belt in on its own floor, an open frame one storey up or down,
// and half a belt out on the other floor; each end is hidden with its own storey. Presentation only.
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
            public int Lift;
            // A lift's upper end, hidden with the upper storey; null for a flat belt.
            public GameObject Upper;
        }

        // Names of a lift model's parts, so the upper end can be found in an instance.
        private const string LowerPart = "Lower";
        private const string UpperPart = "Upper";
        // Frame of a lift: four corner posts this far from the tile centre.
        private const float PostInset = 0.38f;
        private const float PostSize = 0.07f;

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
        private readonly Dictionary<int, GameObject> _liftModels = new();
        private GameObject _liftModelHolder;
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
            if (_liftModelHolder != null) Destroy(_liftModelHolder);
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
            // A lift's lower end and frame go with its lower storey, its upper end with the upper one.
            foreach (var (id, view) in _belts)
            {
                var belt = _beltById[id];
                var shown = !Hidden(belt, Math.Min(belt.Level, belt.ExitLevel));
                if (view.Root.activeSelf != shown) view.Root.SetActive(shown);
                var upperShown = shown && !Hidden(belt, Math.Max(belt.Level, belt.ExitLevel));
                if (view.Upper != null && view.Upper.activeSelf != upperShown) view.Upper.SetActive(upperShown);
            }
            foreach (var view in _items.Values)
            {
                var shown = !Hidden(_beltById[view.BeltId], BeltPath.LevelAt(_beltById[view.BeltId], view.Position));
                if (view.Renderer.enabled != shown) view.Renderer.enabled = shown;
            }
        }

        private bool Hidden(GoodsBelt belt, int level) => buildings.HidesLevel(belt.CellX, belt.CellZ, level);

        // Belts that take items on one floor (a lift is on the floor it takes items on).
        public static IEnumerable<GoodsBelt> OnLevel(IEnumerable<GoodsBelt> belts, int level) => belts.Where(x => x.Level == level);

        // Belts standing on one floor, including lifts whose other end is there.
        public static IEnumerable<GoodsBelt> Touching(IEnumerable<GoodsBelt> belts, int level) =>
            belts.Where(x => x.Level == level || x.ExitLevel == level);

        // Shape of a belt that exists, or would exist, at a cell on a level: used by the placement ghost.
        public BeltShape ShapeAt(IEnumerable<GoodsBelt> belts, int cellX, int cellZ, int level, int direction) =>
            BeltRules.Shape(BeltRules.ByCell(belts), cellX, cellZ, level, direction);

        // A tile-centred lift model travelling +Z from its own floor (local height 0) to one storey up (lift +1) or down (-1),
        // built once per direction from the straight belt and kept inactive for instancing (placed lifts and ghosts).
        public GameObject LiftModel(int lift)
        {
            if (_liftModels.TryGetValue(lift, out var model)) return model;
            if (_liftModelHolder == null)
            {
                _liftModelHolder = new GameObject("Lift models");
                _liftModelHolder.SetActive(false);
                _liftModelHolder.transform.SetParent(transform, false);
            }
            model = new GameObject(lift > 0 ? "Lift up" : "Lift down");
            model.transform.SetParent(_liftModelHolder.transform, false);
            var height = lift * SiteGridSpace.LevelHeight;
            // The lower part is on the lower storey (the entry of an up-lift, the exit of a down-lift) and carries the frame.
            var lower = new GameObject(LowerPart).transform;
            lower.SetParent(model.transform, false);
            var upper = new GameObject(UpperPart).transform;
            upper.SetParent(model.transform, false);
            HalfBelt(lift > 0 ? lower : upper, 0f, -0.25f);
            HalfBelt(lift > 0 ? upper : lower, height, 0.25f);
            var frame = FrameMaterial();
            var bottom = Mathf.Min(0f, height);
            foreach (var (x, z) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post";
                post.transform.SetParent(lower, false);
                post.transform.localPosition = new Vector3(x * PostInset, bottom + (SiteGridSpace.LevelHeight + BeltPath.SurfaceHeight) * 0.5f, z * PostInset);
                post.transform.localScale = new Vector3(PostSize, SiteGridSpace.LevelHeight + BeltPath.SurfaceHeight, PostSize);
                post.GetComponent<Renderer>().sharedMaterial = frame;
                // Aim rays name the lift by its frame too; players walk through it, like belts.
                post.GetComponent<Collider>().isTrigger = true;
            }
            _liftModels[lift] = model;
            return model;
        }

        // Half a straight belt (its back or front half) at a height, travelling +Z.
        private void HalfBelt(Transform parent, float height, float offset)
        {
            var half = Instantiate(straightPrefab, parent, false);
            half.name = "Half belt";
            half.transform.localPosition = new Vector3(0f, height, offset);
            half.transform.localScale = new Vector3(1f, 1f, 0.5f);
        }

        // The straight model's frame material, so the lift's posts match the belts.
        private Material FrameMaterial()
        {
            var materials = straightPrefab.GetComponentsInChildren<Renderer>(true).SelectMany(x => x.sharedMaterials)
                .Where(x => x != null && !IsTread(x)).ToList();
            return materials.FirstOrDefault(x => x.name.Contains("Frame")) ?? materials.FirstOrDefault();
        }

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
            // Each floor is its own belt network (decision 0020), joined only by lifts.
            var cells = BeltRules.ByCell(belts);
            _beltById.Clear();
            _shapes.Clear();
            _links.Clear();
            _beltOfLocation.Clear();
            foreach (var belt in belts)
            {
                _beltById[belt.Id] = belt;
                _shapes[belt.Id] = BeltRules.Shape(cells, belt);
                _links[belt.Id] = BeltRules.Link(cells, belt);
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
                if (_belts.TryGetValue(belt.Id, out var view) && (view.Shape != shape || view.Lift != belt.Lift))
                {
                    Destroy(view.Root);
                    _belts.Remove(belt.Id);
                    view = null;
                }
                if (view == null)
                {
                    var root = CreateBelt(belt, shape);
                    view = new BeltView { Root = root, Shape = shape, Lift = belt.Lift, Upper = belt.Lift == 0 ? null : root.transform.Find(UpperPart)?.gameObject };
                    _belts.Add(belt.Id, view);
                }
                view.Direction = belt.Direction;
                view.Root.transform.SetPositionAndRotation(BeltPath.CellCenter(_layout, belt.CellX, belt.CellZ, belt.Level),
                    BeltPath.ModelRotation(shape, belt.Direction));
            }
        }

        private GameObject CreateBelt(GoodsBelt belt, BeltShape shape)
        {
            var root = Instantiate(belt.Lift == 0 ? PrefabFor(shape) : LiftModel(belt.Lift), transform, false);
            root.name = belt.Lift == 0 ? $"Belt {belt.Id} ({shape})" : $"Lift {belt.Id} ({(belt.Lift > 0 ? "up" : "down")})";
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
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
