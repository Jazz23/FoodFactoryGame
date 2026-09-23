// Runs equipment pickup/placement through the real DevSite bridge, presenter and authenticator with a host plus a
// loopback-UDP remote client. Every save and identity path is a unique temporary directory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class EquipmentPlacementTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private string _directory;
        private SessionRoot _root;
        private EquipmentPresenter _presenter;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ClientSiteSubscription _remoteSite;
        private ushort _port;
        private readonly Dictionary<string, GoodsOutcome> _results = new();

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _results.Clear();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryEquipmentPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _presenter = UnityEngine.Object.FindAnyObjectByType<EquipmentPresenter>();
            Assert.That(_root, Is.Not.Null);
            Assert.That(_presenter, Is.Not.Null, "DevSite must contain an EquipmentPresenter.");
            Configure();
        }

        private void Configure()
        {
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = Path.Combine(_directory, "host.secret"),
                DisplayName = "Host",
                Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _root.NetworkManager.TransportManager.Transport.SetPort(_port);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _remoteSite?.Reset();
            if (_remote != null)
            {
                _remote.ClientManager.StopConnection();
                UnityEngine.Object.Destroy(_remote.gameObject);
            }
            _remote = null;
            if (_root != null) _root.Shutdown();
            _root = null;
            yield return null;
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            _directory = null;
        }

        private void CreateRemote()
        {
            var go = new GameObject("equipment-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on equipment-test-remote\\."));
