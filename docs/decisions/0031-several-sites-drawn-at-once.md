# 0031 - Several Sites Drawn at Once

Date: 2026-09-30

Status: **accepted, partly implemented**. Piece 3 of [0028](0028-sites-for-generated-buildings.md), built on
[0029](0029-carrying-goods-between-owned-sites.md). The owner approved the plan on 2026-09-30. Everything marked PROTOTYPE is
placeholder tuning chosen by the implementer, not design data.

- 3a, drawing owned sites in place: **implemented** 2026-09-30 (see `docs/architecture.md`).
- 3b, entering a site and carrying goods (0029), and spawning where the player logged out: **planned**.
- 3c, the multiplayer check, the presentation cost measurement and visual captures: **planned**.

## Owner decisions (2026-09-30)

- **Rejoining spawns the player where they logged out.** The avatar's last position is saved (SQLite, like every store).
- **The inventory stays with the player.** It is on the site the player last entered and moves only by entering another
  owned site (0029); relogging never moves it. Nothing about the inventory depends on where the player spawns.

## Decision: one placement rule

`SitePlacement` (Session) is the only code that relates sites, the map and the scene:

- In a generated world, the starting lot's grid centre stands at the scene origin with its building's ground floor at y = 0
  (unchanged since piece 2). Every other lot's grid centre stands at its map position relative to the starting lot's, at its
  building's ground-floor elevation minus the starting building's. Lots never rotate, so a site cell maps to the scene by a
  shift only, and site cell (x, z) is map cell (lot.X + x, lot.Z + z) (0028).
- Sites without a lot (dev worlds, remote dev sites) stand at the origin. Dev worlds keep one drawn site.
- `SiteGridSpace` applies the placement the map presenter activates, so every presenter, placement check and ghost follows
  it without its own origin arithmetic. The map presenter takes its layout origin from the same object.

## Decision: which sites are drawn

- A client watches every site its company owns, read from the ownership records in each baseline of its current site, so
  rejoining re-watches them and a purchase adds its site.
- `DrawnSites` draws the current site plus each watched owned site whose lot is within **300 m** (PROTOTYPE, 0029) of the
  local camera; a drawn site is dropped only past 320 m, so a camera on the boundary does not rebuild it every frame.
  Sites further away still receive baselines for remote management.
- Equipment, shells (walls, floors, roofs, hidden storeys), belts, riding goods, parked trucks, customer figures and the
  runtime NavMesh (one per drawn generated lot) are drawn for every drawn site. The indoor camera switches on inside any
  drawn shell. Machines on a drawn site other than the current one are visible but not usable until the player enters it.
- The map hides its own model of a restaurant or factory while that site draws its shell, and shows it again when the site
  stops being drawn (the starting building's model now exists for that case).
- Every lot is levelled to its building's ground floor and paved when the map is built, blending back to the terrain over
  **8 m** (PROTOTYPE). Every lot can be bought, so no terrain is rebuilt at runtime. Presentation only: stored layouts do not
  change.

## Planned (3b)

- `RequestEnterSite(requestId, siteId)` → `EnterSiteDurably(player, requestId, siteId, mapX, mapZ, path)`: accepted only when
  the player is granted the site, the site is a listed lot, the server's copy of the avatar position (supplied by Session and
  converted with `SitePlacement`) is inside the lot plus a **2 m** margin (PROTOTYPE), the player has an inventory, and nothing
  the player carries is reserved. One commit moves the carried location (its lots re-owned by the new site, as trucks do on
  delivery) and every machine the player holds. Replays return the original outcome; rejections and failed saves change
  nothing.
- Invariant: a player's carried location and held machines are on one site, which the player is granted.
- The client sends one request when its avatar stands inside a granted lot other than its current site; on acceptance the
  current site switches and open screens close. Walking onto the street keeps the last site.
- Joining names the site holding the player's inventory as the current site, and the avatar spawns at its saved position
  (new players: the starting apron, as before).

## Open

- The exact radius, margin and blend; whether a server-side movement check is ever needed beyond entering a site (0029).
- Competitors' customers are still not drawn (competitors are not sites). Employees in generated worlds, merging lots and
  resale stay deferred.
