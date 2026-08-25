using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D))]
public class Movement : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 4f;

    private InputAction _move;
    private Rigidbody2D _body;
    private bool _isTransitioning;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        enabled = false;
    }

    public override void OnStartNetwork()
    {
        ApplyAuthorityState();
    }

    public override void OnStartClient()
    {
        ApplyAuthorityState();
        if (!IsOwner)
        {
            return;
        }

        (_move = InputSystem.actions["Move"]).Enable();
    }

    public override void OnOwnershipClient(NetworkConnection prevOwner)
    {
        ApplyAuthorityState();
    }

    public override void OnStopNetwork()
    {
        if (_move != null)
        {
            _move.Disable();
        }

        enabled = false;
    }

    private void ApplyAuthorityState()
    {
        bool canSimulate = IsOwner;
        enabled = canSimulate;

        if (_body != null)
        {
            _body.simulated = canSimulate;
        }

        if (_move == null)
        {
            return;
        }

        if (canSimulate)
        {
            _move.Enable();
            return;
        }

        _move.Disable();
    }

    private void FixedUpdate()
    {
        if (_move == null || _isTransitioning)
        {
            return;
        }

        if (!SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            SceneGrid.LogMissingGrid(gameObject.scene, this);
            return;
        }

        var movement = _move.ReadValue<Vector2>();
        movement.y *= grid.VerticalMovementMultiplier;

        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        _body.MovePosition(_body.position + movement * (moveSpeed * Time.fixedDeltaTime));
    }

    public void SetTransitioning(bool value)
    {
        _isTransitioning = value;
    }
}
