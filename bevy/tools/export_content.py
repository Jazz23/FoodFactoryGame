"""Package Unity-authored gameplay content for the Bevy port without changing Unity or needing it at runtime.

Reads the ScriptableObject YAML under Assets/Content (items, recipes, equipment definitions, supplier offers, truck models,
FoodBalance prices and the restaurant style catalog) and resolves asset references through .meta GUIDs to the converted
Bevy asset paths (GLB models under bevy/assets/models, icons under bevy/assets/Art). Writes bevy/assets/content/catalog.json.
Run from anywhere: python bevy/tools/export_content.py
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / 'Assets'
CONTENT = ASSETS / 'Content'
BEVY_ASSETS = ROOT / 'bevy' / 'assets'
OUT = BEVY_ASSETS / 'content' / 'catalog.json'


def guid_map():
    found = {}
    for meta in ASSETS.rglob('*.meta'):
        match = re.search(r'^guid: (\w+)', meta.read_text(encoding='utf-8', errors='ignore'), re.M)
        if match:
            found[match[1]] = meta.with_suffix('')
    return found


GUIDS = guid_map()


def fields(text):
    return dict(re.findall(r'^  (\w+):[^\S\n]*(.*?)\r?$', text, re.M))


def number(data, key, default=0):
    value = data.get(key, '')
    try:
        return int(value)
    except ValueError:
        try:
            return float(value)
        except ValueError:
            return default


def reference(value):
    match = re.search(r'guid: (\w+)', value or '')
    return GUIDS.get(match[1]) if match else None


def bevy_model(source):
    """The converted GLB for an FBX (Assets/Art/<x>.fbx -> models/<x>.glb), relative to bevy/assets, or ''."""
    if source is None:
        return ''
    rel = source.relative_to(ASSETS).as_posix()
    if rel.lower().endswith('.fbx') and rel.startswith('Art/'):
        glb = 'models/' + rel[len('Art/'):-4] + '.glb'
        return glb if (BEVY_ASSETS / glb).exists() else ''
    return ''


def prefab_model(prefab):
    """The model a visual prefab instantiates: its source FBX, or a gameplay GLB reconstructed for primitive prefabs."""
    if prefab is None:
        return ''
    if prefab.suffix.lower() == '.fbx':
        return bevy_model(prefab)
    text = prefab.read_text(encoding='utf-8', errors='ignore')
    for match in re.finditer(r'm_SourcePrefab: \{fileID: \d+, guid: (\w+)', text):
        model = bevy_model(GUIDS.get(match[1]))
        if model:
            return model
    for match in re.finditer(r'guid: (\w+)', text):
        source = GUIDS.get(match[1])
        if source is not None and source.suffix.lower() == '.fbx':
            model = bevy_model(source)
            if model:
                return model
    return ''


def icon_path(value):
    source = reference(value)
    if source is None:
        return ''
    rel = source.relative_to(ASSETS).as_posix()
    return rel if (BEVY_ASSETS / rel).exists() else ''


def gameplay_model(kind):
    for candidate in (f'models/gameplay/{kind}.glb', f'models/catalog/equipment/{kind}.glb'):
        if (BEVY_ASSETS / candidate).exists():
            return candidate
    return ''


MOUNTS = ['floor', 'flush', 'wall', 'ceiling', 'tabletop', 'backed']


def main():
    package = {'format': 2, 'items': [], 'recipes': [], 'offers': [], 'equipment': [], 'trucks': [], 'styles': {}}
    assets = sorted(CONTENT.rglob('*.asset'))
    equipment_by_guid = {}
    trucks_by_guid = {}
    for path in assets:
        text = path.read_text(encoding='utf-8-sig', errors='ignore')
        data = fields(text)
        guid = re.search(r'^guid: (\w+)', path.with_name(path.name + '.meta').read_text(), re.M)[1]
        if 'EquipmentDefinition' in text and 'kind' in data:
            kind = data['kind']
            model = gameplay_model(kind) or prefab_model(reference(data.get('visualPrefab')))
            entry = {
                'kind': kind, 'width': number(data, 'width', 1), 'depth': number(data, 'depth', 1),
                'inputCapacity': number(data, 'inputCapacity', 1), 'outputCapacity': number(data, 'outputCapacity', 1),
                'inputRefrigerated': bool(number(data, 'inputRefrigerated')), 'outputRefrigerated': bool(number(data, 'outputRefrigerated')),
                'seats': number(data, 'seats'), 'layer': data.get('layer', '').strip(), 'ambience': number(data, 'ambience'),
                'displayName': data.get('displayName', '').strip() or kind, 'category': data.get('category', '').strip(),
                'mount': MOUNTS[min(number(data, 'mount'), len(MOUNTS) - 1)], 'mountHeight': float(number(data, 'mountHeight', 1.5)),
                'hideFromAbove': bool(number(data, 'hideFromAbove')), 'opensScreen': bool(number(data, 'opensScreen', 1)),
                'manualPower': bool(number(data, 'manualPower')), 'workerBound': bool(number(data, 'workerBound')),
                'model': model, 'icon': icon_path(data.get('icon')),
            }
            equipment_by_guid[guid] = kind
            package['equipment'].append(entry)
        elif 'TruckDefinition' in text:
            trucks_by_guid[guid] = {
                'displayName': data.get('displayName', 'Truck'), 'cargoSlots': number(data, 'cargoSlots', 1),
                'speedMetresPerSecond': number(data, 'speedMetresPerSecond', 1), 'loadUnitsPerSecond': number(data, 'loadUnitsPerSecond', 1),
            }
        elif 'ItemDefinition' in text and 'maxStack' in data:
            package['items'].append({'id': data['id'], 'displayName': data.get('displayName', '').strip() or data['id'],
                                     'maxStack': number(data, 'maxStack', 1), 'icon': icon_path(data.get('icon'))})
        elif 'RecipeAsset' in text and 'stationKind' in data:
            inputs = [{'itemId': item.strip(), 'quantity': int(quantity)}
                      for item, quantity in re.findall(r'- itemId: (.*?)\r?\n\s+quantity: (\d+)', text)]
            sale = number(data, 'saleCents')
            package['recipes'].append({
                'id': data['id'], 'displayName': data.get('displayName', '').strip() or data['id'], 'stationKind': data['stationKind'],
                'durationSeconds': number(data, 'durationSeconds', 1), 'inputs': inputs,
                'outputItemId': '' if sale > 0 else data.get('outputItemId', '').strip(),
                'outputQuantity': 0 if sale > 0 else number(data, 'outputQuantity', 1),
                'outputSpoilAfterSeconds': 0 if sale > 0 else number(data, 'outputSpoilAfterSeconds', 1),
                'saleCents': sale, 'tier': number(data, 'tier', 1), 'cuisine': data.get('cuisine', '').strip(),
            })
    for path in assets:
        text = path.read_text(encoding='utf-8-sig', errors='ignore')
        if 'OfferAsset' not in text:
            continue
        data = fields(text)
        equipment = re.search(r'guid: (\w+)', data.get('equipment', ''))
        truck = re.search(r'guid: (\w+)', data.get('truck', ''))
        offer = {'id': data['id'], 'priceCents': number(data, 'priceCents', 1)}
        if truck and truck[1] in trucks_by_guid:
            offer['truck'] = trucks_by_guid[truck[1]]
        elif equipment and equipment[1] in equipment_by_guid:
            offer['equipmentKind'] = equipment_by_guid[equipment[1]]
        else:
            offer.update({'itemId': data.get('itemId', '').strip(), 'quantity': number(data, 'quantity', 1),
                          'spoilAfterSeconds': number(data, 'spoilAfterSeconds', 1)})
        package['offers'].append(offer)
    balance = (CONTENT / 'FoodBalance.asset').read_text(encoding='utf-8-sig')
    supplies, dishes = balance.split('  supplies:\n', 1)[1].split('  dishes:\n', 1)

    def rows(block):
        return [dict(re.findall(r'(\w+):[^\S\n]*(.*?)\r?$', row, re.M)) for row in block.split('  - ')[1:]]
    for row in rows(supplies):
        item, quantity = row['itemId'].strip(), number(row, 'packQuantity', 5)
        package['offers'].insert(0, {'id': f'supplier-{item}-{quantity}', 'itemId': item, 'quantity': quantity,
                                     'priceCents': number(row, 'packPriceCents', 1), 'spoilAfterSeconds': number(row, 'spoilAfterSeconds', 3600)})
    for row in rows(dishes):
        item = row['itemId'].strip()
        name = row.get('displayName', '').strip() or item
        package['recipes'].append({
            'id': f'counter-sell-{item}', 'displayName': 'Sell ' + name, 'stationKind': 'counter',
            'durationSeconds': max(1, number(row, 'serveSeconds', 5)), 'inputs': [{'itemId': item, 'quantity': 1}],
            'outputItemId': '', 'outputQuantity': 0, 'outputSpoilAfterSeconds': 0, 'saleCents': max(1, number(row, 'saleCents', 100)),
            'tier': max(1, number(row, 'tier', 1)), 'cuisine': row.get('cuisine', '').strip(),
        })
    # Authored dish recipes replaced by the FoodBalance menu keep only the generated one.
    seen = set()
    unique = []
    for recipe in reversed(package['recipes']):
        if recipe['id'] in seen:
            continue
        seen.add(recipe['id'])
        unique.append(recipe)
    package['recipes'] = list(reversed(unique))

    styles_text = (CONTENT / 'Restaurant' / 'RestaurantStyles.asset').read_text(encoding='utf-8-sig')

    def model_ref(value):
        return prefab_model(reference(value))

    def section(name):
        block = styles_text.split(f'  {name}:\n', 1)[1]
        block = re.split(r'\n  \w+:', block, 1)[0]
        return [dict(re.findall(r'(\w+):[^\S\n]*(.*?)\r?$', row, re.M)) for row in block.split('  - ')[1:]]
    package['styles'] = {
        'walls': [{'style': r['style'], 'displayName': r['displayName'], 'wall1m': model_ref(r.get('wall1m')), 'wall2m': model_ref(r.get('wall2m')),
                   'end': model_ref(r.get('end'))} for r in section('wallFinishes')],
        'doors': [{'style': r['style'], 'displayName': r['displayName'], 'leaf': model_ref(r.get('leaf'))} for r in section('doorLeaves')],
        'windows': [{'style': r['style'], 'displayName': r['displayName'], 'bay': model_ref(r.get('bay')), 'window': model_ref(r.get('window'))}
                    for r in section('windowStyles')],
    }
    top = fields(styles_text)
    for key in ('singleDoorway', 'doubleDoorway', 'singleFrame', 'doubleFrame'):
        package['styles'][key] = model_ref(top.get(key))
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(package, indent=1), encoding='utf-8')
    missing = [e['kind'] for e in package['equipment'] if not e['model']]
    print(f"items {len(package['items'])} recipes {len(package['recipes'])} offers {len(package['offers'])} "
          f"equipment {len(package['equipment'])} (no model: {missing})")


if __name__ == '__main__':
    main()
