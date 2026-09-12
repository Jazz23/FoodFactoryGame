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

    [Test]
    public void TerminalDefinitionsInventoriesAndCompleteLinksRoundTripThroughSqlite()
    {
        var buildingAGuid = Guid.NewGuid();
        var buildingBGuid = Guid.NewGuid();
        var floorA0Guid = Guid.NewGuid();
        var floorB0Guid = Guid.NewGuid();
        var floorB1Guid = Guid.NewGuid();
        var producerGuid = Guid.NewGuid();
        var sendingGuid = Guid.NewGuid();
        var receivingGuid = Guid.NewGuid();
        var processorGuid = Guid.NewGuid();
        var storageGuid = Guid.NewGuid();
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingAGuid,
            "outside-test-building",
            Vector3Int.zero,
            new Vector2Int(6, 4),
            1,
            false,
            10));
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingBGuid,
            "outside-test-building",
            new Vector3Int(8, 0, 0),
            new Vector2Int(6, 4),
            2,
            false,
            20));

        var floorA = new FactoryWorldFloorRecord(
            floorA0Guid,
            buildingAGuid,
            0,
            "A Ground",
            0f,
            0f,
            Vector2.one);
        floorA.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                producerGuid,
                floorA0Guid,
                FactoryEntityDefinitions.TestMachineDefinitionId,
                Vector2.one,
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                12,
                4),
            new FactoryWorldEntityRecord(
                sendingGuid,
                floorA0Guid,
                FactoryEntityRecord.SendingTerminalDefinitionId,
                new Vector2(2f, 1f),
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                2,
                false,
                0f,
                0f,
                0,
                7,
                0)
        });
        var floorB0 = new FactoryWorldFloorRecord(
            floorB0Guid,
            buildingBGuid,
            0,
            "B Ground",
            0f,
            0f,
            Vector2.one);
        floorB0.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                receivingGuid,
                floorB0Guid,
                FactoryEntityRecord.ReceivingTerminalDefinitionId,
                Vector2.one,
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                0,
                8),
            new FactoryWorldEntityRecord(
                processorGuid,
                floorB0Guid,
                FactoryEntityRecord.ProcessorDefinitionId,
                new Vector2(2f, 1f),
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                2,
                false,
                1f,
                0.25f,
                3,
                2,
                1)
        });
        var floorB1 = new FactoryWorldFloorRecord(
            floorB1Guid,
            buildingBGuid,
            1,
            "B Upper",
            0f,
            0f,
            Vector2.one);
        floorB1.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                storageGuid,
                floorB1Guid,
                FactoryEntityRecord.PackedStorageDefinitionId,
                Vector2.one,
                0f,
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                0,
                5)
        });
        snapshot.Floors.Add(floorA);
        snapshot.Floors.Add(floorB0);
        snapshot.Floors.Add(floorB1);
        snapshot.Connections.Add(new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            new FactoryWorldEndpoint(buildingAGuid, floorA0Guid, producerGuid, 0),
            new FactoryWorldEndpoint(buildingAGuid, floorA0Guid, sendingGuid, 0)));
        snapshot.Connections.Add(new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            new FactoryWorldEndpoint(buildingAGuid, floorA0Guid, sendingGuid, 0),
            new FactoryWorldEndpoint(buildingBGuid, floorB0Guid, receivingGuid, 0)));
        snapshot.Connections.Add(new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            new FactoryWorldEndpoint(buildingBGuid, floorB0Guid, receivingGuid, 0),
            new FactoryWorldEndpoint(buildingBGuid, floorB0Guid, processorGuid, 0)));
        snapshot.Connections.Add(new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            new FactoryWorldEndpoint(buildingBGuid, floorB0Guid, processorGuid, 0),
            new FactoryWorldEndpoint(buildingBGuid, floorB1Guid, storageGuid, 1)));

        var store = new FactoryWorldSqliteStore(databasePath);
        store.Save(snapshot);
        var restored = store.Load();
        var restoredA = restored.Floors.Find(floor => floor.Guid == floorA0Guid);
        var restoredB = restored.Floors.Find(floor => floor.Guid == floorB0Guid);
        var restoredSending = GetEntity(restoredA, sendingGuid);
        var restoredReceiving = GetEntity(restoredB, receivingGuid);

        Assert.That(restored.Connections, Has.Count.EqualTo(4));
        Assert.That(restoredSending.Guid, Is.EqualTo(sendingGuid));
        Assert.That(restoredSending.DefinitionId, Is.EqualTo(FactoryEntityRecord.SendingTerminalDefinitionId));
        Assert.That(restoredSending.OutputCount, Is.EqualTo(7));
        Assert.That(restoredSending.InputCount, Is.Zero);
        Assert.That(restoredReceiving.Guid, Is.EqualTo(receivingGuid));
        Assert.That(restoredReceiving.DefinitionId, Is.EqualTo(FactoryEntityRecord.ReceivingTerminalDefinitionId));
        Assert.That(restoredReceiving.OutputCount, Is.EqualTo(8));
        Assert.That(restoredReceiving.InputCount, Is.Zero);
        Assert.That(HasConnection(restored, producerGuid, sendingGuid), Is.True);
        Assert.That(HasConnection(restored, sendingGuid, receivingGuid), Is.True);
        Assert.That(HasConnection(restored, receivingGuid, processorGuid), Is.True);
        Assert.That(HasConnection(restored, processorGuid, storageGuid), Is.True);
    }

    [Test]
    public void LegacyDockDefinitionsPreserveInventoryAndFacingThroughSqlite()
    {
        var buildingGuid = Guid.NewGuid();
        var floorGuid = Guid.NewGuid();
        var shippingGuid = Guid.NewGuid();
        var receivingGuid = Guid.NewGuid();
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            Vector3Int.zero,
            new Vector2Int(6, 4),
            1,
            false,
            7));
        var floor = new FactoryWorldFloorRecord(
            floorGuid,
            buildingGuid,
            0,
            "Ground",
            0f,
            0f,
            Vector2.one);
        floor.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                shippingGuid,
                floorGuid,
                "sending-terminal",
                new Vector2(1.5f, 0.5f),
                FactoryDock.DirectionToRotation(GridEdgeDirection.West),
                Vector2.one,
                Array.Empty<byte>(),
                1,
                false,
                0f,
                0f,
                0,
                11),
            new FactoryWorldEntityRecord(
                receivingGuid,
                floorGuid,
                "receiving-terminal",
                new Vector2(2.5f, 1.5f),
                FactoryDock.DirectionToRotation(GridEdgeDirection.North),
                Vector2.one,
                Array.Empty<byte>(),
                2,
                false,
                0f,
                0f,
                0,
                7)
        });
        snapshot.Floors.Add(floor);

        var store = new FactoryWorldSqliteStore(databasePath);
        store.Save(snapshot);
        var restored = store.Load();
        var restoredFloor = restored.Floors.Find(candidate => candidate.Guid == floorGuid);
        var restoredShipping = GetEntity(restoredFloor, shippingGuid);
        var restoredReceiving = GetEntity(restoredFloor, receivingGuid);
        var shipping = new FactoryEntityRecord(
            restoredShipping.LegacyEntityId,
            restoredShipping.DefinitionId,
            restoredShipping.LocalPosition,
            restoredShipping.CycleRate,
            restoredShipping.CycleProgress,
            restoredShipping.ProducedCount,
            restoredShipping.OutputCount,
            restoredShipping.InputCount,
            null,
            FactoryDock.RotationToDirection(restoredShipping.RotationZ));
        var receiving = new FactoryEntityRecord(
            restoredReceiving.LegacyEntityId,
            restoredReceiving.DefinitionId,
            restoredReceiving.LocalPosition,
            restoredReceiving.CycleRate,
            restoredReceiving.CycleProgress,
            restoredReceiving.ProducedCount,
            restoredReceiving.OutputCount,
            restoredReceiving.InputCount,
            null,
            FactoryDock.RotationToDirection(restoredReceiving.RotationZ));

        Assert.That(shipping.DefinitionId, Is.EqualTo(FactoryEntityDefinitions.ShippingDockDefinitionId));
        Assert.That(shipping.IsShippingDock, Is.True);
        Assert.That(shipping.InventoryCount, Is.EqualTo(11));
        Assert.That(shipping.DockDirection, Is.EqualTo(GridEdgeDirection.West));
        Assert.That(receiving.DefinitionId, Is.EqualTo(FactoryEntityDefinitions.ReceivingDockDefinitionId));
        Assert.That(receiving.IsReceivingDock, Is.True);
        Assert.That(receiving.InventoryCount, Is.EqualTo(7));
        Assert.That(receiving.DockDirection, Is.EqualTo(GridEdgeDirection.North));
    }

    private static bool HasConnection(
        FactoryWorldSnapshot snapshot,
        Guid sourceEntityGuid,
        Guid destinationEntityGuid)
    {
        foreach (var connection in snapshot.Connections)
        {
            if (connection.Source.EntityGuid == sourceEntityGuid
                && connection.Destination.EntityGuid == destinationEntityGuid)
            {
                return true;
            }
        }

        return false;
    }

    private static FactoryWorldEntityRecord GetEntity(
        FactoryWorldFloorRecord floor,
        Guid entityGuid)
    {
        foreach (var entity in floor.Entities)
        {
            if (entity.Guid == entityGuid)
            {
                return entity;
            }
        }

        return null!;
    }
}
