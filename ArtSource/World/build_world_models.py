"""Low-poly world art (decision 0026 presentation): road and rail tiles, buildings, farms, station, ground quad.

Run inside Blender after build_world_textures.py:
    exec(open(r'G:/Unity/FoodFactoryGame/ArtSource/World/build_world_models.py').read())
Builds every asset into a fresh scene, saves ArtSource/World/World_Assets.blend and exports
ArtSource/World/Export/WorldArt.fbx (all assets, one object each). Unity reads meshes by object name.

Conventions (Blender, metres, Z up): every building's front (street side, doors) faces -Y; X runs along the street; the
pivot is the footprint centre at ground level. Stacked modules (apartments, offices) have their base at z = 0. Road and rail
tiles run along +Y from y = 0 to their tile length, centred across X. Materials are named WG_* and map one-to-one to Unity
materials; UVs are world-scaled per material (see UV below), with facade materials snapped to whole window bays per wall.
"""
import math
import os
import bmesh
import bpy
from mathutils import Vector

ROOT = 'G:/Unity/FoodFactoryGame/ArtSource/World'
TEX = ROOT + '/Textures'

# Material name -> (texture or None, UV mode, metres per repeat u, v, preview tint)
UV = {
    'WG_Facade_Brick': ('Facade_Brick', 'bay', 3.0, 3.0),
    'WG_Facade_Siding': ('Facade_Siding', 'bay', 3.0, 3.0),
    'WG_Facade_Plaster': ('Facade_Plaster', 'bay', 3.0, 3.0),
    'WG_Facade_Apartment': ('Facade_Apartment', 'bay', 3.2, 3.0),
    'WG_Facade_Office': ('Facade_Office', 'bay', 3.0, 3.5),
    'WG_Facade_OfficeDark': ('Facade_Office', 'bay', 3.0, 3.5),
    'WG_Facade_Storefront': ('Facade_Storefront', 'bay', 3.0, 4.5),
    'WG_Stucco': ('Concrete', 'world', 4.0, 4.0),
    'WG_Trim': ('Concrete', 'world', 4.0, 4.0),
    'WG_Concrete': ('Concrete', 'world', 4.0, 4.0),
    'WG_Metal_Wall': ('Metal_Corrugated', 'world', 4.0, 4.0),
    'WG_Metal_Roof': ('Metal_Corrugated', 'world', 4.0, 4.0),
    'WG_Window_Industrial': ('Window_Industrial', 'bay', 4.0, 1.5),
    'WG_Door_Roller': ('Door_Roller', 'single', 1, 1),
    'WG_Door_Wood': ('Door_Wood', 'single', 1, 1),
    'WG_Door_Glass': ('Door_Glass', 'single', 1, 1),
    'WG_Wood_Barn': ('Wood_BarnPlanks', 'world', 4.0, 4.0),
    'WG_Roof_Shingles': ('Roof_Shingles', 'world', 4.0, 4.0),
    'WG_Roof_ClayTiles': ('Roof_ClayTiles', 'world', 4.0, 4.0),
    'WG_Roof_Flat': ('Roof_Flat', 'world', 8.0, 8.0),
    'WG_Awning': (None, 'world', 1, 1),
    'WG_Sign': (None, 'world', 1, 1),
    'WG_Metal_Dark': (None, 'world', 1, 1),
    'WG_Paint_Yellow': (None, 'world', 1, 1),
    'WG_Steel': (None, 'world', 1, 1),
    'WG_Road_Arterial': ('Road_Arterial', 'explicit', 1, 1),
    'WG_Road_Arterial_Crosswalk': ('Road_Arterial_Crosswalk', 'explicit', 1, 1),
    'WG_Road_Local': ('Road_Local', 'explicit', 1, 1),
    'WG_Road_Local_Crosswalk': ('Road_Local_Crosswalk', 'explicit', 1, 1),
    'WG_Road_Rural': ('Road_Rural', 'explicit', 1, 1),
    'WG_Road_Junction': ('Road_Junction', 'explicit', 1, 1),
    'WG_Rail_Ballast': ('Rail_Ballast', 'explicit', 1, 1),
    'WG_Ground': ('Ground_Grass', 'explicit', 1, 1),
}
TINT = {'WG_Trim': (0.9, 0.9, 0.87), 'WG_Stucco': (0.95, 0.88, 0.74), 'WG_Metal_Wall': (0.62, 0.7, 0.78),
        'WG_Metal_Roof': (0.35, 0.37, 0.4), 'WG_Awning': (0.75, 0.15, 0.12), 'WG_Sign': (0.1, 0.1, 0.12),
        'WG_Metal_Dark': (0.2, 0.21, 0.22), 'WG_Paint_Yellow': (0.9, 0.72, 0.1), 'WG_Steel': (0.55, 0.56, 0.58),
        'WG_Facade_OfficeDark': (0.6, 0.62, 0.66)}


