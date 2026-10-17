"""Bake the tileable surface detail textures the game lays over its flat-coloured models (src/textures.rs).

Each texture is a procedural Blender material baked (Cycles, emission) onto a unit plane: near-white greyscale detail
(wood grain, brushed metal, plaster, weave, concrete, tiles, planks, bricks, plastic, rubber, organic) that the game
multiplies with each material's own colour, so models keep their palette and gain surface. Beside each, `<name>_normal.png`
is an OpenGL-convention tangent-space normal map derived from the same height (wrapping at the edges), so the detail also
catches the light. Noise is sampled on a 4D torus built from the UVs and patterns use whole repeat counts, so every texture
tiles seamlessly.

Run: blender -b --factory-startup --python bevy/tools/blender_surface_textures.py -- bevy/assets/Art/Generated
"""
import math
import os
import sys

import bpy
import numpy as np

SIZE = 512
OUT = os.path.abspath(sys.argv[sys.argv.index('--') + 1] if '--' in sys.argv else 'bevy/assets/Art/Generated')


class Graph:
    """A small helper for wiring shader nodes."""

    def __init__(self, material):
        self.tree = material.node_tree
        self.tree.nodes.clear()
        self.y = 0

    def node(self, kind, **props):
        node = self.tree.nodes.new(kind)
        node.location = (0, self.y)
        self.y -= 160
        for key, value in props.items():
            setattr(node, key, value)
        return node

    def link(self, out, inp):
        self.tree.links.new(out, inp)

    def math(self, op, a, b=None, c=None):
        node = self.node('ShaderNodeMath', operation=op)
        for index, value in enumerate((a, b, c)):
            if value is None:
                continue
            if isinstance(value, (int, float)):
                node.inputs[index].default_value = value
            else:
                self.link(value, node.inputs[index])
        return node.outputs[0]

    def uv(self):
        coords = self.node('ShaderNodeTexCoord')
        split = self.node('ShaderNodeSeparateXYZ')
        self.link(coords.outputs['UV'], split.inputs['Vector'])
        return split.outputs['X'], split.outputs['Y']

    def torus_noise(self, u, v, ru, rv, scale, detail=6.0, roughness=0.55):
        """Seamless noise: u and v each go once round a circle, of radius ru and rv (more radius, more features)."""
        a = self.math('MULTIPLY', u, 2.0 * math.pi)
        b = self.math('MULTIPLY', v, 2.0 * math.pi)
        vec = self.node('ShaderNodeCombineXYZ')
        self.link(self.math('MULTIPLY', self.math('COSINE', a), ru), vec.inputs['X'])
        self.link(self.math('MULTIPLY', self.math('SINE', a), ru), vec.inputs['Y'])
        self.link(self.math('MULTIPLY', self.math('COSINE', b), rv), vec.inputs['Z'])
        noise = self.node('ShaderNodeTexNoise', noise_dimensions='4D')
        self.link(vec.outputs['Vector'], noise.inputs['Vector'])
        self.link(self.math('MULTIPLY', self.math('SINE', b), rv), noise.inputs['W'])
        noise.inputs['Scale'].default_value = scale
        noise.inputs['Detail'].default_value = detail
        noise.inputs['Roughness'].default_value = roughness
        return noise.outputs['Fac']

    def lines(self, coord, count, width):
        """1 on thin lines at each of `count` repeats of a 0..1 coordinate, else 0."""
        f = self.math('FRACT', self.math('MULTIPLY', coord, count))
        near = self.math('MINIMUM', f, self.math('SUBTRACT', 1.0, f))
        return self.math('LESS_THAN', near, width)

    def emit(self, value):
        """Emits value (0..1 grey) as the colour to bake."""
        colour = self.node('ShaderNodeCombineXYZ')
        for axis in 'XYZ':
            self.link(value, colour.inputs[axis])
        emission = self.node('ShaderNodeEmission')
        self.link(colour.outputs['Vector'], emission.inputs['Color'])
        output = self.node('ShaderNodeOutputMaterial')
        self.link(emission.outputs['Emission'], output.inputs['Surface'])


