// Bridges Input System 3D picking to grid-authoritative factory construction commits.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class Factory3DConstructionController : MonoBehaviour
{
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
    private bool buildingPlacement;
    private bool movingSelection;
    private bool initialized;
    private bool previewVisible;

    public FactoryConstructionPreview CurrentPreview => currentPreview;
    public uint SelectedBuildingId => selectedBuildingId;
    public uint SelectedEntityId => selectedEntityId;
    public bool IsBuildingPlacement => buildingPlacement;
    public bool RequireDestructiveConfirmation { get; set; } = true;

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
        EnsurePreviewObject();
        initialized = true;
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
        movingSelection = false;
        buildingPlacement = true;
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
        movingSelection = false;
        buildingPlacement = false;
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

        movingSelection = true;
        buildingPlacement = false;
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
        movingSelection = true;
        buildingPlacement = true;
        currentPreview = null!;
        manager.Clear3DConstructionVisibleFloor();
        return true;
    }

    public void CancelConstruction()
    {
        currentPreview = null!;
        buildingPlacement = false;
        movingSelection = false;
        previewVisible = false;
        if (previewObject is not null && previewObject)
        {
            previewObject.SetActive(false);
        }

        manager.Cancel3DConstruction();
        manager.Clear3DConstructionVisibleFloor();
    }

    public bool TryCommitCurrentPreview(out string error)
    {
        error = string.Empty;
        if (currentPreview is null || !currentPreview.IsValid)
        {
            error = currentPreview?.Error ?? "There is no valid construction preview.";
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
        movingSelection = false;
        return true;
    }

    public bool TryRemoveSelection(out string error)
    {
        error = string.Empty;
        currentPreview = selectedEntityId != 0
            ? manager.Preview3DEntityRemoval(
                selectedBuildingId,
                selectedFloorIndex,
                selectedEntityId)
            : manager.Preview3DBuildingRemoval(selectedBuildingId);
        if (!currentPreview.IsValid)
        {
            error = currentPreview.Error;
            return false;
        }

        return TryCommitCurrentPreview(out error);
    }

    private void Update()
    {
        if (!initialized || manager is null || !manager)
        {
            return;
        }

        if (build.WasPressedThisFrame())
        {
            if (buildingPlacement || movingSelection)
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
            TryCommitCurrentPreview(out _);
        }
    }

    private void SelectAtPointer()
    {
        if (!TryGetPointerCell(out var cell, out _))
        {
            return;
        }

        selectedEntityId = 0;
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
                    var entityCell = GetExteriorEntityCell(record, entity);
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
        if ((!buildingPlacement && !movingSelection)
            || !TryGetPointerCell(out var exteriorCell, out var floorIndex))
        {
            HidePreview();
            return;
        }

        if (buildingPlacement)
        {
            var anchor = new Vector3Int(exteriorCell.x, exteriorCell.y, 0);
            if (movingSelection
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
                movingSelection);
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
            && info.InteriorSize == info.FootprintSize;
        if (!interiorOnly)
        {
            localCell -= Vector2Int.one;
        }

        selectedFloorIndex = Mathf.Clamp(
            movingSelection ? selectedFloorIndex : floorIndex,
            0,
            Mathf.Max(0, buildingRecord.StoryCount - 1));
        currentPreview = movingSelection
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

    private bool TryGetPointerCell(out Vector2Int cell, out int floorIndex)
    {
        cell = default;
        floorIndex = selectedFloorIndex;
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid)
            || Camera.main is null
            || !Camera.main)
        {
            return false;
        }

        var ray = Camera.main.ScreenPointToRay(point.ReadValue<Vector2>());
        var adapter = grid.CreateSpatialAdapter();
        var floorElevation = BuildingCoordinates.GetFloorElevation(
            selectedFloorIndex,
            manager.OutsideTestStoryHeight);
        if (!adapter.TryGetCellFrom3DRay(ray, floorElevation, out var logicalCell))
        {
            return false;
        }

        cell = new Vector2Int(logicalCell.x, logicalCell.y);
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
        var world = grid.LogicalToWorld(logicalPosition);
        var size = preview.TargetKind == FactoryConstructionTargetKind.Building
            ? new Vector3(
                Mathf.Max(0.1f, preview.FootprintSize.x * grid.CellSize * 0.9f),
                0.15f,
                Mathf.Max(0.1f, preview.FootprintSize.y * grid.CellSize * 0.9f))
            : new Vector3(grid.CellSize * 0.7f, grid.CellSize * 0.35f, grid.CellSize * 0.7f);
        previewObject.transform.SetPositionAndRotation(
            new Vector3(
                world.x,
                floorIndex * manager.OutsideTestStoryHeight + size.y * 0.5f,
                world.y),
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
        FactoryEntityRecord entity)
    {
        var isInteriorOnly = BuildingFootprint.GetUsableInteriorSize(record.FootprintSize)
            == record.FootprintSize;
        var local = isInteriorOnly
            ? entity.LogicalPosition
            : BuildingCoordinates.InteriorLocalToExteriorLocal(entity.LogicalPosition);
        var exterior = BuildingCoordinates.LocalToExteriorLogical(record.AnchorCell, local);
        return Vector2Int.FloorToInt(exterior);
    }

    private void OnDestroy()
    {
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
