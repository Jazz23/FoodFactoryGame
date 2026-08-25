using UnityEngine;
using UnityEngine.SceneManagement;

public enum SceneDestination
{
    World,
    Inside
}

public sealed class ScenePortal : MonoBehaviour
{
    [SerializeField] private SceneDestination destination;
    [SerializeField] private Vector2 interactionLogicalPosition;
    [SerializeField] private Vector2 arrivalLogicalPosition;
    [SerializeField, Min(0.1f)] private float interactionRadius = 0.9f;

    public SceneDestination Destination => destination;
    public Vector2 ArrivalLogicalPosition => arrivalLogicalPosition;

    public bool CanUse(Vector2 playerPosition)
    {
        var grid = SceneGrid.GetForScene(gameObject.scene);
        var interactionPosition = grid.LogicalToWorld(interactionLogicalPosition);
        return (playerPosition - interactionPosition).sqrMagnitude <= interactionRadius * interactionRadius;
    }

    public static bool TryGetClosest(
        Scene scene,
        Vector2 playerPosition,
        SceneDestination destination,
        out ScenePortal portal)
    {
        var portals = FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        var closestDistance = float.PositiveInfinity;
        portal = null!;

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene || candidate.destination != destination)
            {
                continue;
            }

            var grid = SceneGrid.GetForScene(scene);
            var interactionPosition = grid.LogicalToWorld(candidate.interactionLogicalPosition);
            var distance = (playerPosition - interactionPosition).sqrMagnitude;
            if (distance >= closestDistance || !candidate.CanUse(playerPosition))
            {
                continue;
            }

            closestDistance = distance;
            portal = candidate;
        }

        return portal != null;
    }

    public static bool TryGetClosest(
        Scene scene,
        Vector2 playerPosition,
        out ScenePortal portal)
    {
        var portals = FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        var closestDistance = float.PositiveInfinity;
        portal = null!;

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene || !candidate.CanUse(playerPosition))
            {
                continue;
            }

            var grid = SceneGrid.GetForScene(scene);
            var interactionPosition = grid.LogicalToWorld(candidate.interactionLogicalPosition);
            var distance = (playerPosition - interactionPosition).sqrMagnitude;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            portal = candidate;
        }

        return portal != null;
    }
}
