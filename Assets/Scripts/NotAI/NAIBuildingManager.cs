using System;
using DefaultNamespace;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace NotAI
{
    public class NAIBuildingManager : NetworkBehaviour
    {
        public GameObject testPrefab;

        private InputAction _buildAction;
        private readonly SyncVar<bool> _isBuilding =
            new(new SyncTypeSettings(WritePermission.ClientUnsynchronized, ReadPermission.ExcludeOwner));
        private GameObject _ghost;
        private Grid _grid;
        private Camera _camera;

        private void Awake()
        {
            enabled = false;
            _grid = GameObject.Find("Grid").GetComponent<Grid>();
            _camera = Camera.main!;
            _ghost = Instantiate(testPrefab);
            _ghost.SetActive(false);
        }

        public override void OnStartClient()
        {
            _isBuilding.OnChange += OnIsBuildingChanged;
            
            if (!IsOwner) return;

            _buildAction = InputSystem.actions["Build"];
            _buildAction.Enable();
            _buildAction.performed += OnBuildButton;
        }

        public override void OnStopClient()
        {
            if (!IsOwner) return;
            _buildAction.Disable();
            _buildAction.performed -= OnBuildButton;
        }

        private void OnBuildButton(InputAction.CallbackContext ctx) => ToggleBuildGhost();

        [ServerRpc(RunLocally = true)]
        private void ToggleBuildGhost() => _isBuilding.Value = !_isBuilding.Value;

        private void OnIsBuildingChanged(bool prev, bool next, bool asServer)
        {
            _ghost.SetActive(next);
            enabled = next;
        }

        private void Update()
        {
            // Raycast from the mouse onto the grid so we can move the ghost building to follow the mouse.
            // Use Input System v2 to get the mouse position.
            var mousePos = Mouse.current.position.ReadValue();
            var worldPos = _camera.ScreenToWorldPoint(mousePos);
            var cellPos = _grid.WorldToCell(worldPos);
            _ghost.transform.position = _grid.GetCellCenterWorld(cellPos);
        }
    }
}