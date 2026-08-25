using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class BuildingOcclusionFader : MonoBehaviour
{
    private static readonly Vector3 ThinDoorwayOutlineScale = new(0.94f, 0.94f, 1f);

    [SerializeField, Range(0.05f, 1f)] private float occludedAlpha = 0.22f;
    [SerializeField, Min(0f)] private float fadeSpeed = 8f;
    [SerializeField] private float rearThresholdOffset = 0.02f;
    [SerializeField] private int buildingSortingOrder = 10;

    private readonly List<Virtual3DSize> players = new();
    private SpriteRenderer buildingRenderer;
    private SpriteRenderer doorRenderer;
    private SpriteRenderer interiorFloorRenderer;
    private SpriteRenderer doorwayRenderer;
    private SpriteRenderer doorwayOutlineRenderer;
    private PolygonCollider2D occlusionFootprint;
    private BuildingInteriorController interiorController;
    private Camera sceneCamera;
    private Vector2[] buildingSpriteVertices;
    private ushort[] buildingSpriteTriangles;
    private float nextPlayerRefreshTime;

    private void Awake()
    {
        buildingRenderer = GetComponent<SpriteRenderer>();
        doorRenderer = transform.Find("Door")?.GetComponent<SpriteRenderer>();
        interiorFloorRenderer = transform.Find("Interior Floor")?.GetComponent<SpriteRenderer>();
        doorwayRenderer = transform.Find("Doorway")?.GetComponent<SpriteRenderer>();
        doorwayOutlineRenderer = transform.Find("Doorway Outline")?.GetComponent<SpriteRenderer>();
        occlusionFootprint = transform.Find("Occlusion Footprint")!.GetComponent<PolygonCollider2D>();
        interiorController = GetComponent<BuildingInteriorController>();
        sceneCamera = Camera.main;
        if (buildingRenderer.sprite != null)
        {
            buildingSpriteVertices = buildingRenderer.sprite.vertices;
            buildingSpriteTriangles = buildingRenderer.sprite.triangles;
        }

        buildingRenderer.sortingOrder = buildingSortingOrder;
        if (doorwayOutlineRenderer != null)
        {
            doorwayOutlineRenderer.sortingOrder = buildingSortingOrder;
            doorwayOutlineRenderer.transform.localScale = ThinDoorwayOutlineScale;

            Color outlineColor = doorwayOutlineRenderer.color;
            outlineColor.r = 0f;
            outlineColor.g = 0f;
            outlineColor.b = 0f;
            outlineColor.a = 0f;
            doorwayOutlineRenderer.color = outlineColor;
        }

        RefreshPlayers();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPlayerRefreshTime)
        {
            RefreshPlayers();
        }

        bool localPlayerIsOccluded = false;
        bool localPlayerIsInside = false;

        foreach (Virtual3DSize player in players)
        {
            if (player == null)
            {
                continue;
            }

            SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
            bool isInside = interiorController != null && interiorController.IsInside(player);
            bool isBehind = false;
            if (interiorController != null
                && !isInside
                && TryGetRearEdgeY(player.FootprintBounds, out float rearEdgeY))
            {
                isBehind = player.FrontY > rearEdgeY + rearThresholdOffset;
            }

            playerRenderer.sortingOrder = isBehind || isInside
                ? buildingSortingOrder - 1
                : buildingSortingOrder + 1;

            if (!AffectsLocalOpacity(player))
            {
                continue;
            }

            localPlayerIsInside |= isInside;
            localPlayerIsOccluded |= isBehind && OverlapsBuildingSprite(player);
        }

        FadeRenderer(interiorFloorRenderer, localPlayerIsInside ? 1f : 0f);

        Color color = buildingRenderer.color;
        float targetAlpha = localPlayerIsInside
            ? 0f
            : localPlayerIsOccluded ? occludedAlpha : 1f;
        color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
        buildingRenderer.color = color;

        if (doorRenderer != null)
        {
            Color doorColor = doorRenderer.color;
            float doorTargetAlpha = localPlayerIsInside ? 0f : targetAlpha;
            doorColor.a = Mathf.MoveTowards(
                doorColor.a,
                doorTargetAlpha,
                fadeSpeed * Time.deltaTime);
            doorRenderer.color = doorColor;
        }

        FadeRenderer(doorwayRenderer, localPlayerIsInside ? 0f : targetAlpha);
        FadeRenderer(doorwayOutlineRenderer, localPlayerIsInside ? 1f : 0f);
    }

    private static bool AffectsLocalOpacity(Virtual3DSize player)
    {
        return player != null
            && (!player.TryGetComponent<NetworkObject>(out NetworkObject networkObject) || networkObject.IsOwner);
    }

    private bool TryGetRearEdgeY(Bounds playerFootprint, out float rearEdgeY)
    {
        rearEdgeY = float.NegativeInfinity;
        var buildingBounds = occlusionFootprint.bounds;
        float overlapMinX = Mathf.Max(playerFootprint.min.x, buildingBounds.min.x);
        float overlapMaxX = Mathf.Min(playerFootprint.max.x, buildingBounds.max.x);
        if (overlapMinX > overlapMaxX)
        {
            return false;
        }

        if (!TryGetRearEdgeAtX(overlapMinX, buildingBounds, out float minRearEdge))
        {
            return false;
        }

        if (!TryGetRearEdgeAtX(overlapMaxX, buildingBounds, out float maxRearEdge))
        {
            return false;
        }

        rearEdgeY = Mathf.Min(minRearEdge, maxRearEdge);
        return true;
    }

    private bool TryGetRearEdgeAtX(float worldX, Bounds buildingBounds, out float rearEdgeY)
    {
        float sampleX = Mathf.Clamp(worldX, buildingBounds.min.x + 0.001f, buildingBounds.max.x - 0.001f);
        var path = occlusionFootprint.GetPath(0);
        rearEdgeY = float.NegativeInfinity;

        for (int index = 0; index < path.Length; index++)
        {
            var start = occlusionFootprint.transform.TransformPoint(path[index]);
            var end = occlusionFootprint.transform.TransformPoint(path[(index + 1) % path.Length]);
            if (sampleX < Mathf.Min(start.x, end.x) || sampleX > Mathf.Max(start.x, end.x))
            {
                continue;
            }

            float deltaX = end.x - start.x;
            if (Mathf.Abs(deltaX) < Mathf.Epsilon)
            {
                rearEdgeY = Mathf.Max(rearEdgeY, start.y, end.y);
                continue;
            }

            float interpolation = (sampleX - start.x) / deltaX;
            rearEdgeY = Mathf.Max(rearEdgeY, Mathf.Lerp(start.y, end.y, interpolation));
        }

        if (float.IsNegativeInfinity(rearEdgeY))
        {
            rearEdgeY = buildingBounds.max.y;
        }

        return true;
    }

    private bool OverlapsBuildingSprite(Virtual3DSize player)
    {
        if (sceneCamera == null)
        {
            sceneCamera = Camera.main;
        }

        if (sceneCamera == null
            || buildingSpriteVertices == null
            || buildingSpriteTriangles == null
            || buildingSpriteTriangles.Length < 3
            || !TryGetScreenRect(player.ProjectedBounds, out Rect playerRect))
        {
            return false;
        }

        for (int index = 0; index < buildingSpriteTriangles.Length; index += 3)
        {
            Vector2 first = WorldToScreenPoint(buildingSpriteVertices[buildingSpriteTriangles[index]]);
            Vector2 second = WorldToScreenPoint(buildingSpriteVertices[buildingSpriteTriangles[index + 1]]);
            Vector2 third = WorldToScreenPoint(buildingSpriteVertices[buildingSpriteTriangles[index + 2]]);

            if (TriangleIntersectsRect(first, second, third, playerRect))
            {
                return true;
            }
        }

        return false;
    }

    private Vector2 WorldToScreenPoint(Vector2 localPoint)
    {
        return sceneCamera.WorldToScreenPoint(transform.TransformPoint(localPoint));
    }

    private bool TryGetScreenRect(Bounds bounds, out Rect screenRect)
    {
        Vector2 first = sceneCamera.WorldToScreenPoint(new Vector3(
            bounds.min.x,
            bounds.min.y,
            bounds.center.z));
        Vector2 second = sceneCamera.WorldToScreenPoint(new Vector3(
            bounds.max.x,
            bounds.min.y,
            bounds.center.z));
        Vector2 third = sceneCamera.WorldToScreenPoint(new Vector3(
            bounds.max.x,
            bounds.max.y,
            bounds.center.z));
        Vector2 fourth = sceneCamera.WorldToScreenPoint(new Vector3(
            bounds.min.x,
            bounds.max.y,
            bounds.center.z));

        float minX = Mathf.Min(first.x, second.x, third.x, fourth.x);
        float maxX = Mathf.Max(first.x, second.x, third.x, fourth.x);
        float minY = Mathf.Min(first.y, second.y, third.y, fourth.y);
        float maxY = Mathf.Max(first.y, second.y, third.y, fourth.y);
        screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return screenRect.width > 0f && screenRect.height > 0f;
    }

    private static bool TriangleIntersectsRect(Vector2 first, Vector2 second, Vector2 third, Rect rectangle)
    {
        Rect triangleBounds = Rect.MinMaxRect(
            Mathf.Min(first.x, second.x, third.x),
            Mathf.Min(first.y, second.y, third.y),
            Mathf.Max(first.x, second.x, third.x),
            Mathf.Max(first.y, second.y, third.y));
        if (!triangleBounds.Overlaps(rectangle, true))
        {
            return false;
        }

        if (rectangle.Contains(first)
            || rectangle.Contains(second)
            || rectangle.Contains(third))
        {
            return true;
        }

        Vector2 bottomLeft = new(rectangle.xMin, rectangle.yMin);
        Vector2 bottomRight = new(rectangle.xMax, rectangle.yMin);
        Vector2 topRight = new(rectangle.xMax, rectangle.yMax);
        Vector2 topLeft = new(rectangle.xMin, rectangle.yMax);

        if (PointInTriangle(bottomLeft, first, second, third)
            || PointInTriangle(bottomRight, first, second, third)
            || PointInTriangle(topRight, first, second, third)
            || PointInTriangle(topLeft, first, second, third))
        {
            return true;
        }

        return SegmentIntersectsRect(first, second, rectangle)
            || SegmentIntersectsRect(second, third, rectangle)
            || SegmentIntersectsRect(third, first, rectangle);
    }

    private static bool PointInTriangle(Vector2 point, Vector2 first, Vector2 second, Vector2 third)
    {
        float firstSign = Cross(second - first, point - first);
        float secondSign = Cross(third - second, point - second);
        float thirdSign = Cross(first - third, point - third);
        const float tolerance = 0.0001f;

        bool hasNegative = firstSign < -tolerance || secondSign < -tolerance || thirdSign < -tolerance;
        bool hasPositive = firstSign > tolerance || secondSign > tolerance || thirdSign > tolerance;
        return !(hasNegative && hasPositive);
    }

    private static bool SegmentIntersectsRect(Vector2 start, Vector2 end, Rect rectangle)
    {
        Vector2 bottomLeft = new(rectangle.xMin, rectangle.yMin);
        Vector2 bottomRight = new(rectangle.xMax, rectangle.yMin);
        Vector2 topRight = new(rectangle.xMax, rectangle.yMax);
        Vector2 topLeft = new(rectangle.xMin, rectangle.yMax);

        return SegmentsIntersect(start, end, bottomLeft, bottomRight)
            || SegmentsIntersect(start, end, bottomRight, topRight)
            || SegmentsIntersect(start, end, topRight, topLeft)
            || SegmentsIntersect(start, end, topLeft, bottomLeft);
    }

    private static bool SegmentsIntersect(Vector2 firstStart, Vector2 firstEnd, Vector2 secondStart, Vector2 secondEnd)
    {
        const float tolerance = 0.0001f;
        float first = Cross(firstEnd - firstStart, secondStart - firstStart);
        float second = Cross(firstEnd - firstStart, secondEnd - firstStart);
        float third = Cross(secondEnd - secondStart, firstStart - secondStart);
        float fourth = Cross(secondEnd - secondStart, firstEnd - secondStart);

        return OppositeSignsOrZero(first, second, tolerance)
            && OppositeSignsOrZero(third, fourth, tolerance);
    }

    private static bool OppositeSignsOrZero(float first, float second, float tolerance)
    {
        return (first <= tolerance && second >= -tolerance)
            || (first >= -tolerance && second <= tolerance);
    }

    private static float Cross(Vector2 first, Vector2 second)
    {
        return first.x * second.y - first.y * second.x;
    }

    private void FadeRenderer(SpriteRenderer renderer, float targetAlpha)
    {
        if (renderer == null)
        {
            return;
        }

        Color color = renderer.color;
        color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
        renderer.color = color;
    }

    private void RefreshPlayers()
    {
        players.Clear();
        Virtual3DSize[] characters = FindObjectsByType<Virtual3DSize>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (Virtual3DSize character in characters)
        {
            players.Add(character);
        }

        nextPlayerRefreshTime = Time.unscaledTime + 0.5f;
    }
}
