// Persists the active OutsideTest world topology and factory state in normalized SQLite tables.
using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;

public sealed class OutsideTestFloorSqliteStore
{
    private const string SaveVersionKey = "save_version";

    public OutsideTestFloorSaveData Load(string path)
    {
        if (FactoryWorldSqliteStore.HasSchema(path))
        {
            return LoadUnifiedProjection(path);
        }

        using var database = new SQLiteConnection(path);
        EnsureReadableSchema(database);

        var versionRows = database.Query<MetadataRow>(
            "SELECT * FROM outside_test_save_metadata WHERE Name = ?",
            SaveVersionKey);
        var data = new OutsideTestFloorSaveData
        {
            Version = versionRows.Count == 0
                ? FactoryWorldState.CurrentSaveVersion
                : versionRows[0].Value
        };

        var doorRows = database.Query<DoorRow>(
            "SELECT * FROM outside_test_building_doors ORDER BY BuildingInstanceId, DoorIndex");
        var doorsByBuilding = new Dictionary<long, List<BuildingRecord.DoorPlacement>>();
        foreach (var row in doorRows)
        {
            if (!doorsByBuilding.TryGetValue(row.BuildingInstanceId, out var doors))
            {
                doors = new List<BuildingRecord.DoorPlacement>();
                doorsByBuilding.Add(row.BuildingInstanceId, doors);
            }

            doors.Add(new BuildingRecord.DoorPlacement(
                row.WallId,
                row.NormalizedOffset));
        }

        var buildingRows = database.Query<BuildingRow>(
            "SELECT * FROM outside_test_buildings ORDER BY BuildingInstanceId");
        foreach (var row in buildingRows)
        {
            doorsByBuilding.TryGetValue(row.BuildingInstanceId, out var doors);
            data.Buildings.Add(new BuildingRecord(
                (uint)row.BuildingInstanceId,
                new Vector3Int(row.AnchorX, row.AnchorY, row.AnchorZ),
                new Vector2Int(row.FootprintX, row.FootprintY),
                row.StoryCount,
                doors));
        }

        var entityRows = database.Query<EntityRow>(
            "SELECT * FROM outside_test_entities ORDER BY BuildingInstanceId, FloorIndex, EntityId");
        var entitiesByFloor = new Dictionary<OutsideTestFloorKey, List<FactoryEntityRecord>>();
        foreach (var row in entityRows)
        {
            var key = new OutsideTestFloorKey(
                (uint)row.BuildingInstanceId,
                row.FloorIndex);
            if (!entitiesByFloor.TryGetValue(key, out var entities))
            {
                entities = new List<FactoryEntityRecord>();
                entitiesByFloor.Add(key, entities);
            }

            entities.Add(new FactoryEntityRecord(
                (uint)row.EntityId,
                row.DefinitionId,
                new Vector2(row.LogicalPositionX, row.LogicalPositionY),
                row.CycleRate,
                row.CycleProgress,
                row.ProducedCount,
                row.OutputCount,
                row.InputCount,
                FactoryConveyorQueue.Decode(row.ConveyorState)));
        }

        var floorRows = database.Query<FloorRow>(
            "SELECT * FROM outside_test_floors ORDER BY BuildingInstanceId, FloorIndex");
        foreach (var row in floorRows)
        {
            var floor = new OutsideTestFloorRecord(
                (uint)row.BuildingInstanceId,
                row.FloorIndex,
                row.Label,
                row.ProductionRate,
                row.AccumulatedProduction,
                new Vector2(row.MarkerPositionX, row.MarkerPositionY));
            var key = new OutsideTestFloorKey(
                (uint)row.BuildingInstanceId,
                row.FloorIndex);
            if (entitiesByFloor.TryGetValue(key, out var entities))
            {
                floor.SetEntities(entities);
            }

            data.Floors.Add(floor);
        }

        var connectionRows = database.Query<ConnectionRow>(
            "SELECT * FROM outside_test_connections ORDER BY ConnectionIndex");
        foreach (var row in connectionRows)
        {
            data.Connections.Add(new FactoryEntityConnectionRecord(
                new FactoryEntityEndpoint(
                    (uint)row.SourceBuildingInstanceId,
                    row.SourceFloorIndex,
                    (uint)row.SourceEntityId),
                new FactoryEntityEndpoint(
                    (uint)row.DestinationBuildingInstanceId,
                    row.DestinationFloorIndex,
                    (uint)row.DestinationEntityId)));
        }

        return data;
    }

