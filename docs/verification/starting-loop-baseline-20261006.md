# P0 Starting-Loop Baseline Playthrough (2026-10-06)

Plan: [starting-loop-p0-plan.md](../starting-loop-p0-plan.md). Parent: [starting-loop-plan.md](../starting-loop-plan.md), piece P0.
Artifacts: [starting-loop-baseline-20261006/](starting-loop-baseline-20261006/) (one folder per pass and seed: `steps.md`,
`conservation.txt`, `console.txt`, `figures.txt`, `nunit.xml`, key captures; plus `m1-demand.txt` and the baseline XML).

This is an audit. Nothing in gameplay, content or scenes changed. The only new code is test-only:
`Assets/Tests/PlayMode/Session/StartingLoopPlaythroughTests.cs` (+ `.Input.cs`) and
`Assets/Tests/EditMode/Session/StartingLoopDemandMeasurement.cs`. Every value measured is PROTOTYPE; pacing is measured,
not judged (owner decision 6).

## 1. Setup and runs

| Item | Value |
|---|---|
| Code | `d4fa69e23bb43e08396e2387ffe3e2007d16acf3` (HEAD). Tree not clean: `Food_Factory_Restaurant_GDD.md` modified and the two plan documents untracked, all documentation; no code or asset changes besides the new test files |
| Editor | Unity 6000.5.9f1, play mode stopped, `WorldGen.unity` open and not dirty; Game view 1470x717 (UI panel 1440x702) |
| Scene | `Assets/Scenes/WorldGen.unity` |
| Seeds | `piece-two` (earlier records) and `p0-second` |
| Saves | New directory per run under `%TEMP%\FoodFactoryStartingLoop\<guid>` (save dir, `host.db` identity, free loopback UDP port), deleted afterwards. The application database was never opened |
| Gate | `Temp/starting-loop.flag` (empty for the recorded runs; deleted afterwards). Its debug lines `watch=` / `only=` were used only while fixing the harness |

Console baseline before any P0 code: Editor console 0 errors, 0 warnings, no compile failure. After adding the fixture:
compiled with 0 errors and no new warnings (the CS0618 `FindObjectsSortMode` warnings come from older test files).

| Run | Filter (type) | Run identity (NUnit `start-time`) | Matched | Result | Artifact |
|---|---|---|---|---|---|
| Baseline before P0 code | `FoodFactoryGame.Session.PlayModeTests` (assembly, playmode, async) | 2026-10-06 19:30:51Z, 183 s | 44 | 41 passed, 1 failed, 2 skipped | `baseline-session-playmode.xml` |
| M1 demand | `FoodFactoryGame.Session.Tests.StartingLoopDemandMeasurement` (testName, editor) | 2026-10-06 19:52:09Z, 2.5 s | 1 | passed | `m1-demand.xml`, `m1-demand.txt` |
| Pass R, `piece-two` | `...StartingLoopPlaythroughTests.PassRequestsFirstSeed` (testName, playmode, async) | 2026-10-06 19:52:27Z, 734 s | 1 | passed (13/13 steps) | `pass-r-piece-two/` |
| Pass I, `piece-two` | `...StartingLoopPlaythroughTests.PassInputFirstSeed` | 2026-10-06 20:43:16Z, 773 s | 1 | failed: S12 | `pass-i-piece-two/` |
| Pass R, `p0-second` | `...StartingLoopPlaythroughTests.PassRequestsSecondSeed` | 2026-10-06 20:56:19Z, 764 s | 1 | passed (13/13 steps) | `pass-r-p0-second/` |
| Pass I, `p0-second` | `...StartingLoopPlaythroughTests.PassInputSecondSeed` | 2026-10-06 21:09:24Z, 829 s | 1 | failed: S12 | `pass-i-p0-second/` |

The playthrough test fails whenever any step does not pass, so a failed run means "a finding was recorded", not "the
harness broke". The baseline failure was `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (7 drawn
samples, needs 8: a timing margin, not seen before P0 code existed). The two known input tests that fail with the Editor
unfocused **passed** in this baseline. The two skips are the flag-gated capture tests.

Harness fixes made during P0 (allowed by the plan): Pass I walks the streets to a building before aiming at it (from the
start apron its own walls hid the neighbour); UI clicks count a server reply as the click's effect (a local host can answer
within two frames); a UI control that pointer picking cannot reach is recorded as a finding and then tried with a pointer
event sent straight to it, and the step is marked failed.

