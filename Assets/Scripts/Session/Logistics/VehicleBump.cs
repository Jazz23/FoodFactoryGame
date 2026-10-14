// Presentation only, on the local player's own avatar: when a drawn vehicle's body (VehicleBody) overlaps the player (it drove
// into them, or they stand where it moved), the player is moved out of the box the shortest way and shoved on with about the
// vehicle's speed, easing off. A moving vehicle that meets the player with its front or back knocks them to its nearer side
// rather than carrying them along ahead of it. Every move goes through the CharacterController, so walls still stop it.
// Remote avatars take the pose their owners send, so each client bumps only its own player; no gameplay rule reads it.
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    // After VehicleBody (-50) has this frame's vehicle velocities, and after every presenter moved its vehicles in Update.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class VehicleBump : MonoBehaviour
    {
        // Clearance left between the player and the box after being moved out, in metres.
        private const float Skin = 0.05f;
        // Shove speed: the vehicle's speed plus a little, within these bounds (metres per second), fading at this rate per second.
        private const float MinShove = 3f;
        private const float MaxShove = 9f;
        private const float ShoveFade = 5f;
        // A vehicle at least this fast (metres per second) knocks the player sideways instead of pushing them ahead of it.
        private const float Moving = 1.5f;
        // Penetration (metres) below which the player only touches a box, and vehicle speed below which it stands still.
        private const float Overlap = 0.02f;
        private const float Still = 0.3f;
        // Vehicles further than this from the player are not tested, in metres.
        private const float Reach = 20f;

        private CharacterController _controller;
        private Vector3 _shove;

        // Binds the local player's controller; called once by the owning avatar.
        public void Bind(CharacterController controller) => _controller = controller;

        private void LateUpdate()
        {
            var dt = Time.deltaTime;
            if (dt <= 0f || _controller == null || !_controller.enabled) return;
            var radius = _controller.radius;
            var centre = _controller.transform.TransformPoint(_controller.center);
            var halfHeight = Mathf.Max(_controller.height * 0.5f, radius);
            var correction = Vector3.zero;
            foreach (var body in VehicleBody.All)
            {
                if (body == null || body.Box == null || !body.Box.enabled) continue;
                var box = body.Box;
                var t = box.transform;
                if ((t.position - centre).sqrMagnitude > Reach * Reach) continue;
                var local = t.InverseTransformPoint(centre) - box.center;
                var half = box.size * 0.5f;
                if (local.y + halfHeight < -half.y || local.y - halfHeight > half.y) continue;
                var dx = half.x + radius - Mathf.Abs(local.x);
                var dz = half.z + radius - Mathf.Abs(local.z);
                // The controller's own contact offset keeps it a hair outside a box it walks into; only real overlap counts.
                if (dx <= Overlap || dz <= Overlap) continue;
                var sideways = dx <= dz;
                var outLocal = sideways ? new Vector3(Mathf.Sign(local.x) * (dx + Skin), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(local.z) * (dz + Skin));
                var outWorld = t.TransformVector(outLocal);
                outWorld.y = 0f;
                correction += outWorld;
                var velocity = body.Velocity;
                velocity.y = 0f;
                var direction = outWorld.normalized;
                if (!sideways && velocity.magnitude >= Moving)
                {
                    // Knocked off the bumper toward the vehicle's nearer side.
                    var side = t.right * Mathf.Sign(local.x);
                    side.y = 0f;
                    direction = (side.normalized + direction * 0.5f).normalized;
                }
                // A standing vehicle only moves the player out of it; a moving one shoves.
                if (velocity.magnitude < Still) continue;
                var speed = Mathf.Clamp(velocity.magnitude + 1f, MinShove, MaxShove);
                if (Vector3.Dot(_shove, direction) < speed) _shove = direction * speed;
            }
            _shove *= Mathf.Exp(-ShoveFade * dt);
            if (_shove.sqrMagnitude < 0.01f) _shove = Vector3.zero;
            var move = correction + _shove * dt;
            if (move.sqrMagnitude < 1e-8f) return;
            // A little down keeps the controller on the ground (isGrounded comes from the last Move).
            _controller.Move(move + Vector3.down * (2f * dt));
        }
    }
}
