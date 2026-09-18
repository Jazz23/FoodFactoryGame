// Verifies bounded interior proxy reconciliation and adapter-based floor coordinates.
using System;
using NUnit.Framework;
using UnityEngine;

public sealed class Factory3DInteriorPresentationTests
{
    private GameObject sceneRoot = null!;
    private GameObject presenterObject = null!;
    private SceneGrid grid = null!;
    private IndoorGrid indoorGrid = null!;
    private InsideFactoryController controller = null!;
    private Factory3DInteriorPresenter presenter = null!;

    [SetUp]
    public void SetUp()
    {
        sceneRoot = new GameObject("Interior Presentation Fixture");
        grid = sceneRoot.AddComponent<SceneGrid>();
        indoorGrid = sceneRoot.AddComponent<IndoorGrid>();
        var portalObject = new GameObject("Interior Exit Portal");
        portalObject.transform.SetParent(sceneRoot.transform, false);
        var portal = portalObject.AddComponent<ScenePortal>();
        controller = sceneRoot.AddComponent<InsideFactoryController>();
        SetPrivateField(controller, "exitPortal", portal);
        controller.Configure(
            new Vector2Int(8, 4),
            new Vector2(0.5f, 0.5f),
            Array.Empty<Vector2>(),
            Array.Empty<Vector2>(),
            Array.Empty<GridEdgeDirection>(),
            Factory3DRouteFixture.BuildingId,
            2,
            0);

        presenterObject = new GameObject("Interior Presentation Presenter");
        presenter = presenterObject.AddComponent<Factory3DInteriorPresenter>();
    }

    [TearDown]
    public void TearDown()
    {
        if (presenterObject is not null && presenterObject)
        {
            UnityEngine.Object.DestroyImmediate(presenterObject);
        }

        if (sceneRoot is not null && sceneRoot)
        {
            UnityEngine.Object.DestroyImmediate(sceneRoot);
        }
    }

    [Test]
    public void AdapterMapsInteriorLogicalPositionToFloorElevation()
    {
        var logical = new FactoryLogicalLocation(
            Factory3DRouteFixture.BuildingId,
            1,
            new Vector2(2.25f, 1.75f));
        var world = grid.CreateSpatialAdapter().LogicalToWorld3D(logical, 3f);

        Assert.That(world.x, Is.EqualTo(grid.LogicalToWorld(logical.FloorPosition).x).Within(0.0001f));
        Assert.That(world.z, Is.EqualTo(grid.LogicalToWorld(logical.FloorPosition).y).Within(0.0001f));
        Assert.That(world.y, Is.EqualTo(3f).Within(0.0001f));
    }

    [Test]
    public void PresenterReusesStableEntityProxiesAndRemovesStaleFloorNodes()
    {
        var state = Factory3DRouteFixture.CreateState();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, grid, 3f);
        var first = presenter.ReconcileSnapshot(
            snapshot,
            controller,
            grid,
            indoorGrid.Size);
        var entity = presenter.ProxyRoot.Find(
            Factory3DInteriorProxyAssembler.GetEntityId(
                Factory3DRouteFixture.BuildingId,
                0,
                Factory3DRouteFixture.SourceEntityId));
        var entityName = entity!.name;

        var second = presenter.ReconcileSnapshot(
            Factory3DRouteSnapshotBuilder.Build(state, grid, 3f),
            controller,
            grid,
            indoorGrid.Size);

        Assert.That(first.EntityCount, Is.EqualTo(3));
        Assert.That(first.ProxyCount, Is.EqualTo(8));
        Assert.That(second.RemovedNodeCount, Is.Zero);
        Assert.That(presenter.ProxyRoot.Find(entityName), Is.SameAs(entity));
        Assert.That(presenter.ProxyCount, Is.EqualTo(8));

        controller.Configure(
            indoorGrid.Size,
            new Vector2(0.5f, 0.5f),
            Array.Empty<Vector2>(),
            Array.Empty<Vector2>(),
            Array.Empty<GridEdgeDirection>(),
            Factory3DRouteFixture.BuildingId,
            2,
            1);
        presenter.ReconcileSnapshot(
            Factory3DRouteSnapshotBuilder.Build(state, grid, 3f),
            controller,
            grid,
            indoorGrid.Size);

        Assert.That(presenter.ProxyRoot.Find(entityName), Is.Null);
        Assert.That(
            presenter.ProxyRoot.Find(
                Factory3DInteriorProxyAssembler.GetEntityId(
                    Factory3DRouteFixture.BuildingId,
                    1,
                    Factory3DRouteFixture.StorageEntityId)),
            Is.Not.Null);
        Assert.That(presenter.LastBuild.FloorIndex, Is.EqualTo(1));
    }

    [Test]
    public void InvalidSnapshotClearsDisposableRootWithoutChangingAuthority()
    {
        var state = Factory3DRouteFixture.CreateState();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, grid, 3f);
        presenter.ReconcileSnapshot(snapshot, controller, grid, indoorGrid.Size);
        var before = state.CaptureState();

        presenter.ReconcileSnapshot(null!, controller, grid, indoorGrid.Size);

        Assert.That(presenter.ProxyCount, Is.Zero);
        Assert.That(state.CaptureState().Floors.Count, Is.EqualTo(before.Floors.Count));
        Assert.That(
            state.CaptureState().Floors[0].Entities.Count,
            Is.EqualTo(before.Floors[0].Entities.Count));
    }

    [Test]
    public void ChangedEntityPresentationMetadataForcesReconciliation()
    {
        var state = Factory3DRouteFixture.CreateState();
        var initialSnapshot = Factory3DRouteSnapshotBuilder.Build(state, grid, 3f);
        presenter.ReconcileSnapshot(initialSnapshot, controller, grid, indoorGrid.Size);
        var initialEntity = presenter.ProxyRoot.Find(
            Factory3DInteriorProxyAssembler.GetEntityId(
                Factory3DRouteFixture.BuildingId,
                0,
                Factory3DRouteFixture.SourceEntityId));
        Assert.That(initialEntity, Is.Not.Null);
        var initialPosition = initialEntity!.transform.position;

        var changedState = Factory3DRouteFixture.CreateState();
        Assert.That(
            changedState.TryRelocateEntity(
                Factory3DRouteFixture.BuildingId,
                0,
                Factory3DRouteFixture.SourceEntityId,
                new Vector2(1.5f, 1.5f),
                out var error),
            Is.True,
            error);
        var changedSnapshot = Factory3DRouteSnapshotBuilder.Build(changedState, grid, 3f);
        presenter.ReconcileSnapshot(changedSnapshot, controller, grid, indoorGrid.Size);

        var changedEntity = presenter.ProxyRoot.Find(initialEntity.name);
        Assert.That(changedEntity, Is.SameAs(initialEntity));
        Assert.That(changedEntity!.transform.position, Is.Not.EqualTo(initialPosition));
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(target, value);
    }
}
