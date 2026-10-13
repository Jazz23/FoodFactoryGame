// Owner feedback of 2026-10-12 (decision 0045) in the real WorldGen scene, as a host of a new generated world in a unique
// temporary directory: the wheel zooms indoors (top-down and third person) but not while a screen is open; Esc opens the in-game
// menu, whose Quit to Main Menu ends the session; Select World deletes a world after a second click; an employee is renamed and
// fired from its own screen; and "Select counter" tints every valid target (the register among them) and picks the one under the
// pointer. Input comes from virtual devices (InputTestFixture); the application's saves and settings are never opened.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoodFactoryGame.Goods;
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
    public sealed class OwnerFeedbackRound3Tests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world (the one the WorldGen session tests use).
        private const string Seed = "piece-two";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private EquipmentInteraction _interaction;
        private Keyboard _keyboard;
        private Mouse _mouse;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 30f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        private static IEnumerator Frames(int count)
        {
            for (var index = 0; index < count; index++) yield return null;
        }

        private static IEnumerator Seconds(float seconds)
        {
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryFeedback3", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "Saves", "feedback"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host", Address = "127.0.0.1", WorldSeed = Seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && Avatar() != null, "host baseline and avatar", 90f);
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

        private PlayerAvatar Avatar() => UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner);

        private OrbitCameraRig Rig => Avatar().CameraRig;

        private EmployeeScriptPanel EmployeePanel => UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();

        private SessionPanel Menu => UnityEngine.Object.FindAnyObjectByType<SessionPanel>();

        private VisualElement MenuRoot => Menu.GetComponent<UIDocument>().rootVisualElement;

        private GoodsBuilding Shell => _root.ClientSite.Buildings.Single(x => x.SiteId == _root.StartOffer.SiteId);

        // Puts the avatar on the interior cell nearest the given cell (the restaurant's middle by default) and waits for the view.
        private IEnumerator GoInside((int X, int Z)? near = null)
        {
            var shell = Shell;
            var target = near ?? (shell.CellX + shell.Width / 2, shell.CellZ + shell.Depth / 2);
            var cell = SiteGrid.InteriorCells(shell)
                .Where(c => _root.ClientSite.Equipment.All(e => e.State != EquipmentState.Placed || !SiteGrid.Overlaps(c.X, c.Z, 1, 1, e.CellX, e.CellZ,
                    SiteGrid.Footprint(e.Width, e.Depth, e.Rotation).Width, SiteGrid.Footprint(e.Width, e.Depth, e.Rotation).Depth)))
                .OrderBy(c => Mathf.Abs(c.X - target.X) + Mathf.Abs(c.Z - target.Z)).First();
            var layout = _root.ClientSite.SiteLayouts.Single(x => x.SiteId == _root.ClientSiteId);
            Avatar().Teleport(SiteGridSpace.FootprintCenter(layout, cell.X, cell.Z, 1, 1) + Vector3.up * 0.1f);
            yield return Until(() => _interaction.Buildings.LocalBuildingId != null && Rig.TopDown, "indoors in the top-down view");
            yield return Seconds(0.6f);
        }

        private IEnumerator Scroll(float y)
        {
            InputSystem.QueueStateEvent(_mouse, new MouseState { scroll = new Vector2(0f, y), position = _mouse.position.ReadValue() });
            yield return Frames(2);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = _mouse.position.ReadValue() });
            yield return Frames(2);
        }

        private IEnumerator Key(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            yield return Frames(2);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return Frames(2);
        }

        private IEnumerator HireOne()
        {
            var before = UnityEngine.Object.FindObjectsByType<EmployeeWorker>().Length;
            _interaction.Hire();
            yield return Until(() => UnityEngine.Object.FindObjectsByType<EmployeeWorker>().Length == before + 1
                && !string.IsNullOrEmpty(UnityEngine.Object.FindObjectsByType<EmployeeWorker>().Last().EmployeeId), "a hired worker");
        }

        private EmployeeWorker NewestWorker() => UnityEngine.Object.FindObjectsByType<EmployeeWorker>()
            .OrderByDescending(x => _root.ClientSite.Employees.FindIndex(e => e.Id == x.EmployeeId)).First();

        [UnityTest]
        public IEnumerator TheWheelZoomsIndoorsButNotWhileAScreenIsOpen()
        {
            TestContext.WriteLine($"start restaurant {Shell.Width} x {Shell.Depth}");
            yield return GoInside();
            var before = Rig.TopDownDistance;
            yield return Scroll(120f);
            Assert.That(Rig.TopDownDistance, Is.LessThan(before - 0.5f), "The wheel zooms the indoor top-down view.");

            _interaction.ToggleInventory();
            yield return Frames(2);
            var open = Rig.TopDownDistance;
            yield return Scroll(120f);
            yield return Scroll(-120f);
            yield return Scroll(-120f);
            Assert.That(Rig.TopDownDistance, Is.EqualTo(open), "Scrolling the inventory leaves the camera alone.");
            _interaction.CloseScreen();
            yield return Frames(2);

            // Third person indoors: one notch in moves the camera in from where walls hold it, not from the hidden distance. C no
            // longer switches views (it opens the phone, decision 0047), so the test switches the rig directly.
            typeof(OrbitCameraRig).GetMethod("SetTopDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Invoke(Rig, new object[] { false });
            yield return Until(() => !Rig.TopDown, "the third-person view");
            yield return Seconds(1f);
            var arm = Rig.CurrentArm;
            var distance = Rig.Distance;
            yield return Scroll(120f);
            TestContext.WriteLine($"third person: arm {arm:F2} of distance {distance:F2}, then distance {Rig.Distance:F2}, arm {Rig.CurrentArm:F2}");
            Assert.That(Rig.Distance, Is.LessThanOrEqualTo(Mathf.Min(arm, distance) - 0.5f), "One notch zooms in from the visible arm.");
            yield return Seconds(0.5f);
            Assert.That(Rig.CurrentArm, Is.LessThan(arm - 0.2f), "The camera moved in.");
        }

        [UnityTest]
        public IEnumerator EscOpensTheInGameMenuAndQuitToMainMenuEndsTheSession()
        {
            yield return Key(UnityEngine.InputSystem.Key.Escape);
            yield return Until(() => _interaction.Screen == InteractionScreen.Menu, "the in-game menu");
            yield return Frames(2);
            Assert.That(MenuRoot.Q("main-menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "The menu overlay shows in game.");
            Assert.That(MenuRoot.Q("menu-pause-resume"), Is.Not.Null);
            Assert.That(MenuRoot.Q<Label>("menu-pause-detail").text, Is.EqualTo("feedback"));
            var place = (InputActionReference)typeof(EquipmentInteraction).GetField("placeAction", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(_interaction);
            var move = place.action.actionMap.FindAction("Move");
            Assert.That(move.enabled, Is.False, "Gameplay input is suspended under the menu.");

            // Settings opens inside the menu, and Esc goes back to the menu rather than closing it.
            Menu.ClickInGameSettings();
            yield return Until(() => MenuRoot.Q("menu-sensitivity") != null, "the settings screen in game");
            yield return Key(UnityEngine.InputSystem.Key.Escape);
            yield return Until(() => MenuRoot.Q("menu-pause-resume") != null, "back to the in-game menu");
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.Menu));

            yield return Key(UnityEngine.InputSystem.Key.Escape);
            yield return Until(() => _interaction.Screen == InteractionScreen.None, "Esc closes the menu");
            yield return Frames(2);
            Assert.That(MenuRoot.Q("main-menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(move.enabled, Is.True, "Gameplay input is back.");

            yield return Key(UnityEngine.InputSystem.Key.Escape);
            yield return Until(() => _interaction.Screen == InteractionScreen.Menu, "the menu again");
            Menu.ClickQuitToMainMenu();
            yield return Until(() => !_root.IsRunning, "the session ends");
            yield return Frames(2);
            Assert.That(MenuRoot.Q("main-menu").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(MenuRoot.Q("menu-select-world"), Is.Not.Null, "Back at the main screen.");
        }

        [UnityTest]
        public IEnumerator SelectWorldDeletesAWorldOnlyAfterASecondClick()
        {
            var world = _root.WorldName;
            _root.Shutdown();
            yield return Until(() => _root.CanBegin, "the session stopped");
            Assert.That(_root.SavedWorlds().Select(x => x.Name), Has.Member(world));
            Menu.ClickDeleteWorld(world);
            Assert.That(_root.SavedWorlds().Select(x => x.Name), Has.Member(world), "The first click only asks.");
            Assert.That(MenuRoot.Q<Button>("menu-delete-world").text, Does.StartWith("Click again"));
            Menu.ClickDeleteWorld(world);
            Assert.That(_root.SavedWorlds().Select(x => x.Name), Has.No.Member(world));
            Assert.That(Directory.Exists(Path.Combine(_directory, "Saves", world)), Is.False);
        }

        [UnityTest]
        public IEnumerator AnEmployeeIsRenamedAndFiredFromItsScreen()
        {
            yield return HireOne();
            var worker = NewestWorker();
            var id = worker.EmployeeId;
            _interaction.OpenEmployeeScreen(worker);
            yield return Until(() => EmployeePanel.Window.resolvedStyle.display == DisplayStyle.Flex, "the employee screen");

            EmployeePanel.ClickRename();
            EmployeePanel.SaveName("   ");
            yield return Frames(5);
            Assert.That(worker.DisplayName, Is.Not.Empty.And.Not.EqualTo("   "), "A blank name is not sent.");
            EmployeePanel.SaveName("  Robin ");
            yield return Until(() => worker.DisplayName == "Robin" && _root.ClientSite.Employees.Single(x => x.Id == id).Name == "Robin", "the rename");
            Assert.That(_root.ServerWorld.Employees().Single(x => x.Id == id).Name, Is.EqualTo("Robin"));
            Assert.That(EmployeePanel.Window.Q<Label>("employee-script-title").text, Is.EqualTo("Robin"));

            EmployeePanel.ClickFire();
            yield return Frames(10);
            Assert.That(_root.ClientSite.Employees.Any(x => x.Id == id), Is.True, "The first Fire only asks.");
            EmployeePanel.ClickFire();
            yield return Until(() => _root.ClientSite.Employees.All(x => x.Id != id) && worker == null, "the firing and despawn");
            yield return Until(() => _interaction.Screen == InteractionScreen.None, "the screen closes");
        }

        // Owner report: after Rename, clicking the name box showed no caret and the typed name was never saved. Clicked as a
        // player does: pointer presses at the centres of Rename, the box and Save, dispatched by position so the panel picks
        // what is drawn there; then key events to whatever the click focused.
        [UnityTest]
        public IEnumerator TheNameBoxTakesAClickAndTheTypedNameIsSaved()
        {
            yield return HireOne();
            var worker = NewestWorker();
            var id = worker.EmployeeId;
            _interaction.OpenEmployeeScreen(worker);
            yield return Until(() => EmployeePanel.Window.resolvedStyle.display == DisplayStyle.Flex, "the employee screen");
            yield return Until(() => _interaction.ScreenClicksArmed, "screen clicks armed");
            var window = EmployeePanel.Window;
            var field = window.Q<TextField>("employee-script-name");
            yield return PointerClick(window.Q<Button>("employee-script-rename"));
            yield return Until(() => field.worldBound.height > 0f, "the name box");
            yield return Frames(3);
            var text = field.Q<TextElement>();
            var picked = window.panel.Pick(text.worldBound.center);
            Assert.That(picked == field || field.Contains(picked), Is.True, $"The name box is under the pointer, not '{picked?.name}' {picked?.worldBound}.");
            yield return PointerClick(text);
            Assert.That(field.focusController?.focusedElement, Is.EqualTo(field), "The click focuses the name box.");
            foreach (var character in "Kai")
            {
                using var typed = KeyDownEvent.GetPooled(character, KeyCode.None, EventModifiers.None);
                ((VisualElement)field.focusController.focusedElement).SendEvent(typed);
                yield return null;
            }
            var name = field.value.Trim();
            Assert.That(name, Does.Contain("Kai"), "Typing reaches the name box.");
            yield return PointerClick(window.Q<Button>("employee-script-rename-save"));
            yield return Until(() => worker.DisplayName == name && _root.ServerWorld.Employees().Single(x => x.Id == id).Name == name, "the typed name saved");
        }

        // Owner report: an employee walked out the front and around the building to the loading dock instead of through the back
        // door. A single (1 m) doorway between whole-cell walls was closed in the NavMesh by the agent radius; now each door of
        // the start restaurant is on it, and the walk to the back door's doorstep goes through that door.
        [UnityTest]
        public IEnumerator EmployeesCanWalkThroughTheBackDoor()
        {
            var shell = Shell;
            var layout = _root.ClientSite.SiteLayouts.Single(x => x.SiteId == _root.ClientSiteId);
            var back = shell.Structures.Single(x => x.Kind == GoodsWorld.DoorStructure && x.Role == GoodsWorld.ServiceDoorRole);
            var door = SiteGridSpace.FootprintCenter(layout, back.X, back.Z, 1, 1);
            yield return Until(() => UnityEngine.AI.NavMesh.SamplePosition(door, out _, 0.3f, UnityEngine.AI.NavMesh.AllAreas), "the back door on the NavMesh", 10f);
            var step = SiteGrid.Doorstep(shell, back.X, back.Z).Value;
            var outside = SiteGridSpace.FootprintCenter(layout, step.X, step.Z, 1, 1);
            var middle = SiteGridSpace.FootprintCenter(layout, shell.CellX + shell.Width / 2, shell.CellZ + shell.Depth / 2, 1, 1);
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(middle, out var from, 2f, UnityEngine.AI.NavMesh.AllAreas), Is.True);
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(outside, out var to, 2f, UnityEngine.AI.NavMesh.AllAreas), Is.True);
            var path = new UnityEngine.AI.NavMeshPath();
            Assert.That(UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, path), Is.True);
            var length = 0f;
            for (var index = 1; index < path.corners.Length; index++) length += Vector3.Distance(path.corners[index - 1], path.corners[index]);
            var straight = Vector3.Distance(middle, outside);
            Assert.That((path.status, length < straight * 1.25f), Is.EqualTo((UnityEngine.AI.NavMeshPathStatus.PathComplete, true)),
                $"The walk to the back door's doorstep is {length:F1} m for {straight:F1} m in a straight line.");
        }

        private static IEnumerator PointerClick(VisualElement target)
        {
            var position = target.worldBound.center;
            var tree = target.panel.visualTree;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = position, button = 0, clickCount = 1 }))
                tree.SendEvent(down);
            yield return Frames(2);
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = position, button = 0, clickCount = 1 }))
                tree.SendEvent(up);
            yield return Frames(3);
        }

        [UnityTest]
        public IEnumerator SelectCounterTintsEveryValidTargetAndPicksTheOneUnderThePointer()
        {
            var register = _root.ClientSite.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            yield return GoInside((register.CellX, register.CellZ));
            yield return HireOne();
            _interaction.OpenEmployeeScreen(NewestWorker());
            yield return Until(() => EmployeePanel.Window.resolvedStyle.display == DisplayStyle.Flex, "the employee screen");
            EmployeePanel.AddTask();
            EmployeePanel.SetTaskType(0, EmployeeTaskType.Work);
            EmployeePanel.ClickPick(0, PickTarget.Station);
            yield return Until(() => _interaction.Screen == InteractionScreen.PickPosition && _interaction.PickTargets.Count > 0, "the counter pick");
            yield return Frames(2);
            var visual = _interaction.PickTargets.OfType<EquipmentVisual>().SingleOrDefault(x => x.EquipmentId == register.Id);
            Assert.That(visual, Is.Not.Null, "The register is a valid counter.");
            foreach (var target in _interaction.PickTargets)
                Assert.That(target.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterials.Any(m => m != null && m.shader.name == "FoodFactory/XRayTint")),
                    Is.True, $"{target.name} is tinted through walls.");

            // Point at the register (the indoor view leaves the pointer free) and click it.
            var camera = Rig.GetComponentInChildren<Camera>();
            var bounds = visual.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
            var screen = camera.WorldToScreenPoint(bounds.center);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(screen.x, screen.y) });
            yield return Until(() => _interaction.PickHovered == visual, "the register under the pointer");
            Assert.That(visual.GetComponentsInChildren<MeshRenderer>().Any(r => r.sharedMaterials.Any(m => m != null && m.name == "TargetOutline")),
                Is.True, "The hovered target has the bold outline.");
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(screen.x, screen.y) }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
            yield return Frames(2);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(screen.x, screen.y) });
            yield return Until(() => _interaction.Screen == InteractionScreen.Employee, "back on the employee screen");
            Assert.That(EmployeePanel.Draft.Tasks[0].Machine, Is.EqualTo(register.Id));
            Assert.That(EmployeePanel.SourceField.value, Does.Contain($"work(\"{register.Id}\")"));
        }
    }
}
