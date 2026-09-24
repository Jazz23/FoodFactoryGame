# 0012 - Company Cash, and JSON Payload vs Relational Tables

Date: 2026-09-24

Status: accepted by the project owner ("shared by the company"); implemented as step 1 of the sell loop (cash only, nothing
earns or spends it yet). See [architecture status](../architecture.md#implemented-company-cash-2026-09-24) and the
[verification record](../verification/company-cash-20260924.md).

## Context

The GDD core loop needs revenue to reinvest (sections 2, 12, 17), and the selected Restaurant Start (section 19) makes selling
food the first loop to close. Planned steps: (1) company cash, (2) a sell counter that turns edible food into cash, (3) a
supplier that sells ingredients and belts for cash, (4) a HUD readout. This record covers step 1 and the storage question it
raised.

## Decision: company cash

- One wallet per **company**, not per player. A `GoodsCompany { Id, Cash, SiteIds }` owns sites; a site has at most one
  owner. Membership is implied: a player granted a site the company owns acts for that company. Goods stay owned by their
  site, as before.
- Cash is whole cents in a `long`. It is never negative (a debit that would overdraw is refused) and arithmetic is `checked`,
  so overflow throws instead of wrapping.
- Only the server changes cash, and only inside a commit. `TryCredit`/`TryDebit` are private helpers for later sales and
  purchases to call inside their own commit alongside their goods. `AdjustCashDurably` (internal: dev/admin and tests) is the
  only standalone change and follows the `TryAdvanceDurably` snapshot/commit/restore pattern.
- A site view includes only the company that owns that site, so a client never sees another company's balance.
- PROTOTYPE seed: `DevWorld` creates `dev-company` owning `dev-site` with 50000 cents ($500.00). A save from before companies
  gets it exactly once (`EnsureCompany`, committed before serving); a site that already has a company is never topped up.

Open: debt/bankruptcy (GDD section 14 draft), more than one company per world, per-member permissions, and prices.

## Decision: keep the JSON payload; no mixed storage

Cash is a field of the versioned world payload (schema v5) in `world.db`, **not** a separate `companies` table.

Reason: recovery. Each `snapshots` row is a complete, checksummed world at one revision, and recovery falls back to the
previous row when the newest fails verification. A separate table has no revision history, so after such a fallback the goods
would roll back while the cash did not: a sale could leave both the food and the money. Giving the table its own revisions
would rebuild the snapshot store inside it. **A mix of JSON payload and relational tables is ruled out**; the world moves as a
whole or not at all.

Known costs of the payload model (not specific to cash):

- The whole world is serialized and written with `synchronous = FULL` about once a second (the clock tick) and on every
  accepted command.
- The database enforces no invariants (no CHECK/UNIQUE/foreign keys); only `GoodsWorld.Validate` does.
- `Outcomes` (terminal request results for idempotent replay) is never pruned, so every commit rewrites the full history.
- The save cannot be inspected or queried with ordinary SQL tools.

### When to move the world to relational tables

Revisit when any of these holds:

- a world commit regularly exceeds **50 ms** (`GoodsCommitStats.SlowCommitMilliseconds`);
- the payload exceeds **1 MB** (`GoodsCommitStats.LargePayloadBytes`);
- a second major system needs persistence (employees, customers, vehicles), i.e. the decision-0002 full-world persistence.

The server measures these (`GoodsSnapshotStore.Stats`, a `[Goods]` log summary each minute, a one-time warning per threshold).
First measurement, one dev host in the Editor: `[Goods] commits=268 avg=6.5ms max=32.0ms payload=2.0KB` (the counters were
shared with test runs in the same Editor process; see the verification record).

The move, when it happens, includes: the whole world at once; writes of changed rows only; per-row revisions or a change log
so rollback still restores one consistent revision; a retention rule for `Outcomes`; database-level constraints; and a
migration dry run against an isolated copy before any real save is touched.

## Consequences

- Payload schema v4 upgrades to v5 in memory (`Companies = []`); `world.db` layout stays `user_version` 1. Pre-SQLite files
  imported by `ImportLegacy` take the same upgrade.
- An existing application `world.db` is upgraded and gains the dev company on the next server start. This is an intended
  write, like `EnsureBeltStock`.
- `InternalsVisibleTo` exposes the internal cash API to the Goods and Session EditMode test assemblies only.