## 2. Step results

Seconds are real seconds. `R` = bridge requests, `I` = virtual Keyboard/Mouse plus UI Toolkit pointer events.

| Step | R `piece-two` | I `piece-two` | R `p0-second` | I `p0-second` | Values (same in both passes unless noted) |
|---|---|---|---|---|---|
| S1 world, host joins | pass 2.4 | pass 2.6 | pass 2.4 | pass 2.6 | $1,000,000.00; `start-counter` + `start-table` only; spawn on the apron (cell (0,7) / (8,0)), outside the shell; starter inventory dough x5, belt x50, lift x10; playable 1.7-2.5 s after `Begin` |
| S2 walk in by the front door | pass 3.6 (teleport) | pass 2.0 | pass 3.6 (teleport) | pass 2.1 | Front door on the street side; inside, camera top-down and pointer free (aims with the pointer, decision 0019). The door-swing probe found no `DoorSwing` and measured nothing; captures show the leaves open |
| S3 supplier: oven + dough | pass | pass | pass | pass | 7 offers listed; debit exactly $152.50; dough into the inventory, the oven held in it. I: both Buy clicks reached the panel from the virtual mouse |
| S4 place the oven | pass | pass | pass | pass | 106 interior anchors accept the 3x3 oven, 14 `blocked`; I: hotbar key, green ghost, left click |
| S5 bake | pass 100 | pass 102 | pass 100 | pass 100 | Auto-start 0.2-2 s; 10 bread in 99 s (10 s each); shelf life 3600 s, shown as `58m` on the slot |
| S6 stock + work the register | pass | pass | pass | pass | Staffing **persists** after using the oven and walking out of the door and back |
| S7 10 real minutes | pass | pass | pass | pass | First arrival / sale / seated diner: `piece-two` 96 / 160 / 166 s (I: 96 / 160 / 164); `p0-second` 327 / 405 / 409 s (I: 315 / 395 / 399). Sales 2, 2, 1, 1; walk-outs 0; cash delta = sales x $2.50 exactly. Frame time mean 6.8-7.2 ms, p95 7.9-9.5 ms |
| S8 figures | pass | pass | pass | pass | Every figure of this restaurant's customers (2, 2, 1, 1) entered through a door; 0 samples in a wall cell. Thin evidence (1-2 figures a run) |
| S9 empty, then unstaffed | pass | pass | pass | pass | Texts below (P0-05) |
| S10 build mode | pass | pass | pass | pass | Bistro table $60.00, wall art $70.00 (ambience 0 -> 11), 2-cell plaster wall $96.00, panel door $150.00, art sold back -$70.00: net exactly $306.00, as previewed. Both tables and the register stay reachable from the street |
| S11 dock on the apron | pass | pass | pass | pass | 46 outdoor anchors accept a 2x1 dock, 2 `out-of-bounds`; placed at (0,0) with street reach, $60.00. Pre-back-door baseline for P1 |
| S12 second site + truck | pass 7.7 | **fail** 38.7 | pass 22.5 | **fail** 90.3 | Nearest for sale: `piece-two` 18 m, $24,300; `p0-second` 102 m, $100,800. I: bought through the buy panel and walked the streets carrying the goods (5 waypoints, 100 m). Dock and register at the second site. R: truck delivered 5/5 dough in 6 s / 20 s. I: stopped at the truck's route chooser (P0-01). The second register is unstaffed once the player leaves: 0 sales there (P0-02). Frame time (R) mean 16.7-18.1 ms, p95 22.6-29.1 ms |
| S13 restart mid-loop | pass | pass | pass | pass | Bread in the register, a truck driving (R) / none (I, blocked by P0-01), no customer queued (could not be forced). The restart resumed exactly the committed state (live was 1 revision / 1 s ahead and identical in goods and cash); units, cash, equipment and the truck (`ToDropoff`, on road) conserved; staffing cleared as 0035 says |

**Conservation:** all 56 step boundaries (14 per pass, including the check just before the restart) balanced: cash plus
recorded charges moved only by sales, packs, trucks and property; units only by packs, bakes and sales; no equipment
disappeared unsold (`*/conservation.txt`).

**M1 demand** (domain only, `m1-demand.txt`; counter stocked with 20 TEST-ONLY bread, worked by a TEST-ONLY player, 7200
clock seconds in 60 s steps):

