// Opening screens from the world through the real SampleScene host (the scene with the employee prefab): the employee script
// screen opens without focusing its text box, so the E press that opened it is never typed into the script, and the hidden
// text box gives up focus once closed, and the left click that opened it does not focus it either; E closes it unless the text box has focus; a left click opens the storage only while it is the highlighted hover target. Every
// save and identity path is a unique temporary directory.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class ScreenOpeningTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private EquipmentInteraction _interaction;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++) yield return null;
        }

        private static IEnumerator Press(Action press, Action release)
        {
            press();
            yield return Frames(3);
            release();
            yield return Frames(3);
        }

        private IEnumerator Key(Key key) => Press(() => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key)),
            () => InputSystem.QueueStateEvent(_keyboard, new KeyboardState()));

        private IEnumerator LeftClick() => Press(() => InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left)),
            () => InputSystem.QueueStateEvent(_mouse, new MouseState()));

        // Keyboard text reaches UI Toolkit from the OS (IMGUI events), not from the Input System test devices, so typing is sent
        // as UI key events to whatever has focus: the key down, then its character, as the OS delivers them.
        private static void Type(EmployeeScriptPanel panel, KeyCode key, char character)
        {
            if (panel.SourceField.focusController?.focusedElement != panel.SourceField) return;
            var editor = panel.SourceField.Q<TextElement>();
            using (var down = KeyDownEvent.GetPooled('\0', key, EventModifiers.None)) editor.SendEvent(down);
            using (var typed = KeyDownEvent.GetPooled(character, KeyCode.None, EventModifiers.None)) editor.SendEvent(typed);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryScreenOpeningPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Needs the editor to load a scene outside the build list.");
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host", Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            _interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null, "host baseline");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_root != null) _root.Shutdown();
                _root = null;
                yield return null;
                if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            finally
            {
                _input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator EmployeeScriptOpensUnfocusedAndLosesFocusWhenClosed()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            EmployeeWorker employee = null;
            yield return Until(() => (employee = UnityEngine.Object.FindAnyObjectByType<EmployeeWorker>()) != null
                && !string.IsNullOrEmpty(employee.EmployeeId), "dev employee spawned");

            // Opened by E: its "e" arrives after the screen opened, as logged in play, and nothing takes it.
            _interaction.OpenEmployeeScreen(employee);
            yield return Frames(2);
            var opened = panel.SourceField.value;
            Type(panel, KeyCode.E, 'e');
            yield return Frames(3);
            Assert.That(panel.Window.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(panel.SourceField.focusController?.focusedElement, Is.Null, "The text box is not focused on opening.");
            Assert.That(panel.SourceField.value, Is.EqualTo(opened), "The opening E was typed into the script.");

            // Once the player focuses it (a click), typing works.
            panel.SourceField.Focus();
            yield return null;
            Type(panel, KeyCode.X, 'x');
            Assert.That(panel.SourceField.value.Length, Is.EqualTo(opened.Length + 1), "Typing into the focused text box.");

            // Esc closes and the hidden text box gives up focus.
            yield return Key(UnityEngine.InputSystem.Key.Escape);
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.None));
            yield return Frames(2);
            Assert.That(panel.SourceField.focusController?.focusedElement, Is.Null, "The hidden text box kept focus.");
        }

        [UnityTest]
        public IEnumerator EClosesEmployeeScriptUnlessTypingInIt()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            EmployeeWorker employee = null;
            yield return Until(() => (employee = UnityEngine.Object.FindAnyObjectByType<EmployeeWorker>()) != null
                && !string.IsNullOrEmpty(employee.EmployeeId), "dev employee spawned");

            _interaction.OpenEmployeeScreen(employee);
            yield return Frames(2);
            yield return Key(UnityEngine.InputSystem.Key.E);
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.None), "E did not close the unfocused script screen.");

            // With the text box focused, E is text: the screen stays open.
            _interaction.OpenEmployeeScreen(employee);
            yield return Frames(2);
            panel.SourceField.Focus();
            yield return null;
            yield return Key(UnityEngine.InputSystem.Key.E);
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.Employee), "E closed the screen while typing.");

            // Once the text box loses focus, E closes it again.
            panel.SourceField.Blur();
            yield return null;
            yield return Key(UnityEngine.InputSystem.Key.E);
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.None), "E did not close the screen after the text box lost focus.");
        }

        // A left press on the text box, as UI Toolkit delivers it from the pointer.
        private static void PressTextBox(EmployeeScriptPanel panel)
        {
            var input = panel.SourceField.Q(TextField.textInputUssName);
            var position = input.worldBound.center;
            using var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = position });
            input.SendEvent(down);
            using var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = position });
            input.SendEvent(up);
        }

        [UnityTest]
        public IEnumerator EmployeeScriptOpenedByLeftClickIsNotFocusedByThatClick()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            EmployeeWorker employee = null;
            yield return Until(() => (employee = UnityEngine.Object.FindAnyObjectByType<EmployeeWorker>()) != null
                && !string.IsNullOrEmpty(employee.EmployeeId), "dev employee spawned");

            // The opening press is still held when the screen appears under the pointer; it reaches the text box late.
            InputSystem.QueueStateEvent(_mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return Frames(2);
            _interaction.OpenEmployeeScreen(employee);
            yield return Frames(3);
            PressTextBox(panel);
            yield return null;
            Assert.That(panel.Window.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(panel.SourceField.focusController?.focusedElement, Is.Null, "The opening click focused the text box.");

            // After that press is released, a click focuses the text box.
            InputSystem.QueueStateEvent(_mouse, new MouseState());
            yield return Frames(3);
            PressTextBox(panel);
            yield return null;
            Assert.That(panel.SourceField.focusController?.focusedElement, Is.EqualTo(panel.SourceField), "A later click did not focus the text box.");
        }

        [UnityTest]
        public IEnumerator LeftClickOpensTheStorageOnlyWhenHighlighted()
        {
            PlayerAvatar avatar = null;
            yield return Until(() => (avatar = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsOwner)) != null
                && _interaction.PointerLocked, "owned avatar and locked pointer");
            // A steep camera puts the crosshair's floor point just ahead of the avatar; moving the avatar moves the aim.
            typeof(OrbitCameraRig).GetField("pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(avatar.CameraRig, 80f);
            typeof(OrbitCameraRig).GetField("yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(avatar.CameraRig, 0f);
            yield return Frames(2);
            var camera = avatar.CameraRig.GetComponentInChildren<Camera>();
            var ray = camera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            Assert.That(new Plane(Vector3.up, avatar.transform.position).Raycast(ray, out var distance), Is.True);
            var aimOffset = ray.GetPoint(distance) - avatar.transform.position;
            aimOffset.y = 0f;
            var storage = UnityEngine.Object.FindObjectsByType<SiteLocationMarker>(FindObjectsSortMode.None).Single(x => x.LocationId == DevWorld.StorageId);
            var controller = avatar.GetComponent<CharacterController>();

            void StandAt(Vector3 aim)
            {
                controller.enabled = false;
                avatar.transform.position = new Vector3(aim.x, avatar.transform.position.y, aim.z) - aimOffset;
                controller.enabled = true;
            }

            // Aimed at open floor well away from the storage: nothing is highlighted and a click opens nothing.
            StandAt(storage.transform.position + new Vector3(6f, 0f, 0f));
            yield return Until(() => _interaction.Hovered == null, "nothing highlighted");
            yield return LeftClick();
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.None), "A click on nothing highlighted opened a screen.");

            // Aimed at the storage within reach: it is highlighted and a click opens it.
            StandAt(storage.transform.position);
            yield return Until(() => _interaction.Hovered == storage, "storage highlighted");
            yield return LeftClick();
            Assert.That((_interaction.Screen, _interaction.StorageOpen), Is.EqualTo((InteractionScreen.Inventory, true)), "Storage screen open.");
        }
    }
}
