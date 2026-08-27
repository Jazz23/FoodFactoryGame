// Represents either an authored scene portal or an exact placed-building entrance.
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
    [SerializeField] private string destinationSceneName = string.Empty;
    [SerializeField] private Vector2 interactionLogicalPosition;
    [SerializeField] private Vector2 arrivalLogicalPosition;
    [SerializeField, Min(0.1f)] private float interactionRadius = 0.9f;

    private uint buildingInstanceId;
    private bool usesWorldInteractionPosition;
    private Vector2 worldInteractionPosition;
    private Vector2 exteriorArrivalLogicalPosition;

    public SceneDestination Destination => destination;
    public string DestinationSceneName => destinationSceneName;
    public Vector2 ArrivalLogicalPosition => arrivalLogicalPosition;
    public Vector2 ExteriorArrivalLogicalPosition => exteriorArrivalLogicalPosition;
    public uint BuildingInstanceId => buildingInstanceId;

    public void ConfigureBuilding(
        uint instanceId,
        Vector2 interactionWorldPosition,
        string interiorScene,
        Vector2 interiorArrivalLogicalPosition,
        Vector2 exteriorArrivalPosition)
    {
        buildingInstanceId = instanceId;
        usesWorldInteractionPosition = true;
        worldInteractionPosition = interactionWorldPosition;
        destinationSceneName = interiorScene;
        arrivalLogicalPosition = interiorArrivalLogicalPosition;
        exteriorArrivalLogicalPosition = exteriorArrivalPosition;
    }

    public bool CanUse(Vector2 playerPosition)
    {
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            return false;
        }

        return CanUse(playerPosition, grid);
    }

    private bool CanUse(Vector2 playerPosition, SceneGrid grid)
    {
        var interactionPosition = usesWorldInteractionPosition
            ? worldInteractionPosition
            : grid.LogicalToWorld(interactionLogicalPosition);
        return (playerPosition - interactionPosition).sqrMagnitude <= interactionRadius * interactionRadius;
    }

    public static bool TryGetBuilding(
        Scene scene,
        Vector2 playerPosition,
        uint buildingId,
        out ScenePortal portal)
    {
        var portals = FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        portal = null!;

        if (!SceneGrid.TryGetForScene(scene, out var grid))
        {
            return false;
        }

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene
                || candidate.buildingInstanceId != buildingId
                || !candidate.CanUse(playerPosition, grid))
            {
                continue;
            }

            portal = candidate;
            return true;
        }

        return false;
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

        if (!SceneGrid.TryGetForScene(scene, out var grid))
        {
            return false;
        }

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene || candidate.destination != destination)
            {
                continue;
            }

            var interactionPosition = candidate.usesWorldInteractionPosition
                ? candidate.worldInteractionPosition
                : grid.LogicalToWorld(candidate.interactionLogicalPosition);
            var distance = (playerPosition - interactionPosition).sqrMagnitude;
            if (distance >= closestDistance || !candidate.CanUse(playerPosition, grid))
            {
                continue;
            }

            closestDistance = distance;
            portal = candidate;
        }

        return portal is not null;
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

        if (!SceneGrid.TryGetForScene(scene, out var grid))
        {
            return false;
        }

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene || !candidate.CanUse(playerPosition, grid))
            {
                continue;
            }

            var interactionPosition = candidate.usesWorldInteractionPosition
                ? candidate.worldInteractionPosition
                : grid.LogicalToWorld(candidate.interactionLogicalPosition);
            var distance = (playerPosition - interactionPosition).sqrMagnitude;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            portal = candidate;
        }

        return portal is not null;
    }
}
