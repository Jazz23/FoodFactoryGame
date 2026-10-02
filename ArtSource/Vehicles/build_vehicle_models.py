# Builds the road vehicles of the generated world (decision 0032, piece 4c) with bmesh into Vehicles.blend and exports
# Export/Vehicles.fbx, one object per vehicle. Low-poly and flat-coloured in the style of ArtSource/World: shapes are bevelled
# boxes and 8-sided wheels, colour comes from a handful of VH_* materials that map one-to-one to Unity materials.
# VH_Body is the paint Unity tints per vehicle (CityTrafficPresenter) or per company (the player's truck), so it is white here.
#
# Conventions (as ArtSource/World): metres, Z up, the vehicle's front faces -Y (imports as Unity +Z), length along Y, pivot on
# the ground at the centre of the wheelbase footprint. Run in Blender 5.2; the script empties the open file first (it does not
# reset preferences or add-ons). Headless, from the project root:
#
#   blender -b --factory-startup --python ArtSource/Vehicles/build_vehicle_models.py
#
# or inside a running Blender:
#
#   VEHICLE_ART_ROOT = r'E:/Projects/Unity/FoodFactoryGame/ArtSource/Vehicles'
#   exec(open(VEHICLE_ART_ROOT + '/build_vehicle_models.py').read(), {'VEHICLE_ART_ROOT': VEHICLE_ART_ROOT})
import math
import bmesh
import bpy

ROOT = globals().get('VEHICLE_ART_ROOT', 'E:/Projects/Unity/FoodFactoryGame/ArtSource/Vehicles')

# name: (base colour, roughness, metallic, emission strength)
MATERIALS = {
    'VH_Body': ((0.92, 0.92, 0.92), 0.35, 0.3, 0.0),
    'VH_Glass': ((0.10, 0.14, 0.18), 0.1, 0.6, 0.0),
    'VH_Tyre': ((0.04, 0.04, 0.045), 0.9, 0.0, 0.0),
    'VH_Trim': ((0.12, 0.12, 0.13), 0.6, 0.2, 0.0),
    'VH_Chrome': ((0.7, 0.72, 0.75), 0.25, 0.9, 0.0),
    'VH_Headlight': ((1.0, 0.96, 0.82), 0.2, 0.0, 1.5),
    'VH_Taillight': ((0.75, 0.05, 0.04), 0.3, 0.0, 0.8),
    'VH_Box': ((0.93, 0.93, 0.9), 0.5, 0.0, 0.0),
    'VH_Taxi': ((0.95, 0.72, 0.08), 0.35, 0.2, 0.0),
    'VH_Bus': ((0.15, 0.42, 0.7), 0.4, 0.2, 0.0),
    'VH_Sign': ((0.98, 0.98, 0.95), 0.4, 0.0, 0.6),
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

    # A box from (x0, y0, z0) to (x1, y1, z1), optionally bevelled; `taper_front`/`taper_back` pull the top face in along Y
    # (windscreens, rear windows), `narrow` pulls the top in along X (tumblehome).
    def box(self, material_name, x0, y0, z0, x1, y1, z1, bevel=0.0, taper_front=0.0, taper_back=0.0, narrow=0.0):
        index = self.slot(material_name)
        made = bmesh.ops.create_cube(self.bm, size=1.0)
        verts = made['verts']
        for v in verts:
            top = v.co.z > 0
            x = x0 if v.co.x < 0 else x1
            y = y0 if v.co.y < 0 else y1
            z = z0 if v.co.z < 0 else z1
            if top:
                x += narrow if v.co.x < 0 else -narrow
                y += taper_front if v.co.y < 0 else -taper_back
            v.co = (x, y, z)
        faces = list({f for v in verts for f in v.link_faces})
        for f in faces:
            f.material_index = index
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            result = bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=1, affect='EDGES', profile=0.5)
            for f in result.get('faces', []):
                f.material_index = index
        return faces

    # An 8-sided wheel with its axle along X, centred at (x, y, radius).
    def wheel(self, x, y, radius, width):
        index = self.slot('VH_Tyre')
        made = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=8, radius1=radius, radius2=radius, depth=width)
        rotation = bmesh.ops.rotate
        rotation(self.bm, verts=made['verts'], cent=(0, 0, 0), matrix=__import__('mathutils').Matrix.Rotation(math.pi / 2, 3, 'Y'))
        bmesh.ops.translate(self.bm, verts=made['verts'], vec=(x, y, radius))
        for f in {f for v in made['verts'] for f in v.link_faces}:
            f.material_index = index
        hub = self.slot('VH_Chrome')
        side = 1 if x > 0 else -1
        cap = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=6, radius1=radius * 0.45, radius2=radius * 0.45,
                                    depth=0.02)
        rotation(self.bm, verts=cap['verts'], cent=(0, 0, 0), matrix=__import__('mathutils').Matrix.Rotation(math.pi / 2, 3, 'Y'))
        bmesh.ops.translate(self.bm, verts=cap['verts'], vec=(x + side * width / 2, y, radius))
        for f in {f for v in cap['verts'] for f in v.link_faces}:
            f.material_index = hub

    def lights(self, half_width, front_y, back_y, z, inset=0.25, size=(0.3, 0.12)):
        for side in (-1, 1):
            x = side * (half_width - inset)
            self.box('VH_Headlight', x - size[0] / 2, front_y - 0.02, z, x + size[0] / 2, front_y + 0.03, z + size[1])
            self.box('VH_Taillight', x - size[0] / 2, back_y - 0.03, z, x + size[0] / 2, back_y + 0.02, z + size[1])

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


