// The main menu, hidden during a session (decision 0041): Resume repeats the last session this menu started (a hosted world or
// a server address), Select World hosts a saved or new world as singleplayer or for others, Connect to Server joins by
// address, Settings holds the name, mouse sensitivity and key bindings, and Quit exits. Its look is the MainMenu UXML and USS
// under Assets/UI/Resources/UI (decision 0042); this class clones a screen, fills it in and wires its buttons.
// Presentation only: it reads session state and calls SessionRoot; admission and simulation stay on the server. Client
// settings load from the session's settings path (none in tests, which then keep defaults and write nothing).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Session.Player;
using SQLite;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class SessionPanel : MonoBehaviour
    {
        private const string Templates = "MainMenu/";

        [SerializeField] private UIDocument document;
        [SerializeField] private SessionRoot session;

        private enum MenuScreen
        {
            Main,
            Worlds,
            Connect,
            Settings
        }

        // A session this menu started, recorded as the Resume target once its local player is admitted.
        private struct PendingSession
        {
            public LastSessionKind Kind;
            public string World;
            public HostVisibility Visibility;
            public string Address;
        }

        private VisualElement _overlay;
        private VisualElement _content;
        private Label _status;
        private MenuScreen _screen;
        private ClientSettings _settings = new();
        private string _settingsPath;
        private InputActionAsset _actions;
        private PendingSession? _pending;
        private string _message;
        private string _lastStatus;
        private bool _wasRunning;

        // World screen state.
        private string _selectedWorld;
        private bool _creatingWorld;
        private HostVisibility _visibility;
        private string _newWorldName;
        private string _newWorldSeed;

        // Settings screen state.
        private InputActionRebindingExtensions.RebindingOperation _rebinding;
        private string _bindingNotice;
        private VisualElement _page;

        // Start, not OnEnable: SessionRoot.Awake must have resolved command-line options first.
        private void Start()
        {
            _actions = InputSystem.actions;
            LoadSettings();
            session.Configured += LoadSettings;
            var root = document.rootVisualElement;
            root.Clear();
            UiTemplates.Fill(root, Templates + "MainMenu");
            _overlay = root.Part("main-menu");
            _content = root.Part("main-menu-content");
            _status = root.Part<Label>("main-menu-status");
            Show(MenuScreen.Main);
        }

        private void OnDestroy()
        {
            if (session != null) session.Configured -= LoadSettings;
            _rebinding?.Cancel();
        }

        private void Update()
        {
            if (_overlay == null) return;
            var running = session.IsRunning;
            _overlay.Show(!running);
            if (running) _rebinding?.Cancel();
            // The menu's own session becomes the Resume target once this client is admitted.
            if (_pending.HasValue && session.Authenticator.LocalPlayerId != null) RecordPending();
            if (_pending.HasValue && !running) _pending = null;
            if (session.Status != _lastStatus)
            {
                _lastStatus = session.Status;
                _message = null;
            }
            _status.SetText(_message ?? StatusText(session.Status));
            // Back at the menu after a session: the main screen, with Resume naming the session just left.
            if (_wasRunning && !running) Show(MenuScreen.Main);
            _wasRunning = running;
        }

        private static string StatusText(string status) => status switch
        {
            null or "Not connected" or "Hosting" or "Server running" => "",
            "Rejected: singleplayer" => "That world is singleplayer; its host is not taking players.",
            "Rejected: server-full" => "That server is full.",
            _ when status.StartsWith("Joined as", StringComparison.Ordinal) => "",
            _ => status
        };

        // ---------- Settings storage ----------

        private void LoadSettings()
        {
            _settingsPath = session.Options?.SettingsPath;
            try
            {
                _settings = ClientSettings.Load(_settingsPath);
            }
            catch (Exception error) when (error is SQLiteException || error is IOException || error is NotSupportedException
                || error is UnauthorizedAccessException)
            {
                Debug.LogWarning($"[Menu] Could not read settings at {_settingsPath}: {error.Message}. Using defaults.");
                _settings = new ClientSettings();
            }
            OrbitCameraRig.LookSensitivity = _settings.LookSensitivity;
            _visibility = _settings.LastVisibility;
            if (_actions != null)
            {
                _actions.RemoveAllBindingOverrides();
                if (!string.IsNullOrEmpty(_settings.BindingOverrides))
                {
                    try { _actions.LoadBindingOverridesFromJson(_settings.BindingOverrides); }
                    catch (ArgumentException error) { Debug.LogWarning($"[Menu] Ignoring unreadable key bindings: {error.Message}"); }
                }
            }
            if (_content != null) Show(_screen);
        }

        private void SaveSettings()
        {
            if (string.IsNullOrWhiteSpace(_settingsPath)) return;
            try
            {
                _settings.Save(_settingsPath);
            }
            catch (Exception error) when (error is SQLiteException || error is IOException || error is NotSupportedException
                || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning($"[Menu] Could not save settings to {_settingsPath}: {error.Message}");
                _message = "Settings could not be saved; see the Console.";
            }
        }

        private void RecordPending()
        {
            var pending = _pending.Value;
            _pending = null;
            _settings.LastKind = pending.Kind;
            _settings.LastWorld = pending.World ?? "";
            _settings.LastVisibility = pending.Visibility;
            _settings.LastAddress = pending.Address ?? "";
            _settings.LastPlayedUtc = DateTime.UtcNow;
            SaveSettings();
        }

        private string DisplayName => string.IsNullOrWhiteSpace(_settings.DisplayName) ? session.Options.DisplayName : _settings.DisplayName;

        // ---------- Starting sessions ----------

        // The worlds this scene can open: every saved world where worlds are generated, else only the scene's own world.
        private List<SavedWorld> Worlds()
        {
            if (session.GeneratesWorld) return session.SavedWorlds().ToList();
            var own = session.SavedWorlds().FirstOrDefault(x => string.Equals(x.Name, session.WorldName, StringComparison.OrdinalIgnoreCase));
            return new List<SavedWorld> { own.Name != null ? own : new SavedWorld(session.WorldName, default) };
        }

        private void HostWorld(string name, HostVisibility visibility, string seed = null)
        {
            if (!session.CanBegin)
            {
                _message = "Still closing the last session; try again in a moment.";
                return;
            }
            if (!session.SelectWorld(name))
            {
                _message = "World names use letters, digits, '-' and '_'.";
                return;
            }
            if (seed != null) session.Options.WorldSeed = seed;
            session.Visibility = visibility;
            if (session.Begin(SessionMode.Host, DisplayName))
                _pending = new PendingSession { Kind = LastSessionKind.World, World = session.WorldName, Visibility = visibility };
        }

        private void JoinServer(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                _message = "Enter the server's address.";
                return;
            }
            if (!session.CanBegin)
            {
                _message = "Still closing the last session; try again in a moment.";
                return;
            }
            if (session.Begin(SessionMode.Client, DisplayName, address))
                _pending = new PendingSession { Kind = LastSessionKind.Server, Address = address.Trim() };
        }

        private void Resume()
        {
            switch (_settings.LastKind)
            {
                case LastSessionKind.World:
                    if (Worlds().All(x => !string.Equals(x.Name, _settings.LastWorld, StringComparison.OrdinalIgnoreCase)))
                    {
                        _message = $"The world {_settings.LastWorld} is not here any more.";
                        return;
                    }
                    HostWorld(_settings.LastWorld, _settings.LastVisibility);
                    break;
                case LastSessionKind.Server:
                    JoinServer(_settings.LastAddress);
                    break;
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Screens ----------

        private void Show(MenuScreen screen)
        {
            if (_screen == MenuScreen.Settings && screen != MenuScreen.Settings && _content.childCount > 0)
            {
                _rebinding?.Cancel();
                SaveSettings();
            }
            _screen = screen;
            _content.Clear();
            switch (screen)
            {
                case MenuScreen.Main:
                    _page = UiTemplates.Clone(Templates + "MenuMain");
                    _content.Add(_page);
                    FillMain();
                    break;
                case MenuScreen.Worlds:
                    _page = UiTemplates.Clone(Templates + "MenuWorlds");
                    _content.Add(_page);
                    FillWorlds();
                    break;
                case MenuScreen.Connect:
                    _page = UiTemplates.Clone(Templates + "MenuConnect");
                    _content.Add(_page);
                    FillConnect();
                    break;
                case MenuScreen.Settings:
                    _page = UiTemplates.Clone(Templates + "MenuSettings");
                    _content.Add(_page);
                    FillSettings();
                    break;
            }
            var back = _page.Q<Button>("menu-back");
            if (back != null) back.clicked += () => Show(MenuScreen.Main);
        }

        private void FillMain()
        {
            var resume = _page.Part<Button>("menu-resume");
            var available = _settings.LastKind != LastSessionKind.None;
            resume.SetEnabled(available);
            resume.clicked += () =>
            {
                if (available) Resume();
            };
            _page.Part<Label>("menu-resume-detail").text = _settings.LastKind switch
            {
                LastSessionKind.World => $"{_settings.LastWorld} · {VisibilityPhrase(_settings.LastVisibility)}",
                LastSessionKind.Server => $"Server {_settings.LastAddress}",
                _ => "Nothing to resume yet"
            };
            _page.Part<Label>("menu-resume-ago").text = available ? Ago(_settings.LastPlayedUtc) : "";
            _page.Part<Button>("menu-select-world").clicked += () =>
            {
                _creatingWorld = false;
                _selectedWorld = null;
                Show(MenuScreen.Worlds);
            };
            _page.Part<Button>("menu-connect").clicked += () => Show(MenuScreen.Connect);
            _page.Part<Button>("menu-settings").clicked += () => Show(MenuScreen.Settings);
            _page.Part<Button>("menu-quit").clicked += Quit;
        }

        private static string VisibilityPhrase(HostVisibility visibility) => visibility switch
        {
            HostVisibility.Singleplayer => "Singleplayer",
            HostVisibility.FriendsOnly => "Hosting, friends only",
            HostVisibility.InviteOnly => "Hosting, invite only",
            _ => "Hosting, public"
        };

        private static string Ago(DateTime utc)
        {
            if (utc == default) return "";
            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
            if (span.TotalDays < 2) return "Yesterday";
            return utc.ToLocalTime().ToString("yyyy-MM-dd");
        }

        // ---------- Select world ----------

        private void FillWorlds()
        {
            var create = _page.Part<Button>("menu-new-world");
            create.Show(session.GeneratesWorld);
            create.clicked += () =>
            {
                _creatingWorld = true;
                _selectedWorld = null;
                _newWorldName = _newWorldSeed = null;
                Show(MenuScreen.Worlds);
            };
            var worlds = Worlds();
            if (!_creatingWorld && (_selectedWorld == null || worlds.All(x => x.Name != _selectedWorld)))
            {
                _selectedWorld = worlds.Count > 0 ? worlds[0].Name : null;
                _creatingWorld = _selectedWorld == null && session.GeneratesWorld;
            }

            var list = _page.Part<ScrollView>("menu-world-list");
            if (worlds.Count == 0)
            {
                var empty = new Label("No saved worlds yet. Press New world.");
                empty.AddToClassList("menu-worlds__empty");
                list.Add(empty);
            }
            foreach (var world in worlds) list.Add(WorldRow(world));

            var existing = _page.Part("menu-world-existing");
            var fresh = _page.Part("menu-world-new");
            existing.Show(!_creatingWorld && _selectedWorld != null);
            fresh.Show(_creatingWorld);
            var nameField = _page.Part<TextField>("menu-new-world-name");
            var seedField = _page.Part<TextField>("menu-new-world-seed");
            if (_creatingWorld)
            {
                nameField.value = string.IsNullOrEmpty(_newWorldName) ? session.NextNewWorldName() : _newWorldName;
                seedField.value = _newWorldSeed ?? "";
                nameField.RegisterValueChangedCallback(change => _newWorldName = change.newValue);
                seedField.RegisterValueChangedCallback(change => _newWorldSeed = change.newValue);
            }
            else if (_selectedWorld != null)
            {
                _page.Part<Label>("menu-world-name").text = _selectedWorld;
                var played = worlds.FirstOrDefault(x => x.Name == _selectedWorld).LastPlayedUtc;
                _page.Part<Label>("menu-world-played").text = played == default ? "Not played yet" : $"Last played {Ago(played).ToLowerInvariant()}";
            }

            FillVisibility();
            var play = _page.Part<Button>("menu-play");
            play.SetEnabled(_creatingWorld || _selectedWorld != null);
            play.clicked += () =>
            {
                if (_creatingWorld)
                {
                    var name = nameField.value?.Trim() ?? "";
                    if (Worlds().Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        _message = $"A world named {name} already exists.";
                        return;
                    }
                    HostWorld(name, _visibility, seedField.value?.Trim() ?? "");
                }
                else if (_selectedWorld != null) HostWorld(_selectedWorld, _visibility);
            };
        }

        private VisualElement WorldRow(SavedWorld world)
        {
            var row = (Button)UiTemplates.Clone(Templates + "MenuWorldRow");
            var name = world.Name;
            row.name = $"menu-world-{name}";
            row.EnableInClassList("menu-world-row--selected", !_creatingWorld && name == _selectedWorld);
            row.Part<Label>("initial").text = name.Substring(0, 1).ToUpperInvariant();
            row.Part<Label>("title").text = name;
            row.Part<Label>("played").text = world.LastPlayedUtc == default ? "New" : Ago(world.LastPlayedUtc);
            row.clicked += () =>
            {
                _creatingWorld = false;
                _selectedWorld = name;
                Show(MenuScreen.Worlds);
            };
            return row;
        }

        // The "Play as" dropdown: the face shows the chosen option's text; the options (fixed in the UXML) open beneath it.
        private void FillVisibility()
        {
            var picker = _page.Part("menu-visibility");
            var toggle = _page.Part<Button>("menu-visibility-toggle");
            var chevron = _page.Part("menu-visibility-chevron");
            void Refresh()
            {
                var chosen = _page.Part<Button>($"menu-visibility-{_visibility}");
                _page.Part<Label>("menu-visibility-label").text = chosen.PartByClass<Label>("menu-choice__title").text;
                _page.Part<Label>("menu-visibility-description").text = chosen.PartByClass<Label>("menu-choice__description").text;
                foreach (HostVisibility value in Enum.GetValues(typeof(HostVisibility)))
                    _page.Part($"menu-visibility-{value}").EnableInClassList("menu-option--chosen", value == _visibility);
                _page.Part<Label>("menu-play-label").text = _visibility == HostVisibility.Singleplayer ? "Play" : "Host and play";
                var open = picker.ClassListContains("menu-visibility--open");
                chevron.EnableInClassList("ff-glyph--chevron-down", !open);
                chevron.EnableInClassList("ff-glyph--chevron-up", open);
                chevron.MarkDirtyRepaint();
            }
            toggle.clicked += () =>
            {
                picker.ToggleInClassList("menu-visibility--open");
                Refresh();
            };
            foreach (HostVisibility value in Enum.GetValues(typeof(HostVisibility)))
            {
                var choice = value;
                _page.Part<Button>($"menu-visibility-{value}").clicked += () =>
                {
                    _visibility = choice;
                    picker.RemoveFromClassList("menu-visibility--open");
                    Refresh();
                };
            }
            Refresh();
        }

        // ---------- Connect to server ----------

        private void FillConnect()
        {
            var name = _page.Part<TextField>("menu-connect-name");
            name.maxLength = PlayerRegistry.MaxNameLength;
            name.value = DisplayName;
            var address = _page.Part<TextField>("menu-connect-address");
            address.value = _settings.LastKind == LastSessionKind.Server && !string.IsNullOrEmpty(_settings.LastAddress)
                ? _settings.LastAddress : session.Options.Address;
            _page.Part<Button>("menu-connect-go").clicked += () =>
            {
                SetName(name.value);
                JoinServer(address.value);
            };
        }

        private void SetName(string value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed == _settings.DisplayName) return;
            _settings.DisplayName = trimmed;
            SaveSettings();
        }

        // ---------- Settings ----------

        private void FillSettings()
        {
            _bindingNotice = null;
            _page.Part<Button>("menu-reset-settings").clicked += ResetSettings;
            _page.Part<Button>("menu-settings-done").clicked += () => Show(MenuScreen.Main);
            var name = _page.Part<TextField>("menu-settings-name");
            name.maxLength = PlayerRegistry.MaxNameLength;
            name.value = DisplayName;
            name.RegisterCallback<FocusOutEvent>(_ => SetName(name.value));

            var value = _page.Part<Label>("menu-sensitivity-value");
            value.text = SensitivityText(_settings.LookSensitivity);
            _page.Part<Label>("menu-sensitivity-min").text = SensitivityText(ClientSettings.MinSensitivity);
            _page.Part<Label>("menu-sensitivity-max").text = SensitivityText(ClientSettings.MaxSensitivity);
            var slider = _page.Part<Slider>("menu-sensitivity");
            slider.lowValue = ClientSettings.MinSensitivity;
            slider.highValue = ClientSettings.MaxSensitivity;
            slider.SetValueWithoutNotify(_settings.LookSensitivity);
            slider.RegisterValueChangedCallback(change =>
            {
                var snapped = ClientSettings.ClampSensitivity(Mathf.Round(change.newValue * 20f) / 20f);
                _settings.LookSensitivity = snapped;
                OrbitCameraRig.LookSensitivity = snapped;
                value.text = SensitivityText(snapped);
            });
            // Saved when the drag ends (and when leaving the screen), not on every step.
            slider.RegisterCallback<PointerCaptureOutEvent>(_ => SaveSettings());
            RefreshKeybinds();
        }

        private static string SensitivityText(float value) => value.ToString("0.00") + "×";

        private void ResetSettings()
        {
            _rebinding?.Cancel();
            _settings.LookSensitivity = 1f;
            OrbitCameraRig.LookSensitivity = 1f;
            _actions?.RemoveAllBindingOverrides();
            _settings.BindingOverrides = "";
            SaveSettings();
            Show(MenuScreen.Settings);
        }

        // Rebuilds the two keybind columns from the asset's current (overridden) bindings: Movement and Interaction on the left,
        // the rest on the right.
        private void RefreshKeybinds()
        {
            if (_screen != MenuScreen.Settings || _page == null) return;
            _page.Part<Label>("menu-binding-notice").text = _bindingNotice ?? "";
            var columns = new[] { _page.Part("menu-keybind-column-0"), _page.Part("menu-keybind-column-1") };
            foreach (var column in columns) column.Clear();
            var groups = KeyBindings.Groups(_actions);
            var split = groups.FindIndex(x => x.Name == "Building");
            if (split < 0) split = (groups.Count + 1) / 2;
            for (var index = 0; index < groups.Count; index++) columns[index < split ? 0 : 1].Add(GroupView(groups[index]));
        }

        private VisualElement GroupView(KeyBindingGroup group)
        {
            var view = UiTemplates.Clone(Templates + "MenuKeybindGroup");
            view.Part<Label>("title").text = group.Name;
            var rows = view.Part("rows");
            foreach (var row in group.Rows) rows.Add(RowView(row));
            return view;
        }

        private VisualElement RowView(KeyBindingRow row)
        {
            var view = UiTemplates.Clone(Templates + "MenuKeybindRow");
            view.Part<Label>("action").text = row.Label;
            var primary = view.Part<Button>("primary");
            var alternate = view.Part<Button>("alternate");
            var fixedKey = view.Part<Label>("fixed-key");
            var fixedNote = view.Part<Label>("fixed-note");
            var isFixed = row.FixedText != null;
            primary.Show(!isFixed);
            alternate.Show(!isFixed);
            fixedKey.Show(isFixed);
            fixedNote.Show(isFixed);
            if (isFixed)
            {
                fixedKey.text = row.FixedText;
                return view;
            }
            SlotButton(primary, row, row.Primary, "primary");
            SlotButton(alternate, row, row.Alternate, "alternate");
            return view;
        }

        private void SlotButton(Button button, KeyBindingRow row, int index, string slot)
        {
            if (index < 0)
            {
                button.text = "—";
                button.AddToClassList("menu-keycap--unbound");
                button.SetEnabled(false);
                return;
            }
            var shown = KeyBindings.Display(row.Action, index);
            button.name = $"menu-bind-{row.Action.name}-{index}";
            button.text = shown.Length > 0 ? shown : "—";
            button.tooltip = $"{row.Label}, {slot} key";
            button.EnableInClassList("menu-keycap--unbound", shown.Length == 0);
            button.EnableInClassList("menu-keycap--changed", shown.Length > 0 && row.Action.bindings[index].overridePath != null);
            button.clicked += () => StartRebind(row, index, button);
        }

        private void StartRebind(KeyBindingRow row, int index, Button button)
        {
            // Clicking another key while one listens cancels that one, which rebuilds the list; carry on with the new button.
            if (_rebinding != null)
            {
                _rebinding.Cancel();
                button = _page.Q<Button>(button.name) ?? button;
            }
            _bindingNotice = null;
            _page.Part<Label>("menu-binding-notice").text = "";
            button.text = "Press…";
            button.AddToClassList("menu-keycap--listening");
            InputActionRebindingExtensions.RebindingOperation operation = null;
            operation = KeyBindings.Rebind(row.Action, index, changed =>
            {
                if (_rebinding == operation) _rebinding = null;
                if (changed)
                {
                    _settings.BindingOverrides = _actions.SaveBindingOverridesAsJson();
                    SaveSettings();
                    var conflicts = KeyBindings.ConflictsOf(_actions, row.Action, index);
                    _bindingNotice = conflicts.Count > 0
                        ? $"{KeyBindings.Display(row.Action, index)} is also bound to {string.Join(", ", conflicts)}"
                        : null;
                }
                RefreshKeybinds();
            });
            _rebinding = operation;
        }
    }
}
