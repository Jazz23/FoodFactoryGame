// Provides generated building presentation and interior state to the scene-level occlusion coordinator.
using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingOcclusionFader : DepthOcclusionPresentation
{
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static readonly Vector3 ThinDoorwayOutlineScale = new(0.94f, 0.94f, 1f);

    [SerializeField, Min(0f)] private float fadeSpeed = 8f;
    [SerializeField] private int buildingSortingOrder = 10;

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
    private BuildingInteriorController interiorController = null!;

    private void Awake()
    {
        RefreshVisuals();
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
        interiorController = GetComponent<BuildingInteriorController>();

        CacheBuildingRenderers();

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

    public override void SetOcclusionState(float targetAlpha, bool isInside)
    {
        FadeBuildingRenderers(targetAlpha);
        FadeSpriteRenderer(interiorFloorRenderer, isInside ? 1f : 0f);
        FadeSpriteRenderer(doorwayOutlineRenderer, isInside ? 1f : 0f);
    }

    public override bool IsInside(Virtual3DSize player)
    {
        return interiorController is not null
            && interiorController.IsInside(player);
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
        meshAlphaByRenderer.Clear();

        AddBuildingSpriteRenderer(buildingRenderer);
        AddBuildingSpriteRenderer(doorRenderer);
        AddBuildingSpriteRenderer(doorwayRenderer);

        if (!TryGetComponent<BuildingVisualView>(out var visualView)
            || visualView.ModularView is null
            || !visualView.ModularView)
        {
            return;
        }

        foreach (var renderer in visualView.ModularView.GeneratedRenderers)
        {
            if (renderer is SpriteRenderer spriteRenderer)
            {
                AddBuildingSpriteRenderer(spriteRenderer);
            }

            if (renderer is MeshRenderer meshRenderer && meshRenderer.enabled)
            {
                buildingMeshRenderers.Add(meshRenderer);
            }

            if (renderer is LineRenderer lineRenderer && lineRenderer.enabled)
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
}
