// Generates thick, centered wall prisms on the scene's dimetric grid.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class GridWall : MonoBehaviour
{
    public readonly struct PlaneSegment
    {
        public PlaneSegment(Vector2 start, Vector2 end)
        {
            Start = start;
            End = end;
        }

        public Vector2 Start { get; }
        public Vector2 End { get; }
    }

    public enum WallKind
    {
        Horizontal,
        Vertical,
        CornerNorthWest,
        CornerNorthEast,
        CornerSouthWest,
        CornerSouthEast
    }

    [SerializeField] private WallKind kind = WallKind.Horizontal;
    [SerializeField] private Vector2Int cell;
    [SerializeField, Min(0f)] private float wallHeight = 1.75f;
    [SerializeField] private Material material = null!;

    private const float HalfCell = 0.5f;
    private const float SurfaceSortingScale = 10f;
    private const int WallTopSortingOffset = 6;
    private const int WallBaseSortingOrder = 1000;

    private static readonly Color WallLightColor = new(0.45f, 0.52f, 0.58f, 1f);
    private static readonly Color WallShadowColor = new(0.28f, 0.35f, 0.41f, 1f);
    private static readonly Color WallTopColor = new(0.62f, 0.68f, 0.74f, 1f);
    private static readonly Vector2[] CornerFootprint =
    {
        new(-0.25f, -0.5f),
        new(0.25f, -0.5f),
        new(0.25f, -0.25f),
        new(0.5f, -0.25f),
        new(0.5f, 0.25f),
        new(-0.25f, 0.25f)
    };

    private Mesh mesh = null!;
    private bool rebuildRequested;
    private int generatedSurfaceCount;

    public WallKind Kind => kind;
    public Vector2Int Cell => cell;
    public float WallHeight => wallHeight;
    public float ThicknessInCells => WallCellGeometry.ThicknessInCells;

    private void OnEnable()
    {
        rebuildRequested = true;
        RebuildIfRequired();
    }

    private void OnValidate()
    {
        rebuildRequested = true;
    }

    private void Update()
    {
        RebuildIfRequired();
    }

    private void RebuildIfRequired()
    {
        if (!rebuildRequested
            && mesh is not null
            && generatedSurfaceCount > 0
            && transform.childCount == generatedSurfaceCount)
        {
            return;
        }

        rebuildRequested = false;
        Rebuild();
    }

    public List<PlaneSegment> GetLogicalPlaneSegments()
    {
        return GetLogicalPlaneSegments(kind, cell);
    }

    public static List<PlaneSegment> GetLogicalPlaneSegments(
        WallKind wallKind,
        Vector2Int wallCell)
    {
        var center = SceneGrid.CellCenterLogical(wallCell);
        var segments = new List<PlaneSegment>();
        switch (wallKind)
        {
            case WallKind.Horizontal:
                segments.Add(new PlaneSegment(
                    center + Vector2.left * HalfCell,
                    center + Vector2.right * HalfCell));
                break;
            case WallKind.Vertical:
                segments.Add(new PlaneSegment(
                    center + Vector2.down * HalfCell,
                    center + Vector2.up * HalfCell));
                break;
            case WallKind.CornerNorthWest:
                AddCornerSegments(segments, center, Vector2.right, Vector2.down);
                break;
            case WallKind.CornerNorthEast:
                AddCornerSegments(segments, center, Vector2.left, Vector2.down);
                break;
            case WallKind.CornerSouthWest:
                AddCornerSegments(segments, center, Vector2.right, Vector2.up);
                break;
            case WallKind.CornerSouthEast:
                AddCornerSegments(segments, center, Vector2.left, Vector2.up);
                break;
        }

        return segments;
    }

    public List<Vector2> GetLogicalFootprint()
    {
        return GetLogicalFootprint(kind, cell);
    }

    public static List<Vector2> GetLogicalFootprint(
        WallKind wallKind,
        Vector2Int wallCell)
    {
        var center = SceneGrid.CellCenterLogical(wallCell);
        var halfThickness = WallCellGeometry.ThicknessInCells * 0.5f;
        switch (wallKind)
        {
            case WallKind.Horizontal:
                return new List<Vector2>
                {
                    center + new Vector2(-HalfCell, -halfThickness),
                    center + new Vector2(HalfCell, -halfThickness),
                    center + new Vector2(HalfCell, halfThickness),
                    center + new Vector2(-HalfCell, halfThickness)
                };
            case WallKind.Vertical:
                return new List<Vector2>
                {
                    center + new Vector2(-halfThickness, -HalfCell),
                    center + new Vector2(halfThickness, -HalfCell),
                    center + new Vector2(halfThickness, HalfCell),
                    center + new Vector2(-halfThickness, HalfCell)
                };
            case WallKind.CornerNorthWest:
            case WallKind.CornerNorthEast:
            case WallKind.CornerSouthWest:
            case WallKind.CornerSouthEast:
                var xSign = wallKind is WallKind.CornerNorthWest or WallKind.CornerSouthWest ? 1f : -1f;
                var ySign = wallKind is WallKind.CornerNorthWest or WallKind.CornerNorthEast ? 1f : -1f;
                var cornerFootprint = new List<Vector2>(CornerFootprint.Length);
                foreach (var point in CornerFootprint)
                {
                    cornerFootprint.Add(center + new Vector2(point.x * xSign, point.y * ySign));
                }

                EnsureCounterClockwise(cornerFootprint);
                return cornerFootprint;
            default:
                return new List<Vector2>();
        }
    }

    private static void AddCornerSegments(
        ICollection<PlaneSegment> segments,
        Vector2 center,
        Vector2 horizontalDirection,
        Vector2 verticalDirection)
    {
        segments.Add(new PlaneSegment(center, center + horizontalDirection * HalfCell));
        segments.Add(new PlaneSegment(center, center + verticalDirection * HalfCell));
    }

    private void Rebuild()
    {
        if (!TryGetGrid(out var grid))
        {
            return;
        }

        if (Application.isPlaying)
        {
            ReleaseGeneratedSurfaces();
        }

        ReleaseGeneratedSurfaces();
        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Grid Wall {kind} {cell.x},{cell.y}",
            hideFlags = HideFlags.DontSave
        };

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        var logicalFootprint = GetLogicalFootprint();
        var worldFootprint = new List<Vector3>(logicalFootprint.Count);
        foreach (var logicalPoint in logicalFootprint)
        {
            worldFootprint.Add(ToWorld(grid, logicalPoint));
        }

        for (var index = 0; index < worldFootprint.Count; index++)
        {
            var nextIndex = (index + 1) % worldFootprint.Count;
            if (IsInternalSideEdge(logicalFootprint[index], logicalFootprint[nextIndex]))
            {
                continue;
            }

            var start = worldFootprint[index];
            var end = worldFootprint[nextIndex];
            var sideVertices = new List<Vector3>();
            var sideTriangles = new List<int>();
            var sideColors = new List<Color>();
            var sideColor = GetSideColor(logicalFootprint[index], logicalFootprint[nextIndex]);
            AddQuad(
                sideVertices,
                sideTriangles,
                sideColors,
                start,
                end,
                end + Vector3.up * wallHeight,
                start + Vector3.up * wallHeight,
                sideColor);
            AddQuad(
                vertices,
                triangles,
                colors,
                start,
                end,
                end + Vector3.up * wallHeight,
                start + Vector3.up * wallHeight,
                sideColor);
            CreateSurface(
                $"Side {index}",
                sideVertices,
                sideTriangles,
                sideColors,
                GetSurfaceSortingOrder(logicalFootprint[index], logicalFootprint[nextIndex]),
                worldFootprint,
                (start + end) * 0.5f,
                logicalFootprint[index],
                logicalFootprint[nextIndex]);
        }

        var topVertices = new List<Vector3>();
        var topTriangles = new List<int>();
        var topColors = new List<Color>();
        AddTopFace(topVertices, topTriangles, topColors, worldFootprint, WallTopColor, wallHeight);
        AddTopFace(vertices, triangles, colors, worldFootprint, WallTopColor, wallHeight);
        CreateSurface(
            "Top",
            topVertices,
            topTriangles,
            topColors,
            GetTopSortingOrder(logicalFootprint),
            worldFootprint,
            GetPolygonCenter(worldFootprint),
            logicalFootprint[0],
            logicalFootprint[2]);

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateBounds();

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.enabled = false;
        meshRenderer.sortingOrder = WallBaseSortingOrder - cell.x - cell.y;
        generatedSurfaceCount = transform.childCount;
    }

    private bool TryGetGrid(out SceneGrid grid)
    {
        if (SceneGrid.TryGetForScene(gameObject.scene, out grid))
        {
            return true;
        }

        // SceneGrid may register after this component while the scene is loading.
        var grids = FindObjectsByType<SceneGrid>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var candidate in grids)
        {
            if (candidate.gameObject.scene == gameObject.scene
                && candidate.enabled
                && candidate.gameObject.activeInHierarchy)
            {
                grid = candidate;
                return true;
            }
        }

        grid = null!;
        return false;
    }

    private static void AddTopFace(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        IReadOnlyList<Vector3> footprint,
        Color color,
        float height)
    {
        var vertexOffset = vertices.Count;
        foreach (var point in footprint)
        {
            vertices.Add(point + Vector3.up * height);
            colors.Add(color);
        }

        var remaining = new List<int>(footprint.Count);
        for (var index = 0; index < footprint.Count; index++)
        {
            remaining.Add(index);
        }

        while (remaining.Count > 2)
        {
            var foundEar = false;
            for (var index = 0; index < remaining.Count; index++)
            {
                var previous = remaining[(index - 1 + remaining.Count) % remaining.Count];
                var current = remaining[index];
                var next = remaining[(index + 1) % remaining.Count];
                if (!IsEar(footprint, previous, current, next, remaining))
                {
                    continue;
                }

                AddFacingTriangle(
                    triangles,
                    vertices,
                    vertexOffset + previous,
                    vertexOffset + current,
                    vertexOffset + next);
                remaining.RemoveAt(index);
                foundEar = true;
                break;
            }

            if (!foundEar)
            {
                return;
            }
        }
    }

    private static Vector3 ToWorld(SceneGrid grid, Vector2 logicalPosition)
    {
        var worldPosition = grid.LogicalToWorld(logicalPosition);
        return new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    private static Color GetSideColor(Vector2 start, Vector2 end)
    {
        var delta = end - start;
        return Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? delta.x > 0f ? WallLightColor : WallShadowColor
            : delta.y > 0f ? WallLightColor : WallShadowColor;
    }

    private static int GetSurfaceSortingOrder(Vector2 start, Vector2 end)
    {
        var depth = (start.x + start.y + end.x + end.y) * 0.5f;
        return WallBaseSortingOrder - Mathf.RoundToInt(depth * SurfaceSortingScale);
    }

    private static int GetTopSortingOrder(IReadOnlyList<Vector2> footprint)
    {
        var depth = 0f;
        foreach (var point in footprint)
        {
            depth += point.x + point.y;
        }

        depth /= footprint.Count;
        return WallBaseSortingOrder - Mathf.RoundToInt(depth * SurfaceSortingScale) + WallTopSortingOffset;
    }

    private bool IsInternalSideEdge(Vector2 start, Vector2 end)
    {
        var edgeLength = Vector2.Distance(start, end);
        return Mathf.Abs(edgeLength - HalfCell) <= 0.0001f;
    }

    private static bool IsEar(
        IReadOnlyList<Vector3> footprint,
        int previous,
        int current,
        int next,
        IReadOnlyList<int> remaining)
    {
        var a = footprint[previous];
        var b = footprint[current];
        var c = footprint[next];
        if (Cross(a, b, c) <= 0.0001f)
        {
            return false;
        }

        foreach (var candidate in remaining)
        {
            if (candidate == previous || candidate == current || candidate == next)
            {
                continue;
            }

            if (IsPointInTriangle(footprint[candidate], a, b, c))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPointInTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
    {
        var first = Cross(a, b, point);
        var second = Cross(b, c, point);
        var third = Cross(c, a, point);
        return first >= 0f && second >= 0f && third >= 0f;
    }

    private static float Cross(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
    }

    private static void EnsureCounterClockwise(List<Vector2> points)
    {
        var area = 0f;
        for (var index = 0; index < points.Count; index++)
        {
            var nextIndex = (index + 1) % points.Count;
            area += points[index].x * points[nextIndex].y - points[nextIndex].x * points[index].y;
        }

        if (area < 0f)
        {
            points.Reverse();
        }
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 first,
        Vector3 second,
        Vector3 third,
        Vector3 fourth,
        Color color)
    {
        var offset = vertices.Count;
        vertices.Add(first);
        vertices.Add(second);
        vertices.Add(third);
        vertices.Add(fourth);
        for (var index = 0; index < 4; index++)
        {
            colors.Add(color);
        }

        AddFacingTriangle(triangles, vertices, offset, offset + 1, offset + 2);
        AddFacingTriangle(triangles, vertices, offset, offset + 2, offset + 3);
    }

    private static void AddFacingTriangle(List<int> triangles, List<Vector3> vertices, int first, int second, int third)
    {
        var a = vertices[first];
        var b = vertices[second];
        var c = vertices[third];
        var cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Mathf.Abs(cross) <= 0.0001f)
        {
            return;
        }

        if (cross > 0f)
        {
            triangles.Add(first);
            triangles.Add(second);
            triangles.Add(third);
        }
        else
        {
            triangles.Add(third);
            triangles.Add(second);
            triangles.Add(first);
        }
    }

    private void CreateSurface(
        string surfaceName,
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        int sortingOrder,
        IReadOnlyList<Vector3> groundPolygon,
        Vector3 depthReference,
        Vector2 logicalStart,
        Vector2 logicalEnd)
    {
        var surfaceObject = new GameObject($"{name} {surfaceName}")
        {
            hideFlags = HideFlags.DontSave
        };
        surfaceObject.transform.SetParent(transform, false);

        var surfaceMesh = new Mesh
        {
            name = $"{mesh.name} {surfaceName}",
            hideFlags = HideFlags.DontSave,
            vertices = vertices.ToArray(),
            triangles = triangles.ToArray(),
            colors = colors.ToArray()
        };
        surfaceMesh.RecalculateBounds();

        var filter = surfaceObject.AddComponent<MeshFilter>();
        filter.sharedMesh = surfaceMesh;
        var renderer = surfaceObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
        var occlusionSurface = surfaceObject.AddComponent<DepthOcclusionSurface>();
        occlusionSurface.Configure(
            vertices,
            groundPolygon,
            depthReference,
            logicalStart,
            logicalEnd);
    }

    private static Vector3 GetPolygonCenter(IReadOnlyList<Vector3> polygon)
    {
        var center = Vector3.zero;
        foreach (var point in polygon)
        {
            center += point;
        }

        return center / polygon.Count;
    }

    private void ReleaseGeneratedSurfaces()
    {
        for (var index = transform.childCount - 1; index >= 0; index--)
        {
            var child = transform.GetChild(index).gameObject;
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }

        generatedSurfaceCount = 0;
    }

    private void OnDisable()
    {
        ReleaseGeneratedSurfaces();
    }

    private void ReleaseMesh()
    {
        if (mesh is null || !mesh)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(mesh);
        }
        else
        {
            DestroyImmediate(mesh);
        }

        mesh = null!;
    }

    private void OnDestroy()
    {
        ReleaseGeneratedSurfaces();
        ReleaseMesh();
    }
}
