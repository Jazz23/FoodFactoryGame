// Configures a portal only for a door explicitly authored by the OutsideTest door creator.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(ScenePortal))]
public sealed class OutsideTestFactoryDoor : MonoBehaviour
{
    public const string InteriorSceneName = "insidefactory";

    [SerializeField, HideInInspector] private string wallId = string.Empty;
    [SerializeField, HideInInspector, Range(0f, 1f)] private float normalizedOffset = 0.5f;

    public string WallId => wallId;
    public float NormalizedOffset => normalizedOffset;

    public bool Matches(string expectedWallId, float expectedOffset)
    {
        return wallId == expectedWallId
            && Mathf.Approximately(normalizedOffset, expectedOffset);
    }

    public void Configure(string newWallId, float newNormalizedOffset)
    {
        wallId = newWallId;
        normalizedOffset = Mathf.Clamp01(newNormalizedOffset);
    }

    private void Awake()
    {
        var portal = GetComponent<ScenePortal>();
        var layout = GetComponentInParent<TestBuildingLayout>();
        layout.MigrateLegacyDoor();
        if (layout.BuildingInstanceId == 0)
        {
            portal.enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(wallId))
        {
            if (layout.Doors.Count != 1)
            {
                portal.enabled = false;
                return;
            }

            var door = layout.Doors[0];
            Configure(door.WallId, door.NormalizedOffset);
        }

        var spans = new List<TestBuildingCreator.ExteriorWallSpan>();
        layout.GetExteriorWallSpans(spans);
        if (!layout.ContainsDoor(wallId, normalizedOffset)
            || !layout.TryGetDoor(spans, wallId, out var wall)
            || !TestBuildingInteriorMapping.TryGetMapping(
                layout,
                wall,
                normalizedOffset,
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
