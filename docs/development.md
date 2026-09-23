# Development Workflow

## Setup

1. Install Unity `6000.5.9f1` with Windows x86-64 player build support and an active Editor license. Git must be available for the declared Git packages.
2. Obtain the project source, including `Assets/FishNet`, all relevant `.meta` files, `Packages/manifest.json`, `Packages/packages-lock.json`, and `ProjectSettings`.
3. Open the project and let Unity import assets and resolve packages. Do not copy another checkout's `Library`, `Temp`, or generated `.csproj` files.
4. For live automation, install Unity CLI and use the project's declared `com.unity.pipeline` package (`0.7.0-exp.1`). Confirm the actual Editor connection before mutations.

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

Decisions: [0005](decisions/0005-session-bootstrap-and-player-identity.md). `Assets/Scenes/DevSite.unity` is the only build scene. It is authored by `AgentScripts/BuildDevSite.cs`, which is idempotent and keeps asset GUIDs; re-run it with MCP `run_script` (`file: AgentScripts/BuildDevSite.cs`, `entry: BuildDevSite.Run`) rather than editing the scene or prefab YAML. Running it also makes FishNet's generator append the spawnable prefabs to `DefaultPrefabObjects.asset`; that is expected.

Where state lives (real play, not tests):

| What | Default | Override |
| --- | --- | --- |
| World snapshot | `%USERPROFILE%\AppData\LocalLow\DefaultCompany\FoodFactoryGame\Saves\dev-world\world.snapshot` (+ `.previous`) | `-save <directory>` |
| Player registry (SQLite) | same directory, `players.db` | `-save <directory>` |
| Client secret | `...\FoodFactoryGame\Identity\client.secret` | `-identity <file>` |

Deleting the save directory resets the dev world and all identities. Deleting a client secret makes that client a new player. Two processes on one machine must use different `-identity` files, or the second is rejected with `already-connected`.

Starting a session:

- From the menu: enter a name, then **Host**, or **Join** with an address (default `127.0.0.1`). Tugboat's port is 7770 (UDP). The status line shows rejection reasons.
- From the command line (skips the menu): `-host`, `-server` (no local player, for `-batchmode -nographics`), or `-connect <address>`, plus optional `-name <display>`, `-save <dir>`, `-identity <file>`.
- Controls: WASD/left stick moves relative to the camera; hold right mouse (or left shoulder) and move the mouse/right stick to orbit; scroll to zoom.

Two-process check on one machine (use an existing artifact directory for logs and isolated saves):

```powershell
Start-Process -FilePath ".\build\Session\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -host -name Host -save "<artifacts>\host-save" -identity "<artifacts>\host.secret" -logFile "<artifacts>\host.log"'
Start-Process -FilePath ".\build\Session\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -connect 127.0.0.1 -name Guest -identity "<artifacts>\guest.secret" -logFile "<artifacts>\guest.log"'
```

Evidence: both logs contain `[Session] Joined as player-...` with different IDs, the host log contains `[Session] Hosting`, and captures of both windows show two avatars and matching site revisions in the readout.

Session tests: `FoodFactoryGame.Session.EditModeTests` (assembly, editor) and `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode, async; make sure the open scene is not dirty). The PlayMode tests load the real `DevSite`, call `SessionRoot.Configure` with a temporary save directory and identity files and a free UDP port, and add a second client-only NetworkManager for the remote client. They expect one `SpawnablePrefabs is null on session-test-remote` error from FishNet's editor `Reset` (declared with `LogAssert.Expect`), and emit "2 audio listeners" warnings because both local clients own a camera in one process. Current counts and run identities are in [the session verification record](verification/session-20260922.md).

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

Do not save or replace an unrelated open scene. The baseline explicitly builds the existing saved starter scene:

```json
{"name":"build","arguments":{"target":"StandaloneWindows64","outputPath":"build/Baseline/FoodFactoryGame.exe","options":["Development","DetailedBuildReport"],"scenes":["Assets/Scenes/SampleScene.unity"],"confirm":true}}
{"name":"build_status","arguments":{}}
```

Poll until `completed`, require a successful BuildReport, and retain its build ID, summary, warnings/errors, and output location. A queued response or dry run is not a successful build. The full report can be large; store it and inspect summary/failure details.

Launch the player with an explicit log path in an existing artifact directory:

```powershell
Start-Process -FilePath ".\build\Baseline\FoodFactoryGame.exe" -ArgumentList '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "<absolute-artifact-directory>\baseline-player.log"' -PassThru
```

Verify that the player stays alive, creates a responsive window, initializes graphics, and has no startup exceptions in its log. Close the specific process launched for the check. This proves starter-player startup, not gameplay, multiplayer, or visual acceptance.

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
