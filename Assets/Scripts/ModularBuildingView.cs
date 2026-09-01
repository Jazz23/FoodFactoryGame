// Generates area-building floors, roofs, directional perimeter walls, entrances, and collision.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

public sealed class ModularBuildingView : MonoBehaviour
{
    private const string GeneratedRootName = "Modular Generated";
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    [SerializeField] private BuildingVisualStyle style = null!;

    private readonly List<Renderer> generatedRenderers = new();
    private readonly Dictionary<Renderer, int> baseSortingOrders = new();
    private readonly Dictionary<SpriteRenderer, Color> baseSpriteColors = new();
    private readonly Dictionary<LineRenderer, Color> baseLineStartColors = new();
    private readonly Dictionary<LineRenderer, Color> baseLineEndColors = new();

    private Transform generatedRoot = null!;
    private MaterialPropertyBlock propertyBlock = null!;

    public BuildingVisualStyle Style => style;
    public Transform GeneratedRoot => generatedRoot;
    public IReadOnlyList<Renderer> GeneratedRenderers => generatedRenderers;

    public void Configure(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground,
        Vector2Int entranceCellOffset,
        bool includeEntrance,
        BuildingVisualMode mode = BuildingVisualMode.Runtime)
    {
        generatedRoot = GetGeneratedRoot();
        ClearGeneratedChildren();
        DisableAuthoredContent();

        if (!BuildingFootprint.IsValid(size))
        {
            SetRuntimeCollidersEnabled(false);
            return;
        }

        SetRuntimeCollidersEnabled(mode == BuildingVisualMode.Runtime);
        if (mode == BuildingVisualMode.Runtime)
        {
            ConfigureInteriorCollider(anchorCell, size, ground);
        }

        ConfigureSorting(anchorCell, size);
        CreateSurfaceModule(
            "Floor",
            GetSurfaceVertices(anchorCell, size, ground, 0f, 0f),
            style.FloorColor,
            style.FloorSortingOrder);
        CreateSurfaceModule(
            "Roof",
            GetSurfaceVertices(
                anchorCell,
                size,
                ground,
                WallCellGeometry.ThicknessInCells * 0.5f,
                style.RoofHeight),
            style.RoofColor,
            style.RoofSortingOrder);

        var entranceEdge = includeEntrance
            ? BuildingFootprint.GetSouthEntranceEdge(anchorCell, size, entranceCellOffset)
            : default;
        CreatePerimeterWalls(anchorCell, size, ground, entranceEdge, includeEntrance, mode);

        if (includeEntrance)
        {
            var entrancePosition = BuildingFootprint.GetEdgeCenterWorld(entranceEdge, ground);
            entrancePosition.z = transform.position.z;
            CreateSpriteModule(
                "Entrance",
                style.EntranceSprite,
                entrancePosition,
                style.EntranceColor,
                style.EntranceSortingOrder,
                style.EntranceHeight);
            CreateSpriteModule(
                "Entrance Outline",
                style.EntranceOutlineSprite,
                entrancePosition,
                style.EntranceColor,
                style.EntranceSortingOrder + 1,
                style.EntranceHeight);
        }

        CreateRoofAccent(anchorCell, size, ground);
        CreatePerimeterOutline(anchorCell, size, ground);
        CacheGeneratedVisuals();
        SetPresentation(Color.white, 0);

        if (TryGetComponent<BuildingOcclusionFader>(out var occlusionFader))
        {
            occlusionFader.enabled = mode == BuildingVisualMode.Runtime;
            if (mode == BuildingVisualMode.Runtime)
            {
                occlusionFader.RefreshVisuals();
            }
        }
    }

