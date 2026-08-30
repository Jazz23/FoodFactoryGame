using System.Collections.Generic;
using System.Linq;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NotAI
{
    public class NAIBuildingManager : NetworkBehaviour
    {
        private static Dictionary<Vector2, GameObject> _buildings = new();
        
        public GameObject ghostPrefab;

        private InputAction _buildAction;
        private InputAction _confirmBuildAction;
        private readonly SyncVar<bool> _isBuilding =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        
        private GameObject _ghost;
        private SpriteRenderer _ghostRenderer;
        private Grid _grid;

        public void SetGhost(GameObject go)
        {
            _ghost = go;
            _ghostRenderer = go.GetComponent<SpriteRenderer>();
        }

        private void Awake() => _grid = GameObject.Find("Grid").GetComponent<Grid>();
        
        public override void OnStartServer()
        {
            var no = Instantiate(ghostPrefab).GetComponent<NetworkObject>();
            ServerManager.Spawn(no, Owner);
        }

        public override void OnStartClient()
        {
            _isBuilding.OnChange += OnIsBuildingChanged;
            
            if (!IsOwner) return;

            _buildAction = InputSystem.actions["Build"];
            _buildAction.Enable();
            _buildAction.performed += OnBuildButton;
            
            _confirmBuildAction = InputSystem.actions["ConfirmBuild"];
            _confirmBuildAction.Enable();
            _confirmBuildAction.performed += OnConfirmBuildButton;
        }

        public override void OnStopClient()
        {
            if (!IsOwner) return;
            _buildAction.Disable();
            _buildAction.performed -= OnBuildButton;
            _confirmBuildAction.Disable();
            _confirmBuildAction.performed -= OnConfirmBuildButton;
        }

        private void OnBuildButton(InputAction.CallbackContext ctx) => ToggleBuildGhost();

        [ServerRpc(RunLocally = true)]
        private void ToggleBuildGhost() => _isBuilding.Value = !_isBuilding.Value;

        private void OnIsBuildingChanged(bool prev, bool next, bool asServer) => _ghost.SetActive(next);
        
        [Client]
        private void OnConfirmBuildButton(InputAction.CallbackContext ctx)
        {
            if (!_isBuilding.Value || !CanBuildHere()) return;
            GetGhostOccupiedCells().ForEach(cell => _buildings[cell] = _ghost);
        }

        public void BuildGhost()
        {
            
        }

        public bool CanBuildHere()
        {
            var occupiedCells = GetGhostOccupiedCells();
            return occupiedCells.All(cell => !_buildings.ContainsKey(cell));
        }

        // Get the world pos cells that the ghost building occupies based on its position and size.
        private List<Vector2> GetGhostOccupiedCells()
        {
            var cellSize = _grid.cellSize;
            var ghostPos = _ghost.transform.position;
            var ghostSize = _ghostRenderer.bounds.size;
            
            var cellsX = Mathf.CeilToInt(ghostSize.x / cellSize.x);
            var cellsY = Mathf.CeilToInt(ghostSize.y / cellSize.y);

            var occupiedCells = new List<Vector2>();
            for (var x = 0; x < cellsX; x++)
            {
                for (var y = 0; y < cellsY; y++)
                {
                    occupiedCells.Add(new Vector2(ghostPos.x + x * cellSize.x, ghostPos.y + y * cellSize.y));
                }
            }
            
            return occupiedCells;
        }
    }
}