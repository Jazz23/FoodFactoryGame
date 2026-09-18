// Verifies the editor companion's disposable proxy lifecycle without authoring scene or save state.
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Factory3DTestBuildingCreatorWindowTests
{
    [Test]
    public void RefreshCreatesOnlyDisposableProxyAndWindowDisableCleansIt()
    {
        var testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var window = (Factory3DTestBuildingCreatorWindow)null!;
        var gridObject = (GameObject)null!;
        var creatorObject = (GameObject)null!;
        try
        {
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(testScene));
            gridObject = new GameObject("Editor Lifecycle Grid");
            var grid = gridObject.AddComponent<SceneGrid>();
            creatorObject = new GameObject("Editor Lifecycle Creator");
            var creator = creatorObject.AddComponent<TestBuildingCreator>();
            SetPrivateField(creator, "grid", grid);
            var dirtyBeforeRefresh = testScene.isDirty;

            window = ScriptableObject.CreateInstance<Factory3DTestBuildingCreatorWindow>();
            SetPrivateField(window, "creator", creator);
            var refreshMethod = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
                "RefreshProxyPresentation",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refreshMethod, Is.Not.Null);
            refreshMethod!.Invoke(window, null);

            var views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            var proxyView = FindInScene(views, testScene);
            Assert.That(proxyView, Is.Not.Null);
            Assert.That(
                proxyView!.gameObject.hideFlags,
                Is.EqualTo(HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild));
            Assert.That(proxyView.GetComponent<TestBuildingLayout>(), Is.Null);
            Assert.That(testScene.isDirty, Is.EqualTo(dirtyBeforeRefresh));

            UnityEngine.Object.DestroyImmediate(window);
            window = null!;

            views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            Assert.That(FindInScene(views, testScene), Is.Null);
            Assert.That(testScene.isDirty, Is.EqualTo(dirtyBeforeRefresh));
        }
        finally
        {
            if (window is not null && window)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            if (creatorObject is not null && creatorObject)
            {
                UnityEngine.Object.DestroyImmediate(creatorObject);
            }

            if (gridObject is not null && gridObject)
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }
    }

    [Test]
    public void RuntimeProxyClearsInsteadOfUsingAuthoredRecordsWhenAuthorityIsUnavailable()
    {
        var testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var window = (Factory3DTestBuildingCreatorWindow)null!;
        var gridObject = (GameObject)null!;
        var creatorObject = (GameObject)null!;
        var stateManagerObject = (GameObject)null!;
        var layout = (FactoryBuildingLayoutAsset)null!;
        try
        {
            gridObject = new GameObject("Runtime Authority Grid");
            var grid = gridObject.AddComponent<SceneGrid>();
            creatorObject = new GameObject("Runtime Authority Creator");
            var creator = creatorObject.AddComponent<TestBuildingCreator>();
            SetPrivateField(creator, "grid", grid);
            layout = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
            Assert.That(
                TestBuildingCreator.TryGetDefaultEntrance(
                    Vector3Int.zero,
                    new Vector2Int(5, 4),
                    out var entrance,
                    out var entranceError),
                Is.True,
                entranceError);
            var record = new BuildingRecord(
                901u,
                Vector3Int.zero,
                new Vector2Int(5, 4),
                1,
                new[] { entrance });
            Assert.That(layout.TryAddRecord(record, out var layoutError), Is.True, layoutError);
            creator.SetAuthoredLayout(layout);

            window = ScriptableObject.CreateInstance<Factory3DTestBuildingCreatorWindow>();
            SetPrivateField(window, "creator", creator);
            var refreshRecordsMethod = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
                "RefreshRecords",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refreshRecordsMethod, Is.Not.Null);
            refreshRecordsMethod!.Invoke(window, null);

            window.RefreshProxyPresentationForMode(false, null!);
            var views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            var proxyView = FindInScene(views, testScene);
            Assert.That(proxyView, Is.Not.Null);
            Assert.That(proxyView!.transform.childCount, Is.GreaterThan(0));

            window.RefreshProxyPresentationForMode(true, null!);
            Assert.That(proxyView.transform.childCount, Is.EqualTo(0));

            stateManagerObject = new GameObject("Unavailable Runtime Authority");
            var stateManager = stateManagerObject.AddComponent<NotAI.NAIStateManager>();
            window.RefreshProxyPresentationForMode(true, stateManager);
            Assert.That(proxyView.transform.childCount, Is.EqualTo(0));
        }
        finally
        {
            if (window is not null && window)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            if (layout is not null && layout)
            {
                UnityEngine.Object.DestroyImmediate(layout);
            }

            if (creatorObject is not null && creatorObject)
            {
                UnityEngine.Object.DestroyImmediate(creatorObject);
            }

            if (stateManagerObject is not null && stateManagerObject)
            {
                UnityEngine.Object.DestroyImmediate(stateManagerObject);
            }

            if (gridObject is not null && gridObject)
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }
    }

    [Test]
    public void PlayModeLifecycleCallbackClearsProxyWithoutSceneViewRepaintOrAuthoredFallback()
    {
        var testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var window = (Factory3DTestBuildingCreatorWindow)null!;
        var gridObject = (GameObject)null!;
        var creatorObject = (GameObject)null!;
        var stateManagerObject = (GameObject)null!;
        var layout = (FactoryBuildingLayoutAsset)null!;
        var instanceField = typeof(NotAI.NAIStateManager).GetField(
            "instance",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(instanceField, Is.Not.Null);
        var previousInstance = instanceField!.GetValue(null);
        try
        {
            gridObject = new GameObject("Lifecycle Callback Grid");
            var grid = gridObject.AddComponent<SceneGrid>();
            creatorObject = new GameObject("Lifecycle Callback Creator");
            var creator = creatorObject.AddComponent<TestBuildingCreator>();
            SetPrivateField(creator, "grid", grid);
            layout = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
            Assert.That(
                TestBuildingCreator.TryGetDefaultEntrance(
                    Vector3Int.zero,
                    new Vector2Int(5, 4),
                    out var entrance,
                    out var entranceError),
                Is.True,
                entranceError);
            Assert.That(
                layout.TryAddRecord(
                    new BuildingRecord(
                        902u,
                        Vector3Int.zero,
                        new Vector2Int(5, 4),
                        1,
                        new[] { entrance }),
                    out var layoutError),
                Is.True,
                layoutError);
            creator.SetAuthoredLayout(layout);

            window = ScriptableObject.CreateInstance<Factory3DTestBuildingCreatorWindow>();
            SetPrivateField(window, "creator", creator);
            var refreshRecordsMethod = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
                "RefreshRecords",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refreshRecordsMethod, Is.Not.Null);
            refreshRecordsMethod!.Invoke(window, null);

            window.RefreshProxyPresentationForMode(false, null!);
            var views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            var proxyView = FindInScene(views, testScene);
            Assert.That(proxyView, Is.Not.Null);
            Assert.That(proxyView!.transform.childCount, Is.GreaterThan(0));

            window.HandlePlayModeStateChanged(PlayModeStateChange.ExitingEditMode);
            Assert.That(proxyView.transform.childCount, Is.Zero);

            window.RefreshProxyPresentationForMode(false, null!);
            Assert.That(proxyView.transform.childCount, Is.GreaterThan(0));

            stateManagerObject = new GameObject("Unready Lifecycle Authority");
            var stateManager = stateManagerObject.AddComponent<NotAI.NAIStateManager>();
            instanceField.SetValue(null, stateManager);
            window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);
            Assert.That(proxyView.transform.childCount, Is.Zero);

            window.HandlePlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
            Assert.That(proxyView.transform.childCount, Is.GreaterThan(0));
        }
        finally
        {
            instanceField.SetValue(null, previousInstance);
            if (window is not null && window)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            if (layout is not null && layout)
            {
                UnityEngine.Object.DestroyImmediate(layout);
            }

            if (creatorObject is not null && creatorObject)
            {
                UnityEngine.Object.DestroyImmediate(creatorObject);
            }

            if (stateManagerObject is not null && stateManagerObject)
            {
                UnityEngine.Object.DestroyImmediate(stateManagerObject);
            }

            if (gridObject is not null && gridObject)
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }
    }

    [Test]
    public void EditorUpdateClearsProxyWhenGridIsRemovedWithoutSceneViewRepaint()
    {
        var testScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        var window = (Factory3DTestBuildingCreatorWindow)null!;
        var gridObject = (GameObject)null!;
        var creatorObject = (GameObject)null!;
        var layout = (FactoryBuildingLayoutAsset)null!;
        try
        {
            gridObject = new GameObject("Editor Update Invalidation Grid");
            var grid = gridObject.AddComponent<SceneGrid>();
            creatorObject = new GameObject("Editor Update Invalidation Creator");
            var creator = creatorObject.AddComponent<TestBuildingCreator>();
            SetPrivateField(creator, "grid", grid);
            layout = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
            Assert.That(
                TestBuildingCreator.TryGetDefaultEntrance(
                    Vector3Int.zero,
                    new Vector2Int(5, 4),
                    out var entrance,
                    out var entranceError),
                Is.True,
                entranceError);
            Assert.That(
                layout.TryAddRecord(
                    new BuildingRecord(
                        903u,
                        Vector3Int.zero,
                        new Vector2Int(5, 4),
                        1,
                        new[] { entrance }),
                    out var layoutError),
                Is.True,
                layoutError);
            creator.SetAuthoredLayout(layout);

            window = ScriptableObject.CreateInstance<Factory3DTestBuildingCreatorWindow>();
            SetPrivateField(window, "creator", creator);
            var refreshRecordsMethod = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
                "RefreshRecords",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var handleEditorUpdateMethod = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
                "HandleEditorUpdate",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(refreshRecordsMethod, Is.Not.Null);
            Assert.That(handleEditorUpdateMethod, Is.Not.Null);
            refreshRecordsMethod!.Invoke(window, null);
            handleEditorUpdateMethod!.Invoke(window, null);

            var views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            var proxyView = FindInScene(views, testScene);
            Assert.That(proxyView, Is.Not.Null);
            Assert.That(proxyView!.transform.childCount, Is.GreaterThan(0));
            var dirtyBeforeInvalidation = testScene.isDirty;

            UnityEngine.Object.DestroyImmediate(gridObject);
            gridObject = null!;
            handleEditorUpdateMethod.Invoke(window, null);

            views = Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>();
            Assert.That(FindInScene(views, testScene), Is.Null);
            Assert.That(testScene.isDirty, Is.EqualTo(dirtyBeforeInvalidation));
        }
        finally
        {
            if (window is not null && window)
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            if (layout is not null && layout)
            {
                UnityEngine.Object.DestroyImmediate(layout);
            }

            if (creatorObject is not null && creatorObject)
            {
                UnityEngine.Object.DestroyImmediate(creatorObject);
            }

            if (gridObject is not null && gridObject)
            {
                UnityEngine.Object.DestroyImmediate(gridObject);
            }
        }
    }

    private static void SetPrivateField(
        TestBuildingCreator creator,
        string fieldName,
        object value)
    {
        var field = typeof(TestBuildingCreator).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(creator, value);
    }

    private static void SetPrivateField(
        Factory3DTestBuildingCreatorWindow window,
        string fieldName,
        object value)
    {
        var field = typeof(Factory3DTestBuildingCreatorWindow).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        field!.SetValue(window, value);
    }

    private static Factory3DOutsideTestProxyView FindInScene(
        Factory3DOutsideTestProxyView[] views,
        Scene scene)
    {
        foreach (var view in views)
        {
            if (view is not null
                && view
                && view.gameObject.scene == scene)
            {
                return view;
            }
        }

        return null!;
    }
}
