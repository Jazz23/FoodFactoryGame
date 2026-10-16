"""Rebuild Unity built-in primitive meshes from read-only prefab YAML and export one GLB."""
import bpy, math, re, sys
from pathlib import Path
from mathutils import Matrix, Quaternion, Vector

prefab, art, destination = map(Path, sys.argv[sys.argv.index('--')+1:])
text = prefab.read_text(errors='ignore')
docs = {}
for m in re.finditer(r'(?m)^--- !u!(\d+) &(\d+)\s*\n(.*?)(?=^--- !u!|\Z)', text, re.S|re.M):
    docs[m.group(2)] = (int(m.group(1)),m.group(3))
def field(doc, key, default=None):
    m=re.search(r'(?m)^\s*'+re.escape(key)+r':\s*(.*)$',doc)
    return m.group(1).strip() if m else default
def vec(doc,key,default=(0,0,0)):
    m=re.search(r'(?m)^\s*'+key+r': \{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',doc)
    return tuple(float(x) for x in m.groups()) if m else default
def mat_from(guid):
    for meta in art.rglob('*.meta'):
        if re.search(r'(?m)^guid:\s*'+re.escape(guid)+r'\s*$',meta.read_text(errors='ignore')):
            path=meta.with_suffix('')
            if path.suffix!='.mat': return None
            body=path.read_text(errors='ignore')
            name=re.search(r'(?m)^  m_Name: (.+)$',body)
            color=re.search(r'(?m)^    - _(?:BaseColor|Color): \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}',body)
            scalar=lambda key, default: float(x.group(1)) if (x:=re.search(r'(?m)^    - '+key+r': ([^\s]+)',body)) else default
            return (name.group(1).strip() if name else path.stem, tuple(map(float,color.groups())) if color else (0.7,0.7,0.7,1), scalar('_Metallic',0),1-scalar('_Smoothness',.5))
    return None
go_names={}; go_components={}; transforms={}; filters={}; renderers={}
for fid,(typ,d) in docs.items():
    if typ==1:
        go_names[fid]=field(d,'m_Name','')
        cm=re.search(r'(?ms)^  m_Component:\s*((?:  - component:.*\n)+)',d)
        go_components[fid]=re.findall(r'fileID: (\d+)',cm.group(1)) if cm else []
    elif typ==4:
        go=re.search(r'(?m)^  m_GameObject: \{fileID: (\d+)\}',d); parent=re.search(r'(?m)^  m_Father: \{fileID: (\d+)\}',d)
        if go:
            transforms[fid]={'go':go.group(1),'pos':vec(d,'m_LocalPosition'),'scale':vec(d,'m_LocalScale',(1,1,1)),
                'rot':tuple(float(x) for x in re.search(r'(?m)^  m_LocalRotation: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}',d).groups()),'parent':parent.group(1) if parent else '0'}
    elif typ==33:
        go=re.search(r'(?m)^  m_GameObject: \{fileID: (\d+)\}',d); mesh=re.search(r'(?m)^  m_Mesh: \{fileID: (\d+), guid: ([^,]+)',d)
        if go and mesh: filters[go.group(1)]=(mesh.group(1),mesh.group(2))
    elif typ==23:
        go=re.search(r'(?m)^  m_GameObject: \{fileID: (\d+)\}',d); mats=re.search(r'(?ms)^  m_Materials:\s*((?:  - \{[^\n]+\}\n?)+)',d)
        if go: renderers[go.group(1)]=re.findall(r'guid: ([^,}]+)',mats.group(1)) if mats else []

def world_matrix(tid,seen=None):
    t=transforms[tid]; q=Quaternion((t['rot'][3],t['rot'][0],t['rot'][1],t['rot'][2]))
    local=Matrix.LocRotScale(Vector(t['pos']),q,Vector(t['scale']))
    par=t['parent']
    return world_matrix(par)@local if par in transforms else local

for o in list(bpy.context.scene.objects):
    bpy.data.objects.remove(o,do_unlink=True)
for go,(meshid,meshguid) in filters.items():
    if meshguid!='0000000000000000e000000000000000':
        # Non-builtin meshes are exported from their source FBX catalogs separately.
        continue
    tids=[tid for tid,t in transforms.items() if t['go']==go]
    if not tids: continue
    # Unity built-in mesh file IDs: cube 10202, sphere 10207, capsule 10208, cylinder 10206, plane 10209, quad 10210.
    ops={'10202':bpy.ops.mesh.primitive_cube_add,'10207':bpy.ops.mesh.primitive_uv_sphere_add,
         '10208':bpy.ops.mesh.primitive_uv_sphere_add,'10206':bpy.ops.mesh.primitive_cylinder_add,
         '10209':bpy.ops.mesh.primitive_plane_add,'10210':bpy.ops.mesh.primitive_plane_add}
    op=ops.get(meshid)
    if not op: continue
    if meshid=='10202': op(size=1.0)
    elif meshid in {'10207','10208'}: op(radius=0.5)
    elif meshid=='10206': op(radius=0.5,depth=2.0)
    elif meshid=='10209': op(size=10.0)
    else: op(size=1.0)
    obj=bpy.context.object; obj.name=go_names.get(go,'mesh')
    # Blender planes/cylinders are Z-up; Unity primitives are Y-up before prefab transforms.
    if meshid in {'10206','10209'}:
        obj.data.transform(Matrix.Rotation(-math.pi/2,4,'X'))
    # Convert Unity Y-up prefab transforms into Blender's Z-up before the glTF exporter rotates back.
    unity_to_blender=Matrix.Rotation(math.pi/2,4,'X')
    obj.data.transform(unity_to_blender @ world_matrix(tids[0]))
    obj.matrix_world=Matrix.Identity(4)
    for guid in renderers.get(go,[]):
        spec=mat_from(guid)
        if not spec: continue
        name,color,metal,rough=spec
        mat=bpy.data.materials.get(name) or bpy.data.materials.new(name)
        mat.diffuse_color=color; mat.use_nodes=True
        bsdf=mat.node_tree.nodes.get('Principled BSDF')
        if bsdf:
            bsdf.inputs['Base Color'].default_value=color; bsdf.inputs['Metallic'].default_value=metal; bsdf.inputs['Roughness'].default_value=rough
        obj.data.materials.append(mat)
    # Ensure each primitive contributes its mapped materials; prefab renderer materials are one or more slots.
    obj.select_set(True)

destination.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.gltf(filepath=str(destination),export_format='GLB',export_materials='EXPORT',export_image_format='AUTO',export_apply=True)
# Bounds in Unity/Bevy Y-up coordinates for the runtime model catalog.
verts=[]
for obj in bpy.context.scene.objects:
    if obj.type=='MESH': verts.extend([obj.matrix_world @ v.co for v in obj.data.vertices])
if verts:
    # Blender back to Unity: (x,y,z)->(x,z,-y)
    u=[Vector((v.x,v.z,-v.y)) for v in verts]
    lo=[min(v[i] for v in u) for i in range(3)]; hi=[max(v[i] for v in u) for i in range(3)]
    meta={'dimensions':[round(hi[i]-lo[i],5) for i in range(3)],'offset':[round((hi[i]+lo[i])/2,5) for i in range(3)],
          'materials':sorted({m.name for o in bpy.context.scene.objects if o.type=='MESH' for m in o.data.materials if m})}
    destination.with_suffix('.json').write_text(__import__('json').dumps(meta,indent=2)+'\n')
