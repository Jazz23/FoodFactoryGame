// Verifies conveyor travel, turns, blocked receivers, and item conservation independently of scene presentation.
using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryConveyorTests
{
    private static FactoryEntityRecord Entity(uint id, string definition, float x, float y)
        => new(id, definition, new Vector2(x, y), 1f, 0f, 0);

    [Test]
    public void DotMovesContinuouslyAcrossSimulationTicksAndBeltCorners()
    {
        var first = Entity(1, "conveyor-east", 1.5f, 0.5f);
        var second = Entity(2, "conveyor-east", 2.5f, 0.5f);
        var corner = Entity(3, "conveyor-north", 3.5f, 0.5f);
        var last = Entity(4, "conveyor-north", 3.5f, 1.5f);
        var storage = Entity(5, FactoryEntityDefinitions.TestStorageDefinitionId, 3.5f, 2.5f);
        var entities = new[] { first, second, corner, last, storage };
        first.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        var simulation = new FactorySimulation(delta =>
        {
            foreach (var entity in entities) entity.Advance(delta);
            FactoryConveyor.TransferAdjacent(entities);
        });
        var previous = FactoryConveyor.ItemPosition(first, entities, 0f);
        var step = 1f / 120f;
        for (var frame = 0; frame < 288; frame++)
        {
            simulation.Advance(step);
            if (storage.OutputCount > 0) break;
            foreach (var belt in entities)
            {
                if (!belt.IsConveyor || belt.InputCount + belt.OutputCount == 0) continue;
                var progress = belt.OutputCount > 0 ? 1f : belt.CycleProgress + simulation.Remainder / 0.6f;
                var current = FactoryConveyor.ItemPosition(belt, entities, progress);
                var distance = Vector2.Distance(previous, current);
                Assert.That(distance, Is.GreaterThan(step / 0.6f * 0.65f), $"Dot paused on frame {frame}.");
                Assert.That(distance, Is.LessThanOrEqualTo(step / 0.6f + 0.0001f), $"Dot jumped on frame {frame}.");
                previous = current;
            }
        }
        Assert.That(storage.OutputCount, Is.EqualTo(1));
    }

    [Test]
    public void SourceBeltTurnAndStorageDeliverWithoutCreatingItems()
    {
        var source = Entity(1, FactoryEntityDefinitions.TestMachineDefinitionId, 0.5f, 0.5f);
        var east = Entity(2, "conveyor-east", 1.5f, 0.5f);
        var north = Entity(3, "conveyor-north", 2.5f, 0.5f);
        var storage = Entity(4, FactoryEntityDefinitions.TestStorageDefinitionId, 2.5f, 1.5f);
        var entities = new[] { source, east, north, storage };
        source.AddOutput(3);
        FactoryConveyor.TransferAdjacent(entities);
        Assert.That(east.InputCount, Is.EqualTo(1));
        Assert.That(storage.OutputCount, Is.Zero);
        for (var tick = 0; tick < 60; tick++)
        {
            east.Advance(0.1f);
            north.Advance(0.1f);
            FactoryConveyor.TransferAdjacent(entities);
        }
        Assert.That(storage.OutputCount, Is.EqualTo(3));
        Assert.That(source.OutputCount + east.InputCount + east.OutputCount + north.InputCount + north.OutputCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FourItemQueuesBackUpAcrossBeltsAndResumeWithoutLoss(bool reverseOrder)
    {
        var source = Entity(1, FactoryEntityDefinitions.TestMachineDefinitionId, 0.5f, 0.5f);
        var first = Entity(2, "conveyor-east", 1.5f, 0.5f);
        var second = Entity(3, "conveyor-east", 2.5f, 0.5f);
        var storage = Entity(4, FactoryEntityDefinitions.TestStorageDefinitionId, 3.5f, 0.5f);
        var entities = reverseOrder ? new[] { storage, second, first, source } : new[] { source, first, second, storage };
        source.AddOutput(12);
        storage.AddOutput(100);
        for (var tick = 0; tick < 100; tick++)
        {
            first.Advance(0.1f);
            second.Advance(0.1f);
            FactoryConveyor.TransferAdjacent(entities);
            Assert.That(source.OutputCount + first.ConveyorPositions.Count + second.ConveyorPositions.Count + storage.OutputCount, Is.EqualTo(112));
            var projected = FactoryConveyor.PredictQueues(entities, 0.05f);
            foreach (var belt in new[] { first, second })
            {
                var values = projected[belt.EntityId];
                Assert.That(values.Length, Is.LessThanOrEqualTo(4));
                for (var index = 1; index < values.Length; index++)
                    Assert.That(values[index - 1] - values[index], Is.GreaterThanOrEqualTo(0.24999f));
            }
            if (projected[first.EntityId].Length > 0 && projected[second.EntityId].Length > 0)
            {
                var ahead = projected[second.EntityId];
                Assert.That(1f + ahead[ahead.Length - 1] - projected[first.EntityId][0], Is.GreaterThanOrEqualTo(0.24999f));
            }
        }
        Assert.That(first.ConveyorPositions, Is.EqualTo(new[] { 1f, 0.75f, 0.5f, 0.25f }).Within(0.0001f));
        Assert.That(second.ConveyorPositions, Is.EqualTo(first.ConveyorPositions));
        Assert.That(source.OutputCount, Is.EqualTo(4));
        storage.DrainOutput();
        for (var tick = 0; tick < 100; tick++)
        {
            first.Advance(0.1f);
            second.Advance(0.1f);
            FactoryConveyor.TransferAdjacent(entities);
        }
        Assert.That(storage.OutputCount, Is.EqualTo(12));
        Assert.That(source.OutputCount + first.ConveyorPositions.Count + second.ConveyorPositions.Count, Is.Zero);
    }

    [Test]
    public void QueueSnapshotsAndLegacySaveMigrationPreserveEveryPosition()
    {
        var positions = new[] { 1f, 0.7f, 0.4f, 0.1f };
        var belt = new FactoryEntityRecord(1, "conveyor-east", Vector2.one, 1f, 0f, 0, 0, 0, positions);
        var snapshot = belt.ToSnapshot();
        var restored = FactoryEntityRecord.FromSnapshot(snapshot);
        var clone = belt.Clone();
        snapshot.ConveyorPositions[0] = 0f;
        Assert.That(restored.ConveyorPositions, Is.EqualTo(positions));
        Assert.That(clone.ConveyorPositions, Is.EqualTo(positions));
        Assert.That(FactoryConveyorQueue.Decode(belt.GetConveyorState()), Is.EqualTo(positions));
        var oldMoving = new FactoryEntityRecord(1, "conveyor-east", Vector2.one, 1f, 0.4f, 0, 0, 1);
        var oldBlocked = new FactoryEntityRecord(1, "conveyor-east", Vector2.one, 1f, 0f, 0, 1, 0);
        Assert.That(oldMoving.ConveyorPositions, Is.EqualTo(new[] { 0.4f }));
        Assert.That(oldBlocked.ConveyorPositions, Is.EqualTo(new[] { 1f }));
        Assert.Throws<InvalidDataException>(() => FactoryConveyorQueue.Decode(new byte[] { 1, 2, 3 }));
    }

    [Test]
    public void QueuePositionsRoundTripThroughLegacyAndUnifiedSqlite()
    {
        var path = Path.Combine(Application.temporaryCachePath, $"conveyor-queue-{Guid.NewGuid():N}.db");
        var unifiedPath = path + ".unified";
        try
        {
            var positions = new[] { 1f, 0.75f, 0.5f, 0.25f };
            var belt = new FactoryEntityRecord(1, "conveyor-east", Vector2.one * 0.5f, 1f, 0f, 0, 0, 0, positions);
            var data = new OutsideTestFloorSaveData { Version = FactoryWorldState.CurrentSaveVersion };
            data.Buildings.Add(new BuildingRecord(1, Vector3Int.zero, new Vector2Int(4, 4), 1, Array.Empty<BuildingRecord.DoorPlacement>()));
            var floor = new OutsideTestFloorRecord(1, 0, "Queue test", 0f, 0f, Vector2.one);
            floor.SetEntities(new[] { belt });
            data.Floors.Add(floor);
            var legacy = new OutsideTestFloorSqliteStore();
            legacy.Save(path, data.Buildings, data.Floors, data.Connections);
            Assert.That(legacy.Load(path).Floors[0].Entities[0].ConveyorPositions, Is.EqualTo(positions));
            var migrated = new FactoryWorldMigration(path, string.Empty).Import();
            var unified = new FactoryWorldSqliteStore(unifiedPath);
            unified.Save(migrated);
            Assert.That(FactoryConveyorQueue.Decode(unified.Load().Floors[0].Entities[0].State), Is.EqualTo(positions));
            Assert.That(legacy.Load(unifiedPath).Floors[0].Entities[0].ConveyorPositions, Is.EqualTo(positions));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(unifiedPath)) File.Delete(unifiedPath);
        }
    }

    [Test]
    public void GapAndOpposingBeltDoNotTransfer()
    {
        var east = Entity(1, "conveyor-east", 0.5f, 0.5f);
        var west = Entity(2, "conveyor-west", 1.5f, 0.5f);
        var storage = Entity(3, FactoryEntityDefinitions.TestStorageDefinitionId, 3.5f, 0.5f);
        east.AddOutput(1);
        FactoryConveyor.TransferAdjacent(new[] { east, west, storage });
        Assert.That(east.OutputCount, Is.EqualTo(1));
        Assert.That(west.InputCount, Is.Zero);
        Assert.That(storage.OutputCount, Is.Zero);
    }

    [Test]
    public void OccupancyUsesWholeCellAndConveyorSnapshotPreservesTravel()
    {
        var belt = Entity(1, "conveyor-south", 1.5f, 2.5f);
        belt.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        belt.Advance(0.3f);
        var restored = FactoryEntityRecord.FromSnapshot(belt.ToSnapshot());
        Assert.That(FactoryConveyor.IsOccupied(new[] { belt }, new Vector2(1.1f, 2.9f)), Is.True);
        Assert.That(FactoryConveyor.IsOccupied(new[] { belt }, new Vector2(2.5f, 2.5f)), Is.False);
        Assert.That(restored.IsConveyor, Is.True);
        Assert.That(restored.CycleProgress, Is.EqualTo(0.5f).Within(0.001f));
        Assert.That(restored.InputCount, Is.EqualTo(1));
        Assert.That(restored.GetAcceptCapacity(FactoryEntityDefinitions.TestProductId), Is.EqualTo(1));
        Assert.That(restored.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1), Is.EqualTo(1));
        Assert.That(restored.GetAcceptCapacity(FactoryEntityDefinitions.TestProductId), Is.Zero);
    }
}
