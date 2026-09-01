// Registers an authored scene building with the same instance and occupancy model as placed buildings.
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(BuildingView))]
public sealed class PreplacedBuilding : MonoBehaviour
{
    [SerializeField] private BuildingDefinition definition = null!;
    [SerializeField] private Vector3Int anchorCell;
    [SerializeField] private Vector2Int size;
    [SerializeField, Min(1)] private uint instanceId = 1;
    [SerializeField] private GridEdgeDirection direction;
    [SerializeField] private WallCellShape wallShape;

    private BuildingView view = null!;

    public BuildingDefinition Definition => definition;
    public Vector3Int AnchorCell => anchorCell;
    public Vector2Int Size => BuildingFootprint.GetEffectiveSize(size, definition.FootprintSize);
    public uint InstanceId => instanceId;
    public GridEdgeDirection Direction => direction;
    public WallCellShape WallShape => wallShape;
    public BuildingView View => view;

    private void Awake()
    {
        view = GetComponent<BuildingView>();
    }

    public void Configure(Tilemap ground, WallConnectionMask wallConnections)
    {
        view = GetComponent<BuildingView>();
        Configure(
            new BuildingInstance(
                instanceId,
                definition.Id,
                anchorCell,
                Size,
                -1,
                direction,
                wallShape),
            ground,
            wallConnections);
    }

    public void Configure(
        BuildingInstance instance,
        Tilemap ground,
        WallConnectionMask wallConnections)
    {
        view = GetComponent<BuildingView>();
        view.Configure(instance, definition, ground, wallConnections);
    }

    public void SetPlacementData(
        BuildingDefinition newDefinition,
        Vector3Int newAnchorCell,
        Vector2Int newSize,
        uint newInstanceId,
        GridEdgeDirection newDirection = GridEdgeDirection.South,
        WallCellShape newWallShape = WallCellShape.Horizontal)
    {
        definition = newDefinition;
        anchorCell = newAnchorCell;
        size = newSize;
        instanceId = newInstanceId;
        direction = newDirection;
        wallShape = newWallShape;
    }
}
