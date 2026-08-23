using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class BuildingOcclusionFader : MonoBehaviour
{
    [SerializeField, Range(0.05f, 1f)] private float occludedAlpha = 0.22f;
    [SerializeField, Min(0f)] private float fadeSpeed = 8f;
    [SerializeField] private float rearThresholdOffset = 0.02f;
    [SerializeField] private int buildingSortingOrder = 10;

    private readonly List<Virtual3DSize> players = new();
    private SpriteRenderer buildingRenderer;
    private SpriteRenderer doorRenderer;
    private SpriteRenderer doorwayRenderer;
    private SpriteRenderer doorwayOutlineRenderer;
    private PolygonCollider2D buildingCollider;
    private BuildingInteriorController interiorController;
    private float nextPlayerRefreshTime;

    private void Awake()
    {
        buildingRenderer = GetComponent<SpriteRenderer>();
        doorRenderer = transform.Find("Door")?.GetComponent<SpriteRenderer>();
        doorwayRenderer = transform.Find("Doorway")?.GetComponent<SpriteRenderer>();
        doorwayOutlineRenderer = transform.Find("Doorway Outline")?.GetComponent<SpriteRenderer>();
        buildingCollider = GetComponent<PolygonCollider2D>();
        interiorController = GetComponent<BuildingInteriorController>();
        buildingRenderer.sortingOrder = buildingSortingOrder;
        RefreshPlayers();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPlayerRefreshTime)
        {
            RefreshPlayers();
        }

        Bounds buildingBounds = buildingRenderer.bounds;
        bool playerIsOccluded = false;
        bool playerIsInside = interiorController != null && interiorController.HasPlayerInside;

        foreach (Virtual3DSize player in players)
        {
            if (player == null)
            {
                continue;
            }

            SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
            Bounds playerBounds = playerRenderer.bounds;
            bool overlapsBuilding = playerBounds.max.x > buildingBounds.min.x
                && playerBounds.min.x < buildingBounds.max.x
                && playerBounds.max.y > buildingBounds.min.y
                && playerBounds.min.y < buildingBounds.max.y;
            float rearThreshold = GetRearEdgeY(player.transform.position.x) + rearThresholdOffset;
            bool isBehind = interiorController != null
                && !interiorController.IsInside(player)
                && player.FrontY > rearThreshold;

            playerRenderer.sortingOrder = isBehind
                ? buildingSortingOrder - 1
                : buildingSortingOrder + 1;
            playerIsOccluded |= overlapsBuilding && isBehind;
        }

        Color color = buildingRenderer.color;
        float targetAlpha = playerIsInside
            ? 0f
            : playerIsOccluded ? occludedAlpha : 1f;
        color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
        buildingRenderer.color = color;

        if (doorRenderer != null)
        {
            Color doorColor = doorRenderer.color;
            float doorTargetAlpha = playerIsInside ? 0f : targetAlpha;
            doorColor.a = Mathf.MoveTowards(
                doorColor.a,
                doorTargetAlpha,
                fadeSpeed * Time.deltaTime);
            doorRenderer.color = doorColor;
        }

        FadeRenderer(doorwayRenderer, playerIsInside ? 0f : targetAlpha);
        FadeRenderer(doorwayOutlineRenderer, playerIsInside ? 1f : targetAlpha);
    }

    private float GetRearEdgeY(float worldX)
    {
        if (buildingCollider == null || buildingCollider.pathCount == 0)
        {
            return transform.position.y;
        }

        Bounds bounds = buildingCollider.bounds;
        float sampleX = Mathf.Clamp(worldX, bounds.min.x + 0.001f, bounds.max.x - 0.001f);
        Vector2[] path = buildingCollider.GetPath(0);
        float rearEdgeY = float.NegativeInfinity;

        for (int index = 0; index < path.Length; index++)
        {
            Vector2 start = transform.TransformPoint(path[index]);
            Vector2 end = transform.TransformPoint(path[(index + 1) % path.Length]);
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

        return float.IsNegativeInfinity(rearEdgeY) ? bounds.max.y : rearEdgeY;
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
