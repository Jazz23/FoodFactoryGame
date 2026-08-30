// Configures centered visual geometry and collision for one standalone wall cell.
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class WallSegmentView : MonoBehaviour
{
    private const string GeneratedObjectName = "Wall Segment Generated";

    [SerializeField] private BuildingVisualStyle style = null!;

    private CenteredWallSegmentRenderer segmentRenderer = null!;

    public BuildingVisualStyle Style => style;
    public CenteredWallSegmentRenderer SegmentRenderer => segmentRenderer;

    public void Configure(
        BuildingInstance instance,
        Tilemap ground,
        BuildingVisualMode mode)
    {
        var generatedTransform = transform.Find(GeneratedObjectName);
        if (generatedTransform is not null
            && generatedTransform
            && !generatedTransform.TryGetComponent<CenteredWallSegmentRenderer>(out _))
        {
            DestroyGeneratedObject(generatedTransform.gameObject);
            generatedTransform = null;
        }

        if (generatedTransform is null || !generatedTransform)
        {
            var generatedObject = new GameObject(GeneratedObjectName);
            generatedObject.transform.SetParent(transform, false);
            segmentRenderer = generatedObject.AddComponent<CenteredWallSegmentRenderer>();
        }
        else
        {
            segmentRenderer = generatedTransform.GetComponent<CenteredWallSegmentRenderer>();
        }

        segmentRenderer.Configure(
            instance.WallShape,
            instance.AnchorCell,
            ground,
            style,
            mode == BuildingVisualMode.Runtime);
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        segmentRenderer.SetPresentation(colorMultiplier, sortingOrderOffset);
    }

    private static void DestroyGeneratedObject(GameObject generatedObject)
    {
        generatedObject.SetActive(false);
        if (Application.isPlaying)
        {
            generatedObject.transform.SetParent(null, false);
            Destroy(generatedObject);
        }
        else
        {
            DestroyImmediate(generatedObject);
        }
    }
}
