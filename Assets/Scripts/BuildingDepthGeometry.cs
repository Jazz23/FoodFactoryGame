// Provides the shared footprint intersection and rear-edge calculations used by building occlusion.
using System.Collections.Generic;
using UnityEngine;

public static class BuildingDepthGeometry
{
    private const float GeometryEpsilon = 0.0001f;

    public static bool IntersectsFootprint(
        PolygonCollider2D footprint,
        Bounds playerFootprint)
    {
        var path = footprint.GetPath(0);
        var worldPath = new List<Vector2>(path.Length);
        foreach (var point in path)
        {
            worldPath.Add(footprint.transform.TransformPoint(point));
        }

        return IntersectsFootprint(worldPath, playerFootprint);
    }

    public static bool IntersectsFootprint(
        IReadOnlyList<Vector2> polygon,
        Bounds playerFootprint)
    {
        var rectangle = new[]
        {
            new Vector2(playerFootprint.min.x, playerFootprint.min.y),
            new Vector2(playerFootprint.max.x, playerFootprint.min.y),
            new Vector2(playerFootprint.max.x, playerFootprint.max.y),
            new Vector2(playerFootprint.min.x, playerFootprint.max.y)
        };

        return IntersectsPolygon(polygon, rectangle);
    }

    public static bool IntersectsPolygon(
        IReadOnlyList<Vector2> firstPolygon,
        IReadOnlyList<Vector2> secondPolygon)
    {
        if (firstPolygon.Count < 3 || secondPolygon.Count < 3)
        {
            return false;
        }

        foreach (var point in firstPolygon)
        {
            if (IsPointInPolygon(point, secondPolygon))
            {
                return true;
            }
        }

        foreach (var point in secondPolygon)
        {
            if (IsPointInPolygon(point, firstPolygon))
            {
                return true;
            }
        }

        for (var firstIndex = 0; firstIndex < firstPolygon.Count; firstIndex++)
        {
            var firstStart = firstPolygon[firstIndex];
            var firstEnd = firstPolygon[(firstIndex + 1) % firstPolygon.Count];
            for (var secondIndex = 0; secondIndex < secondPolygon.Count; secondIndex++)
            {
                var secondStart = secondPolygon[secondIndex];
                var secondEnd = secondPolygon[(secondIndex + 1) % secondPolygon.Count];
                if (SegmentsIntersect(
                        firstStart,
                        firstEnd,
                        secondStart,
                        secondEnd))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static bool TryGetClosestPointOnPolygon(
        IReadOnlyList<Vector2> polygon,
        Vector2 point,
        out Vector2 closestPoint)
    {
        closestPoint = default;
        if (polygon.Count < 3)
        {
            return false;
        }

        if (IsPointInPolygon(point, polygon))
        {
            closestPoint = point;
            return true;
        }

        var closestDistance = float.PositiveInfinity;
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            var candidate = GetClosestPointOnSegment(point, start, end);
            var distance = (candidate - point).sqrMagnitude;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestPoint = candidate;
        }

        return true;
    }

    public static bool TryGetRearEdgeY(
        PolygonCollider2D footprint,
        Bounds playerFootprint,
        out float rearEdgeY)
    {
        var path = footprint.GetPath(0);
        var worldPath = new List<Vector2>(path.Length);
        foreach (var point in path)
        {
            worldPath.Add(footprint.transform.TransformPoint(point));
        }

        return TryGetRearEdgeY(worldPath, playerFootprint, out rearEdgeY);
    }

    public static bool TryGetRearEdgeY(
        IReadOnlyList<Vector2> polygon,
        Bounds playerFootprint,
        out float rearEdgeY)
    {
        rearEdgeY = float.NegativeInfinity;
        if (polygon.Count < 3)
        {
            return false;
        }

        var buildingBounds = GetBounds(polygon);
        if (playerFootprint.max.x < buildingBounds.min.x
            || playerFootprint.min.x > buildingBounds.max.x)
        {
            return false;
        }

        var sampleX = Mathf.Clamp(
            playerFootprint.center.x,
            buildingBounds.min.x + GeometryEpsilon,
            buildingBounds.max.x - GeometryEpsilon);
        for (var index = 0; index < polygon.Count; index++)
        {
            var start = polygon[index];
            var end = polygon[(index + 1) % polygon.Count];
            if (sampleX < Mathf.Min(start.x, end.x) - GeometryEpsilon
                || sampleX > Mathf.Max(start.x, end.x) + GeometryEpsilon)
            {
                continue;
            }

            var deltaX = end.x - start.x;
            if (Mathf.Abs(deltaX) <= GeometryEpsilon)
            {
                rearEdgeY = Mathf.Max(rearEdgeY, start.y, end.y);
                continue;
            }

            var interpolation = (sampleX - start.x) / deltaX;
            rearEdgeY = Mathf.Max(
                rearEdgeY,
                Mathf.Lerp(start.y, end.y, interpolation));
        }

        if (float.IsNegativeInfinity(rearEdgeY))
        {
            rearEdgeY = buildingBounds.max.y;
        }

        return true;
    }

    private static Bounds GetBounds(IReadOnlyList<Vector2> polygon)
    {
        var bounds = new Bounds(polygon[0], Vector3.zero);
        foreach (var point in polygon)
        {
            bounds.Encapsulate(point);
        }

        return bounds;
    }

    private static bool IsPointInPolygon(
        Vector2 point,
        IReadOnlyList<Vector2> polygon)
    {
        var inside = false;
        var previousIndex = polygon.Count - 1;
        for (var index = 0; index < polygon.Count; index++)
        {
            var current = polygon[index];
            var previous = polygon[previousIndex];
            if (IsPointOnSegment(point, previous, current))
            {
                return true;
            }

            var crossesScanline = (current.y > point.y) != (previous.y > point.y);
            if (crossesScanline
                && point.x < (previous.x - current.x) * (point.y - current.y)
                    / (previous.y - current.y) + current.x)
            {
                inside = !inside;
            }

            previousIndex = index;
        }

        return inside;
    }

    private static bool SegmentsIntersect(
        Vector2 firstStart,
        Vector2 firstEnd,
        Vector2 secondStart,
        Vector2 secondEnd)
    {
        var first = Cross(firstStart, firstEnd, secondStart);
        var second = Cross(firstStart, firstEnd, secondEnd);
        var third = Cross(secondStart, secondEnd, firstStart);
        var fourth = Cross(secondStart, secondEnd, firstEnd);

        if (Mathf.Abs(first) <= GeometryEpsilon
            && IsPointOnSegment(secondStart, firstStart, firstEnd))
        {
            return true;
        }

        if (Mathf.Abs(second) <= GeometryEpsilon
            && IsPointOnSegment(secondEnd, firstStart, firstEnd))
        {
            return true;
        }

        if (Mathf.Abs(third) <= GeometryEpsilon
            && IsPointOnSegment(firstStart, secondStart, secondEnd))
        {
            return true;
        }

        if (Mathf.Abs(fourth) <= GeometryEpsilon
            && IsPointOnSegment(firstEnd, secondStart, secondEnd))
        {
            return true;
        }

        return ((first > 0f && second < 0f) || (first < 0f && second > 0f))
            && ((third > 0f && fourth < 0f) || (third < 0f && fourth > 0f));
    }

    private static bool IsPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        return Mathf.Abs(Cross(start, end, point)) <= GeometryEpsilon
            && point.x >= Mathf.Min(start.x, end.x) - GeometryEpsilon
            && point.x <= Mathf.Max(start.x, end.x) + GeometryEpsilon
            && point.y >= Mathf.Min(start.y, end.y) - GeometryEpsilon
            && point.y <= Mathf.Max(start.y, end.y) + GeometryEpsilon;
    }

    private static Vector2 GetClosestPointOnSegment(
        Vector2 point,
        Vector2 start,
        Vector2 end)
    {
        var delta = end - start;
        if (delta.sqrMagnitude <= GeometryEpsilon)
        {
            return start;
        }

        var interpolation = Mathf.Clamp01(Vector2.Dot(point - start, delta) / delta.sqrMagnitude);
        return start + delta * interpolation;
    }

    private static float Cross(Vector2 first, Vector2 second, Vector2 point)
    {
        return (second.x - first.x) * (point.y - first.y)
            - (second.y - first.y) * (point.x - first.x);
    }
}
