// Provides deterministic, grid-authoritative construction previews and commits.
using System;
using System.Collections.Generic;
using UnityEngine;

public enum FactoryConstructionTargetKind
{
    None,
    Building,
    Equipment
}

public enum FactoryConstructionAction
{
    None,
    Create,
    Move,
    Remove
}

public sealed class FactoryConstructionPreview
{
    private readonly List<Vector3Int> footprintCells = new();

    internal FactoryConstructionPreview(
        FactoryConstructionTargetKind newTargetKind,
        FactoryConstructionAction newAction)
    {
        TargetKind = newTargetKind;
        Action = newAction;
    }

    public FactoryConstructionTargetKind TargetKind { get; }
    public FactoryConstructionAction Action { get; }
    public bool IsValid { get; internal set; }
    public string Error { get; internal set; } = string.Empty;
    public uint BuildingInstanceId { get; internal set; }
    public int FloorIndex { get; internal set; } = -1;
    public uint EntityId { get; internal set; }
    public string DefinitionId { get; internal set; } = string.Empty;
    public Vector3Int AnchorCell { get; internal set; }
    public Vector2Int FootprintSize { get; internal set; }
    public int StoryCount { get; internal set; }
    public float DoorCornerExclusionDistance { get; internal set; }
    public Vector2Int Cell { get; internal set; }
    public Vector2 LogicalPosition { get; internal set; }
    public Vector2Int InteriorSize { get; internal set; }
    public BuildingRecord ProposedBuilding { get; internal set; } = null!;
    public IReadOnlyList<Vector3Int> FootprintCells => footprintCells;

    internal void SetFootprint(IEnumerable<Vector3Int> cells)
    {
        footprintCells.Clear();
        if (cells is not null)
        {
            footprintCells.AddRange(cells);
        }
    }

    public static FactoryConstructionPreview Invalid(
        FactoryConstructionTargetKind targetKind,
        FactoryConstructionAction action,
        string error)
    {
        return new FactoryConstructionPreview(targetKind, action)
        {
            Error = string.IsNullOrWhiteSpace(error) ? "Construction preview is invalid." : error
        };
    }
}

public sealed class FactoryConstructionService
{
    private readonly List<Vector3Int> cells = new();
    private BoundsInt? logicalBounds;

    public void SetLogicalBounds(BoundsInt? bounds)
    {
        logicalBounds = bounds.HasValue
            && bounds.Value.size.x > 0
            && bounds.Value.size.y > 0
            ? bounds
            : null;
    }

    public FactoryConstructionPreview PreviewBuilding(
        FactoryWorldState state,
        uint buildingInstanceId,
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        IEnumerable<BuildingRecord.DoorPlacement> doors,
        float doorCornerExclusionDistance,
        bool moving)
    {
        var action = moving ? FactoryConstructionAction.Move : FactoryConstructionAction.Create;
        var effectiveDoors = doors;
        var defaultDoorError = string.Empty;
        var defaultDoor = (BuildingRecord.DoorPlacement)null!;
        if (effectiveDoors is null
            && !TestBuildingCreator.TryGetDefaultEntrance(
                anchorCell,
                footprintSize,
                out defaultDoor,
                out defaultDoorError))
        {
            effectiveDoors = Array.Empty<BuildingRecord.DoorPlacement>();
        }
        else if (effectiveDoors is null)
        {
            effectiveDoors = new[] { defaultDoor };
        }

        var preview = new FactoryConstructionPreview(
            FactoryConstructionTargetKind.Building,
            action)
        {
            BuildingInstanceId = buildingInstanceId,
            AnchorCell = anchorCell,
            FootprintSize = footprintSize,
            StoryCount = storyCount,
            DoorCornerExclusionDistance = doorCornerExclusionDistance,
            ProposedBuilding = new BuildingRecord(
                buildingInstanceId,
                anchorCell,
                footprintSize,
                storyCount,
                effectiveDoors)
        };
        BuildingFootprint.GetCells(anchorCell, footprintSize, cells);
        preview.SetFootprint(cells);

        if (state is null)
        {
            return SetInvalid(preview, "Factory world state is required.");
        }

        if (!string.IsNullOrWhiteSpace(defaultDoorError))
        {
            return SetInvalid(preview, defaultDoorError);
        }

        if (!AreCellsInsideLogicalBounds(cells, out var boundsError))
        {
            return SetInvalid(preview, boundsError);
        }

        if (moving && !state.TryGetBuildingRecord(buildingInstanceId, out _))
        {
            return SetInvalid(preview, $"Building {buildingInstanceId} does not exist.");
        }

        var existingRecords = new List<BuildingRecord>();
        foreach (var existing in state.BuildingRecords)
        {
            if (existing is not null && existing.BuildingInstanceId != buildingInstanceId)
            {
                existingRecords.Add(existing);
            }
        }

        if (!BuildingShellValidation.TryValidate(
                preview.ProposedBuilding,
                existingRecords,
                doorCornerExclusionDistance,
                out var error))
        {
            return SetInvalid(preview, error);
        }

        preview.IsValid = true;
        return preview;
    }

