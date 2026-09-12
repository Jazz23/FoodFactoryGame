// Purely maps building-local and exterior logical coordinates and computes floor elevation. Interior and exterior logical coordinates use the same unit: one logical unit equals one grid cell. Building-local (0, 0) corresponds to the exterior building anchor. Logical X/Y describe the ground plane; elevation is a separate scalar and is not Unity's current rendering Z coordinate. anchor.z is not a story index or elevation and is ignored by planar conversions. Coordinate conversion does not clamp positions to a footprint. Buildings currently have no logical rotation, so this helper does not add rotation support or a transform abstraction.
using UnityEngine;

public static class BuildingCoordinates
{
    public static Vector2 LocalToExteriorLogical(Vector3Int anchor, Vector2 localPosition)
    {
        return localPosition + new Vector2(anchor.x, anchor.y);
    }

    public static Vector2 ExteriorLogicalToLocal(Vector3Int anchor, Vector2 exteriorPosition)
    {
        return exteriorPosition - new Vector2(anchor.x, anchor.y);
    }

    public static Vector2 ExteriorLocalToInteriorLocal(Vector2 exteriorLocalPosition)
    {
        return BuildingFootprint.ExteriorLocalToInteriorLocal(exteriorLocalPosition);
    }

    public static Vector2 InteriorLocalToExteriorLocal(Vector2 interiorLocalPosition)
    {
        return BuildingFootprint.InteriorLocalToExteriorLocal(interiorLocalPosition);
    }

    public static float GetFloorElevation(int floorIndex, float storyHeight)
    {
        return Mathf.Max(0, floorIndex) * Mathf.Max(0f, storyHeight);
    }
}
