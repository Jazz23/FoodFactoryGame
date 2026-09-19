// Owns opt-in 3D player traversal while leaving logical state and transition authority external.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class Factory3DTraversalController : MonoBehaviour
{
    [SerializeField] private bool startWith3DOwnership;
    [SerializeField, Min(0.01f)] private float moveSpeed = 4f;
    [SerializeField, Min(0.1f)] private float capsuleHeight = 1.8f;
    [SerializeField, Min(0.01f)] private float capsuleRadius = 0.3f;
    [SerializeField, Min(0f)] private float gravity = 25f;
    [SerializeField, Min(0f)] private float groundingVelocity = 2f;

    private CharacterController characterController = null!;
    private CapsuleCollider capsuleCollider = null!;
    private Movement legacyMovement = null!;
    private Rigidbody2D legacyBody = null!;
    private PlayerSceneTransition transition = null!;
    private InputAction moveAction = null!;
    private InputAction interactAction = null!;
    private InputAction cancelAction = null!;
    private FactorySpatialAdapter spatialAdapter = null!;
    private uint buildingInstanceId;
    private int floorIndex;
    private float storyHeight = 3f;
    private float verticalVelocity;
    private bool inputOwnershipEnabled;
    private bool uiFocusOverride;
    private bool hasUiFocusOverride;
    private bool grounded;
    private bool screenAlignedMovement;

    public event Action<Vector3> InteractionRequested;

    public bool OwnsPlayer => inputOwnershipEnabled;
    public bool IsGrounded => (characterController is not null
        && characterController
        && characterController.isGrounded) || grounded;
    public bool IsUiInputBlocking => IsPointerInputBlocked();
    public float MoveSpeed => moveSpeed;
    public bool ScreenAlignedMovementEnabled => screenAlignedMovement;
    public Vector3 FootAnchor => transform.position;
    public uint BuildingInstanceId => buildingInstanceId;
    public int FloorIndex => floorIndex;
    public float FloorElevation => BuildingCoordinates.GetFloorElevation(floorIndex, storyHeight);

    public bool TryGetLogicalFootAnchor(out FactoryLogicalLocation location)
    {
        if (spatialAdapter is null)
        {
            location = default;
            return false;
        }

        location = spatialAdapter.WorldToLogical3D(
            FootAnchor,
            buildingInstanceId,
            floorIndex);
        return true;
    }

    public void ConfigureSpatialContext(
        FactorySpatialAdapter adapter,
        uint newBuildingInstanceId,
        int newFloorIndex,
        float newStoryHeight)
    {
        spatialAdapter = adapter;
        buildingInstanceId = newBuildingInstanceId;
        floorIndex = Mathf.Max(0, newFloorIndex);
        storyHeight = float.IsFinite(newStoryHeight) ? Mathf.Max(0f, newStoryHeight) : 3f;
    }

    public void SetMoveSpeed(float newMoveSpeed)
    {
        moveSpeed = Mathf.Max(0.01f, newMoveSpeed);
    }

    public void SetScreenAlignedMovement(bool enabled)
    {
        screenAlignedMovement = enabled;
    }

    public void SetPointerFocusOverride(bool? pointerIsOverUi)
    {
        hasUiFocusOverride = pointerIsOverUi.HasValue;
        uiFocusOverride = pointerIsOverUi.GetValueOrDefault();
    }

    public void Set3DOwnership(bool ownsPlayer)
    {
        EnsureRuntimeComponents();
        inputOwnershipEnabled = ownsPlayer;
        if (ownsPlayer)
        {
            if (characterController is not null && characterController)
            {
                characterController.enabled = true;
            }
            else
            {
                capsuleCollider.enabled = true;
            }
            if (legacyMovement is not null && legacyMovement)
            {
                legacyMovement.enabled = false;
            }

            if (legacyBody is not null && legacyBody)
            {
                legacyBody.simulated = false;
            }

            transition?.Set3DTraversalOwnership(true);
            EnableInput();
            return;
        }

        DisableInput();
        if (characterController is not null && characterController)
        {
            characterController.enabled = false;
        }
        else
        {
            capsuleCollider.enabled = false;
        }
        if (legacyBody is not null && legacyBody)
        {
            legacyBody.simulated = true;
        }

        if (legacyMovement is not null && legacyMovement)
        {
            legacyMovement.enabled = true;
        }

        transition?.Set3DTraversalOwnership(false);
    }

    public void Teleport(Vector3 footAnchor, int newFloorIndex)
    {
        EnsureRuntimeComponents();
        floorIndex = Mathf.Max(0, newFloorIndex);
        verticalVelocity = -groundingVelocity;
        grounded = true;
        if (characterController is not null && characterController)
        {
            characterController.enabled = false;
        }
        else
        {
            capsuleCollider.enabled = false;
        }
        transform.position = footAnchor;
        if (characterController is not null && characterController)
        {
            characterController.enabled = inputOwnershipEnabled;
        }
        else
        {
            capsuleCollider.enabled = inputOwnershipEnabled;
        }
    }

    public void StepForTest(Vector2 input, float deltaTime)
    {
        if (CanAcceptGameplayInput())
        {
            Move(input, deltaTime);
        }
    }

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        legacyMovement = GetComponent<Movement>();
        legacyBody = GetComponent<Rigidbody2D>();
        transition = GetComponent<PlayerSceneTransition>();
        moveAction = (InputSystem.actions.FindAction("Player/Move", false)
            ?? InputSystem.actions.FindAction("Move", false))?.Clone();
        interactAction = (InputSystem.actions.FindAction("Player/Interact", false)
            ?? InputSystem.actions.FindAction("Interact", false))?.Clone();
        cancelAction = InputSystem.actions.FindAction("UI/Cancel", false)?.Clone();
        if (interactAction is not null)
        {
            interactAction.performed += OnInteract;
        }

        if (cancelAction is not null)
        {
            cancelAction.performed += OnCancel;
        }
    }

    private void Start()
    {
        if (startWith3DOwnership
            && (transition is null || transition.IsOwner))
        {
            Set3DOwnership(true);
        }
    }

    private void Update()
    {
        if (!inputOwnershipEnabled || IsUiInputBlocking)
        {
            return;
        }

        var input = moveAction is null ? Vector2.zero : moveAction.ReadValue<Vector2>();
        if (transition is not null && transition.IsElevatorPromptOpen)
        {
            transition.Request3DElevatorFloor(input);
            return;
        }

        if (transition is not null && transition.IsTransitioning)
        {
            return;
        }

        if (transition is not null && transition.IsFactoryBuildPlacementActive)
        {
            return;
        }

        Move(input, Time.deltaTime);
    }

    private void Move(Vector2 input, float deltaTime)
    {
        EnsureRuntimeComponents();
        var planarInput = Vector2.ClampMagnitude(input, 1f);
        if (characterController is not null && characterController)
        {
            MoveWithCharacterController(planarInput, deltaTime);
            return;
        }

        if (grounded)
        {
            verticalVelocity = -groundingVelocity;
        }
        else
        {
            verticalVelocity -= gravity * deltaTime;
        }

        Physics.SyncTransforms();
        MovePlanar(GetPlanarMovement(planarInput) * moveSpeed * deltaTime);
        Physics.SyncTransforms();
        MoveVertical(verticalVelocity * deltaTime);
    }

    private void MoveWithCharacterController(Vector2 planarInput, float deltaTime)
    {
        var movement = GetPlanarMovement(planarInput) * moveSpeed;
        if (characterController.isGrounded)
        {
            verticalVelocity = -groundingVelocity;
        }
        else
        {
            verticalVelocity -= gravity * deltaTime;
        }

        movement.y = verticalVelocity;
        var collisionFlags = characterController.Move(movement * deltaTime);
        grounded = (collisionFlags & CollisionFlags.Below) != 0;
    }

    private static Vector3 GetScreenAlignedPlanarMovement(Vector2 planarInput)
    {
        var cameraRotation = Factory3DPresentationBridge.GetDimetricCameraRotation();
        var cameraGroundRight = Vector3.ProjectOnPlane(
            cameraRotation * Vector3.right,
            Vector3.up).normalized;
        var cameraGroundForward = Vector3.ProjectOnPlane(
            cameraRotation * Vector3.forward,
            Vector3.up).normalized;
        return cameraGroundRight * planarInput.x
            + cameraGroundForward * planarInput.y;
    }

    private Vector3 GetPlanarMovement(Vector2 planarInput)
    {
        return screenAlignedMovement
            ? GetScreenAlignedPlanarMovement(planarInput)
            : new Vector3(planarInput.x, 0f, planarInput.y);
    }

    private void OnInteract(InputAction.CallbackContext _)
    {
        if (!CanAcceptInteractionInput())
        {
            return;
        }

        InteractionRequested?.Invoke(FootAnchor);
        if (transition is not null)
        {
            transition.Request3DInteraction(this);
        }
    }

    private void OnCancel(InputAction.CallbackContext _)
    {
        if (!inputOwnershipEnabled || transition is null)
        {
            return;
        }

        transition.Cancel3DInteraction();
    }

    private bool CanAcceptGameplayInput()
    {
        return inputOwnershipEnabled
            && !IsUiInputBlocking
            && (transition is null || (!transition.IsTransitioning
                && !transition.IsFactoryBuildPlacementActive));
    }

    private bool CanAcceptInteractionInput()
    {
        return inputOwnershipEnabled
            && !IsUiInputBlocking
            && (transition is null || !transition.IsTransitioning);
    }

    private bool IsPointerInputBlocked()
    {
        if (hasUiFocusOverride)
        {
            return uiFocusOverride;
        }

        if (TestToolsShell.IsTextInputFocused)
        {
            return true;
        }

        return EventSystem.current is not null
            && EventSystem.current.IsPointerOverGameObject();
    }

    private void EnsureRuntimeComponents()
    {
        if (characterController is null || !characterController)
        {
            characterController = GetComponent<CharacterController>();
        }

        if (characterController is not null && characterController)
        {
            characterController.height = Mathf.Max(capsuleHeight, capsuleRadius * 2f);
            characterController.radius = Mathf.Min(
                capsuleRadius,
                characterController.height * 0.5f - 0.001f);
            characterController.center = Vector3.up * (characterController.height * 0.5f);
            characterController.stepOffset = Mathf.Min(0.3f, characterController.height * 0.25f);
            characterController.slopeLimit = 0f;
            return;
        }

        if (capsuleCollider is null || !capsuleCollider)
        {
            capsuleCollider = GetComponent<CapsuleCollider>();
            if (capsuleCollider is null || !capsuleCollider)
            {
                capsuleCollider = GetComponentInChildren<CapsuleCollider>(true);
            }
            if (capsuleCollider is null || !capsuleCollider)
            {
                var colliderObject = new GameObject("Factory 3D Traversal Capsule");
                colliderObject.transform.SetParent(transform, false);
                capsuleCollider = colliderObject.AddComponent<CapsuleCollider>();
            }
        }

        capsuleCollider.height = Mathf.Max(capsuleHeight, capsuleRadius * 2f);
        capsuleCollider.radius = Mathf.Min(
            capsuleRadius,
            capsuleCollider.height * 0.5f - 0.001f);
        capsuleCollider.direction = 1;
        capsuleCollider.center = Vector3.up * (capsuleCollider.height * 0.5f);
        capsuleCollider.isTrigger = false;
    }

    private void MovePlanar(Vector3 displacement)
    {
        var distance = new Vector2(displacement.x, displacement.z).magnitude;
        if (distance <= 0.000001f)
        {
            return;
        }

        var direction = new Vector3(displacement.x, 0f, displacement.z) / distance;
        var allowedDistance = distance;
        GetCapsuleEndpoints(transform.position, out var bottom, out var top);
        var physicsScene = GetPhysicsScene();
        if (physicsScene.CapsuleCast(
                bottom,
                top,
                capsuleCollider.radius,
                direction,
                out var hit,
                distance + CollisionSkin,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore))
        {
            allowedDistance = Mathf.Max(0f, hit.distance - CollisionSkin);
        }

        transform.position += direction * allowedDistance;
    }

    private void MoveVertical(float displacement)
    {
        var distance = Mathf.Abs(displacement);
        if (distance <= 0.000001f)
        {
            return;
        }

        var direction = displacement < 0f ? Vector3.down : Vector3.up;
        var allowedDistance = distance;
        GetCapsuleEndpoints(transform.position, out var bottom, out var top);
        var physicsScene = GetPhysicsScene();
        var hitSomething = physicsScene.CapsuleCast(
            bottom,
            top,
            capsuleCollider.radius,
            direction,
            out var hit,
            distance + CollisionSkin,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        if (hitSomething)
        {
            allowedDistance = Mathf.Max(0f, hit.distance - CollisionSkin);
        }

        transform.position += direction * allowedDistance;
        grounded = displacement < 0f && hitSomething;
        if (grounded)
        {
            verticalVelocity = -groundingVelocity;
        }
    }

    private PhysicsScene GetPhysicsScene()
    {
        return gameObject.scene.IsValid()
            ? gameObject.scene.GetPhysicsScene()
            : Physics.defaultPhysicsScene;
    }

    private void GetCapsuleEndpoints(
        Vector3 position,
        out Vector3 bottom,
        out Vector3 top)
    {
        var center = capsuleCollider.transform.TransformPoint(capsuleCollider.center);
        var halfSegment = Mathf.Max(
            0f,
            capsuleCollider.height * 0.5f - capsuleCollider.radius);
        bottom = center - capsuleCollider.transform.up * halfSegment;
        top = center + capsuleCollider.transform.up * halfSegment;
    }

    private const float CollisionSkin = 0.01f;

    private void EnableInput()
    {
        moveAction?.Enable();
        interactAction?.Enable();
        cancelAction?.Enable();
    }

    private void DisableInput()
    {
        moveAction?.Disable();
        interactAction?.Disable();
        cancelAction?.Disable();
    }

    private void OnDestroy()
    {
        if (interactAction is not null)
        {
            interactAction.performed -= OnInteract;
            interactAction.Dispose();
        }

        if (cancelAction is not null)
        {
            cancelAction.performed -= OnCancel;
            cancelAction.Dispose();
        }

        moveAction?.Dispose();
    }
}
