// Provides deterministic factory-save reconciliation and integrity checks for editor tooling.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class FactoryPipelineRelocation
{
    public uint buildingId;
    public int floorIndex;
    public uint entityId;
    public float[] from = null!;
    public float[] to = null!;
}

[Serializable]
public sealed class FactoryPipelineReconciliationResult
{
    public bool changed;
    public List<FactoryPipelineRelocation> relocated = new();
    public List<uint> recoveryEntityIds = new();
    public int identityChanges;
    public int dataLoss;
}

public static class FactoryWorldReconciliationService
{
    public static int CountRecoveryEntities(
        FactoryWorldBuildingRecord building,
        FactoryWorldFloorRecord floor)
    {
        var interiorSize = GetInteriorSize(building);
        var occupiedCells = new HashSet<Vector2Int>();
        var recoveryCount = 0;
        var entities = new List<FactoryWorldEntityRecord>(floor.Entities);
        entities.Sort(CompareEntities);
        foreach (var entity in entities)
        {
            var cell = Vector2Int.FloorToInt(entity.LocalPosition);
            if (!BuildingFootprint.IsUsableInteriorPosition(
                    entity.LocalPosition,
                    interiorSize)
                || !occupiedCells.Add(cell))
            {
                recoveryCount++;
            }
        }

        return recoveryCount;
    }

    public static FactoryPipelineReconciliationResult Reconcile(
        FactoryWorldSnapshot snapshot)
    {
        if (!FactoryWorldValidation.TryValidate(snapshot, out var error))
        {
            throw new InvalidDataException(error);
        }

        var before = snapshot.Clone();
        var result = new FactoryPipelineReconciliationResult();
        var buildings = new List<FactoryWorldBuildingRecord>(snapshot.Buildings);
        buildings.Sort(CompareBuildings);
        foreach (var building in buildings)
        {
            var floors = new List<FactoryWorldFloorRecord>();
            foreach (var floor in snapshot.Floors)
            {
                if (floor.BuildingGuid == building.Guid)
                {
                    floors.Add(floor);
                }
            }

            floors.Sort((left, right) => left.FloorIndex.CompareTo(right.FloorIndex));
            foreach (var floor in floors)
            {
                ReconcileFloor(building, floor, result);
            }
        }

        FactoryWorldIntegrityService.VerifyUnchanged(
            before,
            snapshot,
            result);
        result.changed = result.relocated.Count > 0;
        return result;
    }

    private static void ReconcileFloor(
        FactoryWorldBuildingRecord building,
        FactoryWorldFloorRecord floor,
        FactoryPipelineReconciliationResult result)
    {
        var interiorSize = GetInteriorSize(building);
        var occupiedCells = new HashSet<Vector2Int>();
        var displaced = new List<FactoryWorldEntityRecord>();
        var entities = new List<FactoryWorldEntityRecord>(floor.Entities);
        entities.Sort(CompareEntities);
        foreach (var entity in entities)
        {
            var cell = Vector2Int.FloorToInt(entity.LocalPosition);
            if (BuildingFootprint.IsUsableInteriorPosition(
                    entity.LocalPosition,
                    interiorSize)
                && occupiedCells.Add(cell))
            {
                continue;
            }

            displaced.Add(entity);
        }

        foreach (var entity in displaced)
        {
            if (!TryFindAvailableCell(
                    entity.LocalPosition,
                    interiorSize,
                    occupiedCells,
                    out var targetCell))
            {
                AddRecoveryEntity(result, entity);
                continue;
            }

            var from = entity.LocalPosition;
            var to = (Vector2)targetCell + Vector2.one * 0.5f;
            entity.SetLocalPosition(to);
            occupiedCells.Add(targetCell);
            result.relocated.Add(new FactoryPipelineRelocation
            {
                buildingId = building.LegacyBuildingId,
                floorIndex = floor.FloorIndex,
                entityId = entity.LegacyEntityId,
                from = ToArray(from),
                to = ToArray(to)
            });
        }

        foreach (var entity in entities)
        {
            if (!BuildingFootprint.IsUsableInteriorPosition(
                    entity.LocalPosition,
                    interiorSize))
            {
                AddRecoveryEntity(result, entity);
            }
        }
    }

