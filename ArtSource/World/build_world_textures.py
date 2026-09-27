"""Seamless procedural textures for the world blockout art (decision 0026 presentation).

Run inside Blender (numpy is bundled): exec(open(r'G:/Unity/FoodFactoryGame/ArtSource/World/build_world_textures.py').read())
Writes PNGs to ArtSource/World/Textures. Every texture tiles seamlessly (noise wraps). Base maps are RGBA with smoothness
in alpha (URP Lit "Smoothness source: Albedo alpha"); *_Normal.png are tangent-space normal maps (OpenGL, +Y up).
Texture scale (metres per repeat) is noted per texture and matches the UVs written by build_world_models.py.
Rows run bottom-up: array row 0 is v = 0.
"""
import os
import numpy as np
import bpy

OUT = 'G:/Unity/FoodFactoryGame/ArtSource/World/Textures'
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(20260927)


# ---------------------------------------------------------------- helpers

def noise(h, w, cells_y, cells_x, octaves=4, persistence=0.5):
    """Tileable value noise in [0, 1]: random lattices upsampled bilinearly with wrap-around."""
    total = np.zeros((h, w))
    amplitude, norm = 1.0, 0.0
    cy, cx = cells_y, cells_x
    for _ in range(octaves):
        lattice = rng.random((cy, cx))
        ys = np.arange(h) * cy / h
        xs = np.arange(w) * cx / w
        y0 = np.floor(ys).astype(int)
        x0 = np.floor(xs).astype(int)
        fy = (ys - y0)[:, None]
        fx = (xs - x0)[None, :]
        fy = fy * fy * (3 - 2 * fy)
        fx = fx * fx * (3 - 2 * fx)
        y1 = (y0 + 1) % cy
        x1 = (x0 + 1) % cx
        a = lattice[y0][:, x0]
        b = lattice[y0][:, x1]
        c = lattice[y1][:, x0]
        d = lattice[y1][:, x1]
        total += amplitude * ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy)
        norm += amplitude
        amplitude *= persistence
        cy, cx = cy * 2, cx * 2
    return total / norm


def speckle(h, w, density):
    return (rng.random((h, w)) < density).astype(float)


def grid(h, w, metres_v, metres_u):
    """Metre coordinates of every pixel centre: (v metres, u metres)."""
    v = (np.arange(h) + 0.5) / h * metres_v
    u = (np.arange(w) + 0.5) / w * metres_u
    return np.meshgrid(v, u, indexing='ij')


def band(x, start, end):
    return ((x >= start) & (x < end)).astype(float)


def lerp(a, b, t):
    t = t[..., None] if np.ndim(t) == np.ndim(a) - 1 or (np.ndim(a) == 3 and np.ndim(t) == 2) else t
    return a * (1 - t) + b * t


def colour(h, w, rgb):
    return np.broadcast_to(np.array(rgb, dtype=float), (h, w, 3)).copy()


def paint(base, mask, rgb):
    mask = np.clip(mask, 0, 1)[..., None]
    return base * (1 - mask) + np.array(rgb, dtype=float) * mask


def save(name, rgb, smooth=None, noncolor=False):
    h, w = rgb.shape[:2]
    rgba = np.ones((h, w, 4))
    rgba[..., :3] = np.clip(rgb, 0, 1)
    rgba[..., 3] = 1.0 if smooth is None else np.clip(smooth, 0, 1)
    if not noncolor:
        # Authored in linear-ish values; store sRGB.
        c = rgba[..., :3]
        rgba[..., :3] = np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)
    image = bpy.data.images.get(name) or bpy.data.images.new(name, w, h, alpha=True)
    if image.size[0] != w or image.size[1] != h:
        image.scale(w, h)
    image.colorspace_settings.name = 'Non-Color'  # values above are already encoded
    image.pixels.foreach_set(rgba.astype(np.float32).ravel())
    image.filepath_raw = f'{OUT}/{name}.png'
    image.file_format = 'PNG'
    image.save()
    return image


