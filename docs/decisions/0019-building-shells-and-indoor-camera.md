# 0019 - Building Shells and the Indoor Camera

Date: 2026-09-24

Status: requested by the project owner ("get a start on buildings and the indoor camera"). **Owner choices
(2026-09-24):** the first slice is a server-owned shell plus the indoor camera, with no construction or purchase yet; walls
occupy grid cells; equipment may still be placed anywhere on the site; entering a building switches to the top-down
camera automatically, and the camera toggle (C) still overrides it. The dev restaurant's size, position, doorway and look
are PROTOTYPE values chosen by the implementer. Implemented. See
[architecture status](../architecture.md#implemented-building-shells-and-the-indoor-camera-2026-09-24) and the
[verification record](../verification/buildings-20260924.md).

## Context

GDD section 3 locks the camera model: an orbiting third-person camera outdoors, and a top-down camera indoors, aligned so
the building's floor grid reads horizontally and vertically. Section 5 makes interior floor area a core resource, has
restaurants trade dining space against kitchen, storage and circulation, and makes construction outsourced for money.
Before this step the dev site was a bare 20x20 grid on an open floor, and the top-down view was only a manual toggle.

## Decision

- **A building shell is server data.** `GoodsBuilding { Id, SiteId, CellX, CellZ, Width, Depth, Doors }` lives in the goods
  snapshot (payload schema **v7**; v6 upgrades in memory with no buildings). The footprint includes the walls. Every
  perimeter cell is a wall, except the listed door cells. A door may be any perimeter cell except a corner. The minimum
  size is 3x3 (one interior cell).
- **Walls occupy cells.** `SiteGrid.CellProblem`, the one rule shared by equipment placement, belt placement, the client
  preview and recovery validation, now also returns `blocked` for any footprint that covers a wall cell. Interior and door
  cells are ordinary cells. Belts may run through a doorway, and equipment may stand in one.
- **Placement stays unrestricted.** Equipment and belts may be placed indoors or outdoors.
- **Shells are created only by the server.** `GoodsWorld.Bootstrap(GoodsBuilding)` rejects a blank or duplicate ID, an
  unknown site, a shell smaller than 3x3 or outside the layout, a bad or duplicated door, an overlap with another shell on
  the site, and walls over placed equipment or belts. There is no player command to build, buy, move or remove a shell,
  and shells never change after creation. `Validate` applies the same rules on recovery. `View` includes the viewed site's
  shells, so they reach clients in the existing full baseline with no new RPC.
- **Indoors is presentation.** A client is "indoors" while its own avatar's cell is strictly inside a shell's walls. A
  doorway cell is a threshold, not indoors. No server rule depends on this, because avatar position is presentation-only
  (decision 0005).
- **Camera.** `BuildingPresenter` calls `OrbitCameraRig.SetIndoors` every frame. The rig switches to top-down on entering
  and back to orbit on leaving, only when that state changes. The toggle can override the view in between, until the next
  crossing. The existing top-down view already snaps yaw to 90 degrees and so meets section 3's grid alignment.
- **Presentation.** `BuildingPresenter` builds each shell from the baseline:
  - one solid box per run of wall cells, 3 m high (door cells break the runs);
  - a lintel over each door;
  - a floor tint over the interior;
  - a roof.
  Only walls have colliders, so floor, lintels and roof never catch aim rays and avatars cannot walk through walls. The
  roof of the building the local avatar is in is hidden for that client only.
- **Dev content.** `dev-restaurant`: 11x9 cells at (9, 0), with doors at (13, 8) and (14, 8) in its north wall, beside the
  spawn points. It clears the seeded oven and counter, every existing PlayMode test cell, and the owner's current dev save.
  A new dev world gets it. `DevWorld.EnsureBuilding` adds it once to a save whose dev site has no building, committed
  before serving. If something stands on its wall cells, that save is left unchanged with a warning. The south landmark
  cube, which would stand inside the shell, is no longer authored.

## Alternatives rejected

- **Walls on cell edges.** This keeps every interior cell usable and suits interior walls later. It needs a separate edge
  occupancy rule, and the owner preferred cell walls for this slice.
- **Indoors-only equipment** or a per-kind indoor rule: not chosen by the owner for now.
- **Camera strictly tied to indoors** (no toggle): not chosen by the owner; the toggle stays useful for inspection.
- **Detecting indoors from scene colliders or triggers:** this would make a presentation scene the source of building
  data. Shells are server state, so every client, and later the server's own rules, agree on them.

## Consequences and open questions

- Construction is not implemented: buying land or a shell, outsourced construction time and cost (GDD section 5),
  editing walls and doors, interior walls, and extra floors (section 5, with vertical transport in section 27).
- Floor area is not used by any rule yet. Customers, seating and the dining/kitchen trade-off (section 7) will need to know
  which cells are inside which building.
- The seeded oven and counter stand outside the dev restaurant; players can move them in. Doorways are not reserved, so a
  machine can block one.
- Roof hiding is local: only the building your own avatar is inside loses its roof on your screen, so you cannot see into
  a building from outside. A camera orbiting outside a wall (after the toggle override) may be hidden by it.
- The camera switch is presentation for one client. Whether indoors should affect server rules (such as refrigeration or
  customer access) is undecided.
