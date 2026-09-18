// Verifies read-only route snapshots and disposable proxy reconciliation against fixed authority.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

public sealed class Factory3DRoutePresentationTests
{
    private GameObject gridObject = null!;
    private GameObject proxyRoot = null!;

    [SetUp]
    public void SetUp()
    {
        gridObject = new GameObject("Route Snapshot Grid");
        gridObject.AddComponent<SceneGrid>();
        proxyRoot = new GameObject("Route Proxy Root");
    }

    [TearDown]
    public void TearDown()
    {
        if (proxyRoot is not null && proxyRoot)
        {
            UnityEngine.Object.DestroyImmediate(proxyRoot);
        }

        if (gridObject is not null && gridObject)
        {
            UnityEngine.Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void SnapshotMapsFixedTopologyCoordinatesBuffersAndProduction()
    {
        var state = Factory3DRouteFixture.CreateState();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(
            state,
            gridObject.GetComponent<SceneGrid>()!,
            3f);

        Assert.That(snapshot.Buildings, Has.Count.EqualTo(1));
        Assert.That(snapshot.Floors, Has.Count.EqualTo(2));
        Assert.That(snapshot.Entities, Has.Count.EqualTo(6));
        Assert.That(snapshot.Connections, Has.Count.EqualTo(1));
        Assert.That(snapshot.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    0,
                    Factory3DRouteFixture.SourceEntityId),
                out var source), Is.True);
        Assert.That(source.RouteRole, Is.EqualTo(Factory3DRouteRole.Source));
        Assert.That(source.ProducedCount, Is.Zero);
        Assert.That(source.OutputCount, Is.Zero);

        Assert.That(snapshot.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    0,
                    Factory3DRouteFixture.LowerBeltEntityId),
                out var belt), Is.True);
        Assert.That(belt.ConveyorPositions, Is.Empty);
        Assert.That(belt.BeltOccupants, Is.Empty);
        Assert.That(belt.WorldPosition.y, Is.Zero.Within(0.0001f));

        Assert.That(snapshot.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    0,
                    Factory3DRouteFixture.BottomElevatorEntityId),
                out var elevator), Is.True);
        Assert.That(elevator.ElevatorPhase, Is.EqualTo(Factory3DElevatorTransferPhase.Idle));
        Assert.That(elevator.PairedElevatorId!.Value.EntityId, Is.EqualTo(Factory3DRouteFixture.TopElevatorEntityId));

        Assert.That(snapshot.Entities.Any(entity =>
            entity.EntityId == Factory3DRouteFixture.StorageEntityId
            && entity.InventoryCount == 0
            && entity.InventoryCapacity == FactoryEntityRecord.OutputCapacity), Is.True);
    }

    [Test]
    public void ProxyRebuildIsIdempotentUsesCanonicalIdentityAndFiltersFloors()
    {
        var state = Factory3DRouteFixture.CreateState();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, gridObject.GetComponent<SceneGrid>()!, 3f);
        var assembler = new Factory3DRouteProxyAssembler();

        var first = assembler.Reconcile(proxyRoot.transform, snapshot);
        var firstEntity = proxyRoot.transform.Find(
            Factory3DRouteProxyAssembler.GetEntityCanonicalId(
                new Factory3DRouteEntityId(Factory3DRouteFixture.BuildingId, 0, Factory3DRouteFixture.SourceEntityId)));
        var second = assembler.Reconcile(proxyRoot.transform, snapshot);

        Assert.That(first.EntityCount, Is.EqualTo(6));
        Assert.That(first.ConnectionCount, Is.EqualTo(1));
        Assert.That(first.ElevatorTransferCount, Is.EqualTo(1));
        Assert.That(second.RemovedNodeCount, Is.Zero);
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(8));
        Assert.That(proxyRoot.transform.Find(firstEntity!.name), Is.SameAs(firstEntity));
        Assert.That(firstEntity.GetComponent<Factory3DRouteProxyIdentity>()!.CanonicalId, Is.EqualTo(firstEntity.name));

        assembler.Reconcile(proxyRoot.transform, snapshot, 0);
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(3));
        Assert.That(proxyRoot.transform.Cast<Transform>().All(child => child.name.Contains("-F0-")), Is.True);

        assembler.Reconcile(proxyRoot.transform, snapshot, 1);
        Assert.That(proxyRoot.transform.childCount, Is.EqualTo(3));
        Assert.That(proxyRoot.transform.Cast<Transform>().All(child => child.name.Contains("-F1-")), Is.True);
    }

    [Test]
    public void ProxyRemovesStaleNodesAndClearMissingAuthorityLeavesNoObjects()
    {
        var state = Factory3DRouteFixture.CreateState();
        var assembler = new Factory3DRouteProxyAssembler();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, gridObject.GetComponent<SceneGrid>()!, 3f);
        assembler.Reconcile(proxyRoot.transform, snapshot);

        Assert.That(state.TryRemoveTestEntity(
                Factory3DRouteFixture.BuildingId,
                1,
                Factory3DRouteFixture.StorageEntityId,
                out var error), Is.True, error);
        var reduced = Factory3DRouteSnapshotBuilder.Build(state, gridObject.GetComponent<SceneGrid>()!, 3f);
        var result = assembler.Reconcile(proxyRoot.transform, reduced);
        Assert.That(result.RemovedNodeCount, Is.EqualTo(1));

        assembler.Reconcile(proxyRoot.transform, null!);
        Assert.That(proxyRoot.transform.childCount, Is.Zero);
    }

    [Test]
    public void ProxyRebuildDoesNotChangeAuthoritativeState()
    {
        var state = Factory3DRouteFixture.CreateState();
        var before = state.CaptureState();
        var view = proxyRoot.AddComponent<Factory3DRouteProxyView>();
        view.Rebuild(state, gridObject.GetComponent<SceneGrid>()!, 3f);
        var after = state.CaptureState();

        Assert.That(after.Buildings.Count, Is.EqualTo(before.Buildings.Count));
        Assert.That(after.Floors.Count, Is.EqualTo(before.Floors.Count));
        Assert.That(after.Connections.Count, Is.EqualTo(before.Connections.Count));
        Assert.That(after.Floors.SelectMany(floor => floor.Entities).Select(entity => entity.EntityId),
            Is.EqualTo(before.Floors.SelectMany(floor => floor.Entities).Select(entity => entity.EntityId)));
        Assert.That(Factory3DRouteFixture.CreateIsolatedSavePath().Contains("food-factory-route-"), Is.True);
    }

    [Test]
    public void SnapshotReportsDiscreteElevatorArrivalAfterAuthoritativeTransfer()
    {
        var state = Factory3DRouteFixture.CreateState();
        Assert.That(state.TryGetFloorState(
                Factory3DRouteFixture.BuildingId,
                0,
                out var lowerFloor), Is.True);
        Assert.That(lowerFloor!.TryGetEntity(
                Factory3DRouteFixture.BottomElevatorEntityId,
                out var bottomAuthority), Is.True);
        bottomAuthority!.SetInputCount(1);
        var grid = gridObject.GetComponent<SceneGrid>()!;
        var before = Factory3DRouteSnapshotBuilder.Build(state, grid);
        Assert.That(before.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    0,
                    Factory3DRouteFixture.BottomElevatorEntityId),
                out var bottomBefore), Is.True);
        Assert.That(bottomBefore.ElevatorPhase, Is.EqualTo(Factory3DElevatorTransferPhase.ReadyUpward));

        Factory3DRouteFixture.AdvanceTick(state);
        var after = Factory3DRouteSnapshotBuilder.Build(state, grid);

        Assert.That(after.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    0,
                    Factory3DRouteFixture.BottomElevatorEntityId),
                out var bottomAfter), Is.True);
        Assert.That(bottomAfter.ElevatorPhase, Is.EqualTo(Factory3DElevatorTransferPhase.TransferredUpward));
        Assert.That(after.TryGetEntity(
                new Factory3DRouteEntityId(
                    Factory3DRouteFixture.BuildingId,
                    1,
                    Factory3DRouteFixture.TopElevatorEntityId),
                out var topAfter), Is.True);
        Assert.That(topAfter.OutputCount, Is.EqualTo(1));
    }

    [Test]
    public void FloorSelectionAndProxyRefreshDoNotChangeSimulationResults()
    {
        var withPresentation = Factory3DRouteFixture.CreateState();
        var withoutPresentation = Factory3DRouteFixture.CreateState();
        var grid = gridObject.GetComponent<SceneGrid>()!;
        var assembler = new Factory3DRouteProxyAssembler();

        for (var tick = 0; tick < 4; tick++)
        {
            var snapshot = Factory3DRouteSnapshotBuilder.Build(withPresentation, grid);
            assembler.Reconcile(proxyRoot.transform, snapshot, tick % 2);
            withPresentation.AdvanceProduction(FactorySimulation.DefaultTickInterval);
            withoutPresentation.AdvanceProduction(FactorySimulation.DefaultTickInterval);
        }

        var presentedState = withPresentation.CaptureState();
        var unpresentedState = withoutPresentation.CaptureState();
        Assert.That(
            presentedState.Floors.SelectMany(floor => floor.Entities)
                .Select(entity => (entity.EntityId, entity.InputCount, entity.OutputCount))
                .ToArray(),
            Is.EqualTo(unpresentedState.Floors.SelectMany(floor => floor.Entities)
                .Select(entity => (entity.EntityId, entity.InputCount, entity.OutputCount))
                .ToArray()));
    }

    [Test]
    public void IsolatedSaveReloadRestoresRouteIdentityAndStorageContents()
    {
        var path = Factory3DRouteFixture.CreateIsolatedSavePath(Guid.NewGuid());
        try
        {
            var state = Factory3DRouteFixture.CreateState();
            Assert.That(Factory3DRouteFixture.SaveToIsolatedPath(state, path), Is.True);

            var restored = new FactoryWorldState(Factory3DRouteFixture.BuildingId);
            Assert.That(restored.LoadFromFile(path), Is.True);
            var snapshot = Factory3DRouteSnapshotBuilder.Build(
                restored,
                gridObject.GetComponent<SceneGrid>()!);

            Assert.That(snapshot.Entities.Select(entity => entity.Id.CanonicalId), Is.EqualTo(
                new[]
                {
                    $"B{Factory3DRouteFixture.BuildingId}/F0/E{Factory3DRouteFixture.SourceEntityId}",
                    $"B{Factory3DRouteFixture.BuildingId}/F0/E{Factory3DRouteFixture.LowerBeltEntityId}",
                    $"B{Factory3DRouteFixture.BuildingId}/F0/E{Factory3DRouteFixture.BottomElevatorEntityId}",
                    $"B{Factory3DRouteFixture.BuildingId}/F1/E{Factory3DRouteFixture.TopElevatorEntityId}",
                    $"B{Factory3DRouteFixture.BuildingId}/F1/E{Factory3DRouteFixture.UpperBeltEntityId}",
                    $"B{Factory3DRouteFixture.BuildingId}/F1/E{Factory3DRouteFixture.StorageEntityId}"
                }));
            Assert.That(snapshot.Entities.Single(entity => entity.EntityId == Factory3DRouteFixture.StorageEntityId).StorageContents, Is.Zero);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void DeterministicFixtureCapturesSourceBeltElevatorArrivalAndStorageStages()
    {
        var state = Factory3DRouteFixture.CreateState();
        var grid = gridObject.GetComponent<SceneGrid>()!;
        var tick = 0;
        var initial = Factory3DRouteSnapshotBuilder.Build(state, grid);
        Assert.That(GetEntity(initial, Factory3DRouteFixture.StorageEntityId).StorageContents, Is.Zero);

        var sourceBeltTick = AdvanceUntil(state, grid, ref tick, snapshot =>
            GetEntity(snapshot, Factory3DRouteFixture.LowerBeltEntityId).BeltOccupants.Count > 0);
        var sourceBelt = Factory3DRouteSnapshotBuilder.Build(state, grid);
        Assert.That(GetEntity(sourceBelt, Factory3DRouteFixture.SourceEntityId).EntityId, Is.EqualTo(Factory3DRouteFixture.SourceEntityId));
        Assert.That(GetEntity(sourceBelt, Factory3DRouteFixture.LowerBeltEntityId).BeltOccupants, Is.Not.Empty);

        // The authoritative fixed tick accepts and completes the discrete handoff
        // in one operation; the first observable arrival is the upper output buffer.
        var elevatorTransferTick = AdvanceUntil(state, grid, ref tick, snapshot =>
            GetEntity(snapshot, Factory3DRouteFixture.TopElevatorEntityId).OutputCount > 0);
        var elevatorArrival = Factory3DRouteSnapshotBuilder.Build(state, grid);
        Assert.That(GetEntity(elevatorArrival, Factory3DRouteFixture.BottomElevatorEntityId).ElevatorPhase,
            Is.EqualTo(Factory3DElevatorTransferPhase.TransferredUpward));

        var upperBeltTick = AdvanceUntil(state, grid, ref tick, snapshot =>
            GetEntity(snapshot, Factory3DRouteFixture.UpperBeltEntityId).BeltOccupants.Count > 0);
        var storageTick = AdvanceUntil(state, grid, ref tick, snapshot =>
            GetEntity(snapshot, Factory3DRouteFixture.StorageEntityId).StorageContents > 0);
        var stored = Factory3DRouteSnapshotBuilder.Build(state, grid);
        Assert.That(GetEntity(stored, Factory3DRouteFixture.StorageEntityId).StorageContents, Is.EqualTo(1));
        Assert.That(sourceBeltTick, Is.LessThan(elevatorTransferTick));
        Assert.That(elevatorTransferTick, Is.LessThan(upperBeltTick));
        Assert.That(upperBeltTick, Is.LessThan(storageTick));
        Assert.That(tick, Is.LessThanOrEqualTo(100));
    }

    private static int AdvanceUntil(
        FactoryWorldState state,
        SceneGrid grid,
        ref int tick,
        Func<Factory3DRouteSnapshot, bool> predicate)
    {
        for (var attempts = 0; attempts < 100; attempts++)
        {
            var snapshot = Factory3DRouteSnapshotBuilder.Build(state, grid);
            if (predicate(snapshot))
            {
                return tick;
            }

            Factory3DRouteFixture.AdvanceTick(state);
            tick++;
        }

        Assert.Fail("The deterministic route did not reach the expected stage within 100 ticks.");
        return tick;
    }

    private static Factory3DRouteEntitySnapshot GetEntity(
        Factory3DRouteSnapshot snapshot,
        uint entityId)
    {
        return snapshot.Entities.Single(entity => entity.EntityId == entityId);
    }
}
