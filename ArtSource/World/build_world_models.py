"""Low-poly world art (decision 0026 presentation): road and rail tiles, buildings, farms, station, ground quad.

Run inside Blender after build_world_textures.py, with WORLD_ART_ROOT set to this folder when the project is not at the
default path:
    WORLD_ART_ROOT = r'E:/Projects/Unity/FoodFactoryGame/ArtSource/World'
    exec(open(WORLD_ART_ROOT + '/build_world_models.py').read())
Builds every asset into a fresh scene, saves ArtSource/World/World_Assets.blend and exports
ArtSource/World/Export/WorldArt.fbx (all assets, one object each). Unity reads meshes by object name.

Conventions (Blender, metres, Z up): every building's front (street side, doors) faces -Y; X runs along the street; the
pivot is the footprint centre at ground level. Stacked modules (apartments, offices) have their base at z = 0. Road and rail
tiles run along +Y from y = 0 to their tile length, centred across X, cut into short segments along Y so Unity can drape
them over the land, with skirts reaching below ground to hide seams against the terrain. Pieces placed on the land
(signals, signs, trees) stand on z = 0; the foundation, bridge deck and pier hang below it. Materials are named WG_* and map one-to-one to Unity
materials; UVs are world-scaled per material (see UV below), with facade materials snapped to whole window bays per wall.
"""
import math
import random
import os
import bmesh
import bpy
from mathutils import Vector

ROOT = globals().get('WORLD_ART_ROOT', 'G:/Unity/FoodFactoryGame/ArtSource/World')
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
    # Generator v2 additions (2026-09-28).
    'WG_Bark': ('Bark', 'world', 1.0, 1.0),
    'WG_Foliage': ('Foliage', 'world', 2.0, 2.0),
    'WG_Foliage_Dark': ('Foliage', 'world', 2.0, 2.0),
    'WG_Foliage_Light': ('Foliage', 'world', 2.0, 2.0),
    'WG_Sign_Stop': ('Sign_Stop', 'single', 1, 1),
    'WG_Sign_Crossbuck': ('Sign_Crossbuck', 'explicit', 1, 1),
    'WG_Lamp_Red': (None, 'world', 1, 1),
    'WG_Lamp_Amber': (None, 'world', 1, 1),
    'WG_Lamp_Green': (None, 'world', 1, 1),
    'WG_Paint_Red': (None, 'world', 1, 1),
}
TINT = {'WG_Trim': (0.9, 0.9, 0.87), 'WG_Stucco': (0.95, 0.88, 0.74), 'WG_Metal_Wall': (0.62, 0.7, 0.78),
        'WG_Metal_Roof': (0.35, 0.37, 0.4), 'WG_Awning': (0.75, 0.15, 0.12), 'WG_Sign': (0.1, 0.1, 0.12),
        'WG_Metal_Dark': (0.2, 0.21, 0.22), 'WG_Paint_Yellow': (0.9, 0.72, 0.1), 'WG_Steel': (0.55, 0.56, 0.58),
        'WG_Facade_OfficeDark': (0.6, 0.62, 0.66), 'WG_Foliage_Dark': (0.55, 0.75, 0.6), 'WG_Foliage_Light': (1.0, 1.0, 0.7),
        'WG_Lamp_Red': (0.85, 0.08, 0.05), 'WG_Lamp_Amber': (0.9, 0.55, 0.05), 'WG_Lamp_Green': (0.1, 0.8, 0.35),
        'WG_Paint_Red': (0.75, 0.06, 0.05)}


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
# Tiles are cut into this many pieces along their length so Unity can drape them over the land; skirts reach this deep.
SEGMENTS = 4
SKIRT = -0.6
ROAD_Z = 0.02


