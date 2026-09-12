// Verifies exterior dock placement mapping, shared inventory behavior, and deterministic truck grid paths.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class FactoryDockTests
{
    [Test]
    public void ExteriorPlacementMapsToTheMatchingInteriorPerimeterCell()
    {
        var building = new BuildingRecord(
            7,
            new Vector3Int(10, 20, 0),
            new Vector2Int(6, 4),
            1);

        Assert.That(
            FactoryDock.TryFindExteriorPlacement(
                new[] { building },
                new Vector2(12.5f, 19.5f),
                out var resolvedBuilding,
                out var interiorPosition,
                out var direction),
            Is.True);
        Assert.That(resolvedBuilding.BuildingInstanceId, Is.EqualTo(building.BuildingInstanceId));
        Assert.That(interiorPosition, Is.EqualTo(new Vector2(1.5f, 0.5f)));
        Assert.That(direction, Is.EqualTo(GridEdgeDirection.South));
        Assert.That(
            FactoryDock.TryGetExteriorApproachCell(
                building,
                interiorPosition,
                direction,
                out var exteriorCell),
            Is.True);
        Assert.That(exteriorCell, Is.EqualTo(new Vector2Int(12, 19)));
    }

    [Test]
    public void ShippingDockStoresItemsInItsInteriorEntityInventory()
    {
        var dock = new FactoryEntityRecord(
            1,
            FactoryEntityDefinitions.ShippingDockDefinitionId,
            new Vector2(0.5f, 0.5f),
            0f,
            0f,
            0,
            0,
            0,
            null,
            GridEdgeDirection.West);

        Assert.That(dock.IsShippingDock, Is.True);
        Assert.That(dock.TryAcceptItem(FactoryEntityDefinitions.TestProductId, 12), Is.EqualTo(12));
        Assert.That(dock.InventoryCount, Is.EqualTo(12));
        Assert.That(dock.DockDirection, Is.EqualTo(GridEdgeDirection.West));
    }

    [Test]
    public void TruckPathUsesCardinalCellsAndAvoidsAllBuildingFootprints()
    {
        var buildings = new List<BuildingRecord>
        {
            new(1, new Vector3Int(0, 0, 0), new Vector2Int(6, 4), 1),
            new(2, new Vector3Int(8, -2, 0), new Vector2Int(5, 6), 1),
            new(3, new Vector3Int(16, 0, 0), new Vector2Int(6, 4), 1)
        };
        var path = new List<Vector2Int>();
        var repeatedPath = new List<Vector2Int>();
        var start = new Vector2Int(2, -1);
        var destination = new Vector2Int(18, -1);

        Assert.That(FactoryTruckGridPath.TryBuild(buildings, start, destination, path), Is.True);
        Assert.That(FactoryTruckGridPath.TryBuild(buildings, start, destination, repeatedPath), Is.True);
        Assert.That(path, Is.EqualTo(repeatedPath));
        Assert.That(path[0], Is.EqualTo(start));
        Assert.That(path[^1], Is.EqualTo(destination));
        for (var index = 1; index < path.Count; index++)
        {
            Assert.That((path[index] - path[index - 1]).sqrMagnitude, Is.EqualTo(1));
        }

        foreach (var building in buildings)
        {
            var occupied = new List<Vector3Int>();
            BuildingFootprint.GetCells(building.AnchorCell, building.FootprintSize, occupied);
            foreach (var cell in occupied)
            {
                Assert.That(path.Contains(new Vector2Int(cell.x, cell.y)), Is.False);
            }
        }
    }
}
