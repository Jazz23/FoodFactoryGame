# Conveyor corner kit

## Files

- `Conveyor_Corners.blend`: editable left/right corner modules, the original straight reference, and a connected preview scene.
- `Conveyor_Corner_Left_90.fbx`: left-turn module at its inlet origin.
- `Conveyor_Corner_Right_90.fbx`: right-turn module at its inlet origin.
- `Conveyor_Tread_BaseColor.png`: repeating tread/arrow texture matching the original procedural design. Referenced by the FBX belt material.
- `Conveyor_Corners_Preview.png`: connected assembly preview.

## Placement

Both modules receive items along Blender local +Y and turn 90 degrees. Rotate either module in 90-degree increments in Unity to cover all incoming directions. Left/right versions preserve forward-facing arrows without negative scales.

Dimensions are in metres: belt width 1.0, rail-to-rail width 1.2 (fasteners extend slightly beyond), belt height 0.8, centreline turn radius 2.0. The corner's inlet-to-outlet displacement is 2 m forward and 2 m sideways; **2 m describes the radius/connection spacing, not a 2 x 2 m bounding box**. Outer rail radius is 2.6 m.

The root pivot is the inlet floor centre, matching the original straight. `Left_Socket_IN` / `Right_Socket_IN` and the corresponding `Socket_OUT` empties mark floor-level attachment points. Align the next module's inlet position and travel orientation to the outlet. The carrying surface sits 0.8 m above the socket.

| Module | Blender inlet | Blender outlet | Exit travel |
| --- | --- | --- | --- |
| Right | (0, 0, 0) | (2, 2, 0) | +X |
| Left | (0, 0, 0) | (-2, 2, 0) | -X |

FBX uses Y-up / -Z-forward export conversion and metre units. Use the imported socket transforms for attachment rather than copying Blender coordinates directly into Unity. Keep scale at 1 and rotate around the vertical axis.

## Materials and motion

The Blender source retains the original animated procedural material. The FBX exports use an image-based belt material; assign/remap it to your project's render-pipeline shader and put `Conveyor_Tread_BaseColor.png` in its base-color slot. Set the texture Wrap Mode to **Repeat**, material tiling to (1, 1), metallic to 0.15 and smoothness to approximately 0.32. The amber/frame/hardware/rubber material slots remain separate.

UV U runs across the belt. UV V increases along travel, from 0 at the inlet to 1.5 at the outlet. There are six complete arrow repeats and 24 tread repeats around each corner. Both ends match the straight's repeating pattern phase. For motion, subtract a shared time offset from V; the original Blender material uses 0.5 UV units/second. Scrolling is a Unity material/shader responsibility, not an FBX animation. On this curved belt, equal angular travel necessarily has different surface speeds at inner/outer edges.

## Validation

Both variants were visually inspected joined to an unchanged straight at each end. Compared the inlet/outlet vertex profiles of both amber rails, both frame beams, and the belt bed against the straight: maximum mismatch was approximately 0.00000003 m. Belt faces point upward at height 0.8 m; root scales are (1, 1, 1). The corner curves use 64 segments. This verifies the Blender geometry; Unity import/runtime behavior was not tested.