    public void SetPresentation(Color colorMultiplier, int sortingOrderOffset)
    {
        propertyBlock ??= new MaterialPropertyBlock();

        foreach (var pair in baseSortingOrders)
        {
            pair.Key.sortingOrder = pair.Value + sortingOrderOffset;
        }

        foreach (var pair in baseSpriteColors)
        {
            pair.Key.color = pair.Value * colorMultiplier;
        }

        foreach (var pair in baseLineStartColors)
        {
            pair.Key.startColor = pair.Value * colorMultiplier;
            pair.Key.endColor = baseLineEndColors[pair.Key] * colorMultiplier;
        }

        foreach (var renderer in generatedRoot.GetComponentsInChildren<MeshRenderer>(true))
        {
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorPropertyId, colorMultiplier);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private Transform GetGeneratedRoot()
    {
        var existingRoot = transform.Find(GeneratedRootName);
        if (existingRoot is not null && existingRoot)
        {
            return existingRoot;
        }

        var generatedObject = new GameObject(GeneratedRootName);
        generatedObject.transform.SetParent(transform, false);
        return generatedObject.transform;
    }

    private void ClearGeneratedChildren()
    {
        generatedRenderers.Clear();
        baseSortingOrders.Clear();
        baseSpriteColors.Clear();
        baseLineStartColors.Clear();
        baseLineEndColors.Clear();

        for (var index = generatedRoot.childCount - 1; index >= 0; index--)
        {
            var child = generatedRoot.GetChild(index).gameObject;
            child.SetActive(false);
            if (Application.isPlaying)
            {
                child.transform.SetParent(null, false);
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private void DisableAuthoredContent()
    {
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!renderer.transform.IsChildOf(generatedRoot))
            {
                renderer.enabled = false;
            }
        }

        foreach (var collider in GetComponentsInChildren<Collider2D>(true))
        {
            if (collider.gameObject != gameObject
                && !collider.transform.IsChildOf(generatedRoot))
            {
                collider.enabled = false;
            }
        }
    }

    private void SetRuntimeCollidersEnabled(bool enabled)
    {
        if (TryGetComponent<PolygonCollider2D>(out var collider))
        {
            collider.enabled = enabled;
        }
    }

    private void ConfigureInteriorCollider(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground)
    {
        if (!TryGetComponent<PolygonCollider2D>(out var collider))
        {
            collider = gameObject.AddComponent<PolygonCollider2D>();
        }

        collider.isTrigger = true;
        collider.pathCount = 1;
        collider.SetPath(0, GetBoundaryPoints(anchorCell, size, ground));
        collider.enabled = true;
    }

    private void CreatePerimeterOutline(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground)
    {
        var moduleObject = new GameObject("Wall Perimeter Outline");
        moduleObject.transform.SetParent(generatedRoot, false);
        var line = moduleObject.AddComponent<LineRenderer>();
        line.sharedMaterial = style.ModuleMaterial;
        line.startColor = style.OutlineColor;
        line.endColor = style.OutlineColor;
        line.startWidth = style.OutlineWidth;
        line.endWidth = style.OutlineWidth;
        line.positionCount = 4;
        line.loop = true;
        line.useWorldSpace = false;
        line.sortingOrder = style.OutlineSortingOrder;

        var corners = new[]
        {
            GetCellCornerWorld(ground, anchorCell),
            GetCellCornerWorld(ground, anchorCell + new Vector3Int(size.x, 0)),
            GetCellCornerWorld(ground, anchorCell + new Vector3Int(size.x, size.y)),
            GetCellCornerWorld(ground, anchorCell + new Vector3Int(0, size.y))
        };

        for (var index = 0; index < corners.Length; index++)
        {
            line.SetPosition(
                index,
                transform.InverseTransformPoint(corners[index]) + Vector3.up * style.RoofHeight);
        }
    }

    private void ConfigureSorting(Vector3Int anchorCell, Vector2Int size)
    {
        if (!generatedRoot.TryGetComponent<SortingGroup>(out var sortingGroup))
        {
            sortingGroup = generatedRoot.gameObject.AddComponent<SortingGroup>();
        }

        sortingGroup.sortingOrder = style.GetBuildingSortingOrder(anchorCell, size);
    }

    private void CreatePerimeterWalls(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground,
        GridEdge entranceEdge,
        bool includeEntrance,
        BuildingVisualMode mode)
    {
        var southWest = anchorCell;
        var southEast = anchorCell + new Vector3Int(size.x, 0);
        var northWest = anchorCell + new Vector3Int(0, size.y);
        var northEast = anchorCell + new Vector3Int(size.x, size.y);

        if (includeEntrance)
        {
            if (entranceEdge.Corner != southWest)
            {
                CreateWallRun(
                    GridEdgeDirection.South,
                    southWest,
                    entranceEdge.Corner,
                    ground,
                    true,
                    true,
                    mode);
            }

            if (entranceEdge.EndCorner != southEast)
            {
                CreateWallRun(
                    GridEdgeDirection.South,
                    entranceEdge.EndCorner,
                    southEast,
                    ground,
                    true,
                    true,
                    mode);
            }
        }
        else
        {
            CreateWallRun(
                GridEdgeDirection.South,
                southWest,
                southEast,
                ground,
                true,
                true,
                mode);
        }

        CreateWallRun(
            GridEdgeDirection.North,
            northWest,
            northEast,
            ground,
            true,
            true,
            mode);
        CreateWallRun(
            GridEdgeDirection.West,
            southWest,
            northWest,
            ground,
            false,
            false,
            mode);
        CreateWallRun(
            GridEdgeDirection.East,
            southEast,
            northEast,
            ground,
            false,
            false,
            mode);
    }

    private void CreateWallRun(
        GridEdgeDirection direction,
        Vector3Int startCorner,
        Vector3Int endCorner,
        Tilemap ground,
        bool includeStartCap,
        bool includeEndCap,
        BuildingVisualMode mode)
    {
        var axis = direction is GridEdgeDirection.South or GridEdgeDirection.North
            ? GridEdgeAxis.Horizontal
            : GridEdgeAxis.Vertical;
        var edge = new GridEdge(startCorner, axis);
        var moduleObject = new GameObject(
            $"Wall {direction} {startCorner.x},{startCorner.y}-{endCorner.x},{endCorner.y}");
        moduleObject.transform.SetParent(generatedRoot, false);
        var renderer = moduleObject.AddComponent<DirectionalWallSegmentRenderer>();
        var origin = GetCellCornerWorld(ground, startCorner);
        var thicknessWorld = edge.Axis == GridEdgeAxis.Horizontal
            ? GetCellCornerWorld(ground, startCorner + Vector3Int.up) - origin
            : GetCellCornerWorld(ground, startCorner + Vector3Int.right) - origin;
        thicknessWorld *= WallCellGeometry.ThicknessInCells;
        renderer.Configure(
            direction,
            edge,
            origin,
            GetCellCornerWorld(ground, endCorner),
            thicknessWorld,
            includeStartCap,
            includeEndCap,
            style,
            mode == BuildingVisualMode.Runtime);
    }

    private void CreateSurfaceModule(
        string moduleName,
        Vector3[] vertices,
        Color color,
        int sortingOrder)
    {
        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);
        var mesh = new Mesh
        {
            name = moduleName,
            vertices = vertices,
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            colors = new[] { color, color, color, color }
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        var filter = moduleObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var owner = moduleObject.AddComponent<GeneratedMeshOwner>();
        owner.SetMesh(mesh);
        var renderer = moduleObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = style.ModuleMaterial;
        renderer.sortingOrder = sortingOrder;
    }

    private void CreateSpriteModule(
        string moduleName,
        Sprite sprite,
        Vector3 worldPosition,
        Color color,
        int sortingOrder,
        float targetHeight)
    {
        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);
        moduleObject.transform.localPosition = transform.InverseTransformPoint(worldPosition);
        var renderer = moduleObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
        moduleObject.transform.localScale = Vector3.one * (targetHeight / GetVisibleSpriteHeight(sprite));
    }

    private static float GetVisibleSpriteHeight(Sprite sprite)
    {
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var vertex in sprite.vertices)
        {
            minimum = Mathf.Min(minimum, vertex.y);
            maximum = Mathf.Max(maximum, vertex.y);
        }

        return maximum - minimum;
    }

