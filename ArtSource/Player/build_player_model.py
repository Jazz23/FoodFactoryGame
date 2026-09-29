"""Player character: a factory worker (hard hat and goggles, work shirt with rolled sleeves, bib apron, gloves, cargo
work pants with reflective bands, laced boots) as one skinned mesh with its locomotion clips, built entirely in code.

Run inside Blender 5.2 (starts a fresh file), with PLAYER_ART_ROOT set to this folder when the project is not at the default
path:
    PLAYER_ART_ROOT = r'E:/Projects/Unity/FoodFactoryGame/ArtSource/Player'
    exec(open(PLAYER_ART_ROOT + '/build_player_model.py').read(), {'PLAYER_ART_ROOT': PLAYER_ART_ROOT})
Saves ArtSource/Player/Player_Asset.blend, writes the palette textures to ArtSource/Player/Textures/ and exports
ArtSource/Player/Export/Player.fbx (armature, one skinned mesh and every action as a take). Copy the FBX to
Assets/Art/Models/Player/ and the textures to Assets/Art/Models/Player/Textures/, then run
AgentScripts/BuildPlayerVisual.cs in Unity.

Conventions (Blender, metres, Z up): the character faces -Y with its left side on +X. It imports facing Unity -Z (the
exporter's axis conversion does not turn skinned bones), so the prefab's Model child is turned 180 degrees about Y.
The pivot is between the feet at z = 0 and the top of the hard hat is at about 1.88 m.

Mesh: the body (torso, limbs, neck) is a skin-modifier mesh subdivided twice and weighted automatically; clothing,
gloves, boots and head parts are separate pieces joined into it with authored weights (the apron's skirt follows each
thigh so a stride does not push the legs through it). Two materials: PL_Shirt (plain white, tinted per player in
Unity) and PL_Palette, whose faces all map to the centre of one cell of Textures/Player_Palette.png (base colour) and
Textures/Player_Palette_MetallicGloss.png (R metallic, A smoothness), 8 x 4 cells of 8 px. Each face's cell is kept in
the integer face attribute pl_cell while building (-1 = shirt).

Clips (30 fps, in place, no root motion; the Root bone never moves): Idle, Walk and Run loop; Jump (launch into the air
pose) and Land are one-shots; Fall loops. Walk and Run are authored for the ground speeds in SPEEDS so the Unity blend
tree thresholds keep the feet planted.
"""
import math
import os
import bmesh
import bpy
from mathutils import Euler, Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

ROOT = globals().get('PLAYER_ART_ROOT', 'G:/Unity/FoodFactoryGame/ArtSource/Player')
FPS = 30
# Ground speed (m/s) at which each locomotion clip keeps the stance foot still; mirrored by the Unity blend tree.
SPEEDS = {'Walk': 1.6, 'Run': 6.0}

# Palette cells in order: name, sRGB base colour, smoothness, metallic. Cell i sits at column i % 8, row i // 8.
CELLS = [
    ('Skin', (0.86, 0.64, 0.50), 0.35, 0.0),
    ('Pants', (0.19, 0.21, 0.24), 0.15, 0.0),
    ('Boots', (0.33, 0.20, 0.11), 0.45, 0.0),
    ('Sole', (0.06, 0.06, 0.06), 0.2, 0.0),
    ('Gloves', (0.74, 0.56, 0.33), 0.3, 0.0),
    ('Apron', (0.16, 0.27, 0.38), 0.15, 0.0),
    ('Leather', (0.52, 0.30, 0.14), 0.4, 0.0),
    ('Brass', (0.80, 0.62, 0.26), 0.75, 1.0),
    ('HardHat', (0.98, 0.74, 0.07), 0.8, 0.0),
    ('Hair', (0.20, 0.12, 0.06), 0.3, 0.0),
    ('EyeWhite', (0.95, 0.94, 0.92), 0.7, 0.0),
    ('Iris', (0.30, 0.19, 0.09), 0.85, 0.0),
    ('Dark', (0.03, 0.03, 0.035), 0.5, 0.0),
    ('Brow', (0.15, 0.09, 0.05), 0.2, 0.0),
    ('Lip', (0.72, 0.43, 0.37), 0.45, 0.0),
    ('Lens', (0.30, 0.45, 0.50), 0.95, 0.4),
    ('Frame', (0.09, 0.09, 0.10), 0.5, 0.0),
    ('Reflect', (0.85, 0.87, 0.85), 0.9, 0.6),
    ('Badge', (0.96, 0.96, 0.95), 0.4, 0.0),
    ('BadgeBlue', (0.12, 0.33, 0.72), 0.5, 0.0),
    ('Steel', (0.62, 0.64, 0.67), 0.7, 1.0),
    ('Pen', (0.78, 0.10, 0.09), 0.7, 0.0),
    ('Logo', (0.10, 0.45, 0.28), 0.6, 0.0),
]
CELL = {name: i for i, (name, *_rest) in enumerate(CELLS)}
SHIRT = -1
PALETTE_COLUMNS, PALETTE_ROWS, CELL_PIXELS = 8, 4, 8

# name: (head, tail, parent)
BONES = {
    'Root': ((0, 0, 0), (0, 0, 0.2), None),
    'Hips': ((0, 0, 0.95), (0, 0, 1.08), 'Root'),
    'Spine': ((0, 0, 1.08), (0, 0, 1.25), 'Hips'),
    'Chest': ((0, 0, 1.25), (0, 0, 1.45), 'Spine'),
    'Neck': ((0, 0, 1.45), (0, 0, 1.56), 'Chest'),
    'Head': ((0, 0, 1.56), (0, 0, 1.84), 'Neck'),
}
for side, sx in (('L', 1), ('R', -1)):
    BONES.update({
        f'Shoulder.{side}': ((0.03 * sx, 0, 1.41), (0.16 * sx, 0, 1.43), 'Chest'),
        f'UpperArm.{side}': ((0.17 * sx, 0, 1.43), (0.23 * sx, 0, 1.17), f'Shoulder.{side}'),
        f'LowerArm.{side}': ((0.23 * sx, 0, 1.17), (0.27 * sx, 0, 0.93), f'UpperArm.{side}'),
        f'Hand.{side}': ((0.27 * sx, 0, 0.93), (0.285 * sx, -0.005, 0.82), f'LowerArm.{side}'),
        f'UpperLeg.{side}': ((0.10 * sx, 0, 0.93), (0.11 * sx, 0, 0.52), 'Hips'),
        f'LowerLeg.{side}': ((0.11 * sx, 0, 0.52), (0.11 * sx, 0.02, 0.10), f'UpperLeg.{side}'),
        f'Foot.{side}': ((0.11 * sx, 0.02, 0.10), (0.11 * sx, -0.10, 0.04), f'LowerLeg.{side}'),
        f'Toes.{side}': ((0.11 * sx, -0.10, 0.04), (0.11 * sx, -0.17, 0.04), f'Foot.{side}'),
    })

