"""Render all 59 assets with labels in four studio contact sheets, without modifying the model sources."""
import bpy
import math
from pathlib import Path
from mathutils import Vector
ROOT=Path(globals().get('RESTAURANT_ROOT','G:/Unity/FoodFactoryGame/ArtSource/Restaurant'))
source=bpy.data.scenes['Restaurant Kit']
objects=[o for o in source.objects if o.type=='MESH']
groups={
    'Walls': [o for o in objects if any(s in o.name for s in ('_Wall_','_WallEnd_','_Partition_','_Cornice_'))],
    'Openings': [o for o in objects if any(s in o.name for s in ('_Door','_Window','_Serving'))],
    'Furniture_Service': [o for o in objects if o.users_collection[0].name in ('Furniture','Service')],
    'Decor_Lighting_Surfaces': [o for o in objects if o.users_collection[0].name in ('Decor','Lighting','Surfaces')],
}
assert sum(len(v) for v in groups.values())==59
for key,items in groups.items():
    scene=bpy.data.scenes.new('Sheet '+key); bpy.context.window.scene=scene;scene.world=source.world
    cols=4;rows=math.ceil(len(items)/cols)
    for i,original in enumerate(items):
        obj=bpy.data.objects.new(original.name+' Display',original.data);scene.collection.objects.link(obj);obj.location=((i%cols)*3.5,(i//cols)*4.4,0)
        if 'Pendant' in original.name or 'CeilingStrip' in original.name: obj.location.z=1
        font=bpy.data.curves.new('Label','FONT');font.body=original.name.removeprefix('RT_');font.size=.19;font.align_x='CENTER';font.materials.append(bpy.data.materials['RT_Charcoal'])
        label=bpy.data.objects.new('Label',font);scene.collection.objects.link(label);label.location=(obj.location.x,obj.location.y-1.1,-.08)
    target=Vector((5.25,(rows-1)*2.2,.4))
    cam=bpy.data.objects.new('Sheet Camera',bpy.data.cameras.new('Sheet Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=max(17,rows*4.7);cam.location=target+Vector((3,-14,23));cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
    mesh=bpy.data.meshes.new('Studio Floor');mesh.from_pydata([(-40,-40,-.12),(50,-40,-.12),(50,60,-.12),(-40,60,-.12)],[],[(0,1,2,3)]);mesh.materials.append(bpy.data.materials['RT_Cream']);floor=bpy.data.objects.new('Studio Floor',mesh);scene.collection.objects.link(floor)
    light=bpy.data.objects.new('Softbox',bpy.data.lights.new('Softbox','AREA'));scene.collection.objects.link(light);light.location=target+Vector((0,-4,15));light.data.energy=2600;light.data.size=15;light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
    scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1600;scene.render.resolution_y=1600;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/('Sheet_'+key+'.png'))
    bpy.ops.render.render(write_still=True)
bpy.context.window.scene=bpy.data.scenes['Restaurant Vignette']
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Restaurant_Kit.blend'))
result={'sheets':{k:len(v) for k,v in groups.items()}}
