// Assembles disposable 3D OutsideTest blockouts from the authoritative logical records.
using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct Factory3DOutsideTestProxySettings
{
    public Factory3DOutsideTestProxySettings(
        float newStoryHeight,
        float newWallHeight,
        float newFloorSlabThickness,
        float newWallThickness,
        float newMarkerSize,
        float newDoorCornerExclusionDistance,
        Material newMaterial = null,
        float newRoofThickness = 0f)
    {
        StoryHeight = SanitizeNonNegative(newStoryHeight);
        WallHeight = SanitizeNonNegative(newWallHeight);
        FloorSlabThickness = SanitizePositive(newFloorSlabThickness);
        WallThickness = SanitizePositive(newWallThickness);
        MarkerSize = SanitizePositive(newMarkerSize);
        DoorCornerExclusionDistance = SanitizeNonNegative(newDoorCornerExclusionDistance);
        Material = newMaterial;
        RoofThickness = SanitizePositive(
            newRoofThickness > 0f ? newRoofThickness : newFloorSlabThickness);
    }

    public float StoryHeight { get; }
    public float WallHeight { get; }
    public float FloorSlabThickness { get; }
    public float WallThickness { get; }
    public float MarkerSize { get; }
    public float DoorCornerExclusionDistance { get; }
    public Material Material { get; }
    public float RoofThickness { get; }

    public static Factory3DOutsideTestProxySettings ForGrid(
        float wallHeight,
        float cellSize,
        float doorCornerExclusionDistance,
        Material material = null)
    {
        var safeCellSize = SanitizePositive(cellSize);
        var safeWallHeight = SanitizeNonNegative(wallHeight);
        return new Factory3DOutsideTestProxySettings(
            safeWallHeight,
            safeWallHeight,
            safeCellSize * 0.12f,
            safeCellSize * WallCellGeometry.ThicknessInCells,
            safeCellSize * 0.7f,
            doorCornerExclusionDistance,
            material,
            safeCellSize * 0.12f);
    }

    private static float SanitizeNonNegative(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? 0f
            : Mathf.Max(0f, value);
    }

    private static float SanitizePositive(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value)
            ? 0.01f
            : Mathf.Max(0.01f, value);
    }
}

public readonly struct Factory3DOutsideTestProxyBuildResult
{
    public Factory3DOutsideTestProxyBuildResult(
        int newBuildingCount,
        int newFloorCount,
        int newWallCount,
        int newWallSegmentCount,
        int newDoorCount,
        int newEquipmentCount,
        int newRemovedNodeCount,
        int newSkippedBuildingCount)
    {
        BuildingCount = newBuildingCount;
        FloorCount = newFloorCount;
        WallCount = newWallCount;
        WallSegmentCount = newWallSegmentCount;
        DoorCount = newDoorCount;
        EquipmentCount = newEquipmentCount;
        RemovedNodeCount = newRemovedNodeCount;
        SkippedBuildingCount = newSkippedBuildingCount;
    }

    public int BuildingCount { get; }
    public int FloorCount { get; }
    public int WallCount { get; }
    public int WallSegmentCount { get; }
    public int DoorCount { get; }
    public int EquipmentCount { get; }
    public int RemovedNodeCount { get; }
    public int SkippedBuildingCount { get; }
}

public readonly struct Factory3DOutsideTestProxyBuildingSource
{
    public Factory3DOutsideTestProxyBuildingSource(
        BuildingRecord newRecord,
        bool newIsInteriorOnly,
        Vector2Int newUsableInteriorSize)
    {
        Record = newRecord;
        IsInteriorOnly = newIsInteriorOnly;
        UsableInteriorSize = newUsableInteriorSize;
    }

    public BuildingRecord Record { get; }
    public bool IsInteriorOnly { get; }
    public Vector2Int UsableInteriorSize { get; }
}

public sealed class Factory3DOutsideTestProxyAssembler
{
    public const string RootName = "Factory 3D OutsideTest Proxies";
    public const string FloorSlabName = "Floor Slab";
    public const string RoofName = "Roof";

    private const string GeneratedMeshPrefix = "Factory3DOutsideTestProxy";

    private readonly List<Factory3DOutsideTestProxyBuildingSource> preparedSources = new();
    private readonly List<Factory3DOutsideTestProxyBuildingSource> candidateSources = new();
    private readonly List<BuildingRecord> validRecords = new();
    private readonly List<OutsideTestFloorRecord> candidateFloors = new();
    private readonly Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord> floorsByKey = new();
    private readonly Dictionary<uint, BuildingRecord> recordsById = new();
    private readonly Dictionary<uint, bool> equipmentUsesShellInsetById = new();
    private readonly List<TestBuildingCreator.WallPlacement> wallPlacements = new();
    private readonly List<TestBuildingCreator.ExteriorWallSpan> wallSpans = new();
    private readonly List<GridWall.PlaneSegment> wallSegments = new();
    private readonly List<BuildingRecord.DoorPlacement> validDoors = new();

