// Drives the logistics screen and the dock screen (decision 0022) through the real DevSite host: the panel watches the remote
// warehouse, ships warehouse dough to its dock with ordinary transfers the seeded truck then loads, and applies a cargo
// filter to the truck; the restaurant dock's screen names the truck that delivers there. Every save and identity path is a
// unique temporary directory.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Logistics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class LogisticsPanelTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // Isolated input state, as in the other interaction tests: no real device reaches the session under test.
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryLogisticsPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host", Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
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

        private int ServerUnits(string locationId) =>
            _root.ServerWorld.Snapshot().Lots.Where(x => x.LocationId == locationId).Sum(x => x.Quantity);

        [UnityTest]
        public IEnumerator PanelShipsRemoteStockAndSetsTheTruckCargo()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<LogisticsPanel>();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null, "host dev-site baseline");
            yield return Until(() => _root.ClientSubscription.Remote(DevWorld.WarehouseSiteId) != null, "warehouse baseline watched by the panel");
            yield return Until(() =>
            {
                if (interaction.Screen == InteractionScreen.None) interaction.ToggleLogistics();
                return interaction.Screen == InteractionScreen.Logistics;
            }, "logistics screen open");
            yield return null;
            Assert.That(panel.Window.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(panel.Docks().Select(x => x.Id), Is.EqualTo(new[] { DevWorld.DockId, DevWorld.WarehouseDockId }), "Local dock first.");
            var status = panel.Window.Q<Label>($"logistics-truck-status-{DevWorld.TruckId}");
            Assert.That(status.text, Is.EqualTo("Loading at Warehouse"));

            // Ship one stack of warehouse dough to the warehouse dock; the waiting truck starts loading it.
            var outgoing = DevWorld.WarehouseDockId + ":in";
            var cargo = DevWorld.TruckId + ":cargo";
            panel.MoveStack(DevWorld.WarehouseSiteId, DevWorld.WarehouseStorageId, DevWorld.DoughItemId, false, outgoing);
            yield return Until(() => !panel.HasPendingRequests, "ship answered");
            Assert.That(panel.LastRejection, Is.Null);
            Assert.That(ServerUnits(outgoing) + ServerUnits(cargo), Is.EqualTo(20), "One max stack of dough left the warehouse storage.");
            yield return Until(() => ServerUnits(cargo) == 20, "the truck loaded it", 8f);
            yield return Until(() => panel.Window.Q<Label>($"logistics-truck-cargo-{DevWorld.TruckId}").text.Contains("20 Dough"), "cargo shown");

            // Cargo: Any -> the first item (dough), applied.
            panel.CycleDraft(DevWorld.TruckId, LogisticsPanel.CargoField, 1);
            panel.ApplyRoute(DevWorld.TruckId);
            yield return Until(() => !panel.HasPendingRequests, "route answered");
            var truck = _root.ServerWorld.Snapshot().Trucks.Single();
            Assert.That((truck.AllowedItemIds.Single(), truck.State), Is.EqualTo((DevWorld.DoughItemId, TruckState.ToDropoff)),
                "Loaded, so the new route sends it on to the restaurant.");

            // The restaurant dock's screen names its truck.
            interaction.CloseScreen();
            interaction.OpenMachine(DevWorld.DockId);
            yield return Until(() => hud.ScreenRoot.Q<Label>("hud-dock-trucks") != null, "dock screen");
            Assert.That(hud.ScreenRoot.Q<Label>("hud-dock-trucks").text, Is.EqualTo("Truck 1: delivers here"));
        }
    }
}
