// Generates one direction-aware wall mesh and exposes its geometry for editor diagnostics.
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
    [SerializeField] private int baseSortingOrder;

    private Mesh mesh = null!;
    private MeshRenderer meshRenderer = null!;

    public GridEdgeDirection Direction => direction;
    public GridEdge Edge => edge;
    public float WallHeight => wallHeight;
    public float LipBottomHeight => lipBottomHeight;
    public bool IsDegenerate => isDegenerate;
    public Vector3 WorldStart => transform.TransformPoint(localStart);
    public Vector3 WorldEnd => transform.TransformPoint(localEnd);
    public Bounds WorldBounds => meshRenderer.bounds;
    public int SortingOrder => meshRenderer.sortingOrder;

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
        BuildingVisualStyle style,
        bool includeCollider)
    {
        this.direction = direction;
        this.edge = edge;
        wallHeight = style.WallHeight;
        lipBottomHeight = style.WallHeight - style.RoofLipHeight;
        localStart = transform.InverseTransformPoint(startWorld);
        localEnd = transform.InverseTransformPoint(endWorld);

        var meshStart = localStart;
        var meshEnd = localEnd;
        if (meshEnd.x < meshStart.x)
        {
            (meshStart, meshEnd) = (meshEnd, meshStart);
        }

        var vertices = GetVertices(meshStart, meshEnd);
        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Wall {direction} {edge.Corner.x},{edge.Corner.y}",
            vertices = vertices,
            triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                4, 5, 6,
                4, 6, 7
            },
            colors = new[]
            {
                style.GetWallColor(direction),
                style.GetWallColor(direction),
                style.GetWallColor(direction),
                style.GetWallColor(direction),
                style.RoofColor,
                style.RoofColor,
                style.RoofColor,
                style.RoofColor
            }
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        var first = vertices[0];
        var second = vertices[1];
        var third = vertices[2];
        isDegenerate = Mathf.Abs(
            (second.x - first.x) * (third.y - first.y)
            - (second.y - first.y) * (third.x - first.x)) < 0.0001f;

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = style.ModuleMaterial;
        baseSortingOrder = style.GetWallSortingOrder(direction, edge);
        meshRenderer.sortingOrder = baseSortingOrder;

        ConfigureCollider(includeCollider, style.WallColliderRadius);
        SetPresentation(Color.white, 0);
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        meshRenderer.sortingOrder = baseSortingOrder + sortingOrderOffset;
        var propertyBlock = new MaterialPropertyBlock();
        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorPropertyId, colorMultiplier);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private Vector3[] GetVertices(Vector3 start, Vector3 end)
    {
        return new[]
        {
            start,
            end,
            end + Vector3.up * LipBottomHeight,
            start + Vector3.up * LipBottomHeight,
            start + Vector3.up * LipBottomHeight,
            end + Vector3.up * LipBottomHeight,
            end + Vector3.up * WallHeight,
            start + Vector3.up * WallHeight
        };
    }

    private void ConfigureCollider(bool includeCollider, float radius)
    {
        if (!TryGetComponent<EdgeCollider2D>(out var edgeCollider))
        {
            if (!includeCollider)
            {
                return;
            }

            edgeCollider = gameObject.AddComponent<EdgeCollider2D>();
        }

        edgeCollider.enabled = includeCollider;
        if (!includeCollider)
        {
            return;
        }

        edgeCollider.edgeRadius = radius;
        edgeCollider.points = new[]
        {
            new Vector2(localStart.x, localStart.y),
            new Vector2(localEnd.x, localEnd.y)
        };
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
