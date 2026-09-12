// Enumerates rectangular grid footprints and their stable entrance geometry.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class BuildingFootprint
{
    public static bool IsValid(Vector2Int size)
    {
        return size.x > 0 && size.y > 0;
    }

    public static Vector2Int GetEffectiveSize(Vector2Int size, Vector2Int fallback)
    {
        return IsValid(size) ? size : fallback;
    }

    public static Vector2Int GetUsableInteriorSize(Vector2Int footprintSize)
    {
        return new Vector2Int(
            Mathf.Max(0, footprintSize.x - 2),
            Mathf.Max(0, footprintSize.y - 2));
    }

    public static Vector2Int GetInteriorOriginOffset(Vector2Int footprintSize)
    {
        return IsValid(GetUsableInteriorSize(footprintSize))
            ? Vector2Int.one
            : Vector2Int.zero;
    }

    public static Vector2 ExteriorLocalToInteriorLocal(Vector2 exteriorLocalPosition)
    {
        return exteriorLocalPosition - Vector2.one;
    }

    public static Vector2 InteriorLocalToExteriorLocal(Vector2 interiorLocalPosition)
    {
        return interiorLocalPosition + Vector2.one;
    }

    public static bool IsUsableInteriorPosition(Vector2 position, Vector2Int interiorSize)
    {
        return IsValid(interiorSize)
            && !float.IsNaN(position.x)
            && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y)
            && !float.IsInfinity(position.y)
            && position.x >= 0.5f
            && position.y >= 0.5f
            && position.x <= interiorSize.x - 0.5f
            && position.y <= interiorSize.y - 0.5f;
    }

    public static Vector3Int GetLowerLeftAnchorCell(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        return new Vector3Int(
            Mathf.Min(firstCorner.x, secondCorner.x),
            Mathf.Min(firstCorner.y, secondCorner.y),
            firstCorner.z);
    }

    public static Vector2Int GetInclusiveSize(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        return new Vector2Int(
            Mathf.Abs(firstCorner.x - secondCorner.x) + 1,
            Mathf.Abs(firstCorner.y - secondCorner.y) + 1);
    }

    public static void GetCells(Vector3Int anchorCell, Vector2Int size, List<Vector3Int> cells)
    {
        cells.Clear();

        if (!IsValid(size))
        {
            return;
        }

        for (var y = 0; y < size.y; y++)
        {
            for (var x = 0; x < size.x; x++)
            {
                cells.Add(anchorCell + new Vector3Int(x, y));
            }
        }
    }

    public static void GetBoundaryCells(Vector3Int anchorCell, Vector2Int size, List<Vector3Int> cells)
    {
        cells.Clear();

        if (!IsValid(size))
        {
            return;
        }

        for (var x = 0; x < size.x; x++)
        {
            cells.Add(anchorCell + new Vector3Int(x, 0));
            if (size.y > 1)
            {
                cells.Add(anchorCell + new Vector3Int(x, size.y - 1));
            }
        }

        for (var y = 1; y < size.y - 1; y++)
        {
            cells.Add(anchorCell + new Vector3Int(0, y));
            if (size.x > 1)
            {
                cells.Add(anchorCell + new Vector3Int(size.x - 1, y));
            }
        }
    }

    public static Vector3Int GetVisualAnchorCell(Vector3Int anchorCell, Vector2Int visualAnchorCellOffset)
    {
        return anchorCell + new Vector3Int(
            visualAnchorCellOffset.x,
            visualAnchorCellOffset.y);
    }

    public static GridEdge GetSouthEntranceEdge(
        Vector3Int anchorCell,
        Vector2Int size,
        Vector2Int entranceCellOffset)
    {
        var entranceX = Mathf.Clamp(entranceCellOffset.x, 0, size.x - 1);
        return GridEdge.FromCellSide(
            anchorCell + new Vector3Int(entranceX, 0),
            GridEdgeDirection.South);
    }

    public static Vector3 GetEdgeCenterWorld(GridEdge edge, Tilemap ground)
    {
        return (ground.CellToWorld(edge.Corner) + ground.CellToWorld(edge.EndCorner)) * 0.5f;
    }
}
