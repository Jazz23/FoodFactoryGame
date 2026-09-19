// Verifies grid-authoritative 3D construction previews, commits, proxies, and isolated persistence.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FactoryConstructionServiceTests
{
    private readonly FactoryConstructionService service = new();

    [TestCase(GridProjection.Orthogonal)]
    [TestCase(GridProjection.Dimetric)]
    public void ThreeDRaySnapsToTheSameLogicalCellForBothGridProjections(GridProjection projection)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var gridObject = new GameObject("Construction Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var serializedGrid = new SerializedObject(grid);
        serializedGrid.FindProperty("projection").enumValueIndex = (int)projection;
        serializedGrid.FindProperty("cellSize").floatValue = 2f;
        serializedGrid.ApplyModifiedPropertiesWithoutUndo();

        var logicalCenter = SceneGrid.CellCenterLogical(new Vector2Int(3, 4));
        var adapter = grid.CreateSpatialAdapter();
        var projected = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(0u, 0, logicalCenter),
            0f);
        var ray = new Ray(new Vector3(projected.x, 10f, projected.z), Vector3.down);

        Assert.That(adapter.TryGetCellFrom3DRay(ray, 0f, out var cell), Is.True);
        Assert.That(new Vector2Int(cell.x, cell.y), Is.EqualTo(new Vector2Int(3, 4)));

        UnityEngine.Object.DestroyImmediate(gridObject);
    }

    [Test]
    public void BuildingPreviewContainsWallFootprintAndSuppliesDefaultDoor()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        var preview = service.PreviewBuilding(
            state,
            2,
            new Vector3Int(8, 2, 0),
            new Vector2Int(4, 5),
            2,
            null,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            false);

        Assert.That(preview.IsValid, Is.True, preview.Error);
        Assert.That(preview.FootprintCells, Has.Count.EqualTo(20));
        Assert.That(preview.FootprintCells, Does.Contain(new Vector3Int(8, 2, 0)));
        Assert.That(preview.FootprintCells, Does.Contain(new Vector3Int(11, 6, 0)));
        Assert.That(preview.ProposedBuilding.Doors, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildingPreviewRejectsFootprintsOutsideConfiguredSceneBounds()
    {
        var boundedService = new FactoryConstructionService();
        boundedService.SetLogicalBounds(new BoundsInt(0, 0, 0, 8, 8, 1));
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(3, 3), 1);
        var preview = boundedService.PreviewBuilding(
            state,
            2,
            new Vector3Int(6, 2, 0),
            new Vector2Int(3, 3),
            1,
            null,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            false);

        Assert.That(preview.IsValid, Is.False);
        Assert.That(preview.Error, Does.Contain("scene bounds"));
    }

    [Test]
    public void OutsideTestSceneLoadsConfiguredLogicalConstructionBounds()
    {
        var scene = EditorSceneManager.OpenScene(
            "Assets/Scenes/OutsideTest.unity",
            OpenSceneMode.Additive);
        try
        {
            var grids = UnityEngine.Object.FindObjectsByType<SceneGrid>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            SceneGrid grid = null!;
            foreach (var candidate in grids)
            {
                if (candidate.gameObject.scene == scene)
                {
                    grid = candidate;
                    break;
                }
            }
            Assert.That(grid, Is.Not.Null);
            Assert.That(grid!.HasLogicalBounds, Is.True);
            Assert.That(grid.LogicalBounds.Contains(new Vector3Int(-32, -32, 0)), Is.True);
            Assert.That(grid.LogicalBounds.Contains(new Vector3Int(32, 0, 0)), Is.False);

            var boundedService = new FactoryConstructionService();
            boundedService.SetLogicalBounds(grid.LogicalBounds);
            var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(3, 3), 1);
            var preview = boundedService.PreviewBuilding(
                state,
                2,
                new Vector3Int(30, 0, 0),
                new Vector2Int(3, 3),
                1,
                null,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                false);
            Assert.That(preview.IsValid, Is.False);
            Assert.That(preview.Error, Does.Contain("scene bounds"));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void BuildingPreviewRejectsWallCellOverlapWithoutMutatingState()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(5, 5), 1);
        var before = state.CaptureState();
        var preview = service.PreviewBuilding(
            state,
            2,
            new Vector3Int(4, 1, 0),
            new Vector2Int(4, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>(),
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            false);

        Assert.That(preview.IsValid, Is.False);
        Assert.That(preview.Error, Does.Contain("overlaps"));
        Assert.That(state.CaptureState().Buildings, Has.Count.EqualTo(before.Buildings.Count));
        Assert.That(state.CaptureState().Floors, Has.Count.EqualTo(before.Floors.Count));
    }

    [Test]
    public void EquipmentPreviewEnforcesInteriorBoundsAndCellOccupancy()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        Assert.That(state.TryAddTestMachine(1, 0, new Vector2(0.5f, 0.5f), out _, out var addError), Is.True, addError);

        var occupied = service.PreviewEquipment(
            state, 1, 0, "conveyor-east", new Vector2Int(0, 0));
        var valid = service.PreviewEquipment(
            state, 1, 0, "conveyor-east", new Vector2Int(3, 3));
        var outside = service.PreviewEquipment(
            state, 1, 0, "conveyor-east", new Vector2Int(4, 0));

        Assert.That(occupied.IsValid, Is.False);
        Assert.That(occupied.Error, Does.Contain("occupied"));
        Assert.That(valid.IsValid, Is.True, valid.Error);
        Assert.That(outside.IsValid, Is.False);
        Assert.That(outside.Error, Does.Contain("usable interior"));
    }

    [Test]
    public void ElevatorPreviewRequiresAnAlignedPairedFloorEntity()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 2);
        Assert.That(state.TryAddTestEntity(
            1,
            0,
            FactoryEntityDefinitions.ElevatorBottomDefinitionId,
            new Vector2(1.5f, 2.5f),
            out _,
            out var addError), Is.True, addError);

        var aligned = service.PreviewEquipment(
            state,
            1,
            1,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            new Vector2Int(1, 2));
        var misaligned = service.PreviewEquipment(
            state,
            1,
            1,
            FactoryEntityDefinitions.ElevatorTopDefinitionId,
            new Vector2Int(2, 2));

        Assert.That(aligned.IsValid, Is.True, aligned.Error);
        Assert.That(misaligned.IsValid, Is.False);
        Assert.That(misaligned.Error, Does.Contain("paired elevator"));
    }

    [Test]
    public void CancelAndInvalidPreviewDoNotMutateStableEntityOrBuildingIds()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        Assert.That(state.TryAddTestMachine(1, 0, new Vector2(0.5f, 0.5f), out var entityId, out var addError), Is.True, addError);
        var move = service.PreviewEntityMove(state, 1, 0, entityId, new Vector2Int(1, 1));
        service.Cancel();

        Assert.That(service.TryCommit(state, move, false, out _, out var moveError), Is.True, moveError);
        Assert.That(state.TryGetFloorState(1, 0, out var floor), Is.True);
        Assert.That(floor.TryGetEntity(entityId, out var movedEntity), Is.True);
        Assert.That(movedEntity.LogicalPosition, Is.EqualTo(new Vector2(1.5f, 1.5f)));
        Assert.That(entityId, Is.GreaterThan(0u));

        var buildingMove = service.PreviewBuilding(
            state,
            1,
            new Vector3Int(3, 2, 0),
            new Vector2Int(6, 6),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>(),
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            true);
        Assert.That(service.TryCommit(state, buildingMove, false, out _, out var buildingError), Is.True, buildingError);
        Assert.That(state.TryGetBuildingRecord(1, out var record), Is.True);
        Assert.That(record.BuildingInstanceId, Is.EqualTo(1u));
        Assert.That(record.AnchorCell, Is.EqualTo(new Vector3Int(3, 2, 0)));
    }

    [Test]
    public void BuildingMovePreservesConnectionEndpointsAndGameplayState()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 2);
        Assert.That(state.TryAddTestMachine(1, 0, new Vector2(0.5f, 0.5f), out var sourceId, out var sourceError), Is.True, sourceError);
        Assert.That(state.TryAddTestStorage(1, 1, new Vector2(1.5f, 0.5f), out var destinationId, out var destinationError), Is.True, destinationError);
        Assert.That(state.TryAddConnection(
            new FactoryEntityEndpoint(1, 0, sourceId),
            new FactoryEntityEndpoint(1, 1, destinationId),
            out var connectionError), Is.True, connectionError);
        var connection = state.Connections[0];

        var preview = service.PreviewBuilding(
            state,
            1,
            new Vector3Int(2, 3, 0),
            new Vector2Int(6, 6),
            2,
            Array.Empty<BuildingRecord.DoorPlacement>(),
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            true);
        Assert.That(service.TryCommit(state, preview, false, out _, out var error), Is.True, error);

        Assert.That(state.Connections, Has.Count.EqualTo(1));
        Assert.That(state.Connections[0].Guid, Is.EqualTo(connection.Guid));
        Assert.That(state.Connections[0].Source, Is.EqualTo(connection.Source));
        Assert.That(state.Connections[0].Destination, Is.EqualTo(connection.Destination));
        Assert.That(state.TryGetFloorState(1, 0, out var floor), Is.True);
        Assert.That(floor.TryGetEntity(sourceId, out _), Is.True);
        Assert.That(state.TryGetFloorState(1, 1, out var upperFloor), Is.True);
        Assert.That(upperFloor.TryGetEntity(destinationId, out _), Is.True);
    }

    [Test]
    public void BuildingRemovalRequiresConfirmationAndThenUsesTheAuthoritativeDelete()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        var preview = service.PreviewBuildingRemoval(state, 1);

        Assert.That(service.TryCommit(state, preview, false, out _, out var rejectedError), Is.False);
        Assert.That(rejectedError, Does.Contain("target-specific confirmation"));
        Assert.That(state.TryGetBuildingRecord(1, out _), Is.True);
        Assert.That(
            service.TryCommit(
                state,
                preview,
                new FactoryConstructionConfirmation(preview),
                out _,
                out var deleteError),
            Is.True,
            deleteError);
        Assert.That(state.TryGetBuildingRecord(1, out _), Is.False);
    }

    [Test]
    public void EntityRemovalRequiresTheSameFreshTargetConfirmationAndRejectsWithoutMutation()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        Assert.That(
            state.TryAddTestMachine(
                1,
                0,
                new Vector2(1.5f, 1.5f),
                out var entityId,
                out var addError),
            Is.True,
            addError);
        var preview = service.PreviewEntityRemoval(state, 1, 0, entityId);
        var replacementPreview = service.PreviewEntityRemoval(state, 1, 0, entityId);
        var before = state.CaptureState();
        service.Cancel();
        Assert.That(state.CaptureState().Floors, Has.Count.EqualTo(before.Floors.Count));

        Assert.That(
            service.TryCommit(state, preview, true, out _, out var legacyError),
            Is.False);
        Assert.That(legacyError, Does.Contain("fresh target-specific"));
        Assert.That(
            service.TryCommit(
                state,
                preview,
                new FactoryConstructionConfirmation(replacementPreview),
                out _,
                out var wrongConfirmationError),
            Is.False);
        Assert.That(wrongConfirmationError, Does.Contain("fresh target-specific"));
        Assert.That(state.CaptureState().Floors, Has.Count.EqualTo(before.Floors.Count));
        Assert.That(state.TryGetFloorState(1, 0, out var floor), Is.True);
        Assert.That(floor.TryGetEntity(entityId, out _), Is.True);

        Assert.That(
            service.TryCommit(
                state,
                preview,
                new FactoryConstructionConfirmation(preview),
                out _,
                out var confirmationError),
            Is.True,
            confirmationError);
        Assert.That(floor.TryGetEntity(entityId, out _), Is.False);
    }

    [Test]
    public void EquipmentPlacementMoveRemovalAndReloadPreserveUnrelatedGameplayState()
    {
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"factory-construction-flow-{Guid.NewGuid():N}.db");
        try
        {
            var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 2);
            Assert.That(
                state.TryAddTestMachine(
                    1,
                    0,
                    new Vector2(0.5f, 0.5f),
                    out var sourceId,
                    out var sourceError),
                Is.True,
                sourceError);
            Assert.That(
                state.TryAddTestStorage(
                    1,
                    1,
                    new Vector2(1.5f, 1.5f),
                    out var destinationId,
                    out var destinationError),
                Is.True,
                destinationError);
            Assert.That(
                state.TryAddConnection(
                    new FactoryEntityEndpoint(1, 0, sourceId),
                    new FactoryEntityEndpoint(1, 1, destinationId),
                    out var connectionError),
                Is.True,
                connectionError);
            state.AdvanceProduction(2f);
            Assert.That(state.TryGetFloorState(1, 0, out var sourceFloor), Is.True);
            Assert.That(sourceFloor.TryGetEntity(sourceId, out var source), Is.True);
            var producedBeforeFlow = source.ProducedCount;

            var placement = service.PreviewEquipment(
                state,
                1,
                0,
                "conveyor-east",
                new Vector2Int(2, 2));
            Assert.That(placement.IsValid, Is.True, placement.Error);
            Assert.That(
                service.TryCommit(state, placement, false, out var placedEntityId, out var placementError),
                Is.True,
                placementError);

            var move = service.PreviewEntityMove(
                state,
                1,
                0,
                placedEntityId,
                new Vector2Int(3, 2));
            Assert.That(move.IsValid, Is.True, move.Error);
            Assert.That(
                service.TryCommit(state, move, false, out _, out var moveError),
                Is.True,
                moveError);

            var removal = service.PreviewEntityRemoval(state, 1, 0, placedEntityId);
            Assert.That(removal.IsValid, Is.True, removal.Error);
            var beforeRemoval = state.CaptureState();
            service.Cancel();
            Assert.That(state.ConnectionCount, Is.EqualTo(beforeRemoval.Connections.Count));
            Assert.That(state.TryGetFloorState(1, 0, out var unchangedFloor), Is.True);
            Assert.That(unchangedFloor.TryGetEntity(placedEntityId, out _), Is.True);

            Assert.That(
                service.TryCommit(
                    state,
                    removal,
                    new FactoryConstructionConfirmation(removal),
                    out _,
                    out var removalError),
                Is.True,
                removalError);
            Assert.That(state.TryGetFloorState(1, 0, out var finalFloor), Is.True);
            Assert.That(finalFloor.TryGetEntity(placedEntityId, out _), Is.False);
            Assert.That(state.ConnectionCount, Is.EqualTo(1));
            Assert.That(finalFloor.TryGetEntity(sourceId, out var finalSource), Is.True);
            Assert.That(finalSource.ProducedCount, Is.EqualTo(producedBeforeFlow));

            Assert.That(state.SaveToFile(path), Is.True);
            var restored = new FactoryWorldState(1);
            Assert.That(restored.LoadFromFile(path), Is.True);
            Assert.That(restored.ConnectionCount, Is.EqualTo(1));
            Assert.That(restored.TryGetFloorState(1, 0, out var restoredFloor), Is.True);
            Assert.That(restoredFloor.TryGetEntity(placedEntityId, out _), Is.False);
            Assert.That(restoredFloor.TryGetEntity(sourceId, out var restoredSource), Is.True);
            Assert.That(restoredSource.ProducedCount, Is.EqualTo(producedBeforeFlow));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    [Test]
    public void InteriorOnlySemanticsComeFromAuthorityInsteadOfFootprintEquality()
    {
        var state = new FactoryWorldState(1);
        Assert.That(
            state.TryRegisterBuilding(
                new BuildingRecord(
                    1,
                    Vector3Int.zero,
                    new Vector2Int(6, 6),
                    1,
                    Array.Empty<BuildingRecord.DoorPlacement>()),
                out var shellError),
            Is.True,
            shellError);
        Assert.That(
            state.TryRegisterBuilding(2, 1, new Vector2Int(6, 6), out var legacyError),
            Is.True,
            legacyError);

        Assert.That(state.TryGetBuildingInfo(1, out var shellInfo), Is.True);
        Assert.That(state.TryGetBuildingInfo(2, out var legacyInfo), Is.True);
        Assert.That(shellInfo.IsInteriorOnly, Is.False);
        Assert.That(shellInfo.InteriorSize, Is.EqualTo(new Vector2Int(4, 4)));
        Assert.That(legacyInfo.IsInteriorOnly, Is.True);
        Assert.That(legacyInfo.InteriorSize, Is.EqualTo(new Vector2Int(6, 6)));
    }

    [Test]
    public void ThreeDConstructionOwnershipCanBeDisabledWithoutALiveManager()
    {
        var ownerObject = new GameObject("3D Construction Ownership Test");
        try
        {
            var controller = ownerObject.AddComponent<Factory3DConstructionController>();
            controller.Set3DInputOwnership(true);
            Assert.That(
                Factory3DConstructionController.IsInputOwnedBy3D(ownerObject.scene),
                Is.True);
            Assert.That(
                Factory3DConstructionController.IsConstructionActiveIn3D(ownerObject.scene),
                Is.False);
            Assert.That(controller.Mode, Is.EqualTo(Factory3DConstructionMode.Idle));

            controller.SetPointerFocusOverride(true);
            Assert.That(controller.IsPointerFocusBlockingInput, Is.True);
            controller.SetPointerFocusOverride(false);
            Assert.That(controller.IsPointerFocusBlockingInput, Is.False);

            controller.enabled = false;
            Assert.That(
                Factory3DConstructionController.IsInputOwnedBy3D(ownerObject.scene),
                Is.False);
            controller.enabled = true;
            Assert.That(
                Factory3DConstructionController.IsInputOwnedBy3D(ownerObject.scene),
                Is.True);

            controller.Set3DInputOwnership(false);
            Assert.That(
                Factory3DConstructionController.IsInputOwnedBy3D(ownerObject.scene),
                Is.False);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(ownerObject);
        }
    }

    [Test]
    public void ProxyReconciliationReflectsCommittedBuildingWithoutBecomingAuthority()
    {
        var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 1);
        var preview = service.PreviewBuilding(
            state,
            2,
            new Vector3Int(8, 0, 0),
            new Vector2Int(4, 4),
            1,
            Array.Empty<BuildingRecord.DoorPlacement>(),
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            false);
        Assert.That(service.TryCommit(state, preview, false, out _, out var error), Is.True, error);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var gridObject = new GameObject("Proxy Grid");
        SceneManager.MoveGameObjectToScene(gridObject, scene);
        var grid = gridObject.AddComponent<SceneGrid>();
        var root = new GameObject("Proxy Root");
        SceneManager.MoveGameObjectToScene(root, scene);
        var view = root.AddComponent<Factory3DOutsideTestProxyView>();
        var result = view.Rebuild(
            grid,
            state,
            3f,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance);

        Assert.That(result.BuildingCount, Is.EqualTo(2));
        Assert.That(root.transform.Find("Building 2"), Is.Not.Null);
        Assert.That(state.BuildingRecords, Has.Count.EqualTo(2));

        UnityEngine.Object.DestroyImmediate(root);
        UnityEngine.Object.DestroyImmediate(gridObject);
    }

    [Test]
    public void ChangedTopologyAndConnectionsRoundTripThroughAnIsolatedDatabase()
    {
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"factory-construction-{Guid.NewGuid():N}.db");
        try
        {
            var buildingGuid = Guid.NewGuid();
            var floorGuid = Guid.NewGuid();
            var upperFloorGuid = Guid.NewGuid();
            var sourceGuid = Guid.NewGuid();
            var destinationGuid = Guid.NewGuid();
            var snapshot = new FactoryWorldSnapshot();
            snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
                buildingGuid,
                "outside-test-building",
                new Vector3Int(7, -2, 0),
                new Vector2Int(6, 6),
                2,
                false,
                1));
            var floor = new FactoryWorldFloorRecord(
                floorGuid,
                buildingGuid,
                0,
                "Construction",
                1f,
                0f,
                Vector2.one);
            floor.SetEntities(new[]
            {
                new FactoryWorldEntityRecord(
                    sourceGuid, floorGuid, FactoryEntityDefinitions.TestMachineDefinitionId,
                    new Vector2(0.5f, 0.5f), 0f, Vector2.one, Array.Empty<byte>(), 1)
            });
            snapshot.Floors.Add(floor);
            var upperFloor = new FactoryWorldFloorRecord(
                upperFloorGuid,
                buildingGuid,
                1,
                "Construction Upper",
                1f,
                0f,
                Vector2.one);
            upperFloor.SetEntities(new[]
            {
                new FactoryWorldEntityRecord(
                    destinationGuid, upperFloorGuid, FactoryEntityDefinitions.TestStorageDefinitionId,
                    new Vector2(1.5f, 0.5f), 0f, Vector2.one, Array.Empty<byte>(), 2)
            });
            snapshot.Floors.Add(upperFloor);
            snapshot.Connections.Add(new FactoryWorldConnectionRecord(
                Guid.NewGuid(),
                new FactoryWorldEndpoint(buildingGuid, floorGuid, sourceGuid, 0),
                new FactoryWorldEndpoint(buildingGuid, upperFloorGuid, destinationGuid, 1)));

            var store = new FactoryWorldSqliteStore(path);
            store.Save(snapshot);
            var loaded = store.Load();

            Assert.That(path, Does.Not.EndWith("factory-world.db"));
            Assert.That(loaded.Buildings[0].AnchorCell, Is.EqualTo(new Vector3Int(7, -2, 0)));
            Assert.That(loaded.Buildings[0].LegacyBuildingId, Is.EqualTo(1u));
            Assert.That(loaded.Connections, Has.Count.EqualTo(1));
            Assert.That(loaded.Connections[0].Source.EntityGuid, Is.EqualTo(sourceGuid));
            Assert.That(loaded.Connections[0].Destination.EntityGuid, Is.EqualTo(destinationGuid));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    [Test]
    public void CommittedConstructionStateRoundTripsThroughAnIsolatedSave()
    {
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"factory-construction-committed-{Guid.NewGuid():N}.db");
        try
        {
            var state = CreateStateWithBuilding(1, Vector3Int.zero, new Vector2Int(6, 6), 2);
            Assert.That(
                state.TryAddTestMachine(
                    1,
                    0,
                    new Vector2(0.5f, 0.5f),
                    out var sourceId,
                    out var sourceError),
                Is.True,
                sourceError);
            Assert.That(
                state.TryAddTestStorage(
                    1,
                    1,
                    new Vector2(1.5f, 0.5f),
                    out var destinationId,
                    out var destinationError),
                Is.True,
                destinationError);
            Assert.That(
                state.TryAddConnection(
                    new FactoryEntityEndpoint(1, 0, sourceId),
                    new FactoryEntityEndpoint(1, 1, destinationId),
                    out var connectionError),
                Is.True,
                connectionError);

            var move = service.PreviewBuilding(
                state,
                1,
                new Vector3Int(4, 3, 0),
                new Vector2Int(6, 6),
                2,
                null,
                TestBuildingCreator.DefaultDoorCornerExclusionDistance,
                true);
            Assert.That(
                service.TryCommit(state, move, false, out _, out var commitError),
                Is.True,
                commitError);
            Assert.That(state.SaveToFile(path), Is.True);

            var restored = new FactoryWorldState(1);
            Assert.That(restored.LoadFromFile(path), Is.True);
            Assert.That(restored.TryGetBuildingRecord(1, out var restoredBuilding), Is.True);
            Assert.That(restoredBuilding.AnchorCell, Is.EqualTo(new Vector3Int(4, 3, 0)));
            Assert.That(restored.TryGetFloorState(1, 0, out var restoredFloor), Is.True);
            Assert.That(restoredFloor.TryGetEntity(sourceId, out _), Is.True);
            Assert.That(restored.TryGetFloorState(1, 1, out var restoredUpperFloor), Is.True);
            Assert.That(restoredUpperFloor.TryGetEntity(destinationId, out _), Is.True);
            Assert.That(restored.ConnectionCount, Is.EqualTo(1));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var candidate = path + suffix;
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }

    private static FactoryWorldState CreateStateWithBuilding(
        uint buildingId,
        Vector3Int anchor,
        Vector2Int footprint,
        int stories)
    {
        var state = new FactoryWorldState(1);
        Assert.That(state.TryRegisterBuilding(
            new BuildingRecord(
                buildingId,
                anchor,
                footprint,
                stories,
                Array.Empty<BuildingRecord.DoorPlacement>()),
            out var error), Is.True, error);
        return state;
    }
}
