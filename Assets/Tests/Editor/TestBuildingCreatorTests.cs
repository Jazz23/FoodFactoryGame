// Verifies the selected building perimeter and roof bounds used by TestBuildingCreator.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class TestBuildingCreatorTests
{
    [Test]
    public void WallRingMatchesTheSelectedCellsAsItsFootprint()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            new Vector3Int(3, 5),
            new Vector3Int(1, 4),
            placements);

        Assert.That(placements, Has.Count.EqualTo(6));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Horizontal),
            Has.Count.EqualTo(2));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Vertical),
            Has.Count.EqualTo(0));
        Assert.That(
            placements.FindAll(placement => placement.Kind is GridWall.WallKind.CornerNorthWest
                or GridWall.WallKind.CornerNorthEast
                or GridWall.WallKind.CornerSouthWest
                or GridWall.WallKind.CornerSouthEast),
            Has.Count.EqualTo(4));
        Assert.That(
            placements.Exists(placement => placement.Kind == GridWall.WallKind.Horizontal
                && placement.Cell == new Vector2Int(2, 4)),
            Is.True);
        Assert.That(
            placements.Exists(placement => placement.Kind == GridWall.WallKind.CornerSouthWest
                && placement.Cell == new Vector2Int(1, 4)),
            Is.True);
    }

    [Test]
    public void RoofBoundsMatchTheSelectedCells()
    {
        var first = new Vector3Int(-2, 4);
        var second = new Vector3Int(1, 5);

        Assert.That(
            TestBuildingCreator.GetRoofLogicalMin(first, second),
            Is.EqualTo(new Vector2(-1.75f, 4.25f)));
        Assert.That(
            TestBuildingCreator.GetRoofLogicalMax(first, second),
            Is.EqualTo(new Vector2(1.75f, 5.75f)));
    }

    [Test]
    public void SingleCellSelectionCreatesFourCornerWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(Vector3Int.zero, Vector3Int.zero, placements);

        Assert.That(placements, Has.Count.EqualTo(4));
        Assert.That(
            placements.FindAll(placement => placement.Kind is GridWall.WallKind.CornerNorthWest
                or GridWall.WallKind.CornerNorthEast
                or GridWall.WallKind.CornerSouthWest
                or GridWall.WallKind.CornerSouthEast),
            Has.Count.EqualTo(4));
        Assert.That(placements.ConvertAll(placement => placement.Cell), Has.All.EqualTo(Vector2Int.zero));
    }

    [Test]
    public void ThreeByThreeSelectionCreatesOnlyItsPerimeterWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            Vector3Int.zero,
            new Vector3Int(2, 2),
            placements);

        Assert.That(placements, Has.Count.EqualTo(8));
        foreach (var placement in placements)
        {
            Assert.That(placement.Cell.x, Is.InRange(0, 2));
            Assert.That(placement.Cell.y, Is.InRange(0, 2));
        }
    }

    [Test]
    public void OneCellWideSelectionsDoNotDuplicateStraightWallPieces()
    {
        var placements = new List<TestBuildingCreator.WallPlacement>();

        TestBuildingCreator.GetWallPlacements(
            Vector3Int.zero,
            new Vector3Int(2, 0),
            placements);

        Assert.That(placements, Has.Count.EqualTo(5));
        Assert.That(
            placements.FindAll(placement => placement.Kind == GridWall.WallKind.Horizontal),
            Has.Count.EqualTo(1));
    }

    [Test]
    public void RoofSortingFollowsTheBuildingDepth()
    {
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                Vector3Int.zero,
                Vector3Int.zero,
                20),
            Is.EqualTo(20));
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                new Vector3Int(4, -2),
                new Vector3Int(5, 0),
                20),
            Is.EqualTo(0));
        Assert.That(
            TestBuildingCreator.GetRoofSortingOrder(
                new Vector3Int(-4, 2),
                new Vector3Int(-2, 3),
                20),
            Is.EqualTo(40));
    }

    [Test]
    public void RoofClearanceKeepsTheSlabAboveTheWallTop()
    {
        var wallHeight = 2f;
        var roofThickness = 0.1f;

        Assert.That(
            TestBuildingCreator.GetRoofTopHeight(wallHeight, 2f, roofThickness),
            Is.EqualTo(2.1f));
        Assert.That(
            TestBuildingCreator.GetRoofTopHeight(wallHeight, 2.5f, roofThickness),
            Is.EqualTo(2.5f));
    }
}
