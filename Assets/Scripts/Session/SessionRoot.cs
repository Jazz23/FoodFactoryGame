// Server composition root: commits the world save before FishNet starts, owns the player registry and bridge,
// spawns one avatar per authenticated connection and one worker per saved employee. The world runs whenever the server runs,
// observed or not. In a scene with a world layout bridge, a newly created world also gets a generated layout (decision 0026),
// stored before its first snapshot and replicated to clients for presentation only. A layout with lots (format 3) makes a
// generated world whose primary site is its starting restaurant (GeneratedWorld, decision 0028): players join and spawn there,
// and the join answer tells each client which site to subscribe to. Otherwise the dev world and its dev site are used.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Equipment;
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
        // Content for every equipment kind the site can show; the dev seed places the "oven" kind.
        [SerializeField] private EquipmentDefinition[] equipmentDefinitions = Array.Empty<EquipmentDefinition>();
        // Recipe content: the server registers every entry with the world; clients read it for the machine screen.
        [SerializeField] private RecipeAsset[] recipes = Array.Empty<RecipeAsset>();
        // Supplier offers (decision 0014): content, registered on every server start like recipes.
        [SerializeField] private OfferAsset[] offers = Array.Empty<OfferAsset>();
        // Item content: names and icons for the HUD, and each item's max stack, which the server registers with the world.
        [SerializeField] private ItemDefinition[] items = Array.Empty<ItemDefinition>();
        // PROTOTYPE: when set, the server seeds the dev employee and spawns one of these per saved employee record once the
        // goods bridge serves. Scenes without a NavMesh leave it empty; their saves keep any employee records untouched.
        [SerializeField] private EmployeeWorker employeePrefab;
        // When set, a world created by this server gets a generated layout, and this bridge replicates the stored layout to
        // clients. Scenes without it (DevSite) never generate one; their saves are unaffected.
        [SerializeField] private NetworkObject worldLayoutBridgePrefab;
        // Save folder under the saves directory when -save is not given. WorldGen uses its own, so it never loads a DevSite
        // world (which could never gain a layout).
        [SerializeField] private string saveFolder = SessionOptions.DefaultSaveFolder;
        [SerializeField] private bool readCommandLine = true;

        private SessionOptions _options;
        private PlayerRegistry _registry;
        private ClientSiteSubscription _site;
        private int _nextSpawn;
        private bool _stopping;
        // Server-only: the stored layout's placement (null without a generated world), for the enter-site position check.
        private SitePlacement _serverPlacement;
        // Server-only: each connected player's avatar, and its latest pose, saved to the registry when the player leaves.
        private readonly Dictionary<NetworkConnection, (string PlayerId, PlayerAvatar Avatar)> _avatars = new();
        private readonly Dictionary<string, (Vector3 Position, float Yaw)> _poses = new();
        private PlayerAvatar _localAvatar;

        public SessionMode Mode { get; private set; }
        public string Status { get; private set; } = "Not connected";
        public NetworkManager NetworkManager => networkManager;
        public DevAuthenticator Authenticator => authenticator;
        public SessionOptions Options => _options;
        // Server-only diagnostics; null on pure clients.
        public GoodsWorld ServerWorld { get; private set; }
        public GoodsNetworkBridge ServerBridge { get; private set; }
        // Server-only: this world's stored layout, or null (no generation in this scene, or a save from before it).
        public StoredWorldLayout ServerLayout { get; private set; }
        // Server-only: the starting restaurant's lot when this is a generated world (decision 0028), otherwise null.
        public PropertyOffer StartOffer { get; private set; }
        public bool GeneratesWorld => worldLayoutBridgePrefab != null;
        public PlayerRegistry ServerRegistry => _registry;
        public GoodsSnapshot ClientSite => _site?.Latest;
        public ClientSiteSubscription ClientSubscription => _site;
        // The sites this client draws: the current one and owned ones near the camera (decision 0029).
        public DrawnSites DrawnSites { get; private set; }
        // Walking into another owned lot enters it (decision 0031).
        public SiteEntry SiteEntry { get; private set; }
        // The site this client presents (named by the server's join answer); null before joining.
        public string ClientSiteId => _site?.SiteId;
        public IReadOnlyList<EquipmentDefinition> EquipmentDefinitions => equipmentDefinitions;
        public IReadOnlyList<RecipeAsset> Recipes => recipes;
        public IReadOnlyList<OfferAsset> Offers => offers;
        public IReadOnlyList<ItemDefinition> Items => items;
        // Client previews count slots with the same content the server registers; an item without a definition stacks to 1.
        public int MaxStack(string itemId) => items.FirstOrDefault(x => x != null && x.Id == itemId)?.MaxStack ?? 1;
        public bool IsRunning => Mode != SessionMode.None;
        // The transport finishes stopping on a later iteration. Starting before then lets the old server's late Stopped event
        // release the new world, so Begin waits for both local connections to be fully stopped.
        public bool CanBegin => !IsRunning && networkManager.TransportManager.Transport.GetConnectionState(true) == LocalConnectionState.Stopped
            && networkManager.TransportManager.Transport.GetConnectionState(false) == LocalConnectionState.Stopped;

        private void Awake()
        {
            _options = readCommandLine ? SessionOptions.FromCommandLine(Environment.GetCommandLineArgs()) : SessionOptions.FromCommandLine(Array.Empty<string>());
            if (!_options.SaveDirectoryExplicit && SessionOptions.IsValidWorldName(saveFolder))
                _options.SaveDirectory = Path.Combine(SessionOptions.SavesRoot, saveFolder);
            _site = new ClientSiteSubscription(networkManager);
            DrawnSites = new DrawnSites(_site);
            SiteEntry = new SiteEntry(_site);
            networkManager.ServerManager.OnServerConnectionState += OnServerState;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteState;
            networkManager.ClientManager.OnClientConnectionState += OnClientState;
            networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            authenticator.ClientJoinAnswered += OnJoinAnswered;
        }

        private void Start()
        {
            if (_options.Mode != SessionMode.None) Begin(_options.Mode);
        }

        private void Update()
        {
            if (_avatars.Count > 0) TrackPoses();
            _site.Tick();
            var camera = Belts.BeltPresenter.ViewCamera();
            DrawnSites.Tick(camera != null ? camera.transform.position : (Vector3?)null);
            var avatar = LocalAvatar();
            SiteEntry.Tick(avatar != null ? avatar.transform.position : (Vector3?)null);
        }

        // This client's own avatar, or null before it spawns.
        private PlayerAvatar LocalAvatar()
        {
            if (_localAvatar != null && _localAvatar.IsOwner) return _localAvatar;
            _localAvatar = networkManager.IsClientStarted
                ? networkManager.ClientManager.Objects.Spawned.Values.Select(x => x.GetComponent<PlayerAvatar>()).FirstOrDefault(x => x != null && x.IsOwner)
                : null;
            return _localAvatar;
        }

        private void OnDestroy()
        {
            Shutdown();
            if (networkManager != null)
            {
                networkManager.ServerManager.OnServerConnectionState -= OnServerState;
                networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteState;
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

        // The world a host or server will open: the name of its save folder beside the current one.
        public string WorldName => Path.GetFileName(_options.SaveDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        // Points the next start at the named world's save folder, a sibling of the current one; a new name makes a new world.
        public bool SelectWorld(string name)
        {
            if (IsRunning || !SessionOptions.IsValidWorldName(name)) return false;
            _options.SaveDirectory = Path.Combine(WorldsDirectory, name.Trim());
            return true;
        }

        // First unused "world-N" beside the current save folder.
        public string NextNewWorldName()
        {
            var number = 1;
            while (Directory.Exists(Path.Combine(WorldsDirectory, $"world-{number}"))) number++;
            return $"world-{number}";
        }

        // The worlds SelectWorld can open, most recently played first.
        public IReadOnlyList<SavedWorld> SavedWorlds() => SessionOptions.SavedWorlds(WorldsDirectory);

        // The folder holding the current save folder and its sibling worlds.
        private string WorldsDirectory => Path.GetDirectoryName(_options.SaveDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        public bool Begin(SessionMode mode, string displayName = null, string address = null)
        {
            if (!CanBegin || mode == SessionMode.None) return false;
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
                || error is ArgumentException || error is FormatException)
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
                SiteEntry?.Reset();
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
            // Before the world save: a new world's layout is written first, so a crash cannot leave a new world without one.
            ServerLayout = GeneratesWorld
                ? WorldGeneration.PrepareLayout(_options.WorldPath, _options.LegacyWorldPath, DevWorld.WorldId, _options.WorldSeed)
                : null;
            _serverPlacement = SitePlacement.For(ServerLayout?.Layout);
            // The world is committed before FishNet listens, so the bridge never serves an uncommitted state.
            // Max stacks are content: capacity counts slots, so they are registered (inside LoadOrCreate, before the seed)
            // ahead of any request. A layout with lots (format 3) makes a generated world that starts in its own restaurant, with
            // the property catalog registered inside; no layout, or format 1 or 2, keeps the dev world beside the map.
            ServerWorld = GeneratedWorld.Supports(ServerLayout)
                ? GeneratedWorld.LoadOrCreate(_options.WorldPath, ServerLayout, items,
                    equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == DevWorld.CounterKind),
                    equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == DevWorld.TableKind))
                : null;
            StartOffer = ServerWorld != null ? GeneratedWorld.StartOffer(ServerLayout.Layout) : null;
            ServerWorld ??= DevWorld.LoadOrCreate(_options.WorldPath, equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == "oven"), items,
                _options.LegacyWorldPath, equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == DevWorld.CounterKind),
                employeePrefab != null, equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == GoodsWorld.DockKind),
                equipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == DevWorld.TableKind));
            var primarySite = StartOffer?.SiteId ?? DevWorld.SiteId;
            // Machine buffer slot counts follow content, so a saved machine created with older counts is brought up to date.
            foreach (var definition in equipmentDefinitions.Where(x => x != null))
                ServerWorld.ApplyEquipmentCapacitiesDurably(definition.Kind, definition.InputCapacity, definition.OutputCapacity, _options.WorldPath);
            // Nobody is connected yet, so no player works a register (decision 0034); employees keep theirs.
            ServerWorld.ReleaseStaffDurably(null, _options.WorldPath);
            // Recipes are content, not saved state, so they are registered on every start, including a recovered save.
            foreach (var recipe in recipes) ServerWorld.RegisterRecipe(recipe.ToDefinition());
            // Kinds that run only while switched on (decision 0037), content like recipes.
            foreach (var definition in equipmentDefinitions.Where(x => x != null && x.ManualPower)) ServerWorld.RegisterManualPower(definition.Kind);
            foreach (var offer in offers) offer.RegisterWith(ServerWorld);
            ServerWorld.RegisterFloorOffer(DevWorld.FloorOffer);
            // Like floors, the property catalog is content: the stored layout's lots (decision 0028), none without a layout. A
            // generated world registered its own while loading.
            if (ServerLayout != null && StartOffer == null) ServerWorld.RegisterPropertyOffers(WorldLayoutShells.PropertyOffers(ServerLayout.Layout));
            // Machines run by themselves, Factorio-style (decision 0008), once switched on where their kind needs it (decision
            // 0037); like recipes, this is configuration, not saved.
            ServerWorld.AutomaticJobs = true;
            _registry = new PlayerRegistry(_options.RegistryPath);
            // PROTOTYPE: players start with the dev inventory and starter goods on the primary site in either kind of world.
            authenticator.ConfigureServer(new SessionAdmission(_registry, ServerWorld, primarySite, _options.WorldPath,
                DevWorld.InventoryCapacity, DevWorld.StarterGoods, StartOffer == null ? DevWorld.RemoteSiteIds : null));
            SetStatus("Starting server...");
            if (!networkManager.ServerManager.StartConnection()) throw new InvalidOperationException("Transport refused to start the server.");
        }

        private void StartClient()
        {
            var secret = ClientIdentity.LoadOrCreate(_options.IdentityPath, _options.LegacyIdentityPath);
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
                if (worldLayoutBridgePrefab != null)
                {
                    var layoutBridge = Instantiate(worldLayoutBridgePrefab);
                    layoutBridge.GetComponent<WorldLayoutBridge>().InitializeServer(ServerLayout);
                    networkManager.ServerManager.Spawn(layoutBridge);
                }
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
            bridge.InitializeServer(ServerWorld, authenticator.PlayerIdOf, _options.WorldPath, MapPositionOf);
            ServerBridge = bridge;
            var registry = _registry;
            if (registry != null) bridge.ConfigurePlayerSettings(registry.WageWarningHoursOf, registry.SaveWageWarningHours);
            // A scene without the employee prefab cannot show employees, so it does not hire them (decision 0039).
            if (employeePrefab != null) bridge.ConfigureWorkforce(ScenePoseOf, SpawnEmployee, DespawnEmployee);
            SpawnEmployees(bridge);
        }

        // Employees exist because the save has them: one networked worker per record, at its saved pose.
        private void SpawnEmployees(GoodsNetworkBridge bridge)
        {
            if (employeePrefab == null) return;
            foreach (var record in bridge.Employees()) SpawnEmployee(record);
        }

        // Spawns the worker of a record that has none (a hire, decision 0039, or the saved records at start).
        private void SpawnEmployee(GoodsEmployee record)
        {
            if (employeePrefab == null || record == null || WorkerOf(record.Id) != null) return;
            var worker = Instantiate(employeePrefab, new Vector3(record.X, record.Y, record.Z), Quaternion.Euler(0f, record.Yaw, 0f));
            worker.Configure(record);
            networkManager.ServerManager.Spawn(worker.gameObject);
        }

        // A fired employee's worker leaves the scene (decision 0039).
        private void DespawnEmployee(string employeeId)
        {
            var worker = WorkerOf(employeeId);
            if (worker != null) networkManager.ServerManager.Despawn(worker.gameObject);
        }

        private static EmployeeWorker WorkerOf(string employeeId) =>
            FindObjectsByType<EmployeeWorker>().FirstOrDefault(x => x.ServerEmployeeId == employeeId);

        // Server: where a new hire stands, a metre to the right of the hiring player's avatar (the server's copy of it).
        private (Vector3 Position, float Yaw)? ScenePoseOf(NetworkConnection connection)
        {
            if (connection == null || !_avatars.TryGetValue(connection, out var entry) || entry.Avatar == null) return null;
            var avatar = entry.Avatar.transform;
            return (avatar.position + avatar.right, avatar.eulerAngles.y);
        }

        private void ReleaseServer()
        {
            // The bridge holds the world save while serving and ticks between commits (decision 0016); FishNet may stop it a
            // frame later, so save the pending ticks and close the save now.
            if (ServerWorld != null && _options != null && !ServerWorld.TryCommitDurably(_options.WorldPath))
                Debug.LogWarning("[Session] Could not save the last clock ticks while stopping; the save keeps its previous revision.");
            if (_options != null) GoodsSnapshotStore.Release(_options.WorldPath);
            ServerBridge = null;
            ServerWorld = null;
            ServerLayout = null;
            StartOffer = null;
            SavePoses();
            _avatars.Clear();
            _poses.Clear();
            _serverPlacement = null;
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
            // Arrives before FishNet marks the connection authenticated, so the site is known before the subscription starts.
            if (response.Accepted) _site.SetPrimary(string.IsNullOrEmpty(response.SiteId) ? DevWorld.SiteId : response.SiteId);
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
            // A returning player starts where they left (owner decision, 0031); a new one at the next spawn point.
            var spawn = SpawnFor(playerId);
            var instance = Instantiate(playerPrefab, spawn.position, spawn.rotation);
            networkManager.ServerManager.Spawn(instance, connection);
            networkManager.SceneManager.AddOwnerToDefaultScene(instance);
            var avatar = instance.GetComponent<PlayerAvatar>();
            avatar.SetDisplayName(_registry.DisplayNameOf(playerId));
            _avatars[connection] = (playerId, avatar);
        }

        // A returning player starts where they left (owner decision, 0031), but a saved pose is checked first, since a client
        // can save one after falling through the world: on a lot, the height is set to that building's ground floor unless it
        // is plausibly on one of its storeys; off every lot and far below the starting floor, the player goes to the apron of
        // the site holding their inventory. New players use the next spawn point.
        private (Vector3 position, Quaternion rotation) SpawnFor(string playerId)
        {
            var saved = _registry.PoseOf(playerId);
            if (saved == null) return NextSpawn();
            var position = new Vector3(saved.Value.X, saved.Value.Y, saved.Value.Z);
            var rotation = Quaternion.Euler(0f, saved.Value.Yaw, 0f);
            if (_serverPlacement == null) return position.y > -LostDepth ? (position, rotation) : NextSpawn();
            var lot = _serverPlacement.SiteAt(position);
            if (lot != null)
            {
                var floor = _serverPlacement.SiteOrigin(lot).y;
                if (position.y < floor - 1f || position.y > floor + 10 * SiteGridSpace.LevelHeight) position.y = floor + 0.05f;
                return (position, rotation);
            }
            if (position.y > -LostDepth) return (position, rotation);
            var home = ServerWorld.CarriedSiteOf(playerId);
            var offer = home == null ? null : WorldLayoutShells.PropertyOffers(ServerLayout.Layout).FirstOrDefault(x => x.SiteId == home);
            return offer == null ? NextSpawn() : ApronSpawn(offer, 0, _serverPlacement.SiteOrigin(home));
        }

        // Metres below the starting floor at which a saved pose off every lot counts as lost under the world.
        private const float LostDepth = 50f;

        // Server: keeps each connected avatar's latest pose, so it can be saved once the avatar is gone.
        private void TrackPoses()
        {
            foreach (var (playerId, avatar) in _avatars.Values)
                if (avatar != null) _poses[playerId] = (avatar.transform.position, avatar.transform.eulerAngles.y);
        }

        private void OnRemoteState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped || !_avatars.TryGetValue(connection, out var entry)) return;
            TrackPoses();
            _avatars.Remove(connection);
            SavePose(entry.PlayerId);
            // A player who left no longer works a register (decision 0034).
            if (ServerBridge != null) ServerBridge.ReleaseStaff(entry.PlayerId);
        }

        private void SavePoses()
        {
            TrackPoses();
            foreach (var playerId in _poses.Keys.ToList()) SavePose(playerId);
        }

        private void SavePose(string playerId)
        {
            if (_registry == null || !_poses.TryGetValue(playerId, out var pose)) return;
            if (!_registry.SavePose(playerId, pose.Position.x, pose.Position.y, pose.Position.z, pose.Yaw))
                Debug.LogWarning($"[Session] Could not save where {playerId} stood; they will rejoin at their previous point.");
            _poses.Remove(playerId);
        }

        // Server: where a connection's avatar stands on the map, from the server's own copy of it (decision 0031); null without
        // a generated world or an avatar. The avatar moves under its owner's control, so this is a sanity check, not anti-cheat.
        private (float X, float Z)? MapPositionOf(NetworkConnection connection)
        {
            if (_serverPlacement == null || connection == null || !_avatars.TryGetValue(connection, out var entry) || entry.Avatar == null) return null;
            var map = _serverPlacement.ToMap(entry.Avatar.transform.position);
            return (map.x, map.y);
        }

        private (Vector3 position, Quaternion rotation) NextSpawn()
        {
            if (StartOffer != null) return ApronSpawn(StartOffer, _nextSpawn++);
            if (spawnPoints.Length == 0) return (Vector3.zero, Quaternion.identity);
            var point = spawnPoints[_nextSpawn++ % spawnPoints.Length];
            return (point.position, point.rotation);
        }

        // Generated worlds (decision 0028): players arrive on the starting lot's apron two cells out from its first door, facing
        // it, spread along the wall (0, +2, -2, +4, -4 cells, repeating) and kept on the lot. The site grid is centred on the scene
        // origin (SiteGridSpace), the presenter moves the map to match.
        // origin: where the lot's site stands; by default the active placement's (the server passes its own, decision 0031).
        public static (Vector3 position, Quaternion rotation) ApronSpawn(PropertyOffer offer, int index, Vector3? origin = null)
        {
            var door = offer.Doors[0];
            var outward = door.Z == offer.BuildingZ + offer.BuildingDepth - 1 ? new Vector2Int(0, 1)
                : door.Z == offer.BuildingZ ? new Vector2Int(0, -1)
                : door.X == offer.BuildingX ? new Vector2Int(-1, 0) : new Vector2Int(1, 0);
            var along = new Vector2Int(outward.y, outward.x);
            var step = index % 5;
            var offset = step == 0 ? 0 : (step % 2 == 1 ? 1 : -1) * 2 * ((step + 1) / 2);
            var x = Mathf.Clamp(door.X + outward.x * 2 + along.x * offset, 0, offer.Width - 1);
            var z = Mathf.Clamp(door.Z + outward.y * 2 + along.y * offset, 0, offer.Depth - 1);
            var grid = new SiteLayout { SiteId = offer.SiteId, Width = offer.Width, Depth = offer.Depth };
            var position = SiteGridSpace.FootprintCenter(grid, x, z, 1, 1) - SiteGridSpace.Origin(grid) + (origin ?? SiteGridSpace.Origin(grid))
                + Vector3.up * 0.05f;
            return (position, Quaternion.LookRotation(new Vector3(-outward.x, 0f, -outward.y)));
        }

        private void SetStatus(string status)
        {
            Status = status;
            Debug.Log($"[Session] {status}");
        }
    }
}
