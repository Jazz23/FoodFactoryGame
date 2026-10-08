// Hiring and wages through the real SampleScene host (decision 0039): Hire in the Staff window spawns a worker beside the
// player and Fire despawns it; an employee its company cannot pay stops its running script, walks outside the restaurant and
// the HUD warns, then it restarts once paid; clicking a square in the employee screen's hands moves that stack into the player's
// inventory; and the warning setting is saved for the player. The clock is stepped through the server world, not waited out.
// Every save and identity path is a unique temporary directory.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class HiringSessionTests
    {
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        // Isolates the Input System like the other session tests, so the scene's input never touches the real devices.
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private EquipmentInteraction _interaction;
        private PlayerHud _hud;

        private string WorldPath => Path.Combine(_directory, "save", SessionOptions.WorldFileName);

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        private static EmployeeWorker[] Workers() => UnityEngine.Object.FindObjectsByType<EmployeeWorker>();

        private static EmployeeWorker Worker(string id) => Workers().FirstOrDefault(x => x.EmployeeId == id);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryHiringPlay", Guid.NewGuid().ToString("N"));
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
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && Worker(DevWorld.EmployeeId) != null,
                "host baseline and the dev employee");
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
        public IEnumerator HireFromTheStaffWindowSpawnsAWorkerBesideThePlayerAndFireRemovesIt()
        {
            _interaction.ToggleInventory();
            yield return Until(() => _hud.ScreenRoot.Q("hud-staff-window") != null, "the Staff window");
            Assert.That(_hud.ScreenRoot.Q<Label>("hud-staff-count").text, Does.StartWith("Employees: 1 of 3"));

            _hud.ClickHire();
            yield return Until(() => _root.ClientSite.Employees.Count == 2 && Workers().Length == 2, "the hire and its worker");
            var hired = _root.ClientSite.Employees.Single(x => x.Id != DevWorld.EmployeeId).Id;
            var avatar = UnityEngine.Object.FindObjectsByType<Player.PlayerAvatar>().Single(x => x.IsOwner).transform.position;
            var worker = Worker(hired);
            Assert.That(Vector3.Distance(new Vector3(worker.transform.position.x, 0f, worker.transform.position.z), new Vector3(avatar.x, 0f, avatar.z)),
                Is.LessThan(2.5f), "The hire appears beside the player.");
            yield return Until(() => _hud.ScreenRoot.Q($"hud-fire-{hired}") != null, "the hire's Fire button");

            _hud.ClickFire(hired);
            yield return Until(() => _root.ClientSite.Employees.Count == 1 && Worker(hired) == null, "the firing and its despawn");
            Assert.That(_interaction.LastRejection, Is.Null.Or.Empty);
        }

        [UnityTest]
        public IEnumerator AnUnpaidEmployeeStopsWalksOutsideAndRestartsOncePaid()
        {
            var worker = Worker(DevWorld.EmployeeId);
            var layout = _root.ClientSite.SiteLayouts.Single(x => x.SiteId == DevWorld.SiteId);
            var shell = _root.ClientSite.Buildings.Single(x => x.Id == DevWorld.RestaurantId);
            var center = SiteGridSpace.FootprintCenter(layout, shell.CellX, shell.CellZ, shell.Width, shell.Depth);
            bool Inside()
            {
                var half = new Vector2(shell.Width, shell.Depth) * (SiteGrid.CellSize * 0.5f);
                var at = worker.transform.position;
                return Mathf.Abs(at.x - center.x) < half.x && Mathf.Abs(at.z - center.z) < half.y;
            }
            // The dev employee starts outside; it works inside the restaurant here, so it has somewhere to walk out of.
            var agent = worker.GetComponent<UnityEngine.AI.NavMeshAgent>();
            Assert.That(agent.Warp(center), Is.True, "The restaurant floor is walkable.");
            yield return null;
            Assert.That(Inside(), Is.True);
            var start = worker.transform.position;
            worker.RequestRun("while true do wait(1) end");
            yield return Until(() => worker.Status.StartsWith("Running"), "the script to run");
            var world = _root.ServerWorld;
            var cash = world.Snapshot().Companies.Single(x => x.Id == DevWorld.CompanyId).Cash;
            Assert.That(world.AdjustCashDurably(DevWorld.CompanyId, -cash, WorldPath), Is.Null);
            world.AdvanceUncommitted(GoodsWorld.GameHourSeconds);
            Assert.That(world.IsUnpaid(DevWorld.EmployeeId), Is.True);

            yield return Until(() => worker.Unpaid && worker.Status.StartsWith("Unpaid"), "the employee to stop unpaid");
            yield return Until(() => _root.ClientSite.Employees.Single().Unpaid, "the unpaid baseline");
            Assert.That(PlayerHud.WageWarning(_root.ClientSite, _interaction.WageWarningHours), Does.Contain("unpaid"));
            Assert.That(_hud.GetComponent<UIDocument>().rootVisualElement.Q("hud-wage-warning").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            yield return Until(() => !Inside() && agent.velocity.sqrMagnitude < 0.01f, "the employee to wait outside the restaurant", 40f);
            Assert.That(Vector3.Distance(worker.transform.position, start), Is.GreaterThan(2f), "It walked out.");

            Assert.That(world.AdjustCashDurably(DevWorld.CompanyId, 5000, WorldPath), Is.Null);
            world.AdvanceUncommitted(1);
            yield return Until(() => !worker.Unpaid && worker.Status.Contains("paid again"), "the employee to restart once paid");
            Assert.That(world.Snapshot().Companies.Single(x => x.Id == DevWorld.CompanyId).Cash, Is.EqualTo(5000 - GoodsWorld.WageCentsPerHour));
        }

        [UnityTest]
        public IEnumerator TakingFromTheHandsSquareMovesTheStackToTheInventory()
        {
            var world = _root.ServerWorld;
            var hands = GoodsWorld.InventoryLocationId(DevWorld.EmployeeId);
            var lot = world.Snapshot().Lots.First(x => x.LocationId == DevWorld.StorageId && x.ItemId == DevWorld.DoughItemId);
            var moved = world.TransferDurably(DevWorld.EmployeeId, new TransferIntent
            {
                RequestId = "test-into-hands", LotId = lot.Id, DestinationId = hands, Quantity = 2
            }, WorldPath);
            Assert.That(moved.Accepted, Is.True, moved.Reason);
            var inventory = _interaction.InventoryId;
            int Held(string location) => _root.ClientSite.Lots.Where(x => x.LocationId == location && x.ItemId == DevWorld.DoughItemId).Sum(x => x.Quantity);
            var before = Held(inventory);
            _interaction.OpenEmployeeScreen(Worker(DevWorld.EmployeeId));
            var panel = UnityEngine.Object.FindAnyObjectByType<EmployeeScriptPanel>();
            yield return Until(() => Held(hands) == 2 && panel.Hands.Q("employee-hand-0") != null && panel.Hands.Q("employee-hand-3") != null,
                "the hands row with the dough");

            panel.ClickHand(DevWorld.DoughItemId, false);
            yield return Until(() => Held(hands) == 0 && Held(inventory) == before + 2, "the dough to reach the inventory");
        }

        [UnityTest]
        public IEnumerator TheWageWarningSettingIsSavedForThePlayer()
        {
            Assert.That(_interaction.WageWarningHours, Is.EqualTo(PlayerRegistry.DefaultWageWarningHours));
            _interaction.ToggleInventory();
            yield return Until(() => _hud.ScreenRoot.Q("hud-wage-warning-setting") != null, "the warning setting");
            _hud.ClickWageWarning(2);
            yield return Until(() => _interaction.WageWarningHours == PlayerRegistry.DefaultWageWarningHours + 2, "the saved setting");
            Assert.That(_root.ServerRegistry.WageWarningHoursOf(_interaction.LocalPlayerId), Is.EqualTo(PlayerRegistry.DefaultWageWarningHours + 2));
            yield return Until(() => _hud.ScreenRoot.Q<Label>("hud-wage-warning-hours")?.text == $"{PlayerRegistry.DefaultWageWarningHours + 2} h",
                "the Staff window to show it");
        }
    }
}
