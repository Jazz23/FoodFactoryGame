// Applies validated building topology edits without silently dropping populated floor state.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class FactoryBuildingEditResult
{
    public bool Changed { get; internal set; }
    public IReadOnlyList<FactoryBuildingEntityRelocation> Relocations => relocations;
    public IReadOnlyList<uint> RecoveryEntityIds => recoveryEntityIds;

    internal List<FactoryBuildingEntityRelocation> RelocationsInternal => relocations;
    internal List<uint> RecoveryEntityIdsInternal => recoveryEntityIds;

    private readonly List<FactoryBuildingEntityRelocation> relocations = new();
    private readonly List<uint> recoveryEntityIds = new();
}

public sealed class FactoryBuildingEntityRelocation
{
    public uint EntityId { get; internal set; }
    public int FromFloorIndex { get; internal set; }
    public int ToFloorIndex { get; internal set; }
    public Vector2 FromPosition { get; internal set; }
    public Vector2 ToPosition { get; internal set; }
}

public static class FactoryBuildingEditService
{
    public static bool TryUpdateBuildingRecord(
        FactoryWorldState state,
        BuildingRecord record,
        float doorCornerExclusionDistance,
        out string error,
        out FactoryBuildingEditResult result)
    {
        error = string.Empty;
        result = new FactoryBuildingEditResult();
        if (state is null)
        {
            error = "Factory world state is required.";
            return false;
        }

        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var existingRecord in state.BuildingRecords)
        {
            if (existingRecord.BuildingInstanceId != record.BuildingInstanceId)
            {
                otherRecords.Add(existingRecord);
            }
        }

        if (!BuildingShellValidation.TryValidate(
                record,
                otherRecords,
                doorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        if (!state.TryGetBuildingRecord(record.BuildingInstanceId, out var previousRecord))
        {
            state.ApplyBuildingRecord(record);
            result.Changed = true;
            return true;
        }

        var retainedFloors = new List<OutsideTestFloorRecord>();
        var affectedFloors = new List<OutsideTestFloorRecord>();
        foreach (var floor in state.FloorStates)
        {
            if (floor.BuildingInstanceId != record.BuildingInstanceId)
            {
                continue;
            }

            if (floor.FloorIndex < record.StoryCount)
            {
                retainedFloors.Add(floor);
            }
            else
            {
                affectedFloors.Add(floor);
            }
        }

        if (affectedFloors.Count == 0)
        {
            state.ApplyBuildingRecord(record);
            result.Changed = true;
            return true;
        }

        retainedFloors.Sort((left, right) => left.FloorIndex.CompareTo(right.FloorIndex));
        affectedFloors.Sort((left, right) => left.FloorIndex.CompareTo(right.FloorIndex));
        var interiorSize = state.GetInteriorSize(record);
        var occupiedCells = new Dictionary<int, HashSet<Vector2Int>>();
        var entityIds = new Dictionary<int, HashSet<uint>>();
        foreach (var floor in retainedFloors)
        {
            occupiedCells[floor.FloorIndex] = new HashSet<Vector2Int>();
            entityIds[floor.FloorIndex] = new HashSet<uint>();
            foreach (var entity in floor.Entities)
            {
                if (entity is null)
                {
                    continue;
                }

                entityIds[floor.FloorIndex].Add(entity.EntityId);
                if (BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize))
                {
                    occupiedCells[floor.FloorIndex].Add(Vector2Int.FloorToInt(entity.LogicalPosition));
                }
            }
        }

        var plans = new List<RelocationPlan>();
        foreach (var floor in affectedFloors)
        {
            var entities = new List<FactoryEntityRecord>(floor.Entities);
            entities.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
            foreach (var entity in entities)
            {
                if (entity is null)
                {
                    continue;
                }

                if (!TryFindTarget(
                        entity,
                        floor.FloorIndex,
                        retainedFloors,
                        interiorSize,
                        occupiedCells,
                        entityIds,
                        out var targetFloor,
                        out var targetPosition))
                {
                    result.RecoveryEntityIdsInternal.Add(entity.EntityId);
                    error = $"Building {record.BuildingInstanceId} cannot retain entity {entity.EntityId} while reducing its story count.";
                    return false;
                }

                occupiedCells[targetFloor.FloorIndex].Add(Vector2Int.FloorToInt(targetPosition));
                entityIds[targetFloor.FloorIndex].Add(entity.EntityId);
                plans.Add(new RelocationPlan(floor, targetFloor, entity, targetPosition));
            }
        }

        var applied = new List<RelocationPlan>();
        try
        {
            foreach (var plan in plans)
            {
                if (!plan.Source.RemoveEntity(plan.Entity.EntityId))
                {
                    throw new InvalidOperationException($"Entity {plan.Entity.EntityId} disappeared during building edit planning.");
                }

                plan.Entity.SetLogicalPosition(plan.TargetPosition);
                plan.Target.AddEntity(plan.Entity);
                state.RebindEntityEndpoint(
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Source.FloorIndex,
                        plan.Entity.EntityId),
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Target.FloorIndex,
                        plan.Entity.EntityId));
                result.RelocationsInternal.Add(new FactoryBuildingEntityRelocation
                {
                    EntityId = plan.Entity.EntityId,
                    FromFloorIndex = plan.Source.FloorIndex,
                    ToFloorIndex = plan.Target.FloorIndex,
                    FromPosition = plan.OriginalPosition,
                    ToPosition = plan.TargetPosition
                });
                applied.Add(plan);
            }

