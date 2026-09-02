// Moves the owning player through Rigidbody2D physics while keeping animation state in sync.
using FishNet.Object;
using GameKit.Dependencies.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class NAIPlayerMove : NetworkBehaviour
    {
        public int speed;
        
        private static readonly int IsMoving = Animator.StringToHash("IsMoving");
        private InputAction _move;
        private Animator _animator;
        private Rigidbody2D _body;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _body = GetComponent<Rigidbody2D>();
            enabled = false; // Disable this script for everybody by default, re-enable for local player
        }
        
        public override void OnStartClient()
        {
            if (!IsOwner) return;

            enabled = true;
            (_move = InputSystem.actions["Move"]).Enable();
            var cam = Camera.main!;
            cam.transform.SetParent(transform);
            cam.transform.SetPosition(false,
                new Vector3(transform.position.x, transform.position.y, cam.transform.position.z));
        }

        public override void OnStopClient()
        {
            _move?.Disable();
            // Unity destroys camera before player, so we need to check if it exists before trying to unparent it
            Camera.main?.transform.SetParent(null);
        }

        private void FixedUpdate()
        {
            var moveInput = _move.ReadValue<Vector2>();

            if (moveInput == Vector2.zero)
            {
                _animator.Play("idle");
                _body.linearVelocity = Vector2.zero;
                return;
            }

            var direction = Mathf.RoundToInt(
                Mathf.Atan2(moveInput.x, moveInput.y) * Mathf.Rad2Deg / 90f
            ) % 4;

            if (direction < 0)
                direction += 4;

            SetAnimation(direction);

            _body.linearVelocity = moveInput.normalized * speed;
        }

        private void SetAnimation(int direction)
        {
            switch (direction)
            {
                case 0:
                    _animator.Play("north");
                    break;
                case 1:
                    _animator.Play("east");
                    break;
                case 2:
                    _animator.Play("south");
                    break;
                case 3:
                    _animator.Play("west");
                    break;
            }
        }
    }
}
