// TEST-ONLY: while the Editor is not the foreground app, UI Toolkit's runtime event system drops all device input
// (DefaultEventSystem.ShouldIgnoreEventsOnAppNotFocused), so tests that click with a virtual mouse pass or fail with window
// focus. This scope sets Unity's own Unity Remote hook, the static Func DefaultEventSystem.IsEditorRemoteConnected, to report
// a connection, which keeps device input flowing to panels; Dispose restores it. It fails loudly if a Unity upgrade removes it.
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    internal sealed class UnfocusedUiInput : IDisposable
    {
        private static readonly FieldInfo Hook = typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.DefaultEventSystem")
            ?.GetField("IsEditorRemoteConnected", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        private readonly object _previous;

        public UnfocusedUiInput()
        {
            Assert.That(Hook?.FieldType, Is.EqualTo(typeof(Func<bool>)), "DefaultEventSystem.IsEditorRemoteConnected changed; update UnfocusedUiInput.");
            _previous = Hook.GetValue(null);
            Hook.SetValue(null, (Func<bool>)(() => true));
        }

        public void Dispose() => Hook.SetValue(null, _previous);
    }
}
