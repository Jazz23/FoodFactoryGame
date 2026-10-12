// Decision 0043 through the real SampleScene host (the scene with the employee prefab): stations bought from the supplier and
// placed; a machine with several recipes shows a Recipe chooser on its screen and the choice replicates; the kitchen counter
// makes nothing until someone works it, works through its Work this counter button, and is left when the player walks away;
// and an employee's Work at counter task works it for a batch and leaves. Every save and identity path is a unique temporary
// directory; the beef is TEST-ONLY stock put straight into the counter on the server.
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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class KitchenSessionTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        // Isolates the Input System like the other session tests, so the scene's input never touches the real devices.
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private EquipmentInteraction _interaction;
        private PlayerHud _hud;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 15f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
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
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryKitchenPlay", Guid.NewGuid().ToString("N"));
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
            _hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && Avatar() != null, "host baseline");
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

        private static PlayerAvatar Avatar() => UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner);

        private GoodsEquipment Piece(string id) => _root.ClientSite?.Equipment.SingleOrDefault(x => x.Id == id);

        // Buys a station from the supplier and places it on the first free ground cell of the site, rotation 0; returns its ID.
        private IEnumerator BuyAndPlace(string offerId, string kind, Action<string> placed)
        {
            var bridge = _root.ClientSubscription.Bridge;
            var me = _root.Authenticator.LocalPlayerId;
            bridge.RequestPurchase("buy-" + kind, _root.ClientSiteId, offerId);
            GoodsEquipment held = null;
            yield return Until(() => (held = _root.ClientSite.Equipment.FirstOrDefault(x => x.Kind == kind && x.State == EquipmentState.Held && x.HolderId == me)) != null,
                $"a bought {kind}");
            var site = _root.ClientSite;
            var layout = site.SiteLayouts.Single(x => x.SiteId == _root.ClientSiteId);
            var cell = Enumerable.Range(0, layout.Width * layout.Depth).Select(i => (X: i % layout.Width, Z: i / layout.Width))
                .Where(c => c.X >= 2 && c.Z >= 2)
                .First(c => SiteGrid.PlacementProblem(site, held, c.X, c.Z, 0, 0) == null);
            var id = held.Id;
            bridge.RequestPlace("place-" + kind, id, cell.X, cell.Z, 0);
            yield return Until(() => Piece(id)?.State == EquipmentState.Placed, $"the {kind} placed");
            placed(id);
        }

        // Moves the local avatar to a point on the floor beside a station (or far from it), as walking there would.
        private void Teleport(Vector3 point)
        {
            var avatar = Avatar();
            var controller = avatar.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            avatar.transform.position = point + Vector3.up * 0.05f;
            if (controller != null) controller.enabled = true;
        }

        private Vector3 Beside(string id)
        {
            var piece = Piece(id);
            var layout = _root.ClientSite.SiteLayouts.Single(x => x.SiteId == piece.SiteId);
            var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
            return SiteGridSpace.Center(layout, piece) - new Vector3(0f, 0f, depth * SiteGrid.CellSize * 0.5f + 0.6f);
        }

        private void StockBeef(string counterId) => _root.ServerWorld.Bootstrap(new GoodsLot
        {
            Id = "test-beef", ItemId = "beef", OwnerId = _root.ClientSiteId, LocationId = counterId + ":in", Quantity = 4, SpoilAfterSeconds = 100_000
        });

        private int Patties(string counterId) =>
            _root.ClientSite.Lots.Where(x => x.LocationId == counterId + ":out" && x.ItemId == "patty").Sum(x => x.Quantity);

        [UnityTest]
        public IEnumerator RecipeChooserAndKitchenCounterWorkThroughTheirScreens()
        {
            string mixer = null, counter = null;
            yield return BuyAndPlace("supplier-mixer", "mixer", x => mixer = x);
            yield return BuyAndPlace("supplier-kitchen-counter", "kitchen-counter", x => counter = x);
            var me = _root.Authenticator.LocalPlayerId;

            // The mixer's screen offers Automatic and its four recipes; a choice replicates and the header follows it.
            Teleport(Beside(mixer));
            yield return Until(() => { _interaction.OpenMachine(mixer); return _interaction.Screen == InteractionScreen.Machine; }, "mixer screen");
            yield return null;
            var choice = _hud.ScreenRoot.Q<DropdownField>("hud-recipe-choice");
            Assert.That(choice, Is.Not.Null, "A machine with several recipes has a recipe chooser.");
            Assert.That(choice.choices.Count, Is.EqualTo(5));
            Assert.That(choice.value, Does.StartWith("Automatic"));
            _hud.ClickRecipe(mixer, "mixer-pasta");
            yield return Until(() => Piece(mixer).RecipeId == "mixer-pasta" && !_interaction.HasPendingRequests, "the mixer set to pasta");
            yield return Until(() => _hud.ScreenRoot.Q<DropdownField>("hud-recipe-choice")?.value.Contains("Pasta") == true, "the chooser shows pasta");
            _interaction.CloseScreen();

            // The counter makes nothing until someone works it.
            StockBeef(counter);
            Teleport(Beside(counter));
            yield return Until(() => { _interaction.OpenMachine(counter); return _interaction.Screen == InteractionScreen.Machine; }, "counter screen");
            _hud.ClickRecipe(counter, "kitchen-patty");
            yield return Until(() => Piece(counter).RecipeId == "kitchen-patty" && !_interaction.HasPendingRequests, "the counter set to patties");
            yield return Seconds(2f);
            Assert.That(_root.ClientSite.Jobs.Any(x => x.StationId == counter), Is.False, "Nobody works the counter.");
            var work = _hud.ScreenRoot.Q<Button>("hud-staff-button");
            Assert.That(work?.text, Is.EqualTo("Work this counter"));
            Assert.That(_hud.ScreenRoot.Query<Button>().ToList().Any(x => x.name.StartsWith("hud-staff-employee")), Is.False, "No Assign buttons.");
            _hud.ClickStaff(counter, true);
            yield return Until(() => Piece(counter).StaffId == me, "the host works the counter");
            yield return Until(() => Patties(counter) >= 1, "a patty from the counter", 20f);

            // Walking away leaves the counter, and its next batch waits.
            Teleport(Beside(counter) + new Vector3(0f, 0f, -12f));
            yield return Until(() => Piece(counter).StaffId == "", "the host walked away and left");
            var job = _root.ClientSite.Jobs.SingleOrDefault(x => x.StationId == counter);
            var remaining = job?.RemainingSeconds;
            yield return Seconds(2.5f);
            Assert.That(_root.ClientSite.Jobs.SingleOrDefault(x => x.StationId == counter)?.RemainingSeconds, Is.EqualTo(remaining), "A paused batch waits.");
        }

        // Running-game captures for decision 0043 (the five stations placed, the mixer's recipe chooser, the kitchen counter being
        // worked, the supplier's ingredient packs and an employee's Work at counter task). Runs only when the file
        // Temp/kitchen-captures.flag exists; writes PNGs to docs/verification/kitchen-20261012/.
        [UnityTest]
        public IEnumerator KitchenCaptures()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "..", "Temp", "kitchen-captures.flag")))
                Assert.Ignore("Captures run on request: create Temp/kitchen-captures.flag.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "kitchen-20261012"));
            Directory.CreateDirectory(output);
            IEnumerator Shot(string name)
            {
                for (var frame = 0; frame < 10; frame++) yield return null;
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(output, name), screen.EncodeToPNG());
                UnityEngine.Object.Destroy(screen);
            }
            var ids = new System.Collections.Generic.List<string>();
            yield return BuyAndPlace("supplier-mixer", "mixer", x => ids.Add(x));
            // TEST-ONLY: the dev company cannot afford all five, so the stove, fryer and grill are placed straight on the server.
            foreach (var kind in new[] { "stove", "fryer", "grill" })
            {
                var definition = _root.EquipmentDefinitions.Single(x => x != null && x.Kind == kind);
                var state = _root.ServerWorld.Snapshot();
                var layout0 = state.SiteLayouts.Single(x => x.SiteId == _root.ClientSiteId);
                var probe = definition.CreatePlaced("capture-" + kind, _root.ClientSiteId, 0, 0, 0);
                var cell = Enumerable.Range(0, layout0.Width * layout0.Depth).Select(i => (X: i % layout0.Width, Z: i / layout0.Width))
                    .Where(c => c.X >= 2 && c.Z >= 2).First(c => SiteGrid.PlacementProblem(state, probe, c.X, c.Z, 0, 0) == null);
                _root.ServerWorld.Bootstrap(definition.CreatePlaced("capture-" + kind, _root.ClientSiteId, cell.X, cell.Z, 0));
                ids.Add("capture-" + kind);
            }
            yield return BuyAndPlace("supplier-kitchen-counter", "kitchen-counter", x => ids.Add(x));
            yield return Until(() => ids.All(x => Piece(x)?.State == EquipmentState.Placed), "every station replicated");
            var counter = ids[4];
            StockBeef(counter);
            var layout = _root.ClientSite.SiteLayouts.Single(x => x.SiteId == _root.ClientSiteId);
            var focus = ids.Select(x => SiteGridSpace.Center(layout, Piece(x))).Aggregate(Vector3.zero, (a, b) => a + b) / ids.Count;
            var rig = Avatar().CameraRig;
            rig.SetBuildView(focus, 9f, 180f, 30f);
            yield return Seconds(2f);
            yield return Shot("stations.png");
            rig.SetBuildView(null, 0f);

            Teleport(Beside(ids[0]));
            _interaction.OpenMachine(ids[0]);
            _hud.ClickRecipe(ids[0], "mixer-pasta");
            yield return Until(() => Piece(ids[0]).RecipeId == "mixer-pasta" && !_interaction.HasPendingRequests, "mixer set");
            yield return Shot("mixer-recipe.png");
            _interaction.CloseScreen();

            Teleport(Beside(counter));
            _interaction.OpenMachine(counter);
            _hud.ClickRecipe(counter, "kitchen-patty");
            yield return Until(() => Piece(counter).RecipeId == "kitchen-patty" && !_interaction.HasPendingRequests, "counter set");
            _hud.ClickStaff(counter, true);
            yield return Until(() => _root.ClientSite.Jobs.Any(x => x.StationId == counter), "the counter working");
            yield return Seconds(2f);
            yield return Shot("kitchen-counter-worked.png");
            _interaction.CloseScreen();

            _interaction.ToggleInventory();
            yield return Shot("supplier.png");
            _interaction.CloseScreen();

            EmployeeWorker employee = null;
            yield return Until(() => (employee = UnityEngine.Object.FindAnyObjectByType<EmployeeWorker>()) != null
                && !string.IsNullOrEmpty(employee.EmployeeId), "dev employee spawned");
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            _interaction.OpenEmployeeScreen(employee);
            yield return null;
            panel.AddTask();
            panel.SetTaskType(0, EmployeeTaskType.Work);
            panel.SetPlace(panel.Draft, panel.Draft.Tasks[0], PickTarget.Station, counter);
            panel.SetTaskRecipe(0, "kitchen-patty");
            panel.AddTask();
            panel.SetTaskType(1, EmployeeTaskType.Recipe);
            panel.SetPlace(panel.Draft, panel.Draft.Tasks[1], PickTarget.RecipeMachine, ids[0]);
            panel.SetTaskRecipe(1, "mixer-batter");
            yield return Shot("employee-kitchen-tasks.png");
            _interaction.CloseScreen();
        }

        [UnityTest]
        public IEnumerator AnEmployeeWorksTheCounterForABatchAndLeaves()
        {
            string counter = null;
            yield return BuyAndPlace("supplier-kitchen-counter", "kitchen-counter", x => counter = x);
            StockBeef(counter);
            EmployeeWorker employee = null;
            yield return Until(() => (employee = UnityEngine.Object.FindAnyObjectByType<EmployeeWorker>()) != null
                && !string.IsNullOrEmpty(employee.EmployeeId), "dev employee spawned");
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            _interaction.OpenEmployeeScreen(employee);
            yield return null;
            panel.AddTask();
            panel.SetTaskType(0, EmployeeTaskType.Work);
            panel.SetPlace(panel.Draft, panel.Draft.Tasks[0], PickTarget.Station, counter);
            panel.SetTaskRecipe(0, "kitchen-patty");
            Assert.That(panel.SourceField.value, Does.Contain($"work(\"{counter}\", \"kitchen-patty\")"));
            yield return null;
            Assert.That(panel.TaskList.Q<DropdownField>("employee-task-0-recipe")?.value, Is.EqualTo("Patty"));
            panel.ClickRun();
            yield return Until(() => Piece(counter).StaffId == employee.EmployeeId, "the employee works the counter", 60f);
            yield return Until(() => Patties(counter) >= 1, "a patty made by the employee", 60f);
            Assert.That(employee.Status, Does.Not.StartWith("Error"), employee.Status);
            panel.ClickStop();
            yield return Until(() => Piece(counter).StaffId == "", "stopping leaves the counter");
        }
    }
}