    private static OutsideTestFloorSaveData LoadUnifiedProjection(string path)
    {
        var snapshot = new FactoryWorldSqliteStore().Load(path);
        var data = new OutsideTestFloorSaveData
        {
            Version = FactoryWorldState.CurrentSaveVersion
        };
        var buildingIds = new Dictionary<Guid, uint>();
        foreach (var building in snapshot.Buildings)
        {
            var buildingId = building.LegacyBuildingId;
            buildingIds.Add(building.Guid, buildingId);
            data.Buildings.Add(new BuildingRecord(
                buildingId,
                building.AnchorCell,
                building.FootprintSize,
                building.StoryCount,
                building.Doors));
        }

        var entityIds = new Dictionary<Guid, uint>();
        foreach (var floor in snapshot.Floors)
        {
            if (!buildingIds.TryGetValue(floor.BuildingGuid, out var buildingId))
            {
                continue;
            }

            var legacyFloor = new OutsideTestFloorRecord(
                buildingId,
                floor.FloorIndex,
                floor.Label,
                floor.ProductionRate,
                floor.AccumulatedProduction,
                floor.MarkerPosition);
            foreach (var entity in floor.Entities)
            {
                var entityId = entity.LegacyEntityId;
                entityIds.Add(entity.Guid, entityId);
                legacyFloor.AddEntity(new FactoryEntityRecord(
                    entityId,
                    entity.DefinitionId,
                    entity.LocalPosition,
                    entity.CycleRate,
                    entity.CycleProgress,
                    entity.ProducedCount,
                    entity.OutputCount,
                    entity.InputCount,
                    FactoryConveyor.IsConveyor(entity.DefinitionId) ? FactoryConveyorQueue.Decode(entity.State) : null));
            }

            data.Floors.Add(legacyFloor);
        }

        foreach (var connection in snapshot.Connections)
        {
            if (!buildingIds.TryGetValue(connection.Source.BuildingGuid, out var sourceBuildingId)
                || !buildingIds.TryGetValue(connection.Destination.BuildingGuid, out var destinationBuildingId)
                || !entityIds.TryGetValue(connection.Source.EntityGuid, out var sourceEntityId)
                || !entityIds.TryGetValue(connection.Destination.EntityGuid, out var destinationEntityId))
            {
                continue;
            }

            data.Connections.Add(new FactoryEntityConnectionRecord(
                new FactoryEntityEndpoint(
                    sourceBuildingId,
                    connection.Source.FloorIndex,
                    sourceEntityId),
                new FactoryEntityEndpoint(
                    destinationBuildingId,
                    connection.Destination.FloorIndex,
                    destinationEntityId)));
        }

        return data;
    }

    public void Save(
        string path,
        IEnumerable<BuildingRecord> buildings,
        IEnumerable<OutsideTestFloorRecord> floors,
        IEnumerable<FactoryEntityConnectionRecord> connections)
    {
        EnsureParentDirectory(path);
        using var database = new SQLiteConnection(path);
        EnsureTables(database);
        database.RunInTransaction(() =>
        {
            database.Execute("DELETE FROM outside_test_save_metadata");
            database.Execute("DELETE FROM outside_test_building_doors");
            database.Execute("DELETE FROM outside_test_buildings");
            database.Execute("DELETE FROM outside_test_entities");
            database.Execute("DELETE FROM outside_test_floors");
            database.Execute("DELETE FROM outside_test_connections");

            database.Insert(new MetadataRow
            {
                Name = SaveVersionKey,
                Value = FactoryWorldState.CurrentSaveVersion
            });

            foreach (var building in buildings)
            {
                database.Insert(new BuildingRow
                {
                    BuildingInstanceId = building.BuildingInstanceId,
                    AnchorX = building.AnchorCell.x,
                    AnchorY = building.AnchorCell.y,
                    AnchorZ = building.AnchorCell.z,
                    FootprintX = building.FootprintSize.x,
                    FootprintY = building.FootprintSize.y,
                    StoryCount = building.StoryCount
                });

                for (var doorIndex = 0; doorIndex < building.Doors.Count; doorIndex++)
                {
                    var door = building.Doors[doorIndex];
                    database.Insert(new DoorRow
                    {
                        BuildingInstanceId = building.BuildingInstanceId,
                        DoorIndex = doorIndex,
                        WallId = door.WallId,
                        NormalizedOffset = door.NormalizedOffset
                    });
                }
            }

            foreach (var floor in floors)
            {
                database.Insert(new FloorRow
                {
                    BuildingInstanceId = floor.BuildingInstanceId,
                    FloorIndex = floor.FloorIndex,
                    Label = floor.Label,
                    ProductionRate = floor.ProductionRate,
                    AccumulatedProduction = floor.AccumulatedProduction,
                    MarkerPositionX = floor.MarkerPosition.x,
                    MarkerPositionY = floor.MarkerPosition.y
                });

                foreach (var entity in floor.Entities)
                {
                    database.Insert(new EntityRow
                    {
                        BuildingInstanceId = floor.BuildingInstanceId,
                        FloorIndex = floor.FloorIndex,
                        EntityId = entity.EntityId,
                        DefinitionId = entity.DefinitionId,
                        LogicalPositionX = entity.LogicalPosition.x,
                        LogicalPositionY = entity.LogicalPosition.y,
                        CycleRate = entity.CycleRate,
                        CycleProgress = entity.CycleProgress,
                        ProducedCount = entity.ProducedCount,
                        OutputCount = entity.OutputCount,
                        InputCount = entity.InputCount,
                        ConveyorState = entity.GetConveyorState()
                    });
                }
            }

            var connectionIndex = 0;
            foreach (var connection in connections)
            {
                database.Insert(new ConnectionRow
                {
                    ConnectionIndex = connectionIndex++,
                    SourceBuildingInstanceId = connection.Source.BuildingInstanceId,
                    SourceFloorIndex = connection.Source.FloorIndex,
                    SourceEntityId = connection.Source.EntityId,
                    DestinationBuildingInstanceId = connection.Destination.BuildingInstanceId,
                    DestinationFloorIndex = connection.Destination.FloorIndex,
                    DestinationEntityId = connection.Destination.EntityId
                });
            }
        });
    }

