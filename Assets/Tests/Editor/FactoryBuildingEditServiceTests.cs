// Verifies explicit topology updates preserve schema-9 building and entity state.
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryBuildingEditServiceTests
{
    [Test]
    public void ExplicitResizeUpdatesFootprintAndPreservesEntities()
    {
        var buildingGuid = Guid.NewGuid();
        var floorZeroGuid = Guid.NewGuid();
        var floorOneGuid = Guid.NewGuid();
        var entityZeroGuid = Guid.NewGuid();
        var entityOneGuid = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            buildingGuid,
            floorZeroGuid,
            floorOneGuid,
            entityZeroGuid,
            entityOneGuid,
            new Vector2Int(5, 3));
        var authored = new List<BuildingRecord>
        {
            new(2, new Vector3Int(3, -1, 0), new Vector2Int(6, 5), 2)
        };

        var applied = FactoryBuildingEditService.TryApplyTopologyChanges(
            snapshot,
            authored,
            new[] { 2u },
            0.15f,
            out var updated,
            out var result,
            out var error);

        Assert.That(applied, Is.True, error);
        Assert.That(updated.Buildings[0].FootprintSize, Is.EqualTo(new Vector2Int(6, 5)));
        Assert.That(updated.Buildings[0].StoryCount, Is.EqualTo(2));
        Assert.That(updated.Floors.Count, Is.EqualTo(2));
        Assert.That(updated.Floors[0].Entities.Count, Is.EqualTo(1));
        Assert.That(updated.Floors[1].Entities.Count, Is.EqualTo(1));
        Assert.That(updated.Floors[0].Entities[0].Guid, Is.EqualTo(entityZeroGuid));
        Assert.That(updated.Floors[1].Entities[0].Guid, Is.EqualTo(entityOneGuid));
        Assert.That(result.AppliedBuildingIds, Is.EqualTo(new[] { 2u }));
        Assert.That(result.PreservedEntityCount, Is.EqualTo(2));
        Assert.That(result.RecoveryEntityIds, Is.Empty);
    }

    [Test]
    public void UnsafeStoryReductionRejectsWithoutChangingSnapshot()
    {
        var buildingGuid = Guid.NewGuid();
        var floorZeroGuid = Guid.NewGuid();
        var floorOneGuid = Guid.NewGuid();
        var entityZeroGuid = Guid.NewGuid();
        var entityOneGuid = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            buildingGuid,
            floorZeroGuid,
            floorOneGuid,
            entityZeroGuid,
            entityOneGuid,
            new Vector2Int(5, 3));
        snapshot.Floors[0].SetEntities(new[]
        {
            CreateEntity(floorZeroGuid, entityZeroGuid, 1, new Vector2(0.5f, 0.5f)),
            CreateEntity(floorZeroGuid, Guid.NewGuid(), 3, new Vector2(1.5f, 0.5f)),
            CreateEntity(floorZeroGuid, Guid.NewGuid(), 4, new Vector2(2.5f, 0.5f))
        });
        var authored = new List<BuildingRecord>
        {
            new(2, new Vector3Int(3, -1, 0), new Vector2Int(5, 3), 1)
        };

        var applied = FactoryBuildingEditService.TryApplyTopologyChanges(
            snapshot,
            authored,
            new[] { 2u },
            0.15f,
            out var updated,
            out var result,
            out var error);

        Assert.That(applied, Is.False);
        Assert.That(error, Does.Contain("cannot retain entity"));
        Assert.That(result.Changed, Is.False);
        Assert.That(updated.Buildings[0].FootprintSize, Is.EqualTo(new Vector2Int(5, 3)));
        Assert.That(updated.Floors.Count, Is.EqualTo(2));
        Assert.That(updated.Floors[1].Entities[0].Guid, Is.EqualTo(entityOneGuid));
    }

    [Test]
    public void DeletingBuildingPurgesItsFloorsAndEntities()
    {
        var buildingGuid = Guid.NewGuid();
        var floorGuid = Guid.NewGuid();
        var entityGuid = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            buildingGuid,
            floorGuid,
            Guid.NewGuid(),
            entityGuid,
            Guid.NewGuid(),
            new Vector2Int(5, 3));
        var applied = FactoryBuildingEditService.TryApplyTopologyChanges(
            snapshot,
            Array.Empty<BuildingRecord>(),
            new[] { 2u },
            0.15f,
            true,
            out var updated,
            out var result,
            out var error);

        Assert.That(applied, Is.True, error);
        Assert.That(updated.Buildings, Is.Empty);
        Assert.That(updated.Floors, Is.Empty);
        Assert.That(result.DeletedBuildingIds, Is.EqualTo(new[] { 2u }));
        Assert.That(result.DeletedFloorGuids, Does.Contain(floorGuid));
        Assert.That(result.DeletedEntityGuids, Does.Contain(entityGuid));
    }

    [Test]
    public void CreatingBuildingAddsDefaultFloorsAndPreservesExistingState()
    {
        var buildingGuid = Guid.NewGuid();
        var snapshot = CreateSnapshot(
            buildingGuid,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Vector2Int(5, 3));
        var authored = new List<BuildingRecord>
        {
            new(2, new Vector3Int(3, -1), new Vector2Int(5, 3), 2),
            new(3, new Vector3Int(12, 4), new Vector2Int(6, 5), 2)
        };

        var applied = FactoryBuildingEditService.TryApplyTopologyChanges(
            snapshot,
            authored,
            new[] { 3u },
            0.15f,
            out var updated,
            out var result,
            out var error);

        Assert.That(applied, Is.True, error);
        Assert.That(updated.Buildings, Has.Count.EqualTo(2));
        Assert.That(updated.Floors, Has.Count.EqualTo(4));
        Assert.That(updated.Buildings.Find(building => building.LegacyBuildingId == 3).Guid,
            Is.EqualTo(FactoryGuidMigration.ForBuilding(3)));
        Assert.That(
            updated.Floors.FindAll(floor => floor.BuildingGuid == FactoryGuidMigration.ForBuilding(3))
                .TrueForAll(floor => floor.Entities.Count > 0),
            Is.True);
        Assert.That(result.AppliedBuildingIds, Is.EqualTo(new[] { 3u }));
        Assert.That(result.CreatedBuildingGuids, Has.Count.EqualTo(1));
        Assert.That(result.CreatedFloorGuids, Has.Count.EqualTo(2));
        Assert.That(result.CreatedEntityGuids, Has.Count.EqualTo(2));
        Assert.That(result.PreservedEntityCount, Is.EqualTo(2));
    }

    [Test]
    public void DefaultDatabasePathIsProjectLocalInEditor()
    {
        var expected = Path.Combine(
            FactoryWorldPaths.GetProjectRoot(),
            FactoryWorldSqliteStore.DatabaseFileName);

        Assert.That(
            Path.GetFullPath(FactoryWorldPaths.GetDefaultDatabasePath()),
            Is.EqualTo(Path.GetFullPath(expected)));
    }

    [Test]
    public void StaleAuthoredFingerprintBlocksExactPlanApplication()
    {
        var path = Path.Combine(Application.temporaryCachePath, "factory-topology-stale-" + Guid.NewGuid() + ".db");
        var snapshot = CreateSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Vector2Int(5, 3));
        var authored = new List<BuildingRecord>
        {
            new(2, new Vector3Int(3, -1), new Vector2Int(6, 5), 2)
        };
        var store = new FactoryWorldSqliteStore(path);
        store.Save(snapshot);

        try
        {
            Assert.That(
                FactoryBuildingEditService.TryCreateTopologyPlan(
                    path,
                    authored,
                    0.15f,
                    out var plan,
                    out var planError),
                Is.True,
                planError);
            authored[0].SetTopology(2, new Vector3Int(3, -1), new Vector2Int(7, 5), 2);

            var applied = FactoryBuildingEditService.TryApplyTopologyPlan(
                path,
                plan,
                authored,
                new[] { 2u },
                false,
                0.15f,
                out _,
                out var error);

            Assert.That(applied, Is.False);
            Assert.That(error, Does.Contain("authored layout changed"));

            var originalAuthored = new List<BuildingRecord>
            {
                new(2, new Vector3Int(3, -1), new Vector2Int(6, 5), 2)
            };
            store.Save(snapshot);
            var changedSnapshot = snapshot.Clone();
            changedSnapshot.Floors[0].SetState("Changed", 1f, 0.25f, new Vector2(0.5f, 0.5f));
            store.Save(changedSnapshot);
            applied = FactoryBuildingEditService.TryApplyTopologyPlan(
                path,
                plan,
                originalAuthored,
                new[] { 2u },
                false,
                0.15f,
                out _,
                out error);

            Assert.That(applied, Is.False);
            Assert.That(error, Does.Contain("database changed"));
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

    private static FactoryWorldSnapshot CreateSnapshot(
        Guid buildingGuid,
        Guid floorZeroGuid,
        Guid floorOneGuid,
        Guid entityZeroGuid,
        Guid entityOneGuid,
        Vector2Int footprintSize)
    {
        var snapshot = new FactoryWorldSnapshot();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            new Vector3Int(3, -1, 0),
            footprintSize,
            2,
            false,
            2));
        snapshot.Floors.Add(CreateFloor(
            floorZeroGuid,
            buildingGuid,
            0,
            entityZeroGuid,
            new Vector2(2.5f, 0.5f)));
        snapshot.Floors.Add(CreateFloor(
            floorOneGuid,
            buildingGuid,
            1,
            entityOneGuid,
            new Vector2(0.5f, 0.5f)));
        return snapshot;
    }

    private static FactoryWorldFloorRecord CreateFloor(
        Guid floorGuid,
        Guid buildingGuid,
        int floorIndex,
        Guid entityGuid,
        Vector2 entityPosition)
    {
        var floor = new FactoryWorldFloorRecord(
            floorGuid,
            buildingGuid,
            floorIndex,
            $"Floor {floorIndex}",
            1f,
            0.25f,
            new Vector2(0.5f, 0.5f));
        floor.SetEntities(new[]
        {
            CreateEntity(floorGuid, entityGuid, (uint)(floorIndex + 1), entityPosition)
        });
        return floor;
    }

    private static FactoryWorldEntityRecord CreateEntity(
        Guid floorGuid,
        Guid entityGuid,
        uint entityId,
        Vector2 entityPosition)
    {
        return new FactoryWorldEntityRecord(
            entityGuid,
            floorGuid,
            "test-machine",
            entityPosition,
            0f,
            Vector2.one,
            new byte[] { 1, 2, 3 },
            entityId,
            false,
            2f,
            0.5f,
            7,
            5,
            3);
    }
}
