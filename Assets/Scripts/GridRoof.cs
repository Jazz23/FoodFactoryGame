// Generates a floor or ceiling slab over a logical grid rectangle without adding collision.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class GridRoof : MonoBehaviour
{
    [SerializeField] private Vector2 logicalMin;
    [SerializeField] private Vector2 logicalMax = Vector2.one;
    [SerializeField, Min(0f)] private float topHeight = 2.1f;
    [SerializeField, Min(0f)] private float thickness = 0.1f;
    [SerializeField, Min(0f)] private float baseHeight;
    [SerializeField] private Color topColor = new(0.035f, 0.05f, 0.075f, 1f);
    [SerializeField] private Color sideColor = new(0.16f, 0.21f, 0.26f, 1f);
    [SerializeField] private Material material = null!;
    [SerializeField] private int sortingOrder = 1000;

    private Mesh mesh = null!;
    private bool rebuildRequested;
    private int generatedSurfaceCount;

    public Vector2 LogicalMin => logicalMin;
    public Vector2 LogicalMax => logicalMax;
    public float TopHeight => topHeight;
    public float Thickness => thickness;
    public float BaseHeight => baseHeight;
    public int SortingOrder => sortingOrder;

    private void OnEnable()
    {
        GetComponent<MeshRenderer>().enabled = false;
        rebuildRequested = true;
        RebuildIfRequired();
    }

    private void OnValidate()
    {
        GetComponent<MeshRenderer>().enabled = false;
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

    private void Rebuild()
    {
        var renderer = GetComponent<MeshRenderer>();
        renderer.enabled = false;
        if (!TryGetGrid(out var grid))
        {
            return;
        }

        ReleaseGeneratedSurfaces();
        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Grid Roof {logicalMin.x:0.###},{logicalMin.y:0.###} {logicalMax.x:0.###},{logicalMax.y:0.###}",
            hideFlags = HideFlags.DontSave
        };

        var min = Vector2.Min(logicalMin, logicalMax);
        var max = Vector2.Max(logicalMin, logicalMax);
        var footprint = new[]
        {
            ToWorld(grid, new Vector2(min.x, min.y)),
            ToWorld(grid, new Vector2(max.x, min.y)),
            ToWorld(grid, new Vector2(max.x, max.y)),
            ToWorld(grid, new Vector2(min.x, max.y))
        };
        var logicalFootprint = new[]
        {
            new Vector2(min.x, min.y),
            new Vector2(max.x, min.y),
            new Vector2(max.x, max.y),
            new Vector2(min.x, max.y)
        };

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        var bottomHeight = Mathf.Max(0f, topHeight - thickness);
        for (var index = 0; index < footprint.Length; index++)
        {
            var nextIndex = (index + 1) % footprint.Length;
            var edgePoints = GetSplitEdgePoints(
                logicalFootprint[index],
                logicalFootprint[nextIndex]);
            for (var segmentIndex = 0; segmentIndex < edgePoints.Count - 1; segmentIndex++)
            {
                var logicalStart = edgePoints[segmentIndex];
                var logicalEnd = edgePoints[segmentIndex + 1];
                var start = ToWorld(grid, logicalStart);
                var end = ToWorld(grid, logicalEnd);
                var sideVertices = new List<Vector3>();
                var sideTriangles = new List<int>();
                var sideColors = new List<Color>();
                AddQuad(
                    sideVertices,
                    sideTriangles,
                    sideColors,
                    start + Vector3.up * bottomHeight,
                    end + Vector3.up * bottomHeight,
                    end + Vector3.up * topHeight,
                    start + Vector3.up * topHeight,
                    sideColor);
                AddQuad(
                    vertices,
                    triangles,
                    colors,
                    start + Vector3.up * bottomHeight,
                    end + Vector3.up * bottomHeight,
                    end + Vector3.up * topHeight,
                    start + Vector3.up * topHeight,
                    sideColor);
                CreateSurface(
                    $"Side {index} Segment {segmentIndex}",
                    sideVertices,
                    sideTriangles,
                    sideColors,
                    GridWall.GetSurfaceSortingOrder(
                        logicalStart,
                        logicalEnd,
                        baseHeight,
                        sortingOrder) + 1,
                    footprint,
                    (start + end) * 0.5f,
                    logicalStart,
                    logicalEnd);
            }
        }

        var topVertices = new List<Vector3>();
        var topTriangles = new List<int>();
        var topColors = new List<Color>();
        AddQuad(
            topVertices,
            topTriangles,
            topColors,
            footprint[0] + Vector3.up * topHeight,
            footprint[1] + Vector3.up * topHeight,
            footprint[2] + Vector3.up * topHeight,
            footprint[3] + Vector3.up * topHeight,
            topColor);
        AddQuad(
            vertices,
            triangles,
            colors,
            footprint[0] + Vector3.up * topHeight,
            footprint[1] + Vector3.up * topHeight,
            footprint[2] + Vector3.up * topHeight,
            footprint[3] + Vector3.up * topHeight,
            topColor);
        var minimumDepth = float.PositiveInfinity;
        foreach (var point in logicalFootprint)
        {
            minimumDepth = Mathf.Min(minimumDepth, point.x + point.y);
        }

        var topSortingOrder = GridWall.GetTopSortingOrderAtDepth(
            minimumDepth - WallCellGeometry.ThicknessInCells * 0.5f,
            baseHeight,
            sortingOrder) + 1;
        CreateSurface(
            "Top",
            topVertices,
            topTriangles,
            topColors,
            topSortingOrder,
            footprint,
            GetPolygonCenter(footprint),
            logicalFootprint[0],
            logicalFootprint[2]);

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateBounds();

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
        generatedSurfaceCount = transform.childCount;
    }

    private bool TryGetGrid(out SceneGrid grid)
    {
        if (SceneGrid.TryGetForScene(gameObject.scene, out grid))
        {
            return true;
        }

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

    private static Vector3 ToWorld(SceneGrid grid, Vector2 logicalPosition)
    {
        var worldPosition = grid.LogicalToWorld(logicalPosition);
        return new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    private static List<Vector2> GetSplitEdgePoints(Vector2 start, Vector2 end)
    {
        var points = new List<Vector2> { start };
        var isHorizontal = !Mathf.Approximately(start.x, end.x);
        var startAxis = isHorizontal ? start.x : start.y;
        var endAxis = isHorizontal ? end.x : end.y;
        var direction = Mathf.Sign(endAxis - startAxis);
        if (direction == 0f)
        {
            points.Add(end);
            return points;
        }

        var boundary = direction > 0f
            ? Mathf.Floor(startAxis) + 1f
            : Mathf.Ceil(startAxis) - 1f;
        while ((direction > 0f && boundary < endAxis - 0.0001f)
            || (direction < 0f && boundary > endAxis + 0.0001f))
        {
            points.Add(isHorizontal
                ? new Vector2(boundary, start.y)
                : new Vector2(start.x, boundary));
            boundary += direction;
        }

        points.Add(end);
        return points;
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

    private void CreateSurface(
        string surfaceName,
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        int surfaceSortingOrder,
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
        renderer.sortingOrder = surfaceSortingOrder;
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
