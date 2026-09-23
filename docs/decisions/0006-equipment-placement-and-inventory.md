# 0006 - Equipment Placement and Inventory

Date: 2026-09-22

Status: accepted by the project owner; implemented for the oven on the dev site. See
[architecture status](../architecture.md#implemented-equipment-placement-2026-09-22).

## Context

Decision 0004 modelled pickup as deleting a station and left two items open:
what happens to goods in the station's own buffers, and where the player's
carried location comes from. Step 3b makes the oven movable by players. The
server must own every pickup and placement, and a machine must never be lost
or duplicated across failed saves, replayed requests or disconnects.

## Decision

1. **Picked-up equipment goes into the player's inventory, Factorio-style.**
   `GoodsEquipment` is either `Placed` (on a site grid cell) or `Held`
   (`HolderId` = player). A held piece is abstract: it has no position and no
   visual, and it is not shown on the avatar. The equipment keeps one stable
   ID for its whole life. A station exists only while its equipment is placed
   and reuses the equipment ID. Pickup and placement never delete and recreate
   the machine.
2. **Held equipment is persisted and survives disconnects.** If a player leaves
   while holding a machine, it stays in their inventory and is still theirs
   when they reconnect. Nothing is auto-dropped, so an absent player's machine
   is unavailable to others. There is no cap on how many pieces one player can
   hold (the owner-approved inventory model; no `hands-full` rule).
3. **Pickup sweeps the buffers into the player's goods inventory.** A running
   job's inputs (refund, exposure frozen) or a blocked job's output (0004), plus
   every lot in the station's input and output buffers, move into the player's
   inventory location `carried:<playerId>`. Capacity is the existing unit count
   across mixed items. The move is all-or-nothing: if the total does not fit,
   pickup is refused with `capacity` and nothing changes. Swept lots keep their
   IDs and exposure. Pickup is also refused (`reserved`) if any buffered lot has
   an active reservation. The buffer locations `<id>:in` and `<id>:out` are
   removed while the machine is held and recreated with the same IDs and
   capacities when it is placed.
4. **Placement uses a 1 m cell grid per site.** Bounds are server data
   (`SiteLayout`), not scene data. There are 4 quarter-turn rotations, and the
   anchor is the footprint's minimum cell. The oven is 3×3 cells, covering its
   measured 2.6 × 2.2 m with clearance. The server checks the grant, that the
   player holds this piece (`not-held`), the rotation (`invalid-rotation`), the
   bounds (`out-of-bounds`) and overlap with other placed equipment
   (`blocked`). It does not check player position or range, because position
   is presentation-only (0005). Placement is free for now.
5. **Equipment content is an `EquipmentDefinition` ScriptableObject** (kind,
   footprint, buffer capacities, visual prefab). When the server creates a
   piece, the definition's values are copied into the saved record, like a
   job's copy of its recipe, so recovery never depends on the asset. Recipe
   authoring stays deferred.
6. **Dev content.** A brand-new dev world gets a 20×20 `dev-site` layout and one
   oven, `dev-oven-1`, at cell (12, 13). Admission creates `carried:<playerId>`
   (capacity 10) together with the grant, in one commit. A player admitted
   before this step gets their inventory on their next admission.
7. **Snapshot schema v3** adds `Equipment` and `SiteLayouts`. A v2 save loads
   with neither; a v2 save that contains stations fails validation, because
   every station now needs placed equipment (no shipped save ever had
   stations). An existing dev save does not gain the layout or oven
   retroactively: delete it to get the seed.

## Closes from 0004

- The station's own buffers on pickup: swept into the player's inventory,
  all-or-nothing (item 3).
- The player's carried location: `carried:<playerId>`, created at admission
  (item 6).

## Consequences

- `RemoveStation`/`RemoveStationDurably` and the public `Bootstrap(GoodsStation)`
  are gone. `PickUpDurably` is the only way to remove a machine, and
  `Bootstrap(GoodsEquipment)` is the only way to create a station.
- `Validate` enforces: every station belongs to placed equipment; placed
  equipment has in-bounds, non-overlapping footprints and a matching station
  and buffers; held equipment has a holder and neither station nor buffers.
- Inventory locations belong to one site, so equipment and goods cannot yet
  move between sites.
