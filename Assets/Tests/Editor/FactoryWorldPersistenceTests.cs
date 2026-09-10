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
}
