// Verifies independent OutsideTest floor identity, registration, migration, and simulation.
using System.Collections.Generic;
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
        Assert.That(migratedUpper.Label, Is.EqualTo("Migrated Upper"));
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
    public void MarkerPositionUsesRegisteredInteriorBounds()
    {
        var owner = new OutsideTestFloorStateOwner(1);
        owner.TryRegisterBuilding(2, 2, new Vector2Int(5, 3), out _);

        owner.TrySetFloorState(2, 0, "Line", 1f, new Vector2(100f, -100f));

        Assert.That(owner.TryGetFloorState(2, 0, out var state), Is.True);
        Assert.That(state.MarkerPosition, Is.EqualTo(new Vector2(4.5f, 0.5f)));
    }
}
