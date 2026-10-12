# Builds the kitchen stations of decision 0043 (mixer, stove, fryer, grill and kitchen counter) with bmesh into Kitchen.blend
# and exports Export/Kitchen.fbx, one object per station, plus Kitchen_Preview.png, a workbench render of the line-up.
# Low-poly and flat-coloured like ArtSource/Vehicles: bevelled boxes and few-sided cylinders, colour from a handful of KT_*
# materials that map one-to-one to Unity materials (AgentScripts/BuildKitchenContent.cs).
#
# Conventions (as ArtSource/Vehicles): metres, Z up, the station's working front faces -Y (imports as Unity +Z), width along X,
# pivot on the floor at the centre of the footprint. Footprints are whole 1 m cells: mixer and fryer 1 x 1, stove, grill and
# counter 2 x 1. Run in Blender 5.2; the script empties the open file first. Headless, from the project root:
#
#   blender -b --factory-startup --python ArtSource/Kitchen/build_kitchen_models.py
import math
import os

import bmesh
import bpy
from mathutils import Matrix

ROOT = globals().get('KITCHEN_ART_ROOT', os.path.dirname(os.path.abspath(__file__)))

# name: (base colour, roughness, metallic, emission strength)
MATERIALS = {
    'KT_Steel': ((0.62, 0.65, 0.68), 0.35, 0.85, 0.0),
    'KT_DarkSteel': ((0.16, 0.17, 0.19), 0.45, 0.7, 0.0),
    'KT_Black': ((0.03, 0.03, 0.035), 0.7, 0.1, 0.0),
    'KT_Enamel': ((0.05, 0.36, 0.38), 0.35, 0.3, 0.0),
    'KT_Cream': ((0.86, 0.80, 0.64), 0.4, 0.1, 0.0),
    'KT_Accent': ((0.93, 0.32, 0.07), 0.35, 0.2, 0.0),
    'KT_Wood': ((0.45, 0.28, 0.15), 0.7, 0.0, 0.0),
    'KT_Board': ((0.80, 0.62, 0.40), 0.75, 0.0, 0.0),
    'KT_Glass': ((0.10, 0.14, 0.17), 0.1, 0.5, 0.0),
    'KT_Flame': ((1.0, 0.42, 0.05), 0.3, 0.0, 3.0),
    'KT_Oil': ((0.92, 0.66, 0.12), 0.15, 0.0, 0.4),
    'KT_Food': ((0.55, 0.30, 0.13), 0.8, 0.0, 0.0),
    'KT_Tomato': ((0.80, 0.12, 0.08), 0.4, 0.0, 0.0),
    'KT_Leaf': ((0.25, 0.60, 0.18), 0.6, 0.0, 0.0),
}


def fresh():
    for collection in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for block in list(collection):
            collection.remove(block)


def material(name):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    colour, roughness, metallic, emission = MATERIALS[name]
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = metallic
    if emission > 0:
        bsdf.inputs['Emission Color'].default_value = (*colour, 1.0)
        bsdf.inputs['Emission Strength'].default_value = emission
    m.diffuse_color = (*colour, 1.0)
    return m


