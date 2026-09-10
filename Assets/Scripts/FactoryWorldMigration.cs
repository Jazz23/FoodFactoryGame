// Imports the two legacy save formats into one validated schema-8 factory world snapshot.
using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;
using NotAI;

public sealed class FactoryWorldMigration
{
    public FactoryWorldMigration(
        string newLegacyOutsidePath,
        string newLegacyNaiPath)
    {
        LegacyOutsidePath = newLegacyOutsidePath;
        LegacyNaiPath = newLegacyNaiPath;
    }

    public string LegacyOutsidePath { get; }
    public string LegacyNaiPath { get; }

    public FactoryWorldSnapshot Import()
    {
        var snapshot = new FactoryWorldSnapshot();
        var legacyOutside = LoadLegacyOutside();
        ImportOutsideTest(legacyOutside, snapshot);
        ImportNai(snapshot);

        if (!FactoryWorldValidation.TryValidate(snapshot, out var error))
        {
            throw new InvalidDataException($"Legacy factory migration failed: {error}");
        }

        return snapshot;
    }

    private OutsideTestFloorSaveData LoadLegacyOutside()
    {
        if (string.IsNullOrWhiteSpace(LegacyOutsidePath) || !File.Exists(LegacyOutsidePath))
        {
            return new OutsideTestFloorSaveData
            {
                Version = FactoryWorldState.CurrentSaveVersion
            };
        }

        // OutsideTestFloorSqliteStore.Load only reads here; it never creates or alters legacy tables.
        return new OutsideTestFloorSqliteStore().Load(LegacyOutsidePath);
    }

