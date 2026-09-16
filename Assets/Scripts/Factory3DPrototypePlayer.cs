// Provides Input System movement for the reversible fixed-camera 3D prototype player.
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class Factory3DPrototypePlayer : MonoBehaviour
{
    private CharacterController characterController;
    private InputAction moveAction;
    private float moveSpeed;
    private Factory3DPrototypeController prototype;
    private int floorIndex;

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
        moveAction.Enable();
    }

    private void OnDestroy()
    {
        if (moveAction is not null)
        {
            moveAction.Disable();
            moveAction.Dispose();
        }
    }

    private void Update()
    {
        var input = moveAction.ReadValue<Vector2>();
        var movement = new Vector3(input.x, 0f, input.y) * moveSpeed;
        characterController.Move(movement * Time.deltaTime);
    }

    public void Teleport(Vector3 position, int newFloorIndex)
    {
        floorIndex = newFloorIndex;
        characterController.enabled = false;
        transform.position = position;
        characterController.enabled = true;
    }
}
