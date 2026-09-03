// Loads building-specific interior scenes and returns players to their source building.
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
        public Vector2Int BuildingSize;
        public Vector2[] InteriorExitLogicalPositions = new Vector2[0];
        public Vector2[] ExteriorArrivalLogicalPositions = new Vector2[0];
        public GridEdgeDirection[] InteriorExitDirections = new GridEdgeDirection[0];
        public Scene TargetScene;
    }

    private sealed class BuildingReturn
    {
        public string SceneName = string.Empty;
        public Vector2 ArrivalLogicalPosition;
    }

    [SerializeField] private NetworkObject playerPrefab = null!;
    [SerializeField] private string worldSceneName = "World";
    [SerializeField] private string insideSceneName = "Inside";

    private readonly HashSet<int> awaitingInitialSpawn = new();
    private readonly Dictionary<int, NetworkObject> players = new();
    private readonly Dictionary<int, PendingTransition> pendingTransitions = new();
    private readonly Dictionary<int, BuildingReturn> buildingReturns = new();
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

        var targetSceneName = GetSceneName(portal);
        var arrivalLogicalPosition = portal.ArrivalLogicalPosition;
        var buildingSize = Vector2Int.zero;
        var interiorExitLogicalPositions = new Vector2[0];
        var exteriorArrivalLogicalPositions = new Vector2[0];
        var interiorExitDirections = new GridEdgeDirection[0];
        if (portal.BuildingInstanceId != 0)
        {
            buildingSize = portal.BuildingSize;
            GetBuildingExitPositions(
                player.gameObject.scene,
                portal.BuildingInstanceId,
                portal,
                out interiorExitLogicalPositions,
                out exteriorArrivalLogicalPositions,
                out interiorExitDirections);
            buildingReturns[connection.ClientId] = new BuildingReturn
            {
                SceneName = player.gameObject.scene.name,
                ArrivalLogicalPosition = portal.ExteriorArrivalLogicalPosition
            };
        }
        else if (targetSceneName == worldSceneName
            && buildingReturns.TryGetValue(connection.ClientId, out var buildingReturn))
        {
            targetSceneName = buildingReturn.SceneName;
            arrivalLogicalPosition = portal.HasExteriorArrivalLogicalPosition
                ? portal.ExteriorArrivalLogicalPosition
                : buildingReturn.ArrivalLogicalPosition;
            buildingReturns.Remove(connection.ClientId);
        }

        var lookup = new SceneLookupData(targetSceneName);
        var pendingTransition = new PendingTransition
        {
            Connection = connection,
            Player = player,
            ArrivalLogicalPosition = arrivalLogicalPosition,
            BuildingSize = buildingSize,
            InteriorExitLogicalPositions = interiorExitLogicalPositions,
            ExteriorArrivalLogicalPositions = exteriorArrivalLogicalPositions,
            InteriorExitDirections = interiorExitDirections
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
        buildingReturns.Remove(connection.ClientId);
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
        ConfigureInterior(
            pendingTransition.TargetScene,
            pendingTransition.BuildingSize,
            pendingTransition.ArrivalLogicalPosition,
            pendingTransition.InteriorExitLogicalPositions,
            pendingTransition.ExteriorArrivalLogicalPositions,
            pendingTransition.InteriorExitDirections);
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
            targetPosition,
            pendingTransition.BuildingSize,
            pendingTransition.ArrivalLogicalPosition,
            pendingTransition.InteriorExitLogicalPositions,
            pendingTransition.ExteriorArrivalLogicalPositions,
            pendingTransition.InteriorExitDirections);
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

    private string GetSceneName(ScenePortal portal)
    {
        return string.IsNullOrWhiteSpace(portal.DestinationSceneName)
            ? GetSceneName(portal.Destination)
            : portal.DestinationSceneName;
    }

    private void ConfigureInterior(
        Scene scene,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        Vector2[] interiorExitLogicalPositions,
        Vector2[] exteriorArrivalLogicalPositions,
        GridEdgeDirection[] interiorExitDirections)
    {
        if (InsideFactoryController.TryConfigureForScene(
                scene,
                buildingSize,
                arrivalLogicalPosition,
                interiorExitLogicalPositions,
                exteriorArrivalLogicalPositions,
                interiorExitDirections))
        {
            return;
        }

        IndoorGrid.TryConfigureForScene(scene, buildingSize);
    }

    private static void GetBuildingExitPositions(
        Scene scene,
        uint buildingInstanceId,
        ScenePortal selectedPortal,
        out Vector2[] interiorExitLogicalPositions,
        out Vector2[] exteriorArrivalLogicalPositions,
        out GridEdgeDirection[] interiorExitDirections)
    {
        var interiorPositions = new List<Vector2>();
        var exteriorPositions = new List<Vector2>();
        var interiorDirections = new List<GridEdgeDirection>();
        var selectedPortalFound = false;
        var portals = FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene
                || candidate.BuildingInstanceId != buildingInstanceId
                || candidate.Destination != SceneDestination.Inside
                || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            interiorPositions.Add(candidate.ArrivalLogicalPosition);
            exteriorPositions.Add(candidate.ExteriorArrivalLogicalPosition);
            interiorDirections.Add(candidate.InteriorDoorDirection);
            selectedPortalFound |= candidate == selectedPortal;
        }

        if (!selectedPortalFound)
        {
            interiorPositions.Add(selectedPortal.ArrivalLogicalPosition);
            exteriorPositions.Add(selectedPortal.ExteriorArrivalLogicalPosition);
            interiorDirections.Add(selectedPortal.InteriorDoorDirection);
        }

        interiorExitLogicalPositions = interiorPositions.ToArray();
        exteriorArrivalLogicalPositions = exteriorPositions.ToArray();
        interiorExitDirections = interiorDirections.ToArray();
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
