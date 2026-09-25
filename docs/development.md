# Development Workflow

## Setup

1. Install Unity `6000.5.9f1` with Windows x86-64 player build support and an active Editor license. Git must be available for the declared Git packages.
2. Obtain the project source, including `Assets/FishNet`, all relevant `.meta` files, `Packages/manifest.json`, `Packages/packages-lock.json`, and `ProjectSettings`.
3. Open the project and let Unity import assets and resolve packages. Do not copy another checkout's `Library`, `Temp`, or generated `.csproj` files.
4. For the employee script assistant, run **FoodFactory > Download Script Assistant Model** once. It fetches `qwen2.5-coder-1.5b-instruct-q4_k_m.gguf` (about 1.1 GB, SHA-256 checked) into `Assets/StreamingAssets/Models/`. LLMUnity downloads its LlamaLib runtime (about 3.9 GB, every platform and GPU backend) into `Assets/StreamingAssets/LlamaLib-v2.0.5/` by itself on the first Editor load. Git ignores both. Without the model the game runs normally and the Assistant tab shows an error.
5. For live automation, install Unity CLI and use the project's declared `com.unity.pipeline` package (`0.7.0-exp.1`). Confirm the actual Editor connection before mutations.

FishNet is installed by the source checkout itself; do not also import a second Asset Store/UPM copy. Its version is `4.7.3`. The reason for retaining the existing installation is in [decision 0001](decisions/0001-reproducible-baseline.md).

## Live Editor Discovery

PowerShell, from the project root:

```powershell
unity status --project-path "E:\Projects\Unity\FoodFactoryGame" --format json
unity list --project-path "E:\Projects\Unity\FoodFactoryGame" --format json
```

For a different checkout, substitute its absolute path. Configure the agent's MCP client to launch:

```text
unity mcp --project-path E:\Projects\Unity\FoodFactoryGame
```

The MCP client performs `initialize`, `notifications/initialized`, `tools/list`, and `tools/call`. Rediscover schemas after package or command changes. The calls below are `tools/call` payloads, not shell commands. They were exercised against this Editor through Unity CLI MCP; no custom `factory_*` commands are required.

## Compilation and Console

```json
{"name":"console_status","arguments":{}}
{"name":"recompile","arguments":{}}
{"name":"recompile_status","arguments":{}}
{"name":"console","arguments":{"level":"warn","tail":15}}
```

Establish a console baseline, request compilation, then poll until it finishes successfully. Check `compilationFailed` and new console errors. Preserve the console session/cursor when following output; a domain reload can invalidate an old cursor. Do not clear the user's console to manufacture a clean result.

## EditMode Baseline Tests

Assembly: `FoodFactoryGame.Baseline.EditModeTests`.

```json
{"name":"list_tests","arguments":{"mode":"editor"}}
{"name":"run_tests","arguments":{"mode":"editor","filter":"FoodFactoryGame.Baseline.EditModeTests","filter_type":"assembly","async_tests":true,"timeout":180}}
{"name":"test_status","arguments":{}}
```

The runner may return a completed result immediately despite `async_tests=true`. Otherwise poll `test_status`. Archive the result before starting another run; this command reports the most recent run. Require exactly four tests for the current baseline, all passed, none skipped. Zero matched tests is failure.

Checks: FishNet runtime availability, all entries in the default network prefab collection resolving, starter Player/UI input actions importing, and enabled build scenes loading without missing scripts and with a camera source (a scene camera, or a `SessionRoot` whose player prefab has one; `DevSite` intentionally has no scene camera). These are setup checks, not gameplay or multiplayer acceptance.

The tests use preview scenes and do not write save data or modify application databases. Future stateful tests must receive explicit isolated save paths.

## Multiplayer Foundation Verification

