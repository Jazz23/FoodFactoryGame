// Drives the real main menu in DevSite (decision 0041): hosting from Select World records the Resume target in the settings
// database, and Resume starts that world again the same way. Saves, identity and settings live in a unique temporary
// directory; the application's data is never opened.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class MainMenuTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private VisualElement _menu;

        private string SettingsPath => Path.Combine(_directory, "settings.db");

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryMainMenu", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "Saves", "menu-world"),
                IdentityPath = Path.Combine(_directory, "identity.db"),
                SettingsPath = SettingsPath,
                DisplayName = "Host",
                Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            yield return null;
            _menu = UnityEngine.Object.FindObjectsByType<UIDocument>().Select(x => x.rootVisualElement).FirstOrDefault(x => x.Q("main-menu") != null);
            Assert.That(_menu, Is.Not.Null, "DevSite shows the main menu.");
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
                _directory = null;
            }
            finally
            {
                _input.TearDown();
            }
        }

        // Clicks a menu button as a pointer would, through its Clickable.
        private void Click(string name)
        {
            var button = _menu.Q<Button>(name);
            Assert.That(button, Is.Not.Null, $"Menu button {name}");
            Assert.That(button.enabledSelf, Is.True, $"{name} is enabled");
            var invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
                new[] { typeof(EventBase) }, null);
            using var evt = NavigationSubmitEvent.GetPooled();
            evt.target = button;
            invoke.Invoke(button.clickable, new object[] { evt });
        }

        private bool MenuShown => _menu.Q("main-menu").resolvedStyle.display == DisplayStyle.Flex;

        [UnityTest]
        public IEnumerator HostingFromTheMenuBecomesTheResumeTarget()
        {
            Assert.That(_menu.Q<Button>("menu-resume").enabledSelf, Is.False, "Nothing to resume on a fresh settings database.");
            Assert.That(File.Exists(SettingsPath), Is.False, "Showing the menu writes nothing.");

            Click("menu-select-world");
            Click("menu-visibility-toggle");
            Click($"menu-visibility-{HostVisibility.Singleplayer}");
            Assert.That(_menu.Q<Label>("menu-play-label").text, Is.EqualTo("Play"));
            Click("menu-play");
            yield return Until(() => _root.Authenticator.LocalPlayerId != null, "host admitted");
            Assert.That(_root.Visibility, Is.EqualTo(HostVisibility.Singleplayer));
            yield return Until(() => !MenuShown, "menu hidden during the session");
            yield return Until(() => ClientSettings.Load(SettingsPath).LastKind == LastSessionKind.World, "resume target saved");
            var saved = ClientSettings.Load(SettingsPath);
            Assert.That(saved.LastWorld, Is.EqualTo("menu-world"));
            Assert.That(saved.LastVisibility, Is.EqualTo(HostVisibility.Singleplayer));

            _root.Shutdown();
            yield return Until(() => _root.CanBegin, "session stopped");
            yield return Until(() => MenuShown, "menu back");
            Assert.That(_menu.Q<Button>("menu-resume").enabledSelf, Is.True);
            _root.Visibility = HostVisibility.Public;
            Click("menu-resume");
            yield return Until(() => _root.Authenticator.LocalPlayerId != null, "resumed host admitted");
            Assert.That(_root.Mode, Is.EqualTo(SessionMode.Host));
            Assert.That(_root.WorldName, Is.EqualTo("menu-world"));
            Assert.That(_root.Visibility, Is.EqualTo(HostVisibility.Singleplayer), "Resume restarts the world the way it was hosted.");
        }
    }
}
