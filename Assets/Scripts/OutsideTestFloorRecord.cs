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

    public static OutsideTestFloorRecord CreateDefault(
        uint newBuildingInstanceId,
        int newFloorIndex)
    {
        return newFloorIndex switch
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
        markerPosition = new Vector2(
            Mathf.Clamp(newMarkerPosition.x, 0.5f, 5.5f),
            Mathf.Clamp(newMarkerPosition.y, 0.5f, 5.5f));
    }

    public void Advance(float deltaTime)
    {
        accumulatedProduction += productionRate * Mathf.Max(0f, deltaTime);
    }
}

[Serializable]
public sealed class OutsideTestFloorSaveData
{
    public int Version = 1;
    public List<OutsideTestFloorRecord> Floors = new();

    public OutsideTestFloorSaveData()
    {
    }

    public OutsideTestFloorSaveData(IEnumerable<OutsideTestFloorRecord> records)
    {
        Floors.AddRange(records);
    }
}
