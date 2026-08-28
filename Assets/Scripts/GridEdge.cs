// Defines canonical grid edges so every physical wall has one stable identity.
using System;
using System.Collections.Generic;
using UnityEngine;

public enum GridEdgeDirection : byte
{
    South,
    East,
    North,
    West
}

public enum GridEdgeAxis : byte
{
    Horizontal,
    Vertical
}

[Serializable]
public struct GridEdge : IEquatable<GridEdge>
{
    [SerializeField] private Vector3Int corner;
    [SerializeField] private GridEdgeAxis axis;

    public GridEdge(Vector3Int corner, GridEdgeAxis axis)
    {
        this.corner = corner;
        this.axis = axis;
    }

    public Vector3Int Corner => corner;
    public GridEdgeAxis Axis => axis;

    public Vector3Int EndCorner => Axis == GridEdgeAxis.Horizontal
        ? Corner + Vector3Int.right
        : Corner + Vector3Int.up;

    public Vector3Int FirstAdjacentCell => Axis == GridEdgeAxis.Horizontal
        ? Corner + Vector3Int.down
        : Corner + Vector3Int.left;

    public Vector3Int SecondAdjacentCell => Corner;

    public static GridEdge FromCellSide(Vector3Int cell, GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.South => new GridEdge(cell, GridEdgeAxis.Horizontal),
            GridEdgeDirection.East => new GridEdge(cell + Vector3Int.right, GridEdgeAxis.Vertical),
            GridEdgeDirection.North => new GridEdge(cell + Vector3Int.up, GridEdgeAxis.Horizontal),
            GridEdgeDirection.West => new GridEdge(cell, GridEdgeAxis.Vertical),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null)
        };
    }

    public static GridEdgeDirection RotateClockwise(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.South => GridEdgeDirection.East,
            GridEdgeDirection.East => GridEdgeDirection.North,
            GridEdgeDirection.North => GridEdgeDirection.West,
            GridEdgeDirection.West => GridEdgeDirection.South,
            _ => GridEdgeDirection.South
        };
    }

    public static void GetCellEdges(Vector3Int cell, List<GridEdge> edges)
    {
        edges.Clear();
        edges.Add(FromCellSide(cell, GridEdgeDirection.South));
        edges.Add(FromCellSide(cell, GridEdgeDirection.East));
        edges.Add(FromCellSide(cell, GridEdgeDirection.North));
        edges.Add(FromCellSide(cell, GridEdgeDirection.West));
    }

    public bool Equals(GridEdge other)
    {
        return Corner == other.Corner && Axis == other.Axis;
    }

    public override bool Equals(object value)
    {
        return value is GridEdge other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Corner, Axis);
    }

    public override string ToString()
    {
        return $"{Axis} ({Corner.x}, {Corner.y})";
    }
}