    private bool AreCellsInsideLogicalBounds(
        IReadOnlyList<Vector3Int> footprintCells,
        out string error)
    {
        error = string.Empty;
        if (!logicalBounds.HasValue)
        {
            return true;
        }

        foreach (var cell in footprintCells)
        {
            if (logicalBounds.Value.Contains(new Vector3Int(cell.x, cell.y, 0)))
            {
                continue;
            }

            error = $"Building footprint leaves the configured scene bounds at cell {cell}.";
            return false;
        }

        return true;
    }

    public FactoryConstructionPreview PreviewEquipment(
        FactoryWorldState state,
        uint buildingInstanceId,
        int floorIndex,
        string definitionId,
        Vector2Int cell,
        uint ignoredEntityId = 0)
    {
        var preview = new FactoryConstructionPreview(
            FactoryConstructionTargetKind.Equipment,
            FactoryConstructionAction.Create)
        {
            BuildingInstanceId = buildingInstanceId,
            FloorIndex = floorIndex,
            DefinitionId = FactoryEntityDefinitions.NormalizeDefinitionId(definitionId ?? string.Empty),
            Cell = cell,
            LogicalPosition = SceneGrid.CellCenterLogical(cell)
        };
        return ValidateEquipmentPreview(state, preview, ignoredEntityId);
    }

    public FactoryConstructionPreview PreviewEntityMove(
        FactoryWorldState state,
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        Vector2Int cell)
    {
        var preview = new FactoryConstructionPreview(
            FactoryConstructionTargetKind.Equipment,
            FactoryConstructionAction.Move)
        {
            BuildingInstanceId = buildingInstanceId,
            FloorIndex = floorIndex,
            EntityId = entityId,
            Cell = cell,
            LogicalPosition = SceneGrid.CellCenterLogical(cell)
        };
        if (state is null
            || !state.TryGetFloorState(buildingInstanceId, floorIndex, out var floor)
            || !floor.TryGetEntity(entityId, out var entity))
        {
            return SetInvalid(preview, "The selected entity no longer exists.");
        }

        preview.DefinitionId = entity.DefinitionId;
        return ValidateEquipmentPreview(state, preview, entityId);
    }

    public FactoryConstructionPreview PreviewBuildingRemoval(
        FactoryWorldState state,
        uint buildingInstanceId)
    {
        var preview = new FactoryConstructionPreview(
            FactoryConstructionTargetKind.Building,
            FactoryConstructionAction.Remove)
        {
            BuildingInstanceId = buildingInstanceId
        };
        if (state is null || !state.TryGetBuildingRecord(buildingInstanceId, out _))
        {
            return SetInvalid(preview, $"Building {buildingInstanceId} does not exist.");
        }

        preview.IsValid = true;
        return preview;
    }

    public FactoryConstructionPreview PreviewEntityRemoval(
        FactoryWorldState state,
        uint buildingInstanceId,
        int floorIndex,
        uint entityId)
    {
        var preview = new FactoryConstructionPreview(
            FactoryConstructionTargetKind.Equipment,
            FactoryConstructionAction.Remove)
        {
            BuildingInstanceId = buildingInstanceId,
            FloorIndex = floorIndex,
            EntityId = entityId
        };
        if (state is null
            || !state.TryGetFloorState(buildingInstanceId, floorIndex, out var floor)
            || !floor.TryGetEntity(entityId, out var entity))
        {
            return SetInvalid(preview, "The selected entity no longer exists.");
        }

        preview.DefinitionId = entity.DefinitionId;
        preview.Cell = Vector2Int.FloorToInt(entity.LogicalPosition);
        preview.LogicalPosition = entity.LogicalPosition;
        preview.IsValid = true;
        return preview;
    }