#endif
            _remote = go.AddComponent<NetworkManager>();
            _remote.SpawnablePrefabs = _root.NetworkManager.SpawnablePrefabs;
            typeof(NetworkManager).GetField("_persistence", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_remote, NetworkManager.PersistenceType.AllowMultiple);
            typeof(NetworkManager).GetField("_dontDestroyOnLoad", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(_remote, false);
            _remoteAuth = go.AddComponent<DevAuthenticator>();
            go.SetActive(true);
            _remote.ServerManager.SetAuthenticator(_remoteAuth);
            var tugboat = go.GetComponent<Tugboat>();
            tugboat.SetPort(_port);
            tugboat.SetClientAddress("127.0.0.1");
            _remoteAuth.SetClientCredentials("Remote", ClientIdentity.LoadOrCreate(Path.Combine(_directory, "remote.secret")));
            _remoteSite = new ClientSiteSubscription(_remote, DevWorld.SiteId);
            _remoteSite.ResultReceived += Store;
        }

        private void Store(GoodsOutcome outcome) => _results[outcome.RequestId] = outcome;

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null, "host dev-site baseline");
            _root.ClientSubscription.ResultReceived += Store;
        }

        private IEnumerator ConnectRemote()
        {
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null && _remoteSite.Bridge != null; }, "remote dev-site baseline");
        }

        private IEnumerator Await(string requestId)
        {
            yield return Until(() => { _remoteSite?.Tick(); return _results.ContainsKey(requestId); }, $"result for {requestId}");
        }

        private static GoodsEquipment Oven(GoodsSnapshot site) => site?.Equipment.SingleOrDefault(x => x.Id == DevWorld.OvenId);

        private Vector3 Expected(GoodsEquipment equipment) =>
            SiteGridSpace.Center(_root.ClientSite.SiteLayouts.Single(), equipment);

        [UnityTest]
        public IEnumerator HostMovesSeededOvenAndRemoteSeesEveryStep()
        {
            yield return StartHost();
            var hostId = _root.Authenticator.LocalPlayerId;
            CreateRemote();
            yield return ConnectRemote();
            var remoteId = _remoteAuth.LocalPlayerId;
            var host = _root.ClientSubscription.Bridge;
            var remote = _remoteSite.Bridge;

            // The seed places one oven and gives each admitted player an inventory.
            var seeded = Oven(_root.ClientSite);
            Assert.That((seeded.State, seeded.CellX, seeded.CellZ), Is.EqualTo((EquipmentState.Placed, DevWorld.OvenCellX, DevWorld.OvenCellZ)));
            Assert.That(_root.ServerWorld.Snapshot().Locations.Count(x => x.Kind == "carried"), Is.EqualTo(2));
            yield return Until(() => _presenter.Visuals.ContainsKey(DevWorld.OvenId), "seeded oven visual");
            Assert.That(Vector3.Distance(_presenter.Visuals[DevWorld.OvenId].transform.position, Expected(seeded)), Is.LessThan(0.001f));
            Assert.That(_presenter.Visuals[DevWorld.OvenId].GetComponentInChildren<Collider>(), Is.Not.Null);

            host.RequestPickUp("host-pick", DevWorld.OvenId);
            yield return Await("host-pick");
            Assert.That(_results["host-pick"].Accepted, Is.True, _results["host-pick"].Reason);
            yield return Until(() => { _remoteSite.Tick(); return Oven(_remoteSite.Latest)?.HolderId == hostId; }, "remote sees host holding the oven");
            yield return Until(() => !_presenter.Visuals.ContainsKey(DevWorld.OvenId), "held oven has no scene visual");

            // The held oven occupies an inventory slot; picking it out of the grid and closing the screen shows its ghost.
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            interaction.ToggleInventory();
            yield return Until(() => hud.SlotOf(PlayerHud.InventoryGrid, PlayerHud.MachineKey("oven")) >= 0, "oven in an inventory slot");
            hud.ClickSlot(PlayerHud.InventoryGrid, hud.SlotOf(PlayerHud.InventoryGrid, PlayerHud.MachineKey("oven")));
            Assert.That(interaction.CursorKind, Is.EqualTo("oven"));
            interaction.CloseScreen();
            yield return Until(() => interaction.GhostVisible, "oven ghost at the aim point");
            var ghost = interaction.transform.Cast<Transform>().Select(x => x.gameObject).Single(x => x.name == "Ghost oven");
            Assert.That(ghost.GetComponentsInChildren<Collider>().Any(x => x.enabled), Is.False, "The ghost never blocks the aim ray.");
            Assert.That(ghost.GetComponentsInChildren<MonoBehaviour>(), Is.Empty, "The ghost never runs oven behaviour.");
            Assert.That(ghost.GetComponentsInChildren<Light>().Any(x => x.enabled), Is.False);
            interaction.ClearCursor();
            yield return Until(() => !interaction.GhostVisible, "ghost hidden with an empty cursor");

            remote.RequestPickUp("remote-grab", DevWorld.OvenId);
            yield return Await("remote-grab");
            Assert.That(_results["remote-grab"].Reason, Is.EqualTo("not-placed"));
            remote.RequestPlace("remote-steal", DevWorld.OvenId, 2, 2, 0);
            yield return Await("remote-steal");
            Assert.That(_results["remote-steal"].Reason, Is.EqualTo("not-held"));
            host.RequestPlace("host-edge", DevWorld.OvenId, DevWorld.GridWidth - 2, 0, 0);
            yield return Await("host-edge");
            Assert.That(_results["host-edge"].Reason, Is.EqualTo("out-of-bounds"));

            host.RequestPlace("host-place", DevWorld.OvenId, 4, 5, 1);
            yield return Await("host-place");
            Assert.That(_results["host-place"].Accepted, Is.True, _results["host-place"].Reason);
            yield return Until(() =>
            {
                _remoteSite.Tick();
                var seen = Oven(_remoteSite.Latest);
                return seen != null && seen.State == EquipmentState.Placed && seen.CellX == 4 && seen.CellZ == 5
                    && _remoteSite.Latest.Revision == _root.ClientSite.Revision;
            }, "remote sees the oven at its new cell");
            yield return Until(() => _presenter.Visuals.Count == 1 && _presenter.Visuals.ContainsKey(DevWorld.OvenId), "one host visual");
            var placed = Oven(_root.ClientSite);
            Assert.That(placed.Rotation, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_presenter.Visuals[DevWorld.OvenId].transform.position, Expected(placed)), Is.LessThan(0.001f));
            Assert.That(_root.ServerWorld.Snapshot().Equipment.Count, Is.EqualTo(1));

            // A holder who disconnects keeps the oven in their inventory and still holds it after reconnecting.
            remote.RequestPickUp("remote-pick", DevWorld.OvenId);
            yield return Await("remote-pick");
            Assert.That(_results["remote-pick"].Accepted, Is.True, _results["remote-pick"].Reason);
            _remoteSite.Reset();
            _remote.ClientManager.StopConnection();
            yield return Until(() => _root.Authenticator.AuthenticatedCount == 1, "remote disconnected");
            Assert.That(Oven(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot()).HolderId, Is.EqualTo(remoteId));
            yield return ConnectRemote();
            Assert.That(_remoteAuth.LocalPlayerId, Is.EqualTo(remoteId));
            Assert.That(Oven(_remoteSite.Latest).HolderId, Is.EqualTo(remoteId));
            _remoteSite.Bridge.RequestPlace("remote-place", DevWorld.OvenId, 6, 6, 0);
            yield return Await("remote-place");
            Assert.That(_results["remote-place"].Accepted, Is.True, _results["remote-place"].Reason);
        }

        // The host goes through the real HUD and interaction layer; the remote uses the bridge directly for rejections.
        [UnityTest]
        public IEnumerator HostBakesBreadThroughOvenSlotsAndRemoteSeesItRun()
        {
            yield return StartHost();
            var hostId = _root.Authenticator.LocalPlayerId;
            CreateRemote();
            yield return ConnectRemote();
            var remoteId = _remoteAuth.LocalPlayerId;
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            var input = DevWorld.OvenId + ":in";
            var output = DevWorld.OvenId + ":out";
            int Count(GoodsSnapshot site, string location, string item) =>
                site.Lots.Where(x => x.LocationId == location && x.ItemId == item).Sum(x => x.Quantity);

            // Dev seed: storage dough and starter dough for each new player.
            var server = _root.ServerWorld.Snapshot();
            Assert.That(Count(server, DevWorld.StorageId, "dough"), Is.EqualTo(DevWorld.StorageDough));
            Assert.That(Count(server, GoodsWorld.InventoryLocationId(hostId), "dough"), Is.EqualTo(DevWorld.StarterDough));
            Assert.That(Count(server, GoodsWorld.InventoryLocationId(remoteId), "dough"), Is.EqualTo(DevWorld.StarterDough));

            var remote = _remoteSite.Bridge;
            remote.RequestStartJob("remote-empty", DevWorld.OvenId, "oven-bread");
            yield return Await("remote-empty");
            Assert.That(_results["remote-empty"].Reason, Is.EqualTo("missing-inputs"));
            remote.RequestStartJob("remote-bogus", DevWorld.OvenId, "no-such-recipe");
            yield return Await("remote-bogus");
            Assert.That(_results["remote-bogus"].Reason, Is.EqualTo("invalid-recipe"));

            // The oven screen opens from the crosshair click path's entry point: inventory grid beside input -> output slots.
            yield return Until(() => { interaction.OpenMachine(DevWorld.OvenId); return interaction.Screen == InteractionScreen.Machine; }, "oven screen");
            yield return null;
            var doughSlot = hud.SlotOf(PlayerHud.InventoryGrid, "dough");
            Assert.That(doughSlot, Is.GreaterThanOrEqualTo(0));
            Assert.That(hud.ScreenRoot.Q<Button>($"hud-inventory-slot-{doughSlot}"), Is.Not.Null);
            Assert.That(hud.ScreenRoot.Q<Button>("hud-input-slot-0"), Is.Not.Null);
            Assert.That(hud.ScreenRoot.Q<Button>("hud-output-slot-0"), Is.Not.Null);
            Assert.That(hud.ScreenRoot.Q<Button>("hud-start"), Is.Null, "The oven runs by itself; there is no Start button.");

            // Click the dough slot: the stack rides on the cursor and nothing moves yet.
            hud.ClickSlot(PlayerHud.InventoryGrid, doughSlot);
            Assert.That(interaction.CursorGoods?.ItemId, Is.EqualTo("dough"));
            yield return null;
            Assert.That(hud.CursorIcon.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The stack's icon follows the pointer.");
            Assert.That(Count(_root.ServerWorld.Snapshot(), GoodsWorld.InventoryLocationId(hostId), "dough"), Is.EqualTo(DevWorld.StarterDough));

            // Drop it on the input slot: the dough moves in and the oven starts a batch by itself in the same command.
            hud.ClickSlot(PlayerHud.InputGrid, 0);
            Assert.That(interaction.CursorGoods, Is.Null);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest.Jobs.Any(x => x.StationId == DevWorld.OvenId && x.StartedBy == GoodsWorld.AutomaticStarter); },
                "remote sees the automatic batch");
            Assert.That(interaction.LastRejection, Is.Null);
            yield return Until(() => _presenter.Visuals[DevWorld.OvenId].Running, "oven visual running");
            Assert.That(Count(_root.ServerWorld.Snapshot(), input, "dough"), Is.EqualTo(DevWorld.StarterDough - 1), "One dough per batch.");

            // A manual start while it runs is refused.
            remote.RequestStartJob("remote-busy", DevWorld.OvenId, "oven-bread");
            yield return Await("remote-busy");
            Assert.That(_results["remote-busy"].Reason, Is.EqualTo("station-busy"));

            yield return Until(() => Count(_root.ClientSite, output, "bread") == 1, "bread in the output", 20f);
            Assert.That(_root.ClientSite.Jobs.Any(x => x.StationId == DevWorld.OvenId), Is.True, "The next batch starts while dough remains.");

            // Pick the bread from the output slot and put it into a chosen empty inventory slot.
            hud.ClickSlot(PlayerHud.OutputGrid, 0);
            Assert.That(interaction.CursorGoods?.ItemId, Is.EqualTo("bread"));
            const int breadSlot = 7;
            hud.ClickSlot(PlayerHud.InventoryGrid, breadSlot);
            yield return Until(() => Count(_root.ClientSite, interaction.InventoryId, "bread") >= 1 && !interaction.HasPendingRequests, "bread in the inventory");
            yield return null;
            Assert.That(hud.SlotOf(PlayerHud.InventoryGrid, "bread"), Is.EqualTo(breadSlot), "Dropped goods land in the clicked slot.");
            Assert.That(Count(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot(), GoodsWorld.InventoryLocationId(hostId), "bread"), Is.GreaterThanOrEqualTo(1));
            interaction.CloseScreen();
        }

        [UnityTest]
        public IEnumerator ShiftClickSendsAStackAcrossOrFillsTheOtherContainer()
        {
            yield return StartHost();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            var inventory = interaction.InventoryId;
            var input = DevWorld.OvenId + ":in";
            int Count(string location) => _root.ClientSite.Lots.Where(x => x.LocationId == location && x.ItemId == "dough").Sum(x => x.Quantity);

            // Inventory screen: the storage's 20 dough join the 5 starter dough, one full slot and one slot of 5.
            yield return Until(() =>
            {
                if (interaction.Screen == InteractionScreen.None) interaction.ToggleInventory();
                return hud.SlotOf(PlayerHud.StorageGrid, "dough") >= 0;
            }, "storage dough slot");
            Assert.That(_root.ClientSite.Locations.Single(x => x.Id == inventory).Capacity, Is.EqualTo(DevWorld.InventoryCapacity));
            Assert.That(Count(inventory), Is.EqualTo(DevWorld.StarterDough));
            hud.QuickTransferSlot(PlayerHud.StorageGrid, hud.SlotOf(PlayerHud.StorageGrid, "dough"));
            Assert.That(interaction.CursorGoods, Is.Null, "Quick transfer never uses the cursor.");
            yield return Until(() => Count(inventory) == DevWorld.StarterDough + DevWorld.StorageDough && !interaction.HasPendingRequests, "storage stack in the inventory");
            yield return null;
            Assert.That(Count(DevWorld.StorageId), Is.EqualTo(0));
            Assert.That(hud.CountAt(PlayerHud.InventoryGrid, hud.SlotOf(PlayerHud.InventoryGrid, "dough#0")), Is.EqualTo(20));
            Assert.That(hud.CountAt(PlayerHud.InventoryGrid, hud.SlotOf(PlayerHud.InventoryGrid, "dough#1")), Is.EqualTo(5));
            interaction.CloseScreen();

            // Machine screen: the oven's single input slot takes the full stack, and a batch takes one dough straight away.
            yield return Until(() => { interaction.OpenMachine(DevWorld.OvenId); return interaction.Screen == InteractionScreen.Machine; }, "oven screen");
            yield return null;
            Assert.That(_root.ClientSite.Locations.Single(x => x.Id == input).Capacity, Is.EqualTo(1));
            hud.QuickTransferSlot(PlayerHud.InventoryGrid, hud.SlotOf(PlayerHud.InventoryGrid, "dough#0"));
            yield return Until(() => Count(input) == 19 && _root.ClientSite.Jobs.Any(x => x.StationId == DevWorld.OvenId) && !interaction.HasPendingRequests,
                "full input and the first batch");
            yield return null;

            // Only one dough fits beside the 19 left, so shift-clicking the stack of 5 moves just that one (a batch lasts 10 s).
            var rest = hud.SlotOf(PlayerHud.InventoryGrid, "dough");
            Assert.That(hud.CountAt(PlayerHud.InventoryGrid, rest), Is.EqualTo(5));
            hud.QuickTransferSlot(PlayerHud.InventoryGrid, rest);
            yield return Until(() => Count(inventory) == 4 && !interaction.HasPendingRequests, "input filled from part of the stack");
            Assert.That(interaction.LastRejection, Is.Null);
            Assert.That(Count(input), Is.EqualTo(20));
            interaction.CloseScreen();
        }

        // Drives slot buttons through UI Toolkit pointer events (not ClickSlot), with Shift held on a virtual keyboard, so a
        // button that drops modified clicks fails here.
        [UnityTest]
        public IEnumerator SlotButtonsTakeShiftClicksAndPlainClicks()
        {
            yield return StartHost();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            var inventory = interaction.InventoryId;
            int Count(string location) => _root.ClientSite.Lots.Where(x => x.LocationId == location && x.ItemId == "dough").Sum(x => x.Quantity);
            void Click(string grid, int index, bool shift)
            {
                var button = hud.ScreenRoot.Q<Button>($"hud-{grid}-slot-{index}");
                Assert.That(button, Is.Not.Null, $"{grid} slot {index}");
                var center = button.worldBound.center;
                foreach (var type in new[] { EventType.MouseDown, EventType.MouseUp })
                {
                    var systemEvent = new Event
                    {
                        type = type, button = 0, clickCount = 1, mousePosition = center,
                        modifiers = shift ? EventModifiers.Shift : EventModifiers.None
                    };
                    using EventBase pointer = type == EventType.MouseDown ? PointerDownEvent.GetPooled(systemEvent) : PointerUpEvent.GetPooled(systemEvent);
                    button.SendEvent(pointer);
                }
            }

            var settings = InputSystem.settings;
            var behavior = settings.editorInputBehaviorInPlayMode;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var background = settings.backgroundBehavior;
            // Runs from an unfocused Editor too: keep virtual devices enabled without application focus.
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                yield return Until(() =>
                {
                    if (interaction.Screen == InteractionScreen.None) interaction.ToggleInventory();
                    return hud.SlotOf(PlayerHud.StorageGrid, "dough") >= 0 && interaction.ScreenClicksArmed;
                }, "inventory screen with storage dough");
                yield return null;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift));
                yield return null;
                Assert.That(interaction.QuickTransferHeld, Is.True, "Shift reaches Player/QuickTransfer.");
                Click(PlayerHud.StorageGrid, hud.SlotOf(PlayerHud.StorageGrid, "dough"), true);
                yield return Until(() => Count(inventory) == DevWorld.StarterDough + DevWorld.StorageDough && !interaction.HasPendingRequests,
                    "shift+click moved the storage stack");
                Assert.That(interaction.CursorGoods, Is.Null);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                yield return null;
                yield return null;
                Assert.That(interaction.QuickTransferHeld, Is.False);
                Click(PlayerHud.InventoryGrid, hud.SlotOf(PlayerHud.InventoryGrid, "dough#0"), false);
                Assert.That((interaction.CursorGoods?.ItemId, interaction.CursorGoods?.Quantity), Is.EqualTo(("dough", (int?)20)),
                    "A plain click picks up the slot's stack.");
            }
            finally
            {
                InputSystem.RemoveDevice(keyboard);
                settings.editorInputBehaviorInPlayMode = behavior;
                settings.backgroundBehavior = background;
                interaction.CloseScreen();
            }
        }

        [UnityTest]
        public IEnumerator ThePressThatOpensAScreenNeverClicksASlot()
        {
            yield return StartHost();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var settings = InputSystem.settings;
            var behavior = settings.editorInputBehaviorInPlayMode;
            // The test runner's window may not focus the Game view; route the virtual mouse to the player regardless.
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var background = settings.backgroundBehavior;
            // Runs from an unfocused Editor too: keep virtual devices enabled without application focus.
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return Until(() => interaction.PointerLocked, "gameplay pointer lock");
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                yield return null;
                Assert.That(mouse.leftButton.isPressed, Is.True, "The virtual press reached the input system.");
                var place = InputSystem.ListEnabledActions().FirstOrDefault(x => x.name == "Place");
                Assert.That(place?.IsPressed(), Is.True, $"Place sees the press (controls: {string.Join(",", place?.controls.Select(x => x.path) ?? new string[0])}).");
                yield return Until(() => { interaction.OpenMachine(DevWorld.OvenId); return interaction.Screen == InteractionScreen.Machine; }, "oven screen");
                yield return null;
                yield return null;
                Assert.That(interaction.ScreenClicksArmed, Is.False, "The press that opened the screen is still held.");
                InputSystem.QueueStateEvent(mouse, new MouseState());
                yield return null;
                Assert.That(interaction.ScreenClicksArmed, Is.False, "Its release cannot count as a slot click in the same frame.");
                yield return null;
                Assert.That(interaction.ScreenClicksArmed, Is.True, "A later press may click slots.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                settings.editorInputBehaviorInPlayMode = behavior;
                settings.backgroundBehavior = background;
                interaction.CloseScreen();
            }
        }

        [UnityTest]
        public IEnumerator ServerRestartShowsOvenWhereLastCommitted()
        {
            yield return StartHost();
            var host = _root.ClientSubscription.Bridge;
            host.RequestPickUp("pick", DevWorld.OvenId);
            yield return Await("pick");
            host.RequestPlace("place", DevWorld.OvenId, 3, 8, 2);
            yield return Await("place");
            Assert.That(_results["place"].Accepted, Is.True, _results["place"].Reason);

            _root.Shutdown();
            Assert.That(_root.Begin(SessionMode.Server), Is.False, "A restart must wait until the old server has fully stopped.");
            yield return Until(() => _root.CanBegin, "old server fully stopped");
            Assert.That(_root.Begin(SessionMode.Server), Is.True);
            yield return Until(() => _root.ServerBridge != null, "server-only restart");
            var oven = Oven(_root.ServerWorld.Snapshot());
            Assert.That((oven.State, oven.CellX, oven.CellZ, oven.Rotation), Is.EqualTo((EquipmentState.Placed, 3, 8, 2)));
            Assert.That(_root.ServerWorld.Snapshot().Equipment.Count, Is.EqualTo(1), "The seed is not re-applied to an existing save.");
        }
    }
}
