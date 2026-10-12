# Starting loop, stage 1: truck-card fix and P3 (2026-10-07)

Pieces: the truck-card fix (P0-01) and P3 (ledger and readiness readout) of [the starting-loop plan](../starting-loop-plan.md).
Decision: [0038](../decisions/0038-cash-ledger-and-restaurant-readiness.md). Evidence folder:
[starting-loop-stage1-20261007/](starting-loop-stage1-20261007/). Unity 6000.5.9f1, Editor runs through the Unity MCP.
All stateful runs used isolated temporary saves; the application database was never opened.

## Test runs

| Run | Filter (type, mode) | Identity | Matched | Result | Artifact |
|---|---|---|---|---|---|
| Baseline goods | `FoodFactoryGame.Goods.Tests` (assembly, editmode) | 2026-10-07 ~15:44Z | 232 | 232 passed | `baseline/goods-editmode.json` |
| Baseline session EditMode | `FoodFactoryGame.Session.Tests` (assembly, editmode) | 2026-10-07 ~15:44Z | 125 | 124 passed, 1 skipped (flag) | MCP result (not saved) |
| Baseline session PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode) | start-time 15:45:09Z | 48 | 42 passed, 6 skipped (flags) | `baseline/session-playmode.xml` |
| Negative check, P0-01 | `...LogisticsPanelTests.TruckCardControlsTakeThePointerAfterTheWindowGrows` (testName, playmode), old centring restored | 2026-10-07 ~16:00Z | 1 | 1 failed as expected | `a-truck-card/negative-check.txt` |
| Session PlayMode after the fix | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode) | start-time 16:01:38Z | 49 | 43 passed, 6 skipped | `a-truck-card/session-playmode-after-fix.xml` |
| Pass I after the fix, run 3 | `...StartingLoopPlaythroughTests.PassInputFirstSeed` (testName, playmode), flag | run in `steps.md` | 1 | S1-S12 pass (S12 included), S13 failed on a harness walk | `a-truck-card/pass-i-piece-two-run3/` |
| Final goods | `FoodFactoryGame.Goods.Tests` (assembly, editmode) | 2026-10-07 ~17:10Z | 249 | 249 passed | MCP result (not saved) |
| Final session EditMode | `FoodFactoryGame.Session.Tests` (assembly, editmode) | 2026-10-07 ~17:10Z | 125 | 124 passed, 1 skipped (flag) | MCP result (not saved) |
| Final session PlayMode | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode) | start-time 17:20:40Z | 50 | 44 passed, 6 skipped (flags) | `final/session-playmode.xml` |
| Final Pass R | `...StartingLoopPlaythroughTests.PassRequestsFirstSeed` (testName, playmode), flag `folder=starting-loop-stage1-20261007/final` | run 17:37:53Z, 770 s | 1 | passed, S1-S13 pass | `final/pass-r-piece-two/` |
| Final Pass I | `...StartingLoopPlaythroughTests.PassInputFirstSeed` (testName, playmode), same flag | run 17:55:51Z, 944 s | 1 | passed, S1-S13 pass (S12 now passes) | `final/pass-i-piece-two/` |

Final Pass I note: the Editor was not focused (`Application.isFocused` false), so 29 of its clicks, including the truck card's
`Truck route >` and `Assign`, went through the harness fallback: a UI pointer event sent to the element the panel's own
`Pick` returns at the click point, after the virtual mouse press had no effect for 10 frames. The pick check is what P0-01
broke, and no click reported a "UI FINDING" (unpickable control). The Input System device-to-UI path itself was covered by
`TruckCardControlsTakeThePointerAfterTheWindowGrows`. S12 takes about 190 s in Pass I because the avatar walks 177 m twice.

