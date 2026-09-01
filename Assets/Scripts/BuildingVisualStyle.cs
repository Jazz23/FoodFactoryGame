// Stores the shared generated-building art, dimensions, colors, and sorting policy.
using UnityEngine;

[CreateAssetMenu(menuName = "Food Factory/Building Visual Style", fileName = "BuildingVisualStyle")]
public sealed class BuildingVisualStyle : ScriptableObject
{
    [SerializeField] private Sprite floorSprite = null!;
    [SerializeField] private Sprite entranceSprite = null!;
    [SerializeField] private Sprite entranceOutlineSprite = null!;
    [SerializeField] private Material moduleMaterial = null!;
    [SerializeField] private Color floorColor = new(0.3f, 0.38f, 0.28f, 1f);
    [SerializeField] private Color roofColor = new(0.035f, 0.05f, 0.075f, 1f);
    [SerializeField] private Color wallColor = new(0.45f, 0.52f, 0.58f, 1f);
    [SerializeField] private Color wallSideColor = new(0.28f, 0.35f, 0.41f, 1f);
    [SerializeField] private Color roofAccentColor = new(0.16f, 0.21f, 0.26f, 1f);
    [SerializeField] private Color outlineColor = new(0.015f, 0.02f, 0.025f, 1f);
    [SerializeField] private Color entranceColor = Color.white;
    [SerializeField, Min(0f)] private float wallHeight = 1.75f;
    [SerializeField, Min(0f)] private float roofHeight = 1.75f;
    [SerializeField, Range(0f, 0.5f)] private float roofLipHeight = 0.18f;
    [SerializeField, Range(0.1f, 1f)] private float entranceHeightRatio = 0.9f;
    [SerializeField, Min(0.005f)] private float outlineWidth = 0.045f;
    [SerializeField] private int floorSortingOrder;
    [SerializeField] private int wallSortingOrder = 10;
    [SerializeField] private int roofSortingOrder = 30;
    [SerializeField] private int outlineSortingOrder = 45;
    [SerializeField] private int entranceSortingOrder = 40;

    public Sprite FloorSprite => floorSprite;
    public Sprite EntranceSprite => entranceSprite;
    public Sprite EntranceOutlineSprite => entranceOutlineSprite;
    public Material ModuleMaterial => moduleMaterial;
    public Color FloorColor => floorColor;
    public Color RoofColor => roofColor;
    public Color WallColor => wallColor;
    public Color WallSideColor => wallSideColor;
    public Color RoofAccentColor => roofAccentColor;
    public Color OutlineColor => outlineColor;
    public Color EntranceColor => entranceColor;
    public float WallHeight => wallHeight;
    public float RoofHeight => roofHeight;
    public float RoofLipHeight => Mathf.Min(roofLipHeight, wallHeight);
    public float EntranceHeight => wallHeight * entranceHeightRatio;
    public float OutlineWidth => outlineWidth;
    public int FloorSortingOrder => floorSortingOrder;
    public int RoofSortingOrder => roofSortingOrder;
    public int OutlineSortingOrder => outlineSortingOrder;
    public int EntranceSortingOrder => entranceSortingOrder;

    public Color GetWallColor(GridEdgeDirection direction)
    {
        return direction is GridEdgeDirection.South or GridEdgeDirection.East
            ? wallColor
            : wallSideColor;
    }

    public int GetWallSortingOrder(GridEdgeDirection direction, GridEdge edge)
    {
        return direction is GridEdgeDirection.South or GridEdgeDirection.East
            ? roofSortingOrder + 2
            : wallSortingOrder;
    }

    public int GetWallCellSortingOrder(Vector3Int cell)
    {
        return wallSortingOrder;
    }

    public int GetBuildingSortingOrder(Vector3Int anchorCell, Vector2Int size)
    {
        return anchorCell.x + anchorCell.y + size.x + size.y;
    }
}
