// Owns local player movement and pauses safely while FishNet changes the player's scene.
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(SpriteRenderer))]
public class Movement : NetworkBehaviour
{
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Min(0f)] private float walkFramesPerSecond = 10f;
    [SerializeField] private Sprite[] walkSprites = null!;

    private InputAction _move;
    private Rigidbody2D _body;
    private SpriteRenderer _spriteRenderer;
    private bool _isTransitioning;

    private void Awake()
    {
        _body = GetComponent<Rigidbody2D>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
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
            return;
        }

        var movement = _move.ReadValue<Vector2>();
        movement.y *= grid.VerticalMovementMultiplier;

        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        _body.MovePosition(_body.position + movement * (moveSpeed * Time.fixedDeltaTime));
        UpdateWalkingSprite(movement);
    }

    public void SetTransitioning(bool value)
    {
        _isTransitioning = value;
    }

    private void UpdateWalkingSprite(Vector2 movement)
    {
        if (movement == Vector2.zero)
        {
            return;
        }

        var direction = Mathf.RoundToInt(
            Mathf.Atan2(movement.x, movement.y) * Mathf.Rad2Deg / 45f);
        var directionStart = direction switch
        {
            0 => 0,
            -1 => 8,
            -2 or 6 => 16,
            -3 or 5 => 24,
            4 or -4 => 32,
            1 => 40,
            2 => 48,
            3 => 56,
            _ => 0,
        };
        var frame = Mathf.FloorToInt(Time.time * walkFramesPerSecond) % 8;
        _spriteRenderer.sprite = walkSprites[directionStart + frame];
    }
}
