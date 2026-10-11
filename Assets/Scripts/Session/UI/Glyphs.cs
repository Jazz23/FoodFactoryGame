// Line icons for the UXML screens (decision 0042), drawn rather than typed so they never depend on the font's coverage. A
// template marks an element with "ff-glyph" and one kind class ("ff-glyph--close"); Attach gives it a painter that draws in
// the element's resolved text colour, so USS (including :hover and state classes) chooses the colour, and its size.
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    public static class Glyphs
    {
        public const string Class = "ff-glyph";
        private const string Bound = "ff-glyph--bound";

        // Gives every unbound glyph under root (root included) its painter.
        public static void Attach(VisualElement root)
        {
            root.Query(className: Class).ForEach(Bind);
        }

        // A glyph element of one kind for code that builds elements itself.
        public static VisualElement Create(string kind)
        {
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList(Class);
            glyph.AddToClassList(Class + "--" + kind);
            Bind(glyph);
            return glyph;
        }

        private static void Bind(VisualElement glyph)
        {
            if (glyph.ClassListContains(Bound)) return;
            glyph.AddToClassList(Bound);
            glyph.pickingMode = PickingMode.Ignore;
            glyph.generateVisualContent += context => Draw(glyph, context.painter2D);
        }

        private static void Draw(VisualElement glyph, Painter2D painter)
        {
            var w = glyph.contentRect.width;
            var h = glyph.contentRect.height;
            if (w <= 0f || h <= 0f) return;
            var color = glyph.resolvedStyle.color;
            Vector2 P(float x, float y) => new(w * x, h * y);
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineWidth = Mathf.Max(1.5f, w / 9f);
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            if (glyph.ClassListContains(Class + "--play"))
            {
                painter.MoveTo(P(0.22f, 0.12f));
                painter.LineTo(P(0.9f, 0.5f));
                painter.LineTo(P(0.22f, 0.88f));
                painter.ClosePath();
                painter.Fill();
                return;
            }
            if (glyph.ClassListContains(Class + "--server"))
            {
                // Two stacked units, each with a filled status light.
                Line(painter, P(0.125f, 0.167f), P(0.875f, 0.167f), P(0.875f, 0.458f), P(0.125f, 0.458f));
                painter.ClosePath();
                Line(painter, P(0.125f, 0.542f), P(0.875f, 0.542f), P(0.875f, 0.833f), P(0.125f, 0.833f));
                painter.ClosePath();
                painter.Stroke();
                painter.BeginPath();
                Circle(painter, P(0.292f, 0.3125f), w * 0.06f);
                Circle(painter, P(0.292f, 0.6875f), w * 0.06f);
                painter.Fill();
                return;
            }
            if (glyph.ClassListContains(Class + "--chevron-right")) Line(painter, P(0.38f, 0.25f), P(0.63f, 0.5f), P(0.38f, 0.75f));
            else if (glyph.ClassListContains(Class + "--chevron-left")) Line(painter, P(0.62f, 0.25f), P(0.37f, 0.5f), P(0.62f, 0.75f));
            else if (glyph.ClassListContains(Class + "--chevron-down")) Line(painter, P(0.25f, 0.38f), P(0.5f, 0.63f), P(0.75f, 0.38f));
            else if (glyph.ClassListContains(Class + "--chevron-up")) Line(painter, P(0.25f, 0.62f), P(0.5f, 0.37f), P(0.75f, 0.62f));
            else if (glyph.ClassListContains(Class + "--check")) Line(painter, P(0.2f, 0.52f), P(0.42f, 0.72f), P(0.8f, 0.3f));
            else if (glyph.ClassListContains(Class + "--close"))
            {
                Line(painter, P(0.26f, 0.26f), P(0.74f, 0.74f));
                Line(painter, P(0.74f, 0.26f), P(0.26f, 0.74f));
            }
            else if (glyph.ClassListContains(Class + "--plus"))
            {
                Line(painter, P(0.5f, 0.2f), P(0.5f, 0.8f));
                Line(painter, P(0.2f, 0.5f), P(0.8f, 0.5f));
            }
            else if (glyph.ClassListContains(Class + "--factory"))
            {
                Line(painter, P(0.12f, 0.88f), P(0.12f, 0.42f), P(0.33f, 0.54f), P(0.33f, 0.42f), P(0.54f, 0.54f), P(0.54f, 0.25f),
                    P(0.88f, 0.42f), P(0.88f, 0.88f));
                painter.ClosePath();
            }
            else if (glyph.ClassListContains(Class + "--home"))
            {
                Line(painter, P(0.125f, 0.417f), P(0.5f, 0.167f), P(0.875f, 0.417f), P(0.875f, 0.833f), P(0.125f, 0.833f));
                painter.ClosePath();
                Line(painter, P(0.375f, 0.833f), P(0.375f, 0.583f), P(0.625f, 0.583f), P(0.625f, 0.833f));
            }
            else if (glyph.ClassListContains(Class + "--sliders"))
            {
                // Three tracks, each broken by its knob.
                var knob = w / 12f;
                Line(painter, P(0.167f, 0.25f), P(0.583f, 0.25f));
                Line(painter, P(0.75f, 0.25f), P(0.833f, 0.25f));
                Circle(painter, P(0.667f, 0.25f), knob);
                Line(painter, P(0.167f, 0.5f), P(0.333f, 0.5f));
                Line(painter, P(0.5f, 0.5f), P(0.833f, 0.5f));
                Circle(painter, P(0.417f, 0.5f), knob);
                Line(painter, P(0.167f, 0.75f), P(0.667f, 0.75f));
                Circle(painter, P(0.75f, 0.75f), knob);
            }
            else if (glyph.ClassListContains(Class + "--exit"))
            {
                Line(painter, P(0.583f, 0.167f), P(0.792f, 0.167f), P(0.792f, 0.833f), P(0.583f, 0.833f));
                Line(painter, P(0.417f, 0.333f), P(0.25f, 0.5f), P(0.417f, 0.667f));
                Line(painter, P(0.25f, 0.5f), P(0.667f, 0.5f));
            }
            else if (glyph.ClassListContains(Class + "--arrow-right"))
            {
                Line(painter, P(0.06f, 0.5f), P(0.9f, 0.5f));
                Line(painter, P(0.68f, 0.18f), P(0.9f, 0.5f), P(0.68f, 0.82f));
            }
            painter.Stroke();
        }

        private static void Line(Painter2D painter, params Vector2[] points)
        {
            painter.MoveTo(points[0]);
            for (var index = 1; index < points.Length; index++) painter.LineTo(points[index]);
        }

        // A closed circle as its own subpath.
        private static void Circle(Painter2D painter, Vector2 center, float radius)
        {
            painter.MoveTo(center + new Vector2(radius, 0f));
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
        }
    }
}