class Builder:
    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.slots = []

    def slot(self, material_name):
        if material_name not in self.slots:
            self.slots.append(material_name)
        return self.slots.index(material_name)

    def _paint(self, verts, material_name):
        index = self.slot(material_name)
        faces = list({f for v in verts for f in v.link_faces})
        for f in faces:
            f.material_index = index
        return faces

    # A box from (x0, y0, z0) to (x1, y1, z1), optionally bevelled.
    def box(self, material_name, x0, y0, z0, x1, y1, z1, bevel=0.0):
        made = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = made['verts']
        for v in verts:
            v.co = (x0 if v.co.x < 0 else x1, y0 if v.co.y < 0 else y1, z0 if v.co.z < 0 else z1)
        self._paint(verts, material_name)
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            result = bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=1, affect='EDGES', profile=0.5)
            for f in result.get('faces', []):
                f.material_index = self.slot(material_name)

    # An upright cylinder (or cone, with a different top radius) standing on z0, centred on (x, y).
    def cylinder(self, material_name, x, y, z0, height, radius, top=None, segments=12, axis='Z'):
        made = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=segments, radius1=radius,
                                     radius2=radius if top is None else top, depth=height)
        verts = made['verts']
        if axis == 'X':
            bmesh.ops.rotate(self.bm, verts=verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'Y'))
            bmesh.ops.translate(self.bm, verts=verts, vec=(x, y, z0))
        elif axis == 'Y':
            bmesh.ops.rotate(self.bm, verts=verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.pi / 2, 3, 'X'))
            bmesh.ops.translate(self.bm, verts=verts, vec=(x, y, z0))
        else:
            bmesh.ops.translate(self.bm, verts=verts, vec=(x, y, z0 + height / 2))
        self._paint(verts, material_name)

    def knobs(self, x0, x1, y, z, count, material_name='KT_Black'):
        for i in range(count):
            x = x0 + (x1 - x0) * (i + 0.5) / count
            self.cylinder(material_name, x, y - 0.02, z, 0.04, 0.035, segments=8, axis='Y')

    def finish(self):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        for name in self.slots:
            mesh.materials.append(material(name))
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        return obj


# Mixer, 1 x 1: a stand cabinet with a teal planetary mixer on top (column at the back, head over a steel bowl).
def mixer(name):
    b = Builder(name)
    b.box('KT_Steel', -0.42, -0.38, 0.0, 0.42, 0.38, 0.06)
    b.box('KT_DarkSteel', -0.4, -0.36, 0.06, 0.4, 0.36, 0.78, bevel=0.02)
    b.box('KT_Steel', -0.38, -0.37, 0.12, 0.38, -0.35, 0.72)
    b.box('KT_Black', -0.12, -0.38, 0.58, 0.12, -0.36, 0.62)
    b.box('KT_Steel', -0.44, -0.4, 0.78, 0.44, 0.4, 0.82, bevel=0.01)
    # Base, column and head.
    b.box('KT_Enamel', -0.22, -0.05, 0.82, 0.22, 0.3, 0.9, bevel=0.03)
    b.box('KT_Enamel', -0.12, 0.12, 0.9, 0.12, 0.3, 1.45, bevel=0.03)
    b.box('KT_Enamel', -0.14, -0.22, 1.32, 0.14, 0.3, 1.52, bevel=0.04)
    b.box('KT_Accent', -0.145, 0.0, 1.4, 0.145, 0.18, 1.44)
    b.cylinder('KT_DarkSteel', 0.0, -0.08, 1.24, 0.08, 0.05, segments=8)
    # Bowl with dough, and the beater down into it.
    b.cylinder('KT_Steel', 0.0, -0.08, 0.9, 0.32, 0.17, top=0.24, segments=14)
    b.cylinder('KT_Cream', 0.0, -0.08, 1.12, 0.06, 0.2, segments=14)
    b.cylinder('KT_Steel', 0.0, -0.08, 1.0, 0.24, 0.025, segments=6)
    # Speed lever and a flour sack beside it.
    b.box('KT_Black', 0.14, 0.05, 1.38, 0.2, 0.09, 1.46)
    b.box('KT_Cream', 0.24, 0.05, 0.82, 0.4, 0.3, 1.08, bevel=0.03)
    return b.finish()


