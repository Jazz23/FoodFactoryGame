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

        private void Update()
        {
        }

        private void FixedUpdate()
        {
            var moveInput = _move.ReadValue<Vector2>();
            if (moveInput == Vector2.zero)
            {
                _animator.SetBool(IsMoving, false);
                _body.linearVelocity = Vector2.zero;
                return;
            }
            
            _animator.SetBool(IsMoving, true);
            _body.linearVelocity = moveInput.normalized * speed;
        }
    }
}
