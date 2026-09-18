// Verifies the 3D proxy across real Editor Play Mode transitions without SceneView repainting.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FishNet.Managing;
using NUnit.Framework;
using NotAI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Factory3DOutsideTestProxyPlayModeLifecycleTests
{
    private const uint LifecycleBuildingId = 9701;
    private static readonly Vector3Int AuthoredAnchor = new(-4, 2, 0);
    private static readonly Vector3Int RuntimeAnchor = new(12, -5, 0);
    private static readonly Vector2Int LifecycleFootprint = new(5, 4);

    private readonly List<string> lifecycleErrors = new();
    private Scene fixtureScene;
    private Scene bootstrapScene;
    private string fixtureScenePath = string.Empty;
    private string authoredLayoutPath = string.Empty;
    private GameObject gridObject = null!;
    private GameObject creatorObject = null!;
    private TestBuildingCreator fixtureCreator = null!;
    private SceneGrid grid = null!;
    private FactoryBuildingLayoutAsset authoredLayout = null!;
    private readonly List<Factory3DTestBuildingCreatorWindow> createdWindows = new();
    private Factory3DTestBuildingCreatorWindow window = null!;
    private NetworkManager networkManager = null!;
    private string databasePath = string.Empty;
    private FieldInfo instanceField = null!;
    private FieldInfo configuredDatabasePathField = null!;
    private object previousInstance = null!;
    private string previousConfiguredDatabasePath = string.Empty;
    private SceneSetup[] previousSceneSetup = Array.Empty<SceneSetup>();
    private bool runtimeStaticsCaptured;

    [UnityTest]
    public IEnumerator ActualPlayModeLifecycleUsesCallbacksAndUpdateWithoutSceneViewRepaint()
    {
        Assert.That(EditorApplication.isPlaying, Is.False);
        Assert.That(EditorSettings.enterPlayModeOptionsEnabled, Is.True);
        Assert.That(
            EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload),
            Is.True,
            "This integration test keeps its headless EditorWindow instance across the real transition.");
        Assert.That(
            EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableSceneReload),
            Is.True,
            "The isolated fixture must remain loaded across the real transition.");

        CaptureAndIsolateRuntimeStatics();
        databasePath = Path.Combine(
            Application.temporaryCachePath,
            $"factory-3d-proxy-lifecycle-{Guid.NewGuid():N}.db");
        Application.logMessageReceived += CaptureLifecycleError;

        CreateIsolatedFixture();

        // Creating and destroying headless EditorWindow instances exercises the real
        // OnEnable/OnDisable lifecycle while avoiding GUI and SceneView repaint dependencies.
        for (var cycle = 0; cycle < 2; cycle++)
        {
            var cycleWindow = CreateWindow();
            yield return WaitForCondition(
                () => HasProxyChildren(fixtureScene),
                5f,
                $"The authored proxy did not appear during lifecycle cycle {cycle}.",
                () => DescribeWindow(cycleWindow));
            cycleWindow.Close();
            yield return WaitForCondition(
                () => FindProxy(fixtureScene) is null,
                5f,
                $"Window lifecycle cycle {cycle} left a disposable proxy behind.",
                () => DescribeWindow(cycleWindow));
            if (cycleWindow is not null && cycleWindow)
            {
                UnityEngine.Object.DestroyImmediate(cycleWindow);
            }
        }

        window = CreateWindow();
        yield return WaitForCondition(
            () => HasProxyChildren(fixtureScene),
            5f,
            "The authored proxy did not appear through EditorApplication.update.");
        var authoredCenter = GetSlabCenter(fixtureScene);

        yield return new EnterPlayMode(expectDomainReload: false);
        Assert.That(Application.isPlaying, Is.True);
        yield return null;

        var transitionProxy = FindProxy(fixtureScene);
        Assert.That(transitionProxy, Is.Not.Null);
        Assert.That(
            transitionProxy!.transform.childCount,
            Is.Zero,
            "Entered Play Mode must keep the authored proxy cleared before runtime authority is ready.");
        Assert.That(
            NAIStateManager.Instance is null || !NAIStateManager.Instance.IsInitialized,
            Is.True,
            "The runtime authority must still be absent or unready at the initial Play Mode callback.");

        var sceneManager = UnityEngine.Object.FindAnyObjectByType<GameSceneManager>(FindObjectsInactive.Include);
        Assert.That(sceneManager, Is.Not.Null);
        networkManager = UnityEngine.Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        Assert.That(networkManager, Is.Not.Null);
        Assert.That(networkManager!.ServerManager.Started, Is.False);
        Assert.That(
            sceneManager!.ConfigureOutsideTestStatePath(databasePath),
            Is.True,
            "The runtime authority must be configured with the isolated database before startup.");

        networkManager.ServerManager.StartConnection();
        yield return WaitForCondition(
            () => NAIStateManager.Instance is { IsInitialized: true },
            10f,
            "The runtime authority did not initialize through the network lifecycle.");
        Assert.That(NAIStateManager.Instance!.TryGetBuildingRecord(
                LifecycleBuildingId,
                out var runtimeRecord),
            Is.True);
        Assert.That(runtimeRecord!.AnchorCell, Is.EqualTo(RuntimeAnchor));

        yield return WaitForCondition(
            () => HasProxyChildren(fixtureScene),
            5f,
            "EditorApplication.update did not rebuild the proxy after runtime authority initialization.");
        var runtimeCenter = GetSlabCenter(fixtureScene);
        var expectedRuntimeCenter = GetExpectedSlabCenter(RuntimeAnchor);
        Assert.That(runtimeCenter.x, Is.EqualTo(expectedRuntimeCenter.x).Within(0.0001f));
        Assert.That(runtimeCenter.z, Is.EqualTo(expectedRuntimeCenter.z).Within(0.0001f));
        Assert.That(
            runtimeCenter.x == authoredCenter.x && runtimeCenter.z == authoredCenter.z,
            Is.False,
            "The rebuilt proxy must use the initialized runtime record rather than the authored preview record.");

        networkManager.ServerManager.StopConnection(true);
        yield return WaitForCondition(
            () => !networkManager.ServerManager.Started,
            10f,
            "The isolated runtime authority did not stop before leaving Play Mode.");

        yield return new ExitPlayMode();
        Assert.That(Application.isPlaying, Is.False);
        yield return WaitForCondition(
            () => HasProxyChildren(fixtureScene),
            5f,
            "EnteredEditMode/update did not restore the authored proxy preview.",
            () => DescribeWindow(window));
        var restoredCenter = GetSlabCenter(fixtureScene);
        Assert.That(restoredCenter.x, Is.EqualTo(authoredCenter.x).Within(0.0001f));
        Assert.That(restoredCenter.z, Is.EqualTo(authoredCenter.z).Within(0.0001f));
        Assert.That(lifecycleErrors, Is.Empty, string.Join("\n", lifecycleErrors));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (EditorApplication.isPlaying)
        {
            if (networkManager is not null
                && networkManager
                && networkManager.ServerManager.Started)
            {
                networkManager.ServerManager.StopConnection(true);
                yield return WaitForCondition(
                    () => !networkManager.ServerManager.Started,
                    10f,
                    "The isolated runtime authority did not stop during test cleanup.");
            }

            yield return new ExitPlayMode();
        }

        Application.logMessageReceived -= CaptureLifecycleError;
        foreach (var createdWindow in createdWindows)
        {
            if (createdWindow is not null && createdWindow)
            {
                createdWindow.Close();
                UnityEngine.Object.DestroyImmediate(createdWindow);
            }
        }

        createdWindows.Clear();

        foreach (var proxy in Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>())
        {
            if (proxy is not null
                && proxy
                && proxy.gameObject.scene == fixtureScene)
            {
                UnityEngine.Object.DestroyImmediate(proxy.gameObject);
            }
        }

        if (creatorObject is not null && creatorObject)
        {
            UnityEngine.Object.DestroyImmediate(creatorObject);
        }

        if (gridObject is not null && gridObject)
        {
            UnityEngine.Object.DestroyImmediate(gridObject);
        }

        if (previousSceneSetup.Length > 0)
        {
            EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
        }
        else
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        if (bootstrapScene.IsValid() && bootstrapScene.isLoaded)
        {
            if (fixtureScene.IsValid()
                && fixtureScene.isLoaded
                && SceneManager.GetActiveScene() != fixtureScene)
            {
                SceneManager.SetActiveScene(fixtureScene);
            }

            EditorSceneManager.CloseScene(bootstrapScene, true);
        }

        if (fixtureScene.IsValid() && fixtureScene.isLoaded)
        {
            EditorSceneManager.CloseScene(fixtureScene, true);
        }

        if (!string.IsNullOrWhiteSpace(authoredLayoutPath))
        {
            AssetDatabase.DeleteAsset(authoredLayoutPath);
        }
        else if (authoredLayout is not null && authoredLayout)
        {
            UnityEngine.Object.DestroyImmediate(authoredLayout);
        }

        if (!string.IsNullOrWhiteSpace(fixtureScenePath))
        {
            AssetDatabase.DeleteAsset(fixtureScenePath);
        }

        DeleteDatabaseFiles();
        RestoreRuntimeStatics();
    }

    private void CreateIsolatedFixture()
    {
        previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
        fixtureScene = EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        gridObject = new GameObject("Proxy Lifecycle Grid");
        grid = gridObject.AddComponent<SceneGrid>();
        creatorObject = new GameObject("Proxy Lifecycle Creator");
        fixtureCreator = creatorObject.AddComponent<TestBuildingCreator>();
        SetPrivateField(fixtureCreator, "grid", grid);

        authoredLayout = ScriptableObject.CreateInstance<FactoryBuildingLayoutAsset>();
        var fixtureToken = Guid.NewGuid().ToString("N");
        authoredLayoutPath = $"Assets/Scenes/Factory3DProxyLifecycleLayout-{fixtureToken}.asset";
        AssetDatabase.CreateAsset(authoredLayout, authoredLayoutPath);
        var authoredRecord = new BuildingRecord(
            LifecycleBuildingId,
            AuthoredAnchor,
            LifecycleFootprint,
            1,
            Array.Empty<BuildingRecord.DoorPlacement>());
        Assert.That(
            authoredLayout.TryAddRecord(authoredRecord, out var layoutError),
            Is.True,
            layoutError);
        fixtureCreator.SetAuthoredLayout(authoredLayout);

        fixtureScenePath = $"Assets/Scenes/Factory3DProxyLifecycleScene-{fixtureToken}.unity";
        Assert.That(
            EditorSceneManager.SaveScene(fixtureScene, fixtureScenePath),
            Is.True,
            "The isolated fixture scene must be saved temporarily so real Play Mode transitions can restore it.");

        var bootstrapPath = "Assets/Scenes/Bootstrap.unity";
        bootstrapScene = EditorSceneManager.OpenScene(
            bootstrapPath,
            OpenSceneMode.Additive);
        Assert.That(bootstrapScene.IsValid(), Is.True);
        if (SceneManager.GetActiveScene() != fixtureScene)
        {
            Assert.That(SceneManager.SetActiveScene(fixtureScene), Is.True);
        }

        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(fixtureScene));
        SeedIsolatedRuntimeDatabase();
    }

    private void SeedIsolatedRuntimeDatabase()
    {
        var snapshot = new FactoryWorldSnapshot();
        var buildingGuid = Guid.NewGuid();
        snapshot.Buildings.Add(new FactoryWorldBuildingRecord(
            buildingGuid,
            "outside-test-building",
            RuntimeAnchor,
            LifecycleFootprint,
            1,
            false,
            LifecycleBuildingId));
        snapshot.Floors.Add(new FactoryWorldFloorRecord(
            Guid.NewGuid(),
            buildingGuid,
            0,
            "Lifecycle Floor",
            0f,
            0f,
            new Vector2(0.5f, 0.5f)));
        new FactoryWorldSqliteStore(databasePath).Save(snapshot);
    }

    private Factory3DTestBuildingCreatorWindow CreateWindow()
    {
        var result = ScriptableObject.CreateInstance<Factory3DTestBuildingCreatorWindow>();
        SetPrivateField(result, "creator", fixtureCreator);
        RefreshWindowRecords(result);
        result.ShowUtility();
        createdWindows.Add(result);
        return result;
    }

    private static void RefreshWindowRecords(Factory3DTestBuildingCreatorWindow window)
    {
        var method = typeof(Factory3DTestBuildingCreatorWindow).GetMethod(
            "RefreshRecords",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(method, Is.Not.Null);
        method!.Invoke(window, null);
    }

    private Vector3 GetSlabCenter(Scene scene)
    {
        var proxy = FindProxy(scene);
        Assert.That(proxy, Is.Not.Null);
        var slab = proxy!.transform.Find(
            $"{Factory3DOutsideTestProxyAssembler.GetBuildingName(LifecycleBuildingId)}/"
            + $"{Factory3DOutsideTestProxyAssembler.GetFloorName(0)}/"
            + Factory3DOutsideTestProxyAssembler.FloorSlabName);
        Assert.That(slab, Is.Not.Null);
        var mesh = slab!.GetComponent<MeshFilter>()!.sharedMesh;
        Assert.That(mesh, Is.Not.Null);
        return slab.TransformPoint(mesh!.bounds.center);
    }

    private Vector3 GetExpectedSlabCenter(Vector3Int anchor)
    {
        return grid.CreateSpatialAdapter().LogicalToWorld3D(
            new FactoryLogicalLocation(
                LifecycleBuildingId,
                0,
                new Vector2(
                    anchor.x + LifecycleFootprint.x * 0.5f,
                    anchor.y + LifecycleFootprint.y * 0.5f)),
            0f);
    }

    private static Factory3DOutsideTestProxyView FindProxy(Scene scene)
    {
        foreach (var candidate in Resources.FindObjectsOfTypeAll<Factory3DOutsideTestProxyView>())
        {
            if (candidate is not null
                && candidate
                && candidate.gameObject.scene == scene)
            {
                return candidate;
            }
        }

        return null!;
    }

    private bool HasProxyChildren(Scene scene)
    {
        var proxy = FindProxy(scene);
        return proxy is not null
            && proxy
            && proxy.transform.childCount > 0;
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        float timeout,
        string failureMessage,
        Func<string> diagnostics = null!)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                var details = diagnostics is null
                    ? failureMessage
                    : failureMessage + "\n" + diagnostics();
                Assert.Fail(details);
            }

            yield return null;
        }
    }

    private string DescribeWindow(Factory3DTestBuildingCreatorWindow window)
    {
        var creator = GetPrivateField(window, "creator") as TestBuildingCreator;
        var reloadedCreator = creatorObject is not null && creatorObject
            ? creatorObject.GetComponent<TestBuildingCreator>()
            : null;
        var records = GetPrivateField(window, "records") as ICollection;
        var proxyView = GetPrivateField(window, "proxyView") as Factory3DOutsideTestProxyView;
        var initialized = (bool)GetPrivateField(window, "proxyLifecycleInitialized")!;
        var transition = (bool)GetPrivateField(window, "proxyLifecycleTransition")!;
        var proxyChildren = proxyView is not null && proxyView
            ? proxyView.transform.childCount.ToString()
            : "n/a";
        return $"creator={(creator is not null && creator ? creator.name : "null")}, "
            + $"fixtureCreator={(fixtureCreator is not null && fixtureCreator ? fixtureCreator.name : "null")}, "
            + $"creatorObject={(creatorObject is not null && creatorObject ? creatorObject.name : "null")}, "
            + $"reloadedCreator={(reloadedCreator is not null && reloadedCreator ? reloadedCreator.name : "null")}, "
            + $"records={records?.Count ?? -1}, initialized={initialized}, transition={transition}, "
            + $"proxy={(proxyView is not null && proxyView ? proxyView.name : "null")}, "
            + $"proxyChildren={proxyChildren}, activeScene={SceneManager.GetActiveScene().name}, "
            + $"fixtureLoaded={fixtureScene.IsValid() && fixtureScene.isLoaded}";
    }

    private static object GetPrivateField(
        Factory3DTestBuildingCreatorWindow window,
        string fieldName)
    {
        var field = typeof(Factory3DTestBuildingCreatorWindow).GetField(
            fieldName,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(field, Is.Not.Null);
        return field!.GetValue(window)!;
    }

    private void CaptureAndIsolateRuntimeStatics()
    {
        instanceField = typeof(NAIStateManager).GetField(
            "instance",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        configuredDatabasePathField = typeof(NAIStateManager).GetField(
            "configuredDatabasePath",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.That(instanceField, Is.Not.Null);
        Assert.That(configuredDatabasePathField, Is.Not.Null);
        previousInstance = instanceField.GetValue(null);
        previousConfiguredDatabasePath = (string)configuredDatabasePathField.GetValue(null)!;
        instanceField.SetValue(null, null);
        configuredDatabasePathField.SetValue(null, string.Empty);
        runtimeStaticsCaptured = true;
    }

    private void RestoreRuntimeStatics()
    {
        if (!runtimeStaticsCaptured)
        {
            return;
        }

        instanceField.SetValue(null, previousInstance);
        configuredDatabasePathField.SetValue(null, previousConfiguredDatabasePath);
        runtimeStaticsCaptured = false;
    }

    private void DeleteDatabaseFiles()
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
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

    private void CaptureLifecycleError(string message, string stackTrace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception or LogType.Assert)
        {
            lifecycleErrors.Add($"{message}\n{stackTrace}");
        }
    }
}