    public Factory3DOutsideTestProxyBuildResult Reconcile(
        Transform targetRoot,
        SceneGrid grid,
        FactoryWorldState state,
        Factory3DOutsideTestProxySettings settings)
    {
        if (state is null)
        {
            return Reconcile(
                targetRoot,
                grid,
                Array.Empty<BuildingRecord>(),
                Array.Empty<OutsideTestFloorRecord>(),
                settings);
        }

        preparedSources.Clear();
        foreach (var record in state.BuildingRecords)
        {
            if (record is null)
            {
                continue;
            }

            var usableInteriorSize = state.GetInteriorSize(record);
            preparedSources.Add(new Factory3DOutsideTestProxyBuildingSource(
                record,
                usableInteriorSize == record.FootprintSize,
                usableInteriorSize));
        }

        return ReconcileInternal(
            targetRoot,
            grid,
            preparedSources,
            state.FloorStates,
            settings);
    }

    public Factory3DOutsideTestProxyBuildResult Reconcile(
        Transform targetRoot,
        SceneGrid grid,
        IEnumerable<BuildingRecord> sourceRecords,
        IEnumerable<OutsideTestFloorRecord> sourceFloors,
        Factory3DOutsideTestProxySettings settings)
    {
        preparedSources.Clear();
        if (sourceRecords is not null)
        {
            foreach (var record in sourceRecords)
            {
                if (record is not null)
                {
                    preparedSources.Add(CreateShellSource(record));
                }
            }
        }

        return ReconcileInternal(
            targetRoot,
            grid,
            preparedSources,
            sourceFloors,
            settings);
    }

    public Factory3DOutsideTestProxyBuildResult Reconcile(
        Transform targetRoot,
        SceneGrid grid,
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> sourceBuildings,
        IEnumerable<OutsideTestFloorRecord> sourceFloors,
        Factory3DOutsideTestProxySettings settings)
    {
        return ReconcileInternal(
            targetRoot,
            grid,
            sourceBuildings,
            sourceFloors,
            settings);
    }

    private Factory3DOutsideTestProxyBuildResult ReconcileInternal(
        Transform targetRoot,
        SceneGrid grid,
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> sourceBuildings,
        IEnumerable<OutsideTestFloorRecord> sourceFloors,
        Factory3DOutsideTestProxySettings settings)
    {
        if (targetRoot is null || !targetRoot)
        {
            return default;
        }

        var removedNodeCount = 0;
        if (grid is null || !grid)
        {
            removedNodeCount += RemoveChildrenNotIn(
                targetRoot,
                new HashSet<string> { Factory3DOutsideTestProxyView.RouteProxyRootName });
            return new Factory3DOutsideTestProxyBuildResult(
                0,
                0,
                0,
                0,
                0,
                0,
                removedNodeCount,
                0);
        }

        var effectiveSettings = settings;
        if (effectiveSettings.WallThickness <= 0f
            || effectiveSettings.MarkerSize <= 0f
            || effectiveSettings.FloorSlabThickness <= 0f)
        {
            effectiveSettings = Factory3DOutsideTestProxySettings.ForGrid(
                settings.WallHeight,
                grid.CellSize,
                settings.DoorCornerExclusionDistance,
                settings.Material);
        }

        var skippedBuildingCount = CollectValidRecords(
            sourceBuildings,
            effectiveSettings.DoorCornerExclusionDistance);
        CollectFloorRecords(sourceFloors);
        var spatialAdapter = grid.CreateSpatialAdapter();
        var totalFloorCount = 0;
        var totalWallCount = 0;
        var totalWallSegmentCount = 0;
        var totalDoorCount = 0;
        var totalEquipmentCount = 0;
        var desiredBuildingNames = new HashSet<string>();
        foreach (var record in validRecords)
        {
            desiredBuildingNames.Add(GetBuildingName(record.BuildingInstanceId));
        }
        desiredBuildingNames.Add(Factory3DOutsideTestProxyView.RouteProxyRootName);

        removedNodeCount += RemoveChildrenNotIn(targetRoot, desiredBuildingNames);
        foreach (var record in validRecords)
        {
            var buildingRoot = GetOrCreateChild(
                targetRoot,
                GetBuildingName(record.BuildingInstanceId));
            ResetContainerTransform(buildingRoot);
            ReconcileBuilding(
                buildingRoot,
                record,
                equipmentUsesShellInsetById[record.BuildingInstanceId],
                spatialAdapter,
                effectiveSettings,
                out var buildingRemovedNodeCount,
                out var floorCount,
                out var wallCount,
                out var wallSegmentCount,
                out var doorCount,
                out var equipmentCount);
            removedNodeCount += buildingRemovedNodeCount;
            totalFloorCount += floorCount;
            totalWallCount += wallCount;
            totalWallSegmentCount += wallSegmentCount;
            totalDoorCount += doorCount;
            totalEquipmentCount += equipmentCount;
        }

        return new Factory3DOutsideTestProxyBuildResult(
            validRecords.Count,
            totalFloorCount,
            totalWallCount,
            totalWallSegmentCount,
            totalDoorCount,
            totalEquipmentCount,
            removedNodeCount,
            skippedBuildingCount);
    }

