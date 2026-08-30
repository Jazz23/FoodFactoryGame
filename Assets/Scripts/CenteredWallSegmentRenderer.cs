// Generates a centered half-cell wall prism and matching full-footprint collision.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class CenteredWallSegmentRenderer : MonoBehaviour
{
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    [SerializeField] private WallCellShape shape;
    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private float wallHeight;
    [SerializeField] private float lipBottomHeight;
    [SerializeField] private bool isDegenerate;
    [SerializeField] private Vector3 localCenter;
    [SerializeField] private Vector3[] localFootprint = System.Array.Empty<Vector3>();
    [SerializeField] private Vector3[] localCellBoundary = System.Array.Empty<Vector3>();
    [SerializeField] private int baseSortingOrder;

    private Mesh mesh = null!;
    private MeshRenderer meshRenderer = null!;

    public WallCellShape Shape => shape;
    public Vector3Int AnchorCell => anchorCell;
    public float WallHeight => wallHeight;
    public float LipBottomHeight => lipBottomHeight;
    public float ThicknessInCells => WallCellGeometry.ThicknessInCells;
    public bool IsDegenerate => isDegenerate;
    public Vector3 WorldCenter => transform.TransformPoint(localCenter);
    public Bounds WorldBounds => GetMeshRenderer().bounds;
    public int SortingOrder => GetMeshRenderer().sortingOrder;

    private void Awake()
    {
        CacheSerializedMesh();
    }

    private void OnEnable()
    {
        CacheSerializedMesh();
    }

    public void Configure(
        WallCellShape shape,
        Vector3Int anchorCell,
        Tilemap ground,
        BuildingVisualStyle style,
        bool includeCollider)
    {
        this.shape = shape;
        this.anchorCell = anchorCell;
        wallHeight = style.WallHeight;
        lipBottomHeight = style.WallHeight - style.RoofLipHeight;

        var worldFootprint = WallCellGeometry.GetWorldFootprint(
            shape,
            anchorCell,
            ground,
            transform.position.z);
        var worldCellBoundary = WallCellGeometry.GetWorldCellBoundary(
            anchorCell,
            ground,
            transform.position.z);
        localFootprint = ToLocalPoints(worldFootprint);
        localCellBoundary = ToLocalPoints(worldCellBoundary);
        localCenter = transform.InverseTransformPoint(ground.GetCellCenterWorld(anchorCell));
        localCenter.z = 0f;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        var logicalFootprint = WallCellGeometry.GetLogicalFootprint(shape);
        AddSideFaces(vertices, triangles, colors, logicalFootprint, style);
        AddTopFace(vertices, triangles, colors, style.RoofColor);

        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Centered Wall {shape} {anchorCell.x},{anchorCell.y}",
            vertices = vertices.ToArray(),
            triangles = triangles.ToArray(),
            colors = colors.ToArray()
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        isDegenerate = triangles.Count == 0;

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = style.ModuleMaterial;
        baseSortingOrder = style.GetWallCellSortingOrder(anchorCell);
        meshRenderer.sortingOrder = baseSortingOrder;

        ConfigureCollider(includeCollider);
        SetPresentation(Color.white, 0);
    }

    public Vector3[] GetWorldFootprint()
    {
        return ToWorldPoints(localFootprint);
    }

    public Vector3[] GetWorldCellBoundary()
    {
        return ToWorldPoints(localCellBoundary);
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        var renderer = GetMeshRenderer();
        renderer.sortingOrder = baseSortingOrder + sortingOrderOffset;
        var propertyBlock = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorPropertyId, colorMultiplier);
        renderer.SetPropertyBlock(propertyBlock);
    }

    private void AddSideFaces(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        IReadOnlyList<Vector2> logicalFootprint,
        BuildingVisualStyle style)
    {
        for (var index = 0; index < localFootprint.Length; index++)
        {
            var nextIndex = (index + 1) % localFootprint.Length;
            var start = localFootprint[index];
            var end = localFootprint[nextIndex];
            var wallColor = GetEdgeColor(
                logicalFootprint[index],
                logicalFootprint[nextIndex],
                style);
            AddQuad(
                vertices,
                triangles,
                colors,
                start,
                end,
                end + Vector3.up * lipBottomHeight,
                start + Vector3.up * lipBottomHeight,
                wallColor);
            AddQuad(
                vertices,
                triangles,
                colors,
                start + Vector3.up * lipBottomHeight,
                end + Vector3.up * lipBottomHeight,
                end + Vector3.up * wallHeight,
                start + Vector3.up * wallHeight,
                style.RoofColor);
        }
    }

    private void AddTopFace(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Color color)
    {
        var vertexOffset = vertices.Count;
        foreach (var point in localFootprint)
        {
            vertices.Add(point + Vector3.up * wallHeight);
            colors.Add(color);
        }

        var remaining = new List<int>(localFootprint.Length);
        for (var index = 0; index < localFootprint.Length; index++)
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
                if (!IsEar(previous, current, next, remaining))
                {
                    continue;
                }

                AddTriangle(
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

    private bool IsEar(int previous, int current, int next, IReadOnlyList<int> remaining)
    {
        var a = localFootprint[previous];
        var b = localFootprint[current];
        var c = localFootprint[next];
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

            if (IsPointInTriangle(localFootprint[candidate], a, b, c))
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

    private static Color GetEdgeColor(
        Vector2 start,
        Vector2 end,
        BuildingVisualStyle style)
    {
        var delta = end - start;
        var direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? delta.x > 0f ? GridEdgeDirection.South : GridEdgeDirection.North
            : delta.y > 0f ? GridEdgeDirection.East : GridEdgeDirection.West;
        return style.GetWallColor(direction);
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
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        AddTriangle(triangles, vertices, offset, offset + 1, offset + 2);
        AddTriangle(triangles, vertices, offset, offset + 2, offset + 3);
    }

    private static void AddTriangle(
        List<int> triangles,
        IReadOnlyList<Vector3> vertices,
        int first,
        int second,
        int third)
    {
        var cross = Cross(vertices[first], vertices[second], vertices[third]);
        if (Mathf.Abs(cross) <= 0.0001f)
        {
            return;
        }

        triangles.Add(first);
        if (cross > 0f)
        {
            triangles.Add(second);
            triangles.Add(third);
        }
        else
        {
            triangles.Add(third);
            triangles.Add(second);
        }
    }

    private void ConfigureCollider(bool includeCollider)
    {
        if (TryGetComponent<EdgeCollider2D>(out var staleEdgeCollider))
        {
            staleEdgeCollider.enabled = false;
            DestroyComponent(staleEdgeCollider);
        }

        if (!TryGetComponent<PolygonCollider2D>(out var collider))
        {
            if (!includeCollider)
            {
                return;
            }

            collider = gameObject.AddComponent<PolygonCollider2D>();
        }

        collider.enabled = includeCollider;
        if (!includeCollider)
        {
            return;
        }

        var points = new Vector2[localFootprint.Length];
        for (var index = 0; index < localFootprint.Length; index++)
        {
            points[index] = localFootprint[index];
        }

        collider.pathCount = 1;
        collider.SetPath(0, points);
    }

    private Vector3[] ToLocalPoints(IReadOnlyList<Vector3> worldPoints)
    {
        var points = new Vector3[worldPoints.Count];
        for (var index = 0; index < worldPoints.Count; index++)
        {
            points[index] = transform.InverseTransformPoint(worldPoints[index]);
        }

        return points;
    }

    private Vector3[] ToWorldPoints(IReadOnlyList<Vector3> localPoints)
    {
        var points = new Vector3[localPoints.Count];
        for (var index = 0; index < localPoints.Count; index++)
        {
            points[index] = transform.TransformPoint(localPoints[index]);
        }

        return points;
    }

    private static void DestroyComponent(Object component)
    {
        if (Application.isPlaying)
        {
            Destroy(component);
        }
        else
        {
            DestroyImmediate(component);
        }
    }

    private void OnDestroy()
    {
        ReleaseMesh();
    }

    private void CacheSerializedMesh()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        mesh = GetComponent<MeshFilter>().sharedMesh;
    }

    private MeshRenderer GetMeshRenderer()
    {
        if (meshRenderer is null || !meshRenderer)
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        return meshRenderer;
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
}
