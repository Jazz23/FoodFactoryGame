# 0041 - Main Menu and Client Settings

Date: 2026-10-10

Status: **implemented (look moved to UXML and USS by [decision 0042](0042-ui-in-uxml-and-uss.md)); verified by EditMode and PlayMode tests and running-game captures (below). Independent visual review not
yet done.** The owner approved a canvas design ("Main Menu": main menu, Select World with a hosting dropdown, Settings with mouse
sensitivity and keybinds) and asked for it in the game. Friends-only, invite-only and public hosting are **not enforced** yet
(below).

## Decisions

- **The dev session panel becomes the main menu.** `SessionPanel` keeps its component, scene wiring and UI document, and now draws
  the approved menu with `HudTheme` (decision 0040): a centred column with **Resume**, **Select World**, **Connect to Server**,
  **Settings** and **Quit**. It is still presentation only: it calls `SessionRoot` and never decides admission or simulation. The
  backdrop is opaque because no camera renders at the menu. After a session ends the menu returns to its main screen.
- **Resume** repeats the last session this menu started, once its local player was admitted: a hosted world (name and how it was
  hosted) or a server address. It has no screen of its own. It is disabled until there is something to resume, and refuses a
  world that is no longer in the scene's world list. Command-line and test sessions are never recorded.
- **Select World** lists the scene's worlds most recently played first (every saved world where worlds are generated; only the
  scene's own world in DevSite, as before) with a details panel. **New world** (generating scenes only) takes a name and an
  optional seed. The **Play as** dropdown offers Singleplayer, or Host · Friends only / Invite only / Public; the main button reads
  Play or Host and play.
- **Host visibility.** `HostVisibility` is set on `SessionRoot.Visibility` before `Begin`. **Singleplayer is enforced**: the
  authenticator then admits only a client presenting the host's own secret (the host's client arrives over the transport like
  any other connection, so `IsLocalClient` cannot tell them apart at join time); others are rejected with `singleplayer`.
  **Friends only, Invite only and Public currently admit anyone who can reach the address**, as hosting did before. They are
  recorded so Resume hosts the same way, and need a friends/invite/listing service (for example Steam) that the project does not
  have. That service, and the GDD's open hosting model, are owner decisions. Command-line `-host` stays open (Public).
- **Connect to Server** takes a name and an address (default: the last server joined, else `127.0.0.1`).
- **Settings**: the player's name, mouse sensitivity (0.10×–3.00× in steps of 0.05, a multiplier on `OrbitCameraRig`'s orbit
  rate, held in `OrbitCameraRig.LookSensitivity`), and every gameplay key binding with a primary and an alternate slot, plus Reset
  to defaults. Click a slot, then press a key or mouse button; Esc cancels, Backspace unbinds. A key already used by another
  listed action shows a warning, not a refusal (Interact and Inventory share E by design). Rebinding works on the project-wide
  action asset (`InputSystem.actions`, the asset every `InputActionReference` points at), so gameplay uses the new keys at once.
  The asset authors an empty Keyboard&Mouse alternate binding for every listed action that had one key, so each row has two
  slots; empty bindings bind nothing. Hidden as template leftovers: Attack, Look, Crouch, Previous, Next. Shown but fixed: scroll
  zoom and the hotbar digits.
- **Client settings in SQLite** (decision 0011): `ClientSettings` keeps one `settings` key/value table (`user_version` 1, newer
  refused) at `persistentDataPath/Settings/settings.db`, overridable with `-settings <file>`: name, sensitivity, binding overrides
  (`SaveBindingOverridesAsJson`), and the Resume target. Loading never creates the database; only a player's change writes it.
  `SessionRoot.Configure` raises `Configured`, and the menu reloads settings from the new options; options without a settings
  path (all tests) keep defaults and write nothing.

## Not done

- Enforcing friends-only, invite-only and public hosting (needs a platform service; see above).
- Day and cash in the world list (reading them would open each world's save); the list shows last played only.
- A world thumbnail, delete/rename (the design's "…" button), a menu scene behind the menu, keyboard/gamepad navigation of the
  menu, and gamepad rebinding.
- Returning to the menu from inside a session (there is still no in-game menu).

## Verification (2026-10-10, Editor 6000.5.9f1)

- EditMode `FoodFactoryGame.Session.EditModeTests` (assembly): 133/133, including the new `ClientSettingsTests` (5: defaults
  without a database, round trip, replace and clamp, `-settings`, keybind rows). `FoodFactoryGame.Baseline.EditModeTests`: 4/4.
- PlayMode `FoodFactoryGame.Session.PlayModeTests` (assembly): 61 matched, 57 passed, 3 skipped (capture tests that run on
  request), 1 failed: `WorldGenSessionTests.ATruckDrivesTheGeneratedRoadsWhereItIsDrawn` ("drawn while driving" 7, expected
  ≥ 8). It fails the same way with this change stashed (baseline run the same day), so it predates this work. New tests:
  `SessionBootstrapTests.SingleplayerHostRejectsRemotePlayers`, `KeyRebindingTests` (3: rebind, Backspace/Esc, conflict; a
  private copy of the asset and a virtual keyboard), `MainMenuTests.HostingFromTheMenuBecomesTheResumeTarget` (hosts
  Singleplayer from Select World, checks the saved Resume target, stops, and resumes the same world the same way).
- Running-game captures in WorldGen with isolated saves and settings: `docs/verification/main-menu-20261010/` (main menu, Select
  World, the open Play as list, New world, Connect to server, Settings, a slot listening for a key).
- Not checked: a player build, real mouse/keyboard input in a running game (the rebinding test uses a virtual keyboard), and two
  machines over a network.
