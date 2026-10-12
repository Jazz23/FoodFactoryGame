# P0 Pass R (requests), seed `piece-two`

Run 2026-10-07 17:37:53Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\2078446f6b694929829639aee8bb5efc`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.3 |  |
| S2 | Walk in through the front door | pass | 3.6 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.2 |  |
| S4 | Place the oven inside the shell | pass | 0.2 |  |
| S5 | Load dough, bake, take bread out | pass | 100.2 |  |
| S6 | Put bread into the register, work it | pass | 6.2 |  |
| S7 | Wait for sales | pass | 600.0 |  |
| S8 | Watch the customer figures | pass | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 3.2 |  |
| S10 | Build mode: table, decor, wall and door | pass | 0.7 |  |
| S11 | Place a dock on the apron | pass | 0.3 |  |
| S12 | Second restaurant, carry goods, truck route | pass | 26.8 |  |
| S13 | Restart the host mid-loop | pass | 25.9 |  |

## S1 Create the world, host joins: pass
- time to playable 2.2 s (Begin to avatar with live controls)
- seed piece-two; start lot lot-restaurant-0098, site site-restaurant-0098, building restaurant-0098 14x16 at (2,4) in lot 16x20
- cash $1,000,000.00 (expected $1,000,000.00)
- start equipment start-dock:dock@(11,3), start-counter:counter@(8,7), start-table:table@(13,18)
- spawn (-7.50, -0.04, 1.50) = cell (0,11); inside building: False
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
- debit $152.50 (expected $152.50)
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-46abe1cee2de470e8f32f75be95fb13f)
- console since the last step: 0 errors, 0 warnings

## S4 Place the oven inside the shell: pass
- oven 3x3 interior anchors: ok 106, blocked 14
- oven placed at (10,16) rotation 0
- capture s4-oven.png
- console since the last step: 0 errors, 0 warnings

## S5 Load dough, bake, take bread out: pass
- dough held 10; recipe oven-bread: 1 dough -> 1 bread in 10 s
- dough in the oven 10
- auto-start after 0.2 s
- first bread after 9.0 s
- oven output 10 bread after 99.0 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=11 avg=5.8ms max=13.2ms payload=70.0KB

## S6 Put bread into the register, work it: pass
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-46abe1cee2de470e8f32f75be95fb13f'
- console since the last step: 0 errors, 0 warnings

## S7 Wait for sales: pass
- capture s7-customers-inside.png
- capture s7-overview.png (overview)
- watched 600 real s = 600 clock s
- first arrival 176 s, first sale 243 s, first seated diner 249 s (real s after staffing)
- customers that chose this restaurant 1, sales 1, walk-outs 0
- cash delta $2.50; sales x $2.50 = $2.50
- S7 frame time over 300 frames: mean 7.4 ms, p95 8.6 ms, max 29.5 ms (Editor)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=93 avg=7.4ms max=22.4ms payload=111.1KB

## S8 Watch the customer figures: pass
- figures drawn 7 (all restaurants), of customers who chose this restaurant 1 (of 1 such customers); entered the shell 1 (by a door 1); stood at the counter's ordering cells 0; hidden on arrival (seated) 0; with samples in a wall cell 0
- console since the last step: 0 errors, 0 warnings

## S9 Register empty, then unstaffed: pass
- register screen (empty, staffed): 'Nothing to sell: put edible Bread in the input' / 'Staffed by you'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-staffed.png
- register screen (empty, unstaffed): 'Nothing to sell: put edible Bread in the input' / 'Staffed by nobody: customers are not served'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-unstaffed.png
- world HUD line with no screen open: 'B: build mode, E: inventory (pick belts or goods to carry them out), L: trucks, 1-9: hotbar, left click: open machine, right click: pick up, R: turn belt, F: take an item off a belt'
- console since the last step: 0 errors, 0 warnings

## S10 Build mode: table, decor, wall and door: pass
- chosen cells: bistro table (14,17), wall art (3,19), interior wall (13,16)-(13,17), door in it at (13,16)
- table: accepted $60.00
- wall art: accepted $70.00
- ambience with the art 11
- interior wall: accepted $96.00
- door in it: accepted $150.00
- wall art sold: accepted -$70.00
- cash $999,850.00 -> $999,544.00; ambience 0 -> 4 (wall art sold back at the end)
- build outcomes: bought $60.00; bought $70.00; shell-changed $96.00; shell-changed $150.00; sold -$70.00
- seat reachability: start-table (table, 4 seats) reached from the street: False
- seat reachability: buy:player-46abe1cee2de470e8f32f75be95fb13f:p0-37-table:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0098 outside the shell: not-beside-back-door 146, out-of-bounds 6, ok 5, blocked 5
- dock buy:player-46abe1cee2de470e8f32f75be95fb13f:p0-42-dock:0 at (12,2) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: pass
- nearest restaurant for sale lot-restaurant-0106 (restaurant-0106), $41,400.00, 129 m from the start site
- bought for $41,400.00
- carrying belt x50, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, lift x10
- entered site-restaurant-0106 after 0 s; rejection none
- dock 2x1 on lot-restaurant-0106 outside the shell: not-beside-back-door 209, out-of-bounds 29, ok 5, blocked 5
- second register buy:player-46abe1cee2de470e8f32f75be95fb13f:p0-45-diner-counter staffed by 'player-46abe1cee2de470e8f32f75be95fb13f', stocked 0
- capture s12-diner.png (overview)
- back home after 0 s; register at the second site staffed by '' after leaving
- 5 dough on the start dock's outgoing
- truck: docked at the start 0 s, left loaded 1 s, 5/5 dough delivered after 25 s; state ToPickup
- S12 frame time over 300 frames: mean 17.3 ms, p95 22.0 ms, max 51.1 ms (Editor)
- sales at the unviewed second site so far 0; its register staffed by '', stocked 0
- capture s12-sites.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=127 avg=7.1ms max=22.4ms payload=134.1KB

## S13 Restart the host mid-loop: pass
- before restart: revision 1040, clock 767, register bread 1, staff 'player-46abe1cee2de470e8f32f75be95fb13f', customers here 0 (queued 0), truck ToDropoff on road True
- no customer was queued at the restart (none could be forced without test-only seeding)
- committed revision 1033 (live 1040); clock lag 7 s
- time to playable 2.1 s (Begin to avatar with live controls)
- after restart: revision 1042, clock 768, client site site-restaurant-0098
- units committed belt:50 bread:9 dough:10 lift:10 | after belt:50 bread:9 dough:10 lift:10
- cash committed $957,719.00 | after $957,719.00
- equipment committed 9 | after 9; trucks 1 | 1; customers 114 | 114
- truck after restart ToDropoff, on road True, leg road-0463
- register staff after restart '' (0035: cleared)
- resumed vs live at shutdown: same; vs last commit: same (customers may have bought in between)
- console since the last step: 0 errors, 0 warnings

