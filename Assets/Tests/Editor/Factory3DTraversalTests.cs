// Verifies the reusable 3D traversal seam without scene, network, or database state.
using NUnit.Framework;
using UnityEngine;

public sealed class Factory3DTraversalTests
{
    private GameObject gridObject = null!;
    private GameObject playerObject = null!;
    private SceneGrid grid = null!;
    private Factory3DTraversalController traversal = null!;

    [SetUp]
    public void SetUp()
    {
        gridObject = new GameObject("Traversal Grid");
        grid = gridObject.AddComponent<SceneGrid>();
        playerObject = new GameObject("Traversal Player");
        traversal = playerObject.AddComponent<Factory3DTraversalController>();
        traversal.ConfigureSpatialContext(grid.CreateSpatialAdapter(), 17u, 2, 3f);
    }

    [TearDown]
    public void TearDown()
    {
        if (playerObject is not null && playerObject)
        {
            Object.DestroyImmediate(playerObject);
        }

        if (gridObject is not null && gridObject)
        {
            Object.DestroyImmediate(gridObject);
        }
    }

    [Test]
    public void FootAnchorUsesAdapterLogicalCoordinatesAndDerivedElevation()
    {
        playerObject.transform.position = new Vector3(2.25f, 6f, 3.75f);

        Assert.That(traversal.TryGetLogicalFootAnchor(out var location), Is.True);
        Assert.That(location.BuildingInstanceId, Is.EqualTo(17u));
        Assert.That(location.FloorIndex, Is.EqualTo(2));
        Assert.That(
            location.FloorPosition,
            Is.EqualTo(
                grid.CreateSpatialAdapter()
                    .WorldToLogical3D(playerObject.transform.position, 17u, 2)
                    .FloorPosition));
        Assert.That(traversal.FloorElevation, Is.EqualTo(6f).Within(0.0001f));
    }

    [Test]
    public void ThreeDPortalQueryUsesTheSameFootAnchor()
    {
        var portalObject = new GameObject("Traversal Portal");
        var portal = portalObject.AddComponent<ScenePortal>();
        portal.ConfigureInterior(new Vector2(2.25f, 3.75f));

        try
        {
            var adapter = gridObject.GetComponent<SceneGrid>().CreateSpatialAdapter();
            playerObject.transform.position = adapter.LogicalToWorld3D(
                new FactoryLogicalLocation(0u, 0, new Vector2(2.25f, 3.75f)),
                0f);
            Assert.That(
                ScenePortal.TryGetClosest(
                    portalObject.scene,
                    traversal.FootAnchor,
                    adapter,
                    out var resolved),
                Is.True);
            Assert.That(resolved, Is.SameAs(portal));
        }
        finally
        {
            Object.DestroyImmediate(portalObject);
        }
    }
}
