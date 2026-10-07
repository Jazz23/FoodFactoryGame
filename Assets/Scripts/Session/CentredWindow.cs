// Centres a UI Toolkit window on the screen inside a full-screen overlay that never takes a click. A percentage translate
// (translate: -50% -50%) is not used: when a window grows after its first layout, UI Toolkit keeps drawing it with the
// translate resolved at the old size while picking uses the new one, so the drawn controls stop receiving clicks
// (starting-loop finding P0-01, the logistics truck card). Flex centring has no such offset.
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    public static class CentredWindow
    {
        // Returns the overlay holding window; add the overlay to the panel root. The window is limited to the screen.
        public static VisualElement Overlay(string name, VisualElement window)
        {
            var overlay = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = overlay.style.top = overlay.style.right = overlay.style.bottom = 0;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            window.style.maxWidth = new Length(100, LengthUnit.Percent);
            window.style.maxHeight = new Length(100, LengthUnit.Percent);
            overlay.Add(window);
            return overlay;
        }
    }
}

