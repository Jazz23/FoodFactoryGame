// PlayMode smoke test for the automated truck route: UI placement, delivery cycle, reload, blocking, and marker.
using System;
using System.Collections;
using System.IO;
using FishNet.Managing;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class FactoryTruckRoutePlayModeTests
{
    private NetworkManager networkManager = null!;
    private string savePath = string.Empty;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        savePath = Path.Combine(
            Application.temporaryCachePath,
            $"factory-truck-playmode-{Guid.NewGuid():N}.db");
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
    }

    [UnityTest]
    public IEnumerator TruckRouteDeliversAcrossBuildingsAndSurvivesReloadAndBlocking()
    {
        yield return WaitForCondition(
            () => PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest",
            10f,
            "Player did not reach the world.");
        var player = PlayerSceneTransition.LocalOwner;
        var manager = GameSceneManager.Instance;
        var stateManager = NotAI.NAIStateManager.Instance;

        manager.TryGetOutsideTestFloorState(2, 0, out var senderFloor);
        senderFloor.SetEntities(Array.Empty<FactoryEntityRecord>());

        // Second exterior building with the receiving terminal; it has no interior scene at all.
        manager.TryGetOutsideTestFloorState(42, 0, out _);
        Assert.That(
            stateManager.TryRegisterBuilding(
                new BuildingRecord(42, new Vector3Int(40, 0, 0), new Vector2Int(6, 4), 1),
                out var registrationError),
            Is.True,
            registrationError);
        Assert.That(
            stateManager.TryAddReceivingTerminal(42, 0, new Vector2(0.5f, 0.5f), out var receiverId, out var receiverError),
            Is.True,
            receiverError);

        yield return EnterMachineTestBuilding();

        // Place the complete source -> belts -> sending terminal chain through the placement requests.
        player.RequestPlaceEquipment(FactoryEntityDefinitions.TestMachineDefinitionId, new Vector2(0.5f, 0.5f));
        player.RequestPlaceEquipment("conveyor-east", new Vector2(1.5f, 0.5f));
        player.RequestPlaceEquipment(FactoryEntityRecord.SendingTerminalDefinitionId, new Vector2(2.5f, 0.5f));
        yield return WaitForCondition(
            () => senderFloor.Entities.Count == 3,
            5f,
            "Chain placement failed.");
        FactoryEntityRecord sender = null!;
        foreach (var entity in senderFloor.Entities)
        {
            if (entity.DefinitionId == FactoryEntityRecord.SendingTerminalDefinitionId)
            {
                sender = entity;
            }
        }

        Assert.That(sender, Is.Not.Null);
        sender.SetState(sender.EntityId, sender.DefinitionId, sender.LogicalPosition, 0f, 0f, 0, 30);

        // Create the route through the same RPC the route panel uses.
        Assert.That(
            stateManager.TryGetFactoryEntityEndpoint(2, 0, sender.EntityId, out var senderEndpoint),
            Is.True);
        Assert.That(
            stateManager.TryGetFactoryEntityEndpoint(42, 0, receiverId, out var receiverEndpoint),
            Is.True);
        player.RequestCreateTruckRoute(senderEndpoint, receiverEndpoint);
        yield return WaitForCondition(
            () => stateManager.GetTruckRoutes().Count == 1,
            5f,
            "Route creation request failed.");
        var route = stateManager.GetTruckRoutes()[0];
        var routeGuid = route.Guid;
        var truckGuid = route.TruckGuid;

        // The truck loads, departs, and an exterior marker follows the authoritative progress.
        yield return WaitForCondition(
            () => stateManager.GetTrucks().Find(candidate => candidate.Guid == truckGuid) is { State: FactoryTruckState.Outbound },
            10f,
            "Truck did not depart with a full load.");
        var markerView = UnityEngine.Object.FindFirstObjectByType<FactoryTruckMarkerView>();
        Assert.That(markerView, Is.Not.Null);
        Assert.That(
            markerView.GetComponentInChildren<TextMesh>(true),
            Is.Not.Null);

        // Gameplay controls stay usable while the test overlays are hidden.
        TestUIVisibility.SetVisible(false);
        yield return null;
        Assert.That(manager.CanEditCurrentFloorMachines(player), Is.True);
        player.RequestPlaceEquipment(FactoryEntityRecord.StorageDefinitionId, new Vector2(3.5f, 1.5f));
        yield return WaitForCondition(() => senderFloor.Entities.Count == 4, 5f, "Placement failed while test UIs were hidden.");
        TestUIVisibility.SetVisible(true);

        // The receiver is filled so the arriving truck must wait with its cargo intact.
        stateManager.TryGetFloorState(42, 0, out var receiverFloor);
        receiverFloor.TryGetEntity(receiverId, out var receiver);
        receiver.SetState(receiverId, receiver.DefinitionId, receiver.LogicalPosition, 0f, 0f, 0, 100);

        Assert.That(stateManager.SaveWorld(), Is.True);
        yield return ExitMachineTestBuilding();
        yield return RestartHost();
        player = PlayerSceneTransition.LocalOwner;
        stateManager = NotAI.NAIStateManager.Instance;
        manager = GameSceneManager.Instance;

        // The route and journey survive the reload without resetting.
        Assert.That(stateManager.GetTruckRoutes(), Has.Count.EqualTo(1));
        var restoredRoute = stateManager.GetTruckRoutes()[0];
        Assert.That(restoredRoute.Guid, Is.EqualTo(routeGuid));
        Assert.That(stateManager.GetTrucks()[0].CargoCount, Is.EqualTo(20));

        // The truck arrives at the full receiver and blocks with its cargo preserved.
        yield return WaitForCondition(
            () => stateManager.GetTrucks().Find(candidate => candidate.Guid == truckGuid) is { State: FactoryTruckState.Unloading }
                && stateManager.GetTrucks().Find(candidate => candidate.Guid == truckGuid)!.CargoCount == 20,
            20f,
            "Truck did not arrive and hold cargo against the full receiver.");
        Assert.That(stateManager.SaveWorld(), Is.True);
        yield return RestartHost();
        player = PlayerSceneTransition.LocalOwner;
        stateManager = NotAI.NAIStateManager.Instance;
        manager = GameSceneManager.Instance;
        var blockedTruck = stateManager.GetTrucks().Find(candidate => candidate.Guid == truckGuid);
        Assert.That(blockedTruck, Is.Not.Null);
        Assert.That(blockedTruck.State, Is.EqualTo(FactoryTruckState.Unloading));
        Assert.That(blockedTruck.CargoCount, Is.EqualTo(20));

        // Both interiors are unloaded here: the player is outside and building 3 has no interior scene.
        Assert.That(manager.OutsideTestLoadedInteriorCount, Is.Zero);

        // Draining the receiver resumes unloading at the rate limit without bursts.
        stateManager.TryGetFloorState(42, 0, out receiverFloor);
        receiverFloor.TryGetEntity(receiverId, out receiver);
        receiver.SetState(receiverId, receiver.DefinitionId, receiver.LogicalPosition, 0f, 0f, 0, 0);
        yield return WaitForCondition(
            () =>
            {
                var truck = stateManager.GetTrucks().Find(candidate => candidate.Guid == truckGuid)!;
                return truck.CargoCount == 0
                    && (truck.State == FactoryTruckState.Returning || truck.State == FactoryTruckState.Loading);
            },
            20f,
            "Truck did not finish unloading after the receiver was drained.");
        stateManager.TryGetFloorState(42, 0, out receiverFloor);
        receiverFloor.TryGetEntity(receiverId, out var drainedReceiver);
        Assert.That(drainedReceiver.OutputCount, Is.EqualTo(20));

        // An empty-cargo route can be deleted through the panel request.
        player.RequestDeleteTruckRoute(routeGuid);
        yield return WaitForCondition(
            () => stateManager.GetTruckRoutes().Count == 0,
            5f,
            "Route deletion request failed.");
        Assert.That(File.Exists(savePath), Is.True);
    }

    private IEnumerator EnterMachineTestBuilding()
    {
        var player = PlayerSceneTransition.LocalOwner;
        var portal = FindPortal("OutsideTest", candidate => candidate.BuildingInstanceId == 2);
        Assert.That(portal, Is.Not.Null);
        player.ServerTeleport(portal.transform.position);
        yield return WaitForCondition(
            () => GameSceneManager.Instance.RequestTransition(player.NetworkObject, portal),
            3f,
            "Machine test building entry rejected.");
        yield return WaitForCondition(
            () => player.TryGetCurrentOutsideTestFloor(out var building, out var index)
                && building == 2 && index == 0 && !player.IsTransitioning
                && GameSceneManager.Instance.CanEditCurrentFloorMachines(player),
            10f,
            "Machine test building entry failed.");
    }

    private IEnumerator ExitMachineTestBuilding()
    {
        var player = PlayerSceneTransition.LocalOwner;
        var portal = FindPortal(player.gameObject.scene, candidate => candidate.Destination == SceneDestination.World);
        Assert.That(portal, Is.Not.Null);
        player.ServerTeleport(portal.transform.position);
        yield return WaitForCondition(
            () => GameSceneManager.Instance.RequestTransition(player.NetworkObject, portal),
            3f,
            "Machine test building exit rejected.");
        yield return WaitForCondition(
            () => player.gameObject.scene.name == "OutsideTest"
                && !player.IsTransitioning
                && GameSceneManager.Instance.OutsideTestLoadedInteriorCount == 0,
            10f,
            "Interiors did not unload after exit.");
    }

    private IEnumerator RestartHost()
    {
        yield return StopNetworking();
        yield return SceneManager.LoadSceneAsync("Bootstrap", LoadSceneMode.Single);
        yield return null;
        networkManager = UnityEngine.Object.FindFirstObjectByType<NetworkManager>();
        Assert.That(GameSceneManager.Instance.ConfigureOutsideTestStatePath(savePath), Is.True);
        networkManager.ServerManager.StartConnection();
        networkManager.ClientManager.StartConnection();
        yield return WaitForCondition(
            () => PlayerSceneTransition.LocalOwner is not null
                && PlayerSceneTransition.LocalOwner.gameObject.scene.name == "OutsideTest",
            10f,
            "Restarted host did not arrive outside.");
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
