# Verification: visual customers, first build (2026-09-25)

Decision: [0024](../decisions/0024-customer-simulation.md) (visual customers are presentation only).

Unity 6000.5.9f1 Editor. Compilation completed with no errors; only the pre-existing `FindObjectsSortMode` obsolete-API
warnings in older PlayMode tests remained. Every host below used a unique OS temp save directory and identity database
(`FoodFactoryCustomerPresenter/<guid>` or `FoodFactoryCustomerVisualCheck/<guid>`). No application database was touched.

Authoring: `AgentScripts/BuildCustomerVisual.cs` builds `Assets/Prefabs/Customers/Customer.prefab` from the Employee
model's animated character. It removes the worker props and has no network components. `BuildDevSiteNavigation.cs` bakes
`Assets/Scenes/DevSite/NavMesh-Navigation.asset`, and `InstallCustomerPresenter.cs` adds the `CustomerPresenter`
(`maxVisible` 100, customer prefab) and the `Navigation` surface to `DevSite.unity`.

| Requested filter | Run identity (UTC) | Matched | Result | Artifact |
| --- | --- | ---: | --- | --- |
| PlayMode testName `CustomerPresenterTests` | NUnit id 2, 2026-09-26 04:52:45Z | 1 | 1 passed | [Focused XML](artifacts/customer-presenter-focused-20260925.xml) |
| PlayMode assembly `FoodFactoryGame.Session.PlayModeTests` (async) | NUnit id 2, 2026-09-26 04:56:25Z | 25 | 25 passed | [Session XML](artifacts/customer-visuals-session-20260925.xml) |

The only console error during the assembly run was the expected `SpawnablePrefabs is null on session-test-remote`,
which `SessionBootstrapTests` consumes with `LogAssert.Expect`.

## Running-game checks

- The first capture showed no customers because `DevSite`'s player camera is untagged, so `Camera.main` was null and every
  edge was rejected ([before](artifacts/customer-visual-before-camera-fix.png)). The presenter now falls back to the first
  active camera.
- After that fix, `CheckCustomerVisuals` seeded queued visual-check customers into the isolated host. The capture shows 100
  animated, district/appearance-tinted customers walking to and queuing at the counter, with the HUD and the site running
  ([running capture](artifacts/customer-visuals-running-20260925.png)).
- Frame probes (Editor Game view, one machine, not target hardware):
  - [Animated model count probe](artifacts/animated-customer-probe-20260925.csv): median / p95 frame 6.52 / 8.60 ms at 100
    models and 9.95 / 12.17 ms at 200. At 400, only 74 % of frames were under 16.67 ms, which supports the 100 cap.
  - [Running host probe](artifacts/customer-host-frame-probe-20260925.csv): 12 visuals, median / p95 frame 4.62 / 5.62 ms.

## Remote client (2026-09-26)

`CustomerPresenter.Bind(ClientSiteSubscription)` lets a presenter draw another connection's replicated site instead of its
session's own. `RemoteClientDrawsTheCustomerFromItsOwnReplicatedSite` joins a client-only `NetworkManager` in the same
process over loopback UDP with the real `DevAuthenticator` (isolated `remote.db`), binds a second presenter to that
connection's dev-site subscription, seeds a customer on the server and checks three things. The remote baseline carries
the customer with the server's restaurant and appearance. The remote presenter creates its own animated visual, and that
visual walks.

| Requested filter | Run identity (UTC) | Matched | Result | Artifact |
| --- | --- | ---: | --- | --- |
| PlayMode testName `CustomerPresenterTests` (async) | 2026-09-26, before the assembly run | 2 | 2 passed | (overwritten by the assembly run) |
| PlayMode assembly `FoodFactoryGame.Session.PlayModeTests` (async) | NUnit id 2, 2026-09-26 16:56:23Z | 26 | 26 passed | [Session XML](artifacts/customer-visuals-remote-session-20260926.xml) |

Console errors were only the expected `SpawnablePrefabs is null on session-test-remote`, consumed by `LogAssert.Expect`.

## Not verified

- A running-game capture from a separate remote player process. The remote check above shares one process and camera
  with the host.
- Out-of-view spawning against other players' cameras. The presenter only checks the local camera, so another player
  can see a customer appear.
- Crowding behavior: queued visuals stand in a fixed grid beside the counter and pass through each other. There is no
  avoidance.
