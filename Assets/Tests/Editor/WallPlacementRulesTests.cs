// Verifies centered walls reserve and validate exactly their containing grid cell.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class WallPlacementRulesTests
{
    [Test]
    public void WallReservationContainsOnlyItsAnchorCell()
    {
        var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
            "Assets/Buildings/WallBuilding.asset");
        var instance = new BuildingInstance(
            1,
            definition.Id,
            new Vector3Int(4, -3),
            Vector2Int.one,
            -1,
            GridEdgeDirection.South,
            WallCellShape.CornerNorthEast);
        var cells = new List<Vector3Int>();
        var edges = new List<GridEdge>();

        BuildingPlacementRules.GetReservation(instance, definition, cells, edges);

        Assert.That(cells, Is.EqualTo(new[] { instance.AnchorCell }));
        Assert.That(edges, Is.Empty);
    }

    [Test]
    public void DifferentWallShapesCannotReserveTheSameCell()
    {
        var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
            "Assets/Buildings/WallBuilding.asset");
        var cells = new List<Vector3Int>();
        var edges = new List<GridEdge>();
        var occupancy = new BuildingOccupancy();
        var first = new BuildingInstance(
            1,
            definition.Id,
            Vector3Int.zero,
            Vector2Int.one,
            -1,
            GridEdgeDirection.South,
            WallCellShape.Horizontal);
        var second = new BuildingInstance(
            2,
            definition.Id,
            Vector3Int.zero,
            Vector2Int.one,
            -1,
            GridEdgeDirection.East,
            WallCellShape.Vertical);

        BuildingPlacementRules.GetReservation(first, definition, cells, edges);
        Assert.That(occupancy.TryReserve(first.Id, cells, edges), Is.True);
        BuildingPlacementRules.GetReservation(second, definition, cells, edges);

        Assert.That(occupancy.TryReserve(second.Id, cells, edges), Is.False);
    }

    [Test]
    public void WallBuildabilityUsesItsAnchorCellOnly()
    {
        var gridObject = new GameObject("Grid");
        var tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            var grid = gridObject.AddComponent<Grid>();
            var tilemapObject = new GameObject("Ground");
            tilemapObject.transform.SetParent(gridObject.transform, false);
            var ground = tilemapObject.AddComponent<Tilemap>();
            tilemapObject.AddComponent<TilemapRenderer>();
            var definition = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(
                "Assets/Buildings/WallBuilding.asset");
            var instance = new BuildingInstance(
                1,
                definition.Id,
                Vector3Int.zero,
                Vector2Int.one,
                -1);
            var cells = new List<Vector3Int>();
            ground.SetTile(Vector3Int.right, tile);

            Assert.That(
                BuildingPlacementRules.IsBuildable(instance, definition, ground, tile, cells),
                Is.False);

            ground.SetTile(Vector3Int.zero, tile);

            Assert.That(
                BuildingPlacementRules.IsBuildable(instance, definition, ground, tile, cells),
                Is.True);
        }
        finally
        {
            Object.DestroyImmediate(tile);
            Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void PlacementRotationVisitsBothStraightsAndEveryCorner()
    {
        var shape = WallCellShape.Horizontal;

        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.Vertical));
        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.CornerNorthEast));
        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.CornerSouthEast));
        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.CornerSouthWest));
        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.CornerNorthWest));
        shape = WallCellGeometry.GetNextPlacementShape(shape);
        Assert.That(shape, Is.EqualTo(WallCellShape.Horizontal));
    }
}
