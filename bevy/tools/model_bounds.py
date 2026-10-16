"""Record the bounding box of every GLB under bevy/assets/models (glTF Y-up, metres, scene 0 with node transforms applied).

Presentation fits a model to its footprint from these bounds, the way Unity's EquipmentModel centred visuals on their
footprint from rendered bounds. Writes bevy/assets/catalog/bounds.json. Run: python bevy/tools/model_bounds.py
"""
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'assets'


def quat_matrix(q):
    x, y, z, w = q
    return [
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w), 0],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w), 0],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y), 0],
        [0, 0, 0, 1],
    ]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def local(node):
    if 'matrix' in node:
        m = node['matrix']
        return [[m[c * 4 + r] for c in range(4)] for r in range(4)]
    t = node.get('translation', [0, 0, 0])
    r = node.get('rotation', [0, 0, 0, 1])
    s = node.get('scale', [1, 1, 1])
    rot = quat_matrix(r)
    for i in range(3):
        for j in range(3):
            rot[i][j] *= s[j]
    for i in range(3):
        rot[i][3] = t[i]
    return rot


def apply(m, p):
    return [sum(m[i][k] * p[k] for k in range(3)) + m[i][3] for i in range(3)]


def bounds(path):
    data = path.read_bytes()
    length = struct.unpack('<I', data[12:16])[0]
    gltf = json.loads(data[20:20 + length])
    lo = [float('inf')] * 3
    hi = [float('-inf')] * 3
    nodes = gltf.get('nodes', [])
    scene = gltf.get('scenes', [{}])[gltf.get('scene', 0)] if gltf.get('scenes') else {'nodes': list(range(len(nodes)))}

    def walk(index, parent):
        node = nodes[index]
        matrix = multiply(parent, local(node))
        if 'mesh' in node:
            for primitive in gltf['meshes'][node['mesh']]['primitives']:
                accessor = gltf['accessors'][primitive['attributes']['POSITION']]
                mn, mx = accessor.get('min'), accessor.get('max')
                if not mn:
                    continue
                for cx in (mn[0], mx[0]):
                    for cy in (mn[1], mx[1]):
                        for cz in (mn[2], mx[2]):
                            p = apply(matrix, [cx, cy, cz])
                            for i in range(3):
                                lo[i] = min(lo[i], p[i])
                                hi[i] = max(hi[i], p[i])
        for child in node.get('children', []):
            walk(child, matrix)

    identity = [[1 if i == j else 0 for j in range(4)] for i in range(4)]
    for index in scene.get('nodes', []):
        walk(index, identity)
    if lo[0] == float('inf'):
        return None
    return {'min': [round(v, 4) for v in lo], 'max': [round(v, 4) for v in hi]}


def main():
    result = {}
    for path in sorted(ROOT.joinpath('models').rglob('*.glb')):
        box = bounds(path)
        if box:
            result[path.relative_to(ROOT).as_posix()] = box
    out = ROOT / 'catalog' / 'bounds.json'
    out.write_text(json.dumps(result, indent=1), encoding='utf-8')
    print(f'{len(result)} models -> {out}')


if __name__ == '__main__':
    main()
