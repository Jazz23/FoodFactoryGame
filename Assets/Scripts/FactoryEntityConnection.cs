// Defines the persistent, scene-independent endpoints and directed connections used by factory transport.
using System;
using UnityEngine;

[Serializable]
public struct FactoryEntityEndpoint : IEquatable<FactoryEntityEndpoint>
{
    public FactoryEntityEndpoint(
        uint newBuildingInstanceId,
        int newFloorIndex,
        uint newEntityId)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
        EntityId = newEntityId;
    }

    public uint BuildingInstanceId;
    public int FloorIndex;
    public uint EntityId;
    public uint BuildingId => BuildingInstanceId;
    public int Floor => FloorIndex;

    public bool Equals(FactoryEntityEndpoint other)
    {
        return BuildingInstanceId == other.BuildingInstanceId
            && FloorIndex == other.FloorIndex
            && EntityId == other.EntityId;
    }

    public override bool Equals(object obj)
    {
        return obj is FactoryEntityEndpoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = ((int)BuildingInstanceId * 397) ^ FloorIndex;
            return (hash * 397) ^ (int)EntityId;
        }
    }

    public override string ToString()
    {
        return $"B{BuildingInstanceId}/F{FloorIndex}/E{EntityId}";
    }
}

[Serializable]
public sealed class FactoryEntityConnectionRecord
{
    [SerializeField] private FactoryEntityEndpoint source;
    [SerializeField] private FactoryEntityEndpoint destination;

    public FactoryEntityConnectionRecord()
    {
    }

    public FactoryEntityConnectionRecord(
        FactoryEntityEndpoint newSource,
        FactoryEntityEndpoint newDestination)
    {
        source = newSource;
        destination = newDestination;
    }

    public FactoryEntityEndpoint Source => source;
    public FactoryEntityEndpoint Destination => destination;
    public FactoryEntityEndpoint SourceEndpoint => source;
    public FactoryEntityEndpoint DestinationEndpoint => destination;

    public FactoryEntityConnectionRecord Clone()
    {
        return new FactoryEntityConnectionRecord(source, destination);
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
