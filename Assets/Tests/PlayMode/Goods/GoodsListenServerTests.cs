// Exercises the actual FishNet RPC/spawn path with two distinct loopback client managers and no site presentation.
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
using FishNet.Managing.Object;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods.Network;
using NUnit.Framework;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Goods.PlayModeTests
{
    public sealed class GoodsListenServerTests
    {
        private const string PrefabPath = "Assets/Tests/PlayMode/Goods/GoodsBridgeFixture.prefab";
        private readonly List<UnityEngine.Object> _objects = new();
        private string _directory;
        private NetworkObject _prefab;
        private NetworkManager _host;
        private NetworkManager _remote;

        private static NetworkManager CreateManager(string name, NetworkObject prefab, ushort port, bool allowMultiple,
            List<UnityEngine.Object> objects)
        {
            var collection = ScriptableObject.CreateInstance<SinglePrefabObjects>();
            collection.AddObject(prefab, initializeAdded: false);
            objects.Add(collection);
            var go = new GameObject(name);
            go.SetActive(false);
            objects.Add(go);
#if UNITY_EDITOR
            // FishNet's editor Reset validates before the isolated collection below can be assigned.
            LogAssert.Expect(LogType.Error, new Regex($"^SpawnablePrefabs is null on {Regex.Escape(name)}\\."));
#endif
            var manager = go.AddComponent<NetworkManager>();
            manager.SpawnablePrefabs = collection;
            if (allowMultiple)
            {
                typeof(NetworkManager).GetField("_persistence", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, NetworkManager.PersistenceType.AllowMultiple);
            }
            go.SetActive(true);
            Assert.That(manager.Initialized, Is.True, name);
            var tugboat = go.GetComponent<Tugboat>();
            tugboat.SetPort(port);
            tugboat.SetClientAddress("127.0.0.1");
            return manager;
        }

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 8f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        // The fixture prefab is loaded through the Editor asset database, so this proof runs in Editor PlayMode only.
        [UnityTest, UnityPlatform(RuntimePlatform.WindowsEditor, RuntimePlatform.OSXEditor, RuntimePlatform.LinuxEditor)]
        public IEnumerator HostAndRemoteUseRpcForGrantTransferRejectionAndUnobservedTime()
        {
            // Test-only site/item/capacity/spoilage; no scene authoring or application save is touched.
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>(PrefabPath);
#else
            NetworkObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null, "The isolated bridge fixture prefab must be authored by Unity Editor.");
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryGoodsListen", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, "goods.db");
            var world = new GoodsWorld("test-listen-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            world.Bootstrap(new GoodsLocation { Id = "kitchen", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 6 });
            world.Bootstrap(new GoodsLot { Id = "lot-1", ItemId = "ingredient", OwnerId = "restaurant", LocationId = "storage", Quantity = 10, SpoilAfterSeconds = 2 });
            world.Grant("host-player", "restaurant");
            GoodsSnapshotStore.Save(world, path);
            NetworkManager host = null;
            NetworkManager remote = null;
            // The asset is authored non-spawnable so FishNet never auto-registers it in gameplay DefaultPrefabObjects;
            // it is spawnable only in memory for this test and restored below.
            _prefab = prefab;
            prefab.SetIsSpawnable(true);
            try
            {
                ushort port;
                using (var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                    port = (ushort)((IPEndPoint)socket.Client.LocalEndPoint).Port;
                host = _host = CreateManager("goods-test-host", prefab, port, false, _objects);
                remote = _remote = CreateManager("goods-test-remote", prefab, port, true, _objects);
                Assert.That(host.ServerManager.StartConnection(), Is.True);
                yield return Until(() => host.ServerManager.Started, "server started");
                Assert.That(host.ClientManager.StartConnection(), Is.True);
                yield return Until(() => host.IsHostStarted && host.ServerManager.Clients.Count == 1, "local host client");
                Assert.That(remote.ClientManager.StartConnection(), Is.True);
                yield return Until(() => remote.IsClientStarted && host.ServerManager.Clients.Count == 2, "remote UDP client");
                var hostId = host.ClientManager.Connection.ClientId;
                var remoteId = remote.ClientManager.Connection.ClientId;
                Assert.That(hostId, Is.Not.EqualTo(remoteId));
                var identities = new Dictionary<int, string> { [hostId] = "host-player", [remoteId] = "ungranted-player" };
                var instance = UnityEngine.Object.Instantiate(prefab);
                _objects.Add(instance.gameObject);
                host.ServerManager.Spawn(instance);
                var serverBridge = instance.GetComponent<GoodsNetworkBridge>();
                yield return Until(() => serverBridge.IsServerStarted, "server bridge spawned");
                serverBridge.InitializeServer(world, connection => identities.TryGetValue(connection.ClientId, out var player) ? player : null, path);
                host.SceneManager.AddConnectionToScene(host.ServerManager.Clients[hostId], instance.gameObject.scene);
                host.SceneManager.AddConnectionToScene(host.ServerManager.Clients[remoteId], instance.gameObject.scene);
                yield return Until(() => remote.ClientManager.Objects.Spawned.Values.Any(x => x.GetComponent<GoodsNetworkBridge>() != null),
                    "bridge visible on remote client");
                var remoteBridge = remote.ClientManager.Objects.Spawned.Values.Single(x => x.GetComponent<GoodsNetworkBridge>() != null)
                    .GetComponent<GoodsNetworkBridge>();
                var hostResults = new List<GoodsOutcome>();
                var remoteResults = new List<GoodsOutcome>();
                var baselines = new List<GoodsSnapshot>();
                serverBridge.ResultReceived += hostResults.Add;
                remoteBridge.ResultReceived += remoteResults.Add;
                serverBridge.SiteReceived += baselines.Add;
                // No subscription or site presentation. Server-owned time and condition must still advance.
                yield return Until(() => world.Snapshot().Lots.Single().Spoiled, "unobserved site spoilage", 7f);
                Assert.That(GoodsSnapshotStore.Load(path).Snapshot().Lots.Single().Spoiled, Is.True);
                Assert.That(baselines, Is.Empty);
                serverBridge.RequestSite("restaurant");
                yield return Until(() => baselines.Count > 0, "granted site baseline");
                Assert.That(baselines[0].Locations.All(x => x.SiteId == "restaurant"), Is.True);
                remoteBridge.RequestSite("restaurant");
                yield return Until(() => remoteResults.Any(x => x.Reason == "subscription-forbidden"), "denied remote subscription");
                remoteBridge.RequestTransfer("remote-transfer", "lot-1", "kitchen", 2);
                yield return Until(() => remoteResults.Any(x => x.RequestId == "remote-transfer"), "unauthorized remote rejection");
                Assert.That(remoteResults.Single(x => x.RequestId == "remote-transfer").Reason, Is.EqualTo("forbidden"));
                serverBridge.RequestTransfer("host-transfer", "lot-1", "kitchen", 2);
                yield return Until(() => hostResults.Any(x => x.RequestId == "host-transfer"), "authorized host transfer");
                Assert.That(hostResults.Single(x => x.RequestId == "host-transfer").Accepted, Is.True);
                Assert.That(GoodsSnapshotStore.Load(path).Snapshot().Lots.Sum(x => x.Quantity), Is.EqualTo(10));
                Assert.That(world.Snapshot().Lots.Single(x => x.LocationId == "kitchen").Quantity, Is.EqualTo(2));
            }
            finally
            {
                _host = host;
                _remote = remote;
                Cleanup();
            }
        }

        // A failed UnityTest may abandon its coroutine without running finally, so TearDown repeats the cleanup.
        [TearDown]
        public void TearDown() => Cleanup();

        private void Cleanup()
        {
            if (_prefab != null) _prefab.SetIsSpawnable(false);
            _prefab = null;
            if (_remote != null) _remote.ClientManager.StopConnection();
            if (_host != null)
            {
                _host.ClientManager.StopConnection();
                _host.ServerManager.StopConnection(true);
            }
            _remote = null;
            _host = null;
            foreach (var item in _objects)
                if (item != null) UnityEngine.Object.DestroyImmediate(item);
            _objects.Clear();
            if (_directory != null && Directory.Exists(_directory)) Directory.Delete(_directory, true);
            _directory = null;
        }
    }
}
