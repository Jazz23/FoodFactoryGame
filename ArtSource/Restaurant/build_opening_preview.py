"""Assemble opening/frame/leaf pairs for fit review; export the same display layout for Unity preview rendering."""
import bpy
import json
import math
from pathlib import Path
from mathutils import Vector
ROOT=Path(globals().get('RESTAURANT_ROOT','G:/Unity/FoodFactoryGame/ArtSource/Restaurant'))
scene=bpy.data.scenes.new('Opening Fit Study');bpy.context.window.scene=scene
scene.world=bpy.data.scenes['Restaurant Kit'].world
def place(name,pos,angle=0):
    obj=bpy.data.objects.new(name,bpy.data.objects['RT_'+name].data);scene.collection.objects.link(obj);obj.location=pos;obj.rotation_euler.z=math.radians(angle)
for x,style in [(0,'Panel'),(3,'Glazed'),(6,'Kitchen')]:
    place('Doorway_Single_2m',(x,0,0));place('DoorFrame_Single',(x,0,0));place('DoorLeaf_'+style,(x-.475,0,.005))
for x,kind in [(9,'Picture'),(12,'Mullioned')]:
    place('WindowOpening_2m',(x,0,0));place('Window_'+kind,(x,0,.85));place('Plant_Floor',(x,.65,0))
place('Doorway_Double_3m',(15,0,0));place('DoorFrame_Double',(15,0,0))
place('DoorLeaf_Glazed',(14.03,0,.005),-70);place('DoorLeaf_Glazed',(15.97,0,.005),180)
for x in range(-1,17):
    for y in (-.5,.5):place('Floor_Terracotta_1m',(x,y,0))
(ROOT/'opening-layout.json').write_text(json.dumps([{'model':o.data.name,'position':list(o.location),'angle':math.degrees(o.rotation_euler.z)} for o in scene.objects if o.type=='MESH'],indent=2))
cam=bpy.data.objects.new('Fit Camera',bpy.data.cameras.new('Fit Camera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=20;target=Vector((7.5,0,1));cam.location=(11,-22,11);cam.rotation_euler=(target-cam.location).to_track_quat('-Z','Y').to_euler()
light=bpy.data.objects.new('Fit Softbox',bpy.data.lights.new('Fit Softbox','AREA'));scene.collection.objects.link(light);light.location=(6,-4,12);light.data.energy=2400;light.data.size=15;light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='CYCLES';scene.cycles.samples=24;scene.render.resolution_x=1800;scene.render.resolution_y=700;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX';scene.render.image_settings.file_format='PNG';scene.render.filepath=str(ROOT/'Opening_Fit.png')
bpy.ops.render.render(write_still=True)
bpy.context.window.scene=bpy.data.scenes['Restaurant Vignette'];bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Restaurant_Kit.blend'))
result={'preview':scene.render.filepath}
