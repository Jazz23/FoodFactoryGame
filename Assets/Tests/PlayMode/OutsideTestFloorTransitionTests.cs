// Verifies the two-floor scene lifecycle and persistent OutsideTest state.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Managing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public sealed class OutsideTestFloorTransitionTests
{
    private sealed class FloorSnapshot
    {
        public FloorSnapshot(
            uint newBuildingInstanceId,
            int newFloorIndex,
            string newLabel,
            float newProductionRate,
            Vector2 newMarkerPosition,
            FactoryEntitySnapshot newEntity,
            float newAccumulatedProduction = 0f)
        {
            BuildingInstanceId = newBuildingInstanceId;
            FloorIndex = newFloorIndex;
            Label = newLabel;
            ProductionRate = newProductionRate;
            MarkerPosition = newMarkerPosition;
            Entity = newEntity;
            AccumulatedProduction = newAccumulatedProduction;
        }

        public uint BuildingInstanceId { get; }
        public int FloorIndex { get; }
        public string Label { get; }
        public float ProductionRate { get; }
        public float AccumulatedProduction { get; }
        public Vector2 MarkerPosition { get; }
        public FactoryEntitySnapshot Entity { get; }
    }

    private readonly List<string> sceneGridErrors = new();
    private readonly List<string> unexpectedUnityErrors = new();
    private NetworkManager networkManager = null!;
    private string savePath = string.Empty;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Application.logMessageReceived += CaptureSceneGridError;
        Application.logMessageReceived += CaptureUnexpectedUnityError;
        savePath = Path.Combine(
            Application.temporaryCachePath,
            $"outside-test-floor-{Guid.NewGuid():N}.db");
        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return null;

        networkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
        var sceneManager = UnityEngine.Object.FindFirstObjectByType<GameSceneManager>();
        Assert.That(sceneManager, Is.Not.Null);
        Assert.That(
            sceneManager.ConfigureOutsideTestStatePath(savePath),
            Is.True,
            "The isolated PlayMode save path must be configured before FishNet starts.");
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        yield return WaitForCondition(
            () => networkManager.ServerManager.Started
                && networkManager.ClientManager.Started
                && PlayerSceneTransition.LocalOwner is not null,
            10f,
            "FishNet host/client player did not start.");
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (networkManager is not null)
        {
            if (networkManager.ClientManager.Started)
            {
                networkManager.ClientManager.StopConnection();
            }

            if (networkManager.ServerManager.Started)
            {
                networkManager.ServerManager.StopConnection(true);
            }

            yield return WaitForCondition(
                () => !networkManager.ClientManager.Started
                    && !networkManager.ServerManager.Started,
                10f,
                "FishNet did not stop before the isolated save file cleanup.");
            yield return null;
        }

        if (File.Exists(savePath))
        {
            File.Delete(savePath);
        }

        Application.logMessageReceived -= CaptureSceneGridError;
        Application.logMessageReceived -= CaptureUnexpectedUnityError;
        Assert.That(unexpectedUnityErrors, Is.Empty, string.Join("\n", unexpectedUnityErrors));
    }

    [UnityTest]
    public IEnumerator MachineRequestsPersistAcrossTravelAndRestart()
    {
        yield return WaitForCondition(() => PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest",
            10f, "Player did not reach the world.");
        var player = PlayerSceneTransition.LocalOwner;
        var manager = GameSceneManager.Instance;
        Assert.That(manager.CanEditCurrentFloorMachines(player), Is.False);
        Assert.That(manager.TryAddCurrentFloorMachine(player, Vector2.one, out _, out _), Is.False);
        manager.TryGetOutsideTestFloorState(2, 0, out var floor);
        floor.SetEntities(Array.Empty<FactoryEntityRecord>());
        yield return EnterMachineTestBuilding();

        // The debug selection intentionally differs from the occupied floor.
        player.GetComponent<OutsideTestFloorDebugPanel>().SelectFloor(2, 1);
        var addButton = FindMachineButton(player, "Add Test Machine");
        Assert.That(addButton.interactable, Is.True);
        addButton.onClick.Invoke();
        yield return WaitForCondition(() => floor.Entities.Count == 1, 3f, "First machine request failed.");
        var firstId = floor.Entities[0].EntityId;
        player.RequestAddCurrentFloorMachine(new Vector2(2f, 1f));
        yield return WaitForCondition(() => floor.Entities.Count == 2, 3f, "Second machine request failed.");
        var survivorId = floor.Entities[1].EntityId;
        var survivor = floor.Entities[1];
        survivor.SetState(
            survivor.EntityId,
            survivor.DefinitionId,
            survivor.LogicalPosition,
            survivor.CycleRate,
            survivor.CycleProgress,
            survivor.ProducedCount,
            FactoryEntityRecord.OutputCapacity - 1);
        yield return WaitForCondition(() => HasMachineView(player.gameObject.scene, firstId)
            && HasMachineView(player.gameObject.scene, survivorId), 3f, "Machine views did not appear.");
        FindMachineButton(player, "Next Machine").onClick.Invoke();
        var removeButton = FindMachineButton(player, "Remove Machine");
        Assert.That(removeButton.interactable, Is.True);
        removeButton.onClick.Invoke();
        yield return WaitForCondition(() => floor.Entities.Count == 1
            && !HasMachineView(player.gameObject.scene, firstId), 3f, "Removed machine view remains.");
        yield return WaitForCondition(() =>
        {
            if (!manager.RequestFloorTransition(player.NetworkObject, 1))
            {
                return false;
            }

            Assert.That(manager.CanEditCurrentFloorMachines(player), Is.False, "Edits allowed during pending travel.");
            Assert.That(manager.TryRemoveCurrentFloorMachine(player, survivorId, out _), Is.False);
            return true;
        }, 3f, "Upper floor transition rejected.");
        yield return WaitForCondition(() => player.TryGetCurrentOutsideTestFloor(out var building, out var index)
            && building == 2 && index == 1 && !player.IsTransitioning, 10f, "Upper floor arrival failed.");
        yield return WaitForCondition(() => manager.RequestFloorTransition(player.NetworkObject, 0),
            3f, "Ground floor transition rejected.");
        yield return WaitForCondition(() => player.TryGetCurrentOutsideTestFloor(out var building, out var index)
            && building == 2 && index == 0 && !player.IsTransitioning
            && HasMachineView(player.gameObject.scene, survivorId), 10f, "Survivor view did not restore.");
        Assert.That(HasMachineView(player.gameObject.scene, firstId), Is.False);
        yield return WaitForCondition(
            () => floor.Entities[0].OutputCount == FactoryEntityRecord.OutputCapacity,
            3f,
            "Seeded machine did not fill its output buffer.");
        var lifetimeAtCapacity = floor.Entities[0].ProducedCount;
        yield return ExitMachineTestBuilding();
        yield return new WaitForSeconds(0.5f);
        Assert.That(floor.Entities[0].OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(lifetimeAtCapacity));
        Assert.That(manager.SaveOutsideTestFloorState(), Is.True);
        yield return RestartMachineTestHost();
        manager = GameSceneManager.Instance;
        manager.TryGetOutsideTestFloorState(2, 0, out floor);
        Assert.That(floor.Entities, Has.Count.EqualTo(1));
        Assert.That(floor.Entities[0].EntityId, Is.EqualTo(survivorId));
        Assert.That(floor.Entities[0].DefinitionId, Is.EqualTo("test-machine"));
        Assert.That(floor.Entities[0].LogicalPosition, Is.EqualTo(new Vector2(2f, 1f)));
        Assert.That(floor.Entities[0].OutputCount, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(lifetimeAtCapacity));
        yield return EnterMachineTestBuilding();
        player = PlayerSceneTransition.LocalOwner;
        yield return WaitForCondition(() => HasMachineView(player.gameObject.scene, survivorId),
            3f, "Restarted machine view did not restore.");
        FindMachineButton(player, "Next Machine").onClick.Invoke();
        yield return null;
        var lifetimeBeforeDrain = floor.Entities[0].ProducedCount;
        var outputBeforeDrain = floor.Entities[0].OutputCount;
        Assert.That(outputBeforeDrain, Is.EqualTo(FactoryEntityRecord.OutputCapacity));
        var drainButton = FindMachineButton(player, "Drain Output");
        Assert.That(drainButton.interactable, Is.True);
        drainButton.onClick.Invoke();
        yield return WaitForCondition(
            () => floor.Entities.Count == 1 && floor.Entities[0].OutputCount == 0,
            3f,
            "Drain output request did not clear the buffer.");
        Assert.That(floor.Entities[0].ProducedCount, Is.EqualTo(lifetimeBeforeDrain));
        yield return WaitForCondition(
            () => floor.Entities[0].ProducedCount > lifetimeBeforeDrain
                && floor.Entities[0].OutputCount > 0,
            3f,
            "Production did not resume after draining output.");
        player.RequestRemoveCurrentFloorMachine(survivorId);
        yield return WaitForCondition(() => floor.Entities.Count == 0
            && !HasMachineView(player.gameObject.scene, survivorId), 3f, "Final removal failed.");
        yield return ExitMachineTestBuilding();
        Assert.That(manager.SaveOutsideTestFloorState(), Is.True);
        yield return RestartMachineTestHost();
        GameSceneManager.Instance.TryGetOutsideTestFloorState(2, 0, out floor);
        Assert.That(floor.Entities, Is.Empty);
        yield return EnterMachineTestBuilding();
        yield return null;
        Assert.That(floor.Entities, Is.Empty);
        Assert.That(HasMachineView(PlayerSceneTransition.LocalOwner.gameObject.scene, survivorId), Is.False);
    }

    [UnityTest]
    public IEnumerator InterFloorTransferPersistsWhenInteriorsUnloadAndRestart()
    {
        yield return WaitForCondition(
            () => PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest",
            10f,
            "Player did not reach the world.");
        var player = PlayerSceneTransition.LocalOwner;
        var manager = GameSceneManager.Instance;
        Assert.That(manager.TryGetOutsideTestFloorState(2, 0, out var sourceFloor), Is.True);
        Assert.That(manager.TryGetOutsideTestFloorState(2, 1, out var destinationFloor), Is.True);
        sourceFloor.SetEntities(Array.Empty<FactoryEntityRecord>());
        destinationFloor.SetEntities(Array.Empty<FactoryEntityRecord>());

        yield return EnterMachineTestBuilding();
        FindMachineButton(player, "Add Test Machine").onClick.Invoke();
        yield return WaitForCondition(
            () => sourceFloor.Entities.Count == 1,
            3f,
            "The source machine request failed.");
        var sourceId = sourceFloor.Entities[0].EntityId;

        yield return WaitForCondition(
            () => manager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The destination floor transition was rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 1
                && !player.IsTransitioning,
            10f,
            "The player did not reach the destination floor.");
        FindMachineButton(player, "Add Test Storage").onClick.Invoke();
        yield return WaitForCondition(
            () => destinationFloor.Entities.Count == 1,
            3f,
            "The destination storage request failed.");
        var destinationId = destinationFloor.Entities[0].EntityId;

        yield return WaitForCondition(
            () => manager.RequestFloorTransition(player.NetworkObject, 0),
            3f,
            "The source floor transition was rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 0
                && !player.IsTransitioning,
            10f,
            "The player did not return to the source floor.");

        sourceFloor.Entities[0].SetState(
            sourceId,
            "test-machine",
            Vector2.one,
            0f,
            0f,
            17,
            3);
        destinationFloor.Entities[0].SetState(
            destinationId,
            FactoryEntityRecord.StorageDefinitionId,
            Vector2.one,
            0f,
            0f,
            0,
            0);

        FindMachineButton(player, "Next Machine").onClick.Invoke();
        yield return null;
        FindMachineButton(player, "Destination Floor 1 Button").onClick.Invoke();
        yield return null;
        FindMachineButton(player, $"Destination Storage {destinationId} Button").onClick.Invoke();
        yield return null;
        var connectButton = FindMachineButton(player, "Connect");
        Assert.That(connectButton.interactable, Is.True);
        connectButton.onClick.Invoke();
        yield return WaitForCondition(
            () => manager.GetOutsideTestConnections().Length == 1,
            3f,
            "The source and destination were not connected.");

        yield return ExitMachineTestBuilding();
        yield return WaitForCondition(
            () => manager.OutsideTestLoadedInteriorCount == 0
                && sourceFloor.Entities[0].OutputCount == 0
                && destinationFloor.Entities[0].OutputCount == 3,
            3f,
            "The unloaded inter-floor transfer did not conserve all three items.");
        Assert.That(sourceFloor.Entities[0].ProducedCount, Is.EqualTo(17));
        Assert.That(destinationFloor.Entities[0].ProducedCount, Is.Zero);
        Assert.That(manager.SaveOutsideTestFloorState(), Is.True);

        var savedData = new OutsideTestFloorSqliteStore().Load(savePath);
        Assert.That(savedData.Version, Is.EqualTo(OutsideTestFloorStateOwner.CurrentSaveVersion));
        Assert.That(savedData.Connections, Has.Count.EqualTo(1));
        Assert.That(savedData.Connections[0].Source,
            Is.EqualTo(new FactoryEntityEndpoint(2, 0, sourceId)));
        Assert.That(savedData.Connections[0].Destination,
            Is.EqualTo(new FactoryEntityEndpoint(2, 1, destinationId)));

        yield return RestartMachineTestHost();
        manager = GameSceneManager.Instance;
        player = PlayerSceneTransition.LocalOwner;
        Assert.That(manager.GetOutsideTestConnections(), Has.Length.EqualTo(1));
        Assert.That(manager.TryGetOutsideTestFloorState(2, 0, out sourceFloor), Is.True);
        Assert.That(manager.TryGetOutsideTestFloorState(2, 1, out destinationFloor), Is.True);
        Assert.That(sourceFloor.Entities[0].OutputCount, Is.Zero);
        Assert.That(destinationFloor.Entities[0].OutputCount, Is.EqualTo(3));

        yield return EnterMachineTestBuilding();
        yield return WaitForCondition(
            () => HasEntityLabelText(
                player.gameObject.scene,
                sourceId,
                "0/100 test-product"),
            3f,
            "The restored source output label did not hydrate.");
        yield return WaitForCondition(
            () => manager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The restored destination floor transition was rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 1
                && !player.IsTransitioning,
            10f,
            "The player did not revisit the destination floor.");
        yield return WaitForCondition(
            () => HasEntityLabelText(
                player.gameObject.scene,
                destinationId,
                "Stored 3/100 test-product"),
            3f,
            "The restored storage label did not hydrate.");

        FindMachineButton(player, "Next Machine").onClick.Invoke();
        yield return null;
        var disconnectButton = FindMachineButton(player, "Disconnect");
        Assert.That(disconnectButton.interactable, Is.True);
        disconnectButton.onClick.Invoke();
        yield return WaitForCondition(
            () => manager.GetOutsideTestConnections().Length == 0,
            3f,
            "Disconnect did not remove the connection.");
        Assert.That(destinationFloor.Entities[0].OutputCount, Is.EqualTo(3));

        yield return WaitForCondition(
            () => manager.RequestFloorTransition(player.NetworkObject, 0),
            3f,
            "The source floor transition after disconnect was rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 0
                && !player.IsTransitioning,
            10f,
            "The player did not return to the source floor after disconnect.");
        FindMachineButton(player, "Next Machine").onClick.Invoke();
        yield return null;
        FindMachineButton(player, "Destination Floor 1 Button").onClick.Invoke();
        yield return null;
        FindMachineButton(player, $"Destination Storage {destinationId} Button").onClick.Invoke();
        yield return null;
        FindMachineButton(player, "Connect").onClick.Invoke();
        yield return WaitForCondition(
            () => manager.GetOutsideTestConnections().Length == 1,
            3f,
            "Reconnect did not restore the connection.");

        yield return WaitForCondition(
            () => manager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The destination floor transition before endpoint removal was rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 1
                && !player.IsTransitioning,
            10f,
            "The player did not reach the destination before endpoint removal.");
        FindMachineButton(player, "Next Machine").onClick.Invoke();
        yield return null;
        FindMachineButton(player, "Remove Machine").onClick.Invoke();
        yield return WaitForCondition(
            () => destinationFloor.Entities.Count == 0
                && manager.GetOutsideTestConnections().Length == 0,
            3f,
            "Removing the storage endpoint did not clean up the connection.");
    }

    private IEnumerator EnterMachineTestBuilding()
    {
        var player = PlayerSceneTransition.LocalOwner;
        var portal = FindPortal("OutsideTest", candidate => candidate.BuildingInstanceId == 2);
        Assert.That(portal, Is.Not.Null);
        player.ServerTeleport(portal.transform.position);
        yield return WaitForCondition(() => GameSceneManager.Instance.RequestTransition(player.NetworkObject, portal),
            3f, "Machine test building entry rejected.");
        yield return WaitForCondition(() => player.TryGetCurrentOutsideTestFloor(out var building, out var index)
            && building == 2 && index == 0 && !player.IsTransitioning
            && GameSceneManager.Instance.CanEditCurrentFloorMachines(player), 10f, "Machine test building entry failed.");
    }

    private IEnumerator ExitMachineTestBuilding()
    {
        var player = PlayerSceneTransition.LocalOwner;
        var portal = FindPortal(player.gameObject.scene, candidate => candidate.Destination == SceneDestination.World);
        Assert.That(portal, Is.Not.Null);
        player.ServerTeleport(portal.transform.position);
        yield return WaitForCondition(() => GameSceneManager.Instance.RequestTransition(player.NetworkObject, portal),
            3f, "Machine test building exit rejected.");
        yield return WaitForCondition(() => player.gameObject.scene.name == "OutsideTest"
            && !player.IsTransitioning && GameSceneManager.Instance.OutsideTestLoadedInteriorCount == 0,
            10f, "Interiors did not unload after exit.");
    }

    private IEnumerator RestartMachineTestHost()
    {
        yield return StopNetworking();
        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return null;
        networkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
        Assert.That(GameSceneManager.Instance.ConfigureOutsideTestStatePath(savePath), Is.True);
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        yield return WaitForCondition(() => PlayerSceneTransition.LocalOwner is not null
            && PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest",
            10f, "Restarted machine test host did not arrive outside.");
    }

    private static Button FindMachineButton(PlayerSceneTransition player, string name)
    {
        foreach (var button in player.GetComponentsInChildren<Button>(true))
        {
            if (button.name == name)
            {
                return button;
            }
        }

        Assert.Fail($"Missing machine debug button: {name}");
        return null!;
    }

    private static bool HasMachineView(Scene scene, uint entityId)
    {
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
        {
            if (renderer.gameObject.scene == scene && renderer.gameObject.name == $"Factory Entity {entityId}")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasEntityLabelText(Scene scene, uint entityId, string expectedText)
    {
        var entityObject = FindEntityObject(scene, entityId);
        foreach (var label in entityObject.GetComponentsInChildren<TextMesh>(true))
        {
            if (label.text.Contains(expectedText, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    [UnityTest]
    public IEnumerator TwoFloorSceneLifecycleKeepsRecordsProducing()
    {
        var player = PlayerSceneTransition.LocalOwner;
        yield return WaitForCondition(
            () => player.gameObject.scene.name == "OutsideTest",
            10f,
            "Player did not reach OutsideTest.");

        var sceneManager = GameSceneManager.Instance;
        Assert.That(sceneManager.OutsideTestStatePath, Is.EqualTo(savePath));
        var groundProof = new FloorSnapshot(
            2,
            0,
            "Building 2 Ground Proof",
            1.75f,
            new Vector2(1.5f, 1.25f),
            new FactoryEntitySnapshot(
                2001u,
                "ground-proof-machine",
                new Vector2(1.25f, 1.25f),
                8f,
                0.15f,
                10));
        var upperProof = new FloorSnapshot(
            2,
            1,
            "Building 2 Upper Proof",
            2.25f,
            new Vector2(3.5f, 1.75f),
            new FactoryEntitySnapshot(
                2002u,
                "upper-proof-machine",
                new Vector2(3.25f, 1.75f),
                6f,
                0.35f,
                20));
        SeedProofState(sceneManager, groundProof, upperProof);

        var portals = UnityEngine.Object.FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        var buildingPortal = Array.Find(
            portals,
            portal => portal.BuildingInstanceId == 2
                && portal.gameObject.scene.name == "OutsideTest");
        Assert.That(buildingPortal, Is.Not.Null);
        player.transform.position = buildingPortal.transform.position;
        yield return WaitForCondition(
            () => sceneManager.RequestTransition(player.NetworkObject, buildingPortal),
            3f,
            "The building entry transition was rejected.");

        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 0
                && !player.IsTransitioning
                && IsFloorLoaded(sceneManager, 2, 0),
            10f,
            "The player did not enter building 2 floor 0.");

        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, groundProof.Entity.DefinitionId),
            3f,
            "The ground floor entity view was not hydrated.");
        AssertFloorIdentityAndPresentation(groundProof);

        yield return WaitForCondition(
            () => sceneManager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The floor transition request to floor 1 was rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 1
                && !player.IsTransitioning
                && IsFloorLoaded(sceneManager, 2, 1),
            10f,
            "The floor transition to floor 1 did not complete.");

        yield return WaitForCondition(
            () => !IsFloorLoaded(sceneManager, 2, 0),
            10f,
            "The ground floor scene was not unloaded after the elevator transition.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, upperProof.Entity.DefinitionId),
            3f,
            "The upper floor entity view was not hydrated.");
        AssertFloorIdentityAndPresentation(upperProof);
        Assert.That(sceneManager.OutsideTestLoadedInteriorCount, Is.EqualTo(1));

        yield return WaitForCondition(
            () => sceneManager.TryGetOutsideTestFloorState(2, 0, out var ground)
                && ground.Entities[0].ProducedCount > groundProof.Entity.ProducedCount,
            3f,
            "The unloaded ground floor did not continue producing.");

        yield return WaitForCondition(
            () => sceneManager.RequestFloorTransition(player.NetworkObject, 0),
            3f,
            "The floor transition request back to floor 0 was rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && player.TryGetCurrentOutsideTestFloor(out var building, out var floor)
                && building == 2
                && floor == 0
                && !player.IsTransitioning
                && IsFloorLoaded(sceneManager, 2, 0),
            10f,
            "The floor transition return to floor 0 did not complete.");
        yield return WaitForCondition(
            () => !IsFloorLoaded(sceneManager, 2, 1),
            10f,
            "The upper floor scene was not unloaded after returning to floor 0.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, groundProof.Entity.DefinitionId),
            3f,
            "The ground floor entity view was not restored.");
        AssertFloorIdentityAndPresentation(groundProof);

        var groundExitPortal = FindPortal(
            TestBuildingFloorScenes.TemplateSceneName,
            portal => portal.Destination == SceneDestination.World);
        Assert.That(
            groundExitPortal,
            Is.Not.Null,
            DescribeScenePortals(TestBuildingFloorScenes.TemplateSceneName));
        Assert.That(groundExitPortal.isActiveAndEnabled, Is.True);
        player.transform.position = groundExitPortal.transform.position;
        yield return WaitForCondition(
            () => sceneManager.RequestTransition(player.NetworkObject, groundExitPortal),
            3f,
            "The ground-floor exit transition was rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == "OutsideTest"
                && !player.IsTransitioning
                && !IsFloorLoaded(sceneManager, 2, 0)
                && !IsFloorLoaded(sceneManager, 2, 1)
                && sceneManager.OutsideTestLoadedInteriorCount == 0,
            10f,
            "The player did not return outside with both building-2 interiors unloaded.");

        var groundBeforeOfflineProduction = CaptureFloor(sceneManager, groundProof);
        var upperBeforeOfflineProduction = CaptureFloor(sceneManager, upperProof);
        yield return WaitForCondition(
            () => sceneManager.OutsideTestLoadedInteriorCount == 0
                && HasProducedMore(sceneManager, groundProof, groundBeforeOfflineProduction)
                && HasProducedMore(sceneManager, upperProof, upperBeforeOfflineProduction),
            3f,
            "Both entities did not produce while all interiors were unloaded.",
            () => DescribeFloorProduction(sceneManager, groundProof, upperProof));

        var groundAfterOfflineProduction = CaptureFloor(sceneManager, groundProof);
        var upperAfterOfflineProduction = CaptureFloor(sceneManager, upperProof);
        Assert.That(
            groundAfterOfflineProduction.Entity.ProducedCount,
            Is.GreaterThan(groundBeforeOfflineProduction.Entity.ProducedCount));
        Assert.That(
            upperAfterOfflineProduction.Entity.ProducedCount,
            Is.GreaterThan(upperBeforeOfflineProduction.Entity.ProducedCount));

        buildingPortal = FindPortal(
            "OutsideTest",
            portal => portal.BuildingInstanceId == 2);
        Assert.That(buildingPortal, Is.Not.Null);
        player.transform.position = buildingPortal.transform.position;
        yield return WaitForCondition(
            () => sceneManager.RequestTransition(player.NetworkObject, buildingPortal),
            3f,
            "The building entry transition after offline production was rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && !player.IsTransitioning,
            10f,
            "The ground floor did not reload after offline production.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, groundProof.Entity.DefinitionId),
            3f,
            "The ground floor entity did not hydrate after offline production.");
        AssertFloorIdentityAndPresentation(groundProof);
        Assert.That(
            GetEntity(sceneManager, groundProof).ProducedCount,
            Is.GreaterThanOrEqualTo(groundAfterOfflineProduction.Entity.ProducedCount));

        yield return WaitForCondition(
            () => sceneManager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The upper floor did not become available after re-entry.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && !player.IsTransitioning,
            10f,
            "The upper floor did not reload after offline production.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, upperProof.Entity.DefinitionId),
            3f,
            "The upper floor entity did not hydrate after offline production.");
        AssertFloorIdentityAndPresentation(upperProof);
        Assert.That(
            GetEntity(sceneManager, upperProof).ProducedCount,
            Is.GreaterThanOrEqualTo(upperAfterOfflineProduction.Entity.ProducedCount));

        Assert.That(
            sceneManager.TryGetOutsideTestFloorScene(2, 1, out var upperScene),
            Is.True);
        groundExitPortal = FindPortal(
            upperScene,
            portal => portal.Destination == SceneDestination.World
                && portal.isActiveAndEnabled);
        Assert.That(groundExitPortal, Is.Null);
        yield return WaitForCondition(
            () => sceneManager.RequestFloorTransition(player.NetworkObject, 0),
            3f,
            "The ground floor did not become available before saving.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && !player.IsTransitioning,
            10f,
            "The player did not return to the ground floor before saving.");
        yield return WaitForCondition(
            () => sceneManager.TryGetOutsideTestFloorScene(2, 0, out var groundScene)
                && FindPortal(
                groundScene,
                portal => portal.Destination == SceneDestination.World
                    && portal.isActiveAndEnabled) is not null,
            3f,
            "The ground-floor exit portal was not configured before saving.",
            () => DescribeScenePortals(TestBuildingFloorScenes.TemplateSceneName));
        Assert.That(
            sceneManager.TryGetOutsideTestFloorScene(2, 0, out var restoredGroundScene),
            Is.True);
        groundExitPortal = FindPortal(
            restoredGroundScene,
            portal => portal.Destination == SceneDestination.World);
        Assert.That(
            groundExitPortal,
            Is.Not.Null,
            DescribeScenePortals(TestBuildingFloorScenes.TemplateSceneName));
        Assert.That(groundExitPortal.isActiveAndEnabled, Is.True);
        player.transform.position = groundExitPortal.transform.position;
        yield return WaitForCondition(
            () => sceneManager.RequestTransition(player.NetworkObject, groundExitPortal),
            3f,
            "The ground-floor exit transition before saving was rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == "OutsideTest"
                && !player.IsTransitioning
                && sceneManager.OutsideTestLoadedInteriorCount == 0,
            10f,
            "The player did not return outside before saving.");

        Assert.That(sceneManager.SaveOutsideTestFloorState(), Is.True);
        var savedData = new OutsideTestFloorSqliteStore().Load(savePath);
        var savedGround = CaptureFloor(sceneManager, groundProof);
        var savedUpper = CaptureFloor(sceneManager, upperProof);
        Assert.That(
            savedData.Floors.Exists(record => record.Label == "Building 2 Ground Proof"),
            Is.True);
        Assert.That(
            savedData.Floors.Exists(record =>
                record.Entities.Any(entity => entity.DefinitionId == "ground-proof-machine")),
            Is.True);

        Assert.That(
            sceneManager.TrySetOutsideTestFloorState(
                2,
                0,
                "Transient test edit",
                99f,
                new Vector2(4f, 2f)),
            Is.True);
        Assert.That(sceneManager.TryGetOutsideTestFloorState(2, 0, out var alteredGround), Is.True);
        alteredGround.SetEntities(Array.Empty<FactoryEntityRecord>());
        Assert.That(sceneManager.TryGetOutsideTestFloorState(2, 1, out var alteredUpper), Is.True);
        alteredUpper.SetEntities(Array.Empty<FactoryEntityRecord>());
        yield return WaitForCondition(
            () => SceneManager.GetSceneByName("OutsideTest").isLoaded,
            3f,
            "OutsideTest was not loaded before the explicit state reload.");
        Assert.That(sceneManager.LoadOutsideTestFloorState(), Is.True);
        AssertFloorExact(sceneManager, savedGround);
        AssertFloorExact(sceneManager, savedUpper);
        var reloadedData = new OutsideTestFloorSqliteStore().Load(savePath);
        Assert.That(reloadedData.Version, Is.EqualTo(savedData.Version));
        Assert.That(reloadedData.Buildings.Count, Is.EqualTo(savedData.Buildings.Count));
        Assert.That(reloadedData.Floors.Count, Is.EqualTo(savedData.Floors.Count));
        Assert.That(reloadedData.Connections.Count, Is.EqualTo(savedData.Connections.Count));

        yield return StopNetworking();
        Assert.That(File.Exists(savePath), Is.True);
        var restartedData = new OutsideTestFloorSqliteStore().Load(savePath);
        var restartedGround = restartedData.Floors.Find(
            record => record.BuildingInstanceId == groundProof.BuildingInstanceId
                && record.FloorIndex == groundProof.FloorIndex);
        var restartedUpper = restartedData.Floors.Find(
            record => record.BuildingInstanceId == upperProof.BuildingInstanceId
                && record.FloorIndex == upperProof.FloorIndex);
        Assert.That(restartedGround, Is.Not.Null);
        Assert.That(restartedUpper, Is.Not.Null);

        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return null;
        networkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
        sceneManager = UnityEngine.Object.FindFirstObjectByType<GameSceneManager>();
        Assert.That(
            sceneManager.ConfigureOutsideTestStatePath(savePath),
            Is.True,
            "The restarted host must use the same isolated save path before FishNet starts.");
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        yield return WaitForCondition(
            () => networkManager.ServerManager.Started
                && networkManager.ClientManager.Started
                && PlayerSceneTransition.LocalOwner is not null,
            10f,
            "FishNet did not restart with the isolated save path.");
        player = PlayerSceneTransition.LocalOwner;
        yield return WaitForCondition(
            () => player.gameObject.scene.name == "OutsideTest",
            10f,
            "The restarted host player did not reach OutsideTest.");
        AssertFloorPersistedAfterRestart(sceneManager, restartedGround);
        AssertFloorPersistedAfterRestart(sceneManager, restartedUpper);
        yield return WaitForCondition(
            () => HasProducedMoreThanPersisted(sceneManager, restartedGround)
                && HasProducedMoreThanPersisted(sceneManager, restartedUpper),
            3f,
            "Production did not resume after the fresh host session.");

        buildingPortal = FindPortal("OutsideTest", portal => portal.BuildingInstanceId == 2);
        player.transform.position = buildingPortal.transform.position;
        yield return WaitForCondition(
            () => sceneManager.RequestTransition(player.NetworkObject, buildingPortal),
            3f,
            "The restarted host rejected the building entry transition.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && !player.IsTransitioning,
            10f,
            "The restarted host did not enter building 2 floor 0.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, groundProof.Entity.DefinitionId),
            3f,
            "The ground floor presentation did not hydrate after the fresh host session.");
        AssertFloorIdentityAndPresentation(groundProof);
        yield return WaitForCondition(
            () => sceneManager.RequestFloorTransition(player.NetworkObject, 1),
            3f,
            "The restarted host rejected the floor 1 transition.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == TestBuildingFloorScenes.TemplateSceneName
                && !player.IsTransitioning,
            10f,
            "The restarted host did not enter building 2 floor 1.");
        yield return WaitForCondition(
            () => HasEntityLabel(TestBuildingFloorScenes.TemplateSceneName, upperProof.Entity.DefinitionId),
            3f,
            "The upper floor presentation did not hydrate after the fresh host session.");
        AssertFloorIdentityAndPresentation(upperProof);

        Assert.That(sceneGridErrors, Is.Empty, string.Join("\n", sceneGridErrors));
    }

    private void CaptureSceneGridError(string message, string _, LogType __)
    {
        if (message.Contains("needs exactly one enabled SceneGrid.", StringComparison.Ordinal))
        {
            sceneGridErrors.Add(message);
        }
    }

    private void CaptureUnexpectedUnityError(string message, string _, LogType type)
    {
        if (type is LogType.Error or LogType.Exception or LogType.Assert
            && !message.Contains("needs exactly one enabled SceneGrid.", StringComparison.Ordinal))
        {
            unexpectedUnityErrors.Add(message);
        }
    }

    private static void SeedProofState(
        GameSceneManager sceneManager,
        FloorSnapshot groundProof,
        FloorSnapshot upperProof)
    {
        Assert.That(
            sceneManager.TrySetOutsideTestFloorState(
                groundProof.BuildingInstanceId,
                groundProof.FloorIndex,
                groundProof.Label,
                groundProof.ProductionRate,
                groundProof.MarkerPosition),
            Is.True);
        Assert.That(
            sceneManager.TrySetOutsideTestFloorState(
                upperProof.BuildingInstanceId,
                upperProof.FloorIndex,
                upperProof.Label,
                upperProof.ProductionRate,
                upperProof.MarkerPosition),
            Is.True);
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                groundProof.BuildingInstanceId,
                groundProof.FloorIndex,
                out var ground),
            Is.True);
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                upperProof.BuildingInstanceId,
                upperProof.FloorIndex,
                out var upper),
            Is.True);
        ground.SetEntities(new[] { FactoryEntityRecord.FromSnapshot(groundProof.Entity) });
        upper.SetEntities(new[] { FactoryEntityRecord.FromSnapshot(upperProof.Entity) });
    }

    private static FloorSnapshot CaptureFloor(
        GameSceneManager sceneManager,
        FloorSnapshot identity)
    {
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                identity.BuildingInstanceId,
                identity.FloorIndex,
                out var floor),
            Is.True);
        Assert.That(floor.Entities, Has.Count.EqualTo(1));
        return new FloorSnapshot(
            floor.BuildingInstanceId,
            floor.FloorIndex,
            floor.Label,
            floor.ProductionRate,
            floor.MarkerPosition,
            floor.Entities[0].ToSnapshot(),
            floor.AccumulatedProduction);
    }

    private static void AssertFloorExact(
        GameSceneManager sceneManager,
        FloorSnapshot expected)
    {
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                expected.BuildingInstanceId,
                expected.FloorIndex,
                out var floor),
            Is.True);
        Assert.That(floor.Label, Is.EqualTo(expected.Label));
        Assert.That(floor.ProductionRate, Is.EqualTo(expected.ProductionRate));
        Assert.That(floor.AccumulatedProduction, Is.EqualTo(expected.AccumulatedProduction));
        Assert.That(floor.MarkerPosition, Is.EqualTo(expected.MarkerPosition));
        Assert.That(floor.Entities, Has.Count.EqualTo(1));
        var entity = floor.Entities[0];
        Assert.That(entity.EntityId, Is.EqualTo(expected.Entity.EntityId));
        Assert.That(entity.DefinitionId, Is.EqualTo(expected.Entity.DefinitionId));
        Assert.That(entity.LogicalPosition, Is.EqualTo(expected.Entity.LogicalPosition));
        Assert.That(entity.CycleRate, Is.EqualTo(expected.Entity.CycleRate));
        Assert.That(entity.CycleProgress, Is.EqualTo(expected.Entity.CycleProgress));
        Assert.That(entity.ProducedCount, Is.EqualTo(expected.Entity.ProducedCount));
    }

    private static void AssertFloorPersistedAfterRestart(
        GameSceneManager sceneManager,
        OutsideTestFloorRecord persisted)
    {
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                persisted.BuildingInstanceId,
                persisted.FloorIndex,
                out var floor),
            Is.True);
        Assert.That(floor.Label, Is.EqualTo(persisted.Label));
        Assert.That(floor.ProductionRate, Is.EqualTo(persisted.ProductionRate));
        Assert.That(floor.MarkerPosition, Is.EqualTo(persisted.MarkerPosition));
        Assert.That(floor.Entities, Has.Count.EqualTo(persisted.Entities.Count));
        for (var index = 0; index < persisted.Entities.Count; index++)
        {
            var actual = floor.Entities[index];
            var expected = persisted.Entities[index];
            Assert.That(actual.EntityId, Is.EqualTo(expected.EntityId));
            Assert.That(actual.DefinitionId, Is.EqualTo(expected.DefinitionId));
            Assert.That(actual.LogicalPosition, Is.EqualTo(expected.LogicalPosition));
            Assert.That(actual.CycleRate, Is.EqualTo(expected.CycleRate));
            Assert.That(actual.ProducedCount, Is.GreaterThanOrEqualTo(expected.ProducedCount));
            Assert.That(actual.CycleProgress, Is.InRange(0f, 1f));
        }
    }

    private static void AssertFloorIdentityAndPresentation(FloorSnapshot expected)
    {
        var sceneManager = GameSceneManager.Instance;
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                expected.BuildingInstanceId,
                expected.FloorIndex,
                out var floor),
            Is.True);
        Assert.That(floor.Label, Is.EqualTo(expected.Label));
        Assert.That(floor.ProductionRate, Is.EqualTo(expected.ProductionRate));
        Assert.That(floor.MarkerPosition, Is.EqualTo(expected.MarkerPosition));
        Assert.That(floor.Entities, Has.Count.EqualTo(1));
        var entity = GetEntity(sceneManager, expected);
        Assert.That(entity.EntityId, Is.EqualTo(expected.Entity.EntityId));
        Assert.That(entity.DefinitionId, Is.EqualTo(expected.Entity.DefinitionId));
        Assert.That(entity.LogicalPosition, Is.EqualTo(expected.Entity.LogicalPosition));
        Assert.That(entity.CycleRate, Is.EqualTo(expected.Entity.CycleRate));
        Assert.That(entity.ProducedCount, Is.GreaterThanOrEqualTo(expected.Entity.ProducedCount));
        Assert.That(entity.CycleProgress, Is.InRange(0f, 1f));

        var sceneName = TestBuildingFloorScenes.TemplateSceneName;
        Assert.That(
            sceneManager.TryGetOutsideTestFloorScene(
                expected.BuildingInstanceId,
                expected.FloorIndex,
                out var scene),
            Is.True);
        var entityObject = FindEntityObject(scene, expected.Entity.EntityId);
        Assert.That(
            entityObject,
            Is.Not.Null,
            $"Expected Factory Entity {expected.Entity.EntityId} in scene {sceneName}. "
            + DescribeSceneTransforms(sceneName)
            + "; " + DescribeScenePresentations(sceneName)
            + $"; state={DescribeFloor(sceneManager, expected)}");
        var grid = FindSceneGrid(scene);
        var expectedWorldPosition = grid.LogicalToWorld(entity.LogicalPosition);
        Assert.That(
            Vector2.Distance(entityObject.transform.position, expectedWorldPosition),
            Is.LessThan(0.001f));
        Assert.That(HasEntityLabel(sceneName, expected.Entity.DefinitionId), Is.True);
    }

    private static FactoryEntityRecord GetEntity(
        GameSceneManager sceneManager,
        FloorSnapshot expected)
    {
        Assert.That(
            sceneManager.TryGetOutsideTestFloorState(
                expected.BuildingInstanceId,
                expected.FloorIndex,
                out var floor),
            Is.True);
        foreach (var entity in floor.Entities)
        {
            if (entity.EntityId == expected.Entity.EntityId)
            {
                return entity;
            }
        }

        var actualIds = new List<string>();
        foreach (var entity in floor.Entities)
        {
            actualIds.Add(entity is null ? "null" : entity.EntityId.ToString());
        }

        Assert.Fail(
            $"Entity {expected.Entity.EntityId} was not found on "
            + $"building {expected.BuildingInstanceId}, floor {expected.FloorIndex}. "
            + $"Actual IDs: {string.Join(",", actualIds)}.");
        return null!;
    }

    private static bool HasProducedMore(
        GameSceneManager sceneManager,
        FloorSnapshot expected,
        FloorSnapshot baseline)
    {
        return GetEntity(sceneManager, expected).ProducedCount
            > baseline.Entity.ProducedCount;
    }

    private static bool HasProducedMoreThanPersisted(
        GameSceneManager sceneManager,
        OutsideTestFloorRecord persisted)
    {
        if (!sceneManager.TryGetOutsideTestFloorState(
                persisted.BuildingInstanceId,
                persisted.FloorIndex,
                out var floor)
            || floor.Entities.Count != persisted.Entities.Count)
        {
            return false;
        }

        for (var index = 0; index < persisted.Entities.Count; index++)
        {
            if (floor.Entities[index].ProducedCount <= persisted.Entities[index].ProducedCount)
            {
                return false;
            }
        }

        return true;
    }

    private static string DescribeFloorProduction(
        GameSceneManager sceneManager,
        FloorSnapshot ground,
        FloorSnapshot upper)
    {
        var groundText = DescribeFloor(sceneManager, ground);
        var upperText = DescribeFloor(sceneManager, upper);
        return $"ground={groundText}; upper={upperText}; "
            + $"loaded interiors={sceneManager.OutsideTestLoadedInteriorCount}";
    }

    private static string DescribeFloor(
        GameSceneManager sceneManager,
        FloorSnapshot expected)
    {
        if (!sceneManager.TryGetOutsideTestFloorState(
                expected.BuildingInstanceId,
                expected.FloorIndex,
                out var floor)
            || floor.Entities.Count == 0)
        {
            return "missing";
        }

        return $"{floor.Entities[0].EntityId}:{floor.Entities[0].DefinitionId} "
            + $"{floor.Entities[0].ProducedCount} / {floor.Entities[0].CycleProgress:0.00}";
    }

    private static ScenePortal FindPortal(
        string sceneName,
        Func<ScenePortal, bool> predicate)
    {
        return FindPortal(SceneManager.GetSceneByName(sceneName), predicate);
    }

    private static ScenePortal FindPortal(
        Scene scene,
        Func<ScenePortal, bool> predicate)
    {
        var portals = UnityEngine.Object.FindObjectsByType<ScenePortal>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var portal in portals)
        {
            if (portal.gameObject.scene == scene && predicate(portal))
            {
                return portal;
            }
        }

        return null!;
    }

    private static bool IsFloorLoaded(
        GameSceneManager sceneManager,
        uint buildingInstanceId,
        int floorIndex)
    {
        return sceneManager.TryGetOutsideTestFloorScene(
                buildingInstanceId,
                floorIndex,
                out var scene)
            && scene.isLoaded;
    }

    private static GameObject FindEntityObject(string sceneName, uint entityId)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return FindEntityObject(scene, entityId);
    }

    private static GameObject FindEntityObject(Scene scene, uint entityId)
    {
        if (!scene.isLoaded)
        {
            return null!;
        }

        var expectedName = $"Factory Entity {entityId}";
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == expectedName)
                {
                    return transform.gameObject;
                }
            }
        }

        return null!;
    }

    private static string DescribeSceneTransforms(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        var names = new List<string>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var hasLabel = transform.TryGetComponent<TextMesh>(out var label);
                names.Add(!hasLabel
                    ? transform.name
                    : $"{transform.name}[{label.text.Replace("\n", "/")}]");
            }
        }

        return $"Transforms: {string.Join(",", names)}";
    }

    private static string DescribeScenePortals(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        var descriptions = new List<string>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var portal in root.GetComponentsInChildren<ScenePortal>(true))
            {
                descriptions.Add(
                    $"{portal.name}:{portal.Destination}:{portal.isActiveAndEnabled}");
            }
        }

        return $"Portals in {sceneName}: {string.Join(",", descriptions)}";
    }

    private static string DescribeScenePresentations(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        var descriptions = new List<string>();
        var type = typeof(OutsideTestFloorPresentation);
        var buildingField = type.GetField(
            "buildingInstanceId",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        var floorField = type.GetField(
            "floorIndex",
            System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic);
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var presentation in root.GetComponentsInChildren<OutsideTestFloorPresentation>(true))
            {
                descriptions.Add(
                    $"B{buildingField.GetValue(presentation)}"
                    + $"/F{floorField.GetValue(presentation)}:{presentation.enabled}");
            }
        }

        return $"Presentations in {sceneName}: {string.Join(",", descriptions)}";
    }

    private static SceneGrid FindSceneGrid(string sceneName)
    {
        var scene = SceneManager.GetSceneByName(sceneName);
        return FindSceneGrid(scene);
    }

    private static SceneGrid FindSceneGrid(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var grid in root.GetComponentsInChildren<SceneGrid>(true))
            {
                if (grid.isActiveAndEnabled)
                {
                    return grid;
                }
            }
        }

        Assert.Fail($"Scene '{scene.name}' has no enabled SceneGrid.");
        return null!;
    }

    private IEnumerator StopNetworking()
    {
        if (networkManager.ClientManager.Started)
        {
            networkManager.ClientManager.StopConnection();
        }

        if (networkManager.ServerManager.Started)
        {
            networkManager.ServerManager.StopConnection(true);
        }

        yield return WaitForCondition(
            () => !networkManager.ClientManager.Started
                && !networkManager.ServerManager.Started,
            10f,
            "FishNet did not stop before restarting the host.");
        yield return null;
    }

    private static bool HasEntityLabel(string sceneName, string definitionId)
    {
        var labels = UnityEngine.Object.FindObjectsByType<TextMesh>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var label in labels)
        {
            if (label.gameObject.scene.name == sceneName
                && label.text.Contains(definitionId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerator WaitForCondition(
        Func<bool> condition,
        float timeout,
        string failureMessage,
        Func<string> diagnostics = null)
    {
        var deadline = Time.realtimeSinceStartup + timeout;
        while (!condition())
        {
            if (Time.realtimeSinceStartup >= deadline)
            {
                Assert.Fail(diagnostics is null
                    ? failureMessage
                    : failureMessage + " " + diagnostics());
            }

            yield return null;
        }
    }
}
