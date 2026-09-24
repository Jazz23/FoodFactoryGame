# 0018 - Spoilage Timing and Refrigeration

Date: 2026-09-24

Status: requested by the project owner ("implement spoilage timing and refrigeration"). **Owner decision (2026-09-24):
refrigeration completely pauses spoilage.** Implemented. The fridge's size and price are PROTOTYPE values chosen by the
implementer, not owner decisions. See
[architecture status](../architecture.md#implemented-spoilage-timing-and-refrigeration-2026-09-24) and the
[verification record](../verification/spoilage-20260924.md).

## Context

GDD section 6 makes food condition binary (edible or spoiled) and says refrigeration extends the time before spoilage.
Section 24 says refrigeration only affects how long an item remains edible. Decision 0003 requires spoilage to use
authoritative time and each lot's exposure, with refrigeration changing future exposure and moves never erasing history.

The goods slice already counted ambient exposure per lot and skipped refrigerated locations. Nothing in the game was
refrigerated, and players could not see when goods would spoil.

A first implementation made refrigeration *slow* spoilage, reading the GDD's "unrefrigerated/refrigerated time" as both
counting. The owner then decided that refrigeration pauses spoilage completely. That model was withdrawn before it was
committed, so no save ever held its data.

## Decision

- **Refrigeration pauses spoilage.** A lot's `ExposureSeconds` grows only while it is in an unrefrigerated location. In a
  refrigerated location it does not change, however long the goods stay. Taking goods out resumes spoilage from where it
  stopped; moving never resets exposure. This is the existing domain rule (`Advance` skips refrigerated locations; a job
  finishing into a refrigerated output gets no overshoot exposure), now confirmed by the owner.
- A machine buffer can be refrigerated on input (`GoodsEquipment.InputRefrigerated`, new) as well as output
  (`OutputRefrigerated`, already present). `Validate` requires a placed piece's buffers to match. The new field reads false
  in older saves, which is correct for every existing machine, so the payload schema stays v6.
- **The fridge** is a machine with no recipes and a refrigerated input: 1x1, 8 slots, sold by the supplier for $80.00
  (PROTOTYPE). It uses the existing equipment rules (buy, place, pick up, transfer). The HUD opens a machine with no
  recipes as storage, and the readout shows a storage hint.
- **Timing is visible.** Each edible goods slot shows the ambient time left before its first lot spoils (compact: `45s`,
  `12m`, `1h`). The timer counts down white, turns orange in the last tenth of the shelf life, and shows frozen in blue in a
  refrigerated location. The hover line gives the full time: `spoils in 1h 59m`, or in the cold
  `refrigerated: not spoiling (1h 59m left out of the cold)`. Presentation only; it follows each baseline.

## Alternatives rejected

- **Refrigeration slows spoilage** (a second, longer refrigerated shelf life): implemented first, then replaced by the owner's
  decision above.

## Consequences and open questions

- The GDD section 6 wording ("spoil after enough unrefrigerated/refrigerated time has elapsed", "refrigeration extends
  time") does not yet say that refrigeration pauses spoilage; the owner may want to update it.
- Refrigerated storage has no cost beyond its purchase: no power, running cost or failure yet (GDD section 5 lists
  refrigeration among utilities). With a full pause, storage capacity and those costs are the only limits on stockpiling.
- Spoiled goods still cannot be discarded (GDD section 6 says they must be). They stay in their slots and block entries.
- Transport conditions (refrigerated vehicles, time in transit) wait for vehicles.