# Stove, 2 x 1: a range with four burners (two with pots), an oven door with a window and a row of knobs.
def stove(name):
    b = Builder(name)
    b.box('KT_Black', -0.94, -0.38, 0.0, 0.94, 0.4, 0.08)
    b.box('KT_Steel', -0.96, -0.42, 0.08, 0.96, 0.42, 0.88, bevel=0.02)
    b.box('KT_DarkSteel', -0.96, -0.42, 0.88, 0.96, 0.42, 0.92)
    # Oven doors and windows.
    for x in (-0.48, 0.48):
        b.box('KT_DarkSteel', x - 0.42, -0.44, 0.16, x + 0.42, -0.42, 0.64)
        b.box('KT_Glass', x - 0.3, -0.45, 0.28, x + 0.3, -0.44, 0.56)
        b.box('KT_Steel', x - 0.3, -0.48, 0.6, x + 0.3, -0.45, 0.62)
    b.box('KT_DarkSteel', -0.96, -0.44, 0.7, 0.96, -0.42, 0.84)
    b.knobs(-0.9, 0.9, -0.44, 0.77, 6, 'KT_Accent')
    # Burners: grates, rings of flame.
    burners = [(-0.55, -0.15), (-0.55, 0.2), (0.45, -0.15), (0.45, 0.2)]
    for x, y in burners:
        b.cylinder('KT_Black', x, y, 0.92, 0.03, 0.17, segments=12)
        b.cylinder('KT_Flame', x, y, 0.95, 0.01, 0.09, segments=10)
    # A pot of sauce and a pan.
    b.cylinder('KT_Steel', -0.55, -0.15, 0.95, 0.24, 0.16, segments=14)
    b.cylinder('KT_Tomato', -0.55, -0.15, 1.17, 0.03, 0.145, segments=14)
    b.cylinder('KT_DarkSteel', 0.45, 0.2, 0.95, 0.06, 0.18, segments=14)
    b.box('KT_Black', 0.6, 0.18, 0.98, 0.85, 0.22, 1.0)
    # Backsplash and shelf.
    b.box('KT_Steel', -0.96, 0.36, 0.92, 0.96, 0.42, 1.55)
    b.box('KT_DarkSteel', -0.9, 0.18, 1.45, 0.9, 0.42, 1.49)
    return b.finish()


# Fryer, 1 x 1: a steel cabinet with two golden oil wells, two baskets on hooks and a control panel.
def fryer(name):
    b = Builder(name)
    b.box('KT_Black', -0.38, -0.36, 0.0, 0.38, 0.36, 0.08)
    b.box('KT_Steel', -0.42, -0.4, 0.08, 0.42, 0.4, 0.92, bevel=0.02)
    b.box('KT_DarkSteel', -0.4, -0.42, 0.14, 0.4, -0.4, 0.62)
    b.box('KT_Steel', -0.08, -0.45, 0.4, 0.08, -0.42, 0.44)
    b.box('KT_DarkSteel', -0.42, -0.42, 0.66, 0.42, -0.4, 0.86)
    b.knobs(-0.36, 0.36, -0.42, 0.76, 4, 'KT_Accent')
    for x in (-0.2, 0.2):
        b.box('KT_DarkSteel', x - 0.17, -0.3, 0.88, x + 0.17, 0.22, 0.93)
        b.box('KT_Oil', x - 0.15, -0.28, 0.9, x + 0.15, 0.2, 0.935)
    # Baskets: one down in the oil, one hanging up to drain, each with a handle forward.
    b.box('KT_DarkSteel', -0.33, -0.22, 0.86, -0.07, 0.12, 0.97)
    b.box('KT_Food', -0.31, -0.2, 0.94, -0.09, 0.1, 0.975)
    b.box('KT_Black', -0.22, -0.42, 0.95, -0.18, -0.22, 0.98)
    b.box('KT_DarkSteel', 0.07, -0.02, 1.08, 0.33, 0.14, 1.28)
    b.box('KT_Food', 0.09, 0.0, 1.1, 0.31, 0.12, 1.2)
    b.box('KT_Black', 0.18, -0.2, 1.24, 0.22, -0.02, 1.27)
    # Back panel with the basket hooks.
    b.box('KT_Steel', -0.42, 0.24, 0.92, 0.42, 0.4, 1.5, bevel=0.02)
    b.box('KT_DarkSteel', -0.38, 0.22, 1.3, 0.38, 0.26, 1.34)
    return b.finish()


