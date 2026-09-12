// Coordinates floor scene reuse, loading, player arrival, failure recovery, and unload decisions.
using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Utility.Extension;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_6000_5_OR_NEWER
using SceneHandle = System.UInt64;
#else
using SceneHandle = System.Int32;
#endif

public enum FloorTransitionPhase
{
    Idle,
    Requested,
    Loading,
    AwaitingPresence,
    Arriving,
    Completed,
    Failed
}

public enum FloorTransitionSceneDecision
{
    LoadTemplate,
    ReuseLoaded
}

public sealed class FloorTransitionRequest
{
    public FloorTransitionRequest(
        int clientId = 0,
        uint buildingInstanceId = 0,
        int floorIndex = 0)
    {
        ClientId = clientId;
        BuildingInstanceId = buildingInstanceId;
        FloorIndex = floorIndex;
    }

    public int ClientId { get; internal set; }
    public NetworkConnection Connection { get; internal set; } = null!;
    public NetworkObject Player { get; internal set; } = null!;
    public Vector2 ArrivalLogicalPosition { get; internal set; }
    public Vector2Int BuildingSize { get; internal set; }
    public Vector2[] InteriorExitLogicalPositions { get; internal set; } = Array.Empty<Vector2>();
    public Vector2[] ExteriorArrivalLogicalPositions { get; internal set; } = Array.Empty<Vector2>();
    public GridEdgeDirection[] InteriorExitDirections { get; internal set; } = Array.Empty<GridEdgeDirection>();
    public uint BuildingInstanceId { get; internal set; }
    public int StoryCount { get; internal set; } = 1;
    public int FloorIndex { get; internal set; }
    public uint Sequence { get; internal set; }
    public Scene PreviousScene { get; internal set; }
    public Scene TargetScene { get; internal set; }
    public bool CreatesBuildingReturn { get; internal set; }
    public bool ClearsBuildingReturn { get; internal set; }
    public FloorTransitionPhase Phase { get; internal set; }
    public FloorTransitionSceneDecision SceneDecision { get; internal set; }
    public string Error { get; internal set; } = string.Empty;

    public OutsideTestFloorKey TargetFloor => new(BuildingInstanceId, FloorIndex);
}

public sealed class FloorReturnState
{
    public FloorReturnState(
        uint buildingInstanceId = 0,
        int currentFloor = 0)
    {
        BuildingInstanceId = buildingInstanceId;
        CurrentFloor = currentFloor;
    }

    public string SceneName { get; internal set; } = string.Empty;
    public Vector2 ArrivalLogicalPosition { get; internal set; }
    public Vector2Int BuildingSize { get; internal set; }
    public uint BuildingInstanceId { get; internal set; }
    public int StoryCount { get; internal set; } = 1;
    public int CurrentFloor { get; internal set; }
    public Vector2[] InteriorExitLogicalPositions { get; internal set; } = Array.Empty<Vector2>();
    public Vector2[] ExteriorArrivalLogicalPositions { get; internal set; } = Array.Empty<Vector2>();
    public GridEdgeDirection[] InteriorExitDirections { get; internal set; } = Array.Empty<GridEdgeDirection>();
}

public readonly struct FloorTransitionScenePlan
{
    public FloorTransitionScenePlan(
        FloorTransitionSceneDecision newDecision,
        Scene newExistingScene,
        bool newAllowStacking)
    {
        Decision = newDecision;
        ExistingScene = newExistingScene;
        AllowStacking = newAllowStacking;
    }

    public FloorTransitionSceneDecision Decision { get; }
    public Scene ExistingScene { get; }
    public bool AllowStacking { get; }
    public bool ReusesLoadedScene => Decision == FloorTransitionSceneDecision.ReuseLoaded;
}

public readonly struct FloorTransitionStatus
{
    public FloorTransitionStatus(
        int newClientId,
        uint newSequence,
        FloorTransitionPhase newPhase,
        OutsideTestFloorKey newTargetFloor,
        FloorTransitionSceneDecision newSceneDecision,
        string newTargetSceneHandle,
        string newError,
        bool newIsActive)
    {
        ClientId = newClientId;
        Sequence = newSequence;
        Phase = newPhase;
        TargetFloor = newTargetFloor;
        SceneDecision = newSceneDecision;
        TargetSceneHandle = newTargetSceneHandle ?? string.Empty;
        Error = newError ?? string.Empty;
        IsActive = newIsActive;
    }

    public int ClientId { get; }
    public uint Sequence { get; }
    public FloorTransitionPhase Phase { get; }
    public OutsideTestFloorKey TargetFloor { get; }
    public FloorTransitionSceneDecision SceneDecision { get; }
    public string TargetSceneHandle { get; }
    public string Error { get; }
    public bool IsActive { get; }
    public bool HasTargetFloor => TargetFloor.BuildingInstanceId != 0;
}

