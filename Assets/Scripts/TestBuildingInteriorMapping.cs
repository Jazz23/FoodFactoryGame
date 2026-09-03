// Maps authored OutsideTest door positions onto matching edges inside insidefactory.
using UnityEngine;

public static class TestBuildingInteriorMapping
{
    public const float InteriorInset = 0.5f;
    public const float ExteriorArrivalOffset = 0.75f;

    public static bool TryGetMapping(
        TestBuildingLayout layout,
        TestBuildingCreator.ExteriorWallSpan wall,
        float doorOffset,
        out Vector2 exteriorDoorLogicalPosition,
        out Vector2 exteriorArrivalLogicalPosition,
        out Vector2 interiorArrivalLogicalPosition,
        out float normalizedWallPosition)
    {
        exteriorDoorLogicalPosition = default;
        exteriorArrivalLogicalPosition = default;
        interiorArrivalLogicalPosition = default;
        normalizedWallPosition = 0f;

        if (!TestBuildingCreator.IsSupportedSize(layout.Size) || wall.IsCorner)
        {
            return false;
        }

        exteriorDoorLogicalPosition = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            Mathf.Clamp01(doorOffset));

        var isHorizontal = wall.Direction is GridEdgeDirection.South or GridEdgeDirection.North;
        var wallStart = isHorizontal
            ? layout.AnchorCell.x + InteriorInset
            : layout.AnchorCell.y + InteriorInset;
        var wallLength = isHorizontal
            ? layout.Size.x - 1f
            : layout.Size.y - 1f;
        var wallPosition = isHorizontal
            ? exteriorDoorLogicalPosition.x
            : exteriorDoorLogicalPosition.y;
        normalizedWallPosition = Mathf.Clamp01((wallPosition - wallStart) / wallLength);

        var interiorLength = isHorizontal
            ? layout.Size.x - 1f
            : layout.Size.y - 1f;
        var interiorWallPosition = InteriorInset + normalizedWallPosition * interiorLength;
        interiorArrivalLogicalPosition = wall.Direction switch
        {
            GridEdgeDirection.South => new Vector2(interiorWallPosition, InteriorInset),
            GridEdgeDirection.West => new Vector2(InteriorInset, interiorWallPosition),
            GridEdgeDirection.North => new Vector2(
                interiorWallPosition,
                layout.Size.y - InteriorInset),
            GridEdgeDirection.East => new Vector2(
                layout.Size.x - InteriorInset,
                interiorWallPosition),
            _ => default
        };

        var outwardDirection = wall.Direction switch
        {
            GridEdgeDirection.South => Vector2.down,
            GridEdgeDirection.West => Vector2.left,
            GridEdgeDirection.North => Vector2.up,
            GridEdgeDirection.East => Vector2.right,
            _ => Vector2.down
        };
        exteriorArrivalLogicalPosition = exteriorDoorLogicalPosition
            + outwardDirection * ExteriorArrivalOffset;
        return true;
    }

    public static bool TryGetMapping(
        TestBuildingLayout layout,
        TestBuildingCreator.ExteriorWallSpan wall,
        out Vector2 exteriorDoorLogicalPosition,
        out Vector2 exteriorArrivalLogicalPosition,
        out Vector2 interiorArrivalLogicalPosition,
        out float normalizedWallPosition)
    {
        return TryGetMapping(
            layout,
            wall,
            layout.DoorOffset,
            out exteriorDoorLogicalPosition,
            out exteriorArrivalLogicalPosition,
            out interiorArrivalLogicalPosition,
            out normalizedWallPosition);
    }
}
