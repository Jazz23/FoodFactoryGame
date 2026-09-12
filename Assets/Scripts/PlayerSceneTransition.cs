// Transitions a player through the exact portal they interacted with.
using System;
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
    private uint lastCompletedTransitionSequence;
    private InsideFactoryElevator activeElevator = null!;
    private OutsideTestFloorDebugPanel debugPanel = null!;
    private FactoryBuildController factoryBuilder = null!;
    private FactoryTruckRoutePanel routePanel = null!;

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
        debugPanel = gameObject.AddComponent<OutsideTestFloorDebugPanel>();
        debugPanel.Initialize(this);
        factoryBuilder = gameObject.AddComponent<FactoryBuildController>();
        factoryBuilder.Initialize(this);
        routePanel = gameObject.AddComponent<FactoryTruckRoutePanel>();
        routePanel.Initialize(this);
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
        if (factoryBuilder is not null) Destroy(factoryBuilder);
        if (routePanel is not null) Destroy(routePanel);
        if (debugPanel is not null && debugPanel)
        {
            Destroy(debugPanel);
            debugPanel = null!;
        }

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

    public void ServerBeginTransition()
    {
        SetTransitionState(true);
        TargetSetTransitionState(Owner, true);
    }

    public bool TryGetCurrentOutsideTestFloor(
        out uint buildingInstanceId,
        out int floorIndex)
    {
        buildingInstanceId = 0;
        floorIndex = -1;
        if (!InsideFactoryController.TryGetForScene(
                gameObject.scene,
                out var controller)
            || controller.BuildingInstanceId == 0)
        {
            return false;
        }

        buildingInstanceId = controller.BuildingInstanceId;
        floorIndex = controller.CurrentFloor;
        return true;
    }

    public void RequestPlaceEquipment(string definitionId, Vector2 position)
    {
        if (IsOwner) RequestPlaceEquipmentServerRpc(definitionId, position);
    }

    public void RequestPlaceExteriorDock(string definitionId, Vector2 exteriorLogicalPosition)
    {
        if (IsOwner)
        {
            RequestPlaceExteriorDockServerRpc(definitionId, exteriorLogicalPosition);
        }
    }

    public void RequestRelocateCurrentFloorEntity(uint entityId, Vector2 position)
    {
        if (IsOwner)
        {
            RequestRelocateCurrentFloorEntityServerRpc(entityId, position);
        }
    }

    [ServerRpc]
    private void RequestRelocateCurrentFloorEntityServerRpc(uint entityId, Vector2 position)
    {
        var relocated = GameSceneManager.Instance.TryRelocateCurrentFloorEntity(
            this,
            entityId,
            position,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            relocated ? $"Recovered entity {entityId}." : error);
    }

    [ServerRpc]
    private void RequestPlaceEquipmentServerRpc(string definitionId, Vector2 position)
    {
        var placed = GameSceneManager.Instance.TryPlaceCurrentFloorEquipment(
            this, definitionId, position, out _, out var error);
        TargetReceiveMachineEditResult(Owner, placed ? "Equipment placed." : error);
    }

    [ServerRpc]
    private void RequestPlaceExteriorDockServerRpc(
        string definitionId,
        Vector2 exteriorLogicalPosition)
    {
        var placed = GameSceneManager.Instance.TryPlaceExteriorDock(
            this,
            definitionId,
            exteriorLogicalPosition,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            placed ? $"Placed dock {entityId}." : error);
    }

    public void RequestAddCurrentFloorMachine(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorMachineServerRpc(position);
        }
    }

    public void RequestAddCurrentFloorStorage(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorStorageServerRpc(position);
        }
    }

    public void RequestAddCurrentFloorProcessor(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorProcessorServerRpc(position);
        }
    }

    public void RequestAddCurrentFloorPackedStorage(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorPackedStorageServerRpc(position);
        }
    }

    public void RequestAddCurrentFloorSendingTerminal(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorSendingTerminalServerRpc(position);
        }
    }

    public void RequestAddCurrentFloorReceivingTerminal(Vector2 position)
    {
        if (IsOwner)
        {
            RequestAddCurrentFloorReceivingTerminalServerRpc(position);
        }
    }

    public void RequestRemoveCurrentFloorMachine(uint entityId)
    {
        if (IsOwner)
        {
            RequestRemoveCurrentFloorMachineServerRpc(entityId);
        }
    }

    public void RequestRemoveCurrentFloorEntity(uint entityId)
    {
        RequestRemoveCurrentFloorMachine(entityId);
    }

    public void RequestDrainCurrentFloorMachine(uint entityId)
    {
        if (IsOwner)
        {
            RequestDrainCurrentFloorMachineServerRpc(entityId);
        }
    }

    public void RequestDrainCurrentFloorEntity(uint entityId)
    {
        RequestDrainCurrentFloorMachine(entityId);
    }

    public void RequestConnectCurrentFloorEntity(
        uint sourceEntityId,
        uint destinationBuildingInstanceId,
        int destinationFloorIndex,
        uint destinationEntityId)
    {
        if (IsOwner)
        {
            RequestConnectCurrentFloorEntityServerRpc(
                sourceEntityId,
                destinationBuildingInstanceId,
                destinationFloorIndex,
                destinationEntityId);
        }
    }

    public void RequestDisconnectCurrentFloorEntity(uint entityId)
    {
        if (IsOwner)
        {
            RequestDisconnectCurrentFloorEntityServerRpc(entityId);
        }
    }

    public void RequestDisconnectCurrentFloorEntity(
        uint entityId,
        FactoryEntityConnectionDirection direction)
    {
        if (IsOwner)
        {
            RequestDisconnectCurrentFloorEntityDirectionServerRpc(entityId, direction);
        }
    }

    [ServerRpc]
    private void RequestAddCurrentFloorMachineServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorMachine(
            this, position, out var entityId, out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added machine {entityId}." : error);
    }

    [ServerRpc]
    private void RequestAddCurrentFloorStorageServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorStorage(
            this,
            position,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added storage {entityId}." : error);
    }

    [ServerRpc]
    private void RequestAddCurrentFloorProcessorServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorProcessor(
            this,
            position,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added processor {entityId}." : error);
    }

    [ServerRpc]
    private void RequestAddCurrentFloorPackedStorageServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorPackedStorage(
            this,
            position,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added packed storage {entityId}." : error);
    }

    [ServerRpc]
    private void RequestAddCurrentFloorSendingTerminalServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorSendingTerminal(
            this,
            position,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added sending terminal {entityId}." : error);
    }

    [ServerRpc]
    private void RequestAddCurrentFloorReceivingTerminalServerRpc(Vector2 position)
    {
        var added = GameSceneManager.Instance.TryAddCurrentFloorReceivingTerminal(
            this,
            position,
            out var entityId,
            out var error);
        TargetReceiveMachineEditResult(Owner, added ? $"Added receiving terminal {entityId}." : error);
    }

    [ServerRpc]
    private void RequestRemoveCurrentFloorMachineServerRpc(uint entityId)
    {
        var removed = GameSceneManager.Instance.TryRemoveCurrentFloorMachine(this, entityId, out var error);
        TargetReceiveMachineEditResult(Owner, removed ? $"Removed machine {entityId}." : error);
    }

    [ServerRpc]
    private void RequestDrainCurrentFloorMachineServerRpc(uint entityId)
    {
        var drained = GameSceneManager.Instance.TryDrainCurrentFloorMachine(
            this,
            entityId,
            out var removed,
            out var error);
        var message = drained
            ? $"Drained {removed} {FactoryEntityRecord.OutputProductId} item(s); discarded."
            : error;
        TargetReceiveMachineEditResult(Owner, message);
    }

    [ServerRpc]
    private void RequestConnectCurrentFloorEntityServerRpc(
        uint sourceEntityId,
        uint destinationBuildingInstanceId,
        int destinationFloorIndex,
        uint destinationEntityId)
    {
        var connected = GameSceneManager.Instance.TryConnectCurrentFloorEntity(
            this,
            sourceEntityId,
            destinationBuildingInstanceId,
            destinationFloorIndex,
            destinationEntityId,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            connected
                ? $"Connected entity {sourceEntityId} to receiver "
                    + $"B{destinationBuildingInstanceId}/E{destinationEntityId}."
                : error);
    }

    [ServerRpc]
    private void RequestDisconnectCurrentFloorEntityServerRpc(uint entityId)
    {
        var disconnected = GameSceneManager.Instance.TryDisconnectCurrentFloorEntity(
            this,
            entityId,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            disconnected ? $"Disconnected entity {entityId}." : error);
    }

    [ServerRpc]
    private void RequestDisconnectCurrentFloorEntityDirectionServerRpc(
        uint entityId,
        FactoryEntityConnectionDirection direction)
    {
        var disconnected = GameSceneManager.Instance.TryDisconnectCurrentFloorEntity(
            this,
            entityId,
            direction,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            disconnected ? $"Disconnected {direction.ToString().ToLowerInvariant()} connection for entity {entityId}." : error);
    }

    [TargetRpc]
    private void TargetReceiveMachineEditResult(NetworkConnection connection, string message)
    {
        debugPanel.SetMachineEditResult(message);
        factoryBuilder.SetStatus(message);
        routePanel.SetStatus(message);
    }

    public void RequestCreateTruckRoute(
        FactoryEntityEndpoint source,
        FactoryEntityEndpoint destination)
    {
        if (IsOwner)
        {
            RequestCreateTruckRouteServerRpc(
                source.BuildingInstanceId,
                source.FloorIndex,
                source.EntityId,
                destination.BuildingInstanceId,
                destination.FloorIndex,
                destination.EntityId);
        }
    }

    public void RequestDeleteTruckRoute(Guid routeGuid)
    {
        if (IsOwner)
        {
            RequestDeleteTruckRouteServerRpc(FactoryGuidMigration.ToCanonical(routeGuid));
        }
    }

    [ServerRpc]
    private void RequestCreateTruckRouteServerRpc(
        uint sourceBuildingInstanceId,
        int sourceFloorIndex,
        uint sourceEntityId,
        uint destinationBuildingInstanceId,
        int destinationFloorIndex,
        uint destinationEntityId)
    {
        var created = GameSceneManager.Instance.TryCreateTruckRoute(
            this,
            new FactoryEntityEndpoint(sourceBuildingInstanceId, sourceFloorIndex, sourceEntityId),
            new FactoryEntityEndpoint(destinationBuildingInstanceId, destinationFloorIndex, destinationEntityId),
            out var routeGuid,
            out var error);
        TargetReceiveMachineEditResult(
            Owner,
            created ? $"Created truck route {routeGuid:D}." : error);
    }

    [ServerRpc]
    private void RequestDeleteTruckRouteServerRpc(string routeGuidString)
    {
        var parsed = FactoryGuidMigration.TryParseCanonical(routeGuidString, out var routeGuid);
        var error = string.Empty;
        var deleted = parsed
            && GameSceneManager.Instance.TryDeleteTruckRoute(this, routeGuid, out error);
        TargetReceiveMachineEditResult(
            Owner,
            deleted ? "Deleted truck route." : parsed ? error : "The truck route identity is invalid.");
    }

    public void RequestOutsideTestFloorSnapshot()
    {
        if (!IsOwner)
        {
            return;
        }

        RequestOutsideTestFloorSnapshotServerRpc();
    }

    public void RequestSetOutsideTestFloorState(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        if (!IsOwner)
        {
            return;
        }

        RequestSetOutsideTestFloorStateServerRpc(
            buildingInstanceId,
            floorIndex,
            label,
            productionRate,
            markerPosition);
    }

    public void RequestSaveOutsideTestFloorState()
    {
        if (IsOwner)
        {
            RequestSaveOutsideTestFloorStateServerRpc();
        }
    }

    public void RequestLoadOutsideTestFloorState()
    {
        if (IsOwner)
        {
            RequestLoadOutsideTestFloorStateServerRpc();
        }
    }

    public void RequestCreateOutsideTestBuilding(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        bool includeEntrance = true)
    {
        if (IsOwner)
        {
            RequestCreateOutsideTestBuildingServerRpc(
                anchorCell,
                footprintSize,
                storyCount,
                includeEntrance);
        }
    }

    public void ServerSendOutsideTestFloorState(
        OutsideTestFloorRecord floor,
        int loadedInteriorCount)
    {
        TargetReceiveOutsideTestFloorState(
            Owner,
            floor.BuildingInstanceId,
            floor.FloorIndex,
            floor.Label,
            floor.ProductionRate,
            floor.AccumulatedProduction,
            floor.MarkerPosition,
            floor.GetEntitySnapshots(),
            loadedInteriorCount);
    }

    [ServerRpc]
    private void RequestOutsideTestFloorSnapshotServerRpc()
    {
        GameSceneManager.Instance.SendOutsideTestFloorSnapshot(this);
    }

    [ServerRpc]
    private void RequestSetOutsideTestFloorStateServerRpc(
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        Vector2 markerPosition)
    {
        GameSceneManager.Instance.TrySetOutsideTestFloorState(
            buildingInstanceId,
            floorIndex,
            label,
            productionRate,
            markerPosition);
    }

    [ServerRpc]
    private void RequestSaveOutsideTestFloorStateServerRpc()
    {
        GameSceneManager.Instance.SaveOutsideTestFloorState();
    }

    [ServerRpc]
    private void RequestLoadOutsideTestFloorStateServerRpc()
    {
        GameSceneManager.Instance.LoadOutsideTestFloorState();
    }

    [ServerRpc]
    private void RequestCreateOutsideTestBuildingServerRpc(
        Vector3Int anchorCell,
        Vector2Int footprintSize,
        int storyCount,
        bool includeEntrance,
        NetworkConnection sender = null)
    {
        var created = GameSceneManager.Instance.TryCreateBuilding(
            anchorCell,
            footprintSize,
            storyCount,
            includeEntrance,
            out var buildingInstanceId,
            out var error);
        TargetReceiveOutsideTestBuildingCreation(
            sender,
            created,
            buildingInstanceId,
            error);
    }

    [TargetRpc]
    private void TargetReceiveOutsideTestFloorState(
        NetworkConnection connection,
        uint buildingInstanceId,
        int floorIndex,
        string label,
        float productionRate,
        float accumulatedProduction,
        Vector2 markerPosition,
        FactoryEntitySnapshot[] entitySnapshots,
        int loadedInteriorCount)
    {
        GameSceneManager.Instance.ReceiveOutsideTestFloorState(
            buildingInstanceId,
            floorIndex,
            label,
            productionRate,
            accumulatedProduction,
            markerPosition,
            entitySnapshots,
            loadedInteriorCount);
    }

    [TargetRpc]
    private void TargetReceiveOutsideTestBuildingCreation(
        NetworkConnection connection,
        bool created,
        uint buildingInstanceId,
        string error)
    {
        if (debugPanel is not null && debugPanel)
        {
            debugPanel.SetBuildingCreationResult(
                created,
                buildingInstanceId,
                error);
        }
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
        int floorIndex,
        uint sequence)
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
            floorIndex,
            sequence);
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
        int floorIndex,
        uint sequence)
    {
        if (sequence <= lastCompletedTransitionSequence)
        {
            return;
        }

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

        if (buildingInstanceId != 0
            && (!InsideFactoryController.TryGetForScene(
                    gameObject.scene,
                    out var configuredController)
                || configuredController.BuildingInstanceId != buildingInstanceId
                || configuredController.CurrentFloor != floorIndex))
        {
            return;
        }

        lastCompletedTransitionSequence = sequence;
        body.position = position;
        transform.SetPositionAndRotation(position, Quaternion.identity);
        networkTransform.Teleport();
        CloseElevatorPrompt();
        SetTransitionState(false);
        if (buildingInstanceId != 0)
        {
            if (debugPanel is not null && debugPanel)
            {
                debugPanel.SelectFloor(buildingInstanceId, floorIndex);
            }

            RequestOutsideTestFloorSnapshot();
        }
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
