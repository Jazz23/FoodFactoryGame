// Reconciles disposable interior geometry from an immutable runtime presentation snapshot.
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct Factory3DInteriorProxySettings
{
    public Factory3DInteriorProxySettings(
        float newStoryHeight,
        float newFloorSlabThickness,
        float newWallThickness,
        float newWallHeight,
        float newMarkerSize,
        Material newMaterial = null)
    {
        StoryHeight = SanitizePositive(newStoryHeight);
        FloorSlabThickness = SanitizePositive(newFloorSlabThickness);
        WallThickness = SanitizePositive(newWallThickness);
        WallHeight = SanitizePositive(newWallHeight);
        MarkerSize = SanitizePositive(newMarkerSize);
        Material = newMaterial;
    }

    public float StoryHeight { get; }
    public float FloorSlabThickness { get; }
    public float WallThickness { get; }
    public float WallHeight { get; }
    public float MarkerSize { get; }
    public Material Material { get; }

    public static Factory3DInteriorProxySettings ForGrid(
        float storyHeight,
        float cellSize,
        Material material = null)
    {
        var safeCellSize = SanitizePositive(cellSize);
        var safeStoryHeight = SanitizePositive(storyHeight);
        return new Factory3DInteriorProxySettings(
            safeStoryHeight,
            safeCellSize * 0.08f,
            safeCellSize * 0.08f,
            Mathf.Min(safeStoryHeight * 0.9f, safeStoryHeight),
            safeCellSize * 0.55f,
            material);
    }

    private static float SanitizePositive(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? 0.01f
            : Mathf.Max(0.01f, value);
    }
}

public readonly struct Factory3DInteriorProxyBuildResult
{
    public Factory3DInteriorProxyBuildResult(
        uint buildingInstanceId,
        int floorIndex,
        int proxyCount,
        int entityCount,
        int removedNodeCount)
    {
        BuildingInstanceId = buildingInstanceId;
        FloorIndex = floorIndex;
        ProxyCount = proxyCount;
        EntityCount = entityCount;
        RemovedNodeCount = removedNodeCount;
    }

    public uint BuildingInstanceId { get; }
    public int FloorIndex { get; }
    public int ProxyCount { get; }
    public int EntityCount { get; }
    public int RemovedNodeCount { get; }
    public bool IsValid => BuildingInstanceId != 0 && FloorIndex >= 0;
}

public sealed class Factory3DInteriorProxyAssembler
{
    public const string FloorSlabPrefix = "Interior Floor Slab";
    public const string WallPrefix = "Interior Wall";
    public const string EntityPrefix = "Interior Entity";

    private readonly List<Factory3DRouteEntitySnapshot> validEntities = new();

    public Factory3DInteriorProxyBuildResult Reconcile(
        Transform targetRoot,
        SceneGrid grid,
        Factory3DRouteSnapshot snapshot,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        Factory3DInteriorProxySettings settings)
    {
        if (targetRoot is null || !targetRoot)
        {
            return default;
        }

        if (grid is null
            || !grid
            || snapshot is null
            || buildingInstanceId == 0
            || floorIndex < 0
            || !BuildingFootprint.IsValid(interiorSize)
            || !TryGetFloor(snapshot, buildingInstanceId, floorIndex, out var floor))
        {
            return ClearWithResult(targetRoot, buildingInstanceId, floorIndex);
        }

        var effectiveSettings = settings.StoryHeight <= 0f
            ? Factory3DInteriorProxySettings.ForGrid(3f, grid.CellSize, settings.Material)
            : settings;
        var adapter = grid.CreateSpatialAdapter();
        var elevation = BuildingCoordinates.GetFloorElevation(
            floorIndex,
            effectiveSettings.StoryHeight);
        var desiredIds = new HashSet<string>();

        var slabId = GetFloorSlabId(buildingInstanceId, floorIndex);
        desiredIds.Add(slabId);
        var slab = GetOrCreatePrimitive(
            targetRoot,
            slabId,
            Factory3DInteriorProxyKind.FloorSlab,
            buildingInstanceId,
            floorIndex,
            material: effectiveSettings.Material);
        ConfigureSlab(slab, adapter, buildingInstanceId, floorIndex, interiorSize, elevation, effectiveSettings);

        var wallCount = 0;
        ConfigureWall(
            targetRoot,
            desiredIds,
            "South",
            new Vector2(0f, 0f),
            new Vector2(interiorSize.x, 0f),
            adapter,
            buildingInstanceId,
            floorIndex,
            elevation,
            effectiveSettings);
        wallCount++;
        ConfigureWall(
            targetRoot,
            desiredIds,
            "East",
            new Vector2(interiorSize.x, 0f),
            new Vector2(interiorSize.x, interiorSize.y),
            adapter,
            buildingInstanceId,
            floorIndex,
            elevation,
            effectiveSettings);
        wallCount++;
        ConfigureWall(
            targetRoot,
            desiredIds,
            "North",
            new Vector2(interiorSize.x, interiorSize.y),
            new Vector2(0f, interiorSize.y),
            adapter,
            buildingInstanceId,
            floorIndex,
            elevation,
            effectiveSettings);
        wallCount++;
        ConfigureWall(
            targetRoot,
            desiredIds,
            "West",
            new Vector2(0f, interiorSize.y),
            new Vector2(0f, 0f),
            adapter,
            buildingInstanceId,
            floorIndex,
            elevation,
            effectiveSettings);
        wallCount++;

        validEntities.Clear();
        var entityIds = new HashSet<uint>();
        foreach (var entity in floor.Entities)
        {
            if (entity.EntityId == 0 || !entityIds.Add(entity.EntityId))
            {
                continue;
            }

            validEntities.Add(entity);
        }

        validEntities.Sort((left, right) => left.EntityId.CompareTo(right.EntityId));
        foreach (var entity in validEntities)
        {
            var entityId = GetEntityId(buildingInstanceId, floorIndex, entity.EntityId);
            desiredIds.Add(entityId);
            var entityObject = GetOrCreatePrimitive(
                targetRoot,
                entityId,
                Factory3DInteriorProxyKind.Entity,
                buildingInstanceId,
                floorIndex,
                entity.EntityId,
                entity.DefinitionId,
                effectiveSettings.Material);
            ConfigureEntity(entityObject, adapter, buildingInstanceId, floorIndex, entity, elevation, effectiveSettings);
        }

        var removedNodeCount = RemoveChildrenNotIn(targetRoot, desiredIds);
        return new Factory3DInteriorProxyBuildResult(
            buildingInstanceId,
            floorIndex,
            1 + wallCount + validEntities.Count,
            validEntities.Count,
            removedNodeCount);
    }

