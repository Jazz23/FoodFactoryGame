import bpy,json,sys,math
from pathlib import Path
from mathutils import Vector
glb,out=[str(Path(x).resolve()) for x in sys.argv[sys.argv.index('--')+1:]]
for obj in list(bpy.data.objects):
 bpy.data.objects.remove(obj,do_unlink=True)
bpy.ops.import_scene.gltf(filepath=glb)
meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
if not meshes: raise RuntimeError('GLB contains no mesh: '+glb)
pts=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
lo=[min(p[i] for p in pts) for i in range(3)]; hi=[max(p[i] for p in pts) for i in range(3)]
center=Vector([(lo[i]+hi[i])/2 for i in range(3)]); size=max(hi[i]-lo[i] for i in range(3))
# Camera-facing material preview; neutral studio lights expose shape and missing materials.
for obj in list(bpy.context.scene.objects):
 if obj.type in {'CAMERA','LIGHT'}: bpy.data.objects.remove(obj,do_unlink=True)
bpy.ops.object.camera_add(location=center+Vector((size*1.8,size*1.35,size*1.8)))
cam=bpy.context.object; cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler()
scene=bpy.context.scene; scene.camera=cam; cam.data.type='ORTHO'; cam.data.ortho_scale=max(size*1.8,.5)
for loc,power,size_l in [(center+Vector((size,size,size*1.5)),1200,size),(center+Vector((-size*.8,size*.5,-size)),800,size*.8)]:
 bpy.ops.object.light_add(type='AREA',location=loc); l=bpy.context.object; l.data.energy=power;l.data.shape='DISK';l.data.size=size;l.rotation_euler=(center-l.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='BLENDER_EEVEE_NEXT'; scene.render.resolution_x=640;scene.render.resolution_y=480;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'; scene.render.filepath=out
scene.world.color=(.18,.18,.18); scene.view_settings.view_transform='Standard'
bpy.ops.render.render(write_still=True)
materials=sorted({m.name for o in meshes for m in o.data.materials if m})
report={'path':glb,'bytes':Path(glb).stat().st_size,'meshObjects':len(meshes),'materials':materials,
 'dimensions':[round(hi[0]-lo[0],5),round(hi[2]-lo[2],5),round(hi[1]-lo[1],5)],'render':out}
print('VALIDATION_JSON:'+json.dumps(report))