    private void CreateRoofAccent(Vector3Int anchorCell, Vector2Int size, Tilemap ground)
    {
        const float accentInset = 0.45f;
        var vertices = GetSurfaceVertices(
            anchorCell,
            size,
            ground,
            accentInset,
            style.RoofHeight);
        var moduleObject = new GameObject("Roof Accent");
        moduleObject.transform.SetParent(generatedRoot, false);
        var line = moduleObject.AddComponent<LineRenderer>();
        line.sharedMaterial = style.ModuleMaterial;
        line.startColor = style.RoofAccentColor;
        line.endColor = style.RoofAccentColor;
        line.startWidth = style.OutlineWidth * 0.75f;
        line.endWidth = style.OutlineWidth * 0.75f;
        line.positionCount = vertices.Length;
        line.loop = true;
        line.useWorldSpace = false;
        line.sortingOrder = style.RoofSortingOrder + 1;
        line.SetPositions(vertices);
    }

    private Vector3[] GetSurfaceVertices(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground,
        float inset,
        float height)
    {
        var origin = GetCellCornerWorld(ground, anchorCell);
        var right = GetCellCornerWorld(ground, anchorCell + Vector3Int.right) - origin;
        var up = GetCellCornerWorld(ground, anchorCell + Vector3Int.up) - origin;
        var corners = new[]
        {
            origin + right * inset + up * inset,
            origin + right * (size.x - inset) + up * inset,
            origin + right * (size.x - inset) + up * (size.y - inset),
            origin + right * inset + up * (size.y - inset)
        };
        var vertices = new Vector3[corners.Length];
        for (var index = 0; index < corners.Length; index++)
        {
            vertices[index] = transform.InverseTransformPoint(corners[index]) + Vector3.up * height;
        }

        return vertices;
    }

