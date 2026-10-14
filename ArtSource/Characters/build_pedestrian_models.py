# Builds the two extra street figures used by customers and ambient pedestrians (CustomerPresenter / AmbientPedestrians):
# Pedestrian_A (stocky, colour-block hoodie, jeans, beanie, backpack) and Pedestrian_B (slim, long hair, blouse, skirt,
# crossbody bag). Each is built on a copy of the Employee armature from ArtSource/Employee/Employee_Asset.blend, unchanged, so
# the Employee clips (Idle / Walk / CarryIdle / CarryWalk, bound by transform path "root/spine/...") drive them in Unity: like
# the Employee, every part is a rigid mesh parented to one bone, in the same low-poly style (bevelled boxes, 10-sided limbs,
# icosphere heads and hands, flat colours).
#
# Renderer names matter in Unity: CustomerPresenter tints any renderer whose name contains Torso, UpperArm, Thigh, Shin or
# Pelvis with the district colour. Shirts/hoodies use Torso/UpperArm names (tinted); trousers, skirts and accessories avoid
# those words so they keep their own colours.
#
# Export matches Employee.fbx (global scale 0.43, no leaf bones, -Z forward / Y up, armature as the single root node). No
# animation is exported; the Employee clips are reused. Headless, from the project root:
#
#   blender -b --factory-startup --python ArtSource/Characters/build_pedestrian_models.py
#
# Writes ArtSource/Characters/Pedestrian_A.blend, Pedestrian_B.blend, Export/*.fbx, a preview PNG, and copies the FBX files to
# Assets/Art/Characters/Resources/Characters/ (loaded at runtime with Resources.LoadAll).
import math
import os
import shutil

import bmesh
import bpy
from mathutils import Matrix, Vector

ROOT = globals().get('CHARACTER_ART_ROOT') or os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(ROOT))
SOURCE = os.path.join(PROJECT, 'ArtSource', 'Employee', 'Employee_Asset.blend')
EXPORT_DIR = os.path.join(ROOT, 'Export')
UNITY_DIR = os.path.join(PROJECT, 'Assets', 'Art', 'Characters', 'Resources', 'Characters')
ARMATURE = 'Employee_Armature'
SCALE = 0.43


def material(name, colour, roughness=0.72):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Base Color'].default_value = (*colour, 1.0)
    bsdf.inputs['Roughness'].default_value = roughness
    bsdf.inputs['Metallic'].default_value = 0.0
    m.diffuse_color = (*colour, 1.0)
    return m


