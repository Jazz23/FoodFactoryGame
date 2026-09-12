// Verifies usable interior bounds and deterministic position-only reconciliation.
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryInteriorReconciliationTests
{
    [Test]
    public void ExistingValidPositionsAndEntityStateRemainUnchanged()
    {
        var floor = new OutsideTestFloorRecord(1, 0, "Floor", 1f, 4f, Vector2.one);
        var entity = new FactoryEntityRecord(
            7,
            FactoryEntityDefinitions.ProcessorDefinitionId,
            new Vector2(1.25f, 2.25f),
            1f,
            0.4f,
            9,
            8,
            3);
        floor.AddEntity(entity);

        Assert.That(floor.ReconcileEntityPositions(new Vector2Int(3, 3)), Is.Zero);
        Assert.That(floor.Entities[0].LogicalPosition, Is.EqualTo(new Vector2(1.25f, 2.25f)));
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(9));
        Assert.That(floor.Entities[0].InputCount, Is.EqualTo(3));
        Assert.That(floor.Entities[0].OutputCount, Is.EqualTo(8));
    }

    [Test]
    public void ConflictingAndOutOfBoundsEntitiesMoveByStableIdToDistinctCells()
    {
        var floor = new OutsideTestFloorRecord(1, 0, "Floor", 1f, 0f, Vector2.one);
        floor.AddEntity(new FactoryEntityRecord(3, "test-machine", new Vector2(0.5f, 0.5f), 1f, 0f, 0));
        floor.AddEntity(new FactoryEntityRecord(1, "test-machine", new Vector2(0.5f, 0.5f), 1f, 0f, 0));
        floor.AddEntity(new FactoryEntityRecord(2, "test-machine", new Vector2(9.5f, 9.5f), 1f, 0f, 0));

        Assert.That(floor.ReconcileEntityPositions(new Vector2Int(2, 2)), Is.EqualTo(2));
        Assert.That(floor.Entities[0].LogicalPosition, Is.EqualTo(new Vector2(0.5f, 1.5f)));
        Assert.That(floor.Entities[1].LogicalPosition, Is.EqualTo(new Vector2(0.5f, 0.5f)));
        Assert.That(floor.Entities[2].LogicalPosition, Is.EqualTo(new Vector2(1.5f, 1.5f)));
    }

    [Test]
    public void NoUsableInteriorRetainsRecoveryRecordsAndDoesNotAdvanceThem()
    {
        var floor = new OutsideTestFloorRecord(1, 0, "Floor", 1f, 0f, Vector2.one);
        floor.AddEntity(new FactoryEntityRecord(1, "test-machine", new Vector2(0.5f, 0.5f), 1f, 0f, 0));

        floor.ReconcileEntityPositions(Vector2Int.zero);
        floor.Advance(10f, Vector2Int.zero);

        Assert.That(floor.GetRecoveryEntityCount(Vector2Int.zero), Is.EqualTo(1));
        Assert.That(floor.Entities[0].ProducedCount, Is.Zero);
    }
}