The contracts for the first multiplayer implementation are in [decision
0002](decisions/0002-authoritative-multiplayer-foundation.md). They are
accepted design contracts. A bounded goods domain and a FishNet bridge exist; the only live
multiplayer proof is an isolated in-Editor listen-server test fixture. Do not mark the foundation complete
from a domain test, build, or FishNet import check alone.

When the foundation exists, verify these layers separately:

- Domain/EditMode tests: server-only command validation, permission checks,
  atomic rejection, request-ID idempotency, reservations, stable IDs, clock
  scheduling, and save/recovery rules. Report the exact filter, run identity,
  matched count, pass/fail/skip counts, and artifact path. Zero matches is a
  failure.
- Headless-compatible simulation: advance a site with no camera, local player,
  or presentation scene; confirm that an unsubscribed site follows the same
  model as a subscribed site.
- Listen-server PlayMode/smoke test: run the server and local client together,
  connect a remote client, reject an unauthorized command, and establish a
  server-validated remote management subscription. Capture the run identity,
  logs, and any relevant network diagnostics.
- Persistence/recovery: use a new isolated save/database path, run migration
  and reconciliation as a dry run first when supported, and verify stable IDs
  plus no duplicated or silently deleted inventory/payments after failure and
  cancellation.
- Scale/performance: use approved hardware and population interpretation
  before measuring the GDD targets. Keep server simulation timing separate
  from client rendered FPS.

Keep one owner for live Editor mutations, compilation, and test execution.
The multiplayer and scale procedures above remain acceptance requirements, not
verified workflows today; the isolated goods-domain and goods listen-server test procedures below have been exercised.

The bounded goods domain has an exercised EditMode workflow: confirm `editor_status` reports **play mode stopped**, compile and poll `recompile_status`, then run `run_tests` with `mode: editor`, `filter_type: assembly`, `filter: FoodFactoryGame.Goods.EditModeTests`; require 17 matched/17 passed. Run the unchanged baseline separately with `FoodFactoryGame.Baseline.EditModeTests` (4 matched/4 passed). Test fixtures create and delete unique isolated directories under the OS temp path. The exact run identities, counts, resolved test-runner precondition failure, and remaining unverified multiplayer checks are recorded in [the verification artifact](verification/goods-20260922.md). The Pipeline runner returns no native artifact path on synchronous completion; preserve this recorded result or use an explicitly configured CI XML run for future machine-ingested evidence.

The goods listen-server PlayMode test has an exercised workflow: make sure the active scene is **not dirty** (the Test Runner otherwise blocks on its save-scene prompt and the async run never enters Play mode), then run `run_tests` with `mode: playmode`, `async_tests: true`, `filter_type: assembly`, `filter: FoodFactoryGame.Goods.PlayModeTests`; require 1 matched/1 passed. The synchronous HTTP call is dropped by the Play-mode domain reload, so read the result from `test_status` or the async response. Unity writes the NUnit XML to `%USERPROFILE%/AppData/LocalLow/DefaultCompany/FoodFactoryGame/TestResults.xml` and overwrites it on each run; copy it to `docs/verification/artifacts/` when it is evidence. The fixture prefab `Assets/Tests/PlayMode/Goods/GoodsBridgeFixture.prefab` is intentionally authored non-spawnable so FishNet’s default-prefab generator never adds it to `Assets/DefaultPrefabObjects.asset`; the test enables spawning in memory only.

## Session Bootstrap (host, join, multi-process)

