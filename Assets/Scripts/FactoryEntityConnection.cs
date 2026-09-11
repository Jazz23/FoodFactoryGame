// Defines the persistent, scene-independent endpoints and directed connections used by factory transport.
using System;
using UnityEngine;

[Serializable]
public struct FactoryEntityEndpoint : IEquatable<FactoryEntityEndpoint>
{
    [SerializeField] private string buildingGuidString;
    [SerializeField] private string floorGuidString;
    [SerializeField] private string entityGuidString;

    public FactoryEntityEndpoint(
        uint newBuildingInstanceId,
        int newFloorIndex,
        uint newEntityId)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
        EntityId = newEntityId;
        buildingGuidString = newBuildingInstanceId == 0
            ? string.Empty
            : FactoryGuidMigration.ForBuilding(newBuildingInstanceId).ToString("D");
        floorGuidString = newBuildingInstanceId == 0 || newFloorIndex < 0
            ? string.Empty
            : FactoryGuidMigration.ForFloor(newBuildingInstanceId, newFloorIndex).ToString("D");
        entityGuidString = newBuildingInstanceId == 0 || newFloorIndex < 0 || newEntityId == 0
            ? string.Empty
            : FactoryGuidMigration.ForEntity(newBuildingInstanceId, newFloorIndex, newEntityId).ToString("D");
    }

    public FactoryEntityEndpoint(
        Guid newBuildingGuid,
        Guid newFloorGuid,
        Guid newEntityGuid,
        int newFloorIndex)
    {
        BuildingInstanceId = 0;
        FloorIndex = newFloorIndex;
        EntityId = 0;
        buildingGuidString = newBuildingGuid == Guid.Empty ? string.Empty : newBuildingGuid.ToString("D");
        floorGuidString = newFloorGuid == Guid.Empty ? string.Empty : newFloorGuid.ToString("D");
        entityGuidString = newEntityGuid == Guid.Empty ? string.Empty : newEntityGuid.ToString("D");
    }

    public FactoryEntityEndpoint(
        uint newBuildingInstanceId,
        int newFloorIndex,
        uint newEntityId,
        Guid newBuildingGuid,
        Guid newFloorGuid,
        Guid newEntityGuid)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
        EntityId = newEntityId;
        buildingGuidString = newBuildingGuid == Guid.Empty ? string.Empty : newBuildingGuid.ToString("D");
        floorGuidString = newFloorGuid == Guid.Empty ? string.Empty : newFloorGuid.ToString("D");
        entityGuidString = newEntityGuid == Guid.Empty ? string.Empty : newEntityGuid.ToString("D");
    }

    public uint BuildingInstanceId;
    public int FloorIndex;
    public uint EntityId;
    public uint BuildingId => BuildingInstanceId;
    public int Floor => FloorIndex;
    public Guid BuildingGuid => GetGuid(buildingGuidString);
    public Guid FloorGuid => GetGuid(floorGuidString);
    public Guid EntityGuid => GetGuid(entityGuidString);

    public bool Equals(FactoryEntityEndpoint other)
    {
        return BuildingGuid == other.BuildingGuid
            && FloorGuid == other.FloorGuid
            && EntityGuid == other.EntityGuid;
    }

    public override bool Equals(object obj)
    {
        return obj is FactoryEntityEndpoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(BuildingGuid, FloorGuid, EntityGuid);
    }

    public override string ToString()
    {
        return $"B{BuildingGuid:D}/F{FloorIndex}:{FloorGuid:D}/E{EntityGuid:D}";
    }

    private static Guid GetGuid(string value)
    {
        return FactoryGuidMigration.TryParseCanonical(value, out var result)
            ? result
            : Guid.Empty;
    }
}

public enum FactoryEntityConnectionDirection
{
    Incoming,
    Outgoing
}

[Serializable]
public sealed class FactoryEntityConnectionRecord
{
    [SerializeField] private string connectionGuidString = string.Empty;
    [SerializeField] private FactoryEntityEndpoint source;
    [SerializeField] private FactoryEntityEndpoint destination;

    public FactoryEntityConnectionRecord()
    {
    }

    public FactoryEntityConnectionRecord(
        FactoryEntityEndpoint newSource,
        FactoryEntityEndpoint newDestination)
    {
        Guid = FactoryGuidMigration.ForConnection(newSource, newDestination);
        source = newSource;
        destination = newDestination;
    }

    public FactoryEntityConnectionRecord(
        Guid newGuid,
        FactoryEntityEndpoint newSource,
        FactoryEntityEndpoint newDestination)
    {
        Guid = newGuid;
        source = newSource;
        destination = newDestination;
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(connectionGuidString, out var value)
            ? value
            : Guid.Empty;
        private set => connectionGuidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public FactoryEntityEndpoint Source => source;
    public FactoryEntityEndpoint Destination => destination;
    public FactoryEntityEndpoint SourceEndpoint => source;
    public FactoryEntityEndpoint DestinationEndpoint => destination;

    public FactoryEntityConnectionRecord Clone()
    {
        return new FactoryEntityConnectionRecord(Guid, source, destination);
    }

    public bool HasSameEndpoints(FactoryEntityConnectionRecord other)
    {
        return other is not null
            && Source.Equals(other.Source)
            && Destination.Equals(other.Destination);
    }

    public override string ToString()
    {
        return $"{Source} -> {Destination}";
    }
}
