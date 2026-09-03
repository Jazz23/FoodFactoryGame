// Configures a portal only for a door explicitly authored by the OutsideTest door creator.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ScenePortal))]
public sealed class OutsideTestFactoryDoor : MonoBehaviour
{
    public const string InteriorSceneName = "insidefactory";

    private void Awake()
    {
        var portal = GetComponent<ScenePortal>();
        var layout = GetComponentInParent<TestBuildingLayout>();
        if (!layout.HasDoor || layout.BuildingInstanceId == 0)
        {
            portal.enabled = false;
            return;
        }

        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        layout.GetExteriorWallSpans(spans);
        if (!layout.TryGetDoor(spans, out var wall)
            || !TestBuildingInteriorMapping.TryGetMapping(
                layout,
                wall,
                out var exteriorDoorLogicalPosition,
                out var exteriorArrivalLogicalPosition,
                out var interiorArrivalLogicalPosition,
                out _))
        {
            portal.enabled = false;
            return;
        }

        var creator = layout.GetComponentInParent<TestBuildingCreator>();
        var grid = creator.Grid;
        portal.ConfigureBuilding(
            layout.BuildingInstanceId,
            layout.Size,
            grid.LogicalToWorld(exteriorDoorLogicalPosition),
            InteriorSceneName,
            interiorArrivalLogicalPosition,
            exteriorArrivalLogicalPosition);
    }
}
