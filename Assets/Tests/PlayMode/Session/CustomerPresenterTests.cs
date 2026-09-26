// Verifies that a replicated customer becomes a walking local visual in DevSite, on the host and on a loopback-UDP
// remote client, using isolated host and remote save/identity paths.
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
using FoodFactoryGame.Session.Customers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class CustomerPresenterTests
    {
        private readonly InputTestFixture _input = new();
        private string _directory;
        private SessionRoot _root;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ushort _port;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _input.Setup();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryCustomerPresenter", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync("Assets/Scenes/DevSite.unity", LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"), IdentityPath = Path.Combine(_directory, "host.db"),
                DisplayName = "Customer Presenter Test", Address = "127.0.0.1"
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
            }
            finally
            {
                _input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator ReplicatedCustomerWalksFromOffscreenTowardTheCounter()
        {
            var presenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
            Assert.That(presenter, Is.Not.Null, "DevSite needs its customer presenter.");
            yield return StartHost();
            SeedCustomer("visual-test-customer");
            yield return Until(() => _root.ClientSite.Customers.Any(x => x.Id == "visual-test-customer"), "customer baseline");
            yield return Until(() => presenter.VisibleCount == 1, "customer visual");
            yield return AssertWalks(presenter, "visual-test-customer");
        }

        [UnityTest]
        public IEnumerator RemoteClientDrawsTheCustomerFromItsOwnReplicatedSite()
        {
            // A second presenter, driven only by the remote connection's site baseline; copied before any visual exists.
            var hostPresenter = UnityEngine.Object.FindAnyObjectByType<CustomerPresenter>();
            Assert.That(hostPresenter, Is.Not.Null, "DevSite needs its customer presenter.");
            var remotePresenter = UnityEngine.Object.Instantiate(hostPresenter);
            remotePresenter.name = "Remote CustomerPresenter";
            yield return StartHost();

            CreateRemote();
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            var remoteSite = new ClientSiteSubscription(_remote, DevWorld.SiteId);
            yield return Until(() => { remoteSite.Tick(); return remoteSite.Latest != null; }, "remote dev-site baseline");
            remotePresenter.Bind(remoteSite);

            SeedCustomer("remote-visual-customer");
            yield return Until(() => remoteSite.Latest.Customers.Any(x => x.Id == "remote-visual-customer"), "remote customer baseline");
            var server = _root.ServerWorld.Snapshot().Customers.Single(x => x.Id == "remote-visual-customer");
            var replicated = remoteSite.Latest.Customers.Single(x => x.Id == "remote-visual-customer");
            Assert.That(replicated.RestaurantId, Is.EqualTo(server.RestaurantId));
            Assert.That(replicated.Appearance, Is.EqualTo(server.Appearance));

            yield return Until(() => remotePresenter.VisibleCount == 1, "remote customer visual");
            yield return AssertWalks(remotePresenter, "remote-visual-customer");
            Assert.That(hostPresenter.VisibleCount, Is.EqualTo(1), "The host still draws its own copy.");
            Assert.That(remotePresenter.transform.Cast<Transform>().Single().gameObject,
                Is.Not.SameAs(hostPresenter.transform.Cast<Transform>().Single().gameObject));
        }

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ServerBridge != null && _root.ServerBridge.IsServing
                && _root.ClientSite != null && Camera.allCameras.Any(x => x.isActiveAndEnabled), "host and local camera");
        }

        private void SeedCustomer(string id)
        {
            var recipe = _root.Recipes.Single(x => x != null && x.IsSale && x.StationKind == GoodsWorld.CounterKind);
            _root.ServerWorld.Bootstrap(new GoodsCustomer
            {
                Id = id, DistrictId = DevWorld.DistrictId, Appearance = 2, DineIn = true, PatienceSeconds = 600,
                State = CustomerState.Queued, RestaurantId = DevWorld.SiteId, RecipeId = recipe.Id, Ticket = 1000
            });
        }

        private static IEnumerator AssertWalks(CustomerPresenter presenter, string id)
        {
            var visual = presenter.transform.Cast<Transform>().Single(x => x.name == "Customer " + id);
            var initial = visual.position;
            for (var frame = 0; frame < 20; frame++) yield return null;
            Assert.That(Vector3.Distance(visual.position, initial), Is.GreaterThan(0.1f), "The customer should walk toward the counter.");
            Assert.That(visual.GetComponentInChildren<Animator>(), Is.Not.Null);
        }

        // A second, client-only NetworkManager in this process, using the same catalog and the real authenticator.
        private void CreateRemote()
        {
            var go = new GameObject("session-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            // FishNet's editor Reset validates before the catalog below can be assigned.
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on session-test-remote\\."));
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
        }

        private static IEnumerator Until(Func<bool> condition, string step)
        {
            var end = Time.realtimeSinceStartup + 10f;
            while (!condition() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(condition(), Is.True, "Timed out waiting for " + step);
        }
    }
}
