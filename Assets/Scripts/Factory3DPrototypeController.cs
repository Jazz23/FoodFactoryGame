// Builds the bounded two-floor 3D factory slice and bridges its disposable presentation to FactoryWorldState.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class Factory3DPrototypeController : MonoBehaviour
{
    [SerializeField, Min(1f)] private float floorHeight = 3f;
    [SerializeField] private Vector2Int interiorSize = new(8, 6);
    [SerializeField] private uint buildingInstanceId = 9001u;
    [SerializeField, Min(0.1f)] private float playerSpeed = 4f;
    [SerializeField] private Camera prototypeCamera;

    private readonly Dictionary<(int Floor, uint EntityId), GameObject> entityViews = new();
    private readonly List<GameObject> floorViews = new();
    private FactoryWorldState worldState;
    private FactorySpatialAdapter spatialAdapter;
    private InputActionMap buildActions;
    private InputAction pointAction;
    private InputAction placeAction;
    private InputAction rotateAction;
    private Factory3DPrototypePlayer player;
    private int activeFloor;
    private Material floorMaterial;
    private Material wallMaterial;
    private Material machineMaterial;
    private Material conveyorMaterial;
    private Material elevatorMaterial;
    private Material playerMaterial;

    public FactoryWorldState WorldState => worldState;
    public Factory3DPrototypePlayer Player => player;
    public Camera PrototypeCamera => prototypeCamera;
    public FactorySpatialAdapter SpatialAdapter => spatialAdapter;
    public float StoryHeight => floorHeight * spatialAdapter.CellSize;
    public uint BuildingInstanceId => buildingInstanceId;
    public int ActiveFloor => activeFloor;
    public string LastInteraction { get; private set; } = string.Empty;
    public uint LastInteractedEntityId { get; private set; }
    public int LastInteractedProducedCount { get; private set; }
    public bool IsolatedSaveLoadPreservedTopology { get; private set; }

    private void Start()
    {
        BuildSlice();
    }

    private void Update()
    {
        if (worldState is null)
        {
            return;
        }

        worldState.AdvanceProduction(Time.deltaTime);
        RefreshEntityViews();
    }

    private void OnDestroy()
    {
        if (buildActions is not null)
        {
            buildActions.Disable();
            buildActions.Dispose();
        }

        if (pointAction is not null)
        {
            pointAction.Disable();
            pointAction.Dispose();
        }
    }

    private void BuildSlice()
    {
        var grid = GetComponent<SceneGrid>();
        if (grid is null)
        {
            grid = gameObject.AddComponent<SceneGrid>();
        }

        spatialAdapter = grid.CreateSpatialAdapter();
        worldState = CreateFixtureState();
        CreateMaterials();
        CreateFloors();
        CreatePlayer();
        ConfigureCamera();
        ConfigureInput();
        SetFloorVisibility();
        SaveAndReloadIsolatedFixture();
    }

    private FactoryWorldState CreateFixtureState()
    {
        var state = new FactoryWorldState(buildingInstanceId);
        if (!state.TryRegisterBuilding(buildingInstanceId, 2, interiorSize, out var registrationError))
        {
            Debug.LogError($"3D prototype fixture registration failed: {registrationError}");
            return state;
        }

        state.TryAddTestMachine(buildingInstanceId, 0, new Vector2(1.5f, 2.5f), out var machineId, out _);
        state.TryAddTestEntity(buildingInstanceId, 0, "conveyor-east", new Vector2(2.5f, 2.5f), out var firstConveyorId, out _);
        state.TryAddTestEntity(buildingInstanceId, 0, "conveyor-east", new Vector2(3.5f, 2.5f), out var secondConveyorId, out _);
        state.TryAddTestProcessor(buildingInstanceId, 0, new Vector2(4.5f, 2.5f), out var processorId, out _);
        state.TryAddTestEntity(buildingInstanceId, 0, FactoryEntityDefinitions.ElevatorBottomDefinitionId, new Vector2(6.5f, 1.5f), out var bottomElevatorId, out _);
        state.TryAddTestEntity(buildingInstanceId, 1, FactoryEntityDefinitions.ElevatorTopDefinitionId, new Vector2(6.5f, 1.5f), out var topElevatorId, out _);
        state.TryAddConnection(
            new FactoryEntityEndpoint(buildingInstanceId, 0, machineId),
            new FactoryEntityEndpoint(buildingInstanceId, 0, firstConveyorId),
            out _);
        state.TryAddConnection(
            new FactoryEntityEndpoint(buildingInstanceId, 0, firstConveyorId),
            new FactoryEntityEndpoint(buildingInstanceId, 0, secondConveyorId),
            out _);
        state.TryAddConnection(
            new FactoryEntityEndpoint(buildingInstanceId, 0, secondConveyorId),
            new FactoryEntityEndpoint(buildingInstanceId, 0, processorId),
            out _);
        state.TryAddConnection(
            new FactoryEntityEndpoint(buildingInstanceId, 0, bottomElevatorId),
            new FactoryEntityEndpoint(buildingInstanceId, 1, topElevatorId),
            out _);
        state.MarkCurrentStateAsAuthoritative();
        return state;
    }

    private void CreateMaterials()
    {
        floorMaterial = CreateMaterial(new Color(0.18f, 0.22f, 0.28f));
        wallMaterial = CreateMaterial(new Color(0.08f, 0.11f, 0.16f));
        machineMaterial = CreateMaterial(new Color(0.12f, 0.55f, 0.9f));
        conveyorMaterial = CreateMaterial(new Color(0.95f, 0.55f, 0.12f));
        elevatorMaterial = CreateMaterial(new Color(0.65f, 0.25f, 0.85f));
        playerMaterial = CreateMaterial(new Color(0.2f, 0.9f, 0.45f));
    }

    private Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader is null)
        {
            shader = Shader.Find("Standard");
        }

        return new Material(shader)
        {
            color = color
        };
    }

    private void CreateFloors()
    {
        var cellSize = spatialAdapter.CellSize;
        for (var floorIndex = 0; floorIndex < 2; floorIndex++)
        {
            var floorRoot = new GameObject($"3D Floor {floorIndex}");
            floorRoot.transform.SetParent(transform, false);
            floorViews.Add(floorRoot);
            var elevation = GetFloorElevation(floorIndex);
            var floorCenter = LogicalToWorld(new Vector2(interiorSize.x * 0.5f, interiorSize.y * 0.5f), elevation - 0.15f * cellSize, floorIndex);
            CreatePrimitive(
                PrimitiveType.Cube,
                $"Floor Surface {floorIndex}",
                floorRoot.transform,
                floorCenter,
                new Vector3(interiorSize.x * cellSize, 0.3f * cellSize, interiorSize.y * cellSize),
                floorMaterial);

            var wallHeight = 2f * cellSize;
            var wallThickness = 0.2f * cellSize;
            CreateWall(floorRoot.transform, $"North Wall {floorIndex}", new Vector2(interiorSize.x * 0.5f, interiorSize.y), new Vector3(interiorSize.x * cellSize, wallHeight, wallThickness), elevation, floorIndex);
            CreateWall(floorRoot.transform, $"West Wall {floorIndex}", new Vector2(0f, interiorSize.y * 0.5f), new Vector3(wallThickness, wallHeight, interiorSize.y * cellSize), elevation, floorIndex);
            CreateWall(floorRoot.transform, $"East Wall {floorIndex}", new Vector2(interiorSize.x, interiorSize.y * 0.5f), new Vector3(wallThickness, wallHeight, interiorSize.y * cellSize), elevation, floorIndex);

            var doorStart = Mathf.Min(1.5f, interiorSize.x * 0.25f);
            var doorWidth = Mathf.Min(2f, Mathf.Max(0.5f, interiorSize.x - doorStart - 0.5f));
            var rightWidth = interiorSize.x - doorStart - doorWidth;
            if (doorStart > 0.05f)
            {
                CreateWall(floorRoot.transform, $"South Door West {floorIndex}", new Vector2(doorStart * 0.5f, 0f), new Vector3(doorStart * cellSize, wallHeight, wallThickness), elevation, floorIndex);
            }
            if (rightWidth > 0.05f)
            {
                CreateWall(floorRoot.transform, $"South Door East {floorIndex}", new Vector2(doorStart + doorWidth + rightWidth * 0.5f, 0f), new Vector3(rightWidth * cellSize, wallHeight, wallThickness), elevation, floorIndex);
            }

            var elevatorPosition = LogicalToWorld(new Vector2(6.5f, 1.5f), elevation, floorIndex);
            CreatePrimitive(
                PrimitiveType.Cylinder,
                $"Elevator Shaft Marker {floorIndex}",
                floorRoot.transform,
                elevatorPosition + Vector3.up * (1.1f * cellSize),
                new Vector3(0.9f * cellSize, 1.1f * cellSize, 0.9f * cellSize),
                elevatorMaterial,
                false);
            RenderFloorEntities(floorIndex);
        }
    }

    private void CreateWall(Transform parent, string name, Vector2 logicalPosition, Vector3 scale, float elevation, int floorIndex)
    {
        CreatePrimitive(
            PrimitiveType.Cube,
            name,
            parent,
            LogicalToWorld(logicalPosition, elevation + scale.y * 0.5f, floorIndex),
            scale,
            wallMaterial);
    }

    private void CreatePlayer()
    {
        var cellSize = spatialAdapter.CellSize;
        var start = LogicalToWorld(new Vector2(0.5f, 0.5f), GetFloorElevation(0), 0);
        var playerObject = new GameObject("3D Prototype Player");
        playerObject.transform.SetParent(transform, false);
        playerObject.transform.position = start;
        var playerHeight = 1.8f * cellSize;
        var playerVisual = CreatePrimitive(
            PrimitiveType.Capsule,
            "Player Visual",
            playerObject.transform,
            Vector3.zero,
            new Vector3(0.7f * cellSize, 0.9f * cellSize, 0.7f * cellSize),
            playerMaterial,
            false);
        playerVisual.transform.localPosition = Vector3.up * (playerHeight * 0.5f);

        var characterController = playerObject.AddComponent<CharacterController>();
        characterController.height = playerHeight;
        characterController.radius = 0.35f * cellSize;
        characterController.center = Vector3.up * (playerHeight * 0.5f);
        player = playerObject.AddComponent<Factory3DPrototypePlayer>();
        player.Configure(playerSpeed * cellSize, 0, this);
    }

    private void ConfigureCamera()
    {
        if (prototypeCamera is null)
        {
            prototypeCamera = Camera.main;
        }

        if (prototypeCamera is null)
        {
            var cameraObject = new GameObject("3D Prototype Camera");
            prototypeCamera = cameraObject.AddComponent<Camera>();
        }

        var center = LogicalToWorld(
            new Vector2(interiorSize.x * 0.5f, interiorSize.y * 0.5f),
            GetFloorElevation(activeFloor) + floorHeight * spatialAdapter.CellSize * 0.5f,
            0);
        var size = new Vector2(interiorSize.x, interiorSize.y) * spatialAdapter.CellSize;
        prototypeCamera.transform.position = center + new Vector3(size.x * 0.8f, floorHeight * spatialAdapter.CellSize * 2.4f, -size.y * 1.2f);
        prototypeCamera.transform.LookAt(center);
        prototypeCamera.orthographic = true;
        prototypeCamera.orthographicSize = Mathf.Max(
            spatialAdapter.OrthographicSize * spatialAdapter.CellSize,
            Mathf.Max(size.x, size.y) * 0.65f);
        prototypeCamera.nearClipPlane = 0.1f;
        prototypeCamera.farClipPlane = Mathf.Max(100f, floorHeight * spatialAdapter.CellSize * 20f);
    }

    private void ConfigureInput()
    {
        buildActions = InputSystem.actions.FindActionMap("Build", true).Clone();
        pointAction = InputSystem.actions.FindAction("UI/Point", true).Clone();
        placeAction = buildActions.FindAction("Place", true);
        rotateAction = buildActions.FindAction("Rotate", true);
        placeAction.performed += PlaceEntity;
        rotateAction.performed += CycleFloor;
        buildActions.Enable();
        pointAction.Enable();
    }

    private void PlaceEntity(InputAction.CallbackContext context)
    {
        var pointer = pointAction.ReadValue<Vector2>();
        var ray = prototypeCamera.ScreenPointToRay(pointer);
        var plane = new Plane(Vector3.up, new Vector3(0f, GetFloorElevation(activeFloor), 0f));
        if (!plane.Raycast(ray, out var distance))
        {
            return;
        }

        var hit = ray.GetPoint(distance);
        var location = spatialAdapter.WorldToLogical3D(hit, buildingInstanceId, activeFloor);
        var cell = new Vector2(Mathf.Floor(location.FloorPosition.x) + 0.5f, Mathf.Floor(location.FloorPosition.y) + 0.5f);
        if (!TryPlaceStorageAtLogicalPosition(cell, out var error)
            && !string.IsNullOrWhiteSpace(error))
        {
            Debug.Log($"3D prototype placement skipped: {error}");
        }
    }

    private void CycleFloor(InputAction.CallbackContext context)
    {
        CycleFloorForTest();
    }

    public void CycleFloorForTest()
    {
        var targetFloor = (activeFloor + 1) % floorViews.Count;
        var logicalPosition = spatialAdapter.WorldToLogical3D(
            player.transform.position,
            buildingInstanceId,
            activeFloor).FloorPosition;
        TransitionToFloor(targetFloor, ClampLogicalPosition(logicalPosition));
    }

    public bool InteractForTest()
    {
        if (player is null || worldState is null)
        {
            return false;
        }

        if (!worldState.TryGetFloorState(buildingInstanceId, activeFloor, out var floor))
        {
            return false;
        }

        var playerLogicalPosition = spatialAdapter.WorldToLogical3D(
            player.transform.position,
            buildingInstanceId,
            activeFloor).FloorPosition;
        foreach (var entity in floor.Entities)
        {
            if (entity is null || Vector2.Distance(playerLogicalPosition, entity.LogicalPosition) > 1.2f)
            {
                continue;
            }

            if (entity.IsElevator)
            {
                var targetFloor = (activeFloor + 1) % floorViews.Count;
                TransitionToFloor(targetFloor, entity.LogicalPosition);
                LastInteraction = $"Elevator:{targetFloor}";
                LastInteractedEntityId = entity.EntityId;
                LastInteractedProducedCount = entity.ProducedCount;
                return true;
            }
        }

        foreach (var entity in floor.Entities)
        {
            if (entity is null
                || !entity.IsProducingMachine
                || Vector2.Distance(playerLogicalPosition, entity.LogicalPosition) > 1.2f)
            {
                continue;
            }

            worldState.AdvanceProduction(1f);
            LastInteraction = $"Machine:{entity.EntityId}:{entity.ProducedCount}";
            LastInteractedEntityId = entity.EntityId;
            LastInteractedProducedCount = entity.ProducedCount;
            RefreshEntityViews();
            return true;
        }

        LastInteraction = "None";
        return false;
    }

    public string GetInteractionPrompt(Factory3DPrototypePlayer targetPlayer)
    {
        if (targetPlayer is null || worldState is null)
        {
            return string.Empty;
        }

        if (!worldState.TryGetFloorState(buildingInstanceId, targetPlayer.FloorIndex, out var floor))
        {
            return string.Empty;
        }

        var position = spatialAdapter.WorldToLogical3D(targetPlayer.transform.position, buildingInstanceId, targetPlayer.FloorIndex).FloorPosition;
        foreach (var entity in floor.Entities)
        {
            if (entity is not null && Vector2.Distance(position, entity.LogicalPosition) <= 1.2f)
            {
                return entity.IsElevator ? "Interact: elevator" : entity.IsProducingMachine ? "Interact: machine" : string.Empty;
            }
        }

        return string.Empty;
    }

    public bool TryPlaceStorageAtLogicalPosition(Vector2 logicalPosition, out string error)
    {
        if (worldState is null)
        {
            error = "The prototype fixture is not initialized.";
            return false;
        }

        var placed = worldState.TryAddTestStorage(buildingInstanceId, activeFloor, logicalPosition, out _, out error);
        if (placed)
        {
            RenderFloorEntities(activeFloor);
        }

        return placed;
    }

    public bool IsFloorVisible(int floorIndex)
    {
        return floorIndex >= 0 && floorIndex < floorViews.Count && floorViews[floorIndex].activeSelf;
    }

    public Vector3 LogicalToWorldPositionForTest(Vector2 logicalPosition, int floorIndex)
    {
        var resolvedFloorIndex = Mathf.Clamp(floorIndex, 0, floorViews.Count - 1);
        return LogicalToWorld(logicalPosition, GetFloorElevation(resolvedFloorIndex), resolvedFloorIndex);
    }

    public Bounds GetFloorBoundsForTest(int floorIndex)
    {
        var resolvedFloorIndex = Mathf.Clamp(floorIndex, 0, floorViews.Count - 1);
        var elevation = GetFloorElevation(resolvedFloorIndex);
        var minimum = LogicalToWorld(Vector2.zero, elevation, resolvedFloorIndex);
        var maximum = LogicalToWorld(new Vector2(interiorSize.x, interiorSize.y), elevation, resolvedFloorIndex);
        var center = (minimum + maximum) * 0.5f;
        return new Bounds(center, new Vector3(Mathf.Abs(maximum.x - minimum.x), 0f, Mathf.Abs(maximum.z - minimum.z)));
    }

    public void TeleportPlayer(Factory3DPrototypePlayer targetPlayer, Vector3 position, int floorIndex)
    {
        if (targetPlayer is null)
        {
            return;
        }

        activeFloor = Mathf.Clamp(floorIndex, 0, floorViews.Count - 1);
        SetFloorVisibility();
        targetPlayer.Teleport(position, activeFloor);
    }

    public void ConstrainPlayerToFloor(Factory3DPrototypePlayer targetPlayer)
    {
        var bounds = GetFloorBoundsForTest(activeFloor);
        var radius = targetPlayer.GetComponent<CharacterController>().radius;
        var position = targetPlayer.transform.position;
        position.x = Mathf.Clamp(position.x, bounds.min.x + radius, bounds.max.x - radius);
        position.z = Mathf.Clamp(position.z, bounds.min.z + radius, bounds.max.z - radius);
        targetPlayer.transform.position = position;
    }

    private void TransitionToFloor(int targetFloor, Vector2 logicalPosition)
    {
        activeFloor = Mathf.Clamp(targetFloor, 0, floorViews.Count - 1);
        SetFloorVisibility();
        ConfigureCamera();
        var destination = LogicalToWorld(ClampLogicalPosition(logicalPosition), GetFloorElevation(activeFloor), activeFloor);
        player.Teleport(destination, activeFloor);
    }

    private Vector2 ClampLogicalPosition(Vector2 position)
    {
        return new Vector2(
            Mathf.Clamp(position.x, 0.5f, Mathf.Max(0.5f, interiorSize.x - 0.5f)),
            Mathf.Clamp(position.y, 0.5f, Mathf.Max(0.5f, interiorSize.y - 0.5f)));
    }

    private void SetFloorVisibility()
    {
        for (var index = 0; index < floorViews.Count; index++)
        {
            floorViews[index].SetActive(index == activeFloor);
        }
    }

    private void RenderFloorEntities(int floorIndex)
    {
        if (!worldState.TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            return;
        }

        var present = new HashSet<uint>();
        foreach (var entity in floor.Entities)
        {
            if (entity is null)
            {
                continue;
            }

            present.Add(entity.EntityId);
            var key = (floorIndex, entity.EntityId);
            if (!entityViews.TryGetValue(key, out var view))
            {
                view = CreatePrimitive(
                    PrimitiveType.Cube,
                    $"Entity {entity.EntityId} ({entity.DefinitionId})",
                    floorViews[floorIndex].transform,
                    Vector3.zero,
                    GetEntityScale(entity.DefinitionId),
                    GetEntityMaterial(entity.DefinitionId),
                    false);
                entityViews.Add(key, view);
            }

            view.transform.position = LogicalToWorld(
                entity.LogicalPosition,
                GetFloorElevation(floorIndex) + GetEntityHeight(entity.DefinitionId),
                floorIndex);
        }

        var staleKeys = new List<(int Floor, uint EntityId)>();
        foreach (var pair in entityViews)
        {
            if (pair.Key.Floor == floorIndex && !present.Contains(pair.Key.EntityId))
            {
                Object.Destroy(pair.Value);
                staleKeys.Add(pair.Key);
            }
        }
        foreach (var key in staleKeys)
        {
            entityViews.Remove(key);
        }
    }

    private void RefreshEntityViews()
    {
        for (var floorIndex = 0; floorIndex < floorViews.Count; floorIndex++)
        {
            RenderFloorEntities(floorIndex);
        }
    }

    private GameObject CreatePrimitive(
        PrimitiveType type,
        string objectName,
        Transform parent,
        Vector3 position,
        Vector3 scale,
        Material material,
        bool collidable = true)
    {
        var result = GameObject.CreatePrimitive(type);
        result.name = objectName;
        result.transform.SetParent(parent, false);
        result.transform.position = position;
        result.transform.localScale = scale;
        result.GetComponent<Renderer>().sharedMaterial = material;
        var collider = result.GetComponent<Collider>();
        if (collider is not null)
        {
            collider.enabled = collidable;
        }
        return result;
    }

    private Material GetEntityMaterial(string definitionId)
    {
        if (definitionId.Contains("conveyor"))
        {
            return conveyorMaterial;
        }
        if (definitionId.Contains("elevator"))
        {
            return elevatorMaterial;
        }
        return machineMaterial;
    }

    private Vector3 GetEntityScale(string definitionId)
    {
        var cellSize = spatialAdapter.CellSize;
        if (definitionId.Contains("conveyor"))
        {
            return new Vector3(0.9f * cellSize, 0.25f * cellSize, 0.9f * cellSize);
        }
        if (definitionId.Contains("elevator"))
        {
            return new Vector3(cellSize, 0.15f * cellSize, cellSize);
        }
        return new Vector3(0.9f * cellSize, 0.8f * cellSize, 0.9f * cellSize);
    }

    private float GetEntityHeight(string definitionId)
    {
        return (definitionId.Contains("conveyor") || definitionId.Contains("elevator") ? 0.15f : 0.4f) * spatialAdapter.CellSize;
    }

    private float GetFloorElevation(int floorIndex)
    {
        return floorIndex * floorHeight * spatialAdapter.CellSize;
    }

    private Vector3 LogicalToWorld(Vector2 logicalPosition, float elevation, int floorIndex = -1)
    {
        var resolvedFloorIndex = floorIndex < 0 ? activeFloor : floorIndex;
        return spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, resolvedFloorIndex, logicalPosition),
            elevation);
    }

    private void SaveAndReloadIsolatedFixture()
    {
        var path = Path.Combine(Application.temporaryCachePath, "food-factory-3d-prototype.db");
        var captured = worldState.CaptureState();
        var store = new OutsideTestFloorSqliteStore();
        store.Save(path, worldState.BuildingRecords, captured.Floors, captured.Connections);
        var reloaded = new FactoryWorldState(buildingInstanceId);
        var loaded = reloaded.LoadFromFile(path);
        IsolatedSaveLoadPreservedTopology = loaded
            && reloaded.BuildingRecords.Count() == worldState.BuildingRecords.Count()
            && reloaded.FloorStates.Count() == worldState.FloorStates.Count()
            && reloaded.ConnectionCount == worldState.ConnectionCount;
        Debug.Log($"3D prototype isolated fixture save/load: {loaded}, path={path}, connections={reloaded.ConnectionCount}");
    }
}
