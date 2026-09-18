// Provides grounded Input System movement and interaction for the disposable 3D prototype player.
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class Factory3DPrototypePlayer : MonoBehaviour
{
    private CharacterController characterController;
    private InputAction moveAction;
    private InputAction interactAction;
    private float moveSpeed;
    private float verticalVelocity;
    private Factory3DPrototypeController prototype;
    private int floorIndex;

    public int FloorIndex => floorIndex;
    public bool IsGrounded => characterController.isGrounded;
    public float MoveSpeed => moveSpeed;
    public string InteractionPrompt => prototype.GetInteractionPrompt(this);

    public void Configure(float newMoveSpeed, int newFloorIndex, Factory3DPrototypeController newPrototype)
    {
        moveSpeed = newMoveSpeed;
        floorIndex = newFloorIndex;
        prototype = newPrototype;
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        moveAction = InputSystem.actions.FindAction("Player/Move", true).Clone();
        interactAction = InputSystem.actions.FindAction("Player/Interact", true).Clone();
        interactAction.performed += OnInteract;
        moveAction.Enable();
        interactAction.Enable();
    }

    private void OnDestroy()
    {
        if (interactAction is not null)
        {
            interactAction.performed -= OnInteract;
            interactAction.Disable();
            interactAction.Dispose();
        }

        if (moveAction is not null)
        {
            moveAction.Disable();
            moveAction.Dispose();
        }
    }

    private void Update()
    {
        var input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        var movement = new Vector3(input.x, 0f, input.y) * moveSpeed;
        if (characterController.isGrounded)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        movement.y = verticalVelocity;
        characterController.Move(movement * Time.deltaTime);
        prototype.ConstrainPlayerToFloor(this);
    }

    private void OnInteract(InputAction.CallbackContext context)
    {
        prototype.InteractForTest();
    }

    public void Teleport(Vector3 position, int newFloorIndex)
    {
        floorIndex = newFloorIndex;
        verticalVelocity = -2f;
        characterController.enabled = false;
        transform.position = position;
        characterController.enabled = true;
    }
}
