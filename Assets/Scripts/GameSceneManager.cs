// Loads building-specific interior scenes and returns players to their source building.
using System;
using System.Collections.Generic;
using System.IO;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Utility.Extension;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;
using NotAI;
#if UNITY_6000_5_OR_NEWER
using SceneHandle = System.UInt64;
#else
using SceneHandle = System.Int32;
#endif

[RequireComponent(typeof(NetworkManager))]
public sealed class GameSceneManager : MonoBehaviour
{
    public const uint LegacyOutsideTestBuildingId = 1;
    public const string OutsideTestStateFileName = "outsidetest-floor-state.db";

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
        public uint Sequence;
        public Scene PreviousScene;
        public bool CreatesBuildingReturn;
        public bool ClearsBuildingReturn;
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
    [SerializeField] private NetworkObject stateManagerPrefab = null!;
    [SerializeField] private string worldSceneName = "World";
    [SerializeField] private string insideSceneName = "Inside";
    [SerializeField] private string outsideTestStatePath = string.Empty;

    private readonly HashSet<int> awaitingInitialSpawn = new();
    private readonly Dictionary<int, NetworkObject> players = new();
    private readonly Dictionary<int, PendingTransition> pendingTransitions = new();
    private readonly Dictionary<int, BuildingReturn> buildingReturns = new();
    private readonly OutsideTestFloorInstanceRegistry outsideTestFloorInstances = new();
    private readonly HashSet<uint> duplicateOutsideTestBuildingWarnings = new();
    private readonly HashSet<SceneHandle> unloadingOutsideTestFloorScenes = new();
    private NetworkManager networkManager = null!;
    private NAIStateManager stateManager = null!;
    private bool outsideTestStateLoaded;
    private bool outsideTestStateLoadedFromDisk;
    private bool outsideTestStateLoadFailed;
    private bool outsideTestStateNeedsSave;
    private FactoryTruckMarkerView truckMarkerView = null!;
    private SceneHandle registeredOutsideTestSceneHandle;
    private bool outsideTestWorldReconciled;
    private int clientOutsideTestLoadedInteriorCount;
    private float nextOutsideTestBroadcastTime;
    private uint nextTransitionSequence;
    private string lastOutsideTestError = string.Empty;

    public static GameSceneManager Instance = null!;

    public int OutsideTestLoadedInteriorCount => networkManager.IsServerStarted
        ? OutsideTestFloorPresentation.LoadedCount
        : clientOutsideTestLoadedInteriorCount;

    public string OutsideTestStatePath => GetOutsideTestStatePath();
    public string LastOutsideTestError => lastOutsideTestError;
    public NAIStateManager StateManager => stateManager;

    public bool TryGetOutsideTestFloorScene(
        uint buildingInstanceId,
        int floorIndex,
        out Scene scene)
    {
        if (outsideTestFloorInstances.TryGetLoaded(
                new OutsideTestFloorKey(buildingInstanceId, floorIndex),
                out var instance))
        {
            scene = instance.Scene;
            return true;
        }

        scene = default;
        return false;
    }

    public static string GetDefaultOutsideTestStatePath()
    {
        return Path.Combine(Application.persistentDataPath, OutsideTestStateFileName);
    }

    public bool ConfigureOutsideTestStatePath(string path)
    {
        if (networkManager.IsServerStarted)
        {
            return false;
        }

        EnsureStateManagerReference();

        outsideTestStatePath = string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path;
        outsideTestStateLoadedFromDisk = false;
        outsideTestStateLoaded = false;
        outsideTestWorldReconciled = false;
        outsideTestStateLoadFailed = false;
        outsideTestStateNeedsSave = false;
        outsideTestFloorInstances.Clear();
        registeredOutsideTestSceneHandle = default;
        lastOutsideTestError = string.Empty;
        if (!stateManager.ConfigureLegacyPaths(
                outsideTestStatePath,
                FactoryWorldPaths.GetLegacyNaiWorldPath()))
        {
            return false;
        }

        NAIStateManager.ConfigureDatabasePath(outsideTestStatePath);
        return true;
    }

    private void Awake()
    {
        Instance = this;
        networkManager = GetComponent<NetworkManager>();
        stateManager = FindFirstObjectByType<NAIStateManager>(FindObjectsInactive.Include);
        EnsureStateManagerReference();
    }

    private void OnEnable()
    {
        networkManager.ServerManager.OnAuthenticationResult += AuthenticationResult;
        networkManager.ServerManager.OnServerConnectionState += ServerConnectionStateChanged;
        networkManager.ServerManager.OnRemoteConnectionState += RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd += SceneLoadEnd;
        networkManager.SceneManager.OnUnloadEnd += SceneUnloadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd += ClientPresenceChangeEnd;
    }

    private void OnDisable()
    {
        networkManager.ServerManager.OnAuthenticationResult -= AuthenticationResult;
        networkManager.ServerManager.OnServerConnectionState -= ServerConnectionStateChanged;
        networkManager.ServerManager.OnRemoteConnectionState -= RemoteConnectionStateChanged;
        networkManager.SceneManager.OnLoadEnd -= SceneLoadEnd;
        networkManager.SceneManager.OnUnloadEnd -= SceneUnloadEnd;
        networkManager.SceneManager.OnClientPresenceChangeEnd -= ClientPresenceChangeEnd;

        outsideTestFloorInstances.Clear();
    }

    private void OnApplicationQuit()
    {
        outsideTestFloorInstances.Clear();
    }

