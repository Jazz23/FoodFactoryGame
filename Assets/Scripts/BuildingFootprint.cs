// Enumerates rectangular grid footprints from a stable lower-left anchor cell.
using System.Collections.Generic;
using UnityEngine;

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
}
