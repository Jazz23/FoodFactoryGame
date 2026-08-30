// Generates area-building floors, roofs, directional perimeter walls, entrances, and collision.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ModularBuildingView : MonoBehaviour
{
    private const string GeneratedRootName = "Modular Generated";
    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

    [SerializeField] private BuildingVisualStyle style = null!;

    private readonly List<Vector3Int> footprintCells = new();
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

        BuildingFootprint.GetCells(anchorCell, size, footprintCells);
        foreach (var cell in footprintCells)
        {
            var floorPosition = GetCellCenterWorld(ground, cell);
            CreateSpriteModule(
                $"Floor {cell.x},{cell.y}",
                style.FloorSprite,
                floorPosition,
                style.FloorColor,
                style.FloorSortingOrder);
            CreateRoofModule(
                $"Roof {cell.x},{cell.y}",
                cell,
                ground,
                style.RoofSortingOrder + cell.x + cell.y);
        }

        for (var x = 0; x < size.x; x++)
        {
            var southCell = anchorCell + new Vector3Int(x, 0);
            var northCell = anchorCell + new Vector3Int(x, size.y - 1);
            CreateWallSegment(southCell, GridEdgeDirection.South, ground, mode);
            CreateWallSegment(northCell, GridEdgeDirection.North, ground, mode);
        }

        for (var y = 0; y < size.y; y++)
        {
            var westCell = anchorCell + new Vector3Int(0, y);
            var eastCell = anchorCell + new Vector3Int(size.x - 1, y);
            CreateWallSegment(westCell, GridEdgeDirection.West, ground, mode);
            CreateWallSegment(eastCell, GridEdgeDirection.East, ground, mode);
        }

        if (includeEntrance)
        {
            var entranceCell = anchorCell + new Vector3Int(
                entranceCellOffset.x,
                entranceCellOffset.y);
            var entrancePosition = GetCellCenterWorld(ground, entranceCell);
            CreateSpriteModule(
                "Entrance",
                style.EntranceSprite,
                entrancePosition,
                style.EntranceColor,
                style.EntranceSortingOrder);
            CreateSpriteModule(
                "Entrance Outline",
                style.EntranceOutlineSprite,
                entrancePosition,
                style.EntranceColor,
                style.EntranceSortingOrder + 1);
        }

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

    private void CreateWallSegment(
        Vector3Int cell,
        GridEdgeDirection direction,
        Tilemap ground,
        BuildingVisualMode mode)
    {
        var edge = GridEdge.FromCellSide(cell, direction);
        var moduleObject = new GameObject(
            $"Wall {direction} {edge.Corner.x},{edge.Corner.y}");
        moduleObject.transform.SetParent(generatedRoot, false);
        var renderer = moduleObject.AddComponent<DirectionalWallSegmentRenderer>();
        var origin = GetCellCornerWorld(ground, edge.Corner);
        var thicknessWorld = edge.Axis == GridEdgeAxis.Horizontal
            ? GetCellCornerWorld(ground, edge.Corner + Vector3Int.up) - origin
            : GetCellCornerWorld(ground, edge.Corner + Vector3Int.right) - origin;
        thicknessWorld *= WallCellGeometry.ThicknessInCells;
        renderer.Configure(
            direction,
            edge,
            origin,
            GetCellCornerWorld(ground, edge.EndCorner),
            thicknessWorld,
            style,
            mode == BuildingVisualMode.Runtime);
    }

    private void CreateRoofModule(
        string moduleName,
        Vector3Int cell,
        Tilemap ground,
        int sortingOrder)
    {
        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);
        var corners = new[]
        {
            GetCellCornerWorld(ground, cell),
            GetCellCornerWorld(ground, cell + new Vector3Int(1, 0)),
            GetCellCornerWorld(ground, cell + new Vector3Int(1, 1)),
            GetCellCornerWorld(ground, cell + new Vector3Int(0, 1))
        };
        var vertices = new Vector3[corners.Length];
        for (var index = 0; index < corners.Length; index++)
        {
            var point = transform.InverseTransformPoint(corners[index]);
            vertices[index] = point + Vector3.up * style.RoofHeight;
        }

        var mesh = new Mesh
        {
            name = moduleName,
            vertices = vertices,
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            colors = new[] { style.RoofColor, style.RoofColor, style.RoofColor, style.RoofColor }
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
        int sortingOrder)
    {
        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);
        moduleObject.transform.localPosition = transform.InverseTransformPoint(worldPosition);
        var renderer = moduleObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
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

    private Vector3 GetCellCenterWorld(Tilemap ground, Vector3Int cell)
    {
        var position = ground.GetCellCenterWorld(cell);
        position.z = transform.position.z;
        return position;
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