PARTS = []


def reset_scene():
    bpy.ops.wm.read_homefile(use_empty=True, use_factory_startup=True)
    scene = bpy.context.scene
    scene.render.fps = FPS
    scene.unit_settings.system = 'METRIC'
    scene.unit_settings.scale_length = 1.0


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ---- Palette and materials ------------------------------------------------------------------------------------------

def build_palette_images():
    width, height = PALETTE_COLUMNS * CELL_PIXELS, PALETTE_ROWS * CELL_PIXELS
    base = [0.0] * (width * height * 4)
    gloss = [0.0] * (width * height * 4)
    for i, (_, rgb, smoothness, metallic) in enumerate(CELLS):
        column, row = i % PALETTE_COLUMNS, i // PALETTE_COLUMNS
        for y in range(CELL_PIXELS):
            # Blender images store rows bottom-up; row 0 of the palette is the top of the texture.
            py = height - 1 - (row * CELL_PIXELS + y)
            for x in range(CELL_PIXELS):
                index = (py * width + column * CELL_PIXELS + x) * 4
                base[index:index + 4] = [*rgb, 1.0]
                gloss[index:index + 4] = [metallic, 0.0, 0.0, smoothness]
    os.makedirs(ROOT + '/Textures', exist_ok=True)
    images = {}
    for name, pixels, colorspace in (('Player_Palette', base, 'sRGB'), ('Player_Palette_MetallicGloss', gloss, 'Non-Color')):
        image = bpy.data.images.new(name, width, height, alpha=True)
        image.colorspace_settings.name = colorspace
        image.pixels = pixels
        image.filepath_raw = f'{ROOT}/Textures/{name}.png'
        image.file_format = 'PNG'
        image.save()
        images[name] = image
    return images


def build_materials(images):
    shirt = bpy.data.materials.new('PL_Shirt')
    shirt.use_nodes = True
    bsdf = shirt.node_tree.nodes['Principled BSDF']
    bsdf.inputs['Base Color'].default_value = (0.8, 0.8, 0.8, 1.0)
    bsdf.inputs['Roughness'].default_value = 0.8
    shirt.diffuse_color = (0.8, 0.8, 0.8, 1.0)

    palette = bpy.data.materials.new('PL_Palette')
    palette.use_nodes = True
    nodes, links = palette.node_tree.nodes, palette.node_tree.links
    bsdf = nodes['Principled BSDF']
    base = nodes.new('ShaderNodeTexImage')
    base.image, base.interpolation = images['Player_Palette'], 'Closest'
    gloss = nodes.new('ShaderNodeTexImage')
    gloss.image, gloss.interpolation = images['Player_Palette_MetallicGloss'], 'Closest'
    split = nodes.new('ShaderNodeSeparateColor')
    rough = nodes.new('ShaderNodeMath')
    rough.operation = 'SUBTRACT'
    rough.inputs[0].default_value = 1.0
    links.new(base.outputs['Color'], bsdf.inputs['Base Color'])
    links.new(gloss.outputs['Color'], split.inputs['Color'])
    links.new(split.outputs['Red'], bsdf.inputs['Metallic'])
    links.new(gloss.outputs['Alpha'], rough.inputs[1])
    links.new(rough.outputs['Value'], bsdf.inputs['Roughness'])
    return [shirt, palette]


# ---- Geometry helpers -----------------------------------------------------------------------------------------------

def xform(bm, scale=None, rotation=None, location=None, verts=None):
    verts = verts if verts is not None else bm.verts
    for v in verts:
        co = v.co.copy()
        if scale is not None:
            co = Vector((co.x * scale[0], co.y * scale[1], co.z * scale[2]))
        if rotation is not None:
            co = rotation @ co
        if location is not None:
            co = co + Vector(location)
        v.co = co
    return bm


def aligned(axis):
    return Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized())


def bm_ellipsoid(center, radii, segments=16, rings=10, rotation=None):
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=1.0)
    return xform(bm, radii, rotation, center)


def bm_capsule(a, b, radius, segments=10, rings=8):
    a, b = Vector(a), Vector(b)
    half = (b - a).length / 2 + radius
    return bm_ellipsoid((a + b) / 2, (radius, radius, half), segments, rings, aligned(b - a))


def bm_box(center, size, bevel=0.0, segments=2, rotation=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    xform(bm, size)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=bm.verts[:] + bm.edges[:], offset=bevel, offset_type='OFFSET', segments=segments,
                        profile=0.5, affect='EDGES', clamp_overlap=True)
    return xform(bm, None, rotation, center)


def bm_frustum(bottom, top, radius_bottom, radius_top, segments=20, caps=False):
    bottom, top = Vector(bottom), Vector(top)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=caps, cap_tris=False, segments=segments, radius1=radius_bottom,
                          radius2=radius_top, depth=(top - bottom).length)
    return xform(bm, None, aligned(top - bottom), (bottom + top) / 2)


def bm_torus(center, axis, major, minor, segments=24, sides=8, scale=None):
    bm = bmesh.new()
    rings = []
    for i in range(segments):
        a = 2 * math.pi * i / segments
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    for i in range(segments):
        for j in range(sides):
            bm.faces.new((rings[i][j], rings[(i + 1) % segments][j], rings[(i + 1) % segments][(j + 1) % sides],
                          rings[i][(j + 1) % sides]))
    return xform(bm, scale, aligned(axis), center)