    private void Update()
    {
        EnsureOutsideTestStateLoaded();
        EnsureTruckMarkerView();

        if (!networkManager.IsServerStarted)
        {
            return;
        }

        if (Time.unscaledTime < nextOutsideTestBroadcastTime)
        {
            return;
        }

        nextOutsideTestBroadcastTime = Time.unscaledTime + OutsideTestBroadcastInterval;
        BroadcastOutsideTestFloorStates();
    }

    private void EnsureTruckMarkerView()
    {
        if (!stateManager.IsInitialized)
        {
            return;
        }

        if (truckMarkerView is null || !truckMarkerView)
        {
            truckMarkerView = new GameObject("Factory Truck Markers").AddComponent<FactoryTruckMarkerView>();
        }

        if (SceneGrid.TryGetForScene(truckMarkerView.gameObject.scene, out _))
        {
            return;
        }

        for (var sceneIndex = 0; sceneIndex < UnitySceneManager.sceneCount; sceneIndex++)
        {
            var scene = UnitySceneManager.GetSceneAt(sceneIndex);
            if (scene.IsValid()
                && scene.isLoaded
                && SceneGrid.TryGetForScene(scene, out _))
            {
                UnitySceneManager.MoveGameObjectToScene(truckMarkerView.gameObject, scene);
                return;
            }
        }
    }

