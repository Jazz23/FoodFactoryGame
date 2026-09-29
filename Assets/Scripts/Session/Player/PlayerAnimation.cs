// Presentation only: drives the character Animator from how the avatar actually moves, so the owner's copy and every
// remote copy (posed by NetworkTransform) animate alike without replicating animator state. A position jump larger
// than teleportDistance in one frame, such as an elevator ride, counts as a teleport rather than motion.
using UnityEngine;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAnimation : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private static readonly int GroundedId = Animator.StringToHash("Grounded");
        private static readonly int VerticalSpeedId = Animator.StringToHash("VerticalSpeed");

        [SerializeField] private Animator animator;
        // How far below the feet (metres) ground still counts as underfoot, so steps and slopes do not read as falling.
        [SerializeField] private float groundProbe = 0.25f;
        [SerializeField] private LayerMask groundLayers = ~0;
        [SerializeField] private float teleportDistance = 1.5f;
        // Velocity smoothing rate (1/s); remote poses arrive in network ticks.
        [SerializeField] private float smoothing = 12f;
        [SerializeField] private float speedDampSeconds = 0.1f;

        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private bool _hasLastPosition;

        private void OnEnable() => _hasLastPosition = false;

        private void LateUpdate()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            var position = transform.position;
            if (_hasLastPosition && (position - _lastPosition).sqrMagnitude < teleportDistance * teleportDistance)
                _velocity = Vector3.Lerp(_velocity, (position - _lastPosition) / deltaTime, 1f - Mathf.Exp(-smoothing * deltaTime));
            else
                _velocity = Vector3.zero;
            _lastPosition = position;
            _hasLastPosition = true;

            // The ray starts inside the avatar's own controller capsule, which a raycast does not report.
            var grounded = Physics.Raycast(position + Vector3.up * 0.1f, Vector3.down, 0.1f + groundProbe, groundLayers,
                QueryTriggerInteraction.Ignore);
            animator.SetFloat(SpeedId, new Vector2(_velocity.x, _velocity.z).magnitude, speedDampSeconds, deltaTime);
            animator.SetBool(GroundedId, grounded);
            animator.SetFloat(VerticalSpeedId, _velocity.y);
        }
    }
}
