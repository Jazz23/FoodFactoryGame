// Moves the NAI building preview to the current build pointer position.
using DefaultNamespace;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NotAI
{
    public class NAIGhostBuilding : NetworkBehaviour
    {
        public int buildingId;
        
        private Grid _grid;
        private Camera _camera;
        private NAIBuildingManager _buildingManager;
        private SpriteRenderer _spriteRenderer;
        private InputAction _pointAction;

        public override void OnStartNetwork() => gameObject.SetActive(false);

        public override void OnStartClient()
        {
            _buildingManager = Owner.GetPlayerComponent<NAIBuildingManager>();
            _buildingManager.SetGhost(this);
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = _buildingManager.buildingPrefabs[_buildingManager.SelectedBuildingId.Value]
                .GetComponent<SpriteRenderer>().sprite;

            if (!IsOwner) return;
            
            _camera = Camera.main!;
            _grid = GameObject.Find("Grid").GetComponent<Grid>();
            (_pointAction = InputSystem.actions["Build/Point"]).Enable();
        }

        public override void OnStopClient()
        {
            if (IsOwner) _pointAction.Disable();
        }
        
        // The ghost building is deactivated by building manager so Update() is not called when the player is not building.
        private void Update()
        {
            var newColor = _buildingManager.CanBuildGhostHere() ? Color.white : Color.red;
            newColor.a = _spriteRenderer.color.a;
            _spriteRenderer.color = newColor;
            
            if (!IsOwner) return;
            var mousePos = _pointAction.ReadValue<Vector2>();
            var worldPos = _camera.ScreenToWorldPoint(mousePos);
            var cellPos = _grid.WorldToCell(worldPos);
            transform.position = _grid.GetCellCenterWorld(cellPos);
        }

        public void Rotate()
        {
            
        }
    }
}
