"""Make exported equipment GLBs floor-centred in Blender CLI; source Unity files are never written."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector

root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
catalog=json.loads((root/'assets/catalog/models.json').read_text(encoding='utf-8'))
records=[]
seen=set()
for entry in catalog['entries']:
    if entry.get('group')!='equipment' or not entry.get('prefab'):
        continue
    if entry['kind'] in seen: continue
    seen.add(entry['kind'])
    path=root/entry['path']
    if not path.exists():
        continue
    for obj in list(bpy.data.objects): bpy.data.objects.remove(obj,do_unlink=True)
    bpy.ops.import_scene.gltf(filepath=str(path))
    bpy.context.view_layer.update()
    meshes=[obj for obj in bpy.context.scene.objects if obj.type=='MESH']
    if not meshes: continue
    points=[obj.matrix_world@Vector(corner) for obj in meshes for corner in obj.bound_box]
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    offset=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    for obj in bpy.context.scene.objects:
        if obj.parent is None: obj.location-=offset
    bpy.context.view_layer.update()
    target=root/'assets/models/gameplay'/f"{entry['kind']}.glb"
    target.parent.mkdir(parents=True,exist_ok=True)
    bpy.ops.export_scene.gltf(filepath=str(target),export_format='GLB',export_apply=True)
    records.append({'kind':entry['kind'],'path':target.relative_to(root/'assets').as_posix(),
        'size':[high.x-low.x,high.z-low.z,high.y-low.y]})
(root/'assets/catalog/equipment-runtime.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print(f'Normalized {len(records)} equipment assets')
