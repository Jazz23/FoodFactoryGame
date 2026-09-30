// Runs the real WorldGen scene (decision 0028, piece 2) as a host, with a loopback-UDP remote client where needed, on a new
// generated world in a unique temporary directory: the player starts on the starting restaurant's own site, the map's starting
// lot lines up with the site grid, a dock (small enough for a restaurant's 2 m apron) places there, a purchase through the
// buy panel reaches both clients, and a whole sale in the pre-equipped starting restaurant (decision 0030) reaches both.
// The application's saves are never opened.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Buildings;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class WorldGenSessionTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world; any seed works.
        private const string Seed = "piece-two";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ClientSiteSubscription _remoteSite;
        private ushort _port;

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
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryWorldGenPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            // WorldGen is not a build scene, so it is loaded from the asset database.
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
            Assert.That((_root != null, _map != null, _root != null && _root.GeneratesWorld), Is.EqualTo((true, true, true)), "WorldGen generates worlds.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Host",
                Address = "127.0.0.1",
                WorldSeed = Seed
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _root.NetworkManager.TransportManager.Transport.SetPort(_port);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            try
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
            finally
            {
                _input.TearDown();
            }
        }

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null && _map.Generated, "host baseline and map", 90f);
        }

        private PropertyOffer Start => _root.StartOffer;

        private static PlayerAvatar LocalAvatar() =>
            UnityEngine.Object.FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x.IsOwner);

        [UnityTest]
        public IEnumerator ANewWorldStartsThePlayerOnItsOwnRestaurant()
        {
            yield return StartHost();
            Assert.That(Start, Is.Not.Null, "A format 3 layout makes a generated world.");
            Assert.That(_root.ClientSiteId, Is.EqualTo(Start.SiteId), "The join answer names the starting site.");
            var site = _root.ClientSite;
            Assert.That(site.Companies.Single().Cash, Is.EqualTo(GeneratedWorld.StartingCash));
            Assert.That(site.Properties.Single().LotId, Is.EqualTo(Start.LotId));
            Assert.That(site.Locations.Single(x => x.Id == GoodsWorld.InventoryLocationId(_root.Authenticator.LocalPlayerId)).SiteId, Is.EqualTo(Start.SiteId));
            Assert.That(site.Equipment.Select(x => x.Id), Is.EquivalentTo(new[] { GeneratedWorld.StartCounterId, GeneratedWorld.StartTableId }),
                "Pre-equipped with a counter and a table (decision 0030).");
            Assert.That(site.Belts.Count + site.Employees.Count, Is.Zero, "No dev seed.");
            Assert.That((GameObject.Find("Navigation"), GameObject.Find("Landmark 1")), Is.EqualTo(((GameObject)null, (GameObject)null)),
                "The dev site's ground and NavMesh are hidden in a generated world.");

            yield return Until(() => LocalAvatar() != null, "local avatar");
            yield return new WaitForSeconds(1f);
            var avatar = LocalAvatar().transform.position;
            var (x, z) = SiteGridSpace.AnchorAt(_root.ClientSite.SiteLayouts.Single(), avatar, 1, 1);
            Assert.That(x >= 0 && x < Start.Width && z >= 0 && z < Start.Depth, Is.True, $"The avatar stands on the lot (cell {x}, {z}).");
            Assert.That(SiteGrid.Overlaps(x, z, 1, 1, Start.BuildingX, Start.BuildingZ, Start.BuildingWidth, Start.BuildingDepth), Is.False,
                "It arrives on the apron, outside the building.");
            Assert.That(Mathf.Abs(avatar.y), Is.LessThan(0.5f), "It stands on the levelled ground at floor height.");
        }

        [UnityTest]
        public IEnumerator TheMapsStartingLotLinesUpWithTheSiteGrid()
        {
            yield return StartHost();
            var buildings = UnityEngine.Object.FindAnyObjectByType<BuildingPresenter>();
            yield return Until(() => buildings.ShellOf(Start.BuildingId) != null, "starting shell drawn from site data");
            var layout = _map.Shown;
            var building = layout.Buildings.Single(b => b.Id == Start.BuildingId);
            var lot = layout.Lots.Single(l => l.BuildingId == Start.BuildingId);
            var centre = _map.ScenePoint(lot.X + lot.Width / 2f, lot.Z + lot.Depth / 2f, building.ElevationCm / 100f);
            Assert.That(centre.magnitude, Is.LessThan(0.01f), "The lot's centre at floor height is the scene origin.");

            var renderers = buildings.ShellOf(Start.BuildingId).GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var min = _map.ScenePoint(building.X, building.Z);
            var max = _map.ScenePoint(building.X + building.Width, building.Z + building.Depth);
            Assert.That(new[] { bounds.min.x - min.x, bounds.min.z - min.z, bounds.max.x - max.x, bounds.max.z - max.z }.Select(Mathf.Abs),
                Has.All.LessThanOrEqualTo(SiteGrid.CellSize), $"Shell bounds {bounds.min}-{bounds.max} against the map's {min}-{max}.");
            Assert.That(UnityEngine.Object.FindObjectsByType<PropertyMarker>().Any(m => m.BuildingId == Start.BuildingId), Is.False,
                "The map leaves out the starting building's own model.");
            Assert.That(UnityEngine.Object.FindObjectsByType<PropertyMarker>().Length, Is.GreaterThan(10), "Other property is marked.");
        }

        [UnityTest]
        public IEnumerator ADockPlacesOnTheApron()
        {
            yield return StartHost();
            var results = new System.Collections.Generic.Dictionary<string, GoodsOutcome>();
            _root.ClientSubscription.ResultReceived += x => results[x.RequestId] = x;
            var me = _root.Authenticator.LocalPlayerId;
            var bridge = _root.ClientSubscription.Bridge;
            bridge.RequestPurchase("buy-dock", Start.SiteId, "supplier-dock");
            yield return Until(() => results.ContainsKey("buy-dock"), "dock purchase");
            Assert.That(results["buy-dock"].Accepted, Is.True, results["buy-dock"].Reason);
            yield return Until(() => _root.ClientSite.Equipment.Any(x => x.HolderId == me), "held dock");
            var site = _root.ClientSite;
            var dock = site.Equipment.Single(x => x.HolderId == me);
            var (width, depth) = SiteGrid.Footprint(dock.Width, dock.Depth, 0);
            var anchor = Enumerable.Range(0, Start.Width * Start.Depth).Select(i => (X: i % Start.Width, Z: i / Start.Width))
                .First(c => !SiteGrid.Overlaps(c.X, c.Z, width, depth, Start.BuildingX, Start.BuildingZ, Start.BuildingWidth, Start.BuildingDepth)
                            && SiteGrid.PlacementProblem(site, dock, c.X, c.Z, 0, 0) == null);
            bridge.RequestPlace("place-dock", dock.Id, anchor.X, anchor.Z, 0);
            yield return Until(() => results.ContainsKey("place-dock"), "placement reply");
            Assert.That(results["place-dock"].Accepted, Is.True, results["place-dock"].Reason);
            var presenter = UnityEngine.Object.FindAnyObjectByType<EquipmentPresenter>();
            yield return Until(() => presenter.Visuals.ContainsKey(dock.Id), "dock visual on the apron");
            var placed = _root.ClientSite.Equipment.Single(x => x.Id == dock.Id);
            Assert.That(Vector3.Distance(presenter.Visuals[dock.Id].transform.position, SiteGridSpace.Center(_root.ClientSite.SiteLayouts.Single(), placed)),
                Is.LessThan(0.01f));
            Assert.That(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot().Equipment.Single(x => x.Id == dock.Id).State, Is.EqualTo(EquipmentState.Placed));
        }

        // Listen-server multiplayer: the host buys a restaurant through the buy panel; the remote teammate's baseline shows the
        // new owner and it gains the new site, and the host's map re-tints the awning and its client watches the new site.
        [UnityTest]
        public IEnumerator ABuildingBoughtThroughThePanelReachesBothClients()
        {
            yield return StartHost();
            CreateRemote();
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null; }, "remote baseline of the starting site");
            Assert.That(_remoteSite.SiteId, Is.EqualTo(Start.SiteId));
            var remoteId = _remoteAuth.LocalPlayerId;
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var panel = UnityEngine.Object.FindAnyObjectByType<PropertyPanel>();
            var forSale = _map.Offers.Values.Where(x => x.ForSale).ToList();
            var diner = forSale.Where(x => x.Category == GoodsWorld.RestaurantKind).OrderBy(x => x.PriceCents).First();
            Assert.That(_map.AwningColour(diner.BuildingId), Is.EqualTo((Color?)new Color(0.95f, 0.75f, 0.15f)), "For sale before.");
            // TEST-ONLY: the company is left one cent short of the diner on this isolated save, so the first attempt is refused.
            var shortfall = GeneratedWorld.StartingCash - diner.PriceCents + 1;
            Assert.That(_root.ServerWorld.AdjustCashDurably(GeneratedWorld.CompanyId, -shortfall, _root.Options.WorldPath), Is.Null);

            yield return Until(() => { interaction.OpenProperty(diner.LotId); return interaction.Screen == InteractionScreen.Property; }, "buy panel");
            yield return Until(() => panel.CanBuy, "Buy enabled");
            Assert.That(panel.StateText, Is.EqualTo("For sale"));
            panel.Buy();
            yield return Until(() => !panel.HasPendingRequests, "rejection");
            Assert.That(panel.LastRejection, Is.EqualTo("insufficient-funds"));
            interaction.CloseScreen();

            Assert.That(_root.ServerWorld.AdjustCashDurably(GeneratedWorld.CompanyId, 1, _root.Options.WorldPath), Is.Null);
            yield return Until(() => { if (interaction.Screen == InteractionScreen.None) interaction.OpenProperty(diner.LotId); return interaction.OpenLotId == diner.LotId; }, "buy panel");
            yield return Until(() => panel.CanBuy, "Buy enabled");
            panel.Buy();
            yield return Until(() => !panel.HasPendingRequests, "purchase");
            Assert.That(panel.LastRejection, Is.Null);
            yield return Until(() => panel.StateText == "Owned by your company", "panel shows the new owner");
            interaction.CloseScreen();

            var saved = GoodsSnapshotStore.Load(_root.Options.WorldPath);
            Assert.That(saved.Snapshot().Companies.Single().Cash, Is.Zero);
            Assert.That(saved.CanView(remoteId, diner.SiteId), Is.True, "The teammate gains the new site.");
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest.Properties.Any(x => x.LotId == diner.LotId); }, "remote sees the owner");
            Assert.That(_remoteSite.Latest.Properties.Single(x => x.LotId == diner.LotId).CompanyId, Is.EqualTo(_remoteSite.Latest.Companies.Single().Id));
            _remoteSite.Watch(diner.SiteId);
            yield return Until(() => _remoteSite.Remote(diner.SiteId) != null, "remote baseline of the bought site");
            yield return Until(() => _root.ClientSubscription.Remote(diner.SiteId) != null, "buyer watches the bought site");
            yield return Until(() => _map.AwningColour(diner.BuildingId) == new Color(0.18f, 0.62f, 0.26f), "awning turns the owner's green");
        }

        // A whole sale in the pre-equipped starting restaurant (decision 0030), with a listen-server teammate: the host buys an
        // oven and dough from the supplier, places the oven inside the shell, bakes bread and puts it on the starting counter; a
        // customer buys it, the company's cash goes up on both clients (the teammate also sees the customers), and the committed
        // save matches. The local NavMesh leads from the street through a door to the counter, and customer figures are drawn.
        [UnityTest]
        public IEnumerator AWholeSaleInTheStartingRestaurantReachesBothClients()
        {
            yield return StartHost();
            CreateRemote();
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null; }, "remote baseline of the starting site");
            var results = new System.Collections.Generic.Dictionary<string, GoodsOutcome>();
            _root.ClientSubscription.ResultReceived += x => results[x.RequestId] = x;
            var me = _root.Authenticator.LocalPlayerId;
            var bridge = _root.ClientSubscription.Bridge;
            var inventory = GoodsWorld.InventoryLocationId(me);
            var startCash = _root.ClientSite.Companies.Single().Cash;

            bridge.RequestPurchase("buy-oven", Start.SiteId, "supplier-oven");
            bridge.RequestPurchase("buy-dough", Start.SiteId, "supplier-dough-5");
            yield return Until(() => results.ContainsKey("buy-oven") && results.ContainsKey("buy-dough"), "supplier purchases");
            Assert.That((results["buy-oven"].Accepted, results["buy-dough"].Accepted), Is.EqualTo((true, true)),
                results["buy-oven"].Reason + " / " + results["buy-dough"].Reason);
            yield return Until(() => _root.ClientSite.Equipment.Any(x => x.HolderId == me && x.Kind == "oven"), "held oven");
            var site = _root.ClientSite;
            var oven = site.Equipment.Single(x => x.HolderId == me && x.Kind == "oven");
            var shell = site.Buildings.Single();
            var anchor = Enumerable.Range(0, Start.Width * Start.Depth).Select(i => (X: i % Start.Width, Z: i / Start.Width))
                .Where(c => SiteGrid.InsideInterior(shell, c.X, c.Z, oven.Width, oven.Depth) && SiteGrid.PlacementProblem(site, oven, c.X, c.Z, 0, 0) == null)
                // As far from the doors as possible, so the oven never stands in the customers' way in.
                .OrderByDescending(c => shell.Doors.Min(d => Mathf.Abs(d.X - (c.X + 1)) + Mathf.Abs(d.Z - (c.Z + 1)))).First();
            bridge.RequestPlace("place-oven", oven.Id, anchor.X, anchor.Z, 0);
            yield return Until(() => results.ContainsKey("place-oven"), "oven placement");
            Assert.That(results["place-oven"].Accepted, Is.True, results["place-oven"].Reason);

            yield return Until(() => _root.ClientSite.Lots.Any(x => x.LocationId == inventory && x.ItemId == DevWorld.DoughItemId), "dough in hand");
            var dough = _root.ClientSite.Lots.First(x => x.LocationId == inventory && x.ItemId == DevWorld.DoughItemId);
            bridge.RequestTransfer("load-oven", dough.Id, oven.Id + ":in", 2);
            yield return Until(() => results.ContainsKey("load-oven"), "dough into the oven");
            Assert.That(results["load-oven"].Accepted, Is.True, results["load-oven"].Reason);
            yield return Until(() => _root.ClientSite.Lots.Any(x => x.LocationId == oven.Id + ":out" && x.ItemId == "bread"), "baked bread", 40f);
            var bread = _root.ClientSite.Lots.First(x => x.LocationId == oven.Id + ":out" && x.ItemId == "bread");
            bridge.RequestTransfer("stock-counter", bread.Id, GeneratedWorld.StartCounterId + ":in", bread.Quantity);
            yield return Until(() => results.ContainsKey("stock-counter"), "bread onto the counter");
            Assert.That(results["stock-counter"].Accepted, Is.True, results["stock-counter"].Reason);

            // TEST-ONLY district standing on the starting site's map point with a 5 m range, so only this restaurant is in its
            // range and a customer comes within seconds (the map's own districts share each customer among ~100 restaurants).
            var map = _root.ServerWorld.Snapshot().Sites.Single(x => x.Id == Start.SiteId);
            _root.ServerWorld.Bootstrap(new GoodsDistrict
            {
                Id = "test-district", Name = "Test", MapX = map.MapX, MapZ = map.MapZ, CustomersPerHour = 720, WealthPercent = 40,
                AppearanceVariants = 1, LikedCuisines = { "bakery" }, DineInPercent = 50, RangeMetres = 5
            });
            yield return Until(() => _root.ClientSite.Companies.Single().Cash > startCash - SupplierSpend, "a customer buys bread", 60f);
            var cash = _root.ClientSite.Companies.Single().Cash;
            Assert.That(cash, Is.EqualTo(startCash - SupplierSpend + 250 * (_root.ClientSite.Diners.Single(x => x.RestaurantId == Start.SiteId).Served)));

            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest.Companies.Single().Cash >= cash; }, "teammate sees the cash");
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest.Customers.Any(x => x.RestaurantId == Start.SiteId); },
                "teammate sees the customers");
            yield return Until(() =>
            {
                var saved = GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot();
                var live = _root.ServerWorld.Snapshot();
                return saved.Companies.Single().Cash > startCash - SupplierSpend && saved.Companies.Single().Cash ==
                    startCash - SupplierSpend + 250 * saved.Diners.Single(x => x.RestaurantId == Start.SiteId).Served
                    && live.Revision >= saved.Revision;
            }, "the committed save matches its sales", 30f);

            var navigation = UnityEngine.Object.FindAnyObjectByType<Customers.SiteNavigation>();
            Assert.That(navigation.BuiltFor, Does.StartWith(Start.SiteId), "The generated lot has a runtime NavMesh.");
            var layout = _root.ClientSite.SiteLayouts.Single();
            var counter = _root.ClientSite.Equipment.Single(x => x.Id == GeneratedWorld.StartCounterId);
            var street = Customers.SiteStreet.Points(layout, Customers.SiteStreet.Outward(layout, _root.ClientSite.Buildings).Value);
            var goal = SiteGridSpace.Center(layout, counter) + new Vector3(0, 0, -0.9f);
            var path = new UnityEngine.AI.NavMeshPath();
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(street[0], out var from, 1f, UnityEngine.AI.NavMesh.AllAreas), Is.True);
            Assert.That(UnityEngine.AI.NavMesh.SamplePosition(goal, out var to, 1f, UnityEngine.AI.NavMesh.AllAreas), Is.True);
            Assert.That(UnityEngine.AI.NavMesh.CalculatePath(from.position, to.position, UnityEngine.AI.NavMesh.AllAreas, path) && path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete,
                Is.True, "The street connects to the counter.");
            var doors = shell.Doors.Select(d => SiteGridSpace.FootprintCenter(layout, d.X, d.Z, 1, 1)).ToList();
            Assert.That(UnityEngine.AI.NavMesh.Raycast(from.position, to.position, out _, UnityEngine.AI.NavMesh.AllAreas), Is.True,
                "A straight line from the street to the counter is blocked by a wall.");
            Assert.That(PathPassesNear(path, doors, 1.2f), Is.True, "The path goes through a door.");
            var figures = UnityEngine.Object.FindAnyObjectByType<Customers.CustomerPresenter>();
            yield return Until(() => figures.VisibleCount > 0, "customer figures drawn", 20f);
        }

        // TEST-ONLY expectation from the supplier content: one oven ($150.00) and one pack of dough ($2.50); bread sells for $2.50.
        private const long SupplierSpend = 15000 + 250;

        private static bool PathPassesNear(UnityEngine.AI.NavMeshPath path, System.Collections.Generic.List<Vector3> points, float distance)
        {
            for (var index = 1; index < path.corners.Length; index++)
                for (var step = 0; step <= 20; step++)
                {
                    var p = Vector3.Lerp(path.corners[index - 1], path.corners[index], step / 20f);
                    if (points.Any(x => new Vector2(x.x - p.x, x.z - p.z).magnitude <= distance)) return true;
                }
            return false;
        }

        private void CreateRemote()
        {
            var go = new GameObject("worldgen-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on worldgen-test-remote\\."));
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
            _remoteAuth.SetClientCredentials("Remote", ClientIdentity.LoadOrCreate(Path.Combine(_directory, "remote.db")));
            // Like SessionRoot: the join answer names the site to subscribe to.
            _remoteSite = new ClientSiteSubscription(_remote);
            _remoteAuth.ClientJoinAnswered += answer => { if (answer.Accepted) _remoteSite.SetPrimary(answer.SiteId); };
        }
    }
}
