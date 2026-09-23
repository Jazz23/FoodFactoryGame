// Runs equipment pickup/placement through the real DevSite bridge, presenter and authenticator with a host plus a
// loopback-UDP remote client. Every save and identity path is a unique temporary directory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.RegularExpressions;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class EquipmentPlacementTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private string _directory;
        private SessionRoot _root;
        private EquipmentPresenter _presenter;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ClientSiteSubscription _remoteSite;
        private ushort _port;
        private readonly Dictionary<string, GoodsOutcome> _results = new();

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _results.Clear();
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryEquipmentPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            _presenter = UnityEngine.Object.FindAnyObjectByType<EquipmentPresenter>();
            Assert.That(_root, Is.Not.Null);
            Assert.That(_presenter, Is.Not.Null, "DevSite must contain an EquipmentPresenter.");
            Configure();
        }

        private void Configure()
        {
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = Path.Combine(_directory, "host.secret"),
                DisplayName = "Host",
                Address = "127.0.0.1"
            });
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                _port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
            _root.NetworkManager.TransportManager.Transport.SetPort(_port);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
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

        private void CreateRemote()
        {
            var go = new GameObject("equipment-test-remote");
            go.SetActive(false);
#if UNITY_EDITOR
            LogAssert.Expect(LogType.Error, new Regex("^SpawnablePrefabs is null on equipment-test-remote\\."));
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
            _remoteAuth.SetClientCredentials("Remote", ClientIdentity.LoadOrCreate(Path.Combine(_directory, "remote.secret")));
            _remoteSite = new ClientSiteSubscription(_remote, DevWorld.SiteId);
            _remoteSite.ResultReceived += Store;
        }

        private void Store(GoodsOutcome outcome) => _results[outcome.RequestId] = outcome;

        private IEnumerator StartHost()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.ClientSite != null && _root.ClientSubscription.Bridge != null, "host dev-site baseline");
            _root.ClientSubscription.ResultReceived += Store;
        }

        private IEnumerator ConnectRemote()
        {
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest != null && _remoteSite.Bridge != null; }, "remote dev-site baseline");
        }

        private IEnumerator Await(string requestId)
        {
            yield return Until(() => { _remoteSite?.Tick(); return _results.ContainsKey(requestId); }, $"result for {requestId}");
        }

        private static GoodsEquipment Oven(GoodsSnapshot site) => site?.Equipment.SingleOrDefault(x => x.Id == DevWorld.OvenId);

        private Vector3 Expected(GoodsEquipment equipment) =>
            SiteGridSpace.Center(_root.ClientSite.SiteLayouts.Single(), equipment);

        [UnityTest]
        public IEnumerator HostMovesSeededOvenAndRemoteSeesEveryStep()
        {
            yield return StartHost();
            var hostId = _root.Authenticator.LocalPlayerId;
            CreateRemote();
            yield return ConnectRemote();
            var remoteId = _remoteAuth.LocalPlayerId;
            var host = _root.ClientSubscription.Bridge;
            var remote = _remoteSite.Bridge;

            // The seed places one oven and gives each admitted player an inventory.
            var seeded = Oven(_root.ClientSite);
            Assert.That((seeded.State, seeded.CellX, seeded.CellZ), Is.EqualTo((EquipmentState.Placed, DevWorld.OvenCellX, DevWorld.OvenCellZ)));
            Assert.That(_root.ServerWorld.Snapshot().Locations.Count(x => x.Kind == "carried"), Is.EqualTo(2));
            yield return Until(() => _presenter.Visuals.ContainsKey(DevWorld.OvenId), "seeded oven visual");
            Assert.That(Vector3.Distance(_presenter.Visuals[DevWorld.OvenId].transform.position, Expected(seeded)), Is.LessThan(0.001f));
            Assert.That(_presenter.Visuals[DevWorld.OvenId].GetComponentInChildren<Collider>(), Is.Not.Null);

            host.RequestPickUp("host-pick", DevWorld.OvenId);
            yield return Await("host-pick");
            Assert.That(_results["host-pick"].Accepted, Is.True, _results["host-pick"].Reason);
            yield return Until(() => { _remoteSite.Tick(); return Oven(_remoteSite.Latest)?.HolderId == hostId; }, "remote sees host holding the oven");
            yield return Until(() => !_presenter.Visuals.ContainsKey(DevWorld.OvenId), "held oven has no scene visual");

            remote.RequestPickUp("remote-grab", DevWorld.OvenId);
            yield return Await("remote-grab");
            Assert.That(_results["remote-grab"].Reason, Is.EqualTo("not-placed"));
            remote.RequestPlace("remote-steal", DevWorld.OvenId, 2, 2, 0);
            yield return Await("remote-steal");
            Assert.That(_results["remote-steal"].Reason, Is.EqualTo("not-held"));
            host.RequestPlace("host-edge", DevWorld.OvenId, DevWorld.GridWidth - 2, 0, 0);
            yield return Await("host-edge");
            Assert.That(_results["host-edge"].Reason, Is.EqualTo("out-of-bounds"));

            host.RequestPlace("host-place", DevWorld.OvenId, 4, 5, 1);
            yield return Await("host-place");
            Assert.That(_results["host-place"].Accepted, Is.True, _results["host-place"].Reason);
            yield return Until(() =>
            {
                _remoteSite.Tick();
                var seen = Oven(_remoteSite.Latest);
                return seen != null && seen.State == EquipmentState.Placed && seen.CellX == 4 && seen.CellZ == 5
                    && _remoteSite.Latest.Revision == _root.ClientSite.Revision;
            }, "remote sees the oven at its new cell");
            yield return Until(() => _presenter.Visuals.Count == 1 && _presenter.Visuals.ContainsKey(DevWorld.OvenId), "one host visual");
            var placed = Oven(_root.ClientSite);
            Assert.That(placed.Rotation, Is.EqualTo(1));
            Assert.That(Vector3.Distance(_presenter.Visuals[DevWorld.OvenId].transform.position, Expected(placed)), Is.LessThan(0.001f));
            Assert.That(_root.ServerWorld.Snapshot().Equipment.Count, Is.EqualTo(1));

            // A holder who disconnects keeps the oven in their inventory and still holds it after reconnecting.
            remote.RequestPickUp("remote-pick", DevWorld.OvenId);
            yield return Await("remote-pick");
            Assert.That(_results["remote-pick"].Accepted, Is.True, _results["remote-pick"].Reason);
            _remoteSite.Reset();
            _remote.ClientManager.StopConnection();
            yield return Until(() => _root.Authenticator.AuthenticatedCount == 1, "remote disconnected");
            Assert.That(Oven(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot()).HolderId, Is.EqualTo(remoteId));
            yield return ConnectRemote();
            Assert.That(_remoteAuth.LocalPlayerId, Is.EqualTo(remoteId));
            Assert.That(Oven(_remoteSite.Latest).HolderId, Is.EqualTo(remoteId));
            _remoteSite.Bridge.RequestPlace("remote-place", DevWorld.OvenId, 6, 6, 0);
            yield return Await("remote-place");
            Assert.That(_results["remote-place"].Accepted, Is.True, _results["remote-place"].Reason);
        }

        // The host goes through the real HUD and interaction layer; the remote uses the bridge directly for rejections.
        [UnityTest]
        public IEnumerator HostBakesBreadThroughTheOvenScreenAndRemoteSeesItRun()
        {
            yield return StartHost();
            var hostId = _root.Authenticator.LocalPlayerId;
            CreateRemote();
            yield return ConnectRemote();
            var remoteId = _remoteAuth.LocalPlayerId;
            var interaction = UnityEngine.Object.FindAnyObjectByType<EquipmentInteraction>();
            var hud = UnityEngine.Object.FindAnyObjectByType<PlayerHud>();
            var input = DevWorld.OvenId + ":in";
            var output = DevWorld.OvenId + ":out";
            int Count(GoodsSnapshot site, string location, string item) =>
                site.Lots.Where(x => x.LocationId == location && x.ItemId == item).Sum(x => x.Quantity);

            // Dev seed: storage dough and starter dough for each new player.
            var server = _root.ServerWorld.Snapshot();
            Assert.That(Count(server, DevWorld.StorageId, "dough"), Is.EqualTo(DevWorld.StorageDough));
            Assert.That(Count(server, GoodsWorld.InventoryLocationId(hostId), "dough"), Is.EqualTo(DevWorld.StarterDough));
            Assert.That(Count(server, GoodsWorld.InventoryLocationId(remoteId), "dough"), Is.EqualTo(DevWorld.StarterDough));

            var remote = _remoteSite.Bridge;
            remote.RequestStartJob("remote-empty", DevWorld.OvenId, "oven-bread");
            yield return Await("remote-empty");
            Assert.That(_results["remote-empty"].Reason, Is.EqualTo("missing-inputs"));
            remote.RequestStartJob("remote-bogus", DevWorld.OvenId, "no-such-recipe");
            yield return Await("remote-bogus");
            Assert.That(_results["remote-bogus"].Reason, Is.EqualTo("invalid-recipe"));

            // The oven screen opens from the crosshair click path's entry point and lists the recipe and a Start button.
            yield return Until(() => { interaction.OpenMachine(DevWorld.OvenId); return interaction.Screen == InteractionScreen.Machine; }, "oven screen");
            yield return null;
            Assert.That(hud.ScreenRoot.Q<Button>("hud-recipe-oven-bread"), Is.Not.Null);
            Assert.That(hud.ScreenRoot.Q<Button>("hud-start"), Is.Not.Null);
            Assert.That(hud.ScreenRoot.Q<Button>("hud-inventory-dough"), Is.Not.Null);

            // Clicking the inventory stack moves all of it into the oven input.
            interaction.MoveGoods(_root.ClientSite.Lots.Where(x => x.LocationId == interaction.InventoryId).ToList(), input);
            yield return Until(() => Count(_root.ClientSite, input, "dough") == DevWorld.StarterDough && !interaction.HasPendingRequests, "dough in the oven");
            yield return null;
            Assert.That(hud.ScreenRoot.Q<Button>("hud-machine-input-dough"), Is.Not.Null);

            interaction.StartJob(DevWorld.OvenId, "oven-bread");
            yield return Until(() => { _remoteSite.Tick(); return _remoteSite.Latest.Jobs.Any(x => x.StationId == DevWorld.OvenId && x.StartedBy == hostId); },
                "remote sees the running batch");
            Assert.That(interaction.LastRejection, Is.Null);
            yield return Until(() => _presenter.Visuals[DevWorld.OvenId].Running, "oven visual running");
            Assert.That(Count(_root.ServerWorld.Snapshot(), input, "dough"), Is.EqualTo(DevWorld.StarterDough - 1), "One dough per batch.");

            // One batch per click: a second start while it runs is refused.
            remote.RequestStartJob("remote-busy", DevWorld.OvenId, "oven-bread");
            yield return Await("remote-busy");
            Assert.That(_results["remote-busy"].Reason, Is.EqualTo("station-busy"));

            yield return Until(() => Count(_root.ClientSite, output, "bread") == 1, "bread in the output", 20f);
            yield return Until(() => !_presenter.Visuals[DevWorld.OvenId].Running, "oven visual idle");
            Assert.That(_root.ClientSite.Jobs, Is.Empty, "The oven does not start another batch by itself.");

            // Clicking the output moves the bread into the inventory.
            interaction.MoveGoods(_root.ClientSite.Lots.Where(x => x.LocationId == output).ToList(), interaction.InventoryId);
            yield return Until(() => Count(_root.ClientSite, interaction.InventoryId, "bread") == 1 && !interaction.HasPendingRequests, "bread in the inventory");
            Assert.That(Count(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot(), GoodsWorld.InventoryLocationId(hostId), "bread"), Is.EqualTo(1));
            interaction.CloseScreen();
        }

        [UnityTest]
        public IEnumerator ServerRestartShowsOvenWhereLastCommitted()
        {
            yield return StartHost();
            var host = _root.ClientSubscription.Bridge;
            host.RequestPickUp("pick", DevWorld.OvenId);
            yield return Await("pick");
            host.RequestPlace("place", DevWorld.OvenId, 3, 8, 2);
            yield return Await("place");
            Assert.That(_results["place"].Accepted, Is.True, _results["place"].Reason);

            _root.Shutdown();
            Assert.That(_root.Begin(SessionMode.Server), Is.False, "A restart must wait until the old server has fully stopped.");
            yield return Until(() => _root.CanBegin, "old server fully stopped");
            Assert.That(_root.Begin(SessionMode.Server), Is.True);
            yield return Until(() => _root.ServerBridge != null, "server-only restart");
            var oven = Oven(_root.ServerWorld.Snapshot());
            Assert.That((oven.State, oven.CellX, oven.CellZ, oven.Rotation), Is.EqualTo((EquipmentState.Placed, 3, 8, 2)));
            Assert.That(_root.ServerWorld.Snapshot().Equipment.Count, Is.EqualTo(1), "The seed is not re-applied to an existing save.");
        }
    }
}
