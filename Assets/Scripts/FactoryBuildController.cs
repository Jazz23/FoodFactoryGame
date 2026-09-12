// Provides play-mode factory equipment selection, placement preview, rotation, and removal on the current floor.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
    private readonly string[] labels = { "1 Source", "2 Conveyor", "3 Storage", "4 Shipping dock", "5 Receiving dock" };
    private readonly string[] arrows = { "East >", "North ^", "West <", "South v" };
    private string Definition => selection == 0 ? FactoryEntityDefinitions.TestMachineDefinitionId
        : selection == 2 ? FactoryEntityDefinitions.TestStorageDefinitionId
        : selection == 3 ? FactoryEntityDefinitions.ShippingDockDefinitionId
        : selection == 4 ? FactoryEntityDefinitions.ReceivingDockDefinitionId
        : FactoryConveyor.Definitions[direction];
    private bool IsConveyorSelection => selection == 1;
    private bool IsDockSelection => selection is 3 or 4;

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
            ? "Click an exterior cell beside a wall to place a dock | Esc: cancel | T: routes"
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
        if (TestToolsShell.IsTextInputFocused)
            return;
        if (actions["Toggle"].WasPressedThisFrame()) building = !building;
        if (actions["Cancel"].WasPressedThisFrame()) building = false;
        for (var index = 0; index < labels.Length; index++)
            if (actions[$"Select{index + 1}"].WasPressedThisFrame()) { selection = index; building = true; }
        if (actions["Rotate"].WasPressedThisFrame()) direction = (direction + 1) % 4;
        preview.SetActive(building);
        if (!building) return;
        if (activeExterior)
        {
            UpdateExteriorDockPlacement();
            return;
        }
        if (IsDockSelection)
        {
            validCell = false;
            previewRenderer.color = new Color(1f, 0.2f, 0.2f, 0.5f);
            previewLabel.text = "Place docks\noutside";
            return;
        }
        var screen = actions["Point"].ReadValue<Vector2>();
        var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        var overUI = TestToolsShell.ContainsPointer(guiPoint)
            || (EventSystem.current is not null && EventSystem.current.IsPointerOverGameObject());
        SceneGrid.TryGetForScene(gameObject.scene, out var grid);
        var camera = Camera.main!;
        var ray = camera.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out var distance)) { preview.SetActive(false); return; }
        var logical = grid.WorldToLogical(ray.GetPoint(distance));
        position = (Vector2)Vector2Int.FloorToInt(logical) + Vector2.one * 0.5f;
        IndoorGrid.TryGetForScene(gameObject.scene, out var indoor);
        owner.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        GameSceneManager.Instance.TryGetOutsideTestFloorState(buildingId, floorIndex, out var floor);
        hoveredId = 0;
        foreach (var entity in floor.Entities)
            if (Vector2Int.FloorToInt(entity.LogicalPosition) == Vector2Int.FloorToInt(position)) hoveredId = entity.EntityId;
        validCell = !overUI && hoveredId == 0 && position.x >= 0.5f && position.y >= 0.5f
            && position.x < indoor.Size.x && position.y < indoor.Size.y;
        preview.transform.position = grid.LogicalToWorld(position);
        previewRenderer.sprite = IsConveyorSelection ? art.sprite : previewSprite;
        var delta = grid.LogicalToWorld(position + FactoryConveyor.Direction(Definition)) - grid.LogicalToWorld(position);
        preview.transform.rotation = IsConveyorSelection
            ? Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f) : Quaternion.identity;
        preview.transform.localScale = IsConveyorSelection ? new Vector3(delta.magnitude * 0.8f, delta.magnitude, 1f) : Vector3.one * 0.65f;
        previewLabel.transform.rotation = Quaternion.identity;
        previewLabel.transform.position = preview.transform.position + Vector3.up * 0.65f;
        previewLabel.text = IsConveyorSelection
            ? arrows[direction]
            : selection == 3
                ? "Shipping\nDock"
                : selection == 4
                    ? "Receiving\nDock"
                    : labels[selection].Substring(2);
        previewRenderer.color = validCell ? GetPreviewColor() : new Color(1f, 0.2f, 0.2f, 0.5f);
        if (!overUI && actions["Remove"].WasPressedThisFrame() && hoveredId != 0) owner.RequestRemoveCurrentFloorEntity(hoveredId);
        if (validCell && actions["Place"].WasPressedThisFrame())
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
            _ => new Color(0.3f, 1f, 0.6f, 0.5f)
        };
    }

    private void UpdateExteriorDockPlacement()
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
        preview.transform.localScale = new Vector3(0.7f, 0.35f, 1f);
        previewRenderer.sprite = previewSprite;
        previewLabel.transform.rotation = Quaternion.identity;
        previewLabel.transform.position = preview.transform.position + Vector3.up * 0.5f;
        previewLabel.text = selection == 3 ? "Shipping\nDock" : selection == 4 ? "Receiving\nDock" : "Select a\ndock";
        previewRenderer.color = validCell ? GetPreviewColor() : new Color(1f, 0.2f, 0.2f, 0.5f);
        if (validCell && actions["Place"].WasPressedThisFrame())
        {
            owner.RequestPlaceExteriorDock(Definition, position);
        }
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

