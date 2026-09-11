// Verifies GUID identity, normalized schema-8 persistence, and migration metadata.
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryWorldPersistenceTests
{
    private string databasePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        databasePath = Path.Combine(
            Application.temporaryCachePath,
            $"factory-world-test-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }

    [Test]
    public void SnapshotRoundTripsGuidIdentityStateAndMigrationMappings()
    {
        var buildingGuid = Guid.NewGuid();
        var floorGuid = Guid.NewGuid();
        var entityGuid = Guid.NewGuid();
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            Vector3Int.zero,
            new Vector2Int(8, 8),
            1,
            false,
            17));
        var floor = new FactoryWorldFloorRecord(
            floorGuid,
            buildingGuid,
            0,
            "Floor 0",
            1f,
            0.5f,
            new Vector2(0.5f, 0.5f));
        floor.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                entityGuid,
                floorGuid,
                "test-machine",
                new Vector2(2f, 3f),
                90f,
                Vector2.one,
                new byte[] { 1, 2, 3 },
                4,
                false,
                1f,
                0.25f,
                6,
                7,
                8)
        });
        snapshot.Floors.Add(floor);
        snapshot.MigrationMappings.Add(new FactoryWorldMigrationMapping(
            "building",
            "17",
            buildingGuid));

        var store = new FactoryWorldSqliteStore(databasePath);
        store.Save(snapshot);
        var restored = store.Load();

        Assert.That(restored.Buildings[0].Guid, Is.EqualTo(buildingGuid));
        Assert.That(restored.Floors[0].Guid, Is.EqualTo(floorGuid));
        Assert.That(restored.Floors[0].Entities[0].Guid, Is.EqualTo(entityGuid));
        Assert.That(restored.Floors[0].Entities[0].State, Is.EqualTo(new byte[] { 1, 2, 3 }));
        Assert.That(restored.MigrationMappings[0].LegacyKey, Is.EqualTo("17"));
        Assert.That(restored.MigrationMappings[0].Guid, Is.EqualTo(buildingGuid));
    }

    [Test]
    public void ValidationRejectsDuplicateFloorIndicesWithinOneBuilding()
    {
        var buildingGuid = Guid.NewGuid();
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            Vector3Int.zero,
            new Vector2Int(8, 8),
            2,
            false));
        snapshot.Floors.Add(new FactoryWorldFloorRecord(
            Guid.NewGuid(),
            buildingGuid,
            0,
            "Floor 0",
            1f,
            0f,
            new Vector2(0.5f, 0.5f)));
        snapshot.Floors.Add(new FactoryWorldFloorRecord(
            Guid.NewGuid(),
            buildingGuid,
            0,
            "Duplicate floor 0",
            1f,
            0f,
            new Vector2(0.5f, 0.5f)));

        Assert.That(FactoryWorldValidation.TryValidate(snapshot, out var error), Is.False);
        Assert.That(error, Does.Contain("duplicate floor index"));
    }

    [Test]
    public void CrossBuildingConnectionRoundTripPreservesGuidsAndInventories()
    {
        var buildingAGuid = Guid.NewGuid();
        var buildingBGuid = Guid.NewGuid();
        var floorAGuid = Guid.NewGuid();
        var floorBGuid = Guid.NewGuid();
        var entityAGuid = Guid.NewGuid();
        var entityBGuid = Guid.NewGuid();
        var connectionGuid = Guid.NewGuid();
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingAGuid,
            "outside-test-building",
            Vector3Int.zero,
            new Vector2Int(5, 3),
            1,
            false,
            20));
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingBGuid,
            "outside-test-building",
            new Vector3Int(8, 0, 0),
            new Vector2Int(5, 3),
            1,
            false,
            30));

        var sourceFloor = new FactoryWorldFloorRecord(
            floorAGuid,
            buildingAGuid,
            0,
            "A Floor 0",
            0f,
            0f,
            Vector2.one);
        sourceFloor.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                entityAGuid,
                floorAGuid,
                FactoryEntityDefinitions.TestMachineDefinitionId,
                Vector2.one,
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                4,
                3)
        });
        var destinationFloor = new FactoryWorldFloorRecord(
            floorBGuid,
            buildingBGuid,
            0,
            "B Floor 0",
            0f,
            0f,
            Vector2.one);
        destinationFloor.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                entityBGuid,
                floorBGuid,
                FactoryEntityRecord.StorageDefinitionId,
                Vector2.one,
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                0,
                2)
        });
        snapshot.Floors.Add(sourceFloor);
        snapshot.Floors.Add(destinationFloor);
        snapshot.Connections.Add(new FactoryWorldConnectionRecord(
            connectionGuid,
            new FactoryWorldEndpoint(buildingAGuid, floorAGuid, entityAGuid, 0),
            new FactoryWorldEndpoint(buildingBGuid, floorBGuid, entityBGuid, 0)));

        var store = new FactoryWorldSqliteStore(databasePath);
        store.Save(snapshot);
        var restored = store.Load();

        Assert.That(restored.Connections, Has.Count.EqualTo(1));
        Assert.That(restored.Connections[0].Guid, Is.EqualTo(connectionGuid));
        Assert.That(restored.Connections[0].Source.BuildingGuid, Is.EqualTo(buildingAGuid));
        Assert.That(restored.Connections[0].Source.FloorGuid, Is.EqualTo(floorAGuid));
        Assert.That(restored.Connections[0].Source.EntityGuid, Is.EqualTo(entityAGuid));
        Assert.That(restored.Connections[0].Destination.BuildingGuid, Is.EqualTo(buildingBGuid));
        Assert.That(restored.Connections[0].Destination.FloorGuid, Is.EqualTo(floorBGuid));
        Assert.That(restored.Connections[0].Destination.EntityGuid, Is.EqualTo(entityBGuid));
        var restoredSourceFloor = restored.Floors.Find(floor => floor.Guid == floorAGuid);
        var restoredDestinationFloor = restored.Floors.Find(floor => floor.Guid == floorBGuid);
        Assert.That(restoredSourceFloor.Entities[0].OutputCount, Is.EqualTo(3));
        Assert.That(restoredDestinationFloor.Entities[0].OutputCount, Is.EqualTo(2));
    }
}