    private static void EnsureTables(SQLiteConnection database)
    {
        database.CreateTable<MetadataRow>();
        database.CreateTable<BuildingRow>();
        database.CreateTable<DoorRow>();
        database.CreateTable<FloorRow>();
        database.CreateTable<EntityRow>();
        database.CreateTable<ConnectionRow>();
    }

    private static void EnsureReadableSchema(SQLiteConnection database)
    {
        var rows = database.Query<SchemaRow>(
            "SELECT name AS Name FROM sqlite_master WHERE type = 'table'");
        var names = new HashSet<string>();
        foreach (var row in rows)
        {
            names.Add(row.Name);
        }

        var requiredTables = new[]
        {
            "outside_test_save_metadata",
            "outside_test_buildings",
            "outside_test_building_doors",
            "outside_test_floors",
            "outside_test_entities",
            "outside_test_connections"
        };
        foreach (var table in requiredTables)
        {
            if (!names.Contains(table))
            {
                throw new InvalidDataException(
                    $"Legacy OutsideTest database is missing table '{table}'.");
            }
        }
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [Table("outside_test_save_metadata")]
    private sealed class MetadataRow
    {
        [PrimaryKey]
        public string Name { get; set; } = string.Empty;

        public int Value { get; set; }
    }

    [Table("outside_test_buildings")]
    private sealed class BuildingRow
    {
        [PrimaryKey]
        public long BuildingInstanceId { get; set; }

        public int AnchorX { get; set; }
        public int AnchorY { get; set; }
        public int AnchorZ { get; set; }
        public int FootprintX { get; set; }
        public int FootprintY { get; set; }
        public int StoryCount { get; set; }
    }

    [Table("outside_test_building_doors")]
    private sealed class DoorRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public long BuildingInstanceId { get; set; }
        public int DoorIndex { get; set; }
        public string WallId { get; set; } = string.Empty;
        public float NormalizedOffset { get; set; }
    }

    [Table("outside_test_floors")]
    private sealed class FloorRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public long BuildingInstanceId { get; set; }
        public int FloorIndex { get; set; }
        public string Label { get; set; } = string.Empty;
        public float ProductionRate { get; set; }
        public float AccumulatedProduction { get; set; }
        public float MarkerPositionX { get; set; }
        public float MarkerPositionY { get; set; }
    }

    [Table("outside_test_entities")]
    private sealed class EntityRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public long BuildingInstanceId { get; set; }
        public int FloorIndex { get; set; }
        public long EntityId { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public float LogicalPositionX { get; set; }
        public float LogicalPositionY { get; set; }
        public float CycleRate { get; set; }
        public float CycleProgress { get; set; }
        public int ProducedCount { get; set; }
        public int OutputCount { get; set; }
        public int InputCount { get; set; }
        public byte[] ConveyorState { get; set; } = Array.Empty<byte>();
    }

    [Table("outside_test_connections")]
    private sealed class ConnectionRow
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int ConnectionIndex { get; set; }
        public long SourceBuildingInstanceId { get; set; }
        public int SourceFloorIndex { get; set; }
        public long SourceEntityId { get; set; }
        public long DestinationBuildingInstanceId { get; set; }
        public int DestinationFloorIndex { get; set; }
        public long DestinationEntityId { get; set; }
    }

    [Table("sqlite_master")]
    private sealed class SchemaRow
    {
        public string Name { get; set; } = string.Empty;
    }
}