| Seed | Districts reaching the restaurant | Chose it, hour 1 / 2 | Sales hour 1 / 2 | Walk-outs | First arrival / first sale |
|---|---|---|---|---|---|
| `piece-two` | 7 of 25 (693 of 1870 customers/h) | 8 / 11 | 6 / 12 | 0 | 240 / 300 clock s |
| `p0-second` | 7 of 25 (342 of 1870 customers/h) | 5 / 11 | 5 / 10 | 0 | 480 / 600 clock s |

The city creates about 1,830 customers a clock hour; the starting restaurant gets 0.3-0.6 % of them. `piece-two` ran its
20 bread down to 2 by the end of hour 2, so hour 2 was close to stock-limited.

## 3. Findings (by severity)

A finding only Pass I hits is a UI or input problem; one both passes hit is a systems problem.

| ID | Step / pass | Severity | Expected | Observed | Evidence | Bucket |
|---|---|---|---|---|---|---|
| P0-01 | S12 / I (both seeds) | **Blocker** for the truck sub-loop (cause not established) | Decision 0023: a truck card's Route arrows pick a route, Assign sends it | The truck card's controls cannot be reached by the pointer. Neither `Pick` nor `PickAll` reaches the truck card anywhere inside it (they stop at `logistics-trucks`), while the route cards beside it pick and click normally. A virtual-mouse press on `>` and a pointer event sent straight to the button both left the draft on "Parked". The `>` also overflows its card toward the "Other sites" column. This is the only UI path to put a truck on a route. Pass R (requests) routes and delivers fine, so it is UI only | `pass-i-*/steps.md` S12 "UI FINDING" (bounds, ancestors, PickAll); `pass-i-*/failed-s12.jpg` | P2 (logistics screen); confirm with one manual click in the Editor first |
| P0-02 | S12 / R and I | Major | Parent plan goal and P5: "see it sell while nobody views it"; GDD distant-site operation | A register sells only while staffed and staffing ends when the player leaves the site (0035), so a second restaurant never sells unless a player stands in it. No employees exist in the generated-world loop | `steps.md` S12: second register staff '' after leaving, 0 sales | Owner decision (0035 Q5 / option B employees); outside A unless P5 is reworded |
| P0-03 | S7, M1 / R and I | Pacing (measured, not judged) | Owner decision 6 (targets open) | 10 real minutes: 1-2 sales; first sale 160-405 s after staffing. M1: 5-12 sales per clock hour, first sale 300-600 clock s, 0 walk-outs. Bread lasts 3600 s, so a register stocked with 10 bread at ~6 sales/h loses some to spoilage | `m1-demand.txt`; S7 notes | P4 |
| P0-04 | code review / M1 | Minor | One clock for pacing | Customer rates are per 3600 clock s (`GoodsDistrict.CustomersPerHour`, `GoodsWorld.Customers.cs:277`), while road traffic's "game hour" is 60 clock s (`RoadTraffic.HourSeconds`). A rush hour lasts a real minute, but demand is counted per real hour. "2 game hours" in the plan is therefore ambiguous; M1 used 7200 clock s | `RoadTraffic.cs:13`, `GoodsWorld.Customers.cs:277-280` | P4 |
| P0-05 | S9 / R and I | Minor | Parent plan P3: the player is told why nothing sells | Empty and staffed: "No customers waiting; keep Bread in the input" (it never says the input is empty). Empty and unstaffed: the same line plus "Staffed by nobody: customers are not served", shown only inside the register screen. The world HUD line with no screen open mentions neither | `steps.md` S9; `pass-r-piece-two/s9-empty-unstaffed.jpg` | P3 |
| P0-06 | code / S3, S12 | Minor | P3's sales ledger reads existing records | Supplier purchases, property purchases and truck purchases record no cents in their outcome (`Record(...)` without `Cents` in `GoodsWorld.Supply.cs:127,146,158`, `GoodsWorld.Property.cs:118`); only build-mode orders do (`RecordCents`). Sales leave only `Diners.Served` and the customer's `PaidCents`, which goes when the customer leaves. A ledger needs a new record | P0 conservation had to rebuild spends from offer prices | P3 (scope grows: a durable spend/sale record) |
| P0-07 | S6, S9, S12 / I | Minor | Factorio-style slot UI | A click moves a whole slot stack; there is no way to move part of one (half, or one unit). The fixture had to put all bread on the register and ship a separate dough pack | harness changes in `StartingLoopPlaythroughTests.cs` S6/S12 | P3 (or outside A) |
| P0-08 | S5, S7 / R and I | Minor (Editor) | Decision 0012 signals: commit <= 50 ms | One-time `[Goods]` warnings in every run: 63.9, 77.8, 89.8 and 381.3 ms commits (S5 or S7), payload 75-135 KB, average 5-7 ms | `*/console.txt` | P4 (rerun 0025 budgets in a player) |
| P0-09 | S1 / R and I | Minor | Parent plan P4 proposal: start kit for a restaurant | The player starts with the dev starter goods: 5 dough, 50 belts and 10 lifts. Belts and lifts have no use in the restaurant loop | S1 notes | P4 |

