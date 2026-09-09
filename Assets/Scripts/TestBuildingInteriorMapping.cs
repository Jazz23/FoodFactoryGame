// Maps authored OutsideTest door positions onto matching local edges inside insidefactory.
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
        return TryGetMapping(
            layout.AnchorCell,
            layout.Size,
            wall,
            doorOffset,
            out exteriorDoorLogicalPosition,
            out exteriorArrivalLogicalPosition,
            out interiorArrivalLogicalPosition,
            out normalizedWallPosition);
    }

    public static bool TryGetMapping(
        Vector3Int anchorCell,
        Vector2Int size,
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

        if (!TestBuildingCreator.IsSupportedSize(size) || wall.IsCorner)
        {
            return false;
        }

        exteriorDoorLogicalPosition = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            Mathf.Clamp01(doorOffset));

        var localExteriorDoorLogicalPosition = BuildingCoordinates.ExteriorLogicalToLocal(
            anchorCell,
            exteriorDoorLogicalPosition);
        var isHorizontal = wall.Direction is GridEdgeDirection.South or GridEdgeDirection.North;
        var wallStart = InteriorInset;
        var wallLength = isHorizontal
            ? size.x - 1f
            : size.y - 1f;
        var wallPosition = isHorizontal
            ? localExteriorDoorLogicalPosition.x
            : localExteriorDoorLogicalPosition.y;
        normalizedWallPosition = Mathf.Clamp01((wallPosition - wallStart) / wallLength);

        var interiorLength = isHorizontal
            ? size.x - 1f
            : size.y - 1f;
        var interiorWallPosition = InteriorInset + normalizedWallPosition * interiorLength;
        interiorArrivalLogicalPosition = wall.Direction switch
        {
            GridEdgeDirection.South => new Vector2(interiorWallPosition, InteriorInset),
            GridEdgeDirection.West => new Vector2(InteriorInset, interiorWallPosition),
            GridEdgeDirection.North => new Vector2(
                interiorWallPosition,
                size.y - InteriorInset),
            GridEdgeDirection.East => new Vector2(
                size.x - InteriorInset,
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
