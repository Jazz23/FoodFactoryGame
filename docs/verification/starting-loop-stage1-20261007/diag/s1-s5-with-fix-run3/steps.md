# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-07 16:26:07Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\84b2e1899b47440890ff8920a91ccad9`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.6 |  |
| S2 | Walk in through the front door | pass | 2.2 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.5 |  |
| S4 | Place the oven inside the shell | pass | 0.3 |  |
| S5 | Load dough, bake, take bread out | pass | 101.5 |  |
| S6 | Put bread into the register, work it | skipped (debug) | 0.0 |  |
| S7 | Wait for sales | skipped (debug) | 0.0 |  |
| S8 | Watch the customer figures | skipped (debug) | 0.0 |  |
| S9 | Register empty, then unstaffed | skipped (debug) | 0.0 |  |
| S10 | Build mode: table, decor, wall and door | skipped (debug) | 0.0 |  |
| S11 | Place a dock on the apron | skipped (debug) | 0.0 |  |
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

## S3 Supplier: buy an oven and dough: pass
- supplier window offers: supplier-dough-5 5 dough $2.50; supplier-belt-10 10 belt $5.00; supplier-oven 1 oven $150.00; supplier-fridge 1 fridge $80.00; supplier-dock 1 dock $60.00; supplier-table 1 table $40.00; supplier-counter 1 counter $50.00
- Buy supplier-oven: accepted
- Buy supplier-dough-5: accepted
- UI clicks so far: 2 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- debit $152.50 (expected $152.50)
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-11728aa05e264e9cb39fa29e12aa0c71)
- console since the last step: 0 errors, 0 warnings

## S4 Place the oven inside the shell: pass
- oven 3x3 interior anchors: ok 106, blocked 14
- oven ghost at (10,16): visible True, status 'Cursor: oven: left click places, R rotates, X or Esc clears'
- oven placed at (10,16) rotation 0
- capture s4-oven.png
- console since the last step: 0 errors, 0 warnings

## S5 Load dough, bake, take bread out: pass
- dough held 10; recipe oven-bread: 1 dough -> 1 bread in 10 s
- oven screen progress line 'Making Bread: 0/10 s'
- dough in the oven 10
- auto-start after 1.9 s
- first bread after 9.2 s
- oven output 10 bread after 99.2 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=11 avg=4.7ms max=6.9ms payload=69.8KB

## S6 Put bread into the register, work it: skipped (debug)

## S7 Wait for sales: skipped (debug)

## S8 Watch the customer figures: skipped (debug)

## S9 Register empty, then unstaffed: skipped (debug)

## S10 Build mode: table, decor, wall and door: skipped (debug)

## S11 Place a dock on the apron: skipped (debug)

## S12 Second restaurant, carry goods, truck route: skipped (debug)

## S13 Restart the host mid-loop: skipped (debug)

