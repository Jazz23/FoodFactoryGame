"""Build the cardboard box stacks a storage shelf shows as its slots fill (RT_StorageShelf_1_2m, 8 slots).

Run headless from the checkout root:
    "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python ArtSource/Restaurant/build_shelf_boxes.py

Exports Assets/Art/Restaurant/Models/Service/RT_StorageShelf_Boxes.fbx: eight mesh objects, Boxes_1 .. Boxes_8, one stack per
slot in fill order (bottom deck first, two stacks per deck). Every object keeps its origin at the shelf's origin, with its
vertices already standing on its deck, so the FBX drops onto the shelf model unmoved. Also saves StorageShelf_Boxes.blend and
StorageShelf_Boxes.png (the stacks on the imported shelf) next to this script. Deterministic: a fixed seed shapes the stacks.
"""
import bpy
import bmesh
import math
import random
from pathlib import Path
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parent
PROJECT = ROOT.parent.parent
OUT = PROJECT / 'Assets/Art/Restaurant/Models/Service/RT_StorageShelf_Boxes.fbx'
SHELF = PROJECT / 'Assets/Art/Restaurant/Models/Service/RT_StorageShelf_1_2m.fbx'

# The shelf (build_restaurant.py): posts at x +-0.56, y +-0.23 (radius 0.02); 4 cm decks centred at these heights.
DECKS = [.12, .66, 1.2, 1.78]
DECK_TOP = [z + .02 for z in DECKS]
# Free height above each deck: up to the next deck's underside less a finger's gap; the open top deck gets a modest stack.
HEADROOM = [DECKS[i + 1] - .02 - DECK_TOP[i] - .03 for i in range(3)] + [.32]
HALF_SPAN = .53   # inside the posts
DEPTH = .42       # inside the posts, front to back

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'


def linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4


def material(name, hexcode, rough):
    rgb = [linear(int(hexcode[i:i + 2], 16) / 255) for i in (1, 3, 5)]
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = m.diffuse_color
    bsdf.inputs['Roughness'].default_value = rough
    return m


# Cardboard and tape are new kit materials; the label reuses the kit's RT_Ivory so Unity maps it to the existing material.
MATS = [material('RT_Cardboard', '#b8895a', .88), material('RT_Tape', '#9c6c3f', .45), material('RT_Ivory', '#f5efdc', .4)]
CARD, TAPE, LABEL = range(3)


def add_box(bm, layer_mats, center, size, yaw, mat):
    """An axis box of size (x, y, z) standing on center.z, turned yaw radians about Z."""
    geom = bmesh.ops.create_cube(bm, size=1.0)
    verts = geom['verts']
    m = Matrix.Translation(Vector(center) + Vector((0, 0, size[2] / 2))) @ Matrix.Rotation(yaw, 4, 'Z') @ Matrix.Diagonal((*size, 1))
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    faces = {f for v in verts for f in v.link_faces}
    for f in faces:
        f.material_index = mat


def carton(bm, rng, x, y, z, w, d, h, yaw):
    """One taped carton with a label on its front (-Y, Unity +Z): returns its top height."""
    add_box(bm, MATS, (x, y, z), (w, d, h), yaw, CARD)
    rot = Matrix.Rotation(yaw, 3, 'Z')
    # Tape over the top seam and down the front and back.
    add_box(bm, MATS, Vector((x, y, z)) + rot @ Vector((0, 0, h)), (.055, d + .004, .003), yaw, TAPE)
    for side in (-1, 1):
        add_box(bm, MATS, Vector((x, y, z)) + rot @ Vector((0, side * (d / 2 + .0015), h * .62)), (.055, .003, h * .38), yaw, TAPE)
    # A shipping label on the front, off centre.
    lw, lh = min(.12, w * .35), min(.08, h * .4)
    lx = rng.choice((-1, 1)) * (w / 2 - lw / 2 - .03)
    add_box(bm, MATS, Vector((x, y, z)) + rot @ Vector((lx, -(d / 2 + .0015), h * .25)), (lw, .003, lh), yaw, LABEL)
    return z + h