def bm_grid(rows, thickness=0.0):
    """rows: list of rows of Vector, all the same length; quads between neighbours, optionally solidified."""
    bm = bmesh.new()
    verts = [[bm.verts.new(p) for p in row] for row in rows]
    for r in range(len(rows) - 1):
        for c in range(len(rows[0]) - 1):
            bm.faces.new((verts[r][c], verts[r][c + 1], verts[r + 1][c + 1], verts[r + 1][c]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if thickness:
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thickness)
    return bm


def bm_band(rings, closed=True, thickness=0.004):
    """A strip between two point loops (lists of Vector of equal length)."""
    bm = bmesh.new()
    a = [bm.verts.new(p) for p in rings[0]]
    b = [bm.verts.new(p) for p in rings[1]]
    count = len(a) if closed else len(a) - 1
    for i in range(count):
        j = (i + 1) % len(a)
        bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    if thickness:
        bmesh.ops.solidify(bm, geom=bm.faces[:], thickness=thickness)
    return bm


def bm_ribbon(points, normals, width, thickness=0.004):
    """A flat strap along points; normals give the strap's face direction at each point."""
    left, right = [], []
    for i, p in enumerate(points):
        tangent = (points[min(i + 1, len(points) - 1)] - points[max(i - 1, 0)]).normalized()
        side = tangent.cross(normals[i]).normalized() * (width / 2)
        left.append(p + side)
        right.append(p - side)
    return bm_band([left, right], closed=False, thickness=thickness)


def add_part(name, bm, cell, weights):
    """cell: palette cell index (or SHIRT), or a callable of face centre; weights: dict or callable of vertex co."""
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    obj = link(bpy.data.objects.new(name, mesh))
    attribute = mesh.attributes.new('pl_cell', 'INT', 'FACE')
    for polygon in mesh.polygons:
        attribute.data[polygon.index].value = cell(polygon.center) if callable(cell) else cell
        polygon.use_smooth = True
    groups = {}
    for vertex in mesh.vertices:
        for bone, weight in (weights(vertex.co) if callable(weights) else weights).items():
            if weight <= 0:
                continue
            if bone not in groups:
                groups[bone] = obj.vertex_groups.new(name=bone)
            groups[bone].add([vertex.index], weight, 'REPLACE')
    PARTS.append(obj)
    return obj


def cast(bvh, origin, direction):
    hit = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    return hit[0]


# ---- Body -----------------------------------------------------------------------------------------------------------

def build_armature():
    data = bpy.data.armatures.new('Player_Armature')
    arm = link(bpy.data.objects.new('Player_Armature', data))
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    for name, (head, tail, parent) in BONES.items():
        bone = data.edit_bones.new(name)
        bone.head, bone.tail = head, tail
        # Roll so each bone's local Z points forward (-Y); pose code works in armature space and does not depend on it.
        bone.align_roll(Vector((0, -1, 0)) if abs(Vector(tail).y - Vector(head).y) < 0.05 else Vector((0, 0, 1)))
        bone.use_deform = name != 'Root'
    for name, (_, _, parent) in BONES.items():
        if parent:
            bone = data.edit_bones[name]
            bone.parent = data.edit_bones[parent]
            bone.use_connect = (Vector(bone.head) - Vector(bone.parent.tail)).length < 1e-4
    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def apply_modifiers(obj):
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        for mod in list(obj.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)


def build_body():
    # Skeleton graph for the skin modifier: (position, radius x, radius y). Legs end inside the boots, arms inside the
    # glove cuffs, the neck inside the head.
    nodes = {
        'pelvis': ((0, 0, 0.95), 0.155, 0.105), 'spine': ((0, 0, 1.12), 0.14, 0.10),
        'chest': ((0, 0, 1.29), 0.175, 0.115), 'upper_chest': ((0, 0, 1.40), 0.16, 0.10),
        'neck': ((0, 0, 1.47), 0.058, 0.058), 'neck_top': ((0, 0, 1.58), 0.052, 0.052),
    }
    edges = [('pelvis', 'spine'), ('spine', 'chest'), ('chest', 'upper_chest'), ('upper_chest', 'neck'), ('neck', 'neck_top')]
    for side, sx in (('L', 1), ('R', -1)):
        nodes.update({
            f'shoulder{side}': ((0.175 * sx, 0, 1.42), 0.068, 0.068), f'elbow{side}': ((0.235 * sx, 0, 1.17), 0.054, 0.054),
            f'wrist{side}': ((0.27 * sx, 0, 0.95), 0.040, 0.040),
            f'hip{side}': ((0.10 * sx, 0, 0.90), 0.095, 0.095), f'knee{side}': ((0.11 * sx, 0.005, 0.52), 0.070, 0.070),
            f'ankle{side}': ((0.11 * sx, 0.02, 0.13), 0.057, 0.057),
        })
        edges += [('upper_chest', f'shoulder{side}'), (f'shoulder{side}', f'elbow{side}'), (f'elbow{side}', f'wrist{side}'),
                  ('pelvis', f'hip{side}'), (f'hip{side}', f'knee{side}'), (f'knee{side}', f'ankle{side}')]
    names = list(nodes)
    mesh = bpy.data.meshes.new('Player_Body')
    mesh.from_pydata([nodes[n][0] for n in names], [(names.index(a), names.index(b)) for a, b in edges], [])
    body = link(bpy.data.objects.new('Player_Body', mesh))
    skin = body.modifiers.new('Skin', 'SKIN')
    skin.branch_smoothing = 0.6
    skin.use_smooth_shade = True
    for i, name in enumerate(names):
        vertex = mesh.skin_vertices[0].data[i]
        vertex.radius = (nodes[name][1], nodes[name][2])
        vertex.use_root = name == 'pelvis'
    body.modifiers.new('Subdivision', 'SUBSURF').levels = 2
    apply_modifiers(body)
    for polygon in body.data.polygons:
        polygon.use_smooth = True
    return body


def bone_param(name, point):
    head, tail, _ = BONES[name]
    a, b = Vector(head), Vector(tail)
    t = max(0.0, min(1.0, (point - a).dot(b - a) / (b - a).length_squared))
    return t, (point - (a + (b - a) * t)).length


def nearest_bone(point, names=None):
    names = names or [n for n in BONES if n != 'Root']
    return min(names, key=lambda n: bone_param(n, point)[1])


def body_cell(center):
    bone = nearest_bone(center)
    if bone in ('Neck', 'Head'):
        return CELL['Skin']
    if bone.startswith(('UpperLeg', 'LowerLeg', 'Foot', 'Toes')):
        return CELL['Pants']
    if bone.startswith('LowerArm'):
        # Sleeves rolled to just below the elbow.
        return SHIRT if bone_param(bone, center)[0] < 0.22 else CELL['Skin']
    if bone.startswith('Hand'):
        return CELL['Skin']
    if bone in ('Hips', 'Spine') and abs(center.x) < 0.22:
        return SHIRT if center.z > 1.0 else CELL['Pants']
    return SHIRT


def region_bvh(body, bones):
    verts = [v.co.copy() for v in body.data.vertices]
    polys = [tuple(p.vertices) for p in body.data.polygons if nearest_bone(p.center) in bones]
    return BVHTree.FromPolygons(verts, polys)


def tag_body(body):
    attribute = body.data.attributes.new('pl_cell', 'INT', 'FACE')
    for polygon in body.data.polygons:
        attribute.data[polygon.index].value = body_cell(polygon.center)


# ---- Clothing and gear ----------------------------------------------------------------------------------------------

def wrap_ring(bvh, center, z, offset, angles, reach=0.5, fallback=0.1):
    """Points on a surface around a vertical axis at height z; angle 0 faces front (-Y), + turns to the left (+X)."""
    points = []
    for angle in angles:
        direction = Vector((math.sin(angle), -math.cos(angle), 0))
        origin = Vector((center[0], center[1], z)) + direction * reach
        hit = cast(bvh, origin, -direction)
        base = hit if hit is not None else Vector((center[0], center[1], z)) + direction * fallback
        points.append(base + direction * offset)
    return points


def build_apron(torso):
    waist = 0.99

    def front(x, z):
        clamp = max(-0.11, min(0.11, x))
        hit = cast(torso, (clamp, -1.0, z), (0, 1, 0))
        y = hit.y if hit is not None else -0.12
        return y + max(0.0, abs(x) - 0.11) * 1.5

    def surface(x, z, lift=0.0):
        if z >= waist:
            y = front(x, z) - 0.012
        else:
            # Hangs from the waist, flaring forward slightly and curving round the legs.
            y = front(x, waist) - 0.012 - 0.035 * (waist - z) / 0.45 + 0.9 * x * x
        return Vector((x, y - lift, z))

    def weights(co):
        x, z = co.x, co.z
        if z >= 1.16:
            return {'Chest': 1.0}
        if z >= 1.02:
            t = (z - 1.02) / 0.14
            return {'Chest': t, 'Spine': 1 - t}
        if z >= 0.95:
            t = (z - 0.95) / 0.07
            return {'Spine': t, 'Hips': 1 - t}
        s = smoothstep(0.95, 0.56, z) * 0.85
        left = smoothstep(-0.09, 0.09, x)
        return {'Hips': 1 - s, 'UpperLeg.L': s * left, 'UpperLeg.R': s * (1 - left)}

    def half_width(z):
        if z >= 1.20:
            return 0.105 + (1.40 - z) / 0.20 * 0.015
        if z >= 1.02:
            return 0.12 + smoothstep(1.20, 1.02, z) * 0.045
        return 0.165 + (1.02 - z) / 0.48 * 0.035

    row_z = [1.40 - i * (1.40 - 0.54) / 34 for i in range(35)]
    columns = 16
    rows = [[surface(half_width(z) * (-1 + 2 * c / columns), z) for c in range(columns + 1)] for z in row_z]
    bm = bm_grid(rows, thickness=0.005)

    def cell(center):
        # Leather binding along the hem and sides.
        at_side = abs(center.x) > half_width(center.z) * 0.93
        at_edge = center.z > row_z[0] - 0.02 or center.z < row_z[-1] + 0.02
        return CELL['Leather'] if at_side or at_edge else CELL['Apron']

    add_part('Apron', bm, cell, weights)

    # Front pocket with a pen, and brass rivets at its top corners and the bib corners.
    pocket_rows = [[surface(-0.11 + 0.22 * c / 8, z, 0.012) for c in range(9)] for z in (0.90, 0.86, 0.82, 0.78, 0.74)]
    add_part('ApronPocket', bm_grid(pocket_rows, thickness=0.003),
             lambda c: CELL['Leather'] if c.z > 0.885 else CELL['Apron'], weights)
    pen_base = surface(0.07, 0.83, 0.006)
    add_part('Pen', bm_capsule(pen_base, pen_base + Vector((0.006, 0, 0.12)), 0.0045), CELL['Pen'], weights)
    add_part('PenClip', bm_box(surface(0.0735, 0.935, 0.0115), (0.004, 0.002, 0.03)), CELL['Steel'], weights)
    for x, z, lift in ((-0.105, 0.895, 0.016), (0.105, 0.895, 0.016), (-0.095, 1.385, 0.006), (0.095, 1.385, 0.006)):
        add_part('Rivet', bm_ellipsoid(surface(x, z, lift), (0.0065, 0.004, 0.0065), 8, 6), CELL['Brass'], weights)

    # ID badge clipped to the bib on the character's left.
    badge = surface(0.06, 1.28, 0.009)
    add_part('Badge', bm_box(badge, (0.046, 0.003, 0.064), 0.004, 1), CELL['Badge'], {'Chest': 1.0})
    add_part('BadgeStripe', bm_box(badge + Vector((0, -0.002, 0.022)), (0.046, 0.002, 0.018)), CELL['BadgeBlue'], {'Chest': 1.0})
    add_part('BadgePhoto', bm_box(badge + Vector((-0.011, -0.002, -0.006)), (0.016, 0.002, 0.02)), CELL['Dark'], {'Chest': 1.0})
    add_part('BadgeClip', bm_box(badge + Vector((0, 0.0, 0.037)), (0.012, 0.005, 0.012)), CELL['Steel'], {'Chest': 1.0})

    # Leather neck strap from the bib corners up round the back of the neck.
    front_normal = Vector((0, -1, 0))
    strap = [surface(0.095, 1.395, 0.001), surface(0.09, 1.43, 0.002)]
    normals = [front_normal, front_normal]
    for degrees in range(50, 311, 20):
        direction = Vector((math.sin(math.radians(degrees)), -math.cos(math.radians(degrees)), 0))
        strap.append(Vector((0, 0.005, 1.49)) + direction * 0.084)
        normals.append(direction)
    strap += [surface(-0.09, 1.43, 0.002), surface(-0.095, 1.395, 0.001)]
    normals += [front_normal, front_normal]
    add_part('NeckStrap', bm_ribbon(strap, normals, 0.022), CELL['Leather'],
             lambda co: {'Chest': 0.7, 'Neck': 0.3} if co.z > 1.47 else {'Chest': 1.0})

    # Waist ties round the back, finished with a bow.
    angles = [math.radians(a) for a in range(62, 299, 8)]
    lower = wrap_ring(torso, (0, 0), 0.985, 0.008, angles, reach=0.25)
    upper = wrap_ring(torso, (0, 0), 1.013, 0.008, angles, reach=0.25)
    add_part('WaistTie', bm_band([lower, upper], closed=False, thickness=0.003), CELL['Apron'],
             {'Hips': 0.5, 'Spine': 0.5})
    back = wrap_ring(torso, (0, 0), 1.0, 0.016, [math.pi], reach=0.25)[0]
    bow = []
    for sx in (1, -1):
        add_part('BowLoop', bm_torus(back + Vector((0.034 * sx, 0.004, 0.004)), (0, 1, 0), 0.026, 0.008, 16, 6,
                                     (1.35, 1.0, 0.8)), CELL['Apron'], {'Hips': 1.0})
        tail = [back + Vector((0.01 * sx, 0.006, -0.005)), back + Vector((0.03 * sx, 0.012, -0.07)),
                back + Vector((0.045 * sx, 0.016, -0.14))]
        add_part('BowTail', bm_ribbon(tail, [Vector((0, 1, 0))] * 3, 0.026, 0.003), CELL['Apron'], {'Hips': 1.0})
    add_part('BowKnot', bm_ellipsoid(back + Vector((0, 0.006, 0)), (0.017, 0.012, 0.015), 10, 8), CELL['Apron'],
             {'Hips': 1.0})


def build_shirt_details(torso):
    add_part('Collar', bm_frustum((0, 0.004, 1.435), (0, 0.004, 1.515), 0.086, 0.068, 24), SHIRT,
             {'Chest': 0.7, 'Neck': 0.3})
    for side, sx in (('L', 1), ('R', -1)):
        elbow = Vector((0.235 * sx, 0, 1.17))
        wrist = Vector((0.27 * sx, 0, 0.95))
        axis = (wrist - elbow).normalized()
        add_part('SleeveRoll', bm_torus(elbow + (wrist - elbow) * 0.21, axis, 0.052, 0.014, 20, 8), SHIRT,
                 {f'LowerArm.{side}': 1.0})


def build_gloves():
    for side, sx in (('L', 1), ('R', -1)):
        hand = f'Hand.{side}'
        wrist = Vector((0.27 * sx, 0, 0.95))
        down = (Vector((0.285 * sx, -0.005, 0.82)) - wrist).normalized()
        inward = Vector((-sx, 0, 0))
        rotation = aligned(down)
        palm = wrist + down * 0.052
        add_part('GloveCuff', bm_frustum(wrist - down * 0.035, wrist + down * 0.02, 0.049, 0.045, 18), CELL['Gloves'],
                 {f'LowerArm.{side}': 0.5, hand: 0.5})
        add_part('GlovePalm', bm_box(palm, (0.034, 0.084, 0.082), 0.013, 2, rotation), CELL['Gloves'], {hand: 1.0})
        curl = (down * math.cos(math.radians(22)) + inward * math.sin(math.radians(22))).normalized()
        for y, length in ((-0.029, 0.066), (-0.0095, 0.074), (0.0095, 0.071), (0.028, 0.058)):
            base = palm + down * 0.036 + Vector((0, y, 0))
            add_part('GloveFinger', bm_capsule(base, base + curl * length, 0.0112), CELL['Gloves'], {hand: 1.0})
        thumb = palm + Vector((-sx * 0.01, -0.038, 0.012))
        direction = Vector((-sx * 0.35, -0.65, -0.62)).normalized()
        add_part('GloveThumb', bm_capsule(thumb, thumb + direction * 0.05, 0.0125), CELL['Gloves'], {hand: 1.0})
        # Leather reinforcement across the knuckles.
        add_part('GloveKnuckle', bm_box(palm + down * 0.03 - inward * 0.017, (0.008, 0.088, 0.018), 0.003, 1, rotation),
                 CELL['Leather'], {hand: 1.0})


def build_boots_and_legs(legs):
    for side, sx in (('L', 1), ('R', -1)):
        foot, shin = f'Foot.{side}', f'LowerLeg.{side}'
        x = 0.11 * sx

        def shaft_weights(co, foot=foot, shin=shin):
            t = smoothstep(0.10, 0.17, co.z)
            return {shin: t, foot: 1 - t}

        add_part('BootShaft', bm_frustum((x, 0.022, 0.07), (x, 0.024, 0.25), 0.066, 0.071, 22), CELL['Boots'], shaft_weights)
        add_part('BootCollar', bm_torus((x, 0.024, 0.25), (0, 0, 1), 0.071, 0.011, 22, 6), CELL['Boots'], {shin: 1.0})
        box = bm_box((x, -0.045, 0.075), (0.115, 0.235, 0.10), 0.035, 3)
        for v in box.verts:
            # The toe slopes down towards the tip.
            t = max(0.0, min(1.0, (-0.06 - v.co.y) / 0.1))
            v.co.z = 0.035 + (v.co.z - 0.035) * (1 - 0.35 * t)
        add_part('BootFoot', box, CELL['Boots'], {foot: 1.0})
        add_part('BootSole', bm_box((x, -0.045, 0.017), (0.128, 0.25, 0.034), 0.01, 2), CELL['Sole'], {foot: 1.0})
        add_part('BootHeel', bm_box((x, 0.045, 0.012), (0.12, 0.07, 0.024), 0.006, 1), CELL['Sole'], {foot: 1.0})
        for z in (0.12, 0.155, 0.19, 0.225):
            add_part('BootLace', bm_box((x, 0.022 - 0.068, z), (0.05, 0.006, 0.006), 0.002, 1), CELL['Dark'], shaft_weights)
        add_part('BootLaceRun', bm_box((x, 0.022 - 0.066, 0.17), (0.012, 0.004, 0.12)), CELL['Leather'], shaft_weights)

        # Cargo pocket and flap on the outer thigh, and a reflective band round the shin.
        hit = cast(legs[side], (x + sx * 0.5, 0.0, 0.72), (-sx, 0, 0))
        outer = hit if hit is not None else Vector((x + sx * 0.09, 0, 0.72))
        add_part('CargoPocket', bm_box(outer + Vector((sx * 0.006, 0, 0)), (0.014, 0.1, 0.12), 0.005, 1), CELL['Pants'],
                 {f'UpperLeg.{side}': 1.0})
        add_part('CargoFlap', bm_box(outer + Vector((sx * 0.012, 0, 0.055)), (0.012, 0.106, 0.032), 0.004, 1),
                 CELL['Pants'], {f'UpperLeg.{side}': 1.0})
        add_part('CargoSnap', bm_ellipsoid(outer + Vector((sx * 0.019, 0, 0.05)), (0.004, 0.007, 0.007), 8, 6),
                 CELL['Brass'], {f'UpperLeg.{side}': 1.0})
        angles = [2 * math.pi * i / 28 for i in range(28)]
        center = (x, 0.012)
        band = [wrap_ring(legs[side], center, z, 0.004, angles, reach=0.15) for z in (0.315, 0.35)]
        add_part('ReflectiveBand', bm_band(band, closed=True, thickness=0.002), CELL['Reflect'], {shin: 1.0})


def build_head():
    center = Vector((0, -0.005, 1.688))
    bm = bm_ellipsoid(center, (0.12, 0.128, 0.14), 32, 20)
    for v in bm.verts:
        # Narrower jaw and a slightly forward chin.
        t = max(0.0, min(1.0, (center.z - v.co.z) / 0.14))
        v.co.x *= 1 - 0.14 * t
        if v.co.y < center.y:
            v.co.y -= 0.012 * t * t
    head_bvh = BVHTree.FromBMesh(bm)
    add_part('Head', bm, CELL['Skin'], {'Head': 1.0})

    def face_y(x, z):
        hit = cast(head_bvh, (x, -1.0, z), (0, 1, 0))
        return hit.y if hit is not None else -0.12

    rigid = {'Head': 1.0}
    for sx in (1, -1):
        ex, ez = 0.042 * sx, 1.702
        hy = face_y(ex, ez)
        add_part('EyeWhite', bm_ellipsoid((ex, hy + 0.011, ez), (0.019, 0.013, 0.0155), 14, 10), CELL['EyeWhite'], rigid)
        add_part('Iris', bm_ellipsoid((ex, hy - 0.0008, ez), (0.0102, 0.003, 0.0102), 12, 8), CELL['Iris'], rigid)
        add_part('Pupil', bm_ellipsoid((ex, hy - 0.0028, ez), (0.0048, 0.0018, 0.0048), 10, 6), CELL['Dark'], rigid)
        add_part('Eyelid', bm_ellipsoid((ex, hy + 0.009, ez + 0.0125), (0.021, 0.014, 0.0075), 12, 8), CELL['Skin'], rigid)
        by = face_y(ex * 1.05, 1.738)
        add_part('Brow', bm_box((ex * 1.05, by - 0.003, 1.738), (0.036, 0.009, 0.009), 0.003, 1,
                                Euler((0, math.radians(-9 * sx), 0)).to_quaternion()), CELL['Brow'], rigid)
        add_part('Ear', bm_ellipsoid((0.121 * sx, 0.005, 1.683), (0.013, 0.028, 0.037), 12, 8), CELL['Skin'], rigid)
    ny = face_y(0, 1.672)
    add_part('Nose', bm_ellipsoid((0, ny - 0.007, 1.668), (0.015, 0.019, 0.025), 12, 8), CELL['Skin'], rigid)
    my = face_y(0, 1.628)
    add_part('Lips', bm_ellipsoid((0, my + 0.0005, 1.628), (0.026, 0.0045, 0.0068), 12, 8), CELL['Lip'], rigid)
    add_part('MouthLine', bm_ellipsoid((0, my - 0.0038, 1.628), (0.022, 0.002, 0.0014), 10, 6), CELL['Dark'], rigid)

    # Short hair showing under the hard hat: a shell over the head whose face-side vertices sink inside the skull, so the
    # hairline is where the two surfaces cross (smooth, unlike deleted faces). It runs diagonally from the nape up past
    # the ears to the temples; the forehead fringe shows under the brim.
    hair = bm_ellipsoid(center, (0.12 * 1.045, 0.128 * 1.045, 0.14 * 1.035), 40, 24)
    for v in hair.verts:
        t = max(0.0, min(1.0, (center.z - v.co.z) / 0.14))
        v.co.x *= 1 - 0.14 * t
        # Signed distance-like margin to the hairline (positive = hair); the sink eases across it.
        margin = min(max(v.co.y + 0.02 + 0.07 * smoothstep(1.62, 1.72, v.co.z), v.co.z - 1.745), v.co.z - 1.615)
        v.co = center + (v.co - center) * (0.92 + 0.08 * smoothstep(-0.012, 0.012, margin))
    add_part('Hair', hair, CELL['Hair'], rigid)

    # Hard hat: shell with a centre ridge, a brim that peaks at the front, a logo, and goggles on an elastic strap.
    hat_center = Vector((0, 0.005, 1.765))
    dome = bm_ellipsoid(hat_center, (0.134, 0.15, 0.108), 32, 16)
    bmesh.ops.delete(dome, geom=[v for v in dome.verts if v.co.z < hat_center.z - 0.004], context='VERTS')
    dome_bvh = BVHTree.FromBMesh(dome)
    bmesh.ops.solidify(dome, geom=dome.faces[:], thickness=0.006)
    add_part('HardHat', dome, CELL['HardHat'], rigid)
    ridge = bm_ellipsoid(hat_center, (0.02, 0.154, 0.115), 16, 16)
    bmesh.ops.delete(ridge, geom=[v for v in ridge.verts if v.co.z < hat_center.z + 0.03], context='VERTS')
    bmesh.ops.solidify(ridge, geom=ridge.faces[:], thickness=0.004)
    add_part('HardHatRidge', ridge, CELL['HardHat'], rigid)
    inner, outer = [], []
    for i in range(48):
        angle = 2 * math.pi * i / 48
        direction = Vector((math.sin(angle), -math.cos(angle), 0))
        front = max(0.0, math.cos(angle)) ** 3
        base = hat_center + Vector((direction.x * 0.134 * 0.97, direction.y * 0.15 * 0.97, 0))
        inner.append(base)
        outer.append(base + direction * (0.022 + 0.05 * front) + Vector((0, 0, -0.012 - 0.008 * front)))
    add_part('HardHatBrim', bm_band([inner, outer], closed=True, thickness=0.007), CELL['HardHat'], rigid)
    logo_y = cast(dome_bvh, (0, -1, 1.825), (0, 1, 0))
    if logo_y is not None:
        add_part('HardHatLogo', bm_ellipsoid(logo_y + Vector((0, -0.001, 0)), (0.024, 0.004, 0.02), 14, 8), CELL['Logo'],
                 rigid)
    strap_angles = [2 * math.pi * i / 40 for i in range(40)]
    strap = [wrap_ring(dome_bvh, (0, 0.005), z, 0.003, strap_angles, reach=0.3) for z in (1.788, 1.801)]
    add_part('GoggleStrap', bm_band(strap, closed=True, thickness=0.002), CELL['Frame'], rigid)
    for sx in (1, -1):
        hit = cast(dome_bvh, (0.036 * sx, -1, 1.797), (0, 1, 0))
        lens = (hit if hit is not None else Vector((0.036 * sx, -0.14, 1.797))) + Vector((0, -0.012, 0))
        tilt = Euler((math.radians(-25), 0, math.radians(8 * sx))).to_quaternion()
        add_part('GoggleFrame', bm_torus(lens, tilt @ Vector((0, 1, 0)), 0.024, 0.005, 20, 6, (1.25, 1.0, 1.0)),
                 CELL['Frame'], rigid)
        add_part('GoggleLens', bm_ellipsoid(lens, (0.029, 0.0035, 0.024), 14, 8, tilt), CELL['Lens'], rigid)
    bridge = cast(dome_bvh, (0, -1, 1.797), (0, 1, 0))
    if bridge is not None:
        add_part('GoggleBridge', bm_box(bridge + Vector((0, -0.012, 0)), (0.02, 0.008, 0.008), 0.003, 1), CELL['Frame'], rigid)


def skin_to_armature(body, arm, materials):
    bpy.ops.object.select_all(action='DESELECT')
    body.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    # Heat weighting uses deform bones only, so Root gets no group.
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    bpy.ops.object.select_all(action='DESELECT')
    for part in PARTS:
        part.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = body.data.name = 'Player_Body'
    mesh = body.data
    unweighted = [v.index for v in mesh.vertices if not any(g.weight > 0 for g in v.groups)]
    if unweighted:
        raise RuntimeError(f'{len(unweighted)} body vertices have no bone weights.')

    # Materials and palette UVs from each face's cell.
    mesh.materials.clear()
    for material in materials:
        mesh.materials.append(material)
    uv = mesh.uv_layers.new(name='UVMap')
    cells = mesh.attributes['pl_cell'].data
    for polygon in mesh.polygons:
        cell = cells[polygon.index].value
        polygon.material_index = 0 if cell == SHIRT else 1
        u, v = (0.5, 0.5) if cell == SHIRT else ((cell % PALETTE_COLUMNS + 0.5) / PALETTE_COLUMNS,
                                                  1 - (cell // PALETTE_COLUMNS + 0.5) / PALETTE_ROWS)
        for loop in polygon.loop_indices:
            uv.data[loop].uv = (u, v)
    return body


# ---- Animation -------------------------------------------------------------------------------------------------------
# Poses are rotations in armature space applied on top of the parent's pose, in degrees about X (pitch: a bone pointing
# down swings its tip back for +, a bone pointing up leans forward for +, the foot points its toe down for +), Y (roll
# about the forward axis: + tilts an upright bone to the character's left) and Z (yaw: + turns to the character's left).

def gauss(phase, center, width=0.35):
    delta = (phase - center + math.pi) % (2 * math.pi) - math.pi
    return math.exp(-delta * delta / (2 * width * width))


def leg(pose, side, thigh_forward, knee, foot=None, spread=0.0):
    thigh = -thigh_forward
    pose[f'UpperLeg.{side}'] = (thigh, spread if side == 'R' else -spread, 0)
    pose[f'LowerLeg.{side}'] = (knee, 0, 0)
    # Default: keep the sole level with the ground.
    pose[f'Foot.{side}'] = (-(thigh + knee) if foot is None else foot, 0, 0)


def arm(pose, side, forward, elbow, out=0.0):
    abduct = -out if side == 'L' else out
    pose[f'UpperArm.{side}'] = (-forward, abduct, 0)
    pose[f'LowerArm.{side}'] = (-elbow, 0, 0)


def idle_pose(t):
    b = math.sin(2 * math.pi * t)
    pose = {'Spine': (1 + 0.8 * b, 0, 0), 'Chest': (1.5 * b, 0, 0), 'Neck': (-1 - b, 0, 0),
            'Head': (0, 0, 4 * math.sin(2 * math.pi * t + 0.8)), '_hips': (0, 0, 0.004 * b)}
    for side in 'LR':
        arm(pose, side, 2 + 2 * b, 12 + 2 * b, out=-6)
        leg(pose, side, 2, 3)
    return pose


def walk_pose(t):
    p = 2 * math.pi * t
    pose = {'Hips': (0, 0, -6 * math.sin(p)), 'Spine': (3, 0, 3 * math.sin(p)), 'Chest': (3, 0, 5 * math.sin(p)),
            'Neck': (-4, 0, -3 * math.sin(p)), 'Head': (-1, 0, -2 * math.sin(p)),
            '_hips': (0, 0, 0.022 * math.cos(2 * p) - 0.012)}
    for side, q in (('L', p), ('R', p + math.pi)):
        s = math.sin(q)
        thigh = 23 * s + 3
        knee = 5 + 52 * max(0.0, math.cos(q + 0.35)) ** 2
        foot = -(-thigh + knee) + 25 * gauss(q, -1.2) - 12 * gauss(q, 1.5)
        leg(pose, side, thigh, knee, foot)
        arm(pose, side, -18 * s, 15 + 12 * max(0.0, -s), out=-4)
    return pose


def run_pose(t):
    p = 2 * math.pi * t
    pose = {'Hips': (0, 0, -9 * math.sin(p)), 'Spine': (9, 0, 5 * math.sin(p)), 'Chest': (6, 0, 9 * math.sin(p)),
            'Neck': (-9, 0, -5 * math.sin(p)), 'Head': (-4, 0, -3 * math.sin(p)),
            '_hips': (0, 0, -0.035 - 0.035 * math.cos(2 * (p - 0.5 - math.pi / 2)))}
    for side, q in (('L', p), ('R', p + math.pi)):
        s = math.sin(q)
        thigh = 45 * s + 12
        knee = 18 + 88 * max(0.0, math.cos(q + 0.5)) ** 1.5 + 12 * gauss(q, 2.0)
        foot = -(-thigh + knee) * 0.6 + 32 * gauss(q, -1.1)
        leg(pose, side, thigh, knee, foot)
        arm(pose, side, -40 * s + 5, 80 + 15 * max(0.0, -s), out=6)
    return pose


def crouch_pose(depth, arms_forward, arms_out=0.0, lean=0.0):
    # depth 0..1: 0 standing, 1 deep crouch (hips 0.19 m lower, feet planted).
    thigh = 45 * depth
    knee = 80 * depth
    pose = {'Spine': (lean + 12 * depth, 0, 0), 'Chest': (4 * depth, 0, 0), 'Neck': (-10 * depth, 0, 0),
            '_hips': (0, 0, -0.19 * depth)}
    for side in 'LR':
        leg(pose, side, thigh, knee)
        arm(pose, side, arms_forward, 20 + 20 * depth, out=arms_out)
    return pose


def jump_pose(t):
    # 0: leaving the ground from a crouch, arms swung back; 1: tucked air pose with arms up.
    if t < 0.35:
        k = t / 0.35
        pose = crouch_pose(0.55 * (1 - k), -30 + 80 * k, lean=4)
        return pose
    k = min(1.0, (t - 0.35) / 0.65)
    pose = {'Spine': (4 - 6 * k, 0, 0), 'Chest': (-3 * k, 0, 0), 'Neck': (2 * k, 0, 0), '_hips': (0, 0, 0)}
    for side in 'LR':
        leg(pose, side, 30 * k + (6 if side == 'L' else -4) * k, 55 * k, None)
        arm(pose, side, 50 + 40 * k, 35, out=20 * k)
    return pose


def fall_pose(t):
    p = 2 * math.pi * t
    pose = {'Spine': (-4, 0, 2 * math.sin(p)), 'Chest': (-2, 0, 0), 'Neck': (4, 0, 0), '_hips': (0, 0, 0)}
    leg(pose, 'L', 22 + 6 * math.sin(p), 32 + 6 * math.sin(p))
    leg(pose, 'R', 6 - 6 * math.sin(p), 48 - 6 * math.sin(p))
    arm(pose, 'L', 25 + 8 * math.sin(p), 45, out=32 + 8 * math.sin(p + 1))
    arm(pose, 'R', 25 - 8 * math.sin(p), 45, out=32 + 8 * math.sin(p + 2))
    return pose


def land_pose(t):
    if t < 0.25:
        k = t / 0.25
        return blend(fall_pose(0), crouch_pose(1.0, 25, arms_out=15), k)
    k = (t - 0.25) / 0.75
    k = k * k * (3 - 2 * k)
    return blend(crouch_pose(1.0, 25, arms_out=15), idle_pose(0), k)


def blend(a, b, k):
    return {name: tuple(x + (y - x) * k for x, y in zip(a.get(name, (0, 0, 0)), b.get(name, (0, 0, 0))))
            for name in set(a) | set(b)}


CLIPS = [
    # name, frames, looping, pose(t in [0, 1])
    ('Idle', 60, True, idle_pose),
    ('Walk', 30, True, walk_pose),
    ('Run', 20, True, run_pose),
    ('Jump', 12, False, jump_pose),
    ('Fall', 30, True, fall_pose),
    ('Land', 14, False, land_pose),
]


def apply_pose(arm_obj, pose):
    for pose_bone in arm_obj.pose.bones:
        rest = pose_bone.bone.matrix_local.to_quaternion()
        x, y, z = pose.get(pose_bone.name, (0, 0, 0))
        rotation = Euler((math.radians(x), math.radians(y), math.radians(z)), 'XYZ').to_quaternion()
        pose_bone.rotation_mode = 'QUATERNION'
        pose_bone.rotation_quaternion = rest.inverted() @ rotation @ rest
        pose_bone.location = (0, 0, 0)
    hips = arm_obj.pose.bones['Hips']
    offset = Vector(pose.get('_hips', (0, 0, 0)))
    hips.location = hips.bone.matrix_local.to_quaternion().inverted() @ offset


def build_actions(arm_obj):
    arm_obj.animation_data_create()
    for name, frames, looping, pose_fn in CLIPS:
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        arm_obj.animation_data.action = action
        step = 1 if frames <= 14 else 2
        keys = list(range(0, frames + 1, step))
        if keys[-1] != frames:
            keys.append(frames)
        for frame in keys:
            # A loop's last key repeats its first exactly.
            apply_pose(arm_obj, pose_fn(0.0 if looping and frame == frames else frame / frames))
            for pose_bone in arm_obj.pose.bones:
                pose_bone.keyframe_insert('rotation_quaternion', frame=frame + 1, group=pose_bone.name)
                if pose_bone.name == 'Hips':
                    pose_bone.keyframe_insert('location', frame=frame + 1, group=pose_bone.name)
        action.frame_range = (1, frames + 1)
    arm_obj.animation_data.action = bpy.data.actions['Idle']
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 61


def export(arm_obj, body):
    os.makedirs(ROOT + '/Export', exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=ROOT + '/Player_Asset.blend')
    bpy.ops.object.select_all(action='DESELECT')
    arm_obj.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.export_scene.fbx(filepath=ROOT + '/Export/Player.fbx', use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             object_types={'ARMATURE', 'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                             add_leaf_bones=False, use_armature_deform_only=False, armature_nodetype='NULL',
                             bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
                             path_mode='STRIP', embed_textures=False)


reset_scene()
# read_homefile leaves no window context, so every operator runs under an explicit override.
window = bpy.context.window_manager.windows[0]
with bpy.context.temp_override(window=window, screen=window.screen, area=window.screen.areas[0],
                               scene=bpy.data.scenes[0], view_layer=bpy.data.scenes[0].view_layers[0]):
    materials = build_materials(build_palette_images())
    armature = build_armature()
    body_obj = build_body()
    tag_body(body_obj)
    torso = region_bvh(body_obj, {'Hips', 'Spine', 'Chest'})
    legs = {side: region_bvh(body_obj, {f'UpperLeg.{side}', f'LowerLeg.{side}'}) for side in 'LR'}
    build_shirt_details(torso)
    build_apron(torso)
    build_gloves()
    build_boots_and_legs(legs)
    build_head()
    body_obj = skin_to_armature(body_obj, armature, materials)
    build_actions(armature)
    export(armature, body_obj)
result = {
    'triangles': sum(len(p.vertices) - 2 for p in body_obj.data.polygons),
    'vertices': len(body_obj.data.vertices),
    'bones': len(armature.data.bones),
    'materials': [m.name for m in body_obj.data.materials],
    'actions': [(a.name, tuple(a.frame_range)) for a in bpy.data.actions],
    'height': max((body_obj.matrix_world @ v.co).z for v in body_obj.data.vertices),
}