Decisions: [0005](decisions/0005-session-bootstrap-and-player-identity.md). `Assets/Scenes/DevSite.unity` is the only build scene. It is authored by `AgentScripts/BuildDevSite.cs`, which is idempotent and keeps asset GUIDs; re-run it with MCP `run_script` (`file: AgentScripts/BuildDevSite.cs`, `entry: BuildDevSite.Run`) rather than editing the scene or prefab YAML. Running it also makes FishNet's generator append the spawnable prefabs to `DefaultPrefabObjects.asset`; that is expected. It re-saves `Player.prefab` and `GoodsNetworkBridge.prefab` with FishNet's cached `NetworkObject` fields unset, and the generator then logs `… have the same assetPath hash of 0`. Run the MCP `eval` `EditorApplication.ExecuteMenuItem("Tools/Fish-Networking/Utility/Refresh Default Prefabs")` afterwards to restore the hashes, and revert the reordering it makes in `DefaultPrefabObjects.asset`. The other cached fields (`PrefabId`, `NetworkBehaviours`, …) flip between raw and filled as FishNet processes the prefabs; the session PlayMode tests pass with either. The script also rewrites `Assets/Materials/EquipmentGhost.mat` to its own blend settings (premultiplied alpha off, `_SrcBlend` 5), which differ from the committed, later-tuned material; revert that file unless the ghost look is the change (seen 2026-09-24).

MCP `capture_game_view` resolves `save_path` under `Assets/` and refuses `..`. Save captures to `Temp/<run>/…` (that is, `Assets/Temp/<run>`), then move them to `docs/verification/` and delete that `<run>` folder and its `.meta`, so no capture is imported as an asset. Do not delete `Assets/Temp` itself: it holds tracked older captures.

Where state lives (real play, not tests):

