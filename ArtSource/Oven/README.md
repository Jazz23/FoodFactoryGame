# HEARTH / 04 — food-factory oven

Open `FF_Oven.blend` in Blender. Press **Space** over the 3D Viewport to play the cooking loop. Use **Rendered** shading to see the animated heater emission and chamber light; fan rotation also works in Solid and Material Preview shading.

## Asset

- Cream enamel and petrol-teal industrial oven, stainless trays, eighteen scored bread rolls, thermostat controls, safety cage, and exhaust.
- `OVEN | Model` contains the asset, parented to `FF_Oven` with its origin at floor level. Blender units are meters.
- `OVEN | Presentation` contains the separate floor, pedestal, camera, and studio lighting.
- `FF_Oven.png` is the rendered preview.
- `build_oven.py` is the generation source, intended for a fresh Blender scene.

## Cooking animation

- **24 fps; frames 1–96; four seconds.** Frame 97 is the matching endpoint and is intentionally outside the playback range, avoiding a duplicated frame at the seam.
- Two convection rotors turn four full revolutions per cycle; the exposed side rotor turns two in the opposite direction. Rotation curves are linear for constant speed.
- Heater emission and chamber lighting pulse twice per cycle with smooth, flat tangents at their extrema.
- All animation curves use Cycles extrapolation, so evaluation repeats beyond the timeline range as well.

## Verification

Checked all 171 model objects at frames 1, 97, and 193. Maximum world-transform difference at both repeated endpoints was `7.004e-7`. Heater strength matched at `3.0`, and chamber-light energy matched at `35.0`. Frame 13 confirmed non-static motion and a changed heater strength of `4.0`. The saved preview was visually inspected.

## Unity handoff

This is the Blender source asset; it has not been installed as a Unity prefab. Export only `OVEN | Model` for the game, excluding presentation objects. A Unity animation clip should be configured to loop. Blender shader-node and light-energy animation requires a Unity-side equivalent; it does not transfer as ordinary transform animation in FBX.
