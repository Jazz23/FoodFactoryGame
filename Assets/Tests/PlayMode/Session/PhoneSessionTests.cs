// The phone (decision 0047) in the real WorldGen scene on a new generated world in a unique temporary directory: it opens and closes
// as a screen that suspends walking and shows the generated map; buying a farm from the map and ordering from it sends a supplier
// truck that delivers to the starting dock at the owner's price; places are renamed from it; and its staff app hires, opens the
// employee's task screen, and Esc there returns to the phone; and its Trucks app is the logistics screen, where trucks are bought. The application's saves are never opened.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class PhoneSessionTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world; any seed with a dairy farm works.
        private const string Seed = "phone";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;
        private EquipmentInteraction _interaction;
        private PhonePanel _phone;

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 20f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryPhone", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Needs the editor to load a scene outside the build list.");
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
            _interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            _phone = UnityEngine.Object.FindAnyObjectByType<PhonePanel>();
            Assert.That(_phone, Is.Not.Null, "WorldGen has the phone.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host", Address = "127.0.0.1", WorldSeed = Seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _root.NetworkManager.TransportManager.Transport.SetPort((ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port);
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _map.Generated, "host baseline and map", 90f);
            yield return Until(() => UnityEngine.Object.FindObjectsByType<PlayerAvatar>().Any(x => x.IsOwner), "local avatar");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
            {
                if (_root != null)
                {
                    if (_root.ClientSubscription?.Bridge != null) _root.ClientSubscription.Bridge.ClockRate = 1f;
                    _root.Shutdown();
                }
                _root = null;
                yield return null;
                if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            finally
            {
                _input.TearDown();
            }
        }

        private IEnumerator OpenPhone()
        {
            _interaction.TogglePhone();
            yield return Until(() => _phone.Window.resolvedStyle.display == DisplayStyle.Flex && _interaction.Screen == InteractionScreen.Phone
                && _phone.Window.worldBound.width > 0f, "the phone laid out");
        }

        [UnityTest]
        public IEnumerator ThePhoneOpensOverTheMapAndSuspendsWalking()
        {
            var place = (InputActionReference)typeof(EquipmentInteraction).GetField("placeAction", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_interaction);
            var move = place.action.actionMap.FindAction("Move", true);
            yield return OpenPhone();
            Assert.That(_phone.MapView.Layout, Is.SameAs(_map.Shown), "The map app draws the generated layout.");
            Assert.That(move.enabled, Is.False, "Walking is suspended while the phone is open.");
            Assert.That(_phone.Window.worldBound.width, Is.GreaterThan(Screen.width * 0.6f), "The phone takes up most of the screen.");
            var tree = _phone.Window.panel.visualTree;
            var hudCash = tree.Q("hud-cash-block");
            TestContext.WriteLine("panel order: " + string.Join(", ", tree.Children().Select(x => x.name + "/" + x.childCount)));
            Assert.That(hudCash, Is.Not.Null, "The HUD's cash card is on the same panel.");
            {
                var hudTop = tree.Children().First(x => x.Contains(hudCash));
                var phoneTop = tree.Children().First(x => x.Contains(_phone.Window));
                Assert.That(tree.IndexOf(phoneTop), Is.GreaterThan(tree.IndexOf(hudTop)), "The phone draws over the HUD's cards.");
            }
            _interaction.TogglePhone();
            yield return Until(() => _interaction.Screen == InteractionScreen.None, "the phone closing");
            Assert.That(move.enabled, Is.True, "Walking comes back.");
        }

        [UnityTest]
        public IEnumerator BuyingAFarmAndOrderingFromItDeliversToTheStartingDock()
        {
            var start = _root.StartOffer;
            var dockId = _root.ClientSite.Equipment.First(x => x.Kind == GoodsWorld.DockKind && x.SiteId == start.SiteId).Id;
            yield return OpenPhone();
            var farm = _map.Offers.Values.Where(x => x.Category == GoodsWorld.FarmCategory && _phone.KindOf(x.LotId) == FarmKind.Dairy)
                .OrderBy(x => Mathf.Abs(x.AccessX - start.AccessX) + Mathf.Abs(x.AccessZ - start.AccessZ)).FirstOrDefault();
            if (farm == null) Assert.Inconclusive("This seed has no dairy farm.");

            _phone.SelectLot(farm.LotId);
            _phone.Buy();
            yield return Until(() => _root.ClientSite.Properties.Any(x => x.LotId == farm.LotId), "the farm purchase", 30f);
            Assert.That(_phone.LastRejection, Is.Null);

            var milk = _phone.FarmGoods(farm.LotId).Single(x => x.ItemId == "milk");
            _phone.ChooseGood(milk.Id);
            _phone.SetPacks(2);
            Assert.That(_phone.DockId, Is.EqualTo(dockId), "The order goes to the starting dock by default.");
            Assert.That(_phone.OrderCents(), Is.EqualTo(milk.PriceCents + GoodsWorld.FarmDeliveryFeeCents), "Owner's price: half the goods, the whole fee.");
            Assert.That(_phone.CanOrder(), Is.True);
            var cash = _root.ClientSite.Companies.Single().Cash;
            _phone.Order();
            yield return Until(() => _root.ClientSite.FarmOrders.Count == 1, "the order", 30f);
            Assert.That(_root.ClientSite.Companies.Single().Cash, Is.EqualTo(cash - milk.PriceCents - GoodsWorld.FarmDeliveryFeeCents));
            var order = _root.ClientSite.FarmOrders.Single();
            Assert.That(_root.ClientSite.Trucks.Single(x => x.Id == order.TruckId).Supplier, Is.True, "A farm truck brings it.");
            _phone.ShowTab(PhonePanel.OrdersTab);
            yield return null;
            Assert.That(_phone.OrderStatus(order.Id), Does.Contain("bringing it to"));

            // TEST-ONLY: the clock runs faster so the truck arrives sooner; the rules are unchanged.
            _root.ClientSubscription.Bridge.ClockRate = 10f;
            var incoming = _root.ClientSite.Equipment.Single(x => x.Id == dockId).OutputLocationId;
            yield return Until(() => _root.ClientSite.FarmOrders.Count == 0, "the delivery", 120f);
            Assert.That(_root.ClientSite.Lots.Where(x => x.LocationId == incoming && x.ItemId == "milk").Sum(x => x.Quantity), Is.EqualTo(2 * milk.Quantity),
                "The milk is in the dock's incoming buffer.");
            Assert.That(_root.ClientSite.Trucks.Any(x => x.Supplier), Is.False, "The farm truck left.");
        }

        [UnityTest]
        public IEnumerator TheTrucksAppShowsTheLogisticsScreenAndBuysATruck()
        {
            var logistics = UnityEngine.Object.FindAnyObjectByType<Logistics.LogisticsPanel>();
            var offer = _root.Offers.FirstOrDefault(x => x != null && x.Truck != null);
            if (offer == null) Assert.Inconclusive("WorldGen sells no truck.");
            yield return OpenPhone();
            _phone.ShowTab(PhonePanel.TrucksTab);
            yield return Until(() => logistics.Embedded && logistics.Window.resolvedStyle.display == DisplayStyle.Flex, "the logistics screen in the phone");
            Assert.That(_phone.Window.Contains(logistics.Window), Is.True, "It is drawn inside the phone.");
            Assert.That(logistics.Window.Q<Button>(className: "logi__buy"), Is.Not.Null, "Trucks can be bought here.");
            var before = _root.ClientSite.Trucks.Count(x => !x.Supplier);
            logistics.BuyTruck(offer.Id);
            yield return Until(() => _root.ClientSite.Trucks.Count(x => !x.Supplier) == before + 1, "the bought truck", 30f);

            _phone.ShowTab(PhonePanel.MapTab);
            yield return null;
            Assert.That(logistics.Embedded, Is.False, "Other apps give the screen back.");
            _phone.ShowTab(PhonePanel.TrucksTab);
            yield return Until(() => logistics.Embedded, "the Trucks app again");
            _interaction.TogglePhone();
            yield return Until(() => _interaction.Screen == InteractionScreen.None && !logistics.Embedded
                && logistics.Window.resolvedStyle.display == DisplayStyle.None, "the phone closed and the screen back home");
        }

        // Running-game captures of every app, for visual review, into docs/verification/phone-20261013. Runs only when the file
        // Temp/phone-captures.flag exists.
        [UnityTest]
        public IEnumerator PhoneCaptures()
        {
            if (!File.Exists(Path.Combine(Application.dataPath, "..", "Temp", "phone-captures.flag")))
                Assert.Ignore("Captures run on request: create Temp/phone-captures.flag.");
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "verification", "phone-20261013"));
            Directory.CreateDirectory(output);
            IEnumerator Shot(string name)
            {
                for (var frame = 0; frame < 10; frame++) yield return null;
                yield return new WaitForEndOfFrame();
                var screen = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(output, name), screen.EncodeToPNG());
                UnityEngine.Object.Destroy(screen);
            }
            var start = _root.StartOffer;
            yield return new WaitForSeconds(2f);
            yield return OpenPhone();
            yield return Shot("map.png");
            var farm = _map.Offers.Values.Where(x => x.Category == GoodsWorld.FarmCategory)
                .OrderBy(x => Mathf.Abs(x.AccessX - start.AccessX) + Mathf.Abs(x.AccessZ - start.AccessZ)).First();
            _phone.ShowOnMap(farm.LotId);
            _phone.ChooseGood(_phone.FarmGoods(farm.LotId).First().Id);
            yield return Shot("farm-order.png");
            _phone.Order();
            yield return Until(() => _root.ClientSite.FarmOrders.Count == 1, "the order", 30f);
            _phone.ShowTab(PhonePanel.OrdersTab);
            yield return Shot("orders.png");
            _phone.ShowTab(PhonePanel.TrucksTab);
            yield return Shot("trucks.png");
            _phone.Hire();
            _phone.ShowTab(PhonePanel.StaffTab);
            yield return Until(() => _root.ClientSite.Employees.Count == 1, "the hire");
            yield return Shot("staff.png");
            _phone.ShowTab(PhonePanel.PlacesTab);
            yield return Shot("places.png");
            _phone.ShowOnMap(_map.Offers.Values.First(x => x.SiteId == start.SiteId).LotId);
            yield return Shot("own-restaurant.png");
        }

        [UnityTest]
        public IEnumerator PlacesAreRenamedAndStaffAreManagedFromThePhone()
        {
            var start = _root.StartOffer;
            yield return OpenPhone();
            _phone.ShowTab(PhonePanel.PlacesTab);
            yield return null;
            Assert.That(_phone.Window.Q<TextField>("phone-name-" + start.SiteId), Is.Not.Null, "The starting restaurant is listed.");
            _phone.Rename(start.SiteId, "Corner Bistro");
            yield return Until(() => _root.ClientSite.Sites.Single(x => x.Id == start.SiteId).Name == "Corner Bistro", "the rename");

            _phone.ShowTab(PhonePanel.StaffTab);
            _phone.Hire();
            yield return Until(() => _root.ClientSite.Employees.Count == 1, "the hire");
            var id = _root.ClientSite.Employees.Single().Id;
            yield return Until(() => UnityEngine.Object.FindObjectsByType<EmployeeWorker>().Any(x => x.EmployeeId == id), "the worker");
            _phone.OpenTasks(id);
            Assert.That((_interaction.Screen, _interaction.OpenEmployee.EmployeeId, _interaction.EmployeeFromPhone),
                Is.EqualTo((InteractionScreen.Employee, id, true)));
            typeof(EquipmentInteraction).GetMethod("OnCloseScreen", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_interaction, new object[] { default(InputAction.CallbackContext) });
            Assert.That(_interaction.Screen, Is.EqualTo(InteractionScreen.Phone), "Esc on the task screen returns to the phone.");
        }
    }
}
