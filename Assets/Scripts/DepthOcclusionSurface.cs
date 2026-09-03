// Stores one explicit projected occlusion surface and the renderer it controls.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Renderer))]
public sealed class DepthOcclusionSurface : MonoBehaviour
{
    [SerializeField] private Vector2[] projectedPolygon = Array.Empty<Vector2>();
    [SerializeField] private Vector2[] groundPolygon = Array.Empty<Vector2>();
    [SerializeField] private Vector2 logicalStart;
    [SerializeField] private Vector2 logicalEnd;
    [SerializeField] private Vector3 localDepthReference;

    private Renderer surfaceRenderer = null!;
    private DepthOcclusionPresentation presentation = null!;
    private readonly List<Vector2> worldGroundPolygon = new();

    public IReadOnlyList<Vector2> ProjectedPolygon => projectedPolygon;
    public IReadOnlyList<Vector2> GroundPolygon => groundPolygon;
    public Vector2 LogicalStart => logicalStart;
    public Vector2 LogicalEnd => logicalEnd;
    public Renderer SurfaceRenderer => GetSurfaceRenderer();
    public DepthOcclusionPresentation Presentation => GetPresentation();
    public float DepthKey => transform.TransformPoint(localDepthReference).y;

    public float GetDepthKey(Vector2 referencePoint)
    {
        GetGroundPolygon(worldGroundPolygon);
        if (BuildingDepthGeometry.TryGetClosestPointOnPolygon(
                worldGroundPolygon,
                referencePoint,
                out var closestPoint))
        {
            return closestPoint.y;
        }

        return DepthKey;
    }

    private void Awake()
    {
        surfaceRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        surfaceRenderer = GetComponent<Renderer>();
    }

    public void Configure(
        IReadOnlyList<Vector3> projectedWorldPolygon,
        IReadOnlyList<Vector3> groundWorldPolygon,
        Vector3 depthWorldReference,
        Vector2 logicalSegmentStart,
        Vector2 logicalSegmentEnd)
    {
        projectedPolygon = ToLocalPolygon(projectedWorldPolygon);
        groundPolygon = ToLocalPolygon(groundWorldPolygon);
        localDepthReference = transform.InverseTransformPoint(depthWorldReference);
        logicalStart = logicalSegmentStart;
        logicalEnd = logicalSegmentEnd;
        surfaceRenderer = GetComponent<Renderer>();
        presentation = null!;
    }

    public void GetProjectedPolygon(List<Vector2> points)
    {
        points.Clear();
        foreach (var point in projectedPolygon)
        {
            var worldPoint = transform.TransformPoint(new Vector3(point.x, point.y, 0f));
            points.Add(new Vector2(worldPoint.x, worldPoint.y));
        }
    }

    public void GetGroundPolygon(List<Vector2> points)
    {
        points.Clear();
        foreach (var point in groundPolygon)
        {
            var worldPoint = transform.TransformPoint(new Vector3(point.x, point.y, 0f));
            points.Add(new Vector2(worldPoint.x, worldPoint.y));
        }
    }

    public int GetSortingOrder()
    {
        var sortingGroup = GetComponentInParent<SortingGroup>();
        return sortingGroup is not null && sortingGroup
            ? sortingGroup.sortingOrder
            : GetSurfaceRenderer().sortingOrder;
    }

    public bool IsConfigured
    {
        get
        {
            return projectedPolygon.Length >= 3
                && groundPolygon.Length >= 3
                && GetSurfaceRenderer().enabled;
        }
    }

    private Renderer GetSurfaceRenderer()
    {
        if (surfaceRenderer is null || !surfaceRenderer)
        {
            surfaceRenderer = GetComponent<Renderer>();
        }

        return surfaceRenderer;
    }

    private DepthOcclusionPresentation GetPresentation()
    {
        if (presentation is null || !presentation)
        {
            presentation = GetComponentInParent<DepthOcclusionPresentation>();
        }

        return presentation;
    }

    private Vector2[] ToLocalPolygon(IReadOnlyList<Vector3> worldPolygon)
    {
        var localPolygon = new Vector2[worldPolygon.Count];
        for (var index = 0; index < worldPolygon.Count; index++)
        {
            var localPoint = transform.InverseTransformPoint(worldPolygon[index]);
            localPolygon[index] = new Vector2(localPoint.x, localPoint.y);
        }

        return localPolygon;
    }
}
