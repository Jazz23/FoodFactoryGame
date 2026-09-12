// Maps shared factory dock entities between exterior approach cells and interior perimeter cells.
using System.Collections.Generic;
using UnityEngine;

public static class FactoryDock
{
    public static bool IsDock(string definitionId)
    {
        return FactoryEntityDefinitions.Get(definitionId).IsDock;
    }

    public static bool TryFindExteriorPlacement(
        IEnumerable<BuildingRecord> buildings,
        Vector2 exteriorLogicalPosition,
        out BuildingRecord building,
        out Vector2 interiorPosition,
        out GridEdgeDirection direction)
    {
        building = null!;
        interiorPosition = default;
        direction = GridEdgeDirection.South;
        if (buildings is null
            || !IsFinite(exteriorLogicalPosition))
        {
            return false;
        }

        var exteriorCell = Vector2Int.FloorToInt(exteriorLogicalPosition);
        foreach (var candidate in buildings)
        {
            if (candidate is null
                || !TryGetInteriorPosition(
                    candidate,
                    exteriorCell,
                    out interiorPosition,
                    out direction))
            {
                continue;
            }

            building = candidate;
            return true;
        }

        return false;
    }

    public static bool TryGetInteriorPosition(
        BuildingRecord building,
        Vector2Int exteriorCell,
        out Vector2 interiorPosition,
        out GridEdgeDirection direction)
    {
        interiorPosition = default;
        direction = GridEdgeDirection.South;
        if (building is null)
        {
            return false;
        }

        var interiorSize = BuildingFootprint.GetUsableInteriorSize(building.FootprintSize);
        if (!BuildingFootprint.IsValid(interiorSize))
        {
            return false;
        }

        var local = exteriorCell - new Vector2Int(
            building.AnchorCell.x,
            building.AnchorCell.y);
        if (local.y == -1 && local.x >= 1 && local.x <= building.FootprintSize.x - 2)
        {
            interiorPosition = new Vector2(local.x - 0.5f, 0.5f);
            direction = GridEdgeDirection.South;
            return true;
        }

        if (local.x == building.FootprintSize.x
            && local.y >= 1
            && local.y <= building.FootprintSize.y - 2)
        {
            interiorPosition = new Vector2(interiorSize.x - 0.5f, local.y - 0.5f);
            direction = GridEdgeDirection.East;
            return true;
        }

        if (local.y == building.FootprintSize.y
            && local.x >= 1
            && local.x <= building.FootprintSize.x - 2)
        {
            interiorPosition = new Vector2(local.x - 0.5f, interiorSize.y - 0.5f);
            direction = GridEdgeDirection.North;
            return true;
        }

        if (local.x == -1 && local.y >= 1 && local.y <= building.FootprintSize.y - 2)
        {
            interiorPosition = new Vector2(0.5f, local.y - 0.5f);
            direction = GridEdgeDirection.West;
            return true;
        }

        return false;
    }

    public static bool TryGetExteriorApproachCell(
        BuildingRecord building,
        Vector2 interiorPosition,
        GridEdgeDirection direction,
        out Vector2Int exteriorCell)
    {
        exteriorCell = default;
        if (building is null)
        {
            return false;
        }

        var interiorSize = BuildingFootprint.GetUsableInteriorSize(building.FootprintSize);
        if (!BuildingFootprint.IsValid(interiorSize)
            || !BuildingFootprint.IsUsableInteriorPosition(interiorPosition, interiorSize))
        {
            return false;
        }

        var interiorCell = Vector2Int.FloorToInt(interiorPosition);
        switch (direction)
        {
            case GridEdgeDirection.South when interiorCell.y == 0:
                exteriorCell = new Vector2Int(
                    building.AnchorCell.x + interiorCell.x + 1,
                    building.AnchorCell.y - 1);
                return true;
            case GridEdgeDirection.East when interiorCell.x == interiorSize.x - 1:
                exteriorCell = new Vector2Int(
                    building.AnchorCell.x + building.FootprintSize.x,
                    building.AnchorCell.y + interiorCell.y + 1);
                return true;
            case GridEdgeDirection.North when interiorCell.y == interiorSize.y - 1:
                exteriorCell = new Vector2Int(
                    building.AnchorCell.x + interiorCell.x + 1,
                    building.AnchorCell.y + building.FootprintSize.y);
                return true;
            case GridEdgeDirection.West when interiorCell.x == 0:
                exteriorCell = new Vector2Int(
                    building.AnchorCell.x - 1,
                    building.AnchorCell.y + interiorCell.y + 1);
                return true;
            default:
                return false;
        }
    }

    public static float DirectionToRotation(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.East => 90f,
            GridEdgeDirection.North => 180f,
            GridEdgeDirection.West => 270f,
            _ => 0f
        };
    }

    public static GridEdgeDirection RotationToDirection(float rotation)
    {
        var normalized = Mathf.Repeat(rotation, 360f);
        var quarterTurn = Mathf.RoundToInt(normalized / 90f) % 4;
        return (GridEdgeDirection)quarterTurn;
    }

    private static bool IsFinite(Vector2 position)
    {
        return !float.IsNaN(position.x)
            && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y)
            && !float.IsInfinity(position.y);
    }
}
