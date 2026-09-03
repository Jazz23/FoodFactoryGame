// Configures the authored insidefactory exit portal and grid for the entered building.
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(SceneGrid), typeof(IndoorGrid))]
public sealed class InsideFactoryController : MonoBehaviour
{
    [SerializeField] private ScenePortal exitPortal = null!;

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

            controller.Configure(buildingSize, arrivalLogicalPosition);
            return true;
        }

        return false;
    }

    public void Configure(Vector2Int buildingSize, Vector2 arrivalLogicalPosition)
    {
        if (!BuildingFootprint.IsValid(buildingSize))
        {
            return;
        }

        grid ??= GetComponent<SceneGrid>();
        indoorGrid ??= GetComponent<IndoorGrid>();
        indoorGrid.ConfigureSize(buildingSize);
        ConfigureExitPortal(arrivalLogicalPosition);
    }

    private void ConfigureExitPortal(Vector2 arrivalLogicalPosition)
    {
        exitPortal.ConfigureInterior(arrivalLogicalPosition);
        var worldPosition = grid.LogicalToWorld(arrivalLogicalPosition);
        var position = exitPortal.transform.position;
        exitPortal.transform.position = new Vector3(
            worldPosition.x,
            worldPosition.y,
            position.z);
    }
}