    private static int CompareEntities(
        FactoryWorldEntityRecord left,
        FactoryWorldEntityRecord right)
    {
        var legacyIdComparison = left.LegacyEntityId.CompareTo(right.LegacyEntityId);
        return legacyIdComparison != 0
            ? legacyIdComparison
            : left.Guid.CompareTo(right.Guid);
    }

    private static int CompareBuildings(
        FactoryWorldBuildingRecord left,
        FactoryWorldBuildingRecord right)
    {
        var legacyIdComparison = left.LegacyBuildingId.CompareTo(right.LegacyBuildingId);
        return legacyIdComparison != 0
            ? legacyIdComparison
            : left.Guid.CompareTo(right.Guid);
    }

    private static Vector2Int GetInteriorSize(FactoryWorldBuildingRecord building)
    {
        return building.InteriorOnly
            ? building.FootprintSize
            : BuildingFootprint.GetUsableInteriorSize(building.FootprintSize);
    }

    private static bool TryFindAvailableCell(
        Vector2 position,
        Vector2Int interiorSize,
        HashSet<Vector2Int> occupiedCells,
        out Vector2Int targetCell)
    {
        targetCell = Vector2Int.zero;
        var found = false;
        var bestDistance = float.PositiveInfinity;
        for (var y = 0; y < interiorSize.y; y++)
        {
            for (var x = 0; x < interiorSize.x; x++)
            {
                var candidate = new Vector2Int(x, y);
                if (occupiedCells.Contains(candidate))
                {
                    continue;
                }

                var center = (Vector2)candidate + Vector2.one * 0.5f;
                var distance = (center - position).sqrMagnitude;
                if (found
                    && (distance > bestDistance
                        || (Mathf.Approximately(distance, bestDistance)
                            && (candidate.y < targetCell.y
                                || (candidate.y == targetCell.y
                                    && candidate.x < targetCell.x)))))
                {
                    continue;
                }

                targetCell = candidate;
                bestDistance = distance;
                found = true;
            }
        }

        return found;
    }

    private static void AddRecoveryEntity(
        FactoryPipelineReconciliationResult result,
        FactoryWorldEntityRecord entity)
    {
        if (entity.LegacyEntityId != 0
            && !result.recoveryEntityIds.Contains(entity.LegacyEntityId))
        {
            result.recoveryEntityIds.Add(entity.LegacyEntityId);
        }
    }

    private static float[] ToArray(Vector2 value)
    {
        return new[] { value.x, value.y };
    }
}

public static class FactoryWorldIntegrityService
{
    public static void VerifyUnchanged(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after,
        FactoryPipelineReconciliationResult result)
    {
        result.identityChanges = CountIdentityChanges(before, after);
        result.dataLoss = CountDataLoss(before, after);
    }