class Figure:
    def __init__(self, prefix, palette):
        bpy.ops.wm.open_mainfile(filepath=SOURCE)
        self.prefix = prefix
        self.arm = bpy.data.objects[ARMATURE]
        for obj in list(bpy.data.objects):
            if obj.type == 'MESH':
                bpy.data.objects.remove(obj, do_unlink=True)
        for block in (bpy.data.meshes, bpy.data.materials):
            for item in list(block):
                if item.users == 0:
                    block.remove(item)
        # Rest pose: parts are placed against the rest bones, and nothing animated is exported.
        if self.arm.animation_data:
            self.arm.animation_data.action = None
        for action in bpy.data.actions:
            action.use_fake_user = True
        for bone in self.arm.pose.bones:
            bone.location = (0, 0, 0)
            bone.rotation_quaternion = (1, 0, 0, 0)
            bone.rotation_euler = (0, 0, 0)
            bone.scale = (1, 1, 1)
        bpy.context.view_layer.update()
        self.materials = {key: material(f'{prefix}_{key}', *value) for key, value in palette.items()}
        self.parts = []

    def head(self, bone):
        return self.arm.matrix_world @ self.arm.data.bones[bone].head_local

    def tail(self, bone):
        return self.arm.matrix_world @ self.arm.data.bones[bone].tail_local

    def along(self, bone, t):
        return self.head(bone).lerp(self.tail(bone), t)

    def _attach(self, name, mesh, bone, world, mat, bevel=0.0, smooth=False):
        obj = bpy.data.objects.new(f'{self.prefix}_{name}', mesh)
        bpy.context.scene.collection.objects.link(obj)
        mesh.materials.append(self.materials[mat])
        for poly in mesh.polygons:
            poly.use_smooth = smooth
        if bevel > 0:
            modifier = obj.modifiers.new('Bevel', 'BEVEL')
            modifier.width = bevel
            modifier.segments = 2
            modifier.limit_method = 'ANGLE'
        # Rigid bone parent, as the Employee: the object's frame is relative to the bone's tail in rest pose.
        data_bone = self.arm.data.bones[bone]
        parent = self.arm.matrix_world @ data_bone.matrix_local @ Matrix.Translation((0, data_bone.length, 0))
        obj.parent = self.arm
        obj.parent_type = 'BONE'
        obj.parent_bone = bone
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_basis = parent.inverted() @ world
        self.parts.append(obj)
        return obj

    @staticmethod
    def _mesh(name, build):
        mesh = bpy.data.meshes.new(name)
        bm = bmesh.new()
        build(bm)
        uv = bm.loops.layers.uv.new('UVMap')
        for face in bm.faces:
            for loop in face.loops:
                loop[uv].uv = (loop.vert.co.x, loop.vert.co.z)
        bm.to_mesh(mesh)
        bm.free()
        return mesh

    def box(self, name, bone, centre, size, mat, bevel=0.04, tilt=None):
        def build(bm):
            bmesh.ops.create_cube(bm, size=1.0)
            for v in bm.verts:
                v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        world = Matrix.Translation(centre) @ (tilt or Matrix.Identity(4))
        return self._attach(name, self._mesh(name, build), bone, world, mat, bevel)

    def ellipsoid(self, name, bone, centre, radii, mat, subdivisions=2):
        def build(bm):
            bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
            for v in bm.verts:
                v.co = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2]))
        return self._attach(name, self._mesh(name, build), bone, Matrix.Translation(centre), mat, smooth=True)

    def tube(self, name, bone, start, end, radius, mat, end_radius=None, depth_scale=1.0, bevel=0.04, segments=10):
        start, end = Vector(start), Vector(end)
        axis = end - start
        r2 = radius if end_radius is None else end_radius

        def build(bm):
            bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius, radius2=r2, depth=axis.length)
            for v in bm.verts:
                v.co.y *= depth_scale
        rotation = axis.normalized().to_track_quat('Z', 'Y').to_matrix().to_4x4()
        world = Matrix.Translation((start + end) / 2) @ rotation
        return self._attach(name, self._mesh(name, build), bone, world, mat, bevel, smooth=True)

    def limb(self, name, bone, radius, mat, t0=0.0, t1=1.0, end_radius=None, bevel=0.04):
        return self.tube(name, bone, self.along(bone, t0), self.along(bone, t1), radius, mat, end_radius, bevel=bevel)

    def face(self, skin, eye='Eye', y=-0.3, eye_white=None):
        # Ears, eyes, nose and mouth on the head bone, as the Employee's.
        for side, x in (('L', -1), ('R', 1)):
            self.ellipsoid(f'Ear.{side}', 'head', (x * 0.375, 0.05, 3.83), (0.085, 0.065, 0.12), skin, 1)
            if eye_white:
                self.ellipsoid(f'EyeWhite.{side}', 'head', (x * 0.145, y + 0.035, 3.94), (0.06, 0.025, 0.055), eye_white, 1)
            self.ellipsoid(f'Eye.{side}', 'head', (x * 0.145, y + 0.03, 3.94), (0.04, 0.03, 0.045), eye, 1)
        self.ellipsoid('Nose', 'head', (0, y - 0.005, 3.83), (0.06, 0.055, 0.075), skin, 1)
        self.box('Mouth', 'head', (0, y + 0.01, 3.675), (0.14, 0.025, 0.035), eye, 0.012)

    def hands(self, skin, radii):
        for side in ('L', 'R'):
            self.ellipsoid(f'Hand.{side}', f'hand.{side}', self.along(f'hand.{side}', 0.4), radii, skin, 1)

    def finish(self, label):
        name = f'Pedestrian_{label}'
        self.arm.data.pose_position = 'POSE'
        bpy.context.view_layer.update()
        os.makedirs(EXPORT_DIR, exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=os.path.join(ROOT, f'{name}.blend'))
        for obj in bpy.context.view_layer.objects:
            obj.select_set(False)
        self.arm.select_set(True)
        for part in self.parts:
            part.select_set(True)
        path = os.path.join(EXPORT_DIR, f'{name}.fbx')
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH'}, global_scale=SCALE,
                                 apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
                                 use_mesh_modifiers=True, mesh_smooth_type='OFF', add_leaf_bones=False, primary_bone_axis='Y',
                                 secondary_bone_axis='X', use_armature_deform_only=False, armature_nodetype='NULL',
                                 bake_anim=False, path_mode='STRIP', embed_textures=False)
        os.makedirs(UNITY_DIR, exist_ok=True)
        shutil.copyfile(path, os.path.join(UNITY_DIR, f'{name}.fbx'))
        tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(bpy.context.evaluated_depsgraph_get()).to_mesh().polygons)
                   for o in self.parts)
        print('PEDESTRIAN', name, len(self.parts), 'parts', tris, 'tris')


