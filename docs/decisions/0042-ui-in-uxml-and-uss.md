# 0042 - UI in UXML and USS

Date: 2026-10-10

Status: **implemented; verified by EditMode and PlayMode tests and running-game captures (below). Independent visual review not
yet done.** The owner asked for the main menu and every other screen to use the designs' structure directly. HTML from the
design canvases cannot run in Unity, so each screen was rebuilt as UXML (structure) and USS (look) from the canvas source
("Main Menu" and "Food Factory UI"). Presentation only: no gameplay state, request, save or schema change.

## Decisions

- **Where the UI lives.** `Assets/UI/Theme/Theme.uss` holds the colour tokens (`--ff-*` on `:root`) and the shared component
  classes (`ff-panel`, `ff-card`, `ff-button` with `--primary`/`--danger`/`--ghost`/`--overlay` and sizes, `ff-slot`, `ff-icon`,
  `ff-text-field`, `ff-toggle`, `ff-dropdown`, `ff-slider`, `ff-scroll`, `ff-segmented`, `ff-chip`, `ff-dot`, `ff-avatar`, …).
  Each screen has a folder under `Assets/UI/Resources/UI/` with its UXML and one USS: `MainMenu`, `Hud`, `Employee`, `Build`,
  `Logistics`, `Property`. A screen's frame is one UXML; repeated items (slots, rows, cards, chips) are small UXML templates
  with one root element.
- **How code uses it.** `UiTemplates` loads a template with `Resources.Load` (path relative to `Resources/UI`, e.g.
  `"Hud/HudSlot"`), `Fill`s a panel root with a screen or `Clone`s an item, and finds parts by name (`Part<T>`), throwing for a
  missing template or part. Panels keep their logic, public API and element names (tests and gameplay code query them); they
  set text, add or remove optional parts, and toggle state classes (`ff-slot--selected`, `hud-power__switch--on`, …). Inline
  styles remain only for values that follow the game: element visibility, progress width, and positions that follow the
  pointer. Hover and disabled looks are USS pseudo-classes, not pointer callbacks.
- **Theme import.** `DevRuntimeTheme.tss` (the panel settings' theme) imports `Theme.uss` and every screen sheet, so cloned items
  are styled wherever they are added. Each UXML also references its sheets with `<Style>` so UI Builder previews it. The label
  reset is a `.unity-label` rule after the default theme, so any later class wins over it; text colour inherits from each
  screen's `ff-root` element.
- **Icons.** The designs' line icons (play, chevrons, close, check, plus, arrow, factory) are drawn by `Glyphs` in the element's
  USS text colour: mark an element `ff-glyph ff-glyph--<kind>`. Item and machine icons stay sprites, set from code.
- **HudTheme** now only mirrors the two colours the removal wheels paint with; its element builders are gone.
- **Look changes.** Logistics and the property panel move from the old dev look to the shared look (dark panel, cards,
  chevron choosers, icon close buttons, a dim backdrop). The other screens keep their decision 0040 / 0041 look; small
  differences: the machine screen's arrow is drawn, captions under grids are upper case as before. Fonts are unchanged (the
  designs' Rubik and JetBrains Mono are not in the project).

## Editing the UI

Open a screen's UXML in UI Builder or a text editor and its USS beside it; colours and shared components are in
`Theme.uss`. Keep the `name` of anything code or tests use (`UiTemplatesTests` and the PlayMode tests fail when a part is
missing). UXML comments must not contain `--` (the XML parser refuses them, so BEM modifier names cannot appear in comments).
USS has no `gap`, `:first-child` or `inherit`; use margins on classes.

## Verification (2026-10-10, Editor 6000.5.9f1)

- EditMode `FoodFactoryGame.Session.EditModeTests` (assembly): 136/136, including `UiTemplatesTests` (3: every template loads
  and clones, item templates have one root and screens an `ff-root`, every screen sheet is imported by the theme).
- PlayMode `FoodFactoryGame.Session.PlayModeTests` (assembly): 61 matched, 57 passed, 3 skipped (captures on request), 1 failed:
  `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn`, which fails the same way without these changes
  (decision 0041's baseline run).
- Running-game captures: `docs/verification/ui-uxml-20261010/` (main menu screens; HUD, inventory with storage, supplier and
  staff, oven, employee tasks and Lua from `ScreenOpeningTests.HudAndScreenCaptures`; build mode from
  `RestaurantBuildingSessionTests.BuildModeAndDecoratedRestaurantCaptures`; logistics and property from a play-mode script in
  WorldGen with an isolated save). The capture tests write into their older folders; those were restored afterwards.
- Not checked: a player build, and every screen state (a dock, a table, a register with staff, a factory floor window) by
  capture; those are covered by the PlayMode tests' element checks only.
