# 0029 - Carrying Goods Between Owned Sites

Date: 2026-09-30

Status: **accepted, implemented** 2026-09-30 by [0031](0031-several-sites-drawn-at-once.md) piece 3b (owner, 2026-09-30). Part of piece 3 of [0028](0028-sites-for-generated-buildings.md):
drawing every owned site in place so players can walk into bought buildings. Customers in generated worlds are built first
(owner: "whatever you suggest").

## Context

Today a player's inventory is a location on one site. Transfers need the source and destination on the same site
(`GoodsWorld.Transfer`), belts and pickup need the inventory on the belt's or machine's site, and a held machine keeps its
site. Avatar movement is controlled by the owning client (`PlayerAvatar`), and the server does not check where a player
stands. Walking from one owned building into another therefore needs a rule for what the player carries.

## Options considered

- A. The inventory moves with the player: on entering another owned lot the client requests it, and the server moves the
  player's carried location and held machines to that site in one commit. **[SELECTED]**
- B. One inventory per site: simple, but nothing can be carried between your own buildings; only trucks move goods.
- C. Allow transfers across sites for carried inventories: breaks 0028 constraint 4 (one path for cross-site movement).

## Decision

- `RequestEnterSite(siteId)` → `EnterSiteDurably`. The server accepts only when the player is granted the site and the
  server's copy of the avatar position is inside that site's lot (plus a small PROTOTYPE margin). In one commit it moves the
  player's carried location (with its lots) and every machine the player holds to the site. Nothing is created or lost;
  a rejection or failed save changes nothing.
- Invariant: a player's carried location and held machines are always on the same site, and it is a site the player is
  granted.
- Leaving a lot onto the street keeps the last site. Employees keep their site-bound hands.
- Owned sites are drawn in place when within **300 m** (PROTOTYPE) of the local camera; farther ones still receive
  baselines for remote management.

## Open

- The exact margin, and whether a server-side movement check is ever needed beyond entering a site.
- Whether goods carried into a building that is later sold move anywhere (resale is still open, GDD 29.6).
