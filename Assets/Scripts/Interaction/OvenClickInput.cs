// Routes the configured Input System click action to ovens hit by the presentation camera.
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Interaction
{

[DisallowMultipleComponent]
public sealed class OvenClickInput : MonoBehaviour
{
    [SerializeField] private InputActionReference clickAction;
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private float maximumRayDistance = 100f;

    private void OnEnable()
    {
        clickAction.action.performed += HandleClick;
        clickAction.action.Enable();
    }

    private void OnDisable()
    {
        clickAction.action.performed -= HandleClick;
        clickAction.action.Disable();
    }

    public bool TryToggleAtScreenPosition(Vector2 screenPosition)
    {
        var ray = interactionCamera.ScreenPointToRay(screenPosition);
        if (!Physics.Raycast(ray, out var hit, maximumRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        var oven = hit.collider.GetComponentInParent<OvenToggle>();
        if (oven == null)
        {
            return false;
        }

        oven.Toggle();
        return true;
    }

    private void HandleClick(InputAction.CallbackContext context)
    {
        TryToggleAtScreenPosition(Pointer.current.position.ReadValue());
    }
}
}
