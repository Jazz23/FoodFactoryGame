// A one-line tip drawn beside the pointer while it rests on a target that wants one, such as a disabled Buy button saying why it
// cannot be pressed. Disabled elements take no pointer events, so the pointer is read each frame from the Player/Point action (the
// one action kept enabled while a screen suspends gameplay input) and tested against the target's bounds. Presentation only.
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.WorldMap
{
    public sealed class CursorTooltip
    {
        private readonly VisualElement _root;
        private readonly Label _label;
        private InputAction _point;

        // What the tip shows now (empty while hidden), for tests and captures.
        public string Text => _label.style.display == DisplayStyle.None ? "" : _label.text;

        public CursorTooltip(VisualElement root, string name)
        {
            _root = root;
            _label = new Label { name = name, pickingMode = PickingMode.Ignore };
            _label.AddToClassList("ff-small");
            var style = _label.style;
            style.position = Position.Absolute;
            style.maxWidth = 320;
            style.whiteSpace = WhiteSpace.Normal;
            style.paddingLeft = style.paddingRight = 8;
            style.paddingTop = style.paddingBottom = 5;
            style.backgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.95f);
            style.color = new Color(0.96f, 0.96f, 0.96f);
            style.borderTopLeftRadius = style.borderTopRightRadius = style.borderBottomLeftRadius = style.borderBottomRightRadius = 5;
            style.borderLeftWidth = style.borderRightWidth = style.borderTopWidth = style.borderBottomWidth = 1;
            style.borderLeftColor = style.borderRightColor = style.borderTopColor = style.borderBottomColor = new Color(1f, 1f, 1f, 0.25f);
            style.display = DisplayStyle.None;
            root.Add(_label);
        }

        // Shows text beside the pointer while the pointer is over target (attached and shown); hides the tip otherwise.
        public void Show(VisualElement target, string text)
        {
            var at = PointerOnPanel();
            var show = !string.IsNullOrEmpty(text) && target?.panel != null && at is { } point && target.resolvedStyle.display != DisplayStyle.None
                       && target.worldBound.Contains(point);
            _label.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            if (_label.text != text) _label.text = text;
            var local = _root.WorldToLocal(at.Value);
            // Kept inside the panel: flipped to the pointer's left near the right edge.
            var width = _label.resolvedStyle.width;
            var left = local.x + 14f;
            if (!float.IsNaN(width) && left + width > _root.layout.width) left = local.x - 8f - width;
            _label.style.left = left;
            _label.style.top = local.y + 18f;
            _label.BringToFront();
        }

        public void Hide() => _label.style.display = DisplayStyle.None;

        // The pointer in panel coordinates, or null when it is unknown.
        private Vector2? PointerOnPanel()
        {
            if (_root.panel == null) return null;
            _point ??= InputSystem.actions?.FindAction("Player/Point");
            Vector2 screen;
            if (_point is { enabled: true }) screen = _point.ReadValue<Vector2>();
            else if (Pointer.current != null) screen = Pointer.current.position.ReadValue();
            else return null;
            return RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, UnityEngine.Screen.height - screen.y));
        }
    }
}
