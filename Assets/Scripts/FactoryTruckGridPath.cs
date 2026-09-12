// Builds deterministic exterior grid routes and samples them for truck presentation.
using System.Collections.Generic;
using UnityEngine;

public static class FactoryTruckGridPath
{
    private static readonly Vector2Int[] Neighbours =
    {
        Vector2Int.down,
        Vector2Int.right,
        Vector2Int.up,
        Vector2Int.left
    };

    public static bool TryBuild(
        IReadOnlyList<BuildingRecord> buildings,
        Vector2Int start,
        Vector2Int destination,
        List<Vector2Int> cells)
    {
        cells.Clear();
        if (buildings is null)
        {
            return false;
        }

        var blocked = new HashSet<Vector2Int>();
        var buildingCells = new List<Vector3Int>();
        var min = Vector2Int.Min(start, destination);
        var max = Vector2Int.Max(start, destination);
        foreach (var building in buildings)
        {
            if (building is null)
            {
                continue;
            }

            min = Vector2Int.Min(min, new Vector2Int(building.AnchorCell.x, building.AnchorCell.y));
            max = Vector2Int.Max(
                max,
                new Vector2Int(
                    building.AnchorCell.x + building.FootprintSize.x - 1,
                    building.AnchorCell.y + building.FootprintSize.y - 1));
            BuildingFootprint.GetCells(
                building.AnchorCell,
                building.FootprintSize,
                buildingCells);
            foreach (var cell in buildingCells)
            {
                blocked.Add(new Vector2Int(cell.x, cell.y));
            }
        }

        blocked.Remove(start);
        blocked.Remove(destination);
        var margin = Mathf.Max(4, buildings.Count * 2 + 2);
        min -= Vector2Int.one * margin;
        max += Vector2Int.one * margin;

        var parents = new Dictionary<Vector2Int, Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        parents.Add(start, start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == destination)
            {
                ReconstructPath(parents, destination, cells);
                return true;
            }

            foreach (var offset in Neighbours)
            {
                var next = current + offset;
                if (next.x < min.x || next.x > max.x
                    || next.y < min.y || next.y > max.y
                    || blocked.Contains(next)
                    || parents.ContainsKey(next))
                {
                    continue;
                }

                parents.Add(next, current);
                queue.Enqueue(next);
            }
        }

        return false;
    }

    public static Vector2 Sample(IReadOnlyList<Vector2Int> cells, float progress)
    {
        if (cells is null || cells.Count == 0)
        {
            return default;
        }

        if (cells.Count == 1)
        {
            return SceneGrid.CellCenterLogical(cells[0]);
        }

        var distance = Mathf.Clamp01(progress) * (cells.Count - 1);
        var index = Mathf.Min(Mathf.FloorToInt(distance), cells.Count - 2);
        return Vector2.Lerp(
            SceneGrid.CellCenterLogical(cells[index]),
            SceneGrid.CellCenterLogical(cells[index + 1]),
            distance - index);
    }

    private static void ReconstructPath(
        IReadOnlyDictionary<Vector2Int, Vector2Int> parents,
        Vector2Int destination,
        List<Vector2Int> cells)
    {
        var current = destination;
        cells.Add(current);
        while (parents[current] != current)
        {
            current = parents[current];
            cells.Add(current);
        }

        cells.Reverse();
    }
}