# ---------------------------------------------------------------- scene and materials

bpy.ops.wm.read_homefile(use_empty=True)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
assets = bpy.data.collections.new('WORLD | Assets')
scene.collection.children.link(assets)


def material(name):
    existing = bpy.data.materials.get(name)
    if existing:
        return existing
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    tint = TINT.get(name, (1, 1, 1))
    bsdf.inputs['Base Color'].default_value = (*tint, 1)
    bsdf.inputs['Roughness'].default_value = 0.7
    texture = UV[name][0]
    if texture:
        node = m.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(f'{TEX}/{texture}.png', check_existing=True)
        mix = m.node_tree.nodes.new('ShaderNodeMix')
        mix.data_type = 'RGBA'
        mix.blend_type = 'MULTIPLY'
        mix.inputs['Factor'].default_value = 1.0
        mix.inputs[7].default_value = (*tint, 1)
        m.node_tree.links.new(node.outputs['Color'], mix.inputs[6])
        m.node_tree.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
    m.diffuse_color = (*tint, 1)
    return m


# ---------------------------------------------------------------- mesh builder

class Builder:
    """Collects faces (outward-wound vertex loops) with a material and optional explicit UVs, then makes one object."""

    def __init__(self, name):
        self.name = name
        self.faces = []

    def face(self, points, mat, uvs=None):
        self.faces.append(([Vector(p) for p in points], mat, uvs))

    def quad(self, a, b, c, d, mat, uvs=None):
        self.face([a, b, c, d], mat, uvs)

    def box(self, x0, y0, z0, x1, y1, z1, mat, sides=None, skip=()):
        """Axis-aligned box; sides may override the material per side (front=-Y, back=+Y, left=-X, right=+X, top, bottom)."""
        sides = sides or {}
        p = [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0), (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]
        spec = {'front': (0, 1, 5, 4), 'right': (1, 2, 6, 5), 'back': (2, 3, 7, 6), 'left': (3, 0, 4, 7),
                'top': (4, 5, 6, 7), 'bottom': (3, 2, 1, 0)}
        for side, idx in spec.items():
            if side in skip:
                continue
            self.face([p[i] for i in idx], sides.get(side, mat))

    def prism_x(self, x0, x1, y0, y1, z0, ridge_z, mat_roof, mat_gable, overhang=0.0, thickness=0.12):
        """Gable roof with its ridge along X over the rectangle; gable triangles at both X ends."""
        ym = (y0 + y1) / 2
        ox = overhang
        # Slopes as thin slabs (top surface and eave edge), front then back.
        for ya, yb in ((y0 - ox, ym), (y1 + ox, ym)):
            drop = (ridge_z - z0) * ox / ((y1 - y0) / 2)
            za = z0 - drop
            lo = (x0 - ox, ya, za)
            lo2 = (x1 + ox, ya, za)
            hi2 = (x1 + ox, yb, ridge_z)
            hi = (x0 - ox, yb, ridge_z)
            if ya < ym:
                self.quad(lo, lo2, hi2, hi, mat_roof)
                self.quad((lo[0], lo[1], lo[2] - thickness), (lo2[0], lo2[1], lo2[2] - thickness), lo2, lo, mat_roof)
                self.quad((hi[0], hi[1], hi[2] - thickness), (hi2[0], hi2[1], hi2[2] - thickness), (lo2[0], lo2[1], lo2[2] - thickness),
                          (lo[0], lo[1], lo[2] - thickness), mat_roof)
            else:
                self.quad(lo2, lo, hi, hi2, mat_roof)
                self.quad((lo2[0], lo2[1], lo2[2] - thickness), (lo[0], lo[1], lo[2] - thickness), lo, lo2, mat_roof)
                self.quad((lo[0], lo[1], lo[2] - thickness), (lo2[0], lo2[1], lo2[2] - thickness), (hi2[0], hi2[1], hi2[2] - thickness),
                          (hi[0], hi[1], hi[2] - thickness), mat_roof)
        self.face([(x0, y1, z0), (x0, y0, z0), (x0, ym, ridge_z)], mat_gable)
        self.face([(x1, y0, z0), (x1, y1, z0), (x1, ym, ridge_z)], mat_gable)

    def hip(self, x0, x1, y0, y1, z0, ridge_z, mat, overhang=0.4):
        """Hip roof over the rectangle, ridge along the longer side."""
        x0, x1, y0, y1 = x0 - overhang, x1 + overhang, y0 - overhang, y1 + overhang
        w, d = x1 - x0, y1 - y0
        if w >= d:
            inset = d / 2
            r0, r1 = (x0 + inset, (y0 + y1) / 2, ridge_z), (x1 - inset, (y0 + y1) / 2, ridge_z)
            self.quad((x0, y0, z0), (x1, y0, z0), r1, r0, mat)
            self.quad((x1, y1, z0), (x0, y1, z0), r0, r1, mat)
            self.face([(x1, y0, z0), (x1, y1, z0), r1], mat)
            self.face([(x0, y1, z0), (x0, y0, z0), r0], mat)
        else:
            inset = w / 2
            r0, r1 = ((x0 + x1) / 2, y0 + inset, ridge_z), ((x0 + x1) / 2, y1 - inset, ridge_z)
            self.face([(x0, y0, z0), (x1, y0, z0), r0], mat)
            self.face([(x1, y1, z0), (x0, y1, z0), r1], mat)
            self.quad((x1, y0, z0), (x1, y1, z0), r1, r0, mat)
            self.quad((x0, y1, z0), (x0, y0, z0), r0, r1, mat)
        self.quad((x0, y1, z0), (x1, y1, z0), (x1, y0, z0), (x0, y0, z0), mat)  # soffit

    def cylinder(self, cx, cy, z0, z1, radius, segments, mat, cap_mat=None, cone=0.0):
        ring = [(cx + radius * math.cos(2 * math.pi * i / segments), cy + radius * math.sin(2 * math.pi * i / segments)) for i in range(segments)]
        for i in range(segments):
            a, b = ring[i], ring[(i + 1) % segments]
            self.quad((a[0], a[1], z0), (b[0], b[1], z0), (b[0], b[1], z1), (a[0], a[1], z1), mat)
        top = cap_mat or mat
        if cone > 0:
            for i in range(segments):
                a, b = ring[i], ring[(i + 1) % segments]
                self.face([(a[0], a[1], z1), (b[0], b[1], z1), (cx, cy, z1 + cone)], top)
        else:
            self.face([(x, y, z1) for x, y in ring], top)

    def build(self, location=(0, 0, 0)):
        mesh = bpy.data.meshes.new(self.name)
        bm = bmesh.new()
        uv_layer = bm.loops.layers.uv.new('UVMap')
        names = []
        for points, mat, _ in self.faces:
            if mat not in names:
                names.append(mat)
        for points, mat, uvs in self.faces:
            verts = [bm.verts.new(p) for p in points]
            face = bm.faces.new(verts)
            face.material_index = names.index(mat)
            face.normal_update()
            coords = uv_for(points, face.normal, mat, uvs)
            for loop, uv in zip(face.loops, coords):
                loop[uv_layer].uv = uv
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.to_mesh(mesh)
        bm.free()
        for name in names:
            mesh.materials.append(material(name))
        for poly in mesh.polygons:
            poly.use_smooth = False
        obj = bpy.data.objects.new(self.name, mesh)
        assets.objects.link(obj)
        obj.location = location
        return obj


