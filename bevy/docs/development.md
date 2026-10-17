# Bevy verification workflows

## Belt construction regression (2026-10-17)

From `bevy/`, run `cargo test --bin food_factory_bevy belt --no-default-features --features dev`.
Run identity: `belts-build-20261017`. Filter: `belt`. Six tests matched and passed; zero matches is a failure.
The catalog test uses a disconnected in-memory session and does not open an identity or save database.
Log: `docs/verification/belts-build-20261017/tests.log`.

For running-game verification, set `FF_DATA_ROOT` to a new isolated directory and `FF_CAPTURE_DIR` to the screenshot
directory, then run `cargo run --no-default-features --features dev`. `WGPU_BACKEND=dx12` bypasses this machine's
Vulkan overlay loader error. `RUST_LOG=info,wgpu=warn,naga=warn` includes capture state records.

The capture script supports `hold place`, `release place`, `hold rotate`, `release rotate`, and `beltstate LABEL`.
These drive the normal named actions and log the current build tool, floor, camera settings, and replicated belt cells.
They are active only when `FF_CAPTURE_DIR` is set. Use `bcell X Z` for a deterministic build pointer.

Artifacts for this run are under `docs/verification/belts-build-20261017/`: `updated/` contains the first successful
before/selected/placed captures, and `final/` plus `final-capture.log` contains the final run with repeat selection and
Escape/exit checks. The isolated databases are ignored by Git and never use the application's normal data root.

Final run passed: camera focus, yaw, pitch, and distance were identical before selection, after selection, and after
placement. The server snapshot contained seven belts forming a continuous corner. Esc cancelled the drag, then cleared
the tool; B exited and cleared the build camera focus. An independent reviewer checked the source, tests, screenshots,
and state log with no remaining blocker. Automation logged Windows cursor-grab access-denied warnings on startup/exit;
the free-pointer build interaction and capture completed successfully. This was a local host check, not remote multiplayer.

## Belt corners and repeat lines (2026-10-17)

Run identity: `belts-corners-20261017`. Filter: `belt` (same command as above). Six tests matched and passed.
Log: `docs/verification/belts-corners-20261017/tests.log`.

Defects reproduced in the running game (DX12, isolated `FF_DATA_ROOT` in a temp directory):
- Corner models were drawn a half turn off: a right turn arced between the cell's left and top edges instead of its
  entry and exit edges (`before/corners-zoom.png`). Fix: `sites::belt_model` reverses Unity's corner yaw, because the
  corner files already sit a half turn from Unity's prefabs after `fitted`'s handedness turn.
- Build mode only bought one 10-belt pack on selecting Belts, so the second line stopped after 3 belts and the third
  placed none (`before/three-lines.png`, `beltstate.log` line1/line2/line3). Fix: in build mode the client sends a
  `Buy` for the belt pack ahead of any placement the carried and already-purchasing belts cannot cover; the ghost stays
  green there. Walking placement still uses only carried belts.

After: four lines (two right turns, two left turns) replicated 28 belts with continuous corners
(`after/corners.png`, `after/corners-zoom.png`, `beltstate.log` final). Local host only; not remote multiplayer.
