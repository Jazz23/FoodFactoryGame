using DefaultNamespace;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NotAI.UI
{
    // Attached to the root canvas. Opens the UI of things with a UI when the player clicks on them.
    public class NAIUIOpener : MonoBehaviour
    {
        private InputAction _selectAction;
        private InputAction _cancelAction;
        private GameObject _openUI;

        private void Awake()
        {
            _selectAction = InputSystem.actions["Player/Select"];
            _cancelAction = InputSystem.actions["UI/Cancel"];
            _selectAction.Enable();
            _cancelAction.Enable();
            
            _selectAction.performed += OnSelectButton;
            _cancelAction.performed += OnCancelButton;
        }

        private void OnCancelButton(InputAction.CallbackContext ctx)
        {
            Destroy(_openUI.gameObject);
            _selectAction.Enable();
        }

        private void OnDestroy()
        {
            _selectAction.performed -= OnSelectButton;
            _cancelAction.performed -= OnCancelButton;
        }

        private void OnSelectButton(InputAction.CallbackContext ctx)
        {
            var cellPos = NAIExtensions.GetPointerCellPos2D();
            if (!NAIStateManager.OccupiedTiles.TryGetValue(cellPos, out var guid)) return;
            
            var buildable = NAIStateManager.Buildables[guid];
            if (!buildable.TryGetComponent(out NAIOpenableUI openableUI)) return;
            
            _openUI = openableUI.OpenUI(transform);
            _selectAction.Disable();
        }
    }
}