    public void Clear(Transform targetRoot)
    {
        if (targetRoot is null || !targetRoot)
        {
            return;
        }

        RemoveChildrenNotIn(
            targetRoot,
            new HashSet<string> { Factory3DOutsideTestProxyView.RouteProxyRootName });
    }

    public static string GetBuildingName(uint buildingInstanceId)
    {
        return $"Building {buildingInstanceId}";
    }

    public static string GetFloorName(int floorIndex)
    {
        return $"Floor {floorIndex}";
    }

    private int CollectValidRecords(
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> sourceBuildings,
        float doorCornerExclusionDistance)
    {
        candidateSources.Clear();
        validRecords.Clear();
        recordsById.Clear();
        equipmentUsesShellInsetById.Clear();
        if (sourceBuildings is not null)
        {
            foreach (var source in sourceBuildings)
            {
                if (source.Record is not null)
                {
                    candidateSources.Add(source);
                }
            }
        }

        candidateSources.Sort(CompareSources);
        var skippedCount = 0;
        foreach (var source in candidateSources)
        {
            var record = source.Record;
            if (record.BuildingInstanceId == 0
                || recordsById.ContainsKey(record.BuildingInstanceId)
                || !BuildingShellValidation.TryValidate(
                    record,
                    validRecords,
                    doorCornerExclusionDistance,
                    out _))
            {
                skippedCount++;
                continue;
            }

            recordsById.Add(record.BuildingInstanceId, record);
            equipmentUsesShellInsetById.Add(
                record.BuildingInstanceId,
                !source.IsInteriorOnly);
            validRecords.Add(record);
        }

        return skippedCount;
    }

    private void CollectFloorRecords(IEnumerable<OutsideTestFloorRecord> sourceFloors)
    {
        candidateFloors.Clear();
        floorsByKey.Clear();
        if (sourceFloors is not null)
        {
            foreach (var floor in sourceFloors)
            {
                if (floor is not null
                    && recordsById.ContainsKey(floor.BuildingInstanceId)
                    && floor.FloorIndex >= 0
                    && floor.FloorIndex < recordsById[floor.BuildingInstanceId].StoryCount)
                {
                    candidateFloors.Add(floor);
                }
            }
        }

        candidateFloors.Sort(CompareFloors);
        foreach (var floor in candidateFloors)
        {
            floorsByKey.TryAdd(
                new OutsideTestFloorKey(floor.BuildingInstanceId, floor.FloorIndex),
                floor);
        }
    }

    private void ReconcileBuilding(
        Transform buildingRoot,
        BuildingRecord record,
        bool equipmentUsesShellInset,
        FactorySpatialAdapter spatialAdapter,
        Factory3DOutsideTestProxySettings settings,
        out int removedNodeCount,
        out int floorCount,
        out int wallCount,
        out int wallSegmentCount,
        out int doorCount,
        out int equipmentCount)
    {
        var desiredFloorNames = new HashSet<string>();
        for (var floorIndex = 0; floorIndex < record.StoryCount; floorIndex++)
        {
            desiredFloorNames.Add(GetFloorName(floorIndex));
        }

        var removed = RemoveChildrenNotIn(buildingRoot, desiredFloorNames);
        removedNodeCount = removed;
        var floorResultCount = 0;
        var wallResultCount = 0;
        var wallSegmentResultCount = 0;
        var doorResultCount = 0;
        var equipmentResultCount = 0;
        for (var floorIndex = 0; floorIndex < record.StoryCount; floorIndex++)
        {
            var floorRoot = GetOrCreateChild(buildingRoot, GetFloorName(floorIndex));
            ResetContainerTransform(floorRoot);
            ReconcileFloor(
                floorRoot,
                record,
                floorIndex,
                equipmentUsesShellInset,
                spatialAdapter,
                settings,
                out var floorWallCount,
                out var floorWallSegmentCount,
                out var floorDoorCount,
                out var floorEquipmentCount);
            floorResultCount++;
            wallResultCount += floorWallCount;
            wallSegmentResultCount += floorWallSegmentCount;
            doorResultCount += floorDoorCount;
            equipmentResultCount += floorEquipmentCount;
        }

        floorCount = floorResultCount;
        wallCount = wallResultCount;
        wallSegmentCount = wallSegmentResultCount;
        doorCount = doorResultCount;
        equipmentCount = equipmentResultCount;
    }