    public bool TryCommit(
        FactoryWorldState state,
        FactoryConstructionPreview preview,
        bool confirmDestructiveRemoval,
        out uint affectedEntityId,
        out string error)
    {
        affectedEntityId = 0;
        error = string.Empty;
        if (state is null || preview is null || !preview.IsValid)
        {
            error = preview?.Error ?? "A valid construction preview is required.";
            return false;
        }

        if (preview.TargetKind == FactoryConstructionTargetKind.Building)
        {
            if (preview.Action == FactoryConstructionAction.Remove)
            {
                if (!confirmDestructiveRemoval)
                {
                    error = "Whole-building deletion requires destructive confirmation.";
                    return false;
                }

                return state.RemoveBuildingAndFloors(preview.BuildingInstanceId);
            }

            if (preview.Action == FactoryConstructionAction.Create)
            {
                return state.TryRegisterBuilding(preview.ProposedBuilding, out error);
            }

            return state.TryUpdateBuildingRecord(
                preview.ProposedBuilding,
                preview.DoorCornerExclusionDistance,
                out error);
        }

        if (preview.Action == FactoryConstructionAction.Remove)
        {
            return state.TryRemoveTestEntity(
                preview.BuildingInstanceId,
                preview.FloorIndex,
                preview.EntityId,
                out error);
        }

        if (preview.Action == FactoryConstructionAction.Move)
        {
            var moved = state.TryRelocateEntity(
                preview.BuildingInstanceId,
                preview.FloorIndex,
                preview.EntityId,
                preview.LogicalPosition,
                out error);
            affectedEntityId = moved ? preview.EntityId : 0;
            return moved;
        }

        var added = state.TryAddTestEntity(
            preview.BuildingInstanceId,
            preview.FloorIndex,
            preview.DefinitionId,
            preview.LogicalPosition,
            out affectedEntityId,
            out error);
        return added;
    }

    public void Cancel()
    {
    }

    private FactoryConstructionPreview ValidateEquipmentPreview(
        FactoryWorldState state,
        FactoryConstructionPreview preview,
        uint ignoredEntityId)
    {
        if (state is null)
        {
            return SetInvalid(preview, "Factory world state is required.");
        }

        if (!state.TryGetBuildingRecord(preview.BuildingInstanceId, out _)
            || !state.TryGetInteriorSize(preview.BuildingInstanceId, out var interiorSize))
        {
            return SetInvalid(preview, "The target building does not exist.");
        }

        preview.InteriorSize = interiorSize;
        if (!FactoryConveyor.IsPlaceable(preview.DefinitionId)
            && preview.DefinitionId != FactoryEntityDefinitions.ProcessorDefinitionId
            && preview.DefinitionId != FactoryEntityDefinitions.PackedStorageDefinitionId)
        {
            return SetInvalid(preview, "Unknown equipment type.");
        }

        if (FactoryDock.IsDock(preview.DefinitionId))
        {
            return SetInvalid(preview, "Docks must be placed on an exterior building wall.");
        }

        if (!BuildingFootprint.IsUsableInteriorPosition(
                preview.LogicalPosition,
                interiorSize))
        {
            return SetInvalid(preview, "Equipment must be placed inside the usable interior.");
        }

        if (!state.TryGetFloorState(
                preview.BuildingInstanceId,
                preview.FloorIndex,
                out var floor))
        {
            return SetInvalid(preview, "The target floor does not exist.");
        }

        foreach (var entity in floor.Entities)
        {
            if (entity is not null
                && entity.EntityId != ignoredEntityId
                && Vector2Int.FloorToInt(entity.LogicalPosition) == preview.Cell)
            {
                return SetInvalid(preview, "That cell is occupied.");
            }
        }

        if (FactoryEntityDefinitions.IsElevator(preview.DefinitionId)
            && !HasAlignedElevator(state, preview))
        {
            return SetInvalid(preview, "An elevator must align with its paired elevator on the adjacent floor.");
        }

        preview.IsValid = true;
        return preview;
    }

    private static bool HasAlignedElevator(
        FactoryWorldState state,
        FactoryConstructionPreview preview)
    {
        var adjacentFloorIndex = preview.DefinitionId == FactoryEntityDefinitions.ElevatorTopDefinitionId
            ? preview.FloorIndex - 1
            : preview.FloorIndex + 1;
        var pairedDefinition = preview.DefinitionId == FactoryEntityDefinitions.ElevatorTopDefinitionId
            ? FactoryEntityDefinitions.ElevatorBottomDefinitionId
            : FactoryEntityDefinitions.ElevatorTopDefinitionId;
        if (adjacentFloorIndex < 0
            || !state.TryGetFloorState(
                preview.BuildingInstanceId,
                adjacentFloorIndex,
                out var adjacentFloor))
        {
            return false;
        }

        foreach (var entity in adjacentFloor.Entities)
        {
            if (entity is not null
                && entity.DefinitionId == pairedDefinition
                && Vector2Int.FloorToInt(entity.LogicalPosition) == preview.Cell)
            {
                return true;
            }
        }

        return false;
    }

    private static FactoryConstructionPreview SetInvalid(
        FactoryConstructionPreview preview,
        string error)
    {
        preview.IsValid = false;
        preview.Error = string.IsNullOrWhiteSpace(error)
            ? "Construction preview is invalid."
            : error;
        return preview;
    }
}
