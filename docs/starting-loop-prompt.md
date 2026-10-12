# Prompt: Starting Loop, Stage 1 (truck-card fix + P3 readiness and ledger)

Copy the block below into an implementation agent. It covers the next two pieces in the plan's order: the truck-card fix
(P0-01) and P3. P4, P2, P5 and P6 come later and are out of scope.

```text
You are implementing Stage 1 of the starting-loop plan in the Food Factory Game Unity project
(E:\Projects\Unity\FoodFactoryGame): (A) the logistics truck-card fix (P0-01), then (B) piece P3, restaurant readiness and a
durable cash ledger. Do A first and finish its verification before starting B.

READ FIRST: AGENTS.md; docs/starting-loop-plan.md (authoritative for scope and owner decisions, revised 2026-10-07);
docs/verification/starting-loop-baseline-20261006.md (findings P0-01, P0-05, P0-06, P0-07); Food_Factory_Restaurant_GDD.md
sections 7 ("Registers, Seats & Ambience") and 15 (alerts); decisions 0012, 0014, 0023, 0030, 0034, 0035, 0036, 0037;
docs/architecture.md; docs/development.md for the verified Unity workflow.

=== A. Truck-card fix (P0-01) ===
Problem: in the logistics screen (Assets/Scripts/Session/Logistics/LogisticsPanel.cs) the truck card's controls cannot be
reached by the pointer. Panel Pick/PickAll stop at `logistics-trucks`, while the route cards beside it pick normally. A
virtual-mouse press on `>` and a pointer event sent straight to the button both left the draft on "Parked". The `>` also
overflows its card toward the "Other sites" column. Pass R (bridge requests) routes trucks fine, so this is UI only.
Steps:
1. Confirm by hand: in the Editor, play WorldGen with an isolated save, put a truck in the logistics screen and click its
   route arrows with the real mouse. Record what happens. If the manual click works, the defect may be in the test's
   pointer path; say so and investigate that instead of changing the UI.
2. Find the cause with evidence (picking mode, layout, an overlaying element, a clipped or zero-size parent) before changing
   anything. State the hypothesis and the evidence in your report.
3. Fix it in the panel's UXML/USS/C#. Controls stay inside their card at the P0 panel size (1440x702) and at a narrow width.
Acceptance: the truck card's route arrows and Assign work by pointer; the starting-loop Pass I playthrough reaches S12's
route and delivery on seed `piece-two` (StartingLoopPlaythroughTests.PassInputFirstSeed, gated by Temp/starting-loop.flag,
delete the flag afterwards); a running-game capture of the truck card with a route chosen.

=== B. P3: readiness readout and ledger ===
B1. Ledger (senior-owned design; schema change). Today only build-mode orders record cents (GoodsWorld.Shell.cs RecordCents);
supplier, property and truck purchases do not (GoodsWorld.Supply.cs, GoodsWorld.Property.cs), and a sale leaves only the
customer's PaidCents, which goes when the customer leaves.
- Add a durable, server-written ledger entry for every company cash change: sale, supplier purchase, property purchase,
  truck purchase, build-mode order (charge and refund), and any other path that moves company cash. Find every such path;
  list them in your report. Each entry: stable ID, company, site, kind, signed cents, clock time, revision, and the related
  IDs (request, equipment, customer, offer) where they exist.
- Written in the same commit as the cash change; a rejected or failed request writes nothing; a replayed duplicate request
  writes nothing new.
- Retention is bounded (PROTOTYPE count per company, named constant). Keep a carried-forward total so that, at every
  commit, opening cash + carried total + sum of kept entries == company cash. Add that as a validation/test invariant.
- Goods snapshot schema v19 -> v20 (GoodsWorld.CurrentSchema, GoodsSnapshotStore upgrade chain) with an in-memory upgrade:
  older saves start with an empty ledger and a carried total that makes the invariant hold. Dry-run the upgrade on a copy
  of an older isolated test save first. Persist only in the existing SQLite snapshot.
- Replicate to clients of that company only, within existing baseline payload habits; report the payload change.
- Record the design in a new decision record docs/decisions/0038-*.md before or with the implementation.
B2. Readiness readout (presentation only; it writes no state).
- Computed on the client from replicated state with the same pure rules the server uses (RestaurantRules, Diners walk
  rules, staffing, register input). Do not duplicate rules: if a needed check is not a pure function today, extract it so
  server and client call the same code, without changing server outcomes.
- Blockers, in priority order: no customer door; no register reachable from the street by customers; no register staffed;
  no edible menu item in any staffed register's input.
- Warnings: no reachable seat (takeaway only); a dock not beside a back door (0037 grandfathered docks); register stock
  that spoils within a PROTOTYPE window.
- Status: customers queued and eating; recent sales and spend from the ledger (PROTOTYPE window, named constant).
- Shown in the world HUD line with no screen open (PlayerHud.cs; P0-05) and in the register screen. Fix the empty-register
  text: today it says "No customers waiting; keep Bread in the input" even when the input is empty.
- A ledger view the player can open (last entries, newest first: time, kind, site, amount). Use the Input System action
  maps for any new input; no hard-coded keys.
Deferred: partial stack moves (P0-07) unless trivially small; anything from P4, P2, P5, P6.

CONTRACTS (non-negotiable):
- The server owns all state; the readout and ledger view never write state. No server rule changes beyond writing ledger
  entries.
- Cash and goods conservation hold exactly; no new path deletes or duplicates goods or payments.
- Owner decisions in docs/starting-loop-plan.md are confirmed; do not reopen them. Proposals and PROTOTYPE values stay
  labelled; list every value you choose. Do not promote them into the GDD.
- AGENTS.md C# conventions (var; purpose comment at the top of each new file; Unity-aware null checks; serialized references
  validated by authoring checks). No save files, PlayerPrefs or other stores.
- Do not change gameplay authoring to suit tests; use isolated fixtures and TEST-ONLY seeding.

VERIFICATION (isolated save paths only; never the application database):
- Before any code: compile, record a console baseline, and run FoodFactoryGame.Goods.EditModeTests,
  FoodFactoryGame.Session.EditModeTests and FoodFactoryGame.Session.PlayModeTests once as a baseline. Known: P0 saw
  WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn fail once on a timing margin.
- Domain tests: a ledger entry per cash path; atomicity with the cash change; rejected and replayed requests write nothing;
  retention keeps the invariant; v19 -> v20 upgrade; save/restore round trip; each readout blocker and warning from a
  constructed state (pure rules).
- PlayMode: the HUD shows each blocker in a generated world (empty register, unstaffed register); the ledger updates after
  a sale and a purchase; the truck-card pointer test.
- Rerun the starting-loop playthrough Pass R and Pass I on `piece-two` at the end; conservation must still balance at every
  step, and S12 must now pass in Pass I.
- Running-game captures: the truck card, the HUD readout in the blocked and ready states, the ledger view. Request an
  independent visual review; you are not the reviewer.
- After C# changes wait for compilation, check for new console errors, run the relevant tests. On a failure, capture
  diagnostics and state an evidence-backed hypothesis before changing anything; do not add waits speculatively.
- Report for every run: filter, run identity, matched test count (zero is a failure), result, artifact path. Put the record
  in docs/verification/starting-loop-stage1-<date>.md with the captures beside it.

DOCS: decision 0038 (ledger and readout); docs/architecture.md (implemented vs planned, schema v20); mark the truck-card fix
and P3 status in docs/starting-loop-plan.md; docs/development.md only if a workflow changed.

REPORT: changed files and behaviour, every cash path found, verification evidence, PROTOTYPE values chosen, remaining
issues, decisions needing the owner. ESCALATE before changing a confirmed rule, if a cash path cannot be recorded atomically,
or if the client cannot compute a readout item from replicated state without a server change.
```
