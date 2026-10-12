# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-07 17:02:13Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\bed34d0e02e94a408a9300aeaea7bb1d`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 3.0 |  |
| S2 | Walk in through the front door | pass | 2.2 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.5 |  |
| S4 | Place the oven inside the shell | pass | 0.3 |  |
| S5 | Load dough, bake, take bread out | pass | 102.0 |  |
| S6 | Put bread into the register, work it | pass | 9.3 |  |
| S7 | Wait for sales | skipped (debug) | 0.0 |  |
| S8 | Watch the customer figures | skipped (debug) | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 3.9 |  |
| S10 | Build mode: table, decor, wall and door | pass | 2.7 |  |
| S11 | Place a dock on the apron | pass | 1.7 |  |
| S12 | Second restaurant, carry goods, truck route | pass | 193.2 |  |
| S13 | Restart the host mid-loop | pass | 23.7 |  |

## S1 Create the world, host joins: pass
- time to playable 2.9 s (Begin to avatar with live controls)
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
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-b037f53f85ca4ad7be9fc355143bfbc8)
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
- first bread after 9.7 s
- oven output 10 bread after 99.8 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=11 avg=4.7ms max=7.1ms payload=69.8KB

## S6 Put bread into the register, work it: pass
- staff button reads 'Work this register'
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-b037f53f85ca4ad7be9fc355143bfbc8'
- console since the last step: 0 errors, 0 warnings

## S7 Wait for sales: skipped (debug)

## S8 Watch the customer figures: skipped (debug)

## S9 Register empty, then unstaffed: pass
- register screen (empty, staffed): 'No customers waiting; keep Bread in the input' / 'Staffed by you'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-staffed.png
- staff button reads 'Leave'
- register screen (empty, unstaffed): 'No customers waiting; keep Bread in the input' / 'Staffed by nobody: customers are not served'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-unstaffed.png
- world HUD line with no screen open: 'B: build mode, E: inventory (pick belts or goods to carry them out), L: trucks, 1-9: hotbar, left click: open machine, right click: pick up, R: turn belt, F: take an item off a belt'
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=50 avg=5.2ms max=25.4ms payload=89.8KB

## S10 Build mode: table, decor, wall and door: pass
- chosen cells: bistro table (14,17), wall art (3,19), interior wall (13,16)-(13,17), door in it at (13,16)
- bistro table preview at (14,17): 1 x Bistro table (2 seats): charge $60.00, refund $0.00
- bistro table: accepted; cash now $999,787.50
- wall art preview at (3,19): 1 x Abstract wall art: charge $70.00, refund $0.00
- wall art: accepted; cash now $999,717.50
- ambience with the art 11
- interior wall drawn: pending True, 2 m of Plaster wall charge $96.00 
- interior wall: accepted; cash now $999,621.50
- door in the interior wall preview at (13,16): Panel door door: charge $150.00, refund $0.00
- door in the interior wall: accepted; cash now $999,471.50
- sell the wall art preview at (3,19): Sell Abstract wall art: charge $0.00, refund $70.00
- sell the wall art: accepted; cash now $999,541.50
- cash $999,847.50 -> $999,541.50; ambience 0 -> 4 (wall art sold back at the end)
- build outcomes: bought $60.00; bought $70.00; shell-changed $96.00; shell-changed $150.00; sold -$70.00
- seat reachability: start-table (table, 4 seats) reached from the street: False
- seat reachability: buy:player-b037f53f85ca4ad7be9fc355143bfbc8:e7af144eb271499189be3efe8ab2c846:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0098 outside the shell: not-beside-back-door 146, out-of-bounds 6, ok 5, blocked 5
- supplier-dock preview at (12,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,481.50
- dock buy:player-b037f53f85ca4ad7be9fc355143bfbc8:36990fb8131f486484735cf2efb41fe8:0 at (12,2) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: pass
- nearest restaurant for sale lot-restaurant-0106 (restaurant-0106), $41,400.00, 129 m from the start site
- diner marker 130 m from the avatar on the apron (reach 60 m)
- street walk: 6 waypoints, 177 m
- buy panel: 'For sale', price shown 'Price: $41,400.00  (lot 22 x 27 m)'
- bought for $41,400.00
- carrying belt x50, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, lift x10
- street walk: 6 waypoints, 177 m
- entered site-restaurant-0106 after 71 s; rejection none
- dock 2x1 on lot-restaurant-0106 outside the shell: not-beside-back-door 209, out-of-bounds 29, ok 5, blocked 5
- supplier-dock preview at (1,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $958,021.50
- Buy supplier-counter: accepted
- UI clicks so far: 21 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- counter ghost at (5,25): visible True, status 'Cursor: counter: left click places, R rotates, X or Esc clears'
- staff button reads 'Work this register'
- second register buy:player-b037f53f85ca4ad7be9fc355143bfbc8:2289d58e90b44c33a43d469f04d4c440 staffed by 'player-b037f53f85ca4ad7be9fc355143bfbc8', stocked 0
- capture s12-diner.png (overview)
- street walk: 6 waypoints, 177 m
- back home after 36 s; register at the second site staffed by '' after leaving
- Buy supplier-dough-5: accepted
- UI clicks so far: 23 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- walk to buy:player-b037f53f85ca4ad7be9fc355143bfbc8:36990fb8131f486484735cf2efb41fe8:0 outside: indoors True at (-4.46, 0.02, 1.58), after leaving False at (5.40, 0.03, -5.92), target (5.50, 0.00, -6.50)
- dock screen says 'No truck serves this dock; press L to set up a route.'
- 5 dough on the start dock's outgoing
- UI clicks so far: 33 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- truck: docked at the start 0 s, left loaded 1 s, 5/5 dough delivered after 37 s; state ToPickup
- S12 frame time over 300 frames: mean 21.4 ms, p95 31.6 ms, max 52.3 ms (Editor)
- sales at the unviewed second site so far 0; its register staffed by '', stocked 0
- capture s12-sites.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=86 avg=6.2ms max=25.4ms payload=128.3KB

## S13 Restart the host mid-loop: pass
- staff button reads 'Work this register'
- Buy supplier-dough-5: accepted
- UI clicks so far: 37 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- walk to buy:player-b037f53f85ca4ad7be9fc355143bfbc8:36990fb8131f486484735cf2efb41fe8:0 outside: indoors True at (-0.96, 0.03, -1.32), after leaving False at (5.40, 0.03, -5.94), target (5.50, 0.00, -6.50)
- dock screen says 'Truck 1: picks up here'
- before restart: revision 621, clock 340, register bread 10, staff 'player-b037f53f85ca4ad7be9fc355143bfbc8', customers here 0 (queued 0), truck ToDropoff on road True
- no customer was queued at the restart (none could be forced without test-only seeding)
- committed revision 621 (live 621); clock lag 0 s
- time to playable 1.8 s (Begin to avatar with live controls)
- after restart: revision 623, clock 341, client site site-restaurant-0098
- units committed belt:50 bread:10 dough:10 lift:10 | after belt:50 bread:10 dough:10 lift:10
- cash committed $957,716.50 | after $957,716.50
- equipment committed 9 | after 9; trucks 1 | 1; customers 117 | 116
- truck after restart ToDropoff, on road True, leg road-0463
- register staff after restart '' (0035: cleared)
- resumed vs live at shutdown: same; vs last commit: same (customers may have bought in between)
- console since the last step: 0 errors, 0 warnings

