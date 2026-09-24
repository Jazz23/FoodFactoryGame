# 0011 - SQLite for All Data Storage

Date: 2026-09-23

Status: required by the project owner ("all data storage should use sqlite");
implemented for every store that existed on this date. See
[architecture status](../architecture.md#implemented-sqlite-storage-2026-09-23).

## Decision

Every piece of persisted game or player data is stored in a SQLite database
through the declared `com.gilzoide.sqlite-net` package. New persistence must
not add JSON/text/binary save files, `PlayerPrefs`, or other stores. Content
authored as Unity assets (ScriptableObjects, scenes, prefabs) is not data
storage in this sense and is unchanged.

Stores on this date:

| Store | Database | Layout |
| --- | --- | --- |
| Goods world snapshot | `<save>/world.db` | `snapshots` (revision, world, payload schema, JSON payload, SHA-256), `quarantined_snapshots`; `user_version` 1 |
| Player registry | `<save>/players.db` | unchanged (decision 0005) |
| Client secret | `Identity/identity.db` | `identity` (one row); `user_version` 1 |

## Consequences

- The goods snapshot keeps its versioned JSON payload and checksum; only its
  container changed. A commit is one `BEGIN IMMEDIATE` transaction with
  `synchronous = FULL`, which replaces the temporary-file/rename/`.previous`
  scheme. The newest row and the prior valid row are kept, so a damaged
  latest payload still recovers the previous commit. Rows that fail
  verification are moved to `quarantined_snapshots` instead of deleted.
  Stale/conflicting revisions are still refused, and a newer payload schema
  is still never read as an older backup.
- The payload is not normalized into relational tables. That would be a
  larger rewrite of the snapshot contract; it can be revisited when the
  decision-0002 full-world persistence is implemented.
- World and player registry remain separate databases, so a grant and a new
  identity still commit separately (as before). Merging them is open.
- Pre-SQLite files are imported once and left in place: the server imports
  `world.snapshot` (or its `.previous`) into a missing `world.db` after a dry
  run; a client imports the default `Identity/client.secret` into a missing
  `identity.db`, keeping its player ID. An explicit `-identity` path is
  always opened as a SQLite database; an old plain-text secret passed there
  fails to open.
- Both snapshot rows live in one file, so damage to the database file
  itself (rather than one row) is not covered by the previous-row fallback.
  No backup/export tooling exists yet.
