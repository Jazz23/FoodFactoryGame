using UnityEngine;

[RequireComponent(typeof(SceneGrid), typeof(EdgeCollider2D))]
public sealed class IndoorGrid : MonoBehaviour
{
    [SerializeField] private Vector2Int size = new(2, 2);
    [SerializeField, Min(0.005f)] private float lineWidth = 0.025f;
    [SerializeField] private Color lineColor = new(0.17f, 0.21f, 0.27f, 1f);
    [SerializeField] private int sortingOrder = -10;
    [SerializeField, Min(0f)] private float bottomCollisionPadding = 0.5f;

    private void Awake()
    {
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
        lineObject.transform.SetParent(transform, false);

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

    private Vector2 ToLocalPoint(SceneGrid grid, Vector2 localGridPosition)
    {
        var logicalPosition = grid.LogicalOrigin + localGridPosition;
        var localPosition = transform.InverseTransformPoint(grid.LogicalToWorld(logicalPosition));
        return localPosition;
    }
}
