// Verifies scene-independent recipe processing, item capabilities, transport, and persistence.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using OutsideTestFloorStateOwner = FactoryWorldState;

public sealed class FactoryRecipeSimulationTests
{
    [Test]
    public void ProcessorWaitsForInputsConsumesAtomicallyAndDoesNotBuildBacklog()
    {
        var processor = new FactoryEntityRecord(
            1u,
            FactoryEntityRecord.ProcessorDefinitionId,
            Vector2.one,
            1f,
            0f,
            0);

        processor.Advance(5f);
        Assert.That(processor.InputCount, Is.Zero);
        Assert.That(processor.OutputCount, Is.Zero);
        Assert.That(processor.CycleProgress, Is.Zero);

        Assert.That(processor.TryAcceptItem(
            FactoryEntityRecord.OutputProductId,
            2,
            out var accepted), Is.True);
        Assert.That(accepted, Is.EqualTo(2));

        processor.Advance(0.25f);
        Assert.That(processor.InputCount, Is.EqualTo(2));
        Assert.That(processor.OutputCount, Is.Zero);
        Assert.That(processor.CycleProgress, Is.EqualTo(0.25f).Within(0.0001f));

        processor.Advance(0.75f);
        Assert.That(processor.InputCount, Is.Zero);
        Assert.That(processor.OutputCount, Is.EqualTo(1));
        Assert.That(processor.ProducedCount, Is.EqualTo(1));
        Assert.That(processor.CycleProgress, Is.Zero);

        processor.Advance(5f);
        Assert.That(processor.InputCount, Is.Zero);
        Assert.That(processor.OutputCount, Is.EqualTo(1));
        Assert.That(processor.ProducedCount, Is.EqualTo(1));
        Assert.That(processor.CycleProgress, Is.Zero);
    }

