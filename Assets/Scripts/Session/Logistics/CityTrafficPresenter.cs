// Presentation only (decision 0032, piece 4b): draws the city cars CityCars places near the camera in a generated world, with
// pooled models picked and tinted per car, easing between frames so lane changes of the stream (a new hour's headway) glide
// instead of jumping. Cars have no colliders and never touch the simulation; with no map shown nothing is drawn.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class CityTrafficPresenter : MonoBehaviour
    {
        // PROTOTYPE: at most this many cars are drawn, the nearest first, within the draw radius of the camera.
        public const int MaxCars = 150;
        private const float Radius = DrawnSites.DrawRadius;
        private const float EaseRate = 8f;
        private const float SnapDistance = 25f;

        [SerializeField] private SessionRoot session;
        [SerializeField] private GameObject[] carPrefabs = new GameObject[0];
        [SerializeField] private Color[] tints = new Color[0];

        private readonly Dictionary<uint, GameObject> _cars = new();
        private readonly Dictionary<int, Stack<GameObject>> _pool = new();
        private MaterialPropertyBlock _block;
        private GoodsSnapshot _baseline;
        private float _receivedAt;

        public int Shown => _cars.Count;
        public IReadOnlyDictionary<uint, GameObject> Cars => _cars;

        private void Update()
        {
            var placement = SitePlacement.Active;
            var current = session.DrawnSites.Sites.FirstOrDefault(x => x.Current)?.Snapshot;
            var camera = Belts.BeltPresenter.ViewCamera();
            if (placement == null || current == null || camera == null || carPrefabs.Length == 0)
            {
                Clear();
                return;
            }
            if (!ReferenceEquals(current, _baseline))
            {
                _baseline = current;
                _receivedAt = Time.time;
            }
            var since = Mathf.Min(Time.time - _receivedAt, RoadPose.MaxExtrapolationSeconds);
            var network = placement.Roads;
            var cars = CityCars.Compute(network, current.ClockSeconds + since, placement.ToMap(camera.transform.position), Radius,
                current.Trucks.Concat(current.RoadTrucks), since, MaxCars);
            var keep = new HashSet<uint>();
            var k = 1f - Mathf.Exp(-EaseRate * Time.deltaTime);
            foreach (var car in cars)
            {
                if (!keep.Add(car.Key)) continue;
                var segment = network.Segments[car.Segment];
                var offset = car.Forward ? car.Along : segment.Length - car.Along;
                var (map, heading) = RoadPose.Along(network, car.Segment, offset, car.Forward, car.Lane);
                var position = placement.OnGround(map.x, map.y);
                var rotation = Quaternion.LookRotation(new Vector3(heading.x, 0f, heading.y));
                if (!_cars.TryGetValue(car.Key, out var visual))
                {
                    visual = Take(car.Key);
                    _cars[car.Key] = visual;
                    visual.transform.SetPositionAndRotation(position, rotation);
                    continue;
                }
                var shown = visual.transform;
                if ((shown.position - position).sqrMagnitude > SnapDistance * SnapDistance) shown.SetPositionAndRotation(position, rotation);
                else shown.SetPositionAndRotation(Vector3.Lerp(shown.position, position, k), Quaternion.Slerp(shown.rotation, rotation, k));
            }
            foreach (var key in _cars.Keys.Where(x => !keep.Contains(x)).ToList())
            {
                Release(key, _cars[key]);
                _cars.Remove(key);
            }
        }

        private int Variant(uint key) => (int)(key % (uint)carPrefabs.Length);

        // A pooled car of the key's model, tinted with the key's colour.
        private GameObject Take(uint key)
        {
            var variant = Variant(key);
            GameObject visual = null;
            if (_pool.TryGetValue(variant, out var stack))
                while (stack.Count > 0 && visual == null) visual = stack.Pop();
            if (visual == null)
            {
                visual = Instantiate(carPrefabs[variant], transform);
                foreach (var collider in visual.GetComponentsInChildren<Collider>()) Destroy(collider);
            }
            visual.name = $"Car {key:x8}";
            visual.SetActive(true);
            if (tints.Length > 0)
            {
                _block ??= new MaterialPropertyBlock();
                var tint = tints[(int)((key >> 8) % (uint)tints.Length)];
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>())
                {
                    // Only the body material takes the tint (its name starts with the car body prefix).
                    for (var i = 0; i < renderer.sharedMaterials.Length; i++)
                    {
                        var material = renderer.sharedMaterials[i];
                        if (material == null || !material.name.StartsWith(BodyMaterialPrefix)) continue;
                        renderer.GetPropertyBlock(_block, i);
                        _block.SetColor(BaseColor, tint);
                        renderer.SetPropertyBlock(_block, i);
                    }
                }
            }
            return visual;
        }

        public const string BodyMaterialPrefix = "VH_Body";
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        private void Release(uint key, GameObject visual)
        {
            if (visual == null) return;
            visual.SetActive(false);
            var variant = Variant(key);
            if (!_pool.TryGetValue(variant, out var stack)) _pool[variant] = stack = new Stack<GameObject>();
            stack.Push(visual);
        }

        private void Clear()
        {
            foreach (var (key, visual) in _cars) Release(key, visual);
            _cars.Clear();
            _baseline = null;
        }
    }
}