def road_tile(kind, crosswalk):
    width, side = ROAD[kind]
    mat = f'WG_Road_{kind}{"_Crosswalk" if crosswalk else ""}'
    b = Builder(f'Road_{kind}{"_Crosswalk" if crosswalk else ""}')
    length = 10.0
    half = width / 2

    def u(x):
        return (x + half) / width

    def strip(xa, za, xb, zb, ua=None, ub=None):
        """A face from (xa, za) to (xb, zb) across the tile, cut into SEGMENTS along it so it can follow the land."""
        ua = u(xa) if ua is None else ua
        ub = u(xb) if ub is None else ub
        for s in range(SEGMENTS):
            y0, y1 = length * s / SEGMENTS, length * (s + 1) / SEGMENTS
            b.quad((xa, y0, za), (xb, y0, zb), (xb, y1, zb), (xa, y1, za), mat,
                   [(ua, y0 / length), (ub, y0 / length), (ub, y1 / length), (ua, y1 / length)])

    if side > 0:
        inner = half - side
        top = ROAD_Z + CURB
        strip(-inner, ROAD_Z, inner, ROAD_Z)
        # Sidewalk tops, curb faces, and outer edges reaching down below ground as skirts.
        strip(-half, top, -inner, top)
        strip(-inner, top, -inner, ROAD_Z, u(-inner - 0.1), u(-inner))
        strip(-half, SKIRT, -half, top, u(-half), u(-half + 0.05))
        strip(inner, top, half, top)
        strip(inner, ROAD_Z, inner, top, u(inner), u(inner + 0.1))
        strip(half, top, half, SKIRT, u(half - 0.05), u(half))
    else:
        # Asphalt crown with gravel shoulders sloping to the ground, then skirts.
        strip(-half + 0.9, ROAD_Z + 0.04, half - 0.9, ROAD_Z + 0.04)
        strip(-half, 0.0, -half + 0.9, ROAD_Z + 0.04)
        strip(half - 0.9, ROAD_Z + 0.04, half, 0.0)
        strip(-half, SKIRT, -half, 0.0, u(-half), u(-half + 0.05))
        strip(half, 0.0, half, SKIRT, u(half - 0.05), u(half))
    return b.build()


def junction_tile():
    """Unit square, cut 4 x 4 so a scaled-up junction can follow the land."""
    b = Builder('Road_Junction')
    n = 4
    z = ROAD_Z + 0.005
    for i in range(n):
        for j in range(n):
            x0, x1, y0, y1 = -0.5 + i / n, -0.5 + (i + 1) / n, -0.5 + j / n, -0.5 + (j + 1) / n
            b.quad((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z), 'WG_Road_Junction',
                   [(x0 + 0.5, y0 + 0.5), (x1 + 0.5, y0 + 0.5), (x1 + 0.5, y1 + 0.5), (x0 + 0.5, y1 + 0.5)])
    return b.build()


def ground_quad():
    b = Builder('Ground_Quad')
    b.quad((-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0), 'WG_Ground', [(0, 0), (1, 0), (1, 1), (0, 1)])
    return b.build()


RAIL_HEIGHT = 0.15
RAIL_GAUGE = 0.7175


def rails(b, length, segments, z):
    """Two rails along +Y on the given base height, cut into segments."""
    for x in (-RAIL_GAUGE, RAIL_GAUGE):
        for s in range(segments):
            y0, y1 = length * s / segments, length * (s + 1) / segments
            b.box(x - 0.036, y0, z, x + 0.036, y1, z + 0.12, 'WG_Steel', skip=('bottom', 'front', 'back'))


def rail_tile():
    b = Builder('Rail_Track')
    length, bottom, top, height = 6.0, 2.0, 1.5, RAIL_HEIGHT
    m = 'WG_Rail_Ballast'
    segments = 3

    def u(x):
        return (x + 2.0) / 4.0

    for s in range(segments):
        y0, y1 = length * s / segments, length * (s + 1) / segments
        v0, v1 = y0 / length, y1 / length
        b.quad((-top, y0, height), (top, y0, height), (top, y1, height), (-top, y1, height), m,
               [(u(-top), v0), (u(top), v0), (u(top), v1), (u(-top), v1)])
        b.quad((-bottom, y0, 0), (-top, y0, height), (-top, y1, height), (-bottom, y1, 0), m,
               [(u(-bottom), v0), (u(-top), v0), (u(-top), v1), (u(-bottom), v1)])
        b.quad((top, y0, height), (bottom, y0, 0), (bottom, y1, 0), (top, y1, height), m,
               [(u(top), v0), (u(bottom), v0), (u(bottom), v1), (u(top), v1)])
        b.quad((-bottom, y0, SKIRT), (-bottom, y0, 0), (-bottom, y1, 0), (-bottom, y1, SKIRT), m,
               [(0, v0), (0.01, v0), (0.01, v1), (0, v1)])
        b.quad((bottom, y0, 0), (bottom, y0, SKIRT), (bottom, y1, SKIRT), (bottom, y1, 0), m,
               [(0.99, v0), (1, v0), (1, v1), (0.99, v1)])
    rails(b, length, segments, height)
    return b.build()