| What | Default | Override |
| --- | --- | --- |
| World snapshot (SQLite) | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\FoodFactoryGame\Saves\dev-world\world.db` | `-save <directory>` |
| Player registry (SQLite) | same directory, `players.db` | `-save <directory>` |
| Client secret (SQLite) | `...\FoodFactoryGame\Identity\identity.db` | `-identity <file>` |

All stored data is SQLite ([decision 0011](decisions/0011-sqlite-for-all-data-storage.md)). A pre-SQLite `world.snapshot` in the save directory and the default `Identity\client.secret` are imported once when their database does not exist yet, and left in place; an old plain-text secret passed with `-identity` is not imported. Deleting the save directory resets the dev world and all identities. Deleting a client identity database makes that client a new player. Two processes on one machine must use different `-identity` files, or the second is rejected with `already-connected`.

Starting a session:

- From the menu: enter a name, then **Host**, or **Join** with an address (default `127.0.0.1`). Tugboat's port is 7770 (UDP). The status line shows rejection reasons.
- From the command line (skips the menu): `-host`, `-server` (no local player, for `-batchmode -nographics`), or `-connect <address>`, plus optional `-name <display>`, `-save <dir>`, `-identity <file>`.
- Controls ([decision 0007](decisions/0007-player-controls-and-working-oven.md), [0008](decisions/0008-slot-grid-ui-and-automatic-machines.md)): WASD/left stick moves relative to the camera; the mouse/right stick always orbits (the pointer is locked to a centre crosshair), from looking straight up to straight down; scroll zooms. Anything with a screen under the crosshair (a machine, an employee, or the storage shelf) gets a green outline when you stand within 2.5 m of it, and E opens its screen (left click also opens a machine or employee in reach); E on nothing opens the inventory (a slot grid of your goods and machines), and E closes any open screen. 1–9, or clicking a machine in the grid, put a held machine kind on the cursor (see-through green/red ghost, R rotates, X or Esc clears); left click places it, or with an empty cursor opens the machine under the crosshair; right click picks the machine up. On a screen, click a slot to pick its stack up (the icon follows the pointer) and click another slot to put it down; dropping on another container moves the goods. The oven starts by itself as soon as dough is in its input and keeps going while dough remains and the output has room. Esc closes a screen, else empties a non-empty cursor, or else releases the pointer until the next click. The readout shows the hint and the server's last rejection reason. Belts ([decision 0010](decisions/0010-conveyor-belts.md)): click the belt stack in the inventory and close it with E; the belts stay on the cursor (icon beside the crosshair) with a ghost belt at the crosshair. R turns the ghost; hold left click and move forward to lay a line; press R mid-drag with the crosshair off to the side of the line to turn a corner toward it, laying belts out to and including the crosshair's cell, and keep dragging (R over any belt mid-drag does nothing). R can be held through the drag, so the line follows the crosshair round each turn. R on a belt with an empty cursor turns it; hold right click over belts to take them up (their items come back too). Carry any other stack out the same way, aim at a belt (a ghost of the item shows where it lands) and press Z to put one on it; F takes the item nearest the crosshair off a belt into your inventory, whatever the cursor holds.
- Don't edit scripts while the Editor is in play mode during a live check: Unity's recompile-and-continue reloads the domain, which drops the FishNet session (the subscription loses its bridge) and turns null strings into empty ones. Stop play mode first.
- The dev seed (a 20×20 grid, one oven, 20 dough and 200 belts in storage) is applied only when a world is created, and players get 5 starter dough and 50 belts only when their inventory is created. A save made before these steps lacks them, so delete it to get them; the exceptions are the storage belts, which a save that has never had belts receives once on the next server start, and the dev company with $500.00 (decision 0012), which a save without a company receives once.
- Company cash is shown top right of the HUD. The sell counter (decisions 0013, 0024) earns it: open the counter left of the oven and drop edible bread into its input; customers from the dev district "Old Town" (60 m north, about one every 15 s, choosing between you, Corner Cafe and Noodle Bar) walk over, queue and buy one bread each for $2.50, taking a seat at the 4-seat table inside the restaurant if they dine in (dine-in customers wait for a free seat). The counter screen shows who is being served and how many wait; opening the table shows its seats and the restaurant's served, walked-out and reputation figures. A counter serving someone or an occupied table cannot be picked up (`occupied`). Tables cost $40.00 at the Supplier; a save from before customers gains the district, competitors and table once (cells (11-12, 3) must be free). The inventory screen's Supplier window spends it (decision 0014): 5 dough for $2.50, 10 belts for $5.00, delivered straight into your inventory, and an oven for $150.00, delivered held in your inventory and placed like a picked-up oven (decision 0017); the readout shows the reason if the server refuses (for example `insufficient-funds` or `capacity`). A save from before the counter gets it once on the next server start (cells (6, 13) must be free). The Supplier also sells a fridge for $80.00 (decision 0018): place it like an oven, open it and drop goods in; they do not spoil while inside. Each edible stack shows its time left in the slot corner (frozen and blue in the fridge), and hovering it shows the full time.
- Trucks ([decision 0022](decisions/0022-trucks.md)): a remote Warehouse site (150 m by road) holds 200 dough and a loading dock; the restaurant's dock stands at the west end of the north edge. Truck 1 repeats warehouse dock → restaurant dock, 10 s each way. Press L for the logistics screen: under Other sites, Ship moves one stack of warehouse storage onto its dock, and the waiting truck loads it (5 a second) and drives off once the dock is empty; open the restaurant dock and take the delivery from Incoming. Routes ([decision 0023](decisions/0023-truck-routes-and-fleet.md)) are their own cards: the arrows pick the pickup dock, the dropoff dock and the cargo (Any or one item), Apply route changes a route for every truck on it, Delete parks its trucks, and the New route card's Create route adds one. Each truck card's Route arrows pick a route or Parked, and Assign/Park sends it; Buy Truck ($250.00) parks a new truck at the restaurant. A truck loading or unloading at the restaurant dock stands behind it. Docks cost $60.00 at the Supplier. A save from before trucks gains the warehouse, both docks and the truck once on the next server start (restaurant dock cells (0-1, 18) must be free, or the truck starts parked).
- World save cost (decision 0012): a hosting or server-only process logs `[Goods] commits=<n> avg=<ms> max=<ms> payload=<KB>` once a minute, cumulative since the server started serving the world (counters reset then, so earlier Editor test saves are excluded), and a one-time `[Goods]` warning if a commit takes over 50 ms or the payload passes 1 MB. Those thresholds are the signals for moving the world from the JSON payload to relational tables. Time excludes waiting for another writer; only saves that write a new revision count. Since [decision 0015](decisions/0015-wal-and-held-world-connection.md) a running server keeps `world.db` open in WAL mode, so `world.db-wal`/`-shm` exist beside it until the server stops; copy all three (or stop the server) to back up a live save. Since [decision 0016](decisions/0016-periodic-tick-commits.md) clock ticks are committed every 10 s (player commands at once), so expect about 6 tick commits a minute in the summary, and a save read while the server runs may lag the live clock by up to 10 s.

Two-process check on one machine (use an existing artifact directory for logs and isolated saves):

```powershell
Start-Process -FilePath ".\build\Session\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -host -name Host -save "<artifacts>\host-save" -identity "<artifacts>\host.db" -logFile "<artifacts>\host.log"'
Start-Process -FilePath ".\build\Session\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -connect 127.0.0.1 -name Guest -identity "<artifacts>\guest.db" -logFile "<artifacts>\guest.log"'
```

Evidence: both logs contain `[Session] Joined as player-...` with different IDs, the host log contains `[Session] Hosting`, and captures of both windows show two avatars and matching site revisions in the readout.

Driving a player build from a script (used for the placement captures): the players read keys by scan code, so synthetic key events need a real scan code (`MapVirtualKey`); zero-scan-code events are ignored. Don't move player windows with `MoveWindow` before sending mouse input, because the player then reports a pointer position offset from the real cursor. Leave windows where Unity opens them and capture each one with `PrintWindow`, so overlapping windows don't matter. Run the helper DPI-aware, and check that it really took focus before sending input.

Session tests: `FoodFactoryGame.Session.EditModeTests` (assembly, editor) and `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode, async; make sure the open scene is not dirty). The PlayMode tests load the real `DevSite`, call `SessionRoot.Configure` with a temporary save directory and identity files and a free UDP port, and add a second client-only NetworkManager for the remote client. They expect one `SpawnablePrefabs is null on session-test-remote` error from FishNet's editor `Reset` (declared with `LogAssert.Expect`), and emit "2 audio listeners" warnings because both local clients own a camera in one process. Current counts and run identities are in [the session verification record](verification/session-20260922.md).

