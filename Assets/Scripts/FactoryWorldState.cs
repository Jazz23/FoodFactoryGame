// Owns persistent OutsideTest topology and floor state, registration, migration, and simulation.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public readonly struct OutsideTestFloorKey : IEquatable<OutsideTestFloorKey>
{
    public OutsideTestFloorKey(uint newBuildingInstanceId, int newFloorIndex)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
    }

    public uint BuildingInstanceId { get; }
    public int FloorIndex { get; }

    public bool Equals(OutsideTestFloorKey other)
    {
        return BuildingInstanceId == other.BuildingInstanceId
            && FloorIndex == other.FloorIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is OutsideTestFloorKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((int)BuildingInstanceId * 397) ^ FloorIndex;
        }
    }

    public override string ToString()
    {
        return $"Building {BuildingInstanceId} / Floor {FloorIndex}";
    }
}

public readonly struct OutsideTestBuildingInfo
{
    public OutsideTestBuildingInfo(
        uint newBuildingInstanceId,
        int newStoryCount,
        Vector2Int newInteriorSize)
    {
        BuildingInstanceId = newBuildingInstanceId;
        StoryCount = newStoryCount;
        InteriorSize = newInteriorSize;
        AnchorCell = Vector3Int.zero;
        FootprintSize = newInteriorSize;
    }

    public OutsideTestBuildingInfo(BuildingRecord record)
    {
        BuildingInstanceId = record.BuildingInstanceId;
        StoryCount = record.StoryCount;
        InteriorSize = BuildingFootprint.GetUsableInteriorSize(record.FootprintSize);
        AnchorCell = record.AnchorCell;
        FootprintSize = record.FootprintSize;
    }

    public uint BuildingInstanceId { get; }
    public int StoryCount { get; }
    public Vector2Int InteriorSize { get; }
    public Vector3Int AnchorCell { get; }
    public Vector2Int FootprintSize { get; }
}

public sealed class FactoryWorldState
{
    public const int BuildingRecordsSaveVersion = 4;
    public const int OutputBuffersSaveVersion = 5;
    public const int OutputBufferSaveVersion = OutputBuffersSaveVersion;
    public const int ConnectionsSaveVersion = 6;
    public const int InputBuffersSaveVersion = 7;
    public const int InputBufferSaveVersion = InputBuffersSaveVersion;
    public const int CurrentSaveVersion = 7;

    private readonly uint legacyBuildingInstanceId;
    private readonly Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord> floorStates = new();
    private readonly Dictionary<uint, BuildingRecord> buildingRecords = new();
    private readonly HashSet<uint> interiorOnlyBuildingIds = new();
    private readonly List<FactoryEntityConnectionRecord> connections = new();
    private readonly List<FactoryTruckRouteRecord> truckRoutes = new();
    private readonly List<FactoryTruckRecord> trucks = new();
    private int lastLoadedVersion;
    private bool lastLoadHadBuildingRecords;

    public FactoryWorldState(uint newLegacyBuildingInstanceId)
    {
        legacyBuildingInstanceId = newLegacyBuildingInstanceId;
    }

    public IEnumerable<OutsideTestFloorRecord> FloorStates => floorStates.Values;
    public IEnumerable<BuildingRecord> BuildingRecords => buildingRecords.Values;
    public IEnumerable<OutsideTestBuildingInfo> Buildings => GetBuildingInfos();
    public IReadOnlyList<FactoryEntityConnectionRecord> Connections => connections;
    public IReadOnlyList<FactoryTruckRouteRecord> TruckRoutes => truckRoutes;
    public IReadOnlyList<FactoryTruckRecord> Trucks => trucks;
    public int TruckRouteCount => truckRoutes.Count;
    public int ConnectionCount => connections.Count;
    public int LastLoadedVersion => lastLoadedVersion;
    public bool LastLoadHadBuildingRecords => lastLoadHadBuildingRecords;

    public void MarkCurrentStateAsAuthoritative()
    {
        lastLoadedVersion = CurrentSaveVersion;
        lastLoadHadBuildingRecords = true;
    }

