// Development host/join panel and in-session readout (player ID, server clock, site revision, equipment hint) built in UI Toolkit.
// In a scene that generates worlds it also takes the seed for a new world and shows the stored layout's seed (decision 0026).
// Presentation only: it reads session state and calls SessionRoot; it never decides admission or simulation.
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class SessionPanel : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private SessionRoot session;
        [SerializeField] private EquipmentInteraction equipment;

        private VisualElement _menu;
        private TextField _name;
        private TextField _address;
        private TextField _seed;
        private TextField _world;
        private Label _menuStatus;
        private Label _readout;

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
                _menu.Add(_world);
                _menu.Add(_seed);
                buttons.Add(new Button(NewWorld) { name = "session-new-world", text = "New world" });
            }
            _menu.Add(buttons);
            _menu.Add(_menuStatus);
            // Wraps within part of the width so long hints stay on screen and clear of the HUD's cash readout (top right).
            _readout = new Label { name = "session-readout", style = { color = Color.white, whiteSpace = WhiteSpace.Normal, maxWidth = new Length(60, LengthUnit.Percent) } };
            Style(_readout, 8);
            root.Add(_menu);
            root.Add(_readout);
        }

        private void Update()
        {
            if (_menu == null) return;
            var running = session.IsRunning;
            _menu.style.display = running ? DisplayStyle.None : DisplayStyle.Flex;
            _readout.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            _menuStatus.text = session.Status;
            if (!running) return;
            var site = session.ClientSite;
            var server = session.ServerWorld?.Snapshot();
            _readout.text = $"{session.Mode} | {session.Status}\n"
                + $"Player: {session.Authenticator.LocalPlayerId ?? "(none)"}\n"
                + (site == null ? "Site: waiting for dev-site baseline\n"
                    : $"Site {session.ClientSiteId}: clock {site.ClockSeconds}s, revision {site.Revision}\n")
                + (server == null ? "" : $"Server: clock {server.ClockSeconds}s, revision {server.Revision}, players {session.Authenticator.AuthenticatedCount}\n")
                + WorldLine()
                + equipment.Status;
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

        private string WorldLine()
        {
            if (!session.GeneratesWorld || session.ServerWorld == null) return "";
            var stored = session.ServerLayout;
            if (stored == null) return $"World '{session.WorldName}': no generated layout (made before world generation; use New world)\n";
            var layout = stored.Layout;
            return $"World '{session.WorldName}': seed '{layout.RequestedSeed}' ({layout.Seed}, attempt {layout.Attempt}), generator v{layout.GeneratorVersion}, "
                + $"{layout.Buildings.Count} buildings, start {layout.StartRestaurantId}\n";
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
