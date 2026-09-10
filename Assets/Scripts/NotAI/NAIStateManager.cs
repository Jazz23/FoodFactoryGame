// Owns the persistent factory world, NAI entity records, occupancy, simulation, and view bindings.
using System;
using System.Collections.Generic;
using System.IO;
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

        public bool TryAddTestMachine(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
            => factoryState.TryAddTestMachine(buildingInstanceId, floorIndex, position, out entityId, out error);

        public bool TryAddTestStorage(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
            => factoryState.TryAddTestStorage(buildingInstanceId, floorIndex, position, out entityId, out error);

        public bool TryAddTestProcessor(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
            => factoryState.TryAddTestProcessor(buildingInstanceId, floorIndex, position, out entityId, out error);

        public bool TryAddPackedStorage(uint buildingInstanceId, int floorIndex, Vector2 position, out uint entityId, out string error)
            => factoryState.TryAddPackedStorage(buildingInstanceId, floorIndex, position, out entityId, out error);

        public bool TryRemoveTestMachine(uint buildingInstanceId, int floorIndex, uint entityId, out string error)
            => factoryState.TryRemoveTestMachine(buildingInstanceId, floorIndex, entityId, out error);

        public bool TryDrainTestMachine(uint buildingInstanceId, int floorIndex, uint entityId, out int removed, out string error)
            => factoryState.TryDrainTestMachine(buildingInstanceId, floorIndex, entityId, out removed, out error);

        public bool TryAddConnection(FactoryEntityEndpoint source, FactoryEntityEndpoint destination, out string error)
            => factoryState.TryAddConnection(source, destination, out error);

        public bool TryRemoveConnectionForEndpoint(FactoryEntityEndpoint endpoint, out FactoryEntityConnectionRecord removedConnection, out string error)
            => factoryState.TryRemoveConnectionForEndpoint(endpoint, out removedConnection, out error);

        public bool TryRemoveConnectionForEndpoint(FactoryEntityEndpoint endpoint, FactoryEntityConnectionDirection direction, out FactoryEntityConnectionRecord removedConnection, out string error)
            => factoryState.TryRemoveConnectionForEndpoint(endpoint, direction, out removedConnection, out error);

        public bool TrySetFloorState(uint buildingInstanceId, int floorIndex, string label, float productionRate, Vector2 markerPosition)
            => factoryState.TrySetFloorState(buildingInstanceId, floorIndex, label, productionRate, markerPosition);

        public OutsideTestFloorSaveData CaptureState() => factoryState.CaptureState();

        public bool LoadState(OutsideTestFloorSaveData data) => factoryState.LoadState(data);

        public bool LoadState(OutsideTestFloorSaveData data, float doorCornerExclusionDistance)
            => factoryState.LoadState(data, doorCornerExclusionDistance);

        public bool ApplySnapshot(uint buildingInstanceId, int floorIndex, string label, float productionRate, float accumulatedProduction, Vector2 markerPosition, FactoryEntitySnapshot[] entitySnapshots)
            => factoryState.ApplySnapshot(buildingInstanceId, floorIndex, label, productionRate, accumulatedProduction, markerPosition, entitySnapshots);

        public bool TryGetBuildingInfo(uint buildingInstanceId, out OutsideTestBuildingInfo info)
            => factoryState.TryGetBuildingInfo(buildingInstanceId, out info);

        public bool TryGetBuildingRecord(uint buildingInstanceId, out BuildingRecord record)
            => factoryState.TryGetBuildingRecord(buildingInstanceId, out record);

        public uint GetNextBuildingId(IEnumerable<uint> additionalIds)
            => factoryState.GetNextBuildingId(additionalIds);

        public bool TryRegisterBuilding(BuildingRecord record, out string error)
            => factoryState.TryRegisterBuilding(record, out error);

        public bool RemoveBuildingAndFloors(uint buildingInstanceId)
            => factoryState.RemoveBuildingAndFloors(buildingInstanceId);
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
            if (InitializationStatus is not NAIStateInitializationStatus.Uninitialized)
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
            worldSnapshot = new FactoryWorldSnapshot();
            simulation = null!;
            worldStore = null!;
            worldDirty = false;
            viewsHydrated = false;
        }

        private void ApplyWorldSnapshot(FactoryWorldSnapshot snapshot)
        {
            var factoryData = new OutsideTestFloorSaveData();
            var legacyBuildingIds = new Dictionary<Guid, uint>();
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

                legacyBuildingIds[building.Guid] = legacyId;
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
                    entities.Add(new FactoryEntityRecord(
                        legacyEntityId,
                        entity.DefinitionId,
                        entity.LocalPosition,
                        entity.CycleRate,
                        entity.CycleProgress,
                        entity.ProducedCount,
                        entity.OutputCount,
                        entity.InputCount));
                }

                floorRecord.SetEntities(entities);
                factoryData.Floors.Add(floorRecord);
            }

            foreach (var connection in snapshot.Connections)
            {
                if (!legacyBuildingIds.TryGetValue(connection.Source.BuildingGuid, out var sourceBuildingId)
                    || !legacyBuildingIds.TryGetValue(connection.Destination.BuildingGuid, out var destinationBuildingId))
                {
                    continue;
                }

                factoryData.Connections.Add(new FactoryEntityConnectionRecord(
                    new FactoryEntityEndpoint(
                        sourceBuildingId,
                        connection.Source.FloorIndex,
                        ResolveLegacyEntityId(snapshot, connection.Source)),
                    new FactoryEntityEndpoint(
                        destinationBuildingId,
                        connection.Destination.FloorIndex,
                        ResolveLegacyEntityId(snapshot, connection.Destination))));
            }

            factoryData.Version = FactoryWorldState.CurrentSaveVersion;
            if (!factoryState.LoadState(factoryData))
            {
                throw new InvalidDataException("The unified factory snapshot failed factory-state validation.");
            }
        }

        private FactoryWorldSnapshot CaptureWorldSnapshot()
        {
            var result = new FactoryWorldSnapshot();
            var factoryData = factoryState.CaptureState();
            foreach (var building in factoryData.Buildings)
            {
                result.Buildings.Add(new FactoryWorldBuildingRecord(
                    FactoryGuidMigration.ForBuilding(building.BuildingInstanceId),
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
                var floorGuid = FactoryGuidMigration.ForFloor(
                    floor.BuildingInstanceId,
                    floor.FloorIndex);
                var worldFloor = new FactoryWorldFloorRecord(
                    floorGuid,
                    FactoryGuidMigration.ForBuilding(floor.BuildingInstanceId),
                    floor.FloorIndex,
                    floor.Label,
                    floor.ProductionRate,
                    floor.AccumulatedProduction,
                    floor.MarkerPosition);
                var worldEntities = new List<FactoryWorldEntityRecord>();
                foreach (var entity in floor.Entities)
                {
                    worldEntities.Add(new FactoryWorldEntityRecord(
                        FactoryGuidMigration.ForEntity(
                            floor.BuildingInstanceId,
                            floor.FloorIndex,
                            entity.EntityId),
                        floorGuid,
                        entity.DefinitionId,
                        entity.LogicalPosition,
                        0f,
                        Vector2.one,
                        Array.Empty<byte>(),
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
                    FactoryGuidMigration.ForConnection(connection.Source, connection.Destination),
                    source,
                    destination));
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

        private static uint ResolveLegacyEntityId(
            FactoryWorldSnapshot snapshot,
            FactoryWorldEndpoint endpoint)
        {
            foreach (var floor in snapshot.Floors)
            {
                if (floor.Guid != endpoint.FloorGuid)
                {
                    continue;
                }

                foreach (var entity in floor.Entities)
                {
                    if (entity.Guid == endpoint.EntityGuid)
                    {
                        return entity.LegacyEntityId;
                    }
                }
            }

            throw new InvalidDataException($"Entity endpoint {endpoint.EntityGuid:D} has no legacy mapping.");
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
