// Builds the runtime-sized insidefactory floor, walls, elevator, and door visuals.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

[DisallowMultipleComponent]
public sealed class InsideFactoryVisuals : MonoBehaviour
{
    private const string GeneratedRootName = "Generated Interior Visuals";
    private const float DefaultWallThickness = 0.5f;
    private const float DefaultDoorWidth = 0.8f;
    private const float DefaultElevatorWidth = 1.5f;
    private const float DefaultElevatorGap = 0.04f;

    [SerializeField] private TileBase floorTile = null!;
    [SerializeField] private Sprite doorSprite = null!;
    [SerializeField] private Material material = null!;
    [SerializeField] private Color floorColor = Color.white;
    [SerializeField] private Color wallColor = new(0.34f, 0.38f, 0.42f, 1f);
    [SerializeField] private Color wallOutlineColor = new(0.03f, 0.04f, 0.05f, 1f);
    [SerializeField] private Color elevatorColor = new(0.22f, 0.26f, 0.3f, 1f);
    [SerializeField] private Color elevatorGapColor = new(0.04f, 0.05f, 0.06f, 1f);
    [SerializeField, Min(0.01f)] private float wallThickness = DefaultWallThickness;
    [SerializeField, Min(0.01f)] private float doorWidth = DefaultDoorWidth;
    [SerializeField, Min(0.01f)] private float elevatorWidth = DefaultElevatorWidth;
    [SerializeField, Min(0.001f)] private float elevatorGap = DefaultElevatorGap;
    [SerializeField, Min(0.001f)] private float outlineWidth = 0.06f;
    [SerializeField] private int floorSortingOrder = -20;
    [SerializeField] private int wallSortingOrder = -10;
    [SerializeField] private int wallOutlineSortingOrder = -5;
    [SerializeField] private int elevatorSortingOrder = 0;
    [SerializeField] private int doorSortingOrder = 5;

    private Transform generatedRoot = null!;
    private SceneGrid grid = null!;

    public Transform GeneratedRoot => generatedRoot;
    public int DoorVisualCount { get; private set; }
    public bool HasElevatorVisual { get; private set; }

