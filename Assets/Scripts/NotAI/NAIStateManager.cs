// Owns the persistent factory world, NAI entity records, occupancy, simulation, and view bindings.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NotAI
{
    public enum NAIStateInitializationStatus
    {
        Uninitialized,
        Loading,
        Ready,
        Failed,
        ShuttingDown
    }

    [DisallowMultipleComponent]
    public sealed class NAIStateManager : NetworkBehaviour
    {
        private readonly struct RuntimeEntityKey : IEquatable<RuntimeEntityKey>
        {
            public RuntimeEntityKey(
                uint newBuildingInstanceId,
                int newFloorIndex,
                uint newEntityId)
            {
                BuildingInstanceId = newBuildingInstanceId;
                FloorIndex = newFloorIndex;
                EntityId = newEntityId;
            }

            public uint BuildingInstanceId { get; }
            public int FloorIndex { get; }
            public uint EntityId { get; }

            public bool Equals(RuntimeEntityKey other)
            {
                return BuildingInstanceId == other.BuildingInstanceId
                    && FloorIndex == other.FloorIndex
                    && EntityId == other.EntityId;
            }

            public override bool Equals(object obj)
            {
                return obj is RuntimeEntityKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(BuildingInstanceId, FloorIndex, EntityId);
            }
        }

        private const string InsideTestDefinitionId = "nai-inside-test";

        [SerializeField] public List<GameObject> buildingPrefabs = new();
        [SerializeField] private string databasePath = string.Empty;
        [SerializeField] private string legacyOutsideTestPath = string.Empty;
        [SerializeField] private string legacyNaiWorldPath = string.Empty;

        private static NAIStateManager instance = null!;
        private static string configuredDatabasePath = string.Empty;

        private readonly Dictionary<Vector2, Guid> occupiedTiles = new();
        private readonly Dictionary<Guid, NAIBuildable> buildableViews = new();
        private readonly HashSet<Guid> viewGuids = new();
        private readonly Dictionary<Guid, GameObject> spawnedViews = new();
        private readonly Dictionary<Guid, int> definitionIndices = new();
        private readonly Dictionary<uint, Guid> factoryBuildingGuids = new();
        private readonly Dictionary<OutsideTestFloorKey, Guid> factoryFloorGuids = new();
        private readonly Dictionary<RuntimeEntityKey, Guid> factoryEntityGuids = new();
        private readonly FactoryWorldState factoryState =
            new(GameSceneManager.LegacyOutsideTestBuildingId);

        private FactoryWorldSnapshot worldSnapshot = new();
        private FactoryWorldSqliteStore worldStore = null!;
        private FactorySimulation simulation = null!;
        private bool acceptingMutations;
        private bool worldDirty;
        private bool viewsHydrated;
        private string initializationError = string.Empty;
        private string configuredLegacyOutsidePath = string.Empty;
        private string configuredLegacyNaiPath = string.Empty;

        public static NAIStateManager Instance => instance;
        public float SimulationRemainder => simulation.Remainder;
        public static IReadOnlyDictionary<Vector2, Guid> OccupiedTiles => instance.occupiedTiles;
        public static IReadOnlyDictionary<Guid, NAIBuildable> Buildables => instance.buildableViews;

        public NAIStateInitializationStatus InitializationStatus { get; private set; }
            = NAIStateInitializationStatus.Uninitialized;
        public string InitializationError => initializationError;
        public bool IsInitialized => InitializationStatus == NAIStateInitializationStatus.Ready;
        public bool IsAcceptingMutations => acceptingMutations;
        public string DatabasePath => worldStore is null ? GetDatabasePath() : worldStore.Path;
        public bool WorldDirty => worldDirty;
        public IEnumerable<OutsideTestFloorRecord> FloorStates => factoryState.FloorStates;
        public IEnumerable<OutsideTestBuildingInfo> Buildings => factoryState.Buildings;
        public IEnumerable<BuildingRecord> BuildingRecords => factoryState.BuildingRecords;
        public IReadOnlyList<FactoryEntityConnectionRecord> Connections => factoryState.Connections;

        public void MarkCurrentStateAsAuthoritative() => factoryState.MarkCurrentStateAsAuthoritative();

        public bool TryGetFloorState(uint buildingInstanceId, int floorIndex, out OutsideTestFloorRecord state)
            => factoryState.TryGetFloorState(buildingInstanceId, floorIndex, out state);

        public bool TryRelocateEntity(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId,
            Vector2 position,
            out string error)
            => factoryState.TryRelocateEntity(
                buildingInstanceId,
                floorIndex,
                entityId,
                position,
                out error);

        public bool TryAddTestMachine(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
        {
            var added = factoryState.TryAddTestMachine(
                buildingInstanceId,
                floorIndex,
                position,
                out entityId,
                out error);
            if (added)
            {
                EnsureFactoryEntityGuid(buildingInstanceId, floorIndex, entityId);
            }

            return added;
        }

        public bool TryAddTestEntity(
            uint buildingInstanceId,
            int floorIndex,
            string definitionId,
            Vector2 position,
            out uint entityId,
            out string error,
            GridEdgeDirection dockDirection = GridEdgeDirection.South)
        {
            var added = factoryState.TryAddTestEntity(
                buildingInstanceId,
                floorIndex,
                definitionId,
                position,
                out entityId,
                out error,
                dockDirection);
            if (added)
            {
                EnsureFactoryEntityGuid(buildingInstanceId, floorIndex, entityId);
            }

            return added;
        }

        public bool TryAddTestStorage(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
        {
            var added = factoryState.TryAddTestStorage(
                buildingInstanceId,
                floorIndex,
                position,
                out entityId,
                out error);
            if (added)
            {
                EnsureFactoryEntityGuid(buildingInstanceId, floorIndex, entityId);
            }

            return added;
        }

        public bool TryAddTestProcessor(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
        {
            var added = factoryState.TryAddTestProcessor(
                buildingInstanceId,
                floorIndex,
                position,
                out entityId,
                out error);
            if (added)
            {
                EnsureFactoryEntityGuid(buildingInstanceId, floorIndex, entityId);
            }

            return added;
        }

        public bool TryAddPackedStorage(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
        {
            var added = factoryState.TryAddPackedStorage(
                buildingInstanceId,
                floorIndex,
                position,
                out entityId,
                out error);
            if (added)
            {
                EnsureFactoryEntityGuid(buildingInstanceId, floorIndex, entityId);
            }

            return added;
        }

        public bool TryAddSendingTerminal(
            uint buildingInstanceId,
            int floorIndex,
            Vector2 position,
            out uint entityId,
            out string error)
        {
            return TryAddTestEntity(
                buildingInstanceId,
                floorIndex,
                FactoryEntityRecord.SendingTerminalDefinitionId,
                position,
                out entityId,
                out error);
        }

        public bool TryAddReceivingTerminal(
            uint buildingInstanceId,
            int floorIndex,
            Vector2 position,
            out uint entityId,
            out string error)
        {
            return TryAddTestEntity(
                buildingInstanceId,
                floorIndex,
                FactoryEntityRecord.ReceivingTerminalDefinitionId,
                position,
                out entityId,
                out error);
        }

        public bool TryAddExteriorDock(
            uint buildingInstanceId,
            string definitionId,
            Vector2 interiorPosition,
            GridEdgeDirection direction,
            out uint entityId,
            out string error)
        {
            var normalizedDefinitionId = string.IsNullOrWhiteSpace(definitionId)
                ? string.Empty
                : FactoryEntityDefinitions.NormalizeDefinitionId(definitionId.Trim());
            if (normalizedDefinitionId == FactoryEntityDefinitions.ShippingDockDefinitionId)
            {
                return TryAddShippingDock(
                    buildingInstanceId,
                    0,
                    interiorPosition,
                    direction,
                    out entityId,
                    out error);
            }

            if (normalizedDefinitionId == FactoryEntityDefinitions.ReceivingDockDefinitionId)
            {
                return TryAddReceivingDock(
                    buildingInstanceId,
                    0,
                    interiorPosition,
                    direction,
                    out entityId,
                    out error);
            }

            entityId = 0;
            error = "Unknown dock type.";
            return false;
        }

        public bool TryAddShippingDock(
            uint buildingInstanceId,
            int floorIndex,
            Vector2 position,
            GridEdgeDirection direction,
            out uint entityId,
            out string error)
        {
            return TryAddTestEntity(
                buildingInstanceId,
                floorIndex,
                FactoryEntityDefinitions.ShippingDockDefinitionId,
                position,
                out entityId,
                out error,
                direction);
        }

        public bool TryAddReceivingDock(
            uint buildingInstanceId,
            int floorIndex,
            Vector2 position,
            GridEdgeDirection direction,
            out uint entityId,
            out string error)
        {
            return TryAddTestEntity(
                buildingInstanceId,
                floorIndex,
                FactoryEntityDefinitions.ReceivingDockDefinitionId,
                position,
                out entityId,
                out error,
                direction);
        }

        public bool TryRemoveTestMachine(uint buildingInstanceId, int floorIndex, uint entityId, out string error)
        {
            return TryRemoveTestEntity(
                buildingInstanceId,
                floorIndex,
                entityId,
                out error);
        }

        public bool TryRemoveTestEntity(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId,
            out string error)
        {
            var removed = factoryState.TryRemoveTestEntity(
                buildingInstanceId,
                floorIndex,
                entityId,
                out error);
            if (removed)
            {
                factoryEntityGuids.Remove(new RuntimeEntityKey(
                    buildingInstanceId,
                    floorIndex,
                    entityId));
            }

            return removed;
        }

        public bool TryDrainTestMachine(uint buildingInstanceId, int floorIndex, uint entityId, out int removed, out string error)
            => TryDrainTestEntity(buildingInstanceId, floorIndex, entityId, out removed, out error);

        public bool TryDrainTestEntity(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId,
            out int removed,
            out string error)
            => factoryState.TryDrainTestEntity(buildingInstanceId, floorIndex, entityId, out removed, out error);

        public bool TryAddConnection(FactoryEntityEndpoint source, FactoryEntityEndpoint destination, out string error)
        {
            if (!TryResolveFactoryEndpoint(source, out var resolvedSource, out error)
                || !TryResolveFactoryEndpoint(destination, out var resolvedDestination, out error))
            {
                return false;
            }

            return factoryState.TryAddConnection(
                resolvedSource,
                resolvedDestination,
                out error);
        }

        public bool TryRemoveConnectionForEndpoint(FactoryEntityEndpoint endpoint, out FactoryEntityConnectionRecord removedConnection, out string error)
        {
            if (!TryResolveFactoryEndpoint(endpoint, out var resolvedEndpoint, out error))
            {
                removedConnection = null!;
                return false;
            }

            return factoryState.TryRemoveConnectionForEndpoint(
                resolvedEndpoint,
                out removedConnection,
                out error);
        }

        public bool TryRemoveConnectionForEndpoint(FactoryEntityEndpoint endpoint, FactoryEntityConnectionDirection direction, out FactoryEntityConnectionRecord removedConnection, out string error)
        {
            if (!TryResolveFactoryEndpoint(endpoint, out var resolvedEndpoint, out error))
            {
                removedConnection = null!;
                return false;
            }

            return factoryState.TryRemoveConnectionForEndpoint(
                resolvedEndpoint,
                direction,
                out removedConnection,
                out error);
        }

        public bool TryGetFactoryEntityEndpoint(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId,
            out FactoryEntityEndpoint endpoint)
        {
            return TryResolveFactoryEndpoint(
                new FactoryEntityEndpoint(buildingInstanceId, floorIndex, entityId),
                out endpoint,
                out _);
        }

        public bool TryCreateTruckRoute(
            FactoryEntityEndpoint source,
            FactoryEntityEndpoint destination,
            out Guid routeGuid,
            out string error)
        {
            routeGuid = Guid.Empty;
            if (!TryResolveFactoryEndpoint(source, out var resolvedSource, out error)
                || !TryResolveFactoryEndpoint(destination, out var resolvedDestination, out error))
            {
                return false;
            }

            if (!factoryState.TryCreateTruckRoute(
                    resolvedSource,
                    resolvedDestination,
                    out routeGuid,
                    out _,
                    out error))
            {
                return false;
            }

            worldDirty = true;
            return true;
        }

        public bool TryDeleteTruckRoute(Guid routeGuid, out string error)
        {
            if (!factoryState.TryRemoveTruckRoute(routeGuid, out error))
            {
                return false;
            }

            worldDirty = true;
            return true;
        }

        public List<FactoryTruckRouteRecord> GetTruckRoutes()
        {
            var result = new List<FactoryTruckRouteRecord>();
            foreach (var route in factoryState.TruckRoutes)
            {
                result.Add(route.Clone());
            }

            return result;
        }

        public List<FactoryTruckRecord> GetTrucks()
        {
            var result = new List<FactoryTruckRecord>();
            foreach (var truck in factoryState.Trucks)
            {
                result.Add(truck.Clone());
            }

            return result;
        }

        public List<FactoryTerminalListing> GetFactoryTerminalListings()
        {
            var result = new List<FactoryTerminalListing>();
            if (!IsInitialized)
            {
                return result;
            }

            foreach (var building in factoryState.BuildingRecords)
            {
                for (var floorIndex = 0; floorIndex < building.StoryCount; floorIndex++)
                {
                    if (!factoryState.TryGetFloorState(building.BuildingInstanceId, floorIndex, out var floor))
                    {
                        continue;
                    }

                    foreach (var entity in floor.Entities)
                    {
                        if (!entity.IsDock
                            || !TryResolveFactoryEndpoint(
                                new FactoryEntityEndpoint(building.BuildingInstanceId, floorIndex, entity.EntityId),
                                out var endpoint,
                                out _))
                        {
                            continue;
                        }

                        result.Add(new FactoryTerminalListing(
                            endpoint,
                            building.BuildingInstanceId,
                            floorIndex,
                            entity.EntityId,
                            entity.DefinitionId,
                            floor.Label));
                    }
                }
            }

            return result;
        }

        public bool TrySetFloorState(uint buildingInstanceId, int floorIndex, string label, float productionRate, Vector2 markerPosition)
            => factoryState.TrySetFloorState(buildingInstanceId, floorIndex, label, productionRate, markerPosition);

        public OutsideTestFloorSaveData CaptureState() => factoryState.CaptureState();

        public bool LoadState(OutsideTestFloorSaveData data) => factoryState.LoadState(data);

        public bool LoadState(OutsideTestFloorSaveData data, float doorCornerExclusionDistance)
            => factoryState.LoadState(data, doorCornerExclusionDistance);

        public bool ApplySnapshot(uint buildingInstanceId, int floorIndex, string label, float productionRate, float accumulatedProduction, Vector2 markerPosition, FactoryEntitySnapshot[] entitySnapshots)
        {
            if (buildingInstanceId == 2 && floorIndex == 0)
            {
                Debug.LogWarning($"Factory client snapshot marker={markerPosition} server={IsServerStarted}.", this);
            }

            return factoryState.ApplySnapshot(
                buildingInstanceId,
                floorIndex,
                label,
                productionRate,
                accumulatedProduction,
                markerPosition,
                entitySnapshots);
        }

        public bool TryGetBuildingInfo(uint buildingInstanceId, out OutsideTestBuildingInfo info)
            => factoryState.TryGetBuildingInfo(buildingInstanceId, out info);

        public bool TryGetBuildingRecord(uint buildingInstanceId, out BuildingRecord record)
            => factoryState.TryGetBuildingRecord(buildingInstanceId, out record);

        public uint GetNextBuildingId(IEnumerable<uint> additionalIds)
            => factoryState.GetNextBuildingId(additionalIds);

        public bool TryRegisterBuilding(BuildingRecord record, out string error)
        {
            var registered = factoryState.TryRegisterBuilding(record, out error);
            if (registered)
            {
                EnsureFactoryBuildingGuid(record.BuildingInstanceId);
                EnsureFactoryFloorGuids(record.BuildingInstanceId, record.StoryCount);
            }

            return registered;
        }

        public bool RemoveBuildingAndFloors(uint buildingInstanceId)
        {
            var removed = factoryState.RemoveBuildingAndFloors(buildingInstanceId);
            if (!removed)
            {
                return false;
            }

            factoryBuildingGuids.Remove(buildingInstanceId);
            var floorKeys = new List<OutsideTestFloorKey>();
            foreach (var key in factoryFloorGuids.Keys)
            {
                if (key.BuildingInstanceId == buildingInstanceId)
                {
                    floorKeys.Add(key);
                }
            }

            foreach (var key in floorKeys)
            {
                factoryFloorGuids.Remove(key);
            }

            var entityKeys = new List<RuntimeEntityKey>();
            foreach (var key in factoryEntityGuids.Keys)
            {
                if (key.BuildingInstanceId == buildingInstanceId)
                {
                    entityKeys.Add(key);
                }
            }

            foreach (var key in entityKeys)
            {
                factoryEntityGuids.Remove(key);
            }

            return true;
        }
        public bool LoadedFactoryBuildingRecords
        {
            get
            {
                foreach (var building in worldSnapshot.Buildings)
                {
                    if (!building.InteriorOnly)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public static void ConfigureDatabasePath(string path)
        {
            if (instance is not null && instance.InitializationStatus is not NAIStateInitializationStatus.Uninitialized)
            {
                return;
            }

            configuredDatabasePath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        }

        public bool ConfigureLegacyPaths(string outsidePath, string naiPath)
        {
            if (InitializationStatus is NAIStateInitializationStatus.Loading
                or NAIStateInitializationStatus.Ready)
            {
                return false;
            }

            configuredLegacyOutsidePath = outsidePath ?? string.Empty;
            configuredLegacyNaiPath = naiPath ?? string.Empty;
            return true;
        }

        public override void OnStartNetwork()
        {
            instance = this;
        }

        public override void OnStartServer()
        {
            instance = this;
            InitializeServerState();
        }

        public override void OnStopServer()
        {
            ShutdownState();
        }

        public override void OnStopNetwork()
        {
            if (instance == this)
            {
                instance = null!;
            }
        }

        private void Update()
        {
            if (!IsServerStarted || !IsInitialized)
            {
                return;
            }

            simulation.Advance(Time.deltaTime);
            worldDirty = true;
        }

        public bool EnsureInitialized()
        {
            if (IsInitialized)
            {
                return true;
            }

            if (InitializationStatus == NAIStateInitializationStatus.Failed)
            {
                return false;
            }

            InitializeServerState();
            return IsInitialized;
        }

        public bool SaveWorld()
        {
            if (!EnsureInitialized())
            {
                return false;
            }

            try
            {
                worldSnapshot = CaptureWorldSnapshot();
                worldStore.Save(worldSnapshot);
                worldDirty = false;
                return true;
            }
            catch (Exception exception)
            {
                initializationError = exception.Message;
                return false;
            }
        }

        public bool LoadWorld()
        {
            if (!EnsureInitialized() || !acceptingMutations)
            {
                return false;
            }

            try
            {
                var loadedSnapshot = worldStore.Load();
                ApplyWorldSnapshot(loadedSnapshot);
                worldDirty = false;
                viewsHydrated = false;
                return true;
            }
            catch (Exception exception)
            {
                initializationError = exception.Message;
                return false;
            }
        }

        public void AdvanceSimulation(float deltaTime)
        {
            if (!IsInitialized || !acceptingMutations)
            {
                return;
            }

            simulation.Advance(deltaTime);
            worldDirty = true;
        }

        public bool TryGetNaiEntity(Guid guid, out FactoryWorldEntityRecord record)
        {
            record = null!;
            if (!IsInitialized || guid == Guid.Empty)
            {
                return false;
            }

            var floor = GetInsideTestFloor();
            foreach (var entity in floor.Entities)
            {
                if (entity.Guid == guid)
                {
                    record = entity.Clone();
                    return true;
                }
            }

            return false;
        }

        public bool TryGetBuildableView(Guid guid, out NAIBuildable buildable)
        {
            return buildableViews.TryGetValue(guid, out buildable);
        }

        public void RegisterBuildableView(NAIBuildable buildable)
        {
            if (buildable is null || buildable.guid == Guid.Empty)
            {
                return;
            }

            buildableViews[buildable.guid] = buildable;
            viewGuids.Add(buildable.guid);
        }

        public void UnregisterBuildableView(Guid guid, NAIBuildable buildable)
        {
            if (buildableViews.TryGetValue(guid, out var existing) && existing == buildable)
            {
                buildableViews.Remove(guid);
            }

            viewGuids.Remove(guid);
        }

        public bool TryAcceptNaiPlacement(
            int definitionIndex,
            Vector3 position,
            Quaternion rotation,
            Vector2 footprint,
            out Guid guid,
            out string error)
        {
            guid = Guid.Empty;
            error = string.Empty;
            if (!EnsureInitialized() || !acceptingMutations)
            {
                error = "The factory world is still loading.";
                return false;
            }

            if (!TryGetDefinitionId(definitionIndex, out var definitionId))
            {
                error = $"NAI buildable definition index {definitionIndex} is not registered.";
                return false;
            }

            var cells = GetOccupiedCells(position, footprint, GetNaiCellSize());
            foreach (var cell in cells)
            {
                if (occupiedTiles.ContainsKey(cell))
                {
                    error = $"The NAI footprint is occupied at {cell}.";
                    return false;
                }
            }

            guid = Guid.NewGuid();
            var floor = GetInsideTestFloor();
            floor.SetEntities(AddEntity(
                floor.Entities,
                new FactoryWorldEntityRecord(
                    guid,
                    floor.Guid,
                    definitionId,
                    new Vector2(position.x, position.y),
                    rotation.eulerAngles.z,
                    footprint,
                    Array.Empty<byte>(),
                    0,
                    true)));
            RebuildNaiOccupancy();
            worldDirty = true;
            return true;
        }

        public bool TryRemoveNaiEntity(Guid guid, out string error)
        {
            error = string.Empty;
            if (!EnsureInitialized() || !acceptingMutations || guid == Guid.Empty)
            {
                error = "The factory world is not accepting NAI mutations.";
                return false;
            }

            var floor = GetInsideTestFloor();
            var retained = new List<FactoryWorldEntityRecord>();
            var removed = false;
            foreach (var entity in floor.Entities)
            {
                if (entity.Guid == guid)
                {
                    removed = true;
                    continue;
                }

                retained.Add(entity);
            }

            if (!removed)
            {
                error = $"NAI entity {guid:D} does not exist.";
                return false;
            }

            floor.SetEntities(retained);
            if (spawnedViews.TryGetValue(guid, out var view) && view)
            {
                Destroy(view);
                spawnedViews.Remove(guid);
            }

            buildableViews.Remove(guid);
            RebuildNaiOccupancy();
            worldDirty = true;
            return true;
        }

        public void HydrateInsideTestScene(Scene scene)
        {
            if (!EnsureInitialized() || !scene.IsValid() || !scene.isLoaded || viewsHydrated)
            {
                return;
            }

            var gridObject = GameObject.Find("Grid");
            var grid = gridObject is null ? null : gridObject.GetComponent<Grid>();
            var floor = GetInsideTestFloor();
            foreach (var entity in floor.Entities)
            {
                if (!entity.IsNaiEntity || entity.Guid == Guid.Empty || viewGuids.Contains(entity.Guid))
                {
                    continue;
                }

                if (!NAIBuildableDefinitionCatalog.TryGetLegacyIndex(entity.DefinitionId, out var definitionIndex)
                    || definitionIndex < 0
                    || definitionIndex >= buildingPrefabs.Count)
                {
                    continue;
                }

                var worldPosition = new Vector3(entity.LocalPosition.x, entity.LocalPosition.y, 0f);
                if (grid is not null)
                {
                    worldPosition = new Vector3(
                        entity.LocalPosition.x,
                        entity.LocalPosition.y,
                        0f);
                }

                var view = Instantiate(
                    buildingPrefabs[definitionIndex],
                    worldPosition,
                    Quaternion.Euler(0f, 0f, entity.RotationZ));
                var buildable = view.GetComponent<NAIBuildable>();
                buildable.guid = entity.Guid;
                buildable.buildableId = definitionIndex;
                buildable.State = entity.State;
                spawnedViews[entity.Guid] = view;
                RegisterBuildableView(buildable);
                if (IsServerStarted)
                {
                    ServerManager.Spawn(view);
                }
            }

            viewsHydrated = true;
        }

        public static NAIBuildablePredictiveSpawn SpawnBuilding(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation)
        {
            return Instantiate(prefab, position, rotation).GetComponent<NAIBuildablePredictiveSpawn>();
        }

        public static List<Vector2> GetOccupiedCells(
            Vector3 position,
            Vector2 size,
            Vector2 cellSize)
        {
            size.x = Mathf.RoundToInt(size.x * 1000f) / 1000f;
            size.y = Mathf.RoundToInt(size.y * 1000f) / 1000f;
            var cellsX = Mathf.CeilToInt(size.x / cellSize.x);
            var cellsY = Mathf.CeilToInt(size.y / cellSize.y);
            var result = new List<Vector2>(cellsX * cellsY);
            for (var x = 0; x < cellsX; x++)
            {
                for (var y = 0; y < cellsY; y++)
                {
                    result.Add(new Vector2(
                        Mathf.FloorToInt(position.x) + x * cellSize.x,
                        Mathf.FloorToInt(position.y) + y * cellSize.y));
                }
            }

            return result;
        }

        private void InitializeServerState()
        {
            if (InitializationStatus is NAIStateInitializationStatus.Loading
                or NAIStateInitializationStatus.Ready)
            {
                return;
            }

            InitializationStatus = NAIStateInitializationStatus.Loading;
            acceptingMutations = false;
            try
            {
                worldStore = new FactoryWorldSqliteStore(GetDatabasePath());
                if (worldStore.Exists)
                {
                    worldSnapshot = worldStore.Load();
                }
                else
                {
                    var migration = new FactoryWorldMigration(
                        GetLegacyOutsidePath(),
                        GetLegacyNaiPath());
                    worldSnapshot = migration.Import();
                }

                ApplyWorldSnapshot(worldSnapshot);
                simulation = new FactorySimulation(factoryState.AdvanceProduction);
                RebuildNaiOccupancy();
                acceptingMutations = true;
                InitializationStatus = NAIStateInitializationStatus.Ready;
                initializationError = string.Empty;
                GetInsideTestFloor();
                worldSnapshot = CaptureWorldSnapshot();
                worldStore.Save(worldSnapshot);
                worldDirty = false;
            }
            catch (Exception exception)
            {
                acceptingMutations = false;
                InitializationStatus = NAIStateInitializationStatus.Failed;
                initializationError = exception.Message;
                Debug.LogError($"NAIStateManager initialization failed: {exception}", this);
            }
        }

        private void ShutdownState()
        {
            if (InitializationStatus == NAIStateInitializationStatus.ShuttingDown)
            {
                return;
            }

            acceptingMutations = false;
            if (worldDirty && worldStore is not null)
            {
                try
                {
                    worldSnapshot = CaptureWorldSnapshot();
                    worldStore.Save(worldSnapshot);
                    worldDirty = false;
                }
                catch (Exception exception)
                {
                    initializationError = exception.Message;
                }
            }

            InitializationStatus = NAIStateInitializationStatus.ShuttingDown;

            foreach (var view in spawnedViews.Values)
            {
                if (view)
                {
                    Destroy(view);
                }
            }

            spawnedViews.Clear();
            buildableViews.Clear();
            viewGuids.Clear();
            occupiedTiles.Clear();
            factoryBuildingGuids.Clear();
            factoryFloorGuids.Clear();
            factoryEntityGuids.Clear();
            worldSnapshot = new FactoryWorldSnapshot();
            simulation = null!;
            worldStore = null!;
            worldDirty = false;
            viewsHydrated = false;
            InitializationStatus = NAIStateInitializationStatus.Uninitialized;
        }

        private void ApplyWorldSnapshot(FactoryWorldSnapshot snapshot)
        {
            if (!FactoryWorldValidation.TryValidate(snapshot, out var validationError))
            {
                throw new InvalidDataException(validationError);
            }

            var factoryData = new OutsideTestFloorSaveData();
            var legacyBuildingIds = new Dictionary<Guid, uint>();
            var loadedFactoryBuildingGuids = new Dictionary<uint, Guid>();
            var loadedFactoryFloorGuids = new Dictionary<OutsideTestFloorKey, Guid>();
            var loadedFactoryEntityGuids = new Dictionary<RuntimeEntityKey, Guid>();
            var legacyEntityIds = new Dictionary<Guid, uint>();
            foreach (var building in snapshot.Buildings)
            {
                if (building.InteriorOnly)
                {
                    continue;
                }

                var legacyId = building.LegacyBuildingId;
                if (legacyId == 0)
                {
                    legacyId = GetNextLegacyBuildingId(legacyBuildingIds.Values);
                }

                if (!legacyBuildingIds.TryAdd(building.Guid, legacyId)
                    || !loadedFactoryBuildingGuids.TryAdd(legacyId, building.Guid))
                {
                    throw new InvalidDataException(
                        $"Factory building {building.Guid:D} has a duplicate runtime building ID {legacyId}.");
                }

                factoryData.Buildings.Add(new BuildingRecord(
                    legacyId,
                    building.AnchorCell,
                    building.FootprintSize,
                    building.StoryCount,
                    building.Doors));
            }

            foreach (var floor in snapshot.Floors)
            {
                if (!legacyBuildingIds.TryGetValue(floor.BuildingGuid, out var legacyBuildingId))
                {
                    continue;
                }

                var floorKey = new OutsideTestFloorKey(
                    legacyBuildingId,
                    floor.FloorIndex);
                if (!loadedFactoryFloorGuids.TryAdd(floorKey, floor.Guid))
                {
                    throw new InvalidDataException(
                        $"Factory floor {floor.Guid:D} has a duplicate runtime floor identity.");
                }

                var floorRecord = new OutsideTestFloorRecord(
                    legacyBuildingId,
                    floor.FloorIndex,
                    floor.Label,
                    floor.ProductionRate,
                    floor.AccumulatedProduction,
                    floor.MarkerPosition);
                var entities = new List<FactoryEntityRecord>();
                foreach (var entity in floor.Entities)
                {
                    if (entity.IsNaiEntity)
                    {
                        continue;
                    }

                    var legacyEntityId = entity.LegacyEntityId == 0
                        ? GetNextLegacyEntityId(entities)
                        : entity.LegacyEntityId;
                    var entityKey = new RuntimeEntityKey(
                        legacyBuildingId,
                        floor.FloorIndex,
                        legacyEntityId);
                    if (!loadedFactoryEntityGuids.TryAdd(entityKey, entity.Guid)
                        || !legacyEntityIds.TryAdd(entity.Guid, legacyEntityId))
                    {
                        throw new InvalidDataException(
                            $"Factory entity {entity.Guid:D} has a duplicate runtime entity ID.");
                    }

                    entities.Add(new FactoryEntityRecord(
                        legacyEntityId,
                        entity.DefinitionId,
                        entity.LocalPosition,
                        entity.CycleRate,
                        entity.CycleProgress,
                        entity.ProducedCount,
                        entity.OutputCount,
                        entity.InputCount,
                        FactoryConveyor.IsConveyor(entity.DefinitionId) ? FactoryConveyorQueue.Decode(entity.State) : null,
                        FactoryDock.RotationToDirection(entity.RotationZ)));
                }

                floorRecord.SetEntities(entities);
                factoryData.Floors.Add(floorRecord);
            }

            foreach (var connection in snapshot.Connections)
            {
                var sourceResolved = TryResolveWorldEndpoint(
                    connection.Source,
                    snapshot,
                    legacyBuildingIds,
                    legacyEntityIds,
                    out var source,
                    out var sourceError);
                var destinationResolved = TryResolveWorldEndpoint(
                    connection.Destination,
                    snapshot,
                    legacyBuildingIds,
                    legacyEntityIds,
                    out var destination,
                    out var destinationError);
                if (!sourceResolved || !destinationResolved)
                {
                    throw new InvalidDataException(
                        string.IsNullOrEmpty(sourceError) ? destinationError : sourceError);
                }

                factoryData.Connections.Add(new FactoryEntityConnectionRecord(
                    connection.Guid,
                    source,
                    destination));
            }

            factoryData.Version = FactoryWorldState.CurrentSaveVersion;
            if (!factoryState.LoadState(factoryData))
            {
                throw new InvalidDataException("The unified factory snapshot failed factory-state validation.");
            }

            RestoreTruckRoutes(snapshot, legacyBuildingIds, legacyEntityIds);

            factoryBuildingGuids.Clear();
            foreach (var pair in loadedFactoryBuildingGuids)
            {
                factoryBuildingGuids.Add(pair.Key, pair.Value);
            }

            factoryFloorGuids.Clear();
            foreach (var pair in loadedFactoryFloorGuids)
            {
                factoryFloorGuids.Add(pair.Key, pair.Value);
            }

            factoryEntityGuids.Clear();
            foreach (var pair in loadedFactoryEntityGuids)
            {
                factoryEntityGuids.Add(pair.Key, pair.Value);
            }

            worldSnapshot = snapshot.Clone();
        }

        private void RestoreTruckRoutes(
            FactoryWorldSnapshot snapshot,
            IReadOnlyDictionary<Guid, uint> legacyBuildingIds,
            IReadOnlyDictionary<Guid, uint> legacyEntityIds)
        {
            var restoredRoutes = new List<FactoryTruckRouteRecord>();
            var restoredTrucks = new List<FactoryTruckRecord>();
            foreach (var route in snapshot.Routes)
            {
                FactoryWorldTruckRecord matchingTruck = null!;
                foreach (var candidate in snapshot.Trucks)
                {
                    if (candidate.RouteGuid == route.Guid)
                    {
                        matchingTruck = candidate;
                        break;
                    }
                }

                var source = ResolveTruckRouteEndpoint(
                    route.Source,
                    matchingTruck.State,
                    snapshot,
                    legacyBuildingIds,
                    legacyEntityIds);
                var destination = ResolveTruckRouteEndpoint(
                    route.Destination,
                    matchingTruck.State,
                    snapshot,
                    legacyBuildingIds,
                    legacyEntityIds);
                restoredRoutes.Add(new FactoryTruckRouteRecord(
                    route.Guid,
                    route.TruckGuid,
                    source,
                    destination,
                    route.ItemId,
                    route.CargoCapacity,
                    route.TransferRateItemsPerSecond,
                    route.OutboundTravelSeconds,
                    route.ReturnTravelSeconds,
                    route.PartialLoadDepartureWindowSeconds));
                restoredTrucks.Add(new FactoryTruckRecord(
                    matchingTruck.Guid,
                    route.Guid,
                    matchingTruck.State,
                    matchingTruck.CargoItemId,
                    matchingTruck.CargoCount,
                    matchingTruck.RemainingTravelSeconds,
                    matchingTruck.LoadingWindowProgress,
                    matchingTruck.BlockingReason));
            }

            if (restoredRoutes.Count == 0)
            {
                return;
            }

            if (!factoryState.TryRestoreTruckRoutes(restoredRoutes, restoredTrucks, out var error))
            {
                throw new InvalidDataException(error);
            }
        }

        private static FactoryEntityEndpoint ResolveTruckRouteEndpoint(
            FactoryWorldEndpoint endpoint,
            FactoryTruckState truckState,
            FactoryWorldSnapshot snapshot,
            IReadOnlyDictionary<Guid, uint> legacyBuildingIds,
            IReadOnlyDictionary<Guid, uint> legacyEntityIds)
        {
            if (TryResolveWorldEndpoint(
                    endpoint,
                    snapshot,
                    legacyBuildingIds,
                    legacyEntityIds,
                    out var resolved,
                    out _))
            {
                return resolved;
            }

            if (truckState == FactoryTruckState.Blocked)
            {
                return new FactoryEntityEndpoint(
                    endpoint.BuildingGuid,
                    endpoint.FloorGuid,
                    endpoint.EntityGuid,
                    endpoint.FloorIndex);
            }

            throw new InvalidDataException(
                $"Truck route endpoint {endpoint.EntityGuid:D} could not be restored for an active truck.");
        }

        private FactoryWorldSnapshot CaptureWorldSnapshot()
        {
            var result = new FactoryWorldSnapshot();
            var factoryData = factoryState.CaptureState();
            foreach (var building in factoryData.Buildings)
            {
                var buildingGuid = EnsureFactoryBuildingGuid(building.BuildingInstanceId);
                result.Buildings.Add(new FactoryWorldBuildingRecord(
                    buildingGuid,
                    "outside-test-building",
                    building.AnchorCell,
                    building.FootprintSize,
                    building.StoryCount,
                    false,
                    building.BuildingInstanceId,
                    building.Doors));
            }

            foreach (var floor in factoryData.Floors)
            {
                var buildingGuid = EnsureFactoryBuildingGuid(floor.BuildingInstanceId);
                var floorGuid = EnsureFactoryFloorGuid(
                    floor.BuildingInstanceId,
                    floor.FloorIndex);
                var worldFloor = new FactoryWorldFloorRecord(
                    floorGuid,
                    buildingGuid,
                    floor.FloorIndex,
                    floor.Label,
                    floor.ProductionRate,
                    floor.AccumulatedProduction,
                    floor.MarkerPosition);
                var worldEntities = new List<FactoryWorldEntityRecord>();
                foreach (var entity in floor.Entities)
                {
                    var entityGuid = EnsureFactoryEntityGuid(
                        floor.BuildingInstanceId,
                        floor.FloorIndex,
                        entity.EntityId);
                    worldEntities.Add(new FactoryWorldEntityRecord(
                        entityGuid,
                        floorGuid,
                        entity.DefinitionId,
                        entity.LogicalPosition,
                        entity.IsDock ? FactoryDock.DirectionToRotation(entity.DockDirection) : 0f,
                        Vector2.one,
                        entity.GetConveyorState(),
                        entity.EntityId,
                        false,
                        entity.CycleRate,
                        entity.CycleProgress,
                        entity.ProducedCount,
                        entity.OutputCount,
                        entity.InputCount));
                }

                worldFloor.SetEntities(worldEntities);
                result.Floors.Add(worldFloor);
            }

            foreach (var connection in factoryData.Connections)
            {
                var source = CreateWorldEndpoint(connection.Source);
                var destination = CreateWorldEndpoint(connection.Destination);
                result.Connections.Add(new FactoryWorldConnectionRecord(
                    connection.Guid == Guid.Empty
                        ? FactoryGuidMigration.ForConnection(connection.Source, connection.Destination)
                        : connection.Guid,
                    source,
                    destination));
            }

            foreach (var route in factoryState.TruckRoutes)
            {
                result.Routes.Add(new FactoryWorldTruckRouteRecord(
                    route.Guid,
                    route.TruckGuid,
                    CreateWorldEndpoint(route.Source),
                    CreateWorldEndpoint(route.Destination),
                    route.ItemId,
                    route.CargoCapacity,
                    route.TransferRateItemsPerSecond,
                    route.OutboundTravelSeconds,
                    route.ReturnTravelSeconds,
                    route.PartialLoadDepartureWindowSeconds));
                if (factoryState.TryGetTruck(route.TruckGuid, out var truck))
                {
                    result.Trucks.Add(new FactoryWorldTruckRecord(
                        truck.Guid,
                        truck.RouteGuid,
                        truck.State,
                        truck.CargoItemId,
                        truck.CargoCount,
                        truck.RemainingTravelSeconds,
                        truck.LoadingWindowProgress,
                        truck.BlockingReason));
                }
            }

            foreach (var mapping in worldSnapshot.MigrationMappings)
            {
                result.MigrationMappings.Add(new FactoryWorldMigrationMapping(
                    mapping.Kind,
                    mapping.LegacyKey,
                    mapping.Guid));
            }

            foreach (var building in worldSnapshot.Buildings)
            {
                if (!building.InteriorOnly)
                {
                    continue;
                }

                result.Buildings.Add(building.Clone());
            }

            foreach (var floor in worldSnapshot.Floors)
            {
                if (worldSnapshot.Buildings.Exists(
                        building => building.InteriorOnly
                            && building.Guid == floor.BuildingGuid))
                {
                    result.Floors.Add(floor.Clone());
                }
            }

            return result;
        }

        private FactoryWorldFloorRecord GetInsideTestFloor()
        {
            var buildingGuid = FactoryGuidMigration.ForInsideTestBuilding();
            var floorGuid = FactoryGuidMigration.ForInsideTestFloor();
            foreach (var floor in worldSnapshot.Floors)
            {
                if (floor.Guid == floorGuid)
                {
                    return floor;
                }
            }

            var building = new FactoryWorldBuildingRecord(
                buildingGuid,
                InsideTestDefinitionId,
                Vector3Int.zero,
                new Vector2Int(32, 32),
                1,
                true);
            var createdFloor = new FactoryWorldFloorRecord(
                floorGuid,
                buildingGuid,
                0,
                "InsideTest",
                0f,
                0f,
                new Vector2(0.5f, 0.5f));
            if (!worldSnapshot.Buildings.Exists(candidate => candidate.Guid == buildingGuid))
            {
                worldSnapshot.Buildings.Add(building);
            }

            worldSnapshot.Floors.Add(createdFloor);
            return createdFloor;
        }

        private void RebuildNaiOccupancy()
        {
            occupiedTiles.Clear();
            var floor = GetInsideTestFloor();
            var cellSize = GetNaiCellSize();
            foreach (var entity in floor.Entities)
            {
                if (!entity.IsNaiEntity || entity.Guid == Guid.Empty)
                {
                    continue;
                }

                foreach (var cell in GetOccupiedCells(
                             new Vector3(entity.LocalPosition.x, entity.LocalPosition.y, 0f),
                             entity.Footprint,
                             cellSize))
                {
                    occupiedTiles[cell] = entity.Guid;
                }
            }
        }

        private Vector2 GetNaiCellSize()
        {
            var grid = GameObject.Find("Grid");
            return grid is null ? Vector2.one : grid.GetComponent<Grid>().cellSize;
        }

        private bool TryGetDefinitionId(int index, out string definitionId)
        {
            if (index >= 0 && index < NAIBuildableDefinitionCatalog.Definitions.Count)
            {
                definitionId = NAIBuildableDefinitionCatalog.Definitions[index].Id;
                return true;
            }

            if (index >= 0 && index < buildingPrefabs.Count)
            {
                definitionId = $"nai-prefab-{index}";
                return true;
            }

            definitionId = string.Empty;
            return false;
        }

        private string GetDatabasePath()
        {
            return string.IsNullOrWhiteSpace(databasePath)
                ? string.IsNullOrWhiteSpace(configuredDatabasePath)
                    ? FactoryWorldPaths.GetDefaultDatabasePath()
                    : configuredDatabasePath
                : databasePath;
        }

        private string GetLegacyOutsidePath()
        {
            return string.IsNullOrWhiteSpace(legacyOutsideTestPath)
                ? string.IsNullOrWhiteSpace(configuredLegacyOutsidePath)
                    ? FactoryWorldPaths.GetLegacyOutsideTestPath()
                    : configuredLegacyOutsidePath
                : legacyOutsideTestPath;
        }

        private string GetLegacyNaiPath()
        {
            return string.IsNullOrWhiteSpace(legacyNaiWorldPath)
                ? string.IsNullOrWhiteSpace(configuredLegacyNaiPath)
                    ? FactoryWorldPaths.GetLegacyNaiWorldPath()
                    : configuredLegacyNaiPath
                : legacyNaiWorldPath;
        }

        private static List<FactoryWorldEntityRecord> AddEntity(
            IReadOnlyList<FactoryWorldEntityRecord> entities,
            FactoryWorldEntityRecord entity)
        {
            var result = new List<FactoryWorldEntityRecord>(entities)
            {
                entity
            };
            return result;
        }

        private static uint GetNextLegacyBuildingId(IEnumerable<uint> existingIds)
        {
            var max = 0u;
            foreach (var id in existingIds)
            {
                max = Math.Max(max, id);
            }

            return max == uint.MaxValue ? 1u : max + 1u;
        }

        private static uint GetNextLegacyEntityId(IEnumerable<FactoryEntityRecord> entities)
        {
            var max = 0u;
            foreach (var entity in entities)
            {
                max = Math.Max(max, entity.EntityId);
            }

            return max == uint.MaxValue ? 1u : max + 1u;
        }

        private bool TryResolveFactoryEndpoint(
            FactoryEntityEndpoint requested,
            out FactoryEntityEndpoint resolved,
            out string error)
        {
            resolved = default;
            error = string.Empty;
            if (!IsInitialized)
            {
                error = "The factory world is not initialized.";
                return false;
            }

            var hasRuntimeIdentity = requested.BuildingInstanceId != 0
                && requested.FloorIndex >= 0
                && requested.EntityId != 0;
            if (hasRuntimeIdentity)
            {
                if (!factoryState.TryGetFloorState(
                        requested.BuildingInstanceId,
                        requested.FloorIndex,
                        out var floor)
                    || !floor.TryGetEntity(requested.EntityId, out _))
                {
                    error = "Connection endpoints must refer to existing entities.";
                    return false;
                }

                var expected = CreateRuntimeEndpoint(
                    requested.BuildingInstanceId,
                    requested.FloorIndex,
                    requested.EntityId);
                if (!IsLegacyEndpointIdentity(requested, expected))
                {
                    error = "The selected endpoint GUID does not match its authoritative record.";
                    return false;
                }

                resolved = expected;
                return true;
            }

            if (requested.BuildingGuid == Guid.Empty
                || requested.FloorGuid == Guid.Empty
                || requested.EntityGuid == Guid.Empty
                || requested.FloorIndex < 0)
            {
                error = "Connection endpoints must contain resolvable GUID and runtime identity.";
                return false;
            }

            foreach (var pair in factoryEntityGuids)
            {
                if (pair.Value != requested.EntityGuid)
                {
                    continue;
                }

                var expected = CreateRuntimeEndpoint(
                    pair.Key.BuildingInstanceId,
                    pair.Key.FloorIndex,
                    pair.Key.EntityId);
                if (requested.BuildingGuid != expected.BuildingGuid
                    || requested.FloorGuid != expected.FloorGuid
                    || requested.FloorIndex != expected.FloorIndex)
                {
                    error = "The selected endpoint GUID does not match its authoritative record.";
                    return false;
                }

                resolved = expected;
                return true;
            }

            error = $"Entity endpoint {requested.EntityGuid:D} does not exist.";
            return false;
        }

        private FactoryEntityEndpoint CreateRuntimeEndpoint(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId)
        {
            var buildingGuid = EnsureFactoryBuildingGuid(buildingInstanceId);
            var floorGuid = EnsureFactoryFloorGuid(buildingInstanceId, floorIndex);
            var entityGuid = EnsureFactoryEntityGuid(
                buildingInstanceId,
                floorIndex,
                entityId);
            return new FactoryEntityEndpoint(
                buildingInstanceId,
                floorIndex,
                entityId,
                buildingGuid,
                floorGuid,
                entityGuid);
        }

        private static bool IsLegacyEndpointIdentity(
            FactoryEntityEndpoint requested,
            FactoryEntityEndpoint expected)
        {
            return (requested.BuildingGuid == Guid.Empty
                    || requested.BuildingGuid == expected.BuildingGuid
                    || requested.BuildingGuid == FactoryGuidMigration.ForBuilding(
                        requested.BuildingInstanceId))
                && (requested.FloorGuid == Guid.Empty
                    || requested.FloorGuid == expected.FloorGuid
                    || requested.FloorGuid == FactoryGuidMigration.ForFloor(
                        requested.BuildingInstanceId,
                        requested.FloorIndex))
                && (requested.EntityGuid == Guid.Empty
                    || requested.EntityGuid == expected.EntityGuid
                    || requested.EntityGuid == FactoryGuidMigration.ForEntity(
                        requested.BuildingInstanceId,
                        requested.FloorIndex,
                        requested.EntityId));
        }

        private Guid EnsureFactoryBuildingGuid(uint buildingInstanceId)
        {
            if (buildingInstanceId == 0)
            {
                return Guid.Empty;
            }

            if (factoryBuildingGuids.TryGetValue(buildingInstanceId, out var existingGuid))
            {
                return existingGuid;
            }

            var newGuid = Guid.NewGuid();
            factoryBuildingGuids.Add(buildingInstanceId, newGuid);
            return newGuid;
        }

        private void EnsureFactoryFloorGuids(
            uint buildingInstanceId,
            int storyCount)
        {
            for (var floorIndex = 0; floorIndex < storyCount; floorIndex++)
            {
                EnsureFactoryFloorGuid(buildingInstanceId, floorIndex);
            }
        }

        private Guid EnsureFactoryFloorGuid(
            uint buildingInstanceId,
            int floorIndex)
        {
            var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
            if (factoryFloorGuids.TryGetValue(key, out var existingGuid))
            {
                return existingGuid;
            }

            var buildingGuid = EnsureFactoryBuildingGuid(buildingInstanceId);
            var newGuid = Guid.NewGuid();
            factoryFloorGuids.Add(key, newGuid);
            return newGuid;
        }

        private Guid EnsureFactoryEntityGuid(
            uint buildingInstanceId,
            int floorIndex,
            uint entityId)
        {
            var key = new RuntimeEntityKey(buildingInstanceId, floorIndex, entityId);
            if (factoryEntityGuids.TryGetValue(key, out var existingGuid))
            {
                return existingGuid;
            }

            var newGuid = Guid.NewGuid();
            factoryEntityGuids.Add(key, newGuid);
            return newGuid;
        }

        private static bool TryResolveWorldEndpoint(
            FactoryWorldEndpoint endpoint,
            FactoryWorldSnapshot snapshot,
            IReadOnlyDictionary<Guid, uint> legacyBuildingIds,
            IReadOnlyDictionary<Guid, uint> legacyEntityIds,
            out FactoryEntityEndpoint resolved,
            out string error)
        {
            resolved = default;
            error = string.Empty;
            if (!legacyBuildingIds.TryGetValue(
                    endpoint.BuildingGuid,
                    out var buildingId))
            {
                error = $"Building endpoint {endpoint.BuildingGuid:D} has no runtime mapping.";
                return false;
            }

            FactoryWorldFloorRecord matchingFloor = null!;
            foreach (var floor in snapshot.Floors)
            {
                if (floor.Guid == endpoint.FloorGuid)
                {
                    matchingFloor = floor;
                    break;
                }
            }

            if (matchingFloor is null
                || matchingFloor.BuildingGuid != endpoint.BuildingGuid
                || matchingFloor.FloorIndex != endpoint.FloorIndex)
            {
                error = $"Floor endpoint {endpoint.FloorGuid:D} has inconsistent building or index.";
                return false;
            }

            if (!legacyEntityIds.TryGetValue(endpoint.EntityGuid, out var entityId))
            {
                error = $"Entity endpoint {endpoint.EntityGuid:D} has no runtime mapping.";
                return false;
            }

            var entityFound = false;
            foreach (var entity in matchingFloor.Entities)
            {
                if (entity.Guid == endpoint.EntityGuid
                    && entity.FloorGuid == endpoint.FloorGuid
                    && !entity.IsNaiEntity)
                {
                    entityFound = true;
                    break;
                }
            }

            if (!entityFound)
            {
                error = $"Entity endpoint {endpoint.EntityGuid:D} does not belong to floor {endpoint.FloorGuid:D}.";
                return false;
            }

            resolved = new FactoryEntityEndpoint(
                buildingId,
                endpoint.FloorIndex,
                entityId,
                endpoint.BuildingGuid,
                endpoint.FloorGuid,
                endpoint.EntityGuid);
            return true;
        }

        private static FactoryWorldEndpoint CreateWorldEndpoint(FactoryEntityEndpoint endpoint)
        {
            return new FactoryWorldEndpoint(
                endpoint.BuildingGuid,
                endpoint.FloorGuid,
                endpoint.EntityGuid,
                endpoint.FloorIndex);
        }
    }
}
