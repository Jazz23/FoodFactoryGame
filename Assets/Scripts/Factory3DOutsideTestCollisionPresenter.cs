// Builds disposable 3D traversal colliders from authoritative OutsideTest topology and floor records.
using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Factory3DOutsideTestCollisionPresenter : MonoBehaviour
{
    private const string CollisionRootName = "Factory 3D OutsideTest Traversal Collisions";
    private const float FloorThickness = 0.1f;
    private const float DoorOpeningWidth = 0.8f;
    private const float DefaultWallHeight = 3f;

    private readonly List<Factory3DOutsideTestProxyBuildingSource> buildingSources = new();
    private readonly List<OutsideTestFloorRecord> floorRecords = new();
    private readonly List<TestBuildingCreator.WallPlacement> wallPlacements = new();
    private readonly List<TestBuildingCreator.ExteriorWallSpan> wallSpans = new();
    private readonly List<Factory3DOutsideTestProxyBuildingSource> validSources = new();
    private readonly Dictionary<OutsideTestFloorKey, OutsideTestFloorRecord> floorsByKey = new();
    private Transform collisionRoot = null!;
    private int lastFingerprint;
    private bool hasFingerprint;

    public Transform CollisionRoot => collisionRoot;
    public int ColliderCount => collisionRoot is null || !collisionRoot
        ? 0
        : collisionRoot.GetComponentsInChildren<Collider>(true).Length;

    public void Reconcile(
        SceneGrid grid,
        FactoryWorldState state,
        float wallHeight = DefaultWallHeight,
        float doorCornerExclusionDistance = TestBuildingCreator.DefaultDoorCornerExclusionDistance)
    {
        if (state is null)
        {
            Reconcile(
                grid,
                Array.Empty<Factory3DOutsideTestProxyBuildingSource>(),
                Array.Empty<OutsideTestFloorRecord>(),
                wallHeight,
                doorCornerExclusionDistance);
            return;
        }

        buildingSources.Clear();
        foreach (var record in state.BuildingRecords)
        {
            if (record is null
                || !state.TryGetInteriorSemantics(
                    record.BuildingInstanceId,
                    out var isInteriorOnly,
                    out var interiorSize))
            {
                continue;
            }

            buildingSources.Add(new Factory3DOutsideTestProxyBuildingSource(
                record,
                isInteriorOnly,
                interiorSize));
        }

        Reconcile(
            grid,
            buildingSources,
            state.FloorStates,
            wallHeight,
            doorCornerExclusionDistance);
    }

    public void Reconcile(
        SceneGrid grid,
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> sources,
        IEnumerable<OutsideTestFloorRecord> floors,
        float wallHeight = DefaultWallHeight,
        float doorCornerExclusionDistance = TestBuildingCreator.DefaultDoorCornerExclusionDistance)
    {
        EnsureRoot();
        if (grid is null || !grid)
        {
            Clear();
            return;
        }

        CollectValidSources(sources, doorCornerExclusionDistance);
        CollectFloors(floors);
        var effectiveWallHeight = float.IsFinite(wallHeight)
            ? Mathf.Max(0.1f, wallHeight)
            : DefaultWallHeight;
        var fingerprint = ComputeFingerprint(
            grid,
            effectiveWallHeight,
            doorCornerExclusionDistance);
        if (hasFingerprint && fingerprint == lastFingerprint)
        {
            return;
        }

        ClearChildren();
        var adapter = grid.CreateSpatialAdapter();
        CreateWorldFloor(grid, adapter);
        foreach (var source in validSources)
        {
            CreateBuildingColliders(
                source,
                adapter,
                grid,
                effectiveWallHeight);
        }

        collisionRoot.gameObject.SetActive(true);
        lastFingerprint = fingerprint;
        hasFingerprint = true;
    }

    public void Clear()
    {
        EnsureRoot();
        ClearChildren();
        collisionRoot.gameObject.SetActive(false);
        lastFingerprint = 0;
        hasFingerprint = false;
    }

    private void CollectValidSources(
        IEnumerable<Factory3DOutsideTestProxyBuildingSource> sources,
        float doorCornerExclusionDistance)
    {
        validSources.Clear();
        var candidates = new List<Factory3DOutsideTestProxyBuildingSource>();
        if (sources is not null)
        {
            foreach (var source in sources)
            {
                if (source.Record is not null)
                {
                    candidates.Add(source);
                }
            }
        }

        candidates.Sort((left, right) => left.Record.BuildingInstanceId.CompareTo(
            right.Record.BuildingInstanceId));
        var records = new List<BuildingRecord>();
        foreach (var source in candidates)
        {
            var record = source.Record;
            if (record is null
                || record.BuildingInstanceId == 0
                || !BuildingShellValidation.TryValidate(
                    record,
                    records,
                    doorCornerExclusionDistance,
                    out _))
            {
                continue;
            }

            validSources.Add(source);
            records.Add(record);
        }
    }

    private void CollectFloors(IEnumerable<OutsideTestFloorRecord> floors)
    {
        floorRecords.Clear();
        floorsByKey.Clear();
        if (floors is null)
        {
            return;
        }

        foreach (var floor in floors)
        {
            if (floor is not null)
            {
                floorRecords.Add(floor);
            }
        }

        floorRecords.Sort((left, right) =>
        {
            var buildingComparison = left.BuildingInstanceId.CompareTo(right.BuildingInstanceId);
            return buildingComparison != 0
                ? buildingComparison
                : left.FloorIndex.CompareTo(right.FloorIndex);
        });
        foreach (var floor in floorRecords)
        {
            floorsByKey.TryAdd(
                new OutsideTestFloorKey(floor.BuildingInstanceId, floor.FloorIndex),
                floor);
        }
    }

    private void CreateWorldFloor(SceneGrid grid, FactorySpatialAdapter adapter)
    {
        var hasBounds = grid.HasLogicalBounds;
        var minimum = hasBounds
            ? new Vector2(grid.LogicalBounds.min.x, grid.LogicalBounds.min.y)
            : new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = hasBounds
            ? new Vector2(grid.LogicalBounds.max.x, grid.LogicalBounds.max.y)
            : new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        if (!hasBounds)
        {
            foreach (var source in validSources)
            {
                var record = source.Record;
                minimum = Vector2.Min(minimum, new Vector2(record.AnchorCell.x, record.AnchorCell.y));
                maximum = Vector2.Max(
                    maximum,
                    new Vector2(
                        record.AnchorCell.x + record.FootprintSize.x,
                        record.AnchorCell.y + record.FootprintSize.y));
            }

            if (!float.IsFinite(minimum.x) || !float.IsFinite(minimum.y))
            {
                return;
            }

            minimum -= Vector2.one * 2f;
            maximum += Vector2.one * 2f;
        }

        CreateFloor(
            "World Floor",
            adapter,
            0u,
            0,
            minimum,
            maximum,
            0f);
    }

    private void CreateBuildingColliders(
        Factory3DOutsideTestProxyBuildingSource source,
        FactorySpatialAdapter adapter,
        SceneGrid grid,
        float wallHeight)
    {
        var record = source.Record;
        var secondCorner = record.AnchorCell + new Vector3Int(
            record.FootprintSize.x - 1,
            record.FootprintSize.y - 1);
        TestBuildingCreator.GetWallPlacements(
            record.AnchorCell,
            secondCorner,
            wallPlacements);
        TestBuildingCreator.GetExteriorWallSpans(
            record.AnchorCell,
            record.FootprintSize,
            wallSpans);
        for (var floorIndex = 0; floorIndex < record.StoryCount; floorIndex++)
        {
            var elevation = BuildingCoordinates.GetFloorElevation(floorIndex, wallHeight);
            CreateFloor(
                $"Building {record.BuildingInstanceId} Floor {floorIndex}",
                adapter,
                record.BuildingInstanceId,
                floorIndex,
                new Vector2(record.AnchorCell.x, record.AnchorCell.y),
                new Vector2(
                    record.AnchorCell.x + record.FootprintSize.x,
                    record.AnchorCell.y + record.FootprintSize.y),
                elevation);

            foreach (var placement in wallPlacements)
            {
                var segments = GridWall.GetLogicalPlaneSegments(
                    placement.Kind,
                    placement.Cell);
                for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    var segment = segments[segmentIndex];
                    var span = FindSpan(placement, segmentIndex);
                    var openings = floorIndex == 0
                        ? GetDoorOpenings(record, span)
                        : new List<Vector2>();
                    CreateWallPieces(
                        adapter,
                        record.BuildingInstanceId,
                        floorIndex,
                        segment,
                        openings,
                        elevation,
                        wallHeight,
                        grid.CellSize,
                        $"Wall {placement.Cell.x},{placement.Cell.y} {segmentIndex}");
                }
            }

            if (floorsByKey.TryGetValue(
                    new OutsideTestFloorKey(record.BuildingInstanceId, floorIndex),
                    out var floor))
            {
                CreateEquipmentColliders(
                    source,
                    floor,
                    adapter,
                    floorIndex,
                    elevation,
                    grid.CellSize);
            }
        }
    }

    private void CreateEquipmentColliders(
        Factory3DOutsideTestProxyBuildingSource source,
        OutsideTestFloorRecord floor,
        FactorySpatialAdapter adapter,
        int floorIndex,
        float elevation,
        float cellSize)
    {
        foreach (var entity in floor.Entities)
        {
            if (entity is null || entity.EntityId == 0)
            {
                continue;
            }

            var localPosition = source.IsInteriorOnly
                ? entity.LogicalPosition
                : BuildingCoordinates.InteriorLocalToExteriorLocal(entity.LogicalPosition);
            var logicalPosition = BuildingCoordinates.LocalToExteriorLogical(
                source.Record.AnchorCell,
                localPosition);
            var worldPosition = adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(
                    source.Record.BuildingInstanceId,
                    floorIndex,
                    logicalPosition),
                elevation);
            var height = entity.IsConveyor || entity.IsElevator
                ? cellSize * 0.25f
                : cellSize * 0.75f;
            CreateBox(
                $"Equipment {entity.EntityId}",
                worldPosition + Vector3.up * (height * 0.5f),
                Quaternion.identity,
                new Vector3(cellSize * 0.8f, height, cellSize * 0.8f));
        }
    }

    private void CreateWallPieces(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        GridWall.PlaneSegment segment,
        List<Vector2> openings,
        float elevation,
        float wallHeight,
        float cellSize,
        string name)
    {
        var direction = segment.End - segment.Start;
        var length = direction.magnitude;
        if (length <= 0.0001f)
        {
            return;
        }

        var intervals = new List<Vector2>();
        foreach (var opening in openings)
        {
            var halfWidth = Mathf.Min(
                DoorOpeningWidth * 0.5f,
                Mathf.Max(0.01f, length * 0.5f - 0.01f));
            var center = Mathf.Clamp01(opening.x);
            var halfParameter = halfWidth / length;
            intervals.Add(new Vector2(
                Mathf.Max(0f, center - halfParameter),
                Mathf.Min(1f, center + halfParameter)));
        }

        intervals.Sort((left, right) => left.x.CompareTo(right.x));
        var cursor = 0f;
        var pieceIndex = 0;
        foreach (var interval in intervals)
        {
            if (interval.x > cursor)
            {
                CreateWallPiece(
                    adapter,
                    buildingInstanceId,
                    floorIndex,
                    segment,
                    cursor,
                    interval.x,
                    elevation,
                    wallHeight,
                    cellSize,
                    $"{name} {pieceIndex++}");
            }

            cursor = Mathf.Max(cursor, interval.y);
        }

        if (cursor < 1f)
        {
            CreateWallPiece(
                adapter,
                buildingInstanceId,
                floorIndex,
                segment,
                cursor,
                1f,
                elevation,
                wallHeight,
                cellSize,
                $"{name} {pieceIndex}");
        }
    }

    private void CreateWallPiece(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        GridWall.PlaneSegment segment,
        float startParameter,
        float endParameter,
        float elevation,
        float wallHeight,
        float cellSize,
        string name)
    {
        var startLogical = Vector2.Lerp(segment.Start, segment.End, startParameter);
        var endLogical = Vector2.Lerp(segment.Start, segment.End, endParameter);
        var start = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, floorIndex, startLogical),
            elevation);
        var end = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, floorIndex, endLogical),
            elevation);
        var direction = end - start;
        direction.y = 0f;
        var length = new Vector2(direction.x, direction.z).magnitude;
        if (length <= 0.0001f)
        {
            return;
        }

        CreateBox(
            name,
            (start + end) * 0.5f + Vector3.up * (wallHeight * 0.5f),
            Quaternion.LookRotation(direction, Vector3.up),
            new Vector3(cellSize * 0.08f, wallHeight, length));
    }

    private void CreateFloor(
        string name,
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2 minimum,
        Vector2 maximum,
        float elevation)
    {
        var corners = new[]
        {
            adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(buildingInstanceId, floorIndex, minimum),
                elevation),
            adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(
                    buildingInstanceId,
                    floorIndex,
                    new Vector2(maximum.x, minimum.y)),
                elevation),
            adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(buildingInstanceId, floorIndex, maximum),
                elevation),
            adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(
                    buildingInstanceId,
                    floorIndex,
                    new Vector2(minimum.x, maximum.y)),
                elevation)
        };
        CreatePrism(
            name,
            corners,
            elevation - FloorThickness,
            elevation);
    }

    private GameObject CreateBox(
        string name,
        Vector3 position,
        Quaternion rotation,
        Vector3 size)
    {
        var result = new GameObject(name);
        result.transform.SetParent(collisionRoot, false);
        result.transform.SetPositionAndRotation(position, rotation);
        result.AddComponent<BoxCollider>().size = size;
        return result;
    }

    private GameObject CreatePrism(
        string name,
        IReadOnlyList<Vector3> corners,
        float bottom,
        float top)
    {
        var result = new GameObject(name);
        result.transform.SetParent(collisionRoot, false);
        var mesh = new Mesh { name = $"{name} Collision Mesh", hideFlags = HideFlags.DontSave };
        var vertices = new Vector3[8];
        for (var index = 0; index < corners.Count; index++)
        {
            var bottomCorner = corners[index];
            bottomCorner.y = bottom;
            var topCorner = corners[index];
            topCorner.y = top;
            vertices[index] = result.transform.InverseTransformPoint(bottomCorner);
            vertices[index + 4] = result.transform.InverseTransformPoint(topCorner);
        }

        mesh.vertices = vertices;
        mesh.triangles = new[]
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        result.AddComponent<MeshCollider>().sharedMesh = mesh;
        return result;
    }

    private List<Vector2> GetDoorOpenings(
        BuildingRecord record,
        TestBuildingCreator.ExteriorWallSpan span)
    {
        var result = new List<Vector2>();
        if (span.IsCorner)
        {
            return result;
        }

        foreach (var door in record.Doors)
        {
            if (door is not null && door.WallId == span.StableId)
            {
                result.Add(new Vector2(door.NormalizedOffset, 0f));
            }
        }

        return result;
    }

    private TestBuildingCreator.ExteriorWallSpan FindSpan(
        TestBuildingCreator.WallPlacement placement,
        int segmentIndex)
    {
        foreach (var span in wallSpans)
        {
            if (span.Kind == placement.Kind
                && span.Cell == placement.Cell
                && span.SegmentIndex == segmentIndex)
            {
                return span;
            }
        }

        return default;
    }

    private int ComputeFingerprint(
        SceneGrid grid,
        float wallHeight,
        float doorCornerExclusionDistance)
    {
        var hash = new HashCode();
        hash.Add(grid.GetEntityId());
        hash.Add(grid.CellSize);
        hash.Add(wallHeight);
        hash.Add(doorCornerExclusionDistance);
        foreach (var source in validSources)
        {
            var record = source.Record;
            hash.Add(record.BuildingInstanceId);
            hash.Add(record.AnchorCell);
            hash.Add(record.FootprintSize);
            hash.Add(record.StoryCount);
            hash.Add(source.IsInteriorOnly);
            hash.Add(source.UsableInteriorSize);
            foreach (var door in record.Doors)
            {
                hash.Add(door?.WallId);
                hash.Add(door?.NormalizedOffset ?? 0f);
            }
        }

        foreach (var floor in floorRecords)
        {
            hash.Add(floor.BuildingInstanceId);
            hash.Add(floor.FloorIndex);
            foreach (var entity in floor.Entities)
            {
                hash.Add(entity?.EntityId ?? 0u);
                hash.Add(entity?.DefinitionId);
                hash.Add(entity?.LogicalPosition ?? Vector2.zero);
            }
        }

        return hash.ToHashCode();
    }

    private void EnsureRoot()
    {
        if (collisionRoot is not null && collisionRoot)
        {
            return;
        }

        var root = new GameObject(CollisionRootName);
        root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        root.transform.SetParent(transform, false);
        collisionRoot = root.transform;
    }

    private void ClearChildren()
    {
        if (collisionRoot is null || !collisionRoot)
        {
            return;
        }

        var children = new List<GameObject>();
        for (var index = 0; index < collisionRoot.childCount; index++)
        {
            children.Add(collisionRoot.GetChild(index).gameObject);
        }

        foreach (var child in children)
        {
            var meshCollider = child.GetComponent<MeshCollider>();
            if (meshCollider is not null && meshCollider)
            {
                var mesh = meshCollider.sharedMesh;
                meshCollider.sharedMesh = null;
                if (mesh is not null)
                {
                    DestroyGeneratedObject(mesh);
                }
            }

            DestroyGeneratedObject(child);
        }
    }

    private static void DestroyGeneratedObject(UnityEngine.Object target)
    {
        if (target is null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}
