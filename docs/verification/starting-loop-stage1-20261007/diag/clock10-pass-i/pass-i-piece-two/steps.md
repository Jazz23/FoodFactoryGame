# P0 Pass I (simulated input), seed `piece-two`

Run 2026-10-07 18:07:55Z; isolated save `C:\Users\Deven\AppData\Local\Temp\FoodFactoryStartingLoop\54442430e19341599906d9eded2eac43`; Unity 6000.5.9f1; screen 1470x717.
**Debug run: server clock x10 (flag `clock=`); not an acceptance run.**

| Step | Action | Result | Seconds | Notes |
|---|---|---|---|---|
| S1 | Create the world, host joins | pass | 2.2 |  |
| S2 | Walk in through the front door | pass | 2.1 |  |
| S3 | Supplier: buy an oven and dough | pass | 0.5 |  |
| S4 | Place the oven inside the shell | pass | 0.3 |  |
| S5 | Load dough, bake, take bread out | pass | 12.2 |  |
| S6 | Put bread into the register, work it | pass | 9.4 |  |
| S7 | Wait for sales | pass | 60.0 |  |
| S8 | Watch the customer figures | pass | 0.0 |  |
| S9 | Register empty, then unstaffed | pass | 4.1 |  |
| S10 | Build mode: table, decor, wall and door | pass | 2.9 |  |
| S11 | Place a dock on the apron | pass | 1.8 |  |
| S12 | Second restaurant, carry goods, truck route | pass | 162.8 |  |
| S13 | Restart the host mid-loop | pass | 18.6 |  |

## S1 Create the world, host joins: pass
- time to playable 2.1 s (Begin to avatar with live controls)
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
- goods land in: dough x10, belt x50, lift x10, machine oven (location carried:player-4a797b1263724a899cd5f4b12eab5c91)
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
- first bread after 0.9 s
- oven output 10 bread after 9.9 s; dough left in input 0; blocked False
- spoil timers: 1 bread 3510 s left of 3600, 1 bread 3520 s left of 3600, 1 bread 3530 s left of 3600, 1 bread 3540 s left of 3600, 1 bread 3550 s left of 3600, 1 bread 3560 s left of 3600, 1 bread 3570 s left of 3600, 1 bread 3580 s left of 3600, 1 bread 3590 s left of 3600, 1 bread 3600 s left of 3600
- spoil timers shown on the oven screen: 58m
- held after baking: belt x50, lift x10, bread x10
- console since the last step: 0 errors, 0 warnings

## S6 Put bread into the register, work it: pass
- staff button reads 'Work this register'
- register stocked with 10 bread, staffed by the host
- capture s6-register.png
- after walking out of the door and using the oven, register staff = 'player-4a797b1263724a899cd5f4b12eab5c91'
- console since the last step: 0 errors, 0 warnings

## S7 Wait for sales: pass
- capture s7-customers-inside.png
- capture s7-overview.png (overview)
- watched 60 real s = 601 clock s (clock x10)
- first arrival 4 s, first sale 10 s, first seated diner 10 s (real s after staffing)
- customers that chose this restaurant 1, sales 1, walk-outs 0
- cash delta $2.50; sales x $2.50 = $2.50
- S7 frame time over 300 frames: mean 8.6 ms, p95 11.4 ms, max 31.2 ms (Editor)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=86 avg=5.8ms max=11.8ms payload=114.6KB

## S8 Watch the customer figures: pass
- figures drawn 7 (all restaurants), of customers who chose this restaurant 1 (of 1 such customers); entered the shell 1 (by a door 1); stood at the counter's ordering cells 0; hidden on arrival (seated) 0; with samples in a wall cell 0
- console since the last step: 0 errors, 0 warnings

## S9 Register empty, then unstaffed: pass
- register screen (empty, staffed): 'Nothing to sell: put edible Bread in the input' / 'Staffed by you'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
- capture s9-empty-staffed.png
- staff button reads 'Leave'
- register screen (empty, unstaffed): 'Nothing to sell: put edible Bread in the input' / 'Staffed by nobody: customers are not served'; status line 'Register: put edible goods in the input; customers queue and pay here only while someone works it (Work this register); E or Esc closes'
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
- seat reachability: buy:player-4a797b1263724a899cd5f4b12eab5c91:b4014f9a01af47878b83e0006f7c9a4e:0 (rt-table-bistro, 2 seats) reached from the street: True
- register reached from the street: True
- capture s10-build.png (overview)
- console since the last step: 0 errors, 0 warnings

