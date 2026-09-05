// Moves the NAI building preview to the current build pointer position.
using DefaultNamespace;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NotAI
{
    public class NAIGhostBuildable : NetworkBehaviour
    {
        public int buildingId;
        
        private Grid _grid;
        private Camera _camera;
        private NAIBuildableManager _buildableManager;
        private SpriteRenderer _spriteRenderer;
        private InputAction _pointAction;

        public override void OnStartNetwork() => gameObject.SetActive(false);

        public override void OnStartClient()
        {
            _buildableManager = Owner.GetPlayerComponent<NAIBuildableManager>();
            _buildableManager.SetGhost(this);
            _spriteRenderer = GetComponent<SpriteRenderer>();
            _spriteRenderer.sprite = _buildableManager.buildingPrefabs[_buildableManager.SelectedBuildableId.Value]
                .GetComponent<SpriteRenderer>().sprite;

            if (!IsOwner) return;
            
            _camera = Camera.main!;
            _grid = GameObject.Find("Grid").GetComponent<Grid>();
            (_pointAction = InputSystem.actions["Player/Point"]).Enable();
        }

        public override void OnStopClient()
        {
            if (IsOwner) _pointAction.Disable();
        }
        
        // The ghost building is deactivated by building manager so Update() is not called when the player is not building.
        private void Update()
        {
            var newColor = _buildableManager.CanBuildGhostHere() ? Color.white : Color.red;
            newColor.a = _spriteRenderer.color.a;
            _spriteRenderer.color = newColor;
            
            if (!IsOwner) return;
            var mousePos = _pointAction.ReadValue<Vector2>();
            var worldPos = _camera.ScreenToWorldPoint(mousePos);
            var cellPos = _grid.WorldToCell(worldPos);
            
            // Calculate the vertical offset from the center of the sprite since the transform's origin is at the bottom
            var spriteHeight = _spriteRenderer.bounds.size.y;
            var verticalOffset = spriteHeight / 2f;
            transform.position = _grid.GetCellCenterWorld(cellPos) - new Vector3(0, verticalOffset, 0);
        }

        public void Rotate()
        {
            
        }
    }
}