Customer choice benchmark (separate prototype, test-only): `run_tests` with `mode: editor`, `filter_type: testName`, `filter: FoodFactoryGame.Benchmarks.Tests.CustomerChoiceBenchmarkTests`; require 6 matched/6 passed. Each scenario logs one `[Benchmark]` console line (machine, mean/p99/max tick, outcome counts). Budgets are provisional and Editor Mono timings are not server timings. Unity's Mono always reports 0 from `GC.GetAllocatedBytesForCurrentThread`, so check allocations with `UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory()`. Results: [the benchmark record](verification/customer-choice-benchmark-20260925.md).

Runtime customer benchmark: `run_tests` with `mode: editor`, `filter_type: testName`, `filter: FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests`; require 1 matched test. It uses an isolated SQLite database under the OS temp directory, warms to at least 1,000 customers across 20 restaurants, times one accepted player transfer, then advances 300 seconds and commits every 10 seconds. Console `[Benchmark]` lines report tick p99, command latency, and save phase mean/max (copy, validation, JSON, transaction before `COMMIT`, and `COMMIT` including sync); preserve them and the NUnit XML. The provisional 16.7 ms tick p99 and 50 ms maximum-commit signals remain assertions, so a measured warning fails the test. Current numbers, run identities and artifacts are in [the save phase verification record](verification/save-phases-20260925.md); the earlier customer cache run is [here](verification/customer-cache-20260925.md).