def uv_for(points, normal, mat, explicit):
    if explicit:
        return explicit
    _, mode, tu, tv = UV[mat]
    n = Vector(normal)
    if abs(n.z) > 0.999:
        t, b = Vector((1, 0, 0)), Vector((0, 1, 0)) if n.z > 0 else Vector((0, -1, 0))
    else:
        t = Vector((0, 0, 1)).cross(n).normalized()
        b = n.cross(t).normalized()
    us = [p.dot(t) for p in points]
    vs = [p.dot(b) for p in points]
    if mode == 'single':
        u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
        return [((u - u0) / max(1e-4, u1 - u0), (v - v0) / max(1e-4, v1 - v0)) for u, v in zip(us, vs)]
    if mode == 'bay' and abs(n.z) < 0.2:
        # Whole bays across each wall; storeys counted from ground (z = 0 of the model).
        u0, u1 = min(us), max(us)
        bays = max(1, round((u1 - u0) / tu))
        return [((u - u0) / max(1e-4, u1 - u0) * bays, p.z / tv) for u, p in zip(us, points)]
    return [(u / tu, v / tv) for u, v in zip(us, vs)]


# ---------------------------------------------------------------- road and rail tiles (along +Y, 0..length)

ROAD = {'Arterial': (14.0, 2.0), 'Local': (10.0, 1.5), 'Rural': (8.0, 0.0)}
CURB = 0.15
ROAD_Z = 0.02


