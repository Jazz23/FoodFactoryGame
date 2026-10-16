"""Blender CLI worker: split each mesh object to its own GLB, retaining PBR material information."""
import bpy, json, re, sys
from pathlib import Path

src, out, art = map(Path, sys.argv[sys.argv.index('--')+1:])
out.mkdir(parents=True, exist_ok=True)
bpy.ops.import_scene.fbx(filepath=str(src), use_image_search=True)

# Read-only bridge from Unity .mat GUID references to source textures and PBR scalar/color values.
guid_paths = {}
for meta in art.rglob('*.meta'):
    m = re.search(r'^guid:\s*(\w+)', meta.read_text(errors='ignore'), re.M)
    if m: guid_paths[m.group(1)] = meta.with_suffix('')
unity_mats = {}
for matfile in art.rglob('*.mat'):
    text = matfile.read_text(errors='ignore')
    n = re.search(r'^  m_Name:\s*(.+)$', text, re.M)
    if not n: continue
    colors = re.search(r'(?m)^    - _(?:BaseColor|Color): \{r: ([\d.eE+-]+), g: ([\d.eE+-]+), b: ([\d.eE+-]+), a: ([\d.eE+-]+)\}', text)
    metallic = re.search(r'(?m)^    - _Metallic: ([\d.eE+-]+)', text)
    smooth = re.search(r'(?m)^    - _Smoothness: ([\d.eE+-]+)', text)
    texture_guids = re.findall(r'(?m)^\s*m_Texture: \{fileID: \d+, guid: (\w+), type: \d+\}', text)
    unity_mats[n.group(1).strip()] = {'color': tuple(map(float, colors.groups())) if colors else None,
        'metallic': float(metallic.group(1)) if metallic else 0.0,
        'roughness': 1.0-float(smooth.group(1)) if smooth else 0.5,
        'textures': [guid_paths[g] for g in texture_guids if g in guid_paths]}

def configure_material(mat):
    spec = unity_mats.get(mat.name)
    if not spec: return
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get('Principled BSDF')
    if not bsdf: return
    if spec['color']: bsdf.inputs['Base Color'].default_value = spec['color']
    bsdf.inputs['Metallic'].default_value = spec['metallic']
    bsdf.inputs['Roughness'].default_value = spec['roughness']
    # Base-color and tangent normal maps are inferred by texture names, not guessed from arbitrary slots.
    for texpath in spec['textures']:
        if not texpath.is_file() or texpath.suffix.lower() not in {'.png', '.jpg', '.tga'}: continue
        image = bpy.data.images.load(str(texpath), check_existing=True)
        node = mat.node_tree.nodes.new('ShaderNodeTexImage'); node.image = image
        if 'Normal' in texpath.stem:
            normal = mat.node_tree.nodes.new('ShaderNodeNormalMap')
            mat.node_tree.links.new(node.outputs['Color'], normal.inputs['Color'])
            mat.node_tree.links.new(normal.outputs['Normal'], bsdf.inputs['Normal'])
        elif 'Base Color' not in bsdf.inputs or not any(l.to_node == bsdf and l.to_socket == bsdf.inputs['Base Color'] for l in mat.node_tree.links):
            mat.node_tree.links.new(node.outputs['Color'], bsdf.inputs['Base Color'])
        try: image.pack()
        except RuntimeError: pass

mesh_objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
records = []
for obj in mesh_objects:
    if obj.name == 'Cube':
        # Blender's default/collateral FBX mesh is not an authored catalog model.
        continue
    for mat in obj.data.materials:
        if mat: configure_material(mat)
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    # Apply source world transform into geometry so standalone GLBs don't inherit catalog layout offsets.
    matrix = obj.matrix_world.copy()
    obj.data.transform(matrix)
    obj.matrix_world.identity()
    # Bounding box is now in the asset's authored local/world space and has no catalog scene offset.
    corners = [obj.matrix_world @ __import__('mathutils').Vector(c) for c in obj.bound_box]
    lo = [min(v[i] for v in corners) for i in range(3)]; hi = [max(v[i] for v in corners) for i in range(3)]
    # Unity/ArtSource FBXs use Z-up. glTF export rotates to Y-up: (x,y,z)->(x,z,-y).
    srcdims = [hi[i]-lo[i] for i in range(3)]
    srccenter = [(hi[i]+lo[i])*0.5 for i in range(3)]
    dims = [round(srcdims[0], 5), round(srcdims[2], 5), round(srcdims[1], 5)]
    center = [round(srccenter[0], 5), round(srccenter[2], 5), round(-srccenter[1], 5)]
    name = obj.name
    group = 'world' if 'WorldArt' in src.name else ('vehicles' if 'Vehicles' in src.name else 'kitchen-equipment')
    if group == 'kitchen-equipment':
        safe = {'Kitchen_Counter':'kitchen-counter','Kitchen_Fryer':'fryer','Kitchen_Grill':'grill','Kitchen_Mixer':'mixer','Kitchen_Stove':'stove'}.get(name, re.sub(r'[^A-Za-z0-9_.-]+', '_', name).lower())
        out = out.parent / 'equipment'
    elif group == 'vehicles':
        safe = re.sub(r'^Vehicle_', '', name).lower().replace('_', '-')
    else:
        safe = re.sub(r'[^A-Za-z0-9_.-]+', '_', name)
    target = out / f'{safe}.glb'
    bpy.ops.export_scene.gltf(filepath=str(target), export_format='GLB', use_selection=True,
        export_materials='EXPORT', export_image_format='AUTO', export_apply=True)
    if not target.is_file() or target.stat().st_size < 20: raise RuntimeError(f'GLB export failed: {target}')
    records.append({'kind': name, 'group': group, 'path': target.relative_to(Path(__file__).resolve().parents[1]).as_posix(),
        'source': src.relative_to(Path(__file__).resolve().parents[2]).as_posix(),
        'dimensions': dims, 'offset': center, 'scale': [1,1,1],
        'materials': [m.name for m in obj.data.materials if m],
        'notes': 'Measured Blender FBX mesh bounds; catalog object split, Unity prefab rotation/placement not applied.'})
print('CATALOG_JSON:'+json.dumps(records))
