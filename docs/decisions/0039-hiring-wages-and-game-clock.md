# 0039 - Hiring, Wages and the Game Clock

Date: 2026-10-08

Status: **implemented; verified by domain and PlayMode tests (below). No running-game capture yet.** The owner's answers
(2026-10-08) are confirmed requirements. Everything marked PROTOTYPE is a default proposed with them and stays open.

## Context

Employees exist only as server-seeded records (`GoodsWorld.Bootstrap(GoodsEmployee)`, decision 0037). No running cost exists:
company cash (decision 0012) only falls on purchases, so GDD section 12's wage cost and section 14's pressure have no mechanic.
The world clock (`ClockSeconds`) counts real seconds with no notion of hours or days.

## Owner decisions (confirmed, 2026-10-08)

1. **Hiring.** A player hires from a GUI option in their inventory screen, at an owned site. Hiring is free. The employee belongs
   to that site.
2. **Cap.** Each site allows 1 employee per 25 floor cells, counted over all its storeys, with a minimum of 1.
3. **Firing.** Free, from the same GUI. Refused while the employee holds anything; the player empties the hand slot first, so
   firing never loses goods.
4. **Transfers.** Employees cannot move to another site in this slice (GDD section 4's transport between sites stays planned).
5. **Game clock.** A new game clock: 1 game hour = 1 real minute (a game day = 24 real minutes). The HUD shows the day and time
   (for example "Day 3, 14:00"). In this slice the clock affects only wages and that display: no lighting, opening hours or
   demand by time of day.
6. **Wages.** A flat $10 per game hour per employee, working or idle, charged by the server on the world tick.
7. **Unpaid employees.** Cash never goes below zero. An employee whose wage cannot be paid stops **immediately**, wherever its
   script is, walks outside the restaurant, and waits there still holding whatever it held. When cash covers its wage again, it
   walks back in and restarts its script from the top.
8. **Warning.** The HUD warns that employees will stop soon. Each player configures when the warning appears.
9. **Hand slot in the employee screen.** The employee screen shows a small inventory square for what the employee holds. A
   player can take items out of it at any time, under the ordinary transfer rules. A running script whose hands are then empty
   fails that step and carries on with its loop.

The owner first asked that employees finish their task loop before stopping, then withdrew it in favour of decision 7 with the
warning (8).

## Defaults (PROTOTYPE, open)

- Warning setting: a number of game hours of total wages left in company cash, default 1, set from the HUD or the settings
  panel. It is per player and stored in the SQLite player registry (`players.db`, decision 0011), not `PlayerPrefs`.
- Partial payment: when cash covers only some wages, employees are paid in hire order; the most recently hired stop first.
- "Outside" is the nearest walkable point outside the site's buildings: the street in front of a generated lot, otherwise a
  ring 2 m around the buildings' footprints (the dev site has no street side). This replaces the proposed "street cell outside
  the door", which has no general definition for free walls.
- The wage per employee is shown in the Staff window; the company's total per game hour appears in the warning.
- $10/hour and 25 cells per employee are tuned against the current starting cash and bread price.

## Implementation

- **Domain (`GoodsWorld.Wages.cs`, goods snapshot schema v19).** `GoodsEmployee.Unpaid`, `GoodsSnapshot.WagesPaidHour` (the
  last game hour charged) and `NextEmployeeNumber` are saved. `GameHourSeconds` = 60, `WageCentsPerHour` = 1000,
  `FloorCellsPerEmployee` = 25, `HiredHandSlots` = 4 (PROTOTYPE). `PayWages` runs inside every clock step after customers, so a
  charge commits with the goods and cash of that step: for each game hour passed it debits each paid employee in record (hire)
  order, marking any it cannot pay `Unpaid`, then pays one hour for any unpaid employee the cash now covers. Missed hours are
  never charged back. Employees of a site no company owns cost nothing. `Hire`/`HireDurably` hire at the site of the player's
  inventory, with a site grant, a company and room under `EmployeeCap` (interior cells of the site's buildings times storeys,
  / 25, at least 1). The new ID `employee-<n>` (skipping IDs in use) comes back as the outcome's `EquipmentId`, and a replay
  returns it again. `Fire`/`FireDurably` refuse `holding` (goods or a held machine in its hands) and `busy` (an active
  reservation), otherwise clear its register and remove the record, grant and hands. The view carries
  `CompanyWageCentsPerHour` (view-only, 0 when stored). A register worked by an unpaid employee serves nobody (`Diners`).
  v18 saves load with `WagesPaidHour` = the hours already on the clock, so no back pay is charged.
- **Server/session.** `GoodsNetworkBridge.RequestHire`/`RequestFire`. A hire stands one metre to the right of the server's copy
  of the hiring avatar (`SessionRoot.ScenePoseOf`). The session spawns a worker for a hired record that has none, and despawns a
  fired one. A scene whose `SessionRoot` has no employee prefab does not configure the workforce and refuses hires
  (`hiring-unavailable`). `SampleScene` and `WorldGen` have the prefab (WorldGen's from `AgentScripts/InstallWorldGenEmployees.cs`,
  owner request 2026-10-08, which adds the prefab to its network catalog and the employee screen); `DevSite` refuses hiring. Worker
  actions (`WorkerTransfer`, place, pick up, belts, power) are refused `unpaid` on the server.
- **Employee runtime (`EmployeeWorker`).** Every 0.5 s the server checks `IsUnpaid`. On becoming unpaid the script stops at once
  (its saved running assignment is kept), the replicated `Unpaid` flag is set, and the agent walks to the outside point. Once
  paid it restarts the script from its first line ("Running (paid again, restarted from the top)"), or stays idle if none was
  running. Run while unpaid saves the program to start once paid. A server restart with an unpaid record waits outside.
- **Warning setting.** `players.db` schema v3 adds `player_settings(player_id, wage_warning_hours)`.
  `PlayerRegistry.WageWarningHoursOf` and `SaveWageWarningHours` handle it, with a default of 1 game hour and 0 meaning off. The bridge's
  `RequestWageWarning`/`RequestSetWageWarning` (0 to 48) read and save it for the sending connection's player.
  `ClientSiteSubscription.WageWarningHours` keeps the latest.
- **HUD (`PlayerHud`).** Under the cash, `hud-clock` shows "Day d, hh:mm" (clock 0 reads as day 1, 08:00) and
  `hud-wage-warning` shows either the unpaid count or, when cash covers fewer hours of company wages than the setting, how long
  until the next charge or how many hours cash covers. The inventory screen has a **Staff** window: employees against the cap,
  the wage, Hire, each employee with its state and Fire (disabled while holding), and the warning setting's -/+. It sits under
  the Supplier window in one column, so the screen does not grow wider.
- **Generated worlds (WorldGen).** Lots have no baked NavMesh: `SiteNavigation` builds one at runtime for each drawn lot and,
  on a host, also for every generated lot that has employees, read from the server world every 2 s. So an employee at a
  restaurant far from the player's camera keeps walking. An `EmployeeWorker` off the NavMesh (spawned before its lot's NavMesh
  exists, during a rebuild, or hired by a player standing off the lot) retries each second: within 2 m of where it stands, else
  the nearest walkable point on its own lot. Not supported: a dedicated `-server` process with no local client, where nothing
  sets the scene placement of generated lots, so employees there cannot be placed or walk.
