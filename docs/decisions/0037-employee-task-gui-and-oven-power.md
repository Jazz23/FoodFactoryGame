# 0037 - Employee Task GUI and Manual Oven Power

Date: 2026-10-06

Status: **implemented; verified by domain, PlayMode and running-game captures (below).** Owner requests from 2026-10-06 are
confirmed requirements. The owner's answers to follow-up questions (the same day) are also confirmed. Everything marked
PROTOTYPE is an implementation default that stays open.

## Owner requests (confirmed, 2026-10-06)

1. Employees get a GUI to schedule them visually. It generates the Lua that actually runs. The visual tab is the default and the
   Lua source tab is the only other tab.
2. The script assistant is disabled.
3. A list with a + button adds tasks. A dropdown picks the task type: "Move stuff from A to B" or "Turn machine on".
4. Move: "Set source" and "Set destination" buttons close the GUI. Every valid source or destination then pulses slightly red,
   and clicking one sets it. A checkbox chooses "move any valid item" (for example, raw dough from a shelf when the
   destination is an oven). When it is unchecked there is an item list, which is a whitelist by default, or a blacklist via a
   checkbox. A search box suggests items in a drop-down while typing.
5. Turn machine on: "Select machine" works like "Set source", and the machine is picked with E or a click. There are three
   checkboxes, "turn machine on", "turn machine off" and "toggle". Toggle greys out the other two.
6. The oven no longer processes dough by itself. It must be turned on and off manually, with a switch in its GUI.

Follow-up answers (confirmed, 2026-10-06):

- Off **pauses** a running batch where it is, and on resumes it. While on, the oven starts a batch whenever its input allows.
- The task list **loops forever, one step per task**: one carrying trip, or one switch action, per task per pass.
- The Lua tab is **editable**. A hand edit **detaches** the employee from the visual tasks. The Tasks tab then locks behind a
  "Reset to tasks" banner.
- The assistant tab is removed. Its code (`ScriptAssistant`, `LocalScriptModel`, tests, model download menu) is kept, unused.
- Valid sources and destinations are **everything with an inventory a player can access**.
- On, off and toggle are **exclusive**: exactly one is chosen.
- **Only the oven** has a power switch for now.
- A trip carries **as much as fits** in the destination.

## Implementation

