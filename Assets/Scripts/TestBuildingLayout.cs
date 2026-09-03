// Stores one persistent test building's topology and Scene View door selection.
using System.Collections.Generic;
using UnityEngine;

public sealed class TestBuildingLayout : MonoBehaviour
{
    public const string GeneratedVisualsName = "Generated Visuals";
    public const string GeneratedCollisionName = "Generated Collision";
    public const string VisualDoorsName = "Visual Doors";

    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private Vector2Int size;
    [SerializeField] private string doorWallId = string.Empty;
    [SerializeField, Range(0f, 1f)] private float doorOffset = 0.5f;

    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int Size => size;
    public bool HasDoor => !string.IsNullOrEmpty(doorWallId);
    public string DoorWallId => doorWallId;
    public float DoorOffset => doorOffset;

    public void Configure(Vector3Int newAnchorCell, Vector2Int newSize)
    {
        anchorCell = newAnchorCell;
        size = newSize;
    }

    public void GetExteriorWallSpans(List<TestBuildingCreator.ExteriorWallSpan> spans)
    {
        TestBuildingCreator.GetExteriorWallSpans(anchorCell, size, spans);
    }

    public void GetWorldFootprint(SceneGrid grid, List<Vector2> points)
    {
        points.Clear();
        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        var maximum = anchorCell + new Vector3Int(size.x, size.y);
        points.Add(grid.LogicalToWorld(new Vector2(anchorCell.x, anchorCell.y)));
        points.Add(grid.LogicalToWorld(new Vector2(maximum.x, anchorCell.y)));
        points.Add(grid.LogicalToWorld(new Vector2(maximum.x, maximum.y)));
        points.Add(grid.LogicalToWorld(new Vector2(anchorCell.x, maximum.y)));
    }

    public bool IntersectsFootprint(SceneGrid grid, Bounds playerFootprint)
    {
        var points = new List<Vector2>(4);
        GetWorldFootprint(grid, points);
        return BuildingDepthGeometry.IntersectsFootprint(points, playerFootprint);
    }

    public bool TryGetRearEdgeY(
        SceneGrid grid,
        Bounds playerFootprint,
        out float rearEdgeY)
    {
        var points = new List<Vector2>(4);
        GetWorldFootprint(grid, points);
        return BuildingDepthGeometry.TryGetRearEdgeY(points, playerFootprint, out rearEdgeY);
    }

    public void SetDoor(TestBuildingCreator.ExteriorWallSpan wall, float normalizedOffset)
    {
        doorWallId = wall.StableId;
        doorOffset = Mathf.Clamp01(normalizedOffset);
    }

    public void ClearDoor()
    {
        doorWallId = string.Empty;
        doorOffset = 0.5f;
    }

    public bool TryGetDoor(
        List<TestBuildingCreator.ExteriorWallSpan> spans,
        out TestBuildingCreator.ExteriorWallSpan wall)
    {
        foreach (var candidate in spans)
        {
            if (candidate.StableId != doorWallId)
            {
                continue;
            }

            wall = candidate;
            return true;
        }

        wall = default;
        return false;
    }

}
