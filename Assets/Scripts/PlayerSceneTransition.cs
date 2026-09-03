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
    private Movement movement = null!;
    private NetworkTransform networkTransform = null!;
    private Rigidbody2D body = null!;
    private bool isTransitioning;

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
        interact.Enable();
        interact.performed += InteractPerformed;
    }

    public override void OnStopClient()
    {
        if (!IsOwner)
        {
            return;
        }

        interact.performed -= InteractPerformed;
        interact.Disable();

        if (LocalOwner == this)
        {
            LocalOwner = null!;
        }
    }

    private void InteractPerformed(InputAction.CallbackContext _)
    {
        if (isTransitioning || !ScenePortal.TryGetClosest(gameObject.scene, transform.position, out var portal))
        {
            return;
        }

        SetTransitionState(true);
        RequestTransitionServerRpc(portal.BuildingInstanceId);
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
        Vector2[] exteriorArrivalLogicalPositions)
    {
        TargetTeleport(
            connection,
            position,
            buildingSize,
            arrivalLogicalPosition,
            interiorExitLogicalPositions,
            exteriorArrivalLogicalPositions);
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
        Vector2[] exteriorArrivalLogicalPositions)
    {
        if (!InsideFactoryController.TryConfigureForScene(
                gameObject.scene,
                buildingSize,
                arrivalLogicalPosition,
                interiorExitLogicalPositions,
                exteriorArrivalLogicalPositions))
        {
            IndoorGrid.TryConfigureForScene(gameObject.scene, buildingSize);
        }

        body.position = position;
        transform.SetPositionAndRotation(position, Quaternion.identity);
        networkTransform.Teleport();
        SetTransitionState(false);
    }

    private void SetTransitionState(bool value)
    {
        isTransitioning = value;
        movement.SetTransitioning(value);
    }
}
