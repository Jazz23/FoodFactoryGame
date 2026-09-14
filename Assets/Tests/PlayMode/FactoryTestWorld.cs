// Owns isolated PlayMode world setup, teardown, and failure diagnostics.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FishNet.Managing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class FactoryTestWorld
{
    private const uint FloorTransitionFixtureBuildingId = 2;
    private const int FloorTransitionFixtureStoryCount = 2;
    private static readonly Vector3Int FloorTransitionFixtureAnchor = new(20, 20, 0);
    private static readonly Vector2Int FloorTransitionFixtureFootprint = new(6, 5);

    private readonly List<string> consoleErrors = new();

    public NetworkManager NetworkManager { get; private set; } = null!;
    public string SavePath { get; private set; } = string.Empty;

    public IEnumerator SetUp()
    {
        SavePath = Path.Combine(
            Application.temporaryCachePath,
            $"factory-test-world-{Guid.NewGuid():N}.db");
        Application.logMessageReceived += CaptureConsoleError;
        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return null;

        NetworkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
        var sceneManager = UnityEngine.Object.FindFirstObjectByType<GameSceneManager>();
        Assert.That(sceneManager, Is.Not.Null);
        Assert.That(
            sceneManager.ConfigureOutsideTestStatePath(SavePath),
            Is.True,
            "The isolated PlayMode save path must be configured before FishNet starts.");
        NetworkManager.ServerManager.StartConnection();
        NetworkManager.ClientManager.StartConnection();
        yield return WaitForCondition(
            () => NetworkManager.ServerManager.Started
                && NetworkManager.ClientManager.Started
                && PlayerSceneTransition.LocalOwner is not null
                && PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest"
                && GameSceneManager.Instance is not null
                && GameSceneManager.Instance.StateManager is not null
                && GameSceneManager.Instance.StateManager.IsInitialized,
            10f,
            "FishNet host/client player did not start.");
        EnsureFloorTransitionFixture();
    }

    public IEnumerator TearDown()
    {
        var failed = TestContext.CurrentContext.Result.Outcome.Status
            != NUnit.Framework.Interfaces.TestStatus.Passed;
        if (failed)
        {
            WriteFailureDiagnostics();
        }

        if (NetworkManager is not null)
        {
            if (NetworkManager.ClientManager.Started)
            {
                NetworkManager.ClientManager.StopConnection();
            }

            if (NetworkManager.ServerManager.Started)
            {
                NetworkManager.ServerManager.StopConnection(true);
            }

            yield return WaitForCondition(
                () => !NetworkManager.ClientManager.Started
                    && !NetworkManager.ServerManager.Started,
                10f,
                "FishNet did not stop before the isolated save file cleanup.");
            yield return null;
        }

        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
        }

        Application.logMessageReceived -= CaptureConsoleError;
    }

    private void CaptureConsoleError(string message, string _, LogType type)
    {
        if (type is LogType.Error or LogType.Exception or LogType.Assert)
        {
            consoleErrors.Add(message);
        }
    }

    private static void EnsureFloorTransitionFixture()
    {
        var sceneManager = GameSceneManager.Instance;
        var stateManager = sceneManager.StateManager;
        if (stateManager.TryGetBuildingRecord(FloorTransitionFixtureBuildingId, out _))
        {
            return;
        }

        var creators = UnityEngine.Object.FindObjectsByType<TestBuildingCreator>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        TestBuildingCreator creator = null!;
        foreach (var candidate in creators)
        {
            if (candidate.gameObject.scene.name == "OutsideTest")
            {
                creator = candidate;
                break;
            }
        }

        Assert.That(creator, Is.Not.Null, "The OutsideTest fixture requires a building creator.");
        Assert.That(
            TestBuildingCreator.TryGetDefaultEntrance(
                FloorTransitionFixtureAnchor,
                FloorTransitionFixtureFootprint,
                out var entrance,
                out var entranceError),
            Is.True,
            entranceError);
        var record = new BuildingRecord(
            FloorTransitionFixtureBuildingId,
            FloorTransitionFixtureAnchor,
            FloorTransitionFixtureFootprint,
            FloorTransitionFixtureStoryCount,
            new[] { entrance });
        Assert.That(
            stateManager.TryRegisterBuilding(record, out var registrationError),
            Is.True,
            registrationError);

        var shell = new BuildingShellAssembler().CreateShell(
            record,
            creator,
            creator.GeneratedBuildings);
        Assert.That(shell, Is.Not.Null, "The isolated OutsideTest fixture shell could not be created.");
        Assert.That(
            sceneManager.SaveOutsideTestFloorState(),
            Is.True,
            sceneManager.LastOutsideTestError);
    }

    private void WriteFailureDiagnostics()
    {
        var player = PlayerSceneTransition.LocalOwner;
        var manager = GameSceneManager.Instance;
        var floorPanel = player is null
            ? null
            : player.GetComponentInChildren<OutsideTestFloorDebugPanel>(true);
        var diagnostic = new FactoryTestWorldDiagnostics
        {
            testName = TestContext.CurrentContext.Test.Name,
            savePath = SavePath,
            networkServerStarted = NetworkManager is not null
                && NetworkManager.ServerManager.Started,
            networkClientStarted = NetworkManager is not null
                && NetworkManager.ClientManager.Started,
            playerScene = player is null ? string.Empty : player.gameObject.scene.name,
            playerIsTransitioning = player is not null && player.IsTransitioning,
            loadedInteriorCount = manager is null ? -1 : manager.OutsideTestLoadedInteriorCount,
            machineEditDiagnostics = floorPanel is null
                ? "panel=missing"
                : floorPanel.GetMachineEditDiagnostics()
                    + $";buttonMatches={DescribeMachineButtons(player)}",
            consoleErrors = new List<string>(consoleErrors)
        };
        if (player is not null
            && player.TryGetCurrentOutsideTestFloor(
                out var buildingInstanceId,
                out var floorIndex))
        {
            diagnostic.playerBuildingInstanceId = buildingInstanceId.ToString();
            diagnostic.playerFloorIndex = floorIndex;
        }

        var directory = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Temp",
            "factory-test-diagnostics");
        Directory.CreateDirectory(directory);
        var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json";
        diagnostic.artifactPath = Path.Combine(directory, fileName);
        File.WriteAllText(diagnostic.artifactPath, JsonUtility.ToJson(diagnostic, true));
        Debug.LogWarning($"Factory test diagnostics written to {diagnostic.artifactPath}.");
    }

    private static string DescribeMachineButtons(PlayerSceneTransition player)
    {
        if (player is null)
        {
            return "none";
        }

        var descriptions = new List<string>();
        foreach (var button in player.GetComponentsInChildren<Button>(true))
        {
            if (button.name is "Add Test Machine" or "Next Machine")
            {
                descriptions.Add(
                    $"{button.name}[active={button.gameObject.activeInHierarchy},"
                    + $"enabled={button.isActiveAndEnabled},interactable={button.interactable}]" );
            }
        }

        return descriptions.Count == 0 ? "none" : string.Join(",", descriptions);
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        float timeout,
        string failureMessage)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                Assert.Fail(failureMessage);
            }

            yield return null;
        }
    }
}

[Serializable]
public sealed class FactoryTestWorldDiagnostics
{
    public string testName = string.Empty;
    public string savePath = string.Empty;
    public string artifactPath = string.Empty;
    public bool networkServerStarted;
    public bool networkClientStarted;
    public string playerScene = string.Empty;
    public bool playerIsTransitioning;
    public string playerBuildingInstanceId = string.Empty;
    public int playerFloorIndex = -1;
    public int loadedInteriorCount;
    public string machineEditDiagnostics = string.Empty;
    public List<string> consoleErrors = new();
}
