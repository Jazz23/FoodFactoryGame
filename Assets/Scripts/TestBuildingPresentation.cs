// Owns generated test-building renderer presentation, including shared occlusion alpha.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TestBuildingPresentation : DepthOcclusionPresentation
{
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    private readonly List<Renderer> renderers = new();
    private readonly Dictionary<SpriteRenderer, Color> baseSpriteColors = new();
    private readonly Dictionary<LineRenderer, (Color start, Color end)> baseLineColors = new();
    private MaterialPropertyBlock propertyBlock = null!;

    public int MinimumSortingOrder { get; private set; }
    public int MaximumSortingOrder { get; private set; }

    private void OnEnable()
    {
        RefreshRenderers();
        SetOcclusionAlpha(1f);
    }

    public void RefreshRenderers()
    {
        renderers.Clear();
        MinimumSortingOrder = int.MaxValue;
        MaximumSortingOrder = int.MinValue;

        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled)
            {
                continue;
            }

            renderers.Add(renderer);
            MinimumSortingOrder = Mathf.Min(MinimumSortingOrder, renderer.sortingOrder);
            MaximumSortingOrder = Mathf.Max(MaximumSortingOrder, renderer.sortingOrder);

            if (renderer is SpriteRenderer spriteRenderer
                && !baseSpriteColors.ContainsKey(spriteRenderer))
            {
                baseSpriteColors[spriteRenderer] = spriteRenderer.color;
            }

            if (renderer is LineRenderer lineRenderer
                && !baseLineColors.ContainsKey(lineRenderer))
            {
                baseLineColors[lineRenderer] = (
                    lineRenderer.startColor,
                    lineRenderer.endColor);
            }
        }

        if (renderers.Count == 0)
        {
            MinimumSortingOrder = 0;
            MaximumSortingOrder = 0;
        }
    }

    public override void CollectOcclusionSurfaces(List<DepthOcclusionSurface> surfaces)
    {
        RefreshRenderers();
        base.CollectOcclusionSurfaces(surfaces);
    }

    public override void SetOcclusionState(float targetAlpha, bool isInside)
    {
        SetOcclusionAlpha(targetAlpha);
    }

    public void SetOcclusionAlpha(float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        propertyBlock ??= new MaterialPropertyBlock();

        foreach (var renderer in renderers)
        {
            if (renderer is null || !renderer)
            {
                continue;
            }

            if (renderer is SpriteRenderer spriteRenderer
                && baseSpriteColors.TryGetValue(spriteRenderer, out var baseSpriteColor))
            {
                var color = baseSpriteColor;
                color.a *= alpha;
                spriteRenderer.color = color;
                continue;
            }

            if (renderer is LineRenderer lineRenderer
                && baseLineColors.TryGetValue(lineRenderer, out var baseLineColor))
            {
                var startColor = baseLineColor.start;
                var endColor = baseLineColor.end;
                startColor.a *= alpha;
                endColor.a *= alpha;
                lineRenderer.startColor = startColor;
                lineRenderer.endColor = endColor;
                continue;
            }

            if (renderer is MeshRenderer meshRenderer)
            {
                meshRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(ColorPropertyId, new Color(1f, 1f, 1f, alpha));
                meshRenderer.SetPropertyBlock(propertyBlock);
            }
        }
    }

}
