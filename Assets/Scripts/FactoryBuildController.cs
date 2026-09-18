// Provides play-mode factory equipment selection, placement preview, rotation, and removal on the current floor.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class FactoryBuildController : MonoBehaviour
{
    private PlayerSceneTransition owner = null!;
    private InputActionMap actions = null!;
    private TestToolsShell shell = null!;
    private GameObject preview = null!;
    private SpriteRenderer previewRenderer = null!;
    private Sprite previewSprite = null!;
    private FactoryConveyorArt art = null!;
    private TextMesh previewLabel = null!;
    private int selection;
    private int direction;
    private bool building;
    private bool activeFloor;
    private bool activeExterior;
    private bool validCell;
    private Vector2 position;
    private uint hoveredId;
    private uint recoveryId;
    private string status = "Place sources, belts, and storage inside. Place shipping and receiving docks outside.";
    private readonly string[] labels =
    {
        "Source", "Conveyor", "Storage", "Shipping dock", "Receiving dock", "Elevator top", "Elevator bottom"
    };
    private readonly string[] arrows = { "East >", "South v", "West <", "North ^" };
    private readonly int[] conveyorDefinitionIndices = { 0, 3, 2, 1 };
    private string Definition => selection switch
    {
        0 => FactoryEntityDefinitions.TestMachineDefinitionId,
        1 => FactoryConveyor.Definitions[conveyorDefinitionIndices[direction]],
        2 => FactoryEntityDefinitions.TestStorageDefinitionId,
        3 => FactoryEntityDefinitions.ShippingDockDefinitionId,
        4 => FactoryEntityDefinitions.ReceivingDockDefinitionId,
        5 => FactoryEntityDefinitions.ElevatorTopDefinitionId,
        _ => FactoryEntityDefinitions.ElevatorBottomDefinitionId
    };
    private bool IsConveyorSelection => selection == 1;
    private bool IsDockSelection => selection is 3 or 4;
    private bool IsElevatorSelection => selection is 5 or 6;

    public void Initialize(PlayerSceneTransition newOwner)
    {
        owner = newOwner;
        shell = TestToolsShell.GetOrCreate();
        shell.BindBuilder(this);
        actions = InputSystem.actions.FindActionMap("FactoryBuild", true).Clone();
        actions.Enable();
        preview = new GameObject("Factory Placement Preview");
        preview.transform.SetParent(transform, false);
        previewRenderer = preview.AddComponent<SpriteRenderer>();
        previewSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        previewRenderer.sprite = previewSprite;
        previewRenderer.sortingOrder = FactoryConveyor.EquipmentSortingOrder;
        preview.transform.localScale = Vector3.one * 0.65f;
        var labelObject = new GameObject("Placement Label");
        labelObject.transform.SetParent(preview.transform, false);
        previewLabel = labelObject.AddComponent<TextMesh>();
        previewLabel.anchor = TextAnchor.MiddleCenter;
        previewLabel.fontSize = 24;
        previewLabel.characterSize = 0.06f;
        previewLabel.GetComponent<MeshRenderer>().sortingOrder = FactoryConveyor.ItemSortingOrder;
        art = Resources.Load<FactoryConveyorArt>("FactoryConveyorArt");
        preview.SetActive(false);
    }

    public void SetStatus(string message) => status = message;

    public bool IsBuilding => building;
    public bool IsBuildContextActive => activeFloor || activeExterior;
    public string Status => status;
    public string DirectionLabel => arrows[direction];
    public int RecoveryCount => GetRecoveryCount();

    public string GetEquipmentLabel(int index) => labels[index];

    public string GetInstructions()
    {
        return activeExterior
            ? "Click an exterior cell beside a wall to place a dock | Right click: remove dock | Esc: cancel | T: routes"
            : "Click: place | Right click: remove | Esc: cancel | T: routes | F2: hide tools | F3: floors";
    }

    public void ToggleBuilding()
    {
        if (IsBuildContextActive)
        {
            building = !building;
        }
    }

    public void SelectEquipment(int index)
    {
        if (index < 0 || index >= labels.Length || !IsBuildContextActive)
        {
            return;
        }

        selection = index;
        building = true;
    }

    public void EquipHotbarItem(string? itemId)
    {
        if (!IsBuildContextActive)
        {
            return;
        }

        if (itemId is null)
        {
            building = false;
            return;
        }

        if (itemId == "conveyor-belt")
        {
            SelectEquipment(1);
            return;
        }

        if (itemId == FactoryEntityDefinitions.ElevatorTopItemId)
        {
            SelectEquipment(5);
            return;
        }

        if (itemId == FactoryEntityDefinitions.ElevatorBottomItemId)
        {
            SelectEquipment(6);
            return;
        }

        building = false;
    }

    public void RotateBuilding()
    {
        if (IsBuildContextActive)
        {
            direction = (direction + 1) % 4;
        }
    }

    public bool TryGetRecoveryCandidate(int index, out uint entityId, out string label)
    {
        entityId = 0;
        label = string.Empty;
        if (!TryGetCurrentMachineFloor(out var floor, out var buildingInfo))
        {
            return false;
        }

        var candidateIndex = 0;
        foreach (var entity in floor.Entities)
        {
            if (entity is null
                || BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, buildingInfo.InteriorSize))
            {
                continue;
            }

            if (candidateIndex != index)
            {
                candidateIndex++;
                continue;
            }

            entityId = entity.EntityId;
            label = $"RECOVER E{entity.EntityId}  {entity.DefinitionId}  ({entity.OutputCount + entity.InputCount} item(s))";
            return true;
        }

        return false;
    }

    public void SelectRecovery(uint entityId)
    {
        recoveryId = entityId;
        building = true;
        status = $"Recovery selected: entity {entityId}. Click a free interior cell.";
    }

    private void Update()
    {
        activeFloor = !owner.IsTransitioning && owner.TryGetCurrentOutsideTestFloor(out _, out _);
        activeExterior = !activeFloor
            && !owner.IsTransitioning
            && gameObject.scene.name == "OutsideTest";
        if (!activeFloor && !activeExterior)
        {
            preview.SetActive(false);
            building = false;
            return;
        }

        if (Factory3DConstructionController.IsConstructionActiveIn3D(gameObject.scene))
        {
            preview.SetActive(false);
            building = false;
            return;
        }

        var removePressed = shell.IsBuildToolsExpanded && actions["Remove"].WasPressedThisFrame();
        if (TestToolsShell.IsTextInputFocused)
            return;
        if (actions["Toggle"].WasPressedThisFrame()) building = !building;
        if (actions["Cancel"].WasPressedThisFrame()) building = false;
        if (actions["Rotate"].WasPressedThisFrame()) direction = (direction + 1) % 4;
        preview.SetActive(building);
        if (activeExterior)
        {
            if (building || removePressed)
            {
                UpdateExteriorDockPlacement(removePressed);
            }

            return;
        }

        if (!building && !removePressed)
        {
            return;
        }

        var screen = actions["Point"].ReadValue<Vector2>();
        var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        var overUI = TestToolsShell.ContainsPointer(guiPoint)
            || (EventSystem.current is not null && EventSystem.current.IsPointerOverGameObject());
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid)
            || !IndoorGrid.TryGetForScene(gameObject.scene, out var indoor))
        {
            preview.SetActive(false);
            return;
        }

        var camera = Camera.main!;
        var ray = camera.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out var distance)) { preview.SetActive(false); return; }
        var logical = grid.WorldToLogical(ray.GetPoint(distance));
        position = (Vector2)Vector2Int.FloorToInt(logical) + Vector2.one * 0.5f;
        var pointerCell = Vector2Int.FloorToInt(position);
        owner.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (!GameSceneManager.Instance.TryGetOutsideTestFloorState(buildingId, floorIndex, out var floor))
        {
            preview.SetActive(false);
            return;
        }

        hoveredId = 0;
        foreach (var entity in floor.Entities)
        {
            if (Vector2Int.FloorToInt(entity.LogicalPosition) == pointerCell)
            {
                hoveredId = entity.EntityId;
            }
        }

        if (removePressed && !overUI && hoveredId != 0)
        {
            owner.RequestRemoveCurrentFloorEntity(hoveredId);
        }

        if (!building)
        {
            return;
        }

        if (IsDockSelection)
        {
            validCell = false;
            previewLabel.text = "Place docks\noutside";
            previewRenderer.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            return;
        }

        var elevatorTarget = default(Vector2);
        var elevatorLabel = string.Empty;
        var hasElevatorTarget = IsElevatorSelection
            && TryGetElevatorConnectionTarget(
                buildingId,
                floorIndex,
                floor,
                out elevatorTarget,
                out elevatorLabel);
        if (hasElevatorTarget)
        {
            position = elevatorTarget;
        }

        var targetCell = Vector2Int.FloorToInt(position);
        var targetOccupied = false;
        foreach (var entity in floor.Entities)
        {
            if (Vector2Int.FloorToInt(entity.LogicalPosition) == targetCell)
            {
                targetOccupied = true;
                break;
            }
        }

        var withinInterior = position.x >= 0.5f && position.y >= 0.5f
            && position.x < indoor.Size.x && position.y < indoor.Size.y;
        validCell = !targetOccupied && withinInterior && (hasElevatorTarget || !overUI);
        preview.transform.position = grid.LogicalToWorld(position);
        previewRenderer.sprite = IsConveyorSelection ? art.sprite : previewSprite;
        var delta = grid.LogicalToWorld(position + FactoryConveyor.Direction(Definition)) - grid.LogicalToWorld(position);
        preview.transform.rotation = IsConveyorSelection
            ? Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f) : Quaternion.identity;
        preview.transform.localScale = IsConveyorSelection
            ? new Vector3(delta.magnitude * 0.8f, delta.magnitude, 1f)
            : Vector3.one * (0.65f * grid.CellSize);
        previewLabel.transform.rotation = Quaternion.identity;
        previewLabel.transform.position = preview.transform.position
            + Vector3.up * (0.65f * grid.CellSize);
        previewLabel.text = hasElevatorTarget
            ? elevatorLabel
            : IsConveyorSelection
            ? arrows[direction]
            : selection == 3
                ? "Shipping\nDock"
                : selection == 4
                    ? "Receiving\nDock"
                    : labels[selection];
        previewRenderer.color = validCell ? GetPreviewColor() : new Color(1f, 0.2f, 0.2f, 0.5f);
        var pointerOnTarget = !hasElevatorTarget || pointerCell == targetCell;
        if (validCell && pointerOnTarget && !overUI && actions["Place"].WasPressedThisFrame())
        {
            if (recoveryId != 0)
            {
                owner.RequestRelocateCurrentFloorEntity(recoveryId, position);
                recoveryId = 0;
            }
            else
            {
                owner.RequestPlaceEquipment(Definition, position);
            }
        }
    }

    private Color GetPreviewColor()
    {
        return selection switch
        {
            3 => new Color(0.25f, 0.95f, 0.55f, 0.5f),
            4 => new Color(0.35f, 0.65f, 1f, 0.5f),
            5 => new Color(0.75f, 0.5f, 1f, 0.5f),
            6 => new Color(1f, 0.7f, 0.3f, 0.5f),
            _ => new Color(0.3f, 1f, 0.6f, 0.5f)
        };
    }

    private void UpdateExteriorDockPlacement(bool removePressed)
    {
        var screen = actions["Point"].ReadValue<Vector2>();
        var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        var overUI = TestToolsShell.ContainsPointer(guiPoint)
            || (EventSystem.current is not null && EventSystem.current.IsPointerOverGameObject());
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            preview.SetActive(false);
            return;
        }

        var ray = Camera.main!.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out var distance))
        {
            preview.SetActive(false);
            return;
        }

        var logical = grid.WorldToLogical(ray.GetPoint(distance));
        position = (Vector2)Vector2Int.FloorToInt(logical) + Vector2.one * 0.5f;
        var manager = NotAI.NAIStateManager.Instance;
        if (removePressed && !overUI
            && manager is { IsInitialized: true }
            && FactoryDock.TryFindExteriorPlacement(
                manager.BuildingRecords,
                position,
                out var dockBuilding,
                out var dockInteriorPosition,
                out var dockDirection)
            && GameSceneManager.Instance.TryGetOutsideTestFloorState(
                dockBuilding.BuildingInstanceId,
                0,
                out var dockFloor))
        {
            foreach (var entity in dockFloor.Entities)
            {
                if (entity.IsDock
                    && entity.DockDirection == dockDirection
                    && Vector2Int.FloorToInt(entity.LogicalPosition)
                        == Vector2Int.FloorToInt(dockInteriorPosition))
                {
                    owner.RequestRemoveExteriorDock(dockBuilding.BuildingInstanceId, entity.EntityId);
                    break;
                }
            }
        }

        if (!building)
        {
            return;
        }

        if (IsElevatorSelection)
        {
            validCell = false;
            previewLabel.text = "Elevators must be\nplaced inside factories";
            previewRenderer.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            return;
        }

        validCell = !overUI
            && IsDockSelection
            && manager is { IsInitialized: true }
            && FactoryDock.TryFindExteriorPlacement(
                manager.BuildingRecords,
                position,
                out _,
                out _,
                out _);
        preview.transform.position = grid.LogicalToWorld(position);
        preview.transform.rotation = Quaternion.identity;
        preview.transform.localScale = new Vector3(
            0.7f * grid.CellSize,
            0.35f * grid.CellSize,
            1f);
        previewRenderer.sprite = previewSprite;
        previewLabel.transform.rotation = Quaternion.identity;
        previewLabel.transform.position = preview.transform.position
            + Vector3.up * (0.5f * grid.CellSize);
        previewLabel.text = selection == 3 ? "Shipping\nDock" : selection == 4 ? "Receiving\nDock" : "Select a\ndock";
        previewRenderer.color = validCell ? GetPreviewColor() : new Color(1f, 0.2f, 0.2f, 0.5f);
        if (validCell && actions["Place"].WasPressedThisFrame())
        {
            owner.RequestPlaceExteriorDock(Definition, position);
        }
    }

    private bool TryGetElevatorConnectionTarget(
        uint buildingId,
        int floorIndex,
        OutsideTestFloorRecord currentFloor,
        out Vector2 targetPosition,
        out string label)
    {
        targetPosition = default;
        label = string.Empty;
        if (GameSceneManager.Instance is null)
        {
            return false;
        }

        var adjacentFloor = selection == 5 ? floorIndex - 1 : floorIndex + 1;
        var requiredDefinition = selection == 5
            ? FactoryEntityDefinitions.ElevatorBottomDefinitionId
            : FactoryEntityDefinitions.ElevatorTopDefinitionId;
        if (adjacentFloor < 0
            || !GameSceneManager.Instance.TryGetOutsideTestFloorState(
                buildingId,
                adjacentFloor,
                out var adjacentState))
        {
            return false;
        }

        foreach (var entity in adjacentState.Entities)
        {
            if (entity.DefinitionId != requiredDefinition)
            {
                continue;
            }

            var targetCell = Vector2Int.FloorToInt(entity.LogicalPosition);
            var targetOccupied = false;
            foreach (var currentEntity in currentFloor.Entities)
            {
                if (Vector2Int.FloorToInt(currentEntity.LogicalPosition) == targetCell)
                {
                    targetOccupied = true;
                    break;
                }
            }

            if (targetOccupied)
            {
                continue;
            }

            targetPosition = entity.LogicalPosition;
            label = selection == 5 ? "Connect above" : "Connect below";
            return true;
        }

        return false;
    }

    private int GetRecoveryCount()
    {
        if (!TryGetCurrentMachineFloor(out var floor, out var buildingInfo))
        {
            return 0;
        }

        var count = 0;
        foreach (var entity in floor.Entities)
        {
            if (entity is not null
                && !BuildingFootprint.IsUsableInteriorPosition(
                    entity.LogicalPosition,
                    buildingInfo.InteriorSize))
            {
                count++;
            }
        }

        return count;
    }

    private bool TryGetCurrentMachineFloor(
        out OutsideTestFloorRecord floor,
        out OutsideTestBuildingInfo buildingInfo)
    {
        floor = null!;
        buildingInfo = default;
        if (owner is null
            || !owner.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex)
            || GameSceneManager.Instance is null
            || !GameSceneManager.Instance.TryGetOutsideTestFloorState(buildingId, floorIndex, out floor)
            || !GameSceneManager.Instance.TryGetOutsideTestBuildingInfo(buildingId, out buildingInfo))
        {
            return false;
        }

        return true;
    }

    private void OnDestroy()
    {
        if (shell is not null && shell)
        {
            shell.UnbindBuilder(this);
        }

        if (actions is not null)
        {
            actions.Disable();
        }
        if (preview is not null) Destroy(preview);
        if (previewSprite is not null) Destroy(previewSprite);
    }
}

