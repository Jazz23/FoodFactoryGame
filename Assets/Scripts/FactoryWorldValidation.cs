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

            if (connection.Source.BuildingGuid == connection.Destination.BuildingGuid
                && connection.Source.FloorGuid == connection.Destination.FloorGuid)
            {
                error = $"Connection {connection.Guid:D} endpoints must be on different floors.";
                return false;
            }

            var sourceDefinition = FactoryEntityDefinitions.Get(
                entityRecords[connection.Source.EntityGuid].DefinitionId);
            var destinationDefinition = FactoryEntityDefinitions.Get(
                entityRecords[connection.Destination.EntityGuid].DefinitionId);
            if (!sourceDefinition.IsProducer)
            {
                error = $"Connection {connection.Guid:D} source must produce an item.";
                return false;
            }

            if (!destinationDefinition.IsReceiver)
            {
                error = $"Connection {connection.Guid:D} destination must accept an item.";
                return false;
            }

            if (sourceDefinition.ProducedItemId != destinationDefinition.AcceptedItemId)
            {
                error = $"Connection {connection.Guid:D} item types do not match.";
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
}