def stack(rng, deck, side):
    """A stack of one to three cartons in one half of a deck, largest at the bottom, within that deck's headroom."""
    bm = bmesh.new()
    floor = DECK_TOP[deck]
    room = HEADROOM[deck]
    cx = side * HALF_SPAN / 2
    z = floor
    w = rng.uniform(.40, .47)
    d = rng.uniform(.32, DEPTH - .02)
    count = 1 if deck == 3 else rng.choice((2, 2, 3))
    for level in range(count):
        h = rng.uniform(.15, .22) if level == 0 else rng.uniform(.11, .17)
        if z + h > floor + room:
            break
        yaw = 0 if level == 0 else math.radians(rng.uniform(-9, 9))
        x = cx + rng.uniform(-.02, .02)
        y = rng.uniform(-.015, .015) + (DEPTH - d) / 2 * rng.uniform(-.6, .6)
        z = carton(bm, rng, x, y, z, w, d, h, yaw)
        w *= rng.uniform(.72, .9)
        d *= rng.uniform(.75, .92)
    return bm


rng = random.Random(20261012)
objects = []
for slot in range(8):
    deck, side = divmod(slot, 2)
    bm = stack(rng, deck, -1 if side == 0 else 1)
    bmesh.ops.bevel(bm, geom=[e for e in bm.edges], offset=.0025, segments=1, affect='EDGES', clamp_overlap=True)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    mesh = bpy.data.meshes.new(f'Boxes_{slot + 1}')
    bm.to_mesh(mesh)
    bm.free()
    for m in MATS:
        mesh.materials.append(m)
    uv = mesh.uv_layers.new(name='UVMap')
    for poly in mesh.polygons:
        major = max(range(3), key=lambda i: abs(poly.normal[i]))
        axes = [i for i in range(3) if i != major]
        for li in poly.loop_indices:
            co = mesh.vertices[mesh.loops[li].vertex_index].co
            uv.data[li].uv = (co[axes[0]], co[axes[1]])
    obj = bpy.data.objects.new(mesh.name, mesh)
    scene.collection.objects.link(obj)
    objects.append(obj)

for obj in bpy.context.selected_objects:
    obj.select_set(False)
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
OUT.parent.mkdir(parents=True, exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(OUT), use_selection=True, object_types={'MESH'}, axis_forward='-Z', axis_up='Y', global_scale=1,
                         apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, use_mesh_modifiers=True,
                         mesh_smooth_type='FACE', add_leaf_bones=False, bake_anim=False, path_mode='AUTO')

# Preview: the stacks on the shelf model, three-quarter front view.
bpy.ops.import_scene.fbx(filepath=str(SHELF))
camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
scene.collection.objects.link(camera)
scene.camera = camera
target = Vector((0, 0, .95))
camera.location = target + Vector((1.9, -3.0, .9))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.lens = 40
light = bpy.data.objects.new('Key', bpy.data.lights.new('Key', 'AREA'))
light.data.energy = 600
light.data.size = 4
light.location = (2, -3, 4)
light.rotation_euler = (Vector((0, 0, 1)) - light.location).to_track_quat('-Z', 'Y').to_euler()
scene.collection.objects.link(light)
scene.world = bpy.data.worlds.new('Studio')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value = (.35, .35, .35, 1)
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 900
scene.render.resolution_y = 1100
scene.render.image_settings.file_format = 'PNG'
scene.render.filepath = str(ROOT / 'StorageShelf_Boxes.png')
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'StorageShelf_Boxes.blend'))
print('SHELF BOXES', OUT, sum(len(o.data.polygons) for o in objects), 'triangles',
      [round(max(v.co.z for v in o.data.vertices), 3) for o in objects])