def remap(g, value, low, high):
    """value 0..1 to low..high."""
    return g.math('MULTIPLY_ADD', value, high - low, low)


def contrast(g, value, amount):
    """Spreads noise (about 0.5) by `amount`, clamped to 0..1."""
    return g.math('MULTIPLY_ADD', g.math('SUBTRACT', value, 0.5), amount, 0.5)


def wood(g, u, v):
    grain = contrast(g, g.torus_noise(u, v, 0.35, 6.0, 2.2, 8.0, 0.6), 2.4)
    rings = g.math('SINE', g.math('MULTIPLY', g.math('ADD', g.math('MULTIPLY', v, 40.0 * math.pi), g.math('MULTIPLY', g.torus_noise(u, v, 0.8, 1.5, 1.5), 9.0)), 1.0))
    rings = g.math('MULTIPLY_ADD', rings, 0.5, 0.5)
    mixed = g.math('ADD', g.math('MULTIPLY', grain, 0.6), g.math('MULTIPLY', rings, 0.4))
    return remap(g, g.math('MINIMUM', g.math('MAXIMUM', mixed, 0.0), 1.0), 0.66, 1.0)


def planks(g, u, v):
    base = wood(g, u, v)
    seams = g.math('MAXIMUM', g.lines(v, 4, 0.012), g.math('MULTIPLY', g.lines(g.math('ADD', u, g.math('MULTIPLY', g.math('FLOOR', g.math('MULTIPLY', v, 4)), 0.37)), 1, 0.006), 1.0))
    return g.math('MULTIPLY', base, g.math('MULTIPLY_ADD', seams, -0.45, 1.0))


def metal(g, u, v):
    brushed = contrast(g, g.torus_noise(u, v, 0.15, 12.0, 6.0, 4.0, 0.5), 2.0)
    blotch = g.torus_noise(u, v, 1.0, 1.0, 1.4, 3.0, 0.5)
    return remap(g, g.math('ADD', g.math('MULTIPLY', brushed, 0.7), g.math('MULTIPLY', blotch, 0.3)), 0.8, 1.0)


def plaster(g, u, v):
    soft = g.torus_noise(u, v, 1.2, 1.2, 3.0, 10.0, 0.62)
    return remap(g, contrast(g, soft, 1.6), 0.84, 1.0)


def fabric(g, u, v):
    warp = g.math('MULTIPLY_ADD', g.math('SINE', g.math('MULTIPLY', u, 2 * math.pi * 32)), 0.5, 0.5)
    weft = g.math('MULTIPLY_ADD', g.math('SINE', g.math('MULTIPLY', v, 2 * math.pi * 32)), 0.5, 0.5)
    weave = g.math('MULTIPLY', g.math('ADD', warp, weft), 0.5)
    fuzz = g.torus_noise(u, v, 2.0, 2.0, 6.0, 6.0, 0.6)
    return remap(g, g.math('ADD', g.math('MULTIPLY', weave, 0.55), g.math('MULTIPLY', fuzz, 0.45)), 0.76, 1.0)


def concrete(g, u, v):
    coarse = g.torus_noise(u, v, 1.0, 1.0, 2.5, 8.0, 0.6)
    specks = g.math('GREATER_THAN', g.torus_noise(u, v, 6.0, 6.0, 8.0, 2.0, 0.4), 0.64)
    value = g.math('SUBTRACT', remap(g, contrast(g, coarse, 1.8), 0.76, 1.0), g.math('MULTIPLY', specks, 0.12))
    return value


def tiles(g, u, v):
    face = remap(g, g.torus_noise(u, v, 1.0, 1.0, 3.0, 4.0, 0.5), 0.9, 1.0)
    grout = g.math('MAXIMUM', g.lines(u, 4, 0.018), g.lines(v, 4, 0.018))
    return g.math('MULTIPLY', face, g.math('MULTIPLY_ADD', grout, -0.4, 1.0))


