// Verifies the bounded prototype fixture's logical topology, machine state, and isolated persistence path.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class Factory3DPrototypeFixtureTests
{
    [Test]
    public void TwoFloorFixturePreservesTopologyThroughIsolatedSaveLoad()
    {
        var buildingId = 9001u;
        var state = new FactoryWorldState(buildingId);
        Assert.That(state.TryRegisterBuilding(buildingId, 2, new Vector2Int(8, 6), out var registrationError), Is.True, registrationError);
        Assert.That(state.TryAddTestEntity(buildingId, 0, "conveyor-east", new Vector2(2.5f, 2.5f), out var lowerConveyorId, out var lowerError), Is.True, lowerError);
        Assert.That(state.TryAddTestEntity(buildingId, 1, "conveyor-east", new Vector2(2.5f, 2.5f), out var upperConveyorId, out var upperError), Is.True, upperError);
        Assert.That(state.TryAddTestMachine(buildingId, 0, new Vector2(1.5f, 1.5f), out var machineId, out var machineError), Is.True, machineError);
        Assert.That(state.TryAddConnection(
            new FactoryEntityEndpoint(buildingId, 0, lowerConveyorId),
            new FactoryEntityEndpoint(buildingId, 1, upperConveyorId),
            out var connectionError), Is.True, connectionError);
        state.AdvanceProduction(1f);

        var path = Path.Combine(Path.GetTempPath(), $"food-factory-3d-fixture-{Guid.NewGuid():N}.db");
        try
        {
            var captured = state.CaptureState();
            new OutsideTestFloorSqliteStore().Save(
                path,
                state.BuildingRecords,
                captured.Floors,
                captured.Connections);

            var reloaded = new FactoryWorldState(buildingId);
            Assert.That(reloaded.LoadFromFile(path), Is.True);
            Assert.That(reloaded.BuildingRecords.Count(), Is.EqualTo(1));
            Assert.That(reloaded.BuildingRecords.Single().BuildingInstanceId, Is.EqualTo(buildingId));
            Assert.That(reloaded.BuildingRecords.Single().StoryCount, Is.EqualTo(2));
            Assert.That(reloaded.BuildingRecords.Single().FootprintSize, Is.EqualTo(new Vector2Int(8, 6)));
            Assert.That(reloaded.FloorStates.Count(), Is.EqualTo(2));
            Assert.That(reloaded.ConnectionCount, Is.EqualTo(1));
            var reloadedConnection = reloaded.Connections.Single();
            Assert.That(reloadedConnection.Source.EntityId, Is.EqualTo(lowerConveyorId));
            Assert.That(reloadedConnection.Source.FloorIndex, Is.EqualTo(0));
            Assert.That(reloadedConnection.Destination.EntityId, Is.EqualTo(upperConveyorId));
            Assert.That(reloadedConnection.Destination.FloorIndex, Is.EqualTo(1));
            Assert.That(reloaded.TryGetFloorState(buildingId, 0, out var lowerFloor), Is.True);
            Assert.That(reloaded.TryGetFloorState(buildingId, 1, out var upperFloor), Is.True);
            Assert.That(lowerFloor.Entities.Any(entity => entity.EntityId == lowerConveyorId), Is.True);
            Assert.That(upperFloor.Entities.Any(entity => entity.EntityId == upperConveyorId), Is.True);
            Assert.That(lowerFloor.TryGetEntity(machineId, out var machine), Is.True);
            Assert.That(machine.ProducedCount, Is.GreaterThan(0));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