Held-connection fast save verification: run EditMode `filter_type: assembly`, `filter: FoodFactoryGame.Goods.EditModeTests` and require a nonzero match count with all save, recovery and quarantine tests passing; then run the runtime customer benchmark above. The first 2026-09-25 goods run matched 164/164. After a cache-placement adjustment, the full rerun matched 164 with 163 passing and the already-known nondeterministic truck-ordering test failing; focused world, held-save and cash fixtures on that final code passed 17/17, 4/4 and 8/8. The final benchmark matched 1 and failed only its separate clock-tick p99 signal. Preserve the XML reports and benchmark console lines; [the fast-save record](verification/fast-save-20260925.md) has run identities, artifacts and numbers. `GoodsSnapshotStore` falls back to full validation whenever the cached held-connection head differs from the stored row; an isolated external-corruption probe exercised that path on the final code.

Input isolation (so the developer can keep using other controllers, such as a racing wheel, while agents work in the Editor):

- Each Session PlayMode fixture owns an `InputTestFixture` (from `Unity.InputSystem.TestFramework`). It calls `Setup()` before loading `DevSite` and `TearDown()` in a `finally` at the end of `[UnityTearDown]`. While a test runs, the Input System has no real devices and drops all native input. Tests add virtual `Mouse`/`Keyboard` devices and queue state for them; do not change focus settings in tests. New PlayMode fixtures that load scenes with input-reading components must follow the same pattern.
- `Assets/InputSystem.inputsettings.asset`, registered as `com.unity.input.settings`, sets Background Behavior to **Reset And Disable All Devices** and Play Mode Input Behavior to **All Devices Respect Game View Focus**. Outside tests, a play session in the Editor ignores every device, the wheel included, unless the Game view is focused. A player build still pauses when it loses focus (`runInBackground: 0`).
- To keep the Editor off a controller entirely, run tests from a separate command-line checkout (below). Nothing more is isolated than that: the Editor still lists real devices while it is idle.

For a separate checkout with its Editor closed, the batch equivalent is:

```powershell
unity test "<checkout>" --editor-version 6000.5.9f1 --mode EditMode --filter "FoodFactoryGame.Baseline.Tests.ProjectBaselineTests" --output "<existing-artifact-directory>\baseline-tests.xml" --report-format nunit,junit --timeout 600 --format json
```

Do not launch a second Editor on the same project directory. Keep one owner for all live mutations, compilation, and tests. Check report counts and exit status, not just whether XML exists.

## Windows Development Player

Inspect the saved build scene and open-scene state first:

```json
{"name":"get_build_settings","arguments":{}}
{"name":"list_open_scenes","arguments":{}}
```

Do not save or replace an unrelated open scene. Stop play mode and wait for compilation with no console errors first. Build the saved `DevSite` scene, the only build scene, to `build/Session`, the path the session commands above use:

```json
{"name":"build","arguments":{"target":"StandaloneWindows64","outputPath":"build/Session/FoodFactoryGame.exe","options":["Development","DetailedBuildReport"],"scenes":["Assets/Scenes/DevSite.unity"],"confirm":true}}
{"name":"build_status","arguments":{}}
```

Poll until `completed`, require a successful BuildReport, and retain its build ID, summary, warnings/errors, and output location. A queued response or dry run is not a successful build. The full report can be large; store it and inspect summary/failure details.

Manual equivalent in the Editor: **File → Build Profiles**, Windows platform, confirm `Scenes/DevSite` is the only checked scene, optionally enable **Development Build**, then **Build** into `build/Session` as `FoodFactoryGame.exe`. Many FishNet vendor warnings are expected (see the verification record below).

Launch the player with an explicit log path, an isolated save, and an identity file in an existing artifact directory:

```powershell
Start-Process -FilePath ".\build\Session\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -save "<absolute-artifact-directory>\player-save" -identity "<absolute-artifact-directory>\player.db" -logFile "<absolute-artifact-directory>\player.log"' -PassThru
```