def road_tile(kind, crosswalk):
    width, side = ROAD[kind]
    mat = f'WG_Road_{kind}{"_Crosswalk" if crosswalk else ""}'
    b = Builder(f'Road_{kind}{"_Crosswalk" if crosswalk else ""}')
    length = 10.0
    half = width / 2

    def u(x):
        return (x + half) / width

    def strip(xa, xb, za, zb):
        b.quad((xa, 0, za), (xb, 0, zb), (xb, length, zb), (xa, length, za), mat,
               [(u(xa), 0), (u(xb), 0), (u(xb), 1), (u(xa), 1)])

    if side > 0:
        inner = half - side
        top = ROAD_Z + CURB
        strip(-inner, inner, ROAD_Z, ROAD_Z)
        # Curb faces, sidewalk tops, outer edges down to the ground.
        for sign in (-1, 1):
            curb_x = sign * inner
            walk_x = sign * half
            if sign < 0:
                strip(walk_x, curb_x, top, top)
                b.quad((curb_x, 0, top), (curb_x, 0, ROAD_Z), (curb_x, length, ROAD_Z), (curb_x, length, top), mat,
                       [(u(curb_x - 0.1), 0), (u(curb_x), 0), (u(curb_x), 1), (u(curb_x - 0.1), 1)])
                b.quad((walk_x, 0, 0), (walk_x, 0, top), (walk_x, length, top), (walk_x, length, 0), mat,
                       [(u(walk_x), 0), (u(walk_x + 0.05), 0), (u(walk_x + 0.05), 1), (u(walk_x), 1)])
            else:
                strip(curb_x, walk_x, top, top)
                b.quad((curb_x, 0, ROAD_Z), (curb_x, 0, top), (curb_x, length, top), (curb_x, length, ROAD_Z), mat,
                       [(u(curb_x), 0), (u(curb_x + 0.1), 0), (u(curb_x + 0.1), 1), (u(curb_x), 1)])
                b.quad((walk_x, 0, top), (walk_x, 0, 0), (walk_x, length, 0), (walk_x, length, top), mat,
                       [(u(walk_x - 0.05), 0), (u(walk_x), 0), (u(walk_x), 1), (u(walk_x - 0.05), 1)])
    else:
        # Asphalt crown with gravel shoulders sloping to the ground.
        strip(-half + 0.9, half - 0.9, ROAD_Z + 0.04, ROAD_Z + 0.04)
        strip(-half, -half + 0.9, 0.0, ROAD_Z + 0.04)
        strip(half - 0.9, half, ROAD_Z + 0.04, 0.0)
    return b.build()


def junction_tile():
    b = Builder('Road_Junction')
    b.quad((-0.5, -0.5, ROAD_Z + 0.005), (0.5, -0.5, ROAD_Z + 0.005), (0.5, 0.5, ROAD_Z + 0.005), (-0.5, 0.5, ROAD_Z + 0.005),
           'WG_Road_Junction', [(0, 0), (1, 0), (1, 1), (0, 1)])
    return b.build()


def ground_quad():
    b = Builder('Ground_Quad')
    b.quad((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0), 'WG_Ground', [(0, 0), (1, 0), (1, 1), (0, 1)])
    return b.build()


def rail_tile():
    b = Builder('Rail_Track')
    length, bottom, top, height = 6.0, 2.0, 1.5, 0.3
    m = 'WG_Rail_Ballast'

    def u(x):
        return (x + 2.0) / 4.0

    v1 = 1.0
    b.quad((-top, 0, height), (top, 0, height), (top, length, height), (-top, length, height), m,
           [(u(-top), 0), (u(top), 0), (u(top), v1), (u(-top), v1)])
    b.quad((-bottom, 0, 0), (-top, 0, height), (-top, length, height), (-bottom, length, 0), m,
           [(u(-bottom), 0), (u(-top), 0), (u(-top), v1), (u(-bottom), v1)])
    b.quad((top, 0, height), (bottom, 0, 0), (bottom, length, 0), (top, length, height), m,
           [(u(top), 0), (u(bottom), 0), (u(bottom), v1), (u(top), v1)])
    for x in (-0.7175, 0.7175):
        b.box(x - 0.036, 0, height + 0.02, x + 0.036, length, height + 0.17, 'WG_Steel', skip=('bottom', 'front', 'back'))
    return b.build()


# ---------------------------------------------------------------- buildings (front faces -Y, pivot at footprint centre)

def door(b, x, y, width, height, mat, z=0.0, depth=0.03):
    """A door panel standing just proud of a wall facing -Y at y."""
    b.quad((x - width / 2, y - depth, z), (x + width / 2, y - depth, z), (x + width / 2, y - depth, z + height),
           (x - width / 2, y - depth, z + height), mat)


def house_small(name, wall, roof):
    b = Builder(name)
    w, d, h = 10.0, 9.0, 3.0
    b.box(-w / 2, -d / 2, 0, w / 2, d / 2, h, wall, skip=('bottom', 'top'))
    b.box(-w / 2 - 0.05, -d / 2 - 0.05, 0, w / 2 + 0.05, d / 2 + 0.05, 0.35, 'WG_Concrete', skip=('bottom', 'top'))
    b.prism_x(-w / 2, w / 2, -d / 2, d / 2, h, h + 2.8, roof, 'WG_Trim', overhang=0.45)
    b.box(2.2, 0.8, h + 1.0, 3.0, 1.6, h + 3.4, 'WG_Concrete', skip=('bottom',))
    door(b, -1.6, -d / 2, 1.0, 2.2, 'WG_Door_Wood', z=0.15)
    b.box(-2.4, -d / 2 - 1.2, 0, -0.8, -d / 2, 0.15, 'WG_Concrete', skip=('bottom',))
    return b.build()


