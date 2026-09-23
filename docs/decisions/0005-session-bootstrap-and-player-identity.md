# 0005 - Session Bootstrap and Player Identity

Date: 2026-09-22

Status: accepted by the project owner (recommendations 1–6, with SQLite for
player identity); implemented on branch `session-bootstrap`. See
[architecture status](../architecture.md#implemented-session-bootstrap-and-networked-player-2026-09-22).

## Context

Decision 0002 requires a server-owned world with authenticated clients.
`GoodsNetworkBridge.InitializeServer` needs a world that matches its committed
save and a connection→player resolver, but nothing outside a test fixture
provided either. There was no NetworkManager in a game scene, no player
identity, no player object or camera, and no save location for real play. The
GDD leaves player count, hosting/disconnect behavior and site ownership open.

## Decision

1. **PROTOTYPE identity: a development FishNet `Authenticator`.** Each client
   creates a random 256-bit secret once and keeps it in a local file. On
   connect it sends a display name and that secret. The server stores only the
   SHA-256 hash and maps it to a server-generated stable player ID, so a
   reconnect resolves the same ID and knowing a player ID does not let anyone
   claim it. There is no transport encryption or account system; replace it with
   Unity Authentication or Steam later without changing the
   connection→player contract (`DevAuthenticator.PlayerIdOf`). Clients never
   send player IDs. A second connection for an already-connected identity is
   rejected (`already-connected`).
2. **PROTOTYPE movement authority: the owning client.** The avatar uses a
   client-authoritative FishNet `NetworkTransform`. Position is presentation
   only; no server gameplay rule may trust it. Server range checks or
   client-side prediction come when interactions depend on distance.
3. **Player identity lives in SQLite, not the world snapshot.** The owner asked
   for SQLite (the already-declared `com.gilzoide.sqlite-net` package). The
   registry is `players.db` beside `world.snapshot` in the save directory, with
   one table `players(player_id, display_name, secret_hash UNIQUE,
   created_utc)`, `PRAGMA user_version = 1`, and `synchronous = FULL`. Newer
   registry schemas are refused. This replaces the earlier proposal to put a
   player list in world snapshot v3; the goods snapshot stays at v2.

   Because two stores cannot commit atomically, admission is ordered:
   (a) resolve or insert the identity in one SQLite transaction, then
   (b) commit the site grant through the new idempotent
   `GoodsWorld.TryGrantDurably`, then (c) admit. If (a) fails the connection is
   rejected with nothing written. If (b) fails the grant rolls back, the
   connection is rejected (`persistence-unavailable`), and the identity row
   remains without privileges; the next join resolves the same ID and retries
   the grant. A player is never admitted without both a durable identity and a
   durable grant, and no path duplicates an identity.
4. **PROTOTYPE access rule:** every authenticated player receives the
   `dev-site` grant. GDD ownership/cooperation rules remain open.
5. **Scene:** `Assets/Scenes/DevSite.unity` is the only build scene. It has the
   NetworkManager (Tugboat, `DevAuthenticator`, `GamePrefabs` catalog, not
   DontDestroyOnLoad), `SessionRoot`, the UI Toolkit `SessionPanel`, a floor,
   landmarks and a light, and no scene camera. `SampleScene` and the oven
   prototype stay in the project, outside the build, until placement (3b).
6. **PROTOTYPE player cap:** 8 authenticated players (`server-full`). The real
   maximum is an open GDD decision.

## Open

- Hosting model, host leaving/host migration, disconnect grace, and player
  count (GDD).
- Real accounts, encryption, lobbies, discovery or relay.
- Server trust of position and interaction range.
- Whether identity should be per world (current: registry beside each world
  save) or per installation/account.
- Registry schema migrations beyond v1 (only "refuse newer" exists).

## Consequences and verification

The session composition root commits the world save before FishNet listens and
initializes the bridge with the authenticator's resolver, so no gameplay path
resolves identity from payloads. Evidence is the `FoodFactoryGame.Session`
EditMode and PlayMode suites and the
[verification record](../verification/session-20260922.md).