def build_a():
    # Stocky build: broad hoodie (tinted), charcoal lower sleeves, jeans, white sneakers, mustard beanie, beard, navy backpack.
    f = Figure('PedA', {
        'Hoodie': ((0.16, 0.30, 0.20),), 'Cuff': ((0.05, 0.05, 0.06),), 'String': ((0.85, 0.85, 0.82),),
        'Skin': ((0.26, 0.13, 0.07),), 'Hair': ((0.02, 0.015, 0.01),), 'Eye': ((0.015, 0.012, 0.01), 0.5),
        'EyeWhite': ((0.85, 0.85, 0.8), 0.4), 'Beanie': ((0.78, 0.47, 0.04),), 'Denim': ((0.06, 0.12, 0.28), 0.8),
        'Sneaker': ((0.88, 0.88, 0.85), 0.5), 'Sole': ((0.62, 0.07, 0.04), 0.6), 'Pack': ((0.035, 0.06, 0.15), 0.6),
    })
    f.box('Torso', 'spine', (0, 0.03, 2.62), (1.0, 0.62, 1.2), 'Hoodie', 0.1)
    f.box('TorsoPocket', 'spine', (0, -0.29, 2.3), (0.58, 0.06, 0.3), 'Hoodie', 0.03)
    f.ellipsoid('TorsoHood', 'spine', (0, 0.25, 3.27), (0.42, 0.22, 0.17), 'Hoodie')
    for side, x in (('L', -1), ('R', 1)):
        f.box(f'Drawstring.{side}', 'spine', (x * 0.1, -0.29, 3.0), (0.035, 0.03, 0.26), 'String', 0.01)
        f.box(f'PackStrap.{side}', 'spine', (x * 0.3, -0.3, 2.88), (0.1, 0.04, 0.62), 'Pack', 0.015)
        f.limb(f'UpperArm.{side}', f'upper_arm.{side}', 0.19, 'Hoodie', -0.05, 1.0, 0.17)
        f.limb(f'Sleeve.{side}', f'forearm.{side}', 0.165, 'Cuff', 0.0, 0.85, 0.15)
        f.limb(f'JeansUpper.{side}', f'thigh.{side}', 0.23, 'Denim', 0.0, 1.0, 0.2)
        f.limb(f'JeansLower.{side}', f'shin.{side}', 0.2, 'Denim', 0.0, 0.92, 0.19)
        fx = x * 0.24
        f.box(f'Sneaker.{side}', f'foot.{side}', (fx, -0.2, 0.15), (0.4, 0.74, 0.22), 'Sneaker', 0.07)
        f.box(f'SneakerSole.{side}', f'foot.{side}', (fx, -0.2, 0.035), (0.42, 0.76, 0.07), 'Sole', 0.02)
    f.box('Backpack', 'spine', (0, 0.47, 2.72), (0.7, 0.32, 0.86), 'Pack', 0.09)
    f.box('BackpackPocket', 'spine', (0, 0.64, 2.55), (0.5, 0.08, 0.36), 'Pack', 0.04)
    f.box('Hips', 'root', (0, 0.0, 1.96), (0.9, 0.56, 0.4), 'Denim', 0.07)
    f.tube('Neck', 'spine', (0, 0.05, 3.2), (0, 0.05, 3.48), 0.17, 'Skin')
    f.ellipsoid('Head', 'head', (0, 0.05, 3.81), (0.39, 0.36, 0.45), 'Skin')
    f.ellipsoid('Beard', 'head', (0, -0.12, 3.52), (0.31, 0.22, 0.17), 'Hair')
    f.ellipsoid('Beanie', 'head', (0, 0.06, 4.06), (0.41, 0.385, 0.29), 'Beanie')
    f.tube('BeanieCuff', 'head', (0, 0.05, 3.93), (0, 0.05, 4.06), 0.415, 'Beanie', depth_scale=0.93, bevel=0.03)
    f.ellipsoid('BeaniePom', 'head', (0, 0.06, 4.37), (0.1, 0.1, 0.09), 'Beanie', 1)
    f.face('Skin', eye='Eye', y=-0.3, eye_white='EyeWhite')
    f.hands('Skin', (0.15, 0.13, 0.17))
    f.finish('A')


