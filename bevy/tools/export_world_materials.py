"""Export the Unity world materials (Assets/Art/World/Materials/WG_*.mat) the map's models are drawn with.

The world GLBs carry only material names (their textures were bound by the Unity materials), so this records each material's
base colour, base and normal textures (as bevy/assets paths), tiling, metallic and smoothness, and emission, for the map to
rebuild them. Writes bevy/assets/catalog/world_materials.json. Run: python bevy/tools/export_world_materials.py
"""
import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
MATERIALS = REPO / 'Assets' / 'Art' / 'World' / 'Materials'
TEXTURES = REPO / 'Assets' / 'Art' / 'World' / 'Textures'
OUT = REPO / 'bevy' / 'assets' / 'catalog' / 'world_materials.json'


def guids():
    table = {}
    for meta in TEXTURES.glob('*.png.meta'):
        match = re.search(r'^guid: ([0-9a-f]+)', meta.read_text(encoding='utf-8'), re.M)
        if match:
            table[match.group(1)] = 'Art/World/Textures/' + meta.name[:-len('.meta')]
    return table


def texture(text, name, table):
    match = re.search(r'- ' + name + r':\s*\n\s*m_Texture: \{fileID: (\d+)(?:, guid: ([0-9a-f]+))?.*\n\s*m_Scale: \{x: ([-\d.e]+), y: ([-\d.e]+)\}\n\s*m_Offset: \{x: ([-\d.e]+), y: ([-\d.e]+)\}', text)
    if not match or match.group(1) == '0' or not match.group(2):
        return None
    return {'path': table.get(match.group(2), ''), 'scale': [float(match.group(3)), float(match.group(4))], 'offset': [float(match.group(5)), float(match.group(6))]}


def number(text, name, default):
    match = re.search(r'- ' + name + r': ([-\d.e]+)', text)
    return float(match.group(1)) if match else default


def colour(text, name):
    match = re.search(r'- ' + name + r': \{r: ([-\d.e]+), g: ([-\d.e]+), b: ([-\d.e]+), a: ([-\d.e]+)\}', text)
    return [float(match.group(i)) for i in range(1, 5)] if match else None


def main():
    table = guids()
    result = {}
    for path in sorted(MATERIALS.glob('*.mat')):
        text = path.read_text(encoding='utf-8')
        name = re.search(r'm_Name: (\S+)', text.split('Material:', 1)[1]).group(1)
        keywords = re.findall(r'^  - (_[A-Z_]+)$', text, re.M)
        base = texture(text, '_BaseMap', table)
        bump = texture(text, '_BumpMap', table)
        emission = colour(text, '_EmissionColor') if '_EMISSION' in keywords else None
        result[name] = {
            'baseColor': colour(text, '_BaseColor') or [1, 1, 1, 1],
            'baseMap': base,
            'normalMap': bump['path'] if bump else '',
            'metallic': number(text, '_Metallic', 0.0),
            'smoothness': number(text, '_Smoothness', 0.5),
            # Smoothness read from the albedo alpha (Unity's channel 1) is approximated by the slider alone.
            'smoothnessFromAlbedo': '_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A' in keywords,
            'transparent': number(text, '_Surface', 0.0) == 1.0,
            'emission': emission,
        }
    OUT.write_text(json.dumps(result, indent=1), encoding='utf-8')
    print(f'{len(result)} materials -> {OUT}')


if __name__ == '__main__':
    main()
