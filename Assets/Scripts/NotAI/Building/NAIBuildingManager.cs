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
    public class NAIBuildingManager : NetworkBehaviour
    {
        public GameObject ghostPrefab;
        public List<GameObject> buildingPrefabs;

        private InputAction _buildAction;
        private InputAction _placeAction;
        
        private readonly SyncVar<bool> _isBuilding =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        
        // The index of buildingPrefabs
        public readonly SyncVar<int> SelectedBuildingId =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        
        private NAIGhostBuilding _ghost;
        private SpriteRenderer _ghostRenderer;
        private Grid _grid;

        [Client]
        public void SetGhost(NAIGhostBuilding ghost)
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
        }

        public override void OnStopClient()
        {
            if (!IsOwner) return;
            _buildAction.Disable();
            _buildAction.performed -= OnBuildButton;
            _placeAction.Disable();
            _placeAction.performed -= OnPlaceButton;
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
            var building = Instantiate(buildingPrefabs[SelectedBuildingId.Value], _ghost.transform.position, Quaternion.identity)
                .GetComponent<NAIBuildingPredictiveSpawn>();
            building.SetBuildingManager(this);
            building.SetBuildingId(SelectedBuildingId.Value);
            ServerManager.Spawn(building);
            
            // Instantly build on the client
            UpdateGrid(_ghost.transform.position, _ghostRenderer.bounds.size, SelectedBuildingId.Value);
        }

        [Client]
        public bool CanBuildGhostHere()
        {
            return CanBuildHere(_ghost.transform.position, _ghostRenderer.bounds.size);
        }
        
        // Uses the spawned building on the server, where the client-only ghost does not exist.
        public void UpdateGrid(Vector3 position, Vector2 size, int buildingIndex)
        {
            GetOccupiedCells(position, size).ForEach(cell => NAIStateManager.Buildables[cell] = buildingIndex);
        }

        public bool CanBuildHere(Vector3 position, Vector2 size)
        {
            var occupiedCells = GetOccupiedCells(position, size);
            return occupiedCells.All(cell => !NAIStateManager.Buildables.ContainsKey(cell));
        }

        // Get the world pos cells that the ghost building occupies based on its position and size.
        private List<Vector2> GetOccupiedCells(Vector3 position, Vector2 size)
        {
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
