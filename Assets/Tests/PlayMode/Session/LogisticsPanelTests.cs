// Drives the logistics screen and the dock screen (decisions 0022, 0023) through the real DevSite host: the panel watches the remote
// warehouse, ships warehouse dough to its dock with ordinary transfers the seeded truck then loads, and applies a cargo
// filter to the seeded route, buys a truck, creates a route, assigns and parks the new truck and deletes the route; the
// restaurant dock's screen names the truck that delivers there. Every save and identity path is a
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
using UnityEngine.InputSystem.LowLevel;
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
        public IEnumerator PanelShipsStockEditsRoutesAndManagesTheFleet()
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

            // Cargo: Any -> the first item (dough), applied to the seeded route.
            panel.CycleDraft(DevWorld.RouteId, LogisticsPanel.CargoField, 1);
            panel.ApplyRoute(DevWorld.RouteId);
            yield return Until(() => !panel.HasPendingRequests, "route answered");
            Assert.That(panel.LastRejection, Is.Null);
            var truck = _root.ServerWorld.Snapshot().Trucks.Single();
            Assert.That((_root.ServerWorld.Snapshot().Routes.Single().AllowedItemIds.Single(), truck.State),
                Is.EqualTo((DevWorld.DoughItemId, TruckState.ToDropoff)), "Loaded, so the edited route sends it on to the restaurant.");

            // Buy a truck: it arrives parked at the restaurant.
            var offer = _root.Offers.Single(x => x != null && x.Truck != null);
            panel.BuyTruck(offer.Id);
            yield return Until(() => !panel.HasPendingRequests, "truck purchase answered");
            Assert.That(panel.LastRejection, Is.Null);
            var bought = _root.ServerWorld.Snapshot().Trucks.Single(x => x.Id != DevWorld.TruckId);
            Assert.That((bought.Name, bought.State, bought.SiteId), Is.EqualTo(("Truck 2", TruckState.Parked, DevWorld.SiteId)));
            yield return Until(() => panel.Window.Q<Label>($"logistics-truck-status-{bought.Id}")?.text == "Parked at Restaurant: no route",
                "bought truck shown");

            // A new route from the restaurant dock (first option) to the warehouse dock (second).
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.PickupField, 1);
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.DropoffField, 1);
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.DropoffField, 1);
            panel.CreateRoute();
            yield return Until(() => !panel.HasPendingRequests, "route created");
            Assert.That(panel.LastRejection, Is.Null);
            var created = _root.ServerWorld.Snapshot().Routes.Single(x => x.Id != DevWorld.RouteId);
            Assert.That((created.PickupDockId, created.DropoffDockId), Is.EqualTo((DevWorld.DockId, DevWorld.WarehouseDockId)));
            yield return Until(() => _root.ClientSite.Routes.Count == 2 && panel.Window.Q($"logistics-route-{created.Id}") != null, "route shown");

            // Assign the bought truck to it (options: Parked, the seeded route, the new one), then park it again.
            panel.CycleTruckRoute(bought.Id, 1);
            panel.CycleTruckRoute(bought.Id, 1);
            panel.AssignTruck(bought.Id);
            yield return Until(() => !panel.HasPendingRequests, "truck assigned");
            Assert.That(panel.LastRejection, Is.Null);
            bought = _root.ServerWorld.Snapshot().Trucks.Single(x => x.Id == bought.Id);
            Assert.That((bought.RouteId, bought.State), Is.EqualTo((created.Id, TruckState.Loading)), "Empty and already at the pickup.");
            yield return Until(() => _root.ClientSite.Trucks.Single(x => x.Id == bought.Id).RouteId == created.Id, "assignment shown");
            panel.CycleTruckRoute(bought.Id, -1);
            panel.CycleTruckRoute(bought.Id, -1);
            panel.AssignTruck(bought.Id);
            yield return Until(() => !panel.HasPendingRequests, "truck parked");
            Assert.That(_root.ServerWorld.Snapshot().Trucks.Single(x => x.Id == bought.Id).State, Is.EqualTo(TruckState.Parked));

            // Deleting the new route leaves the seeded one.
            panel.DeleteRoute(created.Id);
            yield return Until(() => !panel.HasPendingRequests, "route deleted");
            Assert.That(_root.ServerWorld.Snapshot().Routes.Single().Id, Is.EqualTo(DevWorld.RouteId));

            // The restaurant dock's screen names its truck.
            interaction.CloseScreen();
            interaction.OpenMachine(DevWorld.DockId);
            yield return Until(() => hud.ScreenRoot.Q<Label>("hud-dock-trucks") != null, "dock screen");
            Assert.That(hud.ScreenRoot.Q<Label>("hud-dock-trucks").text, Is.EqualTo("Truck 1: delivers here"));
        }

        // P0-01: after the window grows (a bought truck, a new route), a truck card's route arrows and Assign take pointer picks
        // where they are drawn and stay inside the card, also when the window is squeezed narrow; the virtual mouse then picks a
        // route and assigns it.
        [UnityTest]
        public IEnumerator TruckCardControlsTakeThePointerAfterTheWindowGrows()
        {
            var panel = UnityEngine.Object.FindAnyObjectByType<LogisticsPanel>();
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var mouse = InputSystem.AddDevice<Mouse>();
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null, "host dev-site baseline");
            yield return Until(() =>
            {
                if (interaction.Screen == InteractionScreen.None) interaction.ToggleLogistics();
                return interaction.Screen == InteractionScreen.Logistics;
            }, "logistics screen open");
            yield return null;
            panel.BuyTruck(_root.Offers.Single(x => x != null && x.Truck != null).Id);
            yield return Until(() => !panel.HasPendingRequests && _root.ClientSite.Trucks.Count == 2, "truck bought");
            var truckId = _root.ClientSite.Trucks.Single(x => x.Id != DevWorld.TruckId).Id;
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.PickupField, 1);
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.DropoffField, 1);
            panel.CycleDraft(LogisticsPanel.NewRouteKey, LogisticsPanel.DropoffField, 1);
            panel.CreateRoute();
            yield return Until(() => !panel.HasPendingRequests && _root.ClientSite.Routes.Count == 2, "route created");
            var routeId = _root.ClientSite.Routes.Single(x => x.Id != DevWorld.RouteId).Id;
            yield return Until(() => panel.Window.Q($"logistics-route-{routeId}") != null, "route card");
            for (var frame = 0; frame < 3; frame++) yield return null;

            VisualElement Card() => panel.Window.Q($"logistics-truck-{truckId}");
            Button[] Controls() => Card().Query<Button>().ToList().ToArray();
            void AssertReachable(string when)
            {
                var card = Card();
                Assert.That(Controls().Length, Is.EqualTo(3), "<, > and Assign.");
                foreach (var button in Controls())
                {
                    var bounds = button.worldBound;
                    var picked = card.panel.Pick(bounds.center);
                    Assert.That(picked == button || button.Contains(picked), Is.True,
                        $"{when}: '{button.text}' at {bounds} picks '{picked?.name}' {picked?.worldBound}");
                    Assert.That(bounds.xMin >= card.worldBound.xMin - 0.5f && bounds.xMax <= card.worldBound.xMax + 0.5f, Is.True,
                        $"{when}: '{button.text}' {bounds} outside its card {card.worldBound}");
                }
            }
            AssertReachable("full width");

            // A narrow screen: the overlay that centres the window is narrowed, so the window and its columns shrink.
            var overlay = panel.Window.parent;
            overlay.style.right = StyleKeyword.Auto;
            overlay.style.width = 820;
            for (var frame = 0; frame < 3; frame++) yield return null;
            Assert.That(panel.Window.worldBound.width, Is.LessThanOrEqualTo(820.5f), "The window stays on the narrow screen.");
            AssertReachable("narrow");
            overlay.style.width = StyleKeyword.Null;
            overlay.style.right = 0;
            for (var frame = 0; frame < 3; frame++) yield return null;

            IEnumerator MouseClick(Button button)
            {
                var origin = RuntimePanelUtils.ScreenToPanel(button.panel, Vector2.zero);
                var unit = RuntimePanelUtils.ScreenToPanel(button.panel, Vector2.one) - origin;
                var centre = button.worldBound.center;
                var screen = new Vector2((centre.x - origin.x) / unit.x, UnityEngine.Screen.height - (centre.y - origin.y) / unit.y);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                for (var frame = 0; frame < 2; frame++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen }.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                for (var frame = 0; frame < 3; frame++) yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                for (var frame = 0; frame < 3; frame++) yield return null;
            }

            // The virtual mouse must reach the panel whether or not the Editor has focus.
            var unfocused = new UnfocusedUiInput();
            try
            {
                // Options: Parked, then the routes by ID; > reaches the new one.
                var presses = 1 + _root.ClientSite.Routes.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal).ToList().IndexOf(routeId);
                var value = panel.Window.Q<Label>($"logistics-route-value-{truckId}");
                for (var press = 0; press < presses; press++)
                {
                    var before = value.text;
                    var target = Controls()[1];
                    var seen = new System.Collections.Generic.List<string>();
                    target.RegisterCallback<PointerDownEvent>(e => seen.Add("down " + e.position), TrickleDown.TrickleDown);
                    target.RegisterCallback<PointerMoveEvent>(e => seen.Add("move " + e.position), TrickleDown.TrickleDown);
                    panel.Window.RegisterCallback<PointerDownEvent>(e => seen.Add("window down " + e.position + " target " + (e.target as VisualElement)?.name), TrickleDown.TrickleDown);
                    yield return MouseClick(target);
                    value = panel.Window.Q<Label>($"logistics-route-value-{truckId}");
                    Assert.That(value.text, Is.Not.EqualTo(before), $"> press {press + 1} changed the route choice. DIAG focused={Application.isFocused} current={Mouse.current == mouse} pos={mouse.position.ReadValue()} target={target.worldBound} screen={UnityEngine.Screen.width}x{UnityEngine.Screen.height} bg={InputSystem.settings.backgroundBehavior} editor={InputSystem.settings.editorInputBehaviorInPlayMode} events=[{string.Join("; ", seen)}]");
                }
                AssertReachable("after choosing");
                var assign = panel.Window.Q<Button>($"logistics-assign-{truckId}");
                Assert.That((assign.text, assign.enabledSelf), Is.EqualTo(("Assign", true)));
                yield return MouseClick(assign);
                yield return Until(() => !panel.HasPendingRequests, "assignment answered");
                Assert.That(panel.LastRejection, Is.Null);
                Assert.That(_root.ServerWorld.Snapshot().Trucks.Single(x => x.Id == truckId).RouteId, Is.EqualTo(routeId),
                    "The pointer assigned the truck to the new route.");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                unfocused.Dispose();
                interaction.CloseScreen();
            }
        }
    }
}