def house_large(name, wall, roof):
    b = Builder(name)
    w, d, h = 11.0, 12.0, 6.0
    x0 = -7.5
    b.box(x0, -d / 2, 0, x0 + w, d / 2, h, wall, skip=('bottom', 'top'))
    b.box(x0 - 0.05, -d / 2 - 0.05, 0, x0 + w + 0.05, d / 2 + 0.05, 0.4, 'WG_Concrete', skip=('bottom', 'top'))
    b.hip(x0, x0 + w, -d / 2, d / 2, h, h + 2.6, roof, overhang=0.5)
    door(b, x0 + w / 2, -d / 2, 1.1, 2.3, 'WG_Door_Wood', z=0.2)
    b.box(x0 + w / 2 - 1.2, -d / 2 - 1.6, 0, x0 + w / 2 + 1.2, -d / 2, 0.2, 'WG_Concrete', skip=('bottom',))
    # Porch roof on two posts.
    b.box(x0 + w / 2 - 1.5, -d / 2 - 1.8, 2.7, x0 + w / 2 + 1.5, -d / 2, 2.9, 'WG_Trim', skip=('back',))
    for px in (-1.3, 1.3):
        b.box(x0 + w / 2 + px - 0.1, -d / 2 - 1.7, 0.2, x0 + w / 2 + px + 0.1, -d / 2 - 1.5, 2.7, 'WG_Trim', skip=('bottom', 'top'))
    # Garage beside it with a roller door and a flat roof.
    gx0, gx1 = x0 + w + 0.2, 7.5
    b.box(gx0, -d / 2 + 1.0, 0, gx1, d / 2 - 2.0, 3.0, wall, sides={'top': 'WG_Roof_Flat'}, skip=('bottom',))
    door(b, (gx0 + gx1) / 2, -d / 2 + 1.0, 3.0, 2.4, 'WG_Door_Roller')
    return b.build()


def apartment(prefix, wall):
    w, d = 18.0, 15.0
    ground = Builder(f'{prefix}_Ground')
    ground.box(-w / 2, -d / 2, 0, w / 2, d / 2, 3.0, wall, skip=('bottom', 'top'))
    ground.box(-w / 2 - 0.08, -d / 2 - 0.08, 0, w / 2 + 0.08, d / 2 + 0.08, 0.6, 'WG_Concrete', skip=('bottom', 'top'))
    door(ground, 0, -d / 2, 2.0, 2.5, 'WG_Door_Glass')
    ground.box(-1.8, -d / 2 - 1.4, 2.6, 1.8, -d / 2, 2.8, 'WG_Concrete', skip=('back',))
    middle = Builder(f'{prefix}_Middle')
    middle.box(-w / 2, -d / 2, 0, w / 2, d / 2, 3.0, wall, skip=('bottom', 'top'))
    middle.box(-w / 2 - 0.1, -d / 2 - 0.1, 0, w / 2 + 0.1, d / 2 + 0.1, 0.18, 'WG_Concrete', skip=('bottom',))
    roof = Builder(f'{prefix}_Roof')
    roof.box(-w / 2, -d / 2, 0, w / 2, d / 2, 0.05, 'WG_Roof_Flat', skip=('bottom', 'front', 'back', 'left', 'right'))
    for x0, y0, x1, y1 in ((-w / 2, -d / 2, w / 2, -d / 2 + 0.3), (-w / 2, d / 2 - 0.3, w / 2, d / 2),
                           (-w / 2, -d / 2 + 0.3, -w / 2 + 0.3, d / 2 - 0.3), (w / 2 - 0.3, -d / 2 + 0.3, w / 2, d / 2 - 0.3)):
        roof.box(x0, y0, 0, x1, y1, 1.0, 'WG_Concrete', skip=('bottom',))
    roof.box(2.0, 1.0, 0, 5.5, 4.0, 2.6, 'WG_Concrete', skip=('bottom',))
    return [ground.build(), middle.build(), roof.build()]


