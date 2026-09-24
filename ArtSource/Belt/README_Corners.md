# Conveyor corner kit

## Files

- `Conveyor_Corners.blend`: editable left/right corner modules, the original straight reference, and a connected preview scene.
- `Conveyor_Corner_Left_90.fbx`: left-turn module at its inlet origin.
- `Conveyor_Corner_Right_90.fbx`: right-turn module at its inlet origin.
- `Conveyor_Tread_BaseColor.png`: repeating tread/arrow texture matching the original procedural design. Referenced by the FBX belt material.
- `Conveyor_Corners_Preview.png`: connected assembly preview.

## Placement

Both modules receive items along Blender local +Y and turn 90 degrees. Rotate either module in 90-degree increments in Unity to cover all incoming directions. Left/right versions preserve forward-facing arrows without negative scales.

Dimensions are in metres: belt width 1.0, rail-to-rail width 1.2 at the inlet/outlet (fasteners extend slightly beyond), belt height 0.8, centreline turn radius 0.5. The belt surface fills exactly a 1 x 1 m tile: the belt pivots about the tile's inner corner, so the belt's inner edge has zero radius. Inlet-to-outlet displacement is 0.5 m forward and 0.5 m sideways (edge centre to edge centre). There is no inner rail or inner frame beam on the curve; the straight's inner rail simply ends at the joint. The outer rail radius is 1.1 m, so the outer rail/beam overhang the tile by 0.1 m, just as the straight's rails overhang its 1 m belt width. Supports: two legs under the outer edge and one leg near the pivot.

The root pivot is the inlet floor centre, matching the original straight. `Left_Socket_IN` / `Right_Socket_IN` and the corresponding `Socket_OUT` empties mark floor-level attachment points. Align the next module's inlet position and travel orientation to the outlet. The carrying surface sits 0.8 m above the socket.

| Module | Blender inlet | Blender outlet | Exit travel |
| --- | --- | --- | --- |
| Right | (0, 0, 0) | (0.5, 0.5, 0) | +X |
| Left | (0, 0, 0) | (-0.5, 0.5, 0) | -X |

FBX uses Y-up / -Z-forward export conversion and metre units. Use the imported socket transforms for attachment rather than copying Blender coordinates directly into Unity. Keep scale at 1 and rotate around the vertical axis.

## Materials and motion

The Blender source retains the original animated procedural material. The FBX exports use an image-based belt material; assign/remap it to your project's render-pipeline shader and put `Conveyor_Tread_BaseColor.png` in its base-color slot. Set the texture Wrap Mode to **Repeat**, material tiling to (1, 1), metallic to 0.15 and smoothness to approximately 0.32. The amber/frame/hardware/rubber material slots remain separate.

UV U runs across the belt. UV V increases along travel, from 0 at the inlet to 0.5 at the outlet. There are two complete arrow repeats and 8 tread repeats around each corner; the texture pinches toward the pivot because the inner edge has zero radius. Both ends match the straight's repeating pattern phase. For motion, subtract a shared time offset from V; the original Blender material uses 0.5 UV units/second. Scrolling is a Unity material/shader responsibility, not an FBX animation. On this curved belt, equal angular travel necessarily has different surface speeds at inner/outer edges.

## Validation

The 1 x 1 m version was derived from the original 2 m-radius corner by shifting every swept profile 1.5 m toward the turn centre, which keeps the belt, bed, outer rail and outer beam cross-sections identical to the straight at both ends. Both variants were visually inspected joined to an unchanged straight at each end. The re-exported FBX files were re-imported into Blender: the belt spans x -0.5..0.5 and y 0..1 in Blender coordinates, and `Socket_OUT` is at (±0.5, 0.5, 0). Belt faces point upward at height 0.8 m; root scales are (1, 1, 1). The corner curves use 64 segments. This verifies the Blender geometry; Unity import/runtime behavior was not tested.