## S11 Place a dock on the apron: pass
- dock 2x1 on lot-restaurant-0098 outside the shell: not-beside-back-door 146, out-of-bounds 6, ok 5, blocked 5
- supplier-dock preview at (12,2): 1 x dock: charge $60.00, refund $0.00
- supplier-dock: accepted; cash now $999,484.00
- dock buy:player-4a797b1263724a899cd5f4b12eab5c91:7c64ac92721241748cc0e7b3d117fa41:0 at (12,2) rotation 0; street reach True; charged $60.00
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
- UI route: Buy supplier-counter: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- Buy supplier-counter: accepted
- UI clicks so far: 20 taken from the virtual mouse, 1 needed a UI pointer event, 0 controls not pickable
- counter ghost at (5,25): visible True, status 'Cursor: counter: left click places, R rotates, X or Esc clears'
- staff button reads 'Work this register'
- UI route: Work this register: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- second register buy:player-4a797b1263724a899cd5f4b12eab5c91:dc096106a054486aa321e0083b6822ac staffed by 'player-4a797b1263724a899cd5f4b12eab5c91', stocked 0
- capture s12-diner.png (overview)
- street walk: 6 waypoints, 177 m
- back home after 36 s; register at the second site staffed by '' after leaving
- UI route: Buy supplier-dough-5: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- Buy supplier-dough-5: accepted
- UI clicks so far: 20 taken from the virtual mouse, 3 needed a UI pointer event, 0 controls not pickable
- walk to buy:player-4a797b1263724a899cd5f4b12eab5c91:7c64ac92721241748cc0e7b3d117fa41:0 outside: indoors True at (-4.45, 0.02, 1.61), after leaving False at (5.40, 0.03, -5.94), target (5.50, 0.00, -6.50)
- UI route: dough onto the dock: pick up dough: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: dough onto the dock: drop into input: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- dock screen says 'No truck serves this dock; press L to set up a route.'
- 5 dough on the start dock's outgoing
- UI route: Buy truck: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Load at >: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Deliver to >: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Deliver to >: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Deliver to >: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Create route: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Truck route >: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI route: Assign: the virtual mouse press had no effect in 10 frames; a UI pointer event did; clicks armed True, element attached True, mouse left False, device enabled True, Game view focused False
- UI clicks so far: 20 taken from the virtual mouse, 13 needed a UI pointer event, 0 controls not pickable
- truck: docked at the start never, left loaded 0 s, 5/5 dough delivered after 3 s; state ToPickup
- S12 frame time over 79 frames: mean 32.4 ms, p95 51.2 ms, max 80.9 ms (Editor)
- sales at the unviewed second site so far 0; its register staffed by '', stocked 0
- capture s12-sites.png (overview)
- console since the last step: 0 errors, 0 warnings
- [Goods] commits=288 avg=7.7ms max=26.5ms payload=133.4KB

## S13 Restart the host mid-loop: pass
- staff button reads 'Work this register'
- Buy supplier-dough-5: accepted
- UI clicks so far: 24 taken from the virtual mouse, 13 needed a UI pointer event, 0 controls not pickable
- walk to buy:player-4a797b1263724a899cd5f4b12eab5c91:7c64ac92721241748cc0e7b3d117fa41:0 outside: indoors True at (-0.93, 0.03, -1.30), after leaving False at (5.40, 0.03, -5.94), target (5.50, 0.00, -6.50)
- dock screen says 'Truck 1: picks up here'
- before restart: revision 3009, clock 2745, register bread 9, staff 'player-4a797b1263724a899cd5f4b12eab5c91', customers here 0 (queued 0), truck ToDropoff on road True
- no customer was queued at the restart (none could be forced without test-only seeding)
- committed revision 3009 (live 3009); clock lag 0 s
- time to playable 1.8 s (Begin to avatar with live controls)
- after restart: revision 3011, clock 2759, client site site-restaurant-0098
- units committed belt:50 bread:9 dough:10 lift:10 | after belt:50 bread:9 dough:10 lift:10
- cash committed $957,719.00 | after $957,719.00
- equipment committed 9 | after 9; trucks 1 | 1; customers 129 | 126
- truck after restart ToPickup, on road True, leg road-0485
- register staff after restart '' (0035: cleared)
- resumed vs live at shutdown: same; vs last commit: same (customers may have bought in between)
- console since the last step: 0 errors, 0 warnings