public sealed class FloorTransitionCoordinator
{
    private readonly Dictionary<int, FloorTransitionRequest> activeTransitions = new();
    private readonly Dictionary<int, FloorTransitionStatus> statuses = new();
    private readonly Dictionary<int, FloorReturnState> buildingReturns = new();
    private readonly OutsideTestFloorInstanceRegistry floorInstances = new();
    private readonly HashSet<SceneHandle> unloadingScenes = new();
    private uint nextSequence;

    public OutsideTestFloorInstanceRegistry FloorInstances => floorInstances;
    public IEnumerable<FloorTransitionRequest> ActiveTransitions => activeTransitions.Values;
    public int ActiveTransitionCount => activeTransitions.Count;
    public int PendingLoadCount => floorInstances.PendingCount;

    public bool HasActiveTransition(int clientId)
    {
        return activeTransitions.ContainsKey(clientId);
    }

    public bool TryGetActiveTransition(
        int clientId,
        out FloorTransitionRequest request)
    {
        return activeTransitions.TryGetValue(clientId, out request);
    }

    public bool TryBegin(
        FloorTransitionRequest request,
        out FloorTransitionScenePlan plan,
        out string error)
    {
        plan = default;
        error = string.Empty;
        if (request is null)
        {
            error = "A floor transition request is required.";
            return false;
        }

        if (request.ClientId < 0)
        {
            error = "The transition client identity is invalid.";
            return false;
        }

        if (request.Connection is not null
            && !request.Connection.IsValid)
        {
            error = "The transition connection is invalid.";
            return false;
        }

        if (activeTransitions.ContainsKey(request.ClientId))
        {
            error = "A floor transition is already pending for this connection.";
            return false;
        }

        request.Sequence = ++nextSequence;
        request.Phase = FloorTransitionPhase.Requested;
        request.Error = string.Empty;
        if (request.BuildingInstanceId != 0)
        {
            if (floorInstances.TryGetLoaded(request.TargetFloor, out var instance))
            {
                request.SceneDecision = FloorTransitionSceneDecision.ReuseLoaded;
                plan = new FloorTransitionScenePlan(
                    request.SceneDecision,
                    instance.Scene,
                    false);
            }
            else if (!floorInstances.TryBeginLoad(request.TargetFloor))
            {
                error = $"Floor {request.FloorIndex} in building {request.BuildingInstanceId} is already loading.";
                return false;
            }
            else
            {
                request.SceneDecision = FloorTransitionSceneDecision.LoadTemplate;
                plan = new FloorTransitionScenePlan(
                    request.SceneDecision,
                    default,
                    true);
            }
        }
        else
        {
            request.SceneDecision = FloorTransitionSceneDecision.LoadTemplate;
            plan = new FloorTransitionScenePlan(
                request.SceneDecision,
                default,
                false);
        }

        activeTransitions.Add(request.ClientId, request);
        statuses[request.ClientId] = CreateStatus(request, true);
        return true;
    }

    public bool MarkLoadStarted(int clientId, uint sequence)
    {
        if (!TryGetActive(clientId, sequence, out var request)
            || request.Phase != FloorTransitionPhase.Requested)
        {
            return false;
        }

        request.Phase = FloorTransitionPhase.Loading;
        statuses[clientId] = CreateStatus(request, true);
        return true;
    }

    public bool TryMarkSceneLoadEnd(
        int clientId,
        uint sequence,
        Scene targetScene,
        out string error)
    {
        error = string.Empty;
        if (!TryGetActive(clientId, sequence, out var request))
        {
            error = "The transition request is no longer active.";
            return false;
        }

        if (!targetScene.IsValid() || !targetScene.isLoaded)
        {
            error = "The target scene is invalid or not loaded.";
            return false;
        }

        if (request.BuildingInstanceId != 0
            && !floorInstances.RegisterLoaded(request.TargetFloor, targetScene))
        {
            error = "The target floor scene could not be registered.";
            return false;
        }

        request.TargetScene = targetScene;
        request.Phase = FloorTransitionPhase.AwaitingPresence;
        statuses[clientId] = CreateStatus(request, true);
        return true;
    }

