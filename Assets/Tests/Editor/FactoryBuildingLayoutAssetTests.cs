// Verifies compact building-layout serialization is ordered, cloned, and validated independently of shell geometry.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryBuildingLayoutAssetTests
{
    [Test]
    public void ReplaceRecordsSortsByStableBuildingIdentityAndClonesTopology()
    {
        var asset = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
        var first = new BuildingRecord(2, new Vector3Int(4, 5), new Vector2Int(5, 5), 1);
        var second = new BuildingRecord(1, new Vector3Int(-2, 3), new Vector2Int(5, 5), 2);
        try
        {
            asset.ReplaceRecords(new[] { first, second });
            first.SetTopology(99, new Vector3Int(20, 20), new Vector2Int(3, 3), 1);

            Assert.That(asset.Count, Is.EqualTo(2));
            Assert.That(asset.BuildingRecords[0].BuildingInstanceId, Is.EqualTo(1));
            Assert.That(asset.BuildingRecords[1].BuildingInstanceId, Is.EqualTo(2));
            Assert.That(asset.BuildingRecords[0].StoryCount, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void HasSameRecordsIgnoresInputOrderButDetectsTopologyChanges()
    {
        var asset = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
        var records = new List<BuildingRecord>
        {
            new(3, new Vector3Int(0, 0), new Vector2Int(5, 5), 1),
            new(4, new Vector3Int(8, 0), new Vector2Int(6, 4), 2)
        };
        try
        {
            asset.ReplaceRecords(records);

            Assert.That(asset.HasSameRecords(new[] { records[1], records[0] }), Is.True);
            records[1].SetTopology(4, new Vector3Int(8, 0), new Vector2Int(7, 4), 2);
            Assert.That(asset.HasSameRecords(records), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void DuplicateBuildingIdentitiesAreRejectedByAssetValidation()
    {
        var asset = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
        try
        {
            asset.ReplaceRecords(new[]
            {
                new BuildingRecord(5, new Vector3Int(0, 0), new Vector2Int(5, 5), 1),
                new BuildingRecord(5, new Vector3Int(8, 0), new Vector2Int(5, 5), 1)
            });

            Assert.That(
                asset.TryValidate(TestBuildingCreator.DefaultDoorCornerExclusionDistance, out var error),
                Is.False);
            Assert.That(error, Does.Contain("Duplicate building ID"));
        }
        finally
        {
            Object.DestroyImmediate(asset);
        }
    }

    [Test]
    public void RecordMutationsAreClonedAndKeepStableOrdering()
    {
        var asset = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
        var first = new BuildingRecord(2, new Vector3Int(2, 2), new Vector2Int(5, 5), 1);
        var second = new BuildingRecord(1, new Vector3Int(0, 0), new Vector2Int(4, 4), 2);
        try
        {
            Assert.That(asset.TryAddRecord(first, out var addError), Is.True, addError);
            Assert.That(asset.TryUpdateRecord(second, out var missingError), Is.False);
            Assert.That(missingError, Does.Contain("was not found"));
            Assert.That(asset.TryAddRecord(second, out addError), Is.True, addError);
            Assert.That(asset.TryRemoveRecord(2, out var removeError), Is.True, removeError);
            Assert.That(asset.Count, Is.EqualTo(1));
            Assert.That(asset.BuildingRecords[0].BuildingInstanceId, Is.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(asset);
        }
    }
}
