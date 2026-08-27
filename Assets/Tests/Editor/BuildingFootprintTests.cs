// Verifies rectangular building footprint enumeration and validation.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingFootprintTests
{
    [Test]
    public void GetCellsUsesTheAnchorAsTheLowerLeftCell()
    {
        var cells = new List<Vector3Int>();

        BuildingFootprint.GetCells(new Vector3Int(-2, 3), new Vector2Int(2, 3), cells);

        Assert.That(cells, Is.EqualTo(new[]
        {
            new Vector3Int(-2, 3),
            new Vector3Int(-1, 3),
            new Vector3Int(-2, 4),
            new Vector3Int(-1, 4),
            new Vector3Int(-2, 5),
            new Vector3Int(-1, 5)
        }));
    }

    [Test]
    public void GetCellsRejectsZeroAndNegativeSizes()
    {
        var cells = new List<Vector3Int> { Vector3Int.zero };

        BuildingFootprint.GetCells(Vector3Int.zero, new Vector2Int(0, 1), cells);

        Assert.That(cells, Is.Empty);
        Assert.That(BuildingFootprint.IsValid(new Vector2Int(1, -1)), Is.False);
    }

    [Test]
    public void GetVisualAnchorCellAppliesTheDefinitionOffset()
    {
        var visualAnchor = BuildingFootprint.GetVisualAnchorCell(
            new Vector3Int(-2, 1),
            new Vector2Int(3, -1));

        Assert.That(visualAnchor, Is.EqualTo(new Vector3Int(1, 0)));
    }

    [Test]
    public void InclusiveRectangleUsesEitherCornerOrder()
    {
        var firstCorner = new Vector3Int(5, 7);
        var secondCorner = new Vector3Int(2, 3);

        Assert.That(
            BuildingFootprint.GetLowerLeftAnchorCell(firstCorner, secondCorner),
            Is.EqualTo(new Vector3Int(2, 3)));
        Assert.That(
            BuildingFootprint.GetInclusiveSize(firstCorner, secondCorner),
            Is.EqualTo(new Vector2Int(4, 5)));
    }

    [Test]
    public void GetBoundaryCellsContainsEachPerimeterCellOnce()
    {
        var cells = new List<Vector3Int>();

        BuildingFootprint.GetBoundaryCells(
            new Vector3Int(2, 3),
            new Vector2Int(4, 3),
            cells);

        Assert.That(cells, Has.Count.EqualTo(10));
        Assert.That(cells, Is.Unique);
        CollectionAssert.Contains(cells, new Vector3Int(2, 3));
        CollectionAssert.Contains(cells, new Vector3Int(5, 5));
        CollectionAssert.DoesNotContain(cells, new Vector3Int(3, 4));
    }

    [Test]
    public void GetEffectiveSizeUsesDefinitionSizeForInvalidSerializedData()
    {
        var fallback = new Vector2Int(6, 6);

        Assert.That(
            BuildingFootprint.GetEffectiveSize(Vector2Int.zero, fallback),
            Is.EqualTo(fallback));
        Assert.That(
            BuildingFootprint.GetEffectiveSize(new Vector2Int(2, 3), fallback),
            Is.EqualTo(new Vector2Int(2, 3)));
    }
}