    public bool TryBeginArrival(
        int clientId,
        Scene scene,
        out FloorTransitionRequest request,
        out string error)
    {
        request = null!;
        error = string.Empty;
        if (!activeTransitions.TryGetValue(clientId, out request))
        {
            error = "The transition request is no longer active.";
            return false;
        }

        if (request.Phase != FloorTransitionPhase.AwaitingPresence
            || !request.TargetScene.IsValid()
            || request.TargetScene.GetRawHandle() != scene.GetRawHandle())
        {
            error = "The player presence does not match the pending transition.";
            return false;
        }

        request.Phase = FloorTransitionPhase.Arriving;
        statuses[clientId] = CreateStatus(request, true);
        return true;
    }

    public bool TryComplete(
        int clientId,
        uint sequence)
    {
        if (!TryGetActive(clientId, sequence, out var request)
            || request.Phase != FloorTransitionPhase.Arriving)
        {
            return false;
        }

        request.Phase = FloorTransitionPhase.Completed;
        statuses[clientId] = CreateStatus(request, false);
        activeTransitions.Remove(clientId);
        return true;
    }

    public bool TryFail(
        int clientId,
        uint sequence,
        string error,
        out FloorTransitionRequest request)
    {
        request = null!;
        if (!TryGetActive(clientId, sequence, out request))
        {
            return false;
        }

        request.Phase = FloorTransitionPhase.Failed;
        request.Error = string.IsNullOrWhiteSpace(error)
            ? "The floor transition failed."
            : error;
        if (request.BuildingInstanceId != 0)
        {
            floorInstances.FailLoad(request.TargetFloor);
        }

        statuses[clientId] = CreateStatus(request, false);
        activeTransitions.Remove(clientId);
        return true;
    }

    public void SetBuildingReturn(
        int clientId,
        FloorReturnState buildingReturn)
    {
        buildingReturns[clientId] = buildingReturn;
    }

    public bool TryGetBuildingReturn(
        int clientId,
        out FloorReturnState buildingReturn)
    {
        return buildingReturns.TryGetValue(clientId, out buildingReturn);
    }

    public bool TryUpdateBuildingReturnFloor(
        int clientId,
        int floorIndex)
    {
        if (!buildingReturns.TryGetValue(clientId, out var buildingReturn))
        {
            return false;
        }

        buildingReturn.CurrentFloor = floorIndex;
        return true;
    }

    public bool ClearBuildingReturn(int clientId)
    {
        return buildingReturns.Remove(clientId);
    }

    public bool TryBeginUnload(SceneHandle handle)
    {
        return unloadingScenes.Add(handle);
    }

    public bool IsUnloading(SceneHandle handle)
    {
        return unloadingScenes.Contains(handle);
    }

    public bool CompleteUnload(SceneHandle handle)
    {
        unloadingScenes.Remove(handle);
        return floorInstances.Remove(handle);
    }

    public bool TryGetFloorKey(
        SceneHandle handle,
        out OutsideTestFloorKey key)
    {
        return floorInstances.TryGetKey(handle, out key);
    }

    public FloorTransitionStatus GetStatus(int clientId)
    {
        return statuses.TryGetValue(clientId, out var status)
            ? status
            : new FloorTransitionStatus(
                clientId,
                0,
                FloorTransitionPhase.Idle,
                default,
                FloorTransitionSceneDecision.LoadTemplate,
                string.Empty,
                string.Empty,
                false);
    }

    public void Clear()
    {
        activeTransitions.Clear();
        statuses.Clear();
        buildingReturns.Clear();
        floorInstances.Clear();
        unloadingScenes.Clear();
        nextSequence = 0;
    }

    private bool TryGetActive(
        int clientId,
        uint sequence,
        out FloorTransitionRequest request)
    {
        return activeTransitions.TryGetValue(clientId, out request)
            && request.Sequence == sequence;
    }

    private static FloorTransitionStatus CreateStatus(
        FloorTransitionRequest request,
        bool isActive)
    {
        var targetSceneHandle = request.TargetScene.IsValid()
            ? request.TargetScene.GetRawHandle().ToString()
            : string.Empty;
        return new FloorTransitionStatus(
            request.ClientId,
            request.Sequence,
            request.Phase,
            request.TargetFloor,
            request.SceneDecision,
            targetSceneHandle,
            request.Error,
            isActive);
    }
}
