// Generates reusable grid-aligned floor, roof, wall, entrance, and collision modules.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class ModularBuildingView : MonoBehaviour
{
    private const string GeneratedRootName = "Modular Generated";

    [SerializeField] private Sprite floorSprite = null!;
    [SerializeField] private Sprite entranceSprite = null!;
    [SerializeField] private Sprite entranceOutlineSprite = null!;
    [SerializeField] private Material moduleMaterial = null!;
    [SerializeField] private Color floorColor = new(0.3f, 0.38f, 0.28f, 1f);
    [SerializeField] private Color roofColor = new(0.035f, 0.05f, 0.075f, 1f);
    [SerializeField] private Color wallColor = new(0.45f, 0.52f, 0.58f, 1f);
    [SerializeField] private Color wallSideColor = new(0.28f, 0.35f, 0.41f, 1f);
    [SerializeField] private Color outlineColor = new(0.015f, 0.02f, 0.025f, 1f);
    [SerializeField] private Color entranceColor = Color.white;
    [SerializeField, Min(0f)] private float wallHeight = 1.75f;
    [SerializeField, Min(0f)] private float roofHeight = 1.75f;
    [SerializeField, Range(0f, 0.5f)] private float roofLipHeight = 0.18f;
    [SerializeField, Min(0.005f)] private float outlineWidth = 0.045f;
    [SerializeField] private int floorSortingOrder;
    [SerializeField] private int wallSortingOrder = 10;
    [SerializeField] private int roofSortingOrder = 30;
    [SerializeField] private int outlineSortingOrder = 45;
    [SerializeField] private int entranceSortingOrder = 40;

    private readonly List<Vector3Int> footprintCells = new();

    public void Configure(
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground,
        Vector2Int entranceCellOffset,
        bool includeEntrance)
    {
        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        ConfigureInteriorCollider(anchorCell, size, ground);
        var generatedRoot = GetGeneratedRoot();
        ClearGeneratedChildren(generatedRoot);
        DisableAuthoredContent(generatedRoot);

        BuildingFootprint.GetCells(anchorCell, size, footprintCells);

        foreach (var cell in footprintCells)
        {
            var floorPosition = GetCellCenterWorld(ground, cell);
            CreateSpriteModule(
                generatedRoot,
                $"Floor {cell.x},{cell.y}",
                floorSprite,
                floorPosition,
                floorColor,
                floorSortingOrder);

            CreateRoofModule(
                generatedRoot,
                $"Roof {cell.x},{cell.y}",
                cell,
                ground,
                roofSortingOrder + cell.x + cell.y);
        }

        for (var x = 0; x < size.x; x++)
        {
            var bottomCell = anchorCell + new Vector3Int(x, 0);
            var topCell = anchorCell + new Vector3Int(x, size.y);
            CreateWallSegment(
                generatedRoot,
                $"Wall Bottom {bottomCell.x},{bottomCell.y}",
                GetCellCornerWorld(ground, bottomCell),
                GetCellCornerWorld(ground, bottomCell + new Vector3Int(1, 0)),
                wallColor,
                roofSortingOrder + bottomCell.x + bottomCell.y + 1);
            CreateWallSegment(
                generatedRoot,
                $"Wall Top {topCell.x},{topCell.y}",
                GetCellCornerWorld(ground, topCell),
                GetCellCornerWorld(ground, topCell + new Vector3Int(1, 0)),
                wallSideColor,
                wallSortingOrder + topCell.x + topCell.y);
        }

        for (var y = 0; y < size.y; y++)
        {
            var leftCell = anchorCell + new Vector3Int(0, y);
            var rightCell = anchorCell + new Vector3Int(size.x, y);
            CreateWallSegment(
                generatedRoot,
                $"Wall Left {leftCell.x},{leftCell.y}",
                GetCellCornerWorld(ground, leftCell),
                GetCellCornerWorld(ground, leftCell + new Vector3Int(0, 1)),
                wallSideColor,
                wallSortingOrder + leftCell.x + leftCell.y);
            CreateWallSegment(
                generatedRoot,
                $"Wall Right {rightCell.x},{rightCell.y}",
                GetCellCornerWorld(ground, rightCell),
                GetCellCornerWorld(ground, rightCell + new Vector3Int(0, 1)),
                wallColor,
                roofSortingOrder + rightCell.x + rightCell.y + 1);
        }

        if (includeEntrance)
        {
            var entranceCell = anchorCell + new Vector3Int(
                entranceCellOffset.x,
                entranceCellOffset.y);
            var entrancePosition = GetCellCenterWorld(ground, entranceCell);
            CreateSpriteModule(
                generatedRoot,
                "Entrance",
                entranceSprite,
                entrancePosition,
                entranceColor,
                entranceSortingOrder);
            CreateSpriteModule(
                generatedRoot,
                "Entrance Outline",
                entranceOutlineSprite,
                entrancePosition,
                entranceColor,
                entranceSortingOrder + 1);
        }

        CreateBoundaryCollider(generatedRoot, anchorCell, size, ground);
        CreatePerimeterOutline(generatedRoot, anchorCell, size, ground);

        if (TryGetComponent<BuildingOcclusionFader>(out var occlusionFader))
        {
            occlusionFader.enabled = true;
            occlusionFader.RefreshVisuals();
        }
    }

    private Transform GetGeneratedRoot()
    {
        var generatedRoot = transform.Find(GeneratedRootName);
        if (generatedRoot is not null && generatedRoot)
        {
            return generatedRoot;
        }

        var generatedObject = new GameObject(GeneratedRootName);
        generatedObject.transform.SetParent(transform, false);

        return generatedObject.transform;
    }

    private void ClearGeneratedChildren(Transform generatedRoot)
    {
        for (var index = generatedRoot.childCount - 1; index >= 0; index--)
        {
            var child = generatedRoot.GetChild(index).gameObject;

            if (!Application.isPlaying)
            {
                DestroyImmediate(child);
            }
            else
            {
                Destroy(child);
            }
        }
    }

    private void DisableAuthoredContent(Transform generatedRoot)
    {
        foreach (var renderer in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (renderer.transform.IsChildOf(generatedRoot))
            {
                continue;
            }

            renderer.enabled = false;
        }

        foreach (var collider in GetComponentsInChildren<Collider2D>(true))
        {
            if (collider.gameObject == gameObject
                || collider.transform.IsChildOf(generatedRoot))
            {
                continue;
            }

            collider.enabled = false;
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

    private void CreateBoundaryCollider(
        Transform generatedRoot,
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground)
    {
        var collisionObject = new GameObject("Boundary Collision");
        collisionObject.transform.SetParent(generatedRoot, false);

        var collider = collisionObject.AddComponent<EdgeCollider2D>();
        collider.edgeRadius = 0.06f;
        collider.points = GetBoundaryPoints(anchorCell, size, ground);
    }

    private void CreatePerimeterOutline(
        Transform generatedRoot,
        Vector3Int anchorCell,
        Vector2Int size,
        Tilemap ground)
    {
        var moduleObject = new GameObject("Wall Perimeter Outline");
        moduleObject.transform.SetParent(generatedRoot, false);

        var line = moduleObject.AddComponent<LineRenderer>();
        line.sharedMaterial = moduleMaterial;
        line.startColor = outlineColor;
        line.endColor = outlineColor;
        line.startWidth = outlineWidth;
        line.endWidth = outlineWidth;
        line.positionCount = 4;
        line.loop = true;
        line.useWorldSpace = false;
        line.sortingOrder = outlineSortingOrder;

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
                transform.InverseTransformPoint(corners[index]) + Vector3.up * roofHeight);
        }
    }

    private void CreateWallSegment(
        Transform generatedRoot,
        string moduleName,
        Vector3 startWorld,
        Vector3 endWorld,
        Color color,
        int sortingOrder)
    {
        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);

        var mesh = new Mesh
        {
            name = moduleName,
            vertices = GetWallVertices(startWorld, endWorld),
            triangles = new[]
            {
                0, 1, 2,
                0, 2, 3,
                4, 5, 6,
                4, 6, 7
            },
            colors = new[]
            {
                color,
                color,
                color,
                color,
                roofColor,
                roofColor,
                roofColor,
                roofColor
            }
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        var filter = moduleObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = moduleObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = moduleMaterial;
        renderer.sortingOrder = sortingOrder;
    }

    private void CreateRoofModule(
        Transform generatedRoot,
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
            vertices[index] = point + Vector3.up * roofHeight;
        }

        var mesh = new Mesh
        {
            name = moduleName,
            vertices = vertices,
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            colors = new[] { roofColor, roofColor, roofColor, roofColor }
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();

        var filter = moduleObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = moduleObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = moduleMaterial;
        renderer.sortingOrder = sortingOrder;
    }

    private void CreateSpriteModule(
        Transform generatedRoot,
        string moduleName,
        Sprite sprite,
        Vector3 worldPosition,
        Color color,
        int sortingOrder)
    {
        if (sprite is null || !sprite)
        {
            return;
        }

        var moduleObject = new GameObject(moduleName);
        moduleObject.transform.SetParent(generatedRoot, false);
        moduleObject.transform.localPosition = transform.InverseTransformPoint(worldPosition);

        var renderer = moduleObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = sortingOrder;
    }

    private Vector3[] GetWallVertices(Vector3 startWorld, Vector3 endWorld)
    {
        startWorld.z = transform.position.z;
        endWorld.z = transform.position.z;
        var start = transform.InverseTransformPoint(startWorld);
        var end = transform.InverseTransformPoint(endWorld);
        if (end.x < start.x)
        {
            (start, end) = (end, start);
        }

        var lipBottom = wallHeight - roofLipHeight;
        return new[]
        {
            start,
            end,
            end + Vector3.up * lipBottom,
            start + Vector3.up * lipBottom,
            start + Vector3.up * lipBottom,
            end + Vector3.up * lipBottom,
            end + Vector3.up * wallHeight,
            start + Vector3.up * wallHeight
        };
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
