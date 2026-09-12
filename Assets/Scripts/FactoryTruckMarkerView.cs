// Presents authoritative truck progress as simple exterior markers between persistent building pickup points.
using System;
using System.Collections.Generic;
using UnityEngine;
using NotAI;

public sealed class FactoryTruckMarkerView : MonoBehaviour
{
    private readonly Dictionary<Guid, GameObject> markers = new();

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
        if (routes.Count == 0)
        {
            ClearMarkers();
            return;
        }

        var seen = new HashSet<Guid>();
        foreach (var route in routes)
        {
            FactoryTruckRecord truck = null!;
            foreach (var candidate in trucks)
            {
                if (candidate.Guid == route.TruckGuid)
                {
                    truck = candidate;
                    break;
                }
            }

            if (truck is null)
            {
                continue;
            }

            if (!TryResolveBuildingPoint(route.Source, grid, out var pickup))
            {
                continue;
            }

            if (!TryResolveBuildingPoint(route.Destination, grid, out var dropoff))
            {
                continue;
            }

            if (route.Source.BuildingInstanceId == route.Destination.BuildingInstanceId)
            {
                continue;
            }

            if (!TryComputeMarkerPosition(route, truck, pickup, dropoff, out var position))
            {
                continue;
            }

            seen.Add(truck.Guid);
            if (!markers.TryGetValue(truck.Guid, out var marker) || marker is null || !marker)
            {
                marker = CreateMarker(truck.Guid);
                markers[truck.Guid] = marker;
            }

            marker.transform.position = position;
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
            if (markers[guid]) Destroy(markers[guid]);
            markers.Remove(guid);
        }
    }

    private static bool TryResolveBuildingPoint(
        FactoryEntityEndpoint endpoint,
        SceneGrid grid,
        out Vector2 worldPoint)
    {
        worldPoint = default;
        if (endpoint.BuildingInstanceId == 0
            || !NAIStateManager.Instance!.TryGetBuildingRecord(endpoint.BuildingInstanceId, out var record))
        {
            return false;
        }

        var pickupLocal = new Vector2(record.FootprintSize.x * 0.5f, record.FootprintSize.y * 0.5f);
        var exteriorLogical = BuildingCoordinates.LocalToExteriorLogical(record.AnchorCell, pickupLocal);
        worldPoint = grid.LogicalToWorld(exteriorLogical);
        return true;
    }

    private static bool TryComputeMarkerPosition(
        FactoryTruckRouteRecord route,
        FactoryTruckRecord truck,
        Vector2 pickup,
        Vector2 dropoff,
        out Vector2 position)
    {
        position = pickup;
        switch (truck.State)
        {
            case FactoryTruckState.Loading:
            case FactoryTruckState.Blocked:
                position = pickup;
                return true;
            case FactoryTruckState.Unloading:
                position = dropoff;
                return true;
            case FactoryTruckState.Outbound:
                position = Vector2.Lerp(pickup, dropoff, OutboundProgress(route, truck));
                return true;
            case FactoryTruckState.Returning:
                position = Vector2.Lerp(dropoff, pickup, ReturnProgress(route, truck));
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

    private GameObject CreateMarker(Guid truckGuid)
    {
        var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = $"Factory Truck {truckGuid:D}";
        marker.transform.SetParent(transform, false);
        marker.transform.localScale = new Vector3(0.7f, 0.7f, 0.7f);
        var labelObject = new GameObject("Truck Label");
        labelObject.transform.SetParent(marker.transform, false);
        var label = labelObject.AddComponent<TextMesh>();
        label.text = "Truck";
        label.anchor = TextAnchor.MiddleCenter;
        label.fontSize = 24;
        label.characterSize = 0.06f;
        label.transform.localPosition = Vector3.up * 0.9f;
        return marker;
    }

    private void ClearMarkers()
    {
        foreach (var pair in markers)
        {
            if (pair.Value) Destroy(pair.Value);
        }

        markers.Clear();
    }
}