            state.ApplyBuildingRecord(record);
            state.ResumeRecoveryRoutes();
            result.Changed = true;
            return true;
        }
        catch (Exception exception)
        {
            for (var index = applied.Count - 1; index >= 0; index--)
            {
                var plan = applied[index];
                plan.Target.RemoveEntity(plan.Entity.EntityId);
                plan.Entity.SetLogicalPosition(plan.OriginalPosition);
                plan.Source.AddEntity(plan.Entity);
                state.RebindEntityEndpoint(
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Target.FloorIndex,
                        plan.Entity.EntityId),
                    new FactoryEntityEndpoint(
                        record.BuildingInstanceId,
                        plan.Source.FloorIndex,
                        plan.Entity.EntityId));
            }

            result.RelocationsInternal.Clear();
            state.ApplyBuildingRecord(previousRecord);
            error = exception.Message;
            return false;
        }
    }

    private static bool TryFindTarget(
        FactoryEntityRecord entity,
        int sourceFloorIndex,
        IReadOnlyList<OutsideTestFloorRecord> retainedFloors,
        Vector2Int interiorSize,
        IReadOnlyDictionary<int, HashSet<Vector2Int>> occupiedCells,
        IReadOnlyDictionary<int, HashSet<uint>> entityIds,
        out OutsideTestFloorRecord targetFloor,
        out Vector2 targetPosition)
    {
        targetFloor = null!;
        targetPosition = default;
        var found = false;
        var bestFloorDistance = int.MaxValue;
        var bestDistance = float.PositiveInfinity;
        var bestCell = Vector2Int.zero;
        foreach (var floor in retainedFloors)
        {
            if (entityIds[floor.FloorIndex].Contains(entity.EntityId))
            {
                continue;
            }

            for (var y = 0; y < interiorSize.y; y++)
            {
                for (var x = 0; x < interiorSize.x; x++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (occupiedCells[floor.FloorIndex].Contains(candidate))
                    {
                        continue;
                    }

                    var floorDistance = Mathf.Abs(floor.FloorIndex - sourceFloorIndex);
                    var center = (Vector2)candidate + Vector2.one * 0.5f;
                    var distance = (center - entity.LogicalPosition).sqrMagnitude;
                    if (found
                        && !IsBetterTarget(
                            floorDistance,
                            distance,
                            floor.FloorIndex,
                            candidate,
                            bestFloorDistance,
                            bestDistance,
                            targetFloor.FloorIndex,
                            bestCell))
                    {
                        continue;
                    }

                    found = true;
                    bestFloorDistance = floorDistance;
                    bestDistance = distance;
                    bestCell = candidate;
                    targetFloor = floor;
                }
            }
        }

        if (found)
        {
            targetPosition = (Vector2)bestCell + Vector2.one * 0.5f;
        }

        return found;
    }

    private static bool IsBetterTarget(
        int floorDistance,
        float distance,
        int floorIndex,
        Vector2Int cell,
        int bestFloorDistance,
        float bestDistance,
        int bestFloorIndex,
        Vector2Int bestCell)
    {
        if (floorDistance != bestFloorDistance)
        {
            return floorDistance < bestFloorDistance;
        }

        if (!Mathf.Approximately(distance, bestDistance))
        {
            return distance < bestDistance;
        }

        if (floorIndex != bestFloorIndex)
        {
            return floorIndex < bestFloorIndex;
        }

        return cell.y < bestCell.y
            || (cell.y == bestCell.y && cell.x < bestCell.x);
    }

    private sealed class RelocationPlan
    {
        public RelocationPlan(
            OutsideTestFloorRecord newSource,
            OutsideTestFloorRecord newTarget,
            FactoryEntityRecord newEntity,
            Vector2 newTargetPosition)
        {
            Source = newSource;
            Target = newTarget;
            Entity = newEntity;
            TargetPosition = newTargetPosition;
            OriginalPosition = newEntity.LogicalPosition;
        }

        public OutsideTestFloorRecord Source { get; }
        public OutsideTestFloorRecord Target { get; }
        public FactoryEntityRecord Entity { get; }
        public Vector2 OriginalPosition { get; }
        public Vector2 TargetPosition { get; }
    }
}
