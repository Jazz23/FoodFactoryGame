// Development host/join panel built in UI Toolkit, hidden during a session (there is no in-session readout; the HUD shows what
// players need). In a scene that generates worlds it also lists the saved worlds to pick from and takes the seed for a new world
// (decision 0026).
// Presentation only: it reads session state and calls SessionRoot; it never decides admission or simulation.
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class SessionPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private SessionRoot session;

        private VisualElement _menu;
        private TextField _name;
        private TextField _address;
        private TextField _seed;
        private TextField _world;
        private ScrollView _savedWorlds;
        private Label _menuStatus;
        private bool _wasRunning;

        // Start, not OnEnable: SessionRoot.Awake must have resolved command-line options first.
        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            _menu = new VisualElement { name = "session-menu" };
            Style(_menu, 16);
            _menu.style.width = 320;
            _menu.Add(new Label("Food Factory — dev session") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginBottom = 8, color = Color.white } });
            _name = new TextField("Name") { name = "session-name", maxLength = PlayerRegistry.MaxNameLength, value = session.Options.DisplayName };
            _address = new TextField("Address") { name = "session-address", value = session.Options.Address };
            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 8 } };
            buttons.Add(new Button(Host) { name = "session-host", text = "Host" });
            buttons.Add(new Button(() => session.Begin(SessionMode.Client, _name.value, _address.value)) { name = "session-join", text = "Join" });
            _menuStatus = new Label { name = "session-status", style = { marginTop = 8, whiteSpace = WhiteSpace.Normal, color = Color.white } };
            // Only labels are white; inputs and buttons keep the theme's dark-on-light text.
            _name.labelElement.style.color = Color.white;
            _address.labelElement.style.color = Color.white;
            _menu.Add(_name);
            _menu.Add(_address);
            if (session.GeneratesWorld)
            {
                // Host opens the named world, creating (and generating) it when its save folder is new. The seed is used only
                // when a world is created; blank means random.
                _world = new TextField("World") { name = "session-world", value = session.WorldName };
                _seed = new TextField("World seed") { name = "session-seed", value = session.Options.WorldSeed };
                _world.labelElement.style.color = Color.white;
                _seed.labelElement.style.color = Color.white;
                _world.RegisterValueChangedCallback(_ => HighlightSelectedWorld());
                // Saved worlds, most recently played first: a click puts the name in the World field, ready to Host.
                _savedWorlds = new ScrollView { name = "session-saved-worlds", style = { maxHeight = 160, marginTop = 4, marginBottom = 4 } };
                _menu.Add(_world);
                _menu.Add(new Label("Saved worlds") { style = { color = Color.white, marginTop = 4 } });
                _menu.Add(_savedWorlds);
                _menu.Add(_seed);
                RefreshSavedWorlds();
                buttons.Add(new Button(NewWorld) { name = "session-new-world", text = "New world" });
            }
            _menu.Add(buttons);
            _menu.Add(_menuStatus);
            root.Add(_menu);
        }

        private void Update()
        {
            if (_menu == null) return;
            var running = session.IsRunning;
            _menu.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            _menuStatus.text = session.Status;
            // Back at the menu after a session: the world just played moved to the top, or a new one now exists.
            if (_wasRunning && !running) RefreshSavedWorlds();
            _wasRunning = running;
        }

        private void Host()
        {
            if (_world != null && !session.SelectWorld(_world.value))
            {
                _menuStatus.text = "World names use letters, digits, '-' and '_'.";
                return;
            }
            if (_seed != null) session.Options.WorldSeed = _seed.value;
            session.Begin(SessionMode.Host, _name.value);
        }

        // Hosts a brand-new world in the first unused "world-N" save folder, generated from the seed box.
        private void NewWorld()
        {
            _world.value = session.NextNewWorldName();
            Host();
        }

        // Rebuilds the saved-world list from the saves directory; called while the menu shows, never during a session.
        private void RefreshSavedWorlds()
        {
            if (_savedWorlds == null) return;
            _savedWorlds.Clear();
            var worlds = session.SavedWorlds();
            if (worlds.Count == 0)
            {
                _savedWorlds.Add(new Label("None yet. Press New world.") { style = { color = new Color(0.7f, 0.7f, 0.7f) } });
                return;
            }
            foreach (var world in worlds)
            {
                var name = world.Name;
                var button = new Button(() => _world.value = name)
                {
                    name = $"session-saved-world-{name}",
                    text = $"{name}  ·  {world.LastPlayedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                    userData = name,
                    style = { unityTextAlign = TextAnchor.MiddleLeft, marginLeft = 0, marginRight = 0 }
                };
                _savedWorlds.Add(button);
            }
            HighlightSelectedWorld();
        }

        private void HighlightSelectedWorld()
        {
            if (_savedWorlds == null) return;
            foreach (var button in _savedWorlds.Query<Button>().ToList())
            {
                var selected = string.Equals(button.userData as string, _world.value?.Trim(), System.StringComparison.OrdinalIgnoreCase);
                button.style.backgroundColor = selected ? new StyleColor(new Color(0.35f, 0.55f, 0.9f)) : new StyleColor(StyleKeyword.Null);
                button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private static void Style(VisualElement element, int padding)
        {
            element.style.position = Position.Absolute;
            element.style.left = 12;
            element.style.top = 12;
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;
            element.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.85f);
        }
    }
}
