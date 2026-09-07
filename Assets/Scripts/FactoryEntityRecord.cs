// Stores one persistent factory entity and the compact snapshot sent to floor clients.
using System;
using FishNet.CodeGenerating;
using UnityEngine;

[Serializable]
[IncludeSerialization]
public struct FactoryEntitySnapshot
{
    public FactoryEntitySnapshot(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount)
    {
        EntityId = newEntityId;
        DefinitionId = newDefinitionId;
        LogicalPosition = newLogicalPosition;
        CycleRate = newCycleRate;
        CycleProgress = newCycleProgress;
        ProducedCount = newProducedCount;
    }

    public uint EntityId;
    public string DefinitionId;
    public Vector2 LogicalPosition;
    public float CycleRate;
    public float CycleProgress;
    public int ProducedCount;
}

[Serializable]
public sealed class FactoryEntityRecord
{
    private const string DefaultDefinitionId = "test-machine";

    [SerializeField] private uint entityId;
    [SerializeField] private string definitionId = DefaultDefinitionId;
    [SerializeField] private Vector2 logicalPosition;
    [SerializeField] private float cycleRate;
    [SerializeField] private float cycleProgress;
    [SerializeField] private int producedCount;

    public FactoryEntityRecord()
    {
    }

    public FactoryEntityRecord(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount)
    {
        SetState(
            newEntityId,
            newDefinitionId,
            newLogicalPosition,
            newCycleRate,
            newCycleProgress,
            newProducedCount);
    }

    public uint EntityId => entityId;
    public string DefinitionId => definitionId;
    public Vector2 LogicalPosition => logicalPosition;
    public float CycleRate => cycleRate;
    public float CycleProgress => cycleProgress;
    public int ProducedCount => producedCount;

    public static FactoryEntityRecord CreateDefault(
        uint newEntityId,
        int floorIndex)
    {
        var definition = floorIndex switch
        {
            0 => "test-machine-ground",
            1 => "test-machine-upper",
            _ => DefaultDefinitionId
        };
        var position = floorIndex switch
        {
            0 => new Vector2(2f, 1f),
            1 => new Vector2(3f, 1.5f),
            _ => new Vector2(2f, 2f)
        };
        var cycleRate = floorIndex switch
        {
            0 => 0.5f,
            1 => 1f,
            _ => 0.25f
        };
        var cycleProgress = floorIndex switch
        {
            0 => 0.25f,
            1 => 0.5f,
            _ => 0f
        };

        return new FactoryEntityRecord(
            newEntityId,
            definition,
            position,
            cycleRate,
            cycleProgress,
            0);
    }

    public static FactoryEntityRecord FromSnapshot(
        FactoryEntitySnapshot snapshot)
    {
        return new FactoryEntityRecord(
            snapshot.EntityId,
            snapshot.DefinitionId,
            snapshot.LogicalPosition,
            snapshot.CycleRate,
            snapshot.CycleProgress,
            snapshot.ProducedCount);
    }

    public void SetState(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount)
    {
        entityId = newEntityId;
        definitionId = string.IsNullOrWhiteSpace(newDefinitionId)
            ? DefaultDefinitionId
            : newDefinitionId.Trim();
        logicalPosition = SanitizeLogicalPosition(newLogicalPosition);
        cycleRate = float.IsNaN(newCycleRate)
            || float.IsInfinity(newCycleRate)
            ? 0f
            : Mathf.Clamp(newCycleRate, 0f, 1000f);
        cycleProgress = float.IsNaN(newCycleProgress)
            || float.IsInfinity(newCycleProgress)
            ? 0f
            : Mathf.Clamp01(newCycleProgress);
        producedCount = Mathf.Max(0, newProducedCount);
    }

    public void ClampPosition(Vector2Int interiorSize)
    {
        var safePosition = SanitizeLogicalPosition(logicalPosition);
        var maximum = new Vector2(
            Mathf.Max(0.5f, interiorSize.x - 0.5f),
            Mathf.Max(0.5f, interiorSize.y - 0.5f));
        logicalPosition = new Vector2(
            Mathf.Clamp(safePosition.x, 0.5f, maximum.x),
            Mathf.Clamp(safePosition.y, 0.5f, maximum.y));
    }

    public void Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime)
            || deltaTime <= 0f
            || cycleRate <= 0f)
        {
            return;
        }

        cycleProgress += cycleRate * deltaTime;
        while (cycleProgress >= 1f)
        {
            cycleProgress -= 1f;
            if (producedCount < int.MaxValue)
            {
                producedCount++;
            }
        }
    }

    public FactoryEntityRecord Clone()
    {
        return new FactoryEntityRecord(
            entityId,
            definitionId,
            logicalPosition,
            cycleRate,
            cycleProgress,
            producedCount);
    }

    public FactoryEntitySnapshot ToSnapshot()
    {
        return new FactoryEntitySnapshot(
            entityId,
            definitionId,
            logicalPosition,
            cycleRate,
            cycleProgress,
            producedCount);
    }

    private static Vector2 SanitizeLogicalPosition(Vector2 position)
    {
        return new Vector2(
            float.IsNaN(position.x) || float.IsInfinity(position.x) ? 0.5f : position.x,
            float.IsNaN(position.y) || float.IsInfinity(position.y) ? 0.5f : position.y);
    }
}