    public void Clear(Transform targetRoot)
    {
        if (targetRoot is not null && targetRoot)
        {
            RemoveChildrenNotIn(targetRoot, new HashSet<string>());
        }
    }

    public static string GetFloorSlabId(uint buildingInstanceId, int floorIndex)
    {
        return $"{FloorSlabPrefix} B{buildingInstanceId}-F{floorIndex}";
    }

    public static string GetWallId(uint buildingInstanceId, int floorIndex, string side)
    {
        return $"{WallPrefix} B{buildingInstanceId}-F{floorIndex}-{side}";
    }

    public static string GetEntityId(uint buildingInstanceId, int floorIndex, uint entityId)
    {
        return $"{EntityPrefix} B{buildingInstanceId}-F{floorIndex}-E{entityId}";
    }

    private static Factory3DInteriorProxyBuildResult ClearWithResult(
        Transform targetRoot,
        uint buildingInstanceId,
        int floorIndex)
    {
        var removedNodeCount = targetRoot.childCount;
        new Factory3DInteriorProxyAssembler().Clear(targetRoot);
        return new Factory3DInteriorProxyBuildResult(
            buildingInstanceId,
            floorIndex,
            0,
            0,
            removedNodeCount);
    }

    private static bool TryGetFloor(
        Factory3DRouteSnapshot snapshot,
        uint buildingInstanceId,
        int floorIndex,
        out Factory3DRouteFloorSnapshot floor)
    {
        foreach (var candidate in snapshot.Floors)
        {
            if (candidate.BuildingInstanceId == buildingInstanceId
                && candidate.FloorIndex == floorIndex)
            {
                floor = candidate;
                return true;
            }
        }

        floor = default;
        return false;
    }

