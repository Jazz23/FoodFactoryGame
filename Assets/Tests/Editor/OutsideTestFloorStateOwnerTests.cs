// Verifies independent OutsideTest floor identity, SQLite persistence, migration, and simulation.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using OutsideTestFloorStateOwner = FactoryWorldState;

public sealed class OutsideTestFloorStateOwnerTests
{
    [Test]
    public void MachineEditsPreserveSurvivorsProductionAndEmptyFloors()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var floor);
        floor.SetEntities(Array.Empty<FactoryEntityRecord>());
        Assert.That(owner.TryAddTestMachine(2, 0, new Vector2(1f, 1f), out var firstId, out var error), Is.True, error);
        Assert.That(owner.TryAddTestMachine(2, 0, new Vector2(2f, 2f), out var secondId, out error), Is.True, error);
        Assert.That(firstId, Is.GreaterThan(0));
        Assert.That(secondId, Is.GreaterThan(firstId));
        new FactorySimulation(owner.AdvanceProduction).Advance(1.5f);
        Assert.That(owner.TryRemoveTestMachine(2, 0, firstId, out error), Is.True, error);
        Assert.That(floor.Entities, Has.Count.EqualTo(1));
        Assert.That(floor.Entities[0].EntityId, Is.EqualTo(secondId));
        Assert.That(floor.Entities[0].DefinitionId, Is.EqualTo("test-machine"));
        Assert.That(floor.Entities[0].LogicalPosition, Is.EqualTo(new Vector2(2f, 2f)));
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(1));
        Assert.That(floor.Entities[0].CycleProgress, Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(owner.TryRemoveTestMachine(2, 0, secondId, out error), Is.True, error);
        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(owner.CaptureState()), Is.True);
        restored.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        restored.TryGetFloorState(2, 0, out var empty);
        Assert.That(empty.Entities, Is.Empty);
        restored.ApplySnapshot(2, 0, empty.Label, empty.ProductionRate, empty.AccumulatedProduction,
            empty.MarkerPosition, floor.GetEntitySnapshots());
        Assert.That(empty.Entities, Is.Empty);
        restored.TryGetFloorState(2, 1, out var upper);
        Assert.That(upper.Entities, Has.Count.EqualTo(1));
    }

    [TestCase(float.NaN, 1f)]
    [TestCase(float.PositiveInfinity, 1f)]
    [TestCase(1f, float.NegativeInfinity)]
    [TestCase(0.49f, 1f)]
    [TestCase(4.51f, 1f)]
    [TestCase(1f, 2.51f)]
    public void InvalidMachinePositionLeavesStateUnchanged(float x, float y)
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        var before = owner.CaptureState();
        Assert.That(owner.TryAddTestMachine(2, 0, new Vector2(x, y), out var id, out var error), Is.False);
        Assert.That(id, Is.Zero);
        Assert.That(error, Is.Not.Empty);
        Assert.That(StatesMatch(owner.CaptureState(), before), Is.True);
    }

    [Test]
    public void InvalidMachineTargetsAndIdExhaustionLeaveStateUnchanged()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var floor);
        floor.SetEntities(new[] { new FactoryEntityRecord(uint.MaxValue, "test-machine", Vector2.one, 1f, 0f, 0) });
        var before = owner.CaptureState();
        Assert.That(owner.TryAddTestMachine(2, 0, Vector2.one, out _, out _), Is.False);
        Assert.That(owner.TryAddTestMachine(2, 9, Vector2.one, out _, out _), Is.False);
        Assert.That(owner.TryAddTestMachine(99, 0, Vector2.one, out _, out _), Is.False);
        Assert.That(owner.TryRemoveTestMachine(2, 0, 44u, out _), Is.False);
        Assert.That(owner.TryRemoveTestMachine(2, 9, 1u, out _), Is.False);
        Assert.That(StatesMatch(owner.CaptureState(), before), Is.True);
    }

    [Test]
    public void CompositeIdentityKeepsBuildingsIndependent()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(1, 1, new Vector2Int(5, 5), out _);
        owner.TryRegisterBuilding(2, 1, new Vector2Int(5, 3), out _);

        owner.TrySetFloorState(1, 0, "Building One", 3f, new Vector2(1f, 1f));
        owner.TrySetFloorState(2, 0, "Building Two", 7f, new Vector2(2f, 2f));

        Assert.That(
            new OutsideTestFloorKey(1, 0),
            Is.Not.EqualTo(new OutsideTestFloorKey(2, 0)));
        Assert.That(owner.TryGetFloorState(1, 0, out var first), Is.True);
        Assert.That(owner.TryGetFloorState(2, 0, out var second), Is.True);
        Assert.That(first.Label, Is.EqualTo("Building One"));
        Assert.That(second.Label, Is.EqualTo("Building Two"));
        Assert.That(first.ProductionRate, Is.EqualTo(3f));
        Assert.That(second.ProductionRate, Is.EqualTo(7f));
    }

    [Test]
    public void RepeatedRegistrationPreservesStateAndConflictsAreRejected()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        var firstRegistration = owner.TryRegisterBuilding(
            2,
            2,
            new Vector2Int(5, 3),
            out var firstError);
        owner.TrySetFloorState(2, 0, "Edited", 4f, new Vector2(4.5f, 2.5f));

        var repeatedRegistration = owner.TryRegisterBuilding(
            2,
            2,
            new Vector2Int(5, 3),
            out var repeatedError);
        var conflictingRegistration = owner.TryRegisterBuilding(
            2,
            3,
            new Vector2Int(5, 3),
            out var conflictingError);

        Assert.That(firstRegistration, Is.True);
        Assert.That(firstError, Is.Empty);
        Assert.That(repeatedRegistration, Is.False);
        Assert.That(repeatedError, Is.Empty);
        Assert.That(conflictingRegistration, Is.False);
        Assert.That(conflictingError, Does.Contain("Duplicate building ID 2"));
        Assert.That(owner.TryGetFloorState(2, 0, out var state), Is.True);
        Assert.That(state.Label, Is.EqualTo("Edited"));
        Assert.That(state.ProductionRate, Is.EqualTo(4f));
    }

    [Test]
    public void VersionFourRoundTripPreservesBuildingTopologyAndMultipleDoors()
    {
        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        TestBuildingCreator.GetExteriorWallSpans(
            new Vector3Int(4, -2, 0),
            new Vector2Int(5, 4),
            spans);
        var straightSpans = new List<TestBuildingCreator.ExteriorWallSpan>();
        foreach (var span in spans)
        {
            if (!span.IsCorner)
            {
                straightSpans.Add(span);
            }
        }

        var record = new BuildingRecord(
            7,
            new Vector3Int(4, -2, 0),
            new Vector2Int(5, 4),
            3,
            new[]
            {
                new BuildingRecord.DoorPlacement(straightSpans[0].StableId, 0.25f),
                new BuildingRecord.DoorPlacement(straightSpans[1].StableId, 0.75f)
            });
        var source = new OutsideTestFloorStateOwner(1);
        Assert.That(source.TryRegisterBuilding(record, out var registrationError), Is.True);
        Assert.That(registrationError, Is.Empty);

        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(source.CaptureState()), Is.True);
        Assert.That(restored.TryGetBuildingRecord(7, out var restoredRecord), Is.True);
        Assert.That(restoredRecord.HasSameTopology(record), Is.True);
        Assert.That(new List<OutsideTestBuildingInfo>(restored.Buildings), Has.Count.EqualTo(1));
        Assert.That(new List<OutsideTestFloorRecord>(restored.FloorStates), Has.Count.EqualTo(3));
    }

    [Test]
    public void InvalidVersionFourRecordsDoNotReplaceLiveState()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 1, new Vector2Int(4, 4), out _);
        owner.TrySetFloorState(2, 0, "Preserved", 6f, new Vector2(2f, 2f));
        var invalidData = new OutsideTestFloorSaveData
        {
            Version = OutsideTestFloorStateOwner.CurrentSaveVersion,
            Buildings = new List<BuildingRecord>
            {
                new BuildingRecord(9, Vector3Int.zero, new Vector2Int(4, 4), 1),
                new BuildingRecord(9, new Vector3Int(8, 0, 0), new Vector2Int(4, 4), 1)
            },
            Floors = new List<OutsideTestFloorRecord>()
        };

        Assert.That(owner.LoadState(invalidData), Is.False);
        Assert.That(owner.TryGetFloorState(2, 0, out var state), Is.True);
        Assert.That(state.Label, Is.EqualTo("Preserved"));
        Assert.That(state.ProductionRate, Is.EqualTo(6f));
    }

    [Test]
    public void NextBuildingIdIncludesUnregisteredLegacyFloorIds()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        Assert.That(
            owner.ApplySnapshot(
                11,
                0,
                "Legacy floor",
                1f,
                0f,
                new Vector2(1f, 1f),
                Array.Empty<FactoryEntitySnapshot>()),
            Is.True);

        Assert.That(owner.GetNextBuildingId(), Is.EqualTo(12u));
    }

    [Test]
    public void VersionOneSaveMigratesLegacyRecordsAndAddsMissingBuildings()
    {
        var legacyData = new OutsideTestFloorSaveData
        {
            Version = 1,
            Floors = new List<OutsideTestFloorRecord>
            {
                new OutsideTestFloorRecord(
                    1,
                    0,
                    "Migrated Ground",
                    8f,
                    12f,
                    new Vector2(4.5f, 4.5f)),
                new OutsideTestFloorRecord(
                    0,
                    1,
                    "Migrated Upper",
                    2f,
                    3f,
                    new Vector2(5.5f, 5.5f))
            }
        };
        var owner = new OutsideTestFloorStateOwner(1);

        Assert.That(owner.LoadState(legacyData), Is.True);
        owner.TryRegisterBuilding(1, 3, new Vector2Int(5, 5), out _);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryRegisterBuilding(3, 3, new Vector2Int(4, 4), out _);

        Assert.That(owner.TryGetFloorState(1, 0, out var migratedGround), Is.True);
        Assert.That(owner.TryGetFloorState(1, 1, out var migratedUpper), Is.True);
        Assert.That(owner.TryGetFloorState(2, 0, out var newBuildingFloor), Is.True);
        Assert.That(migratedGround.Label, Is.EqualTo("Migrated Ground"));
        Assert.That(migratedGround.AccumulatedProduction, Is.EqualTo(12f));
        Assert.That(migratedGround.Entities, Has.Count.EqualTo(1));
        Assert.That(migratedUpper.Label, Is.EqualTo("Migrated Upper"));
        Assert.That(migratedUpper.Entities, Has.Count.EqualTo(1));
        Assert.That(newBuildingFloor.BuildingInstanceId, Is.EqualTo(2u));
        Assert.That(owner.CaptureState().Version,
            Is.EqualTo(OutsideTestFloorStateOwner.CurrentSaveVersion));
        Assert.That(new List<OutsideTestFloorRecord>(owner.FloorStates), Has.Count.EqualTo(8));
    }

    [Test]
    public void ProductionAdvancesWithoutSceneObjects()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(1, 1, new Vector2Int(5, 5), out _);
        owner.TrySetFloorState(1, 0, "Line", 4f, new Vector2(1f, 1f));

        owner.AdvanceProduction(2.5f);

        Assert.That(owner.TryGetFloorState(1, 0, out var state), Is.True);
        Assert.That(state.AccumulatedProduction, Is.EqualTo(10f).Within(0.0001f));
    }

    [Test]
    public void FixedTickSimulationAdvancesBothFloorsWithoutSceneObjects()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TrySetFloorState(2, 0, "Ground", 3f, new Vector2(1f, 1f));
        owner.TrySetFloorState(2, 1, "Upper", 7f, new Vector2(4f, 2f));
        var simulation = new FactorySimulation(owner.AdvanceProduction);

        Assert.That(simulation.Advance(0.25f), Is.EqualTo(2));
        Assert.That(owner.TryGetFloorState(2, 0, out var ground), Is.True);
        Assert.That(owner.TryGetFloorState(2, 1, out var upper), Is.True);
        Assert.That(ground.AccumulatedProduction, Is.EqualTo(0.6f).Within(0.0001f));
        Assert.That(upper.AccumulatedProduction, Is.EqualTo(1.4f).Within(0.0001f));

        Assert.That(simulation.Advance(0.04f), Is.EqualTo(0));
        Assert.That(simulation.Advance(0.01f), Is.EqualTo(1));
        Assert.That(owner.TryGetFloorState(2, 0, out ground), Is.True);
        Assert.That(owner.TryGetFloorState(2, 1, out upper), Is.True);
        Assert.That(ground.AccumulatedProduction, Is.EqualTo(0.9f).Within(0.0001f));
        Assert.That(upper.AccumulatedProduction, Is.EqualTo(2.1f).Within(0.0001f));
        Assert.That(ground.Entities, Has.Count.EqualTo(1));
        Assert.That(upper.Entities, Has.Count.EqualTo(1));
        Assert.That(ground.Entities[0].DefinitionId, Is.EqualTo("test-machine-ground"));
        Assert.That(upper.Entities[0].DefinitionId, Is.EqualTo("test-machine-upper"));
        Assert.That(ground.Entities[0].ProducedCount, Is.EqualTo(0));
        Assert.That(upper.Entities[0].ProducedCount, Is.EqualTo(0));
        Assert.That(ground.Entities[0].CycleProgress, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(upper.Entities[0].CycleProgress, Is.EqualTo(0.8f).Within(0.0001f));
    }

    [Test]
    public void EntitySnapshotRestoresIndependentFloorContents()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        Assert.That(source.TryGetFloorState(2, 1, out var sourceFloor), Is.True);
        sourceFloor.Entities[0].SetState(
            17u,
            "test-machine-upper",
            new Vector2(100f, -100f),
            2f,
            0.4f,
            5);

        var target = new OutsideTestFloorStateOwner(1);
        target.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        Assert.That(
            target.ApplySnapshot(
                2,
                1,
                sourceFloor.Label,
                sourceFloor.ProductionRate,
                sourceFloor.AccumulatedProduction,
                sourceFloor.MarkerPosition,
                sourceFloor.GetEntitySnapshots()),
            Is.True);
        Assert.That(target.TryGetFloorState(2, 1, out var targetFloor), Is.True);
        Assert.That(targetFloor.Entities, Has.Count.EqualTo(1));
        Assert.That(targetFloor.Entities[0].EntityId, Is.EqualTo(17u));
        Assert.That(targetFloor.Entities[0].DefinitionId, Is.EqualTo("test-machine-upper"));
        Assert.That(targetFloor.Entities[0].LogicalPosition, Is.EqualTo(new Vector2(4.5f, 0.5f)));
        Assert.That(targetFloor.Entities[0].ProducedCount, Is.EqualTo(5));
    }

    [Test]
    public void DifferentEntitiesAdvanceThroughIndependentCycles()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var ground);
        owner.TryGetFloorState(2, 1, out var upper);
        ground.SetEntities(new[]
        {
            new FactoryEntityRecord(
                201u,
                "ground-recipe-machine",
                new Vector2(1.5f, 1.5f),
                2f,
                0.9f,
                3)
        });
        upper.SetEntities(new[]
        {
            new FactoryEntityRecord(
                202u,
                "upper-recipe-machine",
                new Vector2(3.5f, 1.5f),
                0.75f,
                0.9f,
                4)
        });

        owner.AdvanceProduction(0.2f);

        Assert.That(ground.Entities[0].ProducedCount, Is.EqualTo(4));
        Assert.That(ground.Entities[0].CycleProgress, Is.EqualTo(0.3f).Within(0.0001f));
        Assert.That(upper.Entities[0].ProducedCount, Is.EqualTo(5));
        Assert.That(upper.Entities[0].CycleProgress, Is.EqualTo(0.05f).Within(0.0001f));
    }

    [Test]
    public void TwoFloorSaveLoadRoundTripPreservesIndependentProgress()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TrySetFloorState(
            2,
            0,
            "Ground Line",
            3f,
            new Vector2(1f, 1f));
        owner.TrySetFloorState(
            2,
            1,
            "Upper Line",
            7f,
            new Vector2(4f, 2f));
        var simulation = new FactorySimulation(owner.AdvanceProduction);
        simulation.Advance(1.25f);
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"outside-test-two-floor-{Guid.NewGuid():N}.db");

        try
        {
            Assert.That(owner.SaveToFile(path), Is.True);

            var restored = new OutsideTestFloorStateOwner(1);
            restored.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
            Assert.That(restored.LoadFromFile(path), Is.True);
            Assert.That(restored.TryGetFloorState(2, 0, out var ground), Is.True);
            Assert.That(restored.TryGetFloorState(2, 1, out var upper), Is.True);
            Assert.That(ground.Label, Is.EqualTo("Ground Line"));
            Assert.That(upper.Label, Is.EqualTo("Upper Line"));
            Assert.That(ground.MarkerPosition, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(upper.MarkerPosition, Is.EqualTo(new Vector2(4f, 2f)));
            Assert.That(ground.AccumulatedProduction, Is.EqualTo(3.6f).Within(0.0001f));
            Assert.That(upper.AccumulatedProduction, Is.EqualTo(8.4f).Within(0.0001f));
            Assert.That(ground.Entities, Has.Count.EqualTo(1));
            Assert.That(upper.Entities, Has.Count.EqualTo(1));
            Assert.That(ground.Entities[0].EntityId, Is.EqualTo(1u));
            Assert.That(upper.Entities[0].EntityId, Is.EqualTo(1u));
            Assert.That(ground.Entities[0].DefinitionId, Is.EqualTo("test-machine-ground"));
            Assert.That(upper.Entities[0].DefinitionId, Is.EqualTo("test-machine-upper"));
            Assert.That(ground.Entities[0].ProducedCount, Is.EqualTo(0));
            Assert.That(upper.Entities[0].ProducedCount, Is.EqualTo(1));
            Assert.That(ground.Entities[0].CycleProgress, Is.EqualTo(0.85f).Within(0.0001f));
            Assert.That(upper.Entities[0].CycleProgress, Is.EqualTo(0.7f).Within(0.0001f));
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
    public void SaveLoadPreservesEveryFloorAndEntityField()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        source.TryGetFloorState(2, 0, out var ground);
        source.TryGetFloorState(2, 1, out var upper);
        ground.SetState(
            "Exact Ground",
            3.25f,
            4.75f,
            new Vector2(1.5f, 1.25f));
        upper.SetState(
            "Exact Upper",
            6.5f,
            8.125f,
            new Vector2(3.5f, 1.75f));
        ground.SetEntities(new[]
        {
            new FactoryEntityRecord(
                401u,
                "ground-definition",
                new Vector2(1.25f, 1.5f),
                2.75f,
                0.375f,
                12)
        });
        upper.SetEntities(new[]
        {
            new FactoryEntityRecord(
                402u,
                "upper-definition",
                new Vector2(3.25f, 1.25f),
                5.5f,
                0.875f,
                23)
        });
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"outside-test-exact-fields-{Guid.NewGuid():N}.db");

        try
        {
            Assert.That(source.SaveToFile(path), Is.True);
            var restored = new OutsideTestFloorStateOwner(1);
            restored.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
            Assert.That(restored.LoadFromFile(path), Is.True);

            Assert.That(restored.TryGetFloorState(2, 0, out var restoredGround), Is.True);
            Assert.That(restored.TryGetFloorState(2, 1, out var restoredUpper), Is.True);
            Assert.That(restoredGround.Label, Is.EqualTo(ground.Label));
            Assert.That(restoredGround.ProductionRate, Is.EqualTo(ground.ProductionRate));
            Assert.That(restoredGround.AccumulatedProduction, Is.EqualTo(ground.AccumulatedProduction));
            Assert.That(restoredGround.MarkerPosition, Is.EqualTo(ground.MarkerPosition));
            Assert.That(restoredUpper.Label, Is.EqualTo(upper.Label));
            Assert.That(restoredUpper.ProductionRate, Is.EqualTo(upper.ProductionRate));
            Assert.That(restoredUpper.AccumulatedProduction, Is.EqualTo(upper.AccumulatedProduction));
            Assert.That(restoredUpper.MarkerPosition, Is.EqualTo(upper.MarkerPosition));
            AssertEntityFields(restoredGround.Entities[0], ground.Entities[0]);
            AssertEntityFields(restoredUpper.Entities[0], upper.Entities[0]);
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
    public void VersionThreeEmptyEntitiesRemainEmptyAfterRegistrationSaveLoadAndSnapshots()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        source.TryGetFloorState(2, 0, out var sourceGround);
        source.TryGetFloorState(2, 1, out var sourceUpper);
        sourceGround.SetEntities(Array.Empty<FactoryEntityRecord>());
        sourceUpper.SetEntities(Array.Empty<FactoryEntityRecord>());
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        Assert.That(sourceGround.Entities, Is.Empty);
        Assert.That(sourceUpper.Entities, Is.Empty);

        var restored = new OutsideTestFloorStateOwner(1);
        restored.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        Assert.That(restored.LoadState(source.CaptureState()), Is.True);
        Assert.That(restored.TryGetFloorState(2, 0, out var restoredGround), Is.True);
        Assert.That(restored.TryGetFloorState(2, 1, out var restoredUpper), Is.True);
        Assert.That(restoredGround.Entities, Is.Empty);
        Assert.That(restoredUpper.Entities, Is.Empty);

        var snapshotTarget = new OutsideTestFloorStateOwner(1);
        snapshotTarget.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        Assert.That(
            snapshotTarget.ApplySnapshot(
                2,
                0,
                restoredGround.Label,
                restoredGround.ProductionRate,
                restoredGround.AccumulatedProduction,
                restoredGround.MarkerPosition,
                restoredGround.GetEntitySnapshots()),
            Is.True);
        Assert.That(
            snapshotTarget.ApplySnapshot(
                2,
                1,
                restoredUpper.Label,
                restoredUpper.ProductionRate,
                restoredUpper.AccumulatedProduction,
                restoredUpper.MarkerPosition,
                restoredUpper.GetEntitySnapshots()),
            Is.True);
        Assert.That(snapshotTarget.TryGetFloorState(2, 0, out var snapshotGround), Is.True);
        Assert.That(snapshotTarget.TryGetFloorState(2, 1, out var snapshotUpper), Is.True);
        Assert.That(snapshotGround.Entities, Is.Empty);
        Assert.That(snapshotUpper.Entities, Is.Empty);
    }

    [Test]
    public void FailedLoadPreservesCurrentStateAndSourceFile()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TrySetFloorState(2, 0, "Before Failure", 3f, new Vector2(1f, 1f));
        var path = Path.Combine(
            Application.temporaryCachePath,
            $"outside-test-failed-load-{Guid.NewGuid():N}.db");
        var failedSource = "this is not a save";

        try
        {
            File.WriteAllText(path, failedSource);
            owner.TrySetFloorState(2, 0, "Current State", 9f, new Vector2(4f, 2f));
            owner.TryGetFloorState(2, 0, out var currentFloor);
            currentFloor.SetEntities(new[]
            {
                new FactoryEntityRecord(
                    301u,
                    "current-machine",
                    new Vector2(2.5f, 1.5f),
                    4f,
                    0.7f,
                    8)
            });
            var currentState = owner.CaptureState();

            Assert.That(owner.LoadFromFile(path), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(failedSource));
            Assert.That(StatesMatch(owner.CaptureState(), currentState), Is.True);
            Assert.That(owner.TryGetFloorState(2, 0, out var state), Is.True);
            Assert.That(state.Label, Is.EqualTo("Current State"));
            Assert.That(state.Entities[0].EntityId, Is.EqualTo(301u));
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
    public void MarkerPositionUsesRegisteredInteriorBounds()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);

        owner.TrySetFloorState(2, 0, "Line", 1f, new Vector2(100f, -100f));

        Assert.That(owner.TryGetFloorState(2, 0, out var state), Is.True);
        Assert.That(state.MarkerPosition, Is.EqualTo(new Vector2(4.5f, 0.5f)));
    }

    [Test]
    public void CompletedCyclesIncrementLifetimeAndStoredOutput()
    {
        var entity = new FactoryEntityRecord(
            1u,
            "test-machine",
            Vector2.one,
            2f,
            0.5f,
            7,
            4);

        entity.Advance(0.25f);

        Assert.That(entity.OutputCount, Is.EqualTo(5));
        Assert.That(entity.ProducedCount, Is.EqualTo(8));
        Assert.That(entity.CycleProgress, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void FullOutputStopsAtCapacityAndDiscardsUnusedTickTime()
    {
        var entity = new FactoryEntityRecord(
            1u,
            "test-machine",
            Vector2.one,
            1f,
            0f,
            10,
            FactoryEntityRecord.OutputCapacity - 1);

        entity.Advance(2.5f);

        Assert.That(entity.OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(entity.ProducedCount, Is.EqualTo(11));
        Assert.That(entity.CycleProgress, Is.EqualTo(0f).Within(0.0001f));

        Assert.That(entity.DrainOutput(), Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        entity.Advance(0.25f);
        Assert.That(entity.OutputCount, Is.Zero);
        Assert.That(entity.ProducedCount, Is.EqualTo(11));
        Assert.That(entity.CycleProgress, Is.EqualTo(0.25f).Within(0.0001f));
    }

    [Test]
    public void FullBufferPreservesFractionalProgressUntilDrained()
    {
        var entity = new FactoryEntityRecord(
            1u,
            "test-machine",
            Vector2.one,
            1f,
            0.4f,
            12,
            FactoryEntityRecord.OutputCapacity);

        entity.Advance(2f);

        Assert.That(entity.OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(entity.ProducedCount, Is.EqualTo(12));
        Assert.That(entity.CycleProgress, Is.EqualTo(0.4f).Within(0.0001f));
        Assert.That(entity.DrainOutput(), Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(entity.ProducedCount, Is.EqualTo(12));
        Assert.That(entity.CycleProgress, Is.EqualTo(0.4f).Within(0.0001f));
    }

    [Test]
    public void MissingDrainTargetLeavesStateUnchangedAndEmptyDrainSucceeds()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 1, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var floor);
        floor.Entities[0].SetState(
            17u,
            "test-machine",
            Vector2.one,
            1f,
            0.3f,
            8,
            0);
        var before = owner.CaptureState();

        Assert.That(owner.TryDrainTestMachine(2, 0, 44u, out var removed, out _), Is.False);
        Assert.That(removed, Is.Zero);
        Assert.That(StatesMatch(owner.CaptureState(), before), Is.True);
        Assert.That(owner.TryDrainTestMachine(2, 0, 17u, out removed, out _), Is.True);
        Assert.That(removed, Is.Zero);
        Assert.That(StatesMatch(owner.CaptureState(), before), Is.True);
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(8));
        Assert.That(floor.Entities[0].CycleProgress, Is.EqualTo(0.3f).Within(0.0001f));
    }

    [Test]
    public void CloneSnapshotsAndVersionFiveSaveLoadPreserveOutput()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        source.TryGetFloorState(2, 0, out var ground);
        source.TryGetFloorState(2, 1, out var upper);
        var entity = new FactoryEntityRecord(
            41u,
            "test-machine",
            new Vector2(1.5f, 1.5f),
            3f,
            0.75f,
            22,
            FactoryEntityRecord.OutputCapacity);
        ground.SetEntities(new[] { entity });
        upper.SetEntities(Array.Empty<FactoryEntityRecord>());

        Assert.That(entity.Clone().OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(FactoryEntityRecord.FromSnapshot(entity.ToSnapshot()).OutputCount,
            Is.EqualTo(FactoryEntityRecord.OutputCapacity));

        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(source.CaptureState()), Is.True);
        Assert.That(restored.LastLoadedVersion, Is.EqualTo(OutsideTestFloorStateOwner.CurrentSaveVersion));
        Assert.That(restored.TryGetFloorState(2, 0, out var restoredGround), Is.True);
        Assert.That(restored.TryGetFloorState(2, 1, out var restoredUpper), Is.True);
        Assert.That(restoredGround.Entities[0].OutputCount,
            Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(restoredGround.Entities[0].ProducedCount, Is.EqualTo(22));
        Assert.That(restoredUpper.Entities, Is.Empty);
    }

    [Test]
    public void VersionFourRuntimeBuildingRetainsTopologyAndStartsWithEmptyOutput()
    {
        var building = new BuildingRecord(
            71u,
            new Vector3Int(8, -2, 0),
            new Vector2Int(5, 4),
            2);
        var ground = new OutsideTestFloorRecord(
            71,
            0,
            "Runtime Ground",
            1f,
            0f,
            Vector2.one);
        ground.SetEntities(new[]
        {
            new FactoryEntityRecord(
                9u,
                "runtime-machine",
                Vector2.one,
                1f,
                0.6f,
                14,
                77)
        });
        var upper = new OutsideTestFloorRecord(
            71,
            1,
            "Runtime Upper",
            1f,
            0f,
            Vector2.one);
        upper.SetEntities(Array.Empty<FactoryEntityRecord>());
        var data = new OutsideTestFloorSaveData
        {
            Version = OutsideTestFloorStateOwner.BuildingRecordsSaveVersion,
            Buildings = new List<BuildingRecord> { building },
            Floors = new List<OutsideTestFloorRecord> { ground, upper }
        };

        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(data), Is.True);
        Assert.That(restored.LastLoadHadBuildingRecords, Is.True);
        Assert.That(restored.TryGetBuildingRecord(71, out var restoredBuilding), Is.True);
        Assert.That(restoredBuilding.HasSameTopology(building), Is.True);
        Assert.That(restored.TryGetFloorState(71, 0, out var restoredGround), Is.True);
        Assert.That(restored.TryGetFloorState(71, 1, out var restoredUpper), Is.True);
        Assert.That(restoredGround.Entities[0].ProducedCount, Is.EqualTo(14));
        Assert.That(restoredGround.Entities[0].OutputCount, Is.Zero);
        Assert.That(restoredUpper.Entities, Is.Empty);
    }

    [Test]
    public void OneFixedTickTransfersExactlyOneItemAndConservesBuffers()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var sourceFloor);
        owner.TryGetFloorState(2, 1, out var destinationFloor);
        sourceFloor.SetEntities(Array.Empty<FactoryEntityRecord>());
        destinationFloor.SetEntities(Array.Empty<FactoryEntityRecord>());
        owner.TryAddTestMachine(2, 0, Vector2.one, out var sourceId, out _);
        owner.TryAddTestStorage(2, 1, Vector2.one, out var destinationId, out _);
        sourceFloor.Entities[0].SetState(
            sourceId,
            "test-machine",
            Vector2.one,
            0f,
            0f,
            11,
            3);

        Assert.That(owner.TryAddConnection(
                new FactoryEntityEndpoint(2, 0, sourceId),
                new FactoryEntityEndpoint(2, 1, destinationId),
                out var error), Is.True, error);

        new FactorySimulation(owner.AdvanceProduction).Advance(0.1f);

        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(2));
        Assert.That(destinationFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(
            sourceFloor.Entities[0].OutputCount + destinationFloor.Entities[0].OutputCount,
            Is.EqualTo(3));
        Assert.That(sourceFloor.Entities[0].ProducedCount, Is.EqualTo(11));
        Assert.That(destinationFloor.Entities[0].ProducedCount, Is.Zero);
    }

    [Test]
    public void EmptyAndFullDestinationsStopAndResumeWithoutCatchUp()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var sourceFloor);
        owner.TryGetFloorState(2, 1, out var destinationFloor);
        sourceFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(1, "test-machine", Vector2.one, 0f, 0f, 4, 2)
        });
        destinationFloor.SetEntities(new[]
        {
            new FactoryEntityRecord(
                1,
                FactoryEntityRecord.StorageDefinitionId,
                Vector2.one,
                10f,
                0.8f,
                9,
                FactoryEntityRecord.OutputCapacity)
        });
        owner.TryAddConnection(
            new FactoryEntityEndpoint(2, 0, 1),
            new FactoryEntityEndpoint(2, 1, 1),
            out _);

        new FactorySimulation(owner.AdvanceProduction).Advance(0.1f);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(2));
        Assert.That(destinationFloor.Entities[0].OutputCount,
            Is.EqualTo(FactoryEntityRecord.OutputCapacity));

        destinationFloor.Entities[0].DrainOutput();
        new FactorySimulation(owner.AdvanceProduction).Advance(0.1f);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(destinationFloor.Entities[0].OutputCount, Is.EqualTo(1));
        Assert.That(destinationFloor.Entities[0].ProducedCount, Is.Zero);
    }

    [Test]
    public void ConnectionValidationAndEndpointRemovalLeaveStateUnchangedOrClean()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        owner.TryGetFloorState(2, 0, out var ground);
        owner.TryGetFloorState(2, 1, out var upper);
        ground.SetEntities(new[]
        {
            new FactoryEntityRecord(1, "test-machine", Vector2.one, 0f, 0f, 2, 5)
        });
        upper.SetEntities(new[]
        {
            new FactoryEntityRecord(9, FactoryEntityRecord.StorageDefinitionId, Vector2.one, 0f, 0f, 0, 4)
        });
        var source = new FactoryEntityEndpoint(2, 0, 1);
        var destination = new FactoryEntityEndpoint(2, 1, 9);
        var beforeInvalid = owner.CaptureState();

        Assert.That(owner.TryAddConnection(
                new FactoryEntityEndpoint(3, 0, 7),
                destination,
                out _), Is.False);
        Assert.That(owner.TryAddConnection(
                source,
                new FactoryEntityEndpoint(2, 0, 9),
                out _), Is.False);
        Assert.That(owner.TryAddConnection(source, destination, out _), Is.True);
        var connected = owner.CaptureState();
        Assert.That(owner.TryAddConnection(source, destination, out _), Is.False);
        Assert.That(StatesMatch(owner.CaptureState(), connected), Is.True);
        Assert.That(owner.TryRemoveTestMachine(2, 0, 1, out _), Is.True);
        Assert.That(owner.Connections, Is.Empty);
        Assert.That(owner.TryAddTestMachine(2, 0, Vector2.one, out var reusedId, out _), Is.True);
        Assert.That(reusedId, Is.EqualTo(1u));
        Assert.That(StatesMatch(owner.CaptureState(), beforeInvalid), Is.False);
    }

    [Test]
    public void VersionFivePreservesOutputAndVersionSixRoundTripPreservesConnection()
    {
        var source = new OutsideTestFloorStateOwner(1);
        source.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);
        source.TryGetFloorState(2, 0, out var ground);
        source.TryGetFloorState(2, 1, out var upper);
        ground.SetEntities(new[]
        {
            new FactoryEntityRecord(41, "test-machine", Vector2.one, 0f, 0f, 22, 7)
        });
        upper.SetEntities(new[]
        {
            new FactoryEntityRecord(
                42,
                FactoryEntityRecord.StorageDefinitionId,
                Vector2.one,
                4f,
                0.5f,
                9,
                6)
        });
        source.TryAddConnection(
            new FactoryEntityEndpoint(2, 0, 41),
            new FactoryEntityEndpoint(2, 1, 42),
            out _);

        var restored = new OutsideTestFloorStateOwner(1);
        Assert.That(restored.LoadState(source.CaptureState()), Is.True);
        Assert.That(restored.Connections, Has.Count.EqualTo(1));
        Assert.That(restored.Connections[0].Source, Is.EqualTo(new FactoryEntityEndpoint(2, 0, 41)));
        Assert.That(restored.TryGetFloorState(2, 0, out var restoredGround), Is.True);
        Assert.That(restored.TryGetFloorState(2, 1, out var restoredUpper), Is.True);
        Assert.That(restoredGround.Entities[0].OutputCount, Is.EqualTo(7));
        Assert.That(restoredUpper.Entities[0].OutputCount, Is.EqualTo(6));
        Assert.That(restoredUpper.Entities[0].ProducedCount, Is.Zero);

        var versionFiveData = new OutsideTestFloorSaveData
        {
            Version = OutsideTestFloorStateOwner.OutputBuffersSaveVersion,
            Buildings = new List<BuildingRecord>
            {
                new BuildingRecord(2, Vector3Int.zero, new Vector2Int(5, 3), 2)
            },
            Floors = new List<OutsideTestFloorRecord> { ground, upper }
        };
        var migrated = new OutsideTestFloorStateOwner(1);
        Assert.That(migrated.LoadState(versionFiveData), Is.True);
        Assert.That(migrated.TryGetFloorState(2, 0, out var migratedGround), Is.True);
        Assert.That(migratedGround.Entities[0].OutputCount, Is.EqualTo(7));
        Assert.That(migrated.Connections, Is.Empty);
    }

    [Test]
    public void InvalidSavedConnectionDoesNotReplaceLiveState()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 1, new Vector2Int(5, 3), out _);
        owner.TrySetFloorState(2, 0, "Live", 3f, Vector2.one);
        var before = owner.CaptureState();
        var invalid = new OutsideTestFloorSaveData
        {
            Version = OutsideTestFloorStateOwner.CurrentSaveVersion,
            Buildings = new List<BuildingRecord>
            {
                new BuildingRecord(2, Vector3Int.zero, new Vector2Int(5, 3), 1)
            },
            Floors = new List<OutsideTestFloorRecord>
            {
                new OutsideTestFloorRecord(2, 0, "Replacement", 9f, 0f, Vector2.one)
            },
            Connections = new List<FactoryEntityConnectionRecord>
            {
                new FactoryEntityConnectionRecord(
                    new FactoryEntityEndpoint(2, 0, 999),
                    new FactoryEntityEndpoint(2, 0, 1000))
            }
        };

        Assert.That(owner.LoadState(invalid), Is.False);
        Assert.That(StatesMatch(owner.CaptureState(), before), Is.True);
    }

    private static bool StatesMatch(
        OutsideTestFloorSaveData left,
        OutsideTestFloorSaveData right)
    {
        if (left.Version != right.Version
            || left.Buildings.Count != right.Buildings.Count
            || left.Floors.Count != right.Floors.Count
            || left.Connections.Count != right.Connections.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Buildings.Count; index++)
        {
            if (!left.Buildings[index].HasSameTopology(right.Buildings[index]))
            {
                return false;
            }
        }

        for (var index = 0; index < left.Floors.Count; index++)
        {
            var leftFloor = left.Floors[index];
            var rightFloor = right.Floors[index];
            if (leftFloor.BuildingInstanceId != rightFloor.BuildingInstanceId
                || leftFloor.FloorIndex != rightFloor.FloorIndex
                || leftFloor.Label != rightFloor.Label
                || !Mathf.Approximately(leftFloor.ProductionRate, rightFloor.ProductionRate)
                || !Mathf.Approximately(
                    leftFloor.AccumulatedProduction,
                    rightFloor.AccumulatedProduction)
                || leftFloor.MarkerPosition != rightFloor.MarkerPosition
                || leftFloor.Entities.Count != rightFloor.Entities.Count)
            {
                return false;
            }

            for (var entityIndex = 0;
                entityIndex < leftFloor.Entities.Count;
                entityIndex++)
            {
                if (!EntitiesMatch(
                        leftFloor.Entities[entityIndex],
                        rightFloor.Entities[entityIndex]))
                {
                    return false;
                }
            }
        }

        for (var index = 0; index < left.Connections.Count; index++)
        {
            if (!left.Connections[index].HasSameEndpoints(right.Connections[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntitiesMatch(
        FactoryEntityRecord left,
        FactoryEntityRecord right)
    {
        return left.EntityId == right.EntityId
            && left.DefinitionId == right.DefinitionId
            && left.LogicalPosition == right.LogicalPosition
            && Mathf.Approximately(left.CycleRate, right.CycleRate)
            && Mathf.Approximately(left.CycleProgress, right.CycleProgress)
            && left.ProducedCount == right.ProducedCount
            && left.OutputCount == right.OutputCount
            && left.InputCount == right.InputCount;
    }

    private static void AssertEntityFields(
        FactoryEntityRecord actual,
        FactoryEntityRecord expected)
    {
        Assert.That(actual.EntityId, Is.EqualTo(expected.EntityId));
        Assert.That(actual.DefinitionId, Is.EqualTo(expected.DefinitionId));
        Assert.That(actual.LogicalPosition, Is.EqualTo(expected.LogicalPosition));
        Assert.That(actual.CycleRate, Is.EqualTo(expected.CycleRate));
        Assert.That(actual.CycleProgress, Is.EqualTo(expected.CycleProgress));
        Assert.That(actual.ProducedCount, Is.EqualTo(expected.ProducedCount));
        Assert.That(actual.OutputCount, Is.EqualTo(expected.OutputCount));
    }
}
