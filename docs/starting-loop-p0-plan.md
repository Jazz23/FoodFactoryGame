# P0 Plan: Starting-Loop Baseline Playthrough

Date: 2026-10-06. Parent: [starting-loop-plan.md](starting-loop-plan.md), piece P0.

## Purpose

Play the opening loop in a fresh generated world on the current HEAD and record what works, what breaks and what confuses
the player. The output is evidence and a triaged list of findings. **No gameplay, content or scene authoring changes.**
The only new code is a test-only fixture.

Owner decision (2026-10-06): the player-facing pass uses **simulated input**, meaning Input System virtual devices that go
through the real action maps, not OS input or a human.

## Setup

| Item | Value |
|---|---|
| Code | HEAD at run time, clean tree. Record the commit hash. |
| Editor | Unity 6000.5.9f1, play mode stopped, open scene not dirty |
| Scene | `Assets/Scenes/WorldGen.unity` (the generated-world path) |
| World | Fresh each run. Seed `piece-two` (matches earlier records), plus one second seed for layout variance |
| Saves | New isolated directory per run via `SessionOptions` (save dir, identity file, free UDP port), as `WorldGenSessionTests` does. Never the application database. |
| Fixture | New `StartingLoopPlaythroughTests` in `FoodFactoryGame.Session.PlayModeTests`. Ignored unless `Temp/starting-loop.flag` exists (same gating as the capture tests). Run by `testName`, one test per call. |

**Preconditions:** compile, then record a console baseline. Run `FoodFactoryGame.Session.PlayModeTests` once as a baseline
and record which tests fail before any P0 code exists (two input tests are known to fail when the Editor is unfocused).
Doing this before writing the fixture shows the harness itself works.

## Two passes

Each pass is its own test and starts from its own fresh world.

- **Pass R (requests):** drives every action through the client's network bridge requests, the same calls the UI makes.
  It shows whether the **systems** chain together.
- **Pass I (simulated input):** drives every action through a virtual `Keyboard`/`Mouse` (`InputTestFixture`) and UI
  Toolkit pointer events. It shows whether a **player** can do it. Rules for Pass I:
  - It may *read* state, to aim, to find targets and to judge results, but it acts only through devices.
  - It never calls bridge requests or private methods.
  - Aiming is closed-loop: feed Look deltas until the crosshair ray hits the target (bounded attempts). Walking is also
    closed-loop: hold Move toward the target until within reach. Every helper has a timeout.
  - UI clicks go to the centre of the target element's panel rect, converted to screen space.

A step failure is recorded and the pass continues where later steps can still run (soft assertions). A **blocker** stops
only the steps that depend on it. Pass I uses the same step IDs as Pass R, so the two results can be compared side by
side.

## Steps

| ID | Action | Record |
|---|---|---|
| S1 | Create the world, host joins | Cash, start equipment (expected `start-counter`, `start-table`), spawn point, starter inventory, time to playable |
| S2 | Walk in through the front door | Door opens, avatar enters, camera behaviour indoors |
| S3 | Supplier: buy an oven and dough | Offers listed, prices, exact debits, where the goods land |
| S4 | Place the oven inside the shell | Whether the 3x3 oven fits, the ghost's valid/invalid verdict, rejection reasons |
| S5 | Load dough, bake, take bread out | Auto-start, bake time, output, spoilage timers shown |
| S6 | Put bread into the register, work it | Staffing works; staffing persists while the player walks away or uses the oven |
| S7 | Wait for sales | Time to first customer arrival, first sale and first seated diner; sales and walk-outs over 10 real minutes; cash delta = sales |
| S8 | Watch the customer figures | They enter by a door, queue at the register, sit, leave. No wall clipping. |
| S9 | Let the register run empty, then leave it unstaffed | What the player is told in each case (input for P3) |
| S10 | Build mode: add a table and decor, add a wall and a door | Exact charges and refunds, seat reachability, ambience change |
| S11 | Place a dock on the apron | Current street rule verdict; truck docking. This is the pre-back-door baseline for P1. |
| S12 | Buy a second restaurant, walk over carrying goods, route a truck between the two docks | Purchase, carry, route, delivery, sales at the unviewed site |
| S13 | Restart the host mid-loop: bread in the register, a customer queued, a truck driving | Everything below conserved; staffing cleared as documented (0035) |

**Demand rate (M1):** 10 real minutes is too short to judge the sales rate. If `GeneratedWorld` can be created outside the
scene, add a domain-only measurement: the same seed's world with a stocked, staffed starting restaurant, advanced 2 game
hours headless. Record arrivals, choices, sales and walk-outs per hour. If it cannot be created that way, record that and
use S7 only.

## Checks at every step boundary

- **Conservation:** units per item across all locations, company cash, and recorded charges. Any change a step does not
  explain is a **blocker**.
- Console errors and warnings since the baseline.
- The `[Goods]` commit summary line, and frame time at S7 and S12.
- Captures at S1, S2, S4, S6, S7 (customers inside), S10, S11 and S12. Save them via `Temp/<run>/`, then move them to the
  artifact folder, as described in `development.md`.

## Findings

One row per finding:

| Field | Content |
|---|---|
| ID | `P0-nn` |
| Step / pass | e.g. `S6 / I` |
| Severity | **Blocker**: the loop cannot continue. **Major**: the player can continue only with knowledge the game doesn't give, or the outcome is wrong. **Minor**: friction. **Polish**. |
| Expected | Cite the GDD section or decision |
| Observed | Facts only |
| Evidence | Log line, capture, revision, test output |
| Bucket | P1 / P2 / P3 / P4 / P5 / outside A |

A finding that only Pass I hits is a UI or input problem. One that both passes hit is a systems problem.

Record findings only, with no fixes. The one exception is a harness bug in the new fixture.

## Output

`docs/verification/starting-loop-baseline-<date>.md` and a folder of the same name holding captures, NUnit XML, console
excerpts and M1 numbers. The record contains:

1. Commit, Editor, seeds, save paths. For each run: filter, run identity, matched count (zero = failure), result, artifact
   path.
2. A step table per pass (pass / fail / blocked, with times and values).
3. The findings table, sorted by severity.
4. Recommended changes to P1-P5 scope.

## Done when

- Both passes ran to S13 or stopped at recorded blockers, on both seeds.
- Every step has a result with evidence, and M1 is measured or recorded as not possible.
- Findings are triaged into buckets, and the parent plan is updated with any scope change.

## Limits (stated in the record)

- Simulated input skips OS focus, the real cursor and scan codes. Real OS input and a player build are checked in P5.
- One process only. Separate-process multiplayer is checked in P5.
- Editor timings are not player-build timings.
- Every value measured is PROTOTYPE. P0 measures pacing but does not judge it (owner decision 6).

## Risks

- **Closed-loop aiming is flaky.** Mitigation: bounded retries, and the target is logged on failure. A helper failure is
  a harness bug, not a game finding, unless Pass R shows the same step failing.
- **A long runtime.** S7 is 10 minutes per pass per seed. M1 carries the rate measurement.
- **Known flaky truck-ordering test.** It doesn't block P0; note it if seen.
- **Domain reload drops the session.** Never edit scripts during a run.
