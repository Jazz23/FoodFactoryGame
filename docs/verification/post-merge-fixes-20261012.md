# Verification: post-merge test and UI fixes (2026-10-12)

Follows [the post-merge baseline](post-merge-20261012.md) ("Failures" 2 and 3, and the two failures in its "Follow-up").
Base: `main` at `8cfcff1` with the uncommitted post-merge work in the tree (DevSite fixtures loading by path, decision 0044).
Live Editor Unity 6000.5.9f1, `WorldGen` open and clean, play mode stopped before each run. One owner ran every compile and
test in sequence. Every stateful test and the manual session used isolated temporary saves; no application save or database
was opened. Nothing is committed.

The Pipeline runner gives no run ID of its own (every NUnit `test-run` reads `id="2"`), so each run below has an assigned ID
(`pmf-…`) and is identified by the NUnit start/end times in its XML.

## Changes

| Task | Files | Change |
|---|---|---|
| 1. Runtime customer benchmark population | `Assets/Tests/EditMode/Benchmarks/CustomerRuntimeBenchmarkTests.cs` | District wealth `20 * index` → `15 * index` (0-60%, below `GoodsWorld.PickyWealthPercent` 70), so every district buys the tier-1 bread (decision 0043). Comment updated. Target, signals and the rest unchanged. |
| 2. Truck road benchmark | `Assets/Tests/EditMode/Benchmarks/TruckRoadBenchmarkTests.cs` | The 300 clock-second window runs three times, each on a fresh world built with the same seed and setup (`MeasureOnce`); asserts the median p99 ≤ 16.7 ms; one `[Benchmark]` line logs the median and every run. The per-run conservation and legs checks are kept. Budget unchanged, no forced GC, no truck code change. |
| 3. Oven shift-click test (decision 0043) | `Assets/Tests/PlayMode/Session/EquipmentPlacementTests.cs` (`ShiftClickSendsAStackAcrossOrFillsTheOtherContainer`) | Reads the oven input's slot count from the replicated location (3, from content) and the dough stack size from `SessionRoot.MaxStack` (20); buys `supplier-dough-5` until the inventory holds one full stack per slot plus a stack of 5; each full stack moves whole (the first batch takes one dough); the stack of 5 then moves only the one that fits, leaving 4, and the input is full. Inventory half unchanged. |
| 4. Truck-card regression (P0-01) | `Assets/UI/Theme/Theme.uss`, `Assets/UI/Resources/UI/Logistics/Logistics.uxml`, `Logistics.uss` | The logistics window is centred by flex on `logistics-root` (new theme class `ff-centering`) instead of `ff-centered`'s `translate: -50% -50%`; `.logi` gets `max-width: 100%`; columns use `flex-basis` with `flex-shrink: 1` and `min-width: 200px`; chooser captions and arrows don't shrink, and the value shrinks and elides. These are the branch's `CentredWindow`/`Column`/`Chooser` rules, now in USS. No C# change, no inline style, element names unchanged. |

Docs: `docs/development.md` (truck benchmark is a median of 3; UI editing note on growing windows), `docs/architecture.md`
(0038 section: truck-card fix status after the UXML port).

## Task 4 diagnosis (before the fix)

