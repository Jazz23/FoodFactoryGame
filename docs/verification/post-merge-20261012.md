# Verification: main after the starting-loop merge (2026-10-12)

Step 1 of the post-merge task: a baseline of `main` at `8cfcff1` ("Merge starting-loop-p1 into main") before any code change.
Working tree: the plan edit in `docs/starting-loop-plan.md` and FishNet's known reorder of `Assets/DefaultPrefabObjects.asset`
(one prefab moved, nothing added or removed; see `docs/development.md`). Live Editor Unity 6000.5.9f1, play mode stopped,
`WorldGen` open and clean. One owner ran every test in sequence.

The Pipeline runner returns no native run ID for EditMode runs, so each run has an assigned ID (`pm-…`) and its JSON response is the
artifact. PlayMode runs carry the NUnit `test-run` ID and times from `TestResults.xml`. Every fixture uses isolated temporary saves;
no application save or database was opened by a test.

## Runs

| Run ID | Mode | Filter (type) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| pm-baseline-edit (≈18:18Z) | EditMode | `FoodFactoryGame.Baseline.EditModeTests` (assembly) | 4 | 4 passed | [JSON](post-merge-20261012/artifacts/baseline-editmode.json) |
| pm-world-edit (18:19:02-18:19:45Z) | EditMode | `FoodFactoryGame.World.EditModeTests` (assembly) | 33 | 33 passed | [JSON](post-merge-20261012/artifacts/world-editmode.json) |
| pm-goods-edit (18:19:45-18:19:54Z) | EditMode | `FoodFactoryGame.Goods.EditModeTests` (assembly) | 274 | 274 passed | [JSON](post-merge-20261012/artifacts/goods-editmode.json) |
| pm-session-edit (18:19:54-18:20:07Z) | EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly) | 145 | 142 passed, **1 failed**, 2 skipped (flag-gated measurements) | [JSON](post-merge-20261012/artifacts/session-editmode.json) |
| pm-bench-edit (18:20:07-18:20:15Z) | EditMode | `FoodFactoryGame.Benchmarks.EditModeTests` (assembly) | 10 | 8 passed, **2 failed** | [JSON](post-merge-20261012/artifacts/benchmarks-editmode.json) |
| pm-truck-bench-2 (18:21:20Z) | EditMode | `FoodFactoryGame.Benchmarks.Tests.TruckRoadBenchmarkTests` (testName) | 1 | passed | [JSON](post-merge-20261012/artifacts/truck-benchmark-rerun2.json) |
| pm-truck-bench-3 (18:21:26Z) | EditMode | same | 1 | passed | [JSON](post-merge-20261012/artifacts/truck-benchmark-rerun3.json) |
| NUnit run 2 (18:21:46-18:21:59Z) | PlayMode | `FoodFactoryGame.Goods.PlayModeTests` (assembly) | 2 | 2 passed | [XML](post-merge-20261012/artifacts/goods-playmode-nunit.xml) |
| NUnit run 2 (18:22:07-18:25:45Z) | PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly) | 70 | 38 passed, **24 failed**, 8 skipped (flag-gated captures and the P0 playthrough) | [XML](post-merge-20261012/artifacts/session-playmode-nunit.xml) |

Every assembly matched a nonzero count. The only console error outside the failures below is the expected FishNet
`SpawnablePrefabs is null on worldgen-test-remote` from the remote-client fixture.

## Failures

None is in the merge's reconciliation (schema v21-v24, ledger wages, ported UXML). Two predate the merge on main; one is a test
fixture that the merge combined with a main rule; one is a known timing margin.

### 1. DevSite is not in the build list: 1 EditMode + 24 PlayMode failures (pre-merge, not flaky)

- `SessionAuthoringTests.DevSiteIsTheOnlyBuildSceneAndIsWiredForSessions`: expected `Assets/Scenes/DevSite.unity`, was
  `Assets/Scenes/WorldGen.unity`.
