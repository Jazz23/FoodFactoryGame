// Owner-only camera with two views that SwitchCamera toggles, blending smoothly between them. The third-person view
// orbits with Look (the gameplay cursor is locked to a centre crosshair) unless the local UI suspends it through
// OrbitEnabled while a screen is open; its pivot sits beside the avatar (over the shoulder), so the crosshair aims past
// the avatar at the floor ahead instead of through it. Pitch runs from straight up to straight down; looking up pulls the
// camera in along its arm so it stays above the avatar's floor. The top-down view looks straight down on the avatar with its yaw
// snapped to a multiple of 90 degrees, so the world-aligned site grid reads as horizontal and vertical lines; Look does
// not orbit it. Each view zooms its own distance in steps, and Yaw follows the blend so movement stays screen-relative.
// The third-person arm never passes through a wall: a sphere cast from the avatar pulls the camera in front of the first solid
// collider at least WallHeight tall that is not an avatar, a machine, furniture, an employee or the dev storage (colliders the
// cast starts inside are ignored), and indoors it also stays under the room's ceiling,
// so in a small room the camera squeezes in beside the avatar instead of looking in from outside.
// Indoors uses the top-down view (GDD section 3): SetIndoors switches views only when the avatar crosses a building's
// threshold, so SwitchCamera still overrides the view until the next crossing (decision 0019).
// Build mode (decision 0034) frames a whole lot: while BuildFocus is set the camera looks at that point from BuildDistance instead
// of following the avatar, and the previous view returns when it is cleared. While BuildControls is on, Move pans the focus,
// Zoom changes the distance and holding CameraTilt turns Look into tilt (pitch) and turn (yaw).
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class OrbitCameraRig : MonoBehaviour
    {
        // Colliders shorter than this (chairs, tables, counters) never pull the camera in.
        private const float WallHeight = 1.5f;
        private const float CastRadius = 0.2f;
        // Closest the camera comes to its pivot when a wall pulls it in, and its gap below a ceiling.
        private const float MinimumArm = 0.4f;
        private const float CeilingGap = 0.25f;
        // Build mode limits: distance (metres) and pitch (degrees above the horizon).
        private const float MinBuildDistance = 4f;
        private const float MaxBuildDistance = 90f;
        private const float MinBuildPitch = 30f;

        [SerializeField] private Transform target;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private InputActionReference lookAction;
        [SerializeField] private InputActionReference zoomAction;
        [SerializeField] private InputActionReference switchViewAction;
        // Build mode camera controls (Player/Move pans, Player/CameraTilt held tilts).
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference tiltAction;
        [SerializeField] private float focusHeight = 1f;
        [SerializeField] private float shoulderOffset = 1f;
        [SerializeField] private float orbitDegreesPerUnit = 0.2f;
        [SerializeField] private float zoomStep = 1f;
        [SerializeField] private float minPitch = -89f;
        [SerializeField] private float maxPitch = 89f;
        // Least height (metres) the third-person camera keeps above the avatar's feet when looking up.
        [SerializeField] private float floorClearance = 0.3f;
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 20f;
        [SerializeField] private float yaw;
        [SerializeField] private float pitch = 40f;
        [SerializeField] private float distance = 9f;
        [SerializeField] private float topDownDistance = 14f;
        [SerializeField] private float transitionSeconds = 0.4f;
        // Build mode pan speed in metres per second at a 20 m distance (it scales with the distance).
        [SerializeField] private float buildPanSpeed = 12f;
        // Seconds the arm takes to ease back out after a wall stops blocking it (pulling in is immediate).
        [SerializeField] private float armReturnSeconds = 0.25f;

        // 0 is the third-person view and 1 the top-down view; eased when applied.
        private float _blend;
        private float _topDownYaw;
        private bool _indoors;
        private float? _ceiling;
        private float _arm = float.MaxValue;
        private float _buildYaw;
        private float _buildPitch = 90f;
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        public float Yaw => BuildFocus.HasValue ? _buildYaw : Mathf.LerpAngle(yaw, _topDownYaw, Eased);
        public float Pitch => pitch;
        public float Distance => distance;
        // The view the rig is showing or blending towards.
        public bool TopDown { get; private set; }
        // Presentation state owned by the local interaction layer; false while an inventory or machine screen is open.
        public bool OrbitEnabled { get; set; } = true;
        // Build mode's framing (decision 0034); null while the rig follows the avatar.
        public Vector3? BuildFocus { get; private set; }
        public float BuildDistance { get; private set; }
        public float BuildPitch => _buildPitch;
        public float BuildYaw => _buildYaw;
        // Set by build mode: false while the pointer is over its panel, so scrolling the catalog does not zoom.
        public bool BuildControls { get; set; } = true;
        // How far the third-person camera currently stands from its pivot (shortened by walls and ceilings).
        public float CurrentArm { get; private set; }
        private bool _topDownBeforeBuild;

        // Looks down on focus from distance metres (null focus returns to the view used before). Entering build mode starts
        // straight down at the orbit yaw snapped to the grid, unless a yaw and pitch are given (a remembered build view).
        public void SetBuildView(Vector3? focus, float distance, float? buildYaw = null, float? buildPitch = null)
        {
            if (focus.HasValue && !BuildFocus.HasValue)
            {
                _topDownBeforeBuild = TopDown;
                _buildYaw = buildYaw ?? Mathf.Round(yaw / 90f) * 90f;
                _buildPitch = Mathf.Clamp(buildPitch ?? 90f, MinBuildPitch, 90f);
            }
            if (!focus.HasValue && BuildFocus.HasValue) SetTopDown(_topDownBeforeBuild || _indoors);
            BuildFocus = focus;
            BuildDistance = distance;
            if (focus.HasValue) SetTopDown(true);
        }

        private float Eased => Mathf.SmoothStep(0f, 1f, _blend);

        private void OnEnable()
        {
            lookAction.action.Enable();
            zoomAction.action.Enable();
            moveAction.action.Enable();
            tiltAction.action.Enable();
            switchViewAction.action.performed += OnSwitchView;
            switchViewAction.action.Enable();
        }

        private void OnDisable()
        {
            lookAction.action.Disable();
            zoomAction.action.Disable();
            tiltAction.action.Disable();
            switchViewAction.action.performed -= OnSwitchView;
            switchViewAction.action.Disable();
        }

        private void OnSwitchView(InputAction.CallbackContext _)
        {
            if (!BuildFocus.HasValue) SetTopDown(!TopDown);
        }

        // Presentation state from the local building presenter: true while the avatar stands inside a building, with the height
        // of its ceiling (null when the presenter knows none).
        public void SetIndoors(bool indoors, float? ceiling = null)
        {
            _ceiling = indoors ? ceiling : null;
            if (indoors == _indoors) return;
            _indoors = indoors;
            if (!BuildFocus.HasValue) SetTopDown(indoors);
        }

        private void SetTopDown(bool topDown)
        {
            if (topDown == TopDown) return;
            TopDown = topDown;
            // The orbit yaw does not change while top-down, so re-entering mid-blend picks the same grid-aligned yaw.
            if (TopDown) _topDownYaw = Mathf.Round(yaw / 90f) * 90f;
        }

        private void LateUpdate()
        {
            if (BuildFocus.HasValue) UpdateBuildControls();
            else if (OrbitEnabled && !TopDown)
            {
                var delta = lookAction.action.ReadValue<Vector2>();
                yaw = Mathf.Repeat(yaw + delta.x * orbitDegreesPerUnit, 360f);
                pitch -= delta.y * orbitDegreesPerUnit;
            }
            // Scroll magnitude differs by platform/device, so each nonzero frame is one step.
            var scroll = BuildFocus.HasValue ? 0f : zoomAction.action.ReadValue<Vector2>().y;
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
            var focus = BuildFocus ?? target.position + Vector3.up * focusHeight;
            var avatarFocus = target.position + Vector3.up * focusHeight;
            var side = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            // The shoulder pivot never sits inside a wall beside the avatar.
            var shoulderReach = BuildFocus.HasValue ? shoulderOffset : Reach(avatarFocus, side, shoulderOffset, 0f);
            var shoulder = focus + side * shoulderReach;
            var downRotation = BuildFocus.HasValue ? Quaternion.Euler(_buildPitch, _buildYaw, 0f) : Quaternion.Euler(90f, _topDownYaw, 0f);
            var rotation = Quaternion.Slerp(Quaternion.Euler(pitch, yaw, 0f), downRotation, eased);
            var pivot = Vector3.Lerp(shoulder, focus, eased);
            transform.SetPositionAndRotation(pivot, rotation);
            // Looking up swings the camera below the focus; its arm is shortened so it never drops through the floor.
            var orbitDistance = pitch < 0f
                ? Mathf.Min(distance, Mathf.Max(0f, focusHeight - floorClearance) / Mathf.Sin(-pitch * Mathf.Deg2Rad))
                : distance;
            if (!BuildFocus.HasValue) orbitDistance = Collide(pivot, rotation * Vector3.back, orbitDistance);
            CurrentArm = orbitDistance;
            var downDistance = BuildFocus.HasValue ? BuildDistance : topDownDistance;
            cameraTransform.SetLocalPositionAndRotation(new Vector3(0f, 0f, -Mathf.Lerp(orbitDistance, downDistance, eased)), Quaternion.identity);
        }

        // Pan with Move, zoom with Zoom, and while CameraTilt is held tilt and turn with Look.
        private void UpdateBuildControls()
        {
            if (!BuildControls) return;
            var move = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);
            if (move.sqrMagnitude > 0.0001f)
            {
                var pan = Quaternion.Euler(0f, _buildYaw, 0f) * new Vector3(move.x, 0f, move.y);
                BuildFocus += pan * (buildPanSpeed * Mathf.Max(0.5f, BuildDistance / 20f) * Time.unscaledDeltaTime);
            }
            var scroll = zoomAction.action.ReadValue<Vector2>().y;
            if (!Mathf.Approximately(scroll, 0f))
                BuildDistance = Mathf.Clamp(BuildDistance * (scroll > 0f ? 0.88f : 1f / 0.88f), MinBuildDistance, MaxBuildDistance);
            if (tiltAction.action.IsPressed())
            {
                var delta = lookAction.action.ReadValue<Vector2>();
                _buildYaw = Mathf.Repeat(_buildYaw + delta.x * orbitDegreesPerUnit, 360f);
                _buildPitch = Mathf.Clamp(_buildPitch - delta.y * orbitDegreesPerUnit, MinBuildPitch, 90f);
            }
        }

        // How far along direction the camera can stand from pivot: in front of the first wall, and under the ceiling indoors.
        // Pulling in is immediate; easing back out takes armReturnSeconds.
        private float Collide(Vector3 pivot, Vector3 direction, float wanted)
        {
            var allowed = Reach(pivot, direction, wanted, MinimumArm);
            if (_ceiling.HasValue && direction.y > 0.001f)
                allowed = Mathf.Min(allowed, Mathf.Max(MinimumArm, (_ceiling.Value - CeilingGap - pivot.y) / direction.y));
            _arm = allowed < _arm || armReturnSeconds <= 0f ? allowed : Mathf.MoveTowards(_arm, allowed, wanted * Time.deltaTime / armReturnSeconds);
            return Mathf.Min(_arm, wanted);
        }

        private float Reach(Vector3 from, Vector3 direction, float wanted, float minimum)
        {
            var count = Physics.SphereCastNonAlloc(from, CastRadius, direction, _hits, wanted, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var reach = wanted;
            for (var index = 0; index < count; index++)
            {
                var hit = _hits[index];
                // A collider the cast starts inside reports distance 0 at the origin; it is beside the pivot, not between.
                if (!Blocks(hit.collider) || (hit.distance <= 0f && hit.point == Vector3.zero)) continue;
                reach = Mathf.Min(reach, hit.distance);
            }
            return Mathf.Clamp(reach, Mathf.Min(minimum, wanted), wanted);
        }

        private bool Blocks(Collider collider) =>
            collider != null && collider is not CharacterController && !collider.transform.IsChildOf(target)
            && collider.bounds.size.y >= WallHeight
            && collider.GetComponentInParent<Equipment.EquipmentVisual>() == null
            && collider.GetComponentInParent<Employees.SiteLocationMarker>() == null
            && collider.GetComponentInParent<Employees.EmployeeWorker>() == null;
    }
}
