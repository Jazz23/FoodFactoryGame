# 0035 - Restaurant Building: Implementation Choices

Date: 2026-10-01

Status: **implemented (slices 1-5 of [0034](0034-restaurant-building.md)); every value marked PROTOTYPE is open.** This
record covers technical choices made while implementing the owner's decisions in 0034. It does not change any confirmed rule
and does not promote any 0034 PROPOSAL to a requirement; each proposal is implemented as a labelled PROTOTYPE value behind a
named constant or content asset (list below). Verification: [record](../verification/restaurant-building-20261001.md).

## Storage (goods snapshot schema v16)

- `GoodsBuilding.Structures` (`GoodsStructure { Kind, X, Z, Axis, Style, ChargedCents }`) and `GoodsBuilding.WallStyle`.
  Interior walls (`partition`), interior doors and windows exist only as records. `floor` and `wall` records remember what an
  order paid for a footprint cell or a perimeter wall cell; a perimeter door's record holds its price (the door itself stays in
  `Doors`, so decision 0019 code is unchanged). Structure that came with the bought building has no record and refunds nothing.
- `GoodsEquipment.Layer`, `Ambience`, `ChargedCents`, `StaffId`; `GoodsTruck.Docked`; `GoodsOutcome.Cents`;
  `PropertyOffer.PricePercent` (content, from the lot's district).
- The v15 to v16 upgrade is in memory: plain shells, plaster finish, object-layer equipment with no price, ambience or staff,
  no docked truck. IDs never change. Equipment bought before v16 sells for 0 because its price was not recorded (owner decision
  needed if that should change).

## Shell orders (slice 1)

- One pure planner, `GoodsWorld.PlanShell(state, order, pricePercent)`, used by the server command (`OrderShellDurably`) and the
  build-mode preview. Orders: `resize`, `partition`, `door`, `window`, `remove`, `finish`.
- Interior walls occupy whole cells like perimeter walls (decision 0019's occupancy; GDD 29.4 notes edge walls as an open
  detail). A door is one cell; two door cells in line are drawn as a double door. A window covers two wall cells in line, the
  width of the kit's window bay.
- Resize: doors and windows on a moving side move with it; one that lands on a corner or off the side is removed and refunded.
  Interior walls left outside the new interior are removed and refunded with their doors and windows. A shell needs at least
  one perimeter door (`no-door`). New perimeter walls are charged; perimeter walls that disappear refund their record. So
  shrinking a bought building's original walls inward charges the new walls, and restoring a grown shell to its original size
  charges the original walls again (they were demolished, unpaid). **Owner decision needed** if restoring should be free.
- Removal is one layer per order: a window or door first, the interior wall under it on a later order.
- Rejections (`blocked`) cover the 0034 contract: no wall over object-layer equipment or belts, and no change that leaves wall
  decor without its wall or ceiling decor outside the interior.
- Exactness: each new piece records its price; the order fee rides on the order's first new piece, so removing everything an
  order built refunds the fee too. Company cash plus everything recorded as charged never changes (tested).

## Furnishing, tables, registers (slices 2-3)

- Build mode buys and places in one order (`BuyAndPlaceDurably`: one charge for all pieces, all or nothing, IDs
  `buy:<player>:<request>:<n>`), and anything sells back for exactly `ChargedCents` (`SellDurably`). A sale moves the piece's
  buffered goods and any running job's inputs to the seller's inventory (the recorded location); if they do not fit, the sale is
  refused (`capacity`). A dock on a route must leave the route first (`in-route`).
- **Register = the existing counter.** The kind ID stays `counter` (saves, recipes, customer `CounterId` unchanged); it is shown
  as "Register". Existing counters, including the starting restaurant's `start-counter` (decision 0030), become unstaffed
  registers in the v16 upgrade. A register sells only while `StaffId` names the player working it (`StaffDurably`; the player's
  inventory must be on that site) or an employee of the site. A player works at most one register; entering another site,
  disconnecting and every server start clear players' (not employees') registers. Consequence: existing restaurants make no
  sales until someone works the register (confirmed rule, 0034).
- A dining table is any piece with seats (`GoodsWorld.IsTable`), so the kit's tables have their own kinds; the original
  `table` kind must still have seats.
