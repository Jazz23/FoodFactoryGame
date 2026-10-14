// The phone's world map (decision 0047): draws the stored layout's districts, rivers, roads and every lot from above, coloured by
// what the lot is and who owns it, with the local player's position. Map x runs right and map z up (north). The wheel zooms about
// the pointer, dragging pans, and a click without a drag selects the lot under the pointer (LotClicked) or clears the selection.
// The company's trucks are small icons (SetTrucks) kept as child elements and only moved in place (a translate, no rebuild or
// layout pass) as their positions change; hovering one highlights it and shows its name, clicking it raises TruckClicked.
// Presentation only: it reads the layout, a colour callback and truck positions, and never changes state.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.WorldMap
{
    public sealed class PhoneMapView : VisualElement
    {
        // A truck icon: where it is on the map (metres) and its place among trucks standing at the same spot (drawn side by side).
        public readonly struct MapTruck
        {
            public readonly string Id;
            public readonly string Name;
            public readonly Vector2 Map;
            public readonly int Stack;

            public MapTruck(string id, string name, Vector2 map, int stack)
            {
                Id = id;
                Name = name;
                Map = map;
                Stack = stack;
            }
        }

        public const string TruckClass = "phone-map-truck";
        public const string TruckHighlightClass = "phone-map-truck--highlighted";
        // Pixels between icons of trucks standing at the same spot.
        private const float StackSpacing = 12f;

        private const float MinZoom = 1f;
        private const float MaxZoom = 12f;
        // Pointer travel (pixels) beyond which a press is a drag, not a click.
        private const float DragThreshold = 6f;

        private static readonly Color Ground = new(0.118f, 0.165f, 0.125f);
        private static readonly Color City = new(0.17f, 0.18f, 0.19f);
        private static readonly Color Water = new(0.22f, 0.42f, 0.62f);
        private static readonly Color Road = new(0.44f, 0.45f, 0.47f);
        private static readonly Color Highlight = new(1f, 1f, 1f);
        private static readonly Color PlayerDot = new(0.49f, 0.78f, 0.94f);

        private WorldLayout _layout;
        private float _zoom = MinZoom;
        // Map point at the view's centre.
        private Vector2 _centre;
        private Vector2 _pressAt;
        private Vector2 _lastPointer;
        private bool _pressed;
        private bool _dragged;

        // The lot's fill, or null to leave it out (scenery has no lot).
        public Func<string, Color?> LotColour { get; set; }
        public string SelectedLotId { get; set; }
        // The local player on the map, or null when unknown.
        public Vector2? Player { get; set; }
        public event Action<string> LotClicked;
        // A truck icon was clicked (its truck ID).
        public event Action<string> TruckClicked;

        private readonly VisualElement _truckLayer;
        private readonly Dictionary<string, (VisualElement Icon, Label Name)> _truckIcons = new();
        private readonly Dictionary<string, MapTruck> _trucks = new();
        private string _highlightedTruckId;

        public PhoneMapView()
        {
            name = "phone-map";
            style.flexGrow = 1;
            style.overflow = Overflow.Hidden;
            generateVisualContent += Draw;
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<GeometryChangedEvent>(_ => PlaceTrucks());
            // Icons are drawn over the map; the layer itself lets the pointer through to the map.
            _truckLayer = new VisualElement { name = "phone-map-trucks", pickingMode = PickingMode.Ignore };
            _truckLayer.style.position = Position.Absolute;
            _truckLayer.style.left = 0;
            _truckLayer.style.top = 0;
            _truckLayer.style.right = 0;
            _truckLayer.style.bottom = 0;
            Add(_truckLayer);
        }

        // The truck drawn highlighted (the one chosen in the Trucks app), or null.
        public string HighlightedTruckId
        {
            get => _highlightedTruckId;
            set
            {
                if (value == _highlightedTruckId) return;
                _highlightedTruckId = value;
                foreach (var (id, parts) in _truckIcons) parts.Icon.EnableInClassList(TruckHighlightClass, id == value);
            }
        }

        // The truck icon under the pointer, or null.
        public string HoveredTruckId { get; private set; }

        public IReadOnlyCollection<string> TruckIds => _truckIcons.Keys;

        // Shows exactly these trucks. Icons are created or removed only when the set of trucks changes; otherwise they are
        // moved in place, so calling this every frame costs no rebuild.
        public void SetTrucks(IReadOnlyList<MapTruck> trucks)
        {
            _trucks.Clear();
            foreach (var truck in trucks ?? Array.Empty<MapTruck>())
                if (!string.IsNullOrEmpty(truck.Id)) _trucks[truck.Id] = truck;
            foreach (var gone in _truckIcons.Keys.Where(x => !_trucks.ContainsKey(x)).ToList())
            {
                _truckIcons[gone].Icon.RemoveFromHierarchy();
                _truckIcons.Remove(gone);
                if (HoveredTruckId == gone) HoveredTruckId = null;
            }
            foreach (var truck in _trucks.Values)
            {
                if (!_truckIcons.TryGetValue(truck.Id, out var parts))
                {
                    parts = CreateTruckIcon(truck.Id);
                    _truckIcons[truck.Id] = parts;
                    _truckLayer.Add(parts.Icon);
                }
                if (parts.Name.text != truck.Name) parts.Name.text = truck.Name;
            }
            PlaceTrucks();
        }

        private (VisualElement Icon, Label Name) CreateTruckIcon(string truckId)
        {
            var icon = new VisualElement { name = "phone-map-truck-" + truckId, pickingMode = PickingMode.Position };
            icon.AddToClassList(TruckClass);
            icon.EnableInClassList(TruckHighlightClass, truckId == _highlightedTruckId);
            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.AddToClassList("phone-map-truck__body");
            var cab = new VisualElement { pickingMode = PickingMode.Ignore };
            cab.AddToClassList("phone-map-truck__cab");
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.AddToClassList("phone-map-truck__name");
            icon.Add(body);
            icon.Add(cab);
            icon.Add(label);
            icon.RegisterCallback<PointerEnterEvent>(_ => HoveredTruckId = truckId);
            icon.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (HoveredTruckId == truckId) HoveredTruckId = null;
            });
            // The press is the icon's, not the start of a map drag or a lot click.
            icon.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0) evt.StopPropagation();
            });
            icon.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button != 0 || _pressed) return;
                evt.StopPropagation();
                TruckClicked?.Invoke(truckId);
            });
            return (icon, label);
        }

        // Moves every icon to its truck's place at the current zoom and pan; one off the view is hidden.
        private void PlaceTrucks()
        {
            if (_truckIcons.Count == 0) return;
            var size = contentRect.size;
            foreach (var (id, parts) in _truckIcons)
            {
                if (_layout == null || !_trucks.TryGetValue(id, out var truck) || size.x < 1f || size.y < 1f)
                {
                    parts.Icon.style.visibility = Visibility.Hidden;
                    continue;
                }
                var local = ToLocal(truck.Map) + new Vector2(truck.Stack * StackSpacing, 0f);
                var inside = local.x >= -8f && local.y >= -8f && local.x <= size.x + 8f && local.y <= size.y + 8f;
                parts.Icon.style.visibility = inside ? Visibility.Visible : Visibility.Hidden;
                parts.Icon.style.translate = new Translate(local.x, local.y);
            }
        }

        public WorldLayout Layout
        {
            get => _layout;
            set
            {
                if (ReferenceEquals(value, _layout)) return;
                _layout = value;
                _zoom = MinZoom;
                _centre = Vector2.zero;
                PlaceTrucks();
                MarkDirtyRepaint();
            }
        }

        // Centres the view on a map point (zooming in a little if the whole map is shown).
        public void Focus(Vector2 map)
        {
            _centre = map;
            _zoom = Mathf.Max(_zoom, 2.5f);
            PlaceTrucks();
            MarkDirtyRepaint();
        }

        private float Scale => _layout == null ? 1f
            : Mathf.Min(contentRect.width, contentRect.height) / Mathf.Max(1f, 2f * _layout.MapHalfSize) * _zoom;

        public Vector2 ToLocal(Vector2 map)
        {
            var size = contentRect.size;
            var scale = Scale;
            return new Vector2(size.x * 0.5f + (map.x - _centre.x) * scale, size.y * 0.5f - (map.y - _centre.y) * scale);
        }

        public Vector2 ToMap(Vector2 local)
        {
            var size = contentRect.size;
            var scale = Mathf.Max(0.0001f, Scale);
            return new Vector2(_centre.x + (local.x - size.x * 0.5f) / scale, _centre.y - (local.y - size.y * 0.5f) / scale);
        }

        // The lot whose rectangle holds a map point (the smallest one if several overlap), or null.
        public string LotAt(Vector2 map) => _layout?.Lots
            .Where(x => map.x >= x.X && map.x < x.X + x.Width && map.y >= x.Z && map.y < x.Z + x.Depth)
            .OrderBy(x => x.Width * x.Depth).Select(x => x.Id).FirstOrDefault();

        private void OnWheel(WheelEvent evt)
        {
            if (_layout == null) return;
            var before = ToMap(evt.localMousePosition);
            _zoom = Mathf.Clamp(_zoom * (evt.delta.y > 0 ? 1f / 1.2f : 1.2f), MinZoom, MaxZoom);
            // Keep the point under the pointer where it is.
            _centre += before - ToMap(evt.localMousePosition);
            Clamp();
            PlaceTrucks();
            MarkDirtyRepaint();
            evt.StopPropagation();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _pressed = true;
            _dragged = false;
            _pressAt = _lastPointer = evt.localPosition;
            this.CapturePointer(evt.pointerId);
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_pressed) return;
            Vector2 position = evt.localPosition;
            if ((position - _pressAt).sqrMagnitude > DragThreshold * DragThreshold) _dragged = true;
            if (_dragged)
            {
                _centre += new Vector2(-(position.x - _lastPointer.x), position.y - _lastPointer.y) / Mathf.Max(0.0001f, Scale);
                Clamp();
                PlaceTrucks();
                MarkDirtyRepaint();
            }
            _lastPointer = position;
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (!_pressed) return;
            _pressed = false;
            this.ReleasePointer(evt.pointerId);
            if (_dragged || _layout == null) return;
            LotClicked?.Invoke(LotAt(ToMap(evt.localPosition)));
        }

        private void Clamp()
        {
            if (_layout == null) return;
            var half = _layout.MapHalfSize;
            _centre = new Vector2(Mathf.Clamp(_centre.x, -half, half), Mathf.Clamp(_centre.y, -half, half));
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_layout == null || contentRect.width < 1f || contentRect.height < 1f) return;
            var painter = context.painter2D;
            var scale = Scale;
            var half = _layout.MapHalfSize;
            Rect(painter, -half, -half, 2 * half, 2 * half, Ground);
            foreach (var area in _layout.Districts.SelectMany(x => x.Areas)) Rect(painter, area.X, area.Z, area.Width, area.Depth, City);
            foreach (var river in _layout.Rivers) Line(painter, river.Points.Select(x => new Vector2(x.X, x.Z)), Mathf.Max(3f, river.Width * scale), Water);
            var nodes = _layout.Nodes.ToDictionary(x => x.Id);
            foreach (var road in _layout.Roads)
            {
                if (!nodes.TryGetValue(road.FromId, out var from) || !nodes.TryGetValue(road.ToId, out var to)) continue;
                Line(painter, new[] { new Vector2(from.X, from.Z), new Vector2(to.X, to.Z) }, Mathf.Max(1.5f, road.Width * scale), Road);
            }
            WorldLot selected = null;
            foreach (var lot in _layout.Lots)
            {
                var colour = LotColour?.Invoke(lot.Id);
                if (colour == null) continue;
                Rect(painter, lot.X, lot.Z, lot.Width, lot.Depth, colour.Value);
                if (lot.Id == SelectedLotId) selected = lot;
            }
            if (selected != null) Outline(painter, selected.X, selected.Z, selected.Width, selected.Depth, Highlight, 3f);
            if (Player is { } player)
            {
                painter.fillColor = PlayerDot;
                painter.strokeColor = Color.white;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(ToLocal(player), 6f, 0f, 360f);
                painter.Fill();
                painter.Stroke();
            }
        }

        private void Rect(Painter2D painter, float x, float z, float width, float depth, Color colour)
        {
            var a = ToLocal(new Vector2(x, z + depth));
            var b = ToLocal(new Vector2(x + width, z));
            painter.fillColor = colour;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(new Vector2(b.x, a.y));
            painter.LineTo(b);
            painter.LineTo(new Vector2(a.x, b.y));
            painter.ClosePath();
            painter.Fill();
        }

        private void Outline(Painter2D painter, float x, float z, float width, float depth, Color colour, float lineWidth)
        {
            var a = ToLocal(new Vector2(x, z + depth));
            var b = ToLocal(new Vector2(x + width, z));
            painter.strokeColor = colour;
            painter.lineWidth = lineWidth;
            painter.BeginPath();
            painter.MoveTo(a);
            painter.LineTo(new Vector2(b.x, a.y));
            painter.LineTo(b);
            painter.LineTo(new Vector2(a.x, b.y));
            painter.ClosePath();
            painter.Stroke();
        }

        private void Line(Painter2D painter, IEnumerable<Vector2> points, float width, Color colour)
        {
            var list = points.ToList();
            if (list.Count < 2) return;
            painter.strokeColor = colour;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            painter.MoveTo(ToLocal(list[0]));
            for (var index = 1; index < list.Count; index++) painter.LineTo(ToLocal(list[index]));
            painter.Stroke();
        }
    }
}