def build_b():
    # Slim build: narrow blouse with short sleeves (tinted), bare arms, plum skirt, dark tights, red flats, long auburn hair,
    # tan crossbody bag.
    f = Figure('PedB', {
        'Blouse': ((0.85, 0.75, 0.55),), 'Collar': ((0.9, 0.9, 0.86),), 'Belt': ((0.12, 0.06, 0.03), 0.5),
        'Skin': ((0.78, 0.5, 0.37),), 'Hair': ((0.33, 0.07, 0.025), 0.6), 'Eye': ((0.03, 0.025, 0.02), 0.5),
        'Skirt': ((0.3, 0.05, 0.14),), 'Tights': ((0.035, 0.035, 0.045), 0.6), 'Shoe': ((0.58, 0.04, 0.05), 0.4),
        'Bag': ((0.55, 0.33, 0.13), 0.55), 'Gold': ((0.9, 0.65, 0.2), 0.3),
    })
    f.box('Torso', 'spine', (0, 0.03, 2.64), (0.74, 0.46, 1.14), 'Blouse', 0.1)
    f.box('Collar', 'spine', (0, -0.205, 3.17), (0.38, 0.06, 0.1), 'Collar', 0.02)
    f.box('Belt', 'spine', (0, 0.03, 2.1), (0.76, 0.48, 0.08), 'Belt', 0.02)
    strap = Matrix.Rotation(math.radians(-38), 4, 'Y')
    f.box('BagStrap', 'spine', (0.02, -0.215, 2.66), (0.06, 0.03, 1.2), 'Bag', 0.01, strap)
    f.box('Bag', 'spine', (0.42, -0.02, 2.2), (0.12, 0.42, 0.36), 'Bag', 0.04)
    for side, x in (('L', -1), ('R', 1)):
        f.limb(f'UpperArm.{side}', f'upper_arm.{side}', 0.165, 'Blouse', -0.05, 0.5, 0.15)
        f.limb(f'Arm.{side}', f'upper_arm.{side}', 0.11, 'Skin', 0.35, 1.0, 0.1)
        f.limb(f'Forearm.{side}', f'forearm.{side}', 0.1, 'Skin', 0.0, 0.9, 0.09)
        f.limb(f'Leg.{side}', f'thigh.{side}', 0.15, 'Tights', 0.0, 1.0, 0.13)
        f.limb(f'LowerLeg.{side}', f'shin.{side}', 0.13, 'Tights', 0.0, 1.05, 0.11)
        fx = x * 0.24
        f.box(f'Shoe.{side}', f'foot.{side}', (fx, -0.16, 0.13), (0.3, 0.6, 0.24), 'Shoe', 0.08)
        f.box(f'ShoeSole.{side}', f'foot.{side}', (fx, -0.16, 0.025), (0.31, 0.62, 0.05), 'Belt', 0.015)
        f.ellipsoid(f'Earring.{side}', 'head', (x * 0.37, 0.04, 3.66), (0.04, 0.04, 0.04), 'Gold', 1)
    # Flared skirt on the root bone: elliptical, wider and deeper at the hem so swinging thighs stay inside it.
    skirt = f.tube('Skirt', 'root', (0, 0.0, 1.38), (0, 0.0, 2.16), 0.6, 'Skirt', end_radius=0.4, depth_scale=0.8, bevel=0.03)
    skirt.modifiers.clear()
    f.tube('Neck', 'spine', (0, 0.05, 3.2), (0, 0.05, 3.48), 0.13, 'Skin')
    f.ellipsoid('Head', 'head', (0, 0.05, 3.82), (0.36, 0.34, 0.44), 'Skin')
    f.ellipsoid('Hair', 'head', (0, 0.12, 3.98), (0.4, 0.37, 0.36), 'Hair')
    f.ellipsoid('HairFringe', 'head', (0, -0.17, 4.1), (0.36, 0.13, 0.11), 'Hair')
    f.box('HairLong', 'head', (0, 0.24, 3.55), (0.78, 0.26, 0.86), 'Hair', 0.12)
    f.face('Skin', eye='Eye', y=-0.29)
    f.hands('Skin', (0.12, 0.105, 0.15))
    f.finish('B')


