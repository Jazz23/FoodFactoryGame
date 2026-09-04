// Stores test-building generation settings and calculates rectangular wall and roof layouts.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TestBuildingCreator : MonoBehaviour
{
    public const int CurrentSettingsVersion = 4;
    public const int DefaultRoofSortingOrder = 1000;
    public const float RoofBoundaryInset = 0.5f + WallCellGeometry.ThicknessInCells * 0.5f;

    public readonly struct WallPlacement
    {
        public WallPlacement(GridWall.WallKind kind, Vector2Int cell)
        {
            Kind = kind;
            Cell = cell;
        }

        public GridWall.WallKind Kind { get; }
        public Vector2Int Cell { get; }
    }

    public readonly struct ExteriorWallSpan
    {
        public ExteriorWallSpan(
            WallPlacement placement,
            int segmentIndex,
            GridEdgeDirection direction,
            GridWall.PlaneSegment segment)
        {
            Kind = placement.Kind;
            Cell = placement.Cell;
            SegmentIndex = segmentIndex;
            Direction = direction;
            LogicalStart = segment.Start;
            LogicalEnd = segment.End;
        }

        public GridWall.WallKind Kind { get; }
        public Vector2Int Cell { get; }
        public int SegmentIndex { get; }
        public GridEdgeDirection Direction { get; }
        public Vector2 LogicalStart { get; }
        public Vector2 LogicalEnd { get; }
        public bool IsCorner => Kind is not GridWall.WallKind.Horizontal
            and not GridWall.WallKind.Vertical;
        public string StableId => $"{Direction}:{Cell.x}:{Cell.y}:{SegmentIndex}";
    }

    [SerializeField] private SceneGrid grid = null!;
    [SerializeField] private Transform generatedBuildings = null!;
    [SerializeField] private Material material = null!;
    [SerializeField, Min(0f)] private float wallHeight = 2f;
    [SerializeField, HideInInspector, Min(0f)] private float roofTopHeight = 2f;
    [SerializeField, Min(0f)] private float roofThickness = 0.1f;
    [SerializeField] private Color roofTopColor = new(0.035f, 0.05f, 0.075f, 1f);
    [SerializeField] private Color roofSideColor = new(0.16f, 0.21f, 0.26f, 1f);
    [SerializeField] private int roofSortingOrder = DefaultRoofSortingOrder;
    [SerializeField] private BuildingVisualStyle visualStyle = null!;
    [SerializeField, Min(0.01f)] private float doorCornerExclusionDistance = 0.15f;
    [SerializeField, HideInInspector] private int settingsVersion;

    public SceneGrid Grid => grid;
    public Transform GeneratedBuildings => generatedBuildings;
    public Material Material => material;
    public float WallHeight => wallHeight;
    public float RoofTopHeight => roofTopHeight;
    public float RoofThickness => roofThickness;
    public Color RoofTopColor => roofTopColor;
    public Color RoofSideColor => roofSideColor;
    public int RoofSortingOrder => roofSortingOrder;
    public BuildingVisualStyle VisualStyle => visualStyle;
    public float DoorCornerExclusionDistance => doorCornerExclusionDistance;

    public uint GetNextBuildingInstanceId()
    {
        var usedIds = new HashSet<uint>();
        foreach (var layout in generatedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId != 0)
            {
                usedIds.Add(layout.BuildingInstanceId);
            }
        }

        var nextId = 1u;
        while (usedIds.Contains(nextId))
        {
            nextId++;
        }

        return nextId;
    }

    public static Vector3Int GetAnchorCell(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        return BuildingFootprint.GetLowerLeftAnchorCell(firstCorner, secondCorner);
    }

    public static Vector2Int GetSize(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        return BuildingFootprint.GetInclusiveSize(firstCorner, secondCorner);
    }

    public static bool IsSupportedSize(Vector2Int size)
    {
        return size.x >= 2 && size.y >= 2;
    }

    public static void GetWallPlacements(
        Vector3Int firstCorner,
        Vector3Int secondCorner,
        List<WallPlacement> placements)
    {
        placements.Clear();
        var anchor = GetAnchorCell(firstCorner, secondCorner);
        var size = GetSize(firstCorner, secondCorner);
        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        var maximum = anchor + new Vector3Int(size.x - 1, size.y - 1);
        for (var x = anchor.x + 1; x < maximum.x; x++)
        {
            placements.Add(new WallPlacement(
                GridWall.WallKind.Horizontal,
                new Vector2Int(x, anchor.y)));
            if (size.y > 1)
            {
                placements.Add(new WallPlacement(
                    GridWall.WallKind.Horizontal,
                    new Vector2Int(x, maximum.y)));
            }
        }

        for (var y = anchor.y + 1; y < maximum.y; y++)
        {
            if (size.x == 1)
            {
                placements.Add(new WallPlacement(
                    GridWall.WallKind.Vertical,
                    new Vector2Int(anchor.x, y)));
            }
            else
            {
                placements.Add(new WallPlacement(
                    GridWall.WallKind.Vertical,
                    new Vector2Int(anchor.x, y)));
                placements.Add(new WallPlacement(
                    GridWall.WallKind.Vertical,
                    new Vector2Int(maximum.x, y)));
            }
        }

        placements.Add(new WallPlacement(
            GridWall.WallKind.CornerSouthWest,
            new Vector2Int(anchor.x, anchor.y)));
        placements.Add(new WallPlacement(
            GridWall.WallKind.CornerSouthEast,
            new Vector2Int(maximum.x, anchor.y)));
        placements.Add(new WallPlacement(
            GridWall.WallKind.CornerNorthWest,
            new Vector2Int(anchor.x, maximum.y)));
        placements.Add(new WallPlacement(
            GridWall.WallKind.CornerNorthEast,
            new Vector2Int(maximum.x, maximum.y)));
    }

    public static void GetExteriorWallSpans(
        Vector3Int anchorCell,
        Vector2Int size,
        List<ExteriorWallSpan> spans)
    {
        spans.Clear();
        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        var placements = new List<WallPlacement>();
        var secondCorner = anchorCell + new Vector3Int(size.x - 1, size.y - 1);
        GetWallPlacements(anchorCell, secondCorner, placements);
        foreach (var placement in placements)
        {
            var segments = GridWall.GetLogicalPlaneSegments(placement.Kind, placement.Cell);
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                spans.Add(new ExteriorWallSpan(
                    placement,
                    segmentIndex,
                    GetExteriorDirection(placement, anchorCell, segmentIndex),
                    segments[segmentIndex]));
            }
        }
    }

    private static GridEdgeDirection GetExteriorDirection(
        WallPlacement placement,
        Vector3Int anchor,
        int segmentIndex)
    {
        return placement.Kind switch
        {
            GridWall.WallKind.Horizontal => placement.Cell.y != anchor.y
                ? GridEdgeDirection.North
                : GridEdgeDirection.South,
            GridWall.WallKind.Vertical => placement.Cell.x != anchor.x
                ? GridEdgeDirection.East
                : GridEdgeDirection.West,
            GridWall.WallKind.CornerNorthWest => segmentIndex == 0
                ? GridEdgeDirection.North
                : GridEdgeDirection.West,
            GridWall.WallKind.CornerNorthEast => segmentIndex == 0
                ? GridEdgeDirection.North
                : GridEdgeDirection.East,
            GridWall.WallKind.CornerSouthWest => segmentIndex == 0
                ? GridEdgeDirection.South
                : GridEdgeDirection.West,
            GridWall.WallKind.CornerSouthEast => segmentIndex == 0
                ? GridEdgeDirection.South
                : GridEdgeDirection.East,
            _ => GridEdgeDirection.South
        };
    }

    public static Vector2 GetRoofLogicalMin(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        var anchor = GetAnchorCell(firstCorner, secondCorner);
        return new Vector2(
            anchor.x + RoofBoundaryInset,
            anchor.y + RoofBoundaryInset);
    }

    public static Vector2 GetRoofLogicalMax(Vector3Int firstCorner, Vector3Int secondCorner)
    {
        var anchor = GetAnchorCell(firstCorner, secondCorner);
        var size = GetSize(firstCorner, secondCorner);
        var exclusiveMaximum = anchor + new Vector3Int(size.x, size.y);
        return new Vector2(
            exclusiveMaximum.x - RoofBoundaryInset,
            exclusiveMaximum.y - RoofBoundaryInset);
    }

    public static float GetRoofTopHeight(
        float wallHeight,
        float requestedRoofTopHeight,
        float roofThickness)
    {
        return Mathf.Max(requestedRoofTopHeight, wallHeight + roofThickness);
    }

    public static float GetStoryBaseHeight(float wallHeight, int storyIndex)
    {
        return Mathf.Max(0f, storyIndex) * Mathf.Max(0f, wallHeight);
    }

    public static float GetStoryTopHeight(float wallHeight, int storyIndex)
    {
        return GetStoryBaseHeight(wallHeight, storyIndex) + Mathf.Max(0f, wallHeight);
    }

    public static float GetStorySlabBottomHeight(
        float wallHeight,
        int storyIndex,
        float slabThickness)
    {
        return Mathf.Max(
            0f,
            GetStoryTopHeight(wallHeight, storyIndex) - Mathf.Max(0f, slabThickness));
    }

    public static int GetRoofSortingOrder(
        Vector3Int firstCorner,
        Vector3Int secondCorner,
        int baseSortingOrder)
    {
        var anchor = GetAnchorCell(firstCorner, secondCorner);
        return baseSortingOrder - (anchor.x + anchor.y) * 10;
    }
}
