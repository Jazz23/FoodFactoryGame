// Bridges Input System 3D picking to grid-authoritative factory construction commits.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum Factory3DConstructionMode
{
    Idle,
    BuildingPlacement,
    EquipmentPlacement,
    MovingSelection,
    RemovalConfirmation
}

public sealed class Factory3DConstructionController : MonoBehaviour
{
    private static Factory3DConstructionController activeInputOwner = null!;
    private readonly List<BuildingRecord> records = new();
    private GameSceneManager manager = null!;
    private InputActionMap playerActions = null!;
    private InputActionMap buildActions = null!;
    private InputAction point = null!;
    private InputAction select = null!;
    private InputAction build = null!;
    private InputAction place = null!;
    private InputAction demolish = null!;
    private InputAction rotate = null!;
    [SerializeField] private Camera constructionCamera = null!;
    private GameObject previewObject = null!;
    private Renderer previewRenderer = null!;
    private Material previewMaterial = null!;
    private FactoryConstructionPreview currentPreview = null!;
    private uint selectedBuildingId;
    private uint selectedEntityId;
    private int selectedFloorIndex;
    private string selectedDefinitionId = FactoryEntityDefinitions.TestMachineDefinitionId;
    private Vector2Int selectedFootprintSize = new(4, 4);
    private int selectedStoryCount = 1;
    private Factory3DConstructionMode mode;
    private FactoryConstructionPreview pendingRemovalPreview = null!;
    private FactoryConstructionConfirmation pendingRemovalConfirmation = null!;
    private bool movingBuilding;
    private bool inputOwnershipEnabled = true;
    private bool? pointerFocusOverride;
    private bool initialized;
    private bool previewVisible;

    public FactoryConstructionPreview CurrentPreview => currentPreview;
    public Factory3DConstructionMode Mode => mode;
    public FactoryConstructionPreview RemovalConfirmationTarget => pendingRemovalPreview;
    public bool HasRemovalConfirmation => mode == Factory3DConstructionMode.RemovalConfirmation
        && pendingRemovalConfirmation is not null;
    public string RemovalConfirmationDescription => pendingRemovalPreview is null
        ? string.Empty
        : pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? $"Building {pendingRemovalPreview.BuildingInstanceId}"
            : $"Equipment {pendingRemovalPreview.DefinitionId} (entity {pendingRemovalPreview.EntityId}) in building {pendingRemovalPreview.BuildingInstanceId}, floor {pendingRemovalPreview.FloorIndex}";
    public uint ConfirmationTargetBuildingId => pendingRemovalConfirmation?.BuildingInstanceId ?? 0;
    public uint ConfirmationTargetEntityId => pendingRemovalConfirmation?.EntityId ?? 0;
    public bool IsEquipmentPlacement => mode == Factory3DConstructionMode.EquipmentPlacement;
    public bool IsMovingSelection => mode == Factory3DConstructionMode.MovingSelection;
    public uint SelectedBuildingId => selectedBuildingId;
    public uint SelectedEntityId => selectedEntityId;
    public bool IsBuildingPlacement => mode == Factory3DConstructionMode.BuildingPlacement;
    public bool IsPreviewVisible => previewVisible;
    public bool IsPointerFocusBlockingInput => IsPointerInputBlocked();
    public bool Is3DInputOwner => inputOwnershipEnabled
        && activeInputOwner == this;
    public bool RequireDestructiveConfirmation { get; set; } = true;

    public static bool IsInputOwnedBy3D(Scene scene)
    {
        return activeInputOwner is not null
            && activeInputOwner
            && activeInputOwner.isActiveAndEnabled
            && activeInputOwner.inputOwnershipEnabled
            && activeInputOwner.gameObject.scene == scene;
    }

    public static bool IsConstructionActiveIn3D(Scene scene)
    {
        return IsInputOwnedBy3D(scene)
            && activeInputOwner.mode != Factory3DConstructionMode.Idle;
    }

    public void SetConstructionCamera(Camera camera)
    {
        constructionCamera = camera;
    }

