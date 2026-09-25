# 0021 - Conveyor Lifts

Date: 2026-09-24

Status: requested by the project owner ("add conveyor lifts to move items between floors"), the slice deferred by decision
0020. **Owner choices (2026-09-24):** a lift is a belt-like item placed and picked up by a player, not a paid construction
order; a lift spans one storey, and taller runs chain lifts. The lift stack size, the dev stock, the V key, the model and
the lift's speed are PROTOTYPE values chosen by the implementer. Implemented. See
[architecture status](../architecture.md#implemented-conveyor-lifts-2026-09-24).

## Context

GDD section 27 selects conveyor lifts for automated goods flow between floors and freight elevators for workers and bulk
loads. Decision 0020 made each floor its own belt network and deferred lifts. The GDD's construction proposal (section 5)
lists conveyor-lift shafts as construction; that proposal is not approved, and the owner chose equipment-like lifts for
this slice.

## Decision

- **A lift is a belt.** `GoodsBelt.Lift` is 0 for a flat belt and +1 or -1 for a lift whose front end is one storey up or
  down; `ExitLevel = Level + Lift`. A lift keeps the belt's identity, location (`<id>:items`), riding rules, speed and
  spacing, so nothing about moving, taking or removing goods changes. Goods snapshot schema **v10**; a v9 save upgrades
  with every belt flat (the version changes so an older build refuses lifts rather than reading them as flat belts).
- **Made from an item.** `lift` is an ordinary goods item. `PlaceLift(player, request, site, x, z, direction, level, lift)`
  consumes one from the inventory (`no-lifts`); `RemoveBelt` returns it with every riding item, all-or-nothing. Placing
  a lift where the same lift already stands turns it at no cost. `invalid-lift` for any lift other than +1 or -1.
- **Two cells.** A lift occupies its cell on `Level` and on `ExitLevel`; `SiteGrid.CellProblem` counts both, so the exit
  level must exist there (`no-floor`, or `out-of-bounds` below the ground), neither cell may hold anything else, and the
  elevator shaft stays clear. A belt drag never turns a lift into a belt.
- **Links.** `BeltRules.ByCell` keys the whole site by cell and level, a lift at both ends. A lift takes items on its own
  level like a straight belt (from behind at 0, from a side at the middle) and hands them to the belt in front on its
  exit level with the usual entry rules; the belt in front curves from the lift when fed from its side. A lift's exit end
  takes nothing in, and its entry end feeds nothing on its own level. Floors are still separate networks joined only by
  lifts.
- **Controls.** With lifts on the cursor a ghost lift shows the aimed cell going up or down; Place puts one there, R
  turns the cursor, `Player/FlipLift` (V) switches up and down (the action is found in the Player map by name, so the
  authored scene needed no new reference). With an empty cursor, R turns the aimed lift and Remove takes it up.
  `RequestPlaceLift` carries the level and direction; the server re-checks everything.
- **Presentation.** `BeltPresenter` builds the lift from the straight belt model at runtime: half a belt in, four frame
  posts one storey tall, half a belt out on the other floor. Items climb vertically through the middle half of the path.
  The lower end and frame hide with the lower storey, the upper end with the upper one.
- **Dev content.** Lift item (stack 50, icon from `DrawItemIcons.ps1`), 20 in the dev storage (new worlds, and once for an
  older save without lifts: `DevWorld.EnsureLiftStock`) and 10 in each new player's inventory.

## Alternatives rejected

- **Construction order (company cash, permanent):** not chosen by the owner for this slice.
- **One lift spanning any number of storeys:** not chosen; chaining one-storey lifts keeps one cell rule and one path
  length.
- **A separate lift entity and location kind:** duplicates belt movement, removal and validation for no gameplay gain.

## Consequences and open questions

- Lifts move goods at belt speed along one tile of path, so the climb is visually fast (3 m in half a second).
- Upper floor slabs are solid; the lift frame and climbing items pass through them visually.
- Lifts cannot be bought yet, and there is no gamepad binding for FlipLift.
- Whether lift shafts become construction (GDD 29.1) remains open; changing that would change how lifts are obtained,
  not how they carry goods.