def office(prefix, glass):
    w, d = 22.0, 19.0
    ground = Builder(f'{prefix}_Ground')
    ground.box(-w / 2, -d / 2, 0, w / 2, d / 2, 3.5, glass, skip=('bottom', 'top'))
    door(ground, 0, -d / 2, 2.0, 2.5, 'WG_Door_Glass')
    ground.box(-3.0, -d / 2 - 2.5, 3.0, 3.0, -d / 2, 3.25, 'WG_Metal_Dark', skip=('back',))
    middle = Builder(f'{prefix}_Middle')
    middle.box(-w / 2, -d / 2, 0, w / 2, d / 2, 3.5, glass, skip=('bottom', 'top'))
    roof = Builder(f'{prefix}_Roof')
    roof.box(-w / 2, -d / 2, 0, w / 2, d / 2, 0.05, 'WG_Roof_Flat', skip=('bottom', 'front', 'back', 'left', 'right'))
    for x0, y0, x1, y1 in ((-w / 2, -d / 2, w / 2, -d / 2 + 0.4), (-w / 2, d / 2 - 0.4, w / 2, d / 2),
                           (-w / 2, -d / 2 + 0.4, -w / 2 + 0.4, d / 2 - 0.4), (w / 2 - 0.4, -d / 2 + 0.4, w / 2, d / 2 - 0.4)):
        roof.box(x0, y0, 0, x1, y1, 1.2, 'WG_Metal_Dark', skip=('bottom',))
    roof.box(-5.0, -3.0, 0, 5.0, 4.0, 3.2, 'WG_Metal_Wall', sides={'top': 'WG_Roof_Flat'}, skip=('bottom',))
    return [ground.build(), middle.build(), roof.build()]


def restaurant(name, side_wall):
    b = Builder(name)
    w, d, h = 12.0, 11.0, 4.5
    b.box(-w / 2, -d / 2, 0, w / 2, d / 2, h, side_wall, sides={'front': 'WG_Facade_Storefront'}, skip=('bottom', 'top'))
    b.box(-w / 2, -d / 2, h, w / 2, d / 2, h + 0.05, 'WG_Roof_Flat', skip=('bottom', 'front', 'back', 'left', 'right'))
    for x0, y0, x1, y1 in ((-w / 2, -d / 2, w / 2, -d / 2 + 0.3), (-w / 2, d / 2 - 0.3, w / 2, d / 2),
                           (-w / 2, -d / 2 + 0.3, -w / 2 + 0.3, d / 2 - 0.3), (w / 2 - 0.3, -d / 2 + 0.3, w / 2, d / 2 - 0.3)):
        b.box(x0, y0, h, x1, y1, h + 0.8, 'WG_Trim', skip=('bottom',))
    door(b, 0, -d / 2, 2.0, 2.5, 'WG_Door_Glass')
    # Awning (tinted per owner in Unity through its own material slot) and a sign board above it.
    b.quad((-w / 2 + 0.3, -d / 2 - 1.6, 2.9), (w / 2 - 0.3, -d / 2 - 1.6, 2.9), (w / 2 - 0.3, -d / 2, 3.5), (-w / 2 + 0.3, -d / 2, 3.5), 'WG_Awning')
    b.quad((-w / 2 + 0.3, -d / 2 - 1.6, 2.6), (w / 2 - 0.3, -d / 2 - 1.6, 2.6), (w / 2 - 0.3, -d / 2 - 1.6, 2.9), (-w / 2 + 0.3, -d / 2 - 1.6, 2.9), 'WG_Awning')
    b.quad((w / 2 - 0.3, -d / 2 - 1.6, 2.9), (-w / 2 + 0.3, -d / 2 - 1.6, 2.9), (-w / 2 + 0.3, -d / 2, 3.5), (w / 2 - 0.3, -d / 2, 3.5), 'WG_Awning')
    b.box(-3.5, -d / 2 - 0.15, 3.6, 3.5, -d / 2, 4.3, 'WG_Sign', skip=('back',))
    b.box(-3.0, 0.0, h + 0.05, -1.0, 2.0, h + 1.0, 'WG_Metal_Wall', skip=('bottom',))
    return b.build()