    public void Set3DInputOwnership(bool isOwner)
    {
        inputOwnershipEnabled = isOwner;
        if (!isOwner && activeInputOwner == this)
        {
            activeInputOwner = null!;
        }
        else if (isOwner)
        {
            activeInputOwner = this;
        }
    }

    public void SetPointerFocusOverride(bool? pointerIsOverUi)
    {
        pointerFocusOverride = pointerIsOverUi;
    }

    public void Initialize(GameSceneManager newManager)
    {
        if (initialized && manager == newManager)
        {
            return;
        }

        manager = newManager;
        playerActions = InputSystem.actions.FindActionMap("Player", true).Clone();
        buildActions = InputSystem.actions.FindActionMap("Build", true).Clone();
        point = playerActions.FindAction("Point", true);
        select = playerActions.FindAction("Select", true);
        build = buildActions.FindAction("Build", true);
        place = buildActions.FindAction("Place", true);
        demolish = buildActions.FindAction("Demolish", true);
        rotate = buildActions.FindAction("Rotate", true);
        playerActions.Enable();
        buildActions.Enable();
        if (inputOwnershipEnabled)
        {
            activeInputOwner = this;
        }
        ResolveConstructionCamera();
        EnsurePreviewObject();
        initialized = true;
        if (inputOwnershipEnabled)
        {
            activeInputOwner = this;
        }
    }

    public void BeginBuildingPlacement(
        Vector2Int footprintSize,
        int storyCount,
        IEnumerable<BuildingRecord.DoorPlacement> doors = null)
    {
        selectedBuildingId = manager.GetNextOutsideTestBuildingId();
        selectedFootprintSize = footprintSize;
        selectedStoryCount = storyCount;
        selectedEntityId = 0;
        selectedFloorIndex = 0;
        movingBuilding = false;
        ClearRemovalConfirmation();
        mode = Factory3DConstructionMode.BuildingPlacement;
        currentPreview = null!;
        manager.Clear3DConstructionVisibleFloor();
        UpdatePreview(doors);
    }

    public void BeginEquipmentPlacement(
        uint buildingInstanceId,
        int floorIndex,
        string definitionId)
    {
        selectedBuildingId = buildingInstanceId;
        selectedFloorIndex = floorIndex;
        selectedDefinitionId = FactoryEntityDefinitions.NormalizeDefinitionId(definitionId);
        selectedEntityId = 0;
        movingBuilding = false;
        ClearRemovalConfirmation();
        mode = Factory3DConstructionMode.EquipmentPlacement;
        currentPreview = null!;
        manager.Set3DConstructionVisibleFloor(floorIndex);
        UpdatePreview(null);
    }

    public bool BeginSelectedEntityMove()
    {
        if (selectedBuildingId == 0 || selectedEntityId == 0)
        {
            return false;
        }

        movingBuilding = false;
        ClearRemovalConfirmation();
        mode = Factory3DConstructionMode.MovingSelection;
        currentPreview = null!;
        manager.Set3DConstructionVisibleFloor(selectedFloorIndex);
        return true;
    }

    public bool BeginSelectedBuildingMove()
    {
        if (selectedBuildingId == 0
            || !manager.TryGetOutsideTestBuildingRecord(selectedBuildingId, out var record))
        {
            return false;
        }

        selectedFootprintSize = record.FootprintSize;
        selectedStoryCount = record.StoryCount;
        selectedFloorIndex = 0;
        movingBuilding = true;
        ClearRemovalConfirmation();
        mode = Factory3DConstructionMode.MovingSelection;
        currentPreview = null!;
        manager.Clear3DConstructionVisibleFloor();
        return true;
    }

    public void CancelConstruction()
    {
        currentPreview = null!;
        mode = Factory3DConstructionMode.Idle;
        movingBuilding = false;
        ClearRemovalConfirmation();
        previewVisible = false;
        if (previewObject is not null && previewObject)
        {
            previewObject.SetActive(false);
        }

        if (manager is not null && manager)
        {
            manager.Cancel3DConstruction();
            manager.Clear3DConstructionVisibleFloor();
        }
    }

