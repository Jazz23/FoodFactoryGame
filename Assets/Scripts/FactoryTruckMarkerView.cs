// Presents authoritative truck progress along exterior grid paths between shared loading docks.
using System;
using System.Collections.Generic;
using UnityEngine;
using NotAI;

public sealed class FactoryTruckMarkerView : MonoBehaviour
{
    private const float MarkerSize = 0.7f;
    private const float OcclusionHysteresis = 0.05f;
    private const float WireframeWidth = 0.035f;
    private const int WireframeSortingOrder = 2000;

    private readonly Dictionary<Guid, MarkerPresentation> markers = new();
    private readonly List<Vector2Int> pathCells = new();
    private readonly List<DepthOcclusionSurface> occlusionSurfaces = new();
    private readonly List<Vector2> markerPolygon = new();
    private readonly List<Vector2> surfacePolygon = new();
    private readonly Dictionary<OcclusionStateKey, bool> occlusionStates = new();
    private readonly List<OcclusionStateKey> staleOcclusionStates = new();

    private Material wireframeMaterial = null!;
    private float nextSurfaceRefreshTime;

    private void Update()
    {
        if (NAIStateManager.Instance is not { IsInitialized: true } manager)
        {
            ClearMarkers();
            return;
        }

        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            ClearMarkers();
            return;
        }

        var routes = manager.GetTruckRoutes();
        var trucks = manager.GetTrucks();
        var buildings = new List<BuildingRecord>(manager.BuildingRecords);
        if (routes.Count == 0)
        {
            ClearMarkers();
            return;
        }

        var seen = new HashSet<Guid>();
        foreach (var route in routes)
        {
            var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);

            if (truck is null)
            {
                continue;
            }

            if (!TryResolveDockCell(route.Source, out var pickup))
            {
                continue;
            }

            if (!TryResolveDockCell(route.Destination, out var dropoff)
                || !FactoryTruckGridPath.TryBuild(buildings, pickup, dropoff, pathCells))
            {
                continue;
            }

            if (route.Source.BuildingInstanceId == route.Destination.BuildingInstanceId)
            {
                continue;
            }

            if (!TryComputeMarkerPosition(route, truck, grid, pathCells, out var position))
            {
                continue;
            }

            seen.Add(truck.Guid);
            if (!markers.TryGetValue(truck.Guid, out var marker)
                || marker is null
                || !marker.Object)
            {
                marker = CreateMarker(truck.Guid);
                markers[truck.Guid] = marker;
            }

