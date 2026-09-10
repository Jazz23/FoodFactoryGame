// Stores one complete factory world snapshot in the schema-8 normalized SQLite database.
using System;
using System.Collections.Generic;
using System.IO;
using SQLite;

public sealed class FactoryWorldSqliteStore
{
    public const string DatabaseFileName = "factory-world.db";
    public const string SchemaVersionKey = "schema_version";
    public const string MigrationCompleteKey = "migration_complete";

    public FactoryWorldSqliteStore(string newPath = null)
    {
        Path = string.IsNullOrWhiteSpace(newPath)
            ? FactoryWorldPaths.GetDefaultDatabasePath()
            : newPath;
    }

    public string Path { get; }

    public bool Exists => File.Exists(Path);

    public FactoryWorldSnapshot Load()
    {
        return Load(Path);
    }

    public FactoryWorldSnapshot Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("The unified factory database does not exist.", path);
        }

        using var database = new SQLiteConnection(path);
        if (!HasSchema(database))
        {
            throw new InvalidDataException("The unified factory database has no schema-8 metadata.");
        }

        var version = ReadMetadata(database, SchemaVersionKey);
        if (version != FactoryWorldSnapshot.CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Unsupported unified factory schema version {version}.");
        }

        var snapshot = new FactoryWorldSnapshot { SchemaVersion = version };
        foreach (var row in database.Query<MigrationMappingRow>(
                     "SELECT * FROM factory_migration_map ORDER BY Kind, LegacyKey"))
        {
            snapshot.MigrationMappings.Add(new FactoryWorldMigrationMapping(
                row.Kind,
                row.LegacyKey,
                ParseGuid(row.Guid, "migration mapping")));
        }
        var doors = ReadDoors(database);
        foreach (var row in database.Query<BuildingRow>(
                     "SELECT * FROM factory_buildings ORDER BY Guid"))
        {
            doors.TryGetValue(row.Guid, out var buildingDoors);
            snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
                ParseGuid(row.Guid, "building"),
                row.DefinitionId,
                new UnityEngine.Vector3Int(row.AnchorX, row.AnchorY, row.AnchorZ),
                new UnityEngine.Vector2Int(row.FootprintX, row.FootprintY),
                row.StoryCount,
                row.InteriorOnly != 0,
                checked((uint)Math.Max(0, row.LegacyBuildingId)),
                buildingDoors));
        }

        var entities = ReadEntities(database);
        var entitiesByFloor = new Dictionary<string, List<FactoryWorldEntityRecord>>();
        foreach (var entity in entities)
        {
            var floorGuid = FactoryGuidMigration.ToCanonical(entity.FloorGuid);
            if (!entitiesByFloor.TryGetValue(floorGuid, out var entityList))
            {
                entityList = new List<FactoryWorldEntityRecord>();
                entitiesByFloor.Add(floorGuid, entityList);
            }

            entityList.Add(entity);
        }

        foreach (var row in database.Query<FloorRow>(
                     "SELECT * FROM factory_floors ORDER BY BuildingGuid, FloorIndex"))
        {
            var floor = new FactoryWorldFloorRecord(
                ParseGuid(row.Guid, "floor"),
                ParseGuid(row.BuildingGuid, "floor parent"),
                row.FloorIndex,
                row.Label,
                row.ProductionRate,
                row.AccumulatedProduction,
                new UnityEngine.Vector2(row.MarkerPositionX, row.MarkerPositionY));
            if (entitiesByFloor.TryGetValue(row.Guid, out var floorEntities))
            {
                floor.SetEntities(floorEntities);
            }

            snapshot.Floors.Add(floor);
        }

        foreach (var row in database.Query<ConnectionRow>(
                     "SELECT * FROM factory_connections ORDER BY Guid"))
        {
            snapshot.Connections.Add(new FactoryWorldConnectionRecord(
                ParseGuid(row.Guid, "connection"),
                new FactoryWorldEndpoint(
                    ParseGuid(row.SourceBuildingGuid, "source building"),
                    ParseGuid(row.SourceFloorGuid, "source floor"),
                    ParseGuid(row.SourceEntityGuid, "source entity"),
                    row.SourceFloorIndex),
                new FactoryWorldEndpoint(
                    ParseGuid(row.DestinationBuildingGuid, "destination building"),
                    ParseGuid(row.DestinationFloorGuid, "destination floor"),
                    ParseGuid(row.DestinationEntityGuid, "destination entity"),
                    row.DestinationFloorIndex)));
        }

        if (!FactoryWorldValidation.TryValidate(snapshot, out var error))
        {
            throw new InvalidDataException(error);
        }

        return snapshot;
    }

    public void Save(FactoryWorldSnapshot snapshot)
    {
        Save(Path, snapshot);
    }

    public void Save(string path, FactoryWorldSnapshot snapshot)
    {
        if (!FactoryWorldValidation.TryValidate(snapshot, out var error))
        {
            throw new InvalidDataException(error);
        }

        EnsureParentDirectory(path);
        using var database = new SQLiteConnection(path);
        EnsureSchema(database);
        database.RunInTransaction(() =>
        {
            database.Execute("DELETE FROM factory_metadata");
            database.Execute("DELETE FROM factory_doors");
            database.Execute("DELETE FROM factory_entities");
            database.Execute("DELETE FROM factory_floors");
            database.Execute("DELETE FROM factory_buildings");
            database.Execute("DELETE FROM factory_connections");
            database.Execute("DELETE FROM factory_migration_map");

            database.Insert(new MetadataRow
            {
                Name = SchemaVersionKey,
                Value = FactoryWorldSnapshot.CurrentSchemaVersion.ToString()
            });
            database.Insert(new MetadataRow
            {
                Name = MigrationCompleteKey,
                Value = "1"
            });

            foreach (var building in snapshot.Buildings)
            {
                database.Insert(new BuildingRow
                {
                    Guid = FactoryGuidMigration.ToCanonical(building.Guid),
                    LegacyBuildingId = building.LegacyBuildingId,
                    DefinitionId = building.DefinitionId,
                    AnchorX = building.AnchorCell.x,
                    AnchorY = building.AnchorCell.y,
                    AnchorZ = building.AnchorCell.z,
                    FootprintX = building.FootprintSize.x,
                    FootprintY = building.FootprintSize.y,
                    StoryCount = building.StoryCount,
                    InteriorOnly = building.InteriorOnly ? 1 : 0
                });

                for (var index = 0; index < building.Doors.Count; index++)
                {
                    var door = building.Doors[index];
                    database.Insert(new DoorRow
                    {
                        BuildingGuid = FactoryGuidMigration.ToCanonical(building.Guid),
                        DoorIndex = index,
                        WallId = door.WallId,
                        NormalizedOffset = door.NormalizedOffset
                    });
                }
            }

            foreach (var floor in snapshot.Floors)
            {
                database.Insert(new FloorRow
                {
                    Guid = FactoryGuidMigration.ToCanonical(floor.Guid),
                    BuildingGuid = FactoryGuidMigration.ToCanonical(floor.BuildingGuid),
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
                        Guid = FactoryGuidMigration.ToCanonical(entity.Guid),
                        FloorGuid = FactoryGuidMigration.ToCanonical(entity.FloorGuid),
                        LegacyEntityId = entity.LegacyEntityId,
                        DefinitionId = entity.DefinitionId,
                        PositionX = entity.LocalPosition.x,
                        PositionY = entity.LocalPosition.y,
                        RotationZ = entity.RotationZ,
                        FootprintX = entity.Footprint.x,
                        FootprintY = entity.Footprint.y,
                        State = entity.State,
                        CycleRate = entity.CycleRate,
                        CycleProgress = entity.CycleProgress,
                        ProducedCount = entity.ProducedCount,
                        OutputCount = entity.OutputCount,
                        InputCount = entity.InputCount,
                        NaiEntity = entity.IsNaiEntity ? 1 : 0
                    });
                }
            }

            foreach (var connection in snapshot.Connections)
            {
                database.Insert(new ConnectionRow
                {
                    Guid = FactoryGuidMigration.ToCanonical(connection.Guid),
                    SourceBuildingGuid = FactoryGuidMigration.ToCanonical(connection.Source.BuildingGuid),
                    SourceFloorGuid = FactoryGuidMigration.ToCanonical(connection.Source.FloorGuid),
                    SourceEntityGuid = FactoryGuidMigration.ToCanonical(connection.Source.EntityGuid),
                    SourceFloorIndex = connection.Source.FloorIndex,
                    DestinationBuildingGuid = FactoryGuidMigration.ToCanonical(connection.Destination.BuildingGuid),
                    DestinationFloorGuid = FactoryGuidMigration.ToCanonical(connection.Destination.FloorGuid),
                    DestinationEntityGuid = FactoryGuidMigration.ToCanonical(connection.Destination.EntityGuid),
                    DestinationFloorIndex = connection.Destination.FloorIndex
                });
            }

            foreach (var mapping in snapshot.MigrationMappings)
            {
                database.Insert(new MigrationMappingRow
                {
                    Kind = mapping.Kind,
                    LegacyKey = mapping.LegacyKey,
                    Guid = FactoryGuidMigration.ToCanonical(mapping.Guid)
                });
            }
        });
    }

    public static bool HasSchema(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        using var database = new SQLiteConnection(path);
        return HasSchema(database);
    }

    private static bool HasSchema(SQLiteConnection database)
    {
        var tables = database.Query<SchemaRow>(
            "SELECT name AS Name FROM sqlite_master WHERE type = 'table'");
        var names = new HashSet<string>();
        foreach (var table in tables)
        {
            names.Add(table.Name);
        }

        return names.Contains("factory_metadata")
            && names.Contains("factory_buildings")
            && names.Contains("factory_floors")
            && names.Contains("factory_entities")
            && names.Contains("factory_connections")
            && names.Contains("factory_migration_map")
            && ReadMetadata(database, SchemaVersionKey) == FactoryWorldSnapshot.CurrentSchemaVersion;
    }

    private static void EnsureSchema(SQLiteConnection database)
    {
        database.Execute("CREATE TABLE IF NOT EXISTS factory_metadata (Name TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL)");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_buildings (Guid TEXT PRIMARY KEY NOT NULL, LegacyBuildingId INTEGER NOT NULL, DefinitionId TEXT NOT NULL, AnchorX INTEGER NOT NULL, AnchorY INTEGER NOT NULL, AnchorZ INTEGER NOT NULL, FootprintX INTEGER NOT NULL, FootprintY INTEGER NOT NULL, StoryCount INTEGER NOT NULL, InteriorOnly INTEGER NOT NULL)");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_floors (Guid TEXT PRIMARY KEY NOT NULL, BuildingGuid TEXT NOT NULL, FloorIndex INTEGER NOT NULL, Label TEXT NOT NULL, ProductionRate REAL NOT NULL, AccumulatedProduction REAL NOT NULL, MarkerPositionX REAL NOT NULL, MarkerPositionY REAL NOT NULL)");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_entities (Guid TEXT PRIMARY KEY NOT NULL, FloorGuid TEXT NOT NULL, LegacyEntityId INTEGER NOT NULL, DefinitionId TEXT NOT NULL, PositionX REAL NOT NULL, PositionY REAL NOT NULL, RotationZ REAL NOT NULL, FootprintX REAL NOT NULL, FootprintY REAL NOT NULL, State BLOB, CycleRate REAL NOT NULL, CycleProgress REAL NOT NULL, ProducedCount INTEGER NOT NULL, OutputCount INTEGER NOT NULL, InputCount INTEGER NOT NULL, NaiEntity INTEGER NOT NULL)");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_connections (Guid TEXT PRIMARY KEY NOT NULL, SourceBuildingGuid TEXT NOT NULL, SourceFloorGuid TEXT NOT NULL, SourceEntityGuid TEXT NOT NULL, SourceFloorIndex INTEGER NOT NULL, DestinationBuildingGuid TEXT NOT NULL, DestinationFloorGuid TEXT NOT NULL, DestinationEntityGuid TEXT NOT NULL, DestinationFloorIndex INTEGER NOT NULL)");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_doors (BuildingGuid TEXT NOT NULL, DoorIndex INTEGER NOT NULL, WallId TEXT NOT NULL, NormalizedOffset REAL NOT NULL, PRIMARY KEY (BuildingGuid, DoorIndex))");
        database.Execute("CREATE TABLE IF NOT EXISTS factory_migration_map (Kind TEXT NOT NULL, LegacyKey TEXT NOT NULL, Guid TEXT PRIMARY KEY NOT NULL)");
        database.Execute("CREATE UNIQUE INDEX IF NOT EXISTS factory_floor_order ON factory_floors (BuildingGuid, FloorIndex)");
    }

    private static int ReadMetadata(SQLiteConnection database, string name)
    {
        var rows = database.Query<MetadataRow>(
            "SELECT * FROM factory_metadata WHERE Name = ?",
            name);
        return rows.Count == 0 || !int.TryParse(rows[0].Value, out var value) ? 0 : value;
    }

    private static Dictionary<string, List<BuildingRecord.DoorPlacement>> ReadDoors(SQLiteConnection database)
    {
        var result = new Dictionary<string, List<BuildingRecord.DoorPlacement>>();
        foreach (var row in database.Query<DoorRow>(
                     "SELECT * FROM factory_doors ORDER BY BuildingGuid, DoorIndex"))
        {
            if (!result.TryGetValue(row.BuildingGuid, out var doors))
            {
                doors = new List<BuildingRecord.DoorPlacement>();
                result.Add(row.BuildingGuid, doors);
            }

            doors.Add(new BuildingRecord.DoorPlacement(row.WallId, row.NormalizedOffset));
        }

        return result;
    }

    private static List<FactoryWorldEntityRecord> ReadEntities(SQLiteConnection database)
    {
        var result = new List<FactoryWorldEntityRecord>();
        foreach (var row in database.Query<EntityRow>(
                     "SELECT * FROM factory_entities ORDER BY FloorGuid, Guid"))
        {
            result.Add(new FactoryWorldEntityRecord(
                ParseGuid(row.Guid, "entity"),
                ParseGuid(row.FloorGuid, "entity parent"),
                row.DefinitionId,
                new UnityEngine.Vector2(row.PositionX, row.PositionY),
                row.RotationZ,
                new UnityEngine.Vector2(row.FootprintX, row.FootprintY),
                row.State,
                checked((uint)Math.Max(0, row.LegacyEntityId)),
                row.NaiEntity != 0,
                row.CycleRate,
                row.CycleProgress,
                row.ProducedCount,
                row.OutputCount,
                row.InputCount));
        }

        return result;
    }

    private static Guid ParseGuid(string value, string kind)
    {
        if (!FactoryGuidMigration.TryParseCanonical(value, out var result))
        {
            throw new InvalidDataException($"The {kind} GUID '{value}' is not canonical.");
        }

        return result;
    }

    private static void EnsureParentDirectory(string path)
    {
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    [Table("factory_metadata")]
    private sealed class MetadataRow
    {
        [PrimaryKey] public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    [Table("factory_schema_tables")]
    private sealed class SchemaRow
    {
        public string Name { get; set; } = string.Empty;
    }

    [Table("factory_migration_map")]
    private sealed class MigrationMappingRow
    {
        public string Kind { get; set; } = string.Empty;
        public string LegacyKey { get; set; } = string.Empty;
        [PrimaryKey] public string Guid { get; set; } = string.Empty;
    }

    [Table("factory_buildings")]
    private sealed class BuildingRow
    {
        [PrimaryKey] public string Guid { get; set; } = string.Empty;
        public uint LegacyBuildingId { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public int AnchorX { get; set; }
        public int AnchorY { get; set; }
        public int AnchorZ { get; set; }
        public int FootprintX { get; set; }
        public int FootprintY { get; set; }
        public int StoryCount { get; set; }
        public int InteriorOnly { get; set; }
    }

    [Table("factory_floors")]
    private sealed class FloorRow
    {
        [PrimaryKey] public string Guid { get; set; } = string.Empty;
        public string BuildingGuid { get; set; } = string.Empty;
        public int FloorIndex { get; set; }
        public string Label { get; set; } = string.Empty;
        public float ProductionRate { get; set; }
        public float AccumulatedProduction { get; set; }
        public float MarkerPositionX { get; set; }
        public float MarkerPositionY { get; set; }
    }

    [Table("factory_entities")]
    private sealed class EntityRow
    {
        [PrimaryKey] public string Guid { get; set; } = string.Empty;
        public string FloorGuid { get; set; } = string.Empty;
        public uint LegacyEntityId { get; set; }
        public string DefinitionId { get; set; } = string.Empty;
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float RotationZ { get; set; }
        public float FootprintX { get; set; }
        public float FootprintY { get; set; }
        public byte[] State { get; set; } = Array.Empty<byte>();
        public float CycleRate { get; set; }
        public float CycleProgress { get; set; }
        public int ProducedCount { get; set; }
        public int OutputCount { get; set; }
        public int InputCount { get; set; }
        public int NaiEntity { get; set; }
    }

    [Table("factory_connections")]
    private sealed class ConnectionRow
    {
        [PrimaryKey] public string Guid { get; set; } = string.Empty;
        public string SourceBuildingGuid { get; set; } = string.Empty;
        public string SourceFloorGuid { get; set; } = string.Empty;
        public string SourceEntityGuid { get; set; } = string.Empty;
        public int SourceFloorIndex { get; set; }
        public string DestinationBuildingGuid { get; set; } = string.Empty;
        public string DestinationFloorGuid { get; set; } = string.Empty;
        public string DestinationEntityGuid { get; set; } = string.Empty;
        public int DestinationFloorIndex { get; set; }
    }

    [Table("factory_doors")]
    private sealed class DoorRow
    {
        [PrimaryKey] public string BuildingGuid { get; set; } = string.Empty;
        public int DoorIndex { get; set; }
        public string WallId { get; set; } = string.Empty;
        public float NormalizedOffset { get; set; }
    }
}

public static class FactoryWorldPaths
{
    public static string GetDefaultDatabasePath()
    {
        return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, FactoryWorldSqliteStore.DatabaseFileName);
    }

    public static string GetProjectRoot()
    {
        return Directory.GetParent(UnityEngine.Application.dataPath).FullName;
    }

    public static string GetLegacyOutsideTestPath()
    {
        return System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, GameSceneManager.OutsideTestStateFileName);
    }

    public static string GetLegacyNaiWorldPath()
    {
        return System.IO.Path.Combine(GetProjectRoot(), "World.db");
    }
}
