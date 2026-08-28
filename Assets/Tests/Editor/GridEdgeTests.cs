// Verifies canonical wall-edge identity, adjacency, and cardinal rotation.
using NUnit.Framework;
using UnityEngine;

public sealed class GridEdgeTests
{
    [Test]
    public void OppositeCellSidesProduceTheSameCanonicalEdge()
    {
        var cell = new Vector3Int(4, -2);

        var north = GridEdge.FromCellSide(cell, GridEdgeDirection.North);
        var southOfNeighbor = GridEdge.FromCellSide(
            cell + Vector3Int.up,
            GridEdgeDirection.South);
        var east = GridEdge.FromCellSide(cell, GridEdgeDirection.East);
        var westOfNeighbor = GridEdge.FromCellSide(
            cell + Vector3Int.right,
            GridEdgeDirection.West);

        Assert.That(north, Is.EqualTo(southOfNeighbor));
        Assert.That(east, Is.EqualTo(westOfNeighbor));
    }

    [Test]
    public void ClockwiseRotationVisitsEveryDirection()
    {
        var direction = GridEdgeDirection.South;

        direction = GridEdge.RotateClockwise(direction);
        Assert.That(direction, Is.EqualTo(GridEdgeDirection.East));
        direction = GridEdge.RotateClockwise(direction);
        Assert.That(direction, Is.EqualTo(GridEdgeDirection.North));
        direction = GridEdge.RotateClockwise(direction);
        Assert.That(direction, Is.EqualTo(GridEdgeDirection.West));
        direction = GridEdge.RotateClockwise(direction);
        Assert.That(direction, Is.EqualTo(GridEdgeDirection.South));
    }
}