Without `-host`, `-server`, or `-connect` the player opens the session menu. Verify that the player stays alive, creates a responsive window, initializes graphics, and has no startup exceptions in its log. Close the specific process launched for the check. This proves player startup, not gameplay, multiplayer, or visual acceptance; use the two-process check above for a session.

The 2026-09-21 record below predates `DevSite` and describes a `SampleScene` build to `build/Baseline`.

## Verification Record - 2026-09-21

Initial live Editor: `6000.5.9f1`, PID `5068`, ready; zero errors/warnings, no compile failure. Open scene was unnamed and clean; the enabled saved build scene was `Assets/Scenes/SampleScene.unity`. Initial discovery found zero tests, so no pre-existing passing test suite is claimed.

- Initial inspection artifact: `TestResults/baseline-initial.json`.
- Live EditMode run identity: `baseline-20260921-editor`; filter type `assembly`, filter `FoodFactoryGame.Baseline.EditModeTests`; 4 matched, 4 passed, 0 failed/skipped; duration 0.78 seconds. Artifact: `TestResults/baseline-20260921-editor-tests.json`. This is an assigned artifact identity; the Pipeline response did not include a native test run ID.
- Recompile completed successfully with no compiler errors.
- During verification Unity warned that `Packages/com.unity.render-pipelines.core/Runtime/Debugging/Runtime UI Resources/RuntimeDebugWindow_PanelSettings.asset` in an immutable package had changed. No package asset was intentionally edited by this work. Record rather than suppress it; clean-source comparison is part of baseline verification.
- Player build: `build_643715384ca2`, `StandaloneWindows64`, succeeded with 0 build errors and 174 warnings; output `build/Baseline/FoodFactoryGame.exe`, 190,108,783 bytes, build duration 161,048 ms. Full captured artifact: `TestResults/baseline-20260921-build.json`.
- Player launch: process PID `45112` remained responsive and alive after startup, with a window handle observed; it was then closed normally. The log initialized Unity `6000.5.9f1`, D3D12 on an NVIDIA GeForce GTX 1070, PhysX, and Input System. Artifact: `TestResults/baseline-20260921-player.log`. The log contains a nonfatal D3D12 info-queue query message; no managed exception or startup crash was observed.
- Clean-source snapshot: 2,087 Git-eligible input files, including 2,014 FishNet files, SHA-256 `f60e56fa982e4b9d38d28573bac2432b8a64a387a7c02f7a8a8191f50de4a1b4`, exported to `C:\Users\Deven\AppData\Local\Temp\opencode\FoodFactoryBaseline-20260921`; `Library` was not copied. Artifact: `TestResults/baseline-20260921-source-snapshot.json`.
- Clean-source EditMode run identity: batch test run id `2`, project `FoodFactoryBaseline-20260921`, filter `FoodFactoryGame.Baseline.Tests.ProjectBaselineTests`, mode `EditMode`, 4 matched, 4 passed, 0 failed/skipped. Artifacts: `TestResults/baseline-20260921-clean-cli.json`, `baseline-20260921-clean-tests.xml`, and `baseline-20260921-clean-tests.junit.xml`. This demonstrates package/dependency restoration from the local source snapshot, not an empty-machine/global-cache test.
- Build warnings are currently mostly FishNet vendor serialization-analyzer/obsolete API warnings. The first warning is that no RuntimePipelineConfig asset exists, so Pipeline is disabled in Player builds. Do not suppress or modify vendor warnings as part of gameplay work; decide separately whether to configure Pipeline runtime support for development builds.

Artifacts are intentionally ignored by Git. Preserve required evidence externally when sharing acceptance or running CI.

## Remaining Work

- An actual multiplayer smoke-test procedure requires the networking foundation. No server/client replication is implemented by this setup.
- Specify benchmark hardware and representative simulation scenarios before accepting performance targets.
- Test on a new machine/empty global package cache if that environment is required; local cold import alone does not certify it.
