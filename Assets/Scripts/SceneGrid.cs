using UnityEngine;
using UnityEngine.SceneManagement;

public enum GridProjection
{
    Dimetric,
    Orthogonal
}

public sealed class SceneGrid : MonoBehaviour
{
    [SerializeField] private GridProjection projection;
    [SerializeField] private Vector2 logicalOrigin;
    [SerializeField] private Vector2 visualOrigin;
    [SerializeField, Min(0.01f)] private float cellSize = 1f;
    [SerializeField, Min(0f)] private float verticalMovementMultiplier = 1f;
    [SerializeField, Min(0.1f)] private float orthographicSize = 5f;
    [SerializeField] private Vector2 initialPlayerLogicalPosition;

    public GridProjection Projection => projection;
    public Vector2 LogicalOrigin => logicalOrigin;
    public Vector2 InitialPlayerLogicalPosition => initialPlayerLogicalPosition;
    public float VerticalMovementMultiplier => verticalMovementMultiplier;
    public float OrthographicSize => orthographicSize;

    public Vector2 LogicalToWorld(Vector2 logicalPosition)
    {
        var localPosition = (logicalPosition - logicalOrigin) * cellSize;
        return visualOrigin + Project(projection, localPosition);
    }

    public Vector2 WorldToLogical(Vector2 worldPosition)
    {
        var localPosition = (worldPosition - visualOrigin) / cellSize;
        return logicalOrigin + Unproject(projection, localPosition);
    }

    public static Vector2 Project(GridProjection projection, Vector2 logicalPosition)
    {
        return projection == GridProjection.Dimetric
            ? new Vector2(
                logicalPosition.x - logicalPosition.y,
                (logicalPosition.x + logicalPosition.y) * 0.5f)
            : logicalPosition;
    }

    public static Vector2 Unproject(GridProjection projection, Vector2 worldPosition)
    {
        return projection == GridProjection.Dimetric
            ? new Vector2(
                worldPosition.x * 0.5f + worldPosition.y,
                worldPosition.y - worldPosition.x * 0.5f)
            : worldPosition;
    }

    public static SceneGrid GetForScene(Scene scene)
    {
        var grids = FindObjectsByType<SceneGrid>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (var grid in grids)
        {
            if (grid.gameObject.scene == scene)
            {
                return grid;
            }
        }

        throw new MissingReferenceException($"No SceneGrid exists in scene '{scene.name}'.");
    }
}
