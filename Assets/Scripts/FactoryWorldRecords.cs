// Defines the GUID-based snapshot records shared by the authoritative state and SQLite repository.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class FactoryWorldBuildingRecord
{
    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private uint legacyBuildingId;
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private Vector2Int footprintSize;
    [SerializeField, Min(1)] private int storyCount = 1;
    [SerializeField] private bool interiorOnly;
    [SerializeField] private List<BuildingRecord.DoorPlacement> doors = new();

    public FactoryWorldBuildingRecord()
    {
    }

    public FactoryWorldBuildingRecord(
        Guid newGuid,
        string newDefinitionId,
        Vector3Int newAnchorCell,
        Vector2Int newFootprintSize,
        int newStoryCount,
        bool newInteriorOnly,
        uint newLegacyBuildingId = 0,
        IEnumerable<BuildingRecord.DoorPlacement> newDoors = null)
    {
        Guid = newGuid;
        definitionId = newDefinitionId ?? string.Empty;
        anchorCell = newAnchorCell;
        footprintSize = newFootprintSize;
        storyCount = newStoryCount;
        interiorOnly = newInteriorOnly;
        legacyBuildingId = newLegacyBuildingId;
        SetDoors(newDoors);
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public uint LegacyBuildingId => legacyBuildingId;
    public string DefinitionId => definitionId;
    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int FootprintSize => footprintSize;
    public int StoryCount => Mathf.Max(1, storyCount);
    public bool InteriorOnly => interiorOnly;
    public IReadOnlyList<BuildingRecord.DoorPlacement> Doors => GetDoors();

    public FactoryWorldBuildingRecord Clone()
    {
        return new FactoryWorldBuildingRecord(
            Guid,
            definitionId,
            anchorCell,
            footprintSize,
            storyCount,
            interiorOnly,
            legacyBuildingId,
            Doors);
    }

    public void SetDoors(IEnumerable<BuildingRecord.DoorPlacement> newDoors)
    {
        var target = GetDoors();
        target.Clear();
        if (newDoors is null)
        {
            return;
        }

        foreach (var door in newDoors)
        {
            if (door is not null)
            {
                target.Add(door.Clone());
            }
        }
    }

    private List<BuildingRecord.DoorPlacement> GetDoors()
    {
        if (doors is null)
        {
            doors = new List<BuildingRecord.DoorPlacement>();
        }

        return doors;
    }
}

[Serializable]
public sealed class FactoryWorldFloorRecord
{
    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private string buildingGuidString = string.Empty;
    [SerializeField] private int floorIndex;
    [SerializeField] private string label = string.Empty;
    [SerializeField] private float productionRate;
    [SerializeField] private float accumulatedProduction;
    [SerializeField] private Vector2 markerPosition;
    [SerializeField] private List<FactoryWorldEntityRecord> entities = new();

    public FactoryWorldFloorRecord()
    {
    }

    public FactoryWorldFloorRecord(
        Guid newGuid,
        Guid newBuildingGuid,
        int newFloorIndex,
        string newLabel,
        float newProductionRate,
        float newAccumulatedProduction,
        Vector2 newMarkerPosition)
    {
        Guid = newGuid;
        BuildingGuid = newBuildingGuid;
        floorIndex = newFloorIndex;
        SetState(
            newLabel,
            newProductionRate,
            newAccumulatedProduction,
            newMarkerPosition);
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public Guid BuildingGuid
    {
        get => FactoryGuidMigration.TryParseCanonical(buildingGuidString, out var value)
            ? value
            : Guid.Empty;
        private set => buildingGuidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public int FloorIndex => floorIndex;
    public string Label => label;
    public float ProductionRate => productionRate;
    public float AccumulatedProduction => accumulatedProduction;
    public Vector2 MarkerPosition => markerPosition;
    public IReadOnlyList<FactoryWorldEntityRecord> Entities => GetEntities();

    public void SetState(
        string newLabel,
        float newProductionRate,
        float newAccumulatedProduction,
        Vector2 newMarkerPosition)
    {
        label = string.IsNullOrWhiteSpace(newLabel) ? $"Floor {floorIndex}" : newLabel.Trim();
        productionRate = float.IsNaN(newProductionRate)
            || float.IsInfinity(newProductionRate)
            ? 0f
            : Mathf.Clamp(newProductionRate, 0f, 1000f);
        accumulatedProduction = float.IsNaN(newAccumulatedProduction)
            || float.IsInfinity(newAccumulatedProduction)
            ? 0f
            : Mathf.Max(0f, newAccumulatedProduction);
        markerPosition = OutsideTestFloorRecord.SanitizeMarkerPosition(newMarkerPosition);
    }

    public void SetEntities(IEnumerable<FactoryWorldEntityRecord> newEntities)
    {
        var target = GetEntities();
        target.Clear();
        if (newEntities is null)
        {
            return;
        }

        foreach (var entity in newEntities)
        {
            if (entity is not null)
            {
                target.Add(entity.Clone());
            }
        }
    }

    public FactoryWorldFloorRecord Clone()
    {
        var result = new FactoryWorldFloorRecord(
            Guid,
            BuildingGuid,
            floorIndex,
            label,
            productionRate,
            accumulatedProduction,
            markerPosition);
        result.SetEntities(Entities);
        return result;
    }

    private List<FactoryWorldEntityRecord> GetEntities()
    {
        if (entities is null)
        {
            entities = new List<FactoryWorldEntityRecord>();
        }

        return entities;
    }
}

[Serializable]
public sealed class FactoryWorldEntityRecord
{
    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private string floorGuidString = string.Empty;
    [SerializeField] private uint legacyEntityId;
    [SerializeField] private string definitionId = string.Empty;
    [SerializeField] private Vector2 localPosition;
    [SerializeField] private float rotationZ;
    [SerializeField] private Vector2 footprint = Vector2.one;
    [SerializeField] private byte[] state = Array.Empty<byte>();
    [SerializeField] private float cycleRate;
    [SerializeField] private float cycleProgress;
    [SerializeField] private int producedCount;
    [SerializeField] private int outputCount;
    [SerializeField] private int inputCount;
    [SerializeField] private bool naiEntity;

    public FactoryWorldEntityRecord()
    {
    }

    public FactoryWorldEntityRecord(
        Guid newGuid,
        Guid newFloorGuid,
        string newDefinitionId,
        Vector2 newLocalPosition,
        float newRotationZ,
        Vector2 newFootprint,
        byte[] newState,
        uint newLegacyEntityId = 0,
        bool newNaiEntity = false,
        float newCycleRate = 0f,
        float newCycleProgress = 0f,
        int newProducedCount = 0,
        int newOutputCount = 0,
        int newInputCount = 0)
    {
        Guid = newGuid;
        FloorGuid = newFloorGuid;
        legacyEntityId = newLegacyEntityId;
        definitionId = string.IsNullOrWhiteSpace(newDefinitionId) ? string.Empty : newDefinitionId.Trim();
        localPosition = newLocalPosition;
        rotationZ = newRotationZ;
        footprint = newFootprint;
        state = newState is null ? Array.Empty<byte>() : (byte[])newState.Clone();
        naiEntity = newNaiEntity;
        cycleRate = newCycleRate;
        cycleProgress = newCycleProgress;
        producedCount = newProducedCount;
        outputCount = newOutputCount;
        inputCount = newInputCount;
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public Guid FloorGuid
    {
        get => FactoryGuidMigration.TryParseCanonical(floorGuidString, out var value)
            ? value
            : Guid.Empty;
        private set => floorGuidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public uint LegacyEntityId => legacyEntityId;
    public string DefinitionId => definitionId;
    public Vector2 LocalPosition => localPosition;
    public float RotationZ => rotationZ;
    public Vector2 Footprint => footprint;
    public byte[] State => state is null ? Array.Empty<byte>() : (byte[])state.Clone();
    public float CycleRate => cycleRate;
    public float CycleProgress => cycleProgress;
    public int ProducedCount => producedCount;
    public int OutputCount => outputCount;
    public int InputCount => inputCount;
    public bool IsNaiEntity => naiEntity;

    public FactoryWorldEntityRecord Clone()
    {
        return new FactoryWorldEntityRecord(
            Guid,
            FloorGuid,
            definitionId,
            localPosition,
            rotationZ,
            footprint,
            state,
            legacyEntityId,
            naiEntity,
            cycleRate,
            cycleProgress,
            producedCount,
            outputCount,
            inputCount);
    }
}

[Serializable]
public readonly struct FactoryWorldEndpoint : IEquatable<FactoryWorldEndpoint>
{
    public FactoryWorldEndpoint(
        Guid newBuildingGuid,
        Guid newFloorGuid,
        Guid newEntityGuid,
        int newFloorIndex)
    {
        BuildingGuid = newBuildingGuid;
        FloorGuid = newFloorGuid;
        EntityGuid = newEntityGuid;
        FloorIndex = newFloorIndex;
    }

    public Guid BuildingGuid { get; }
    public Guid FloorGuid { get; }
    public Guid EntityGuid { get; }
    public int FloorIndex { get; }

    public bool Equals(FactoryWorldEndpoint other)
    {
        return BuildingGuid == other.BuildingGuid
            && FloorGuid == other.FloorGuid
            && EntityGuid == other.EntityGuid;
    }

    public override bool Equals(object obj)
    {
        return obj is FactoryWorldEndpoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(BuildingGuid, FloorGuid, EntityGuid);
    }

    public override string ToString()
    {
        return $"B{BuildingGuid:D}/F{FloorIndex}:{FloorGuid:D}/E{EntityGuid:D}";
    }
}

[Serializable]
public sealed class FactoryWorldConnectionRecord
{
    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private FactoryWorldEndpoint source;
    [SerializeField] private FactoryWorldEndpoint destination;

    public FactoryWorldConnectionRecord()
    {
    }

    public FactoryWorldConnectionRecord(
        Guid newGuid,
        FactoryWorldEndpoint newSource,
        FactoryWorldEndpoint newDestination)
    {
        Guid = newGuid;
        source = newSource;
        destination = newDestination;
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public FactoryWorldEndpoint Source => source;
    public FactoryWorldEndpoint Destination => destination;

    public FactoryWorldConnectionRecord Clone()
    {
        return new FactoryWorldConnectionRecord(Guid, source, destination);
    }
}

[Serializable]
public sealed class FactoryWorldMigrationMapping
{
    public string Kind = string.Empty;
    public string LegacyKey = string.Empty;
    [SerializeField] private string guidString = string.Empty;

    public FactoryWorldMigrationMapping()
    {
    }

    public FactoryWorldMigrationMapping(string newKind, string newLegacyKey, Guid newGuid)
    {
        Kind = newKind ?? string.Empty;
        LegacyKey = newLegacyKey ?? string.Empty;
        Guid = newGuid;
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }
}

[Serializable]
public sealed class FactoryWorldSnapshot
{
    public const int CurrentSchemaVersion = 8;

    public int SchemaVersion = CurrentSchemaVersion;
    public List<FactoryWorldBuildingRecord> Buildings = new();
    public List<FactoryWorldFloorRecord> Floors = new();
    public List<FactoryWorldConnectionRecord> Connections = new();
    public List<FactoryWorldMigrationMapping> MigrationMappings = new();

    public FactoryWorldSnapshot Clone()
    {
        var result = new FactoryWorldSnapshot { SchemaVersion = SchemaVersion };
        foreach (var building in Buildings)
        {
            result.Buildings.Add(building.Clone());
        }

        foreach (var floor in Floors)
        {
            result.Floors.Add(floor.Clone());
        }

        foreach (var connection in Connections)
        {
            result.Connections.Add(connection.Clone());
        }

        foreach (var mapping in MigrationMappings)
        {
            result.MigrationMappings.Add(new FactoryWorldMigrationMapping(
                mapping.Kind,
                mapping.LegacyKey,
                mapping.Guid));
        }

        return result;
    }
}
