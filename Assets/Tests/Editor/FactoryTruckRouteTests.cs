// Verifies truck route creation, delivery-cycle simulation, remote-role conflicts, blocking, and persistence.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SQLite;
using UnityEngine;

public sealed class FactoryTruckRouteTests
{
    private const float Tick = 0.1f;

    private string databasePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        databasePath = Path.Combine(
            Application.temporaryCachePath,
            $"factory-truck-test-{Guid.NewGuid():N}.db");
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
    public void TerminalsArePlaceableAndReserveTheirGridCell()
    {
        Assert.That(FactoryConveyor.IsPlaceable(FactoryEntityRecord.SendingTerminalDefinitionId), Is.True);
        Assert.That(FactoryConveyor.IsPlaceable(FactoryEntityRecord.ReceivingTerminalDefinitionId), Is.True);
        Assert.That(FactoryConveyor.IsPlaceable("nai-roomba"), Is.False);

        var owner = new FactoryWorldState(1);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out var registrationError);
        Assert.That(registrationError, Is.Empty);
        Assert.That(
            owner.TryAddSendingTerminal(20, 0, new Vector2(0.5f, 0.5f), out var senderId, out var senderError),
            Is.True,
            senderError);

        owner.TryGetFloorState(20, 0, out var floor);
        Assert.That(FactoryConveyor.IsOccupied(floor.Entities, new Vector2(0.5f, 0.5f)), Is.True, "Terminals must reserve their grid cell.");
        Assert.That(FactoryConveyor.IsOccupied(floor.Entities, new Vector2(1.5f, 0.5f)), Is.False);
        Assert.That(floor.TryGetEntity(senderId, out var sender), Is.True);
        Assert.That(sender.IsSendingTerminal, Is.True);
        Assert.That(sender.InventoryCapacity, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(sender.InventoryCount, Is.Zero);
    }

