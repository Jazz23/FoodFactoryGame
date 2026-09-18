// Reconciles a bounded, disposable 3D route view without mutating FactoryWorldState.
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct Factory3DRouteProxyBuildResult
{
    public Factory3DRouteProxyBuildResult(
        int entityCount,
        int connectionCount,
        int elevatorTransferCount,
        int removedNodeCount)
    {
        EntityCount = entityCount;
        ConnectionCount = connectionCount;
        ElevatorTransferCount = elevatorTransferCount;
        RemovedNodeCount = removedNodeCount;
    }

    public int EntityCount { get; }
    public int ConnectionCount { get; }
    public int ElevatorTransferCount { get; }
    public int RemovedNodeCount { get; }
}

public sealed class Factory3DRouteProxyAssembler
{
    public const string RootName = "Factory 3D Route Proxies";

    private const string EntityPrefix = "Entity ";
    private const string ConnectionPrefix = "Connection ";
    private const string ElevatorPrefix = "Elevator Transfer ";

    public Factory3DRouteProxyBuildResult Reconcile(
        Transform targetRoot,
        Factory3DRouteSnapshot snapshot,
        int activeFloorIndex = -1,
        Material material = null)
    {
        if (targetRoot is null || !targetRoot)
        {
            return default;
        }

        if (snapshot is null)
        {
            return new Factory3DRouteProxyBuildResult(0, 0, 0, RemoveChildrenNotIn(targetRoot, new HashSet<string>()));
        }

        var selectedEntities = new Dictionary<Factory3DRouteEntityId, Factory3DRouteEntitySnapshot>();
        var desiredNames = new HashSet<string>();
        foreach (var entity in snapshot.Entities)
        {
            if (!IsFloorSelected(entity.FloorIndex, activeFloorIndex))
            {
                continue;
            }

            var canonicalId = GetEntityCanonicalId(entity.Id);
            selectedEntities[entity.Id] = entity;
            desiredNames.Add(canonicalId);
            var entityObject = GetOrCreatePrimitive(
                targetRoot,
                canonicalId,
                Factory3DRouteProxyKind.Entity,
                entity.DefinitionId,
                PrimitiveType.Cube,
                material,
                entity.Id);
            ConfigureEntity(entityObject, entity);
        }

        var connectionCount = 0;
        foreach (var connection in snapshot.Connections)
        {
            if (!selectedEntities.TryGetValue(connection.Source, out var source)
                || !selectedEntities.TryGetValue(connection.Destination, out var destination))
            {
                continue;
            }

            var canonicalId = GetConnectionCanonicalId(connection);
            desiredNames.Add(canonicalId);
            var connectionObject = GetOrCreatePrimitive(
                targetRoot,
                canonicalId,
                Factory3DRouteProxyKind.Connection,
                string.Empty,
                PrimitiveType.Cube,
                material,
                null,
                connection.Source,
                connection.Destination);
            ConfigureRoute(connectionObject, source.WorldPosition, destination.WorldPosition, 0.12f, material);
            connectionCount++;
        }

        var elevatorTransferCount = 0;
        foreach (var entity in selectedEntities.Values)
        {
            if (!entity.PairedElevatorId.HasValue
                || !selectedEntities.TryGetValue(entity.PairedElevatorId.Value, out var paired)
                || CompareIds(entity.Id, paired.Id) >= 0)
            {
                continue;
            }

            var canonicalId = GetElevatorTransferCanonicalId(entity.Id, paired.Id);
            desiredNames.Add(canonicalId);
            var transferObject = GetOrCreatePrimitive(
                targetRoot,
                canonicalId,
                Factory3DRouteProxyKind.ElevatorTransfer,
                string.Empty,
                PrimitiveType.Cube,
                material,
                null,
                entity.Id,
                paired.Id);
            ConfigureRoute(transferObject, entity.WorldPosition, paired.WorldPosition, 0.18f, material);
            elevatorTransferCount++;
        }

        var removedNodeCount = RemoveChildrenNotIn(targetRoot, desiredNames);
        return new Factory3DRouteProxyBuildResult(
            selectedEntities.Count,
            connectionCount,
            elevatorTransferCount,
            removedNodeCount);
    }

    public void Clear(Transform targetRoot)
    {
        if (targetRoot is not null && targetRoot)
        {
            RemoveChildrenNotIn(targetRoot, new HashSet<string>());
        }
    }

    public static string GetEntityCanonicalId(Factory3DRouteEntityId id)
    {
        return $"{EntityPrefix}B{id.BuildingId.Value}-F{id.FloorIndex}-E{id.EntityId}";
    }

    public static string GetConnectionCanonicalId(Factory3DRouteConnectionSnapshot connection)
    {
        return $"{ConnectionPrefix}{FormatId(connection.Source)}_to_{FormatId(connection.Destination)}";
    }

