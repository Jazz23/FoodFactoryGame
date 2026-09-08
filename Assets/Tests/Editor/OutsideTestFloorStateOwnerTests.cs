// Verifies independent OutsideTest floor identity, registration, migration, and simulation.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class OutsideTestFloorStateOwnerTests
{
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

        Assert.That(owner.LoadFromJson(JsonUtility.ToJson(legacyData)), Is.True);
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
        Assert.That(JsonUtility.FromJson<OutsideTestFloorSaveData>(owner.ToJson()).Version,
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
            $"outside-test-two-floor-{Guid.NewGuid():N}.json");

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
            $"outside-test-exact-fields-{Guid.NewGuid():N}.json");

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
        Assert.That(restored.LoadFromJson(source.ToJson()), Is.True);
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
            $"outside-test-failed-load-{Guid.NewGuid():N}.json");
        var failedSource = "this is not a save";

        try
        {
            File.WriteAllText(path, failedSource);
            owner.TrySetFloorState(2, 0, "Current State", 9f, new Vector2(4f, 2f));
            owner.TryGetFloorState(2, 0, out var currentState);
            currentState.SetEntities(new[]
            {
                new FactoryEntityRecord(
                    301u,
                    "current-machine",
                    new Vector2(2.5f, 1.5f),
                    4f,
                    0.7f,
                    8)
            });
            var currentJson = owner.ToJson();

            Assert.That(owner.LoadFromFile(path), Is.False);
            Assert.That(File.ReadAllText(path), Is.EqualTo(failedSource));
            Assert.That(owner.ToJson(), Is.EqualTo(currentJson));
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
    }
}