def save_normal(name, height, strength):
    """Normal map from a wrapping height field (1 = raised)."""
    dx = (np.roll(height, -1, axis=1) - np.roll(height, 1, axis=1)) * 0.5 * strength
    dy = (np.roll(height, -1, axis=0) - np.roll(height, 1, axis=0)) * 0.5 * strength
    n = np.stack([-dx, -dy, np.ones_like(height)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    save(name + '_Normal', n * 0.5 + 0.5, None, noncolor=True)


def asphalt(h, w, metres_v, metres_u, tone=0.14, wear=0.0):
    grain = noise(h, w, max(2, int(metres_v * 2)), max(2, int(metres_u * 2)), 5, 0.6)
    patches = noise(h, w, max(1, int(metres_v / 3)), max(1, int(metres_u / 3)), 3, 0.5)
    stones = speckle(h, w, 0.04)
    soft = np.clip((patches - 0.58) / 0.12, 0, 1)
    value = tone * (0.82 + 0.3 * grain) + 0.025 * stones - 0.012 * soft + wear * 0.04 * patches
    rgb = np.stack([value, value, value * 1.04], axis=-1)
    height = 0.6 * grain + 0.4 * stones
    return rgb, height


def concrete(h, w, metres_v, metres_u, tone=0.52, joints=(1.0, 1.0)):
    grain = noise(h, w, max(2, int(metres_v * 2)), max(2, int(metres_u * 2)), 5, 0.55)
    stain = noise(h, w, max(1, int(metres_v / 2)), max(1, int(metres_u / 2)), 3, 0.5)
    v, u = grid(h, w, metres_v, metres_u)
    value = tone * (0.88 + 0.18 * grain) - 0.05 * stain
    height = 0.5 + 0.2 * grain
    if joints:
        jv = np.abs(((v + joints[0] / 2) % joints[0]) - joints[0] / 2) < 0.012
        ju = np.abs(((u + joints[1] / 2) % joints[1]) - joints[1] / 2) < 0.012
        joint = (jv | ju).astype(float)
        value = value * (1 - 0.35 * joint)
        height = height - 0.5 * joint
    rgb = np.stack([value * 1.02, value, value * 0.96], axis=-1)
    return rgb, height


# ---------------------------------------------------------------- roads (u across the road, v along a 10 m tile)

ROADS = {
    # kind: total width, sidewalk (each side, including a 0.2 m curb), asphalt markings
    'Arterial': dict(width=14.0, sidewalk=2.0, gravel=0.0, marks=[
        (2.35, 0.12, 'white', None), (11.65, 0.12, 'white', None),
        (4.5, 0.12, 'white', (0.0, 3.0)), (9.5, 0.12, 'white', (0.0, 3.0)),
        (6.88, 0.1, 'yellow', None), (7.12, 0.1, 'yellow', None)]),
    'Local': dict(width=10.0, sidewalk=1.5, gravel=0.0, marks=[
        (1.8, 0.1, 'white', None), (8.2, 0.1, 'white', None), (5.0, 0.12, 'yellow', (0.0, 3.0))]),
    'Rural': dict(width=8.0, sidewalk=0.0, gravel=0.9, marks=[
        (1.1, 0.1, 'white', None), (6.9, 0.1, 'white', None), (4.0, 0.12, 'white', (0.0, 3.0))]),
}
PAINT = {'white': (0.82, 0.82, 0.8), 'yellow': (0.85, 0.66, 0.12)}


def road(kind, crosswalk):
    spec = ROADS[kind]
    width, length = spec['width'], 10.0
    h, w = 512, int(round(width * 64))
    v, u = grid(h, w, length, width)
    rgb, height = asphalt(h, w, length, width, 0.13 if kind != 'Rural' else 0.16, 1.0 if kind == 'Rural' else 0.3)
    smooth = np.full((h, w), 0.18)
    side, gravel = spec['sidewalk'], spec['gravel']
    zebra_zone = band(v, 0.0, 4.6) if crosswalk else np.zeros((h, w))
    wear = noise(h, w, 20, int(width * 2), 3, 0.6)
    for centre, mark_width, tint, dash in spec['marks']:
        mask = band(u, centre - mark_width / 2, centre + mark_width / 2)
        if dash:
            mask *= band(v, *dash)
        mask *= (1 - zebra_zone) * (wear > (0.35 if kind == 'Rural' else 0.2))
        rgb = paint(rgb, mask, PAINT[tint])
        height += 0.3 * mask
    if crosswalk:
        inner0, inner1 = side + 0.2, width - side - 0.2
        stripes = band(v, 0.8, 3.8) * band(u, inner0, inner1) * (((u - inner0) % 1.0) < 0.55)
        stop = band(v, 4.2, 4.5) * band(u, inner0, inner1)
        mask = (stripes + stop) * (wear > 0.15)
        rgb = paint(rgb, mask, PAINT['white'])
        height += 0.3 * mask
    if side > 0:
        walk_rgb, walk_h = concrete(h, w, length, width, 0.5, (1.0, 1.0))
        walk = band(u, 0, side - 0.2) + band(u, width - side + 0.2, width)
        curb = band(u, side - 0.2, side) + band(u, width - side, width - side + 0.2)
        rgb = rgb * (1 - walk[..., None]) + walk_rgb * walk[..., None]
        rgb = paint(rgb, curb * (0.9 + 0.1 * noise(h, w, 8, 8, 2)), (0.6, 0.6, 0.58))
        height = height * (1 - walk) + walk_h * walk
        smooth = np.where(walk + curb > 0, 0.12, smooth)
    if gravel > 0:
        g = noise(h, w, 80, int(width * 20), 3, 0.7)
        mask = band(u, 0, gravel) + band(u, width - gravel, width)
        gravel_rgb = np.stack([0.34 + 0.2 * g, 0.31 + 0.18 * g, 0.26 + 0.15 * g], axis=-1)
        rgb = rgb * (1 - mask[..., None]) + gravel_rgb * mask[..., None]
        height = height * (1 - mask) + g * mask
        smooth = np.where(mask > 0, 0.05, smooth)
    name = f'Road_{kind}{"_Crosswalk" if crosswalk else ""}'
    save(name, rgb, smooth)
    save_normal(name, height, 2.5)


# ---------------------------------------------------------------- ground, lots, fields

def ground_textures():
    h = w = 512
    # Grass, 8 m per repeat.
    g1 = noise(h, w, 16, 16, 5, 0.6)
    g2 = noise(h, w, 4, 4, 3, 0.5)
    blades = speckle(h, w, 0.2) * rng.random((h, w))
    clumps = noise(h, w, 32, 32, 3, 0.7)
    dry = np.clip((g2 - 0.55) / 0.3, 0, 1)
    rgb = np.stack([0.1 + 0.07 * g1 + 0.1 * dry, 0.2 + 0.12 * g1 + 0.06 * blades + 0.04 * dry, 0.06 + 0.04 * g1], axis=-1)
    rgb *= (0.75 + 0.45 * clumps)[..., None]
    save('Ground_Grass', rgb, np.full((h, w), 0.08))
    save_normal('Ground_Grass', g1 + 0.5 * blades, 3.0)
    # Paved plaza / downtown lot, 8 m per repeat, 2 m slabs.
    rgb, height = concrete(h, w, 8, 8, 0.56, (2.0, 2.0))
    save('Ground_Paving', rgb, np.full((h, w), 0.15))
    save_normal('Ground_Paving', height, 3.0)
    # Industrial yard: stained concrete, 8 m per repeat, 4 m joints.
    rgb, height = concrete(h, w, 8, 8, 0.44, (4.0, 4.0))
    oil = noise(h, w, 6, 6, 4, 0.6)
    rgb = rgb * (1 - 0.12 * np.clip((oil - 0.55) / 0.15, 0, 1)[..., None])
    save('Ground_Yard', rgb, np.full((h, w), 0.2))
    save_normal('Ground_Yard', height, 3.0)
    # Fields: crop rows every 0.75 m along v, 12 m per repeat.
    for name, soil, crop in (('Field_Wheat', (0.3, 0.22, 0.13), (0.72, 0.6, 0.28)),
                             ('Field_Greens', (0.24, 0.17, 0.1), (0.2, 0.4, 0.12))):
        v, u = grid(h, w, 12, 12)
        row = 0.5 + 0.5 * np.cos((u % 0.75) / 0.75 * 2 * np.pi)
        n = noise(h, w, 24, 24, 4, 0.6)
        plants = np.clip(row * 1.3 - 0.3 + 0.3 * (n - 0.5), 0, 1)
        rgb = lerp(colour(h, w, soil) * (0.85 + 0.3 * n[..., None]), colour(h, w, crop) * (0.8 + 0.4 * n[..., None]), plants)
        save(name, rgb, np.full((h, w), 0.05))
        save_normal(name, plants + 0.3 * n, 4.0)


# ---------------------------------------------------------------- rail (u across 4 m of ballast, v along a 6 m tile)

def rail_texture():
    h, w = 512, 352
    v, u = grid(h, w, 6.0, 4.0)
    stones = noise(h, w, 96, 64, 3, 0.7)
    rgb = np.stack([0.33 + 0.2 * stones, 0.31 + 0.19 * stones, 0.28 + 0.17 * stones], axis=-1)
    height = stones.copy()
    sleeper = band(u, 0.7, 3.3) * (((v + 0.125) % 0.6) < 0.25)
    grainwood = noise(h, w, 40, 4, 3, 0.6)
    rgb = paint(rgb, sleeper, (0.0, 0.0, 0.0))
    rgb += sleeper[..., None] * np.stack([0.2 + 0.08 * grainwood, 0.14 + 0.05 * grainwood, 0.09 + 0.03 * grainwood], axis=-1)
    height = height * (1 - sleeper) + (1.2 + 0.1 * grainwood) * sleeper
    save('Rail_Ballast', rgb, np.full((h, w), 0.06))
    save_normal('Rail_Ballast', height, 3.0)


# ---------------------------------------------------------------- facades (one bay wide by one storey high)

def window(rgb, height, smooth, v, u, x0, x1, y0, y1, frame=0.06, frame_rgb=(0.86, 0.86, 0.84), mullion=True, sill=True):
    """Paints a framed window; glass reflects a sky gradient and is smooth."""
    inner = band(u, x0 + frame, x1 - frame) * band(v, y0 + frame, y1 - frame)
    outer = band(u, x0, x1) * band(v, y0, y1)
    t = np.clip((v - y0) / max(1e-3, y1 - y0), 0, 1)
    sky = np.stack([0.18 + 0.28 * t, 0.24 + 0.3 * t, 0.3 + 0.32 * t], axis=-1)
    rgb = rgb * (1 - inner[..., None]) + sky * inner[..., None]
    rgb = paint(rgb, outer - inner, frame_rgb)
    if mullion:
        mid_u, mid_v = (x0 + x1) / 2, y0 + (y1 - y0) * 0.62
        bars = (band(u, mid_u - frame / 3, mid_u + frame / 3) + band(v, mid_v - frame / 3, mid_v + frame / 3)) * inner
        rgb = paint(rgb, bars, frame_rgb)
        inner = inner * (1 - np.clip(bars, 0, 1))
    if sill:
        ledge = band(u, x0 - 0.05, x1 + 0.05) * band(v, y0 - 0.07, y0)
        rgb = paint(rgb, ledge, (0.7, 0.69, 0.66))
        height += 0.4 * ledge
    # The frame replaces the wall's relief (no brick grooves under the glass); glass is flat and recessed.
    height = np.where(outer > 0, np.where(inner > 0, 0.0, 0.8), height)
    smooth = np.where(inner > 0, 0.88, smooth)
    return rgb, height, smooth


def brick_wall(h, w, metres_v, metres_u, base=(0.46, 0.2, 0.13)):
    v, u = grid(h, w, metres_v, metres_u)
    course = np.floor(v / 0.075)
    offset = (course % 2) * 0.1075
    mortar = ((v % 0.075) < 0.012) | (((u + offset) % 0.215) < 0.012)
    brick_id = np.floor((u + offset) / 0.215) + course * 37
    tone = (np.sin(brick_id * 12.9898) * 43758.5453) % 1.0
    n = noise(h, w, 24, 24, 3, 0.5)
    rgb = colour(h, w, base) * (0.8 + 0.3 * tone[..., None] + 0.12 * n[..., None])
    rgb = paint(rgb, mortar.astype(float), (0.6, 0.58, 0.54))
    height = 1.0 - 0.8 * mortar + 0.1 * n
    return rgb, height, np.full((h, w), 0.1), v, u


def plaster_wall(h, w, metres_v, metres_u, base):
    v, u = grid(h, w, metres_v, metres_u)
    n = noise(h, w, 16, 16, 5, 0.6)
    rgb = colour(h, w, base) * (0.9 + 0.15 * n[..., None])
    return rgb, 0.5 + 0.3 * n, np.full((h, w), 0.12), v, u


def siding_wall(h, w, metres_v, metres_u, base):
    v, u = grid(h, w, metres_v, metres_u)
    board = (v % 0.2) / 0.2
    n = noise(h, w, 8, 32, 3, 0.5)
    rgb = colour(h, w, base) * (0.82 + 0.18 * board[..., None] + 0.05 * n[..., None])
    return rgb, board + 0.1 * n, np.full((h, w), 0.2), v, u


def facade_textures():
    h = w = 512
    # Houses: 3 m bay x 3 m storey, one window.
    for name, maker, args in (('Facade_Brick', brick_wall, ()),
                              ('Facade_Siding', siding_wall, ((0.55, 0.62, 0.68),)),
                              ('Facade_Plaster', plaster_wall, ((0.82, 0.74, 0.6),))):
        rgb, height, smooth, v, u = maker(h, w, 3.0, 3.0, *args)
        rgb, height, smooth = window(rgb, height, smooth, v, u, 0.9, 2.1, 0.95, 2.35)
        save(name, rgb, smooth)
        save_normal(name, height, 2.0)
    # Apartments: 3.2 m bay x 3 m storey, concrete panels, wide window, floor band.
    v, u = grid(h, w, 3.0, 3.2)
    rgb, height = concrete(h, w, 3.0, 3.2, 0.62, (3.0, 3.2))
    smooth = np.full((h, w), 0.12)
    rgb = paint(rgb, band(v, 0.0, 0.3), (0.42, 0.41, 0.4))
    rgb, height, smooth = window(rgb, height, smooth, v, u, 0.7, 2.5, 0.9, 2.5, frame_rgb=(0.3, 0.3, 0.32))
    rail = band(u, 0.6, 2.6) * band(v, 0.9, 1.0)
    rgb = paint(rgb, rail, (0.25, 0.25, 0.27))
    save('Facade_Apartment', rgb, smooth)
    save_normal('Facade_Apartment', height, 2.0)
    # Offices: 3 m bay x 3.5 m storey curtain wall; spandrel at the floor line.
    v, u = grid(h, w, 3.5, 3.0)
    t = v / 3.5
    rgb = np.stack([0.12 + 0.3 * t, 0.2 + 0.35 * t, 0.28 + 0.38 * t], axis=-1)
    rgb *= (0.9 + 0.2 * noise(h, w, 2, 2, 2))[..., None]
    smooth = np.full((h, w), 0.92)
    spandrel = band(v, 0.0, 0.9)
    rgb = paint(rgb, spandrel, (0.1, 0.12, 0.14))
    mullions = band(u % 1.5, 0.0, 0.05) + band(v, 0.88, 0.95) + band(v, 3.45, 3.5)
    rgb = paint(rgb, mullions, (0.55, 0.57, 0.6))
    smooth = np.where(mullions > 0, 0.5, np.where(spandrel > 0, 0.8, smooth))
    height = 1.0 * np.clip(mullions, 0, 1)
    save('Facade_Office', rgb, smooth)
    save_normal('Facade_Office', height, 3.0)
    # Storefront: 3 m bay x 4.5 m storey: kick plate, glazing, sign band.
    v, u = grid(h, w, 4.5, 3.0)
    rgb, height, smooth, _, _ = plaster_wall(h, w, 4.5, 3.0, (0.86, 0.83, 0.76))
    rgb = paint(rgb, band(v, 0.0, 0.45), (0.2, 0.2, 0.21))
    rgb, height, smooth = window(rgb, height, smooth, v, u, 0.1, 2.9, 0.45, 3.2, frame=0.08, frame_rgb=(0.16, 0.16, 0.17),
                                 mullion=False, sill=False)
    save('Facade_Storefront', rgb, smooth)
    save_normal('Facade_Storefront', height, 2.0)
    # Corrugated metal (neutral; tinted per material), 4 m per repeat, ribs every 0.2 m.
    v, u = grid(h, w, 4.0, 4.0)
    rib = 0.5 + 0.5 * np.cos((u % 0.2) / 0.2 * 2 * np.pi)
    streak = noise(h, w, 2, 24, 4, 0.6)
    rgb = colour(h, w, (0.78, 0.8, 0.82)) * (0.78 + 0.18 * rib[..., None] - 0.12 * (streak[..., None] > 0.6))
    save('Metal_Corrugated', rgb, 0.35 + 0.2 * rib)
    save_normal('Metal_Corrugated', rib, 4.0)
    # Industrial window strip: 4 m wide x 1.5 m, small panes.
    v, u = grid(h, 1024, 1.5, 4.0)
    t = v / 1.5
    rgb = np.stack([0.25 + 0.25 * t, 0.3 + 0.25 * t, 0.33 + 0.27 * t], axis=-1)
    panes = band(u % 0.5, 0.0, 0.04) + band(v % 0.5, 0.0, 0.04)
    rgb = paint(rgb, panes, (0.3, 0.32, 0.3))
    save('Window_Industrial', rgb, np.where(panes > 0, 0.4, 0.85))
    save_normal('Window_Industrial', np.clip(panes, 0, 1), 3.0)
    # Roller door: 4 m x 4 m, 0.12 m slats.
    v, u = grid(h, w, 4.0, 4.0)
    slat = (v % 0.12) / 0.12
    rgb = colour(h, w, (0.62, 0.64, 0.64)) * (0.8 + 0.2 * slat[..., None])
    save('Door_Roller', rgb, np.full((h, w), 0.4))
    save_normal('Door_Roller', slat, 3.0)
    # Barn planks (neutral red), 4 m per repeat, 0.25 m boards.
    v, u = grid(h, w, 4.0, 4.0)
    board = np.floor(u / 0.25)
    tone = (np.sin(board * 78.233) * 43758.5453) % 1.0
    wood = noise(h, w, 64, 8, 4, 0.6)
    gaps = ((u % 0.25) < 0.012).astype(float)
    rgb = colour(h, w, (0.48, 0.12, 0.08)) * (0.75 + 0.2 * tone[..., None] + 0.2 * wood[..., None])
    rgb = paint(rgb, gaps, (0.12, 0.05, 0.04))
    save('Wood_BarnPlanks', rgb, np.full((h, w), 0.08))
    save_normal('Wood_BarnPlanks', wood * 0.5 - gaps, 3.0)
    # Doors: 1.0 x 2.2 m panelled wood door; 2.0 x 2.5 m glass double door.
    v, u = grid(h, 256, 2.2, 1.0)
    wood = noise(h, 256, 32, 4, 4, 0.6)
    rgb = colour(h, 256, (0.32, 0.18, 0.1)) * (0.8 + 0.3 * wood[..., None])
    panels = (band(u, 0.15, 0.85) * (band(v, 0.2, 1.0) + band(v, 1.2, 2.0)))
    edge = panels - np.clip(band(u, 0.2, 0.8) * (band(v, 0.25, 0.95) + band(v, 1.25, 1.95)), 0, 1)
    rgb = paint(rgb, edge * 0.5, (0.18, 0.1, 0.06))
    rgb = paint(rgb, band(u, 0.8, 0.88) * band(v, 1.0, 1.08), (0.75, 0.62, 0.3))
    save('Door_Wood', rgb, np.full((h, 256), 0.3))
    save_normal('Door_Wood', panels - 0.5 * edge, 2.0)
    v, u = grid(h, w, 2.5, 2.0)
    rgb = colour(h, w, (0.15, 0.15, 0.16))
    smooth = np.full((h, w), 0.5)
    height = np.ones((h, w))
    for x0 in (0.0, 1.0):
        rgb, height, smooth = window(rgb, height, smooth, v, u, x0 + 0.02, x0 + 0.98, 0.02, 2.48, frame=0.07,
                                     frame_rgb=(0.16, 0.16, 0.17), mullion=False, sill=False)
    save('Door_Glass', rgb, smooth)
    save_normal('Door_Glass', height, 2.0)


# ---------------------------------------------------------------- roofs and trims

def roof_textures():
    h = w = 512
    v, u = grid(h, w, 4.0, 4.0)
    # Asphalt shingles: 0.25 m courses with staggered tabs.
    course = np.floor(v / 0.25)
    offset = (course % 2) * 0.17
    tab = np.floor((u + offset) / 0.34) + course * 13
    tone = (np.sin(tab * 12.9898) * 43758.5453) % 1.0
    lip = (v % 0.25) / 0.25
    gaps = (((u + offset) % 0.34) < 0.01).astype(float)
    n = noise(h, w, 32, 32, 3, 0.5)
    rgb = colour(h, w, (0.22, 0.22, 0.24)) * (0.75 + 0.35 * tone[..., None] + 0.1 * n[..., None]) * (0.8 + 0.2 * lip[..., None])
    rgb = paint(rgb, gaps, (0.06, 0.06, 0.07))
    save('Roof_Shingles', rgb, np.full((h, w), 0.1))
    save_normal('Roof_Shingles', lip - gaps, 3.0)
    # Clay tiles: rounded rows every 0.3 m across, courses every 0.35 m.
    wave = 0.5 + 0.5 * np.cos((u % 0.3) / 0.3 * 2 * np.pi)
    lip = (v % 0.35) / 0.35
    rgb = colour(h, w, (0.55, 0.24, 0.13)) * (0.7 + 0.25 * wave[..., None] + 0.1 * n[..., None]) * (0.85 + 0.15 * lip[..., None])
    save('Roof_ClayTiles', rgb, np.full((h, w), 0.2))
    save_normal('Roof_ClayTiles', wave + 0.6 * lip, 3.0)
    # Flat roof membrane with gravel, 8 m per repeat.
    g = noise(h, w, 64, 64, 3, 0.7)
    seams = band(grid(h, w, 8, 8)[1] % 2.0, 0.0, 0.02)
    rgb = colour(h, w, (0.4, 0.4, 0.4)) * (0.8 + 0.3 * g[..., None])
    rgb = paint(rgb, seams, (0.3, 0.3, 0.3))
    save('Roof_Flat', rgb, np.full((h, w), 0.1))
    save_normal('Roof_Flat', g, 3.0)
    # Plain concrete, 4 m per repeat (platforms, parapets, foundations).
    rgb, height = concrete(h, w, 4, 4, 0.58, (2.0, 2.0))
    save('Concrete', rgb, np.full((h, w), 0.18))
    save_normal('Concrete', height, 2.0)


road('Arterial', False)
road('Arterial', True)
road('Local', False)
road('Local', True)
road('Rural', False)
ground_textures()
rail_texture()
facade_textures()
roof_textures()
junction_rgb, junction_h = asphalt(512, 512, 10, 10, 0.14, 0.4)
save('Road_Junction', junction_rgb, np.full((512, 512), 0.18))
save_normal('Road_Junction', junction_h, 2.5)
result = {"textures": sorted(os.listdir(OUT))}