            marker.Object.transform.position = position;
            ApplyOcclusionAppearance(marker, truck.Guid);
        }

        var removed = new List<Guid>();
        foreach (var pair in markers)
        {
            if (!seen.Contains(pair.Key))
            {
                removed.Add(pair.Key);
            }
        }

        foreach (var guid in removed)
        {
            if (markers[guid].Object) Destroy(markers[guid].Object);
            markers.Remove(guid);
            RemoveOcclusionStates(guid);
        }
    }

    private static bool TryResolveDockCell(
        FactoryEntityEndpoint endpoint,
        out Vector2Int exteriorCell)
    {
        exteriorCell = default;
        if (endpoint.BuildingInstanceId == 0
            || !NAIStateManager.Instance!.TryGetBuildingRecord(endpoint.BuildingInstanceId, out var record)
            || !NAIStateManager.Instance.TryGetFloorState(
                endpoint.BuildingInstanceId,
                endpoint.FloorIndex,
                out var floor)
            || !floor.TryGetEntity(endpoint.EntityId, out var entity)
            || !entity.IsDock)
        {
            return false;
        }

        return FactoryDock.TryGetExteriorApproachCell(
            record,
            entity.LogicalPosition,
            entity.DockDirection,
            out exteriorCell);
    }

    private static bool TryComputeMarkerPosition(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        SceneGrid grid,
        IReadOnlyList<Vector2Int> cells,
        out Vector2 position)
    {
        position = grid.LogicalToWorld(FactoryTruckGridPath.Sample(cells, 0f));
        switch (truck.State)
        {
            case FactoryTruckState.Loading:
            case FactoryTruckState.Blocked:
                position = grid.LogicalToWorld(FactoryTruckGridPath.Sample(cells, 0f));
                return true;
            case FactoryTruckState.Unloading:
                position = grid.LogicalToWorld(FactoryTruckGridPath.Sample(cells, 1f));
                return true;
            case FactoryTruckState.Outbound:
                position = grid.LogicalToWorld(
                    FactoryTruckGridPath.Sample(cells, OutboundProgress(route, truck)));
                return true;
            case FactoryTruckState.Returning:
                position = grid.LogicalToWorld(
                    FactoryTruckGridPath.Sample(cells, 1f - ReturnProgress(route, truck)));
                return true;
            default:
                return false;
        }
    }

    private static float OutboundProgress(FactoryTruckRouteRecord route, FactoryTruckRecord truck)
    {
        return Mathf.Clamp01(1f - truck.RemainingTravelSeconds / route.OutboundTravelSeconds);
    }

    private static float ReturnProgress(FactoryTruckRouteRecord route, FactoryTruckRecord truck)
    {
        return Mathf.Clamp01(1f - truck.RemainingTravelSeconds / route.ReturnTravelSeconds);
    }

    private MarkerPresentation CreateMarker(Guid truckGuid)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = $"Factory Truck {truckGuid:D}";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = Vector3.one * MarkerSize;
        var solidRenderer = marker.GetComponent<MeshRenderer>();
        var labelObject = new GameObject("Truck Label");
        labelObject.transform.SetParent(marker.transform, false);
        var label = labelObject.AddComponent<TextMesh>();
        label.text = "Truck";
        label.anchor = TextAnchor.MiddleCenter;
        label.fontSize = 24;
        label.characterSize = 0.06f;
        label.transform.localPosition = Vector3.up * 0.9f;
        var labelRenderer = label.GetComponent<MeshRenderer>();
        var wireframe = CreateWireframe(marker.transform);
        return new MarkerPresentation(
            marker,
            solidRenderer,
            labelRenderer,
            wireframe);
    }

    private void ClearMarkers()
    {
        foreach (var pair in markers)
        {
            if (pair.Value.Object) Destroy(pair.Value.Object);
        }

        markers.Clear();
        occlusionStates.Clear();
    }

    private void ApplyOcclusionAppearance(MarkerPresentation marker, Guid truckGuid)
    {
        RefreshOcclusionSurfacesIfRequired();
        var isBehind = IsBehindConfiguredSurface(marker, truckGuid);
        marker.SolidRenderer.enabled = !isBehind;
        marker.LabelRenderer.enabled = !isBehind;
        foreach (var line in marker.Wireframe)
        {
            line.enabled = isBehind;
        }
    }

    private bool IsBehindConfiguredSurface(MarkerPresentation marker, Guid truckGuid)
    {
        var bounds = marker.SolidRenderer.bounds;
        markerPolygon.Clear();
        markerPolygon.Add(new Vector2(bounds.min.x, bounds.min.y));
        markerPolygon.Add(new Vector2(bounds.max.x, bounds.min.y));
        markerPolygon.Add(new Vector2(bounds.max.x, bounds.max.y));
        markerPolygon.Add(new Vector2(bounds.min.x, bounds.max.y));

        var groundAnchor = new Vector2(
            marker.Object.transform.position.x,
            marker.Object.transform.position.y);
        var isBehind = false;
        foreach (var surface in occlusionSurfaces)
        {
            if (surface is null || !surface || !surface.IsConfigured)
            {
                continue;
            }

            var stateKey = new OcclusionStateKey(truckGuid, surface);
            var previousValue = occlusionStates.TryGetValue(stateKey, out var previous)
                && previous;
            var surfaceDepth = surface.GetDepthKey(groundAnchor);
            var currentValue = DepthOcclusionCoordinator.ResolveBehind(
                groundAnchor.y,
                surfaceDepth,
                previousValue,
                OcclusionHysteresis);
            occlusionStates[stateKey] = currentValue;

            surface.GetProjectedPolygon(surfacePolygon);
            if (currentValue
                && BuildingDepthGeometry.IntersectsPolygon(surfacePolygon, markerPolygon))
            {
                isBehind = true;
            }
        }

        return isBehind;
    }

    private void RefreshOcclusionSurfacesIfRequired()
    {
        if (Time.unscaledTime < nextSurfaceRefreshTime)
        {
            return;
        }

        occlusionSurfaces.Clear();
        var surfaces = FindObjectsByType<DepthOcclusionSurface>(FindObjectsInactive.Exclude);
        foreach (var surface in surfaces)
        {
            if (surface is not null
                && surface
                && surface.gameObject.scene == gameObject.scene
                && surface.isActiveAndEnabled
                && surface.IsConfigured)
            {
                occlusionSurfaces.Add(surface);
            }
        }

        nextSurfaceRefreshTime = Time.unscaledTime + 0.1f;
    }

    private LineRenderer[] CreateWireframe(Transform markerTransform)
    {
        EnsureWireframeMaterial();
        var corners = new[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, -0.5f),
            new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, 0.5f),
            new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f)
        };
        var edgeIndices = new[]
        {
            new Vector2Int(0, 1), new Vector2Int(1, 2),
            new Vector2Int(2, 3), new Vector2Int(3, 0),
            new Vector2Int(4, 5), new Vector2Int(5, 6),
            new Vector2Int(6, 7), new Vector2Int(7, 4),
            new Vector2Int(0, 4), new Vector2Int(1, 5),
            new Vector2Int(2, 6), new Vector2Int(3, 7)
        };
        var lines = new LineRenderer[edgeIndices.Length];
        for (var index = 0; index < edgeIndices.Length; index++)
        {
            var edgeObject = new GameObject($"Truck Wireframe Edge {index + 1}");
            edgeObject.transform.SetParent(markerTransform, false);
            var line = edgeObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.startWidth = WireframeWidth;
            line.endWidth = WireframeWidth;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.sortingOrder = WireframeSortingOrder;
            line.sharedMaterial = wireframeMaterial;
            line.SetPosition(0, corners[edgeIndices[index].x]);
            line.SetPosition(1, corners[edgeIndices[index].y]);
            line.enabled = false;
            lines[index] = line;
        }

        return lines;
    }

    private void EnsureWireframeMaterial()
    {
        if (wireframeMaterial is not null && wireframeMaterial)
        {
            return;
        }

        var shader = Shader.Find("Sprites/Default");
        if (shader is null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader is null)
        {
            return;
        }

        wireframeMaterial = new Material(shader)
        {
            name = "Factory Truck Wireframe Material",
            hideFlags = HideFlags.DontSave
        };
        wireframeMaterial.color = Color.white;
    }

    private void RemoveOcclusionStates(Guid truckGuid)
    {
        staleOcclusionStates.Clear();
        foreach (var pair in occlusionStates)
        {
            if (pair.Key.TruckGuid == truckGuid)
            {
                staleOcclusionStates.Add(pair.Key);
            }
        }

        foreach (var key in staleOcclusionStates)
        {
            occlusionStates.Remove(key);
        }
    }

    private void OnDestroy()
    {
        ClearMarkers();
        if (wireframeMaterial is not null)
        {
            Destroy(wireframeMaterial);
        }
    }

    private readonly struct OcclusionStateKey : IEquatable<OcclusionStateKey>
    {
        public OcclusionStateKey(Guid truckGuid, DepthOcclusionSurface surface)
        {
            TruckGuid = truckGuid;
            Surface = surface;
        }

        public Guid TruckGuid { get; }
        public DepthOcclusionSurface Surface { get; }

        public bool Equals(OcclusionStateKey other)
        {
            return TruckGuid == other.TruckGuid && Surface == other.Surface;
        }

        public override bool Equals(object obj)
        {
            return obj is OcclusionStateKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(TruckGuid, Surface);
        }
    }

    private sealed class MarkerPresentation
    {
        public MarkerPresentation(
            GameObject newObject,
            MeshRenderer newSolidRenderer,
            MeshRenderer newLabelRenderer,
            LineRenderer[] newWireframe)
        {
            Object = newObject;
            SolidRenderer = newSolidRenderer;
            LabelRenderer = newLabelRenderer;
            Wireframe = newWireframe;
        }

        public GameObject Object { get; }
        public MeshRenderer SolidRenderer { get; }
        public MeshRenderer LabelRenderer { get; }
        public LineRenderer[] Wireframe { get; }
    }
}
