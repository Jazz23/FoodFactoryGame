// Stores one persistent test building's topology and Scene View door selection.
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TestBuildingLayout : MonoBehaviour
{
    [Serializable]
    public sealed class DoorPlacement
    {
        public DoorPlacement()
        {
        }

        public DoorPlacement(string wallId, float normalizedOffset)
        {
            this.wallId = wallId;
            this.normalizedOffset = Mathf.Clamp01(normalizedOffset);
        }

        [SerializeField] private string wallId = string.Empty;
        [SerializeField, Range(0f, 1f)] private float normalizedOffset = 0.5f;

        public string WallId => wallId;
        public float NormalizedOffset => normalizedOffset;
    }

    public const string GeneratedVisualsName = "Generated Visuals";
    public const string GeneratedCollisionName = "Generated Collision";
    public const string VisualDoorsName = "Visual Doors";

    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private Vector2Int size;
    [SerializeField, Min(1)] private uint buildingInstanceId;
    [SerializeField] private List<DoorPlacement> doors = new();

    // These fields preserve door data from scenes saved before multi-door support.
    [SerializeField, HideInInspector] private string doorWallId = string.Empty;
    [SerializeField, HideInInspector, Range(0f, 1f)] private float doorOffset = 0.5f;

    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int Size => size;
    public uint BuildingInstanceId => buildingInstanceId;
    public IReadOnlyList<DoorPlacement> Doors
    {
        get
        {
            MigrateLegacyDoor();
            return doors;
        }
    }

    public bool HasDoor => Doors.Count > 0;
    public string DoorWallId => Doors.Count > 0 ? Doors[0].WallId : string.Empty;
    public float DoorOffset => Doors.Count > 0 ? Doors[0].NormalizedOffset : 0.5f;

    public void Configure(Vector3Int newAnchorCell, Vector2Int newSize)
    {
        anchorCell = newAnchorCell;
        size = newSize;
    }

    public void SetBuildingInstanceId(uint newBuildingInstanceId)
    {
        buildingInstanceId = newBuildingInstanceId;
    }

    public bool MigrateLegacyDoor()
    {
        if (doors.Count > 0 || string.IsNullOrEmpty(doorWallId))
        {
            return false;
        }

        doors.Add(new DoorPlacement(doorWallId, doorOffset));
        doorWallId = string.Empty;
        doorOffset = 0.5f;
        return true;
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
        ClearDoors();
        AddDoor(wall, normalizedOffset);
    }

    public bool AddDoor(
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        MigrateLegacyDoor();
        var clampedOffset = Mathf.Clamp01(normalizedOffset);
        foreach (var door in doors)
        {
            if (door.WallId == wall.StableId
                && Mathf.Approximately(door.NormalizedOffset, clampedOffset))
            {
                return false;
            }
        }

        doors.Add(new DoorPlacement(wall.StableId, clampedOffset));
        return true;
    }

    public bool ContainsDoor(string wallId, float normalizedOffset)
    {
        foreach (var door in Doors)
        {
            if (door.WallId == wallId
                && Mathf.Approximately(door.NormalizedOffset, normalizedOffset))
            {
                return true;
            }
        }

        return false;
    }

    public void ClearDoor()
    {
        ClearDoors();
    }

    public void ClearDoors()
    {
        doors.Clear();
        doorWallId = string.Empty;
        doorOffset = 0.5f;
    }

    public bool TryGetDoor(
        List<TestBuildingCreator.ExteriorWallSpan> spans,
        out TestBuildingCreator.ExteriorWallSpan wall)
    {
        MigrateLegacyDoor();
        if (doors.Count == 0)
        {
            wall = default;
            return false;
        }

        return TryGetDoor(spans, doors[0].WallId, out wall);
    }

    public bool TryGetDoor(
        List<TestBuildingCreator.ExteriorWallSpan> spans,
        string wallId,
        out TestBuildingCreator.ExteriorWallSpan wall)
    {
        foreach (var candidate in spans)
        {
            if (candidate.StableId != wallId)
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
