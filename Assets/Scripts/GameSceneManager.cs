// Loads building-specific interior scenes and returns players to their source building.
using System;
using System.Collections.Generic;
using System.IO;
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
    public const uint OutsideTestBuildingId = 1;
    public const int OutsideTestFloorCount = 3;

    private const string OutsideTestStateFileName = "outsidetest-floor-state.json";
    private const float OutsideTestBroadcastInterval = 0.25f;

    private sealed class PendingTransition
    {
        public NetworkConnection Connection = null!;
        public NetworkObject Player = null!;
        public Vector2 ArrivalLogicalPosition;
        public Vector2Int BuildingSize;
        public Vector2[] InteriorExitLogicalPositions = new Vector2[0];
        public Vector2[] ExteriorArrivalLogicalPositions = new Vector2[0];
        public GridEdgeDirection[] InteriorExitDirections = new GridEdgeDirection[0];
        public uint BuildingInstanceId;
        public int StoryCount = 1;
        public int FloorIndex;
        public Scene TargetScene;
    }

    private sealed class BuildingReturn
    {
        public string SceneName = string.Empty;
        public Vector2 ArrivalLogicalPosition;
        public Vector2Int BuildingSize;
        public uint BuildingInstanceId;
        public int StoryCount = 1;
        public int CurrentFloor;
        public Vector2[] InteriorExitLogicalPositions = new Vector2[0];
        public Vector2[] ExteriorArrivalLogicalPositions = new Vector2[0];
        public GridEdgeDirection[] InteriorExitDirections = new GridEdgeDirection[0];
    }

    [SerializeField] private NetworkObject playerPrefab = null!;
    [SerializeField] private string worldSceneName = "World";
    [SerializeField] private string insideSceneName = "Inside";

    private readonly HashSet<int> awaitingInitialSpawn = new();
    private readonly Dictionary<int, NetworkObject> players = new();
    private readonly Dictionary<int, PendingTransition> pendingTransitions = new();
    private readonly Dictionary<int, BuildingReturn> buildingReturns = new();
    private readonly Dictionary<int, OutsideTestFloorRecord> outsideTestFloors = new();
    private readonly Dictionary<int, OutsideTestFloorRecord> clientOutsideTestFloors = new();
    private NetworkManager networkManager = null!;
    private bool outsideTestStateLoaded;
    private int clientOutsideTestLoadedInteriorCount;
    private float nextOutsideTestBroadcastTime;

    public static GameSceneManager Instance = null!;

    public int OutsideTestLoadedInteriorCount => networkManager.IsServerStarted
        ? OutsideTestFloorPresentation.LoadedCount
        : clientOutsideTestLoadedInteriorCount;

    private void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        EnsureOutsideTestStateLoaded();
    }

    private void OnEnable()
    {
        networkManager.ServerManager.OnAuthenticationResult += AuthenticationResult;
        networkManager.ServerManager.OnServerConnectionState += ServerConnectionStateChanged;
        networkManager.ServerManager.OnRemoteConnectionState += RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd += SceneLoadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd += ClientPresenceChangeEnd;
    }

    private void OnDisable()
    {
        networkManager.ServerManager.OnAuthenticationResult -= AuthenticationResult;
        networkManager.ServerManager.OnServerConnectionState -= ServerConnectionStateChanged;
        networkManager.ServerManager.OnRemoteConnectionState -= RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd -= SceneLoadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd -= ClientPresenceChangeEnd;
        if (networkManager.IsServerStarted)
        {
            SaveOutsideTestFloorState();
        }
    }

    private void OnApplicationQuit()
    {
        if (networkManager.IsServerStarted)
        {
            SaveOutsideTestFloorState();
        }
    }

    private void Update()
    {
        if (!networkManager.IsServerStarted)
        {
            return;
        }

        EnsureOutsideTestStateLoaded();
        var deltaTime = Time.deltaTime;
        foreach (var floor in outsideTestFloors.Values)
        {
            floor.Advance(deltaTime);
        }

        if (Time.unscaledTime < nextOutsideTestBroadcastTime)
        {
            return;
        }

        nextOutsideTestBroadcastTime = Time.unscaledTime + OutsideTestBroadcastInterval;
        BroadcastOutsideTestFloorStates();
    }

    public bool TryGetOutsideTestFloorState(
        int floorIndex,
        out OutsideTestFloorRecord state)
    {
        EnsureOutsideTestStateLoaded();
        var floors = networkManager.IsServerStarted
            ? outsideTestFloors
            : clientOutsideTestFloors;
        return floors.TryGetValue(floorIndex, out state);
    }

    public bool TrySetOutsideTestFloorState(
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        if (!networkManager.IsServerStarted
            || !outsideTestFloors.TryGetValue(floorIndex, out var floor))
        {
            return false;
        }

        floor.SetState(
            label,
            productionRate,
            floor.AccumulatedProduction,
            markerPosition);
        BroadcastOutsideTestFloorState(floorIndex);
        return true;
    }

    public bool SaveOutsideTestFloorState()
    {
        EnsureOutsideTestStateLoaded();
        var saveData = new OutsideTestFloorSaveData(outsideTestFloors.Values);
        try
        {
            File.WriteAllText(GetOutsideTestStatePath(), JsonUtility.ToJson(saveData, true));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"Could not save OutsideTest floor state: {exception.Message}",
                this);
            return false;
        }
    }

    public bool LoadOutsideTestFloorState()
    {
        if (!networkManager.IsServerStarted)
        {
            return false;
        }

        outsideTestStateLoaded = false;
        EnsureOutsideTestStateLoaded();
        SaveOutsideTestFloorState();
        BroadcastOutsideTestFloorStates();
        return true;
    }

    public void SendOutsideTestFloorSnapshot(PlayerSceneTransition player)
    {
        if (!networkManager.IsServerStarted)
        {
            return;
        }

        var loadedInteriorCount = OutsideTestLoadedInteriorCount;
        foreach (var floor in outsideTestFloors.Values)
        {
            player.ServerSendOutsideTestFloorState(floor, loadedInteriorCount);
        }
    }

    public void ReceiveOutsideTestFloorState(
        int floorIndex,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerPosition,
        int loadedInteriorCount)
    {
        if (floorIndex < 0 || floorIndex >= OutsideTestFloorCount)
        {
            return;
        }

        if (!clientOutsideTestFloors.TryGetValue(floorIndex, out var floor))
        {
            floor = new OutsideTestFloorRecord(
                OutsideTestBuildingId,
                floorIndex,
                label,
                productionRate,
                accumulatedProduction,
                markerPosition);
            clientOutsideTestFloors.Add(floorIndex, floor);
        }
        else
        {
            floor.SetState(
                label,
                productionRate,
                accumulatedProduction,
                markerPosition);
        }

        clientOutsideTestLoadedInteriorCount = loadedInteriorCount;
    }

    private void ServerConnectionStateChanged(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            EnsureOutsideTestStateLoaded();
            SaveOutsideTestFloorState();
            BroadcastOutsideTestFloorStates();
            return;
        }

        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            SaveOutsideTestFloorState();
        }
    }

    private void BroadcastOutsideTestFloorStates()
    {
        foreach (var floor in outsideTestFloors.Values)
        {
            BroadcastOutsideTestFloorState(floor.FloorIndex);
        }
    }

    private void BroadcastOutsideTestFloorState(int floorIndex)
    {
        if (!outsideTestFloors.TryGetValue(floorIndex, out var floor))
        {
            return;
        }

        var loadedInteriorCount = OutsideTestLoadedInteriorCount;
        foreach (var playerObject in players.Values)
        {
            var player = playerObject.GetComponent<PlayerSceneTransition>();
            player.ServerSendOutsideTestFloorState(floor, loadedInteriorCount);
        }
    }

    private void EnsureOutsideTestStateLoaded()
    {
        if (outsideTestStateLoaded)
        {
            return;
        }

        outsideTestFloors.Clear();
        var needsSave = true;
        var path = GetOutsideTestStatePath();
        if (File.Exists(path))
        {
            try
            {
                var data = JsonUtility.FromJson<OutsideTestFloorSaveData>(
                    File.ReadAllText(path));
                if (data is not null && data.Floors is not null)
                {
                    foreach (var floor in data.Floors)
                    {
                        if (floor is null
                            || floor.BuildingInstanceId != OutsideTestBuildingId
                            || floor.FloorIndex < 0
                            || floor.FloorIndex >= OutsideTestFloorCount)
                        {
                            continue;
                        }

                        floor.SetState(
                            floor.Label,
                            floor.ProductionRate,
                            floor.AccumulatedProduction,
                            floor.MarkerPosition);
                        outsideTestFloors[floor.FloorIndex] = floor;
                    }

                    needsSave = false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not read OutsideTest floor state: {exception.Message}",
                    this);
            }
        }

        for (var floorIndex = 0; floorIndex < OutsideTestFloorCount; floorIndex++)
        {
            if (outsideTestFloors.ContainsKey(floorIndex))
            {
                continue;
            }

            outsideTestFloors.Add(
                floorIndex,
                OutsideTestFloorRecord.CreateDefault(
                    OutsideTestBuildingId,
                    floorIndex));
            needsSave = true;
        }

        outsideTestStateLoaded = true;
        if (needsSave && networkManager.IsServerStarted)
        {
            SaveOutsideTestFloorState();
        }
    }

    private static string GetOutsideTestStatePath()
    {
        return Path.Combine(Application.persistentDataPath, OutsideTestStateFileName);
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
        var buildingInstanceId = 0u;
        var storyCount = 1;
        var floorIndex = 0;
        if (portal.BuildingInstanceId != 0)
        {
            buildingSize = portal.BuildingSize;
            buildingInstanceId = portal.BuildingInstanceId;
            storyCount = portal.BuildingStoryCount;
            floorIndex = portal.FloorIndex;
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
                ArrivalLogicalPosition = portal.ExteriorArrivalLogicalPosition,
                BuildingSize = buildingSize,
                BuildingInstanceId = buildingInstanceId,
                StoryCount = storyCount,
                CurrentFloor = floorIndex,
                InteriorExitLogicalPositions = interiorExitLogicalPositions,
                ExteriorArrivalLogicalPositions = exteriorArrivalLogicalPositions,
                InteriorExitDirections = interiorExitDirections
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
            InteriorExitDirections = interiorExitDirections,
            BuildingInstanceId = buildingInstanceId,
            StoryCount = storyCount,
            FloorIndex = floorIndex
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

    public bool RequestFloorTransition(NetworkObject player, int targetFloorIndex)
    {
        var connection = player.Owner;
        if (!connection.IsValid || pendingTransitions.ContainsKey(connection.ClientId))
        {
            return false;
        }

        if (!buildingReturns.TryGetValue(connection.ClientId, out var buildingReturn)
            || !InsideFactoryController.TryGetForScene(
                player.gameObject.scene,
                out var controller)
            || controller.BuildingInstanceId != buildingReturn.BuildingInstanceId
            || controller.CurrentFloor != buildingReturn.CurrentFloor
            || controller.StoryCount != buildingReturn.StoryCount)
        {
            return false;
        }

        if (!controller.Elevator.IsFloorAvailable(targetFloorIndex))
        {
            return false;
        }

        var targetSceneName = TestBuildingFloorScenes.GetSceneName(
            buildingReturn.BuildingInstanceId,
            targetFloorIndex);
        if (!Application.CanStreamedLevelBeLoaded(targetSceneName))
        {
            return false;
        }

        var lookup = new SceneLookupData(targetSceneName);
        var arrivalLogicalPosition = InsideFactoryController.GetElevatorArrivalLogicalPosition(
            buildingReturn.BuildingSize);
        var pendingTransition = new PendingTransition
        {
            Connection = connection,
            Player = player,
            ArrivalLogicalPosition = arrivalLogicalPosition,
            BuildingSize = buildingReturn.BuildingSize,
            InteriorExitLogicalPositions = buildingReturn.InteriorExitLogicalPositions,
            ExteriorArrivalLogicalPositions = buildingReturn.ExteriorArrivalLogicalPositions,
            InteriorExitDirections = buildingReturn.InteriorExitDirections,
            BuildingInstanceId = buildingReturn.BuildingInstanceId,
            StoryCount = buildingReturn.StoryCount,
            FloorIndex = targetFloorIndex
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

        buildingReturn.CurrentFloor = targetFloorIndex;
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
            pendingTransition.InteriorExitDirections,
            pendingTransition.BuildingInstanceId,
            pendingTransition.StoryCount,
            pendingTransition.FloorIndex);
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
            pendingTransition.InteriorExitDirections,
            pendingTransition.BuildingInstanceId,
            pendingTransition.StoryCount,
            pendingTransition.FloorIndex);
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
        GridEdgeDirection[] interiorExitDirections,
        uint buildingInstanceId,
        int storyCount,
        int floorIndex)
    {
        if (InsideFactoryController.TryConfigureForScene(
                scene,
                buildingSize,
                arrivalLogicalPosition,
                interiorExitLogicalPositions,
                exteriorArrivalLogicalPositions,
                interiorExitDirections,
                buildingInstanceId,
                storyCount,
                floorIndex))
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
