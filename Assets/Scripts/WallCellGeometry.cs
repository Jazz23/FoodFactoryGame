// Defines centered wall-cell shapes and their grid-relative half-cell footprints.
using System;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum WallCellShape : byte
{
    Horizontal,
    Vertical,
    CornerNorthEast,
    CornerSouthEast,
    CornerSouthWest,
    CornerNorthWest
}

public static class WallCellGeometry
{
    public const float ThicknessInCells = 0.5f;

    public static bool IsValid(WallCellShape shape)
    {
        return Enum.IsDefined(typeof(WallCellShape), shape);
    }

    public static WallCellShape GetNextPlacementShape(WallCellShape shape)
    {
        return shape switch
        {
            WallCellShape.Horizontal => WallCellShape.Vertical,
            WallCellShape.Vertical => WallCellShape.CornerNorthEast,
            WallCellShape.CornerNorthEast => WallCellShape.CornerSouthEast,
            WallCellShape.CornerSouthEast => WallCellShape.CornerSouthWest,
            WallCellShape.CornerSouthWest => WallCellShape.CornerNorthWest,
            _ => WallCellShape.Horizontal
        };
    }

    public static GridEdgeDirection GetPrimaryDirection(WallCellShape shape)
    {
        return shape == WallCellShape.Vertical
            ? GridEdgeDirection.East
            : GridEdgeDirection.South;
    }

    public static Vector2[] GetLogicalFootprint(WallCellShape shape)
    {
        return shape switch
        {
            WallCellShape.Horizontal => new[]
            {
                new Vector2(0f, 0.25f),
                new Vector2(1f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0f, 0.75f)
            },
            WallCellShape.Vertical => new[]
            {
                new Vector2(0.25f, 0f),
                new Vector2(0.75f, 0f),
                new Vector2(0.75f, 1f),
                new Vector2(0.25f, 1f)
            },
            WallCellShape.CornerNorthEast => new[]
            {
                new Vector2(0.5f, 0.25f),
                new Vector2(1f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0.75f, 0.75f),
                new Vector2(0.75f, 1f),
                new Vector2(0.25f, 1f),
                new Vector2(0.25f, 0.5f),
                new Vector2(0.5f, 0.5f)
            },
            WallCellShape.CornerSouthEast => new[]
            {
                new Vector2(0.25f, 0f),
                new Vector2(0.75f, 0f),
                new Vector2(0.75f, 0.25f),
                new Vector2(1f, 0.25f),
                new Vector2(1f, 0.75f),
                new Vector2(0.5f, 0.75f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.25f, 0.5f)
            },
            WallCellShape.CornerSouthWest => new[]
            {
                new Vector2(0.25f, 0f),
                new Vector2(0.75f, 0f),
                new Vector2(0.75f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.75f),
                new Vector2(0f, 0.75f),
                new Vector2(0f, 0.25f),
                new Vector2(0.25f, 0.25f)
            },
            WallCellShape.CornerNorthWest => new[]
            {
                new Vector2(0f, 0.25f),
                new Vector2(0.5f, 0.25f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.75f, 0.5f),
                new Vector2(0.75f, 1f),
                new Vector2(0.25f, 1f),
                new Vector2(0.25f, 0.75f),
                new Vector2(0f, 0.75f)
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };
    }

    public static Vector3[] GetWorldFootprint(
        WallCellShape shape,
        Vector3Int cell,
        Tilemap ground,
        float worldZ)
    {
        var logicalPoints = GetLogicalFootprint(shape);
        var worldPoints = new Vector3[logicalPoints.Length];
        var origin = ground.CellToWorld(cell);
        var right = ground.CellToWorld(cell + Vector3Int.right) - origin;
        var up = ground.CellToWorld(cell + Vector3Int.up) - origin;

        for (var index = 0; index < logicalPoints.Length; index++)
        {
            var logicalPoint = logicalPoints[index];
            worldPoints[index] = origin + right * logicalPoint.x + up * logicalPoint.y;
            worldPoints[index].z = worldZ;
        }

        return worldPoints;
    }

    public static Vector3[] GetWorldCellBoundary(
        Vector3Int cell,
        Tilemap ground,
        float worldZ)
    {
        var points = new[]
        {
            ground.CellToWorld(cell),
            ground.CellToWorld(cell + Vector3Int.right),
            ground.CellToWorld(cell + Vector3Int.right + Vector3Int.up),
            ground.CellToWorld(cell + Vector3Int.up)
        };

        for (var index = 0; index < points.Length; index++)
        {
            points[index].z = worldZ;
        }

        return points;
    }
}
