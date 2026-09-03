// Configures the authored insidefactory exit portal and grid for the entered building.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(SceneGrid), typeof(IndoorGrid))]
public sealed class InsideFactoryController : MonoBehaviour
{
    [SerializeField] private ScenePortal exitPortal = null!;

    private readonly List<ScenePortal> additionalExitPortals = new();
    private SceneGrid grid = null!;
    private IndoorGrid indoorGrid = null!;

    private void Awake()
    {
        grid = GetComponent<SceneGrid>();
        indoorGrid = GetComponent<IndoorGrid>();
    }

    public static bool TryConfigureForScene(
        Scene scene,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition)
    {
        return TryConfigureForScene(
            scene,
            buildingSize,
            arrivalLogicalPosition,
            new[] { arrivalLogicalPosition },
            new Vector2[0],
            new GridEdgeDirection[0]);
    }

    public static bool TryConfigureForScene(
        Scene scene,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        Vector2[] interiorExitLogicalPositions,
        Vector2[] exteriorArrivalLogicalPositions)
    {
        return TryConfigureForScene(
            scene,
            buildingSize,
            arrivalLogicalPosition,
            interiorExitLogicalPositions,
            exteriorArrivalLogicalPositions,
            new GridEdgeDirection[0]);
    }

    public static bool TryConfigureForScene(
        Scene scene,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        Vector2[] interiorExitLogicalPositions,
        Vector2[] exteriorArrivalLogicalPositions,
        GridEdgeDirection[] interiorExitDirections)
    {
        if (!BuildingFootprint.IsValid(buildingSize))
        {
            return false;
        }

        var controllers = FindObjectsByType<InsideFactoryController>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var controller in controllers)
        {
            if (controller.gameObject.scene != scene)
            {
                continue;
            }

            controller.Configure(
                buildingSize,
                arrivalLogicalPosition,
                interiorExitLogicalPositions,
                exteriorArrivalLogicalPositions,
                interiorExitDirections);
            return true;
        }

        return false;
    }

    public void Configure(Vector2Int buildingSize, Vector2 arrivalLogicalPosition)
    {
        Configure(
            buildingSize,
            arrivalLogicalPosition,
            new[] { arrivalLogicalPosition },
            new Vector2[0],
            new GridEdgeDirection[0]);
    }

    public void Configure(
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        IReadOnlyList<Vector2> interiorExitLogicalPositions,
        IReadOnlyList<Vector2> exteriorArrivalLogicalPositions)
    {
        Configure(
            buildingSize,
            arrivalLogicalPosition,
            interiorExitLogicalPositions,
            exteriorArrivalLogicalPositions,
            new GridEdgeDirection[0]);
    }

    public void Configure(
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        IReadOnlyList<Vector2> interiorExitLogicalPositions,
        IReadOnlyList<Vector2> exteriorArrivalLogicalPositions,
        IReadOnlyList<GridEdgeDirection> interiorExitDirections)
    {
        if (!BuildingFootprint.IsValid(buildingSize))
        {
            return;
        }

        grid ??= GetComponent<SceneGrid>();
        indoorGrid ??= GetComponent<IndoorGrid>();
        indoorGrid.ConfigureSize(buildingSize);
        var exitPositions = interiorExitLogicalPositions.Count > 0
            ? interiorExitLogicalPositions
            : new[] { arrivalLogicalPosition };
        ConfigureExitPortals(
            exitPositions,
            exteriorArrivalLogicalPositions,
            interiorExitDirections);
    }

    private void ConfigureExitPortals(
        IReadOnlyList<Vector2> interiorExitLogicalPositions,
        IReadOnlyList<Vector2> exteriorArrivalLogicalPositions,
        IReadOnlyList<GridEdgeDirection> interiorExitDirections)
    {
        var exitDirections = new GridEdgeDirection[interiorExitLogicalPositions.Count];
        for (var index = 0; index < interiorExitLogicalPositions.Count; index++)
        {
            var portal = GetExitPortal(index);
            if (!portal.gameObject.activeSelf)
            {
                portal.gameObject.SetActive(true);
            }

            var interiorPosition = interiorExitLogicalPositions[index];
            var direction = index < interiorExitDirections.Count
                ? interiorExitDirections[index]
                : GridEdgeDirection.South;
            exitDirections[index] = direction;
            if (index < exteriorArrivalLogicalPositions.Count)
            {
                portal.ConfigureInterior(
                    interiorPosition,
                    exteriorArrivalLogicalPositions[index],
                    direction);
            }
            else
            {
                portal.ConfigureInterior(interiorPosition, direction);
            }

            var worldPosition = grid.LogicalToWorld(interiorPosition);
            var position = portal.transform.position;
            portal.transform.position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                position.z);
        }

        if (TryGetComponent<InsideFactoryVisuals>(out var visuals))
        {
            visuals.Configure(
                indoorGrid.Size,
                interiorExitLogicalPositions,
                exitDirections);
        }

        for (var index = interiorExitLogicalPositions.Count - 1;
            index < additionalExitPortals.Count;
            index++)
        {
            if (index >= 0)
            {
                additionalExitPortals[index].gameObject.SetActive(false);
            }
        }
    }

    private ScenePortal GetExitPortal(int index)
    {
        if (index == 0)
        {
            return exitPortal;
        }

        var additionalIndex = index - 1;
        while (additionalExitPortals.Count <= additionalIndex)
        {
            var portalObject = new GameObject(
                $"Exit Portal {additionalExitPortals.Count + 1}");
            portalObject.transform.SetParent(transform, false);
            var portal = portalObject.AddComponent<ScenePortal>();
            additionalExitPortals.Add(portal);
        }

        return additionalExitPortals[additionalIndex];
    }
}
