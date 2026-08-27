// Maintains the authoritative relationship between covered grid cells and building instances.
using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingOccupancy
{
    private readonly Dictionary<Vector3Int, uint> buildingIdsByCell = new();
    private readonly Dictionary<uint, List<Vector3Int>> cellsByBuildingId = new();

    public bool IsOccupied(Vector3Int cell)
    {
        return buildingIdsByCell.ContainsKey(cell);
    }

    public bool TryGetBuildingId(Vector3Int cell, out uint buildingId)
    {
        return buildingIdsByCell.TryGetValue(cell, out buildingId);
    }

    public bool CanReserve(uint buildingId, IReadOnlyList<Vector3Int> cells)
    {
        if (buildingId == 0 || cells.Count == 0 || cellsByBuildingId.ContainsKey(buildingId))
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

        return true;
    }

    public bool TryReserve(uint buildingId, IReadOnlyList<Vector3Int> cells)
    {
        if (!CanReserve(buildingId, cells))
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
        return true;
    }

    public bool Release(uint buildingId)
    {
        if (!cellsByBuildingId.TryGetValue(buildingId, out var cells))
        {
            return false;
        }

        foreach (var cell in cells)
        {
            buildingIdsByCell.Remove(cell);
        }

        cellsByBuildingId.Remove(buildingId);
        return true;
    }

    public void Clear()
    {
        buildingIdsByCell.Clear();
        cellsByBuildingId.Clear();
    }
}
