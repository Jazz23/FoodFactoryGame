// Configures the generated visual and collision for one standalone canonical wall edge.
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class WallSegmentView : MonoBehaviour
{
    private const string GeneratedObjectName = "Wall Segment Generated";

    [SerializeField] private BuildingVisualStyle style = null!;

    private DirectionalWallSegmentRenderer segmentRenderer = null!;

    public BuildingVisualStyle Style => style;
    public DirectionalWallSegmentRenderer SegmentRenderer => segmentRenderer;

    public void Configure(
        BuildingInstance instance,
        Tilemap ground,
        BuildingVisualMode mode)
    {
        var edge = BuildingPlacementRules.GetWallEdge(instance);
        var generatedTransform = transform.Find(GeneratedObjectName);
        if (generatedTransform is null || !generatedTransform)
        {
            var generatedObject = new GameObject(GeneratedObjectName);
            generatedObject.transform.SetParent(transform, false);
            segmentRenderer = generatedObject.AddComponent<DirectionalWallSegmentRenderer>();
        }
        else
        {
            segmentRenderer = generatedTransform.GetComponent<DirectionalWallSegmentRenderer>();
        }

        var startWorld = ground.CellToWorld(edge.Corner);
        var endWorld = ground.CellToWorld(edge.EndCorner);
        startWorld.z = transform.position.z;
        endWorld.z = transform.position.z;
        segmentRenderer.Configure(
            instance.Direction,
            edge,
            startWorld,
            endWorld,
            style,
            mode == BuildingVisualMode.Runtime);
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        segmentRenderer.SetPresentation(colorMultiplier, sortingOrderOffset);
    }
}
