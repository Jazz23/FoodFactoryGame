using DefaultNamespace;
using FishNet.Connection;
using FishNet.Object;
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
        
        public override void OnStartNetwork() => gameObject.SetActive(false);

        public override void OnStartClient()
        {
            _buildingManager = Owner.GetPlayerComponent<NAIBuildingManager>();
            _buildingManager.SetGhost(gameObject);
            
            _spriteRenderer = GetComponent<SpriteRenderer>();

            if (!IsOwner) return;
            
            _camera = Camera.main!;
            _grid = GameObject.Find("Grid").GetComponent<Grid>();
            
        }
        
        private void Update()
        {
            var newColor = _buildingManager.CanBuildHere() ? Color.white : Color.red;
            newColor.a = _spriteRenderer.color.a;
            _spriteRenderer.color = newColor;
            
            if (!IsOwner) return;
            // Raycast from the mouse onto the grid so we can move the ghost building to follow the mouse.
            // Use Input System v2 to get the mouse position.
            var mousePos = Mouse.current.position.ReadValue();
            var worldPos = _camera.ScreenToWorldPoint(mousePos);
            var cellPos = _grid.WorldToCell(worldPos);
            transform.position = _grid.GetCellCenterWorld(cellPos);
        }
    }
}