# Grill, 2 x 1: a flat-top griddle with patties, a grease trough, knobs and a hood at the back.
def grill(name):
    b = Builder(name)
    b.box('KT_Black', -0.94, -0.36, 0.0, 0.94, 0.38, 0.08)
    b.box('KT_DarkSteel', -0.96, -0.42, 0.08, 0.96, 0.42, 0.84, bevel=0.02)
    b.box('KT_Steel', -0.96, -0.44, 0.62, 0.96, -0.42, 0.82)
    b.knobs(-0.85, 0.85, -0.44, 0.72, 5, 'KT_Accent')
    for x in (-0.48, 0.48):
        b.box('KT_Steel', x - 0.4, -0.44, 0.14, x + 0.4, -0.42, 0.56)
        b.box('KT_Black', x - 0.12, -0.47, 0.48, x + 0.12, -0.44, 0.5)
    # The griddle plate, a heat glow under its edge, and the trough in front.
    b.box('KT_Black', -0.92, -0.32, 0.84, 0.92, 0.36, 0.9)
    b.box('KT_Flame', -0.9, -0.33, 0.83, 0.9, -0.31, 0.85)
    b.box('KT_Steel', -0.92, -0.42, 0.84, 0.92, -0.32, 0.88)
    for i, (x, y) in enumerate([(-0.6, -0.1), (-0.25, 0.12), (0.1, -0.12), (0.45, 0.1), (0.7, -0.08)]):
        b.cylinder('KT_Food', x, y, 0.9, 0.035, 0.09, segments=10)
        if i % 2 == 0:
            b.box('KT_Cream', x - 0.08, y - 0.08, 0.935, x + 0.08, y + 0.08, 0.945)
    # Spatula, splash guards and the hood.
    b.box('KT_Steel', 0.2, -0.3, 0.9, 0.32, -0.18, 0.905)
    b.box('KT_Black', 0.24, -0.42, 0.9, 0.28, -0.3, 0.92)
    b.box('KT_Steel', -0.96, -0.32, 0.9, -0.92, 0.42, 1.1)
    b.box('KT_Steel', 0.92, -0.32, 0.9, 0.96, 0.42, 1.1)
    b.box('KT_Steel', -0.96, 0.36, 0.9, 0.96, 0.42, 1.6)
    b.box('KT_DarkSteel', -0.98, 0.06, 1.6, 0.98, 0.44, 1.78, bevel=0.03)
    for x in (-0.6, -0.2, 0.2, 0.6):
        b.box('KT_Black', x - 0.12, 0.05, 1.63, x + 0.12, 0.07, 1.75)
    return b.finish()


