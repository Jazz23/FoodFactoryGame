// Verifies the main menu's client settings store (decision 0041) and the keybind rows it offers, using an isolated temporary
// settings database; the application's settings are never opened.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class ClientSettingsTests
    {
        private string _root;
        private string Path_ => Path.Combine(_root, "Settings", "settings.db");

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "FoodFactoryClientSettingsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void LoadingWithoutADatabaseGivesDefaultsAndCreatesNothing()
        {
            var settings = ClientSettings.Load(Path_);
            Assert.That(settings.DisplayName, Is.Null);
            Assert.That(settings.LookSensitivity, Is.EqualTo(1f));
            Assert.That(settings.BindingOverrides, Is.Empty);
            Assert.That(settings.LastKind, Is.EqualTo(LastSessionKind.None));
            Assert.That(File.Exists(Path_), Is.False);
            Assert.That(ClientSettings.Load(null).LastKind, Is.EqualTo(LastSessionKind.None));
        }

        [Test]
        public void SavedSettingsRoundTrip()
        {
            var played = new DateTime(2026, 10, 10, 12, 30, 0, DateTimeKind.Utc);
            new ClientSettings
            {
                DisplayName = "Chef",
                LookSensitivity = 1.75f,
                BindingOverrides = "{\"bindings\":[]}",
                LastKind = LastSessionKind.World,
                LastWorld = "world-3",
                LastVisibility = HostVisibility.FriendsOnly,
                LastAddress = "",
                LastPlayedUtc = played
            }.Save(Path_);

            var loaded = ClientSettings.Load(Path_);

            Assert.That(loaded.DisplayName, Is.EqualTo("Chef"));
            Assert.That(loaded.LookSensitivity, Is.EqualTo(1.75f));
            Assert.That(loaded.BindingOverrides, Is.EqualTo("{\"bindings\":[]}"));
            Assert.That(loaded.LastKind, Is.EqualTo(LastSessionKind.World));
            Assert.That(loaded.LastWorld, Is.EqualTo("world-3"));
            Assert.That(loaded.LastVisibility, Is.EqualTo(HostVisibility.FriendsOnly));
            Assert.That(loaded.LastPlayedUtc, Is.EqualTo(played));
        }

        [Test]
        public void VolumesDefaultRoundTripAndClamp()
        {
            var defaults = ClientSettings.Load(Path_);
            Assert.That((defaults.Volume, defaults.EffectsVolume, defaults.InterfaceVolume, defaults.AmbienceVolume),
                Is.EqualTo((ClientSettings.DefaultVolume, 1f, 1f, 1f)));

            new ClientSettings { Volume = 0.35f, EffectsVolume = 0f, InterfaceVolume = 0.5f, AmbienceVolume = 7f }.Save(Path_);
            var loaded = ClientSettings.Load(Path_);

            Assert.That((loaded.Volume, loaded.EffectsVolume, loaded.InterfaceVolume, loaded.AmbienceVolume),
                Is.EqualTo((0.35f, 0f, 0.5f, 1f)), "Each volume is kept apart; out-of-range values are clamped to 0..1.");
        }

        [Test]
        public void SavingAgainReplacesValuesAndClampsSensitivity()
        {
            new ClientSettings { LastKind = LastSessionKind.World, LastWorld = "a" }.Save(Path_);
            new ClientSettings { LastKind = LastSessionKind.Server, LastAddress = "10.0.0.5", LookSensitivity = 50f }.Save(Path_);

            var loaded = ClientSettings.Load(Path_);

            Assert.That(loaded.LastKind, Is.EqualTo(LastSessionKind.Server));
            Assert.That(loaded.LastAddress, Is.EqualTo("10.0.0.5"));
            Assert.That(loaded.LastWorld, Is.Empty);
            Assert.That(loaded.LookSensitivity, Is.EqualTo(ClientSettings.MaxSensitivity));
        }

        [Test]
        public void SettingsSwitchNamesTheSettingsDatabase()
        {
            var file = Path.Combine(_root, "custom.db");
            Assert.That(SessionOptions.FromCommandLine(new[] { "-settings", file }).SettingsPath, Is.EqualTo(Path.GetFullPath(file)));
            Assert.That(SessionOptions.FromCommandLine(Array.Empty<string>()).SettingsPath, Is.EqualTo(SessionOptions.DefaultSettingsPath));
        }

        [Test]
        public void EveryRebindableRowHasAPrimaryAndAnEmptyOrKeyedAlternate()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            var groups = KeyBindings.Groups(asset);

            Assert.That(groups.Select(x => x.Name), Is.EqualTo(new[] { "Movement", "Interaction", "Building", "Camera and floors", "Hotbar" }));
            var rows = groups.SelectMany(x => x.Rows).ToList();
            Assert.That(rows.Count, Is.EqualTo(26));
            foreach (var row in rows.Where(x => x.FixedText == null))
            {
                Assert.That(row.Primary, Is.GreaterThanOrEqualTo(0), row.Label);
                Assert.That(row.Alternate, Is.GreaterThan(row.Primary), row.Label);
                Assert.That(KeyBindings.Display(row.Action, row.Primary), Is.Not.Empty, row.Label);
            }
            var forward = rows.Single(x => x.Label == "Move forward");
            Assert.That(forward.Action.bindings[forward.Primary].path, Is.EqualTo("<Keyboard>/w"));
            Assert.That(forward.Action.bindings[forward.Alternate].path, Is.EqualTo("<Keyboard>/upArrow"));
            var jump = rows.Single(x => x.Label == "Jump");
            Assert.That(jump.Action.bindings[jump.Alternate].path, Is.Empty, "An action with one key gets an empty alternate slot.");
            // Interact and Inventory share E by design; the conflict lookup names the other one.
            var interact = rows.Single(x => x.Label == "Interact");
            Assert.That(KeyBindings.ConflictsOf(asset, interact.Action, interact.Primary), Is.EqualTo(new[] { "Inventory" }));
        }
    }
}
