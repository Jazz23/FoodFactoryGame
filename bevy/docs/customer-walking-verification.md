# Customer walking verification — 2026-10-16

## Scope

Priority 1 of the Bevy port: presentation-only street entry, reachable register queues, and walks to table targets.
All implementation and fixtures live under `bevy/`. Gameplay records remain server-owned. The capture fixture is
synthetic SQLite data, not evidence of a real purchase or multiplayer replication.

## Baseline and automated checks

Shell artifacts are under `C:/Users/justi/.local/share/opencode/shell/6e3d87c1e9bcf32564d726379737435e910608fe/`.

- Baseline: `cargo test --test world --test server`; run `sh_146d11b6d0011bhrVSD44llhoW`, **6 matched, 6 passed**.
  Artifact: `sh_146d11b6d0011bhrVSD44llhoW.out`.
- `cargo check --bin food_factory_bevy`; run `sh_146d2c939001ou6Kh0MVxK193C`, passed with the same 15 existing warnings.
  Artifact: `sh_146d2c939001ou6Kh0MVxK193C.out`.
- `cargo test --test site_walk --test world --test server`; run `sh_146d3ad590013mbfxbvXivYiMX`, **11 matched, 11 passed**.
  Artifact: `sh_146d3ad590013mbfxbvXivYiMX.out`. Five routing cases cover front-door routing, back-door exclusion,
  topology changes/unreachable interiors, occupied seat approaches versus queues, and street-side/rotated registers.
- Review fixes: `cargo test --bin food_factory_bevy --test site_walk --test world --test server`;
  run `sh_146dfb1160011N1z0SYayPZuYR`, **18 matched, 18 passed** (4 binary, 8 geometry, 4 server, 2 world).
  Artifact: `sh_146dfb1160011N1z0SYayPZuYR.out`. Added corner-side entry, seat-against-wall and inter-site segment
  regressions; movement systems check corner consumption on slow frames and lowering only after arrival.

## Running-game capture

Initial run `sh_146d49de200108lzcJ96vPsvBB`, default features, RTX 2060/Vulkan, vsync, exited successfully with no runtime
errors. Isolated root: `C:/Users/justi/AppData/Local/Temp/opencode/customer-walk-20261016/`.
Screenshots: `shots/entry.png`, `shots/queue.png`, `shots/to-table.png`, `shots/table.png`.
The images show moving customer figures and the register queue, but the doorway/table fall outside the frame.
Diagnosis: the capture `zoom` command changed orbit distance while the camera was in top-down mode. The command now
updates the active camera mode's distance.

Follow-up run `sh_146e05aba001u9CcQrztigDTgY` rebuilt and ran successfully without runtime errors.
Root: `C:/Users/justi/AppData/Local/Temp/opencode/customer-walk-20261016-followup/`.
Artifacts: `capture.log`, `tests-18.log`, `shots/entry.png`, `shots/queue.png`, `shots/to-table.png`, `shots/table.png`.
The wider images show the doorway, register queue, movement toward the table and a figure at the table.
Independent review found the three code findings addressed and no new code blocker. Still images do not establish
continuous non-clipping through the door; geometry tests cover the routing restriction.

## Remaining acceptance

- The captures used assigned register IDs for all fixture customers; the fixture now uses empty IDs for queued/travelling
  records, matching real server arrivals. That fallback still needs a fresh capture.
- Live topology edits and reconnects are not visually checked.
- A real two-machine multiplayer session is still unverified.
- Departures, competitor walking, skeletal sitting and authored chair/model seat positions remain simplified.
