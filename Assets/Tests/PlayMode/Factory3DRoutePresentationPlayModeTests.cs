// Verifies the cross-floor route proxy through Play Mode refresh, floor switching, and authority loss.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public sealed class Factory3DRoutePresentationPlayModeTests
{
    [UnityTest]
    public IEnumerator RouteProxyRefreshesAcrossFloorsWithoutAdvancingFromPresentation()
    {
        var gridObject = new GameObject("Route PlayMode Grid");
        var grid = gridObject.AddComponent<SceneGrid>();
        var root = new GameObject("Route PlayMode Proxy Root");
        var view = root.AddComponent<Factory3DRouteProxyView>();
        var state = Factory3DRouteFixture.CreateState();
        var before = state.CaptureState();

        var first = view.Rebuild(state, grid, 3f, 0);
        Assert.That(first.EntityCount, Is.EqualTo(3));
        Assert.That(root.transform.childCount, Is.EqualTo(3));

        yield return null;

        view.SetActiveFloor(1);
        yield return null;
        Assert.That(root.transform.childCount, Is.EqualTo(3));
        state.AdvanceProduction(FactorySimulation.DefaultTickInterval);
        view.Refresh(state, grid, 3f);
        Assert.That(view.Snapshot.Entities.Count, Is.EqualTo(6));
        Assert.That(state.CaptureState().Floors.Count, Is.EqualTo(before.Floors.Count));

        view.ClearAuthority();
        yield return null;
        Assert.That(root.transform.childCount, Is.Zero);

        Object.Destroy(root);
        Object.Destroy(gridObject);
    }
}
