// Owner-only camera with two views that SwitchCamera toggles, blending smoothly between them. The third-person view
// orbits with Look (the gameplay cursor is locked to a centre crosshair) unless the local UI suspends it through
// OrbitEnabled while a screen is open; its pivot sits beside the avatar (over the shoulder), so the crosshair aims past
// the avatar at the floor ahead instead of through it. The top-down view looks straight down on the avatar with its yaw
// snapped to a multiple of 90 degrees, so the world-aligned site grid reads as horizontal and vertical lines; Look does
// not orbit it. Each view zooms its own distance in steps, and Yaw follows the blend so movement stays screen-relative.
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
        [SerializeField] private InputActionReference zoomAction;
        [SerializeField] private InputActionReference switchViewAction;
        [SerializeField] private float focusHeight = 1f;
        [SerializeField] private float shoulderOffset = 1f;
        [SerializeField] private float orbitDegreesPerUnit = 0.2f;
        [SerializeField] private float zoomStep = 1f;
        [SerializeField] private float minPitch = 10f;
        [SerializeField] private float maxPitch = 80f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 40f;
        [SerializeField] private float distance = 9f;
        [SerializeField] private float topDownDistance = 14f;
        [SerializeField] private float transitionSeconds = 0.4f;

        // 0 is the third-person view and 1 the top-down view; eased when applied.
        private float _blend;
        private float _topDownYaw;

        public float Yaw => Mathf.LerpAngle(yaw, _topDownYaw, Eased);
        public float Pitch => pitch;
        public float Distance => distance;
        // The view the rig is showing or blending towards.
        public bool TopDown { get; private set; }
        // Presentation state owned by the local interaction layer; false while an inventory or machine screen is open.
        public bool OrbitEnabled { get; set; } = true;

        private float Eased => Mathf.SmoothStep(0f, 1f, _blend);

        private void OnEnable()
        {
            lookAction.action.Enable();
            zoomAction.action.Enable();
            switchViewAction.action.performed += OnSwitchView;
            switchViewAction.action.Enable();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
            zoomAction.action.Disable();
            switchViewAction.action.performed -= OnSwitchView;
            switchViewAction.action.Disable();
        }

        private void OnSwitchView(InputAction.CallbackContext _)
        {
            TopDown = !TopDown;
            // The orbit yaw does not change while top-down, so re-entering mid-blend picks the same grid-aligned yaw.
            if (TopDown) _topDownYaw = Mathf.Round(yaw / 90f) * 90f;
        }

        private void LateUpdate()
        {
            if (OrbitEnabled && !TopDown)
            {
                var delta = lookAction.action.ReadValue<Vector2>();
                yaw = Mathf.Repeat(yaw + delta.x * orbitDegreesPerUnit, 360f);
                pitch -= delta.y * orbitDegreesPerUnit;
            }
            // Scroll magnitude differs by platform/device, so each nonzero frame is one step.
            var scroll = zoomAction.action.ReadValue<Vector2>().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                if (TopDown) topDownDistance -= Mathf.Sign(scroll) * zoomStep;
                else distance -= Mathf.Sign(scroll) * zoomStep;
            }
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            topDownDistance = Mathf.Clamp(topDownDistance, minDistance, maxDistance);
            _blend = transitionSeconds > 0f ? Mathf.MoveTowards(_blend, TopDown ? 1f : 0f, Time.deltaTime / transitionSeconds) : TopDown ? 1f : 0f;

            // The rig ignores the avatar's facing; only position follows the target.
            var eased = Eased;
            var focus = target.position + Vector3.up * focusHeight;
            var shoulder = focus + Quaternion.Euler(0f, yaw, 0f) * Vector3.right * shoulderOffset;
            transform.SetPositionAndRotation(Vector3.Lerp(shoulder, focus, eased),
                Quaternion.Slerp(Quaternion.Euler(pitch, yaw, 0f), Quaternion.Euler(90f, _topDownYaw, 0f), eased));
            cameraTransform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -Mathf.Lerp(distance, topDownDistance, eased)), Quaternion.identity);
        }
    }
}
