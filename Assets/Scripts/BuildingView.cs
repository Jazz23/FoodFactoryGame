// Configures a placed building prefab from its replicated instance data.
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(ScenePortal))]
public sealed class BuildingView : MonoBehaviour
{
    private ScenePortal portal = null!;

    private void Awake()
    {
        portal = GetComponent<ScenePortal>();
    }

    public void Configure(BuildingInstance instance, BuildingDefinition definition, Tilemap ground)
    {
        portal = GetComponent<ScenePortal>();
        var visualAnchorCell = BuildingFootprint.GetVisualAnchorCell(
            instance.AnchorCell,
            definition.VisualAnchorCellOffset);
        var visualAnchorPosition = ground.CellToWorld(visualAnchorCell);
        visualAnchorPosition.z = 0f;
        transform.position = visualAnchorPosition;

        var size = BuildingFootprint.GetEffectiveSize(
            instance.Size,
            definition.FootprintSize);

        if (TryGetComponent<ModularBuildingView>(out var modularView))
        {
            modularView.Configure(
                instance.AnchorCell,
                size,
                ground,
                definition.EntranceCellOffset,
                definition.HasInterior);
        }

        if (!definition.HasInterior)
        {
            portal.enabled = false;
            return;
        }

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
