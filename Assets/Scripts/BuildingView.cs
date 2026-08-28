// Configures a placed building prefab from its replicated instance data.
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(BuildingVisualView))]
public sealed class BuildingView : MonoBehaviour
{
    private ScenePortal portal = null!;
    private BuildingVisualView visualView = null!;

    public uint InstanceId { get; private set; }

    private void Awake()
    {
        visualView = GetComponent<BuildingVisualView>();
    }

    public void Configure(BuildingInstance instance, BuildingDefinition definition, Tilemap ground)
    {
        InstanceId = instance.Id;
        visualView = GetComponent<BuildingVisualView>();
        visualView.Configure(instance, definition, ground, BuildingVisualMode.Runtime);

        if (!definition.HasInterior)
        {
            if (TryGetComponent<ScenePortal>(out portal))
            {
                portal.enabled = false;
            }

            return;
        }

        portal = GetComponent<ScenePortal>();

        var entranceCell = instance.AnchorCell + new Vector3Int(
            definition.EntranceCellOffset.x,
            definition.EntranceCellOffset.y);
        var entranceWorldPosition = ground.GetCellCenterWorld(entranceCell);
        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            SceneGrid.LogMissingGrid(gameObject.scene, this);
            return;
        }

        portal.enabled = true;
        portal.ConfigureBuilding(
            instance.Id,
            entranceWorldPosition,
            definition.InteriorSceneName,
            definition.InteriorArrivalLogicalPosition,
            grid.WorldToLogical(entranceWorldPosition));
    }
}