# Kitchen counter, 2 x 1: a wood-topped prep counter (decision 0043's assembly station) with cupboards, a cutting board,
# a knife, a tomato and lettuce, a stack of plates and a tray of bowls.
def counter(name):
    b = Builder(name)
    b.box('KT_Black', -0.94, -0.36, 0.0, 0.94, 0.38, 0.08)
    b.box('KT_Cream', -0.96, -0.4, 0.08, 0.96, 0.4, 0.86, bevel=0.015)
    for x in (-0.72, -0.24, 0.24, 0.72):
        b.box('KT_Enamel', x - 0.22, -0.42, 0.14, x + 0.22, -0.4, 0.8)
        b.box('KT_Steel', x + (0.14 if x < 0 else -0.16), -0.45, 0.62, x + (0.16 if x < 0 else -0.14), -0.42, 0.74)
    b.box('KT_Wood', -0.98, -0.44, 0.86, 0.98, 0.44, 0.92, bevel=0.01)
    # Cutting board, knife, vegetables.
    b.box('KT_Board', -0.75, -0.3, 0.92, -0.2, 0.1, 0.95, bevel=0.01)
    b.box('KT_Steel', -0.45, -0.22, 0.95, -0.25, -0.18, 0.955)
    b.box('KT_Black', -0.6, -0.22, 0.95, -0.45, -0.18, 0.965)
    b.cylinder('KT_Tomato', -0.62, 0.0, 0.95, 0.07, 0.06, top=0.045, segments=10)
    b.box('KT_Tomato', -0.5, -0.05, 0.95, -0.42, 0.03, 0.97)
    b.cylinder('KT_Leaf', -0.32, 0.02, 0.95, 0.1, 0.08, top=0.05, segments=10)
    # Plates and a burger being assembled.
    for i in range(4):
        b.cylinder('KT_Cream', 0.15, 0.0, 0.92 + i * 0.015, 0.012, 0.13, segments=14)
    b.cylinder('KT_Cream', 0.5, -0.12, 0.92, 0.012, 0.14, segments=14)
    b.cylinder('KT_Board', 0.5, -0.12, 0.932, 0.03, 0.07, segments=10)
    b.cylinder('KT_Food', 0.5, -0.12, 0.962, 0.025, 0.072, segments=10)
    b.cylinder('KT_Leaf', 0.5, -0.12, 0.987, 0.008, 0.075, segments=10)
    b.cylinder('KT_Board', 0.5, -0.12, 0.995, 0.035, 0.068, top=0.05, segments=10)
    # Bowl tray and the back shelf with a ticket rail.
    for x in (0.7, 0.85):
        b.cylinder('KT_Steel', x, 0.25, 0.92, 0.07, 0.065, top=0.08, segments=10)
    b.box('KT_Wood', -0.96, 0.32, 0.92, 0.96, 0.4, 1.35)
    b.box('KT_Steel', -0.96, 0.24, 1.35, 0.96, 0.42, 1.39)
    b.box('KT_DarkSteel', -0.9, 0.3, 1.25, 0.9, 0.32, 1.28)
    for x in (-0.5, -0.1, 0.3):
        b.box('KT_Cream', x, 0.29, 1.12, x + 0.12, 0.3, 1.27)
    return b.finish()


fresh()
objects = [
    mixer('Kitchen_Mixer'),
    stove('Kitchen_Stove'),
    fryer('Kitchen_Fryer'),
    grill('Kitchen_Grill'),
    counter('Kitchen_Counter'),
]
positions = [(-3.6, 0.0), (-1.6, 0.0), (0.4, 0.0), (2.2, 0.0), (4.6, 0.0)]
for obj, (x, y) in zip(objects, positions):
    obj.location = (x, y, 0.0)
bpy.ops.wm.save_as_mainfile(filepath=f'{ROOT}/Kitchen.blend')

# Preview: a workbench render from the front, slightly above.
scene = bpy.context.scene
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows = True
scene.display.shading.show_cavity = True
scene.render.resolution_x = 1600
scene.render.resolution_y = 520
scene.render.film_transparent = False
camera_data = bpy.data.cameras.new('Preview')
camera_data.type = 'ORTHO'
camera_data.ortho_scale = 10.4
camera = bpy.data.objects.new('Preview', camera_data)
scene.collection.objects.link(camera)
camera.location = (0.6, -9.0, 5.6)
camera.rotation_euler = (math.radians(62), 0.0, 0.0)
scene.camera = camera
scene.render.filepath = f'{ROOT}/Kitchen_Preview.png'
bpy.ops.render.render(write_still=True)
bpy.data.objects.remove(camera)

# Export each at the origin (Unity reads meshes by object name).
for obj in objects:
    obj.location = (0.0, 0.0, 0.0)
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
os.makedirs(f'{ROOT}/Export', exist_ok=True)
bpy.ops.export_scene.fbx(filepath=f'{ROOT}/Export/Kitchen.fbx', use_selection=True, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, axis_forward='-Z', axis_up='Y',
                         object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True, path_mode='STRIP',
                         embed_textures=False, add_leaf_bones=False)
for obj, (x, y) in zip(objects, positions):
    obj.location = (x, y, 0.0)
bpy.ops.wm.save_as_mainfile(filepath=f'{ROOT}/Kitchen.blend')
for o in objects:
    print('KITCHEN', o.name, sum(len(p.vertices) - 2 for p in o.data.polygons), 'tris', [m.name for m in o.data.materials],
          tuple(round(d, 2) for d in o.dimensions))