    public bool TryCommitCurrentPreview(out string error)
    {
        error = string.Empty;
        if (mode == Factory3DConstructionMode.Idle
            || mode == Factory3DConstructionMode.RemovalConfirmation)
        {
            error = mode == Factory3DConstructionMode.RemovalConfirmation
                ? "Removal requires an explicit confirmation."
                : "There is no active construction mode.";
            return false;
        }

        if (currentPreview is null || !currentPreview.IsValid)
        {
            error = currentPreview?.Error ?? "There is no valid construction preview.";
            return false;
        }

        if (currentPreview.Action == FactoryConstructionAction.Remove)
        {
            error = "Removal requires an explicit target-specific confirmation.";
            return false;
        }

        if (!manager.TryCommit3DConstruction(
                currentPreview,
                RequireDestructiveConfirmation,
                out var affectedEntityId,
                out error))
        {
            return false;
        }

        if (affectedEntityId != 0)
        {
            selectedEntityId = affectedEntityId;
        }

        currentPreview = null!;
        mode = Factory3DConstructionMode.Idle;
        movingBuilding = false;
        manager.Clear3DConstructionVisibleFloor();
        return true;
    }

    public bool TryRemoveSelection(out string error)
    {
        error = string.Empty;
        ClearRemovalConfirmation();
        currentPreview = null!;
        mode = Factory3DConstructionMode.Idle;
        currentPreview = selectedEntityId != 0
            ? manager.Preview3DEntityRemoval(
                selectedBuildingId,
                selectedFloorIndex,
                selectedEntityId)
            : manager.Preview3DBuildingRemoval(selectedBuildingId);
        if (!currentPreview.IsValid)
        {
            error = currentPreview.Error;
            currentPreview = null!;
            return false;
        }

        pendingRemovalPreview = currentPreview;
        pendingRemovalConfirmation = new FactoryConstructionConfirmation(currentPreview);
        mode = Factory3DConstructionMode.RemovalConfirmation;
        ShowRemovalPreview();
        return true;
    }

    public bool TryConfirmRemoval(out string error)
    {
        error = string.Empty;
        if (mode != Factory3DConstructionMode.RemovalConfirmation
            || pendingRemovalPreview is null
            || pendingRemovalConfirmation is null
            || pendingRemovalPreview != currentPreview
            || !IsRemovalTargetStillSelected())
        {
            error = "A fresh target-specific removal confirmation is required.";
            return false;
        }

        if (!manager.TryCommit3DConstruction(
                pendingRemovalPreview,
                pendingRemovalConfirmation,
                out _,
                out error))
        {
            return false;
        }

        selectedEntityId = 0;
        currentPreview = null!;
        ClearRemovalConfirmation();
        mode = Factory3DConstructionMode.Idle;
        HidePreview();
        manager.Clear3DConstructionVisibleFloor();
        return true;
    }

    private void Update()
    {
        if (!initialized || manager is null || !manager)
        {
            return;
        }

        if (!Is3DInputOwner || IsPointerInputBlocked())
        {
            HidePreview();
            return;
        }

        if (build.WasPressedThisFrame())
        {
            if (mode != Factory3DConstructionMode.Idle)
            {
                CancelConstruction();
            }
            else if (selectedBuildingId != 0)
            {
                BeginEquipmentPlacement(
                    selectedBuildingId,
                    selectedFloorIndex,
                    selectedDefinitionId);
            }
        }

        if (select.WasPressedThisFrame())
        {
            SelectAtPointer();
        }

        if (demolish.WasPressedThisFrame())
        {
            SelectAtPointer();
            TryRemoveSelection(out _);
        }

        UpdatePreview(null);
        if (rotate.WasPressedThisFrame())
        {
            // Logical building rotation is intentionally deferred. The action is
            // consumed so presentation cannot imply unsupported authority.
        }

        if (place.WasPressedThisFrame())
        {
            if (mode == Factory3DConstructionMode.RemovalConfirmation)
            {
                TryConfirmRemoval(out _);
            }
            else
            {
                TryCommitCurrentPreview(out _);
            }
        }
    }