# A passenger car: length L, width W, body height, roof height, and where the cabin sits.
def car(name, length, width, body_top, roof, cabin_front, cabin_back, paint='VH_Body', wheel_radius=0.33, sign=False):
    b = Builder(name)
    hl, hw = length / 2, width / 2
    b.box(paint, -hw, -hl, 0.28, hw, hl, body_top, bevel=0.08)
    b.box('VH_Glass', -hw + 0.08, cabin_front, body_top, hw - 0.08, cabin_back, roof - 0.06, taper_front=0.45, taper_back=0.3, narrow=0.1)
    b.box(paint, -hw + 0.2, cabin_front + 0.47, roof - 0.07, hw - 0.2, cabin_back - 0.32, roof, bevel=0.03)
    b.box('VH_Trim', -hw + 0.05, -hl - 0.04, 0.3, hw - 0.05, -hl + 0.1, 0.48)
    b.box('VH_Trim', -hw + 0.05, hl - 0.1, 0.3, hw - 0.05, hl + 0.04, 0.48)
    b.lights(hw, -hl, hl, body_top - 0.2)
    axle = length * 0.32
    for y in (-axle, axle):
        for side in (-1, 1):
            b.wheel(side * (hw - 0.12), y, wheel_radius, 0.24)
    if sign:
        b.box('VH_Sign', -0.3, (cabin_front + cabin_back) / 2 - 0.12, roof, 0.3, (cabin_front + cabin_back) / 2 + 0.12, roof + 0.22)
    return b.finish()


def van(name):
    b = Builder(name)
    length, width, height = 5.0, 1.95, 2.3
    hl, hw = length / 2, width / 2
    b.box('VH_Body', -hw, -hl + 0.5, 0.3, hw, hl, height, bevel=0.08)
    b.box('VH_Body', -hw, -hl, 0.3, hw, -hl + 0.6, 1.0, bevel=0.08)
    b.box('VH_Glass', -hw + 0.06, -hl + 0.45, 1.0, hw - 0.06, -hl + 1.4, height - 0.25, taper_front=0.4)
    b.box('VH_Glass', -hw - 0.01, -hl + 1.0, 1.25, hw + 0.01, -hl + 1.7, height - 0.35)
    b.box('VH_Trim', -hw + 0.05, -hl - 0.04, 0.3, hw - 0.05, -hl + 0.1, 0.5)
    b.lights(hw, -hl, hl, 0.75)
    for y in (-1.55, 1.6):
        for side in (-1, 1):
            b.wheel(side * (hw - 0.12), y, 0.36, 0.25)
    return b.finish()


def pickup(name):
    b = Builder(name)
    length, width = 5.3, 1.95
    hl, hw = length / 2, width / 2
    b.box('VH_Body', -hw, -hl, 0.4, hw, hl, 1.15, bevel=0.07)
    b.box('VH_Glass', -hw + 0.08, -hl + 1.3, 1.15, hw - 0.08, -hl + 3.0, 1.85, taper_front=0.4, taper_back=0.1, narrow=0.08)
    b.box('VH_Body', -hw + 0.15, -hl + 1.75, 1.78, hw - 0.15, -hl + 2.85, 1.86)
    # The open bed: a darker floor inside the body's rim.
    b.box('VH_Trim', -hw + 0.1, -hl + 3.1, 1.0, hw - 0.1, hl - 0.1, 1.16)
    b.box('VH_Chrome', -hw + 0.05, -hl - 0.05, 0.42, hw - 0.05, -hl + 0.08, 0.62)
    b.lights(hw, -hl, hl, 0.9)
    for y in (-1.7, 1.55):
        for side in (-1, 1):
            b.wheel(side * (hw - 0.12), y, 0.4, 0.28)
    return b.finish()


