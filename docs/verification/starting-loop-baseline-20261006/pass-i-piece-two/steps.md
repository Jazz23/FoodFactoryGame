# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-06 20:56:08Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\5fc363fd141b4a3ca64bde61475e6c6c`; Unity 6000.5.9f1; screen 1470x717.

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.6 |  |
| S2 | Walk in through the front door | pass | 2.0 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.5 |  |
| S4 | Place the oven inside the shell | pass | 0.3 |  |
| S5 | Load dough, bake, take bread out | pass | 101.8 |  |
| S6 | Put bread into the register, work it | pass | 6.9 |  |
| S7 | Wait for sales | pass | 600.0 |  |
| S8 | Watch the customer figures | pass | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 4.1 |  |
| S10 | Build mode: table, decor, wall and door | pass | 3.0 |  |
| S11 | Place a dock on the apron | pass | 1.8 |  |
| S12 | Second restaurant, carry goods, truck route | fail | 38.7 | Truck route >: neither the device press nor a UI pointer event had an effect |
| S13 | Restart the host mid-loop | pass | 10.0 |  |

## S1 Create the world, host joins: pass
- time to playable 2.4 s (Begin to avatar with live controls)
- seed piece-two; start lot lot-restaurant-0068, site site-restaurant-0068, building restaurant-0068 14x16 at (2,0) in lot 16x16
- cash $1,000,000.00 (expected $1,000,000.00)
- start equipment start-counter:counter@(8,3), start-table:table@(13,14)
- spawn (-7.02, 0.01, -0.50) = cell (0,7); inside building: False
- starter inventory dough x5, belt x50, lift x10
- doors (2,7) (2,8); street side (-1, 0)
- districts 25, competitors 178
- capture s1-start.png
- console since the last step: 0 errors, 0 warnings

## S2 Walk in through the front door: pass
- front door cell (2,7) facing (-1.00, 0.00, 0.00)
- door swing found False, largest angle 0 deg
- indoors: building restaurant-0068, camera top-down True, pointer locked False, crosshair aims with the free pointer
- capture s2-indoors.png
- console since the last step: 0 errors, 0 warnings

## S3 Supplier: buy an oven and dough: pass
- supplier window offers: supplier-dough-5 5 dough $2.50; supplier-belt-10 10 belt $5.00; supplier-oven 1 oven $150.00; supplier-fridge 1 fridge $80.00; supplier-dock 1 dock $60.00; supplier-table 1 table $40.00; supplier-counter 1 counter $50.00
- Buy supplier-oven: accepted
- Buy supplier-dough-5: accepted
- UI clicks so far: 2 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- debit $152.50 (expected $152.50)
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-99e35765ca374977ac18d41ced2b07c4)
- console since the last step: 0 errors, 0 warnings

## S4 Place the oven inside the shell: pass
- oven 3x3 interior anchors: ok 106, blocked 14
- oven ghost at (12,1): visible True, status 'Cursor: oven: left click places, R rotates, X or Esc clears'
- oven placed at (12,1) rotation 0
- capture s4-oven.png
- console since the last step: 0 errors, 0 warnings

## S5 Load dough, bake, take bread out: pass
- dough held 10; recipe oven-bread: 1 dough -> 1 bread in 10 s
- oven screen progress line 'Making Bread: 0/10 s'
- dough in the oven 10
- auto-start after 2.2 s
- first bread after 9.0 s
- oven output 10 bread after 99.0 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=11 avg=5.7ms max=13.2ms payload=75.9KB

## S6 Put bread into the register, work it: pass
- staff button reads 'Work this register'
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-99e35765ca374977ac18d41ced2b07c4'
- console since the last step: 0 errors, 0 warnings

## S7 Wait for sales: pass
- capture s7-customers-inside.png
- capture s7-overview.png (overview)
- watched 600 real s = 600 clock s
- first arrival 96 s, first sale 160 s, first seated diner 164 s (real s after staffing)
- customers that chose this restaurant 2, sales 2, walk-outs 0
- cash delta $5.00; sales x $2.50 = $5.00
- S7 frame time over 300 frames: mean 7.2 ms, p95 9.0 ms, max 30.2 ms (Editor)
- console since the last step: 0 errors, 1 warnings
- [Goods] commits=93 avg=11.7ms max=77.8ms payload=118.0KB

