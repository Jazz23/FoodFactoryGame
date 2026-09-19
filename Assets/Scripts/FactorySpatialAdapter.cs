// Bridges logical factory locations to presentation coordinates and scene picking.
using UnityEngine;
using UnityEngine.Tilemaps;

public readonly struct FactoryLogicalLocation
{
    public FactoryLogicalLocation(
        uint newBuildingInstanceId,
        int newFloorIndex,
        Vector2 newFloorPosition)
    {
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
        FloorPosition = newFloorPosition;
    }

    public uint BuildingInstanceId { get; }
    public int FloorIndex { get; }
    public Vector2 FloorPosition { get; }
}

public sealed class FactorySpatialAdapter
{
    private readonly SceneGrid grid;

    public FactorySpatialAdapter(SceneGrid newGrid)
    {
        grid = newGrid;
    }

    public float CellSize => grid.CellSize;
    public float OrthographicSize => grid.OrthographicSize;

    public Vector2 LogicalToWorld2D(FactoryLogicalLocation location)
    {
        return grid.LogicalToWorld(location.FloorPosition);
    }

    public Vector3 LogicalToWorld3D(
        FactoryLogicalLocation location,
        float floorElevation)
    {
        var groundPosition = LogicalToWorld3DGround(location.FloorPosition);
        return new Vector3(
            groundPosition.x,
            floorElevation,
            groundPosition.y);
    }

    public FactoryLogicalLocation WorldToLogical2D(
        Vector2 worldPosition,
        uint buildingInstanceId,
        int floorIndex)
    {
        return new FactoryLogicalLocation(
            buildingInstanceId,
            floorIndex,
            grid.WorldToLogical(worldPosition));
    }

    public FactoryLogicalLocation WorldToLogical3D(
        Vector3 worldPosition,
        uint buildingInstanceId,
        int floorIndex)
    {
        return new FactoryLogicalLocation(
            buildingInstanceId,
            floorIndex,
            WorldToLogical3DGround(new Vector2(worldPosition.x, worldPosition.z)));
    }

    public Vector2 LogicalToWorld3DGround(Vector2 logicalPosition)
    {
        return grid.VisualOrigin
            + (logicalPosition - grid.LogicalOrigin) * CellSize;
    }

    public Vector2 WorldToLogical3DGround(Vector2 worldPosition)
    {
        return grid.LogicalOrigin
            + (worldPosition - grid.VisualOrigin) / CellSize;
    }

    public bool TryGetCellFrom2DRay(
        Ray ray,
        Tilemap ground,
        out Vector3Int cell)
    {
        return TryGetCellFrom2DPlaneRay(ray, ground, out cell);
    }

    public static bool TryGetCellFrom2DPlaneRay(
        Ray ray,
        Tilemap ground,
        out Vector3Int cell)
    {
        var plane = new Plane(Vector3.forward, ground.transform.position.z);
        if (!plane.Raycast(ray, out var distance))
        {
            cell = default;
            return false;
        }

        cell = ground.WorldToCell(ray.GetPoint(distance));
        return true;
    }

    public bool TryGetCellFrom3DRay(
        Ray ray,
        float floorElevation,
        out Vector3Int cell)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, floorElevation, 0f));
        if (!plane.Raycast(ray, out var distance))
        {
            cell = default;
            return false;
        }

        var point = ray.GetPoint(distance);
        var logical = WorldToLogical3DGround(new Vector2(point.x, point.z));
        cell = new Vector3Int(
            Mathf.FloorToInt(logical.x),
            Mathf.FloorToInt(logical.y),
            0);
        return true;
    }
}