def factory(name, pitched):
    b = Builder(name)
    w, d, h = 30.0, 26.0, 8.0
    b.box(-w / 2, -d / 2, 0, w / 2, d / 2, h, 'WG_Metal_Wall', skip=('bottom', 'top'))
    b.box(-w / 2 - 0.05, -d / 2 - 0.05, 0, w / 2 + 0.05, d / 2 + 0.05, 0.8, 'WG_Concrete', skip=('bottom', 'top'))
    # Window bands on the front and both sides.
    for x0, x1 in ((-w / 2 + 1, -2.5), (2.5, w / 2 - 1)):
        b.quad((x0, -d / 2 - 0.03, 5.2), (x1, -d / 2 - 0.03, 5.2), (x1, -d / 2 - 0.03, 6.7), (x0, -d / 2 - 0.03, 6.7), 'WG_Window_Industrial')
    for sx in (-1, 1):
        x = sx * (w / 2 + 0.03)
        y0, y1 = -d / 2 + 1, d / 2 - 1
        pts = [(x, y1, 5.2), (x, y0, 5.2), (x, y0, 6.7), (x, y1, 6.7)] if sx < 0 else [(x, y0, 5.2), (x, y1, 5.2), (x, y1, 6.7), (x, y0, 6.7)]
        b.face(pts, 'WG_Window_Industrial')
    for x in (-8.0, 8.0):
        door(b, x, -d / 2, 4.5, 4.8, 'WG_Door_Roller')
    door(b, 0, -d / 2, 1.0, 2.2, 'WG_Door_Wood')
    b.box(-1.0, -d / 2 - 1.2, 2.6, 1.0, -d / 2, 2.75, 'WG_Metal_Dark', skip=('back',))
    if pitched:
        b.prism_x(-w / 2, w / 2, -d / 2, d / 2, h, h + 2.5, 'WG_Metal_Roof', 'WG_Metal_Wall', overhang=0.3, thickness=0.15)
        for x in (-8.0, 0.0, 8.0):
            b.box(x - 1.0, -0.8, h + 2.2, x + 1.0, 0.8, h + 3.0, 'WG_Metal_Dark', skip=('bottom',))
    else:
        b.box(-w / 2, -d / 2, h, w / 2, d / 2, h + 0.05, 'WG_Roof_Flat', skip=('bottom', 'front', 'back', 'left', 'right'))
        for x0, y0, x1, y1 in ((-w / 2, -d / 2, w / 2, -d / 2 + 0.3), (-w / 2, d / 2 - 0.3, w / 2, d / 2),
                               (-w / 2, -d / 2 + 0.3, -w / 2 + 0.3, d / 2 - 0.3), (w / 2 - 0.3, -d / 2 + 0.3, w / 2, d / 2 - 0.3)):
            b.box(x0, y0, h, x1, y1, h + 0.9, 'WG_Metal_Dark', skip=('bottom',))
        for x in (-9.0, 0.0, 9.0):
            b.box(x - 2.0, -3.0, h, x + 2.0, 3.0, h + 1.8, 'WG_Metal_Wall', sides={'top': 'WG_Roof_Flat'}, skip=('bottom',))
        b.cylinder(11.0, 8.0, h, h + 6.0, 0.6, 8, 'WG_Steel')
    return b.build()


def barn(name, wall):
    b = Builder(name)
    w, d, h = 12.0, 16.0, 4.5
    b.box(-w / 2, -d / 2, 0, w / 2, d / 2, h, wall, skip=('bottom', 'top'))
    # Gambrel roof: ridge along Y (door in the gable end facing -Y).
    kx, kz, rz = 4.4, h + 2.6, h + 4.4
    profile = [(-w / 2 - 0.3, h - 0.2), (-kx, kz), (0, rz), (kx, kz), (w / 2 + 0.3, h - 0.2)]
    # Outer surface, and an underside 0.12 m below it (coincident reversed faces would be merged away).
    for (xa, za), (xb, zb) in zip(profile, profile[1:]):
        b.quad((xa, -d / 2 - 0.3, za), (xb, -d / 2 - 0.3, zb), (xb, d / 2 + 0.3, zb), (xa, d / 2 + 0.3, za), 'WG_Metal_Roof')
        b.quad((xb, -d / 2 - 0.3, zb - 0.12), (xa, -d / 2 - 0.3, za - 0.12), (xa, d / 2 + 0.3, za - 0.12), (xb, d / 2 + 0.3, zb - 0.12), 'WG_Metal_Roof')
    gable = [(-w / 2, h), (-kx, kz - 0.1), (0, rz - 0.15), (kx, kz - 0.1), (w / 2, h)]
    b.face([(x, -d / 2, z) for x, z in reversed(gable)], wall)
    b.face([(x, d / 2, z) for x, z in gable], wall)
    door(b, 0, -d / 2, 4.0, 4.0, 'WG_Wood_Barn', depth=0.06)
    for x0, z0, x1, z1 in ((-2.1, 0, -1.9, 4.0), (1.9, 0, 2.1, 4.0), (-2.1, 3.9, 2.1, 4.1)):
        b.quad((x0, -d / 2 - 0.08, z0), (x1, -d / 2 - 0.08, z0), (x1, -d / 2 - 0.08, z1), (x0, -d / 2 - 0.08, z1), 'WG_Trim')
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * w / 2, sy * d / 2
            b.box(x - 0.12, y - 0.12, 0, x + 0.12, y + 0.12, h, 'WG_Trim', skip=('bottom',))
    # Silo beside the barn.
    b.cylinder(w / 2 + 3.2, d / 2 - 3.0, 0, 11.0, 2.4, 10, 'WG_Metal_Wall', 'WG_Metal_Roof', cone=1.6)
    return b.build()


