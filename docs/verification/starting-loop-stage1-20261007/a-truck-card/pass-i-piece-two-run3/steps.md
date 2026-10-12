# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-07 16:48:31Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\c2b9bdb285274358a16e6b3e9d9f7913`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.6 |  |
| S2 | Walk in through the front door | pass | 2.2 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.5 |  |
| S4 | Place the oven inside the shell | pass | 0.3 |  |
| S5 | Load dough, bake, take bread out | pass | 101.6 |  |
| S6 | Put bread into the register, work it | pass | 9.3 |  |
| S7 | Wait for sales | pass | 600.0 |  |
| S8 | Watch the customer figures | pass | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 4.1 |  |
| S10 | Build mode: table, decor, wall and door | pass | 2.9 |  |
| S11 | Place a dock on the apron | pass | 1.8 |  |
| S12 | Second restaurant, carry goods, truck route | pass | 193.6 |  |
| S13 | Restart the host mid-loop | fail | 28.4 | walking to beside buy:player-1c9678f1827042489204bc93c5b96d2c:181b5aa597a6414cb85f96a72586a7fd:0: still 3.0 m away after 14 s at (2.84, 0.03, -4.54) (target (3.50, 0.00, -7.50)) |

## S1 Create the world, host joins: pass
- time to playable 2.5 s (Begin to avatar with live controls)
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
- Buy supplier-oven: accepted
- Buy supplier-dough-5: accepted
- UI clicks so far: 2 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- debit $152.50 (expected $152.50)
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-1c9678f1827042489204bc93c5b96d2c)
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
- auto-start after 1.8 s
- first bread after 9.3 s
- oven output 10 bread after 99.3 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=11 avg=5.1ms max=8.8ms payload=69.8KB

## S6 Put bread into the register, work it: pass
- staff button reads 'Work this register'
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-1c9678f1827042489204bc93c5b96d2c'
- console since the last step: 0 errors, 0 warnings

## S7 Wait for sales: pass
- capture s7-customers-inside.png
- capture s7-overview.png (overview)
- watched 600 real s = 600 clock s
- first arrival 172 s, first sale 239 s, first seated diner 245 s (real s after staffing)
- customers that chose this restaurant 1, sales 1, walk-outs 0
- cash delta $2.50; sales x $2.50 = $2.50
- S7 frame time over 300 frames: mean 7.5 ms, p95 9.3 ms, max 29.4 ms (Editor)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=93 avg=6.5ms max=23.4ms payload=110.8KB

## S8 Watch the customer figures: pass
- figures drawn 7 (all restaurants), of customers who chose this restaurant 1 (of 1 such customers); entered the shell 1 (by a door 1); stood at the counter's ordering cells 0; hidden on arrival (seated) 0; with samples in a wall cell 0
- console since the last step: 0 errors, 0 warnings

## S9 Register empty, then unstaffed: pass
- register screen (empty, staffed): 'No customers waiting; keep Bread in the input' / 'Staffed by you'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-staffed.png
- staff button reads 'Leave'
- register screen (empty, unstaffed): 'No customers waiting; keep Bread in the input' / 'Staffed by nobody: customers are not served'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-unstaffed.png
- world HUD line with no screen open: 'B: build mode, E: inventory (pick belts or goods to carry them out), L: trucks, 1-9: hotbar, left click: open machine, right click: pick up, R: turn belt, F: take an item off a belt'
- console since the last step: 0 errors, 0 warnings

## S10 Build mode: table, decor, wall and door: pass
- chosen cells: bistro table (14,17), wall art (3,19), interior wall (13,16)-(13,17), door in it at (13,16)
- bistro table preview at (14,17): 1 x Bistro table (2 seats): charge $60.00, refund $0.00
- bistro table: accepted; cash now $999,790.00
- wall art preview at (3,19): 1 x Abstract wall art: charge $70.00, refund $0.00
- wall art: accepted; cash now $999,720.00
- ambience with the art 11
- interior wall drawn: pending True, 2 m of Plaster wall charge $96.00 
- interior wall: accepted; cash now $999,624.00
- door in the interior wall preview at (13,16): Panel door door: charge $150.00, refund $0.00
- door in the interior wall: accepted; cash now $999,474.00
- sell the wall art preview at (3,19): Sell Abstract wall art: charge $0.00, refund $70.00
- sell the wall art: accepted; cash now $999,544.00
- cash $999,850.00 -> $999,544.00; ambience 0 -> 4 (wall art sold back at the end)
- build outcomes: bought $60.00; bought $70.00; shell-changed $96.00; shell-changed $150.00; sold -$70.00
- seat reachability: start-table (table, 4 seats) reached from the street: False
- seat reachability: buy:player-1c9678f1827042489204bc93c5b96d2c:53b540089138449897d7bbaeeb0c11c2:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=109 avg=6.4ms max=23.4ms payload=115.2KB

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0098 outside the shell: not-beside-back-door 146, out-of-bounds 6, ok 5, blocked 5
- supplier-dock preview at (12,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,484.00
- dock buy:player-1c9678f1827042489204bc93c5b96d2c:181b5aa597a6414cb85f96a72586a7fd:0 at (12,2) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: pass
- nearest restaurant for sale lot-restaurant-0106 (restaurant-0106), $41,400.00, 129 m from the start site
- diner marker 130 m from the avatar on the apron (reach 60 m)
- street walk: 6 waypoints, 177 m
- buy panel: 'For sale', price shown 'Price: $41,400.00  (lot 22 x 27 m)'
- bought for $41,400.00
- carrying belt x50, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, lift x10
- street walk: 6 waypoints, 177 m
- entered site-restaurant-0106 after 71 s; rejection none
- dock 2x1 on lot-restaurant-0106 outside the shell: not-beside-back-door 209, out-of-bounds 29, ok 5, blocked 5
- supplier-dock preview at (1,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $958,024.00
- Buy supplier-counter: accepted
- UI clicks so far: 21 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- counter ghost at (5,25): visible True, status 'Cursor: counter: left click places, R rotates, X or Esc clears'
- staff button reads 'Work this register'
- second register buy:player-1c9678f1827042489204bc93c5b96d2c:a947d1740d9a4298b94a3b0987bd10dc staffed by 'player-1c9678f1827042489204bc93c5b96d2c', stocked 0
- capture s12-diner.png (overview)
- street walk: 6 waypoints, 177 m
- back home after 36 s; register at the second site staffed by '' after leaving
- Buy supplier-dough-5: accepted
- UI clicks so far: 23 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- dock screen says 'No truck serves this dock; press L to set up a route.'
- 5 dough on the start dock's outgoing
- UI clicks so far: 33 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- truck: docked at the start 0 s, left loaded 1 s, 5/5 dough delivered after 31 s; state ToPickup
- S12 frame time over 300 frames: mean 17.2 ms, p95 21.4 ms, max 55.3 ms (Editor)
- sales at the unviewed second site so far 0; its register staffed by '', stocked 0
- capture s12-sites.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=145 avg=6.6ms max=23.4ms payload=129.4KB

## S13 Restart the host mid-loop: fail
- staff button reads 'Work this register'
- Buy supplier-dough-5: accepted
- UI clicks so far: 37 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- capture failed-s13.png
- console since the last step: 0 errors, 0 warnings