    public bool TryRegisterBuilding(
        BuildingRecord record,
        out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        if (buildingRecords.TryGetValue(
                record.BuildingInstanceId,
                out var existingRecord))
        {
            if (!existingRecord.HasSameTopology(record))
            {
                error = $"Duplicate building ID {record.BuildingInstanceId} has conflicting layout data.";
                return false;
            }

            NormalizeBuildingFloorStates(existingRecord);
            return false;
        }

        if (!BuildingShellValidation.TryValidate(
                record,
                buildingRecords.Values,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var ownedRecord = record.Clone();
        buildingRecords.Add(ownedRecord.BuildingInstanceId, ownedRecord);
        NormalizeBuildingFloorStates(ownedRecord);
        return true;
    }

    public bool TryRegisterBuilding(
        uint buildingInstanceId,
        int storyCount,
        Vector2Int interiorSize,
        out string error)
    {
        error = string.Empty;
        if (buildingInstanceId == 0)
        {
            error = "Building ID must be greater than zero.";
            return false;
        }

        if (storyCount < 1)
        {
            error = $"Building {buildingInstanceId} must have at least one floor.";
            return false;
        }

        if (!BuildingFootprint.IsValid(interiorSize))
        {
            error = $"Building {buildingInstanceId} has an invalid interior size {interiorSize}.";
            return false;
        }

        if (buildingRecords.TryGetValue(
                buildingInstanceId,
                out var existingRecord))
        {
            interiorOnlyBuildingIds.Add(buildingInstanceId);
            if (existingRecord.StoryCount != storyCount
                || existingRecord.FootprintSize != interiorSize)
            {
                error = $"Duplicate building ID {buildingInstanceId} has conflicting layout data.";
                return false;
            }

            NormalizeBuildingFloorStates(existingRecord);
            return false;
        }

        var legacyRecord = new BuildingRecord(
            buildingInstanceId,
            Vector3Int.zero,
            interiorSize,
            storyCount);
        buildingRecords.Add(buildingInstanceId, legacyRecord);
        interiorOnlyBuildingIds.Add(buildingInstanceId);
        NormalizeBuildingFloorStates(legacyRecord);
        return true;
    }

    public bool TryUpdateBuildingRecord(
        BuildingRecord record,
        float doorCornerExclusionDistance,
        out string error)
    {
        error = string.Empty;
        if (record is null)
        {
            error = "Building record is required.";
            return false;
        }

        var otherRecords = new List<BuildingRecord>();
        foreach (var existingRecord in buildingRecords.Values)
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

        buildingRecords[record.BuildingInstanceId] = record.Clone();
        var staleKeys = new List<OutsideTestFloorKey>();
        foreach (var pair in floorStates)
        {
            if (pair.Key.BuildingInstanceId == record.BuildingInstanceId
                && pair.Key.FloorIndex >= record.StoryCount)
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            BlockRoutesForFloor(
                key.BuildingInstanceId,
                key.FloorIndex,
                $"Route endpoint floor {key.FloorIndex} in building {key.BuildingInstanceId} was removed.");
            RemoveConnectionsForFloor(key.BuildingInstanceId, key.FloorIndex);
            floorStates.Remove(key);
        }

        NormalizeBuildingFloorStates(buildingRecords[record.BuildingInstanceId]);
        return true;
    }

    public bool TryGetBuildingInfo(
        uint buildingInstanceId,
        out OutsideTestBuildingInfo info)
    {
        if (buildingRecords.TryGetValue(buildingInstanceId, out var record))
        {
            info = new OutsideTestBuildingInfo(record);
            return true;
        }

        info = default;
        return false;
    }

    public bool TryGetBuildingRecord(
        uint buildingInstanceId,
        out BuildingRecord record)
    {
        if (buildingRecords.TryGetValue(buildingInstanceId, out var existingRecord))
        {
            record = existingRecord.Clone();
            return true;
        }

        record = null!;
        return false;
    }

    public uint GetNextBuildingId()
    {
        return GetNextBuildingId(Array.Empty<uint>());
    }

    public uint GetNextBuildingId(IEnumerable<uint> additionalIds)
    {
        var maximumId = 0u;
        foreach (var buildingId in buildingRecords.Keys)
        {
            if (buildingId > maximumId)
            {
                maximumId = buildingId;
            }
        }

        foreach (var floor in floorStates.Values)
        {
            if (floor.BuildingInstanceId > maximumId)
            {
                maximumId = floor.BuildingInstanceId;
            }
        }

        if (additionalIds is not null)
        {
            foreach (var buildingId in additionalIds)
            {
                if (buildingId > maximumId)
                {
                    maximumId = buildingId;
                }
            }
        }

        return maximumId == uint.MaxValue ? 0u : maximumId + 1u;
    }

    public bool RemoveBuilding(uint buildingInstanceId)
    {
        BlockRoutesForBuilding(
            buildingInstanceId,
            $"Route endpoint building {buildingInstanceId} was removed.");
        RemoveConnectionsForBuilding(buildingInstanceId);
        var removed = buildingRecords.Remove(buildingInstanceId);
        interiorOnlyBuildingIds.Remove(buildingInstanceId);
        var staleKeys = new List<OutsideTestFloorKey>();
        foreach (var pair in floorStates)
        {
            if (pair.Key.BuildingInstanceId == buildingInstanceId)
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            BlockRoutesForFloor(
                key.BuildingInstanceId,
                key.FloorIndex,
                $"Route endpoint floor {key.FloorIndex} in building {key.BuildingInstanceId} was removed.");
            removed |= floorStates.Remove(key);
        }

        return removed;
    }

    public bool RemoveBuildingAndFloors(uint buildingInstanceId)
    {
        return RemoveBuilding(buildingInstanceId);
    }

    public bool TryGetFloorState(
        uint buildingInstanceId,
        int floorIndex,
        out OutsideTestFloorRecord state)
    {
        return floorStates.TryGetValue(
            new OutsideTestFloorKey(buildingInstanceId, floorIndex),
            out state);
    }

    public bool TryAddTestEntity(
        uint buildingInstanceId,
        int floorIndex,
        string definitionId,
        Vector2 position,
        out uint entityId,
        out string error,
        GridEdgeDirection dockDirection = GridEdgeDirection.South)
    {
        entityId = 0;
        error = string.Empty;
        if (!buildingRecords.TryGetValue(buildingInstanceId, out var building)
            || !TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            error = "The floor does not exist.";
            return false;
        }

        var interiorSize = GetInteriorSize(building.BuildingInstanceId);
        if (!BuildingFootprint.IsUsableInteriorPosition(position, interiorSize))
        {
            error = "Machine/entity position must be finite and inside the floor bounds.";
            return false;
        }

        var maximumId = 0u;
        foreach (var entity in floor.Entities)
        {
            if (entity is not null && entity.EntityId > maximumId)
            {
                maximumId = entity.EntityId;
            }
        }

        if (maximumId == uint.MaxValue)
        {
            error = "No entity IDs are available on this floor.";
            return false;
        }

        entityId = maximumId + 1;
        var safeDefinitionId = string.IsNullOrWhiteSpace(definitionId)
            ? "test-machine"
            : definitionId.Trim();
        var definition = FactoryEntityDefinitions.Get(safeDefinitionId);
        floor.AddEntity(new FactoryEntityRecord(
            entityId,
            safeDefinitionId,
            position,
            definition.IsStorage ? 0f : 1f,
            0f,
            0,
            0,
            0,
            null,
            dockDirection));
        return true;
    }

    public bool TryAddTestMachine(uint buildingInstanceId, int floorIndex,
        Vector2 position, out uint entityId, out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            "test-machine",
            position,
            out entityId,
            out error);
    }

    public bool TryAddTestStorage(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityRecord.StorageDefinitionId,
            position,
            out entityId,
            out error);
    }

