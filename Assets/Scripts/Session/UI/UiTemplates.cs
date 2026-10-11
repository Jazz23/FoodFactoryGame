// Loads the UI's UXML from Assets/UI/Resources/UI (decision 0042) and clones it: a whole screen into a panel's root, or one
// repeated item (a slot, a row, a card) as a detached element the caller adds. Screens keep their logic in C# and find
// their parts by name; the look lives in the UXML and its USS. A missing template is an authoring error, so it throws.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    public static class UiTemplates
    {
        private static readonly Dictionary<string, VisualTreeAsset> Cache = new();

        // path is relative to Resources/UI without the extension, e.g. "Hud/Hud".
        public static VisualTreeAsset Asset(string path)
        {
            if (Cache.TryGetValue(path, out var cached) && cached != null) return cached;
            var asset = Resources.Load<VisualTreeAsset>("UI/" + path);
            if (asset == null) throw new InvalidOperationException($"UI template Resources/UI/{path}.uxml is missing.");
            Cache[path] = asset;
            return asset;
        }

        // Clones a template's elements into parent (and its <Style> sheets onto parent).
        public static void Fill(VisualElement parent, string path)
        {
            Asset(path).CloneTree(parent);
            Glyphs.Attach(parent);
        }

        // Clones a template that has exactly one root element and returns that element, detached.
        public static VisualElement Clone(string path)
        {
            var holder = new VisualElement();
            Asset(path).CloneTree(holder);
            if (holder.childCount != 1) throw new InvalidOperationException($"UI template {path} must have one root element.");
            var root = holder[0];
            root.RemoveFromHierarchy();
            Glyphs.Attach(root);
            return root;
        }

        // A named part of a cloned template; missing parts are authoring errors.
        public static T Part<T>(this VisualElement root, string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"UI part {name} ({typeof(T).Name}) is missing under {root.name}.");

        public static VisualElement Part(this VisualElement root, string name) => root.Part<VisualElement>(name);

        // The first element with a class under root (template parts that repeat, so they have no unique name).
        public static T PartByClass<T>(this VisualElement root, string className) where T : VisualElement =>
            root.Q<T>(className: className) ?? throw new InvalidOperationException($"UI part .{className} ({typeof(T).Name}) is missing under {root.name}.");

        public static void Show(this VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        // Sets a label's text only when it changed, so per-frame refreshes do not re-layout.
        public static void SetText(this TextElement element, string text)
        {
            if (element.text != text) element.text = text;
        }

        // An item or machine icon on an element styled by USS (ff-icon); with no sprite its name stands in.
        public static void SetIcon(this VisualElement icon, Sprite sprite, string name)
        {
            icon.Clear();
            if (sprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
                return;
            }
            icon.style.backgroundImage = StyleKeyword.None;
            var label = new Label(name) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ff-icon__name");
            icon.Add(label);
        }
    }
}
