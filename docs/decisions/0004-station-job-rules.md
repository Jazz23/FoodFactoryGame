# 0004 - Station Job Rules

Date: 2026-09-22

Status: accepted by the project owner; implemented in the goods domain only
(no network command, placed station, visuals or recipe authoring). See
[architecture status](../architecture.md#implemented-station-jobs-domain-2026-09-22).

Numbering note: a parallel belt session may also have created a `0004`. If both
land, renumber whichever merges second.

## Context

The GDD locks automation-first station interactions: tasks run automatically
once started. Decision 0002 requires production starts and completions to be
server-validated, durable, replayable commands that never lose or duplicate
goods. Decision 0003 makes goods location-based lots with retained exposure.
A station job therefore needs rules for when inputs leave inventory, whether
work can be abandoned, what state the output starts in, and what happens to
work in progress when the machine is picked up.

## Decision

1. **Inputs are consumed at job start.** The server removes the recipe's inputs
   from the station's input location in the same locked mutation and durable
   commit that creates the job. The job keeps copies of the consumed slices
   (item, quantity, owner, exposure and threshold). Their exposure is frozen while
   processing, so inputs cannot spoil partway through. Edible, unreserved
   quantity is consumed most-exposed first, with lot ID as the tie-breaker.
2. **Picking up a machine returns its work to the player.** Removing a station
   while its job is running puts the unprocessed inputs, with their frozen
   exposure, into the player's carried location. If the job is blocked because
   the output was finished but had nowhere to go, the finished output goes to
   the player instead. The pickup is refused (`capacity`) and nothing changes if
   the carried location cannot hold them. Returned lot IDs derive from the job
   ID, so a replay or recovery cannot create them twice.
3. **No cancel.** A started job runs until it completes or the station is picked
   up. Separate refund or discard rules can be added later.
4. **Output starts fresh.** The output lot has zero exposure and the recipe's own
   spoilage threshold; it does not inherit input exposure. Ambient time after
   the exact completion instant (`StartedAt + Duration`) counts as output
   exposure, so one large clock step gives the same result as many small ones. A
   blocked output is not aged while held; it starts fresh when it is emitted.
5. **A full output location blocks the job.** The output is never discarded or
   split to fit. The job retries on each clock step, and the station stays busy
   until then.

## Open

- **Recipe authoring format** (ScriptableObject, JSON or otherwise): decided in
  step 2. The domain only sees `RecipeDefinition`, which is content and is not
  saved; running jobs keep their own copy of the output and duration.
- ~~The station's own buffers on pickup, and how the player's carried location
  is created.~~ Closed by [decision 0006](0006-equipment-placement-and-inventory.md):
  buffers are swept into the player's inventory `carried:<playerId>`
  (all-or-nothing), which admission creates. Item 2 above now happens inside
  equipment pickup (`PickUpDurably`), which replaced `RemoveStationDurably`.
- Employees starting jobs, belts feeding input locations, and a start/pickup
  network command.

## Consequences and verification

The goods snapshot is now schema v2 (stations and jobs). A v1 save loads with
no stations or jobs and is written as v2 on its next commit. Evidence is the
`StationJobTests` EditMode suite and the
[verification record](../verification/station-jobs-20260922.md).