- **Employee screen.** A Hands row shows one 34 px square per hand slot with the item icon and count; clicking one moves that
  stack into the player's inventory through ordinary transfers (`EmployeeScriptPanel.ClickHand`).

PROTOTYPE, beyond the defaults above: a new hire's first partial hour is free; paying an unpaid employee again charges a full
hour even mid-hour; the outside point is chosen once when it stops.

## Constraints for implementation

- Wages, unpaid state, the hire counter and the clock are server-owned and saved in the goods world save, committing with cash
  in the same revision. A charge and its cash change are one atomic step; recovery must neither charge twice nor skip a charge.
- Hire and fire are replayable commands (a retry never makes a second employee or refunds twice), validated against the site
  grant, ownership and the cap.
- An unpaid employee keeps its hands, its script and its task list. Goods it holds keep spoiling as usual.
- Stopping and resuming are independent of client visibility (AGENTS.md).

## Open

- What happens when a site's cap falls below its staff (for example, a floor removed): proposed, nobody is fired and hiring is
  refused until under the cap.
- Bankruptcy, loans and debt (GDD section 18) stay undecided; this decision avoids them by never paying below zero.
- Whether sales and other costs should also be shown per game hour or day.

## Verification (2026-10-08, Editor 6000.5.9f1, isolated temp saves)

- EditMode `FoodFactoryGame.Goods.EditModeTests` 239/239, including 8 new `WageTests`: hire beside the player and replay,
  refusals (cap, off-site, no company), the cap over storeys, hourly charges with the newest unpaid first and cash never below
  zero, one hour paid on recovery with no back pay, firing refused while holding (goods kept, ID never reused),
  save/load across a charge, and a v18 row charging nothing for hours already on its clock.
- EditMode `FoodFactoryGame.Session.EditModeTests` 128/128, including `PlayerRegistryTests.WageWarningIsSavedPerPlayerAndSurvivesReopening`
  and `VersionTwoRegistryGainsSettingsAndKeepsPoses`.
- PlayMode `FoodFactoryGame.Session.PlayModeTests.HiringSessionTests` 4/4 (SampleScene host). Hire from the Staff window spawns
  a worker within 2.5 m of the player, and Fire despawns it. An employee placed inside the restaurant with a running script goes
  unpaid at a forced game hour with no cash, shows the HUD warning, walks more than 2 m out of the restaurant and stops there,
  then restarts its script once cash covers one hour. A hands square moves 2 dough into the inventory. The warning setting
  saves to `players.db` and shows in the Staff window.
- PlayMode `FoodFactoryGame.Session.PlayModeTests` 49 of 52 passed, 2 skipped (captures on request), 1 failed:
  `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (7 drawn samples, 8 required), the same pre-existing
  failure recorded in 0038. Two problems were found and fixed on the way. First, `HiringSessionTests` without an
  `InputTestFixture` broke the Input System for 13 later tests. Second, the Staff window as a fourth window widened the
  inventory screen past the test view, so `SlotButtonsTakeShiftClicksAndPlainClicks` clicked off-screen; Staff now shares a
  column with Supplier.
- PlayMode `FoodFactoryGame.Goods.PlayModeTests` 2/2.
- WorldGen (same day, after `InstallWorldGenEmployees.cs`): PlayMode `WorldGenHiringTests` 2/2 (WorldGen host, generated world).
  In `AHireWalksFromTheApronIntoTheRestaurant`, Hire in the Staff window spawns a worker on the starting lot's runtime NavMesh,
  and `move_to` walks it from the apron through the door to a free interior cell, ending within 0.6 m of the cell centre. In
  `AFarLotWithAnEmployeeGetsItsNavMeshWithoutBeingDrawn`, a bought restaurant lot beyond the draw radius is neither drawn nor
  walkable until it has an employee, then gets its NavMesh while still not drawn. Full `FoodFactoryGame.Session.PlayModeTests` 51 of 54
  passed, 2 skipped (captures), 1 failed (the same pre-existing `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn`). Session
  EditMode 128/128.
- Not checked: a running-game capture of the clock, warning, Staff window and hands row; a separate-process multiplayer check;
  real mouse/keyboard input.