    public void Configure(
        Vector2Int size,
        IReadOnlyList<Vector2> doorPositions,
        IReadOnlyList<GridEdgeDirection> doorDirections)
    {
        grid = GetComponent<SceneGrid>();
        generatedRoot = GetGeneratedRoot();
        ClearGeneratedChildren();
        DoorVisualCount = 0;
        HasElevatorVisual = false;

        if (!BuildingFootprint.IsValid(size))
        {
            return;
        }

        CreateFloor(size);
        CreateWalls(size);
        CreateElevator(size);

        for (var index = 0; index < doorPositions.Count; index++)
        {
            var direction = index < doorDirections.Count
                ? doorDirections[index]
                : GridEdgeDirection.South;
            CreateDoorVisual(size, doorPositions[index], direction, index);
        }

        DoorVisualCount = doorPositions.Count;
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

    private void CreateFloor(Vector2Int size)
    {
        var floorObject = new GameObject("Industrial Floor");
        floorObject.transform.SetParent(generatedRoot, false);
        var tilemap = floorObject.AddComponent<Tilemap>();
        var renderer = floorObject.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = floorSortingOrder;
        renderer.sharedMaterial = material;

        for (var y = 0; y < size.y; y++)
        {
            for (var x = 0; x < size.x; x++)
            {
                tilemap.SetTile(new Vector3Int(x, y), floorTile);
                tilemap.SetColor(new Vector3Int(x, y), floorColor);
            }
        }
    }

    private void CreateWalls(Vector2Int size)
    {
        CreateQuad(
            "South Wall",
            new Vector2(0f, -wallThickness),
            new Vector2(size.x, 0f),
            wallColor,
            wallSortingOrder);
        CreateQuad(
            "East Wall",
            new Vector2(size.x, 0f),
            new Vector2(size.x + wallThickness, size.y),
            wallColor,
            wallSortingOrder);
        CreateQuad(
            "North Wall",
            new Vector2(0f, size.y),
            new Vector2(size.x, size.y + wallThickness),
            wallColor,
            wallSortingOrder);
        CreateQuad(
            "West Wall",
            new Vector2(-wallThickness, 0f),
            new Vector2(0f, size.y),
            wallColor,
            wallSortingOrder);

        var outlineObject = new GameObject("Wall Outline");
        outlineObject.transform.SetParent(generatedRoot, false);
        var line = outlineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 4;
        line.startWidth = outlineWidth;
        line.endWidth = outlineWidth;
        line.startColor = wallOutlineColor;
        line.endColor = wallOutlineColor;
        line.material = material;
        line.sortingOrder = wallOutlineSortingOrder;
        line.SetPositions(new[]
        {
            ToLocalPoint(new Vector2(-wallThickness, -wallThickness)),
            ToLocalPoint(new Vector2(size.x + wallThickness, -wallThickness)),
            ToLocalPoint(new Vector2(size.x + wallThickness, size.y + wallThickness)),
            ToLocalPoint(new Vector2(-wallThickness, size.y + wallThickness))
        });
    }

    private void CreateElevator(Vector2Int size)
    {
        var width = Mathf.Min(elevatorWidth, Mathf.Max(0.2f, size.x - wallThickness));
        var left = (size.x - width) * 0.5f;
        var right = left + width;
        var middle = (left + right) * 0.5f;
        var halfGap = elevatorGap * 0.5f;

        CreateQuad(
            "Elevator Left Door",
            new Vector2(left, size.y),
            new Vector2(middle - halfGap, size.y + wallThickness),
            elevatorColor,
            elevatorSortingOrder);
        CreateQuad(
            "Elevator Right Door",
            new Vector2(middle + halfGap, size.y),
            new Vector2(right, size.y + wallThickness),
            elevatorColor,
            elevatorSortingOrder);
        CreateQuad(
            "Elevator Center Gap",
            new Vector2(middle - halfGap, size.y),
            new Vector2(middle + halfGap, size.y + wallThickness),
            elevatorGapColor,
            elevatorSortingOrder + 1);

        HasElevatorVisual = true;
    }

    private void CreateDoorVisual(
        Vector2Int size,
        Vector2 interiorPosition,
        GridEdgeDirection direction,
        int index)
    {
        var wallPosition = direction switch
        {
            GridEdgeDirection.South => new Vector2(interiorPosition.x, 0f),
            GridEdgeDirection.East => new Vector2(size.x, interiorPosition.y),
            GridEdgeDirection.North => new Vector2(interiorPosition.x, size.y),
            GridEdgeDirection.West => new Vector2(0f, interiorPosition.y),
            _ => interiorPosition
        };

        var doorObject = new GameObject($"Interior Door {index + 1}");
        doorObject.transform.SetParent(generatedRoot, false);
        doorObject.transform.localPosition = ToLocalPoint(wallPosition);
        doorObject.transform.localRotation = Quaternion.Euler(
            0f,
            0f,
            GetDoorRotation(direction));

        var renderer = doorObject.AddComponent<SpriteRenderer>();
        renderer.sprite = doorSprite;
        renderer.color = Color.white;
        renderer.sortingOrder = doorSortingOrder;
        doorObject.transform.localScale = Vector3.one * (doorWidth / doorSprite.bounds.size.x);
    }

    private void CreateQuad(
        string objectName,
        Vector2 logicalMin,
        Vector2 logicalMax,
        Color color,
        int sortingOrder)
    {
        var quadObject = new GameObject(objectName);
        quadObject.transform.SetParent(generatedRoot, false);
        var first = ToLocalPoint(new Vector2(logicalMin.x, logicalMin.y));
        var second = ToLocalPoint(new Vector2(logicalMax.x, logicalMin.y));
        var third = ToLocalPoint(new Vector2(logicalMax.x, logicalMax.y));
        var fourth = ToLocalPoint(new Vector2(logicalMin.x, logicalMax.y));
        var mesh = new Mesh
        {
            name = objectName,
            vertices = new[] { first, second, third, fourth },
            triangles = new[] { 0, 1, 2, 0, 2, 3 },
            colors = new[] { color, color, color, color }
        };
        mesh.RecalculateBounds();

        var filter = quadObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        var renderer = quadObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = sortingOrder;
    }

    private Vector3 ToLocalPoint(Vector2 logicalPosition)
    {
        var worldPosition = grid.LogicalToWorld(logicalPosition);
        return transform.InverseTransformPoint(
            new Vector3(worldPosition.x, worldPosition.y, transform.position.z));
    }

    public static float GetDoorRotation(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.South => 0f,
            GridEdgeDirection.East => -90f,
            GridEdgeDirection.North => 180f,
            GridEdgeDirection.West => 90f,
            _ => 0f
        };
    }
}