    private void ReconcileFloor(
        Transform floorRoot,
        BuildingRecord record,
        int floorIndex,
        bool equipmentUsesShellInset,
        FactorySpatialAdapter spatialAdapter,
        Factory3DOutsideTestProxySettings settings,
        out int wallCount,
        out int wallSegmentCount,
        out int doorCount,
        out int equipmentCount)
    {
        var elevation = BuildingCoordinates.GetFloorElevation(
            floorIndex,
            settings.StoryHeight);
        var desiredNames = new HashSet<string> { FloorSlabName };
        if (floorIndex == record.StoryCount - 1)
        {
            desiredNames.Add(RoofName);
        }
        var secondCorner = record.AnchorCell + new Vector3Int(
            record.FootprintSize.x - 1,
            record.FootprintSize.y - 1);
        TestBuildingCreator.GetWallPlacements(
            record.AnchorCell,
            secondCorner,
            wallPlacements);
        foreach (var placement in wallPlacements)
        {
            desiredNames.Add(GetWallName(placement));
        }

        TestBuildingCreator.GetExteriorWallSpans(
            record.AnchorCell,
            record.FootprintSize,
            wallSpans);
        validDoors.Clear();
        foreach (var door in record.Doors)
        {
            // BuildingRecord doors are building-level data. The existing 2D shell
            // presents them on the ground floor because the record has no floor field.
            if (HasBuildingLevelDoorSemantics(floorIndex)
                && door is not null
                && TryGetWallSpan(wallSpans, door.WallId, out _))
            {
                validDoors.Add(door);
                desiredNames.Add(GetDoorName(door));
            }
        }

        var floorState = floorsByKey.TryGetValue(
            new OutsideTestFloorKey(record.BuildingInstanceId, floorIndex),
            out var state)
            ? state
            : null;
        var validEntities = new List<FactoryEntityRecord>();
        var seenEntityIds = new HashSet<uint>();
        if (floorState is not null)
        {
            foreach (var entity in floorState.Entities)
            {
                if (entity is not null
                    && entity.EntityId != 0
                    && seenEntityIds.Add(entity.EntityId))
                {
                    validEntities.Add(entity);
                    desiredNames.Add(GetEquipmentName(entity));
                }
            }
        }

        RemoveChildrenNotIn(floorRoot, desiredNames);
        var slab = GetOrCreateMeshChild(floorRoot, FloorSlabName);
        ConfigureFloorSlab(
            slab,
            record,
            floorIndex,
            spatialAdapter,
            elevation,
            settings);
        if (floorIndex == record.StoryCount - 1)
        {
            var roof = GetOrCreateMeshChild(floorRoot, RoofName);
            ConfigureRoof(
                roof,
                record,
                floorIndex,
                spatialAdapter,
                elevation,
                settings);
        }

        wallSegmentCount = 0;
        foreach (var placement in wallPlacements)
        {
            var wallRoot = GetOrCreateChild(floorRoot, GetWallName(placement));
            ResetContainerTransform(wallRoot);
            wallSegments.Clear();
            wallSegments.AddRange(GridWall.GetLogicalPlaneSegments(
                placement.Kind,
                placement.Cell));
            var desiredSegmentNames = new HashSet<string>();
            for (var segmentIndex = 0; segmentIndex < wallSegments.Count; segmentIndex++)
            {
                desiredSegmentNames.Add(GetWallSegmentName(segmentIndex));
            }

            RemoveChildrenNotIn(wallRoot, desiredSegmentNames);
            for (var segmentIndex = 0; segmentIndex < wallSegments.Count; segmentIndex++)
            {
                var segmentObject = GetOrCreateCubeChild(
                    wallRoot,
                    GetWallSegmentName(segmentIndex));
                ConfigureWallSegment(
                    segmentObject,
                    wallSegments[segmentIndex],
                    record.BuildingInstanceId,
                    floorIndex,
                    spatialAdapter,
                    elevation,
                    settings);
                wallSegmentCount++;
            }
        }

        doorCount = 0;
        foreach (var door in validDoors)
        {
            if (!TryGetWallSpan(wallSpans, door.WallId, out var wall))
            {
                continue;
            }

            var doorObject = GetOrCreateCubeChild(floorRoot, GetDoorName(door));
            ConfigureDoorMarker(
                doorObject,
                record.BuildingInstanceId,
                floorIndex,
                wall,
                door.NormalizedOffset,
                spatialAdapter,
                elevation,
                settings);
            doorCount++;
        }

        equipmentCount = 0;
        foreach (var entity in validEntities)
        {
            var equipmentObject = GetOrCreateCubeChild(
                floorRoot,
                GetEquipmentName(entity));
            ConfigureEquipmentMarker(
                equipmentObject,
                record,
                floorIndex,
                equipmentUsesShellInset,
                entity,
                spatialAdapter,
                elevation,
                settings);
            equipmentCount++;
        }

        wallCount = wallPlacements.Count;
    }

