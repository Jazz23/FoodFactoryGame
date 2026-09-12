// Validates complete GUID-based world snapshots before they become authoritative.
using System;
using System.Collections.Generic;

public static class FactoryWorldValidation
{
    public static bool TryValidate(FactoryWorldSnapshot snapshot, out string error)
    {
        error = string.Empty;
        if (snapshot is null)
        {
            error = "World snapshot is required.";
            return false;
        }

        if (snapshot.SchemaVersion != FactoryWorldSnapshot.CurrentSchemaVersion)
        {
            error = $"World snapshot schema version {snapshot.SchemaVersion} is not supported.";
            return false;
        }

        var buildings = new HashSet<Guid>();
        foreach (var building in snapshot.Buildings)
        {
            if (building is null || building.Guid == Guid.Empty)
            {
                error = "A building has an empty or missing GUID.";
                return false;
            }

            if (!buildings.Add(building.Guid))
            {
                error = $"Duplicate building GUID {building.Guid:D}.";
                return false;
            }

            if (building.StoryCount < 1)
            {
                error = $"Building {building.Guid:D} has no floors.";
                return false;
            }
        }

        var floors = new HashSet<Guid>();
        var floorIndices = new HashSet<string>();
        var floorParents = new Dictionary<Guid, Guid>();
        var floorIndexByGuid = new Dictionary<Guid, int>();
        var buildingStories = new Dictionary<Guid, int>();
        foreach (var building in snapshot.Buildings)
        {
            buildingStories[building.Guid] = building.StoryCount;
        }

        var entities = new HashSet<Guid>();
        var entityParents = new Dictionary<Guid, Guid>();
        var entityRecords = new Dictionary<Guid, FactoryWorldEntityRecord>();
        foreach (var floor in snapshot.Floors)
        {
            if (floor is null || floor.Guid == Guid.Empty || floor.BuildingGuid == Guid.Empty)
            {
                error = "A floor has an empty GUID or parent building GUID.";
                return false;
            }

            if (!buildings.Contains(floor.BuildingGuid))
            {
                error = $"Floor {floor.Guid:D} references missing building {floor.BuildingGuid:D}.";
                return false;
            }

            if (floor.FloorIndex < 0)
            {
                error = $"Floor {floor.Guid:D} has a negative floor index.";
                return false;
            }

            if (floor.FloorIndex >= buildingStories[floor.BuildingGuid])
            {
                error = $"Floor {floor.Guid:D} has index {floor.FloorIndex} outside its building story count.";
                return false;
            }

            if (!floors.Add(floor.Guid))
            {
                error = $"Duplicate floor GUID {floor.Guid:D}.";
                return false;
            }

            floorParents.Add(floor.Guid, floor.BuildingGuid);
            floorIndexByGuid.Add(floor.Guid, floor.FloorIndex);

            if (!floorIndices.Add($"{floor.BuildingGuid:D}/{floor.FloorIndex}"))
            {
                error = $"Building {floor.BuildingGuid:D} has duplicate floor index {floor.FloorIndex}.";
                return false;
            }

            foreach (var entity in floor.Entities)
            {
                if (entity is null || entity.Guid == Guid.Empty)
                {
                    error = $"Floor {floor.Guid:D} contains an entity with an empty GUID.";
                    return false;
                }

                if (entity.FloorGuid != floor.Guid)
                {
                    error = $"Entity {entity.Guid:D} has the wrong parent floor.";
                    return false;
                }

                if (!entities.Add(entity.Guid))
                {
                    error = $"Duplicate entity GUID {entity.Guid:D}.";
                    return false;
                }

                entityParents.Add(entity.Guid, floor.Guid);
                entityRecords.Add(entity.Guid, entity);
                if (FactoryEntityDefinitions.Get(entity.DefinitionId).IsTerminal
                    && entity.InputCount != 0)
                {
                    error = $"Terminal entity {entity.Guid:D} cannot have a separate input buffer.";
                    return false;
                }
            }
        }

        var connections = new HashSet<Guid>();
        var connectedSources = new HashSet<Guid>();
        var connectedDestinations = new HashSet<Guid>();
        foreach (var connection in snapshot.Connections)
        {
            if (connection is null || connection.Guid == Guid.Empty)
            {
                error = "A connection has an empty or missing GUID.";
                return false;
            }

            if (!connections.Add(connection.Guid))
            {
                error = $"Duplicate connection GUID {connection.Guid:D}.";
                return false;
            }

            if (connection.Source.BuildingGuid == Guid.Empty
                || connection.Source.FloorGuid == Guid.Empty
                || connection.Source.EntityGuid == Guid.Empty
                || connection.Destination.BuildingGuid == Guid.Empty
                || connection.Destination.FloorGuid == Guid.Empty
                || connection.Destination.EntityGuid == Guid.Empty)
            {
                error = $"Connection {connection.Guid:D} has an empty endpoint identity.";
                return false;
            }

            if (!buildings.Contains(connection.Source.BuildingGuid)
                || !buildings.Contains(connection.Destination.BuildingGuid)
                || !floors.Contains(connection.Source.FloorGuid)
                || !floors.Contains(connection.Destination.FloorGuid)
                || !entities.Contains(connection.Source.EntityGuid)
                || !entities.Contains(connection.Destination.EntityGuid))
            {
                error = $"Connection {connection.Guid:D} references a missing endpoint record.";
                return false;
            }

            if (floorParents[connection.Source.FloorGuid] != connection.Source.BuildingGuid
                || floorParents[connection.Destination.FloorGuid] != connection.Destination.BuildingGuid
                || floorIndexByGuid[connection.Source.FloorGuid] != connection.Source.FloorIndex
                || floorIndexByGuid[connection.Destination.FloorGuid] != connection.Destination.FloorIndex)
            {
                error = $"Connection {connection.Guid:D} endpoint parent or floor index is inconsistent.";
                return false;
            }

            if (entityParents[connection.Source.EntityGuid] != connection.Source.FloorGuid
                || entityParents[connection.Destination.EntityGuid] != connection.Destination.FloorGuid)
            {
                error = $"Connection {connection.Guid:D} endpoint entity ownership is inconsistent.";
                return false;
            }

            if (connection.Source.Equals(connection.Destination))
            {
                error = $"Connection {connection.Guid:D} cannot target its own endpoint.";
                return false;
            }

            var sourceDefinition = FactoryEntityDefinitions.Get(
                entityRecords[connection.Source.EntityGuid].DefinitionId);
            var destinationDefinition = FactoryEntityDefinitions.Get(
                entityRecords[connection.Destination.EntityGuid].DefinitionId);
            if (!FactoryConnectionRules.TryValidate(
                    sourceDefinition,
                    destinationDefinition,
                    connection.Source.BuildingGuid == connection.Destination.BuildingGuid,
                    connection.Source.FloorGuid == connection.Destination.FloorGuid,
                    out var connectionError))
            {
                error = $"Connection {connection.Guid:D}: {connectionError}";
                return false;
            }

            if (!connectedSources.Add(connection.Source.EntityGuid))
            {
                error = $"Connection source {connection.Source.EntityGuid:D} is already connected.";
                return false;
            }

            if (!connectedDestinations.Add(connection.Destination.EntityGuid))
            {
                error = $"Connection destination {connection.Destination.EntityGuid:D} is already connected.";
                return false;
            }
        }

        var routeGuids = new HashSet<Guid>();
        var routeTruckGuids = new HashSet<Guid>();
        var routeSourceEndpoints = new HashSet<Guid>();
        var routeDestinationEndpoints = new HashSet<Guid>();
        var trucksByRoute = new Dictionary<Guid, FactoryWorldTruckRecord>();
        var truckGuids = new HashSet<Guid>();
        foreach (var truck in snapshot.Trucks)
        {
            if (truck is null || truck.Guid == Guid.Empty || truck.RouteGuid == Guid.Empty)
            {
                error = "A truck has an empty or missing GUID.";
                return false;
            }

            if (!truckGuids.Add(truck.Guid))
            {
                error = $"Duplicate truck GUID {truck.Guid:D}.";
                return false;
            }

            if (!trucksByRoute.TryAdd(truck.RouteGuid, truck))
            {
                error = $"Truck route {truck.RouteGuid:D} has more than one assigned truck.";
                return false;
            }
        }

        var connectionSources = new HashSet<Guid>();
        var connectionDestinations = new HashSet<Guid>();
        foreach (var connection in snapshot.Connections)
        {
            connectionSources.Add(connection.Source.EntityGuid);
            connectionDestinations.Add(connection.Destination.EntityGuid);
        }

        foreach (var route in snapshot.Routes)
        {
            if (route is null || route.Guid == Guid.Empty || route.TruckGuid == Guid.Empty)
            {
                error = "A truck route has an empty or missing GUID.";
                return false;
            }

            if (!routeGuids.Add(route.Guid))
            {
                error = $"Duplicate truck route GUID {route.Guid:D}.";
                return false;
            }

            if (!routeTruckGuids.Add(route.TruckGuid))
            {
                error = $"Truck GUID {route.TruckGuid:D} is assigned to more than one route.";
                return false;
            }

            if (!trucksByRoute.TryGetValue(route.Guid, out var truck)
                || truck.Guid != route.TruckGuid)
            {
                error = $"Truck route {route.Guid:D} has no matching assigned truck.";
                return false;
            }

            if (route.ItemId != FactoryEntityDefinitions.TestProductId
                || route.CargoCapacity <= 0
                || route.TransferRateItemsPerSecond <= 0f
                || route.OutboundTravelSeconds <= 0f
                || route.ReturnTravelSeconds <= 0f
                || route.PartialLoadDepartureWindowSeconds < 0f
                || float.IsNaN(route.TransferRateItemsPerSecond)
                || float.IsInfinity(route.TransferRateItemsPerSecond)
                || float.IsNaN(route.OutboundTravelSeconds)
                || float.IsInfinity(route.OutboundTravelSeconds)
                || float.IsNaN(route.ReturnTravelSeconds)
                || float.IsInfinity(route.ReturnTravelSeconds)
                || float.IsNaN(route.PartialLoadDepartureWindowSeconds)
                || float.IsInfinity(route.PartialLoadDepartureWindowSeconds))
            {
                error = $"Truck route {route.Guid:D} configuration is invalid.";
                return false;
            }

            if (route.Source.BuildingGuid == Guid.Empty
                || route.Source.FloorGuid == Guid.Empty
                || route.Source.EntityGuid == Guid.Empty
                || route.Destination.BuildingGuid == Guid.Empty
                || route.Destination.FloorGuid == Guid.Empty
                || route.Destination.EntityGuid == Guid.Empty)
            {
                error = $"Truck route {route.Guid:D} has an empty endpoint identity.";
                return false;
            }

            if (route.Source.BuildingGuid == route.Destination.BuildingGuid)
            {
                error = $"Truck route {route.Guid:D} requires two distinct buildings.";
                return false;
            }

            if (!routeSourceEndpoints.Add(route.Source.EntityGuid))
            {
                error = $"Truck route {route.Guid:D} sending terminal already has a truck assignment.";
                return false;
            }

            if (!routeDestinationEndpoints.Add(route.Destination.EntityGuid))
            {
                error = $"Truck route {route.Guid:D} receiving terminal already has a truck assignment.";
                return false;
            }

            var sourcePresent = TryValidateTruckRouteEndpoint(
                route.Guid,
                route.Source,
                buildings,
                floors,
                entities,
                floorParents,
                floorIndexByGuid,
                entityParents,
                entityRecords,
                out var sourceError);
            var destinationPresent = TryValidateTruckRouteEndpoint(
                route.Guid,
                route.Destination,
                buildings,
                floors,
                entities,
                floorParents,
                floorIndexByGuid,
                entityParents,
                entityRecords,
                out var destinationError);
            if ((!sourcePresent || !destinationPresent))
            {
                if (truck.State != FactoryTruckState.Blocked
                    || string.IsNullOrWhiteSpace(truck.BlockingReason))
                {
                    error = string.IsNullOrEmpty(sourceError) ? destinationError : sourceError;
                    return false;
                }
            }
            else
            {
                var sourceDefinition = FactoryEntityDefinitions.Get(
                    entityRecords[route.Source.EntityGuid].DefinitionId);
                var destinationDefinition = FactoryEntityDefinitions.Get(
                    entityRecords[route.Destination.EntityGuid].DefinitionId);
                if (!sourceDefinition.IsSendingTerminal || !destinationDefinition.IsReceivingTerminal)
                {
                    error = $"Truck route {route.Guid:D} must connect a sending terminal to a receiving terminal.";
                    return false;
                }

                if (connectionSources.Contains(route.Source.EntityGuid))
                {
                    error = $"Truck route {route.Guid:D} sending terminal already has an explicit connection.";
                    return false;
                }

                if (connectionDestinations.Contains(route.Destination.EntityGuid))
                {
                    error = $"Truck route {route.Guid:D} receiving terminal already has an explicit connection.";
                    return false;
                }
            }

            if (truck.CargoCount < 0 || truck.CargoCount > route.CargoCapacity)
            {
                error = $"Truck {truck.Guid:D} cargo exceeds its configured capacity.";
                return false;
            }

            if (truck.CargoCount == 0 && !string.IsNullOrWhiteSpace(truck.CargoItemId))
            {
                error = $"Truck {truck.Guid:D} cannot retain a cargo item identity while empty.";
                return false;
            }

            if (truck.CargoCount > 0 && truck.CargoItemId != route.ItemId)
            {
                error = $"Truck {truck.Guid:D} cargo item does not match its route item.";
                return false;
            }

            if (truck.State == FactoryTruckState.Blocked
                && string.IsNullOrWhiteSpace(truck.BlockingReason))
            {
                error = $"Truck {truck.Guid:D} is blocked without a readable reason.";
                return false;
            }

            if (truck.State == FactoryTruckState.Returning && truck.CargoCount != 0)
            {
                error = $"Truck {truck.Guid:D} is returning while still holding cargo.";
                return false;
            }

            if (truck.RemainingTravelSeconds < 0f
                || truck.LoadingWindowProgress < 0f
                || float.IsNaN(truck.RemainingTravelSeconds)
                || float.IsInfinity(truck.RemainingTravelSeconds)
                || float.IsNaN(truck.LoadingWindowProgress)
                || float.IsInfinity(truck.LoadingWindowProgress))
            {
                error = $"Truck {truck.Guid:D} progress values must be finite and non-negative.";
                return false;
            }
        }

        var migrationGuids = new HashSet<Guid>();
        var migrationKeys = new HashSet<string>();
        foreach (var mapping in snapshot.MigrationMappings)
        {
            if (mapping is null
                || string.IsNullOrWhiteSpace(mapping.Kind)
                || string.IsNullOrWhiteSpace(mapping.LegacyKey)
                || mapping.Guid == Guid.Empty)
            {
                error = "A migration mapping has an empty key or GUID.";
                return false;
            }

            if (!migrationKeys.Add($"{mapping.Kind}:{mapping.LegacyKey}"))
            {
                error = $"Duplicate migration mapping {mapping.Kind}:{mapping.LegacyKey}.";
                return false;
            }

            migrationGuids.Add(mapping.Guid);
        }

        return true;
    }

