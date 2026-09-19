// Verifies the reversible runtime gate for legacy presentation and disposable 3D player visuals.
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public sealed class Factory3DPresentationBridgeTests
{
    private GameObject bridgeObject = null!;
    private GameObject gridObject = null!;
    private Factory3DPresentationBridge bridge = null!;
    private Scene testScene;
    private Scene bootstrapScene;
    private SceneSetup[] previousSceneSetup = Array.Empty<SceneSetup>();
    private string testScenePath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        bridgeObject = new GameObject("Presentation Bridge");
        SceneManager.MoveGameObjectToScene(bridgeObject, testScene);
        bridge = bridgeObject.AddComponent<Factory3DPresentationBridge>();
        gridObject = new GameObject("Presentation Grid");
        SceneManager.MoveGameObjectToScene(gridObject, testScene);
        gridObject.AddComponent<SceneGrid>();

        testScenePath = $"Assets/Tests/Editor/Factory3DPresentationBridgeFixture-{Guid.NewGuid():N}.unity";
        Assert.That(EditorSceneManager.SaveScene(testScene, testScenePath), Is.True);
    }

    [TearDown]
    public void TearDown()
    {
        if (bridge is not null && bridge)
        {
            bridge.ClearPresentation();
        }

        if (bootstrapScene.IsValid() && bootstrapScene.isLoaded)
        {
            if (testScene.IsValid()
                && testScene.isLoaded
                && SceneManager.GetActiveScene() != testScene)
            {
                EditorSceneManager.SetActiveScene(testScene);
            }

            EditorSceneManager.CloseScene(bootstrapScene, true);
        }

        if (testScene.IsValid() && testScene.isLoaded)
        {
            if (SceneManager.sceneCount > 1)
            {
                EditorSceneManager.CloseScene(testScene, true);
            }
            else
            {
                EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Single);
            }
        }

        if (!string.IsNullOrWhiteSpace(testScenePath))
        {
            AssetDatabase.DeleteAsset(testScenePath);
        }

        if (previousSceneSetup.Length > 0)
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
        }
        else if (SceneManager.sceneCount == 0)
        {
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
        }
    }

    [Test]
    public void LegacyStateIsCapturedAndRestoredWithoutDisablingScreenSpaceUi()
    {
        var worldObject = new GameObject("Legacy World");
        SceneManager.MoveGameObjectToScene(worldObject, testScene);
        var worldRenderer = worldObject.AddComponent<SpriteRenderer>();
        var worldBody = worldObject.AddComponent<Rigidbody2D>();
        var worldCollider = worldObject.AddComponent<BoxCollider2D>();
        var tilemapObject = new GameObject("Legacy Tilemap", typeof(Tilemap));
        SceneManager.MoveGameObjectToScene(tilemapObject, testScene);
        var tilemapRenderer = tilemapObject.AddComponent<TilemapRenderer>();

        var canvasObject = new GameObject("Screen UI", typeof(Canvas));
        SceneManager.MoveGameObjectToScene(canvasObject, testScene);
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var uiObject = new GameObject("UI Sprite");
        uiObject.transform.SetParent(canvasObject.transform, false);
        var uiRenderer = uiObject.AddComponent<SpriteRenderer>();

        bridge.CaptureLegacyForScene(testScene);

        Assert.That(worldRenderer.enabled, Is.False);
        Assert.That(worldBody.simulated, Is.False);
        Assert.That(worldCollider.enabled, Is.False);
        Assert.That(tilemapRenderer.enabled, Is.False);
        Assert.That(uiRenderer.enabled, Is.True);
        Assert.That(bridge.LegacyComponentCount, Is.EqualTo(4));

        bridge.ClearPresentation();

        Assert.That(worldRenderer.enabled, Is.True);
        Assert.That(worldBody.simulated, Is.True);
        Assert.That(worldCollider.enabled, Is.True);
        Assert.That(tilemapRenderer.enabled, Is.True);
        Assert.That(bridge.LegacyComponentCount, Is.Zero);
    }

    [Test]
    public void AuthorityLossCleanupRestoresLegacyStateAndDestroysPlayerVisual()
    {
        var playerObject = new GameObject("3D Player");
        SceneManager.MoveGameObjectToScene(playerObject, testScene);
        var sprite = playerObject.AddComponent<SpriteRenderer>();
        var body = playerObject.AddComponent<Rigidbody2D>();
        var collider = playerObject.AddComponent<CapsuleCollider2D>();
        playerObject.AddComponent<Factory3DTraversalController>();

        var grid = gridObject.GetComponent<SceneGrid>();
        Assert.That(bridge.ActivatePresentation(testScene, grid), Is.True);
        Assert.That(bridge.Active, Is.True);
        Assert.That(bridge.PlayerVisualCount, Is.EqualTo(1));
        Assert.That(sprite.enabled, Is.False);
        Assert.That(body.simulated, Is.True);
        Assert.That(collider.enabled, Is.True);
        Assert.That(bridge.LegacyComponentCount, Is.EqualTo(1));

        Assert.That(
            bridge.ReconcileOutsideTest(null!, null!, 3f, 1f),
            Is.False);

        Assert.That(bridge.Active, Is.False);
        Assert.That(bridge.PlayerVisualCount, Is.Zero);
        Assert.That(sprite.enabled, Is.True);
        Assert.That(body.simulated, Is.True);
        Assert.That(collider.enabled, Is.True);
        Assert.That(bridge.TargetSceneName, Is.Empty);
    }

    [Test]
    public void CameraUsesFixedAngleOrthographicPresentationAndRestoresOnCleanup()
    {
        var cameraObject = new GameObject("Main Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, testScene);
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        var cameraFollow = cameraObject.AddComponent<CameraFollow>();
        camera.orthographic = false;
        var originalPosition = camera.transform.position;

        Assert.That(
            bridge.ActivatePresentation(
                testScene,
                gridObject.GetComponent<SceneGrid>()),
            Is.True);

        Assert.That(camera.orthographic, Is.True);
        Assert.That(camera.transform.position, Is.Not.EqualTo(originalPosition));
        Assert.That(
            camera.transform.rotation,
            Is.EqualTo(Factory3DPresentationBridge.GetDimetricCameraRotation()));
        Assert.That(
            camera.orthographicSize,
            Is.EqualTo(Factory3DPresentationBridge.GetDimetricOrthographicSize(5f, 1f))
                .Within(0.0001f));
        Assert.That(bridge.CameraConfigured, Is.True);
        Assert.That(cameraFollow.enabled, Is.False);

        bridge.ClearPresentation();

        Assert.That(camera.orthographic, Is.False);
        Assert.That(camera.transform.position, Is.EqualTo(originalPosition));
        Assert.That(cameraFollow.enabled, Is.True);
    }

    [Test]
    public void DimetricCameraMatchesTheExistingTwoToOneDiamondBasis()
    {
        var rotation = Factory3DPresentationBridge.GetDimetricCameraRotation();
        var right = rotation * Vector3.right;
        var up = rotation * Vector3.up;
        var xAxis = new Vector2(Vector3.Dot(Vector3.right, right), Vector3.Dot(Vector3.right, up));
        var zAxis = new Vector2(Vector3.Dot(Vector3.forward, right), Vector3.Dot(Vector3.forward, up));

        Assert.That(xAxis.x, Is.EqualTo(Factory3DPresentationBridge.DimetricCameraGroundScale).Within(0.0001f));
        Assert.That(xAxis.y, Is.EqualTo(0.5f * xAxis.x).Within(0.0001f));
        Assert.That(zAxis.x, Is.EqualTo(-xAxis.x).Within(0.0001f));
        Assert.That(zAxis.y, Is.EqualTo(xAxis.y).Within(0.0001f));
    }

    [Test]
    public void CameraCanBeResolvedFromLoadedBootstrapScene()
    {
        bootstrapScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Additive);
        var cameraObject = new GameObject("Bootstrap Main Camera", typeof(Camera));
        SceneManager.MoveGameObjectToScene(cameraObject, bootstrapScene);
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        var cameraFollow = cameraObject.AddComponent<CameraFollow>();
        camera.orthographic = false;

        var disabledCameras = new List<Camera>();
        foreach (var candidate in UnityEngine.Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (candidate is not null
                && candidate
                && candidate != camera
                && candidate.gameObject.activeInHierarchy
                && candidate.enabled)
            {
                disabledCameras.Add(candidate);
                candidate.enabled = false;
            }
        }

        try
        {
            Assert.That(
                bridge.ActivatePresentation(
                    testScene,
                    gridObject.GetComponent<SceneGrid>()),
                Is.True);

            Assert.That(bridge.CameraConfigured, Is.True);
            Assert.That(camera.orthographic, Is.True);
            Assert.That(cameraFollow.enabled, Is.False);

            bridge.ClearPresentation();

            Assert.That(camera.orthographic, Is.False);
            Assert.That(cameraFollow.enabled, Is.True);
        }
        finally
        {
            bridge.ClearPresentation();
            foreach (var candidate in disabledCameras)
            {
                if (candidate is not null && candidate)
                {
                    candidate.enabled = true;
                }
            }
        }
    }
}
