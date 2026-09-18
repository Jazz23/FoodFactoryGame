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
    private Vector2Int buildingSize;
    private int buildingStoryCount = 1;
    private int floorIndex;
    private bool usesWorldInteractionPosition;
    private Vector2 worldInteractionPosition;
    private Vector2 exteriorArrivalLogicalPosition;
    private bool hasExteriorArrivalLogicalPosition;
    private GridEdgeDirection interiorDoorDirection = GridEdgeDirection.South;

    public SceneDestination Destination => destination;
    public string DestinationSceneName => destinationSceneName;
    public Vector2 InteractionLogicalPosition => interactionLogicalPosition;
    public Vector2 ArrivalLogicalPosition => arrivalLogicalPosition;
    public Vector2 ExteriorArrivalLogicalPosition => exteriorArrivalLogicalPosition;
    public bool HasExteriorArrivalLogicalPosition => hasExteriorArrivalLogicalPosition;
    public GridEdgeDirection InteriorDoorDirection => interiorDoorDirection;
    public uint BuildingInstanceId => buildingInstanceId;
    public Vector2Int BuildingSize => buildingSize;
    public int BuildingStoryCount => Mathf.Max(1, buildingStoryCount);
    public int FloorIndex => Mathf.Max(0, floorIndex);

    public void ConfigureBuilding(
        uint instanceId,
        Vector2Int size,
        Vector2 interactionWorldPosition,
        string interiorScene,
        Vector2 interiorArrivalLogicalPosition,
        Vector2 exteriorArrivalPosition,
        GridEdgeDirection interiorDoorDirection = GridEdgeDirection.South,
        int storyCount = 1,
        int targetFloorIndex = 0)
    {
        destination = SceneDestination.Inside;
        buildingInstanceId = instanceId;
        buildingSize = size;
        usesWorldInteractionPosition = true;
        worldInteractionPosition = interactionWorldPosition;
        destinationSceneName = interiorScene;
        arrivalLogicalPosition = interiorArrivalLogicalPosition;
        exteriorArrivalLogicalPosition = exteriorArrivalPosition;
        hasExteriorArrivalLogicalPosition = true;
        this.interiorDoorDirection = interiorDoorDirection;
        buildingStoryCount = Mathf.Max(1, storyCount);
        floorIndex = Mathf.Max(0, targetFloorIndex);
    }

    public void ConfigureInterior(Vector2 interiorLogicalPosition)
    {
        ConfigureInterior(interiorLogicalPosition, default, false, GridEdgeDirection.South);
    }

    public void ConfigureInterior(
        Vector2 interiorLogicalPosition,
        GridEdgeDirection interiorDoorDirection)
    {
        ConfigureInterior(interiorLogicalPosition, default, false, interiorDoorDirection);
    }

    public void ConfigureInterior(
        Vector2 interiorLogicalPosition,
        Vector2 exteriorArrivalPosition)
    {
        ConfigureInterior(
            interiorLogicalPosition,
            exteriorArrivalPosition,
            true,
            GridEdgeDirection.South);
    }

    public void ConfigureInterior(
        Vector2 interiorLogicalPosition,
        Vector2 exteriorArrivalPosition,
        GridEdgeDirection interiorDoorDirection)
    {
        ConfigureInterior(
            interiorLogicalPosition,
            exteriorArrivalPosition,
            true,
            interiorDoorDirection);
    }

    private void ConfigureInterior(
        Vector2 interiorLogicalPosition,
        Vector2 exteriorArrivalPosition,
        bool hasExteriorArrival,
        GridEdgeDirection interiorDoorDirection)
    {
        destination = SceneDestination.World;
        destinationSceneName = string.Empty;
        interactionLogicalPosition = interiorLogicalPosition;
        arrivalLogicalPosition = interiorLogicalPosition;
        buildingInstanceId = 0;
        buildingSize = Vector2Int.zero;
        buildingStoryCount = 1;
        floorIndex = 0;
        usesWorldInteractionPosition = false;
        worldInteractionPosition = default;
        exteriorArrivalLogicalPosition = exteriorArrivalPosition;
        hasExteriorArrivalLogicalPosition = hasExteriorArrival;
        this.interiorDoorDirection = interiorDoorDirection;
        enabled = true;
    }

    public bool CanUse(Vector2 playerPosition)
    {
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            return false;
        }

        return CanUse(playerPosition, grid);
    }

    public bool CanUse(Vector3 playerFootAnchor, FactorySpatialAdapter adapter)
    {
        if (adapter is null)
        {
            return false;
        }

        var interactionPosition = GetInteractionWorld3D(adapter);
        var planarDistance = new Vector2(playerFootAnchor.x, playerFootAnchor.z)
            - new Vector2(interactionPosition.x, interactionPosition.z);
        return planarDistance.sqrMagnitude <= interactionRadius * interactionRadius;
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
        var closestDistance = float.PositiveInfinity;
        portal = null!;

        if (!SceneGrid.TryGetForScene(scene, out var grid))
        {
            return false;
        }

        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene
                || candidate.buildingInstanceId != buildingId
                || candidate.destination != SceneDestination.Inside
                || !candidate.isActiveAndEnabled)
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
            if (candidate.gameObject.scene != scene
                || candidate.destination != destination
                || !candidate.isActiveAndEnabled)
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
            if (candidate.gameObject.scene != scene
                || !candidate.isActiveAndEnabled
                || !candidate.CanUse(playerPosition, grid))
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

    public static bool TryGetBuilding(
        Scene scene,
        Vector3 playerFootAnchor,
        FactorySpatialAdapter adapter,
        uint buildingId,
        out ScenePortal portal)
    {
        return TryGetClosest3D(
            scene,
            playerFootAnchor,
            adapter,
            SceneDestination.Inside,
            buildingId,
            true,
            out portal);
    }

    public static bool TryGetClosest(
        Scene scene,
        Vector3 playerFootAnchor,
        FactorySpatialAdapter adapter,
        out ScenePortal portal)
    {
        return TryGetClosest3D(
            scene,
            playerFootAnchor,
            adapter,
            SceneDestination.World,
            0,
            false,
            out portal);
    }

    public static bool TryGetClosest(
        Scene scene,
        Vector3 playerFootAnchor,
        FactorySpatialAdapter adapter,
        SceneDestination destination,
        out ScenePortal portal)
    {
        return TryGetClosest3D(
            scene,
            playerFootAnchor,
            adapter,
            destination,
            0,
            true,
            out portal);
    }

    private static bool TryGetClosest3D(
        Scene scene,
        Vector3 playerFootAnchor,
        FactorySpatialAdapter adapter,
        SceneDestination destination,
        uint buildingId,
        bool filterDestination,
        out ScenePortal portal)
    {
        portal = null!;
        if (adapter is null)
        {
            return false;
        }

        var portals = FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        var closestDistance = float.PositiveInfinity;
        foreach (var candidate in portals)
        {
            if (candidate.gameObject.scene != scene
                || (filterDestination && candidate.destination != destination)
                || (buildingId != 0 && candidate.buildingInstanceId != buildingId)
                || !candidate.isActiveAndEnabled
                || !candidate.CanUse(playerFootAnchor, adapter))
            {
                continue;
            }

            var interactionPosition = candidate.GetInteractionWorld3D(adapter);
            var delta = new Vector2(playerFootAnchor.x, playerFootAnchor.z)
                - new Vector2(interactionPosition.x, interactionPosition.z);
            var distance = delta.sqrMagnitude;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            portal = candidate;
        }

        return portal is not null;
    }

    private Vector3 GetInteractionWorld3D(FactorySpatialAdapter adapter)
    {
        if (usesWorldInteractionPosition)
        {
            return new Vector3(worldInteractionPosition.x, 0f, worldInteractionPosition.y);
        }

        return adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(0, floorIndex, interactionLogicalPosition),
            0f);
    }
}
