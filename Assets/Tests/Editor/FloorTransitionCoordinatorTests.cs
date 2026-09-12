// Verifies floor-transition phase sequencing, failure recovery, reuse, and return state.
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using NUnit.Framework;

public sealed class FloorTransitionCoordinatorTests
{
    [Test]
    public void LoadedFloorIsReusedAndTransitionCompletes()
    {
        var scene = OpenTestScene();
        try
        {
            var coordinator = new FloorTransitionCoordinator();
            var key = new OutsideTestFloorKey(12, 1);
            Assert.That(coordinator.FloorInstances.RegisterLoaded(key, scene), Is.True);

            var request = new FloorTransitionRequest(4, key.BuildingInstanceId, key.FloorIndex);
            Assert.That(coordinator.TryBegin(request, out var plan, out var error), Is.True, error);
            Assert.That(plan.ReusesLoadedScene, Is.True);
            Assert.That(coordinator.MarkLoadStarted(4, request.Sequence), Is.True);
            Assert.That(coordinator.TryMarkSceneLoadEnd(4, request.Sequence, scene, out error), Is.True, error);
            Assert.That(coordinator.TryBeginArrival(4, scene, out _, out error), Is.True, error);
            Assert.That(coordinator.TryComplete(4, request.Sequence), Is.True);

            var status = coordinator.GetStatus(4);
            Assert.That(status.Phase, Is.EqualTo(FloorTransitionPhase.Completed));
            Assert.That(status.IsActive, Is.False);
            Assert.That(status.SceneDecision, Is.EqualTo(FloorTransitionSceneDecision.ReuseLoaded));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [Test]
    public void FailedLoadReleasesPendingFloorAndPublishesFailure()
    {
        var coordinator = new FloorTransitionCoordinator();
        var request = new FloorTransitionRequest(5, 13, 0);

        Assert.That(coordinator.TryBegin(request, out _, out var error), Is.True, error);
        Assert.That(coordinator.PendingLoadCount, Is.EqualTo(1));
        Assert.That(coordinator.TryFail(5, request.Sequence, "scene load failed", out _), Is.True);
        Assert.That(coordinator.PendingLoadCount, Is.Zero);
        Assert.That(coordinator.ActiveTransitionCount, Is.Zero);

        var status = coordinator.GetStatus(5);
        Assert.That(status.Phase, Is.EqualTo(FloorTransitionPhase.Failed));
        Assert.That(status.Error, Is.EqualTo("scene load failed"));
        Assert.That(status.IsActive, Is.False);
    }

    [Test]
    public void BuildingReturnFloorCanBeUpdatedAndCleared()
    {
        var coordinator = new FloorTransitionCoordinator();
        coordinator.SetBuildingReturn(6, new FloorReturnState(20, 0));

        Assert.That(coordinator.TryUpdateBuildingReturnFloor(6, 2), Is.True);
        Assert.That(coordinator.TryGetBuildingReturn(6, out var buildingReturn), Is.True);
        Assert.That(buildingReturn.CurrentFloor, Is.EqualTo(2));
        Assert.That(coordinator.ClearBuildingReturn(6), Is.True);
        Assert.That(coordinator.TryGetBuildingReturn(6, out _), Is.False);
    }

    [Test]
    public void StaleSequenceCannotAdvanceActiveTransition()
    {
        var coordinator = new FloorTransitionCoordinator();
        var request = new FloorTransitionRequest(7, 21, 0);

        Assert.That(coordinator.TryBegin(request, out _, out var error), Is.True, error);
        Assert.That(coordinator.MarkLoadStarted(7, request.Sequence + 1), Is.False);
        Assert.That(coordinator.GetStatus(7).Phase, Is.EqualTo(FloorTransitionPhase.Requested));
        Assert.That(coordinator.MarkLoadStarted(7, request.Sequence), Is.True);
    }

    private static Scene OpenTestScene()
    {
        return EditorSceneManager.OpenScene(
            "Assets/Scenes/insidefactory0.unity",
            OpenSceneMode.Additive);
    }
}
