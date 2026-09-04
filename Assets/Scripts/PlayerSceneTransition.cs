// Transitions a player through the exact portal they interacted with.
using FishNet.Component.Transforming;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Movement), typeof(NetworkTransform), typeof(Rigidbody2D))]
public sealed class PlayerSceneTransition : NetworkBehaviour
{
    private InputAction interact = null!;
    private InputAction move = null!;
    private InputAction cancel = null!;
    private Movement movement = null!;
    private NetworkTransform networkTransform = null!;
    private Rigidbody2D body = null!;
    private bool isTransitioning;
    private bool elevatorPromptOpen;
    private InsideFactoryElevator activeElevator = null!;

    public static PlayerSceneTransition LocalOwner = null!;
    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        movement = GetComponent<Movement>();
        networkTransform = GetComponent<NetworkTransform>();
        body = GetComponent<Rigidbody2D>();
    }

    public override void OnStartClient()
    {
        if (!IsOwner)
        {
            return;
        }

        LocalOwner = this;
        interact = InputSystem.actions["Interact"];
        move = InputSystem.actions["Move"];
        cancel = InputSystem.actions["UI/Cancel"];
        interact.Enable();
        move.Enable();
        cancel.Enable();
        interact.performed += InteractPerformed;
        move.performed += MovePerformed;
        cancel.performed += CancelPerformed;
    }

    public override void OnStopClient()
    {
        if (!IsOwner)
        {
            return;
        }

        interact.performed -= InteractPerformed;
        move.performed -= MovePerformed;
        cancel.performed -= CancelPerformed;
        interact.Disable();
        move.Disable();
        cancel.Disable();
        CloseElevatorPrompt();

        if (LocalOwner == this)
        {
            LocalOwner = null!;
        }
    }

    private void InteractPerformed(InputAction.CallbackContext _)
    {
        if (isTransitioning)
        {
            return;
        }

        if (elevatorPromptOpen)
        {
            CloseElevatorPrompt();
            return;
        }

        if (InsideFactoryElevator.TryGetForScene(gameObject.scene, out var elevator)
            && elevator.CanUse(transform.position)
            && elevator.CanOpenPrompt)
        {
            activeElevator = elevator;
            activeElevator.OpenPrompt();
            elevatorPromptOpen = true;
            movement.SetTransitioning(true);
            return;
        }

        if (!ScenePortal.TryGetClosest(gameObject.scene, transform.position, out var portal))
        {
            return;
        }

        SetTransitionState(true);
        RequestTransitionServerRpc(portal.BuildingInstanceId);
    }

    private void MovePerformed(InputAction.CallbackContext context)
    {
        if (!elevatorPromptOpen || isTransitioning)
        {
            return;
        }

        var movementInput = context.ReadValue<Vector2>();
        if (movementInput.y > 0.5f && activeElevator.CanGoUp)
        {
            RequestElevatorFloor(activeElevator.CurrentFloor + 1);
        }
        else if (movementInput.y < -0.5f && activeElevator.CanGoDown)
        {
            RequestElevatorFloor(activeElevator.CurrentFloor - 1);
        }
    }

    private void CancelPerformed(InputAction.CallbackContext _)
    {
        if (!isTransitioning && elevatorPromptOpen)
        {
            CloseElevatorPrompt();
        }
    }

    [ServerRpc]
    private void RequestTransitionServerRpc(uint buildingInstanceId)
    {
        var portalExists = buildingInstanceId == 0
            ? ScenePortal.TryGetClosest(gameObject.scene, transform.position, out var portal)
            : ScenePortal.TryGetBuilding(gameObject.scene, transform.position, buildingInstanceId, out portal);
        if (!portalExists)
        {
            TargetSetTransitionState(Owner, false);
            return;
        }

        if (!GameSceneManager.Instance.RequestTransition(NetworkObject, portal))
        {
            TargetSetTransitionState(Owner, false);
        }
    }

    private void RequestElevatorFloor(int targetFloorIndex)
    {
        if (!activeElevator.IsFloorAvailable(targetFloorIndex))
        {
            return;
        }

        CloseElevatorPrompt();
        SetTransitionState(true);
        RequestElevatorFloorServerRpc(targetFloorIndex);
    }

    [ServerRpc]
    private void RequestElevatorFloorServerRpc(int targetFloorIndex)
    {
        var elevatorExists = InsideFactoryElevator.TryGetForScene(
            gameObject.scene,
            out var elevator);
        if (!elevatorExists
            || !elevator.CanUse(transform.position)
            || !elevator.IsFloorAvailable(targetFloorIndex)
            || !GameSceneManager.Instance.RequestFloorTransition(NetworkObject, targetFloorIndex))
        {
            TargetSetTransitionState(Owner, false);
        }
    }

    public void ServerTeleport(Vector3 position)
    {
        body.position = position;
        transform.SetPositionAndRotation(position, Quaternion.identity);
    }

    public void CompleteTransition(
        NetworkConnection connection,
        Vector3 position,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        Vector2[] interiorExitLogicalPositions,
        Vector2[] exteriorArrivalLogicalPositions,
        GridEdgeDirection[] interiorExitDirections,
        uint buildingInstanceId,
        int storyCount,
        int floorIndex)
    {
        TargetTeleport(
            connection,
            position,
            buildingSize,
            arrivalLogicalPosition,
            interiorExitLogicalPositions,
            exteriorArrivalLogicalPositions,
            interiorExitDirections,
            buildingInstanceId,
            storyCount,
            floorIndex);
    }

    [TargetRpc]
    private void TargetSetTransitionState(NetworkConnection connection, bool value)
    {
        SetTransitionState(value);
    }

    [TargetRpc]
    private void TargetTeleport(
        NetworkConnection connection,
        Vector3 position,
        Vector2Int buildingSize,
        Vector2 arrivalLogicalPosition,
        Vector2[] interiorExitLogicalPositions,
        Vector2[] exteriorArrivalLogicalPositions,
        GridEdgeDirection[] interiorExitDirections,
        uint buildingInstanceId,
        int storyCount,
        int floorIndex)
    {
        if (!InsideFactoryController.TryConfigureForScene(
                gameObject.scene,
                buildingSize,
                arrivalLogicalPosition,
                interiorExitLogicalPositions,
                exteriorArrivalLogicalPositions,
                interiorExitDirections,
                buildingInstanceId,
                storyCount,
                floorIndex))
        {
            IndoorGrid.TryConfigureForScene(gameObject.scene, buildingSize);
        }

        body.position = position;
        transform.SetPositionAndRotation(position, Quaternion.identity);
        networkTransform.Teleport();
        CloseElevatorPrompt();
        SetTransitionState(false);
    }

    private void SetTransitionState(bool value)
    {
        isTransitioning = value;
        movement.SetTransitioning(value);
    }

    private void CloseElevatorPrompt()
    {
        if (activeElevator is not null && activeElevator)
        {
            activeElevator.ClosePrompt();
        }

        activeElevator = null!;
        elevatorPromptOpen = false;
        if (!isTransitioning)
        {
            movement.SetTransitioning(false);
        }
    }
}
