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
}
