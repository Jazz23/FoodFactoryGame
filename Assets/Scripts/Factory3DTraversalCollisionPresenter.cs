// Builds disposable 3D collision-only geometry from logical factory presentation data.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Factory3DTraversalCollisionPresenter : MonoBehaviour
{
    private const string CollisionRootName = "Factory 3D Traversal Collisions";
    private Transform collisionRoot = null!;

    public Transform CollisionRoot => collisionRoot;

    public void Reconcile(
        Factory3DRouteSnapshot snapshot,
        SceneGrid grid,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        float storyHeight = 3f,
        bool leaveSouthOpening = false)
    {
        EnsureRoot();
        Clear();
        if (snapshot is null
            || grid is null
            || !grid
            || buildingInstanceId == 0
            || floorIndex < 0
            || !BuildingFootprint.IsValid(interiorSize))
        {
            return;
        }

        var floor = FindFloor(snapshot, buildingInstanceId, floorIndex);
        if (!floor.HasValue)
        {
            return;
        }

        var adapter = grid.CreateSpatialAdapter();
        var elevation = BuildingCoordinates.GetFloorElevation(floorIndex, Mathf.Max(0f, storyHeight));
        var cellSize = Mathf.Max(0.01f, grid.CellSize);
        var wallHeight = Mathf.Max(0.1f, Mathf.Max(0f, storyHeight) * 0.9f);
        var wallThickness = cellSize * 0.08f;
        CreateBox(
            "Floor",
            GetCenter(adapter, buildingInstanceId, floorIndex, interiorSize, elevation - wallThickness * 0.5f),
            GetSize(adapter, buildingInstanceId, floorIndex, interiorSize, wallThickness, wallThickness));
        if (!leaveSouthOpening)
        {
            CreateWall(adapter, buildingInstanceId, floorIndex, "South", Vector2.zero, new Vector2(interiorSize.x, 0f), elevation, wallThickness, wallHeight);
        }
        CreateWall(adapter, buildingInstanceId, floorIndex, "East", new Vector2(interiorSize.x, 0f), new Vector2(interiorSize.x, interiorSize.y), elevation, wallThickness, wallHeight);
        CreateWall(adapter, buildingInstanceId, floorIndex, "North", new Vector2(interiorSize.x, interiorSize.y), new Vector2(0f, interiorSize.y), elevation, wallThickness, wallHeight);
        CreateWall(adapter, buildingInstanceId, floorIndex, "West", new Vector2(0f, interiorSize.y), Vector2.zero, elevation, wallThickness, wallHeight);

        foreach (var entity in floor.Value.Entities)
        {
            var position = adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(buildingInstanceId, floorIndex, entity.LogicalPosition),
                elevation);
            var size = GetEntitySize(entity, cellSize);
            CreateBox(
                $"Entity {entity.EntityId}",
                position + Vector3.up * (size.y * 0.5f),
                size);
        }

        collisionRoot.gameObject.SetActive(true);
    }

    public void Clear()
    {
        if (collisionRoot is null || !collisionRoot)
        {
            return;
        }

        collisionRoot.gameObject.SetActive(false);

        var children = new List<GameObject>();
        for (var index = 0; index < collisionRoot.childCount; index++)
        {
            children.Add(collisionRoot.GetChild(index).gameObject);
        }

        foreach (var child in children)
        {
            child.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
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

    private void CreateWall(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        string name,
        Vector2 startLogical,
        Vector2 endLogical,
        float elevation,
        float thickness,
        float height)
    {
        var start = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, floorIndex, startLogical), elevation);
        var end = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(buildingInstanceId, floorIndex, endLogical), elevation);
        var direction = end - start;
        direction.y = 0f;
        var length = new Vector2(direction.x, direction.z).magnitude;
        var wall = CreateBox(
            name,
            (start + end) * 0.5f + Vector3.up * (height * 0.5f),
            new Vector3(thickness, height, Mathf.Max(thickness, length)));
        wall.transform.rotation = direction.sqrMagnitude <= 0.000001f
            ? Quaternion.identity
            : Quaternion.LookRotation(direction, Vector3.up);
    }

    private GameObject CreateBox(string name, Vector3 position, Vector3 size)
    {
        var result = new GameObject(name);
        result.transform.SetParent(collisionRoot, false);
        result.transform.SetPositionAndRotation(position, Quaternion.identity);
        var collider = result.AddComponent<BoxCollider>();
        collider.size = size;
        return result;
    }

    private static Vector3 GetCenter(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        float elevation)
    {
        var corners = GetCorners(adapter, buildingInstanceId, floorIndex, interiorSize, elevation);
        return (corners[0] + corners[2]) * 0.5f;
    }

    private static Vector3 GetSize(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        float height,
        float thickness)
    {
        var corners = GetCorners(adapter, buildingInstanceId, floorIndex, interiorSize, 0f);
        return new Vector3(
            Mathf.Max(thickness, Mathf.Abs(corners[2].x - corners[0].x)),
            height,
            Mathf.Max(thickness, Mathf.Abs(corners[2].z - corners[0].z)));
    }

    private static Vector3[] GetCorners(
        FactorySpatialAdapter adapter,
        uint buildingInstanceId,
        int floorIndex,
        Vector2Int interiorSize,
        float elevation)
    {
        return new[]
        {
            adapter.LogicalToWorld3D(new FactoryLogicalLocation(buildingInstanceId, floorIndex, Vector2.zero), elevation),
            adapter.LogicalToWorld3D(new FactoryLogicalLocation(buildingInstanceId, floorIndex, new Vector2(interiorSize.x, 0f)), elevation),
            adapter.LogicalToWorld3D(new FactoryLogicalLocation(buildingInstanceId, floorIndex, interiorSize), elevation),
            adapter.LogicalToWorld3D(new FactoryLogicalLocation(buildingInstanceId, floorIndex, new Vector2(0f, interiorSize.y)), elevation)
        };
    }

    private static Vector3 GetEntitySize(Factory3DRouteEntitySnapshot entity, float cellSize)
    {
        var height = entity.RouteRole switch
        {
            Factory3DRouteRole.Belt => cellSize * 0.2f,
            Factory3DRouteRole.Elevator => cellSize * 0.8f,
            _ => cellSize * 0.9f
        };
        return new Vector3(cellSize * 0.8f, height, cellSize * 0.8f);
    }

    private static Factory3DRouteFloorSnapshot? FindFloor(
        Factory3DRouteSnapshot snapshot,
        uint buildingInstanceId,
        int floorIndex)
    {
        foreach (var floor in snapshot.Floors)
        {
            if (floor.BuildingInstanceId == buildingInstanceId && floor.FloorIndex == floorIndex)
            {
                return floor;
            }
        }

        return null;
    }
}
