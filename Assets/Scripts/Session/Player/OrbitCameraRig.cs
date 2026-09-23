// Owner-only third-person camera: orbits while the Orbit action is held so the cursor stays free, and zooms in steps.
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class OrbitCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference orbitAction;
        [SerializeField] private InputActionReference zoomAction;
        [SerializeField] private float focusHeight = 1f;
        [SerializeField] private float orbitDegreesPerUnit = 0.2f;
        [SerializeField] private float zoomStep = 1f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 40f;
        [SerializeField] private float distance = 9f;

        public float Yaw => yaw;
        public float Pitch => pitch;
        public float Distance => distance;

        private void OnEnable()
        {
            lookAction.action.Enable();
            orbitAction.action.Enable();
            zoomAction.action.Enable();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
            orbitAction.action.Disable();
            zoomAction.action.Disable();
        }

        private void LateUpdate()
        {
            if (orbitAction.action.IsPressed())
            {
                var delta = lookAction.action.ReadValue<Vector2>();
                yaw = Mathf.Repeat(yaw + delta.x * orbitDegreesPerUnit, 360f);
                pitch -= delta.y * orbitDegreesPerUnit;
            }
            // Scroll magnitude differs by platform/device, so each nonzero frame is one step.
            var scroll = zoomAction.action.ReadValue<Vector2>().y;
            if (!Mathf.Approximately(scroll, 0f)) distance -= Mathf.Sign(scroll) * zoomStep;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            // The rig ignores the avatar's facing; only position follows the target.
            transform.SetPositionAndRotation(target.position + Vector3.up * focusHeight, Quaternion.Euler(pitch, yaw, 0f));
            cameraTransform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -distance), Quaternion.identity);
        }
    }
}