    private static void ConfigureSlab(
        GameObject slab,
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        float elevation,
        Factory3DInteriorProxySettings settings)
    {
        var corners = new[]
        {
            ToWorld(adapter, buildingInstanceId, floorIndex, new Vector2(0f, 0f), elevation),
            ToWorld(adapter, buildingInstanceId, floorIndex, new Vector2(interiorSize.x, 0f), elevation),
            ToWorld(adapter, buildingInstanceId, floorIndex, new Vector2(interiorSize.x, interiorSize.y), elevation),
            ToWorld(adapter, buildingInstanceId, floorIndex, new Vector2(0f, interiorSize.y), elevation)
        };
        var minimum = corners[0];
        var maximum = corners[0];
        foreach (var corner in corners)
        {
            minimum = Vector3.Min(minimum, corner);
            maximum = Vector3.Max(maximum, corner);
        }

        slab.transform.SetPositionAndRotation(
            new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                elevation - settings.FloorSlabThickness * 0.5f,
                (minimum.z + maximum.z) * 0.5f),
            Quaternion.identity);
        slab.transform.localScale = new Vector3(
            Mathf.Max(0.01f, maximum.x - minimum.x),
            settings.FloorSlabThickness,
            Mathf.Max(0.01f, maximum.z - minimum.z));
    }

    private static void ConfigureWall(
        Transform targetRoot,
        HashSet<string> desiredIds,
        string side,
        Vector2 logicalStart,
        Vector2 logicalEnd,
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        float elevation,
        Factory3DInteriorProxySettings settings)
    {
        var canonicalId = GetWallId(buildingInstanceId, floorIndex, side);
        desiredIds.Add(canonicalId);
        var wall = GetOrCreatePrimitive(
            targetRoot,
            canonicalId,
            Factory3DInteriorProxyKind.Wall,
            buildingInstanceId,
            floorIndex,
            material: settings.Material);
        var start = ToWorld(adapter, buildingInstanceId, floorIndex, logicalStart, elevation);
        var end = ToWorld(adapter, buildingInstanceId, floorIndex, logicalEnd, elevation);
        var direction = end - start;
        direction.y = 0f;
        var length = new Vector2(direction.x, direction.z).magnitude;
        wall.transform.SetPositionAndRotation(
            (start + end) * 0.5f + Vector3.up * (settings.WallHeight * 0.5f),
            direction.sqrMagnitude <= 0.000001f
                ? Quaternion.identity
                : Quaternion.LookRotation(direction, Vector3.up));
        wall.transform.localScale = new Vector3(
            settings.WallThickness,
            settings.WallHeight,
            Mathf.Max(settings.WallThickness, length));
    }

    private static void ConfigureEntity(
        GameObject entityObject,
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Factory3DRouteEntitySnapshot entity,
        float elevation,
        Factory3DInteriorProxySettings settings)
    {
        var height = entity.RouteRole switch
        {
            Factory3DRouteRole.Belt => settings.MarkerSize * 0.35f,
            Factory3DRouteRole.Elevator => settings.MarkerSize * 0.8f,
            Factory3DRouteRole.Storage => settings.MarkerSize * 1.1f,
            _ => settings.MarkerSize
        };
        var position = ToWorld(
            adapter,
            buildingInstanceId,
            floorIndex,
            entity.LogicalPosition,
            elevation);
        entityObject.transform.SetPositionAndRotation(
            position + Vector3.up * (height * 0.5f),
            entity.WorldOrientation);
        entityObject.transform.localScale = new Vector3(
            settings.MarkerSize,
            height,
            settings.MarkerSize);
    }

    private static Vector3 ToWorld(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2 logicalPosition,
        float elevation)
    {
        return adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                logicalPosition),
            elevation);
    }

    private static GameObject GetOrCreatePrimitive(
        Transform parent,
        string canonicalId,
        Factory3DInteriorProxyKind kind,
        uint buildingInstanceId,
        int floorIndex,
        uint entityId = 0,
        string definitionId = "",
        Material material = null)
    {
        var existing = FindByCanonicalId(parent, canonicalId);
        if (existing is not null && existing)
        {
            existing.SetActive(true);
            existing.GetComponent<Factory3DInteriorProxyView>()!.Configure(
                canonicalId,
                buildingInstanceId,
                floorIndex,
                kind,
                entityId,
                definitionId);
            SetMaterial(existing, material);
            RemoveCollider(existing);
            return existing;
        }

        var created = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.name = canonicalId;
        created.hideFlags = parent.gameObject.hideFlags;
        created.transform.SetParent(parent, false);
        created.AddComponent<Factory3DInteriorProxyView>().Configure(
            canonicalId,
            buildingInstanceId,
            floorIndex,
            kind,
            entityId,
            definitionId);
        SetMaterial(created, material);
        RemoveCollider(created);
        return created;
    }

    private static void SetMaterial(GameObject target, Material material)
    {
        if (material is not null)
        {
            target.GetComponent<MeshRenderer>()!.sharedMaterial = material;
        }
    }

    private static GameObject FindByCanonicalId(Transform parent, string canonicalId)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index).gameObject;
            var identity = child.GetComponent<Factory3DInteriorProxyView>();
            if (identity is not null && identity.CanonicalId == canonicalId)
            {
                return child;
            }
        }

        return null!;
    }

    private static int RemoveChildrenNotIn(Transform parent, HashSet<string> desiredIds)
    {
        var stale = new List<GameObject>();
        var seenIds = new HashSet<string>();
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index).gameObject;
            var identity = child.GetComponent<Factory3DInteriorProxyView>();
            var canonicalId = identity is not null ? identity.CanonicalId : child.name;
            if (!desiredIds.Contains(canonicalId) || !seenIds.Add(canonicalId))
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

    private static void RemoveCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider is not null && collider)
        {
            DestroyGeneratedObject(collider);
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