def bus(name):
    b = Builder(name)
    length, width, height = 11.5, 2.5, 3.1
    hl, hw = length / 2, width / 2
    b.box('VH_Bus', -hw, -hl, 0.35, hw, hl, height, bevel=0.1)
    # Window band down both sides, the windscreen and rear window.
    b.box('VH_Glass', -hw - 0.01, -hl + 1.2, 1.35, hw + 0.01, hl - 0.6, 2.6)
    b.box('VH_Glass', -hw + 0.12, -hl - 0.01, 1.2, hw - 0.12, -hl + 0.2, 2.75)
    b.box('VH_Glass', -hw + 0.3, hl - 0.2, 1.6, hw - 0.3, hl + 0.01, 2.6)
    b.box('VH_Box', -hw - 0.012, -hl, 2.62, hw + 0.012, hl, 2.85)
    b.box('VH_Sign', -0.8, -hl - 0.02, 2.75, 0.8, -hl + 0.1, 2.98)
    b.lights(hw, -hl, hl, 0.7, inset=0.3)
    for y in (-3.6, 3.4):
        for side in (-1, 1):
            b.wheel(side * (hw - 0.18), y, 0.5, 0.32)
    return b.finish()


# The player's box truck (replaces the decision 0022 placeholder): a painted cab in front, a white cargo box behind.
def box_truck(name):
    b = Builder(name)
    length, width = 7.5, 2.4
    hl, hw = length / 2, width / 2
    cab_back = -hl + 2.1
    b.box('VH_Trim', -hw + 0.15, -hl + 0.2, 0.45, hw - 0.15, hl - 0.2, 0.75)
    b.box('VH_Body', -hw, -hl, 0.6, hw, cab_back, 1.55, bevel=0.08)
    b.box('VH_Body', -hw + 0.04, -hl + 0.55, 1.55, hw - 0.04, cab_back, 2.75, taper_front=0.25, bevel=0.06)
    b.box('VH_Glass', -hw + 0.12, -hl + 0.53, 1.65, hw - 0.12, -hl + 0.9, 2.5, taper_front=0.22)
    b.box('VH_Glass', -hw + 0.03, -hl + 0.9, 1.75, hw - 0.03, -hl + 1.6, 2.45)
    b.box('VH_Box', -hw, cab_back + 0.15, 0.85, hw, hl, 3.35, bevel=0.05)
    b.box('VH_Body', -hw - 0.01, cab_back + 0.15, 0.85, hw + 0.01, hl, 1.1)
    b.box('VH_Chrome', -hw + 0.1, -hl - 0.06, 0.55, hw - 0.1, -hl + 0.06, 0.8)
    b.lights(hw, -hl, hl, 0.95, inset=0.3)
    for y in (-hl + 1.15, hl - 1.6, hl - 0.6):
        for side in (-1, 1):
            b.wheel(side * (hw - 0.2), y, 0.48, 0.34)
    return b.finish()


fresh()
objects = [
    box_truck('Vehicle_BoxTruck'),
    car('Vehicle_Hatchback', 3.9, 1.75, 0.95, 1.5, -0.75, 1.75),
    car('Vehicle_Sedan', 4.6, 1.8, 0.92, 1.45, -0.7, 1.35),
    van('Vehicle_Van'),
    pickup('Vehicle_Pickup'),
    car('Vehicle_Taxi', 4.6, 1.8, 0.92, 1.45, -0.7, 1.35, paint='VH_Taxi', sign=True),
    bus('Vehicle_Bus'),
]
# Side by side for the preview, then exported each at the origin (Unity reads meshes by object name).
for i, obj in enumerate(objects):
    obj.location = (i * 5.5 - 16.5, 0.0, 0.0)
bpy.ops.wm.save_as_mainfile(filepath=f'{ROOT}/Vehicles.blend')
for obj in objects:
    obj.location = (0.0, 0.0, 0.0)
bpy.ops.object.select_all(action='DESELECT')
for obj in objects:
    obj.select_set(True)
bpy.context.view_layer.objects.active = objects[0]
bpy.ops.export_scene.fbx(filepath=f'{ROOT}/Export/Vehicles.fbx', use_selection=True, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, axis_forward='-Z', axis_up='Y',
                         object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True, path_mode='STRIP',
                         embed_textures=False, add_leaf_bones=False)
for i, obj in enumerate(objects):
    obj.location = (i * 5.5 - 16.5, 0.0, 0.0)
result = {'objects': [(o.name, sum(len(p.vertices) - 2 for p in o.data.polygons), [m.name for m in o.data.materials]) for o in objects]}
