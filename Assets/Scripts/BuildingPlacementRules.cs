// Calculates authoritative cell and edge reservations for every building placement kind.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class BuildingPlacementRules
{
    public static void GetReservation(
        BuildingInstance instance,
        BuildingDefinition definition,
        List<Vector3Int> cells,
        List<GridEdge> edges)
    {
        cells.Clear();
        edges.Clear();

        if (definition.PlacementKind == BuildingPlacementKind.WallSegment)
        {
            cells.Add(instance.AnchorCell);
            return;
        }

        var size = BuildingFootprint.GetEffectiveSize(instance.Size, definition.FootprintSize);
        BuildingFootprint.GetCells(instance.AnchorCell, size, cells);
        GetPerimeterEdges(instance.AnchorCell, size, edges);
    }

    public static void GetPerimeterEdges(
        Vector3Int anchorCell,
        Vector2Int size,
        List<GridEdge> edges)
    {
        edges.Clear();
        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        for (var x = 0; x < size.x; x++)
        {
            edges.Add(GridEdge.FromCellSide(
                anchorCell + new Vector3Int(x, 0),
                GridEdgeDirection.South));
            edges.Add(GridEdge.FromCellSide(
                anchorCell + new Vector3Int(x, size.y - 1),
                GridEdgeDirection.North));
        }

        for (var y = 0; y < size.y; y++)
        {
            edges.Add(GridEdge.FromCellSide(
                anchorCell + new Vector3Int(0, y),
                GridEdgeDirection.West));
            edges.Add(GridEdge.FromCellSide(
                anchorCell + new Vector3Int(size.x - 1, y),
                GridEdgeDirection.East));
        }
    }

    public static bool IsBuildable(
        BuildingInstance instance,
        BuildingDefinition definition,
        Tilemap ground,
        TileBase buildableTile,
        List<Vector3Int> cells)
    {
        if (definition.PlacementKind == BuildingPlacementKind.WallSegment)
        {
            return ground.GetTile(instance.AnchorCell) == buildableTile;
        }

        var size = BuildingFootprint.GetEffectiveSize(instance.Size, definition.FootprintSize);
        BuildingFootprint.GetCells(instance.AnchorCell, size, cells);
        if (cells.Count == 0)
        {
            return false;
        }

        foreach (var cell in cells)
        {
            if (ground.GetTile(cell) != buildableTile)
            {
                return false;
            }
        }

        return true;
    }
}