def preview():
    # Both figures, side by side with the Employee, for a quick visual check (Workbench, no lighting setup needed).
    bpy.ops.wm.read_factory_settings(use_empty=True)
    paths = [os.path.join(PROJECT, 'Assets', 'Art', 'Models', 'Employee', 'Employee.fbx')] + [
        os.path.join(EXPORT_DIR, f'Pedestrian_{x}.fbx') for x in ('A', 'B')]
    for index, path in enumerate(paths):
        before = set(bpy.data.objects)
        bpy.ops.import_scene.fbx(filepath=path)
        for obj in set(bpy.data.objects) - before:
            obj.animation_data_clear()
            if obj.type == 'ARMATURE':
                for bone in obj.pose.bones:
                    bone.matrix_basis = Matrix.Identity(4)
            if obj.parent is None:
                obj.location.x += (index - 1) * 1.1
            if obj.name.startswith('Employee_') and any(k in obj.name for k in ('Apron', 'Cap', 'Carry', 'Stripe', 'Badge')):
                obj.hide_render = True
    scene = bpy.context.scene
    camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
    scene.collection.objects.link(camera)
    camera.location = (2.6, -4.2, 1.6)
    camera.rotation_euler = (math.radians(82), 0, math.radians(32))
    scene.camera = camera
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.render.resolution_x, scene.render.resolution_y = 960, 640
    for suffix, location, rotation in (('', (2.6, -4.2, 1.6), 32), ('_Back', (-2.6, 4.2, 1.6), 212)):
        camera.location = location
        camera.rotation_euler = (math.radians(82), 0, math.radians(rotation))
        scene.render.filepath = os.path.join(ROOT, f'Pedestrians_Preview{suffix}.png')
        bpy.ops.render.render(write_still=True)


build_a()
build_b()
preview()
