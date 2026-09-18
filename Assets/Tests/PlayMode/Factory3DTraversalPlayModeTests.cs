// Verifies runtime CharacterController grounding, collision, and input ownership gating.
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;

public sealed class Factory3DTraversalPlayModeTests
{
    private GameObject gridObject = null!;
    private GameObject playerObject = null!;
    private Factory3DTraversalController traversal = null!;
    private Scene testScene;
    private Scene previousScene;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        previousScene = SceneManager.GetActiveScene();
        testScene = SceneManager.CreateScene(
            "Factory3DTraversalTestScene",
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        SceneManager.SetActiveScene(testScene);
        gridObject = new GameObject("Traversal PlayMode Grid");
        var grid = gridObject.AddComponent<SceneGrid>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Traversal PlayMode Floor";
        floor.transform.SetParent(gridObject.transform, false);
        floor.transform.position = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale = new Vector3(8f, 0.2f, 8f);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Traversal PlayMode Wall";
        wall.transform.SetParent(gridObject.transform, false);
        wall.transform.position = new Vector3(0f, 1f, 1.5f);
        wall.transform.localScale = new Vector3(8f, 2f, 0.2f);
        playerObject = new GameObject("Traversal PlayMode Player");
        traversal = playerObject.AddComponent<Factory3DTraversalController>();
        traversal.ConfigureSpatialContext(grid.CreateSpatialAdapter(), 17u, 0, 3f);
        playerObject.transform.position = new Vector3(0f, 1f, 0f);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (playerObject is not null && playerObject)
        {
            Object.Destroy(playerObject);
        }

        if (gridObject is not null && gridObject)
        {
            Object.Destroy(gridObject);
        }

        yield return null;

        if (testScene.IsValid() && testScene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        if (previousScene.IsValid() && previousScene.isLoaded)
        {
            SceneManager.SetActiveScene(previousScene);
        }
    }

    [UnityTest]
    public IEnumerator CharacterControllerGroundsAndStopsAtDerivedCollision()
    {
        traversal.Set3DOwnership(true);
        traversal.StepForTest(Vector2.up, 1f);
        yield return null;
        traversal.StepForTest(Vector2.zero, 0.02f);
        yield return null;

        Assert.That(traversal.IsGrounded, Is.True);
        Assert.That(playerObject.transform.position.z, Is.LessThan(1.25f));
        Assert.That(playerObject.transform.position.y, Is.EqualTo(0f).Within(0.1f));
    }

    [UnityTest]
    public IEnumerator UiFocusAndOwnershipGatePlanarInput()
    {
        traversal.Set3DOwnership(true);
        traversal.SetPointerFocusOverride(true);
        traversal.StepForTest(Vector2.right, 1f);
        Assert.That(playerObject.transform.position.x, Is.EqualTo(0f).Within(0.0001f));

        traversal.SetPointerFocusOverride(false);
        traversal.StepForTest(Vector2.right, 0.1f);
        Assert.That(playerObject.transform.position.x, Is.GreaterThan(0f));

        traversal.Set3DOwnership(false);
        var positionAfterRelease = playerObject.transform.position;
        traversal.StepForTest(Vector2.right, 0.1f);
        yield return null;

        Assert.That(playerObject.transform.position, Is.EqualTo(positionAfterRelease));
    }

    [UnityTest]
    public IEnumerator DerivedCollisionPresenterStopsThePlayerAtEquipment()
    {
        var grid = gridObject.GetComponent<SceneGrid>();
        var state = Factory3DRouteFixture.CreateState();
        var snapshot = Factory3DRouteSnapshotBuilder.Build(state, grid, 3f);
        var collisions = gridObject.AddComponent<Factory3DTraversalCollisionPresenter>();
        collisions.Reconcile(snapshot, grid, Factory3DRouteFixture.BuildingId, 0, new Vector2Int(10, 6));
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
        var adapter = grid.CreateSpatialAdapter();
        var equipmentWorld = adapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                Factory3DRouteFixture.BuildingId,
                0,
                new Vector2(1.5f, 2.5f)),
            0f);
        playerObject.transform.position = equipmentWorld + Vector3.back * 2f + Vector3.up;
        traversal.ConfigureSpatialContext(adapter, Factory3DRouteFixture.BuildingId, 0, 3f);
        traversal.Set3DOwnership(true);
        traversal.StepForTest(Vector2.up, 1f);
        yield return null;

        Assert.That(playerObject.transform.position.z, Is.LessThan(equipmentWorld.z - 0.35f));
        Assert.That(collisions.CollisionRoot.childCount, Is.GreaterThan(0));
    }
}
