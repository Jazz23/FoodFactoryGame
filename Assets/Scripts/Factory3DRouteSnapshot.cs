// Builds immutable, scene-independent route presentation data from authoritative factory state.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

public readonly struct Factory3DRouteBuildingId : IEquatable<Factory3DRouteBuildingId>
{
    public Factory3DRouteBuildingId(uint value)
    {
        Value = value;
    }

    public uint Value { get; }
    public string CanonicalId => ToString();

    public bool Equals(Factory3DRouteBuildingId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is Factory3DRouteBuildingId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public override string ToString() => $"B{Value}";
    public static bool operator ==(Factory3DRouteBuildingId left, Factory3DRouteBuildingId right) => left.Equals(right);
    public static bool operator !=(Factory3DRouteBuildingId left, Factory3DRouteBuildingId right) => !left.Equals(right);
}

public readonly struct Factory3DRouteFloorId : IEquatable<Factory3DRouteFloorId>
{
    public Factory3DRouteFloorId(uint buildingId, int floorIndex)
    {
        BuildingId = new Factory3DRouteBuildingId(buildingId);
        FloorIndex = floorIndex;
    }

    public Factory3DRouteFloorId(Factory3DRouteBuildingId buildingId, int floorIndex)
    {
        BuildingId = buildingId;
        FloorIndex = floorIndex;
    }

    public Factory3DRouteBuildingId BuildingId { get; }
    public int FloorIndex { get; }
    public string CanonicalId => ToString();

    public bool Equals(Factory3DRouteFloorId other) => BuildingId == other.BuildingId && FloorIndex == other.FloorIndex;
    public override bool Equals(object obj) => obj is Factory3DRouteFloorId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(BuildingId, FloorIndex);
    public override string ToString() => $"{BuildingId}/F{FloorIndex}";
    public static bool operator ==(Factory3DRouteFloorId left, Factory3DRouteFloorId right) => left.Equals(right);
    public static bool operator !=(Factory3DRouteFloorId left, Factory3DRouteFloorId right) => !left.Equals(right);
}

public readonly struct Factory3DRouteEntityId : IEquatable<Factory3DRouteEntityId>
{
    public Factory3DRouteEntityId(uint buildingId, int floorIndex, uint entityId)
    {
        FloorId = new Factory3DRouteFloorId(buildingId, floorIndex);
        EntityId = entityId;
    }

    public Factory3DRouteEntityId(Factory3DRouteFloorId floorId, uint entityId)
    {
        FloorId = floorId;
        EntityId = entityId;
    }

    public Factory3DRouteFloorId FloorId { get; }
    public Factory3DRouteBuildingId BuildingId => FloorId.BuildingId;
    public int FloorIndex => FloorId.FloorIndex;
    public uint EntityId { get; }
    public string CanonicalId => ToString();

    public bool Equals(Factory3DRouteEntityId other) => FloorId == other.FloorId && EntityId == other.EntityId;
    public override bool Equals(object obj) => obj is Factory3DRouteEntityId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(FloorId, EntityId);
    public override string ToString() => $"{FloorId}/E{EntityId}";
    public static bool operator ==(Factory3DRouteEntityId left, Factory3DRouteEntityId right) => left.Equals(right);
    public static bool operator !=(Factory3DRouteEntityId left, Factory3DRouteEntityId right) => !left.Equals(right);
}

public enum Factory3DRouteEntityType
{
    Other,
    Source,
    Processor,
    Conveyor,
    ElevatorBottom,
    ElevatorTop,
    Storage,
    Terminal
}

public enum Factory3DRouteRole
{
    Other,
    Source,
    Processor,
    Belt,
    Elevator,
    Storage,
    Terminal
}

public enum Factory3DElevatorTransferPhase
{
    None,
    Unpaired,
    Idle,
    ReadyUpward,
    ReadyDownward,
    BlockedUpward,
    BlockedDownward,
    TransferredUpward,
    TransferredDownward
}

public readonly struct Factory3DRouteBeltOccupantSnapshot
{
    public Factory3DRouteBeltOccupantSnapshot(
        float queuePosition,
        Vector2 logicalPosition,
        Vector3 worldPosition)
    {
        QueuePosition = queuePosition;
        LogicalPosition = logicalPosition;
        WorldPosition = worldPosition;
    }

    public float QueuePosition { get; }
    public Vector2 LogicalPosition { get; }
    public Vector3 WorldPosition { get; }
}

public readonly struct Factory3DRouteBuildingSnapshot
{
    public Factory3DRouteBuildingSnapshot(
        Factory3DRouteBuildingId id,
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount)
    {
        Id = id;
        AnchorCell = anchorCell;
        FootprintSize = footprintSize;
        StoryCount = storyCount;
    }

    public Factory3DRouteBuildingId Id { get; }
    public uint BuildingInstanceId => Id.Value;
    public Vector3Int AnchorCell { get; }
    public Vector2Int FootprintSize { get; }
    public int StoryCount { get; }
}

public readonly struct Factory3DRouteFloorSnapshot
{
    public Factory3DRouteFloorSnapshot(
        Factory3DRouteFloorId id,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerLogicalPosition,
        Vector3 markerWorldPosition,
        float worldElevation,
        IReadOnlyList<Factory3DRouteEntitySnapshot> entities)
    {
        Id = id;
        Label = label ?? string.Empty;
        ProductionRate = productionRate;
        AccumulatedProduction = accumulatedProduction;
        MarkerLogicalPosition = markerLogicalPosition;
        MarkerWorldPosition = markerWorldPosition;
        WorldElevation = worldElevation;
        Entities = new ReadOnlyCollection<Factory3DRouteEntitySnapshot>(
            new List<Factory3DRouteEntitySnapshot>(entities ?? Array.Empty<Factory3DRouteEntitySnapshot>()));
    }

    public Factory3DRouteFloorId Id { get; }
    public uint BuildingInstanceId => Id.BuildingId.Value;
    public int FloorIndex => Id.FloorIndex;
    public string Label { get; }
    public float ProductionRate { get; }
    public float AccumulatedProduction { get; }
    public Vector2 MarkerLogicalPosition { get; }
    public Vector3 MarkerWorldPosition { get; }
    public float WorldElevation { get; }
    public IReadOnlyList<Factory3DRouteEntitySnapshot> Entities { get; }
}

public readonly struct Factory3DRouteEntitySnapshot
{
    public Factory3DRouteEntitySnapshot(
        Factory3DRouteEntityId id,
        string definitionId,
        Factory3DRouteEntityType entityType,
        Factory3DRouteRole routeRole,
        Vector2 logicalPosition,
        Vector3 worldPosition,
        Quaternion worldOrientation,
        int inputCount,
        int outputCount,
        int inventoryCount,
        int inventoryCapacity,
        float cycleRate,
        float cycleProgress,
        int producedCount,
        IReadOnlyList<float> conveyorPositions,
        IReadOnlyList<Factory3DRouteBeltOccupantSnapshot> beltOccupants,
        Factory3DElevatorTransferPhase elevatorPhase,
        Factory3DRouteEntityId? pairedElevatorId)
    {
        Id = id;
        DefinitionId = definitionId ?? string.Empty;
        EntityType = entityType;
        RouteRole = routeRole;
        LogicalPosition = logicalPosition;
        WorldPosition = worldPosition;
        WorldOrientation = worldOrientation;
        InputCount = inputCount;
        OutputCount = outputCount;
        InventoryCount = inventoryCount;
        InventoryCapacity = inventoryCapacity;
        CycleRate = cycleRate;
        CycleProgress = cycleProgress;
        ProducedCount = producedCount;
        ConveyorPositions = new ReadOnlyCollection<float>(
            new List<float>(conveyorPositions ?? Array.Empty<float>()));
        BeltOccupants = new ReadOnlyCollection<Factory3DRouteBeltOccupantSnapshot>(
            new List<Factory3DRouteBeltOccupantSnapshot>(beltOccupants ?? Array.Empty<Factory3DRouteBeltOccupantSnapshot>()));
        ElevatorPhase = elevatorPhase;
        PairedElevatorId = pairedElevatorId;
    }

    public Factory3DRouteEntityId Id { get; }
    public uint EntityId => Id.EntityId;
    public Factory3DRouteFloorId FloorId => Id.FloorId;
    public int FloorIndex => Id.FloorIndex;
    public string DefinitionId { get; }
    public Factory3DRouteEntityType EntityType { get; }
    public Factory3DRouteRole RouteRole { get; }
    public Vector2 LogicalPosition { get; }
    public Vector3 WorldPosition { get; }
    public Vector3 DerivedWorldPosition => WorldPosition;
    public Quaternion WorldOrientation { get; }
    public Quaternion DerivedWorldOrientation => WorldOrientation;
    public int InputCount { get; }
    public int OutputCount { get; }
    public int InventoryCount { get; }
    public int InventoryCapacity { get; }
    public int StorageContents => InventoryCount;
    public int StorageCapacity => InventoryCapacity;
    public float CycleRate { get; }
    public float CycleProgress { get; }
    public int ProducedCount { get; }
    public float SourceCycleRate => CycleRate;
    public float SourceCycleProgress => CycleProgress;
    public int SourceProducedCount => ProducedCount;
    public IReadOnlyList<float> ConveyorPositions { get; }
    public IReadOnlyList<float> QueuePositions => ConveyorPositions;
    public IReadOnlyList<Factory3DRouteBeltOccupantSnapshot> BeltOccupants { get; }
    public Factory3DElevatorTransferPhase ElevatorPhase { get; }
    public Factory3DRouteEntityId? PairedElevatorId { get; }
    public bool IsSource => RouteRole == Factory3DRouteRole.Source;
    public bool IsProducing => IsSource
        && (CycleRate > 0f || CycleProgress > 0f || OutputCount > 0);
    public bool IsConveyor => RouteRole == Factory3DRouteRole.Belt;
    public bool IsStorage => RouteRole == Factory3DRouteRole.Storage;
}

public readonly struct Factory3DRouteConnectionSnapshot
{
    public Factory3DRouteConnectionSnapshot(
        Guid id,
        Factory3DRouteEntityId source,
        Factory3DRouteEntityId destination)
    {
        Id = id;
        Source = source;
        Destination = destination;
    }

    public Guid Id { get; }
    public Factory3DRouteEntityId Source { get; }
    public Factory3DRouteEntityId Destination { get; }
}

public sealed class Factory3DRouteSnapshot
{
    internal Factory3DRouteSnapshot(
        IReadOnlyList<Factory3DRouteBuildingSnapshot> buildings,
        IReadOnlyList<Factory3DRouteFloorSnapshot> floors,
        IReadOnlyList<Factory3DRouteEntitySnapshot> entities,
        IReadOnlyList<Factory3DRouteConnectionSnapshot> connections)
    {
        Buildings = new ReadOnlyCollection<Factory3DRouteBuildingSnapshot>(new List<Factory3DRouteBuildingSnapshot>(buildings));
        Floors = new ReadOnlyCollection<Factory3DRouteFloorSnapshot>(new List<Factory3DRouteFloorSnapshot>(floors));
        Entities = new ReadOnlyCollection<Factory3DRouteEntitySnapshot>(new List<Factory3DRouteEntitySnapshot>(entities));
        Connections = new ReadOnlyCollection<Factory3DRouteConnectionSnapshot>(new List<Factory3DRouteConnectionSnapshot>(connections));
    }

    public IReadOnlyList<Factory3DRouteBuildingSnapshot> Buildings { get; }
    public IReadOnlyList<Factory3DRouteFloorSnapshot> Floors { get; }
    public IReadOnlyList<Factory3DRouteEntitySnapshot> Entities { get; }
    public IReadOnlyList<Factory3DRouteConnectionSnapshot> Connections { get; }

    public bool TryGetEntity(Factory3DRouteEntityId id, out Factory3DRouteEntitySnapshot entity)
    {
        foreach (var candidate in Entities)
        {
            if (candidate.Id == id)
            {
                entity = candidate;
                return true;
            }
        }

        entity = default;
        return false;
    }
}

public static class Factory3DRouteSnapshotBuilder
{
    public static Factory3DRouteSnapshot Build(
        FactoryWorldState state,
        SceneGrid grid,
        float storyHeight = 3f)
    {
        var buildings = new List<Factory3DRouteBuildingSnapshot>();
        var floors = new List<Factory3DRouteFloorSnapshot>();
        var entities = new List<Factory3DRouteEntitySnapshot>();
        var connections = new List<Factory3DRouteConnectionSnapshot>();
        if (state is null)
        {
            return new Factory3DRouteSnapshot(buildings, floors, entities, connections);
        }

        var safeStoryHeight = float.IsFinite(storyHeight) ? Mathf.Max(0f, storyHeight) : 0f;
        var records = new List<BuildingRecord>();
        foreach (var record in state.BuildingRecords)
        {
            if (record is not null)
            {
                records.Add(record);
            }
        }

        records.Sort((left, right) => left.BuildingInstanceId.CompareTo(right.BuildingInstanceId));
        var floorRecords = new List<OutsideTestFloorRecord>();
        foreach (var floor in state.FloorStates)
        {
            if (floor is not null)
            {
                floorRecords.Add(floor);
            }
        }

        floorRecords.Sort(CompareFloors);
        foreach (var record in records)
        {
            buildings.Add(new Factory3DRouteBuildingSnapshot(
                new Factory3DRouteBuildingId(record.BuildingInstanceId),
                record.AnchorCell,
                record.FootprintSize,
                record.StoryCount));
        }

        var entityRecords = new List<RouteEntitySource>();
        foreach (var floor in floorRecords)
        {
            if (!TryFindRecord(records, floor.BuildingInstanceId, out var building))
            {
                continue;
            }

            var floorId = new Factory3DRouteFloorId(floor.BuildingInstanceId, floor.FloorIndex);
            var elevation = BuildingCoordinates.GetFloorElevation(floor.FloorIndex, safeStoryHeight);
            var adapter = grid is null || !grid ? null : grid.CreateSpatialAdapter();
            var markerWorld = ToWorld(
                adapter,
                floor.MarkerPosition,
                floor.BuildingInstanceId,
                floor.FloorIndex,
                building,
                state.GetInteriorSize(building),
                elevation);
            var floorEntities = new List<Factory3DRouteEntitySnapshot>();
            var sourceEntities = new List<FactoryEntityRecord>();
            foreach (var entity in floor.Entities)
            {
                if (entity is not null && entity.EntityId != 0)
                {
                    sourceEntities.Add(entity);
                }
            }

            sourceEntities.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
            foreach (var entity in sourceEntities)
            {
                entityRecords.Add(new RouteEntitySource(floorId, building, floor, entity, floorRecords));
            }

            foreach (var source in entityRecords)
            {
                if (source.FloorId != floorId)
                {
                    continue;
                }

                floorEntities.Add(CreateEntitySnapshot(
                    source,
                    sourceEntities,
                    floorId,
                    grid,
                    safeStoryHeight));
            }

            floors.Add(new Factory3DRouteFloorSnapshot(
                floorId,
                floor.Label,
                floor.ProductionRate,
                floor.AccumulatedProduction,
                floor.MarkerPosition,
                markerWorld,
                elevation,
                floorEntities));
            entities.AddRange(floorEntities);
        }

        foreach (var connection in state.Connections)
        {
            if (connection is not null)
            {
                connections.Add(new Factory3DRouteConnectionSnapshot(
                    connection.Guid,
                    ToEntityId(connection.Source),
                    ToEntityId(connection.Destination)));
            }
        }

        connections.Sort((left, right) =>
        {
            var result = CompareEntityIds(left.Source, right.Source);
            return result != 0 ? result : CompareEntityIds(left.Destination, right.Destination);
        });
        return new Factory3DRouteSnapshot(buildings, floors, entities, connections);
    }

    private static Factory3DRouteEntitySnapshot CreateEntitySnapshot(
        RouteEntitySource source,
        IReadOnlyList<FactoryEntityRecord> floorEntities,
        Factory3DRouteFloorId floorId,
        SceneGrid grid,
        float storyHeight)
    {
        var entity = source.Entity;
        var id = new Factory3DRouteEntityId(floorId, entity.EntityId);
        var interiorSize = BuildingFootprint.GetUsableInteriorSize(source.Building.FootprintSize);
        var isInteriorOnly = source.Building.FootprintSize == interiorSize;
        var exteriorPosition = isInteriorOnly
            ? entity.LogicalPosition
            : BuildingCoordinates.InteriorLocalToExteriorLocal(entity.LogicalPosition);
        var logicalPosition = BuildingCoordinates.LocalToExteriorLogical(
            source.Building.AnchorCell,
            exteriorPosition);
        var elevation = BuildingCoordinates.GetFloorElevation(source.Floor.FloorIndex, storyHeight);
        var adapter = grid is null || !grid ? null : grid.CreateSpatialAdapter();
        var worldPosition = ToWorld(
            adapter,
            entity.LogicalPosition,
            source.Building.BuildingInstanceId,
            source.Floor.FloorIndex,
            source.Building,
            source.Building.FootprintSize == interiorSize
                ? source.Building.FootprintSize
                : BuildingFootprint.GetUsableInteriorSize(source.Building.FootprintSize),
            elevation);
        var direction = entity.IsConveyor
            ? FactoryConveyor.Direction(entity.DefinitionId)
            : entity.IsDock ? GetDockDirection(entity.DockDirection) : Vector2.zero;
        var orientation = ToOrientation(adapter, logicalPosition, direction, id, elevation);
        var occupants = new List<Factory3DRouteBeltOccupantSnapshot>();
        foreach (var queuePosition in entity.ConveyorPositions)
        {
            var occupantLogical = FactoryConveyor.ItemPosition(entity, floorEntities, queuePosition);
            var occupantExterior = isInteriorOnly
                ? occupantLogical
                : BuildingCoordinates.InteriorLocalToExteriorLocal(occupantLogical);
            var occupantWorld = ToWorld(
                adapter,
                BuildingCoordinates.LocalToExteriorLogical(source.Building.AnchorCell, occupantExterior),
                source.Building.BuildingInstanceId,
                source.Floor.FloorIndex,
                source.Building,
                source.Building.FootprintSize == interiorSize
                    ? source.Building.FootprintSize
                    : BuildingFootprint.GetUsableInteriorSize(source.Building.FootprintSize),
                elevation);
            occupants.Add(new Factory3DRouteBeltOccupantSnapshot(queuePosition, occupantLogical, occupantWorld));
        }

        GetElevatorState(source, out var elevatorPhase, out var pairedId);
        return new Factory3DRouteEntitySnapshot(
            id,
            entity.DefinitionId,
            GetEntityType(entity),
            GetRouteRole(entity),
            entity.LogicalPosition,
            worldPosition,
            orientation,
            entity.InputCount,
            entity.OutputCount,
            entity.InventoryCount,
            entity.InventoryCapacity,
            entity.CycleRate,
            entity.CycleProgress,
            entity.ProducedCount,
            entity.ConveyorPositions,
            occupants,
            elevatorPhase,
            pairedId);
    }

    private static void GetElevatorState(
        RouteEntitySource source,
        out Factory3DElevatorTransferPhase phase,
        out Factory3DRouteEntityId? pairedId)
    {
        phase = Factory3DElevatorTransferPhase.None;
        pairedId = null;
        if (!source.Entity.IsElevator)
        {
            return;
        }

        var desiredDefinition = source.Entity.DefinitionId == FactoryEntityDefinitions.ElevatorBottomDefinitionId
            ? FactoryEntityDefinitions.ElevatorTopDefinitionId
            : FactoryEntityDefinitions.ElevatorBottomDefinitionId;
        var desiredFloor = source.Floor.FloorIndex
            + (source.Entity.DefinitionId == FactoryEntityDefinitions.ElevatorBottomDefinitionId ? 1 : -1);
        foreach (var floor in source.AllFloors)
        {
            if (floor.FloorIndex != desiredFloor || floor.BuildingInstanceId != source.Floor.BuildingInstanceId)
            {
                continue;
            }

            foreach (var candidate in floor.Entities)
            {
                if (candidate is null
                    || !candidate.IsElevator
                    || candidate.DefinitionId != desiredDefinition
                    || Vector2Int.FloorToInt(candidate.LogicalPosition)
                        != Vector2Int.FloorToInt(source.Entity.LogicalPosition))
                {
                    continue;
                }

                pairedId = new Factory3DRouteEntityId(
                    source.Floor.BuildingInstanceId,
                    floor.FloorIndex,
                    candidate.EntityId);
                var isBottom = source.Entity.DefinitionId == FactoryEntityDefinitions.ElevatorBottomDefinitionId;
                var sourceHasInput = source.Entity.InputCount > 0;
                var targetHasCapacity = isBottom
                    ? candidate.OutputCount < FactoryEntityRecord.OutputCapacity
                    : candidate.OutputCount < FactoryEntityRecord.OutputCapacity;
                phase = !sourceHasInput
                    ? candidate.OutputCount > 0
                        ? isBottom
                            ? Factory3DElevatorTransferPhase.TransferredUpward
                            : Factory3DElevatorTransferPhase.TransferredDownward
                        : Factory3DElevatorTransferPhase.Idle
                    : targetHasCapacity
                        ? isBottom
                            ? Factory3DElevatorTransferPhase.ReadyUpward
                            : Factory3DElevatorTransferPhase.ReadyDownward
                        : isBottom
                            ? Factory3DElevatorTransferPhase.BlockedUpward
                            : Factory3DElevatorTransferPhase.BlockedDownward;
                return;
            }
        }

        phase = Factory3DElevatorTransferPhase.Unpaired;
    }

    private static Vector3 ToWorld(
        FactorySpatialAdapter adapter,
        Vector2 logicalPosition,
        uint buildingId,
        int floorIndex,
        BuildingRecord building,
        Vector2Int interiorSize,
        float elevation)
    {
        var exteriorLogical = logicalPosition;
        if (building is not null && interiorSize != building.FootprintSize)
        {
            exteriorLogical = BuildingCoordinates.LocalToExteriorLogical(
                building.AnchorCell,
                BuildingCoordinates.InteriorLocalToExteriorLocal(logicalPosition));
        }

        return adapter is null
            ? new Vector3(exteriorLogical.x, elevation, exteriorLogical.y)
            : adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(buildingId, floorIndex, exteriorLogical),
                elevation);
    }

    private static Quaternion ToOrientation(
        FactorySpatialAdapter adapter,
        Vector2 logicalPosition,
        Vector2 direction,
        Factory3DRouteEntityId id,
        float elevation)
    {
        if (direction.sqrMagnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        if (adapter is null)
        {
            return Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.y), Vector3.up);
        }

        var start = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(id.BuildingId.Value, id.FloorIndex, logicalPosition),
            elevation);
        var end = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(id.BuildingId.Value, id.FloorIndex, logicalPosition + direction),
            elevation);
        var worldDirection = end - start;
        worldDirection.y = 0f;
        return worldDirection.sqrMagnitude <= 0.000001f
            ? Quaternion.identity
            : Quaternion.LookRotation(worldDirection, Vector3.up);
    }

    private static Factory3DRouteEntityType GetEntityType(FactoryEntityRecord entity)
    {
        if (entity.IsConveyor) return Factory3DRouteEntityType.Conveyor;
        if (entity.DefinitionId == FactoryEntityDefinitions.ElevatorBottomDefinitionId) return Factory3DRouteEntityType.ElevatorBottom;
        if (entity.DefinitionId == FactoryEntityDefinitions.ElevatorTopDefinitionId) return Factory3DRouteEntityType.ElevatorTop;
        if (entity.IsStorage) return Factory3DRouteEntityType.Storage;
        if (entity.IsDock) return Factory3DRouteEntityType.Terminal;
        if (entity.IsProcessor) return Factory3DRouteEntityType.Processor;
        if (entity.IsProducer) return Factory3DRouteEntityType.Source;
        return Factory3DRouteEntityType.Other;
    }

    private static Factory3DRouteRole GetRouteRole(FactoryEntityRecord entity)
    {
        if (entity.IsConveyor) return Factory3DRouteRole.Belt;
        if (entity.IsElevator) return Factory3DRouteRole.Elevator;
        if (entity.IsStorage) return Factory3DRouteRole.Storage;
        if (entity.IsDock) return Factory3DRouteRole.Terminal;
        if (entity.IsProcessor) return Factory3DRouteRole.Processor;
        if (entity.IsProducer) return Factory3DRouteRole.Source;
        return Factory3DRouteRole.Other;
    }

    private static Vector2 GetDockDirection(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.East => Vector2.right,
            GridEdgeDirection.North => Vector2.up,
            GridEdgeDirection.West => Vector2.left,
            _ => Vector2.down
        };
    }

    private static Factory3DRouteEntityId ToEntityId(FactoryEntityEndpoint endpoint)
    {
        return new Factory3DRouteEntityId(endpoint.BuildingInstanceId, endpoint.FloorIndex, endpoint.EntityId);
    }

    private static bool TryFindRecord(
        IReadOnlyList<BuildingRecord> records,
        uint buildingId,
        out BuildingRecord record)
    {
        foreach (var candidate in records)
        {
            if (candidate.BuildingInstanceId == buildingId)
            {
                record = candidate;
                return true;
            }
        }

        record = null!;
        return false;
    }

    private static int CompareFloors(OutsideTestFloorRecord left, OutsideTestFloorRecord right)
    {
        var result = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
        return result != 0 ? result : left.FloorIndex.CompareTo(right.FloorIndex);
    }

    private static int CompareEntityIds(Factory3DRouteEntityId left, Factory3DRouteEntityId right)
    {
        var result = left.BuildingId.Value.CompareTo(right.BuildingId.Value);
        if (result != 0) return result;
        result = left.FloorIndex.CompareTo(right.FloorIndex);
        return result != 0 ? result : left.EntityId.CompareTo(right.EntityId);
    }

    private readonly struct RouteEntitySource
    {
        public RouteEntitySource(
            Factory3DRouteFloorId floorId,
            BuildingRecord building,
            OutsideTestFloorRecord floor,
            FactoryEntityRecord entity,
            IReadOnlyList<OutsideTestFloorRecord> allFloors)
        {
            FloorId = floorId;
            Building = building;
            Floor = floor;
            Entity = entity;
            AllFloors = allFloors;
        }

        public Factory3DRouteFloorId FloorId { get; }
        public BuildingRecord Building { get; }
        public OutsideTestFloorRecord Floor { get; }
        public FactoryEntityRecord Entity { get; }
        public IReadOnlyList<OutsideTestFloorRecord> AllFloors { get; }
    }
}
