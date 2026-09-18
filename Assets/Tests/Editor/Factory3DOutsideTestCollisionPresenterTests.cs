// Verifies disposable OutsideTest traversal collision derivation and logical-state preservation.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class Factory3DOutsideTestCollisionPresenterTests
{
    private GameObject gridObject = null!;
    private GameObject presenterObject = null!;
    private SceneGrid grid = null!;
    private Factory3DOutsideTestCollisionPresenter presenter = null!;

    [SetUp]
    public void SetUp()
    {
        gridObject = new GameObject("Outside Collision Test Grid");
        grid = gridObject.AddComponent<SceneGrid>();
        presenterObject = new GameObject("Outside Collision Test Presenter");
        presenter = presenterObject.AddComponent<Factory3DOutsideTestCollisionPresenter>();
    }

    [TearDown]
    public void TearDown()
    {
        if (presenterObject is not null && presenterObject)
        {
            Object.DestroyImmediate(presenterObject);
        }

        if (gridObject is not null && gridObject)
        {
            Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void ReconcileCreatesDisposableFloorWallsEquipmentAndDoorOpening()
    {
        var record = CreateExteriorRecord();
        var floor = OutsideTestFloorRecord.CreateDefault(record.BuildingInstanceId, 0);
        floor.SetEntities(new List<FactoryEntityRecord>
        {
            new(21u, "test-machine", new Vector2(1.5f, 1.5f), 0f, 0f, 0)
        });
        var source = new Factory3DOutsideTestProxyBuildingSource(
            record,
            false,
            new Vector2Int(2, 2));

        presenter.Reconcile(
            grid,
            new[] { source },
            new[] { floor },
            3f);
        Physics.SyncTransforms();

        Assert.That(presenter.CollisionRoot.gameObject.activeSelf, Is.True);
        Assert.That(presenter.ColliderCount, Is.GreaterThan(5));
        var adapter = grid.CreateSpatialAdapter();
        var doorSpan = FindDoorSpan(record);
        var doorPosition = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                record.BuildingInstanceId,
                0,
                Vector2.Lerp(
                    doorSpan.LogicalStart,
                    doorSpan.LogicalEnd,
                    record.Doors[0].NormalizedOffset)),
            0f);
        var blockers = Physics.OverlapSphere(
            doorPosition + Vector3.up,
            0.15f);
        Assert.That(blockers, Is.Empty);

        presenter.Clear();
        Assert.That(presenter.CollisionRoot.gameObject.activeSelf, Is.False);
        Assert.That(presenter.ColliderCount, Is.Zero);
    }

    [Test]
    public void ReconcileLeavesLogicalTopologyAndFloorRecordsUntouched()
    {
        var record = CreateExteriorRecord();
        var floor = OutsideTestFloorRecord.CreateDefault(record.BuildingInstanceId, 0);
        var entity = new FactoryEntityRecord(
            31u,
            "test-machine",
            new Vector2(1.5f, 1.5f),
            0f,
            0f,
            4,
            2);
        floor.SetEntities(new[] { entity });
        var originalRecord = record.Clone();
        var originalPosition = floor.Entities[0].LogicalPosition;
        var source = new Factory3DOutsideTestProxyBuildingSource(
            record,
            false,
            new Vector2Int(2, 2));

        presenter.Reconcile(grid, new[] { source }, new[] { floor });

        Assert.That(record.HasSameTopology(originalRecord), Is.True);
        Assert.That(floor.Entities[0].LogicalPosition, Is.EqualTo(originalPosition));
        Assert.That(floor.Entities[0].EntityId, Is.EqualTo(entity.EntityId));
    }

    [Test]
    public void ProductionTraversalTeleportRetainsLogicalFloorContext()
    {
        var playerObject = new GameObject("Production Traversal Player");
        try
        {
            var traversal = playerObject.AddComponent<Factory3DTraversalController>();
            var adapter = grid.CreateSpatialAdapter();
            var logicalPosition = new Vector2(2.25f, 1.75f);
            traversal.ConfigureSpatialContext(adapter, 19u, 1, 3f);
            traversal.Set3DOwnership(true);
            traversal.Teleport(
                adapter.LogicalToWorld3D(
                    new FactoryLogicalLocation(19u, 1, logicalPosition),
                    3f),
                1);

            Assert.That(traversal.FloorIndex, Is.EqualTo(1));
            Assert.That(traversal.BuildingInstanceId, Is.EqualTo(19u));
            Assert.That(traversal.TryGetLogicalFootAnchor(out var location), Is.True);
            Assert.That(location.FloorPosition, Is.EqualTo(logicalPosition));
            Assert.That(traversal.FootAnchor.y, Is.EqualTo(3f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(playerObject);
        }
    }

    private static BuildingRecord CreateExteriorRecord()
    {
        var record = new BuildingRecord(
            19u,
            Vector3Int.zero,
            new Vector2Int(4, 4),
            1);
        Assert.That(
            TestBuildingCreator.TryGetDefaultEntrance(
                record.AnchorCell,
                record.FootprintSize,
                out var door,
                out var error),
            Is.True,
            error);
        record.SetDoors(new[] { door });
        return record;
    }

    private static TestBuildingCreator.ExteriorWallSpan FindDoorSpan(BuildingRecord record)
    {
        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        TestBuildingCreator.GetExteriorWallSpans(
            record.AnchorCell,
            record.FootprintSize,
            spans);
        foreach (var span in spans)
        {
            if (span.StableId == record.Doors[0].WallId)
            {
                return span;
            }
        }

        Assert.Fail("The authored door did not resolve to an exterior wall span.");
        return default;
    }
}
