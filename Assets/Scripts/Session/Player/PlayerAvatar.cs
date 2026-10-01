// Networked player presence. PROTOTYPE authority: the owner moves it and NetworkTransform replicates the pose;
// position is presentation only and no server gameplay rule may trust it yet.
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class PlayerAvatar : NetworkBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] private CharacterController controller;
        [SerializeField] private OrbitCameraRig cameraRig;
        [SerializeField] private Renderer body;
        // The body submesh that carries the per-player colour (the shirt).
        [SerializeField] private int tintMaterialIndex;
        [SerializeField] private InputActionReference moveAction;
        [SerializeField] private InputActionReference jumpAction;
        [SerializeField] private InputActionReference sprintAction;
        [SerializeField] private float speed = 4f;
        [SerializeField] private float sprintSpeed = 7f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float turnDegreesPerSecond = 720f;
        [SerializeField] private float gravity = -20f;
        // How far below its feet (plus 1 m above) the avatar looks for ground before it starts moving.
        private const float GroundSearch = 4f;

        private readonly SyncVar<string> _displayName = new();
        private float _verticalSpeed;
        // False until ground has been found under the avatar after spawning: the map and site shells are built after the
        // avatar spawns, and a saved position away from the dev floor has nothing under it until then (decision 0031).
        private bool _groundFound;

        public string DisplayName => _displayName.Value;
        public OrbitCameraRig CameraRig => cameraRig;

        private void Awake() => _displayName.OnChange += (_, next, _) => Tint(next);

        [Server]
        public void SetDisplayName(string displayName) => _displayName.Value = displayName;

        public override void OnStartClient()
        {
            // Remote copies and the server's copies never own a camera, listener, or input.
            cameraRig.gameObject.SetActive(IsOwner);
            if (IsOwner)
            {
                moveAction.action.Enable();
                jumpAction.action.Enable();
                sprintAction.action.Enable();
            }
            Tint(_displayName.Value);
        }

        public override void OnStopClient()
        {
            if (IsOwner)
            {
                moveAction.action.Disable();
                jumpAction.action.Disable();
                sprintAction.action.Disable();
            }
            cameraRig.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (!_groundFound)
            {
                if (!Physics.Raycast(transform.position + Vector3.up, Vector3.down, GroundSearch, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return;
                _groundFound = true;
            }
            var input = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);
            var direction = Quaternion.Euler(0f, cameraRig.Yaw, 0f) * new Vector3(input.x, 0f, input.y);
            if (controller.isGrounded)
                _verticalSpeed = jumpAction.action.WasPressedThisFrame() ? Mathf.Sqrt(-2f * gravity * jumpHeight) : -1f;
            else
                _verticalSpeed += gravity * Time.deltaTime;
            var moveSpeed = sprintAction.action.IsPressed() ? sprintSpeed : speed;
            var flags = controller.Move((direction * moveSpeed + Vector3.up * _verticalSpeed) * Time.deltaTime);
            // A jump that meets a ceiling starts falling instead of sticking to it.
            if ((flags & CollisionFlags.Above) != 0 && _verticalSpeed > 0f) _verticalSpeed = 0f;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction),
                    turnDegreesPerSecond * Time.deltaTime);
        }

        // Owner-only presentation move, such as an elevator ride between floors (decision 0020); the controller is paused so
        // it does not resolve the jump as a collision.
        public void Teleport(Vector3 position)
        {
            if (!IsOwner) return;
            controller.enabled = false;
            transform.position = position;
            controller.enabled = true;
            _verticalSpeed = 0f;
        }

        // Presentation only: hides this avatar's renderers on this client, such as a player on a storey the viewer hides.
        public void SetHidden(bool hidden)
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>())
                if (renderer.enabled == hidden) renderer.enabled = !hidden;
        }

        // Identification: the shirt takes a stable hue per display name.
        private void Tint(string displayName)
        {
            if (body == null || string.IsNullOrEmpty(displayName)) return;
            var hash = 17;
            foreach (var character in displayName) hash = hash * 31 + character;
            var block = new MaterialPropertyBlock();
            body.GetPropertyBlock(block, tintMaterialIndex);
            block.SetColor(BaseColor, Color.HSVToRGB((hash & 0xFFFF) / 65535f, 0.65f, 0.9f));
            body.SetPropertyBlock(block, tintMaterialIndex);
        }
    }
}
