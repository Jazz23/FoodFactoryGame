// Places definition-driven rectangular buildings and replicates their instance records.
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public sealed class Builder : NetworkBehaviour
{
    [SerializeField] private Tilemap ground = null!;
    [SerializeField] private TileBase buildableTile = null!;
    [SerializeField] private BuildingCatalog catalog = null!;
    [SerializeField] private string defaultBuildingId = string.Empty;
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.55f;
    [SerializeField] private Color invalidGhostColor = new(1f, 0.35f, 0.35f, 0.65f);
    [SerializeField, Min(0.05f)] private float demolitionEdgeSelectionDistance = 0.35f;

    private readonly SyncDictionary<uint, BuildingInstance> buildings = new();
    private readonly Dictionary<uint, BuildingView> placedBuildings = new();
    private readonly Dictionary<uint, PreplacedBuilding> preplacedBuildingsById = new();
    private readonly BuildingOccupancy occupancy = new();
    private readonly List<Vector3Int> hoveredFootprint = new();
    private readonly List<GridEdge> hoveredEdges = new();
    private readonly List<Vector3Int> rebuildFootprint = new();
    private readonly List<GridEdge> rebuildEdges = new();
    private readonly List<GridEdge> demolitionEdges = new();

    private InputAction point = null!;
    private InputAction place = null!;
    private InputAction demolish = null!;
    private InputAction nextBuilding = null!;
    private InputAction previousBuilding = null!;
    private InputAction rotate = null!;
    private Camera sceneCamera = null!;
    private TilemapCollider2D groundCollider = null!;
    private BuildingDefinition selectedDefinition = null!;
    private GameObject ghostBuilding = null!;
    private BuildingVisualView ghostVisual = null!;
    private Vector3Int hoveredCell;
    private Vector3 hoveredWorldPosition;
    private Vector3Int configuredGhostCell;
    private GridEdgeDirection configuredGhostDirection;
    private GridEdgeDirection selectedDirection;
    private bool hasHoveredCell;
    private bool canPlaceHoveredBuilding;
    private bool ghostConfigured;
    private int selectedDefinitionIndex;
    private uint nextBuildingId = 1;

    public Tilemap Ground => ground;
    public TileBase BuildableTile => buildableTile;
    public BuildingCatalog Catalog => catalog;

    private void Awake()
    {
        point = InputSystem.actions.FindAction("Build/Point", true);
        place = InputSystem.actions.FindAction("Build/Place", true);
        demolish = InputSystem.actions.FindAction("Build/Demolish", true);
        nextBuilding = InputSystem.actions.FindAction("Build/NextBuilding", true);
        previousBuilding = InputSystem.actions.FindAction("Build/PreviousBuilding", true);
        rotate = InputSystem.actions.FindAction("Build/Rotate", true);
        sceneCamera = Camera.main!;
        groundCollider = ground.GetComponent<TilemapCollider2D>()!;
        CachePreplacedBuildings();
        buildings.OnChange += BuildingsOnChange;
    }

    private void OnDestroy()
    {
        buildings.OnChange -= BuildingsOnChange;
    }

    public override void OnStartServer()
    {
        RebuildOccupancy();
        RegisterPreplacedBuildings();
    }

    public override void OnStopServer()
    {
        ClearPlacedBuildings();
        occupancy.Clear();
    }

    public override void OnStartClient()
    {
        sceneCamera = Camera.main!;
        point.Enable();
        place.Enable();
        demolish.Enable();
        nextBuilding.Enable();
        previousBuilding.Enable();
        rotate.Enable();
        place.performed += PlacePerformed;
        demolish.performed += DemolishPerformed;
        nextBuilding.performed += NextBuildingPerformed;
        previousBuilding.performed += PreviousBuildingPerformed;
        rotate.performed += RotatePerformed;

        SelectBuilding(defaultBuildingId);
        RebuildOccupancy();
        RefreshPlacedBuildings();
    }

    public override void OnStopClient()
    {
        place.performed -= PlacePerformed;
        demolish.performed -= DemolishPerformed;
        nextBuilding.performed -= NextBuildingPerformed;
        previousBuilding.performed -= PreviousBuildingPerformed;
        rotate.performed -= RotatePerformed;
        place.Disable();
        demolish.Disable();
        nextBuilding.Disable();
        previousBuilding.Disable();
        rotate.Disable();
        point.Disable();
        hasHoveredCell = false;
        canPlaceHoveredBuilding = false;

        if (ghostBuilding is not null)
        {
            Destroy(ghostBuilding);
            ghostBuilding = null!;
        }

        ClearPlacedBuildings();
    }

    private void Update()
    {
        if (!IsClientInitialized || selectedDefinition is null)
        {
            return;
        }

        UpdateHoveredCell();
        UpdateGhostBuilding();
    }

    public bool SelectBuilding(string definitionId)
    {
        if (!catalog.TryGetDefinition(definitionId, out var definition))
        {
            return false;
        }

        selectedDefinition = definition;
        selectedDefinitionIndex = catalog.GetIndex(definitionId);
        selectedDirection = GridEdgeDirection.South;
        RecreateGhostBuilding();
        return true;
    }

    private void NextBuildingPerformed(InputAction.CallbackContext _)
    {
        if (catalog.Count == 0)
        {
            return;
        }

        selectedDefinitionIndex = (selectedDefinitionIndex + 1) % catalog.Count;
        SelectBuilding(catalog.GetDefinition(selectedDefinitionIndex).Id);
    }

    private void PreviousBuildingPerformed(InputAction.CallbackContext _)
    {
        if (catalog.Count == 0)
        {
            return;
        }

        selectedDefinitionIndex = (selectedDefinitionIndex - 1 + catalog.Count) % catalog.Count;
        SelectBuilding(catalog.GetDefinition(selectedDefinitionIndex).Id);
    }

    private void RotatePerformed(InputAction.CallbackContext _)
    {
        if (selectedDefinition.PlacementKind != BuildingPlacementKind.WallSegment)
        {
            return;
        }

        selectedDirection = GridEdge.RotateClockwise(selectedDirection);
        ghostConfigured = false;
        if (hasHoveredCell)
        {
            RefreshHoveredPlacement();
        }
    }

    private void PlacePerformed(InputAction.CallbackContext _)
    {
        if (!hasHoveredCell || !canPlaceHoveredBuilding)
        {
            return;
        }

        PlaceBuildingServerRpc(selectedDefinition.Id, hoveredCell, selectedDirection);
    }

    private void DemolishPerformed(InputAction.CallbackContext _)
    {
        if (!hasHoveredCell || !TryGetDemolitionBuildingId(out var buildingId))
        {
            return;
        }

        DemolishBuildingServerRpc(buildingId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaceBuildingServerRpc(
        string definitionId,
        Vector3Int anchorCell,
        GridEdgeDirection direction,
        NetworkConnection sender = null)
    {
        TryPlaceBuilding(definitionId, anchorCell, direction, sender.ClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void DemolishBuildingServerRpc(uint buildingId, NetworkConnection sender = null)
    {
        if (!buildings.TryGetValue(buildingId, out var building)
            || building.OwnerClientId != sender.ClientId)
        {
            return;
        }

        occupancy.Release(buildingId);
        buildings.Remove(buildingId);
    }

    private void UpdateHoveredCell()
    {
        var screenPosition = point.ReadValue<Vector2>();
        var ray = sceneCamera.ScreenPointToRay(screenPosition);
        var hits = Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity);

        hasHoveredCell = false;
        canPlaceHoveredBuilding = false;

        foreach (var hit in hits)
        {
            if (hit.collider != groundCollider)
            {
                continue;
            }

            var cell = ground.WorldToCell(hit.point);
            hoveredCell = cell;
            hoveredWorldPosition = hit.point;
            hasHoveredCell = true;
            RefreshHoveredPlacement();
            return;
        }
    }

    private void RefreshHoveredPlacement()
    {
        var candidate = new BuildingInstance(
            uint.MaxValue,
            selectedDefinition.Id,
            hoveredCell,
            selectedDefinition.FootprintSize,
            -1,
            selectedDirection);
        BuildingPlacementRules.GetReservation(
            candidate,
            selectedDefinition,
            hoveredFootprint,
            hoveredEdges);
        canPlaceHoveredBuilding = BuildingPlacementRules.IsBuildable(
                candidate,
                selectedDefinition,
                ground,
                buildableTile,
                hoveredFootprint)
            && occupancy.CanReserve(uint.MaxValue, hoveredFootprint, hoveredEdges);
    }

    private bool TryGetDemolitionBuildingId(out uint buildingId)
    {
        GridEdge.GetCellEdges(hoveredCell, demolitionEdges);
        var closestDistance = float.PositiveInfinity;
        var closestBuildingId = 0u;

        foreach (var edge in demolitionEdges)
        {
            if (!occupancy.TryGetBuildingId(edge, out var edgeBuildingId))
            {
                continue;
            }

            var start = ground.CellToWorld(edge.Corner);
            var end = ground.CellToWorld(edge.EndCorner);
            var distance = DistanceToSegment(hoveredWorldPosition, start, end);
            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            closestBuildingId = edgeBuildingId;
        }

        if (closestBuildingId != 0
            && closestDistance <= demolitionEdgeSelectionDistance)
        {
            buildingId = closestBuildingId;
            return true;
        }

        return occupancy.TryGetBuildingId(hoveredCell, out buildingId);
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        var delta = end - start;
        var lengthSquared = delta.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            return Vector2.Distance(point, start);
        }

        var interpolation = Mathf.Clamp01(Vector2.Dot(point - start, delta) / lengthSquared);
        return Vector2.Distance(point, start + delta * interpolation);
    }

    private void TryPlaceBuilding(
        string definitionId,
        Vector3Int anchorCell,
        GridEdgeDirection direction,
        int ownerClientId)
    {
        if (!catalog.TryGetDefinition(definitionId, out var definition))
        {
            return;
        }

        if (definition.PlacementKind == BuildingPlacementKind.WallSegment
            && !System.Enum.IsDefined(typeof(GridEdgeDirection), direction))
        {
            return;
        }

        if (definition.PlacementKind != BuildingPlacementKind.WallSegment)
        {
            direction = GridEdgeDirection.South;
        }

        var building = new BuildingInstance(
            nextBuildingId,
            definition.Id,
            anchorCell,
            definition.FootprintSize,
            ownerClientId,
            direction);
        BuildingPlacementRules.GetReservation(
            building,
            definition,
            hoveredFootprint,
            hoveredEdges);
        if (!BuildingPlacementRules.IsBuildable(
                building,
                definition,
                ground,
                buildableTile,
                hoveredFootprint)
            || !occupancy.TryReserve(building.Id, hoveredFootprint, hoveredEdges))
        {
            return;
        }

        buildings.Add(building.Id, building);
        nextBuildingId++;
    }

    private void UpdateGhostBuilding()
    {
        if (ghostBuilding is null)
        {
            return;
        }

        if (!hasHoveredCell)
        {
            ghostBuilding.SetActive(false);
            return;
        }

        if (!ghostConfigured
            || configuredGhostCell != hoveredCell
            || configuredGhostDirection != selectedDirection)
        {
            var instance = new BuildingInstance(
                uint.MaxValue,
                selectedDefinition.Id,
                hoveredCell,
                selectedDefinition.FootprintSize,
                -1,
                selectedDirection);
            ghostVisual.Configure(
                instance,
                selectedDefinition,
                ground,
                BuildingVisualMode.Preview);
            configuredGhostCell = hoveredCell;
            configuredGhostDirection = selectedDirection;
            ghostConfigured = true;
        }

        ghostBuilding.SetActive(true);
        ghostVisual.SetPresentation(canPlaceHoveredBuilding
            ? new Color(1f, 1f, 1f, ghostAlpha)
            : invalidGhostColor,
            1);
    }

    private void RecreateGhostBuilding()
    {
        if (ghostBuilding is not null)
        {
            Destroy(ghostBuilding);
            ghostBuilding = null!;
        }

        ghostBuilding = Instantiate(selectedDefinition.PreviewPrefab, transform);
        ghostBuilding.name = $"{selectedDefinition.Prefab.name} Ghost";
        ghostVisual = ghostBuilding.GetComponent<BuildingVisualView>();
        ghostConfigured = false;
        ghostBuilding.SetActive(false);
    }

    private void BuildingsOnChange(
        SyncDictionaryOperation operation,
        uint buildingId,
        BuildingInstance building,
        bool _)
    {
        RebuildOccupancy();

        switch (operation)
        {
            case SyncDictionaryOperation.Add:
            case SyncDictionaryOperation.Set:
                CreatePlacedBuilding(building);
                break;
            case SyncDictionaryOperation.Remove:
                RemovePlacedBuilding(buildingId);
                break;
            case SyncDictionaryOperation.Clear:
                ClearPlacedBuildings();
                break;
        }
    }

    private void RebuildOccupancy()
    {
        occupancy.Clear();
        nextBuildingId = 1;

        foreach (var pair in buildings.Collection)
        {
            var building = pair.Value;
            if (!catalog.TryGetDefinition(building.DefinitionId, out var definition))
            {
                continue;
            }

            BuildingPlacementRules.GetReservation(
                building,
                definition,
                rebuildFootprint,
                rebuildEdges);
            if (!occupancy.TryReserve(building.Id, rebuildFootprint, rebuildEdges))
            {
                Debug.LogError($"Building '{building.Id}' overlaps another building.", this);
            }

            if (building.Id >= nextBuildingId)
            {
                nextBuildingId = building.Id + 1;
            }
        }
    }

    private void RegisterPreplacedBuildings()
    {
        foreach (var preplacedBuilding in preplacedBuildingsById.Values)
        {
            if (buildings.ContainsKey(preplacedBuilding.InstanceId))
            {
                continue;
            }

            var building = new BuildingInstance(
                preplacedBuilding.InstanceId,
                preplacedBuilding.Definition.Id,
                preplacedBuilding.AnchorCell,
                preplacedBuilding.Size,
                -1,
                preplacedBuilding.Direction);
            BuildingPlacementRules.GetReservation(
                building,
                preplacedBuilding.Definition,
                rebuildFootprint,
                rebuildEdges);
            if (!occupancy.TryReserve(
                    preplacedBuilding.InstanceId,
                    rebuildFootprint,
                    rebuildEdges))
            {
                Debug.LogError(
                    $"Preplaced building '{preplacedBuilding.name}' overlaps another building.",
                    preplacedBuilding);
                continue;
            }

            buildings.Add(building.Id, building);
            if (building.Id >= nextBuildingId)
            {
                nextBuildingId = building.Id + 1;
            }
            preplacedBuilding.Configure(building, ground);
        }
    }

    private void CachePreplacedBuildings()
    {
        var preplacedBuildings = FindObjectsByType<PreplacedBuilding>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        foreach (var preplacedBuilding in preplacedBuildings)
        {
            if (preplacedBuilding.gameObject.scene != ground.gameObject.scene)
            {
                continue;
            }

            preplacedBuildingsById[preplacedBuilding.InstanceId] = preplacedBuilding;
        }
    }

    private void RefreshPlacedBuildings()
    {
        ClearPlacedBuildings();

        foreach (var pair in buildings.Collection)
        {
            CreatePlacedBuilding(pair.Value);
        }
    }

    private void CreatePlacedBuilding(BuildingInstance building)
    {
        if (!catalog.TryGetDefinition(building.DefinitionId, out var definition))
        {
            Debug.LogError($"Building definition '{building.DefinitionId}' is missing.", this);
            return;
        }

        if (preplacedBuildingsById.TryGetValue(building.Id, out var preplacedBuilding))
        {
            preplacedBuilding.Configure(building, ground);
            placedBuildings[building.Id] = preplacedBuilding.View;
            return;
        }

        if (placedBuildings.TryGetValue(building.Id, out var existingBuilding)
            && existingBuilding is not null
            && existingBuilding)
        {
            existingBuilding.Configure(building, definition, ground);
            return;
        }

        var buildingObject = Instantiate(definition.Prefab, transform);

        buildingObject.name = $"{definition.Prefab.name} ({building.AnchorCell.x}, {building.AnchorCell.y})";
        var buildingView = buildingObject.GetComponent<BuildingView>();
        buildingView.Configure(building, definition, ground);
        placedBuildings.Add(building.Id, buildingView);
    }

    private void RemovePlacedBuilding(uint buildingId)
    {
        if (!placedBuildings.TryGetValue(buildingId, out var building))
        {
            return;
        }

        placedBuildings.Remove(buildingId);
        if (building is null || !building || building.TryGetComponent<PreplacedBuilding>(out _))
        {
            return;
        }

        Destroy(building.gameObject);
    }

    private void ClearPlacedBuildings()
    {
        foreach (var building in placedBuildings.Values)
        {
            if (building is null || !building || building.TryGetComponent<PreplacedBuilding>(out _))
            {
                continue;
            }

            Destroy(building.gameObject);
        }

        placedBuildings.Clear();
    }
}