- 24 Session PlayMode tests fail in SetUp: 19 with `Scene 'Assets/Scenes/DevSite.unity' couldn't be loaded because it has not
  been added to the active build profile`, and 5 (`BeltPlacementTests` ×3, `BuildingPresenterTests` ×2) with "DevSite must contain a
  BeltPresenter/BuildingPresenter", which is the same failed load seen one step later (those fixtures find nothing after
  `LoadSceneAsync`). Affected classes: `BeltPlacementTests`, `BuildingPresenterTests`, `CustomerPresenterTests`,
  `EquipmentPlacementTests`, `LogisticsPanelTests`, `MainMenuTests`, `SessionBootstrapTests`.
- Evidence: `ProjectSettings/EditorBuildSettings.asset` lists WorldGen enabled and DevSite **disabled**, set by `88c023b` ("Remove
  old build", on main before the merge). The kitchen record (`kitchen-20261012.md`) shows the same 1 + 23 failures at `88c023b`,
  before the merge.
- Hypothesis: the build list was changed on purpose to build WorldGen, and the DevSite fixtures and authoring test were not updated.
  It is a decision for the owner (enable DevSite as a second build scene, or have the fixtures load DevSite without the build list).
  Not fixed here.
- Consequence: no DevSite PlayMode test runs on main, including `ServerRestartShowsOvenWhereLastCommitted` and the session bootstrap
  tests. Every WorldGen and SampleScene PlayMode test runs and passes.

### 2. `CustomerRuntimeBenchmarkTests` does not reach its population (merge combination, deterministic)

- `ThousandCustomersFitAFrameAndCommitUnderTheWarningSignals`: "reached 846 after 1800 s", target 1,000. The timing signals were
  never reached.
- Evidence ([diagnosis](post-merge-20261012/artifacts/runtime-benchmark-diagnosis.txt)): rebuilding the fixture and advancing 1800 s
  gives exactly 846 again. District-4 (wealth 80) sends **no** customer to the ten player sites and about 60% of what each other
  district sends. Main's decision 0043 (`GoodsWorld.MinTier`: wealth ≥ 70 buys tier 2 and up only) excludes the fixture's tier-1
  bread. The branch's P4 record shows the fixture reached its population before the merge (it failed only on the intermittent commit
  signal), with its rates converted to game hours (`e74c91b`) on a tree that had no tiers.
- Hypothesis: a test-fixture assumption (a wealthy district buys bread) broken by an intended main rule. The fix belongs in the fixture
  (for example a district wealth under 70, or more TEST districts), not in gameplay. Not fixed here.

### 3. `TruckRoadBenchmarkTests.HundredTrucksOnTheGeneratedRoads`: p99 19.36 ms over 16.7 ms (known timing margin)

- One failure in three runs (two reruns passed). The trucks record (`trucks-roads-20261002.md`) measured a p99 of 10.4-15.8 ms and
  says the budget holds "with little margin" and the slowest seconds coincide with garbage collections. Here too the three slowest
  seconds ran at GC 292-294.
- Hypothesis: Editor Mono timing noise near the budget. It is flaky, not a regression.

### `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn`

Passed in this run (26.9 s). It is known to fail on timing (7 of 8 drawn samples in P0 and the kitchen baseline).

## A world saved before the merge

[Probe output](post-merge-20261012/artifacts/pre-merge-saves.txt). Four saves were copied from the application's save directory
into a scratch directory, and only the copies were opened:

| Save | Written by | Stored schema | Cash before → after load | Goods before → after load |
|---|---|---|---|---|
| `dev-world` | main, 2026-09-25 | 13 | $90.00 → $90.00 | belt 195, bread 5, dough 240, lift 19 (unchanged) |
| `world-5` | main, 2026-09-30 (generator v3) | 15 | $979,910.00 → same | belt 50, dough 25, lift 10 (unchanged) |
| `world-9` | starting-loop branch, 2026-10-06 (generator v5) | branch 19 | $1,000,000.00 → same | belt 50, dough 5, lift 10 (unchanged) |
| `world-10` | starting-loop branch, 2026-10-07 (generator v5) | branch 19 | $1,000,000.00 → same | belt 50, dough 5, lift 10 (unchanged) |

