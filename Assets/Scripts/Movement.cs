using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class Movement : NetworkBehaviour
{
    private InputAction _move;
    private Rigidbody2D _body;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
    }

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        enabled = true;
        (_move = InputSystem.actions["Move"]).Enable();
    }

    private void FixedUpdate()
    {
        Vector2 movement = _move.ReadValue<Vector2>();
        _body.MovePosition(_body.position + movement * Time.fixedDeltaTime);
    }
}
