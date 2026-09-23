// Server composition root: commits the world save before FishNet starts, owns the player registry and bridge,
// and spawns one avatar per authenticated connection. The world runs whenever the server runs, observed or not.
using System;
using System.Collections;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session.Player;
using SQLite;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    [DisallowMultipleComponent]
    public sealed class SessionRoot : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private DevAuthenticator authenticator;
        [SerializeField] private NetworkObject bridgePrefab;
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private Transform[] spawnPoints = Array.Empty<Transform>();
        [SerializeField] private bool readCommandLine = true;

        private SessionOptions _options;
        private PlayerRegistry _registry;
        private ClientSiteSubscription _site;
        private int _nextSpawn;
        private bool _stopping;

        public SessionMode Mode { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public NetworkManager NetworkManager => networkManager;
        public DevAuthenticator Authenticator => authenticator;
        public SessionOptions Options => _options;
        // Server-only diagnostics; null on pure clients.
        public GoodsWorld ServerWorld { get; private set; }
        public GoodsNetworkBridge ServerBridge { get; private set; }
        public PlayerRegistry ServerRegistry => _registry;
        public GoodsSnapshot ClientSite => _site?.Latest;
        public bool IsRunning => Mode != SessionMode.None;

        private void Awake()
        {
            _options = readCommandLine ? SessionOptions.FromCommandLine(Environment.GetCommandLineArgs()) : SessionOptions.FromCommandLine(Array.Empty<string>());
            _site = new ClientSiteSubscription(networkManager, DevWorld.SiteId);
            networkManager.ServerManager.OnServerConnectionState += OnServerState;
            networkManager.ClientManager.OnClientConnectionState += OnClientState;
            networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            authenticator.ClientJoinAnswered += OnJoinAnswered;
        }

        private void Start()
        {
            if (_options.Mode != SessionMode.None) Begin(_options.Mode);
        }

        private void Update() => _site.Tick();

        private void OnDestroy()
        {
            Shutdown();
            if (networkManager != null)
            {
                networkManager.ServerManager.OnServerConnectionState -= OnServerState;
                networkManager.ClientManager.OnClientConnectionState -= OnClientState;
                networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
            }
            if (authenticator != null) authenticator.ClientJoinAnswered -= OnJoinAnswered;
        }

        // Tests and tools replace command-line options before Begin; paths must then be explicit and isolated.
        public void Configure(SessionOptions options)
        {
            if (IsRunning) throw new InvalidOperationException("Stop the session before reconfiguring it.");
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public bool Begin(SessionMode mode, string displayName = null, string address = null)
        {
            if (IsRunning || mode == SessionMode.None) return false;
            if (!string.IsNullOrWhiteSpace(displayName)) _options.DisplayName = displayName.Trim();
            if (!string.IsNullOrWhiteSpace(address)) _options.Address = address.Trim();
            Mode = mode;
            try
            {
                if (mode != SessionMode.Client) StartServer();
                else StartClient();
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is InvalidOperationException || error is NotSupportedException || error is SQLiteException
                || error is ArgumentException)
            {
                Debug.LogException(error);
                SetStatus($"Could not start: {error.Message}");
                Shutdown();
                return false;
            }
        }

        public void Shutdown()
        {
            if (_stopping) return;
            _stopping = true;
            try
            {
                if (networkManager != null)
                {
                    if (networkManager.IsClientStarted) networkManager.ClientManager.StopConnection();
                    if (networkManager.IsServerStarted) networkManager.ServerManager.StopConnection(true);
                }
                ReleaseServer();
                _site?.Reset();
                Mode = SessionMode.None;
            }
            finally
            {
                _stopping = false;
            }
        }

        private void StartServer()
        {
            Directory.CreateDirectory(_options.SaveDirectory);
            // The world is committed before FishNet listens, so the bridge never serves an uncommitted state.
            ServerWorld = DevWorld.LoadOrCreate(_options.WorldPath);
            _registry = new PlayerRegistry(_options.RegistryPath);
            authenticator.ConfigureServer(new SessionAdmission(_registry, ServerWorld, DevWorld.SiteId, _options.WorldPath));
            SetStatus("Starting server...");
            if (!networkManager.ServerManager.StartConnection()) throw new InvalidOperationException("Transport refused to start the server.");
        }

        private void StartClient()
        {
            var secret = ClientIdentity.LoadOrCreate(_options.IdentityPath);
            authenticator.SetClientCredentials(_options.DisplayName, secret);
            networkManager.TransportManager.Transport.SetClientAddress(_options.Address);
            SetStatus($"Connecting to {_options.Address}...");
            if (!networkManager.ClientManager.StartConnection()) throw new InvalidOperationException("Transport refused to start the client.");
        }

        private void OnServerState(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started && ServerBridge == null && ServerWorld != null)
            {
                var bridge = Instantiate(bridgePrefab);
                networkManager.ServerManager.Spawn(bridge);
                StartCoroutine(InitializeBridge(bridge.GetComponent<GoodsNetworkBridge>()));
                SetStatus(Mode == SessionMode.Server ? "Server running" : "Hosting");
                if (Mode == SessionMode.Host)
                {
                    try { StartClient(); }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException)
                    {
                        Debug.LogException(error);
                        SetStatus($"Server running; local client failed: {error.Message}");
                    }
                }
            }
            else if (args.ConnectionState == LocalConnectionState.Stopped)
            {
                ReleaseServer();
                if (Mode != SessionMode.Client && !_stopping) Mode = SessionMode.None;
            }
        }

        private IEnumerator InitializeBridge(GoodsNetworkBridge bridge)
        {
            while (bridge != null && !bridge.IsServerStarted) yield return null;
            if (bridge == null || ServerWorld == null) yield break;
            bridge.InitializeServer(ServerWorld, authenticator.PlayerIdOf, _options.WorldPath);
            ServerBridge = bridge;
        }

        private void ReleaseServer()
        {
            ServerBridge = null;
            ServerWorld = null;
            _registry?.Dispose();
            _registry = null;
        }

        private void OnClientState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped) return;
            _site.Reset();
            var reason = authenticator.LastRejection;
            SetStatus(string.IsNullOrEmpty(reason) ? "Disconnected" : $"Rejected: {reason}");
            // A pure client returns to the menu; a host keeps its server (and world) running.
            if (Mode == SessionMode.Client && !_stopping) Mode = SessionMode.None;
        }

        private void OnJoinAnswered(JoinResponseBroadcast response)
        {
            SetStatus(response.Accepted ? $"Joined as {response.PlayerId}" : $"Rejected: {response.Reason}");
        }

        // FishNet sends start scenes only after authentication, so every connection here has a player ID.
        private void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer) return;
            var playerId = authenticator.PlayerIdOf(connection);
            if (playerId == null || _registry == null)
            {
                connection.Disconnect(true);
                return;
            }
            var spawn = NextSpawn();
            var instance = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            networkManager.ServerManager.Spawn(instance, connection);
            networkManager.SceneManager.AddOwnerToDefaultScene(instance);
            instance.GetComponent<PlayerAvatar>().SetDisplayName(_registry.DisplayNameOf(playerId));
        }

        private (Vector3 position, Quaternion rotation) NextSpawn()
        {
            if (spawnPoints.Length == 0) return (Vector3.zero, Quaternion.identity);
            var point = spawnPoints[_nextSpawn++ % spawnPoints.Length];
            return (point.position, point.rotation);
        }

        private void SetStatus(string status)
        {
            Status = status;
            Debug.Log($"[Session] {status}");
        }
    }
}
