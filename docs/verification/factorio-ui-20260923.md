# Slot-grid UI and automatic oven verification — 2026-09-23

Branch `placement`. Editor: Unity 6000.5.9f1, project `E:\Projects\Unity\FoodFactoryGame`, driven through Unity CLI MCP. Decision: [0008](../decisions/0008-slot-grid-ui-and-automatic-machines.md). Runs are identified by the NUnit `test-run` start time in the XML Unity writes; each XML was copied right after its run.

## Final runs (code as committed)

| Run identity | Requested filter / mode | Matched | Passed | Failed | Skipped | Artifact |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| NUnit test-run id 2, start 2026-09-23 18:05:30Z | `FoodFactoryGame` (test name) / editor (async) | 71 | 71 | 0 | 0 | [`artifacts/factorio-ui-20260923-editmode.xml`](artifacts/factorio-ui-20260923-editmode.xml) |
| NUnit test-run, start 2026-09-23 18:07:17Z | `FoodFactoryGame.Session.PlayModeTests` (assembly) / playmode (async) | 5 | 5 | 0 | 0 | [`artifacts/factorio-ui-20260923-session-playmode.xml`](artifacts/factorio-ui-20260923-session-playmode.xml) |
| response only (XML overwritten by a later run) | `FoodFactoryGame.Goods.PlayModeTests` (assembly) / playmode (async) | 1 | 1 | 0 | 0 | — |

The Goods PlayMode run preceded two presentation-only HUD edits (icon size, "output full" status text); the goods bridge and domain were unchanged after it. `recompile`: `compilationFailed: false`; the only compiler warnings are the two existing FishNet `CS0618` warnings. `BuildDevSite` was re-run through MCP `run_script`, followed by *Refresh Default Prefabs* and reverting its reordering of `DefaultPrefabObjects.asset` ([development](../development.md#session-bootstrap-host-join-multi-process)).

## Coverage

- Domain (`StationJobTests`): with `AutomaticJobs` off, dough in an input does not start (existing manual-start tests unchanged). With it on, a transfer into the input starts the lowest-ID matching recipe in the same command (`StartedBy = "automatic"`); a failed commit rolls back the transfer and the start together; the committed save holds the job; every ready station starts; a manual start then gets `station-busy`; the next batch starts in the step that finishes the previous one and stops when inputs run out, with goods conserved. A full output prevents a start until a transfer frees it, which starts the batch in that command.
- Authoring: the interaction has `ghostModelMaterial` (transparent render queue); the HUD lists the dough and bread `ItemDefinition`s, each with an icon; the oven definition has an icon.
- PlayMode `HostBakesBreadThroughOvenSlotsAndRemoteSeesItRun` (real `DevSite`, host plus loopback-UDP remote, temp paths): the oven screen has inventory, input and output slot buttons and no Start button. Clicking the dough slot puts it on the cursor (cursor icon displayed) without moving goods; clicking the input slot moves it, and the remote sees an automatic batch; one dough consumed; the oven visual runs; a remote manual start gets `station-busy`. After the first bread the next batch is already running. Picking the bread from the output and clicking inventory slot 7 puts it in slot 7, and the committed save has it.
- PlayMode `HostMovesSeededOvenAndRemoteSeesEveryStep` (added steps): the held oven occupies an inventory slot; clicking it puts `oven` on the cursor; closing the screen shows the ghost at the aim point; the ghost has no enabled collider, no behaviour and no enabled light; clearing the cursor hides it.

## Running-game captures (Editor play mode, host, isolated temp save)

Captured with `capture_game_view` (`source: screen`) at the Editor Game view's size (1954 × 529), after hosting through `SessionRoot.Configure` + `Begin(Host)` with a temp save directory. Clicks were driven through `PlayerHud.ClickSlot` (the slot buttons' handler) and the pointer position was synthesised with an Input System mouse state event.

| File | Shows |
| --- | --- |
| [`01-inventory-cursor-stack.png`](factorio-ui-20260923/01-inventory-cursor-stack.png) | Inventory 5/10 and Storage 20/100 grids with dough icons and counts; the storage dough on the cursor over an inventory slot (hover highlight), its source slot dimmed; oven icon in hotbar slot 1. |
| [`02-oven-baking.png`](factorio-ui-20260923/02-oven-baking.png) | Oven screen right after dropping 10 dough on the input: input 9/10, progress bar and "Making Bread: 3/10 s", empty output. No Start button. |
| [`03-oven-ghost.png`](factorio-ui-20260923/03-oven-ghost.png) | After picking the oven up and selecting it: a see-through green oven on the green footprint at the crosshair; hotbar slot 1 selected. |

An earlier capture session (before the last HUD edit) showed 5 dough become 4 bread in the full output with 1 dough waiting, and the status line then wrongly read "Idle"; it now reads "Stopped: output full, take the results out".

## Not verified

- Real mouse input on the grids (physical clicks, hover, pointer tracking); covered by handler calls and a synthesised pointer position.
- The red (invalid) ghost tint in a capture; it uses the same preview result and colour as the footprint, which the placement tests cover.
- A Windows player build and a separate-process remote client with this UI.
- Independent review of the visual changes.