## S8 Watch the customer figures: pass
- figures drawn 15 (all restaurants), of customers who chose this restaurant 2 (of 2 such customers); entered the shell 2 (by a door 2); stood at the counter's ordering cells 0; hidden on arrival (seated) 0; with samples in a wall cell 0
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
- chosen cells: bistro table (14,13), wall art (3,15), interior wall (11,1)-(11,2), door in it at (11,1)
- bistro table preview at (14,13): 1 x Bistro table (2 seats): charge $60.00, refund $0.00
- bistro table: accepted; cash now $999,792.50
- wall art preview at (3,15): 1 x Abstract wall art: charge $70.00, refund $0.00
- wall art: accepted; cash now $999,722.50
- ambience with the art 11
- interior wall drawn: pending True, 2 m of Plaster wall charge $96.00 
- interior wall: accepted; cash now $999,626.50
- door in the interior wall preview at (11,1): Panel door door: charge $150.00, refund $0.00
- door in the interior wall: accepted; cash now $999,476.50
- sell the wall art preview at (3,15): Sell Abstract wall art: charge $0.00, refund $70.00
- sell the wall art: accepted; cash now $999,546.50
- cash $999,852.50 -> $999,546.50; ambience 0 -> 4 (wall art sold back at the end)
- build outcomes: bought $60.00; bought $70.00; shell-changed $96.00; shell-changed $150.00; sold -$70.00
- seat reachability: start-table (table, 4 seats) reached from the street: True
- seat reachability: buy:player-99e35765ca374977ac18d41ced2b07c4:303a812b8d89418e9df907249cf64139:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=112 avg=12.0ms max=77.8ms payload=129.2KB

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0068 outside the shell: ok 46, out-of-bounds 2
- supplier-dock preview at (0,0): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,486.50
- dock buy:player-99e35765ca374977ac18d41ced2b07c4:d169e756025c4e68b65b30702339b252:0 at (0,0) rotation 0; street reach True; charged $60.00
- capture s11-dock.png (overview)
- console since the last step: 0 errors, 0 warnings

## S12 Second restaurant, carry goods, truck route: fail
- nearest restaurant for sale lot-restaurant-0067 (restaurant-0067), $24,300.00, 18 m from the start site
- diner marker 20 m from the avatar on the apron (reach 60 m)
- street walk: 2 waypoints, 18 m
- buy panel: 'For sale', price shown 'Price: $24,300.00  (lot 17 x 18 m)'
- bought for $24,300.00
- carrying belt x50, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, bread x1, lift x10
- street walk: 2 waypoints, 18 m
- entered site-restaurant-0067 after 9 s; rejection none
- dock 2x1 on lot-restaurant-0067 outside the shell: ok 52, out-of-bounds 2
- supplier-dock preview at (0,0): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $975,126.50
- Buy supplier-counter: accepted
- UI clicks so far: 21 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- counter ghost at (14,16): visible True, status 'Cursor: counter: left click places, R rotates, X or Esc clears'
- staff button reads 'Work this register'
- second register buy:player-99e35765ca374977ac18d41ced2b07c4:ff450be80733497ba9cd90048a373fb5 staffed by 'player-99e35765ca374977ac18d41ced2b07c4', stocked 0
- capture s12-diner.png (overview)
- street walk: 2 waypoints, 18 m
- back home after 10 s; register at the second site staffed by '' after leaving
- Buy supplier-dough-5: accepted
- UI clicks so far: 23 taken from the virtual mouse, 0 needed a UI pointer event, 0 controls not pickable
- dock screen says 'No truck serves this dock; press L to set up a route.'
- 5 dough on the start dock's outgoing
- UI FINDING: Truck route >: no point of it takes a pointer pick (centre over 'logistics-trucks' (x:604.90, y:197.88, width:330.12, height:250.78)); bounds (x:910.53, y:313.47, width:34.29, height:37.22), panel (x:0.00, y:0.00, width:1440.00, height:702.37) (screen 1470x717); ancestors '' (x:910.53, y:313.47, width:34.29, height:37.22) < 'logistics-route-buy:player-99e35765ca374977ac18d41ced2b07c4:b4a9af4a2df64271978fa49d5d0dfad9' (x:612.73, y:311.51, width:314.45, height:41.14) < 'logistics-truck-buy:player-99e35765ca374977ac18d41ced2b07c4:b4a9af4a2df64271978fa49d5d0dfad9' (x:604.90, y:229.22, width:330.12, height:174.37) < 'logistics-trucks' (x:604.90, y:197.88, width:330.12, height:250.78); PickAll at the centre: VisualElement'logistics-trucks', VisualElement'', VisualElement'logistics'; at its parent's centre: VisualElement'logistics-trucks', VisualElement'', VisualElement'logistics'
- capture failed-s12.png
- console since the last step: 0 errors, 0 warnings

## S13 Restart the host mid-loop: pass
- staff button reads 'Work this register'
- before restart: revision 1065, clock 769, register bread 8, staff 'player-99e35765ca374977ac18d41ced2b07c4', customers here 0 (queued 0), truck none
- no customer was queued at the restart (none could be forced without test-only seeding)
- committed revision 1063 (live 1065); clock lag 2 s
- time to playable 1.8 s (Begin to avatar with live controls)
- after restart: revision 1067, clock 770, client site site-restaurant-0068
- units committed belt:50 bread:8 dough:5 lift:10 | after belt:50 bread:8 dough:5 lift:10
- cash committed $974,824.00 | after $974,824.00
- equipment committed 7 | after 7; trucks 1 | 1; customers 132 | 134
- register staff after restart '' (0035: cleared)
- resumed vs live at shutdown: same; vs last commit: same (customers may have bought in between)
- console since the last step: 0 errors, 0 warnings