    private void ImportOutsideTest(
        OutsideTestFloorSaveData legacy,
        FactoryWorldSnapshot snapshot)
    {
        var buildingsById = new Dictionary<uint, BuildingRecord>();
        foreach (var building in legacy.Buildings)
        {
            if (building is null || building.BuildingInstanceId == 0)
            {
                throw new InvalidDataException("Legacy OutsideTest contains an invalid building record.");
            }

            if (!buildingsById.TryAdd(building.BuildingInstanceId, building))
            {
                throw new InvalidDataException(
                    $"Legacy OutsideTest contains duplicate building {building.BuildingInstanceId}.");
            }

            snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
                FactoryGuidMigration.ForBuilding(building.BuildingInstanceId),
                "outside-test-building",
                building.AnchorCell,
                building.FootprintSize,
                building.StoryCount,
                false,
                building.BuildingInstanceId,
                building.Doors));
            AddMapping(
                snapshot,
                "building",
                building.BuildingInstanceId.ToString(),
                FactoryGuidMigration.ForBuilding(building.BuildingInstanceId));
        }

        foreach (var floor in legacy.Floors)
        {
            if (floor is null || floor.BuildingInstanceId == 0 || floor.FloorIndex < 0)
            {
                throw new InvalidDataException("Legacy OutsideTest contains an invalid floor record.");
            }

            if (!buildingsById.TryGetValue(floor.BuildingInstanceId, out var building))
            {
                building = CreateInferredBuilding(floor.BuildingInstanceId, legacy.Floors);
                buildingsById.Add(floor.BuildingInstanceId, building);
                snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
                    FactoryGuidMigration.ForBuilding(building.BuildingInstanceId),
                    "outside-test-building",
                    building.AnchorCell,
                    building.FootprintSize,
                    building.StoryCount,
                    false,
                    building.BuildingInstanceId,
                    building.Doors));
            }

            var floorGuid = FactoryGuidMigration.ForFloor(
                floor.BuildingInstanceId,
                floor.FloorIndex);
            var worldFloor = new FactoryWorldFloorRecord(
                floorGuid,
                FactoryGuidMigration.ForBuilding(floor.BuildingInstanceId),
                floor.FloorIndex,
                floor.Label,
                floor.ProductionRate,
                floor.AccumulatedProduction,
                floor.MarkerPosition);
            var worldEntities = new List<FactoryWorldEntityRecord>();
            foreach (var entity in floor.Entities)
            {
                if (entity is null || entity.EntityId == 0)
                {
                    throw new InvalidDataException(
                        $"Legacy OutsideTest floor {floor.BuildingInstanceId}/{floor.FloorIndex} contains an invalid entity.");
                }

                worldEntities.Add(new FactoryWorldEntityRecord(
                    FactoryGuidMigration.ForEntity(
                        floor.BuildingInstanceId,
                        floor.FloorIndex,
                        entity.EntityId),
                    floorGuid,
                    entity.DefinitionId,
                    entity.LogicalPosition,
                    0f,
                    Vector2.one,
                    Array.Empty<byte>(),
                    entity.EntityId,
                    false,
                    entity.CycleRate,
                    entity.CycleProgress,
                    entity.ProducedCount,
                    entity.OutputCount,
                    entity.InputCount));
            }

            worldFloor.SetEntities(worldEntities);
            snapshot.Floors.Add(worldFloor);
            AddMapping(
                snapshot,
                "floor",
                $"{floor.BuildingInstanceId}:{floor.FloorIndex}",
                floorGuid);
            foreach (var entity in worldEntities)
            {
                AddMapping(
                    snapshot,
                    "entity",
                    $"{floor.BuildingInstanceId}:{floor.FloorIndex}:{entity.LegacyEntityId}",
                    entity.Guid);
            }
        }

        foreach (var building in buildingsById.Values)
        {
            for (var floorIndex = 0; floorIndex < building.StoryCount; floorIndex++)
            {
                var floorGuid = FactoryGuidMigration.ForFloor(
                    building.BuildingInstanceId,
                    floorIndex);
                if (snapshot.Floors.Exists(floor => floor.Guid == floorGuid))
                {
                    continue;
                }

                snapshot.Floors.Add(new FactoryWorldFloorRecord(
                    floorGuid,
                    FactoryGuidMigration.ForBuilding(building.BuildingInstanceId),
                    floorIndex,
                    $"Floor {floorIndex}",
                    1f,
                    0f,
                    new Vector2(0.5f, 0.5f)));
            }
        }

        foreach (var connection in legacy.Connections)
        {
            if (connection is null)
            {
                throw new InvalidDataException("Legacy OutsideTest contains a null connection.");
            }

            var source = CreateWorldEndpoint(connection.Source, snapshot);
            var destination = CreateWorldEndpoint(connection.Destination, snapshot);
            snapshot.Connections.Add(new FactoryWorldConnectionRecord(
                FactoryGuidMigration.ForConnection(
                    new FactoryEntityEndpoint(
                        source.BuildingGuid,
                        source.FloorGuid,
                        source.EntityGuid,
                        source.FloorIndex),
                    new FactoryEntityEndpoint(
                        destination.BuildingGuid,
                        destination.FloorGuid,
                        destination.EntityGuid,
                        destination.FloorIndex)),
                source,
                destination));
            AddMapping(
                snapshot,
                "connection",
                $"{connection.Source.BuildingInstanceId}:{connection.Source.FloorIndex}:{connection.Source.EntityId}>{connection.Destination.BuildingInstanceId}:{connection.Destination.FloorIndex}:{connection.Destination.EntityId}",
                FactoryGuidMigration.ForConnection(
                    connection.Source,
                    connection.Destination));
        }
    }

    private static BuildingRecord CreateInferredBuilding(
        uint buildingId,
        IReadOnlyList<OutsideTestFloorRecord> floors)
    {
        var highestFloor = 0;
        foreach (var floor in floors)
        {
            if (floor.BuildingInstanceId == buildingId)
            {
                highestFloor = Math.Max(highestFloor, floor.FloorIndex);
            }
        }

        return new BuildingRecord(
            buildingId,
            Vector3Int.zero,
            new Vector2Int(8, 8),
            highestFloor + 1);
    }

    private static FactoryWorldEndpoint CreateWorldEndpoint(
        FactoryEntityEndpoint legacyEndpoint,
        FactoryWorldSnapshot snapshot)
    {
        var floorGuid = FactoryGuidMigration.ForFloor(
            legacyEndpoint.BuildingInstanceId,
            legacyEndpoint.FloorIndex);
        var entityGuid = FactoryGuidMigration.ForEntity(
            legacyEndpoint.BuildingInstanceId,
            legacyEndpoint.FloorIndex,
            legacyEndpoint.EntityId);
        var floorExists = snapshot.Floors.Exists(floor => floor.Guid == floorGuid);
        var entityExists = false;
        if (floorExists)
        {
            var matchingFloor = snapshot.Floors.Find(floor => floor.Guid == floorGuid);
            foreach (var entity in matchingFloor.Entities)
            {
                if (entity.Guid == entityGuid)
                {
                    entityExists = true;
                    break;
                }
            }
        }
        if (!floorExists || !entityExists)
        {
            throw new InvalidDataException(
                $"Legacy connection endpoint {legacyEndpoint} has no matching floor/entity record.");
        }

        return new FactoryWorldEndpoint(
            FactoryGuidMigration.ForBuilding(legacyEndpoint.BuildingInstanceId),
            floorGuid,
            entityGuid,
            legacyEndpoint.FloorIndex);
    }

    private void ImportNai(FactoryWorldSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(LegacyNaiPath) || !File.Exists(LegacyNaiPath))
        {
            return;
        }

        using var database = new SQLiteConnection(LegacyNaiPath);
        if (!HasTable(database, "buildables"))
        {
            return;
        }

        var buildingGuid = FactoryGuidMigration.ForInsideTestBuilding();
        var floorGuid = FactoryGuidMigration.ForInsideTestFloor();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "nai-inside-test",
            Vector3Int.zero,
            new Vector2Int(32, 32),
            1,
            true));
        var floor = new FactoryWorldFloorRecord(
            floorGuid,
            buildingGuid,
            0,
            "InsideTest",
            0f,
            0f,
            new Vector2(0.5f, 0.5f));
        var entityGuids = new HashSet<Guid>();
        foreach (var row in database.Query<LegacyNaiBuildableRow>(
                     "SELECT * FROM buildables ORDER BY Guid"))
        {
            if (row.Guid == Guid.Empty)
            {
                throw new InvalidDataException("Legacy NAI buildables contains an empty GUID.");
            }

            if (!entityGuids.Add(row.Guid))
            {
                throw new InvalidDataException($"Legacy NAI buildables contains duplicate GUID {row.Guid:D}.");
            }

            string definitionId;
            try
            {
                definitionId = NAIBuildableDefinitionCatalog.GetDefinitionId(row.BuildableId);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"Legacy NAI buildable {row.Guid:D} uses unresolved prefab index {row.BuildableId}.",
                    exception);
            }

            floor.SetEntities(AddEntity(
                floor.Entities,
                new FactoryWorldEntityRecord(
                    row.Guid,
                    floorGuid,
                    definitionId,
                    new Vector2(row.PositionX, row.PositionY),
                    row.RotationZ,
                    Vector2.one,
                     row.State,
                     0,
                     true)));
            AddMapping(snapshot, "nai-entity", row.Guid.ToString("D"), row.Guid);
        }

        snapshot.Floors.Add(floor);
        AddMapping(snapshot, "building", "inside-test", buildingGuid);
        AddMapping(snapshot, "floor", "inside-test:0", floorGuid);
    }

    private static IReadOnlyList<FactoryWorldEntityRecord> AddEntity(
        IReadOnlyList<FactoryWorldEntityRecord> entities,
        FactoryWorldEntityRecord entity)
    {
        var result = new List<FactoryWorldEntityRecord>(entities)
        {
            entity
        };
        return result;
    }

    private static void AddMapping(
        FactoryWorldSnapshot snapshot,
        string kind,
        string legacyKey,
        Guid guid)
    {
        foreach (var mapping in snapshot.MigrationMappings)
        {
            if (mapping.Kind == kind && mapping.LegacyKey == legacyKey)
            {
                if (mapping.Guid != guid)
                {
                    throw new InvalidDataException($"Migration mapping collision for {kind}:{legacyKey}.");
                }

                return;
            }
        }

        snapshot.MigrationMappings.Add(new FactoryWorldMigrationMapping(kind, legacyKey, guid));
    }

    private static bool HasTable(SQLiteConnection database, string tableName)
    {
        var rows = database.Query<LegacyTableRow>(
            "SELECT name AS Name FROM sqlite_master WHERE type = 'table' AND name = ?",
            tableName);
        return rows.Count > 0;
    }

    [Table("buildables")]
    private sealed class LegacyNaiBuildableRow
    {
        [PrimaryKey] public Guid Guid { get; set; }
        public int BuildableId { get; set; }
        public byte[] State { get; set; } = Array.Empty<byte>();
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public float RotationZ { get; set; }
    }

    [Table("sqlite_master")]
    private sealed class LegacyTableRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
