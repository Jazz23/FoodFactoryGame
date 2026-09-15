// Verifies complementary elevator pairing and item conservation between adjacent factory floors.
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryElevatorTransferTests
{
    [Test]
    public void PairedElevatorsTransferBufferedItemsInBothDirections()
    {
        var position = new Vector2(2.5f, 1.5f);
        var lowerFloor = new OutsideTestFloorRecord(17, 0, "Lower", 0f, 0f, Vector2.zero);
        var upperFloor = new OutsideTestFloorRecord(17, 1, "Upper", 0f, 0f, Vector2.zero);
        var bottom = new FactoryEntityRecord(
            1,
            FactoryEntityDefinitions.ElevatorBottomDefinitionId,
            position,
            0f,
            0f,
            0);
        var top = new FactoryEntityRecord(
            2,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            position,
            0f,
            0f,
            0);
        bottom.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        lowerFloor.AddEntity(bottom);
        upperFloor.AddEntity(top);
        lowerFloor.TryGetEntity(1, out bottom);
        upperFloor.TryGetEntity(2, out top);

        var upwardTransfers = FactoryElevatorTransfer.TransferPairedElevators(
            new[] { lowerFloor, upperFloor });

        Assert.That(upwardTransfers, Is.EqualTo(1));
        Assert.That(bottom.InputCount, Is.Zero);
        Assert.That(top.OutputCount, Is.EqualTo(1));
        var upperStorage = new FactoryEntityRecord(
            3,
            FactoryEntityDefinitions.TestStorageDefinitionId,
            position + Vector2.right,
            0f,
            0f,
            0);
        Assert.That(FactoryItemTransfer.TryTransfer(top, upperStorage, 1), Is.EqualTo(1));
        Assert.That(upperStorage.OutputCount, Is.EqualTo(1));

        top.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        var downwardTransfers = FactoryElevatorTransfer.TransferPairedElevators(
            new[] { lowerFloor, upperFloor });

        Assert.That(downwardTransfers, Is.EqualTo(1));
        Assert.That(top.InputCount, Is.Zero);
        Assert.That(bottom.OutputCount, Is.EqualTo(1));
    }

    [Test]
    public void ElevatorDoesNotConnectAcrossDifferentCells()
    {
        var lowerFloor = new OutsideTestFloorRecord(18, 0, "Lower", 0f, 0f, Vector2.zero);
        var upperFloor = new OutsideTestFloorRecord(18, 1, "Upper", 0f, 0f, Vector2.zero);
        var bottom = new FactoryEntityRecord(
            1,
            FactoryEntityDefinitions.ElevatorBottomDefinitionId,
            new Vector2(1.5f, 1.5f),
            0f,
            0f,
            0);
        var top = new FactoryEntityRecord(
            2,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            new Vector2(2.5f, 1.5f),
            0f,
            0f,
            0);
        bottom.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        lowerFloor.AddEntity(bottom);
        upperFloor.AddEntity(top);
        lowerFloor.TryGetEntity(1, out bottom);
        upperFloor.TryGetEntity(2, out top);

        var transferred = FactoryElevatorTransfer.TransferPairedElevators(
            new[] { lowerFloor, upperFloor });

        Assert.That(transferred, Is.Zero);
        Assert.That(bottom.InputCount, Is.EqualTo(1));
        Assert.That(top.OutputCount, Is.Zero);
    }

    [Test]
    public void ElevatorDoesNotConnectAcrossNonAdjacentFloors()
    {
        var position = new Vector2(1.5f, 1.5f);
        var lowerFloor = new OutsideTestFloorRecord(19, 0, "Lower", 0f, 0f, Vector2.zero);
        var upperFloor = new OutsideTestFloorRecord(19, 2, "Upper", 0f, 0f, Vector2.zero);
        var bottom = new FactoryEntityRecord(
            1,
            FactoryEntityDefinitions.ElevatorBottomDefinitionId,
            position,
            0f,
            0f,
            0);
        var top = new FactoryEntityRecord(
            2,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            position,
            0f,
            0f,
            0);
        bottom.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 1);
        lowerFloor.AddEntity(bottom);
        upperFloor.AddEntity(top);
        lowerFloor.TryGetEntity(1, out bottom);
        upperFloor.TryGetEntity(2, out top);

        var transferred = FactoryElevatorTransfer.TransferPairedElevators(
            new[] { lowerFloor, upperFloor });

        Assert.That(transferred, Is.Zero);
        Assert.That(bottom.InputCount, Is.EqualTo(1));
        Assert.That(top.OutputCount, Is.Zero);
    }

    [Test]
    public void ElevatorPartsArePlaceableAndDoNotProduceItemsByThemselves()
    {
        var definition = FactoryEntityDefinitions.Get(FactoryEntityDefinitions.ElevatorTopDefinitionId);
        var elevator = new FactoryEntityRecord(
            1,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            new Vector2(0.5f, 0.5f),
            1f,
            0f,
            0);

        Assert.That(FactoryConveyor.IsPlaceable(FactoryEntityDefinitions.ElevatorTopDefinitionId), Is.True);
        Assert.That(FactoryConveyor.IsPlaceable(FactoryEntityDefinitions.ElevatorBottomDefinitionId), Is.True);
        Assert.That(definition.IsElevator, Is.True);
        Assert.That(definition.IsReceiver, Is.True);
        Assert.That(definition.IsSupplier, Is.True);
        elevator.Advance(1f);
        Assert.That(elevator.OutputCount, Is.Zero);
    }
}
