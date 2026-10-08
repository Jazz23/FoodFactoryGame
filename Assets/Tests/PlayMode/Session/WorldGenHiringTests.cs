// Employees in the real WorldGen scene (decision 0039) on a new generated world in a unique temporary directory: Hire in the
// Staff window spawns a worker on the starting lot's runtime NavMesh, and its script walks it from the apron through the door to a
// free cell inside the restaurant; and a bought lot too far away to be drawn still gets its NavMesh once it has an employee, so
// how far the player stands never decides whether an employee can walk. The application's saves are never opened.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    public sealed class WorldGenHiringTests
    {
        private const string ScenePath = "Assets/Scenes/WorldGen.unity";
        // TEST-ONLY seed of the isolated world; any seed works.
        private const string Seed = "hiring";
        private readonly InputTestFixture _input = new InputTestFixture();
        private string _directory;
        private SessionRoot _root;
        private WorldLayoutPresenter _map;

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
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryWorldGenHiring", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            Assert.Ignore("Needs the editor to load a scene outside the build list.");
#endif
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _map = UnityEngine.Object.FindAnyObjectByType<WorldLayoutPresenter>();
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
        public IEnumerator AHireWalksFromTheApronIntoTheRestaurant()
        {
            var start = _root.StartOffer;
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            interaction.ToggleInventory();
            yield return Until(() => hud.ScreenRoot.Q("hud-staff-window") != null, "the Staff window");
            hud.ClickHire();
            yield return Until(() => _root.ClientSite.Employees.Count == 1, "the hire");
            var id = _root.ClientSite.Employees.Single().Id;
            EmployeeWorker worker = null;
            yield return Until(() => (worker = UnityEngine.Object.FindObjectsByType<EmployeeWorker>().FirstOrDefault(x => x.EmployeeId == id)) != null,
                "the hire's worker");
            var agent = worker.GetComponent<UnityEngine.AI.NavMeshAgent>();
            yield return Until(() => agent.isOnNavMesh, "the worker on the lot's NavMesh");
            interaction.CloseScreen();

            // A free interior cell at least two cells from the walls and clear of the pre-placed equipment.
            var site = _root.ClientSite;
            var layout = site.SiteLayouts.Single(x => x.SiteId == start.SiteId);
            var shell = site.Buildings.Single(x => x.SiteId == start.SiteId);
            var (cellX, cellZ) = SiteGrid.InteriorCells(shell)
                .Where(c => c.X >= shell.CellX + 2 && c.X < shell.CellX + shell.Width - 2 && c.Z >= shell.CellZ + 2 && c.Z < shell.CellZ + shell.Depth - 2)
                .Where(c => !site.Equipment.Any(e =>
                {
                    var (w, d) = SiteGrid.Footprint(e.Width, e.Depth, e.Rotation);
                    return SiteGrid.Overlaps(c.X - 1, c.Z - 1, 3, 3, e.CellX, e.CellZ, w, d);
                }))
                .First();
            worker.RequestRun($"local ok, why = move_to({{{cellX}, {cellZ}}})\nif not ok then error(why) end");
            yield return Until(() => worker.Status.StartsWith("Finished") || worker.Status.StartsWith("Error"), "the walk", 60f);
            Assert.That(worker.Status, Does.StartWith("Finished"));
            var target = SiteGridSpace.FootprintCenter(layout, cellX, cellZ, 1, 1);
            var at = worker.transform.position;
            Assert.That(new Vector2(at.x - target.x, at.z - target.z).magnitude, Is.LessThan(0.6f), $"It stands on cell ({cellX}, {cellZ}).");
        }

        [UnityTest]
        public IEnumerator AFarLotWithAnEmployeeGetsItsNavMeshWithoutBeingDrawn()
        {
            var start = _root.StartOffer;
            var world = _root.ServerWorld;
            // TEST-ONLY: enough cash on this isolated save for any lot.
            Assert.That(world.AdjustCashDurably(GeneratedWorld.CompanyId, 100_000_000, _root.Options.WorldPath), Is.Null);
            var far = _map.Offers.Values.Where(x => x.ForSale && x.Category == GoodsWorld.RestaurantKind)
                .OrderByDescending(x => Mathf.Abs(x.LotX - start.LotX) + Mathf.Abs(x.LotZ - start.LotZ)).First();
            var distance = new Vector2(far.LotX - start.LotX, far.LotZ - start.LotZ).magnitude;
            if (distance < DrawnSites.DrawRadius + 60f) Assert.Inconclusive($"No restaurant lot beyond the draw radius ({distance:0} m).");
            var results = new Dictionary<string, GoodsOutcome>();
            _root.ClientSubscription.ResultReceived += x => results[x.RequestId] = x;
            _root.ClientSubscription.Bridge.RequestBuyProperty("buy-far", start.SiteId, far.LotId);
            yield return Until(() => results.ContainsKey("buy-far"), "purchase reply");
            Assert.That(results["buy-far"].Accepted, Is.True, results["buy-far"].Reason);

            var navigation = UnityEngine.Object.FindAnyObjectByType<Customers.SiteNavigation>();
            yield return new WaitForSeconds(3f);
            Assert.That(_root.DrawnSites.Find(far.SiteId), Is.Null, "The far lot is not drawn.");
            Assert.That(navigation.BuiltForSite(far.SiteId), Is.Empty, "Nor walkable before it has an employee.");
            // TEST-ONLY: an employee put straight on the far site (hiring needs the player there), which first needs a location;
            // a hire always has the player's inventory there.
            world.Bootstrap(new GoodsLocation { Id = "far-storage", SiteId = far.SiteId, Kind = "storage", Capacity = 1 });
            world.Bootstrap(new GoodsEmployee { Id = "employee-far", SiteId = far.SiteId, Name = "Far" }, GoodsWorld.HiredHandSlots);
            yield return Until(() => navigation.BuiltForSite(far.SiteId) != "", "the far lot's NavMesh");
            Assert.That(_root.DrawnSites.Find(far.SiteId), Is.Null, "Still not drawn.");
        }
    }
}
