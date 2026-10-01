# 0033 - Drawing Competitors' Customers (plan)

Date: 2026-09-30

Status: **accepted, implemented 2026-09-30; acceptance incomplete.** Implementation plan requested by the project owner
("write a plan for drawing competitors' customers"). The owner answered all four questions on 2026-09-30
([Owner decisions](#owner-decisions-2026-09-30)). Every number is PROTOTYPE. Pieces 0 to 3 are built and tested
([verification record](../verification/competitor-customers-20260930.md)). Two parts of piece 4 are open:

- The frame budget: with the 100-figure cap full, the Editor runs at a mean of 19 ms. That is over 16.7 ms, but cheaper
  than the same cap filled by the player's own customers (24 ms), so the cap itself is over budget in the Editor. No player
  build was measured. Lowering the cap or measuring a build first is an owner decision.
- An independent review of the captures.

See [Implementation notes](#implementation-notes) for where the build differs from the plan.

## Context

Confirmed (GDD sections 3, 11 and 23; decision 0024): AI restaurants compete for the same local customers, and each customer
chooses individually. Customers appear out of every player's view. Client visibility never decides whether a customer
exists or progresses. Competitors stay lightweight records tied to their building's lot (0028).

Implemented today:

- Competitor customers are already fully simulated on the server. A `GoodsCustomer` whose `RestaurantId` is a competitor ID
  travels, queues, is served (`Ordering`, by one of the competitor's `Servers`), eats in one of its `Seats` and leaves, the
  same as at a player restaurant (`GoodsWorld.Customers.cs`).
- They are never sent to clients. `GoodsWorld.View` is per site, and `ViewCustomers` keeps only the customers of that site
  and drops competitors, districts and the random state. Competitors are not sites, so no baseline ever carries them.
- `CustomerPresenter` draws only the customers of drawn sites (0031). It caps figures at 100 per client, which is the
  measured comfortable load (0024: 100 animated models fit the frame budget, 400 did not). Figures spawn on a generated
  lot's street band out of the local camera's view (`SiteStreet`) and walk runtime NavMesh paths (`SiteNavigation`).
- In generated worlds, every competitor has a `LotId`, and its map position is the lot's access point (0030). Competitor
  buildings are drawn by `WorldLayoutPresenter` as closed exterior models with doors. They have no interior, no shell
  and no NavMesh.
- The server already knows each connection's avatar map position (`GoodsNetworkBridge._mapPositionOf`, used by
  `EnterSiteDurably`, 0031).
- Scale (0030): about 1,870 customers an hour are shared by about 300 competitors on seed `piece-two`, so a single
  competitor rarely has more than a few customers at once.

Decisions 0030 and 0031 both left "competitors' customers are not drawn" open. Today the street outside the player's
restaurant is empty unless customers are heading to the player, so the competition the player is losing to is invisible.

## Goal

In generated worlds, players near a competitor see its customers. They come along the street out of view, walk up to its
door, queue outside it when it is busy, go inside, and later come out and walk away. This is presentation only. It uses
real server records, so what a player sees matches the simulation: a busy competitor really is busy, and a customer who
walks out of the player's restaurant to a competitor can be seen crossing the street. Dev worlds are unchanged. Their
competitors have no lot or building.

## Proposed plan

The plan has four pieces, each separately shippable and verified. They run in order.

### Piece 0 - Baseline

- Record a green baseline of the Goods and Session EditMode assemblies and the `CustomerPresenterTests`,
  `WorldGenSessionTests` and several-sites PlayMode suites before changing anything. Record the client frame time with
  100 owned-site customer figures as the presentation cost baseline (same method as the
  [several-sites record](../verification/several-sites-20260930.md)).

### Piece 1 - A read-only crowd view in the domain (no network, no visuals)

- **`GoodsWorld.CrowdNear(mapX, mapZ, radiusMetres)`** returns a new `GoodsCrowdView`. It is a read-only copy made under
  the world lock. It lists every competitor with a `LotId` whose map position lies within the radius (Manhattan, like
  every other range), in catalog order. Each entry has the competitor's ID, `LotId`, `Servers` and `Seats`, plus its
  customers. Each customer has only `Id`, `DistrictId`, `Appearance`, `DineIn`, `State`, `Ticket` and, for travelling
  customers, `RemainingSeconds`.
- **Filtering on the server.** Travelling customers are included only in their last 12 s, the same window
  `CustomerPresenter` uses for owned sites. Patience, price, the walk-out restaurant and reputation are never included.
  Competitor prices, servers and seats are fixed derived data, but the view still carries no more than drawing needs.
- **No schema change and no persistence.** Nothing is stored or validated, and the goods snapshot stays at v15. The view
  is derived from live state the way `View` is.
- **Tests (EditMode):** radius inclusion and exclusion, catalog order, no lot means not included, the 12 s travel window,
  no leaked fields (patience, price), and the result is identical before and after `Advance` with nothing changed. A
  benchmark on seed `piece-two` with the full city warmed up (as in `CityCustomerBenchmarkTests`) covers the cost of one
  call and of one call per connection for 8 connections. Budget: under 0.5 ms per call (PROTOTYPE).

### Piece 2 - Replicating the crowd to each client

- **Interest comes from the server's copy of the avatar position, not the camera.** On each broadcast (once per clock
  step), the bridge sends each connection `TargetCrowd(json, epoch, revision)` for the area around
  `_mapPositionOf(connection)`. The radius is **150 m** (PROTOTYPE). This is smaller than the 300 m site draw radius because
  figures are small and capped. A connection without a position, or in a world without a layout, gets nothing. This only
  decides what a client is *told*; the simulation never reads it (AGENTS.md, GDD section 28).
- **Rate.** At most one crowd message per connection per second. If the competitors in range and their customers' IDs and
  states are unchanged since the last send, nothing is sent. Epoch and revision ordering follow `TargetSite`. A stale or
  earlier-epoch message is dropped.
- **Client storage.** `ClientSiteSubscription` keeps the latest crowd (`LatestCrowd`, version-counted) separately from
  site baselines. A world change clears it with the baselines.
- **Tests (PlayMode, real host and remote client):** a remote client standing near a competitor receives its customers,
  and walking away beyond the radius empties the crowd. A client never receives customers of competitors outside its own
  radius. Payload bytes per client per second are measured on `piece-two`. Budget: under 4 KB/s per client (PROTOTYPE).

### Piece 3 - Drawing them

- **One presenter, two sources.** `CustomerPresenter` draws both owned-site customers (unchanged) and crowd customers.
  Visuals stay keyed by customer ID across both sources. So a customer who walks out of the player's restaurant and
  re-chooses a nearby competitor keeps the same figure and walks there instead of vanishing and respawning.
- **Placement.** A competitor's lot is placed in the scene by `SitePlacement`'s existing lot rule: grid centre at its map
  position relative to the starting lot, at its building's ground-floor elevation. It is extended to lots without a site if
  it does not already cover them. Lots are already levelled and paved (0031), so no terrain lookup is needed.
- **Paths without a NavMesh.** Competitor lots get no runtime NavMesh, which keeps rebuild cost bounded. A lot's apron runs
  straight from the street to the building's street-side door (0028), so figures walk a fixed polyline. The polyline runs
  from a street-band point to an apron point in front of the door, then to the door. It is derived from the layout
  building's `Doors[0]` and the lot's `Access` cell. The street-band points generalise `SiteStreet` to take a lot and door
  instead of a shell. Figures still have no avoidance (as today).
- **States** (see owner decision 1):
  - Travelling (last 12 s): appear on the street band out of the local camera's view, then walk toward the door.
  - Queued: stand in a line outside the door, by `Ticket`, running along the facade, up to 8 places (PROTOTYPE). Further
    queued customers wait out of sight along the street band.
  - Ordering and Eating: walk in through the door and are hidden while inside.
  - Leaving (record gone): reappear at the door and walk to an out-of-view street point, or are removed after the existing
    12 s deadline.
- **Shared cap and priority (owner decision 4).** The 100-figure cap stays per client. Customers of the current site (the
  one the player works in) always take places first. The remaining places go to every other candidate, from owned drawn
  sites and the crowd alike, in order of distance from the local camera to the restaurant they are at or heading to.
  Within one restaurant, customers inside or at the counter or door (Ordering, Eating, Queued) come before travelling ones,
  then by `Ticket`. The ranking is redone at each 0.5 s refresh. To stop figures popping as the camera moves, a figure that
  loses its place is removed only once it is out of the local camera's view. Alternatively it walks off to an out-of-view
  street point, as a leaving figure does. A new figure takes the place only after that. The cap does not grow without a
  new frame-time measurement.
- **Tint** uses the same district and appearance rule, so a downtown crowd looks like downtown customers wherever they eat.
- **Tests (PlayMode):** figures appear only off camera, queue order follows tickets, Ordering and Eating figures are hidden,
  current-site figures are never displaced at the cap, other places go by camera distance across owned and crowd sources,
  a displaced figure that is on screen is not removed until it is out of view, and the walk-out figure continues across
  sources.

### Piece 4 - Evidence (acceptance)

- Domain tests and the crowd benchmark (piece 1), the network payload and two-client checks (piece 2), and the presenter
  tests (piece 3), each reported with filter, run identity, matched count and artifact path.
- Client frame time on `piece-two` with the cap full, mixed owned and crowd figures, compared with the piece 0 baseline.
  It must stay under 16.7 ms.
- Running-game captures: a quiet competitor, a busy competitor with a visible queue, and a customer crossing from the
  player's restaurant to a competitor. An independent visual review by someone other than the implementer is required
  (AGENTS.md).
- Update `docs/architecture.md` (customers section and the "remain open" list) and close the open item in 0030 and 0031.

## Owner decisions (2026-09-30)

1. **What is shown at a competitor's building: A.** The queue outside the door is visible, and figures go in and are
   hidden inside. Rejected: B (no outdoor queue) and C (drawn interiors).
2. **A visible queue is competitive information.** It is intended that a rival's line tells the player where demand is.
3. **Real records.** Clients draw the server's real competitor customers (pieces 1 and 2). Rejected: clients inventing
   figures from published wait and free seats, which could contradict the simulation.

4. **Sharing the 100-figure cap: C, current site first, then nearest the camera** (piece 3). Rejected: A (every owned
   site before any competitor), because several busy owned restaurants far from the player could empty the street the
   player is looking at. Also rejected: B (a fixed competitor share), because it can leave the player's own customers
   undrawn while places go to rivals.

   Context for the choice: these are estimates from the PROTOTYPE constants, not measurements, and piece 2 measures them
   on `piece-two`. A customer stays at a competitor about 3 to 4 minutes (service 10 to 30 s, eating 120 to 300 s,
   takeaway leaves at once). About 1,870 customers an hour citywide gives roughly 100 to 150 at competitors at any moment,
   under one per competitor. A 150 m radius covers perhaps 15 to 30 competitors, so about 5 to 20 crowd customers, most
   of them hidden inside. The cap matters only once demand rises, downtown, or with several busy owned restaurants drawn
   near rows of rivals. A customer without a figure still exists and still buys; only the figure is missing.

   Accepted downside: an owned site other than the current one, far from the camera, can show fewer figures than it has.

## Deferred / out of scope

- Competitor AI, acquisitions and competitors' stock (GDD section 11, 0028).
- Customer groups (0024), avoidance between figures, and other players' cameras for out-of-view spawning (0024, still open
  for owned sites too).
- Dev-world competitors (no lot, no building) stay undrawn.
- Customers walking the whole way along roads from their district: figures still appear only near the end of their trip.
- Any change to customer simulation, choice or demand balance. Drawing must not change outcomes. A test checks that
  `Advance` results are identical with and without crowd views requested.

## Escalate if

- The crowd benchmark or payload exceeds budget at 8 connections. Tighten the radius before considering any change to the
  simulation.
- Frame time with the cap full exceeds the baseline by more than 2 ms. Lower the cap and report back; never displace
  current-site figures.
- `SitePlacement` cannot place unbought lots without changing how owned sites are placed. That is a 0031 contract change.

## Implementation notes

Built 2026-09-30. Where the build differs from the plan above, or fills in a detail the plan left open:

- **Crowd view** (`GoodsWorld.Crowd.cs`): `CrowdNear` lists only competitors in range that **have at least one customer to
  draw**. The plan listed every competitor in range; the first benchmark run showed that most of each 3 KB send was empty
  entries. A customer who walked out of another restaurant is sent in any travel state (`WalkedOut`), not only in the last
  12 s, so a figure already on screen can follow them. Prices, patience, seats taken, reputation and the restaurant they
  left are never sent. `CrowdSignature` (competitor and customer IDs, states, tickets) decides whether to resend.
- **Sending** (`GoodsNetworkBridge`): `TargetCrowd` is sent after each clock step, which is at most once a second, to every
  connection with a site subscription, and only when its signature changed. A connection without an avatar position (dev
  worlds, server-only) gets one empty crowd. Crowds never move the client's epoch; site baselines own it. After a clock-step
  rollback every crowd is resent.
- **Client** (`ClientSiteSubscription.LatestCrowd`, `DrawnSites.Crowd`): a crowd change is not part of `DrawnSites.Version`,
  so other presenters do not rebuild for it.
- **Geometry** (`CompetitorFrontage`, `SitePlacement.OfferOf`): the door is the lot's first door, and the street side is the
  side that door faces. The queue runs along the facade toward the side with more room. When a row fills, it continues back
  along a second row further out (the first capture showed a figure seemingly cutting in at the door). The street points
  are `SiteStreet.Points` for any lot.
- **Presenter** (`CustomerPresenter`): one ranking across both sources and one cap, as decided. Figures that are inside a
  competitor are inactive and take no place. A figure walking between a competitor's line and a site's NavMesh keeps its
  identity. Moving onto a site, it starts at the site's floor height.
- Not built or not evidenced: a staged walk-out crossing (the walk-out's next choice is random), and out-of-view checks
  against other players' cameras (open since 0024).
