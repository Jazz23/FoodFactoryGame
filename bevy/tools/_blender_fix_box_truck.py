# Removes the box truck's z-fighting in models/Vehicles/Models/Vehicles.glb: the chassis rail shared its bottom, front and back
# faces with the cargo box, the upper cab's floor lay on the lower cab's roof, and the windshield and side-glass boxes shared a
# face. The rail is pulled inside the box's sides, below its floor and out past its front; the hidden duplicate faces are
# deleted. Run: blender -b --python tools/_blender_fix_box_truck.py -- <Vehicles.glb>
import sys

import bmesh
import bpy

path = sys.argv[-1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=path)
truck = bpy.data.objects["Vehicle_BoxTruck"]
mesh = truck.data
bm = bmesh.new()
bm.from_mesh(mesh)
bm.faces.ensure_lookup_table()
mat = {i: s.material.name for i, s in enumerate(truck.material_slots) if s.material}
EPS = 1e-3


def islands():
    seen, out = set(), []
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, isl = [f], []
        seen.add(f.index)
        while stack:
            g = stack.pop()
            isl.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        out.append(isl)
    return out


def bounds(isl):
    vs = [v.co for f in isl for v in f.verts]
    return [min(v[k] for v in vs) for k in range(3)], [max(v[k] for v in vs) for k in range(3)]


def near(a, b):
    return abs(a - b) < EPS


# Axes as the mesh stores them: x across, y up, z along (front +z).
chassis, doomed = [], []
for isl in islands():
    lo, hi = bounds(isl)
    names = {mat.get(f.material_index) for f in isl}
    if names == {"VH_Body"} and near(lo[1], 0.85) and near(hi[1], 1.1) and near(lo[2], -3.75) and near(hi[2], 1.5):
        chassis.extend(isl)
    # The upper cab's floor (flat at y 1.55 between the cab's frame strips).
    elif names == {"VH_Body"} and near(lo[1], 1.55) and near(hi[1], 1.55) and hi[0] - lo[0] > 2.0 and hi[2] < 3.2:
        doomed.extend(isl)
    # The glass boxes' shared face at z 2.85 (the windshield's back and the side glass's front).
    elif names == {"VH_Glass"} and near(lo[2], 2.85) and near(hi[2], 2.85):
        doomed.extend(isl)

moved = set()
for f in chassis:
    for v in f.verts:
        if v in moved:
            continue
        moved.add(v)
        x, y, z = v.co
        v.co.x = 1.18 if x > 0 else -1.18
        v.co.y = 0.80 if near(y, 0.85) else 1.05
        v.co.z = 1.56 if near(z, 1.5) else -3.68
bmesh.ops.delete(bm, geom=list({f for f in doomed}), context="FACES_ONLY")
bm.to_mesh(mesh)
bm.free()
print(f"[fix_box_truck] moved {len(moved)} chassis verts, deleted {len(doomed)} hidden faces")
assert len(moved) == 8 and len(doomed) == 6, "the box truck no longer matches the faces this fix expects"
bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_apply=False)
