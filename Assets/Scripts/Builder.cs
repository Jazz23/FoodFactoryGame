using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

public class Builder : NetworkBehaviour
{
    [SerializeField] private Tilemap ground;
    [SerializeField] private TileBase grass;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField, Range(0f, 1f)] private float ghostAlpha = 0.55f;
    [SerializeField] private Color invalidGhostColor = new(1f, 0.35f, 0.35f, 0.65f);

    private readonly SyncHashSet<Vector3Int> _occupiedCells = new();
    private readonly Dictionary<Vector3Int, GameObject> _placedWalls = new();

    private InputAction _point = null!;
    private InputAction _click = null!;
    private Camera _sceneCamera = null!;
    private TilemapCollider2D _groundCollider = null!;
    private GameObject _ghostWall = null!;
    private SpriteRenderer _ghostRenderer = null!;
    private Vector3Int _hoveredCell;
    private bool _hasHoveredCell;
    private bool _canPlaceHoveredCell;

    private void Awake()
    {
        _point = InputSystem.actions["Point"];
        _click = InputSystem.actions["Click"];
        _sceneCamera = Camera.main!;
        _groundCollider = ground.GetComponent<TilemapCollider2D>()!;
        _occupiedCells.OnChange += OccupiedCellsOnChange;
    }

    private void OnDestroy()
    {
        _occupiedCells.OnChange -= OccupiedCellsOnChange;
    }

    public override void OnStartClient()
    {
        _sceneCamera = Camera.main!;
        _point.Enable();
        _click.Enable();
        _click.performed += OnClickPerformed;

        EnsureGhostWall();
        RefreshPlacedWalls();
    }

    public override void OnStopClient()
    {
        _click.performed -= OnClickPerformed;
        _click.Disable();
        _point.Disable();
        _hasHoveredCell = false;
        _canPlaceHoveredCell = false;

        if (_ghostWall != null)
        {
            Destroy(_ghostWall);
        }

        ClearPlacedWalls();
    }

    private void Update()
    {
        if (!IsClientInitialized)
        {
            return;
        }

        UpdateHoveredCell();
        UpdateGhostWall();
    }

    private void OnClickPerformed(InputAction.CallbackContext _)
    {
        if (!_hasHoveredCell || !_canPlaceHoveredCell)
        {
            return;
        }

        PlaceWallServerRpc(_hoveredCell);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlaceWallServerRpc(Vector3Int cell)
    {
        TryPlaceWall(cell);
    }

    private void UpdateHoveredCell()
    {
        var screenPosition = _point.ReadValue<Vector2>();
        var ray = _sceneCamera.ScreenPointToRay(screenPosition);
        var hits = Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity);

        _hasHoveredCell = false;
        _canPlaceHoveredCell = false;

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider != _groundCollider)
            {
                continue;
            }

            var cell = ground.WorldToCell(hit.point);
            if (!IsBuildableCell(cell))
            {
                return;
            }

            _hoveredCell = cell;
            _hasHoveredCell = true;
            _canPlaceHoveredCell = !_occupiedCells.Contains(cell);
            return;
        }
    }

    private bool IsBuildableCell(Vector3Int cell)
    {
        return ground.GetTile(cell) == grass;
    }

    private void TryPlaceWall(Vector3Int cell)
    {
        if (!IsBuildableCell(cell) || _occupiedCells.Contains(cell))
        {
            return;
        }

        _occupiedCells.Add(cell);
    }

    private void UpdateGhostWall()
    {
        if (_ghostWall == null)
        {
            return;
        }

        if (!_hasHoveredCell)
        {
            _ghostWall.SetActive(false);
            return;
        }

        var worldPosition = ground.GetCellCenterWorld(_hoveredCell);
        worldPosition.z = 0f;

        _ghostWall.transform.position = worldPosition;
        _ghostWall.SetActive(true);
        _ghostRenderer.color = _canPlaceHoveredCell
            ? new Color(1f, 1f, 1f, ghostAlpha)
            : invalidGhostColor;
    }

    private void EnsureGhostWall()
    {
        if (_ghostWall != null)
        {
            return;
        }

        _ghostWall = Instantiate(wallPrefab, transform);
        _ghostWall.name = $"{wallPrefab.name} Ghost";
        _ghostRenderer = _ghostWall.GetComponent<SpriteRenderer>()!;
        _ghostRenderer.sortingOrder += 1;
        _ghostRenderer.color = new Color(1f, 1f, 1f, ghostAlpha);
        _ghostWall.SetActive(false);
    }

    private void OccupiedCellsOnChange(SyncHashSetOperation operation, Vector3Int cell, bool _)
    {
        if (!IsClientInitialized)
        {
            return;
        }

        switch (operation)
        {
            case SyncHashSetOperation.Add:
                CreatePlacedWall(cell);
                break;
            case SyncHashSetOperation.Remove:
                RemovePlacedWall(cell);
                break;
            case SyncHashSetOperation.Clear:
                ClearPlacedWalls();
                break;
        }
    }

    private void RefreshPlacedWalls()
    {
        ClearPlacedWalls();

        foreach (var cell in _occupiedCells.Collection)
        {
            CreatePlacedWall(cell);
        }
    }

    private void CreatePlacedWall(Vector3Int cell)
    {
        if (_placedWalls.ContainsKey(cell))
        {
            return;
        }

        var wall = Instantiate(wallPrefab, transform);
        var worldPosition = ground.GetCellCenterWorld(cell);
        worldPosition.z = 0f;

        wall.name = $"{wallPrefab.name} ({cell.x}, {cell.y})";
        wall.transform.position = worldPosition;
        _placedWalls[cell] = wall;
    }

    private void RemovePlacedWall(Vector3Int cell)
    {
        if (!_placedWalls.TryGetValue(cell, out var wall))
        {
            return;
        }

        _placedWalls.Remove(cell);
        Destroy(wall);
    }

    private void ClearPlacedWalls()
    {
        foreach (var wall in _placedWalls.Values)
        {
            Destroy(wall);
        }

        _placedWalls.Clear();
    }
}
