using FishNet.Object;
using UnityEngine.InputSystem;

namespace NotAI
{
    public class NAIInventoryManager : NetworkBehaviour
    {
        private InputAction _pointAction;
        private InputAction _selectAction;
        
        public override void OnStartClient()
        {
            (_pointAction = InputSystem.actions["Player/Point"]).Enable();
            (_selectAction = InputSystem.actions["Player/Select"]).Enable();
            _selectAction.performed += OnSelectButton;
        }

        public override void OnStopClient()
        {
            _selectAction.performed -= OnSelectButton;
        }

        private void OnSelectButton(InputAction.CallbackContext ctx)
        {
            
        }
    }
}