def rail_crossing():
    """Level crossing panel: 10 m along +Y (scaled to the road's width), ramped across the track, rails flush with its top."""
    b = Builder('Rail_Crossing')
    length, outer, inner, top = 10.0, 2.6, 1.3, RAIL_HEIGHT + 0.1
    profile = [(-outer, ROAD_Z), (-inner, top), (inner, top), (outer, ROAD_Z)]
    for s in range(SEGMENTS):
        y0, y1 = length * s / SEGMENTS, length * (s + 1) / SEGMENTS
        for (xa, za), (xb, zb) in zip(profile, profile[1:]):
            b.quad((xa, y0, za), (xb, y0, zb), (xb, y1, zb), (xa, y1, za), 'WG_Concrete')
    rails(b, length, SEGMENTS, top - 0.1)
    return b.build()


# ---------------------------------------------------------------- road furniture (faces -Y, toward approaching traffic)

def octagon(apothem, cx, y, cz):
    """Regular octagon with a flat top in the plane y, wound to face -Y."""
    r = apothem / math.cos(math.pi / 8)
    return [(cx + r * math.cos(math.pi / 8 + k * math.pi / 4), y, cz + r * math.sin(math.pi / 8 + k * math.pi / 4)) for k in range(8)]


def disc(b, cx, y, cz, radius, mat, sides=8):
    b.face([(cx + radius * math.cos(2 * math.pi * k / sides), y, cz + radius * math.sin(2 * math.pi * k / sides)) for k in range(sides)], mat)


def stop_sign():
    b = Builder('Stop_Sign')
    b.box(-0.03, -0.03, 0, 0.03, 0.03, 2.1, 'WG_Steel', skip=('bottom',))
    face = octagon(0.375, 0, -0.05, 2.45)
    b.face(face, 'WG_Sign_Stop')
    b.face([(x, -0.03, z) for x, _, z in reversed(face)], 'WG_Steel')
    for a, c in zip(face, face[1:] + face[:1]):
        b.quad((c[0], -0.05, c[2]), (a[0], -0.05, a[2]), (a[0], -0.03, a[2]), (c[0], -0.03, c[2]), 'WG_Steel')
    return b.build()


def signal_head(b, x, z, y=-0.18):
    """Three-lamp signal head centred at (x, z), lenses facing -Y, each under a hood."""
    b.box(x - 0.17, y, z - 0.5, x + 0.17, y + 0.3, z + 0.5, 'WG_Metal_Dark')
    for dz, mat in ((0.3, 'WG_Lamp_Red'), (0.0, 'WG_Lamp_Amber'), (-0.3, 'WG_Lamp_Green')):
        disc(b, x, y - 0.01, z + dz, 0.11, mat)
        b.box(x - 0.14, y - 0.16, z + dz + 0.11, x + 0.14, y, z + dz + 0.13, 'WG_Metal_Dark', skip=('back',))


def traffic_light(name, arm):
    """Pole on the approach's kerb with a mast arm reaching over the lanes to the driver's left (-X seen from the front)."""
    b = Builder(name)
    b.cylinder(0, 0, 0, 6.6, 0.13, 8, 'WG_Metal_Dark')
    b.box(-0.35, -0.35, 0, 0.35, 0.35, 0.25, 'WG_Concrete', skip=('bottom',))
    b.box(-arm, -0.07, 6.0, 0.05, 0.07, 6.18, 'WG_Metal_Dark')
    heads = (arm * 0.55, arm - 0.3) if arm > 4.5 else (arm - 0.4,)
    for offset in heads:
        signal_head(b, -offset, 5.35)
    # A lower head on the pole for the first car at the line.
    signal_head(b, 0.0, 3.2, y=-0.48)
    return b.build()