Manual session in play mode: DevSite loaded by path with `EditorSceneManager.LoadSceneAsyncInPlayMode`, `SessionRoot.Configure`
with save and identity under `%TEMP%\FoodFactoryTruckCardManual\pmf-manual-1` (deleted afterwards), host, logistics opened, a
truck bought and a second route created through `LogisticsPanel` (the test's setup). Screen 1470×717.

- Pick results: every button on the new truck card picked `logistics-truck-dev-truck-1` (the card below). Truck 1's `<`
  picked `logistics-buy-supplier-truck`. **No** button in the new route card picked itself. Picking was off for the whole window,
  not just one card.
- Transform: `resolvedStyle.translate` = (-586.29, -315.92), which is -50% of the current size 1172.57×631.84. But
  `worldTransform` y = 117.06 = layout y 351.67 − 234.6, so the window is drawn and reported with a translate of -50% of a
  ~469 px height (its size before the route card was added). Picking uses the fresh translate, so it sits about 81 px
  above what is drawn.
- Drawn position: the [before capture](post-merge-fixes-20261012/captures/before-fix-logistics.png) shows the window where
  `worldBound` says (top ≈ 120 px against 117.06) and running off the bottom of the screen (to y ≈ 749 of 717). So the drawn
  frame and `worldBound` share the stale offset and `Pick` alone has the new one.
- Cause (evidence-backed): UI Toolkit does not re-resolve a percentage `translate` into the drawn/world transform when the
  element's size changes; picking does. It matches the branch's `CentredWindow` note for P0-01. It is a UI Toolkit behaviour,
  which is the brief's escalation condition. It was worked around in our layer (flex centring) without touching Unity or decision
  0042's rules (USS for layout; no new inline styles).
- Real mouse: **not done.** Three OS-level clicks (`SetCursorPos`/`mouse_event` at the computed Game view position) landed
  on another window covering the Editor (the Input System's `Mouse` never updated, and `mouseOverWindow` was empty). They did
  not reach Unity, so they prove nothing about the UI. No more OS input was sent. A real-mouse click on the `>` and Assign
  still needs a person at the Editor.

After the fix, the same manual setup (`pmf-after-1`, deleted afterwards): translate (0, 0); window at (134.2, 35.3,
1171.6×631.8), centred and on screen; **all 32 visible buttons pick themselves**. Captures:
[full width](post-merge-fixes-20261012/captures/after-fix-logistics.png),
[narrow, root 820 px as in the test](post-merge-fixes-20261012/captures/after-fix-logistics-narrow.png). Each card's controls
sit inside its card; at 820 px the columns narrow, arrows stay in their cards and long values elide ("Warehouse…").
Visible change for review: at full width the centred window is taller than before (before, it ran off the bottom of the
screen), so its top-right corner now overlaps the HUD cash card.

## Runs

| Run ID | Mode | Filter (type) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| pmf-cust-1 (19:42:08-19:42:10Z) | EditMode | `FoodFactoryGame.Benchmarks.Tests.CustomerRuntimeBenchmarkTests` (testName) | 1 | passed | [XML](post-merge-fixes-20261012/artifacts/customer-runtime-benchmark-nunit.xml) |
| pmf-truck-1 (19:42:21Z) | EditMode | `FoodFactoryGame.Benchmarks.Tests.TruckRoadBenchmarkTests` (testName) | 1 | **failed**, median 23.13 ms | console line (below) |
| pmf-truck-2 (19:42:45Z) | EditMode | same | 1 | **failed**, median 17.60 ms | console line (below) |
| pmf-truck-base-1..3 (19:43:32-19:44:14Z) | EditMode | same, **original** single-window test swapped in temporarily | 1 each | passed, failed, passed | console lines (below) |
| pmf-truck-3 (19:44:45-19:45:00Z) | EditMode | same (median version restored) | 1 | passed, median 13.64 ms | [XML](post-merge-fixes-20261012/artifacts/truck-benchmark-1-nunit.xml) |
| pmf-truck-4 | EditMode | same | 1 | **failed**, median 18.13 ms | [XML](post-merge-fixes-20261012/artifacts/truck-benchmark-2-nunit.xml) |
| pmf-truckcard-1 | PlayMode | `FoodFactoryGame.Session.PlayModeTests.LogisticsPanelTests.TruckCardControlsTakeThePointerAfterTheWindowGrows` (testName) | 1 | failed at "narrow" (centring only; full width passed) | - |
| pmf-truckcard-2 | PlayMode | same, after the narrow-screen USS | 1 | passed | [XML](post-merge-fixes-20261012/artifacts/truck-card-test-nunit.xml) |
| pmf-oven-1 | PlayMode | `FoodFactoryGame.Session.PlayModeTests.EquipmentPlacementTests.ShiftClickSendsAStackAcrossOrFillsTheOtherContainer` (testName) | 1 | passed | - |
| pmf-bench-edit (19:51:21-19:51:37Z) | EditMode | `FoodFactoryGame.Benchmarks.EditModeTests` (assembly) | 10 | 10 passed (truck median 15.94 ms) | [XML](post-merge-fixes-20261012/artifacts/benchmarks-editmode-nunit.xml) |
| pmf-session-edit (19:51:46-19:51:57Z) | EditMode | `FoodFactoryGame.Session.EditModeTests` (assembly) | 147 | 144 passed, 3 skipped (on-request measurements) | [XML](post-merge-fixes-20261012/artifacts/session-editmode-nunit.xml) |
| pmf-session-play (19:52:06-19:56:53Z) | PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly, async) | 70 | **62 passed, 0 failed, 8 skipped** (flag-gated captures and the P0 playthrough) | [XML](post-merge-fixes-20261012/artifacts/session-playmode-nunit.xml) |

A first try of `pmf-truckcard-1` used the namespace `FoodFactoryGame.Session.Tests` and matched **0** tests (a failure, not a
pass); the fixture's namespace is `FoodFactoryGame.Session.PlayModeTests`. Every run above matched a nonzero count. Compiles
after each C# change finished with no errors (only the existing CS0618 `FindObjectsSortMode` warnings). The only console errors
in the PlayMode run are the expected FishNet `SpawnablePrefabs is null on <fixture>-test-remote` (15, from the remote-client fixtures'
`AddComponent<NetworkManager>`).

## Task 1: runtime customer benchmark: accepted

`ThousandCustomersFitAFrameAndCommitUnderTheWarningSignals` reaches its population (846 before the change):

- pmf-cust-1: `[Benchmark] player transfer: customers=1001 total=46.32ms save=34.57ms wrapper/action=11.75ms save phases copy=0.00 validate=3.63 json=4.02 transaction=24.58 commit+sync=2.34ms`
- `[Benchmark] runtime customers: warmup=450s customers 1001->963 (Eating=290,Queued=252,Ordering=18,Travelling=403) served=1632 walkedOut=279 | tick mean=0.33ms p99=0.75ms max=1.45ms over 300 ticks (step 0.34ms) | commits=30 avg=23.0ms max=33.3ms payload=465KB`
- `[Benchmark] save phases over 30 tick commits (mean/max ms): copy=0.00/0.00 validate=5.13/6.55 json=6.09/8.31 transaction=8.62/11.64 commit+sync=3.16/9.75`
- pmf-bench-edit (second run): transfer 23.45 ms; tick p99 0.95 ms, max 17.41 ms; commits avg 17.3 ms, max 35.2 ms; 465 KB.

The 50 ms commit signal held in both runs (max 33.3 and 35.2 ms), so no rerun was needed.

## Task 2: truck road benchmark: **not accepted** (needs an owner decision)

Acceptance asked for 3 consecutive passes. The median-of-3 test passed 2 of 5 invocations in this Editor, and never 3 in a row:

| Run | p99 run 1 / 2 / 3 (ms) | Median | Result |
|---|---|---|---|
| pmf-truck-1 | 23.13 / 32.13 / 18.22 | 23.13 | fail |
| pmf-truck-2 | 16.28 / 21.67 / 17.60 | 17.60 | fail |
| pmf-truck-3 | 13.64 / 11.71 / 19.01 | 13.64 | pass |
| pmf-truck-4 | 20.67 / 18.13 / 16.97 | 18.13 | fail |
| pmf-bench-edit | 15.34 / 24.14 / 15.94 | 15.94 | pass |

Baseline in the same Editor state (original single-window test, temporarily restored from `HEAD`): p99 10.91, 21.09 and
13.09 ms (pass, fail, pass). So the noise is in the Editor, not in the change. Every window has the same simulation (3,471
legs, 1,954 queued truck-seconds, 35,675 crates), so the runs share no state (the static `RoadNetwork` cache is per layout;
nothing else grows).

Evidence for the cause: in every run the slowest seconds fall on a gen-0 collection (the `gc=` counter steps up across
them), and each pause costs 15-35 ms. The Editor had been running for about 90 minutes with **944 MB of Mono heap in use**
(1,388 MB reserved, gen-0 count ~1,200 against ~292 in the baseline record's runs). Boehm's collection cost grows with the live
heap, so pauses are longer than in the 10-19 ms runs the trucks record measured. The median absorbs one bad window, but here
two of three windows are often over budget.

Not done, by the brief: raising the budget, forcing a GC before timing, reducing truck-tick allocations (deferred). Options
for the owner: run the benchmark in a fresh Editor or a batch-mode checkout (`unity test …`, development.md), where the heap
is small; or accept the median-of-3 as the gate only under that condition; or take on the deferred allocation work. Not
tested here: an Editor restart would end this session's live state, so it was not done without asking.

## Task 3: oven shift-click test: accepted

Passes alone (pmf-oven-1) and in the full PlayMode run. All 11 `EquipmentPlacementTests` pass in pmf-session-play, the same
set as in the follow-up baseline (no behaviour change in the others).

## Task 4: truck card: test accepted; manual click and review open

- `TruckCardControlsTakeThePointerAfterTheWindowGrows` passes every phase (full width, narrow, both virtual-mouse `>`
  presses changing the choice, Assign), alone and in pmf-session-play.
- Running-game captures: listed above.
- Not done: the real-mouse click on `>` and Assign (see the diagnosis); **independent visual review** with the three
  captures (required for a visual change; the reviewer must not be the implementer).
- Same latent pattern, not changed (out of scope): `EmployeeScript.uxml` and `Property.uxml` windows still use `ff-centered`;
  they would show the same split if they grow while open.
