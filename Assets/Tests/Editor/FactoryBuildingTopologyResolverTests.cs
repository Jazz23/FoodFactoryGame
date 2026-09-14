// Verifies persisted topology precedence and isolated authored imports.
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryBuildingTopologyResolverTests
{
    [Test]
    public void PersistedTopologyWinsWhenAuthoringIsNotImported()
    {
        var persisted = new[]
        {
            new BuildingRecord(2, new Vector3Int(3, -1), new Vector2Int(5, 3), 2)
        };
        var authored = new[]
        {
            new BuildingRecord(2, new Vector3Int(3, -1), new Vector2Int(6, 5), 3)
        };

        var resolved = FactoryBuildingTopologyResolver.TryResolveImports(
            persisted,
            authored,
            true,
            false,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            out var imports,
            out var error);

        Assert.That(resolved, Is.True, error);
        Assert.That(imports, Is.Empty);
    }

    [Test]
    public void AuthoredRecordIsImportedOnlyWhenPersistedRecordIsMissing()
    {
        var authored = new[]
        {
            new BuildingRecord(2, new Vector3Int(3, -1), new Vector2Int(6, 5), 3)
        };

        var resolved = FactoryBuildingTopologyResolver.TryResolveImports(
            System.Array.Empty<BuildingRecord>(),
            authored,
            true,
            true,
            TestBuildingCreator.DefaultDoorCornerExclusionDistance,
            out var imports,
            out var error);

        Assert.That(resolved, Is.True, error);
        Assert.That(imports.Count, Is.EqualTo(1));
        Assert.That(imports[0].FootprintSize, Is.EqualTo(new Vector2Int(6, 5)));
    }
}