    public static string GetElevatorTransferCanonicalId(
        Factory3DRouteEntityId first,
        Factory3DRouteEntityId second)
    {
        return CompareIds(first, second) <= 0
            ? $"{ElevatorPrefix}{FormatId(first)}_to_{FormatId(second)}"
            : $"{ElevatorPrefix}{FormatId(second)}_to_{FormatId(first)}";
    }

    private static bool IsFloorSelected(int floorIndex, int activeFloorIndex)
    {
        return activeFloorIndex < 0 || floorIndex == activeFloorIndex;
    }

    private static GameObject GetOrCreatePrimitive(
        Transform parent,
        string canonicalId,
        Factory3DRouteProxyKind kind,
        string definitionId,
        PrimitiveType primitiveType,
        Material material,
        Factory3DRouteEntityId? entityId = null,
        Factory3DRouteEntityId? sourceEntityId = null,
        Factory3DRouteEntityId? destinationEntityId = null)
    {
        var existing = FindByCanonicalId(parent, canonicalId);
        if (existing is not null && existing)
        {
            existing.SetActive(true);
            var existingIdentity = existing.GetComponent<Factory3DRouteProxyIdentity>();
            existingIdentity.Configure(
                canonicalId,
                kind,
                definitionId,
                entityId,
                sourceEntityId,
                destinationEntityId);
            ConfigureCollider(existing);
            SetMaterial(existing, material);
            return existing;
        }

        var created = GameObject.CreatePrimitive(primitiveType);
        created.name = canonicalId;
        created.hideFlags = parent.gameObject.hideFlags;
        created.transform.SetParent(parent, false);
        var identity = created.AddComponent<Factory3DRouteProxyIdentity>();
        identity.Configure(
            canonicalId,
            kind,
            definitionId,
            entityId,
            sourceEntityId,
            destinationEntityId);
        ConfigureCollider(created);
        SetMaterial(created, material);
        return created;
    }

    private static void ConfigureEntity(GameObject target, Factory3DRouteEntitySnapshot entity)
    {
        var height = entity.RouteRole switch
        {
            Factory3DRouteRole.Belt => 0.18f,
            Factory3DRouteRole.Elevator => 0.42f,
            Factory3DRouteRole.Storage => 0.7f,
            _ => 0.55f
        };
        target.transform.SetPositionAndRotation(entity.WorldPosition + Vector3.up * height * 0.5f, entity.WorldOrientation);
        target.transform.localScale = entity.RouteRole == Factory3DRouteRole.Belt
            ? new Vector3(0.75f, height, 0.75f)
            : new Vector3(0.8f, height, 0.8f);
    }

    private static void ConfigureRoute(
        GameObject target,
        Vector3 start,
        Vector3 end,
        float thickness,
        Material material)
    {
        var direction = end - start;
        var length = direction.magnitude;
        target.transform.SetPositionAndRotation(
            (start + end) * 0.5f,
            length <= 0.0001f ? Quaternion.identity : Quaternion.LookRotation(direction, Vector3.up));
        target.transform.localScale = new Vector3(thickness, thickness, Mathf.Max(thickness, length));
        SetMaterial(target, material);
    }

    private static GameObject FindByCanonicalId(Transform parent, string canonicalId)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index).gameObject;
            var identity = child.GetComponent<Factory3DRouteProxyIdentity>();
            if (identity is not null && identity.CanonicalId == canonicalId)
            {
                return child;
            }
        }

        return null!;
    }

    private static int RemoveChildrenNotIn(Transform parent, HashSet<string> desiredNames)
    {
        var stale = new List<GameObject>();
        var seenNames = new HashSet<string>();
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index).gameObject;
            if (!desiredNames.Contains(child.name) || !seenNames.Add(child.name))
            {
                stale.Add(child);
            }
        }

        foreach (var child in stale)
        {
            DestroyGeneratedObject(child);
        }

        return stale.Count;
    }

    private static int CompareIds(Factory3DRouteEntityId left, Factory3DRouteEntityId right)
    {
        var result = left.BuildingId.Value.CompareTo(right.BuildingId.Value);
        if (result != 0) return result;
        result = left.FloorIndex.CompareTo(right.FloorIndex);
        return result != 0 ? result : left.EntityId.CompareTo(right.EntityId);
    }

    private static string FormatId(Factory3DRouteEntityId id)
    {
        return $"B{id.BuildingId.Value}-F{id.FloorIndex}-E{id.EntityId}";
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        if (material is not null)
        {
            target.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    private static void ConfigureCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider is not null && collider)
        {
            collider.isTrigger = true;
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object target)
    {
        if (target is null || !target)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
