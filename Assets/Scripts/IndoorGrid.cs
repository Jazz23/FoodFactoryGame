// Generates and rebuilds the floor grid and boundary collision for interior scenes.
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SceneGrid), typeof(EdgeCollider2D))]
public sealed class IndoorGrid : MonoBehaviour
{
    private const string GeneratedRootName = "Generated Grid Lines";

    [SerializeField] private Vector2Int size = new(2, 2);
    [SerializeField, Min(0.005f)] private float lineWidth = 0.025f;
    [SerializeField] private Color lineColor = new(0.17f, 0.21f, 0.27f, 1f);
    [SerializeField] private int sortingOrder = -10;
    [SerializeField, Min(0f)] private float bottomCollisionPadding = 0.5f;

    private Transform generatedRoot = null!;

    public Vector2Int Size => size;

    private void Awake()
    {
        Rebuild();
    }

    public void ConfigureSize(Vector2Int newSize)
    {
        if (!BuildingFootprint.IsValid(newSize))
        {
            return;
        }

        size = newSize;
        Rebuild();
    }

    public static bool TryConfigureForScene(Scene scene, Vector2Int newSize)
    {
        if (!BuildingFootprint.IsValid(newSize))
        {
            return false;
        }

        var indoorGrids = FindObjectsByType<IndoorGrid>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var indoorGrid in indoorGrids)
        {
            if (indoorGrid.gameObject.scene != scene)
            {
                continue;
            }

            indoorGrid.ConfigureSize(newSize);
            return true;
        }

        return false;
    }

    private void Rebuild()
    {
        generatedRoot = GetGeneratedRoot();
        ClearGeneratedLines();

        var grid = GetComponent<SceneGrid>();
        var edgeCollider = GetComponent<EdgeCollider2D>();

        for (var x = 0; x <= size.x; x++)
        {
            CreateLine(
                grid,
                new Vector2(x, 0f),
                new Vector2(x, size.y));
        }

        for (var y = 0; y <= size.y; y++)
        {
            CreateLine(
                grid,
                new Vector2(0f, y),
                new Vector2(size.x, y));
        }

        var bottom = -bottomCollisionPadding;
        edgeCollider.points = new[]
        {
            ToLocalPoint(grid, new Vector2(0f, bottom)),
            ToLocalPoint(grid, new Vector2(size.x, bottom)),
            ToLocalPoint(grid, new Vector2(size.x, size.y)),
            ToLocalPoint(grid, new Vector2(0f, size.y)),
            ToLocalPoint(grid, new Vector2(0f, bottom))
        };
    }

    private void CreateLine(SceneGrid grid, Vector2 start, Vector2 end)
    {
        var lineObject = new GameObject("Grid Line");
        lineObject.transform.SetParent(generatedRoot, false);

        var line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.startColor = lineColor;
        line.endColor = lineColor;
        line.numCapVertices = 0;
        line.numCornerVertices = 0;
        line.sortingOrder = sortingOrder;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.SetPosition(0, ToLocalPoint(grid, start));
        line.SetPosition(1, ToLocalPoint(grid, end));
    }

    private Transform GetGeneratedRoot()
    {
        var existingRoot = transform.Find(GeneratedRootName);
        if (existingRoot is not null && existingRoot)
        {
            return existingRoot;
        }

        var generatedObject = new GameObject(GeneratedRootName);
        generatedObject.transform.SetParent(transform, false);
        return generatedObject.transform;
    }

    private void ClearGeneratedLines()
    {
        for (var index = generatedRoot.childCount - 1; index >= 0; index--)
        {
            var child = generatedRoot.GetChild(index).gameObject;
            if (Application.isPlaying)
            {
                child.transform.SetParent(null, false);
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private Vector2 ToLocalPoint(SceneGrid grid, Vector2 localGridPosition)
    {
        var logicalPosition = grid.LogicalOrigin + localGridPosition;
        var localPosition = transform.InverseTransformPoint(grid.LogicalToWorld(logicalPosition));
        return localPosition;
    }
}