    private static int CountIdentityChanges(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after)
    {
        var changes = 0;
        if (before.Buildings.Count != after.Buildings.Count
            || before.Floors.Count != after.Floors.Count
            || before.Connections.Count != after.Connections.Count
            || before.Routes.Count != after.Routes.Count
            || before.Trucks.Count != after.Trucks.Count
            || before.MigrationMappings.Count != after.MigrationMappings.Count)
        {
            changes++;
        }

        foreach (var building in before.Buildings)
        {
            var current = after.Buildings.Find(candidate => candidate.Guid == building.Guid);
            if (current is null)
            {
                changes++;
            }
        }

        foreach (var floor in before.Floors)
        {
            var current = after.Floors.Find(candidate => candidate.Guid == floor.Guid);
            if (current is null || current.BuildingGuid != floor.BuildingGuid)
            {
                changes++;
            }

            if (current is not null)
            {
                foreach (var entity in floor.Entities)
                {
                    var matchingEntity = FindEntity(current.Entities, entity.Guid);
                    if (matchingEntity is null
                        || matchingEntity.FloorGuid != entity.FloorGuid)
                    {
                        changes++;
                    }
                }
            }
        }

        foreach (var connection in before.Connections)
        {
            var current = after.Connections.Find(candidate => candidate.Guid == connection.Guid);
            if (current is null
                || !current.Source.Equals(connection.Source)
                || !current.Destination.Equals(connection.Destination))
            {
                changes++;
            }
        }

        foreach (var route in before.Routes)
        {
            var current = after.Routes.Find(candidate => candidate.Guid == route.Guid);
            if (current is null
                || current.TruckGuid != route.TruckGuid
                || !current.Source.Equals(route.Source)
                || !current.Destination.Equals(route.Destination))
            {
                changes++;
            }
        }

        foreach (var truck in before.Trucks)
        {
            var current = after.Trucks.Find(candidate => candidate.Guid == truck.Guid);
            if (current is null || current.RouteGuid != truck.RouteGuid)
            {
                changes++;
            }
        }

        foreach (var mapping in before.MigrationMappings)
        {
            var current = after.MigrationMappings.Find(candidate =>
                candidate.Kind == mapping.Kind && candidate.LegacyKey == mapping.LegacyKey);
            if (current is null || current.Guid != mapping.Guid)
            {
                changes++;
            }
        }

        return changes;
    }

    private static int CountDataLoss(
        FactoryWorldSnapshot before,
        FactoryWorldSnapshot after)
    {
        var loss = 0;
        loss += CountMissing(
            before.Buildings,
            after.Buildings,
            record => record.Guid);
        loss += CountMissing(
            before.Floors,
            after.Floors,
            record => record.Guid);
        loss += CountMissing(
            before.Connections,
            after.Connections,
            record => record.Guid);
        loss += CountMissing(
            before.Routes,
            after.Routes,
            record => record.Guid);
        loss += CountMissing(
            before.Trucks,
            after.Trucks,
            record => record.Guid);
        loss += CountMissing(
            before.MigrationMappings,
            after.MigrationMappings,
            record => record.Guid);
        foreach (var floor in before.Floors)
        {
            var current = after.Floors.Find(candidate => candidate.Guid == floor.Guid);
            if (current is null)
            {
                continue;
            }

            loss += CountMissing(
                floor.Entities,
                current.Entities,
                record => record.Guid);
        }

        foreach (var building in before.Buildings)
        {
            var current = after.Buildings.Find(candidate => candidate.Guid == building.Guid);
            if (current is not null && !SameBuildingData(building, current))
            {
                loss++;
            }
        }

        foreach (var floor in before.Floors)
        {
            var current = after.Floors.Find(candidate => candidate.Guid == floor.Guid);
            if (current is null)
            {
                continue;
            }

            if (!SameFloorData(floor, current))
            {
                loss++;
            }

            foreach (var entity in floor.Entities)
            {
                var currentEntity = FindEntity(current.Entities, entity.Guid);
                if (currentEntity is not null && !SameEntityData(entity, currentEntity))
                {
                    loss++;
                }
            }
        }

        foreach (var connection in before.Connections)
        {
            var current = after.Connections.Find(candidate => candidate.Guid == connection.Guid);
            if (current is not null
                && (!current.Source.Equals(connection.Source)
                    || !current.Destination.Equals(connection.Destination)))
            {
                loss++;
            }
        }

        foreach (var route in before.Routes)
        {
            var current = after.Routes.Find(candidate => candidate.Guid == route.Guid);
            if (current is not null && !SameRouteData(route, current))
            {
                loss++;
            }
        }

        foreach (var truck in before.Trucks)
        {
            var current = after.Trucks.Find(candidate => candidate.Guid == truck.Guid);
            if (current is not null && !SameTruckData(truck, current))
            {
                loss++;
            }
        }

        foreach (var mapping in before.MigrationMappings)
        {
            var current = after.MigrationMappings.Find(candidate =>
                candidate.Kind == mapping.Kind && candidate.LegacyKey == mapping.LegacyKey);
            if (current is not null && current.Guid != mapping.Guid)
            {
                loss++;
            }
        }

        return loss;
    }

