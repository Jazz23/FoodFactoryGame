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

    private readonly SyncDictionary<uint, BuildingInstance> buildings = new();
    private readonly Dictionary<uint, BuildingView> placedBuildings = new();
    private readonly Dictionary<uint, PreplacedBuilding> preplacedBuildingsById = new();
    private readonly BuildingOccupancy occupancy = new();
    private readonly List<Vector3Int> hoveredFootprint = new();
    private readonly List<Vector3Int> rebuildFootprint = new();

    private InputAction point = null!;
    private InputAction place = null!;
    private InputAction demolish = null!;
    private InputAction nextBuilding = null!;
    private InputAction previousBuilding = null!;
    private Camera sceneCamera = null!;
    private TilemapCollider2D groundCollider = null!;
    private BuildingDefinition selectedDefinition = null!;
    private GameObject ghostBuilding = null!;
    private SpriteRenderer[] ghostRenderers = System.Array.Empty<SpriteRenderer>();
    private Vector3Int hoveredCell;
    private bool hasHoveredCell;
    private bool canPlaceHoveredBuilding;
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
        place.performed += PlacePerformed;
        demolish.performed += DemolishPerformed;
        nextBuilding.performed += NextBuildingPerformed;
        previousBuilding.performed += PreviousBuildingPerformed;

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
        place.Disable();
        demolish.Disable();
        nextBuilding.Disable();
        previousBuilding.Disable();
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

    private void PlacePerformed(InputAction.CallbackContext _)
    {
        if (!hasHoveredCell || !canPlaceHoveredBuilding)
        {
            return;
        }

        PlaceBuildingServerRpc(selectedDefinition.Id, hoveredCell);
    }

    private void DemolishPerformed(InputAction.CallbackContext _)
    {
        if (!hasHoveredCell || !occupancy.TryGetBuildingId(hoveredCell, out var buildingId))
        {
            return;
        }

        DemolishBuildingServerRpc(buildingId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaceBuildingServerRpc(string definitionId, Vector3Int anchorCell, NetworkConnection sender = null)
    {
        TryPlaceBuilding(definitionId, anchorCell, sender.ClientId);
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
            hasHoveredCell = true;
            BuildingFootprint.GetCells(cell, selectedDefinition.FootprintSize, hoveredFootprint);
            canPlaceHoveredBuilding = IsBuildableFootprint(hoveredFootprint)
                && occupancy.CanReserve(uint.MaxValue, hoveredFootprint);
            return;
        }
    }

    private bool IsBuildableFootprint(IReadOnlyList<Vector3Int> footprint)
    {
        if (footprint.Count == 0)
        {
            return false;
        }

        foreach (var cell in footprint)
        {
            if (ground.GetTile(cell) != buildableTile)
            {
                return false;
            }
        }

        return true;
    }

    private void TryPlaceBuilding(string definitionId, Vector3Int anchorCell, int ownerClientId)
    {
        if (!catalog.TryGetDefinition(definitionId, out var definition))
        {
            return;
        }

        BuildingFootprint.GetCells(anchorCell, definition.FootprintSize, hoveredFootprint);
        if (!IsBuildableFootprint(hoveredFootprint))
        {
            return;
        }

        var building = new BuildingInstance(
            nextBuildingId,
            definition.Id,
            anchorCell,
            definition.FootprintSize,
            ownerClientId);
        if (!occupancy.TryReserve(building.Id, hoveredFootprint))
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

        var visualAnchorCell = BuildingFootprint.GetVisualAnchorCell(
            hoveredCell,
            selectedDefinition.VisualAnchorCellOffset);
        var worldPosition = ground.CellToWorld(visualAnchorCell);
        worldPosition.z = 0f;
        ghostBuilding.transform.position = worldPosition;
        ghostBuilding.SetActive(true);
        SetGhostColor(canPlaceHoveredBuilding
            ? new Color(1f, 1f, 1f, ghostAlpha)
            : invalidGhostColor);
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
        ghostRenderers = ghostBuilding.GetComponentsInChildren<SpriteRenderer>();

        foreach (var renderer in ghostRenderers)
        {
            renderer.sortingOrder++;
        }

        ghostBuilding.SetActive(false);
    }

    private void SetGhostColor(Color color)
    {
        foreach (var renderer in ghostRenderers)
        {
            renderer.color = color;
        }
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

            var size = BuildingFootprint.GetEffectiveSize(
                building.Size,
                definition.FootprintSize);
            BuildingFootprint.GetCells(building.AnchorCell, size, rebuildFootprint);
            if (!occupancy.TryReserve(building.Id, rebuildFootprint))
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

            BuildingFootprint.GetCells(
                preplacedBuilding.AnchorCell,
                preplacedBuilding.Size,
                rebuildFootprint);
            if (!occupancy.TryReserve(preplacedBuilding.InstanceId, rebuildFootprint))
            {
                Debug.LogError(
                    $"Preplaced building '{preplacedBuilding.name}' overlaps another building.",
                    preplacedBuilding);
                continue;
            }

            var building = new BuildingInstance(
                preplacedBuilding.InstanceId,
                preplacedBuilding.Definition.Id,
                preplacedBuilding.AnchorCell,
                preplacedBuilding.Size,
                -1);
            buildings.Add(building.Id, building);
            if (building.Id >= nextBuildingId)
            {
                nextBuildingId = building.Id + 1;
            }
            preplacedBuilding.Configure(ground);
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
            preplacedBuilding.Configure(ground);
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
