// Defines the immutable data shared by every instance of one building type.
using UnityEngine;

public enum BuildingPlacementKind : byte
{
    CellArea,
    WallSegment
}

[CreateAssetMenu(menuName = "Food Factory/Building Definition", fileName = "BuildingDefinition")]
public sealed class BuildingDefinition : ScriptableObject
{
    [SerializeField] private string id = "building";
    [SerializeField] private GameObject prefab = null!;
    [SerializeField] private GameObject previewPrefab = null!;
    [SerializeField] private Vector2Int footprintSize = Vector2Int.one;
    [SerializeField] private Vector2Int visualAnchorCellOffset;
    [SerializeField] private Vector2Int entranceCellOffset;
    [SerializeField] private string interiorSceneName = string.Empty;
    [SerializeField] private Vector2 interiorArrivalLogicalPosition;
    [SerializeField] private BuildingPlacementKind placementKind;

    public string Id => id;
    public GameObject Prefab => prefab;
    public GameObject PreviewPrefab => previewPrefab;
    public Vector2Int FootprintSize => footprintSize;
    public Vector2Int VisualAnchorCellOffset => visualAnchorCellOffset;
    public Vector2Int EntranceCellOffset => entranceCellOffset;
    public string InteriorSceneName => interiorSceneName;
    public Vector2 InteriorArrivalLogicalPosition => interiorArrivalLogicalPosition;
    public BuildingPlacementKind PlacementKind => placementKind;

    public bool HasInterior => !string.IsNullOrWhiteSpace(interiorSceneName);
}
