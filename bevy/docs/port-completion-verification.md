# Remaining-port verification — 2026-10-16

Implementation is in progress. This report distinguishes executable checks from visual and physical-machine acceptance.
All saves/fixtures use isolated paths. No Unity files are modified.

## TCP admission regression

`cargo test --test presentation --test multiplayer`, run `sh_146f1225700112ahlsiP3XsMyA`, matched one TCP test and failed
before the presentation suite ran. The new two-client localhost test exposed a disconnect that in-process links missed.
Diagnostic rerun `sh_146f22927001O4t4akzj8LTaJy` (`cargo test --test multiplayer -- --nocapture`) matched one test and failed:
`Invalid server frame: invalid type: null, expected f32`.

Supported cause: `server::apron_spawn` used `f32::NAN` for unknown elevation; serde JSON encoded this as `null`, which the
remote `Welcome` decoder cannot read as `f32`. Admission now supplies the starting lot building's elevation. Submitted
avatar yaw also must be finite, and invalid remote frames report their parse error rather than a generic disconnect.
Verified: `cargo test --test multiplayer` passes (1 test).

## Continuation run — 2026-10-16

- The build had stopped compiling on an unfinished distance-culling edit (`sites.rs` helpers declared only inside the
  test module); the helpers now sit beside `show_storeys`.
- `cars_queue_at_lights_and_discharge_without_overlapping` failed with zero cars. Cause: the fixture sampled a rural
  segment (near-empty by design) at game hour 0; one game hour is 60 s, so `8 * 3600` was still midnight. The fixture
  now picks a non-rural segment at game hour 8. The traffic rules were not changed.
- Full `cargo test` (unfiltered): lib 0, bin unit 9, multiplayer 1, presentation 6, server 4, site_walk 8, world 2 —
  30 matched, all passed.
- Captures (`tools/capture.sh`, isolated data roots under the session scratchpad): phone map, map zoomed twice, map
  panned east, recipe page with an expanded sub-recipe, hover outline on the register. Findings fixed: recipe text used
  `·`/`—`, which the UI font lacks (rendered as boxes), now ASCII, and "1 batches" now reads "1 batch"; the outline edges
  cast a shadow, now `NotShadowCaster`. Both rechecked in a capture.

## Second continuation — 2026-10-16

- Vehicle bump: the front/back test used world axes, so a car driving along X pushed the avatar ahead of it. It now
  uses the body's long side as the direction of travel, matching Unity's vehicle-local test; new unit test.
- Customer departures (Unity `CustomerPresenter.Leave`): routed site figures walk to the street through the customer
  door, then despawn on arrival or deadline. New `site_walk` test for the route out.
- Found while capturing: a generated seat against a wall (table at the back wall, seat 3) made the diner unroutable and
  never drawn. Eating figures now take the next open seat position (`SiteWalk::seat_reachable`, new test).
- `examples/customer_capture.rs` also seeds a diner who leaves after 40 s.
- Capture (isolated root `scratchpad/cust5`, top-down, shots every 2 s from 30 s): seated diner, walking to the front
  door, through the opening door onto the sidewalk, gone. Earlier run `cust3` also shows a served customer walking from
  the register to the table.
- Map click hit-test unit test (centre-origin pointer per `bevy_ui` 0.20 `RelativeCursorPosition`).

## Third continuation — 2026-10-17

Ported: elevator rides, HUD readiness card, aim-to-buy property, storage fill stages, street-character figures, table seat
parts and seated leg pose, competitor frontage figures, world picker for employee task targets, vehicle engine audio and
runtime structure icons. Full `cargo test` (unfiltered): bin unit 14, multiplayer 1, presentation 6, server 4, site_walk 11,
world 2 — 38 matched, all passed.

Fixes found while verifying: an index panic when a table had no seat parts (empty list); the icon camera placed in world
space under a moved root (offset applied twice, images empty); 15 stages at once spawned 30 directional lights (over the
limit), now one shared pair on the icon layer; the hover outline stayed drawn behind an open screen.

Captures (isolated roots under the session scratchpad; the customer fixture now starts at noon and seeds competitor
customers): readiness card text; diner seated at the table with bent legs; street-character variants and competitor
figures on the sidewalk and apron; a passing car moving the avatar to the sidewalk; picker outline, hint and the filled
"From Register" field; structure icons in the catalog.

## Fourth continuation — 2026-10-17

- Validator: all of Unity's checks ported (districts, roads, rails, buildings, lots, service yards, controls, rivers,
  crossings, trees). Generated seeds 1, 2, 3 (and 20260927, presentation, phone-map in a scratch run) pass with no problem;
  a new test breaks a junction control, a level crossing, a tree and an ID and sees each reported.
- Inventory cursor stack with half pick-up and quick transfer; the Map key; confirmations for firing and selling a truck.
  Captures: "Confirm fire" with Unity's prompt; M turning the phone from Ledger to Map.

## Fifth continuation — 2026-10-17

Running-game captures (isolated roots under the session scratchpad), driven by new capture steps (`ui`, `uialt`,
`uishift`, `dump`, `property`, `gotoprop`, `elevator`, `competitor near`, keys `map`, `floorup`, `floordown`):
- Slot clicks: 20 Dough picked up and put in a fridge; right click took 10 back; Shift+click moved the rest. Slot grid with
  empty slots and the held stack highlighted.
- Aim-to-buy: "E restaurant-0051: price and owner" from the competitor's apron, E opened its property screen.
- Elevator: bought a factory, added a floor, rode 0 -> 1 -> 0 (+3 m and back).
- Storage shelf: one box stage on the bottom deck after 20 Dough went in.
- Ledger, trucks (status, cargo, site stock), staff, farms (pickup by a parked truck, a repeating order and a delivery
  with its truck's ETA), recipes cards, inventory supplier, property Teleport to the restaurant front.
Later in the run: slot drag and drop (capture step `uidrag` drives the same intent as a pointer drag: Egg moved to the end,
then back before Milk), the phone map's truck dots with the selected truck outlined and marked on the Trucks tab, the
standalone release build (`cargo build --release --no-default-features --features audio`, 4 m 45 s) started, drew the
world and exited cleanly in capture mode. A real pointer drag and a real click on a map truck are not captured. Dock screen (captions, farm orders bound for it) and
the factory Construction section checked in captures.
Fixes found: camera passed through a wall beside the avatar (shoulder offset now clamped); the ledger showed oldest
entries first; the property screen said "For sale" for lots that are not; storage and sale stations showed an output
grid; machine names were not capitalised. Full `cargo test`: 42 matched, all passed.

## Acceptance still required

- Independent review of the new traffic, crosswalk, map controls, recipes and dry-run code.
- A real mouse click on the phone map, the free-pointer cursor tooltip, engine audio by ear, and distance-culling
  performance in a running game.
- Running-game captures of the new UI and traffic. Still images alone do not prove continuous traffic motion.
- Localhost TCP evidence is not a two-machine menu-host/join check. That requires a second physical machine.
- The script assistant is intentionally absent from the active Unity UI (decision 0037). A new Bevy model/runtime is
  not selected; no external service, credential or model distribution is introduced by this port.

Shell logs reside under `C:/Users/justi/.local/share/opencode/shell/6e3d87c1e9bcf32564d726379737435e910608fe/`, named
`<run-id>.out`.