Not findings: the indoor camera aims with the free pointer (decision 0019, expected); the second site's dock "out-of-bounds"
anchors are lot edges; register staffing persisting while the player cooks (S6) is the expected 0035 behaviour; the
restart clearing staffing is documented (0035).

## 4. Recommended changes to P1-P5

- **P1:** baseline is that any outdoor cell with street reach takes a dock (46 of 48 anchors on both starting lots), so
  the back-door rule will remove most of them. Nothing in P0 blocks P1.
- **P2:** add the logistics truck-card fix (P0-01) to P2, which already reworks the logistics screen. First confirm it
  with one manual click in the Editor. The truck-route systems path works (Pass R delivered in 6-20 s and survived a
  restart while driving).
- **P3:** grows. It needs a durable record of spends and sales for its ledger (P0-06), and its readout must cover the
  empty-register and unstaffed cases outside the register screen (P0-05). Partial stack moves (P0-07) are a candidate.
- **P4:** measure pacing in the customer clock, and decide whether demand should follow the 60-second traffic hour
  (P0-04). Baseline numbers: 5-12 sales per clock hour, first sale 5-10 clock minutes after opening (P0-03). Replace
  the dev starter goods (P0-09). Rerun the 0025 budgets in a player build, since Editor commits reached 381 ms (P0-08).
- **P5:** "see it sell while nobody views it" cannot pass under 0035 staffing (P0-02). Reword it, or decide 0035 Q5
  and employees first. Its customer-figure checks need more customers than 1-2 per 10 minutes; use a TEST-ONLY
  district as the session tests do.

## 5. Captures

The fixture writes full-size PNGs at every capture point, about 68 MB for the four runs. Only these were kept, scaled to
1280 px JPEG; the `capture …png` lines in each `steps.md` name the full set as it was taken.

| Image | Shows |
|---|---|
| `pass-r-piece-two/s1-start.jpg` | S1: spawn on the apron |
| `pass-r-piece-two/s2-indoors.jpg` | S2: top-down camera inside |
| `pass-r-piece-two/s4-oven.jpg` | S4: the placed oven |
| `pass-r-piece-two/s6-register.jpg` | S6: stocked, staffed register |
| `pass-r-piece-two/s7-customers-inside.jpg`, `s7-overview.jpg` | S7/S8: a customer coming in by the front door |
| `pass-i-piece-two/s7-customers-inside.jpg` | S7 in Pass I (devices) |
| `pass-r-piece-two/s9-empty-unstaffed.jpg` | S9: the register screen, empty and unstaffed (P0-05) |
| `pass-r-piece-two/s10-build.jpg` | S10: table, interior wall and door |
| `pass-r-piece-two/s11-dock.jpg` | S11: the dock on the apron |
| `pass-r-piece-two/s12-sites.jpg`, `pass-r-p0-second/s12-diner.jpg` | S12: the start site; the second restaurant 102 m away |
| `pass-i-piece-two/failed-s12.jpg`, `pass-i-p0-second/failed-s12.jpg` | P0-01: the truck card's `>` overflowing its card, route still "Parked" |

## 6. Limits

- Simulated input skips OS focus, the real cursor and scan codes. Real OS input and a player build are P5.
- One process: host only, no remote client. Separate-process multiplayer is P5.
- Editor timings are not player-build timings.
- Pass R has no request for walking, so it moves the avatar with `PlayerAvatar.Teleport` (as the session tests do).
- No customer could be queued at the restart without TEST-ONLY seeding, so S13 checked a queued customer on no run.
- S8 judged only 1-2 figures per run.
- In Pass I, S13 had no truck driving, because P0-01 stopped the route.
- The door-swing probe in S2 found no `DoorSwing` component, so door opening was not measured. The captures show it.
- P0-01's cause is not established. A UI Toolkit picking quirk of the test panel cannot be ruled out without a manual click.
