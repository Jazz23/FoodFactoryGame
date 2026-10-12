# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-06 22:20:29Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\a6ddd3e122ed449898a25f1b1a030239`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.6 |  |
| S2 | Walk in through the front door | pass | 2.2 |  |
| S3 | Supplier: buy an oven and dough | skipped (debug) | 0.0 |  |
| S4 | Place the oven inside the shell | skipped (debug) | 0.0 |  |
| S5 | Load dough, bake, take bread out | skipped (debug) | 0.0 |  |
| S6 | Put bread into the register, work it | skipped (debug) | 0.0 |  |
| S7 | Wait for sales | skipped (debug) | 0.0 |  |
| S8 | Watch the customer figures | skipped (debug) | 0.0 |  |
| S9 | Register empty, then unstaffed | skipped (debug) | 0.0 |  |
| S10 | Build mode: table, decor, wall and door | skipped (debug) | 0.0 |  |
| S11 | Place a dock on the apron | pass | 1.7 |  |
| S12 | Second restaurant, carry goods, truck route | skipped (debug) | 0.0 |  |
| S13 | Restart the host mid-loop | skipped (debug) | 0.0 |  |

## S1 Create the world, host joins: pass
- time to playable 2.5 s (Begin to avatar with live controls)
- seed piece-two; start lot lot-restaurant-0098, site site-restaurant-0098, building restaurant-0098 14x16 at (2,4) in lot 16x20
- cash $1,000,000.00 (expected $1,000,000.00)
- start equipment start-dock:dock@(11,3), start-counter:counter@(8,7), start-table:table@(13,18)
- spawn (-7.50, 0.05, 1.50) = cell (0,11); inside building: False
- starter inventory dough x5, belt x50, lift x10
- doors (2,11) (2,12) (13,4); street side (-1, 0)
- districts 25, competitors 157
- capture s1-start.png
- console since the last step: 0 errors, 0 warnings

## S2 Walk in through the front door: pass
- front door cell (2,11) facing (-1.00, 0.00, 0.00)
- door swing found False, largest angle 0 deg
- indoors: building restaurant-0098, camera top-down True, pointer locked False, crosshair aims with the free pointer
- capture s2-indoors.png
- console since the last step: 0 errors, 0 warnings

## S3 Supplier: buy an oven and dough: skipped (debug)

## S4 Place the oven inside the shell: skipped (debug)

## S5 Load dough, bake, take bread out: skipped (debug)

## S6 Put bread into the register, work it: skipped (debug)

## S7 Wait for sales: skipped (debug)

## S8 Watch the customer figures: skipped (debug)

## S9 Register empty, then unstaffed: skipped (debug)

## S10 Build mode: table, decor, wall and door: skipped (debug)

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0098 outside the shell: not-beside-back-door 146, out-of-bounds 6, ok 5, blocked 5
- supplier-dock preview at (12,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,940.00
- dock buy:player-6f393f1c7d5d436fa2797d4a424c596b:1e1d179faab244aa95534413493edb19:0 at (12,2) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: skipped (debug)

## S13 Restart the host mid-loop: skipped (debug)

