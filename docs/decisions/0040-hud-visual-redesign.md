# 0040 - HUD Visual Redesign

Date: 2026-10-09

Status: **implemented; verified by PlayMode tests and running-game captures (below). Independent visual review not yet done.**
The owner asked for a clean UI, approved a canvas design ("Food Factory UI": HUD, inventory + storage, oven, staff, employee
tasks + Lua), and asked for the whole design in the game. Presentation only: no gameplay state, request or save changes.

## Decisions

- **One theme.** `HudTheme` (`Assets/Scripts/Session/Equipment/HudTheme.cs`) holds the palette (dark panels, one amber
  accent, green for on/paid, red for spoiled/fire, blue for chilled) and builders for panels, buttons (primary, secondary,
  danger), segmented controls, progress tracks, dots, avatars, scroll bars and checkboxes. `PlayerHud` and
  `EmployeeScriptPanel` draw everything through it. Inline styles beat USS pseudo-classes, so button hover is set from
  pointer callbacks.
- **HUD.** Top right: a cash card (cash over the game clock), then the wage warning (decision 0039) and a spoilage alert, each
  its own card. The alert names the edible stack nearest to spoiling outside the cold, once in the last tenth of its shelf
  life, in a place the player can name (their inventory, storage, a placed machine, an employee's hands). Under the
  crosshair a prompt shows the interact key (read from the action's first binding) and "Open <target>". The hotbar is one
  card of 56 px slots, the selected one ringed in amber.
- **Screens.** An open screen dims the world and is centred in the space above the hotbar; the hotbar is drawn above it so it
  stays a drop target. Grid slots are 44 px (hotbar 56 px) so two 10-column grids and a 320 px side column fit a 1440-wide
  panel; grids are explicit rows, since a fixed-width wrapping row loses its last column to device-pixel rounding. Supplier
  and Staff share a scrolling side column. The Staff window adds Hired / Wages / Cash covers tiles; the warning setting is a
  stepper. The oven's power switch is an 88×44 sliding switch.
- **Employee screen.** Header with avatar, name, status light, hands and close; Tasks / Lua source tabs with an amber
  underline; task cards with From → To place boxes (machine icon, place, pick button), tag-style item chips with the search
  box inside; Stop and Run in a footer.
- **Kept against the design.** Decision 0037 requires checkboxes for on / off / toggle and for whitelist / blacklist; the
  canvas drew segmented controls. The game keeps the checkboxes (restyled) until the owner decides otherwise. The canvas's
  key-hint strip and site name chip are not built: the game has no site names, and the dev readout listed the keys (it
  was removed on 2026-10-09 at the owner's request; see architecture.md, owner feedback round 3).
- **Icons.** The oven, fridge, belt, counter, dough and bread sprites in `Assets/Art/Icons/` were replaced with shaded
  128 px versions of the canvas icons, keeping their files and GUIDs. Dock, lift, table and restaurant icons are unchanged.

## Verification

- PlayMode: `ScreenOpeningTests` 9/9 (8 existing plus the capture test), `HiringSessionTests` 4/4,
  `EquipmentPlacementTests` 11/11, run asynchronously through the Pipeline runner on 2026-10-09.
- Captures: `ScreenOpeningTests.HudAndScreenCaptures` (runs only while `Temp/hud-captures.flag` exists) writes
  `docs/verification/hud-redesign-20261009/` (world HUD, inventory + storage with supplier and staff, oven, employee tasks,
  employee Lua).

## Open

- Fonts: the default UI font is used; the canvas's Rubik / JetBrains Mono are not imported.
- Remote (second client) captures and a player build check of the new screens.