    private static void ConfigureFloorSlab(
        Transform slab,
        BuildingRecord record,
        int floorIndex,
        FactorySpatialAdapter spatialAdapter,
        float elevation,
        Factory3DOutsideTestProxySettings settings)
    {
        slab.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        slab.localScale = Vector3.one;
        var corners = GetWorldFootprintCorners(
            record,
            floorIndex,
            spatialAdapter,
            elevation);
        var meshFilter = GetOrAddComponent<MeshFilter>(slab.gameObject);
        var meshRenderer = GetOrAddComponent<MeshRenderer>(slab.gameObject);
        var oldMesh = meshFilter.sharedMesh;
        BuildPrismGeometry(
            slab,
            corners,
            elevation - settings.FloorSlabThickness,
            elevation,
            out var vertices,
            out var triangles);
        if (!HasGeneratedMeshGeometry(oldMesh, vertices, triangles))
        {
            meshFilter.sharedMesh = CreatePrismMesh(
                $"{GeneratedMeshPrefix} Floor {record.BuildingInstanceId} {floorIndex}",
                vertices,
                triangles);
            DestroyGeneratedMesh(oldMesh);
        }
        if (settings.Material is not null)
        {
            meshRenderer.sharedMaterial = settings.Material;
        }
        RemoveCollider(slab.gameObject);
    }

    private static void ConfigureRoof(
        Transform roof,
        BuildingRecord record,
        int floorIndex,
        FactorySpatialAdapter spatialAdapter,
        float elevation,
        Factory3DOutsideTestProxySettings settings)
    {
        roof.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        roof.localScale = Vector3.one;
        var top = elevation + settings.WallHeight;
        var corners = GetWorldFootprintCorners(
            record,
            floorIndex,
            spatialAdapter,
            top);
        var meshFilter = GetOrAddComponent<MeshFilter>(roof.gameObject);
        var meshRenderer = GetOrAddComponent<MeshRenderer>(roof.gameObject);
        var oldMesh = meshFilter.sharedMesh;
        BuildPrismGeometry(
            roof,
            corners,
            top - settings.RoofThickness,
            top,
            out var vertices,
            out var triangles);
        if (!HasGeneratedMeshGeometry(oldMesh, vertices, triangles))
        {
            meshFilter.sharedMesh = CreatePrismMesh(
                $"{GeneratedMeshPrefix} Roof {record.BuildingInstanceId} {floorIndex}",
                vertices,
                triangles);
            DestroyGeneratedMesh(oldMesh);
        }
        if (settings.Material is not null)
        {
            meshRenderer.sharedMaterial = settings.Material;
        }
        RemoveCollider(roof.gameObject);
    }

    private static List<Vector3> GetWorldFootprintCorners(
        BuildingRecord record,
        int floorIndex,
        FactorySpatialAdapter spatialAdapter,
        float elevation)
    {
        var corners = new List<Vector3>(4);
        var logicalCorners = new[]
        {
            new Vector2(record.AnchorCell.x, record.AnchorCell.y),
            new Vector2(record.AnchorCell.x + record.FootprintSize.x, record.AnchorCell.y),
            new Vector2(
                record.AnchorCell.x + record.FootprintSize.x,
                record.AnchorCell.y + record.FootprintSize.y),
            new Vector2(record.AnchorCell.x, record.AnchorCell.y + record.FootprintSize.y)
        };
        foreach (var logicalCorner in logicalCorners)
        {
            corners.Add(spatialAdapter.LogicalToWorld3D(
                new FactoryLogicalLocation(
                    record.BuildingInstanceId,
                    floorIndex,
                    logicalCorner),
                elevation));
        }
        return corners;
    }

