// Verifies interior proxy lifecycle cleanup across a Play Mode frame.
using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Factory3DInteriorPresentationPlayModeTests
{
    [UnityTest]
    public IEnumerator InteriorPresenterReconcilesThenClearsWithoutAuthorityWrites()
    {
        var sceneRoot = new GameObject("Interior PlayMode Fixture");
        var grid = sceneRoot.AddComponent<SceneGrid>();
        var indoorGrid = sceneRoot.AddComponent<IndoorGrid>();
        var portalObject = new GameObject("Interior PlayMode Exit Portal");
        portalObject.transform.SetParent(sceneRoot.transform, false);
        var controller = sceneRoot.AddComponent<InsideFactoryController>();
        var portal = portalObject.AddComponent<ScenePortal>();
        SetPrivateField(controller, "exitPortal", portal);
        controller.Configure(
            new Vector2Int(8, 4),
            new Vector2(0.5f, 0.5f),
            Array.Empty<Vector2>(),
            Array.Empty<Vector2>(),
            Array.Empty<GridEdgeDirection>(),
            Factory3DRouteFixture.BuildingId,
            2,
            0);

        var presenterObject = new GameObject("Interior PlayMode Presenter");
        var presenter = presenterObject.AddComponent<Factory3DInteriorPresenter>();
        var state = Factory3DRouteFixture.CreateState();
        var before = state.CaptureState();

        presenter.ReconcileSnapshot(
            Factory3DRouteSnapshotBuilder.Build(state, grid, 3f),
            controller,
            grid,
            indoorGrid.Size);
        Assert.That(presenter.ProxyCount, Is.EqualTo(8));

        yield return null;

        presenter.Clear();
        Assert.That(presenter.ProxyCount, Is.Zero);
        Assert.That(state.CaptureState().Floors.Count, Is.EqualTo(before.Floors.Count));

        UnityEngine.Object.Destroy(presenterObject);
        UnityEngine.Object.Destroy(sceneRoot);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(target, value);
    }
}
