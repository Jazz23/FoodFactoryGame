// The phone's world map (decision 0047): draws the stored layout's districts, rivers, roads and every lot from above, coloured by
// what the lot is and who owns it, with the local player's position. Map x runs right and map z up (north). The wheel zooms about
// the pointer, dragging pans, and a click without a drag selects the lot under the pointer (LotClicked) or clears the selection.
// Presentation only: it reads the layout and a colour callback, and never changes state.
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

        public PhoneMapView()
        {
            name = "phone-map";
            style.flexGrow = 1;
            generateVisualContent += Draw;
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
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
                MarkDirtyRepaint();
            }
        }

        // Centres the view on a map point (zooming in a little if the whole map is shown).
        public void Focus(Vector2 map)
        {
            _centre = map;
            _zoom = Mathf.Max(_zoom, 2.5f);
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
