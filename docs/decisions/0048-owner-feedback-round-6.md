# 0048 - Owner Feedback Round 6: Camera, Trucks, Traffic, Phone Apps, Machine Screens

Date: 2026-10-13

Status: **implemented; not compiled, tested or captured.** The owner asked for no tests or checks in this round, so
none of the evidence AGENTS.md requires has been gathered yet. The owner's requests are confirmed requirements. Values
marked PROTOTYPE stay open.

## Owner requests (confirmed, 2026-10-13)

1. Entering a building no longer switches the camera to the top-down view.
2. The crosshair sits too low.
3. Trucks cannot unload after a farm purchase.
4. Idle trucks park in the service yard beside their restaurant.
5. The underside of a competing restaurant's awning is invisible.
6. City cars drive through the player; a car stops for the player or a customer in front of it.
7. Customers walk in the road instead of on the sidewalk.
8. The ledger is opened from the phone.
9. Belts are in the build menu.
10. The hotbar starts empty, with no preset machines, items or filters.
11. Machine screens (the stove) show no description text: no "Off: switch it on", "Idle: put ... in the input" or
    "Automatic: makes the first of N recipes".
12. Registers show no text and no price list: a 5 x 5 grid of slots and a short "Staffed by nobody, customers are not served".
13. The phone gets a Recipes app listing every recipe, with each machine's time.
14. Machine progress bars glitch when power is switched and are laggy.
15. The recipe dropdown looks better than the default white menu.
16. Machines have no power switch, on their screens or in the employee menu: a machine processes as soon as its inputs are in.
17. A restaurant's awning stays when the player buys it.

## Causes and changes

- **Camera (1).** `OrbitCameraRig.SetIndoors` only keeps the ceiling limit; the top-down view is build mode's alone.
- **Crosshair (2).** The crosshair and every aim ray sit at 60% of the screen height
  (`EquipmentInteraction.CrosshairViewportY`, `CrosshairScreenPoint`; `Hud.uss` `top: 40%`), so the third-person view aims
  further ahead. PROTOTYPE height.
- **Farm unloading (3).** Evidence from the owner's `world-4` save (read from a copy): the supplier delivery to
  `site-restaurant-0107` was ordered at revision 1100 and unloaded at once after the owner deleted the truck route at revision
  4186, roughly 2,800 game seconds later. A route truck waiting at a restaurant dock with nothing to load kept `Docked`, so
  `TakeDock` refused every other truck forever. A truck whose load or unload second does nothing now lets go of the dock
  (`MoveTrucks`); it takes the dock again when it is free.
- **Yard parking (4).** Presentation only (`TruckPresenter.YardPose`): parked trucks at a restaurant with a service yard stand
  in it, lengthwise and nose to the street, on the side away from the shell so the dock stays clear, one behind another. A
  truck the yard has no room for, or one at a site without a yard, stands at the kerb as before. Simulation positions are
  unchanged.
- **Awnings (5, 17).** `WG_Awning` renders both faces. The awning submesh is split from each restaurant model into its own
  object (`WorldLayoutPresenter.SplitAwning`), so hiding the model for a drawn site's shell leaves the awning up. Open: the
  map awning was placed for the map model, and how it meets a resized shell has not been checked.
- **Cars (6).** `CityCars.Compute` takes the map points of people on foot (`Pedestrians`, registered by `PlayerAvatar` and
  `CustomerPresenter`). Cars in a lane a person stands in stop 4.5 m short of them (PROTOTYPE). Cars are still a pure
  function of the clock, so a car let go jumps ahead to its place in the stream. `CityTrafficPresenter` now eases it there
  at no more than 35 m/s (it snaps only beyond 200 m). Cars still have no colliders.
- **Sidewalk (7).** A generated lot ends at its road's edge. Customer figures walked `SiteStreet.Band / 2` (3 m) out from it,
  which is in the kerb lane. They now walk `SiteStreet.Sidewalk` (0.6 m) out, inside the 1 m kerb margin. The street NavMesh
  band is unchanged.
- **Ledger (8).** The Ledger tab beside the Supplier is gone; the phone's Ledger app lists the same entries.
- **Belts (9).** Build mode's catalog ends with a Belts section. Its card leaves build mode with the inventory's belts on the
  cursor (`EquipmentInteraction.LayBelts`), or buys a pack first and puts the belts on the cursor when they arrive.
- **Hotbar (10).** The hotbar is no longer seeded from equipment content.
- **Machine and register screens (11, 12, 14, 15).** `HudMachineWindow` has no subtitle, progress line, price list,
  register readiness line or power switch. A register's input grid is 5 columns and `Counter.asset` has 25 input slots
  (applied to placed registers at start by `ApplyEquipmentCapacitiesDurably`). The staff line reads "Staffed by nobody,
  customers are not served". The progress bar eases toward the batch's progress every frame and moves back only when a new
  batch starts. The recipe chooser lists outputs ("Automatic", "2 x Pancake"). `Theme.uss` styles the dropdown field and
  Unity's popup menu (`.unity-base-dropdown`) in the dark theme with amber hover.
- **Power (16).** `manualPower` is off on the oven, stove, fryer, grill and mixer, so the server registers no manually
  powered kind and every station runs whenever its inputs are in. The machine screen has no switch. The employee task list
  no longer offers "Turn machine on" (a saved power task still shows). The power fields, requests and schema stay for old
  saves. `PoweredOn` no longer affects anything.
- **Recipes (13).** The phone's Recipes app lists each machine's recipes (inputs, output, time), then the menu items sold at
  the register (price and serving time), from the session's recipe content.

## Open

- Verification: compile, EditMode and PlayMode runs, a farm delivery with a waiting route truck, a two-client traffic check
  and running-game captures of every screen above.
- Tests that use the HUD ledger tab, the power switch, seeded hotbar slots, the old street band or the screen-centre aim will
  need updating.