    public bool TryGetOutsideTestFloorState(
        uint buildingInstanceId,
        int floorIndex,
        out OutsideTestFloorRecord state)
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.TryGetFloorState(
            buildingInstanceId,
            floorIndex,
            out state);
    }

    public bool CanEditCurrentFloorMachines(PlayerSceneTransition player)
    {
        if (!networkManager.IsServerStarted
            || !networkManager.IsClientStarted
            || !player.IsOwner
            || player.Owner != networkManager.ClientManager.Connection
            || player.IsTransitioning
            || !player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex)
            || !IsValidOutsideTestFloor(buildingId, floorIndex))
        {
            return false;
        }

        if (!pendingTransitions.TryGetValue(player.Owner.ClientId, out var pendingTransition))
        {
            return true;
        }

        return pendingTransition.BuildingInstanceId == buildingId
            && pendingTransition.FloorIndex == floorIndex;
    }

    public bool CanEditCurrentFloorEntities(PlayerSceneTransition player)
    {
        return CanEditCurrentFloorMachines(player);
    }

    public bool TryPlaceCurrentFloorEquipment(PlayerSceneTransition player, string definitionId,
        Vector2 position, out uint entityId, out string error)
    {
        entityId = 0;
        error = "Only the host inside a factory floor can build; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player))
        {
            Debug.LogWarning(
                $"Placement rejected before state mutation: scene={player.gameObject.scene.name}, "
                + $"transitioning={player.IsTransitioning}, state={stateManager.InitializationStatus}, "
                + $"accepting={stateManager.IsAcceptingMutations}.",
                this);
            return false;
        }
        if (!FactoryConveyor.IsPlaceable(definitionId))
        {
            error = "Unknown equipment type.";
            return false;
        }
        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        stateManager.TryGetFloorState(buildingId, floorIndex, out var floor);
        stateManager.TryGetBuildingInfo(buildingId, out var buildingInfo);
        stateManager.TryGetBuildingRecord(buildingId, out var buildingRecord);
        Debug.LogWarning(
            $"Placement context: building={buildingId}, floor={floorIndex}, position={position}, "
            + $"footprint={buildingRecord.FootprintSize}, interior={buildingInfo.InteriorSize}.",
            this);
        if (FactoryConveyor.IsOccupied(floor.Entities, position))
        {
            error = "That cell is occupied.";
            return false;
        }
        if (!stateManager.TryAddTestEntity(buildingId, floorIndex, definitionId, position, out entityId, out error))
        {
            Debug.LogWarning(
                $"Placement rejected by world state: building={buildingId}, floor={floorIndex}, "
                + $"position={position}, error={error}, state={stateManager.InitializationStatus}, "
                + $"accepting={stateManager.IsAcceptingMutations}.",
                this);
            return false;
        }
        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryRelocateCurrentFloorEntity(
        PlayerSceneTransition player,
        uint entityId,
        Vector2 position,
        out string error)
    {
        error = "Only the host inside a factory floor can recover equipment; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player)
            || !player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex))
        {
            return false;
        }

        if (!stateManager.TryRelocateEntity(
                buildingId,
                floorIndex,
                entityId,
                position,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorMachine(PlayerSceneTransition player, Vector2 position,
        out uint entityId, out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit machines; wait for travel to finish.";
        if (!CanEditCurrentFloorMachines(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddTestMachine(buildingId, floorIndex, position,
                out entityId, out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorStorage(
        PlayerSceneTransition player,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit entities; wait for travel to finish.";
        var canEdit = CanEditCurrentFloorEntities(player);
        if (!canEdit)
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddTestStorage(
                buildingId,
                floorIndex,
                position,
                out entityId,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorProcessor(
        PlayerSceneTransition player,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit entities; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddTestProcessor(
                buildingId,
                floorIndex,
                position,
                out entityId,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorPackedStorage(
        PlayerSceneTransition player,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit entities; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddPackedStorage(
                buildingId,
                floorIndex,
                position,
                out entityId,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorSendingTerminal(
        PlayerSceneTransition player,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit entities; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddSendingTerminal(
                buildingId,
                floorIndex,
                position,
                out entityId,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryAddCurrentFloorReceivingTerminal(
        PlayerSceneTransition player,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        entityId = 0;
        error = "Only the local host inside a floor can edit entities; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryAddReceivingTerminal(
                buildingId,
                floorIndex,
                position,
                out entityId,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryRemoveCurrentFloorMachine(PlayerSceneTransition player, uint entityId,
        out string error)
    {
        error = "Only the local host inside a floor can edit machines; wait for travel to finish.";
        if (!CanEditCurrentFloorMachines(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryRemoveTestMachine(buildingId, floorIndex, entityId, out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryRemoveCurrentFloorEntity(
        PlayerSceneTransition player,
        uint entityId,
        out string error)
    {
        return TryRemoveCurrentFloorMachine(player, entityId, out error);
    }

    public bool TryDrainCurrentFloorMachine(
        PlayerSceneTransition player,
        uint entityId,
        out int removed,
        out string error)
    {
        removed = 0;
        error = "Only the local host inside a floor can edit machines; wait for travel to finish.";
        if (!CanEditCurrentFloorMachines(player))
        {
            return false;
        }

        player.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!stateManager.TryDrainTestMachine(
                buildingId,
                floorIndex,
                entityId,
                out removed,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryDrainCurrentFloorEntity(
        PlayerSceneTransition player,
        uint entityId,
        out int removed,
        out string error)
    {
        return TryDrainCurrentFloorMachine(
            player,
            entityId,
            out removed,
            out error);
    }

    public bool TryConnectCurrentFloorEntity(
        PlayerSceneTransition player,
        uint sourceEntityId,
        uint destinationBuildingInstanceId,
        int destinationFloorIndex,
        uint destinationEntityId,
        out string error)
    {
        error = "Only the local host inside a floor can edit connections; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player)
            || !player.TryGetCurrentOutsideTestFloor(
                out var buildingId,
                out var sourceFloorIndex))
        {
            return false;
        }

        var source = new FactoryEntityEndpoint(
            buildingId,
            sourceFloorIndex,
            sourceEntityId);
        var destination = new FactoryEntityEndpoint(
            destinationBuildingInstanceId,
            destinationFloorIndex,
            destinationEntityId);
        if (!stateManager.TryAddConnection(source, destination, out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingId, sourceFloorIndex);
        BroadcastOutsideTestFloorState(
            destinationBuildingInstanceId,
            destinationFloorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryGetOutsideTestEntityEndpoint(
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        out FactoryEntityEndpoint endpoint)
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.TryGetFactoryEntityEndpoint(
            buildingInstanceId,
            floorIndex,
            entityId,
            out endpoint);
    }

    public bool TryDisconnectCurrentFloorEntity(
        PlayerSceneTransition player,
        uint entityId,
        out string error)
    {
        error = "Only the local host inside a floor can edit connections; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player)
            || !player.TryGetCurrentOutsideTestFloor(
                out var buildingId,
                out var floorIndex))
        {
            return false;
        }

        var endpoint = new FactoryEntityEndpoint(buildingId, floorIndex, entityId);
        if (!stateManager.TryRemoveConnectionForEndpoint(
                endpoint,
                out var removedConnection,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(
            removedConnection.Source.BuildingInstanceId,
            removedConnection.Source.FloorIndex);
        BroadcastOutsideTestFloorState(
            removedConnection.Destination.BuildingInstanceId,
            removedConnection.Destination.FloorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryDisconnectCurrentFloorEntity(
        PlayerSceneTransition player,
        uint entityId,
        FactoryEntityConnectionDirection direction,
        out string error)
    {
        error = "Only the local host inside a floor can edit connections; wait for travel to finish.";
        if (!CanEditCurrentFloorEntities(player)
            || !player.TryGetCurrentOutsideTestFloor(
                out var buildingId,
                out var floorIndex))
        {
            return false;
        }

        var endpoint = new FactoryEntityEndpoint(buildingId, floorIndex, entityId);
        if (!stateManager.TryRemoveConnectionForEndpoint(
                endpoint,
                direction,
                out var removedConnection,
                out error))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(
            removedConnection.Source.BuildingInstanceId,
            removedConnection.Source.FloorIndex);
        BroadcastOutsideTestFloorState(
            removedConnection.Destination.BuildingInstanceId,
            removedConnection.Destination.FloorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool CanManageTruckRoutes(PlayerSceneTransition player)
    {
        return networkManager.IsServerStarted
            && networkManager.IsClientStarted
            && player.IsOwner
            && player.Owner == networkManager.ClientManager.Connection
            && !player.IsTransitioning;
    }

    public bool TryCreateTruckRoute(
        PlayerSceneTransition player,
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out Guid routeGuid,
        out string error)
    {
        routeGuid = Guid.Empty;
        error = "Only the host can manage truck routes.";
        if (!CanManageTruckRoutes(player))
        {
            return false;
        }

        EnsureOutsideTestStateLoaded();
        if (!stateManager.TryCreateTruckRoute(source, destination, out routeGuid, out error))
        {
            return false;
        }

        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool TryDeleteTruckRoute(
        PlayerSceneTransition player,
        Guid routeGuid,
        out string error)
    {
        error = "Only the host can manage truck routes.";
        if (!CanManageTruckRoutes(player))
        {
            return false;
        }

        EnsureOutsideTestStateLoaded();
        if (!stateManager.TryDeleteTruckRoute(routeGuid, out error))
        {
            return false;
        }

        outsideTestStateNeedsSave = true;
        return true;
    }

    public List<FactoryTerminalListing> GetFactoryTerminalListings()
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.GetFactoryTerminalListings();
    }

    public List<FactoryTruckRouteRecord> GetTruckRoutes()
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.GetTruckRoutes();
    }

    public List<FactoryTruckRecord> GetTrucks()
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.GetTrucks();
    }

    public bool TrySetOutsideTestFloorState(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        EnsureOutsideTestStateLoaded();
        if (!networkManager.IsServerStarted
            || !stateManager.TrySetFloorState(
                buildingInstanceId,
                floorIndex,
                label,
                productionRate,
                markerPosition))
        {
            return false;
        }

        BroadcastOutsideTestFloorState(buildingInstanceId, floorIndex);
        outsideTestStateNeedsSave = true;
        return true;
    }

    public bool SaveOutsideTestFloorState()
    {
        EnsureOutsideTestStateLoaded();
        if (outsideTestStateLoadFailed)
        {
            return false;
        }

        if (!stateManager.SaveWorld())
        {
            lastOutsideTestError = "Could not save OutsideTest building and floor state.";
            return false;
        }

        outsideTestStateNeedsSave = false;
        if (outsideTestWorldReconciled)
        {
            outsideTestStateLoadedFromDisk = true;
            stateManager.MarkCurrentStateAsAuthoritative();
        }
        outsideTestStateLoadFailed = false;
        lastOutsideTestError = string.Empty;
        return true;
    }

    public bool LoadOutsideTestFloorState()
    {
        if (!networkManager.IsServerStarted)
        {
            lastOutsideTestError = "Only the host can load OutsideTest world state.";
            return false;
        }

        EnsureOutsideTestStateLoaded();
        if (!CanLoadOutsideTestState(out var safetyError))
        {
            lastOutsideTestError = safetyError;
            return false;
        }

        var previousState = stateManager.CaptureState();
        var creator = FindOutsideTestCreator();
        var doorCornerExclusionDistance = creator is null || !creator
            ? TestBuildingCreator.DefaultDoorCornerExclusionDistance
            : creator.DoorCornerExclusionDistance;
        if (!stateManager.LoadWorld())
        {
            outsideTestStateLoadFailed = true;
            outsideTestStateNeedsSave = false;
            lastOutsideTestError = "Could not read OutsideTest building and floor state.";
            return false;
        }

        outsideTestStateLoaded = true;
        outsideTestStateLoadedFromDisk = true;
        outsideTestStateLoadFailed = false;
        outsideTestStateNeedsSave = false;
        outsideTestWorldReconciled = false;
        if (!ReconcileOutsideTestWorld(
                !stateManager.LoadedFactoryBuildingRecords,
                out var reconciliationError))
        {
            stateManager.LoadState(
                previousState,
                doorCornerExclusionDistance);
            outsideTestWorldReconciled = false;
            ReconcileOutsideTestWorld(
                false,
                out _);
            lastOutsideTestError = reconciliationError;
            return false;
        }

        nextOutsideTestBroadcastTime = 0f;
        BroadcastOutsideTestFloorStates();
        lastOutsideTestError = string.Empty;
        return true;
    }

    public void SendOutsideTestFloorSnapshot(PlayerSceneTransition player)
    {
        if (!networkManager.IsServerStarted)
        {
            return;
        }

        var loadedInteriorCount = OutsideTestLoadedInteriorCount;
        foreach (var floor in stateManager.FloorStates)
        {
            player.ServerSendOutsideTestFloorState(floor, loadedInteriorCount);
        }
    }

    public void ReceiveOutsideTestFloorState(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerPosition,
        FactoryEntitySnapshot[] entitySnapshots,
        int loadedInteriorCount)
    {
        if (networkManager.IsServerStarted)
        {
            clientOutsideTestLoadedInteriorCount = loadedInteriorCount;
            return;
        }

            stateManager.ApplySnapshot(
            buildingInstanceId,
            floorIndex,
            label,
            productionRate,
            accumulatedProduction,
            markerPosition,
            entitySnapshots);
        clientOutsideTestLoadedInteriorCount = loadedInteriorCount;
    }

    private void ServerConnectionStateChanged(ServerConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            EnsureStateManagerSpawned();
            EnsureOutsideTestStateLoaded();
            BroadcastOutsideTestFloorStates();
            return;
        }

        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            pendingTransitions.Clear();
            buildingReturns.Clear();
            players.Clear();
            awaitingInitialSpawn.Clear();
            outsideTestFloorInstances.Clear();
            unloadingOutsideTestFloorScenes.Clear();
        }
    }

    private void BroadcastOutsideTestFloorStates()
    {
        foreach (var floor in stateManager.FloorStates)
        {
            BroadcastOutsideTestFloorState(
                floor.BuildingInstanceId,
                floor.FloorIndex);
        }
    }

    private void BroadcastOutsideTestFloorState(
        uint buildingInstanceId,
        int floorIndex)
    {
        if (!stateManager.TryGetFloorState(
                buildingInstanceId,
                floorIndex,
                out var floor))
        {
            return;
        }

        var loadedInteriorCount = OutsideTestLoadedInteriorCount;
        foreach (var playerObject in players.Values)
        {
            if (playerObject is null || !playerObject)
            {
                continue;
            }

            var player = playerObject.GetComponent<PlayerSceneTransition>();
            if (player is null || !player)
            {
                continue;
            }

            player.ServerSendOutsideTestFloorState(floor, loadedInteriorCount);
        }
    }

    private void EnsureOutsideTestStateLoaded()
    {
        if (networkManager.IsServerStarted && !outsideTestStateLoadedFromDisk)
        {
            outsideTestStateLoadFailed = false;
            if (!stateManager.EnsureInitialized())
            {
                outsideTestStateLoadFailed = true;
                lastOutsideTestError = stateManager.InitializationError;
                Debug.LogWarning(lastOutsideTestError, this);
            }

            outsideTestStateLoadedFromDisk = true;
        }

        if (outsideTestStateLoadFailed)
        {
            return;
        }

        var worldScene = GetOutsideTestWorldScene();
        if (!worldScene.IsValid() || !worldScene.isLoaded)
        {
            return;
        }

        if (outsideTestWorldReconciled
            && worldScene.GetRawHandle() == registeredOutsideTestSceneHandle)
        {
            return;
        }

        var importSceneLayouts = !stateManager.LoadedFactoryBuildingRecords;
        if (!ReconcileOutsideTestWorld(
                importSceneLayouts,
                out var reconciliationError))
        {
            outsideTestStateLoadFailed = true;
            lastOutsideTestError = reconciliationError;
            Debug.LogError(reconciliationError, this);
            return;
        }

        stateManager.HydrateInsideTestScene(worldScene);

        outsideTestStateLoaded = true;
        outsideTestWorldReconciled = true;
        registeredOutsideTestSceneHandle = worldScene.GetRawHandle();
        if (networkManager.IsServerStarted && importSceneLayouts)
        {
            outsideTestStateNeedsSave = true;
        }
    }

    private void EnsureStateManagerSpawned()
    {
        if (!networkManager.IsServerStarted)
        {
            return;
        }

        EnsureStateManagerReference();
        var networkObject = stateManager.GetComponent<NetworkObject>();
        if (networkObject is null || networkObject.IsSpawned)
        {
            return;
        }

        networkManager.ServerManager.Spawn(networkObject);
    }

    private void EnsureStateManagerReference()
    {
        if (stateManager is not null && stateManager)
        {
            return;
        }

        stateManager = FindFirstObjectByType<NAIStateManager>(FindObjectsInactive.Include);
        if (stateManager is not null && stateManager)
        {
            return;
        }

        stateManager = Instantiate(stateManagerPrefab).GetComponent<NAIStateManager>();
    }

    public uint[] GetRegisteredOutsideTestBuildingIds()
    {
        EnsureOutsideTestStateLoaded();
        var result = new List<uint>();
        foreach (var building in stateManager.Buildings)
        {
            result.Add(building.BuildingInstanceId);
        }

        result.Sort();
        return result.ToArray();
    }

    public bool TryGetOutsideTestBuildingInfo(
        uint buildingInstanceId,
        out OutsideTestBuildingInfo info)
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.TryGetBuildingInfo(
            buildingInstanceId,
            out info);
    }

    public bool TryGetOutsideTestBuildingRecord(
        uint buildingInstanceId,
        out BuildingRecord record)
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.TryGetBuildingRecord(
            buildingInstanceId,
            out record);
    }

    public FactoryEntityConnectionRecord[] GetOutsideTestConnections()
    {
        EnsureOutsideTestStateLoaded();
        var result = new FactoryEntityConnectionRecord[stateManager.Connections.Count];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = stateManager.Connections[index].Clone();
        }

        return result;
    }

    public uint GetNextOutsideTestBuildingId()
    {
        EnsureOutsideTestStateLoaded();
        return stateManager.GetNextBuildingId(
            GetAuthoredOutsideTestBuildingIds());
    }

    public bool TryCreateBuilding(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        bool includeEntrance,
        out uint buildingInstanceId,
        out string error)
    {
        var doors = System.Array.Empty<BuildingRecord.DoorPlacement>();
        var entrance = (BuildingRecord.DoorPlacement)null!;
        if (includeEntrance
            && !TestBuildingCreator.TryGetDefaultEntrance(
                anchorCell,
                footprintSize,
                out entrance,
                out error))
        {
            buildingInstanceId = 0;
            lastOutsideTestError = error;
            return false;
        }

        if (includeEntrance)
        {
            doors = new[] { entrance };
        }

        return TryCreateBuilding(
            anchorCell,
            footprintSize,
            storyCount,
            doors,
            out buildingInstanceId,
            out error);
    }

    public bool TryCreateBuilding(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        IEnumerable<BuildingRecord.DoorPlacement> doorPlacements,
        out uint buildingInstanceId,
        out string error)
    {
        buildingInstanceId = 0;
        error = string.Empty;
        if (!networkManager.IsServerStarted)
        {
            error = "Only the host can create OutsideTest buildings.";
            lastOutsideTestError = error;
            return false;
        }

        EnsureOutsideTestStateLoaded();
        if (outsideTestStateLoadFailed)
        {
            error = lastOutsideTestError;
            return false;
        }

        var creator = FindOutsideTestCreator();
        if (creator is null || !creator)
        {
            error = $"World scene '{worldSceneName}' has no TestBuildingCreator.";
            lastOutsideTestError = error;
            return false;
        }

        buildingInstanceId = stateManager.GetNextBuildingId(
            GetAuthoredOutsideTestBuildingIds());
        if (buildingInstanceId == 0)
        {
            error = "No building IDs are available.";
            lastOutsideTestError = error;
            buildingInstanceId = 0;
            return false;
        }

        var record = new BuildingRecord(
            buildingInstanceId,
            anchorCell,
            footprintSize,
            storyCount,
            doorPlacements);
        if (!BuildingShellValidation.TryValidate(
                record,
                stateManager.BuildingRecords,
                creator.DoorCornerExclusionDistance,
                out error))
        {
            lastOutsideTestError = error;
            buildingInstanceId = 0;
            return false;
        }

        if (!stateManager.TryRegisterBuilding(record, out error))
        {
            if (string.IsNullOrEmpty(error))
            {
                error = $"Building {buildingInstanceId} is already registered.";
            }

            lastOutsideTestError = error;
            buildingInstanceId = 0;
            return false;
        }

        var assembler = new BuildingShellAssembler();
        var shell = assembler.CreateShell(
            record,
            creator,
            creator.GeneratedBuildings);
        if (shell is null || !shell)
        {
            stateManager.RemoveBuildingAndFloors(record.BuildingInstanceId);
            error = "The building shell could not be assembled; creation was rolled back.";
            lastOutsideTestError = error;
            buildingInstanceId = 0;
            return false;
        }

        outsideTestStateNeedsSave = true;
        outsideTestStateLoaded = true;
        outsideTestWorldReconciled = true;
        registeredOutsideTestSceneHandle = creator.gameObject.scene.GetRawHandle();
        lastOutsideTestError = string.Empty;
        BroadcastOutsideTestFloorStates();
        return true;
    }

    public bool TryCreateBuilding(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        out uint buildingInstanceId,
        out string error)
    {
        return TryCreateBuilding(
            anchorCell,
            footprintSize,
            storyCount,
            System.Array.Empty<BuildingRecord.DoorPlacement>(),
            out buildingInstanceId,
            out error);
    }

    public bool TryCreateBuilding(
        Vector2Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        IEnumerable<BuildingRecord.DoorPlacement> doorPlacements,
        out uint buildingInstanceId,
        out string error)
    {
        return TryCreateBuilding(
            new Vector3Int(anchorCell.x, anchorCell.y, 0),
            footprintSize,
            storyCount,
            doorPlacements,
            out buildingInstanceId,
            out error);
    }

    public bool TryCreateBuilding(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        IEnumerable<TestBuildingLayout.DoorPlacement> doorPlacements,
        out uint buildingInstanceId,
        out string error)
    {
        var convertedDoors = new List<BuildingRecord.DoorPlacement>();
        if (doorPlacements is not null)
        {
            foreach (var door in doorPlacements)
            {
                if (door is not null)
                {
                    convertedDoors.Add(new BuildingRecord.DoorPlacement(
                        door.WallId,
                        door.NormalizedOffset));
                }
            }
        }

        return TryCreateBuilding(
            anchorCell,
            footprintSize,
            storyCount,
            convertedDoors,
            out buildingInstanceId,
            out error);
    }

    public bool IsValidOutsideTestFloor(
        uint buildingInstanceId,
        int floorIndex)
    {
        return TryGetOutsideTestBuildingInfo(
                buildingInstanceId,
                out var info)
            && floorIndex >= 0
            && floorIndex < info.StoryCount;
    }

    private bool ReconcileOutsideTestWorld(
        bool importSceneLayouts,
        out string error)
    {
        error = string.Empty;
        var worldScene = GetOutsideTestWorldScene();
        if (!worldScene.IsValid() || !worldScene.isLoaded)
        {
            error = $"World scene '{worldSceneName}' is not loaded.";
            return false;
        }

        var creator = FindOutsideTestCreator();
        if (creator is null || !creator)
        {
            error = $"World scene '{worldSceneName}' has no TestBuildingCreator.";
            return false;
        }

        var layouts = creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true);
        var layoutsById = new Dictionary<uint, TestBuildingLayout>();
        foreach (var layout in layouts)
        {
            if (layout.BuildingInstanceId == 0)
            {
                continue;
            }

            if (layoutsById.ContainsKey(layout.BuildingInstanceId))
            {
                error = $"OutsideTest contains duplicate building ID {layout.BuildingInstanceId}; duplicate layout rejected.";
                return false;
            }

            layoutsById.Add(layout.BuildingInstanceId, layout);
        }

        var layoutRecords = new List<BuildingRecord>();
        foreach (var layout in layoutsById.Values)
        {
            layoutRecords.Add(layout.ExportBuildingRecord());
        }

        if (importSceneLayouts
            && !BuildingShellValidation.TryValidateRecords(
                layoutRecords,
                creator.DoorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        if (importSceneLayouts)
        {
            foreach (var record in layoutRecords)
            {
                if (stateManager.TryGetBuildingRecord(
                        record.BuildingInstanceId,
                        out var existingRecord))
                {
                    if (!existingRecord.HasSameTopology(record))
                    {
                        error = $"Duplicate building ID {record.BuildingInstanceId} has conflicting layout data.";
                        return false;
                    }

                    continue;
                }

                if (!stateManager.TryRegisterBuilding(record, out error))
                {
                    return false;
                }
            }
        }

        if (!BuildingShellValidation.TryValidateRecords(
                stateManager.BuildingRecords,
                creator.DoorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var assembler = new BuildingShellAssembler();
        var retainedLayoutIds = new HashSet<uint>();
        foreach (var record in stateManager.BuildingRecords)
        {
            if (layoutsById.TryGetValue(
                    record.BuildingInstanceId,
                    out var layout))
            {
                retainedLayoutIds.Add(record.BuildingInstanceId);
                var topologyChanged = !layout.ExportBuildingRecord().HasSameTopology(record);
                if (topologyChanged)
                {
                    layout.ApplyBuildingRecord(record);
                }

                var needsRebuild = topologyChanged
                    || BuildingShellAssembler.NeedsRebuild(
                        record,
                        creator,
                        layout.transform);
                if (needsRebuild
                    && !assembler.RebuildShell(record, creator, layout.transform))
                {
                    error = $"Could not rebuild shell for building {record.BuildingInstanceId}.";
                    return false;
                }

                continue;
            }

            var shell = assembler.CreateShell(
                record,
                creator,
                creator.GeneratedBuildings);
            if (shell is null || !shell)
            {
                error = $"Could not create shell for building {record.BuildingInstanceId}.";
                return false;
            }
        }

        if (!importSceneLayouts)
        {
            foreach (var layout in layoutsById.Values)
            {
                if (retainedLayoutIds.Contains(layout.BuildingInstanceId))
                {
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(layout.transform.gameObject);
            }
        }

        return true;
    }

    private TestBuildingCreator FindOutsideTestCreator()
    {
        var creators = FindObjectsByType<TestBuildingCreator>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var candidate in creators)
        {
            if (candidate.gameObject.scene.name == worldSceneName)
            {
                return candidate;
            }
        }

        return null!;
    }

    private Scene GetOutsideTestWorldScene()
    {
        var worldScene = UnitySceneManager.GetSceneByName(worldSceneName);
        if (worldScene.IsValid() && worldScene.isLoaded)
        {
            return worldScene;
        }

        var creator = FindOutsideTestCreator();
        return creator is null || !creator
            ? default
            : creator.gameObject.scene;
    }

    private IEnumerable<uint> GetAuthoredOutsideTestBuildingIds()
    {
        var creator = FindOutsideTestCreator();
        if (creator is null || !creator || creator.GeneratedBuildings is null || !creator.GeneratedBuildings)
        {
            yield break;
        }

        foreach (var layout in creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId != 0)
            {
                yield return layout.BuildingInstanceId;
            }
        }
    }

    private bool CanLoadOutsideTestState(out string error)
    {
        foreach (var pendingTransition in pendingTransitions.Values)
        {
            if (pendingTransition.Player is null || !pendingTransition.Player)
            {
                continue;
            }

            var pendingPlayer = pendingTransition.Player.GetComponent<PlayerSceneTransition>();
            if (pendingPlayer is not null
                && pendingPlayer
                && !pendingPlayer.IsTransitioning
                && pendingTransition.Player.gameObject.scene.name == worldSceneName)
            {
                continue;
            }

            error = "Cannot load OutsideTest state while a scene transition is pending.";
            return false;
        }

        foreach (var playerObject in players.Values)
        {
            if (playerObject is null || !playerObject)
            {
                continue;
            }

            var player = playerObject.GetComponent<PlayerSceneTransition>();
            if (player is null || !player)
            {
                continue;
            }

            if (player.IsTransitioning)
            {
                error = "Cannot load OutsideTest state while a player is transitioning.";
                return false;
            }

            if (player.TryGetCurrentOutsideTestFloor(out var buildingInstanceId, out var floorIndex))
            {
                error = $"Cannot load OutsideTest state while a player is inside building "
                    + $"{buildingInstanceId}, floor {floorIndex}.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private string GetOutsideTestStatePath()
    {
        return string.IsNullOrWhiteSpace(outsideTestStatePath)
            ? GetDefaultOutsideTestStatePath()
            : outsideTestStatePath;
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
        var clearsBuildingReturn = false;
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
            clearsBuildingReturn = true;
        }

        if (!TryPrepareOutsideTestFloorLoad(
            buildingInstanceId,
            floorIndex,
            targetSceneName,
            out var lookup,
            out var allowStacking))
        {
            return false;
        }
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
            FloorIndex = floorIndex,
            Sequence = ++nextTransitionSequence,
            PreviousScene = player.gameObject.scene,
            CreatesBuildingReturn = portal.BuildingInstanceId != 0,
            ClearsBuildingReturn = clearsBuildingReturn
        };
        var sceneLoadData = new SceneLoadData(new[] { lookup }, new[] { player })
        {
            ReplaceScenes = ReplaceOption.OnlineOnly,
            PreferredActiveScene = new PreferredScene(lookup, null!),
            Options = GetOutsideTestLoadOptions(allowStacking),
            Params = new LoadParams
            {
                ServerParams = new object[] { pendingTransition }
            }
        };

        pendingTransitions[connection.ClientId] = pendingTransition;
        player.GetComponent<PlayerSceneTransition>().ServerBeginTransition();
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

        if (!TryPrepareOutsideTestFloorLoad(
            buildingReturn.BuildingInstanceId,
            targetFloorIndex,
            TestBuildingFloorScenes.TemplateSceneName,
            out var lookup,
            out var allowStacking))
        {
            return false;
        }
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
            FloorIndex = targetFloorIndex,
            Sequence = ++nextTransitionSequence,
            PreviousScene = player.gameObject.scene
        };
        var sceneLoadData = new SceneLoadData(new[] { lookup }, new[] { player })
        {
            ReplaceScenes = ReplaceOption.OnlineOnly,
            PreferredActiveScene = new PreferredScene(lookup, null!),
            Options = GetOutsideTestLoadOptions(allowStacking),
            Params = new LoadParams
            {
                ServerParams = new object[] { pendingTransition }
            }
        };

        pendingTransitions[connection.ClientId] = pendingTransition;
        player.GetComponent<PlayerSceneTransition>().ServerBeginTransition();
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
        if (pendingTransitions.TryGetValue(connection.ClientId, out var pendingTransition)
            && pendingTransition.BuildingInstanceId != 0)
        {
            outsideTestFloorInstances.FailLoad(
                new OutsideTestFloorKey(
                    pendingTransition.BuildingInstanceId,
                    pendingTransition.FloorIndex));
        }
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
        if (!pendingTransition.TargetScene.IsValid()
            || !pendingTransition.TargetScene.isLoaded)
        {
            if (pendingTransition.BuildingInstanceId != 0)
            {
                outsideTestFloorInstances.FailLoad(
                    new OutsideTestFloorKey(
                        pendingTransition.BuildingInstanceId,
                        pendingTransition.FloorIndex));
            }

            pendingTransitions.Remove(pendingTransition.Connection.ClientId);
            if (pendingTransition.CreatesBuildingReturn)
            {
                buildingReturns.Remove(pendingTransition.Connection.ClientId);
            }
            return;
        }
        if (pendingTransition.BuildingInstanceId != 0
            && !outsideTestFloorInstances.RegisterLoaded(
                new OutsideTestFloorKey(
                    pendingTransition.BuildingInstanceId,
                    pendingTransition.FloorIndex),
                pendingTransition.TargetScene))
        {
            outsideTestFloorInstances.FailLoad(
                new OutsideTestFloorKey(
                    pendingTransition.BuildingInstanceId,
                    pendingTransition.FloorIndex));
            pendingTransitions.Remove(pendingTransition.Connection.ClientId);
            if (pendingTransition.CreatesBuildingReturn)
            {
                buildingReturns.Remove(pendingTransition.Connection.ClientId);
            }
            return;
        }
        if (pendingTransition.Player is null || !pendingTransition.Player)
        {
            pendingTransitions.Remove(pendingTransition.Connection.ClientId);
            return;
        }

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
            pendingTransitions.Remove(pendingTransition.Connection.ClientId);
            if (pendingTransition.CreatesBuildingReturn)
            {
                buildingReturns.Remove(pendingTransition.Connection.ClientId);
            }

            return;
        }

        var targetPosition = grid.LogicalToWorld(pendingTransition.ArrivalLogicalPosition);
        pendingTransition.Player.GetComponent<PlayerSceneTransition>().ServerTeleport(targetPosition);
    }

    private void ClientPresenceChangeEnd(ClientPresenceChangeEventArgs args)
    {
        if (!args.Added)
        {
            TryUnloadEmptyOutsideTestFloorScene(args.Scene, default);
            return;
        }

        if (awaitingInitialSpawn.Remove(args.Connection.ClientId) && args.Scene.name == worldSceneName)
        {
            SpawnInitialPlayer(args.Connection, args.Scene);
            return;
        }

        if (!pendingTransitions.TryGetValue(args.Connection.ClientId, out var pendingTransition)
            || args.Scene.GetRawHandle() != pendingTransition.TargetScene.GetRawHandle())
        {
            return;
        }

        if (pendingTransition.Player is null || !pendingTransition.Player)
        {
            pendingTransitions.Remove(args.Connection.ClientId);
            return;
        }

        if (!TryGetGrid(args.Scene, out var grid))
        {
            return;
        }

        var targetPosition = grid.LogicalToWorld(pendingTransition.ArrivalLogicalPosition);
        pendingTransitions.Remove(args.Connection.ClientId);
        if (pendingTransition.BuildingInstanceId != 0
            && buildingReturns.TryGetValue(
                args.Connection.ClientId,
                out var buildingReturn))
        {
            buildingReturn.CurrentFloor = pendingTransition.FloorIndex;
        }
        UnloadEmptyPreviousScene(pendingTransition.PreviousScene, pendingTransition.TargetScene);
        if (pendingTransition.ClearsBuildingReturn)
        {
            buildingReturns.Remove(args.Connection.ClientId);
        }
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
            pendingTransition.FloorIndex,
            pendingTransition.Sequence);
    }

    private void UnloadEmptyPreviousScene(Scene previousScene, Scene targetScene)
    {
        TryUnloadEmptyOutsideTestFloorScene(previousScene, targetScene);
    }

    private void TryUnloadEmptyOutsideTestFloorScene(Scene scene, Scene targetScene)
    {
        if (!scene.IsValid()
            || (targetScene.IsValid() && scene.GetRawHandle() == targetScene.GetRawHandle())
            || !outsideTestFloorInstances.TryGetKey(scene.GetRawHandle(), out _))
        {
            return;
        }

        if (unloadingOutsideTestFloorScenes.Contains(scene.GetRawHandle()))
        {
            return;
        }

        if (networkManager.SceneManager.SceneConnections.TryGetValue(
                scene,
                out var connections)
            && connections.Count != 0)
        {
            return;
        }

        unloadingOutsideTestFloorScenes.Add(scene.GetRawHandle());
        networkManager.SceneManager.UnloadConnectionScenes(
            System.Array.Empty<NetworkConnection>(),
            new SceneUnloadData(new SceneLookupData(scene)));
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
        if (portal.BuildingInstanceId != 0)
        {
            return TestBuildingFloorScenes.TemplateSceneName;
        }

        return string.IsNullOrWhiteSpace(portal.DestinationSceneName)
            ? GetSceneName(portal.Destination)
            : portal.DestinationSceneName;
    }

    private void SceneUnloadEnd(SceneUnloadEndEventArgs args)
    {
        if (!args.QueueData.AsServer)
        {
            return;
        }

        foreach (var scene in args.UnloadedScenesV2)
        {
            unloadingOutsideTestFloorScenes.Remove(scene.Handle);
            outsideTestFloorInstances.Remove(scene.Handle);
        }
    }

    private bool TryPrepareOutsideTestFloorLoad(
        uint buildingInstanceId,
        int floorIndex,
        string fallbackSceneName,
        out SceneLookupData lookup,
        out bool allowStacking)
    {
        allowStacking = false;
        if (buildingInstanceId == 0)
        {
            lookup = new SceneLookupData(fallbackSceneName);
            return true;
        }

        var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
        if (outsideTestFloorInstances.TryGetLoaded(key, out var instance))
        {
            lookup = new SceneLookupData(instance.Scene);
            return true;
        }

        if (!outsideTestFloorInstances.TryBeginLoad(key))
        {
            lookup = default;
            return false;
        }

        allowStacking = true;
        lookup = new SceneLookupData(fallbackSceneName);
        return true;
    }

    private static LoadOptions GetOutsideTestLoadOptions(bool allowStacking)
    {
        if (!allowStacking)
        {
            return new LoadOptions();
        }

        return new LoadOptions
        {
            AllowStacking = allowStacking,
            AutomaticallyUnload = allowStacking,
            LocalPhysics = LocalPhysicsMode.Physics2D
        };
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
