# Verification: generated worlds start in their own restaurant (decision 0028, piece 2)

Date: 2026-09-29. Unity 6000.5.9f1, Windows 11, live Editor through the Pipeline MCP `run_tests` tool. That tool reports
the filter, matched count and per-test results but no run ID or results file (`StatusPath: null`), so none are listed.
Players' captures are in [property-piece2-20260929/](property-piece2-20260929/).

## Automated runs

| Filter (mode) | Matched | Result |
| --- | --- | --- |
| assembly `FoodFactoryGame.Goods.EditModeTests` (EditMode) | 179 | 178 passed, 1 failed: `TruckTests.StepSizeDoesNotChangeTheOutcome` |
| assembly `FoodFactoryGame.Session.EditModeTests` (EditMode) | 93 | 92 passed on the first run; the failure (`WorldArtAuthoringTests.WorldGenPresenterHasEveryArtPiece`, art catalog lost by the scene rebuild) was fixed and the test re-run alone: 1/1 passed |
| assembly `FoodFactoryGame.World.EditModeTests` (EditMode) | 21 | 21 passed |
| assembly `FoodFactoryGame.Baseline.EditModeTests` (EditMode) | 4 | 4 passed |
| assembly `FoodFactoryGame.Goods.PlayModeTests` (PlayMode, async) | 2 | 2 passed |
| assembly `FoodFactoryGame.Session.PlayModeTests` (PlayMode, async) | 30 | 30 passed (4 new `WorldGenSessionTests`) |

`StepSizeDoesNotChangeTheOutcome` is the known nondeterministic truck test (see the piece 1 notes in `architecture.md`):
with piece 2 stashed it passed once and failed once in two runs on the unmodified branch, with expected and actual swapping
between runs.

New tests: `PropertyTests.APurchaseGrantsTeammatesButNotEmployeesOrOutsiders`, `ViewsCarryEveryPropertyAndNameABoughtSite`,
`RejectionsGrantAndChargeNothing`; `GeneratedWorldTests` (4: created once and reloaded unchanged, format 2 and a
pre-piece-2 format 3 save keep the dev world, joining grants the starting site and company sites, apron spawns);
`GoodsListenServerTests.PropertyPurchaseRepliesAndReachesEverySubscriber` (unknown-lot, not-for-sale and insufficient-funds
replies, acceptance, a second subscriber sees the property, the teammate subscribes to the new site);
`WorldGenSessionTests` (4: spawn on the starting apron with no dev seed and the dev ground hidden; the shell's bounds match
the map's building to within one cell and the lot's centre is the scene origin; a dock bought from the supplier places on
the apron; a purchase through the buy panel is first rejected for funds, then accepted, reaches the remote teammate's
baseline and grant, is watched by the buyer and turns the awning green).

## Player build

`build` of `Assets/Scenes/WorldGen.unity` only (build settings unchanged) to `build/WorldGen/FoodFactoryGame.exe`, options
Development and DetailedBuildReport: build `build_9f923fb20087`, **Succeeded**, 0 errors, 3 warnings, 227,726 ms. Report:
`TestResults/piece2-20260929/worldgen-build-status.json` (not tracked).

## Separate-process multiplayer check

Host: the Editor in play mode on WorldGen, `SessionRoot.Configure` with the isolated save
`TestResults/piece2-20260929/host-save`, seed `piece-two`, then Host. Guest: the WorldGen player,
`-connect 127.0.0.1 -name Guest -identity …/guest.db -save …/guest-save -logFile …/guest.log`.

- New world: primary site `site-restaurant-0142` (start `restaurant-0142`), `$5,000.00`, both players granted it, the join
  answer naming it on both clients (readouts in `start-host.png` and `start-guest.png`: both avatars on the paved apron in
  front of the shell's doorway, the city around it).
- Purchase: the nearest for-sale restaurant, `restaurant-0140` ($8,100.00, lot 10 x 11). TEST-ONLY: $3,100.00 was added to
  this isolated save with `AdjustCashDurably` so it was affordable. The host opened the buy panel for its lot and pressed Buy
  (`PropertyPanel.Buy`, the button's path). Result: accepted; cash $0.00; `Properties` = both lots; both players granted
  `site-restaurant-0140`; the host watching it; its awning green (`panel-for-sale.png`, `panel-owned.png`,
  `purchase-before.png` yellow, `purchase-after.png` green).
- Guest process log (its own presenter and subscription):
  `[World] restaurant-0140 awning re-tinted: owned by this company.` and
  `[Session] Receiving site site-restaurant-0140 for remote management.`
- Editor console: no errors from the live session (only the two expected `SpawnablePrefabs is null` lines from the earlier
  PlayMode fixtures).

## Visual acceptance

Captures are ready for review. **Not yet reviewed** by anyone other than the implementer; the acceptance criterion stays open
until that review.

## Not verified

- A two-player-build check (both processes built); the host here was the Editor.
- A guest capture of the re-tinted awning (the guest's camera at its spawn does not see `restaurant-0140`; its log is the
  evidence).
- The buy panel opened by aiming and pressing E in a player build (the tests and live check open it through
  `OpenProperty`, the method the hover path calls).
