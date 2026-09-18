// Creates a deterministic two-floor route fixture and exposes only explicitly isolated persistence helpers.
using System;
using System.IO;
using UnityEngine;

public static class Factory3DRouteFixture
{
    public const float SimulationTickInterval = 0.1f;
    public const uint BuildingId = 7301u;
    public const uint SourceEntityId = 1001u;
    public const uint LowerBeltEntityId = 1002u;
    public const uint BottomElevatorEntityId = 1003u;
    public const uint TopElevatorEntityId = 2001u;
    public const uint UpperBeltEntityId = 2002u;
    public const uint StorageEntityId = 2003u;

    public static FactoryWorldState CreateState()
    {
        var state = new FactoryWorldState(BuildingId);
        var building = new BuildingRecord(
            BuildingId,
            Vector3Int.zero,
            new Vector2Int(10, 6),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>());
        if (!state.TryRegisterBuilding(building, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var lowerEntities = new[]
        {
            new FactoryEntitySnapshot(
                SourceEntityId,
                FactoryEntityDefinitions.TestMachineDefinitionId,
                new Vector2(1.5f, 2.5f),
                1f,
                0f,
                0,
                0,
                0),
            new FactoryEntitySnapshot(
                LowerBeltEntityId,
                "conveyor-east",
                new Vector2(2.5f, 2.5f),
                0f,
                0f,
                0,
                0,
                0,
                null),
            new FactoryEntitySnapshot(
                BottomElevatorEntityId,
                FactoryEntityDefinitions.ElevatorBottomDefinitionId,
                new Vector2(3.5f, 2.5f),
                0f,
                0f,
                0,
                0,
                0)
        };
        var upperEntities = new[]
        {
            new FactoryEntitySnapshot(
                TopElevatorEntityId,
                FactoryEntityDefinitions.ElevatorTopDefinitionId,
                new Vector2(3.5f, 2.5f),
                0f,
                0f,
                0),
            new FactoryEntitySnapshot(
                UpperBeltEntityId,
                "conveyor-east",
                new Vector2(4.5f, 2.5f),
                0f,
                0f,
                0,
                0,
                0,
                null),
            new FactoryEntitySnapshot(
                StorageEntityId,
                FactoryEntityDefinitions.TestStorageDefinitionId,
                new Vector2(5.5f, 2.5f),
                0f,
                0f,
                0,
                0)
        };

        if (!state.ApplySnapshot(BuildingId, 0, "Route Ground", 1f, 0.5f, new Vector2(1.5f, 2.5f), lowerEntities)
            || !state.ApplySnapshot(BuildingId, 1, "Route Upper", 1f, 1.5f, new Vector2(4.5f, 2.5f), upperEntities))
        {
            throw new InvalidOperationException("The deterministic route fixture could not apply its floor snapshots.");
        }

        if (!state.TryAddConnection(
                new FactoryEntityEndpoint(BuildingId, 0, BottomElevatorEntityId),
                new FactoryEntityEndpoint(BuildingId, 1, TopElevatorEntityId),
                out error))
        {
            throw new InvalidOperationException(error);
        }

        state.MarkCurrentStateAsAuthoritative();
        return state;
    }

    public static string CreateIsolatedSavePath()
    {
        return CreateIsolatedSavePath(Guid.NewGuid());
    }

    public static string CreateIsolatedSavePath(Guid testRunId)
    {
        return Path.Combine(Path.GetTempPath(), $"food-factory-route-{testRunId:N}.db");
    }

    public static void AdvanceTick(FactoryWorldState state)
    {
        if (state is not null)
        {
            state.AdvanceProduction(SimulationTickInterval);
        }
    }

    public static bool SaveToIsolatedPath(FactoryWorldState state, string path)
    {
        if (state is null || string.IsNullOrWhiteSpace(path) || !IsIsolatedPath(path))
        {
            return false;
        }

        return state.SaveToFile(path);
    }

    private static bool IsIsolatedPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var tempPath = Path.GetFullPath(Path.GetTempPath());
        return fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase)
            && Path.GetFileName(fullPath).StartsWith("food-factory-route-", StringComparison.OrdinalIgnoreCase);
    }
}
