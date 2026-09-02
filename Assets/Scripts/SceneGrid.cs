// Converts continuous logical positions to world positions for each scene's grid projection.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum GridProjection
{
    Dimetric,
    Orthogonal
}

public sealed class SceneGrid : MonoBehaviour
{
    private static readonly Dictionary<int, SceneGrid> sceneGrids = new();
    private static readonly HashSet<int> unresolvedScenes = new();
    private static readonly HashSet<int> reportedScenes = new();

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

    public static Vector2 CellCenterLogical(Vector2Int cell)
    {
        return (Vector2)cell + new Vector2(0.5f, 0.5f);
    }

    public Vector2 CellCenterWorld(Vector2Int cell)
    {
        return LogicalToWorld(CellCenterLogical(cell));
    }

    private void OnEnable()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid())
        {
            return;
        }

        if (sceneGrids.TryGetValue(scene.handle, out var grid) && grid != this)
        {
            sceneGrids.Remove(scene.handle);
        }
        else
        {
            sceneGrids[scene.handle] = this;
        }

        unresolvedScenes.Remove(scene.handle);
        reportedScenes.Remove(scene.handle);
    }

    private void OnDisable()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid())
        {
            return;
        }

        if (sceneGrids.TryGetValue(scene.handle, out var grid) && grid == this)
        {
            sceneGrids.Remove(scene.handle);
        }

        unresolvedScenes.Remove(scene.handle);
        reportedScenes.Remove(scene.handle);
    }

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

    public static bool TryGetForScene(Scene scene, out SceneGrid grid)
    {
        grid = null!;
        if (!scene.IsValid() || unresolvedScenes.Contains(scene.handle))
        {
            return false;
        }

        if (sceneGrids.TryGetValue(scene.handle, out var cachedGrid)
            && cachedGrid.gameObject.scene == scene
            && cachedGrid.isActiveAndEnabled)
        {
            grid = cachedGrid;
            return true;
        }

        sceneGrids.Remove(scene.handle);
        var grids = FindObjectsByType<SceneGrid>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        var resolvedGrid = grid;

        foreach (var candidate in grids)
        {
            if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (resolvedGrid != null)
            {
                unresolvedScenes.Add(scene.handle);
                return false;
            }

            resolvedGrid = candidate;
        }

        if (resolvedGrid == null)
        {
            unresolvedScenes.Add(scene.handle);
            return false;
        }

        sceneGrids[scene.handle] = resolvedGrid;
        grid = resolvedGrid;
        return true;
    }

    public static void LogMissingGrid(Scene scene, Object context)
    {
        if (!scene.IsValid() || !reportedScenes.Add(scene.handle))
        {
            return;
        }

        Debug.LogError($"Scene '{scene.name}' needs exactly one enabled SceneGrid.", context);
    }
}