    [Test]
    public void ProcessorPreservesProgressWhenOutputIsFull()
    {
        var processor = new FactoryEntityRecord(
            1u,
            FactoryEntityRecord.ProcessorDefinitionId,
            Vector2.one,
            1f,
            0.25f,
            8,
            FactoryEntityRecord.OutputCapacity,
            2);

        processor.Advance(4f);

        Assert.That(processor.InputCount, Is.EqualTo(2));
        Assert.That(processor.OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(processor.ProducedCount, Is.EqualTo(8));
        Assert.That(processor.CycleProgress, Is.EqualTo(0.25f).Within(0.0001f));
    }

    [Test]
    public void DefinitionsAcceptOnlyTheirDeclaredItems()
    {
        var processor = new FactoryEntityRecord(
            1u,
            FactoryEntityRecord.ProcessorDefinitionId,
            Vector2.one,
            1f,
            0f,
            0);
        var packedStorage = new FactoryEntityRecord(
            2u,
            FactoryEntityRecord.PackedStorageDefinitionId,
            Vector2.one,
            0f,
            0f,
            0);

        Assert.That(processor.IsReceiver, Is.True);
        Assert.That(processor.IsProducer, Is.True);
        Assert.That(processor.AcceptedItemId, Is.EqualTo(FactoryEntityRecord.OutputProductId));
        Assert.That(processor.ProducedItemId, Is.EqualTo(FactoryEntityRecord.PackedProductId));
        Assert.That(processor.TryAcceptItem(
            FactoryEntityRecord.PackedProductId,
            1,
            out var processorAccepted), Is.False);
        Assert.That(processorAccepted, Is.Zero);
        Assert.That(packedStorage.TryAcceptItem(
            FactoryEntityRecord.PackedProductId,
            1,
            out var storageAccepted), Is.True);
        Assert.That(storageAccepted, Is.EqualTo(1));
        Assert.That(packedStorage.TryAcceptItem(
            FactoryEntityRecord.OutputProductId,
            1,
            out var wrongItemAccepted), Is.False);
        Assert.That(wrongItemAccepted, Is.Zero);
    }

    [Test]
    public void MatchingRecipeChainTransfersOneItemPerConnectionAndKeepsDirectionsIndependent()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(8, 3, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(8, 0, out var sourceFloor);
        owner.TryGetFloorState(8, 1, out var processorFloor);
        owner.TryGetFloorState(8, 2, out var storageFloor);
        var source = new FactoryEntityRecord(
            1u,
            FactoryEntityDefinitions.TestMachineDefinitionId,
            Vector2.one,
            0f,
            0f,
            0,
            2);
        var processor = new FactoryEntityRecord(
            2u,
            FactoryEntityRecord.ProcessorDefinitionId,
            Vector2.one,
            1f,
            0f,
            0);
        var packedStorage = new FactoryEntityRecord(
            3u,
            FactoryEntityRecord.PackedStorageDefinitionId,
            Vector2.one,
            0f,
            0f,
            0);
        sourceFloor.SetEntities(new[] { source });
        processorFloor.SetEntities(new[] { processor });
        storageFloor.SetEntities(new[] { packedStorage });

        var sourceEndpoint = new FactoryEntityEndpoint(8, 0, source.EntityId);
        var processorEndpoint = new FactoryEntityEndpoint(8, 1, processor.EntityId);
        var storageEndpoint = new FactoryEntityEndpoint(8, 2, packedStorage.EntityId);
        Assert.That(owner.TryAddConnection(sourceEndpoint, processorEndpoint, out var error), Is.True, error);
        Assert.That(owner.TryAddConnection(processorEndpoint, storageEndpoint, out error), Is.True, error);

        owner.AdvanceProduction(0.1f);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(processorFloor.Entities[0].InputCount, Is.EqualTo(1));
        Assert.That(processorFloor.Entities[0].OutputCount, Is.Zero);
        Assert.That(storageFloor.Entities[0].OutputCount, Is.Zero);

        owner.AdvanceProduction(0.1f);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.Zero);
        Assert.That(processorFloor.Entities[0].InputCount, Is.EqualTo(2));

        owner.AdvanceProduction(1f);
        Assert.That(processorFloor.Entities[0].InputCount, Is.Zero);
        Assert.That(processorFloor.Entities[0].OutputCount, Is.Zero);
        Assert.That(storageFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(processorFloor.Entities[0].ProducedCount, Is.EqualTo(1));

        Assert.That(owner.TryRemoveConnectionForEndpoint(
            processorEndpoint,
            FactoryEntityConnectionDirection.Incoming,
            out var removedIncoming,
            out error), Is.True, error);
        Assert.That(removedIncoming.Source, Is.EqualTo(sourceEndpoint));
        Assert.That(owner.TryGetConnectionForSource(processorEndpoint, out _), Is.True);
        Assert.That(owner.TryRemoveConnectionForEndpoint(
            processorEndpoint,
            FactoryEntityConnectionDirection.Outgoing,
            out var removedOutgoing,
            out error), Is.True, error);
        Assert.That(removedOutgoing.Destination, Is.EqualTo(storageEndpoint));
    }

    [Test]
    public void ConnectionsRejectIncompatibleReceiverItems()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(9, 2, new Vector2Int(5, 4), out _);
        owner.TryGetFloorState(9, 0, out var sourceFloor);
        owner.TryGetFloorState(9, 1, out var destinationFloor);
        var source = new FactoryEntityRecord(1, "test-machine", Vector2.one, 0f, 0f, 0, 1);
        var packedStorage = new FactoryEntityRecord(
            2,
            FactoryEntityRecord.PackedStorageDefinitionId,
            Vector2.one,
            0f,
            0f,
            0);
        sourceFloor.SetEntities(new[] { source });
        destinationFloor.SetEntities(new[] { packedStorage });

        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(9, 0, source.EntityId),
            new FactoryEntityEndpoint(9, 1, packedStorage.EntityId),
            out var error), Is.False);
        Assert.That(error, Does.Contain("item types"));
        Assert.That(owner.ConnectionCount, Is.Zero);
    }

    [Test]
    public void V7SnapshotsPreserveProcessorInputAndV6MigrationClearsIt()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(10, 1, new Vector2Int(5, 4), out _);
        source.TryGetFloorState(10, 0, out var sourceFloor);
        var processor = new FactoryEntityRecord(
            4,
            FactoryEntityRecord.ProcessorDefinitionId,
            new Vector2(1.5f, 1.5f),
            1f,
            0.4f,
            3,
            2,
            1);
        sourceFloor.SetEntities(new[] { processor });

        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(source.CaptureState()), Is.True);
        restored.TryGetFloorState(10, 0, out var restoredFloor);
        Assert.That(restoredFloor.Entities[0].InputCount, Is.EqualTo(1));
        Assert.That(restoredFloor.Entities[0].CycleProgress, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(restoredFloor.Entities[0].OutputCount, Is.EqualTo(2));

        var versionSixData = new OutsideTestFloorSaveData
        {
            Version = OutsideTestFloorStateOwner.ConnectionsSaveVersion,
            Buildings = new List<BuildingRecord>
            {
                new BuildingRecord(10, Vector3Int.zero, new Vector2Int(5, 4), 1)
            },
            Floors = new List<OutsideTestFloorRecord> { sourceFloor },
            Connections = new List<FactoryEntityConnectionRecord>()
        };
        var migrated = new OutsideTestFloorStateOwner(1);
        Assert.That(migrated.LoadState(versionSixData), Is.True);
        migrated.TryGetFloorState(10, 0, out var migratedFloor);
        Assert.That(migratedFloor.Entities[0].InputCount, Is.Zero);
    }
}
