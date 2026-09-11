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
    public void CrossBuildingFloorZeroConnectionTransfersWithOverlappingEntityIds()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(5, 3), out _);
        owner.TryRegisterBuilding(30, 1, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(20, 0, out var sourceFloor);
        owner.TryGetFloorState(30, 0, out var destinationFloor);
        sourceFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(
                1,
                FactoryEntityDefinitions.TestMachineDefinitionId,
                Vector2.one,
                0f,
                0f,
                0,
                3)
        });
        destinationFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(
                1,
                FactoryEntityRecord.StorageDefinitionId,
                Vector2.one,
                0f,
                0f,
                0)
        });

        var sourceEndpoint = new FactoryEntityEndpoint(20, 0, 1);
        var destinationEndpoint = new FactoryEntityEndpoint(30, 0, 1);
        Assert.That(owner.TryAddConnection(
                sourceEndpoint,
                destinationEndpoint,
                out var error), Is.True, error);

        owner.AdvanceProduction(0.1f);

        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(2));
        Assert.That(destinationFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(owner.Connections[0].Source, Is.EqualTo(sourceEndpoint));
        Assert.That(owner.Connections[0].Destination, Is.EqualTo(destinationEndpoint));
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

    [Test]
    public void TerminalsUseOneBoundedInventoryAndExposeItToOutgoingTransfers()
    {
        var sendingTerminal = new FactoryEntityRecord(
            1,
            FactoryEntityRecord.SendingTerminalDefinitionId,
            Vector2.one,
            1f,
            0.5f,
            9,
            FactoryEntityRecord.OutputCapacity - 1,
            11);
        var receivingTerminal = new FactoryEntityRecord(
            2,
            FactoryEntityRecord.ReceivingTerminalDefinitionId,
            Vector2.one,
            1f,
            0.5f,
            9);
        var processor = new FactoryEntityRecord(
            3,
            FactoryEntityRecord.ProcessorDefinitionId,
            Vector2.one,
            1f,
            0f,
            0);

        Assert.That(sendingTerminal.IsReceiver, Is.True);
        Assert.That(sendingTerminal.IsProducer, Is.False);
        Assert.That(sendingTerminal.IsSupplier, Is.True);
        Assert.That(sendingTerminal.AcceptedItemId, Is.EqualTo(FactoryEntityDefinitions.TestProductId));
        Assert.That(sendingTerminal.SuppliedItemId, Is.EqualTo(FactoryEntityDefinitions.TestProductId));
        Assert.That(sendingTerminal.InputCount, Is.Zero);
        Assert.That(sendingTerminal.InventoryCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity - 1));
        Assert.That(sendingTerminal.InventoryCapacity, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(sendingTerminal.TryAcceptItem(
            FactoryEntityDefinitions.PackedProductId,
            1,
            out var wrongItemAccepted), Is.False);
        Assert.That(wrongItemAccepted, Is.Zero);
        Assert.That(sendingTerminal.TryAcceptItem(
            FactoryEntityDefinitions.TestProductId,
            1,
            out var accepted), Is.True);
        Assert.That(accepted, Is.EqualTo(1));
        Assert.That(sendingTerminal.InventoryCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));

        Assert.That(FactoryItemTransfer.TryTransfer(sendingTerminal, receivingTerminal, 1), Is.EqualTo(1));
        Assert.That(sendingTerminal.InventoryCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity - 1));
        Assert.That(receivingTerminal.InventoryCount, Is.EqualTo(1));
        Assert.That(receivingTerminal.InputCount, Is.Zero);
        Assert.That(FactoryItemTransfer.TryTransfer(receivingTerminal, processor, 1), Is.EqualTo(1));
        Assert.That(receivingTerminal.InventoryCount, Is.Zero);
        Assert.That(processor.InputCount, Is.EqualTo(1));
    }

    [Test]
    public void TerminalConnectionRulesAllowMilestoneLinksAndRejectInvalidRoles()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(10, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(20, 2, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(10, 0, out var buildingAFloor);
        owner.TryGetFloorState(20, 0, out var buildingBFloor);
        owner.TryGetFloorState(20, 1, out var buildingBUpperFloor);
        buildingAFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityDefinitions.TestMachineDefinitionId, Vector2.one, 0f, 0f, 0),
            new FactoryEntityRecord(2, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0)
        });
        buildingBFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0),
            new FactoryEntityRecord(2, FactoryEntityRecord.ProcessorDefinitionId, Vector2.one, 1f, 0f, 0)
        });
        buildingBUpperFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.PackedStorageDefinitionId, Vector2.one, 0f, 0f, 0)
        });

        var producer = new FactoryEntityEndpoint(10, 0, 1);
        var sendingTerminal = new FactoryEntityEndpoint(10, 0, 2);
        var receivingTerminal = new FactoryEntityEndpoint(20, 0, 1);
        var processor = new FactoryEntityEndpoint(20, 0, 2);
        var storage = new FactoryEntityEndpoint(20, 1, 1);
        Assert.That(owner.TryAddConnection(producer, sendingTerminal, out var error), Is.True, error);
        Assert.That(owner.TryAddConnection(sendingTerminal, receivingTerminal, out error), Is.True, error);
        Assert.That(owner.TryAddConnection(receivingTerminal, processor, out error), Is.True, error);
        Assert.That(owner.TryAddConnection(processor, storage, out error), Is.True, error);
        Assert.That(owner.ConnectionCount, Is.EqualTo(4));

        var producerDefinition = FactoryEntityDefinitions.Get(FactoryEntityDefinitions.TestMachineDefinitionId);
        var sendingDefinition = FactoryEntityDefinitions.Get(FactoryEntityRecord.SendingTerminalDefinitionId);
        var receivingDefinition = FactoryEntityDefinitions.Get(FactoryEntityRecord.ReceivingTerminalDefinitionId);
        var processorDefinition = FactoryEntityDefinitions.Get(FactoryEntityRecord.ProcessorDefinitionId);
        var storageDefinition = FactoryEntityDefinitions.Get(FactoryEntityRecord.StorageDefinitionId);
        Assert.That(FactoryConnectionRules.TryValidate(
            producerDefinition,
            receivingDefinition,
            false,
            false,
            out error), Is.False);
        Assert.That(error, Does.Contain("receiving terminal"));
        Assert.That(FactoryConnectionRules.TryValidate(
            sendingDefinition,
            processorDefinition,
            true,
            true,
            out error), Is.False);
        Assert.That(error, Does.Contain("sending terminal"));
        Assert.That(FactoryConnectionRules.TryValidate(
            receivingDefinition,
            storageDefinition,
            false,
            false,
            out error), Is.False);
        Assert.That(error, Does.Contain("receiving terminal"));
        Assert.That(FactoryConnectionRules.TryValidate(
            producerDefinition,
            storageDefinition,
            true,
            true,
            out error), Is.False);
        Assert.That(error, Does.Contain("different floors"));
    }

    [Test]
    public void CompleteTerminalRecipeChainUsesExistingRecipeQuantitiesWithoutScenes()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(10, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(20, 2, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(10, 0, out var buildingAFloor);
        owner.TryGetFloorState(20, 0, out var buildingBFloor);
        owner.TryGetFloorState(20, 1, out var buildingBUpperFloor);
        buildingAFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityDefinitions.TestMachineDefinitionId, Vector2.one, 10f, 0f, 0),
            new FactoryEntityRecord(2, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0)
        });
        buildingBFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0),
            new FactoryEntityRecord(2, FactoryEntityRecord.ProcessorDefinitionId, Vector2.one, 1f, 0f, 0)
        });
        buildingBUpperFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.PackedStorageDefinitionId, Vector2.one, 0f, 0f, 0)
        });

        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 1),
            new FactoryEntityEndpoint(10, 0, 2),
            out var error), Is.True, error);
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 2),
            new FactoryEntityEndpoint(20, 0, 1),
            out error), Is.True, error);
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(20, 0, 1),
            new FactoryEntityEndpoint(20, 0, 2),
            out error), Is.True, error);
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(20, 0, 2),
            new FactoryEntityEndpoint(20, 1, 1),
            out error), Is.True, error);

        var simulation = new FactorySimulation(owner.AdvanceProduction);
        Assert.That(simulation.Advance(0.3f), Is.EqualTo(3));
        Assert.That(buildingAFloor.Entities[0].ProducedCount, Is.EqualTo(3));
        Assert.That(buildingAFloor.Entities[0].OutputCount, Is.Zero);
        Assert.That(buildingAFloor.Entities[1].InventoryCount, Is.Zero);
        Assert.That(buildingBFloor.Entities[0].InventoryCount, Is.Zero);
        Assert.That(buildingBFloor.Entities[1].InputCount, Is.EqualTo(3));
        Assert.That(buildingBFloor.Entities[1].OutputCount, Is.Zero);
        Assert.That(buildingBFloor.Entities[1].ProducedCount, Is.Zero);
        Assert.That(buildingBUpperFloor.Entities[0].OutputCount, Is.Zero);
    }

    [Test]
    public void FullReceivingTerminalBlocksUpstreamAndResumesOneItemPerTick()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(10, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(10, 0, out var sourceFloor);
        owner.TryGetFloorState(20, 0, out var destinationFloor);
        sourceFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 2)
        });
        destinationFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(
                1,
                FactoryEntityRecord.ReceivingTerminalDefinitionId,
                Vector2.one,
                0f,
                0f,
                0,
                FactoryEntityRecord.OutputCapacity)
        });
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 1),
            new FactoryEntityEndpoint(20, 0, 1),
            out var error), Is.True, error);

        new FactorySimulation(owner.AdvanceProduction).Advance(0.1f);
        Assert.That(sourceFloor.Entities[0].InventoryCount, Is.EqualTo(2));
        Assert.That(destinationFloor.Entities[0].InventoryCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));

        destinationFloor.Entities[0].DrainOutput();
        new FactorySimulation(owner.AdvanceProduction).Advance(0.1f);
        Assert.That(sourceFloor.Entities[0].InventoryCount, Is.EqualTo(1));
        Assert.That(destinationFloor.Entities[0].InventoryCount, Is.EqualTo(1));
    }

    [Test]
    public void RemovingEitherTerminalCleansItsLinksAndDiscardsOnlyItsContents()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(10, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(10, 0, out var sourceFloor);
        owner.TryGetFloorState(20, 0, out var destinationFloor);
        sourceFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityDefinitions.TestMachineDefinitionId, Vector2.one, 0f, 0f, 0, 4),
            new FactoryEntityRecord(2, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 3)
        });
        destinationFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 2)
        });
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 1),
            new FactoryEntityEndpoint(10, 0, 2),
            out var error), Is.True, error);
        Assert.That(owner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 2),
            new FactoryEntityEndpoint(20, 0, 1),
            out error), Is.True, error);

        Assert.That(owner.TryRemoveTestEntity(10, 0, 2, out error), Is.True, error);
        Assert.That(owner.ConnectionCount, Is.Zero);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(4));
        Assert.That(destinationFloor.Entities[0].InventoryCount, Is.EqualTo(2));
        Assert.That(sourceFloor.Entities, Has.Count.EqualTo(1));

        var secondOwner = new OutsideTestFloorStateOwner(1);
        secondOwner.TryRegisterBuilding(10, 1, new Vector2Int(6, 4), out _);
        secondOwner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        secondOwner.TryGetFloorState(10, 0, out var secondSourceFloor);
        secondOwner.TryGetFloorState(20, 0, out var secondDestinationFloor);
        secondSourceFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 3)
        });
        secondDestinationFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 2)
        });
        Assert.That(secondOwner.TryAddConnection(
            new FactoryEntityEndpoint(10, 0, 1),
            new FactoryEntityEndpoint(20, 0, 1),
            out error), Is.True, error);
        Assert.That(secondOwner.TryRemoveTestEntity(20, 0, 1, out error), Is.True, error);
        Assert.That(secondOwner.ConnectionCount, Is.Zero);
        Assert.That(secondSourceFloor.Entities[0].InventoryCount, Is.EqualTo(3));
    }
}
