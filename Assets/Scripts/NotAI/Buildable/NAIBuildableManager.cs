using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NotAI
{
    /// <summary>
    /// Attached to each player, this class manages placing down buildables.
    /// </summary>
    public class NAIBuildableManager : NetworkBehaviour
    {
        public GameObject ghostPrefab;
        public List<GameObject> buildingPrefabs;

        private InputAction _buildAction;
        private InputAction _placeAction;
        private InputAction _rotateAction;
        
        private readonly SyncVar<bool> _isBuilding =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        
        // The index of buildingPrefabs
        public readonly SyncVar<int> SelectedBuildableId =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        
        private NAIGhostBuildable _ghost;
        private SpriteRenderer _ghostRenderer;
        private Grid _grid;

        [Client]
        public void SetGhost(NAIGhostBuildable ghost)
        {
            _ghost = ghost;
            _ghostRenderer = ghost.GetComponent<SpriteRenderer>();
        }

        private void Awake()
        {
            _grid = GameObject.Find("Grid").GetComponent<Grid>();
            enabled = false;
        }
        
        public override void OnStartServer()
        {
            var no = Instantiate(ghostPrefab).GetComponent<NetworkObject>();
            ServerManager.Spawn(no, Owner);
        }

        public override void OnStartClient()
        {
            _isBuilding.OnChange += OnIsBuildingChanged;
            
            if (!IsOwner) return;

            _buildAction = InputSystem.actions["Build/Build"];
            _buildAction.Enable();
            _buildAction.performed += OnBuildButton;
            
            _placeAction = InputSystem.actions["Build/Place"];
            _placeAction.Enable();
            _placeAction.performed += OnPlaceButton;
            
            _rotateAction = InputSystem.actions["Build/Rotate"];
            _rotateAction.Enable();
            _rotateAction.performed += OnRotateButton;
        }

        public override void OnStopClient()
        {
            if (!IsOwner) return;
            _buildAction.Disable();
            _buildAction.performed -= OnBuildButton;
            _placeAction.Disable();
            _placeAction.performed -= OnPlaceButton;
            _rotateAction.Disable();
            _rotateAction.performed -= OnRotateButton;
        }

        [Client]
        private void OnBuildButton(InputAction.CallbackContext ctx) => ToggleBuildGhost();

        [Client]
        private void Update()
        {
            if (IsOwner && _isBuilding.Value && _placeAction.IsPressed() && CanBuildGhostHere())
                SpawnBuilding();
        }

        [ServerRpc(RunLocally = true)]
        private void ToggleBuildGhost() => _isBuilding.Value = !_isBuilding.Value;

        [Client]
        private void OnIsBuildingChanged(bool prev, bool next, bool asServer)
        {
            _ghost.gameObject.SetActive(next);
            if (IsOwner) enabled = next;
        }
        
        [Client]
        private void OnPlaceButton(InputAction.CallbackContext ctx)
        {
            if (!_isBuilding.Value || !CanBuildGhostHere()) return;
            SpawnBuilding();
        }

        [Client]
        private void SpawnBuilding()
        {
            var building = Instantiate(buildingPrefabs[SelectedBuildableId.Value], _ghost.transform.position, _ghost.transform.rotation)
                .GetComponent<NAIBuildablePredictiveSpawn>();
            building.SetBuildingManager(this);
            building.SetBuildingId(SelectedBuildableId.Value);
            ServerManager.Spawn(building);
            
            // Temporarily instantly update the grid on the client to prevent further building
            UpdateGrid(_ghost.transform.position, _ghostRenderer.bounds.size, Guid.Empty);
        }

        [Client]
        public bool CanBuildGhostHere()
        {
            return CanBuildHere(_ghost.transform.position, _ghostRenderer.bounds.size);
        }

        [Client]
        private void OnRotateButton(InputAction.CallbackContext ctx)
        {
            if (!_isBuilding.Value) return;
            _ghost.transform.Rotate(0, 0, 90);
        }
        
        // Ran on client and server to update the occupancy grid when a building is placed
        public void UpdateGrid(Vector3 position, Vector2 size, Guid guid)
        {
            GetOccupiedCells(position, size).ForEach(cell => NAIStateManager.OccupiedTiles[cell] = guid);
        }

        public bool CanBuildHere(Vector3 position, Vector2 size)
        {
            var occupiedCells = GetOccupiedCells(position, size);
            return occupiedCells.All(cell => !NAIStateManager.OccupiedTiles.ContainsKey(cell));
        }

        // Get the world pos cells that the ghost building occupies based on its position and size.
        private List<Vector2> GetOccupiedCells(Vector3 position, Vector2 size)
        {
            // Use only 3 decimal places to avoid floating point errors when comparing positions.
            size.x = Mathf.RoundToInt(size.x * 1000f) / 1000f;
            size.y = Mathf.RoundToInt(size.y * 1000f) / 1000f;
            
            var cellSize = _grid.cellSize;
            
            var cellsX = Mathf.CeilToInt(size.x / cellSize.x);
            var cellsY = Mathf.CeilToInt(size.y / cellSize.y);

            var occupiedCells = new List<Vector2>();
            for (var x = 0; x < cellsX; x++)
            {
                for (var y = 0; y < cellsY; y++)
                {
                    occupiedCells.Add(new Vector2(position.x + x * cellSize.x, position.y + y * cellSize.y));
                }
            }
            
            return occupiedCells;
        }
    }
}
