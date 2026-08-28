using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DefaultNamespace
{
    public class NAIPlayerMove : NetworkBehaviour
    {
        public int speed;
        
        private static readonly int IsMoving = Animator.StringToHash("IsMoving");
        private InputAction _move;
        private Animator _animator;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            enabled = false; // Disable this script for everybody by default, re-enable for local player
        }
        
        public override void OnStartClient()
        {
            if (!IsOwner) return;

            enabled = true;
            (_move = InputSystem.actions["Move"]).Enable();
            Camera.main!.transform.SetParent(transform);
        }

        public override void OnStopClient() => _move?.Disable();

        private void Update()
        {
            var moveInput = _move.ReadValue<Vector2>();
            if (moveInput == Vector2.zero)
            {
                _animator.SetBool(IsMoving, false);
                return;
            }
            
            _animator.SetBool(IsMoving, true);
            var moveDirection = new Vector3(moveInput.x, moveInput.y, 0f).normalized;
            transform.position += moveDirection * (Time.deltaTime * speed);
        }
    }
}