The 6 skipped PlayMode tests are the flag-gated playthroughs (the flag was absent for suite runs). The Editor console showed 14
errors after the baseline PlayMode run with none in the captured buffer; this is recorded as baseline, not a regression.
Diagnostic playthrough runs while fixing the harness are in `diag/` (shortened with the flag's `only=` line; not acceptance).

## A. Truck card (P0-01)

- **Cause, with evidence.** In a running session with a route, `PickAll` at the drawn `<` arrow hit the Buy button; a vertical
  pick scan showed the drawn card about 110 px below where it was picked. Re-assigning the window's `translate` moved its
  `worldBound` from y=154 to y=42. The window was centred with `left/top: 50%` and `translate: -50% -50%`; after the window grew
  (the route chooser appears), the drawn transform kept the old offset while picking used the new one. The P0 capture shows the
  window drawn at y~154 with its bottom cut off.
- **Fix.** `CentredWindow.Overlay`: a full-screen overlay with `pickingMode = Ignore` centres the window with flex; used by the
  logistics, property, employee-script and HUD screen windows. The truck card's chooser rows now shrink instead of overflowing.
- **Test.** `TruckCardControlsTakeThePointerAfterTheWindowGrows` (DevSite): picks at every truck-card button centre hit that
  button, inside the card, at full width and with the overlay narrowed to 820 px; the virtual mouse chooses the new route and
  presses Assign; the server assigns it. The negative check (old centring) fails at the `<` pick.
- **Capture.** `a-truck-card/truck-card-route-chosen.png` (running game, route chosen).
- **Not done.** A real-mouse click by a human: the Game view sat off-screen (x=-31518) during the session.

## Harness fixes (test-only)

- S5 intermittently failed "pick up bread: neither ..." (2 of 5 runs): the HUD signature showed a stale baseline (9 bread in the
  inventory, 1 in the output) after the server's reply. `ClickSlot` now waits for the client revision to reach the server's.
  The window-focus hypothesis was tested and rejected.
- S13 walk stuck: with the truck now assigned, the walk to the yard dock went straight from the front door. `GoBeside` now
  routes out through the nearest back door for a dock outside.
- `CheckBooks` also requires `GoodsWorld.LedgerBalances` for every company at every step.

## B. Ledger and readout

- Cash paths and kinds: see decision 0038. EditMode: `LedgerTests` (8) and ledger assertions in the purchase, property, floor,
  furnishing, shell, truck, customer and sale-job tests.
- **v19 to v20 dry run** on a copy of a real v19 save (`b-ledger/v19-upgrade-dry-run.txt`): schema 20 in memory, opening 0,
  carried = cash, balances; file hash unchanged.
- **Payload.** Entries are 244-344 bytes; at 50 kept that is +12-17 KB per baseline per watched site (P0: 75-135 KB, +9-23%).
- **Readout.** `RestaurantReadinessTests` (9, including "ready exactly when a queued customer is served"). PlayMode:
  `TheHudSaysWhyTheRestaurantCannotSellAndTheLedgerFollowsCash` in a generated world (unstaffed, then empty-register blockers,
  register screen text, line hidden with a screen open, purchase and sale rows in the ledger tab, ledger balances).
- **Captures** (running game): `b-ledger/hud-blocked-unstaffed.png`, `hud-blocked-empty-register.png`,
  `register-screen-empty.png`, `hud-ready.png`, `ledger-view.png`.

## Conservation

Every playthrough step checks goods units and cash against charges, sales and supplier spend, plus the ledger invariant.
Final Pass R: conserved at every step and across the restart (`final/pass-r-piece-two/conservation.txt`). Final Pass I:
conserved at every step and across the restart (`final/pass-i-piece-two/conservation.txt`).

## Open

- Independent visual review of the captures (requested; not done by the implementer). Notes for the reviewer: the readout is
  small text, red on a translucent backdrop; the debug status text overlaps the logistics window title now the window is
  centred higher.
- P0-07 (partial stack moves) deferred.
- The pointer test's earlier unexplained failure is explained by Editor focus and fixed (see Test speed).

## Test speed (after stage 1, 2026-10-07)

- **TEST-ONLY `GoodsNetworkBridge.ClockRate`**: simulated seconds per real second on the server, 1 in the game and reset to 1
  when the server stops. Rules are unchanged (a step of n seconds equals n one-second steps; `OneLongStepMatchesManyOneSecondSteps`).
  Used at 10 in `RestaurantDocksInTheYardServeOneTruckAtATime` (49 s to 8 s), `AWholeSaleInTheStartingRestaurantReachesBothClients`
  (21 s to 6 s) and `TheHudSaysWhyTheRestaurantCannotSellAndTheLedgerFollowsCash` (9 s to 4 s). Not used where a test checks
  drawn figures or vehicles against the clock or exact interim values: tried on `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn`
  and reverted, because its time is the drawn-truck sampling and it saved nothing.
- **Playthrough flag `clock=<rate>`** (debug runs only; the record is marked "Debug run"). At `clock=10` on `piece-two`: Pass R
  99 s (770 s at 1), all steps pass, conserved (`diag/clock10-pass-r/`); Pass I 277 s (944 s at 1), all steps pass
  (`diag/clock10-pass-i/`, `diag/clock10-pass-i-unfocused-input/`). Pass I's remaining time is three 177 m street walks in S12,
  which are real input and stay.
- **Focus-independent UI input.** Cause of the pointer test's unexplained failures: with the Editor not the foreground app,
  UI Toolkit's `DefaultEventSystem.ShouldIgnoreEventsOnAppNotFocused()` is true and `isAppFocused` false (read by reflection in the
  Editor), so it drops all device input. Evidence: the first suite run after the clock change failed the pointer test with `focused=False` and no
  pointer event reaching the panel. TEST-ONLY `UnfocusedUiInput` sets Unity's Unity Remote hook
  (`DefaultEventSystem.IsEditorRemoteConnected`) for the test's duration and restores it. After it, the pointer test passed with
  the Editor unfocused, and Pass I took all 37 clicks from the virtual mouse with no UI-event fallback (the final Pass I above
  needed the fallback for 29).
- Already in place: Enter Play Mode Options with domain and scene reload off (`ProjectSettings/EditorSettings.asset`).
- **Runs.** Session PlayMode (assembly, playmode), 18:12Z: 50 matched, 44 passed, 6 skipped (flags), 0 failed, 196 s (254 s
  before); MCP result only, because the next run overwrote `TestResults.xml`. Goods PlayMode (assembly, playmode), start-time
  18:22:40Z: 2 matched, 2 passed (`speed/goods-playmode.xml`). Single-test runs of the three converted tests and of the pointer
  test: 1 matched and passed each.