def crossing_signal():
    """Crossbuck with twin red lamps and a raised gate arm, facing approaching traffic."""
    b = Builder('Rail_Signal')
    b.box(-0.3, -0.3, 0, 0.3, 0.3, 0.2, 'WG_Concrete', skip=('bottom',))
    b.cylinder(0, 0, 0.2, 4.3, 0.07, 8, 'WG_Steel')
    # Crossbuck boards at 45 degrees: RAILROAD on one, CROSSING on the other (explicit UVs into the texture's halves).
    for angle, (v0, v1), y in ((45, (0.5, 1.0), -0.09), (-45, (0.0, 0.5), -0.11)):
        a = math.radians(angle)
        ca, sa = math.cos(a), math.sin(a)
        cz, hl, hw = 3.7, 0.62, 0.11

        def p(s, t, depth):
            return (s * ca - t * sa, depth, cz + s * sa + t * ca)

        b.quad(p(-hl, -hw, y), p(hl, -hw, y), p(hl, hw, y), p(-hl, hw, y), 'WG_Sign_Crossbuck', [(0, v0), (1, v0), (1, v1), (0, v1)])
        b.quad(p(-hl, hw, y + 0.015), p(hl, hw, y + 0.015), p(hl, -hw, y + 0.015), p(-hl, -hw, y + 0.015), 'WG_Steel')
    # Lamp bar with two red lamps in round housings.
    b.box(-0.6, -0.08, 2.75, 0.6, 0.0, 2.82, 'WG_Metal_Dark')
    for x in (-0.45, 0.45):
        b.box(x - 0.2, -0.16, 2.4, x + 0.2, -0.08, 2.8, 'WG_Metal_Dark')
        disc(b, x, -0.17, 2.6, 0.13, 'WG_Lamp_Red', sides=10)
    # Gate mechanism beside the pole with its arm raised, in red and white bands.
    b.box(0.35, -0.2, 0, 0.75, 0.2, 1.1, 'WG_Metal_Dark', skip=('bottom',))
    for k in range(8):
        b.box(0.5, -0.05, 1.1 + k * 0.55, 0.6, 0.05, 1.1 + (k + 1) * 0.55, 'WG_Paint_Red' if k % 2 == 0 else 'WG_Trim',
              skip=('bottom',) if k == 7 else ('top',) if k == 0 else ('top', 'bottom'))
    return b.build()


# ---------------------------------------------------------------- bridges and foundations

def bridge_railing():
    """Concrete parapet with a steel top rail, 10 m along +Y; its faces run down as the deck fascia."""
    b = Builder('Bridge_Railing')
    length = 10.0
    for s in range(SEGMENTS):
        y0, y1 = length * s / SEGMENTS, length * (s + 1) / SEGMENTS
        b.box(-0.15, y0, -1.3, 0.15, y1, 0.95, 'WG_Concrete', skip=('front', 'back', 'bottom'))
        b.box(-0.06, y0, 0.95, 0.06, y1, 1.1, 'WG_Steel', skip=('front', 'back', 'bottom'))
    return b.build()


def bridge_deck():
    """Deck slab under the carried way: unit width (scaled to it), 10 m along +Y, from just under the road down 1.3 m."""
    b = Builder('Bridge_Deck')
    length = 10.0
    for s in range(SEGMENTS):
        y0, y1 = length * s / SEGMENTS, length * (s + 1) / SEGMENTS
        b.box(-0.5, y0, -1.3, 0.5, y1, -0.03, 'WG_Concrete', skip=('front', 'back', 'top'))
    return b.build()


def bridge_pier():
    """Pier wall under a deck: unit width across the way and unit height below its top (both scaled in Unity)."""
    b = Builder('Bridge_Pier')
    b.box(-0.5, -0.6, -1.0, 0.5, 0.6, 0.0, 'WG_Concrete', skip=('top', 'bottom'))
    return b.build()


