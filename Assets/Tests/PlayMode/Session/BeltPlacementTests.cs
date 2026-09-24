// Drives belts through the real DevSite host session with virtual mouse and keyboard input: a belt stack carried out of
// the inventory, a Factorio-style drag with R making a corner mid-drag, synchronised tread scrolling, one item put on a
// belt with Z, carried to the end of the line and taken back off with F, and right-click removal returning belt and
// item. Saves and identities live in a unique temporary directory.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Belts;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class BeltPlacementTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private BeltPresenter _belts;
        private EquipmentInteraction _interaction;
        private PlayerHud _hud;
        private Mouse _mouse;
        private Keyboard _keyboard;
        private PlayerAvatar _avatar;
        private Vector3 _aimOffset;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Isolated input state: no real device (a wheel, the developer's own mouse) reaches the session under test.
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryBeltPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _belts = UnityEngine.Object.FindAnyObjectByType<BeltPresenter>();
            _interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            _hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            Assert.That(_belts, Is.Not.Null, "DevSite must contain a BeltPresenter.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host",
                Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_mouse != null) InputSystem.RemoveDevice(_mouse);
                if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
                _mouse = null;
                _keyboard = null;
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

        private GoodsSnapshot Server => _root.ServerWorld.Snapshot();
        private GoodsBelt BeltAt(int x, int z) => Server.Belts.SingleOrDefault(b => b.CellX == x && b.CellZ == z);
        private int Carried(string itemId) => Server.Lots.Where(x => x.LocationId == _interaction.InventoryId && x.ItemId == itemId).Sum(x => x.Quantity);

        // A steep camera puts the crosshair's floor point just ahead of the avatar; moving the avatar moves the aim.
        private IEnumerator SteepCamera()
        {
            yield return Until(() => (_avatar = UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None).FirstOrDefault(x => x.IsOwner)) != null
                && _interaction.PointerLocked, "owned avatar and locked pointer");
            typeof(OrbitCameraRig).GetField("pitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_avatar.CameraRig, 80f);
            typeof(OrbitCameraRig).GetField("yaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_avatar.CameraRig, 0f);
            yield return null;
            yield return null;
            var camera = _avatar.CameraRig.GetComponentInChildren<Camera>();
            var ray = camera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            Assert.That(new Plane(Vector3.up, Vector3.zero).Raycast(ray, out var distance), Is.True);
            _aimOffset = ray.GetPoint(distance) - _avatar.transform.position;
            _aimOffset.y = 0f;
        }

        private IEnumerator AimAt(int x, int z)
        {
            var layout = _root.ClientSite.SiteLayouts.Single();
            var controller = _avatar.GetComponent<CharacterController>();
            controller.enabled = false;
            _avatar.transform.position = BeltPath.CellCenter(layout, x, z) - _aimOffset;
            controller.enabled = true;
            yield return Until(() => _interaction.AimCell == (x, z), $"crosshair on cell ({x}, {z})");
        }

        private IEnumerator Press(Action press, Action release)
        {
            press();
            yield return null;
            yield return null;
            release();
            yield return null;
        }

        private void Mouse(bool left, bool right)
        {
            var state = new MouseState();
            if (left) state = state.WithButton(MouseButton.Left);
            if (right) state = state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Key(Key key) => Press(() => InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key)),
            () => InputSystem.QueueStateEvent(_keyboard, new KeyboardState()));

        // Picks a whole inventory stack onto the cursor and closes the screen, which keeps it there.
        private IEnumerator CarryFromInventory(string itemId)
        {
            _interaction.ToggleInventory();
            yield return Until(() => _hud.SlotOf(PlayerHud.InventoryGrid, itemId) >= 0, $"{itemId} in an inventory slot");
            _hud.ClickSlot(PlayerHud.InventoryGrid, _hud.SlotOf(PlayerHud.InventoryGrid, itemId));
            Assert.That(_interaction.CursorGoods?.ItemId, Is.EqualTo(itemId));
            _interaction.CloseScreen();
            Assert.That(_interaction.CursorGoods?.ItemId, Is.EqualTo(itemId), "Closing the inventory keeps the stack on the cursor.");
            yield return Until(() => _hud.CursorIcon.style.display.value == UnityEngine.UIElements.DisplayStyle.Flex, "cursor icon beside the crosshair");
        }

        [UnityTest]
        public IEnumerator DragWithRotateLaysACornerAndItemsRideToTheEnd()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _interaction.InventoryId != null, "host baseline");
            // Dev seed: belts in the storage and in each new player's inventory.
            Assert.That(Server.Lots.Where(x => x.LocationId == DevWorld.StorageId && x.ItemId == GoodsWorld.BeltItemId).Sum(x => x.Quantity), Is.EqualTo(DevWorld.StorageBelts));
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(DevWorld.StarterBelts));
            yield return SteepCamera();

            yield return CarryFromInventory(GoodsWorld.BeltItemId);
            Assert.That(_interaction.BeltCursor, Is.True);
            yield return AimAt(2, 2);
            yield return Until(() => _interaction.BeltGhostCount == 1, "hover ghost belt");
            Assert.That(_interaction.Rotation, Is.EqualTo(0), "Belts start facing +Z.");

            // Press and drag north three cells; R over the line's own head does nothing. R with the crosshair two cells east
            // (a right turn) corners the head and lays belts out to and including the crosshair's cell; R there again (now
            // over a belt) does nothing. Drag on east to (6,5).
            Mouse(true, false);
            yield return Until(() => _interaction.Dragging, "drag started");
            yield return AimAt(2, 5);
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.That(_interaction.Rotation, Is.EqualTo(0), "R over a belt while dragging does nothing.");
            yield return AimAt(4, 5);
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.That(_interaction.Rotation, Is.EqualTo(1), "R while dragging turns toward the crosshair.");
            yield return Until(() => Server.Belts.Any(x => x.CellX == 4 && x.CellZ == 5) && Server.Belts.Any(x => x.CellX == 3 && x.CellZ == 5),
                "belts laid out to the crosshair on R");
            Assert.That(BeltAt(2, 5).Direction, Is.EqualTo(1), "R cornered the head toward the crosshair.");
            yield return Key(UnityEngine.InputSystem.Key.R);
            Assert.That(_interaction.Rotation, Is.EqualTo(1), "R over the newly laid belt does nothing.");
            yield return AimAt(6, 5);
            Mouse(false, false);
            yield return Until(() => !_interaction.Dragging, "drag released");
            yield return Until(() => Server.Belts.Count == 8 && _interaction.PendingBeltCount == 0 && !_interaction.HasPendingRequests, "eight belts committed");

            foreach (var z in new[] { 2, 3, 4 }) Assert.That(BeltAt(2, z).Direction, Is.EqualTo(0), $"(2,{z}) north");
            foreach (var x in new[] { 2, 3, 4, 5, 6 }) Assert.That(BeltAt(x, 5).Direction, Is.EqualTo(1), $"({x},5) east");
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(DevWorld.StarterBelts - 8), "Turning the head cost no belt.");
            var cells = BeltRules.ByCell(Server.Belts);
            Assert.That(BeltRules.Shape(cells, BeltAt(2, 5)), Is.EqualTo(BeltShape.CurveFromRight), "The head became a corner.");
            yield return Until(() => _belts.Belts.Count == 8, "eight belt visuals");
            var corner = _belts.Belts[BeltAt(2, 5).Id];
            Assert.That(corner.name, Does.Contain(nameof(BeltShape.CurveFromRight)));
            Assert.That(_belts.Belts.Values.Count(x => x.name.Contains(nameof(BeltShape.Straight))), Is.EqualTo(7));

            // Arrow sync: every belt surface draws with the one scrolling tread material.
            var treads = _belts.Belts.Values.SelectMany(x => x.GetComponentsInChildren<Renderer>()).SelectMany(x => x.sharedMaterials)
                .Where(x => x != null && x.name.StartsWith("BeltTread", StringComparison.Ordinal)).Distinct().ToList();
            Assert.That(treads, Is.EqualTo(new[] { _belts.Tread }));
            var offset = _belts.Tread.GetTextureOffset("_BaseMap");
            yield return new WaitForSeconds(0.2f);
            Assert.That(_belts.Tread.GetTextureOffset("_BaseMap"), Is.Not.EqualTo(offset), "The tread scrolls.");

            // R with an empty cursor turns the belt under the crosshair; turning it back restores the line.
            _interaction.ClearCursor();
            yield return AimAt(6, 5);
            yield return Key(UnityEngine.InputSystem.Key.R);
            yield return Until(() => BeltAt(6, 5).Direction == 2 && !_interaction.HasPendingRequests, "belt turned");
            for (var turn = 0; turn < 3; turn++)
            {
                yield return Key(UnityEngine.InputSystem.Key.R);
                yield return Until(() => !_interaction.HasPendingRequests && _interaction.PendingBeltCount == 0, "turn committed");
            }
            Assert.That(BeltAt(6, 5).Direction, Is.EqualTo(1));

            // One dough carried out of the inventory, ghosted on the aimed belt, and put on it with Z.
            yield return CarryFromInventory(DevWorld.DoughItemId);
            Assert.That(_interaction.ItemCursor, Is.True);
            yield return AimAt(2, 2);
            yield return Until(() => _interaction.ItemGhostVisible, "item ghost on the belt");
            var start = BeltAt(2, 2);
            Assert.That(Vector3.Distance(_interaction.ItemGhostPosition,
                BeltPath.WorldPoint(_belts.Layout, start, BeltShape.Straight, BeltRules.Middle) + Vector3.up * (_belts.ItemSize * 0.5f)), Is.LessThan(0.01f));
            yield return Key(UnityEngine.InputSystem.Key.Z);
            yield return Until(() => Server.Lots.Count(x => x.LocationId.EndsWith(":items", StringComparison.Ordinal)) == 1, "one dough on the belt");
            Assert.That(Carried(DevWorld.DoughItemId), Is.EqualTo(DevWorld.StarterDough - 1), "Z puts exactly one item on the belt.");
            yield return Until(() => _belts.ItemIds.Count == 1, "riding item visual");

            // Seven belts of path from the middle of (2,2) to the end of (6,5); the item rests there, drawn where it is.
            var end = BeltAt(6, 5);
            yield return Until(() => Server.Lots.Any(x => x.LocationId == end.LocationId && x.BeltPosition == BeltRules.EndRest), "item at the end of the line", 15f);
            var drawn = _belts.transform.Cast<Transform>().Single(x => x.name.StartsWith("Riding", StringComparison.Ordinal));
            yield return Until(() => Vector3.Distance(drawn.position,
                BeltPath.WorldPoint(_belts.Layout, end, BeltShape.Straight, BeltRules.EndRest) + Vector3.up * (_belts.ItemSize * 0.5f)) < 0.01f, "drawn item caught up");

            // F with an empty cursor takes it back off the belt into the inventory, under the same ID.
            var ridingId = Server.Lots.Single(x => x.LocationId == end.LocationId).Id;
            _interaction.ClearCursor();
            yield return AimAt(6, 5);
            yield return Key(UnityEngine.InputSystem.Key.F);
            yield return Until(() => Server.Lots.Single(x => x.Id == ridingId).LocationId == _interaction.InventoryId && !_interaction.HasPendingRequests, "dough taken off");
            Assert.That(Carried(DevWorld.DoughItemId), Is.EqualTo(DevWorld.StarterDough));
            yield return Until(() => _belts.ItemIds.Count == 0, "riding visual gone");

            // Holding right click over the last belt takes it up with an item on it.
            yield return CarryFromInventory(DevWorld.DoughItemId);
            yield return AimAt(6, 5);
            yield return Key(UnityEngine.InputSystem.Key.Z);
            yield return Until(() => Server.Lots.Any(x => x.LocationId == end.LocationId) && !_interaction.HasPendingRequests, "dough on the last belt");
            _interaction.ClearCursor();
            Mouse(false, true);
            yield return Until(() => BeltAt(6, 5) == null && !_interaction.HasPendingRequests, "last belt removed");
            Mouse(false, false);
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(DevWorld.StarterBelts - 7));
            Assert.That(Carried(DevWorld.DoughItemId), Is.EqualTo(DevWorld.StarterDough), "The riding dough came back.");
            yield return Until(() => _belts.Belts.Count == 7 && _belts.ItemIds.Count == 0, "visuals follow");
            Assert.That(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot().Belts.Count, Is.EqualTo(7), "Belts are saved.");
        }

        [UnityTest]
        public IEnumerator HoldingRotateWhileDraggingFollowsTheCrosshairRoundCorners()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _interaction.InventoryId != null, "host baseline");
            yield return SteepCamera();
            yield return CarryFromInventory(GoodsWorld.BeltItemId);
            yield return AimAt(2, 2);

            // Start a drag north with R held: the crosshair stepping east turns the line east, then two cells north turns it
            // north again through the crosshair. With R released, moving east of the line lays nothing.
            Mouse(true, false);
            yield return Until(() => _interaction.Dragging, "drag started");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(UnityEngine.InputSystem.Key.R));
            yield return AimAt(3, 2);
            Assert.That(_interaction.Rotation, Is.EqualTo(1), "Held R turns the line east.");
            yield return AimAt(3, 4);
            Assert.That(_interaction.Rotation, Is.EqualTo(0), "Held R turns the line north again.");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return AimAt(5, 4);
            Assert.That(_interaction.Rotation, Is.EqualTo(0), "Without R the drag does not turn.");
            Mouse(false, false);
            yield return Until(() => !_interaction.Dragging, "drag released");
            yield return Until(() => Server.Belts.Count == 4 && _interaction.PendingBeltCount == 0 && !_interaction.HasPendingRequests, "four belts committed");

            Assert.That(BeltAt(2, 2).Direction, Is.EqualTo(1), "(2,2) east");
            foreach (var z in new[] { 2, 3, 4 }) Assert.That(BeltAt(3, z).Direction, Is.EqualTo(0), $"(3,{z}) north");
            Assert.That(Carried(GoodsWorld.BeltItemId), Is.EqualTo(DevWorld.StarterBelts - 4));
        }
    }
}
