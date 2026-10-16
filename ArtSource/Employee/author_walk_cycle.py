"""Re-authors the employee's Walk and CarryWalk leg motion so the feet plant (no ice skating), then re-exports Employee.fbx.

The original cycles swung the legs like pendulums: the front foot held still for a third of the cycle and the stance foot
swept back faster than the body moved, so no playback rate made the feet stick. Here each leg is solved with two-bone IK
from a target ankle path: on the ground the ankle moves backward at exactly CLIP_SPEED (so, played at that ground speed,
the planted foot is still in the world) with the sole flat; in the air it swings forward on a Hermite arc whose end
velocities match the stance, so the foot lifts off and touches down without a world-space slide. Arms, spine and head
keep their authored curves. Only the leg bones and the root bob are rewritten.

EmployeeWorker.walkClipSpeed (Unity, metres per second) must equal CLIP_SPEED: it scales playback to measured speed.

Run headless from the project root:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b ArtSource/Employee/Employee_Asset.blend \
      --python ArtSource/Employee/author_walk_cycle.py
It saves the .blend and writes Assets/Art/Models/Employee/Employee.fbx with the settings the asset was first exported with
(global scale 0.43, so one Blender unit is 0.43 m in Unity). Idempotent.
"""
import math
import os

import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.normpath(os.path.join(ROOT, '..', '..'))
# EMPLOYEE_FBX redirects the export (for a trial run on a copy of the .blend).
FBX_PATH = os.environ.get('EMPLOYEE_FBX') or os.path.join(PROJECT, 'Assets', 'Art', 'Models', 'Employee', 'Employee.fbx')

UNITY_SCALE = 0.43          # Blender unit -> Unity metres (export global_scale)
CLIP_SPEED = 1.0            # Unity m/s at which the cycle, played at 1x, plants its feet
FRAMES = 24                 # one full cycle (frames 1..25, 25 == 1), 24 fps
FPS = 24
STANCE = 0.6                # fraction of the cycle a foot is on the ground
CENTRE = 0.05               # ankle path centre, slightly behind the hip (Blender units, +Y is backward)
LIFT = 0.28                 # swing apex above the ground (Blender units)
HIP_DROP = 0.17             # mean hip lowering, so the knees stay soft at heel strike
BOB = 0.05                  # hip rise at mid-stance, twice per cycle
LEGS = (('L', 0.0), ('R', 0.5))
ACTIONS = ('Employee_Walk', 'Employee_CarryWalk')


def hermite(p0, p1, m0, m1, u):
    u2, u3 = u * u, u * u * u
    return (2 * u3 - 3 * u2 + 1) * p0 + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * p1 + (u3 - u2) * m1


def ankle_target(phase, ground, step):
    """(y, z, foot pitch) of the ankle in armature space for a leg at this phase; +Y is backward (the rig faces -Y)."""
    front, back = CENTRE - step * 0.5, CENTRE + step * 0.5
    if phase < STANCE:
        return front + step * phase / STANCE, ground, 0.0
    u = (phase - STANCE) / (1.0 - STANCE)
    tangent = step * (1.0 - STANCE) / STANCE  # the stance velocity, so lift-off and touch-down do not slide
    y = hermite(back, front, tangent, tangent, u)
    z = ground + LIFT * math.sin(math.pi * u) ** 1.5
    pitch = -0.35 * math.sin(math.pi * u)  # toe up a little through the swing
    return y, z, pitch


def solve(hip, ankle, l1, l2):
    """Absolute thigh angle and relative knee bend, rotating about +X from straight down; the knee bends forward (-Y)."""
    dy, dz = ankle[0] - hip[0], ankle[1] - hip[1]
    d = min(math.hypot(dy, dz), (l1 + l2) * 0.999)
    phi = math.atan2(dy, -dz)
    alpha = math.acos(max(-1.0, min(1.0, (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d))))
    beta = math.acos(max(-1.0, min(1.0, (l2 * l2 + d * d - l1 * l1) / (2 * l2 * d))))
    thigh = phi - alpha
    return thigh, alpha + beta


