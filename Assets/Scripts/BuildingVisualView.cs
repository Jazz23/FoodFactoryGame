// Provides the single configuration path shared by runtime, ghost, and editor building visuals.
using UnityEngine;
using UnityEngine.Tilemaps;

public enum BuildingVisualMode : byte
{
    Runtime,
    Preview
}

public sealed class BuildingVisualView : MonoBehaviour
{
    [SerializeField] private BuildingInstance instance;
    [SerializeField] private BuildingDefinition definition = null!;
    [SerializeField] private BuildingPlacementKind placementKind;

    private ModularBuildingView modularView = null!;
    private WallSegmentView wallSegmentView = null!;

    public ModularBuildingView ModularView => modularView;
    public WallSegmentView WallSegmentView => wallSegmentView;
    public BuildingInstance Instance => instance;
    public BuildingDefinition Definition => definition;

    public void Configure(
        BuildingInstance instance,
        BuildingDefinition definition,
        Tilemap ground,
        BuildingVisualMode mode)
    {
        this.instance = instance;
        this.definition = definition;
        placementKind = definition.PlacementKind;
        var visualAnchorCell = BuildingFootprint.GetVisualAnchorCell(
            instance.AnchorCell,
            definition.VisualAnchorCellOffset);
        var position = ground.CellToWorld(visualAnchorCell);
        position.z = mode == BuildingVisualMode.Preview ? -0.05f : 0f;
        transform.position = position;

        if (placementKind == BuildingPlacementKind.WallSegment)
        {
            wallSegmentView = GetComponent<WallSegmentView>();
            wallSegmentView.Configure(instance, ground, mode);
            return;
        }

        modularView = GetComponent<ModularBuildingView>();
        var size = BuildingFootprint.GetEffectiveSize(instance.Size, definition.FootprintSize);
        modularView.Configure(
            instance.AnchorCell,
            size,
            ground,
            definition.EntranceCellOffset,
            definition.HasInterior,
            mode);
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        if (placementKind == BuildingPlacementKind.WallSegment)
        {
            wallSegmentView.SetPresentation(colorMultiplier, sortingOrderOffset);
            return;
        }

        modularView.SetPresentation(colorMultiplier, sortingOrderOffset);
    }
}