- PROTOTYPE seat rule: one walkable-grid search per restaurant from every site edge (walls, windows and object-layer pieces
  block; doors and decor do not). A register serves only if a walkable cell beside it is reached, and only reached tables give
  seats; since the grid is undirected, that is the door-to-register-to-seat path.

## Decor and ambience (slice 4)

- Equipment layers: object (unchanged rules), `floor` (finishes, rugs), `wall` (hangs on a wall cell that is not a door or
  window), `ceiling` (interior cells only), `tabletop` (wholly on one table). Each layer overlaps only itself, ground level only.
  Factories and machines are unaffected: object-layer `SiteGrid.CellProblem` is unchanged except that it ignores decor layers.
- PROTOTYPE ambience: `round(100 * (1 - exp(-points / 50)))` per restaurant from all placed pieces' points; customer choice adds
  `0.6 * ambience / 100`. Competitors have no ambience. Ambience does not change spend yet.
- Wall finishes are a structure style (perimeter `WallStyle`, partition `Style`), PROTOTYPE free to change and worth no
  ambience; floor finishes, ceiling panels, props and lights are decor equipment with ambience points.

## Docks (slice 5)

- Docks on a site with a restaurant shell: PROTOTYPE street rule (a walkable path from the lot's street edge, the side its
  access cell lies past; every edge on dev sites), checked at placement (`no-street-access`) and every truck second; and one
  truck at a time (`Docked`, taken in truck ID order, released when it drives off). Transfer rate is the truck's
  `LoadUnitsPerSecond` (PROTOTYPE). Trip time stays abstract (0032 open). Docks on other sites keep decision 0022/0023 rules.

## Presentation

- `RestaurantStyleCatalog` maps every server style to art-kit models; `RestaurantShellModel` draws a restaurant's ground storey
  as a wall graph (2 m pieces on straight pairs, end posts with half pieces at corners and junctions, doorway bays with frames
  and leaves, window bays with glazed units, serving hatch) with one invisible collider and carving obstacle per wall cell.
  Factories keep box walls.
- Build mode (`BuildMode`): Player/Build (B) and Player/BuildConfirm (Enter) added to the Player action map; Place, Remove,
  Rotate, Point and CloseScreen are reused. The camera frames the lot top-down (`OrbitCameraRig.SetBuildView`).
- All 59 kit models are buildable: 23 as structure styles, 36 as equipment (prefab, definition, offer, rendered icon), authored
  by `AgentScripts/BuildRestaurantContent.cs`.

## PROTOTYPE values (all open)

| Value | Where | Proposal |
|---|---|---|
| Floor cell $20, wall cell $30, door $150, window $250, order fee $100, scaled by district `PricePercent` | `GoodsWorld.Shell*Cents`, `PropertyOffer.PricePercent` | Price |
| Wall finish change free | `PlanShell` | Price |
| Max 400 cells per shell order, 200 pieces per furnish order | `MaxShellCells`, `MaxFurnishPlacements` | (bounds) |
| Seat and register reachability from site edges | `RestaurantRules`, `Diners()` | Seat rule |
| Dock reachable from the lot's street edge; every edge on dev sites | `RestaurantRules.StreetCells` | Dock rule |
| Transfer at the truck's `LoadUnitsPerSecond` | `GoodsWorld.Trucks` | Transfer rate |
| Ambience cap 100, scale 50, choice weight 0.6; competitors 0 | `RestaurantRules.Ambience*` | Ambience |
| Counter is the register (kind kept); unstaffed after upgrade | v16 upgrade, `Staff` | Counter and register |
| Structure records on `GoodsBuilding` in schema v16 | `GoodsStructure` | Shell storage |
| Item prices, ambience points, seats (bistro 2, high 2, communal 6), storage slots, footprints | `BuildRestaurantContent.cs` content | (content) |
| Chairs, banquette and bench are seating decor; tables supply seats | content | (content) |

## Owner decisions needed

1. Should restoring a bought building's original walls be free (see Shell orders)? **Owner, 2026-10-07: no;** they are
   charged like any wall (today's behaviour).
2. Should equipment bought before v16 refund its offer price instead of 0?
3. Should competitors have ambience, and should ambience change spend (GDD section 7 says "choice and spend")?
4. Should wall finishes count toward ambience and cost money?
5. Should a register serve only while its staff is near it (today staffing is an explicit command; position is not trusted)?
   **Owner, 2026-10-07: no, for now;** staffing stays an explicit command.
