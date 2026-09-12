// Defines directional conveyor cells and conserves items while moving them between adjacent equipment.
using System.Collections.Generic;
using UnityEngine;

public static class FactoryConveyor
{
    public const int EquipmentSortingOrder = -4;
    public const int ItemSortingOrder = -2;

    public static readonly string[] Definitions = { "conveyor-east", "conveyor-north", "conveyor-west", "conveyor-south" };

    public static bool IsConveyor(string definitionId) => System.Array.IndexOf(Definitions, definitionId) >= 0;
    public static bool IsPlaceable(string definitionId) => IsConveyor(definitionId)
        || definitionId == FactoryEntityDefinitions.TestMachineDefinitionId
        || definitionId == FactoryEntityDefinitions.TestStorageDefinitionId
        || definitionId == FactoryEntityDefinitions.SendingTerminalDefinitionId
        || definitionId == FactoryEntityDefinitions.ReceivingTerminalDefinitionId;

    public static Vector2 Direction(string definitionId) => definitionId switch
    {
        "conveyor-north" => Vector2.up,
        "conveyor-west" => Vector2.left,
        "conveyor-south" => Vector2.down,
        _ => Vector2.right
    };

    public static Vector2 ItemPosition(FactoryEntityRecord belt, IReadOnlyList<FactoryEntityRecord> entities, float progress)
    {
        var direction = Direction(belt.DefinitionId);
        var entry = belt.LogicalPosition - direction * 0.5f;
        foreach (var source in entities)
        {
            if (!source.IsConveyor) continue;
            var incoming = Direction(source.DefinitionId);
            if ((source.LogicalPosition + incoming - belt.LogicalPosition).sqrMagnitude > 0.01f
                || incoming == -direction) continue;
            entry = belt.LogicalPosition - incoming * 0.5f;
            break;
        }
        // Both sides of a handoff use the same cell boundary, including corner belts.
        return progress < 0.5f
            ? Vector2.Lerp(entry, belt.LogicalPosition, progress * 2f)
            : Vector2.Lerp(belt.LogicalPosition, belt.LogicalPosition + direction * 0.5f, (progress - 0.5f) * 2f);
    }

    public static bool IsOccupied(IReadOnlyList<FactoryEntityRecord> entities, Vector2 position)
    {
        foreach (var entity in entities)
            if (Vector2Int.FloorToInt(entity.LogicalPosition) == Vector2Int.FloorToInt(position)) return true;
        return false;
    }

    public static Dictionary<uint, float[]> PredictQueues(IReadOnlyList<FactoryEntityRecord> entities, float deltaTime)
    {
        var result = new Dictionary<uint, float[]>();
        foreach (var belt in entities)
        {
            if (!belt.IsConveyor) continue;
            var values = new float[belt.ConveyorPositions.Count];
            var limit = 1f;
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = Mathf.Min(limit, belt.ConveyorPositions[index] + Mathf.Max(0f, deltaTime) / FactoryConveyorQueue.TravelTime);
                limit = values[index] - FactoryConveyorQueue.Spacing;
            }
            result.Add(belt.EntityId, values);
        }
        // Propagate downstream congestion upstream without depending on entity creation order.
        for (var pass = 0; pass < entities.Count; pass++)
        {
            var changed = false;
            foreach (var belt in entities)
            {
                if (!belt.IsConveyor || result[belt.EntityId].Length == 0) continue;
                var values = result[belt.EntityId];
                var limit = 1f;
                var destination = FindDestination(belt, entities);
                if (destination is not null && destination.IsConveyor)
                {
                    var ahead = result[destination.EntityId];
                    if (ahead.Length > 0) limit = Mathf.Min(limit, 1f + ahead[ahead.Length - 1] - FactoryConveyorQueue.Spacing);
                }
                for (var index = 0; index < values.Length; index++)
                {
                    var next = Mathf.Min(values[index], limit);
                    changed |= next < values[index] - 0.000001f;
                    values[index] = next;
                    limit = next - FactoryConveyorQueue.Spacing;
                }
            }
            if (!changed) break;
        }
        return result;
    }

    private static FactoryEntityRecord FindDestination(FactoryEntityRecord belt, IReadOnlyList<FactoryEntityRecord> entities)
    {
        var destinationPosition = belt.LogicalPosition + Direction(belt.DefinitionId);
        foreach (var destination in entities)
        {
            if ((destination.LogicalPosition - destinationPosition).sqrMagnitude > 0.01f) continue;
            if (destination.IsConveyor && Direction(destination.DefinitionId) == -Direction(belt.DefinitionId)) return null!;
            return destination;
        }
        return null!;
    }

    public static void TransferAdjacent(IReadOnlyList<FactoryEntityRecord> entities)
    {
        var positions = PredictQueues(entities, 0f);
        foreach (var belt in entities)
            if (belt.IsConveyor) belt.SetConveyorPositions(positions[belt.EntityId]);
        // Clearing a downstream exit may free an upstream handoff within the same tick.
        for (var pass = 0; pass < entities.Count; pass++)
        {
            var moved = false;
            foreach (var belt in entities)
            {
                if (!belt.IsConveyor) continue;
                var destination = FindDestination(belt, entities);
                if (destination is not null) moved |= FactoryItemTransfer.TryTransfer(belt, destination, 1) > 0;
            }
            if (!moved) break;
        }
        foreach (var belt in entities)
        {
            if (!belt.IsConveyor) continue;
            var sourcePosition = belt.LogicalPosition - Direction(belt.DefinitionId);
            foreach (var source in entities)
            {
                if (source.IsConveyor || (source.LogicalPosition - sourcePosition).sqrMagnitude > 0.01f) continue;
                FactoryItemTransfer.TryTransfer(source, belt, 1);
                break;
            }
        }
    }
}
