// Presents authoritative truck progress along exterior grid paths between shared loading docks.
using System;
using System.Collections.Generic;
using UnityEngine;
using NotAI;

public sealed class FactoryTruckMarkerView : MonoBehaviour
{
    private readonly Dictionary<Guid, GameObject> markers = new();
    private readonly List<Vector2Int> pathCells = new();

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