    private static int CountMissing<T>(
        IReadOnlyList<T> before,
        IReadOnlyList<T> after,
        Func<T, Guid> getGuid)
    {
        var count = 0;
        foreach (var record in before)
        {
            var guid = getGuid(record);
            var found = false;
            foreach (var current in after)
            {
                if (getGuid(current) == guid)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                count++;
            }
        }

        return count;
    }

    private static bool SameBuildingData(
        FactoryWorldBuildingRecord left,
        FactoryWorldBuildingRecord right)
    {
        if (left.LegacyBuildingId != right.LegacyBuildingId
            || left.DefinitionId != right.DefinitionId
            || left.AnchorCell != right.AnchorCell
            || left.FootprintSize != right.FootprintSize
            || left.StoryCount != right.StoryCount
            || left.InteriorOnly != right.InteriorOnly
            || left.Doors.Count != right.Doors.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Doors.Count; index++)
        {
            var leftDoor = left.Doors[index];
            var rightDoor = right.Doors[index];
            if (leftDoor.WallId != rightDoor.WallId
                || !Mathf.Approximately(
                    leftDoor.NormalizedOffset,
                    rightDoor.NormalizedOffset))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameFloorData(
        FactoryWorldFloorRecord left,
        FactoryWorldFloorRecord right)
    {
        return left.BuildingGuid == right.BuildingGuid
            && left.FloorIndex == right.FloorIndex
            && left.Label == right.Label
            && Mathf.Approximately(left.ProductionRate, right.ProductionRate)
            && Mathf.Approximately(
                left.AccumulatedProduction,
                right.AccumulatedProduction)
            && left.MarkerPosition == right.MarkerPosition;
    }

    private static bool SameEntityData(
        FactoryWorldEntityRecord left,
        FactoryWorldEntityRecord right)
    {
        return left.FloorGuid == right.FloorGuid
            && left.LegacyEntityId == right.LegacyEntityId
            && left.DefinitionId == right.DefinitionId
            && Mathf.Approximately(left.RotationZ, right.RotationZ)
            && left.Footprint == right.Footprint
            && left.CycleRate == right.CycleRate
            && left.CycleProgress == right.CycleProgress
            && left.ProducedCount == right.ProducedCount
            && left.OutputCount == right.OutputCount
            && left.InputCount == right.InputCount
            && left.IsNaiEntity == right.IsNaiEntity
            && SameBytes(left.State, right.State);
    }

    private static bool SameRouteData(
        FactoryWorldTruckRouteRecord left,
        FactoryWorldTruckRouteRecord right)
    {
        return left.TruckGuid == right.TruckGuid
            && left.Source.Equals(right.Source)
            && left.Destination.Equals(right.Destination)
            && left.ItemId == right.ItemId
            && left.CargoCapacity == right.CargoCapacity
            && left.TransferRateItemsPerSecond == right.TransferRateItemsPerSecond
            && left.OutboundTravelSeconds == right.OutboundTravelSeconds
            && left.ReturnTravelSeconds == right.ReturnTravelSeconds
            && left.PartialLoadDepartureWindowSeconds
                == right.PartialLoadDepartureWindowSeconds;
    }

    private static bool SameTruckData(
        FactoryWorldTruckRecord left,
        FactoryWorldTruckRecord right)
    {
        return left.RouteGuid == right.RouteGuid
            && left.State == right.State
            && left.CargoItemId == right.CargoItemId
            && left.CargoCount == right.CargoCount
            && left.RemainingTravelSeconds == right.RemainingTravelSeconds
            && left.LoadingWindowProgress == right.LoadingWindowProgress
            && left.BlockingReason == right.BlockingReason;
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static FactoryWorldEntityRecord FindEntity(
        IReadOnlyList<FactoryWorldEntityRecord> entities,
        Guid guid)
    {
        foreach (var entity in entities)
        {
            if (entity.Guid == guid)
            {
                return entity;
            }
        }

        return null!;
    }
}