    private void SelectAtPointer()
    {
        if (!TryGetPointerCell(out var cell, out _))
        {
            selectedBuildingId = 0;
            selectedEntityId = 0;
            currentPreview = null!;
            ClearRemovalConfirmation();
            mode = Factory3DConstructionMode.Idle;
            return;
        }

        ClearRemovalConfirmation();
        currentPreview = null!;
        selectedEntityId = 0;
        selectedBuildingId = 0;
        records.Clear();
        foreach (var record in manager.StateManager.BuildingRecords)
        {
            records.Add(record);
        }

        foreach (var record in records)
        {
            if (!IsCellInside(record, cell))
            {
                continue;
            }

            selectedBuildingId = record.BuildingInstanceId;
            selectedFloorIndex = Mathf.Clamp(
                selectedFloorIndex,
                0,
                record.StoryCount - 1);
            if (manager.TryGetOutsideTestFloorState(
                    selectedBuildingId,
                    selectedFloorIndex,
                    out var floor))
            {
                foreach (var entity in floor.Entities)
                {
                    var entityCell = GetExteriorEntityCell(
                        record,
                        entity.LogicalPosition,
                        manager.TryGetOutsideTestBuildingInfo(
                                selectedBuildingId,
                                out var info)
                            && info.IsInteriorOnly);
                    if (entityCell == cell)
                    {
                        selectedEntityId = entity.EntityId;
                        break;
                    }
                }
            }

            return;
        }
    }

    private void UpdatePreview(IEnumerable<BuildingRecord.DoorPlacement> doors)
    {
        if (mode == Factory3DConstructionMode.RemovalConfirmation)
        {
            ShowRemovalPreview();
            return;
        }

        if (mode == Factory3DConstructionMode.Idle
            || !TryGetPointerCell(out var exteriorCell, out var floorIndex))
        {
            HidePreview();
            return;
        }

        if (mode == Factory3DConstructionMode.BuildingPlacement
            || (mode == Factory3DConstructionMode.MovingSelection && movingBuilding))
        {
            var anchor = new Vector3Int(exteriorCell.x, exteriorCell.y, 0);
            if (mode == Factory3DConstructionMode.MovingSelection
                && manager.TryGetOutsideTestBuildingRecord(
                    selectedBuildingId,
                    out var existingRecord))
            {
                doors = existingRecord.Doors;
            }

            currentPreview = manager.Preview3DBuilding(
                selectedBuildingId,
                anchor,
                selectedFootprintSize,
                selectedStoryCount,
                doors,
                mode == Factory3DConstructionMode.MovingSelection);
            ShowPreview(
                currentPreview,
                new Vector2Int(anchor.x, anchor.y),
                floorIndex);
            return;
        }

        if (selectedBuildingId == 0)
        {
            HidePreview();
            return;
        }

        if (!manager.TryGetOutsideTestBuildingRecord(
                selectedBuildingId,
                out var buildingRecord))
        {
            HidePreview();
            return;
        }

        var localCell = exteriorCell - new Vector2Int(
            buildingRecord.AnchorCell.x,
            buildingRecord.AnchorCell.y);
        var interiorOnly = manager.TryGetOutsideTestBuildingInfo(
                selectedBuildingId,
                out var info)
            && info.IsInteriorOnly;
        if (!interiorOnly)
        {
            localCell -= Vector2Int.one;
        }

        selectedFloorIndex = Mathf.Clamp(
            mode == Factory3DConstructionMode.MovingSelection
                ? selectedFloorIndex
                : floorIndex,
            0,
            Mathf.Max(0, buildingRecord.StoryCount - 1));
        currentPreview = mode == Factory3DConstructionMode.MovingSelection
            ? manager.Preview3DEntityMove(
                selectedBuildingId,
                selectedFloorIndex,
                selectedEntityId,
                localCell)
            : manager.Preview3DEquipment(
                selectedBuildingId,
                selectedFloorIndex,
                selectedDefinitionId,
                localCell);
        ShowPreview(currentPreview, exteriorCell, selectedFloorIndex);
    }

    private void ResolveConstructionCamera()
    {
        if (constructionCamera is not null && constructionCamera)
        {
            return;
        }

        constructionCamera = GetComponent<Camera>();
        if (constructionCamera is null || !constructionCamera)
        {
            constructionCamera = FindFirstObjectByType<Camera>(FindObjectsInactive.Include);
        }
    }