def station(name):
    b = Builder(name)
    w, d, top = 10.0, 30.0, 1.0
    b.box(-w / 2, -d / 2 + 4, 0, w / 2, d / 2, top, 'WG_Concrete', skip=('bottom',))
    # Ramp down to the road at the front (-Y) end.
    b.quad((-w / 2 + 2, -d / 2, 0.02), (w / 2 - 2, -d / 2, 0.02), (w / 2 - 2, -d / 2 + 4, top), (-w / 2 + 2, -d / 2 + 4, top), 'WG_Concrete')
    b.face([(-w / 2 + 2, -d / 2 + 4, 0), (-w / 2 + 2, -d / 2, 0.02), (-w / 2 + 2, -d / 2 + 4, top)], 'WG_Concrete')
    b.face([(w / 2 - 2, -d / 2, 0.02), (w / 2 - 2, -d / 2 + 4, 0), (w / 2 - 2, -d / 2 + 4, top)], 'WG_Concrete')
    # Safety line along the track-side edge (+X).
    b.quad((w / 2 - 0.6, -d / 2 + 4, top + 0.005), (w / 2 - 0.3, -d / 2 + 4, top + 0.005), (w / 2 - 0.3, d / 2, top + 0.005), (w / 2 - 0.6, d / 2, top + 0.005), 'WG_Paint_Yellow')
    # Shelter: posts, roof, glazed back wall, benches, sign.
    for y in (-2.0, 4.0, 10.0):
        b.box(-3.6, y - 0.1, top, -3.4, y + 0.1, top + 3.0, 'WG_Metal_Dark', skip=('bottom', 'top'))
        b.box(1.4, y - 0.1, top, 1.6, y + 0.1, top + 3.0, 'WG_Metal_Dark', skip=('bottom', 'top'))
    b.box(-4.2, -3.0, top + 3.0, 2.2, 11.0, top + 3.2, 'WG_Metal_Roof')
    glass = [(-3.52, 10.0, top + 0.1), (-3.52, -2.0, top + 0.1), (-3.52, -2.0, top + 2.6), (-3.52, 10.0, top + 2.6)]
    b.face(glass, 'WG_Door_Glass')
    b.face([(x + 0.04, y, z) for x, y, z in reversed(glass)], 'WG_Door_Glass')
    for y in (0.0, 6.0):
        b.box(-3.2, y, top + 0.45, -2.6, y + 2.5, top + 0.55, 'WG_Wood_Barn')
    b.box(-0.8, -d / 2 + 4.2, top, -0.6, -d / 2 + 4.4, top + 2.6, 'WG_Metal_Dark', skip=('bottom',))
    b.box(-1.8, -d / 2 + 4.15, top + 2.6, 0.4, -d / 2 + 4.45, top + 3.3, 'WG_Sign')
    return b.build()


# ---------------------------------------------------------------- build all, lay out for preview, export

objects = []
objects += [road_tile('Arterial', False), road_tile('Arterial', True), road_tile('Local', False), road_tile('Local', True),
            road_tile('Rural', False), junction_tile(), rail_tile(), ground_quad()]
objects += [house_small('House_Small_a', 'WG_Facade_Brick', 'WG_Roof_Shingles'),
            house_small('House_Small_b', 'WG_Facade_Siding', 'WG_Roof_Shingles'),
            house_small('House_Small_c', 'WG_Facade_Plaster', 'WG_Roof_ClayTiles'),
            house_large('House_Large_a', 'WG_Facade_Brick', 'WG_Roof_Shingles'),
            house_large('House_Large_b', 'WG_Facade_Siding', 'WG_Roof_ClayTiles'),
            house_large('House_Large_c', 'WG_Facade_Plaster', 'WG_Roof_ClayTiles')]
objects += apartment('Apartment_a', 'WG_Facade_Apartment') + apartment('Apartment_b', 'WG_Facade_Brick')
objects += office('Office_a', 'WG_Facade_Office') + office('Office_b', 'WG_Facade_OfficeDark')
objects += [restaurant('Restaurant_a', 'WG_Stucco'), restaurant('Restaurant_b', 'WG_Facade_Brick'),
            factory('Factory_a', True), factory('Factory_b', False),
            barn('Barn_a', 'WG_Wood_Barn'), barn('Barn_b', 'WG_Metal_Wall'), station('Station')]

x = 0.0
for obj in objects:
    size = max(obj.dimensions.x, 8)
    obj.location = (x + size / 2, 0, 0)
    x += size + 6

bpy.ops.wm.save_as_mainfile(filepath=f'{ROOT}/World_Assets.blend')
os.makedirs(f'{ROOT}/Export', exist_ok=True)
# Object locations are layout only; Unity uses each mesh in its own space. read_homefile leaves no window context, so the
# exporter runs under an explicit override.
window = bpy.context.window_manager.windows[0]
with bpy.context.temp_override(window=window, screen=window.screen, area=window.screen.areas[0], view_layer=scene.view_layers[0]):
    for obj in bpy.data.objects:
        obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=f'{ROOT}/Export/WorldArt.fbx', use_selection=True, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True, axis_forward='-Z', axis_up='Y',
                             object_types={'MESH'}, mesh_smooth_type='FACE', use_mesh_modifiers=True, path_mode='STRIP',
                             embed_textures=False, add_leaf_bones=False)
result = {'objects': [(o.name, len(o.data.polygons), sum(len(p.vertices) - 2 for p in o.data.polygons)) for o in objects]}