Each loads at schema 24 with the same lot, equipment and site counts. The session loader (`GeneratedWorld.LoadOrCreate` with the
stored layout), a save and a fresh load keep the same figures.

The merge's upgrade chain has a stated caveat (`GoodsSnapshotStore`): a save the branch wrote at its v18-v21 "does not load
correctly", because main reuses those numbers. The branch-v19 saves were checked step by step. Main's v19→v20 sets recipes to
automatic, which is right. v20→v21 keeps the stored `service` role (`Role ??= ""`). v21→v22 adds no second back door: each
restaurant still has one. v22→v23 starts the ledger with carried = cash. v23→v24 re-tunes the per-3600 s rates once (531 → 53),
which is right because branch v19 predates the branch's own game-hour change. So branch-v19 saves load correctly.

Residual risk (from reading the code, not observed): a save the branch wrote at its v20 (ledger) or v21 (game-hour rates) is read as
main's v20/v21. Both then run v22→v23, which clears the stored ledger entries and carries the whole cash forward. Cash and goods stay
the same, but the ledger history is lost. A branch-v21 save also runs v23→v24, so rates already counted per game hour are re-tuned
again and demand drops sixfold. No such save exists on this machine; the newest branch save is v19.

## Follow-up: DevSite fixtures load without the build list (owner: "change tests")

The seven DevSite PlayMode fixtures (`BeltPlacementTests`, `BuildingPresenterTests`, `CustomerPresenterTests`,
`EquipmentPlacementTests`, `LogisticsPanelTests`, `MainMenuTests`, `SessionBootstrapTests`) now load DevSite with
`EditorSceneManager.LoadSceneAsyncInPlayMode`, as the WorldGen and SampleScene fixtures already did. The authoring test became
`SessionAuthoringTests.WorldGenIsTheBuildSceneAndDevSiteIsWiredForSessions`: WorldGen is the only enabled build scene, DevSite is
listed but disabled, and DevSite's session wiring is checked as before. No game code loads scenes by name. The build list is unchanged.

| Run | Mode | Filter (type) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| pm-fix-session-edit (19:29:54-19:30:19Z) | EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly) | 147 | 144 passed, 3 skipped (on-request measurements) | [JSON](post-merge-20261012/artifacts/fixes/session-editmode.json) |
| NUnit run 2 (19:30:29-19:35:18Z) | PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly) | 70 | 60 passed, **2 failed**, 8 skipped | [XML](post-merge-20261012/artifacts/fixes/session-playmode-nunit.xml) |

22 of the 24 DevSite tests pass, including `ServerRestartShowsOvenWhereLastCommitted` and the session bootstrap tests. The two
failures had been hidden behind the build-list failure since `88c023b`:

- `EquipmentPlacementTests.ShiftClickSendsAStackAcrossOrFillsTheOtherContainer` (line 483): it expects the oven's input capacity
  to be 1, but it is 3. Decision 0043 (main, before the merge) widened the oven to 3 input slots. The test's second half (one dough
  fits beside 19) assumes a single slot. Hypothesis: a stale test, not a defect. Not fixed.
- `LogisticsPanelTests.TruckCardControlsTakeThePointerAfterTheWindowGrows` (P0-01 regression check): at full width, the new truck
  card's route arrow is drawn at (687, 256, 26×25), but a pick at its centre returns the other truck's card,
  `logistics-truck-dev-truck-1` (605, 336, 330×136), which does not contain that point. This is the P0-01 symptom: picking and
  drawing disagree after the window grows. The branch's fix (`CentredWindow.Overlay`) was removed on merge in favour of main's
  UXML screens (architecture.md, 0038 section). Hypothesis: the merge's UXML port of the logistics screen reintroduced P0-01.
  That is in the merge's reconciliation (ported UXML), so it is **reported, not fixed**. It needs a manual click to confirm, as
  P0-01 did.
