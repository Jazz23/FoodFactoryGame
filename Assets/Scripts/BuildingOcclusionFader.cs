// Keeps building visuals sorted and faded correctly when players move in front of or behind them.
using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class BuildingOcclusionFader : MonoBehaviour
{
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly Vector3 ThinDoorwayOutlineScale = new(0.94f, 0.94f, 1f);

    [SerializeField, Range(0.05f, 1f)] private float occludedAlpha = 0.22f;
    [SerializeField, Min(0f)] private float fadeSpeed = 8f;
    [SerializeField] private float rearThresholdOffset = 0.02f;
    [SerializeField] private int buildingSortingOrder = 10;

    private readonly List<Virtual3DSize> players = new();
    private readonly List<SpriteRenderer> buildingSpriteRenderers = new();
    private readonly List<MeshRenderer> buildingMeshRenderers = new();
    private readonly List<LineRenderer> buildingLineRenderers = new();
    private readonly Dictionary<MeshRenderer, float> meshAlphaByRenderer = new();
    private MaterialPropertyBlock propertyBlock = null!;

    private SpriteRenderer buildingRenderer = null!;
    private SpriteRenderer doorRenderer = null!;
    private SpriteRenderer interiorFloorRenderer = null!;
    private SpriteRenderer doorwayRenderer = null!;
    private SpriteRenderer doorwayOutlineRenderer = null!;
    private PolygonCollider2D occlusionFootprint = null!;
    private BuildingInteriorController interiorController = null!;
    private int rearPlayerSortingOrder;
    private int frontPlayerSortingOrder;
    private float nextPlayerRefreshTime;

    private void Awake()
    {
        RefreshVisuals();
        RefreshPlayers();
    }

    private void OnEnable()
    {
        RefreshVisuals();
    }

    public void RefreshVisuals()
    {
        buildingRenderer = GetComponent<SpriteRenderer>();
        doorRenderer = GetChildSpriteRenderer("Door");
        interiorFloorRenderer = GetChildSpriteRenderer("Interior Floor");
        doorwayRenderer = GetChildSpriteRenderer("Doorway");
        doorwayOutlineRenderer = GetChildSpriteRenderer("Doorway Outline");
        occlusionFootprint = GetComponent<PolygonCollider2D>();
        interiorController = GetComponent<BuildingInteriorController>();

        CacheBuildingRenderers();
        RefreshSortingRange();

        if (doorwayOutlineRenderer)
        {
            doorwayOutlineRenderer.sortingOrder = buildingSortingOrder;
            doorwayOutlineRenderer.transform.localScale = ThinDoorwayOutlineScale;

            var outlineColor = doorwayOutlineRenderer.color;
            outlineColor.r = 0f;
            outlineColor.g = 0f;
            outlineColor.b = 0f;
            outlineColor.a = 0f;
            doorwayOutlineRenderer.color = outlineColor;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextPlayerRefreshTime)
        {
            RefreshPlayers();
        }

        if (!occlusionFootprint)
        {
            RefreshVisuals();
        }

        var localPlayerIsOccluded = false;
        var localPlayerIsInside = false;

        foreach (var player in players)
        {
            if (player is null || !player)
            {
                continue;
            }

            var playerRenderer = player.GetComponent<SpriteRenderer>();
            var isInside = interiorController && interiorController.IsInside(player);
            var isBehind = false;
            if (!isInside && TryGetRearEdgeY(player.FootprintBounds, out var rearEdgeY))
            {
                isBehind = player.FrontY > rearEdgeY + rearThresholdOffset;
            }

            playerRenderer.sortingOrder = isBehind || isInside
                ? rearPlayerSortingOrder
                : frontPlayerSortingOrder;

            if (!AffectsLocalOpacity(player))
            {
                continue;
            }

            localPlayerIsInside |= isInside;
            localPlayerIsOccluded |= isBehind && OverlapsVisibleBuilding(player);
        }

        var targetAlpha = localPlayerIsInside
            ? 0f
            : localPlayerIsOccluded ? occludedAlpha : 1f;
        FadeBuildingRenderers(targetAlpha);
        FadeSpriteRenderer(interiorFloorRenderer, localPlayerIsInside ? 1f : 0f);
        FadeSpriteRenderer(doorwayOutlineRenderer, localPlayerIsInside ? 1f : 0f);
    }

    private static bool AffectsLocalOpacity(Virtual3DSize player)
    {
        return player is not null
            && player
            && (!player.TryGetComponent<NetworkObject>(out var networkObject) || networkObject.IsOwner);
    }

    private SpriteRenderer GetChildSpriteRenderer(string childName)
    {
        var child = transform.Find(childName);
        return child ? child.GetComponent<SpriteRenderer>() : null!;
    }

    private void CacheBuildingRenderers()
    {
        buildingSpriteRenderers.Clear();
        buildingMeshRenderers.Clear();
        buildingLineRenderers.Clear();

        AddBuildingSpriteRenderer(buildingRenderer);
        AddBuildingSpriteRenderer(doorRenderer);
        AddBuildingSpriteRenderer(doorwayRenderer);

        var generatedRoot = transform.Find("Modular Generated");
        if (!generatedRoot)
        {
            return;
        }

        foreach (var spriteRenderer in generatedRoot.GetComponentsInChildren<SpriteRenderer>(true))
        {
            AddBuildingSpriteRenderer(spriteRenderer);
        }

        foreach (var meshRenderer in generatedRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (meshRenderer.enabled)
            {
                buildingMeshRenderers.Add(meshRenderer);
            }
        }

        foreach (var lineRenderer in generatedRoot.GetComponentsInChildren<LineRenderer>(true))
        {
            if (lineRenderer.enabled)
            {
                buildingLineRenderers.Add(lineRenderer);
            }
        }
    }

    private void AddBuildingSpriteRenderer(SpriteRenderer spriteRenderer)
    {
        if (spriteRenderer && spriteRenderer.enabled)
        {
            buildingSpriteRenderers.Add(spriteRenderer);
        }
    }

    private void RefreshSortingRange()
    {
        var minimumSortingOrder = buildingSortingOrder;
        var maximumSortingOrder = buildingSortingOrder;

        foreach (var spriteRenderer in buildingSpriteRenderers)
        {
            minimumSortingOrder = Mathf.Min(minimumSortingOrder, spriteRenderer.sortingOrder);
            maximumSortingOrder = Mathf.Max(maximumSortingOrder, spriteRenderer.sortingOrder);
        }

        foreach (var meshRenderer in buildingMeshRenderers)
        {
            minimumSortingOrder = Mathf.Min(minimumSortingOrder, meshRenderer.sortingOrder);
            maximumSortingOrder = Mathf.Max(maximumSortingOrder, meshRenderer.sortingOrder);
        }

        foreach (var lineRenderer in buildingLineRenderers)
        {
            minimumSortingOrder = Mathf.Min(minimumSortingOrder, lineRenderer.sortingOrder);
            maximumSortingOrder = Mathf.Max(maximumSortingOrder, lineRenderer.sortingOrder);
        }

        rearPlayerSortingOrder = minimumSortingOrder - 1;
        frontPlayerSortingOrder = maximumSortingOrder + 1;
    }

    private bool OverlapsVisibleBuilding(Virtual3DSize player)
    {
        var playerBounds = player.ProjectedBounds;
        foreach (var meshRenderer in buildingMeshRenderers)
        {
            if (IntersectsXY(playerBounds, meshRenderer.bounds))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IntersectsXY(Bounds first, Bounds second)
    {
        return first.min.x <= second.max.x
            && first.max.x >= second.min.x
            && first.min.y <= second.max.y
            && first.max.y >= second.min.y;
    }

    private bool TryGetRearEdgeY(Bounds playerFootprint, out float rearEdgeY)
    {
        rearEdgeY = float.NegativeInfinity;
        var buildingBounds = occlusionFootprint.bounds;
        var overlapMinX = Mathf.Max(playerFootprint.min.x, buildingBounds.min.x);
        var overlapMaxX = Mathf.Min(playerFootprint.max.x, buildingBounds.max.x);
        if (overlapMinX > overlapMaxX)
        {
            return false;
        }

        if (!TryGetRearEdgeAtX(overlapMinX, buildingBounds, out var minRearEdge))
        {
            return false;
        }

        if (!TryGetRearEdgeAtX(overlapMaxX, buildingBounds, out var maxRearEdge))
        {
            return false;
        }

        rearEdgeY = Mathf.Min(minRearEdge, maxRearEdge);
        return true;
    }

    private bool TryGetRearEdgeAtX(float worldX, Bounds buildingBounds, out float rearEdgeY)
    {
        var sampleX = Mathf.Clamp(worldX, buildingBounds.min.x + 0.001f, buildingBounds.max.x - 0.001f);
        var path = occlusionFootprint.GetPath(0);
        rearEdgeY = float.NegativeInfinity;

        for (var index = 0; index < path.Length; index++)
        {
            var start = occlusionFootprint.transform.TransformPoint(path[index]);
            var end = occlusionFootprint.transform.TransformPoint(path[(index + 1) % path.Length]);
            if (sampleX < Mathf.Min(start.x, end.x) || sampleX > Mathf.Max(start.x, end.x))
            {
                continue;
            }

            var deltaX = end.x - start.x;
            if (Mathf.Abs(deltaX) < Mathf.Epsilon)
            {
                rearEdgeY = Mathf.Max(rearEdgeY, start.y, end.y);
                continue;
            }

            var interpolation = (sampleX - start.x) / deltaX;
            rearEdgeY = Mathf.Max(rearEdgeY, Mathf.Lerp(start.y, end.y, interpolation));
        }

        if (float.IsNegativeInfinity(rearEdgeY))
        {
            rearEdgeY = buildingBounds.max.y;
        }

        return true;
    }

    private void FadeBuildingRenderers(float targetAlpha)
    {
        foreach (var spriteRenderer in buildingSpriteRenderers)
        {
            FadeSpriteRenderer(spriteRenderer, targetAlpha);
        }

        foreach (var meshRenderer in buildingMeshRenderers)
        {
            FadeMeshRenderer(meshRenderer, targetAlpha);
        }

        foreach (var lineRenderer in buildingLineRenderers)
        {
            FadeLineRenderer(lineRenderer, targetAlpha);
        }
    }

    private void FadeSpriteRenderer(SpriteRenderer spriteRenderer, float targetAlpha)
    {
        if (!spriteRenderer)
        {
            return;
        }

        var color = spriteRenderer.color;
        color.a = Mathf.MoveTowards(color.a, targetAlpha, fadeSpeed * Time.deltaTime);
        spriteRenderer.color = color;
    }

    private void FadeMeshRenderer(MeshRenderer meshRenderer, float targetAlpha)
    {
        if (!meshRenderer)
        {
            return;
        }

        propertyBlock ??= new MaterialPropertyBlock();

        if (!meshAlphaByRenderer.TryGetValue(meshRenderer, out var alpha))
        {
            alpha = 1f;
        }

        alpha = Mathf.MoveTowards(alpha, targetAlpha, fadeSpeed * Time.deltaTime);
        meshAlphaByRenderer[meshRenderer] = alpha;

        meshRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorPropertyId, new Color(1f, 1f, 1f, alpha));
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    private void FadeLineRenderer(LineRenderer lineRenderer, float targetAlpha)
    {
        if (!lineRenderer)
        {
            return;
        }

        var startColor = lineRenderer.startColor;
        var endColor = lineRenderer.endColor;
        startColor.a = Mathf.MoveTowards(startColor.a, targetAlpha, fadeSpeed * Time.deltaTime);
        endColor.a = Mathf.MoveTowards(endColor.a, targetAlpha, fadeSpeed * Time.deltaTime);
        lineRenderer.startColor = startColor;
        lineRenderer.endColor = endColor;
    }

    private void RefreshPlayers()
    {
        players.Clear();
        var characters = FindObjectsByType<Virtual3DSize>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        foreach (var character in characters)
        {
            players.Add(character);
        }

        nextPlayerRefreshTime = Time.unscaledTime + 0.5f;
    }
}
