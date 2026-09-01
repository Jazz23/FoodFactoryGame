// Generates one thick direction-aware perimeter wall and exposes its geometry for diagnostics.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class DirectionalWallSegmentRenderer : MonoBehaviour
{
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    [SerializeField] private GridEdgeDirection direction;
    [SerializeField] private GridEdge edge;
    [SerializeField] private float wallHeight;
    [SerializeField] private float lipBottomHeight;
    [SerializeField] private bool isDegenerate;
    [SerializeField] private Vector3 localStart;
    [SerializeField] private Vector3 localEnd;
    [SerializeField] private Vector3[] localFootprint = System.Array.Empty<Vector3>();
    [SerializeField] private int baseSortingOrder;
    [SerializeField] private bool hasStartCap;
    [SerializeField] private bool hasEndCap;

    private Mesh mesh = null!;
    private MeshRenderer meshRenderer = null!;

    public GridEdgeDirection Direction => direction;
    public GridEdge Edge => edge;
    public float WallHeight => wallHeight;
    public float LipBottomHeight => lipBottomHeight;
    public bool IsDegenerate => isDegenerate;
    public Vector3 WorldStart => transform.TransformPoint(localStart);
    public Vector3 WorldEnd => transform.TransformPoint(localEnd);
    public Bounds WorldBounds => GetMeshRenderer().bounds;
    public int SortingOrder => GetMeshRenderer().sortingOrder;
    public float ThicknessInCells => WallCellGeometry.ThicknessInCells;
    public bool HasStartCap => hasStartCap;
    public bool HasEndCap => hasEndCap;

    private void Awake()
    {
        CacheSerializedMesh();
    }

    private void OnEnable()
    {
        CacheSerializedMesh();
    }

    public void Configure(
        GridEdgeDirection direction,
        GridEdge edge,
        Vector3 startWorld,
        Vector3 endWorld,
        Vector3 thicknessWorld,
        bool includeStartCap,
        bool includeEndCap,
        BuildingVisualStyle style,
        bool includeCollider)
    {
        this.direction = direction;
        this.edge = edge;
        wallHeight = style.WallHeight;
        lipBottomHeight = style.WallHeight - style.RoofLipHeight;
        localStart = transform.InverseTransformPoint(startWorld);
        localEnd = transform.InverseTransformPoint(endWorld);
        hasStartCap = includeStartCap;
        hasEndCap = includeEndCap;
        var halfThickness = transform.InverseTransformVector(thicknessWorld) * 0.5f;
        localFootprint = new[]
        {
            localStart - halfThickness,
            localEnd - halfThickness,
            localEnd + halfThickness,
            localStart + halfThickness
        };
        EnsureCounterClockwise(localFootprint);

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        AddSideFaces(vertices, triangles, colors, style);
        AddTopFace(vertices, triangles, colors, style.RoofColor);
        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Wall {direction} {edge.Corner.x},{edge.Corner.y}",
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
        baseSortingOrder = style.GetWallSortingOrder(direction, edge);
        meshRenderer.sortingOrder = baseSortingOrder;

        ConfigureCollider(includeCollider);
        SetPresentation(Color.white, 0);
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

    public Vector3[] GetWorldFootprint()
    {
        var points = new Vector3[localFootprint.Length];
        for (var index = 0; index < localFootprint.Length; index++)
        {
            points[index] = transform.TransformPoint(localFootprint[index]);
        }

        return points;
    }

    private void AddSideFaces(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        BuildingVisualStyle style)
    {
        for (var index = 0; index < localFootprint.Length; index++)
        {
            var nextIndex = (index + 1) % localFootprint.Length;
            var start = localFootprint[index];
            var end = localFootprint[nextIndex];
            var midpoint = (start + end) * 0.5f;
            if (!hasStartCap && Vector3.Distance(midpoint, localStart) <= 0.0001f
                || !hasEndCap && Vector3.Distance(midpoint, localEnd) <= 0.0001f)
            {
                continue;
            }

            AddQuad(
                vertices,
                triangles,
                colors,
                start,
                end,
                end + Vector3.up * lipBottomHeight,
                start + Vector3.up * lipBottomHeight,
                style.GetWallColor(direction));
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
        var offset = vertices.Count;
        foreach (var point in localFootprint)
        {
            vertices.Add(point + Vector3.up * wallHeight);
            colors.Add(color);
        }

        AddTriangle(triangles, vertices, offset, offset + 1, offset + 2);
        AddTriangle(triangles, vertices, offset, offset + 2, offset + 3);
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

    private static float Cross(Vector3 first, Vector3 second, Vector3 third)
    {
        return (second.x - first.x) * (third.y - first.y)
            - (second.y - first.y) * (third.x - first.x);
    }

    private static void EnsureCounterClockwise(Vector3[] points)
    {
        var signedArea = 0f;
        for (var index = 0; index < points.Length; index++)
        {
            var next = points[(index + 1) % points.Length];
            signedArea += points[index].x * next.y - next.x * points[index].y;
        }

        if (signedArea < 0f)
        {
            System.Array.Reverse(points);
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