    private static void ConfigureWallSegment(
        Transform wallObject,
        GridWall.PlaneSegment segment,
        uint buildingInstanceId,
        int floorIndex,
        FactorySpatialAdapter spatialAdapter,
        float elevation,
        Factory3DOutsideTestProxySettings settings)
    {
        var start = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                segment.Start),
            elevation);
        var end = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                segment.End),
            elevation);
        ConfigureBlock(
            wallObject,
            start,
            end,
            settings.WallThickness,
            settings.WallHeight,
            settings.Material);
    }

    private static void ConfigureDoorMarker(
        Transform doorObject,
        uint buildingInstanceId,
        int floorIndex,
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset,
        FactorySpatialAdapter spatialAdapter,
        float elevation,
        Factory3DOutsideTestProxySettings settings)
    {
        var logicalPosition = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            Mathf.Clamp01(normalizedOffset));
        var position = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                logicalPosition),
            elevation);
        var start = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                wall.LogicalStart),
            elevation);
        var end = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                wall.LogicalEnd),
            elevation);
        ConfigureBlock(
            doorObject,
            start,
            end,
            settings.WallThickness * 1.4f,
            Mathf.Max(settings.MarkerSize, settings.WallHeight * 0.8f),
            settings.Material,
            position,
            settings.MarkerSize);
    }

    private static void ConfigureEquipmentMarker(
        Transform equipmentObject,
        BuildingRecord record,
        int floorIndex,
        bool equipmentUsesShellInset,
        FactoryEntityRecord entity,
        FactorySpatialAdapter spatialAdapter,
        float elevation,
        Factory3DOutsideTestProxySettings settings)
    {
        var exteriorLocalPosition = equipmentUsesShellInset
            ? BuildingCoordinates.InteriorLocalToExteriorLocal(entity.LogicalPosition)
            : entity.LogicalPosition;
        var exteriorLogicalPosition = BuildingCoordinates.LocalToExteriorLogical(
            record.AnchorCell,
            exteriorLocalPosition);
        var position = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                record.BuildingInstanceId,
                floorIndex,
                exteriorLogicalPosition),
            elevation);
        var height = entity.IsConveyor || entity.IsElevator
            ? Mathf.Max(settings.MarkerSize * 0.3f, settings.WallHeight * 0.15f)
            : Mathf.Max(settings.MarkerSize * 0.7f, settings.WallHeight * 0.35f);
        var width = entity.IsConveyor
            ? settings.MarkerSize * 0.8f
            : settings.MarkerSize;
        var direction = entity.IsConveyor
            ? FactoryConveyor.Direction(entity.DefinitionId)
            : GetDockDirection(entity.DockDirection);
        var rotation = GetProjectedRotation(
            spatialAdapter,
            record.BuildingInstanceId,
            floorIndex,
            exteriorLogicalPosition,
            direction,
            elevation);
        equipmentObject.SetPositionAndRotation(
            position + Vector3.up * (height * 0.5f),
            rotation);
        equipmentObject.localScale = new Vector3(width, height, width);
        if (settings.Material is not null)
        {
            GetOrAddComponent<MeshRenderer>(equipmentObject.gameObject).sharedMaterial = settings.Material;
        }
        RemoveCollider(equipmentObject.gameObject);
    }

    private static void ConfigureBlock(
        Transform block,
        Vector3 start,
        Vector3 end,
        float thickness,
        float height,
        Material material,
        Vector3? explicitPosition = null,
        float explicitLength = 0f)
    {
        var direction = end - start;
        direction.y = 0f;
        var length = explicitLength > 0f
            ? explicitLength
            : new Vector2(direction.x, direction.z).magnitude;
        var center = explicitPosition ?? (start + end) * 0.5f;
        center.y = explicitPosition.HasValue
            ? explicitPosition.Value.y + height * 0.5f
            : start.y + height * 0.5f;
        var rotation = direction.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : Quaternion.identity;
        block.SetPositionAndRotation(center, rotation);
        block.localScale = new Vector3(
            Mathf.Max(0.01f, thickness),
            Mathf.Max(0.01f, height),
            Mathf.Max(0.01f, length));
        if (material is not null)
        {
            GetOrAddComponent<MeshRenderer>(block.gameObject).sharedMaterial = material;
        }
        RemoveCollider(block.gameObject);
    }

    private static Quaternion GetProjectedRotation(
        FactorySpatialAdapter spatialAdapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2 logicalPosition,
        Vector2 logicalDirection,
        float elevation)
    {
        if (logicalDirection.sqrMagnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        var start = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                logicalPosition),
            elevation);
        var end = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                buildingInstanceId,
                floorIndex,
                logicalPosition + logicalDirection),
            elevation);
        var direction = end - start;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.000001f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : Quaternion.identity;
    }

    private static Vector2 GetDockDirection(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.East => Vector2.right,
            GridEdgeDirection.North => Vector2.up,
            GridEdgeDirection.West => Vector2.left,
            _ => Vector2.down
        };
    }

    private static void BuildPrismGeometry(
        Transform target,
        IReadOnlyList<Vector3> worldFootprint,
        float bottom,
        float top,
        out Vector3[] vertices,
        out int[] triangles)
    {
        vertices = new Vector3[worldFootprint.Count * 2];
        for (var index = 0; index < worldFootprint.Count; index++)
        {
            vertices[index] = target.InverseTransformPoint(new Vector3(
                worldFootprint[index].x,
                bottom,
                worldFootprint[index].z));
            vertices[index + worldFootprint.Count] = target.InverseTransformPoint(new Vector3(
                worldFootprint[index].x,
                top,
                worldFootprint[index].z));
        }

        var triangleList = new List<int>(worldFootprint.Count * 12);
        var signedArea = 0f;
        for (var index = 0; index < worldFootprint.Count; index++)
        {
            var next = (index + 1) % worldFootprint.Count;
            signedArea += worldFootprint[index].x * worldFootprint[next].z
                - worldFootprint[index].z * worldFootprint[next].x;
        }

        // A positive XZ polygon area has a downward face normal in Unity's XYZ
        // cross-product convention, so reverse its top and side winding.
        var reverseForUpwardTop = signedArea >= 0f;
        for (var index = 0; index < worldFootprint.Count; index++)
        {
            var next = (index + 1) % worldFootprint.Count;
            if (reverseForUpwardTop)
            {
                AddQuad(
                    triangleList,
                    index,
                    index + worldFootprint.Count,
                    next + worldFootprint.Count,
                    next);
            }
            else
            {
                AddQuad(
                    triangleList,
                    index,
                    next,
                    next + worldFootprint.Count,
                    index + worldFootprint.Count);
            }
        }

        for (var index = 1; index < worldFootprint.Count - 1; index++)
        {
            if (reverseForUpwardTop)
            {
                triangleList.Add(worldFootprint.Count);
                triangleList.Add(worldFootprint.Count + index + 1);
                triangleList.Add(worldFootprint.Count + index);
                triangleList.Add(0);
                triangleList.Add(index);
                triangleList.Add(index + 1);
            }
            else
            {
                triangleList.Add(worldFootprint.Count);
                triangleList.Add(worldFootprint.Count + index);
                triangleList.Add(worldFootprint.Count + index + 1);
                triangleList.Add(0);
                triangleList.Add(index + 1);
                triangleList.Add(index);
            }
        }

        triangles = triangleList.ToArray();
    }

    private static Mesh CreatePrismMesh(
        string meshName,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> triangles)
    {
        var mesh = new Mesh
        {
            name = meshName,
            hideFlags = HideFlags.DontSave
        };
        mesh.vertices = vertices as Vector3[] ?? new List<Vector3>(vertices).ToArray();
        mesh.triangles = triangles as int[] ?? new List<int>(triangles).ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool HasGeneratedMeshGeometry(
        Mesh mesh,
        IReadOnlyList<Vector3> vertices,
        IReadOnlyList<int> triangles)
    {
        if (!IsGeneratedMesh(mesh)
            || mesh.vertexCount != vertices.Count)
        {
            return false;
        }

        var existingVertices = mesh.vertices;
        for (var index = 0; index < vertices.Count; index++)
        {
            if ((existingVertices[index] - vertices[index]).sqrMagnitude > 0.00000001f)
            {
                return false;
            }
        }

        var existingTriangles = mesh.triangles;
        if (existingTriangles.Length != triangles.Count)
        {
            return false;
        }

        for (var index = 0; index < triangles.Count; index++)
        {
            if (existingTriangles[index] != triangles[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void AddQuad(
        ICollection<int> triangles,
        int first,
        int second,
        int third,
        int fourth)
    {
        triangles.Add(first);
        triangles.Add(second);
        triangles.Add(third);
        triangles.Add(first);
        triangles.Add(third);
        triangles.Add(fourth);
    }

    private static bool TryGetWallSpan(
        IReadOnlyList<TestBuildingCreator.ExteriorWallSpan> spans,
        string wallId,
        out TestBuildingCreator.ExteriorWallSpan wall)
    {
        foreach (var candidate in spans)
        {
            if (candidate.StableId == wallId)
            {
                wall = candidate;
                return true;
            }
        }

        wall = default;
        return false;
    }

    private static string GetWallName(TestBuildingCreator.WallPlacement placement)
    {
        return $"Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})";
    }

    private static string GetWallSegmentName(int segmentIndex)
    {
        return $"Segment {segmentIndex}";
    }

    private static string GetDoorName(BuildingRecord.DoorPlacement door)
    {
        return $"Door {door.WallId} {door.NormalizedOffset:0.######}";
    }

    private static string GetEquipmentName(FactoryEntityRecord entity)
    {
        return $"Equipment {entity.EntityId}";
    }

    private static Transform GetOrCreateChild(Transform parent, string childName)
    {
        var existing = FindChild(parent, childName);
        if (existing is not null && existing)
        {
            existing.gameObject.SetActive(true);
            return existing;
        }

        var childObject = new GameObject(childName)
        {
            hideFlags = parent.gameObject.hideFlags
        };
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    private static Transform GetOrCreateCubeChild(Transform parent, string childName)
    {
        var existing = FindChild(parent, childName);
        var existingFilter = existing is not null && existing
            ? existing.GetComponent<MeshFilter>()
            : null;
        var existingRenderer = existing is not null && existing
            ? existing.GetComponent<MeshRenderer>()
            : null;
        if (existing is not null
            && existing
            && existingFilter is not null
            && existingFilter
            && existingRenderer is not null
            && existingRenderer)
        {
            existing.gameObject.SetActive(true);
            return existing;
        }

        if (existing is not null && existing)
        {
            DestroyGeneratedObject(existing.gameObject);
        }

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = childName;
        cube.hideFlags = parent.gameObject.hideFlags;
        cube.transform.SetParent(parent, false);
        RemoveCollider(cube);
        return cube.transform;
    }

    private static Transform GetOrCreateMeshChild(Transform parent, string childName)
    {
        var child = GetOrCreateChild(parent, childName);
        GetOrAddComponent<MeshFilter>(child.gameObject);
        GetOrAddComponent<MeshRenderer>(child.gameObject);
        RemoveCollider(child.gameObject);
        return child;
    }

    private static T GetOrAddComponent<T>(GameObject target)
        where T : Component
    {
        var component = target.GetComponent<T>();
        return component is not null && component
            ? component
            : target.AddComponent<T>();
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (child.name == childName)
            {
                return child;
            }
        }

        return null!;
    }

    private static int RemoveChildrenNotIn(
        Transform parent,
        HashSet<string> desiredNames)
    {
        var staleChildren = new List<GameObject>();
        var seenNames = new HashSet<string>();
        for (var index = 0; index < parent.childCount; index++)
        {
            var child = parent.GetChild(index);
            if (!desiredNames.Contains(child.name) || !seenNames.Add(child.name))
            {
                staleChildren.Add(child.gameObject);
            }
        }

        foreach (var staleChild in staleChildren)
        {
            DestroyGeneratedObject(staleChild);
        }

        return staleChildren.Count;
    }

    private static void ResetContainerTransform(Transform target)
    {
        target.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        target.localScale = Vector3.one;
        target.gameObject.SetActive(true);
    }

    private static void RemoveCollider(GameObject target)
    {
        var collider = target.GetComponent<Collider>();
        if (collider is not null && collider)
        {
            DestroyGeneratedObject(collider);
        }
    }

    private static void DestroyGeneratedMesh(Mesh mesh)
    {
        if (!IsGeneratedMesh(mesh))
        {
            return;
        }

        DestroyGeneratedObject(mesh);
    }

    private static bool IsGeneratedMesh(Mesh mesh)
    {
        return mesh is not null
            && mesh
            && mesh.name.StartsWith(GeneratedMeshPrefix, StringComparison.Ordinal);
    }

    private static void DestroyGeneratedMeshes(GameObject root)
    {
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.sharedMesh;
            if (!IsGeneratedMesh(mesh))
            {
                continue;
            }

            filter.sharedMesh = null;
            DestroyGeneratedMesh(mesh);
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object target)
    {
        if (target is null || !target)
        {
            return;
        }

        if (target is GameObject gameObject)
        {
            DestroyGeneratedMeshes(gameObject);
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

    private static int CompareRecords(BuildingRecord left, BuildingRecord right)
    {
        var comparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.AnchorCell.x.CompareTo(right.AnchorCell.x);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.AnchorCell.y.CompareTo(right.AnchorCell.y);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.FootprintSize.x.CompareTo(right.FootprintSize.x);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.FootprintSize.y.CompareTo(right.FootprintSize.y);
        if (comparison != 0)
        {
            return comparison;
        }

        return left.StoryCount.CompareTo(right.StoryCount);
    }

    private static int CompareSources(
        Factory3DOutsideTestProxyBuildingSource left,
        Factory3DOutsideTestProxyBuildingSource right)
    {
        return CompareRecords(left.Record, right.Record);
    }

    private static Factory3DOutsideTestProxyBuildingSource CreateShellSource(
        BuildingRecord record)
    {
        return new Factory3DOutsideTestProxyBuildingSource(
            record,
            false,
            BuildingFootprint.GetUsableInteriorSize(record.FootprintSize));
    }

    private static bool HasBuildingLevelDoorSemantics(int floorIndex)
    {
        // BuildingRecord.DoorPlacement has no floor field. This is the only
        // valid presentation floor until the authoritative model gains one.
        return floorIndex == 0;
    }

    private static int CompareFloors(
        OutsideTestFloorRecord left,
        OutsideTestFloorRecord right)
    {
        var comparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.FloorIndex.CompareTo(right.FloorIndex);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = string.CompareOrdinal(left.Label, right.Label);
        if (comparison != 0)
        {
            return comparison;
        }

        return left.Entities.Count.CompareTo(right.Entities.Count);
    }
}
