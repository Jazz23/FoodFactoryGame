using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

[RequireComponent(typeof(NetworkManager))]
public sealed class GameSceneManager : MonoBehaviour
{
    private sealed class PendingTransition
    {
        public NetworkConnection Connection = null!;
        public NetworkObject Player = null!;
        public Vector2 ArrivalLogicalPosition;
        public Scene TargetScene;
    }

    [SerializeField] private NetworkObject playerPrefab = null!;
    [SerializeField] private string worldSceneName = "World";
    [SerializeField] private string insideSceneName = "Inside";

    private readonly HashSet<int> awaitingInitialSpawn = new();
    private readonly Dictionary<int, NetworkObject> players = new();
    private readonly Dictionary<int, PendingTransition> pendingTransitions = new();
    private NetworkManager networkManager = null!;

    public static GameSceneManager Instance = null!;

    private void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        networkManager.ServerManager.OnAuthenticationResult += AuthenticationResult;
        networkManager.ServerManager.OnRemoteConnectionState += RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd += SceneLoadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd += ClientPresenceChangeEnd;
    }

    private void OnDisable()
    {
        networkManager.ServerManager.OnAuthenticationResult -= AuthenticationResult;
        networkManager.ServerManager.OnRemoteConnectionState -= RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd -= SceneLoadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd -= ClientPresenceChangeEnd;
    }

    public bool RequestTransition(NetworkObject player, ScenePortal portal)
    {
        var connection = player.Owner;
        if (!connection.IsValid || pendingTransitions.ContainsKey(connection.ClientId))
        {
            return false;
        }

        var lookup = new SceneLookupData(GetSceneName(portal.Destination));
        var pendingTransition = new PendingTransition
        {
            Connection = connection,
            Player = player,
            ArrivalLogicalPosition = portal.ArrivalLogicalPosition
        };
        var sceneLoadData = new SceneLoadData(new[] { lookup }, new[] { player })
        {
            ReplaceScenes = ReplaceOption.OnlineOnly,
            PreferredActiveScene = new PreferredScene(lookup, null!),
            Params = new LoadParams
            {
                ServerParams = new object[] { pendingTransition }
            }
        };

        pendingTransitions[connection.ClientId] = pendingTransition;
        networkManager.SceneManager.LoadConnectionScenes(connection, sceneLoadData);
        return true;
    }

    private void AuthenticationResult(NetworkConnection connection, bool authenticated)
    {
        if (!authenticated)
        {
            return;
        }

        awaitingInitialSpawn.Add(connection.ClientId);
        LoadInitialWorld(connection);
    }

    private void RemoteConnectionStateChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState != RemoteConnectionState.Stopped)
        {
            return;
        }

        awaitingInitialSpawn.Remove(connection.ClientId);
        players.Remove(connection.ClientId);
        pendingTransitions.Remove(connection.ClientId);
    }

    private void LoadInitialWorld(NetworkConnection connection)
    {
        var lookup = new SceneLookupData(worldSceneName);
        var sceneLoadData = new SceneLoadData(lookup)
        {
            ReplaceScenes = ReplaceOption.OnlineOnly,
            PreferredActiveScene = new PreferredScene(lookup, null!)
        };

        networkManager.SceneManager.LoadConnectionScenes(connection, sceneLoadData);
    }

    private void SceneLoadEnd(SceneLoadEndEventArgs args)
    {
        if (!args.QueueData.AsServer || args.QueueData.ScopeType != SceneScopeType.Connections)
        {
            return;
        }

        var serverParameters = args.QueueData.SceneLoadData.Params.ServerParams;
        if (serverParameters.Length != 1 || serverParameters[0] is not PendingTransition pendingTransition)
        {
            return;
        }

        pendingTransition.TargetScene = args.QueueData.SceneLoadData.GetFirstLookupScene();
        if (!TryGetGrid(pendingTransition.TargetScene, out var grid))
        {
            return;
        }

        var targetPosition = grid.LogicalToWorld(pendingTransition.ArrivalLogicalPosition);
        pendingTransition.Player.GetComponent<PlayerSceneTransition>().ServerTeleport(targetPosition);
    }

    private void ClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
    {
        if (!args.Added)
        {
            return;
        }

        if (awaitingInitialSpawn.Remove(args.Connection.ClientId) && args.Scene.name == worldSceneName)
        {
            SpawnInitialPlayer(args.Connection, args.Scene);
            return;
        }

        if (!pendingTransitions.TryGetValue(args.Connection.ClientId, out var pendingTransition)
            || args.Scene.handle != pendingTransition.TargetScene.handle)
        {
            return;
        }

        if (!TryGetGrid(args.Scene, out var grid))
        {
            return;
        }

        var targetPosition = grid.LogicalToWorld(pendingTransition.ArrivalLogicalPosition);
        pendingTransition.Player.GetComponent<PlayerSceneTransition>().CompleteTransition(
            pendingTransition.Connection,
            targetPosition);
        pendingTransitions.Remove(args.Connection.ClientId);
    }

    private void SpawnInitialPlayer(NetworkConnection connection, Scene scene)
    {
        if (!TryGetGrid(scene, out var grid))
        {
            return;
        }

        var position = grid.LogicalToWorld(grid.InitialPlayerLogicalPosition);
        var player = networkManager.GetPooledInstantiated(
            playerPrefab,
            position,
            Quaternion.identity,
            true);

        UnitySceneManager.MoveGameObjectToScene(player.gameObject, scene);
        networkManager.ServerManager.Spawn(player, connection);
        players[connection.ClientId] = player;
    }

    private string GetSceneName(SceneDestination destination)
    {
        return destination == SceneDestination.World ? worldSceneName : insideSceneName;
    }

    private bool TryGetGrid(Scene scene, out SceneGrid grid)
    {
        if (SceneGrid.TryGetForScene(scene, out grid))
        {
            return true;
        }

        SceneGrid.LogMissingGrid(scene, this);
        return false;
    }
}