    public bool TryAddTestProcessor(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityRecord.ProcessorDefinitionId,
            position,
            out entityId,
            out error);
    }

    public bool TryAddPackedStorage(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityRecord.PackedStorageDefinitionId,
            position,
            out entityId,
            out error);
    }

    public bool TryAddSendingTerminal(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityDefinitions.ShippingDockDefinitionId,
            position,
            out entityId,
            out error);
    }

    public bool TryAddReceivingTerminal(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityDefinitions.ReceivingDockDefinitionId,
            position,
            out entityId,
            out error);
    }

    public bool TryAddShippingDock(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        GridEdgeDirection direction,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityDefinitions.ShippingDockDefinitionId,
            position,
            out entityId,
            out error,
            direction);
    }

    public bool TryAddReceivingDock(
        uint buildingInstanceId,
        int floorIndex,
        Vector2 position,
        GridEdgeDirection direction,
        out uint entityId,
        out string error)
    {
        return TryAddTestEntity(
            buildingInstanceId,
            floorIndex,
            FactoryEntityDefinitions.ReceivingDockDefinitionId,
            position,
            out entityId,
            out error,
            direction);
    }

    public bool TryRemoveTestMachine(uint buildingInstanceId, int floorIndex,
        uint entityId, out string error)
    {
        return TryRemoveTestEntity(buildingInstanceId, floorIndex, entityId, out error);
    }

    public bool TryRemoveTestEntity(
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        out string error)
    {
        error = string.Empty;
        if (!buildingRecords.ContainsKey(buildingInstanceId)
            || !TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            error = "The floor does not exist.";
            return false;
        }

        if (entityId == 0 || !floor.TryGetEntity(entityId, out _))
        {
            error = "The selected entity no longer exists.";
            return false;
        }

        BlockRoutesForEndpoint(
            new FactoryEntityEndpoint(buildingInstanceId, floorIndex, entityId),
            $"Route endpoint entity {entityId} on floor {floorIndex} was removed.");
        RemoveConnectionsForEndpoint(new FactoryEntityEndpoint(
            buildingInstanceId,
            floorIndex,
            entityId));
        floor.RemoveEntity(entityId);
        return true;
    }

    public bool TryRelocateEntity(
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        Vector2 position,
        out string error)
    {
        error = string.Empty;
        if (!buildingRecords.ContainsKey(buildingInstanceId)
            || !TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            error = "The floor does not exist.";
            return false;
        }

        var interiorSize = GetInteriorSize(buildingInstanceId);
        if (!BuildingFootprint.IsUsableInteriorPosition(position, interiorSize))
        {
            error = "The recovery position is outside the usable interior.";
            return false;
        }

        var targetCell = Vector2Int.FloorToInt(position);
        foreach (var candidate in floor.Entities)
        {
            if (candidate is not null
                && candidate.EntityId != entityId
                && BuildingFootprint.IsUsableInteriorPosition(candidate.LogicalPosition, interiorSize)
                && Vector2Int.FloorToInt(candidate.LogicalPosition) == targetCell)
            {
                error = "That cell is occupied.";
                return false;
            }
        }

        if (!floor.TryGetEntity(entityId, out var entity))
        {
            error = "The selected recovery entity no longer exists.";
            return false;
        }

        entity.SetLogicalPosition(position);
        ResumeRecoveryTruckRoutes();
        return true;
    }

    public bool TryDrainTestMachine(
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        out int removed,
        out string error)
    {
        return TryDrainTestEntity(
            buildingInstanceId,
            floorIndex,
            entityId,
            out removed,
            out error);
    }

    public bool TryDrainTestEntity(
        uint buildingInstanceId,
        int floorIndex,
        uint entityId,
        out int removed,
        out string error)
    {
        removed = 0;
        error = string.Empty;
        if (!buildingRecords.ContainsKey(buildingInstanceId)
            || !TryGetFloorState(buildingInstanceId, floorIndex, out var floor))
        {
            error = "The floor does not exist.";
            return false;
        }

        if (entityId == 0)
        {
            error = "The selected entity no longer exists.";
            return false;
        }

        foreach (var entity in floor.Entities)
        {
            if (entity is not null && entity.EntityId == entityId)
            {
                removed = entity.DrainOutput();
                return true;
            }
        }

        error = "The selected entity no longer exists.";
        return false;
    }

    public bool TryAddConnection(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out string error)
    {
        return TryAddConnection(
            new FactoryEntityConnectionRecord(source, destination),
            out error);
    }

    public bool TryConnect(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out string error)
    {
        return TryAddConnection(source, destination, out error);
    }

    public bool TryAddConnection(
        FactoryEntityConnectionRecord connection,
        out string error)
    {
        error = string.Empty;
        if (connection is null)
        {
            error = "Connection is required.";
            return false;
        }

        if (!TryValidateConnection(
                connection,
                buildingRecords,
                floorStates,
                connections,
                out error))
        {
            return false;
        }

        if (!TryValidateRemoteRoleAvailability(
                connection.Source,
                connection.Destination,
                out error))
        {
            return false;
        }

        var ownedConnection = connection.Clone();
        if (ownedConnection.Guid == Guid.Empty)
        {
            ownedConnection = new FactoryEntityConnectionRecord(
                ownedConnection.Source,
                ownedConnection.Destination);
        }

        connections.Add(ownedConnection);
        connections.Sort(CompareConnections);
        return true;
    }

    public bool TryRemoveConnection(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out string error)
    {
        error = string.Empty;
        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            if (EndpointsMatch(connection.Source, source)
                && EndpointsMatch(connection.Destination, destination))
            {
                connections.RemoveAt(index);
                return true;
            }
        }

        error = "The connection does not exist.";
        return false;
    }

    public bool TryRemoveConnection(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination)
    {
        return TryRemoveConnection(source, destination, out _);
    }

    public bool TryDisconnect(
        FactoryEntityEndpoint endpoint,
        out FactoryEntityConnectionRecord removedConnection,
        out string error)
    {
        return TryRemoveConnectionForEndpoint(endpoint, out removedConnection, out error);
    }

    public bool TryRemoveConnectionForEndpoint(
        FactoryEntityEndpoint endpoint,
        out FactoryEntityConnectionRecord removedConnection,
        out string error)
    {
        error = string.Empty;
        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            if (!EndpointsMatch(connection.Source, endpoint)
                && !EndpointsMatch(connection.Destination, endpoint))
            {
                continue;
            }

            removedConnection = connection.Clone();
            connections.RemoveAt(index);
            return true;
        }

        removedConnection = null!;
        error = "The selected entity has no connection.";
        return false;
    }

    public bool TryDisconnect(
        FactoryEntityEndpoint endpoint,
        FactoryEntityConnectionDirection direction,
        out FactoryEntityConnectionRecord removedConnection,
        out string error)
    {
        return TryRemoveConnectionForEndpoint(
            endpoint,
            direction,
            out removedConnection,
            out error);
    }

    public bool TryRemoveConnectionForEndpoint(
        FactoryEntityEndpoint endpoint,
        FactoryEntityConnectionDirection direction,
        out FactoryEntityConnectionRecord removedConnection,
        out string error)
    {
        error = string.Empty;
        for (var index = 0; index < connections.Count; index++)
        {
            var connection = connections[index];
            var matches = direction == FactoryEntityConnectionDirection.Outgoing
                ? EndpointsMatch(connection.Source, endpoint)
                : EndpointsMatch(connection.Destination, endpoint);
            if (!matches)
            {
                continue;
            }

            removedConnection = connection.Clone();
            connections.RemoveAt(index);
            return true;
        }

        removedConnection = null!;
        error = direction == FactoryEntityConnectionDirection.Outgoing
            ? "The selected entity has no outgoing connection."
            : "The selected entity has no incoming connection.";
        return false;
    }

    public bool TryGetConnectionForSource(
        FactoryEntityEndpoint source,
        out FactoryEntityConnectionRecord connection)
    {
        foreach (var candidate in connections)
        {
            if (EndpointsMatch(candidate.Source, source))
            {
                connection = candidate;
                return true;
            }
        }

        connection = null!;
        return false;
    }

    public bool TryGetConnectionForDestination(
        FactoryEntityEndpoint destination,
        out FactoryEntityConnectionRecord connection)
    {
        foreach (var candidate in connections)
        {
            if (EndpointsMatch(candidate.Destination, destination))
            {
                connection = candidate;
                return true;
            }
        }

        connection = null!;
        return false;
    }

    public bool TryValidateConnections(out string error)
    {
        var seenConnections = new List<FactoryEntityConnectionRecord>();
        foreach (var connection in connections)
        {
            if (!TryValidateConnection(
                    connection,
                    buildingRecords,
                    floorStates,
                    seenConnections,
                    out error))
            {
                return false;
            }

            if (!TryValidateRemoteRoleAvailability(
                    connection.Source,
                    connection.Destination,
                    out error))
            {
                return false;
            }

            seenConnections.Add(connection);
        }

        error = string.Empty;
        return true;
    }

    public bool TryCreateTruckRoute(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out FactoryTruckRouteRecord route,
        out FactoryTruckRecord truck,
        out string error)
    {
        route = null!;
        truck = null!;
        var routeGuid = Guid.NewGuid();
        var truckGuid = Guid.NewGuid();
        var candidateRoute = new FactoryTruckRouteRecord(
            routeGuid,
            truckGuid,
            source,
            destination);
        var candidateTruck = new FactoryTruckRecord(
            truckGuid,
            routeGuid,
            FactoryTruckState.Loading,
            string.Empty,
            0,
            0f,
            0f,
            string.Empty);
        if (!TryAddTruckRoute(candidateRoute, candidateTruck, out error))
        {
            return false;
        }

        route = candidateRoute;
        truck = candidateTruck;
        return true;
    }

    public bool TryCreateTruckRoute(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out Guid routeGuid,
        out Guid truckGuid,
        out string error)
    {
        routeGuid = Guid.Empty;
        truckGuid = Guid.Empty;
        if (!TryCreateTruckRoute(
                source,
                destination,
                out FactoryTruckRouteRecord route,
                out FactoryTruckRecord truck,
                out error))
        {
            return false;
        }

        routeGuid = route.Guid;
        truckGuid = truck.Guid;
        return true;
    }

    public bool TryAddTruckRoute(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        out string error)
    {
        error = string.Empty;
        if (route is null || truck is null)
        {
            error = "Truck route and truck records are required.";
            return false;
        }

        if (route.Guid == Guid.Empty || route.TruckGuid == Guid.Empty
            || truck.Guid == Guid.Empty || truck.RouteGuid == Guid.Empty
            || route.TruckGuid != truck.Guid || route.Guid != truck.RouteGuid)
        {
            error = "Truck route and truck identities must be stable and agree.";
            return false;
        }

        if (truckRoutes.Exists(candidate => candidate.Guid == route.Guid))
        {
            error = $"Truck route {route.Guid:D} already exists.";
            return false;
        }

        if (trucks.Exists(candidate => candidate.Guid == truck.Guid))
        {
            error = $"Truck {truck.Guid:D} already exists.";
            return false;
        }

        if (!TryValidateTruckRoute(
                route,
                truck,
                truckRoutes,
                out error))
        {
            return false;
        }

        var ownedRoute = route.Clone();
        var ownedTruck = truck.Clone();
        ownedTruck.ConfigureCargoCapacity(ownedRoute);
        truckRoutes.Add(ownedRoute);
        trucks.Add(ownedTruck);
        SortTruckRecords();
        return true;
    }

    public bool TryRestoreTruckRoutes(
        IEnumerable<FactoryTruckRouteRecord> routes,
        IEnumerable<FactoryTruckRecord> restoredTrucks,
        out string error)
    {
        error = string.Empty;
        var routeList = new List<FactoryTruckRouteRecord>();
        var truckList = new List<FactoryTruckRecord>();
        if (routes is null || restoredTrucks is null)
        {
            error = "Truck route and truck collections are required.";
            return false;
        }

        foreach (var route in routes)
        {
            if (route is null)
            {
                error = "Truck route collection contains a null record.";
                return false;
            }

            routeList.Add(route.Clone());
        }

        foreach (var truck in restoredTrucks)
        {
            if (truck is null)
            {
                error = "Truck collection contains a null record.";
                return false;
            }

            truckList.Add(truck.Clone());
        }

        if (routeList.Count != truckList.Count)
        {
            error = "Every truck route must have exactly one truck.";
            return false;
        }

        var validatedRoutes = new List<FactoryTruckRouteRecord>();
        var validatedTrucks = new List<FactoryTruckRecord>();
        foreach (var route in routeList)
        {
            var matchingTruck = truckList.Find(candidate => candidate.RouteGuid == route.Guid);
            if (matchingTruck is null)
            {
                error = $"Truck route {route.Guid:D} has no assigned truck.";
                return false;
            }

            if (!TryValidateTruckRoute(
                    route,
                    matchingTruck,
                    validatedRoutes,
                    out error))
            {
                return false;
            }

            matchingTruck.ConfigureCargoCapacity(route);
            validatedRoutes.Add(route);
            validatedTrucks.Add(matchingTruck);
        }

        truckRoutes.Clear();
        trucks.Clear();
        truckRoutes.AddRange(validatedRoutes);
        trucks.AddRange(validatedTrucks);
        SortTruckRecords();
        return true;
    }

    public bool TryGetTruckRoute(Guid routeGuid, out FactoryTruckRouteRecord route)
    {
        foreach (var candidate in truckRoutes)
        {
            if (candidate.Guid == routeGuid)
            {
                route = candidate;
                return true;
            }
        }

        route = null!;
        return false;
    }

    public bool TryGetTruck(Guid truckGuid, out FactoryTruckRecord truck)
    {
        foreach (var candidate in trucks)
        {
            if (candidate.Guid == truckGuid)
            {
                truck = candidate;
                return true;
            }
        }

        truck = null!;
        return false;
    }

    public bool TryRemoveTruckRoute(Guid routeGuid, out string error)
    {
        error = string.Empty;
        var routeIndex = truckRoutes.FindIndex(candidate => candidate.Guid == routeGuid);
        if (routeIndex < 0)
        {
            error = "The truck route does not exist.";
            return false;
        }

        var route = truckRoutes[routeIndex];
        var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);
        if (truck is not null && truck.CargoCount > 0)
        {
            error = "A truck route can only be deleted when its cargo is empty.";
            return false;
        }

        truckRoutes.RemoveAt(routeIndex);
        trucks.RemoveAll(candidate => candidate.Guid == route.TruckGuid);
        return true;
    }

    public bool TryDeleteTruckRoute(Guid routeGuid, out string error)
    {
        return TryRemoveTruckRoute(routeGuid, out error);
    }

    public void ClearTruckRoutes()
    {
        truckRoutes.Clear();
        trucks.Clear();
    }

    public bool TrySetFloorState(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
        if (!floorStates.TryGetValue(key, out var state))
        {
            return false;
        }

        state.SetState(
            label,
            productionRate,
            state.AccumulatedProduction,
            ClampMarkerPosition(buildingInstanceId, markerPosition));
        return true;
    }

    public Vector2 ClampMarkerPosition(uint buildingInstanceId, Vector2 markerPosition)
    {
        if (!buildingRecords.TryGetValue(buildingInstanceId, out var registration))
        {
            return OutsideTestFloorRecord.SanitizeMarkerPosition(markerPosition);
        }

        return OutsideTestFloorRecord.ClampMarkerPosition(
            markerPosition,
            GetInteriorSize(registration.BuildingInstanceId));
    }

    public void AdvanceProduction(float deltaTime)
    {
        foreach (var state in floorStates.Values)
        {
            state.Advance(
                deltaTime,
                GetInteriorSize(state.BuildingInstanceId));
        }

        TransferOneItemPerConnection();
        foreach (var state in floorStates.Values)
        {
            FactoryConveyor.TransferAdjacent(
                state.GetUsableEntities(GetInteriorSize(state.BuildingInstanceId)));
        }

        AdvanceTruckRoutes(deltaTime);
    }

    public bool ApplySnapshot(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerPosition,
        FactoryEntitySnapshot[] entitySnapshots)
    {
        if (buildingInstanceId == 0 || floorIndex < 0)
        {
            return false;
        }

        var hasRegistration = buildingRecords.TryGetValue(
            buildingInstanceId,
            out var registration);
        if (hasRegistration
            && floorIndex >= registration.StoryCount)
        {
            return false;
        }

        var key = new OutsideTestFloorKey(buildingInstanceId, floorIndex);
        if (!floorStates.TryGetValue(key, out var state))
        {
            state = new OutsideTestFloorRecord(
                buildingInstanceId,
                floorIndex,
                label,
                productionRate,
                accumulatedProduction,
                ClampMarkerPosition(buildingInstanceId, markerPosition));
        }

        state.SetState(
            label,
            productionRate,
            accumulatedProduction,
            ClampMarkerPosition(buildingInstanceId, markerPosition));
        state.SetEntitySnapshots(entitySnapshots);
        if (hasRegistration)
        {
            NormalizeFloorState(registration, state);
        }

        if (!floorStates.ContainsKey(key))
        {
            floorStates.Add(key, state);
        }

        return true;
    }

    public bool LoadFromFile(string path)
    {
        return LoadFromFile(
            path,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);
    }

    public bool LoadFromFile(string path, float doorCornerExclusionDistance)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var data = new OutsideTestFloorSqliteStore().Load(path);
            return LoadState(data, doorCornerExclusionDistance);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool LoadState(
        OutsideTestFloorSaveData data,
        float doorCornerExclusionDistance)
    {
        try
        {
            return ApplyLoadedState(data, doorCornerExclusionDistance);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool LoadState(OutsideTestFloorSaveData data)
    {
        return LoadState(
            data,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);
    }

    public OutsideTestFloorSaveData CaptureState()
    {
        var buildings = new List<BuildingRecord>();
        foreach (var building in GetSortedBuildingRecords())
        {
            buildings.Add(building.Clone());
        }

        var floors = new List<OutsideTestFloorRecord>();
        foreach (var floor in GetSortedFloorStates())
        {
            var copy = new OutsideTestFloorRecord(
                floor.BuildingInstanceId,
                floor.FloorIndex,
                floor.Label,
                floor.ProductionRate,
                floor.AccumulatedProduction,
                floor.MarkerPosition);
            copy.SetEntities(floor.Entities);
            floors.Add(copy);
        }

        return new OutsideTestFloorSaveData(
            buildings,
            floors,
            GetSortedConnections())
        {
            Version = CurrentSaveVersion
        };
    }

    private bool ApplyLoadedState(
        OutsideTestFloorSaveData data,
        float doorCornerExclusionDistance)
    {
        if (data is null || data.Floors is null)
        {
            return false;
        }

        var version = data.Version <= 0 ? 1 : data.Version;
        if (version > CurrentSaveVersion)
        {
            return false;
        }

        // Scene registration rebuilds this classification after persisted state loads.
        var previousInteriorOnlyBuildingIds = new HashSet<uint>(interiorOnlyBuildingIds);
        interiorOnlyBuildingIds.Clear();

        var loadedBuildingRecords = new Dictionary<uint, BuildingRecord>();
        if (version >= BuildingRecordsSaveVersion)
        {
            if (data.Buildings is not null
                && BuildingShellValidation.TryValidateRecords(
                    data.Buildings,
                    doorCornerExclusionDistance,
                    out _))
            {
                foreach (var savedRecord in data.Buildings)
                {
                    if (savedRecord is null
                        || !loadedBuildingRecords.TryAdd(
                            savedRecord.BuildingInstanceId,
                            savedRecord.Clone()))
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }
        else
        {
            foreach (var pair in buildingRecords)
            {
                loadedBuildingRecords.Add(pair.Key, pair.Value.Clone());
            }
        }

        foreach (var buildingInstanceId in previousInteriorOnlyBuildingIds)
        {
            if (loadedBuildingRecords.ContainsKey(buildingInstanceId))
            {
                interiorOnlyBuildingIds.Add(buildingInstanceId);
            }
        }

        var loadedFloorStates = new Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord>();
        foreach (var savedState in data.Floors)
        {
            if (savedState is null || savedState.FloorIndex < 0)
            {
                return false;
            }

            var buildingInstanceId = savedState.BuildingInstanceId;
            if (version == 1 && buildingInstanceId == 0)
            {
                buildingInstanceId = legacyBuildingInstanceId;
            }

            if (buildingInstanceId == 0)
            {
                return false;
            }

            if (loadedBuildingRecords.TryGetValue(
                    buildingInstanceId,
                    out var registration)
                && savedState.FloorIndex >= registration.StoryCount)
            {
                return false;
            }

            var migratedState = new OutsideTestFloorRecord(
                buildingInstanceId,
                savedState.FloorIndex,
                savedState.Label,
                savedState.ProductionRate,
                savedState.AccumulatedProduction,
                savedState.MarkerPosition);
            migratedState.SetEntities(savedState.Entities);
            if (version < OutputBuffersSaveVersion)
            {
                foreach (var entity in migratedState.Entities)
                {
                    if (entity is not null)
                    {
                        entity.DrainOutput();
                    }
                }
            }

            if (version < InputBuffersSaveVersion)
            {
                foreach (var entity in migratedState.Entities)
                {
                    if (entity is not null)
                    {
                        entity.SetInputCount(0);
                    }
                }
            }

            if (version < 3 && migratedState.Entities.Count == 0)
            {
                migratedState.EnsureDefaultEntity();
            }

            if (loadedBuildingRecords.TryGetValue(
                    buildingInstanceId,
                    out registration))
            {
                NormalizeFloorState(registration, migratedState);
            }

            var key = new OutsideTestFloorKey(
                buildingInstanceId,
                savedState.FloorIndex);
            if (!loadedFloorStates.TryAdd(key, migratedState))
            {
                return false;
            }
        }

        foreach (var registration in loadedBuildingRecords.Values)
        {
            for (var floorIndex = 0;
                floorIndex < registration.StoryCount;
                floorIndex++)
            {
                var key = new OutsideTestFloorKey(
                    registration.BuildingInstanceId,
                    floorIndex);
                if (!loadedFloorStates.TryGetValue(key, out var state))
                {
                    state = OutsideTestFloorRecord.CreateDefault(
                        registration.BuildingInstanceId,
                        floorIndex);
                    loadedFloorStates.Add(key, state);
                }

                NormalizeFloorState(registration, state);
            }
        }

        var loadedConnections = new List<FactoryEntityConnectionRecord>();
        if (version >= ConnectionsSaveVersion)
        {
            if (data.Connections is not null)
            {
                foreach (var connection in data.Connections)
                {
                    if (connection is null
                        || !TryValidateConnection(
                            connection,
                            loadedBuildingRecords,
                            loadedFloorStates,
                            loadedConnections,
                            out _))
                    {
                        return false;
                    }

                    loadedConnections.Add(connection.Clone());
                }

                loadedConnections.Sort(CompareConnections);
            }
            else
            {
                return false;
            }
        }

        buildingRecords.Clear();
        foreach (var pair in loadedBuildingRecords)
        {
            buildingRecords.Add(pair.Key, pair.Value);
        }

        floorStates.Clear();
        foreach (var pair in loadedFloorStates)
        {
            floorStates.Add(pair.Key, pair.Value);
        }

        connections.Clear();
        connections.AddRange(loadedConnections);

        lastLoadedVersion = version;
        lastLoadHadBuildingRecords = version >= BuildingRecordsSaveVersion;
        return true;
    }

    public bool SaveToFile(string path)
    {
        try
        {
            new OutsideTestFloorSqliteStore().Save(
                path,
                GetSortedBuildingRecords(),
                GetSortedFloorStates(),
                GetSortedConnections());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private IEnumerable<OutsideTestBuildingInfo> GetBuildingInfos()
    {
        foreach (var record in buildingRecords.Values)
        {
            yield return new OutsideTestBuildingInfo(record);
        }
    }

    private void NormalizeBuildingFloorStates(BuildingRecord registration)
    {
        for (var floorIndex = 0; floorIndex < registration.StoryCount; floorIndex++)
        {
            var key = new OutsideTestFloorKey(
                registration.BuildingInstanceId,
                floorIndex);
            if (!floorStates.TryGetValue(key, out var state))
            {
                state = OutsideTestFloorRecord.CreateDefault(
                    registration.BuildingInstanceId,
                    floorIndex);
                floorStates.Add(key, state);
            }

            NormalizeFloorState(registration, state);
        }
    }

    private void NormalizeFloorState(
        BuildingRecord registration,
        OutsideTestFloorRecord state)
    {
        var interiorSize = interiorOnlyBuildingIds.Contains(registration.BuildingInstanceId)
            ? registration.FootprintSize
            : BuildingFootprint.GetUsableInteriorSize(registration.FootprintSize);
        state.SetState(
            state.Label,
            state.ProductionRate,
            state.AccumulatedProduction,
            OutsideTestFloorRecord.ClampMarkerPosition(
                state.MarkerPosition,
                interiorSize));
        state.ReconcileEntityPositions(
            interiorSize);
    }

    private Vector2Int GetInteriorSize(uint buildingInstanceId)
    {
        return buildingRecords.TryGetValue(buildingInstanceId, out var registration)
            ? interiorOnlyBuildingIds.Contains(buildingInstanceId)
                ? registration.FootprintSize
                : BuildingFootprint.GetUsableInteriorSize(registration.FootprintSize)
            : Vector2Int.zero;
    }

    private List<BuildingRecord> GetSortedBuildingRecords()
    {
        var result = new List<BuildingRecord>(buildingRecords.Values);
        result.Sort((left, right) => left.BuildingInstanceId.CompareTo(right.BuildingInstanceId));
        return result;
    }

    private List<OutsideTestFloorRecord> GetSortedFloorStates()
    {
        var result = new List<OutsideTestFloorRecord>(floorStates.Values);
        result.Sort((left, right) =>
        {
            var buildingComparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
            return buildingComparison != 0
                ? buildingComparison
                : left.FloorIndex.CompareTo(right.FloorIndex);
        });
        return result;
    }

    private List<FactoryEntityConnectionRecord> GetSortedConnections()
    {
        var result = new List<FactoryEntityConnectionRecord>(connections.Count);
        foreach (var connection in connections)
        {
            result.Add(connection.Clone());
        }

        result.Sort(CompareConnections);
        return result;
    }

    private static int CompareConnections(
        FactoryEntityConnectionRecord left,
        FactoryEntityConnectionRecord right)
    {
        var sourceBuildingComparison = left.Source.BuildingInstanceId.CompareTo(
            right.Source.BuildingInstanceId);
        if (sourceBuildingComparison != 0)
        {
            return sourceBuildingComparison;
        }

        var sourceFloorComparison = left.Source.FloorIndex.CompareTo(right.Source.FloorIndex);
        if (sourceFloorComparison != 0)
        {
            return sourceFloorComparison;
        }

        var sourceEntityComparison = left.Source.EntityId.CompareTo(right.Source.EntityId);
        if (sourceEntityComparison != 0)
        {
            return sourceEntityComparison;
        }

        var destinationBuildingComparison = left.Destination.BuildingInstanceId.CompareTo(
            right.Destination.BuildingInstanceId);
        if (destinationBuildingComparison != 0)
        {
            return destinationBuildingComparison;
        }

        var destinationFloorComparison = left.Destination.FloorIndex.CompareTo(
            right.Destination.FloorIndex);
        return destinationFloorComparison != 0
            ? destinationFloorComparison
            : left.Destination.EntityId.CompareTo(right.Destination.EntityId);
    }

    private static bool TryValidateConnection(
        FactoryEntityConnectionRecord connection,
        IReadOnlyDictionary<uint, BuildingRecord> availableBuildings,
        IReadOnlyDictionary<OutsideTestFloorKey, OutsideTestFloorRecord> availableFloors,
        IReadOnlyList<FactoryEntityConnectionRecord> existingConnections,
        out string error)
    {
        error = string.Empty;
        if (connection is null)
        {
            error = "Connection is required.";
            return false;
        }

        var source = connection.Source;
        var destination = connection.Destination;
        if (source.BuildingInstanceId == 0
            || destination.BuildingInstanceId == 0
            || source.EntityId == 0
            || destination.EntityId == 0
            || source.FloorIndex < 0
            || destination.FloorIndex < 0)
        {
            error = "Connection endpoints must contain valid building, floor, and entity IDs.";
            return false;
        }

        if (availableBuildings is not null
            && (!availableBuildings.ContainsKey(source.BuildingInstanceId)
                || !availableBuildings.ContainsKey(destination.BuildingInstanceId)))
        {
            error = !availableBuildings.ContainsKey(source.BuildingInstanceId)
                ? $"Building {source.BuildingInstanceId} does not exist."
                : $"Building {destination.BuildingInstanceId} does not exist.";
            return false;
        }

        var sourceKey = new OutsideTestFloorKey(source.BuildingInstanceId, source.FloorIndex);
        var destinationKey = new OutsideTestFloorKey(
            destination.BuildingInstanceId,
            destination.FloorIndex);
        if (!availableFloors.TryGetValue(sourceKey, out var sourceFloor)
            || !availableFloors.TryGetValue(destinationKey, out var destinationFloor))
        {
            error = "Connection endpoints must refer to existing floors.";
            return false;
        }

        if (!sourceFloor.TryGetEntity(source.EntityId, out var sourceEntity)
            || !destinationFloor.TryGetEntity(destination.EntityId, out var destinationEntity))
        {
            error = "Connection endpoints must refer to existing entities.";
            return false;
        }

        if (source.BuildingInstanceId == destination.BuildingInstanceId
            && source.FloorIndex == destination.FloorIndex
            && source.EntityId == destination.EntityId)
        {
            error = "A connection cannot target its own endpoint.";
            return false;
        }

        if (!FactoryConnectionRules.TryValidate(
                FactoryEntityDefinitions.Get(sourceEntity.DefinitionId),
                FactoryEntityDefinitions.Get(destinationEntity.DefinitionId),
                source.BuildingInstanceId == destination.BuildingInstanceId,
                source.FloorIndex == destination.FloorIndex,
                out error))
        {
            return false;
        }

        foreach (var existingConnection in existingConnections)
        {
            if (EndpointsMatch(existingConnection.Source, source))
            {
                error = $"Source endpoint {source} is already connected.";
                return false;
            }

            if (EndpointsMatch(existingConnection.Destination, destination))
            {
                error = $"Destination endpoint {destination} is already connected.";
                return false;
            }
        }

        return true;
    }

    private bool TryValidateTruckRoute(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        IReadOnlyList<FactoryTruckRouteRecord> existingRoutes,
        out string error)
    {
        error = string.Empty;
        if (route.Guid == Guid.Empty || route.TruckGuid == Guid.Empty
            || truck.Guid == Guid.Empty || truck.RouteGuid == Guid.Empty
            || route.TruckGuid != truck.Guid || route.Guid != truck.RouteGuid)
        {
            error = "Truck route and truck identities must be stable and agree.";
            return false;
        }

        if (route.Source.BuildingInstanceId == 0
            || route.Destination.BuildingInstanceId == 0
            || route.Source.FloorIndex < 0
            || route.Destination.FloorIndex < 0
            || route.Source.EntityId == 0
            || route.Destination.EntityId == 0)
        {
            if (truck.State != FactoryTruckState.Blocked
                || string.IsNullOrWhiteSpace(truck.BlockingReason))
            {
                error = "A truck route must resolve both terminal endpoints unless deliberately blocked.";
                return false;
            }
        }

        if (route.Source.BuildingGuid == Guid.Empty
            || route.Source.FloorGuid == Guid.Empty
            || route.Source.EntityGuid == Guid.Empty
            || route.Destination.BuildingGuid == Guid.Empty
            || route.Destination.FloorGuid == Guid.Empty
            || route.Destination.EntityGuid == Guid.Empty)
        {
            error = "Truck route endpoints must contain stable GUIDs.";
            return false;
        }

        if ((route.Source.BuildingInstanceId != 0
                && route.Source.BuildingInstanceId == route.Destination.BuildingInstanceId)
            || route.Source.BuildingGuid == route.Destination.BuildingGuid)
        {
            error = "A truck route requires two distinct buildings.";
            return false;
        }

        if (route.ItemId != FactoryEntityDefinitions.TestProductId
            || route.CargoCapacity <= 0
            || route.TransferRateItemsPerSecond <= 0f
            || route.OutboundTravelSeconds <= 0f
            || route.ReturnTravelSeconds <= 0f
            || route.PartialLoadDepartureWindowSeconds < 0f
            || float.IsNaN(route.TransferRateItemsPerSecond)
            || float.IsInfinity(route.TransferRateItemsPerSecond)
            || float.IsNaN(route.OutboundTravelSeconds)
            || float.IsInfinity(route.OutboundTravelSeconds)
            || float.IsNaN(route.ReturnTravelSeconds)
            || float.IsInfinity(route.ReturnTravelSeconds)
            || float.IsNaN(route.PartialLoadDepartureWindowSeconds)
            || float.IsInfinity(route.PartialLoadDepartureWindowSeconds))
        {
            error = "Truck route configuration is invalid.";
            return false;
        }

        if (truck.CargoCount < 0 || truck.CargoCount > route.CargoCapacity)
        {
            error = "Truck cargo exceeds its configured capacity.";
            return false;
        }

        if (truck.CargoCount == 0 && !string.IsNullOrWhiteSpace(truck.CargoItemId))
        {
            error = "An empty truck cannot retain a cargo item identity.";
            return false;
        }

        if (truck.CargoCount > 0 && truck.CargoItemId != route.ItemId)
        {
            error = "Truck cargo item does not match the route item.";
            return false;
        }

        if (truck.State == FactoryTruckState.Blocked
            && string.IsNullOrWhiteSpace(truck.BlockingReason))
        {
            error = "A blocked truck must retain a readable blocking reason.";
            return false;
        }

        if (truck.State == FactoryTruckState.Returning && truck.CargoCount != 0)
        {
            error = "A returning truck must have unloaded all cargo.";
            return false;
        }

        if (truck.RemainingTravelSeconds < 0f
            || truck.LoadingWindowProgress < 0f
            || float.IsNaN(truck.RemainingTravelSeconds)
            || float.IsInfinity(truck.RemainingTravelSeconds)
            || float.IsNaN(truck.LoadingWindowProgress)
            || float.IsInfinity(truck.LoadingWindowProgress))
        {
            error = "Truck progress values must be finite and non-negative.";
            return false;
        }

        if (TryGetEntity(route.Source, out var sourceEntity)
            && TryGetEntity(route.Destination, out var destinationEntity))
        {
            if (!sourceEntity.IsShippingDock || !destinationEntity.IsReceivingDock)
            {
                error = "Truck routes must connect a shipping dock to a receiving dock.";
                return false;
            }
        }
        else if (truck.State != FactoryTruckState.Blocked)
        {
            error = "Truck route endpoints must refer to existing terminal entities.";
            return false;
        }

        foreach (var existingRoute in existingRoutes)
        {
            if (existingRoute.Guid == route.Guid
                || existingRoute.TruckGuid == route.TruckGuid)
            {
                error = "Truck route and truck GUIDs must be unique.";
                return false;
            }

            if (EndpointsMatch(existingRoute.Source, route.Source))
            {
                error = "The sending terminal already has a truck assignment.";
                return false;
            }

            if (EndpointsMatch(existingRoute.Destination, route.Destination))
            {
                error = "The receiving terminal already has a truck assignment.";
                return false;
            }
        }

        if (!TryValidateRemoteRoleAvailability(
                route.Source,
                route.Destination,
                out error))
        {
            return false;
        }

        foreach (var connection in connections)
        {
            if (EndpointsMatch(connection.Source, route.Source))
            {
                error = "The sending terminal's outgoing remote role already has an explicit connection.";
                return false;
            }

            if (EndpointsMatch(connection.Destination, route.Destination))
            {
                error = "The receiving terminal's incoming remote role already has an explicit connection.";
                return false;
            }
        }

        return true;
    }

    private bool TryValidateRemoteRoleAvailability(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination,
        out string error)
    {
        foreach (var route in truckRoutes)
        {
            if (EndpointsMatch(route.Source, source))
            {
                error = "The sending terminal's outgoing remote role is already assigned to a truck route.";
                return false;
            }

            if (EndpointsMatch(route.Destination, destination))
            {
                error = "The receiving terminal's incoming remote role is already assigned to a truck route.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private void AdvanceTruckRoutes(float deltaTime)
    {
        if (float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime)
            || deltaTime <= 0f)
        {
            return;
        }

        foreach (var route in truckRoutes)
        {
            var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);
            if (truck is null)
            {
                continue;
            }

            if (truck.State == FactoryTruckState.Blocked)
            {
                if (!truck.BlockingReason.StartsWith("Equipment recovery", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryGetEntity(route.Source, out var sourceEntity)
                    || !TryGetEntity(route.Destination, out var destinationEntity)
                    || !IsUsableEntity(route.Source, sourceEntity)
                    || !IsUsableEntity(route.Destination, destinationEntity)
                    || !truck.TryResumeFromRecovery())
                {
                    continue;
                }
            }

            if (!TryGetEntity(route.Source, out var activeSource)
                || !TryGetEntity(route.Destination, out var activeDestination)
                || !IsUsableEntity(route.Source, activeSource)
                || !IsUsableEntity(route.Destination, activeDestination))
            {
                truck.PauseForRecovery("Equipment recovery is required at a route endpoint.");
                continue;
            }

            switch (truck.State)
            {
                case FactoryTruckState.Loading:
                    AdvanceTruckLoading(route, truck, deltaTime);
                    break;
                case FactoryTruckState.Outbound:
                    AdvanceTruckTravel(truck, route.OutboundTravelSeconds, FactoryTruckState.Unloading, deltaTime);
                    break;
                case FactoryTruckState.Unloading:
                    AdvanceTruckUnloading(route, truck, deltaTime);
                    break;
                case FactoryTruckState.Returning:
                    AdvanceTruckTravel(truck, route.ReturnTravelSeconds, FactoryTruckState.Loading, deltaTime);
                    break;
            }
        }
    }

    private void AdvanceTruckLoading(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        float deltaTime)
    {
        if (!TryGetEntity(route.Source, out var source))
        {
            BlockTruck(truck, "Sending terminal is missing.");
            return;
        }

        var wasEmpty = truck.CargoCount == 0;
        var requested = Mathf.Max(
            1,
            Mathf.FloorToInt(route.TransferRateItemsPerSecond * deltaTime + 0.00001f));
        if (truck.CargoCount < route.CargoCapacity)
        {
            FactoryItemTransfer.TryTransfer(source, truck, requested);
        }

        if (truck.CargoCount >= route.CargoCapacity)
        {
            truck.SetState(
                FactoryTruckState.Outbound,
                route.OutboundTravelSeconds,
                truck.LoadingWindowProgress,
                string.Empty);
            return;
        }

        if (truck.CargoCount <= 0)
        {
            truck.SetState(FactoryTruckState.Loading, 0f, 0f, string.Empty);
            return;
        }

        var progress = truck.LoadingWindowProgress;
        if (!wasEmpty)
        {
            progress += deltaTime;
        }

        if (progress >= route.PartialLoadDepartureWindowSeconds)
        {
            truck.SetState(
                FactoryTruckState.Outbound,
                route.OutboundTravelSeconds,
                progress,
                string.Empty);
        }
        else
        {
            truck.SetState(FactoryTruckState.Loading, 0f, progress, string.Empty);
        }
    }

    private void AdvanceTruckUnloading(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        float deltaTime)
    {
        if (!TryGetEntity(route.Destination, out var destination))
        {
            BlockTruck(truck, "Receiving terminal is missing.");
            return;
        }

        var requested = Mathf.Max(
            1,
            Mathf.FloorToInt(route.TransferRateItemsPerSecond * deltaTime + 0.00001f));
        FactoryItemTransfer.TryTransfer(truck, destination, requested);
        if (truck.CargoCount == 0)
        {
            truck.SetState(
                FactoryTruckState.Returning,
                route.ReturnTravelSeconds,
                0f,
                string.Empty);
        }
    }

    private static void AdvanceTruckTravel(
        FactoryTruckRecord truck,
        float journeyDuration,
        FactoryTruckState arrivalState,
        float deltaTime)
    {
        var remaining = Mathf.Max(0f, truck.RemainingTravelSeconds - deltaTime);
        if (remaining <= 0f)
        {
            truck.SetState(arrivalState, 0f, truck.LoadingWindowProgress, string.Empty);
            return;
        }

        truck.SetState(truck.State, remaining, truck.LoadingWindowProgress, string.Empty);
    }

    private static void BlockTruck(FactoryTruckRecord truck, string reason)
    {
        truck.SetState(
            FactoryTruckState.Blocked,
            truck.RemainingTravelSeconds,
            truck.LoadingWindowProgress,
            reason);
    }

    private void ResumeRecoveryTruckRoutes()
    {
        foreach (var route in truckRoutes)
        {
            if (!TryGetEntity(route.Source, out var sourceEntity)
                || !TryGetEntity(route.Destination, out var destinationEntity)
                || !IsUsableEntity(route.Source, sourceEntity)
                || !IsUsableEntity(route.Destination, destinationEntity))
            {
                continue;
            }

            if (TryGetTruck(route.TruckGuid, out var truck)
                && truck.BlockingReason.StartsWith("Equipment recovery", StringComparison.Ordinal))
            {
                truck.TryResumeFromRecovery();
            }
        }
    }

    private void BlockRoutesForEndpoint(FactoryEntityEndpoint endpoint, string reason)
    {
        foreach (var route in truckRoutes)
        {
            if (!EndpointsMatch(route.Source, endpoint)
                && !EndpointsMatch(route.Destination, endpoint))
            {
                continue;
            }

            if (TryGetTruck(route.TruckGuid, out var truck))
            {
                BlockTruck(truck, reason);
            }
        }
    }

    private void BlockRoutesForFloor(uint buildingInstanceId, int floorIndex, string reason)
    {
        foreach (var route in truckRoutes)
        {
            var sourceMatch = route.Source.BuildingInstanceId == buildingInstanceId
                && route.Source.FloorIndex == floorIndex;
            var destinationMatch = route.Destination.BuildingInstanceId == buildingInstanceId
                && route.Destination.FloorIndex == floorIndex;
            if (sourceMatch || destinationMatch)
            {
                BlockRoutesForEndpoint(
                    sourceMatch ? route.Source : route.Destination,
                    reason);
            }
        }
    }

    private void BlockRoutesForBuilding(uint buildingInstanceId, string reason)
    {
        foreach (var route in truckRoutes)
        {
            if (route.Source.BuildingInstanceId == buildingInstanceId)
            {
                BlockRoutesForEndpoint(route.Source, reason);
            }
            else if (route.Destination.BuildingInstanceId == buildingInstanceId)
            {
                BlockRoutesForEndpoint(route.Destination, reason);
            }
        }
    }

    private void SortTruckRecords()
    {
        truckRoutes.Sort((left, right) => left.Guid.CompareTo(right.Guid));
        trucks.Sort((left, right) => left.Guid.CompareTo(right.Guid));
    }

    private void RemoveConnectionsForEndpoint(FactoryEntityEndpoint endpoint)
    {
        for (var index = connections.Count - 1; index >= 0; index--)
        {
            var connection = connections[index];
            if (EndpointsMatch(connection.Source, endpoint)
                || EndpointsMatch(connection.Destination, endpoint))
            {
                connections.RemoveAt(index);
            }
        }
    }

    private void RemoveConnectionsForFloor(uint buildingInstanceId, int floorIndex)
    {
        for (var index = connections.Count - 1; index >= 0; index--)
        {
            var connection = connections[index];
            if ((connection.Source.BuildingInstanceId == buildingInstanceId
                    && connection.Source.FloorIndex == floorIndex)
                || (connection.Destination.BuildingInstanceId == buildingInstanceId
                    && connection.Destination.FloorIndex == floorIndex))
            {
                connections.RemoveAt(index);
            }
        }
    }

    private void RemoveConnectionsForBuilding(uint buildingInstanceId)
    {
        for (var index = connections.Count - 1; index >= 0; index--)
        {
            var connection = connections[index];
            if (connection.Source.BuildingInstanceId == buildingInstanceId
                || connection.Destination.BuildingInstanceId == buildingInstanceId)
            {
                connections.RemoveAt(index);
            }
        }
    }

    private void TransferOneItemPerConnection()
    {
        foreach (var connection in connections)
        {
            if (!TryGetEntity(connection.Source, out var sourceEntity)
                || !TryGetEntity(connection.Destination, out var destinationEntity))
            {
                continue;
            }

            if (!IsUsableEntity(connection.Source, sourceEntity)
                || !IsUsableEntity(connection.Destination, destinationEntity))
            {
                continue;
            }

            FactoryItemTransfer.TryTransfer(sourceEntity, destinationEntity, 1);
        }
    }

    private bool IsUsableEntity(
        FactoryEntityEndpoint endpoint,
        FactoryEntityRecord entity)
    {
        return buildingRecords.TryGetValue(endpoint.BuildingInstanceId, out var building)
            && BuildingFootprint.IsUsableInteriorPosition(
                entity.LogicalPosition,
                GetInteriorSize(endpoint.BuildingInstanceId));
    }

    private bool TryGetEntity(
        FactoryEntityEndpoint endpoint,
        out FactoryEntityRecord entity)
    {
        entity = null!;
        var key = new OutsideTestFloorKey(
            endpoint.BuildingInstanceId,
            endpoint.FloorIndex);
        return floorStates.TryGetValue(key, out var floor)
             && floor.TryGetEntity(endpoint.EntityId, out entity);
    }

    private static bool EndpointsMatch(
        FactoryEntityEndpoint left,
        FactoryEntityEndpoint right)
    {
        if (left.Equals(right))
        {
            return true;
        }

        return left.BuildingInstanceId != 0
            && right.BuildingInstanceId != 0
            && left.EntityId != 0
            && right.EntityId != 0
            && left.FloorIndex >= 0
            && right.FloorIndex >= 0
            && left.BuildingInstanceId == right.BuildingInstanceId
            && left.FloorIndex == right.FloorIndex
            && left.EntityId == right.EntityId;
    }

}
