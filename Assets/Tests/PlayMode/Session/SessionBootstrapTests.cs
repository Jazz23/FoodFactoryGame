// Runs the real DevSite authoring and DevAuthenticator with a host plus a loopback-UDP remote client in one process.
// Every save and identity path is a unique temporary directory; the application's saves are never opened.
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
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FoodFactoryGame.Session.PlayModeTests
{
    public sealed class SessionBootstrapTests
    {
        private const string ScenePath = "Assets/Scenes/DevSite.unity";
        private string _directory;
        private SessionRoot _root;
        private NetworkManager _remote;
        private DevAuthenticator _remoteAuth;
        private ushort _port;

        private string HostSecretPath => Path.Combine(_directory, "host.secret");
        private string RemoteSecretPath => Path.Combine(_directory, "remote.secret");

        private static IEnumerator Until(Func<bool> predicate, string step, float timeout = 10f)
        {
            var end = Time.realtimeSinceStartup + timeout;
            while (!predicate() && Time.realtimeSinceStartup < end) yield return null;
            Assert.That(predicate(), Is.True, $"Timed out waiting for {step}.");
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactorySessionPlay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _root = UnityEngine.Object.FindAnyObjectByType<SessionRoot>();
            Assert.That(_root, Is.Not.Null, "DevSite must contain a SessionRoot.");
            _root.Configure(new SessionOptions
            {
                SaveDirectory = Path.Combine(_directory, "save"),
                IdentityPath = HostSecretPath,
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

        // A second, client-only NetworkManager in this process, using the same catalog and the real authenticator.
        private void CreateRemote(string secretPath, string name)
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
            _remoteAuth.SetClientCredentials(name, ClientIdentity.LoadOrCreate(secretPath));
        }

        private static List<PlayerAvatar> Avatars(IEnumerable<NetworkObject> spawned) =>
            spawned.Select(x => x.GetComponent<PlayerAvatar>()).Where(x => x != null).ToList();

        private List<PlayerAvatar> ServerAvatars => Avatars(_root.NetworkManager.ServerManager.Objects.Spawned.Values);
        private List<PlayerAvatar> RemoteAvatars => Avatars(_remote.ClientManager.Objects.Spawned.Values);

        [UnityTest]
        public IEnumerator HostAndRemoteJoinWithStableIdsOwnedAvatarsAndSiteState()
        {
            Assert.That(_root.Begin(SessionMode.Host), Is.True);
            yield return Until(() => _root.Authenticator.LocalPlayerId != null, "host local client authenticated");
            var hostId = _root.Authenticator.LocalPlayerId;
            CreateRemote(RemoteSecretPath, "Remote");
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => _remoteAuth.LocalPlayerId != null, "remote authenticated");
            var remoteId = _remoteAuth.LocalPlayerId;
            Assert.That(remoteId, Is.Not.EqualTo(hostId));
            Assert.That(_root.ServerRegistry.Count, Is.EqualTo(2));

            // Exactly one server-spawned avatar per authenticated connection, owned by that connection.
            yield return Until(() => ServerAvatars.Count == 2, "two server avatars");
            foreach (var avatar in ServerAvatars)
                Assert.That(new[] { hostId, remoteId }, Does.Contain(_root.Authenticator.PlayerIdOf(avatar.Owner)));
            Assert.That(ServerAvatars.Select(x => _root.Authenticator.PlayerIdOf(x.Owner)).Distinct().Count(), Is.EqualTo(2));
            Assert.That(ServerAvatars.Select(x => x.DisplayName), Is.EquivalentTo(new[] { "Host", "Remote" }));
            yield return Until(() => RemoteAvatars.Count == 2, "remote observes both avatars");

            // Host process copies: only the host-owned avatar has a live rig. Remote copies: only the remote-owned one.
            var hostOwned = ServerAvatars.Where(x => x.CameraRig.gameObject.activeSelf).ToList();
            Assert.That(hostOwned.Count, Is.EqualTo(1));
            Assert.That(_root.Authenticator.PlayerIdOf(hostOwned[0].Owner), Is.EqualTo(hostId));
            var remoteOwned = RemoteAvatars.Where(x => x.CameraRig.gameObject.activeSelf).ToList();
            Assert.That(remoteOwned.Count, Is.EqualTo(1));
            Assert.That(remoteOwned[0].IsOwner, Is.True);

            // Both clients receive the authorized dev-site baseline through the real bridge.
            var remoteSite = new ClientSiteSubscription(_remote, DevWorld.SiteId);
            yield return Until(() => { remoteSite.Tick(); return remoteSite.Latest != null; }, "remote dev-site baseline");
            yield return Until(() => _root.ClientSite != null, "host dev-site baseline");
            Assert.That(remoteSite.Latest.Locations.All(x => x.SiteId == DevWorld.SiteId), Is.True);
            Assert.That(remoteSite.Latest.Grants, Is.Empty);
            var committed = GoodsSnapshotStore.Load(_root.Options.WorldPath);
            Assert.That(committed.CanView(hostId, DevWorld.SiteId) && committed.CanView(remoteId, DevWorld.SiteId), Is.True);

            // Disconnect removes the avatar; the world keeps running on the server.
            _remote.ClientManager.StopConnection();
            yield return Until(() => ServerAvatars.Count == 1 && _root.Authenticator.AuthenticatedCount == 1, "remote avatar removed");

            // Reconnecting with the same secret resolves the same player ID.
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => _remoteAuth.LocalPlayerId != null, "remote re-authenticated");
            Assert.That(_remoteAuth.LocalPlayerId, Is.EqualTo(remoteId));
            yield return Until(() => ServerAvatars.Count == 2, "reconnected avatar");
            _remote.ClientManager.StopConnection();
            yield return Until(() => ServerAvatars.Count == 1, "remote left again");

            // Presenting an identity that is already connected is rejected without a second avatar.
            _remoteAuth.SetClientCredentials("Impostor", ClientIdentity.LoadOrCreate(HostSecretPath));
            Assert.That(_remote.ClientManager.StartConnection(), Is.True);
            yield return Until(() => _remoteAuth.LastRejection != null, "duplicate identity rejected");
            Assert.That(_remoteAuth.LastRejection, Is.EqualTo("already-connected"));
            // The client leaves after reading the reason, well before the server's 2 s fallback kick.
            yield return Until(() => !_remote.IsClientStarted, "rejected client disconnected itself", 1f);
            Assert.That(ServerAvatars.Count, Is.EqualTo(1));
            Assert.That(_root.ServerRegistry.Count, Is.EqualTo(2));
            Assert.That(_root.ServerRegistry.DisplayNameOf(hostId), Is.EqualTo("Host"), "The impostor must not rename the host.");
            yield return Until(() => _root.NetworkManager.ServerManager.Clients.Count == 1, "server dropped the rejected connection", 4f);
        }

        [UnityTest]
        public IEnumerator ServerOnlyStartAdvancesWorldWithoutAnyClient()
        {
            Assert.That(_root.Begin(SessionMode.Server), Is.True);
            yield return Until(() => _root.ServerBridge != null, "bridge initialized");
            var start = _root.ServerWorld.Snapshot().ClockSeconds;
            yield return Until(() => _root.ServerWorld.Snapshot().ClockSeconds >= start + 2, "unobserved world clock", 6f);
            Assert.That(_root.NetworkManager.IsClientStarted, Is.False);
            Assert.That(ServerAvatars, Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<Camera>(), Is.Empty);
            Assert.That(GoodsSnapshotStore.Load(_root.Options.WorldPath).Snapshot().ClockSeconds, Is.GreaterThanOrEqualTo(start + 2));
        }
    }
}