    private static bool TryValidateTruckRouteEndpoint(
        Guid routeGuid,
        FactoryWorldEndpoint endpoint,
        HashSet<Guid> buildings,
        HashSet<Guid> floors,
        HashSet<Guid> entities,
        Dictionary<Guid, Guid> floorParents,
        Dictionary<Guid, int> floorIndexByGuid,
        Dictionary<Guid, Guid> entityParents,
        Dictionary<Guid, FactoryWorldEntityRecord> entityRecords,
        out string error)
    {
        error = string.Empty;
        if (!buildings.Contains(endpoint.BuildingGuid))
        {
            error = $"Truck route {routeGuid:D} references missing building {endpoint.BuildingGuid:D}.";
            return false;
        }

        if (!floors.Contains(endpoint.FloorGuid)
            || !floorParents.TryGetValue(endpoint.FloorGuid, out var floorParent)
            || floorParent != endpoint.BuildingGuid
            || !floorIndexByGuid.TryGetValue(endpoint.FloorGuid, out var floorIndex)
            || floorIndex != endpoint.FloorIndex)
        {
            error = $"Truck route {routeGuid:D} endpoint floor {endpoint.FloorGuid:D} is inconsistent.";
            return false;
        }

        if (!entities.Contains(endpoint.EntityGuid)
            || !entityParents.TryGetValue(endpoint.EntityGuid, out var entityParent)
            || entityParent != endpoint.FloorGuid)
        {
            error = $"Truck route {routeGuid:D} endpoint entity {endpoint.EntityGuid:D} is missing or inconsistent.";
            return false;
        }

        if (!entityRecords.TryGetValue(endpoint.EntityGuid, out _))
        {
            error = $"Truck route {routeGuid:D} endpoint entity {endpoint.EntityGuid:D} has no record.";
            return false;
        }

        return true;
    }
}
