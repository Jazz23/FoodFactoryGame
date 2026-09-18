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
        var projectedPosition = LogicalToWorld2D(location);
        return new Vector3(
            projectedPosition.x,
            floorElevation,
            projectedPosition.y);
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
        return WorldToLogical2D(
            new Vector2(worldPosition.x, worldPosition.z),
            buildingInstanceId,
            floorIndex);
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
}