    private bool IsPointerInputBlocked()
    {
        if (TestToolsShell.IsTextInputFocused)
        {
            return true;
        }

        if (pointerFocusOverride.HasValue)
        {
            return pointerFocusOverride.Value;
        }

        var screenPoint = point.ReadValue<Vector2>();
        var guiPoint = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
        return TestToolsShell.ContainsPointer(guiPoint)
            || (EventSystem.current is not null
                && EventSystem.current.IsPointerOverGameObject());
    }

    private void ShowRemovalPreview()
    {
        if (pendingRemovalPreview is null
            || !manager.TryGetOutsideTestBuildingRecord(
                pendingRemovalPreview.BuildingInstanceId,
                out var record)
            || !SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            HidePreview();
            return;
        }

        var isInteriorOnly = manager.TryGetOutsideTestBuildingInfo(
                record.BuildingInstanceId,
                out var info)
            && info.IsInteriorOnly;
        var exteriorCell = pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector2Int(record.AnchorCell.x, record.AnchorCell.y)
            : GetExteriorEntityCell(
                record,
                pendingRemovalPreview.LogicalPosition,
                isInteriorOnly);
        var logicalPosition = pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector2(
                record.AnchorCell.x + record.FootprintSize.x * 0.5f,
                record.AnchorCell.y + record.FootprintSize.y * 0.5f)
            : SceneGrid.CellCenterLogical(exteriorCell);
        var previewFloorIndex = pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? 0
            : pendingRemovalPreview.FloorIndex;
        var adapter = grid.CreateSpatialAdapter();
        var world = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(0u, previewFloorIndex, logicalPosition),
            previewFloorIndex * manager.OutsideTestStoryHeight);
        var size = pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector3(
                Mathf.Max(0.1f, record.FootprintSize.x * grid.CellSize * 0.9f),
                0.15f,
                Mathf.Max(0.1f, record.FootprintSize.y * grid.CellSize * 0.9f))
            : new Vector3(grid.CellSize * 0.7f, grid.CellSize * 0.35f, grid.CellSize * 0.7f);
        previewObject.transform.SetPositionAndRotation(
            new Vector3(
                world.x,
                world.y + size.y * 0.5f,
                world.z),
            Quaternion.identity);
        previewObject.transform.localScale = size;
        if (previewMaterial is not null)
        {
            previewMaterial.color = new Color(1f, 0.15f, 0.15f, 0.45f);
        }