    [Test]
    public void BeltsDepositIntoSendingTerminalsAndReceivingTerminalsSupplyBelts()
    {
        var owner = new FactoryWorldState(1);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(30, 1, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(20, 0, out var senderFloor);
        owner.TryGetFloorState(30, 0, out var receiverFloor);
        owner.TryAddTestEntity(20, 0, FactoryEntityDefinitions.TestMachineDefinitionId, new Vector2(0.5f, 0.5f), out var machineId, out _);
        owner.TryAddTestEntity(20, 0, "conveyor-east", new Vector2(1.5f, 0.5f), out var senderBeltId, out _);
        owner.TryAddTestEntity(20, 0, FactoryEntityRecord.SendingTerminalDefinitionId, new Vector2(2.5f, 0.5f), out var senderId, out _);
        owner.TryAddTestEntity(30, 0, FactoryEntityRecord.ReceivingTerminalDefinitionId, new Vector2(0.5f, 0.5f), out var receiverId, out _);
        owner.TryAddTestEntity(30, 0, "conveyor-east", new Vector2(1.5f, 0.5f), out var receiverBeltId, out _);
        owner.TryAddTestEntity(30, 0, FactoryEntityRecord.StorageDefinitionId, new Vector2(2.5f, 0.5f), out var storageId, out _);
        senderFloor.TryGetEntity(machineId, out var machine);
        machine.SetState(machineId, machine.DefinitionId, machine.LogicalPosition, 0f, 0f, 0, 2);
        senderFloor.TryGetEntity(senderId, out var sender);
        senderFloor.TryGetEntity(receiverId, out var receiver);
        receiver.SetState(receiverId, receiver.DefinitionId, receiver.LogicalPosition, 0f, 0f, 0, 2);
        senderFloor.TryGetEntity(senderBeltId, out var senderBelt);
        senderFloor.TryGetEntity(receiverBeltId, out var receiverBelt);
        senderFloor.TryGetEntity(storageId, out var storage);

        AdvanceTicks(owner, 8);

        Assert.That(machine.OutputCount, Is.Zero, "Machine output did not travel into the sending terminal.");
        Assert.That(sender.OutputCount, Is.EqualTo(1));
        Assert.That(senderBelt.ConveyorPositions, Has.Count.EqualTo(1));
        Assert.That(receiver.OutputCount, Is.Zero);
        Assert.That(receiverBelt.ConveyorPositions, Has.Count.EqualTo(1));
        Assert.That(storage.OutputCount, Is.EqualTo(1));

        var senderTotal = machine.OutputCount + senderBelt.ConveyorPositions.Count + sender.OutputCount;
        var receiverTotal = receiver.OutputCount + receiverBelt.ConveyorPositions.Count + storage.OutputCount;
        Assert.That(senderTotal, Is.EqualTo(2));
        Assert.That(receiverTotal, Is.EqualTo(2));
    }

    [Test]
    public void TruckLoadsAtTheConfiguredRateAndDepartsWhenFull()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out var receiverFloor, 100, 0);
        senderFloor.TryGetEntity(1, out var sender);
        receiverFloor.TryGetEntity(2, out var receiver);

        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);
        AdvanceTicks(owner, 1);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Loading));
        Assert.That(truck.CargoCount, Is.EqualTo(1), "Loading must respect the configured transfer rate.");
        Assert.That(sender.OutputCount, Is.EqualTo(99));
        AdvanceTicks(owner, 2);
        Assert.That(truck.CargoCount, Is.EqualTo(3));
        AdvanceTicks(owner, 17);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Outbound));
        Assert.That(truck.CargoCount, Is.EqualTo(20));
        Assert.That(truck.RemainingTravelSeconds, Is.EqualTo(FactoryTruckRouteRecord.DefaultOutboundTravelSeconds).Within(0.05f));
        Assert.That(sender.OutputCount, Is.EqualTo(80));
        Assert.That(receiver.OutputCount, Is.Zero);
    }

    [Test]
    public void TruckDepartsAfterThePartialLoadWindowWithWhatItHas()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out _, 5, 0);
        senderFloor.TryGetEntity(1, out var sender);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);

        AdvanceTicks(owner, 5);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Loading));
        Assert.That(truck.CargoCount, Is.EqualTo(5));
        Assert.That(truck.LoadingWindowProgress, Is.EqualTo(0.4f).Within(0.05f));
        Assert.That(sender.OutputCount, Is.Zero);

        AdvanceTicks(owner, 15);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Loading), "The truck must not depart before the window expires.");
        AdvanceTicks(owner, 1);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Outbound));
        Assert.That(truck.CargoCount, Is.EqualTo(5));
        Assert.That(truck.LoadingWindowProgress, Is.LessThan(FactoryTruckRouteRecord.DefaultPartialLoadDepartureWindowSeconds + 0.1f));
    }

    [Test]
    public void EmptyTruckRemainsAtTheSenderWithoutAdvancingTheWindow()
    {
        var owner = CreateRoutedWorld(out _, out _, 0, 0);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);

        AdvanceTicks(owner, 50);

        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Loading));
        Assert.That(truck.CargoCount, Is.Zero);
        Assert.That(truck.LoadingWindowProgress, Is.Zero);
    }

    [Test]
    public void TruckCompletesRepeatedDeliveryCyclesAndConservesItems()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out var receiverFloor, 60, 0);
        senderFloor.TryGetEntity(1, out var sender);
        receiverFloor.TryGetEntity(2, out var receiver);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);

        AdvanceTicks(owner, 118);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Outbound));
        Assert.That(truck.CargoCount, Is.EqualTo(20));
        Assert.That(receiver.OutputCount, Is.Zero);

        AdvanceTicks(owner, 27);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Returning));
        Assert.That(truck.CargoCount, Is.Zero);
        Assert.That(receiver.OutputCount, Is.EqualTo(20));

        AdvanceTicks(owner, 100);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Loading));
        Assert.That(truck.CargoCount, Is.GreaterThan(0).And.LessThan(20));

        AdvanceTicks(owner, 200);
        Assert.That(receiver.OutputCount, Is.EqualTo(40));
        Assert.That(sender.OutputCount, Is.EqualTo(20));
        Assert.That(truck.CargoCount, Is.Zero);
        Assert.That(truck.CargoCount + sender.OutputCount + receiver.OutputCount, Is.EqualTo(60));
    }

    [Test]
    public void FullReceiverBackpressureHoldsCargoAndResumesAtTheConfiguredRate()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out var receiverFloor, 20, 100);
        receiverFloor.TryGetEntity(2, out var receiver);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);

        AdvanceTicks(owner, 125);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Unloading));
        Assert.That(truck.CargoCount, Is.EqualTo(20), "A blocked truck must keep its cargo intact.");
        Assert.That(receiver.OutputCount, Is.EqualTo(100));

        Assert.That(owner.TryDrainTestEntity(30, 0, receiver.EntityId, out var drained, out var error), Is.True, error);
        Assert.That(drained, Is.EqualTo(100));

        AdvanceTicks(owner, 1);
        Assert.That(receiver.OutputCount, Is.EqualTo(1), "Resumption must not burst.");
        Assert.That(truck.CargoCount, Is.EqualTo(19));
        AdvanceTicks(owner, 19);
        Assert.That(truck.CargoCount, Is.Zero);
        Assert.That(receiver.OutputCount, Is.EqualTo(20));
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Returning));
    }

    [Test]
    public void RouteCreationRejectsTerminalsAlreadyAssignedToExplicitConnections()
    {
        var owner = new FactoryWorldState(1);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(30, 1, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(20, 0, out var senderFloor);
        owner.TryGetFloorState(30, 0, out var receiverFloor);
        senderFloor.SetEntities(new[] { new FactoryEntityRecord(1, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 10) });
        receiverFloor.SetEntities(new[] { new FactoryEntityRecord(1, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0) });

        var source = new FactoryEntityEndpoint(20, 0, 1);
        var destination = new FactoryEntityEndpoint(30, 0, 1);
        Assert.That(owner.TryAddConnection(source, destination, out var connectionError), Is.True, connectionError);
        Assert.That(
            owner.TryCreateTruckRoute(
                source,
                destination,
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var routeError),
            Is.False);
        Assert.That(routeError, Does.Contain("explicit connection"));
    }

    [Test]
    public void ExplicitConnectionsRejectTerminalsAlreadyAssignedToTruckRoutes()
    {
        var owner = CreateRoutedWorld(out _, out _, 10, 0);
        var route = owner.TruckRoutes[0];

        Assert.That(
            owner.TryAddConnection(route.Source, route.Destination, out var connectionError),
            Is.False);
        Assert.That(connectionError, Does.Contain("truck route"));

        owner.TryGetFloorState(20, 0, out var senderFloor);
        owner.TryGetFloorState(30, 0, out var receiverFloor);
        senderFloor.SetEntities(new List<FactoryEntityRecord>(senderFloor.Entities)
        {
            new(3, FactoryEntityDefinitions.TestMachineDefinitionId, new Vector2(0.5f, 2.5f), 0f, 0f, 0, 4)
        });
        receiverFloor.SetEntities(new List<FactoryEntityRecord>(receiverFloor.Entities)
        {
            new(3, FactoryEntityRecord.StorageDefinitionId, new Vector2(0.5f, 2.5f), 0f, 0f, 0)
        });

        Assert.That(
            owner.TryAddConnection(
                new FactoryEntityEndpoint(20, 0, 3),
                new FactoryEntityEndpoint(20, 0, 1),
                out var localSenderFeedError),
            Is.True,
            localSenderFeedError);
        Assert.That(
            owner.TryAddConnection(
                new FactoryEntityEndpoint(30, 0, 2),
                new FactoryEntityEndpoint(30, 0, 3),
                out var localReceiverDrainError),
            Is.True,
            localReceiverDrainError);
        Assert.That(owner.TryValidateConnections(out var validationError), Is.True, validationError);
    }

    [Test]
    public void RouteCreationValidatesTerminalKindsDistinctBuildingsAndDuplicates()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out var receiverFloor, 10, 0);
        var first = owner.TruckRoutes[0];

        Assert.That(
            owner.TryCreateTruckRoute(
                first.Source,
                first.Destination,
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var duplicateError),
            Is.False);
        Assert.That(duplicateError, Does.Contain("already"));

        receiverFloor.SetEntities(new List<FactoryEntityRecord>(receiverFloor.Entities)
        {
            new(3, FactoryEntityRecord.StorageDefinitionId, new Vector2(0.5f, 2.5f), 0f, 0f, 0)
        });
        Assert.That(
            owner.TryCreateTruckRoute(
                first.Source,
                new FactoryEntityEndpoint(30, 0, 3),
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var kindError),
            Is.False);
        Assert.That(kindError, Does.Contain("sending terminal"));

        senderFloor.SetEntities(new List<FactoryEntityRecord>(senderFloor.Entities)
        {
            new(4, FactoryEntityRecord.ReceivingTerminalDefinitionId, new Vector2(0.5f, 1.5f), 0f, 0f, 0)
        });
        Assert.That(
            owner.TryCreateTruckRoute(
                new FactoryEntityEndpoint(20, 0, 1),
                new FactoryEntityEndpoint(20, 0, 4),
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var sameBuildingError),
            Is.False);
        Assert.That(sameBuildingError, Does.Contain("distinct buildings"));

        Assert.That(
            owner.TryCreateTruckRoute(
                new FactoryEntityEndpoint(20, 0, 999),
                new FactoryEntityEndpoint(30, 0, 2),
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var missingError),
            Is.False);
        Assert.That(missingError, Does.Contain("existing"));
    }

    [Test]
    public void RemovingARouteEndpointBlocksTheTruckAndPreservesItsCargo()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out var receiverFloor, 10, 0);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);
        AdvanceTicks(owner, 5);
        Assert.That(truck.CargoCount, Is.EqualTo(5));
        receiverFloor.TryGetEntity(2, out var receiver);

        Assert.That(owner.TryRemoveTestEntity(20, 0, 1, out var error), Is.True, error);

        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Blocked));
        Assert.That(truck.CargoCount, Is.EqualTo(5), "Blocking must preserve cargo.");
        Assert.That(truck.BlockingReason, Is.Not.Empty);
        Assert.That(owner.TruckRouteCount, Is.EqualTo(1), "Blocked trucks must not be silently dropped.");
        Assert.That(receiver.OutputCount, Is.Zero);
        Assert.That(
            owner.TryRemoveTruckRoute(owner.TruckRoutes[0].Guid, out var deleteError),
            Is.False);
        Assert.That(deleteError, Does.Contain("cargo"));
        Assert.That(
            owner.TryAddConnection(owner.TruckRoutes[0].Source, owner.TruckRoutes[0].Destination, out _),
            Is.False,
            "A blocked route must keep reserving its remote roles.");
    }

    [Test]
    public void RestoreKeepsTruckIdentityStateAndProgressAcrossReload()
    {
        var owner = CreateRoutedWorld(out _, out _, 100, 0);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);
        AdvanceTicks(owner, 50);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Outbound));
        var savedRoute = owner.TruckRoutes[0].Clone();
        var savedTruck = truck.Clone();
        var remainingAtSave = savedTruck.RemainingTravelSeconds;

        var restored = new FactoryWorldState(1);
        restored.TryRegisterBuilding(50, 1, new Vector2Int(6, 4), out _);
        restored.TryRegisterBuilding(60, 1, new Vector2Int(6, 4), out _);
        restored.TryGetFloorState(50, 0, out var restoredSenderFloor);
        restored.TryGetFloorState(60, 0, out var restoredReceiverFloor);
        restoredSenderFloor.SetEntities(new[] { new FactoryEntityRecord(7, FactoryEntityRecord.SendingTerminalDefinitionId, Vector2.one, 0f, 0f, 0, 30) });
        restoredReceiverFloor.SetEntities(new[] { new FactoryEntityRecord(9, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0) });
        var restoredSource = new FactoryEntityEndpoint(
            50, 0, 7, savedRoute.Source.BuildingGuid, savedRoute.Source.FloorGuid, savedRoute.Source.EntityGuid);
        var restoredDestination = new FactoryEntityEndpoint(
            60, 0, 9, savedRoute.Destination.BuildingGuid, savedRoute.Destination.FloorGuid, savedRoute.Destination.EntityGuid);
        var restoredRoute = new FactoryTruckRouteRecord(
            savedRoute.Guid,
            savedRoute.TruckGuid,
            restoredSource,
            restoredDestination,
            savedRoute.ItemId,
            savedRoute.CargoCapacity,
            savedRoute.TransferRateItemsPerSecond,
            savedRoute.OutboundTravelSeconds,
            savedRoute.ReturnTravelSeconds,
            savedRoute.PartialLoadDepartureWindowSeconds);

        Assert.That(
            restored.TryRestoreTruckRoutes(new[] { restoredRoute }, new[] { savedTruck }, out var error),
            Is.True,
            error);
        Assert.That(restored.TryGetTruck(savedRoute.TruckGuid, out var restoredTruck), Is.True);
        Assert.That(restoredTruck.State, Is.EqualTo(FactoryTruckState.Outbound));
        Assert.That(restoredTruck.CargoCount, Is.EqualTo(20));
        Assert.That(restoredTruck.RemainingTravelSeconds, Is.EqualTo(remainingAtSave).Within(0.2f));

        restored.AdvanceProduction(remainingAtSave + 0.5f);
        Assert.That(restoredTruck.State, Is.EqualTo(FactoryTruckState.Unloading));
        AdvanceTicks(restored, 20);
        Assert.That(restoredReceiverFloor.Entities[0].OutputCount, Is.EqualTo(20));
        Assert.That(restoredTruck.State, Is.EqualTo(FactoryTruckState.Returning));
    }

    [Test]
    public void RestoreDistinguishesDeliberatelyBlockedRoutesFromMalformedData()
    {
        var owner = CreateRoutedWorld(out var senderFloor, out _, 10, 0);
        Assert.That(owner.TryGetTruck(owner.TruckRoutes[0].TruckGuid, out var truck), Is.True);
        AdvanceTicks(owner, 5);
        Assert.That(owner.TryRemoveTestEntity(20, 0, 1, out _), Is.True);
        Assert.That(truck.State, Is.EqualTo(FactoryTruckState.Blocked));
        var savedRoute = owner.TruckRoutes[0].Clone();
        var savedTruck = truck.Clone();

        var restored = new FactoryWorldState(1);
        restored.TryRegisterBuilding(50, 1, new Vector2Int(6, 4), out _);
        restored.TryRegisterBuilding(60, 1, new Vector2Int(6, 4), out _);
        restored.TryGetFloorState(60, 0, out var restoredReceiverFloor);
        restoredReceiverFloor.SetEntities(new[] { new FactoryEntityRecord(9, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0) });
        var missingSource = new FactoryEntityEndpoint(
            savedRoute.Source.BuildingGuid,
            savedRoute.Source.FloorGuid,
            savedRoute.Source.EntityGuid,
            savedRoute.Source.FloorIndex);
        var restoredDestination = new FactoryEntityEndpoint(
            60, 0, 9, savedRoute.Destination.BuildingGuid, savedRoute.Destination.FloorGuid, savedRoute.Destination.EntityGuid);
        var restoredRoute = new FactoryTruckRouteRecord(
            savedRoute.Guid,
            savedRoute.TruckGuid,
            missingSource,
            restoredDestination,
            savedRoute.ItemId,
            savedRoute.CargoCapacity,
            savedRoute.TransferRateItemsPerSecond,
            savedRoute.OutboundTravelSeconds,
            savedRoute.ReturnTravelSeconds,
            savedRoute.PartialLoadDepartureWindowSeconds);

        Assert.That(
            restored.TryRestoreTruckRoutes(new[] { restoredRoute }, new[] { savedTruck }, out var blockedError),
            Is.True,
            blockedError);
        Assert.That(restored.TryGetTruck(savedRoute.TruckGuid, out var blockedTruck), Is.True);
        Assert.That(blockedTruck.State, Is.EqualTo(FactoryTruckState.Blocked));
        Assert.That(blockedTruck.CargoCount, Is.EqualTo(5));
        Assert.That(blockedTruck.BlockingReason, Is.Not.Empty);

        var awakeWorld = new FactoryWorldState(1);
        awakeWorld.TryRegisterBuilding(50, 1, new Vector2Int(6, 4), out _);
        awakeWorld.TryRegisterBuilding(60, 1, new Vector2Int(6, 4), out _);
        awakeWorld.TryGetFloorState(60, 0, out var awakeReceiverFloor);
        awakeReceiverFloor.SetEntities(new[] { new FactoryEntityRecord(9, FactoryEntityRecord.ReceivingTerminalDefinitionId, Vector2.one, 0f, 0f, 0) });
        var awakeTruck = new FactoryTruckRecord(
            savedTruck.Guid,
            savedTruck.RouteGuid,
            FactoryTruckState.Loading,
            string.Empty,
            0,
            0f,
            0f,
            string.Empty);
        Assert.That(
            awakeWorld.TryRestoreTruckRoutes(new[] { restoredRoute }, new[] { awakeTruck }, out var malformedError),
            Is.False,
            "A non-blocked truck with a missing endpoint is malformed, not deliberately blocked.");
        Assert.That(malformedError, Does.Contain("endpoint"));
    }

    [Test]
    public void SnapshotValidationAcceptsRoutesAndRejectsCorruptTruckData()
    {
        var snapshot = BuildValidSnapshot(out var senderEntityGuid, out var receiverEntityGuid, out var connectionRecord);

        Assert.That(FactoryWorldValidation.TryValidate(snapshot, out var validError), Is.True, validError);

        var overloaded = snapshot.Clone();
        overloaded.Trucks[0] = new FactoryWorldTruckRecord(
            overloaded.Trucks[0].Guid,
            overloaded.Trucks[0].RouteGuid,
            FactoryTruckState.Outbound,
            FactoryEntityDefinitions.TestProductId,
            21,
            5f,
            0f,
            string.Empty);
        Assert.That(FactoryWorldValidation.TryValidate(overloaded, out var capacityError), Is.False);
        Assert.That(capacityError, Does.Contain("capacity"));

        var blockedWithoutReason = snapshot.Clone();
        blockedWithoutReason.Trucks[0] = new FactoryWorldTruckRecord(
            blockedWithoutReason.Trucks[0].Guid,
            blockedWithoutReason.Trucks[0].RouteGuid,
            FactoryTruckState.Blocked,
            string.Empty,
            0,
            0f,
            0f,
            string.Empty);
        Assert.That(FactoryWorldValidation.TryValidate(blockedWithoutReason, out var reasonError), Is.False);
        Assert.That(reasonError, Does.Contain("reason"));

        var blockedWithReason = snapshot.Clone();
        blockedWithReason.Routes[0] = new FactoryWorldTruckRouteRecord(
            blockedWithReason.Routes[0].Guid,
            blockedWithReason.Routes[0].TruckGuid,
            MissingEndpoint(senderEntityGuid, blockedWithReason.Routes[0].Source),
            blockedWithReason.Routes[0].Destination,
            FactoryEntityDefinitions.TestProductId,
            20,
            10f,
            10f,
            10f,
            2f);
        blockedWithReason.Trucks[0] = new FactoryWorldTruckRecord(
            blockedWithReason.Trucks[0].Guid,
            blockedWithReason.Trucks[0].RouteGuid,
            FactoryTruckState.Blocked,
            FactoryEntityDefinitions.TestProductId,
            4,
            0f,
            0f,
            "Route endpoint entity was removed.");
        Assert.That(FactoryWorldValidation.TryValidate(blockedWithReason, out var blockedError), Is.True, blockedError);

        var returningWithCargo = snapshot.Clone();
        returningWithCargo.Trucks[0] = new FactoryWorldTruckRecord(
            returningWithCargo.Trucks[0].Guid,
            returningWithCargo.Trucks[0].RouteGuid,
            FactoryTruckState.Returning,
            FactoryEntityDefinitions.TestProductId,
            3,
            4f,
            0f,
            string.Empty);
        Assert.That(FactoryWorldValidation.TryValidate(returningWithCargo, out var returningError), Is.False);
        Assert.That(returningError, Does.Contain("cargo"));

        var localFeedStillAllowed = snapshot.Clone();
        localFeedStillAllowed.Connections.Add(connectionRecord.Clone());
        Assert.That(FactoryWorldValidation.TryValidate(localFeedStillAllowed, out var localFeedError), Is.True, localFeedError);

        var route = snapshot.Routes[0];
        var conflictingConnection = snapshot.Clone();
        conflictingConnection.Connections.Add(new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            route.Source,
            route.Destination));
        Assert.That(FactoryWorldValidation.TryValidate(conflictingConnection, out var conflictError), Is.False);
        Assert.That(conflictError, Does.Contain("explicit connection"));

        var duplicateAssignment = snapshot.Clone();
        var firstRoute = duplicateAssignment.Routes[0];
        var secondRouteGuid = Guid.NewGuid();
        var secondTruckGuid = Guid.NewGuid();
        duplicateAssignment.Routes.Add(new FactoryWorldTruckRouteRecord(
            secondRouteGuid,
            secondTruckGuid,
            firstRoute.Source,
            firstRoute.Destination,
            FactoryEntityDefinitions.TestProductId,
            20,
            10f,
            10f,
            10f,
            2f));
        duplicateAssignment.Trucks.Add(new FactoryWorldTruckRecord(
            secondTruckGuid,
            secondRouteGuid,
            FactoryTruckState.Loading,
            string.Empty,
            0,
            0f,
            0f,
            string.Empty));
        Assert.That(FactoryWorldValidation.TryValidate(duplicateAssignment, out var duplicateError), Is.False);
        Assert.That(duplicateError, Does.Contain("already"));

        var missingTruck = snapshot.Clone();
        missingTruck.Trucks.Clear();
        Assert.That(FactoryWorldValidation.TryValidate(missingTruck, out var truckError), Is.False);
        Assert.That(truckError, Does.Contain("truck"));
    }

    [Test]
    public void SqliteStoreRoundTripsTruckRoutesCargoAndConveyorState()
    {
        var snapshot = BuildValidSnapshot(out _, out _, out _);
        var conveyor = new FactoryWorldEntityRecord(
            Guid.NewGuid(),
            snapshot.Floors[0].Guid,
            "conveyor-east",
            new Vector2(3.5f, 1.5f),
            0f,
            Vector2.one,
            FactoryConveyorQueue.Encode(new[] { 1f, 0.75f, 0.5f, 0.25f }),
            4,
            false,
            0f,
            0f,
            0,
            0,
            0);
        snapshot.Floors[0].SetEntities(new List<FactoryWorldEntityRecord>(snapshot.Floors[0].Entities) { conveyor });
        var route = snapshot.Routes[0];
        snapshot.Trucks[0] = new FactoryWorldTruckRecord(
            snapshot.Trucks[0].Guid,
            route.Guid,
            FactoryTruckState.Outbound,
            FactoryEntityDefinitions.TestProductId,
            13,
            4.5f,
            1.25f,
            string.Empty);

        var store = new FactoryWorldSqliteStore(databasePath);
        store.Save(snapshot);
        var restored = store.Load();

        Assert.That(restored.Routes, Has.Count.EqualTo(1));
        Assert.That(restored.Routes[0].Guid, Is.EqualTo(route.Guid));
        Assert.That(restored.Routes[0].TruckGuid, Is.EqualTo(route.TruckGuid));
        Assert.That(restored.Routes[0].Source.EntityGuid, Is.EqualTo(route.Source.EntityGuid));
        Assert.That(restored.Routes[0].Destination.EntityGuid, Is.EqualTo(route.Destination.EntityGuid));
        Assert.That(restored.Routes[0].CargoCapacity, Is.EqualTo(20));
        Assert.That(restored.Routes[0].TransferRateItemsPerSecond, Is.EqualTo(10f));
        Assert.That(restored.Routes[0].OutboundTravelSeconds, Is.EqualTo(10f));
        Assert.That(restored.Routes[0].ReturnTravelSeconds, Is.EqualTo(10f));
        Assert.That(restored.Routes[0].PartialLoadDepartureWindowSeconds, Is.EqualTo(2f));
        Assert.That(restored.Trucks, Has.Count.EqualTo(1));
        Assert.That(restored.Trucks[0].Guid, Is.EqualTo(snapshot.Trucks[0].Guid));
        Assert.That(restored.Trucks[0].State, Is.EqualTo(FactoryTruckState.Outbound));
        Assert.That(restored.Trucks[0].CargoItemId, Is.EqualTo(FactoryEntityDefinitions.TestProductId));
        Assert.That(restored.Trucks[0].CargoCount, Is.EqualTo(13));
        Assert.That(restored.Trucks[0].RemainingTravelSeconds, Is.EqualTo(4.5f).Within(0.0001f));
        Assert.That(restored.Trucks[0].LoadingWindowProgress, Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(restored.Trucks[0].BlockingReason, Is.Empty);
        FactoryWorldEntityRecord restoredConveyor = null!;
        foreach (var floor in restored.Floors)
        {
            foreach (var entity in floor.Entities)
            {
                if (entity.DefinitionId == "conveyor-east")
                {
                    restoredConveyor = entity;
                }
            }
        }

        Assert.That(restoredConveyor, Is.Not.Null);
        Assert.That(
            FactoryConveyorQueue.Decode(restoredConveyor.State),
            Is.EqualTo(new[] { 1f, 0.75f, 0.5f, 0.25f }));
    }

    [Test]
    public void Schema8DatabaseUpgradesTransactionallyAndPreservesWorldData()
    {
        CreateSchema8Database(databasePath);

        var store = new FactoryWorldSqliteStore(databasePath);
        var restored = store.Load();

        var buildingGuid = FactoryGuidMigration.ForBuilding(20);
        var floorGuid = FactoryGuidMigration.ForFloor(20, 0);
        var machineGuid = FactoryGuidMigration.ForEntity(20, 0, 1);
        var beltGuid = FactoryGuidMigration.ForEntity(20, 0, 2);
        var storageGuid = FactoryGuidMigration.ForEntity(20, 1, 3);
        Assert.That(restored.Buildings[0].Guid, Is.EqualTo(buildingGuid));
        Assert.That(restored.Buildings[0].LegacyBuildingId, Is.EqualTo(20u));
        Assert.That(restored.Buildings[0].StoryCount, Is.EqualTo(2));
        FactoryWorldFloorRecord senderFloorRecord = null!;
        foreach (var floor in restored.Floors)
        {
            if (floor.Label == "Sender floor")
            {
                senderFloorRecord = floor;
            }
        }

        Assert.That(senderFloorRecord, Is.Not.Null);
        Assert.That(senderFloorRecord.Guid, Is.EqualTo(floorGuid));
        Assert.That(senderFloorRecord.AccumulatedProduction, Is.EqualTo(1.5f).Within(0.0001f));
        Assert.That(senderFloorRecord.Entities[0].Guid, Is.EqualTo(machineGuid));
        Assert.That(senderFloorRecord.Entities[0].OutputCount, Is.EqualTo(7));
        Assert.That(senderFloorRecord.Entities[1].Guid, Is.EqualTo(beltGuid));
        Assert.That(
            FactoryConveyorQueue.Decode(senderFloorRecord.Entities[1].State),
            Is.EqualTo(new[] { 1f, 0.5f }));
        Assert.That(restored.Connections, Has.Count.EqualTo(1));
        Assert.That(restored.Connections[0].Source.EntityGuid, Is.EqualTo(machineGuid));
        Assert.That(restored.Connections[0].Destination.EntityGuid, Is.EqualTo(storageGuid));

        using (var database = new SQLiteConnection(databasePath))
        {
            var tables = database.Query<SchemaRow>("SELECT name AS Name FROM sqlite_master WHERE type = 'table'");
            var names = new List<string>();
            foreach (var row in tables) names.Add(row.Name);
            Assert.That(names, Does.Contain("factory_truck_routes"));
            Assert.That(names, Does.Contain("factory_trucks"));
            var version = database.Query<MetadataRow>("SELECT * FROM factory_metadata WHERE Name = 'schema_version'");
            Assert.That(version[0].Value, Is.EqualTo("9"));
        }

        var reloaded = store.Load();
        Assert.That(reloaded.Buildings[0].Guid, Is.EqualTo(buildingGuid));
        Assert.That(reloaded.Routes, Is.Empty);
    }

    private static FactoryWorldState CreateRoutedWorld(
        out OutsideTestFloorRecord senderFloor,
        out OutsideTestFloorRecord receiverFloor,
        int senderStock,
        int receiverStock)
    {
        var owner = new FactoryWorldState(1);
        owner.TryRegisterBuilding(20, 1, new Vector2Int(6, 4), out _);
        owner.TryRegisterBuilding(30, 1, new Vector2Int(6, 4), out _);
        owner.TryGetFloorState(20, 0, out senderFloor);
        owner.TryGetFloorState(30, 0, out receiverFloor);
        senderFloor.SetEntities(new[] { new FactoryEntityRecord(1, FactoryEntityRecord.SendingTerminalDefinitionId, new Vector2(0.5f, 0.5f), 0f, 0f, 0, senderStock) });
        receiverFloor.SetEntities(new[] { new FactoryEntityRecord(2, FactoryEntityRecord.ReceivingTerminalDefinitionId, new Vector2(0.5f, 0.5f), 0f, 0f, 0, receiverStock) });
        Assert.That(
            owner.TryCreateTruckRoute(
                new FactoryEntityEndpoint(20, 0, 1),
                new FactoryEntityEndpoint(30, 0, 2),
                out FactoryTruckRouteRecord _,
                out FactoryTruckRecord _,
                out var error),
            Is.True,
            error);
        return owner;
    }

    private static void AdvanceTicks(FactoryWorldState owner, int ticks)
    {
        for (var index = 0; index < ticks; index++)
        {
            owner.AdvanceProduction(Tick);
        }
    }

    private static FactoryWorldEndpoint MissingEndpoint(Guid entityGuid, FactoryWorldEndpoint template)
    {
        return new FactoryWorldEndpoint(
            Guid.NewGuid(),
            Guid.NewGuid(),
            entityGuid,
            template.FloorIndex);
    }

    private static FactoryWorldSnapshot BuildValidSnapshot(
        out Guid senderEntityGuid,
        out Guid receiverEntityGuid,
        out FactoryWorldConnectionRecord unrelatedConnection)
    {
        var buildingAGuid = Guid.NewGuid();
        var buildingBGuid = Guid.NewGuid();
        var floorAGuid = Guid.NewGuid();
        var floorBGuid = Guid.NewGuid();
        senderEntityGuid = Guid.NewGuid();
        receiverEntityGuid = Guid.NewGuid();
        var machineEntityGuid = Guid.NewGuid();
        var storageEntityGuid = Guid.NewGuid();
        var routeGuid = Guid.NewGuid();
        var truckGuid = Guid.NewGuid();

        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingAGuid, "outside-test-building", Vector3Int.zero, new Vector2Int(8, 8), 1, false, 20));
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingBGuid, "outside-test-building", new Vector3Int(20, 0, 0), new Vector2Int(8, 8), 1, false, 30));
        var floorA = new FactoryWorldFloorRecord(
            floorAGuid, buildingAGuid, 0, "Sender floor", 1f, 0f, new Vector2(0.5f, 0.5f));
        floorA.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                machineEntityGuid, floorAGuid, FactoryEntityDefinitions.TestMachineDefinitionId,
                new Vector2(0.5f, 0.5f), 0f, Vector2.one, null, 1, false, 1f, 0f, 0, 10, 0),
            new FactoryWorldEntityRecord(
                senderEntityGuid, floorAGuid, FactoryEntityRecord.SendingTerminalDefinitionId,
                new Vector2(1.5f, 0.5f), 0f, Vector2.one, null, 2, false, 0f, 0f, 0, 10, 0)
        });
        var floorB = new FactoryWorldFloorRecord(
            floorBGuid, buildingBGuid, 0, "Receiver floor", 1f, 0f, new Vector2(0.5f, 0.5f));
        floorB.SetEntities(new[]
        {
            new FactoryWorldEntityRecord(
                receiverEntityGuid, floorBGuid, FactoryEntityRecord.ReceivingTerminalDefinitionId,
                new Vector2(0.5f, 0.5f), 0f, Vector2.one, null, 1, false, 0f, 0f, 0, 0, 0),
            new FactoryWorldEntityRecord(
                storageEntityGuid, floorBGuid, FactoryEntityRecord.StorageDefinitionId,
                new Vector2(1.5f, 0.5f), 0f, Vector2.one, null, 2, false, 0f, 0f, 0, 0, 0)
        });
        snapshot.Floors.Add(floorA);
        snapshot.Floors.Add(floorB);

        var source = new FactoryWorldEndpoint(buildingAGuid, floorAGuid, senderEntityGuid, 0);
        var destination = new FactoryWorldEndpoint(buildingBGuid, floorBGuid, receiverEntityGuid, 0);
        snapshot.Routes.Add(new FactoryWorldTruckRouteRecord(routeGuid, truckGuid, source, destination));
        snapshot.Trucks.Add(new FactoryWorldTruckRecord(
            truckGuid,
            routeGuid,
            FactoryTruckState.Loading,
            string.Empty,
            0,
            0f,
            0f,
            string.Empty));

        unrelatedConnection = new FactoryWorldConnectionRecord(
            Guid.NewGuid(),
            new FactoryWorldEndpoint(buildingAGuid, floorAGuid, machineEntityGuid, 0),
            new FactoryWorldEndpoint(buildingAGuid, floorAGuid, senderEntityGuid, 0));

        Assert.That(FactoryWorldValidation.TryValidate(snapshot, out var error), Is.True, error);
        Assert.That(
            FactoryWorldValidation.TryValidate(snapshot.Clone(), out _),
            Is.True);
        return snapshot;
    }

    private static void CreateSchema8Database(string path)
    {
        using var database = new SQLiteConnection(path);
        database.Execute("CREATE TABLE factory_metadata (Name TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL)");
        database.Execute("CREATE TABLE factory_buildings (Guid TEXT PRIMARY KEY NOT NULL, LegacyBuildingId INTEGER NOT NULL, DefinitionId TEXT NOT NULL, AnchorX INTEGER NOT NULL, AnchorY INTEGER NOT NULL, AnchorZ INTEGER NOT NULL, FootprintX INTEGER NOT NULL, FootprintY INTEGER NOT NULL, StoryCount INTEGER NOT NULL, InteriorOnly INTEGER NOT NULL)");
        database.Execute("CREATE TABLE factory_floors (Guid TEXT PRIMARY KEY NOT NULL, BuildingGuid TEXT NOT NULL, FloorIndex INTEGER NOT NULL, Label TEXT NOT NULL, ProductionRate REAL NOT NULL, AccumulatedProduction REAL NOT NULL, MarkerPositionX REAL NOT NULL, MarkerPositionY REAL NOT NULL)");
        database.Execute("CREATE TABLE factory_entities (Guid TEXT PRIMARY KEY NOT NULL, FloorGuid TEXT NOT NULL, LegacyEntityId INTEGER NOT NULL, DefinitionId TEXT NOT NULL, PositionX REAL NOT NULL, PositionY REAL NOT NULL, RotationZ REAL NOT NULL, FootprintX REAL NOT NULL, FootprintY REAL NOT NULL, State BLOB, CycleRate REAL NOT NULL, CycleProgress REAL NOT NULL, ProducedCount INTEGER NOT NULL, OutputCount INTEGER NOT NULL, InputCount INTEGER NOT NULL, NaiEntity INTEGER NOT NULL)");
        database.Execute("CREATE TABLE factory_connections (Guid TEXT PRIMARY KEY NOT NULL, SourceBuildingGuid TEXT NOT NULL, SourceFloorGuid TEXT NOT NULL, SourceEntityGuid TEXT NOT NULL, SourceFloorIndex INTEGER NOT NULL, DestinationBuildingGuid TEXT NOT NULL, DestinationFloorGuid TEXT NOT NULL, DestinationEntityGuid TEXT NOT NULL, DestinationFloorIndex INTEGER NOT NULL)");
        database.Execute("CREATE TABLE factory_doors (BuildingGuid TEXT NOT NULL, DoorIndex INTEGER NOT NULL, WallId TEXT NOT NULL, NormalizedOffset REAL NOT NULL, PRIMARY KEY (BuildingGuid, DoorIndex))");
        database.Execute("CREATE TABLE factory_migration_map (Kind TEXT NOT NULL, LegacyKey TEXT NOT NULL, Guid TEXT PRIMARY KEY NOT NULL)");
        database.Execute("CREATE UNIQUE INDEX factory_floor_order ON factory_floors (BuildingGuid, FloorIndex)");

        var buildingGuid = FactoryGuidMigration.ForBuilding(20);
        var floorGuid = FactoryGuidMigration.ForFloor(20, 0);
        var upperFloorGuid = FactoryGuidMigration.ForFloor(20, 1);
        var machineGuid = FactoryGuidMigration.ForEntity(20, 0, 1);
        var beltGuid = FactoryGuidMigration.ForEntity(20, 0, 2);
        var storageGuid = FactoryGuidMigration.ForEntity(20, 1, 3);
        database.Execute(
            "INSERT INTO factory_metadata (Name, Value) VALUES ('schema_version', '8')");
        database.Execute(
            "INSERT INTO factory_metadata (Name, Value) VALUES ('migration_complete', '1')");
        database.Execute(
            "INSERT INTO factory_buildings (Guid, LegacyBuildingId, DefinitionId, AnchorX, AnchorY, AnchorZ, FootprintX, FootprintY, StoryCount, InteriorOnly) "
            + $"VALUES ('{buildingGuid:D}', 20, 'outside-test-building', 0, 0, 0, 6, 4, 2, 0)");
        database.Execute(
            "INSERT INTO factory_floors (Guid, BuildingGuid, FloorIndex, Label, ProductionRate, AccumulatedProduction, MarkerPositionX, MarkerPositionY) "
            + $"VALUES ('{floorGuid:D}', '{buildingGuid:D}', 0, 'Sender floor', 1, 1.5, 0.5, 0.5)");
        database.Execute(
            "INSERT INTO factory_floors (Guid, BuildingGuid, FloorIndex, Label, ProductionRate, AccumulatedProduction, MarkerPositionX, MarkerPositionY) "
            + $"VALUES ('{upperFloorGuid:D}', '{buildingGuid:D}', 1, 'Upper floor', 1, 0, 0.5, 0.5)");
        database.Execute(
            "INSERT INTO factory_entities (Guid, FloorGuid, LegacyEntityId, DefinitionId, PositionX, PositionY, RotationZ, FootprintX, FootprintY, State, CycleRate, CycleProgress, ProducedCount, OutputCount, InputCount, NaiEntity) "
            + $"VALUES ('{machineGuid:D}', '{floorGuid:D}', 1, 'test-machine', 0.5, 0.5, 0, 1, 1, NULL, 1, 0.25, 30, 7, 0, 0)");
        database.Execute(
            "INSERT INTO factory_entities (Guid, FloorGuid, LegacyEntityId, DefinitionId, PositionX, PositionY, RotationZ, FootprintX, FootprintY, State, CycleRate, CycleProgress, ProducedCount, OutputCount, InputCount, NaiEntity) "
            + $"VALUES ('{beltGuid:D}', '{floorGuid:D}', 2, 'conveyor-east', 1.5, 0.5, 0, 1, 1, ?, 0, 0, 0, 0, 0, 0)",
            FactoryConveyorQueue.Encode(new[] { 1f, 0.5f }));
        database.Execute(
            "INSERT INTO factory_entities (Guid, FloorGuid, LegacyEntityId, DefinitionId, PositionX, PositionY, RotationZ, FootprintX, FootprintY, State, CycleRate, CycleProgress, ProducedCount, OutputCount, InputCount, NaiEntity) "
            + $"VALUES ('{storageGuid:D}', '{upperFloorGuid:D}', 3, 'test-storage', 2.5, 0.5, 0, 1, 1, NULL, 0, 0, 0, 0, 0, 0)");
        var connectionGuid = FactoryGuidMigration.ForConnection(
            new FactoryEntityEndpoint(20, 0, 1),
            new FactoryEntityEndpoint(20, 1, 3));
        database.Execute(
            "INSERT INTO factory_connections (Guid, SourceBuildingGuid, SourceFloorGuid, SourceEntityGuid, SourceFloorIndex, DestinationBuildingGuid, DestinationFloorGuid, DestinationEntityGuid, DestinationFloorIndex) "
            + $"VALUES ('{connectionGuid:D}', "
            + $"'{buildingGuid:D}', '{floorGuid:D}', '{machineGuid:D}', 0, '{buildingGuid:D}', '{upperFloorGuid:D}', '{storageGuid:D}', 1)");
        database.Execute(
            "INSERT INTO factory_doors (BuildingGuid, DoorIndex, WallId, NormalizedOffset) "
            + $"VALUES ('{buildingGuid:D}', 0, 'south', 0.5)");
        database.Execute(
            "INSERT INTO factory_migration_map (Kind, LegacyKey, Guid) VALUES ('building', '20', '" + $"{buildingGuid:D}')");
    }

    private sealed class SchemaRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MetadataRow
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
