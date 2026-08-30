// Verifies atomic reservation and release of complete building footprints.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class BuildingOccupancyTests
{
    [Test]
    public void TryReserveMapsEveryFootprintCellToOneBuilding()
    {
        var occupancy = new BuildingOccupancy();
        var cells = new List<Vector3Int>();
        BuildingFootprint.GetCells(new Vector3Int(4, -1), new Vector2Int(2, 2), cells);

        var reserved = occupancy.TryReserve(7, cells);

        Assert.That(reserved, Is.True);
        foreach (var cell in cells)
        {
            Assert.That(occupancy.TryGetBuildingId(cell, out var buildingId), Is.True);
            Assert.That(buildingId, Is.EqualTo(7));
        }
    }

    [Test]
    public void TryReserveRejectsOverlapsWithoutPartiallyReservingCells()
    {
        var occupancy = new BuildingOccupancy();
        var first = new List<Vector3Int>();
        var overlapping = new List<Vector3Int>();
        BuildingFootprint.GetCells(Vector3Int.zero, new Vector2Int(2, 2), first);
        BuildingFootprint.GetCells(new Vector3Int(1, 1), new Vector2Int(2, 2), overlapping);
        occupancy.TryReserve(1, first);

        var reserved = occupancy.TryReserve(2, overlapping);

        Assert.That(reserved, Is.False);
        Assert.That(occupancy.IsOccupied(new Vector3Int(2, 2)), Is.False);
        Assert.That(occupancy.TryGetBuildingId(new Vector3Int(1, 1), out var buildingId), Is.True);
        Assert.That(buildingId, Is.EqualTo(1));
    }

    [Test]
    public void TryReserveRejectsDuplicateCells()
    {
        var occupancy = new BuildingOccupancy();
        var cells = new List<Vector3Int> { Vector3Int.zero, Vector3Int.zero };

        var reserved = occupancy.TryReserve(1, cells);

        Assert.That(reserved, Is.False);
        Assert.That(occupancy.IsOccupied(Vector3Int.zero), Is.False);
    }

    [Test]
    public void ReleaseFreesTheEntireBuildingFootprint()
    {
        var occupancy = new BuildingOccupancy();
        var cells = new List<Vector3Int>();
        BuildingFootprint.GetCells(Vector3Int.zero, new Vector2Int(3, 1), cells);
        occupancy.TryReserve(3, cells);

        var released = occupancy.Release(3);

        Assert.That(released, Is.True);
        foreach (var cell in cells)
        {
            Assert.That(occupancy.IsOccupied(cell), Is.False);
        }
    }

    [Test]
    public void DifferentEdgesAroundOneCellCanBeReserved()
    {
        var occupancy = new BuildingOccupancy();
        var noCells = System.Array.Empty<Vector3Int>();
        var south = GridEdge.FromCellSide(Vector3Int.zero, GridEdgeDirection.South);
        var east = GridEdge.FromCellSide(Vector3Int.zero, GridEdgeDirection.East);

        var reservedSouth = occupancy.TryReserve(1, noCells, new[] { south });
        var reservedEast = occupancy.TryReserve(2, noCells, new[] { east });

        Assert.That(reservedSouth, Is.True);
        Assert.That(reservedEast, Is.True);
        Assert.That(occupancy.TryGetBuildingId(south, out var southId), Is.True);
        Assert.That(southId, Is.EqualTo(1));
        Assert.That(occupancy.TryGetBuildingId(east, out var eastId), Is.True);
        Assert.That(eastId, Is.EqualTo(2));
    }

    [Test]
    public void OppositeDescriptionsOfOneEdgeConflict()
    {
        var occupancy = new BuildingOccupancy();
        var noCells = System.Array.Empty<Vector3Int>();
        var north = GridEdge.FromCellSide(Vector3Int.zero, GridEdgeDirection.North);
        var southOfNeighbor = GridEdge.FromCellSide(Vector3Int.up, GridEdgeDirection.South);

        occupancy.TryReserve(1, noCells, new[] { north });

        Assert.That(occupancy.TryReserve(2, noCells, new[] { southOfNeighbor }), Is.False);
    }

    [Test]
    public void CellAndPerimeterEdgeAdjacencyIsPlacementOrderIndependent()
    {
        var wallCell = new[] { Vector3Int.up };
        var areaCell = new[] { Vector3Int.zero };
        var perimeterEdge = new[]
        {
            GridEdge.FromCellSide(Vector3Int.zero, GridEdgeDirection.North)
        };
        var wallFirst = new BuildingOccupancy();
        var areaFirst = new BuildingOccupancy();

        Assert.That(wallFirst.TryReserve(1, wallCell), Is.True);
        Assert.That(wallFirst.TryReserve(2, areaCell, perimeterEdge), Is.True);

        Assert.That(areaFirst.TryReserve(1, areaCell, perimeterEdge), Is.True);
        Assert.That(areaFirst.TryReserve(2, wallCell), Is.True);
    }

    [Test]
    public void AreaPerimeterEdgePreventsDuplicateEdgeReservation()
    {
        var occupancy = new BuildingOccupancy();
        var cells = new List<Vector3Int>();
        var edges = new List<GridEdge>();
        BuildingFootprint.GetCells(Vector3Int.zero, new Vector2Int(2, 2), cells);
        BuildingPlacementRules.GetPerimeterEdges(
            Vector3Int.zero,
            new Vector2Int(2, 2),
            edges);
        occupancy.TryReserve(1, cells, edges);
        var south = GridEdge.FromCellSide(Vector3Int.zero, GridEdgeDirection.South);

        var reserved = occupancy.TryReserve(
            2,
            System.Array.Empty<Vector3Int>(),
            new[] { south });

        Assert.That(reserved, Is.False);
    }
}
