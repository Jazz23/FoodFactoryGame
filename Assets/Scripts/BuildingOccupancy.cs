// Maintains authoritative atomic reservations for building cells and canonical wall edges.
using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingOccupancy
{
    private readonly Dictionary<Vector3Int, uint> buildingIdsByCell = new();
    private readonly Dictionary<GridEdge, uint> buildingIdsByEdge = new();
    private readonly Dictionary<uint, List<Vector3Int>> cellsByBuildingId = new();
    private readonly Dictionary<uint, List<GridEdge>> edgesByBuildingId = new();

    public bool IsOccupied(Vector3Int cell)
    {
        return buildingIdsByCell.ContainsKey(cell);
    }

    public bool TryGetBuildingId(Vector3Int cell, out uint buildingId)
    {
        return buildingIdsByCell.TryGetValue(cell, out buildingId);
    }

    public bool IsOccupied(GridEdge edge)
    {
        return buildingIdsByEdge.ContainsKey(edge);
    }

    public bool TryGetBuildingId(GridEdge edge, out uint buildingId)
    {
        return buildingIdsByEdge.TryGetValue(edge, out buildingId);
    }

    public bool CanReserve(uint buildingId, IReadOnlyList<Vector3Int> cells)
    {
        return CanReserve(buildingId, cells, System.Array.Empty<GridEdge>());
    }

    public bool CanReserve(
        uint buildingId,
        IReadOnlyList<Vector3Int> cells,
        IReadOnlyList<GridEdge> edges)
    {
        if (buildingId == 0
            || cells.Count == 0 && edges.Count == 0
            || cellsByBuildingId.ContainsKey(buildingId)
            || edgesByBuildingId.ContainsKey(buildingId))
        {
            return false;
        }

        var uniqueCells = new HashSet<Vector3Int>();
        foreach (var cell in cells)
        {
            if (!uniqueCells.Add(cell) || buildingIdsByCell.ContainsKey(cell))
            {
                return false;
            }
        }

        var uniqueEdges = new HashSet<GridEdge>();
        foreach (var edge in edges)
        {
            if (!uniqueEdges.Add(edge) || buildingIdsByEdge.ContainsKey(edge))
            {
                return false;
            }

            var firstCellIsOccupied = uniqueCells.Contains(edge.FirstAdjacentCell)
                || buildingIdsByCell.ContainsKey(edge.FirstAdjacentCell);
            var secondCellIsOccupied = uniqueCells.Contains(edge.SecondAdjacentCell)
                || buildingIdsByCell.ContainsKey(edge.SecondAdjacentCell);
            if (firstCellIsOccupied && secondCellIsOccupied)
            {
                return false;
            }
        }

        foreach (var edge in buildingIdsByEdge.Keys)
        {
            if (uniqueCells.Contains(edge.FirstAdjacentCell)
                && uniqueCells.Contains(edge.SecondAdjacentCell))
            {
                return false;
            }
        }

        return true;
    }

    public bool TryReserve(uint buildingId, IReadOnlyList<Vector3Int> cells)
    {
        return TryReserve(buildingId, cells, System.Array.Empty<GridEdge>());
    }

    public bool TryReserve(
        uint buildingId,
        IReadOnlyList<Vector3Int> cells,
        IReadOnlyList<GridEdge> edges)
    {
        if (!CanReserve(buildingId, cells, edges))
        {
            return false;
        }

        var reservedCells = new List<Vector3Int>(cells.Count);
        foreach (var cell in cells)
        {
            buildingIdsByCell.Add(cell, buildingId);
            reservedCells.Add(cell);
        }

        cellsByBuildingId.Add(buildingId, reservedCells);

        var reservedEdges = new List<GridEdge>(edges.Count);
        foreach (var edge in edges)
        {
            buildingIdsByEdge.Add(edge, buildingId);
            reservedEdges.Add(edge);
        }

        edgesByBuildingId.Add(buildingId, reservedEdges);
        return true;
    }

    public bool Release(uint buildingId)
    {
        if (!cellsByBuildingId.TryGetValue(buildingId, out var cells)
            || !edgesByBuildingId.TryGetValue(buildingId, out var edges))
        {
            return false;
        }

        foreach (var cell in cells)
        {
            buildingIdsByCell.Remove(cell);
        }

        foreach (var edge in edges)
        {
            buildingIdsByEdge.Remove(edge);
        }

        cellsByBuildingId.Remove(buildingId);
        edgesByBuildingId.Remove(buildingId);
        return true;
    }

    public void Clear()
    {
        buildingIdsByCell.Clear();
        buildingIdsByEdge.Clear();
        cellsByBuildingId.Clear();
        edgesByBuildingId.Clear();
    }
}