def bricks(g, u, v):
    rows = 8
    row = g.math('FLOOR', g.math('MULTIPLY', v, rows))
    shift = g.math('MULTIPLY', g.math('MODULO', row, 2.0), 0.5)
    x = g.math('ADD', g.math('MULTIPLY', u, 4), shift)
    joints = g.math('MAXIMUM', g.lines(v, rows, 0.04), g.lines(x, 1, 0.02))
    face = remap(g, contrast(g, g.torus_noise(u, v, 2.0, 2.0, 4.0, 6.0, 0.6), 1.8), 0.78, 1.0)
    return g.math('MULTIPLY', face, g.math('MULTIPLY_ADD', joints, -0.35, 1.0))


def plastic(g, u, v):
    peel = g.torus_noise(u, v, 3.0, 3.0, 8.0, 3.0, 0.45)
    return remap(g, peel, 0.92, 1.0)


def rubber(g, u, v):
    grit = g.torus_noise(u, v, 5.0, 5.0, 10.0, 4.0, 0.7)
    return remap(g, contrast(g, grit, 1.5), 0.78, 1.0)


def organic(g, u, v):
    veins = g.torus_noise(u, v, 1.5, 1.5, 4.0, 10.0, 0.7)
    return remap(g, contrast(g, veins, 2.2), 0.74, 1.0)


def paper(g, u, v):
    fibres = g.torus_noise(u, v, 0.6, 4.0, 5.0, 8.0, 0.6)
    return remap(g, contrast(g, fibres, 1.6), 0.8, 1.0)


# How steep each texture's relief is in its normal map (height units per texel, scaled).
RELIEF = {
    'wood': 10.0, 'planks': 16.0, 'metal': 7.0, 'plaster': 9.0, 'fabric': 5.0, 'concrete': 12.0,
    'tiles': 16.0, 'bricks': 16.0, 'plastic': 6.0, 'rubber': 14.0, 'organic': 12.0, 'paper': 8.0,
}


def save_normal_map(image, name):
    """Writes the tangent-space normal map of a baked height image (its red channel), wrapping at the edges."""
    pixels = np.empty(SIZE * SIZE * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    height = pixels.reshape(SIZE, SIZE, 4)[:, :, 0]
    # Blender rows run bottom to top, the same way V does, so +row is +V.
    du = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * RELIEF[name]
    dv = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * RELIEF[name]
    normal = np.stack([-du, -dv, np.ones_like(height)], axis=-1)
    normal /= np.linalg.norm(normal, axis=-1, keepdims=True)
    out = np.concatenate([normal * 0.5 + 0.5, np.ones((SIZE, SIZE, 1), dtype=np.float32)], axis=-1)
    target = bpy.data.images.new(f'{name}_normal', SIZE, SIZE, alpha=False, float_buffer=False)
    target.colorspace_settings.name = 'Non-Color'
    target.pixels.foreach_set(out.ravel())
    target.filepath_raw = os.path.join(OUT, f'{name}_normal.png')
    target.file_format = 'PNG'
    target.save()


TEXTURES = {
    'wood': wood, 'planks': planks, 'metal': metal, 'plaster': plaster, 'fabric': fabric, 'concrete': concrete,
    'tiles': tiles, 'bricks': bricks, 'plastic': plastic, 'rubber': rubber, 'organic': organic, 'paper': paper,
}


def main():
    os.makedirs(OUT, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 1
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()
    bpy.ops.mesh.primitive_plane_add(size=2.0)
    plane = bpy.context.active_object
    for name, build in TEXTURES.items():
        material = bpy.data.materials.new(name)
        material.use_nodes = True
        g = Graph(material)
        u, v = g.uv()
        g.emit(build(g, u, v))
        image = bpy.data.images.new(name, SIZE, SIZE, alpha=False, float_buffer=True)
        target = g.node('ShaderNodeTexImage')
        target.image = image
        g.tree.nodes.active = target
        plane.data.materials.clear()
        plane.data.materials.append(material)
        plane.select_set(True)
        bpy.context.view_layer.objects.active = plane
        bpy.ops.object.bake(type='EMIT', margin=0)
        image.filepath_raw = os.path.join(OUT, f'{name}.png')
        image.file_format = 'PNG'
        image.save()
        save_normal_map(image, name)
        print(f'[textures] {name} -> {image.filepath_raw} (+ normal map)')


main()
