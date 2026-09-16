// Builds the reversible fixed-camera 3D factory slice and bridges it to FactoryWorldState.
using System.Collections.Generic;
using System.IO;
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

    private readonly Dictionary<uint, GameObject> entityViews = new();
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
    public int ActiveFloor => activeFloor;

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

        var material = new Material(shader)
        {
            color = color
        };
        return material;
    }

    private void CreateFloors()
    {
        for (var floorIndex = 0; floorIndex < 2; floorIndex++)
        {
            var floorRoot = new GameObject($"3D Floor {floorIndex}");
            floorRoot.transform.SetParent(transform, false);
            floorViews.Add(floorRoot);
            var elevation = GetFloorElevation(floorIndex);
            CreatePrimitive(
                PrimitiveType.Cube,
                $"Floor Surface {floorIndex}",
                floorRoot.transform,
                new Vector3(interiorSize.x * 0.5f, elevation - 0.15f, interiorSize.y * 0.5f),
                new Vector3(interiorSize.x, 0.3f, interiorSize.y),
                floorMaterial);
            CreatePrimitive(
                PrimitiveType.Cube,
                $"North Wall {floorIndex}",
                floorRoot.transform,
                new Vector3(interiorSize.x * 0.5f, elevation + 1f, interiorSize.y),
                new Vector3(interiorSize.x, 2f, 0.2f),
                wallMaterial);
            CreatePrimitive(
                PrimitiveType.Cube,
                $"West Wall {floorIndex}",
                floorRoot.transform,
                new Vector3(0f, elevation + 1f, interiorSize.y * 0.5f),
                new Vector3(0.2f, 2f, interiorSize.y),
                wallMaterial);
            RenderFloorEntities(floorIndex);
        }
    }

    private void CreatePlayer()
    {
        var start = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, 0, new Vector2(0.5f, 0.5f)),
            GetFloorElevation(0) + 0.5f);
        var playerObject = CreatePrimitive(
            PrimitiveType.Capsule,
            "3D Prototype Player",
            transform,
            start,
            new Vector3(0.7f, 1f, 0.7f),
            playerMaterial);
        var characterController = playerObject.AddComponent<CharacterController>();
        characterController.height = 1.8f;
        characterController.radius = 0.35f;
        player = playerObject.AddComponent<Factory3DPrototypePlayer>();
        player.Configure(playerSpeed, 0, this);
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

        prototypeCamera.transform.position = new Vector3(interiorSize.x * 0.5f, 12f, -14f);
        prototypeCamera.transform.LookAt(new Vector3(interiorSize.x * 0.5f, floorHeight * 0.5f, interiorSize.y * 0.5f));
        prototypeCamera.orthographic = false;
        prototypeCamera.fieldOfView = 50f;
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
        var location = spatialAdapter.WorldToLogical3D(
            hit,
            buildingInstanceId,
            activeFloor);
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
        activeFloor = (activeFloor + 1) % 2;
        var currentPosition = player.transform.position;
        currentPosition.y = GetFloorElevation(activeFloor) + 0.5f;
        player.Teleport(currentPosition, activeFloor);
    }

    public bool TryPlaceStorageAtLogicalPosition(Vector2 logicalPosition, out string error)
    {
        if (worldState is null)
        {
            error = "The prototype fixture is not initialized.";
            return false;
        }

        var placed = worldState.TryAddTestStorage(
            buildingInstanceId,
            activeFloor,
            logicalPosition,
            out _,
            out error);
        if (placed)
        {
            RenderFloorEntities(activeFloor);
        }

        return placed;
    }

    private void RenderFloorEntities(int floorIndex)
    {
        if (!worldState.TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            return;
        }

        foreach (var entity in floor.Entities)
        {
            if (entity is null)
            {
                continue;
            }

            if (!entityViews.TryGetValue(entity.EntityId, out var view))
            {
                view = CreatePrimitive(
                    PrimitiveType.Cube,
                    $"Entity {entity.EntityId} ({entity.DefinitionId})",
                    floorViews[floorIndex].transform,
                    Vector3.zero,
                    GetEntityScale(entity.DefinitionId),
                    GetEntityMaterial(entity.DefinitionId));
                entityViews.Add(entity.EntityId, view);
            }

            view.transform.position = spatialAdapter.LogicalToWorld3D(
                new FactoryLogicalLocation(buildingInstanceId, floorIndex, entity.LogicalPosition),
                GetFloorElevation(floorIndex) + GetEntityHeight(entity.DefinitionId));
        }
    }

    private void RefreshEntityViews()
    {
        foreach (var floorIndex in new[] { 0, 1 })
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
        Material material)
    {
        var result = GameObject.CreatePrimitive(type);
        result.name = objectName;
        result.transform.SetParent(parent, false);
        result.transform.position = position;
        result.transform.localScale = scale;
        result.GetComponent<Renderer>().sharedMaterial = material;
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
        if (definitionId.Contains("conveyor"))
        {
            return new Vector3(0.9f, 0.25f, 0.9f);
        }

        if (definitionId.Contains("elevator"))
        {
            return new Vector3(1f, 0.15f, 1f);
        }

        return new Vector3(0.9f, 0.8f, 0.9f);
    }

    private float GetEntityHeight(string definitionId)
    {
        return definitionId.Contains("conveyor") || definitionId.Contains("elevator") ? 0.15f : 0.4f;
    }

    private float GetFloorElevation(int floorIndex)
    {
        return floorIndex * floorHeight;
    }

    private void SaveAndReloadIsolatedFixture()
    {
        var path = Path.Combine(Application.temporaryCachePath, "food-factory-3d-prototype.db");
        var captured = worldState.CaptureState();
        var store = new OutsideTestFloorSqliteStore();
        store.Save(path, worldState.BuildingRecords, captured.Floors, captured.Connections);
        var reloaded = new FactoryWorldState(buildingInstanceId);
        var loaded = reloaded.LoadFromFile(path);
        Debug.Log($"3D prototype isolated fixture save/load: {loaded}, path={path}, connections={reloaded.ConnectionCount}");
    }

    public void TeleportPlayer(Factory3DPrototypePlayer targetPlayer, Vector3 position, int floorIndex)
    {
        activeFloor = Mathf.Clamp(floorIndex, 0, 1);
        targetPlayer.transform.position = position;
    }
}
