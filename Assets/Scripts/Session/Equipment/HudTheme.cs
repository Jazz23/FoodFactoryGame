// The look shared by the HUD and every screen built in code (PlayerHud, EmployeeScriptPanel): one dark palette with a single
// amber accent, rounded panels and flat slots, so the screens read as one system. Inline styles beat USS pseudo-classes, so
// buttons get their hover colour from pointer callbacks here. Presentation only.
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Equipment
{
    public enum HudButton
    {
        // Filled amber: the one main action of a screen (Run, Hire).
        Primary,
        // Outlined neutral: everything else.
        Secondary,
        // Outlined red: removing something (Fire).
        Danger
    }

    public static class HudTheme
    {
        public static readonly Color Panel = Hex(0x1B1C1F);
        // HUD cards over the world are slightly see-through.
        public static readonly Color Overlay = Hex(0x16171A, 0.86f);
        public static readonly Color Edge = Hex(0x34373C);
        public static readonly Color Inset = Hex(0x141517);
        public static readonly Color Card = Hex(0x232528);
        public static readonly Color CardEdge = Hex(0x2E3135);
        public static readonly Color Control = Hex(0x2A2C30);
        public static readonly Color ControlEdge = Hex(0x3C3F44);
        public static readonly Color Slot = Hex(0x26282C);
        public static readonly Color SlotEmpty = Hex(0x222327);
        public static readonly Color Dim = Hex(0x0A0A0C, 0.55f);
        public static readonly Color Text = Hex(0xECE8E1);
        public static readonly Color TextSoft = Hex(0xCFCBC3);
        public static readonly Color Muted = Hex(0xA8A49B);
        public static readonly Color Faint = Hex(0x8F8B83);
        public static readonly Color Ink = Hex(0x16171A);
        public static readonly Color Accent = Hex(0xF2A33A);
        public static readonly Color AccentHover = Hex(0xFFB95A);
        public static readonly Color Positive = Hex(0x6FCB86);
        public static readonly Color PowerOn = Hex(0x3E9A57);
        public static readonly Color Danger = Hex(0xE5644E);
        public static readonly Color DangerText = Hex(0xFF8A78);
        public static readonly Color DangerEdge = Hex(0x5A3A36);
        public static readonly Color Warning = Hex(0xFF9A6B);
        public static readonly Color Chilled = Hex(0x7CC8F0);
        public static readonly Color SpoiledFill = Hex(0x3A2523);

        // USS class (Assets/UI/Hud.uss, imported by the runtime theme) giving a dark text box a light caret and selection.
        public const string TextFieldClass = "hud-text-field";

        public static Color Hex(int rgb, float alpha = 1f) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

        // A screen window: solid panel, hairline edge, 14 px corners.
        public static void StylePanel(VisualElement element, int padding = 18)
        {
            element.style.backgroundColor = Panel;
            Border(element, Edge, 1);
            Radius(element, 14);
            Pad(element, padding);
        }

        // A HUD card floating over the world.
        public static void StyleOverlay(VisualElement element)
        {
            element.style.backgroundColor = Overlay;
            Border(element, Edge, 1);
            Radius(element, 12);
            element.style.paddingLeft = element.style.paddingRight = 14;
            element.style.paddingTop = element.style.paddingBottom = 10;
        }

        public static Label Label(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        // Small upper-case caption over a value ("CASH", "IN").
        public static Label Eyebrow(string text) => Label(text.ToUpperInvariant(), 11, Muted);

        public static Button Button(string name, string text, System.Action clicked, HudButton kind = HudButton.Secondary, int height = 36)
        {
            // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
            var button = new Button(clicked) { name = name, text = text, focusable = false };
            StyleButton(button, kind, height);
            return button;
        }

        public static void StyleButton(Button button, HudButton kind, int height = 36)
        {
            var (rest, hover, edge, text) = kind switch
            {
                HudButton.Primary => (Accent, AccentHover, Accent, Ink),
                HudButton.Danger => (new Color(0, 0, 0, 0), Hex(0x5A3A36, 0.35f), DangerEdge, DangerText),
                _ => (Control, Hex(0x34373C), ControlEdge, Text)
            };
            button.style.height = height;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
            button.style.paddingLeft = button.style.paddingRight = kind == HudButton.Primary ? 20 : 12;
            button.style.paddingTop = button.style.paddingBottom = 0;
            button.style.backgroundColor = rest;
            button.style.color = text;
            button.style.fontSize = kind == HudButton.Primary ? 14 : 13;
            button.style.unityFontStyleAndWeight = kind == HudButton.Primary ? FontStyle.Bold : FontStyle.Normal;
            Border(button, edge, kind == HudButton.Primary ? 0 : 1);
            Radius(button, height >= 44 ? 10 : 8);
            button.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (button.enabledInHierarchy) button.style.backgroundColor = hover;
            });
            button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = rest);
        }

        // A square icon-only button (close, remove).
        public static Button IconButton(string name, string glyph, System.Action clicked, int size = 32)
        {
            var button = Button(name, glyph, clicked, HudButton.Secondary, size);
            button.style.width = size;
            button.style.paddingLeft = button.style.paddingRight = 0;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.color = Muted;
            return button;
        }

        // A segmented control: one option button per choice, the chosen one lit.
        public static VisualElement Segmented()
        {
            var group = new VisualElement();
            group.style.flexDirection = FlexDirection.Row;
            group.style.backgroundColor = Inset;
            Radius(group, 10);
            Pad(group, 3);
            return group;
        }

        public static Button Segment(string name, string text, bool chosen, System.Action clicked)
        {
            var button = new Button(clicked) { name = name, text = text, focusable = false };
            button.style.height = 34;
            button.style.minWidth = 44;
            button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
            button.style.paddingLeft = button.style.paddingRight = 12;
            button.style.backgroundColor = chosen ? Edge : new Color(0, 0, 0, 0);
            button.style.color = chosen ? Text : Muted;
            button.style.fontSize = 13;
            Border(button, Edge, 0);
            Radius(button, 8);
            return button;
        }

        // A thin rounded progress track; returns the fill to size by percent.
        public static VisualElement Progress(string name, out VisualElement fill, int height = 6)
        {
            var track = new VisualElement { name = name };
            track.style.height = height;
            track.style.backgroundColor = Control;
            Radius(track, height / 2f);
            track.style.overflow = Overflow.Hidden;
            fill = new VisualElement { name = name + "-fill", pickingMode = PickingMode.Ignore };
            fill.style.height = new Length(100, LengthUnit.Percent);
            fill.style.backgroundColor = Accent;
            Radius(fill, height / 2f);
            track.Add(fill);
            return track;
        }

        // A thin dark scroll bar without arrow buttons, in place of the default light one.
        public static void StyleScroller(ScrollView view)
        {
            view.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            view.verticalScrollerVisibility = ScrollerVisibility.Auto;
            var scroller = view.verticalScroller;
            scroller.lowButton.style.display = scroller.highButton.style.display = DisplayStyle.None;
            scroller.style.width = 8;
            scroller.style.marginLeft = 6;
            scroller.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Border(scroller, new Color(0f, 0f, 0f, 0f), 0);
            var slider = scroller.slider;
            slider.style.marginTop = slider.style.marginBottom = 0;
            var tracker = slider.Q(className: BaseSlider<float>.trackerUssClassName);
            if (tracker != null)
            {
                tracker.style.backgroundColor = Inset;
                Border(tracker, Inset, 0);
                Radius(tracker, 4);
            }
            var dragger = slider.Q(className: BaseSlider<float>.draggerUssClassName);
            if (dragger != null)
            {
                dragger.style.backgroundColor = ControlEdge;
                Border(dragger, ControlEdge, 0);
                Radius(dragger, 4);
            }
        }

        // A checkbox's box drawn dark with an amber tick.
        public static void StyleToggle(Toggle toggle)
        {
            var box = toggle.Q(className: Toggle.checkmarkUssClassName);
            if (box == null) return;
            box.style.backgroundColor = Panel;
            Border(box, ControlEdge, 1);
            Radius(box, 4);
            box.style.unityBackgroundImageTintColor = Accent;
        }

        // A round dot (status lights).
        public static VisualElement Dot(Color color, int size = 8)
        {
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.width = dot.style.height = size;
            dot.style.flexShrink = 0;
            dot.style.backgroundColor = color;
            Radius(dot, size / 2f);
            return dot;
        }

        // A round avatar placeholder with an initial.
        public static VisualElement Avatar(string name, int size = 36)
        {
            var avatar = new VisualElement { pickingMode = PickingMode.Ignore };
            avatar.style.width = avatar.style.height = size;
            avatar.style.flexShrink = 0;
            avatar.style.backgroundColor = Edge;
            avatar.style.alignItems = Align.Center;
            avatar.style.justifyContent = Justify.Center;
            Radius(avatar, size / 2f);
            var initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            avatar.Add(Label(initial, size / 2 - 2, Text, true));
            return avatar;
        }

        // A centred row; call Gap once its children are in.
        public static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        public static VisualElement Spacer()
        {
            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1;
            return spacer;
        }

        public static VisualElement Divider()
        {
            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.style.height = 1;
            line.style.backgroundColor = Edge;
            line.style.marginTop = line.style.marginBottom = 4;
            return line;
        }

        // Flexbox gap stand-in: a left margin on every child after the first of a row.
        public static void Gap(VisualElement row, int gap)
        {
            for (var index = 1; index < row.childCount; index++) row[index].style.marginLeft = gap;
        }

        public static void Pad(VisualElement element, int padding) =>
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;

        public static void Border(VisualElement element, Color color, int width)
        {
            element.style.borderTopWidth = element.style.borderLeftWidth = element.style.borderBottomWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderLeftColor = element.style.borderBottomColor = element.style.borderRightColor = color;
        }

        public static void Radius(VisualElement element, float radius) =>
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
