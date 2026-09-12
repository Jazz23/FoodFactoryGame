// Stores one independent, serializable OutsideTest floor record and its save-file wrapper.
using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class OutsideTestFloorRecord
{
    [SerializeField] private uint buildingInstanceId;
    [SerializeField] private int floorIndex;
    [SerializeField] private string label = string.Empty;
    [SerializeField] private float productionRate;
    [SerializeField] private float accumulatedProduction;
    [SerializeField] private Vector2 markerPosition;
    [SerializeField] private List<FactoryEntityRecord> entities = null!;

    public OutsideTestFloorRecord()
    {
    }

    public OutsideTestFloorRecord(
        uint newBuildingInstanceId,
        int newFloorIndex,
        string newLabel,
        float newProductionRate,
        float newAccumulatedProduction,
        Vector2 newMarkerPosition)
    {
        buildingInstanceId = newBuildingInstanceId;
        floorIndex = newFloorIndex;
        SetState(
            newLabel,
            newProductionRate,
            newAccumulatedProduction,
            newMarkerPosition);
    }

    public uint BuildingInstanceId => buildingInstanceId;
    public int FloorIndex => floorIndex;
    public string Label => label;
    public float ProductionRate => productionRate;
    public float AccumulatedProduction => accumulatedProduction;
    public Vector2 MarkerPosition => markerPosition;
    public IReadOnlyList<FactoryEntityRecord> Entities => GetEntities();

    public static OutsideTestFloorRecord CreateDefault(
        uint newBuildingInstanceId,
        int newFloorIndex)
    {
        var result = newFloorIndex switch
        {
            0 => new OutsideTestFloorRecord(
                newBuildingInstanceId,
                newFloorIndex,
                "Ground Test Line",
                1f,
                0f,
                new Vector2(1.5f, 1.5f)),
            1 => new OutsideTestFloorRecord(
                newBuildingInstanceId,
                newFloorIndex,
                "Upper Test Line",
                2f,
                0f,
                new Vector2(4.5f, 2.5f)),
            2 => new OutsideTestFloorRecord(
                newBuildingInstanceId,
                newFloorIndex,
                "Roof Test Line",
                0.5f,
                0f,
                new Vector2(2.5f, 4.5f)),
            _ => new OutsideTestFloorRecord(
                newBuildingInstanceId,
                newFloorIndex,
                $"Floor {newFloorIndex}",
                1f,
                0f,
                new Vector2(3f, 3f))
        };
        result.EnsureDefaultEntity();
        return result;
    }

    public void SetState(
        string newLabel,
        float newProductionRate,
        float newAccumulatedProduction,
        Vector2 newMarkerPosition)
    {
        label = string.IsNullOrWhiteSpace(newLabel)
            ? $"Floor {floorIndex}"
            : newLabel.Trim();
        productionRate = float.IsNaN(newProductionRate)
            || float.IsInfinity(newProductionRate)
            ? 0f
            : Mathf.Clamp(newProductionRate, 0f, 1000f);
        accumulatedProduction = float.IsNaN(newAccumulatedProduction)
            || float.IsInfinity(newAccumulatedProduction)
            ? 0f
            : Mathf.Max(0f, newAccumulatedProduction);
        markerPosition = SanitizeMarkerPosition(newMarkerPosition);
    }

    public static Vector2 SanitizeMarkerPosition(Vector2 position)
    {
        return new Vector2(
            float.IsNaN(position.x) || float.IsInfinity(position.x) ? 0.5f : position.x,
            float.IsNaN(position.y) || float.IsInfinity(position.y) ? 0.5f : position.y);
    }

    public static Vector2 ClampMarkerPosition(
        Vector2 position,
        Vector2Int interiorSize)
    {
        var safePosition = SanitizeMarkerPosition(position);
        var maximum = new Vector2(
            Mathf.Max(0.5f, interiorSize.x - 0.5f),
            Mathf.Max(0.5f, interiorSize.y - 0.5f));
        return new Vector2(
            Mathf.Clamp(safePosition.x, 0.5f, maximum.x),
            Mathf.Clamp(safePosition.y, 0.5f, maximum.y));
    }

    public void Advance(float deltaTime, Vector2Int interiorSize)
    {
        var hasUsableInterior = BuildingFootprint.IsValid(interiorSize);
        if (hasUsableInterior)
        {
            accumulatedProduction += productionRate * Mathf.Max(0f, deltaTime);
        }

        foreach (var entity in GetEntities())
        {
            if (entity is not null
                && BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize))
            {
                entity.Advance(deltaTime);
            }
        }
    }

    public void AddEntity(FactoryEntityRecord entity)
    {
        GetEntities().Add(entity.Clone());
    }

    public bool RemoveEntity(uint entityId)
    {
        var index = GetEntities().FindIndex(entity => entity.EntityId == entityId);
        if (index < 0)
        {
            return false;
        }

        GetEntities().RemoveAt(index);
        return true;
    }

    public bool TryGetEntity(uint entityId, out FactoryEntityRecord entity)
    {
        foreach (var candidate in GetEntities())
        {
            if (candidate is not null && candidate.EntityId == entityId)
            {
                entity = candidate;
                return true;
            }
        }

        entity = null!;
        return false;
    }

    public void SetEntities(IEnumerable<FactoryEntityRecord> newEntities)
    {
        var entityList = GetEntities();
        entityList.Clear();
        if (newEntities is null)
        {
            return;
        }

        foreach (var entity in newEntities)
        {
            if (entity is not null)
            {
                entityList.Add(entity.Clone());
            }
        }
    }

    public void SetEntitySnapshots(
        IReadOnlyList<FactoryEntitySnapshot> snapshots)
    {
        var entityList = GetEntities();
        entityList.Clear();
        if (snapshots is null)
        {
            return;
        }

        foreach (var snapshot in snapshots)
        {
            entityList.Add(FactoryEntityRecord.FromSnapshot(snapshot));
        }
    }

    public void EnsureDefaultEntity()
    {
        if (GetEntities().Count == 0)
        {
            GetEntities().Add(FactoryEntityRecord.CreateDefault(1u, floorIndex));
        }
    }

    public int ReconcileEntityPositions(Vector2Int interiorSize)
    {
        var occupiedCells = new HashSet<Vector2Int>();
        var displaced = new List<FactoryEntityRecord>();
        var ordered = new List<FactoryEntityRecord>(GetEntities());
        ordered.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
        foreach (var entity in ordered)
        {
            if (entity is null)
            {
                continue;
            }

            var cell = Vector2Int.FloorToInt(entity.LogicalPosition);
            if (BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize)
                && occupiedCells.Add(cell))
            {
                continue;
            }

            displaced.Add(entity);
        }

        var movedCount = 0;
        foreach (var entity in displaced)
        {
            var bestCell = Vector2Int.zero;
            var foundCell = false;
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
                    var distance = (center - entity.LogicalPosition).sqrMagnitude;
                    if (foundCell
                        && (distance > bestDistance
                            || (Mathf.Approximately(distance, bestDistance)
                                && (candidate.y < bestCell.y
                                    || (candidate.y == bestCell.y && candidate.x < bestCell.x)))))
                    {
                        continue;
                    }

                    bestCell = candidate;
                    bestDistance = distance;
                    foundCell = true;
                }
            }

            if (foundCell)
            {
                entity.SetLogicalPosition((Vector2)bestCell + Vector2.one * 0.5f);
                occupiedCells.Add(bestCell);
                movedCount++;
            }
        }

        return movedCount;
    }

    public List<FactoryEntityRecord> GetUsableEntities(Vector2Int interiorSize)
    {
        var result = new List<FactoryEntityRecord>();
        foreach (var entity in GetEntities())
        {
            if (entity is not null
                && BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize))
            {
                result.Add(entity);
            }
        }

        return result;
    }

    public int GetRecoveryEntityCount(Vector2Int interiorSize)
    {
        var count = 0;
        foreach (var entity in GetEntities())
        {
            if (entity is not null
                && !BuildingFootprint.IsUsableInteriorPosition(entity.LogicalPosition, interiorSize))
            {
                count++;
            }
        }

        return count;
    }

    public FactoryEntitySnapshot[] GetEntitySnapshots()
    {
        var snapshots = new List<FactoryEntitySnapshot>(GetEntities().Count);
        foreach (var entity in GetEntities())
        {
            if (entity is not null)
            {
                snapshots.Add(entity.ToSnapshot());
            }
        }

        return snapshots.ToArray();
    }

    private List<FactoryEntityRecord> GetEntities()
    {
        if (entities is null)
        {
            entities = new List<FactoryEntityRecord>();
        }

        return entities;
    }
}

[Serializable]
public sealed class OutsideTestFloorSaveData
{
    public int Version = FactoryWorldState.CurrentSaveVersion;
    public List<BuildingRecord> Buildings = new();
    public List<OutsideTestFloorRecord> Floors = new();
    public List<FactoryEntityConnectionRecord> Connections = new();

    public OutsideTestFloorSaveData()
    {
    }

    public OutsideTestFloorSaveData(IEnumerable<OutsideTestFloorRecord> records)
    {
        if (records is not null)
        {
            Floors.AddRange(records);
        }
    }

    public OutsideTestFloorSaveData(
        IEnumerable<BuildingRecord> buildingRecords,
        IEnumerable<OutsideTestFloorRecord> floorRecords,
        IEnumerable<FactoryEntityConnectionRecord> connectionRecords = null)
    {
        if (buildingRecords is not null)
        {
            Buildings.AddRange(buildingRecords);
        }

        if (floorRecords is not null)
        {
            Floors.AddRange(floorRecords);
        }

        if (connectionRecords is not null)
        {
            Connections.AddRange(connectionRecords);
        }
    }
}