        previewVisible = true;
        previewObject.SetActive(true);
    }

    private bool IsRemovalTargetStillSelected()
    {
        return pendingRemovalPreview.TargetKind == FactoryConstructionTargetKind.Building
            ? selectedBuildingId == pendingRemovalPreview.BuildingInstanceId
                && selectedEntityId == 0
            : selectedBuildingId == pendingRemovalPreview.BuildingInstanceId
                && selectedFloorIndex == pendingRemovalPreview.FloorIndex
                && selectedEntityId == pendingRemovalPreview.EntityId;
    }

    private void ClearRemovalConfirmation()
    {
        pendingRemovalPreview = null!;
        pendingRemovalConfirmation = null!;
    }

    private bool TryGetPointerCell(out Vector2Int cell, out int floorIndex)
    {
        cell = default;
        floorIndex = selectedFloorIndex;
        ResolveConstructionCamera();
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid)
            || constructionCamera is null
            || !constructionCamera)
        {
            return false;
        }

        var ray = constructionCamera.ScreenPointToRay(point.ReadValue<Vector2>());
        var adapter = grid.CreateSpatialAdapter();
        var pointerFloorIndex = mode == Factory3DConstructionMode.BuildingPlacement
            || (mode == Factory3DConstructionMode.MovingSelection && movingBuilding)
            ? 0
            : selectedFloorIndex;
        var floorElevation = BuildingCoordinates.GetFloorElevation(
            pointerFloorIndex,
            manager.OutsideTestStoryHeight);
        if (!adapter.TryGetCellFrom3DRay(ray, floorElevation, out var logicalCell))
        {
            return false;
        }

        cell = new Vector2Int(logicalCell.x, logicalCell.y);
        floorIndex = pointerFloorIndex;
        return true;
    }

    private void ShowPreview(
        FactoryConstructionPreview preview,
        Vector2Int exteriorCell,
        int floorIndex)
    {
        EnsurePreviewObject();
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            HidePreview();
            return;
        }

        var logicalPosition = preview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector2(
                preview.AnchorCell.x + preview.FootprintSize.x * 0.5f,
                preview.AnchorCell.y + preview.FootprintSize.y * 0.5f)
            : SceneGrid.CellCenterLogical(exteriorCell);
        var adapter = grid.CreateSpatialAdapter();
        var world = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(0u, floorIndex, logicalPosition),
            floorIndex * manager.OutsideTestStoryHeight);
        var size = preview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector3(
                Mathf.Max(0.1f, preview.FootprintSize.x * grid.CellSize * 0.9f),
                0.15f,
                Mathf.Max(0.1f, preview.FootprintSize.y * grid.CellSize * 0.9f))
            : new Vector3(grid.CellSize * 0.7f, grid.CellSize * 0.35f, grid.CellSize * 0.7f);
        previewObject.transform.SetPositionAndRotation(
            new Vector3(
                world.x,
                world.y + size.y * 0.5f,
                world.z),
            Quaternion.identity);
        previewObject.transform.localScale = size;
        if (previewMaterial is not null)
        {
            previewMaterial.color = preview.IsValid
                ? new Color(0.2f, 1f, 0.35f, 0.45f)
                : new Color(1f, 0.15f, 0.15f, 0.45f);
        }
        previewVisible = true;
        previewObject.SetActive(true);
    }

    private void EnsurePreviewObject()
    {
        if (previewObject is not null && previewObject)
        {
            return;
        }

        previewObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        previewObject.name = "Factory 3D Construction Preview";
        previewObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        var collider = previewObject.GetComponent<Collider>();
        if (collider is not null && collider)
        {
            Destroy(collider);
        }

        previewRenderer = previewObject.GetComponent<Renderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader is not null)
        {
            previewMaterial = new Material(shader)
            {
                name = "Factory 3D Construction Preview Material",
                hideFlags = HideFlags.DontSave
            };
            previewRenderer.sharedMaterial = previewMaterial;
        }

        previewObject.SetActive(false);
    }

    private void HidePreview()
    {
        previewVisible = false;
        if (previewObject is not null && previewObject)
        {
            previewObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (inputOwnershipEnabled)
        {
            activeInputOwner = this;
        }

        if (!initialized)
        {
            return;
        }

        playerActions.Enable();
        buildActions.Enable();
    }

    private void OnDisable()
    {
        if (playerActions is not null)
        {
            playerActions.Disable();
        }

        if (buildActions is not null)
        {
            buildActions.Disable();
        }

        if (activeInputOwner == this)
        {
            activeInputOwner = null!;
        }
    }

    private static bool IsCellInside(BuildingRecord record, Vector2Int cell)
    {
        return cell.x >= record.AnchorCell.x
            && cell.y >= record.AnchorCell.y
            && cell.x < record.AnchorCell.x + record.FootprintSize.x
            && cell.y < record.AnchorCell.y + record.FootprintSize.y;
    }

    private static Vector2Int GetExteriorEntityCell(
        BuildingRecord record,
        Vector2 logicalPosition,
        bool isInteriorOnly)
    {
        var local = isInteriorOnly
            ? logicalPosition
            : BuildingCoordinates.InteriorLocalToExteriorLocal(logicalPosition);
        var exterior = BuildingCoordinates.LocalToExteriorLogical(record.AnchorCell, local);
        return Vector2Int.FloorToInt(exterior);
    }

    private void OnDestroy()
    {
        if (activeInputOwner == this)
        {
            activeInputOwner = null!;
        }

        if (playerActions is not null)
        {
            playerActions.Disable();
            playerActions.Dispose();
        }

        if (buildActions is not null)
        {
            buildActions.Disable();
            buildActions.Dispose();
        }
        if (previewMaterial is not null)
        {
            Destroy(previewMaterial);
        }

        if (previewObject is not null && previewObject)
        {
            Destroy(previewObject);
        }
    }
}