def foundation():
    """Plinth under a building on sloping land: unit footprint, top at 0, unit depth below (scaled in Unity)."""
    b = Builder('Foundation')
    b.box(-0.5, -0.5, -1.0, 0.5, 0.5, 0.0, 'WG_Concrete', skip=('top', 'bottom'))
    return b.build()


# ---------------------------------------------------------------- trees (faceted canopies, standing on z = 0)

JITTER = random.Random(20260928)


def blob(b, cx, cy, cz, rx, ry, rz, mat, subdivisions=2, jitter=0.14):
    """A lumpy ellipsoid canopy: an icosphere (subdivisions 1 = 20 faces, 2 = 80) with seeded vertex jitter."""
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdivisions, radius=1.0)
    bm.verts.ensure_lookup_table()
    bm.verts.index_update()
    scale = [1 + JITTER.uniform(-jitter, jitter) for _ in bm.verts]
    for f in bm.faces:
        b.face([(cx + v.co.x * rx * scale[v.index], cy + v.co.y * ry * scale[v.index], cz + v.co.z * rz * scale[v.index]) for v in f.verts], mat)
    bm.free()


def cone(b, cx, cy, z0, z1, radius, segments, mat):
    ring = [(cx + radius * math.cos(2 * math.pi * i / segments), cy + radius * math.sin(2 * math.pi * i / segments)) for i in range(segments)]
    for i in range(segments):
        a, c = ring[i], ring[(i + 1) % segments]
        b.face([(a[0], a[1], z0), (c[0], c[1], z0), (cx, cy, z1)], mat)
    b.face([(x, y, z0) for x, y in reversed(ring)], mat)


def tree_broadleaf(name, trunk, canopy):
    b = Builder(name)
    b.cylinder(0, 0, 0, trunk + 0.8, 0.2, 6, 'WG_Bark')
    blob(b, 0, 0, trunk + canopy * 0.8, canopy, canopy, canopy * 0.85, 'WG_Foliage')
    blob(b, canopy * 0.55, 0.2, trunk + canopy * 0.55, canopy * 0.6, canopy * 0.6, canopy * 0.55, 'WG_Foliage', subdivisions=1)
    blob(b, -canopy * 0.45, -0.35, trunk + canopy * 0.6, canopy * 0.55, canopy * 0.55, canopy * 0.5, 'WG_Foliage', subdivisions=1)
    return b.build()


def tree_conifer():
    b = Builder('Tree_Conifer')
    b.cylinder(0, 0, 0, 1.6, 0.22, 6, 'WG_Bark')
    for z0, z1, r in ((1.2, 5.2, 2.2), (3.4, 7.2, 1.7), (5.4, 9.3, 1.15)):
        cone(b, 0, 0, z0, z1, r, 8, 'WG_Foliage_Dark')
    return b.build()


def tree_poplar():
    b = Builder('Tree_Poplar')
    b.cylinder(0, 0, 0, 2.5, 0.18, 6, 'WG_Bark')
    blob(b, 0, 0, 6.2, 1.3, 1.3, 4.6, 'WG_Foliage_Light')
    return b.build()


def tree_bush():
    b = Builder('Tree_Bush')
    blob(b, 0, 0, 0.55, 1.0, 0.9, 0.75, 'WG_Foliage')
    blob(b, 0.7, 0.3, 0.45, 0.65, 0.6, 0.55, 'WG_Foliage', subdivisions=1)
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
# Generator v2 (2026-09-28): level crossings, junction controls, bridges, foundations and trees.
objects += [rail_crossing(), crossing_signal(), stop_sign(), traffic_light('Traffic_Light_Arterial', 5.6),
            traffic_light('Traffic_Light_Local', 3.6), bridge_railing(), bridge_deck(), bridge_pier(), foundation(),
            tree_broadleaf('Tree_Broadleaf_a', 2.4, 2.4), tree_broadleaf('Tree_Broadleaf_b', 3.0, 2.0), tree_conifer(),
            tree_poplar(), tree_bush()]

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
