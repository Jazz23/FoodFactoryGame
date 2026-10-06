# P0 Pass I (simulated input), seed `p0-second`

Run 2026-10-06 21:23:14Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\25e1e30eb4534b12ac1855be30da4ded`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.6 |  |
| S2 | Walk in through the front door | pass | 2.1 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.4 |  |
| S4 | Place the oven inside the shell | pass | 2.7 |  |
| S5 | Load dough, bake, take bread out | pass | 100.3 |  |
| S6 | Put bread into the register, work it | pass | 14.9 |  |
| S7 | Wait for sales | pass | 600.0 |  |
| S8 | Watch the customer figures | pass | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 3.1 |  |
| S10 | Build mode: table, decor, wall and door | pass | 3.1 |  |
| S11 | Place a dock on the apron | pass | 1.8 |  |
| S12 | Second restaurant, carry goods, truck route | fail | 90.3 | Truck route >: neither the device press nor a UI pointer event had an effect |
| S13 | Restart the host mid-loop | pass | 7.2 |  |

## S1 Create the world, host joins: pass
- time to playable 2.5 s (Begin to avatar with live controls)
- seed p0-second; start lot lot-restaurant-0049, site site-restaurant-0049, building restaurant-0049 16x14 at (0,2) in lot 16x16
- cash $1,000,000.00 (expected $1,000,000.00)
- start equipment start-counter:counter@(7,5), start-table:table@(13,14)
- spawn (0.50, -0.04, -7.50) = cell (8,0); inside building: False
- starter inventory dough x5, belt x50, lift x10
- doors (8,2) (7,2); street side (0, -1)
- districts 25, competitors 166
- capture s1-start.png
- console since the last step: 0 errors, 0 warnings

## S2 Walk in through the front door: pass
- front door cell (8,2) facing (0.00, 0.00, -1.00)
- door swing found False, largest angle 0 deg
- indoors: building restaurant-0049, camera top-down True, pointer locked False, crosshair aims with the free pointer
- capture s2-indoors.png
- console since the last step: 0 errors, 0 warnings

## S3 Supplier: buy an oven and dough: pass
- supplier window offers: supplier-dough-5 5 dough $2.50; supplier-belt-10 10 belt $5.00; supplier-oven 1 oven $150.00; supplier-fridge 1 fridge $80.00; supplier-dock 1 dock $60.00; supplier-table 1 table $40.00; supplier-counter 1 counter $50.00
- Buy supplier-oven: accepted
- Buy supplier-dough-5: accepted
- UI clicks so far: 2 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- debit $152.50 (expected $152.50)
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-11aeb8ed040942fb9a6c74a8d97477e5)
- console since the last step: 0 errors, 0 warnings

## S4 Place the oven inside the shell: pass
- oven 3x3 interior anchors: ok 106, blocked 14
- oven ghost at (1,12): visible True, status 'Cursor: oven: left click places, R rotates, X or Esc clears'
- oven placed at (1,12) rotation 0
- capture s4-oven.png
- console since the last step: 0 errors, 0 warnings

## S5 Load dough, bake, take bread out: pass
- dough held 10; recipe oven-bread: 1 dough -> 1 bread in 10 s
- oven screen progress line 'Making Bread: 0/10 s'
- dough in the oven 10
- auto-start after 0.4 s
- first bread after 9.3 s
- oven output 10 bread after 99.3 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 1 warnings
- [Goods] commits=11 avg=46.2ms max=381.3ms payload=72.0KB

## S6 Put bread into the register, work it: pass
- staff button reads 'Work this register'
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-11aeb8ed040942fb9a6c74a8d97477e5'
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=39 avg=22.6ms max=381.3ms payload=89.1KB

## S7 Wait for sales: pass
- capture s7-customers-inside.png
- capture s7-overview.png (overview)
- watched 600 real s = 600 clock s
- first arrival 315 s, first sale 395 s, first seated diner 399 s (real s after staffing)
- customers that chose this restaurant 1, sales 1, walk-outs 0
- cash delta $2.50; sales x $2.50 = $2.50
- S7 frame time over 300 frames: mean 6.8 ms, p95 8.3 ms, max 27.0 ms (Editor)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=99 avg=25.6ms max=381.3ms payload=115.7KB

## S8 Watch the customer figures: pass
- figures drawn 5 (all restaurants), of customers who chose this restaurant 1 (of 1 such customers); entered the shell 1 (by a door 1); stood at the counter's ordering cells 1; hidden on arrival (seated) 0; with samples in a wall cell 0
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
- chosen cells: bistro table (14,13), wall art (1,15), interior wall (13,12)-(13,13), door in it at (13,12)
- bistro table preview at (14,13): 1 x Bistro table (2 seats): charge $60.00, refund $0.00
- bistro table: accepted; cash now $999,790.00
- wall art preview at (1,15): 1 x Abstract wall art: charge $70.00, refund $0.00
- wall art: accepted; cash now $999,720.00
- ambience with the art 11
- interior wall drawn: pending True, 2 m of Plaster wall charge $96.00 
- interior wall: accepted; cash now $999,624.00
- door in the interior wall preview at (13,12): Panel door door: charge $150.00, refund $0.00
- door in the interior wall: accepted; cash now $999,474.00
- sell the wall art preview at (1,15): Sell Abstract wall art: charge $0.00, refund $70.00
- sell the wall art: accepted; cash now $999,544.00
- cash $999,850.00 -> $999,544.00; ambience 0 -> 4 (wall art sold back at the end)
- build outcomes: bought $60.00; bought $70.00; shell-changed $96.00; shell-changed $150.00; sold -$70.00
- seat reachability: start-table (table, 4 seats) reached from the street: True
- seat reachability: buy:player-11aeb8ed040942fb9a6c74a8d97477e5:45c548d3ab41437aa905cd311676af40:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0049 outside the shell: ok 46, out-of-bounds 2
- supplier-dock preview at (0,0): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,484.00
- dock buy:player-11aeb8ed040942fb9a6c74a8d97477e5:5778fcc0c9614d15ac93902dbb6e7d5e:0 at (0,0) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: fail
- nearest restaurant for sale lot-restaurant-0063 (restaurant-0063), $100,800.00, 102 m from the start site
- diner marker 95 m from the avatar on the apron (reach 60 m)
- street walk: 5 waypoints, 100 m
- buy panel: 'For sale', price shown 'Price: $100,800.00  (lot 16 x 16 m)'
- bought for $100,800.00
- carrying belt x50, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, lift x10
- street walk: 5 waypoints, 100 m
- entered site-restaurant-0063 after 36 s; rejection none
- dock 2x1 on lot-restaurant-0063 outside the shell: ok 46, out-of-bounds 18
- supplier-dock preview at (14,0): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $898,624.00
- Buy supplier-counter: accepted
- UI clicks so far: 21 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- counter ghost at (1,14): visible True, status 'Cursor: counter: left click places, R rotates, X or Esc clears'
- staff button reads 'Work this register'
- second register buy:player-11aeb8ed040942fb9a6c74a8d97477e5:08950d1d926e42838cea086094f9d6cb staffed by 'player-11aeb8ed040942fb9a6c74a8d97477e5', stocked 0
- capture s12-diner.png (overview)
- street walk: 5 waypoints, 100 m
- back home after 25 s; register at the second site staffed by '' after leaving
- Buy supplier-dough-5: accepted
- UI clicks so far: 23 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- dock screen says 'No truck serves this dock; press L to set up a route.'
- 5 dough on the start dock's outgoing
- UI FINDING: Truck route >: no point of it takes a pointer pick (centre over 'logistics-trucks' (x:604.90, y:197.88, width:330.12, height:250.78)); bounds (x:910.53, y:313.47, width:34.29, height:37.22), panel (x:0.00, y:0.00, width:1440.00, height:702.37) (screen 1470x717); ancestors '' (x:910.53, y:313.47, width:34.29, height:37.22) < 'logistics-route-buy:player-11aeb8ed040942fb9a6c74a8d97477e5:77bbc5b54ec44381a610233cf428315e' (x:612.73, y:311.51, width:314.45, height:41.14) < 'logistics-truck-buy:player-11aeb8ed040942fb9a6c74a8d97477e5:77bbc5b54ec44381a610233cf428315e' (x:604.90, y:229.22, width:330.12, height:174.37) < 'logistics-trucks' (x:604.90, y:197.88, width:330.12, height:250.78); PickAll at the centre: VisualElement'logistics-trucks', VisualElement'', VisualElement'logistics'; at its parent's centre: VisualElement'logistics-trucks', VisualElement'', VisualElement'logistics'
- capture failed-s12.png
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=122 avg=23.9ms max=381.3ms payload=124.5KB

## S13 Restart the host mid-loop: pass
- staff button reads 'Work this register'
- before restart: revision 1111, clock 826, register bread 9, staff 'player-11aeb8ed040942fb9a6c74a8d97477e5', customers here 0 (queued 0), truck none
- no customer was queued at the restart (none could be forced without test-only seeding)
- committed revision 1109 (live 1111); clock lag 2 s
- time to playable 1.8 s (Begin to avatar with live controls)
- after restart: revision 1113, clock 827, client site site-restaurant-0049
- units committed belt:50 bread:9 dough:5 lift:10 | after belt:50 bread:9 dough:5 lift:10
- cash committed $898,321.50 | after $898,321.50
- equipment committed 7 | after 7; trucks 1 | 1; customers 122 | 122
- register staff after restart '' (0035: cleared)
- resumed vs live at shutdown: same; vs last commit: same (customers may have bought in between)
- console since the last step: 0 errors, 0 warnings