def main():
    arm = bpy.data.objects['Employee_Armature']
    bones = arm.data.bones
    root = bones['root']
    # Bone-local direction of world up for the root bob, and each leg bone's rotation sign about world +X.
    up_local = root.matrix_local.to_3x3().inverted() @ Vector((0.0, 0.0, 1.0))
    step = CLIP_SPEED / UNITY_SCALE * STANCE * FRAMES / FPS
    if arm.animation_data is None:
        arm.animation_data_create()
    previous = arm.animation_data.action
    for name in ACTIONS:
        action = bpy.data.actions[name]
        arm.animation_data.action = action
        if hasattr(arm.animation_data, 'action_slot') and len(action.slots):
            arm.animation_data.action_slot = action.slots[0]
        for frame in range(1, FRAMES + 2):
            t = ((frame - 1) % FRAMES) / FRAMES
            bob = -HIP_DROP + BOB * math.cos(4.0 * math.pi * (t - STANCE * 0.5))
            pose_root = arm.pose.bones['root']
            pose_root.rotation_mode = 'XYZ'
            pose_root.location = up_local * bob
            pose_root.keyframe_insert('location', frame=frame, group='root')
            for side, offset in LEGS:
                thigh, shin, foot = bones['thigh.' + side], bones['shin.' + side], bones['foot.' + side]
                l1 = (thigh.tail_local - thigh.head_local).length
                l2 = (shin.tail_local - shin.head_local).length
                ground = shin.tail_local.z
                hip = (thigh.head_local.y, thigh.head_local.z + bob)
                y, z, pitch = ankle_target((t + offset) % 1.0, ground, step)
                a, b = solve(hip, (y, z), l1, l2)
                for bone, angle in ((thigh, a), (shin, b), (foot, pitch - a - b)):
                    pose = arm.pose.bones[bone.name]
                    pose.rotation_mode = 'XYZ'
                    sign = 1.0 if bone.matrix_local.col[0].x >= 0 else -1.0
                    pose.rotation_euler = (sign * angle, 0.0, 0.0)
                    pose.keyframe_insert('rotation_euler', frame=frame, group=bone.name)
        action.use_frame_range = True
        action.frame_range = (1, FRAMES)
    verify(arm, step)
    # The FBX's default (non-animated) pose is the pose at export time: the first Walk frame, as originally exported.
    arm.animation_data.action = previous
    bpy.context.scene.frame_set(1)
    bpy.ops.wm.save_mainfile()
    bpy.ops.export_scene.fbx(filepath=FBX_PATH, use_selection=False, object_types={'ARMATURE', 'MESH'},
                             add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True,
                             bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True, global_scale=UNITY_SCALE)
    print('[walk] exported', FBX_PATH, 'clip speed', CLIP_SPEED, 'm/s')


def forward_kinematics(arm, name):
    """Armature-space matrix of a bone from the keyed local transforms (headless frame_set does not refresh pose heads)."""
    bone = arm.data.bones[name]
    basis = arm.pose.bones[name].matrix_basis
    if bone.parent is None:
        return bone.matrix_local @ basis
    return forward_kinematics(arm, bone.parent.name) @ bone.parent.matrix_local.inverted() @ bone.matrix_local @ basis


def verify(arm, step):
    """Fails unless each planted ankle stays put in the world when the body moves forward at the clip speed."""
    scene = bpy.context.scene
    speed = step / (STANCE * FRAMES)  # Blender units per frame
    for name in ACTIONS:
        arm.animation_data.action = bpy.data.actions[name]
        worst = 0.0
        for side, offset in LEGS:
            planted = []
            for frame in range(1, FRAMES + 1):
                t = ((frame - 1) % FRAMES) / FRAMES
                if (t + offset) % 1.0 >= STANCE - 1e-6:
                    continue
                scene.frame_set(frame)
                ankle = forward_kinematics(arm, 'foot.' + side).translation
                planted.append((frame, ankle.y - speed * (frame - 1), ankle.z))
            ys = [p[1] for p in planted]
            zs = [p[2] for p in planted]
            # The stance wraps the cycle end for R; compare within one contiguous run per leg.
            runs = [planted[i][1] - planted[i - 1][1] for i in range(1, len(planted)) if planted[i][0] == planted[i - 1][0] + 1]
            worst = max(worst, max((abs(r) for r in runs), default=0.0), max(zs) - min(zs))
        print(f'[walk] {name}: worst planted-foot drift per frame {worst:.4f} units')
        if worst > 0.01:
            raise RuntimeError(f'{name}: planted foot slides ({worst:.4f} units per frame)')
        arm.animation_data.action = None


main()