    private void CacheGeneratedVisuals()
    {
        foreach (var renderer in generatedRoot.GetComponentsInChildren<Renderer>(true))
        {
            generatedRenderers.Add(renderer);
            baseSortingOrders[renderer] = renderer.sortingOrder;

            if (renderer is SpriteRenderer spriteRenderer)
            {
                baseSpriteColors[spriteRenderer] = spriteRenderer.color;
            }

            if (renderer is LineRenderer lineRenderer)
            {
                baseLineStartColors[lineRenderer] = lineRenderer.startColor;
                baseLineEndColors[lineRenderer] = lineRenderer.endColor;
            }
        }
    }

    private Vector3 GetCellCornerWorld(Tilemap ground, Vector3Int cell)
    {
        var position = ground.CellToWorld(cell);
        position.z = transform.position.z;
        return position;
    }

    private Vector2[] GetBoundaryPoints(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground)
    {
        var points = new Vector2[5];
        points[0] = GetLocalPoint(ground.CellToWorld(anchorCell));
        points[1] = GetLocalPoint(ground.CellToWorld(
            anchorCell + new Vector3Int(size.x, 0)));
        points[2] = GetLocalPoint(ground.CellToWorld(
            anchorCell + new Vector3Int(size.x, size.y)));
        points[3] = GetLocalPoint(ground.CellToWorld(
            anchorCell + new Vector3Int(0, size.y)));
        points[4] = points[0];
        return points;
    }

    private Vector2 GetLocalPoint(Vector3 worldPoint)
    {
        worldPoint.z = transform.position.z;
        var localPoint = transform.InverseTransformPoint(worldPoint);
        return new Vector2(localPoint.x, localPoint.y);
    }
}
