"""Split Unity multi-mesh FBX catalogs into individually loadable GLBs using Blender CLI."""
import argparse
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
BEVY = Path(__file__).resolve().parents[1]

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--blender', default='blender')
    p.add_argument('--project', type=Path, default=ROOT)
    args = p.parse_args()
    project = args.project.resolve()
    source_root = project / 'Assets/Art'
    out_root = BEVY / 'assets/models/catalog'
    worker = Path(__file__).with_name('_blender_split_catalog.py')
    specifications = [
        ('World/Models/WorldArt.fbx', 'world'),
        ('Vehicles/Models/Vehicles.fbx', 'vehicles'),
        ('Kitchen/Models/Kitchen.fbx', 'equipment'),
    ]
    records = []
    for rel, group in specifications:
        source = source_root / rel
        result = subprocess.run([args.blender, '--background', '--factory-startup', '--python', str(worker), '--', str(source), str(out_root / group), str(source_root)], check=True, capture_output=True, text=True)
        # Worker emits one marked JSON line; Blender's import/export diagnostics stay on stdout.
        line = next(x for x in result.stdout.splitlines() if x.startswith('CATALOG_JSON:'))
        group_records = json.loads(line.split(':', 1)[1])
        records.extend(group_records)
        if group == 'equipment':
            for record in group_records:
                target = BEVY / record['path']
                target.with_suffix('.json').write_text(json.dumps({'dimensions':record['dimensions'],'offset':record['offset'],'materials':record['materials']},indent=2)+'\n')
        print(f'{rel}: {len(group_records)} standalone GLBs')
    # Native Unity equipment definitions resolve their visual prefab by GUID; use that contract,
    # not filename guesses, to connect runtime kinds to the individual model assets.
    assets = project / 'Assets'
    prefab_guids = {}
    for meta in assets.rglob('*.meta'):
        m = re.search(r'(?m)^guid:\s*(\w+)', meta.read_text(errors='ignore'))
        if m and meta.with_suffix('').suffix == '.prefab': prefab_guids[m.group(1)] = meta.with_suffix('')
    prefab_worker = Path(__file__).with_name('_blender_prefab_primitives.py')
    simple_worker = Path(__file__).with_name('_blender_fbx_to_glb.py')
    for definition in sorted((assets / 'Content/Equipment').glob('*.asset')):
        text = definition.read_text(errors='ignore')
        def scalar(key, default=''):
            m = re.search(r'(?m)^  '+re.escape(key)+r':\s*(.*)$', text)
            return m.group(1).strip() if m else default
        kind = scalar('kind')
        if not kind: continue
        visual = re.search(r'(?m)^  visualPrefab: \{fileID: \d+, guid: (\w+)',text)
        prefab = prefab_guids.get(visual.group(1)) if visual else None
        if not prefab: continue
        dest = BEVY / 'assets/models/catalog/equipment' / f'{kind}.glb'
        dest.parent.mkdir(parents=True,exist_ok=True)
        if kind in {'mixer','fryer','grill','stove','kitchen-counter'}:
            source_name = {'kitchen-counter':'Kitchen_Counter'}.get(kind,kind.title().replace('-','_'))
            source_file = BEVY / 'assets/models/catalog/equipment' / f'{kind}.glb'
            if not source_file.is_file(): raise FileNotFoundError(f'Missing Blender split model {source_name} for {kind}')
            sidecar = source_file.with_suffix('.json')
        elif kind == 'oven':
            source = assets / 'Art/Models/Oven/FF_Oven.fbx'
            subprocess.run([args.blender,'--background','--factory-startup','--python',str(simple_worker),'--',str(source),str(dest)],check=True,capture_output=True,text=True)
            # The source mesh dimensions are evaluated by Blender below in validation; no guessed size is put in catalog.
            sidecar = None
        else:
            subprocess.run([args.blender,'--background','--factory-startup','--python',str(prefab_worker),'--',str(prefab),str(assets),str(dest)],check=True,capture_output=True,text=True)
            sidecar = dest.with_suffix('.json')
        # Kitchen-sidecar names already are canonical and its JSON was emitted by the catalog worker.
        metrics = json.loads(sidecar.read_text()) if sidecar and sidecar.is_file() else {'dimensions':None,'offset':None,'materials':[]}
        records.append({'kind':kind,'group':'equipment','path':dest.relative_to(BEVY).as_posix(),
            'source':definition.relative_to(project).as_posix(),'prefab':prefab.relative_to(project).as_posix(),
            'displayName':scalar('displayName',kind),'footprint':[int(scalar('width','1')),int(scalar('depth','1'))],
            'mount':scalar('mount','0'),'dimensions':metrics['dimensions'],'offset':metrics['offset'],'scale':[1,1,1],
            'materials':metrics['materials'],'notes':'Unity EquipmentDefinition visualPrefab mapping; prefabs with primitive meshes are reconstructed from read-only YAML.'})
        print(f'{kind}: {dest.relative_to(BEVY)}')
    manifest = BEVY / 'assets/catalog/models.json'
    data = json.loads(manifest.read_text(encoding='utf-8'))
    data['entries'] = list({(r['group'],r['kind']):r for r in records}.values())
    data['status'] = 'generated'
    manifest.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    print(f'Wrote {manifest}: {len(data["entries"])} entries')

if __name__ == '__main__': main()
