// Generates a prism mesh for a wall centered on a grid cell, half a cell thick, projected dimetrically via the scene's SceneGrid.
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class GridWall : MonoBehaviour
{
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
    private const float Thickness = 0.25f;
    private const int WallBaseSortingOrder = 20;

    private static readonly Color SouthEastColor = new(0.45f, 0.52f, 0.58f, 1f);
    private static readonly Color NorthWestColor = new(0.28f, 0.35f, 0.41f, 1f);
    private static readonly Color TopColor = new(0.035f, 0.05f, 0.075f, 1f);

    private Mesh mesh = null!;

    public WallKind Kind => kind;
    public Vector2Int Cell => cell;

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        Rebuild();
    }

    private void Update()
    {
        if (GetComponent<MeshFilter>().sharedMesh is null)
        {
            Rebuild();
        }
    }

    public List<Rect> GetLogicalRects()
    {
        var rects = new List<Rect>();
        switch (kind)
        {
            case WallKind.Horizontal:
                AddHorizontalArm(rects);
                break;
            case WallKind.Vertical:
                AddVerticalArm(rects);
                break;
            case WallKind.CornerNorthWest:
                AddHorizontalArm(rects);
                AddWestConnector(rects);
                break;
            case WallKind.CornerNorthEast:
                AddHorizontalArm(rects);
                AddEastConnector(rects);
                break;
            case WallKind.CornerSouthWest:
                AddHorizontalArm(rects);
                AddWestConnector(rects, downward: false);
                break;
            case WallKind.CornerSouthEast:
                AddHorizontalArm(rects);
                AddEastConnector(rects, downward: false);
                break;
        }

        return rects;
    }

    public Vector3[] GetWorldFootprint()
    {
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            return System.Array.Empty<Vector3>();
        }

        var footprint = new List<Vector3>();
        foreach (var rect in GetLogicalRects())
        {
            footprint.Add(ToWorld(grid, rect.xMin, rect.yMin));
            footprint.Add(ToWorld(grid, rect.xMax, rect.yMin));
            footprint.Add(ToWorld(grid, rect.xMax, rect.yMax));
            footprint.Add(ToWorld(grid, rect.xMin, rect.yMax));
        }

        return footprint.ToArray();
    }

    private static void AddHorizontalArm(ICollection<Rect> rects)
    {
        rects.Add(new Rect(-HalfCell, -Thickness, 2f * HalfCell, 2f * Thickness));
    }

    private static void AddVerticalArm(ICollection<Rect> rects)
    {
        rects.Add(new Rect(-Thickness, -HalfCell, 2f * Thickness, 2f * HalfCell));
    }

    private static void AddWestConnector(ICollection<Rect> rects, bool downward = true)
    {
        var y = downward ? -HalfCell : Thickness;
        rects.Add(new Rect(-Thickness, y, 2f * Thickness, Thickness));
    }

    private static void AddEastConnector(ICollection<Rect> rects, bool downward = true)
    {
        var y = downward ? -HalfCell : Thickness;
        rects.Add(new Rect(-Thickness, y, 2f * Thickness, Thickness));
    }

    private void Rebuild()
    {
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            return;
        }

        ReleaseMesh();
        mesh = new Mesh
        {
            name = $"Grid Wall {kind} {cell.x},{cell.y}",
            hideFlags = HideFlags.DontSave
        };

        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var colors = new List<Color>();
        foreach (var rect in GetLogicalRects())
        {
            AddPrism(vertices, triangles, colors, grid, rect);
        }

        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateBounds();

        var filter = GetComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var meshRenderer = GetComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.sortingOrder = WallBaseSortingOrder + cell.x + cell.y;
    }

    private void AddPrism(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        SceneGrid grid,
        Rect rect)
    {
        var bottomLeft = ToWorld(grid, rect.xMin, rect.yMin);
        var bottomRight = ToWorld(grid, rect.xMax, rect.yMin);
        var topRight = ToWorld(grid, rect.xMax, rect.yMax);
        var topLeft = ToWorld(grid, rect.xMin, rect.yMax);

        AddQuad(vertices, triangles, colors, bottomLeft, bottomRight, SouthEastColor, wallHeight);
        AddQuad(vertices, triangles, colors, bottomRight, topRight, NorthWestColor, wallHeight);
        AddQuad(vertices, triangles, colors, topRight, topLeft, NorthWestColor, wallHeight);
        AddQuad(vertices, triangles, colors, topLeft, bottomLeft, SouthEastColor, wallHeight);
        AddTopFace(vertices, triangles, colors, bottomLeft, bottomRight, topRight, topLeft, wallHeight);
    }

    private Vector3 ToWorld(SceneGrid grid, float logicalX, float logicalY)
    {
        var logicalPosition = new Vector2(cell.x + logicalX, cell.y + logicalY);
        var worldPosition = grid.LogicalToWorld(logicalPosition);
        return new Vector3(worldPosition.x, worldPosition.y, 0f);
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 start,
        Vector3 end,
        Color color,
        float height)
    {
        var offset = vertices.Count;
        vertices.Add(start);
        vertices.Add(end);
        vertices.Add(end + Vector3.up * height);
        vertices.Add(start + Vector3.up * height);
        for (var index = 0; index < 4; index++)
        {
            colors.Add(color);
        }

        AddFacingTriangle(triangles, vertices, offset, offset + 1, offset + 2);
        AddFacingTriangle(triangles, vertices, offset, offset + 2, offset + 3);
    }

    private static void AddTopFace(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 bottomLeft,
        Vector3 bottomRight,
        Vector3 topRight,
        Vector3 topLeft,
        float height)
    {
        var offset = vertices.Count;
        vertices.Add(bottomLeft + Vector3.up * height);
        vertices.Add(bottomRight + Vector3.up * height);
        vertices.Add(topRight + Vector3.up * height);
        vertices.Add(topLeft + Vector3.up * height);
        for (var index = 0; index < 4; index++)
        {
            colors.Add(TopColor);
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
        ReleaseMesh();
    }
}
