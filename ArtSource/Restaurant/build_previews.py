"""Create an art-only assembled vignette and category sheets; preserves all existing scenes.
Run after build_restaurant.py in the same Blender file. The display is not a game scene.
"""
import bpy
from pathlib import Path
from mathutils import Vector
import math
import json

ROOT=Path(globals().get('RESTAURANT_ROOT','G:/Unity/FoodFactoryGame/ArtSource/Restaurant'))
source=bpy.data.scenes['Restaurant Kit']
assert 'Restaurant Vignette' not in bpy.data.scenes
scene=bpy.data.scenes.new('Restaurant Vignette')
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
scene.world=source.world
def place(name,pos,angle=0):
    original=bpy.data.objects['RT_'+name]
    obj=bpy.data.objects.new(name,original.data); scene.collection.objects.link(obj)
    obj.location=pos; obj.rotation_euler.z=math.radians(angle)
    return obj
for x in range(8):
    for y in range(6): place('Floor_Checker_1m',(x+.5,y+.5,0))
for x in (1,3,5,7): place('Wall_Wainscot_2m',(x,6,0))
for y in (1,3,5): place('Wall_Brick_2m',(0,y,0),90)
for x in (1,3): place('Booth_Straight_2m',(x,5.5,0))
for x in (1,3):
    place('Table_BistroRound',(x,4.65,0)); place('Table_Caddy',(x,4.65,.75))
    place('Chair_Upholstered',(x,3.8,0),180)
place('Table_Communal',(2.2,1.8,0))
for x in (1.45,2.2,2.95):
    place('Chair_Spindle',(x,1.05,0),180); place('Chair_Spindle',(x,2.55,0))
place('Vase_Ceramic',(2.2,1.8,.75))
place('Planter_Divider_1_6m',(4.5,2.7,0),90)
place('Plant_Floor',(.6,.65,0)); place('Plant_Floor',(4.8,5.5,0))
place('WallArt_Abstract',(1,5.86,1.45)); place('WallArt_Abstract',(3,5.86,1.45))
place('Light_Sconce',(2,5.86,1.95)); place('Light_Sconce',(4,5.86,1.95))
place('MenuBoard_Wall',(6.5,5.86,1.85))
place('StorageShelf_1_2m',(7,5.5,0)); place('Sink_Single',(5.55,5.45,0))
place('PrepTable_1_5m',(6.5,3.6,0)); place('Trolley_TwoShelf',(7.25,2.1,0))
place('WasteBin',(7.5,4.65,0)); place('MenuBoard_AFrame',(6,.65,0))
place('Light_Pendant',(1,4.65,3)); place('Light_Pendant',(3,4.65,3)); place('Light_Pendant',(2.2,1.8,3))
(ROOT/'vignette-layout.json').write_text(json.dumps([{'model':o.data.name,'position':list(o.location),'angle':math.degrees(o.rotation_euler.z)} for o in scene.objects if o.type=='MESH'],indent=2))
camera=bpy.data.objects.new('Vignette Camera',bpy.data.cameras.new('Vignette Camera')); scene.collection.objects.link(camera); scene.camera=camera
camera.location=(12,-13,13); target=Vector((3.7,3,1)); camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler(); camera.data.type='ORTHO'; camera.data.ortho_scale=12.8
for name,pos,power,size in [('Warm key',(1,-2,10),1800,8),('Soft fill',(10,3,7),1300,7)]:
    obj=bpy.data.objects.new(name,bpy.data.lights.new(name,'AREA')); scene.collection.objects.link(obj); obj.location=pos; obj.data.energy=power; obj.data.shape='DISK'; obj.data.size=size; obj.rotation_euler=(target-obj.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine='CYCLES'; scene.cycles.samples=40
scene.render.resolution_x=1600; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'; scene.render.image_settings.file_format='PNG'; scene.render.filepath=str(ROOT/'Restaurant_Vignette.png')
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Restaurant_Kit.blend'))
bpy.ops.render.render(write_still=True)
result={'preview':scene.render.filepath}