- **Power (goods, server).** `GoodsEquipment.PoweredOn` is saved state (goods snapshot schema **v18**). Kinds that need a
  switch are content: `EquipmentDefinition.manualPower` (set on `Oven.asset`), registered every start through
  `GoodsWorld.RegisterManualPower`, like recipes. For such a station, `StartReadyJob` starts nothing while it is off and
  `ProgressJobs` does not advance its running job (a pause; exposure of the job's frozen inputs is unaffected). `StartJob`
  refuses `powered-off` after its input check. `SetPower`/`SetPowerDurably` (actor granted the site; placed piece; kind has a
  switch, otherwise `forbidden`, `not-placed` or `no-power-switch`; replayable) commit the switch. Switching on starts a ready
  batch in the same command. Picking a piece up switches it off. v17 saves load with every machine **off** (a saved
  mid-batch oven waits, paused) and no employee task lists. Machines without a switch run automatically as before
  (decision 0008).
- **Power (client).** `RequestSetPower` RPC. The oven's machine screen (`PlayerHud.PowerRow`) has a sliding switch
  (`hud-power-switch`). It shows "Off: switch it on to bake" while idle and off, and "Paused (off)" mid-batch. The heater
  visual (`OvenToggle` via `EquipmentPresenter`) follows `PoweredOn` for kinds with a switch.
- **Task list.** `EmployeeTaskList`/`EmployeeTask` (`Session/Employees/EmployeeTasks.cs`) are saved as JSON in
  `GoodsEmployee.Tasks` beside the script. `SetEmployeeScriptDurably(..., tasks)` keeps the saved list when tasks is null,
  limit 16000 characters. Views blank it like scripts, and it reaches clients through `EmployeeWorker.Tasks`. Run sends the
  Lua and the list together. The server never interprets the list: only the Lua runs, through the ordinary validated API.
  `ToLua()` is deterministic, so the client calls a script **detached** when it differs from what its list generates. A
  saved script with no matching list (everything saved before v18) opens detached.
- **Generated Lua.** It is a `while true do ... wait(1) end` loop with one statement per task. Move uses a generated
  `move(source, destination, items)` helper. The helper takes up to `room(destination, items)` units when the source has
  any, puts them in the destination, then returns leftovers to the source. Any valid item is
  `accepts(destination)`, a whitelist is `{"a", "b"}`, and a blacklist is `accepts(destination, {"x"})`. On is
  `if not is_on(m) then turn_on(m) end`, off is the reverse, and toggle is `toggle(m)`. An unfinished task (nothing picked,
  or an empty whitelist) is a comment.
- **New Lua API** (`EmployeeWorker`): `turn_on`/`turn_off`/`toggle(machine)` walk to the machine and call
  `WorkerSetPower` → `SetPowerDurably`. `is_on(machine)` reads the switch. `accepts(place, except)` returns a machine's
  recipe inputs minus the exceptions, or nil (or `{except = ...}`) for a place that takes anything. `room(place, items)`
  returns the most units of one allowed item the place can take now. `take`/`put`/`count` accept an item filter: nil, an
  ID, a list, or `{except = {...}}`. `ScriptDryRun` stand-ins cover the new calls.
- **Picking.** `EquipmentInteraction.BeginTargetPick(PickTarget, done)` reuses `InteractionScreen.PickPosition`, so the
  panel hides, the pointer locks, and walking and looking stay on. Every valid target on the current site pulses (red
  outline, colour and width swinging every 0.9 s), and the one under the crosshair is solid red. Sources and destinations
  are placed pieces that open a goods screen (not tables or decor) plus the storage marker. Machines are placed pieces whose
  kind has a switch. A click or **E** picks, and Esc returns without changing the task. The picked text is `"storage"`; for
  a source, `"<id>:out"` for a recipe machine or dock and `"<id>"` otherwise; for a destination, `"<id>:in"`; for a
  machine, `"<id>"`.
- **Panel** (`EmployeeScriptPanel`): **Tasks** (default) and **Lua source** tabs, both 360 px tall. A task card has its
  type dropdown, ✕, pick buttons with the picked place's name, checkboxes, item chips and a search box with up to 6
  suggestions (Enter adds the first). "Select world pos", the API reference and the caret bar are on the Lua tab. Run, Stop,
  Close, status and Give are shared. All text boxes ignore the opening click and mark typing, so E is text there.

PROTOTYPE: the 1 s wait per pass; leftovers returned to the source (if that fails they stay in the hands for the next pass);
the blacklist still limited to what the destination accepts; pulse timing and widths; walking to a machine on every toggle
pass; picks allowed at any distance.

Open: task reordering; more task types and conditions; per-task amounts; a pick list for places off screen; which machines
other than the oven get switches; power cost; what the employee does when a task can never succeed.

## Verification (2026-10-06, Unity 6000.5.9f1, isolated save paths)

- EditMode: Goods assembly 230/230 (5 new `PowerTests`: off never starts, pause and resume, grants, durable switch, v17
  load; 1 new `EmployeeTests` case for saved task lists). Session assembly 126/126 (3 new `EmployeeTaskTests`: the generated
  Lua passes `ScriptDryRun`, is deterministic, round-trips JSON and quotes text).
- PlayMode (SampleScene host): `ScreenOpeningTests` 7/7. New: Tasks is the default and has no assistant tab; tasks generate
  the Lua; a hand edit locks the list and Reset restores it; Set source pulses the storage and machines with outline
  materials, Esc cancels, and a machine pick offers only ovens; **end to end**, a task list built in the panel and Run made
  the real employee carry dough storage → oven, switch it on, and bread came out, with the list and Lua saved.
  `EquipmentPlacementTests` 10/10 (the bake test now checks that a filled oven that is off does not bake, then switches it on
  through the HUD). `WorldGenSessionTests` 9 passed, 1 skipped by design, and 1 failed:
  `ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` (7 samples, needs 8). It fails identically on unmodified HEAD (baseline run
  with this change stashed), so it is pre-existing.
- Running-game captures (`ScreenCapture`, SampleScene host): the Tasks tab with move, whitelist (chip and suggestion) and
  machine cards; toggle greying on/off; the Lua tab; the four pulsing targets (dock, shelf, counter, oven); the oven switch
  off and on. A capture found that `??=` on the outline materials hit Unity's fake null and threw every frame, so no outline
  appeared. It was fixed with Unity-aware checks, and the PlayMode test now asserts the outline materials.
- Not checked: real mouse/keyboard input for picks and the dropdown (methods were called directly), a separate-process
  multiplayer check, and a player build.
