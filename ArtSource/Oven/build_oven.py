"""Build a stylized food-factory oven and its seamless four-second cooking loop."""
import bpy
import math
import os
from mathutils import Vector

OUT = 'G:/Unity/FoodFactoryGame/ArtSource/Oven'
scene = bpy.context.scene
if bpy.context.object and bpy.context.object.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
model = bpy.data.collections.new('OVEN | Model')
scene.collection.children.link(model)
studio = bpy.data.collections.new('OVEN | Presentation')
scene.collection.children.link(studio)

def move(obj, collection=model):
    for col in list(obj.users_collection):
        col.objects.unlink(obj)
    collection.objects.link(obj)
    return obj

def mat(name, color, metallic=0, rough=.4, emission=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    p = m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value = (*color, 1)
    p.inputs['Metallic'].default_value = metallic
    p.inputs['Roughness'].default_value = rough
    if emission:
        p.inputs['Emission Color'].default_value = (*color, 1)
        p.inputs['Emission Strength'].default_value = emission
    return m

cream = mat('Enamel | Warm porcelain', (.76,.69,.51), .35,.3)
teal = mat('Enamel | Deep petrol', (.028,.19,.20), .48,.31)
dark = mat('Cavity | Graphite', (.022,.033,.043), .4,.43)
rubber = mat('Gaskets | Soft charcoal', (.009,.017,.023), 0,.64)
steel = mat('Metal | Brushed stainless', (.43,.52,.55), .8,.29)
orange = mat('Accent | Safety tangerine', (.93,.22,.045), .25,.3)
gold = mat('Bread | Toasted crust', (.67,.27,.055), 0,.76)
score = mat('Bread | Scored dough', (.98,.64,.23), 0,.84)
heat = mat('Heat | Amber element', (1,.105,.008), .15,.3, 4)
green = mat('Status | Cooking mint', (.1,1,.49), .1,.3, 2)
ink = mat('Lettering | Ivory', (.92,.88,.70), 0,.55)

root = bpy.data.objects.new('FF_Oven', None)
model.objects.link(root)
root['asset'] = 'Hearth 04 | Convection oven'
root['dimensions_m'] = '2.50 wide x 2.00 deep x 2.85 high, excluding steam'
root['animation'] = 'Cooking: 24 fps, frames 1-96; frame 97 repeats frame 1. Cycles extrapolation.'

def finish(obj, name, material, parent=root):
    move(obj)
    obj.name = name
    obj.parent = parent
    if material:
        obj.data.materials.append(material)
    return obj

def box(name, loc, size, material, bevel=.04, parent=root):
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = finish(bpy.context.object,name,material,parent)
    obj.scale = size
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    if bevel:
        mod = obj.modifiers.new('Soft manufactured edges','BEVEL')
        mod.width = bevel
        mod.segments = 3
        obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL')
    return obj

def cylinder(name, loc, radius, depth, material, rotation=(0,0,0), parent=root, vertices=32):
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=depth, location=loc, rotation=rotation)
    obj=finish(bpy.context.object,name,material,parent)
    bevel=obj.modifiers.new('Machined edge','BEVEL'); bevel.width=.012; bevel.segments=2
    obj.modifiers.new('Weighted normals','WEIGHTED_NORMAL')
    return obj

def sphere(name, loc, scale, material, parent=root):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20,ring_count=12,location=loc)
    obj=finish(bpy.context.object,name,material,parent)
    obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    for p in obj.data.polygons: p.use_smooth=True
    return obj

def line(name, coords, radius, material, parent=root):
    curve=bpy.data.curves.new(name,'CURVE'); curve.dimensions='3D'; curve.bevel_depth=radius; curve.bevel_resolution=3
    spline=curve.splines.new('POLY'); spline.points.add(len(coords)-1)
    for p,co in zip(spline.points,coords): p.co=(*co,1)
    obj=bpy.data.objects.new(name,curve); model.objects.link(obj); obj.parent=parent; curve.materials.append(material)
    return obj

def text(name, body, loc, size, material, rotation=(math.pi/2,0,0)):
    curve=bpy.data.curves.new(name,'FONT'); curve.body=body; curve.size=size; curve.align_x='CENTER'; curve.align_y='CENTER'; curve.extrude=.0008
    obj=bpy.data.objects.new(name,curve); model.objects.link(obj); obj.parent=root; obj.location=loc; obj.rotation_euler=rotation; curve.materials.append(material)
    return obj

for x in (-.94,.94):
    for y in (-.65,.65):
        cylinder('Adjustable steel foot',(x,y,.16),.095,.22,steel)
        cylinder('Rubber floor pad',(x,y,.065),.125,.07,rubber)
box('Lower plinth',(0,0,.32),(2.4,1.8,.28),teal,.075)
box('Insulated back',(0,.81,1.43),(2.4,.18,2.04),teal,.075)
box('Left casing',(-1.12,0,1.43),(.16,1.72,2.04),cream)
box('Control-side casing',(1.01,0,1.43),(.38,1.72,2.04),teal)
box('Chamber ceiling',(0,0,2.39),(2.4,1.8,.16),cream)
box('Upper enamel cap',(0,0,2.53),(2.49,1.89,.16),cream,.07)
box('Dark chamber back',(-.14,.69,1.40),(1.89,.06,1.86),dark)
box('Dark chamber floor',(-.14,0,.51),(1.89,1.6,.09),dark)
box('Dark chamber left',(-1.02,0,1.4),(.055,1.65,1.83),dark,.01)
box('Dark chamber right',(.75,0,1.4),(.055,1.65,1.83),dark,.01)

# Thick open-front door frame: a clear view of food and heating hardware.
for x in (-1.025,.77):
    box('Door gasket upright',(x,-.922,1.43),(.12,.12,1.88),rubber,.025)
    box('Door enamel upright',(x,-1.0,1.43),(.12,.12,1.88),cream,.025)
for z in (.53,2.32):
    box('Door gasket crossbar',(-.13,-.92,z),(1.89,.12,.12),rubber,.025)
    box('Door enamel crossbar',(-.13,-1.0,z),(1.91,.12,.12),cream,.025)
for z in (.81,2.04):
    cylinder('Door hinge',(-1.10,-1.045,z),.063,.23,steel)
for x in (-.68,.40):
    box('Handle mount',(x,-1.105,.70),(.10,.20,.10),steel,.025)
box('Cool-touch door handle',(-.14,-1.22,.70),(1.27,.12,.12),teal,.055)

# Rack-and-bread silhouettes are deliberately readable from the game camera.
for n,z in enumerate((.90,1.35,1.80)):
    box('Tray %02d | rim'%n,(-.14,-.03,z),(1.61,1.29,.07),steel,.025)
    box('Tray %02d | dark inset'%n,(-.14,-.03,z+.04),(1.48,1.15,.02),dark,.015)
    for x in (-.94,.66):
        box('Rack support',(x,.03,z-.08),(.055,1.30,.045),steel,.01)
    for x in (-.65,-.14,.37):
        for y in (-.39,.20):
            sphere('Golden baking roll',(x,y,z+.15),(.20,.24,.115),gold)
            for dy in (-.075,.035):
                line('Pale scored crust',[(x-.105,y+dy-.02,z+.236),(x-.06,y+dy,z+.257),(x+.015,y+dy+.04,z+.262),(x+.09,y+dy+.06,z+.244)],.012,score)
for x in (-.92,.64):
    for z in (1.09,1.54,1.99):
        line('Radiant amber rail',[(x,-.7,z),(x,.47,z),(x,.55,z+.05)],.021,heat)

# Convection fans use complete integer rotations over the loop.
fans=[]
for x in (-.57,.29):
    cylinder('Fan well',(x,.633,2.08),.25,.04,rubber,(math.pi/2,0,0))
    hub=bpy.data.objects.new('Cooking | convection rotor',None); model.objects.link(hub); hub.parent=root; hub.location=(x,.596,2.08)
    fans.append(hub)
    cylinder('Fan center',(0,0,0),.065,.06,steel,(math.pi/2,0,0),hub)
    for i in range(5):
        a=i*math.tau/5
        blade=box('Convection blade',(math.sin(a)*.135,0,math.cos(a)*.135),(.10,.022,.19),steel,.025,hub)
        blade.rotation_euler[1]=a+.28

box('Control fascia',(1.01,-.925,1.43),(.32,.065,1.86),dark,.035)
box('Temperature display',(1.01,-.974,2.12),(.265,.025,.23),rubber,.015)
text('Readout','180',(1.01,-.993,2.15),.11,green)
text('Unit label','CELSIUS',(1.01,-.995,2.055),.034,ink)
cylinder('Thermostat bezel',(1.01,-.999,1.78),.106,.035,steel,(math.pi/2,0,0))
cylinder('Thermostat grip',(1.01,-1.034,1.78),.081,.055,teal,(math.pi/2,0,0))
box('Thermostat pointer',(1.01,-1.066,1.824),(.012,.008,.033),ink,.003)
text('Mode label','BAKE',(1.01,-.976,1.59),.044,ink)
cylinder('Cooking lamp',(1.01,-.982,1.47),.041,.025,green,(math.pi/2,0,0))
text('Active label','ACTIVE',(1.01,-.977,1.36),.033,ink)
cylinder('Start button rim',(1.01,-.98,1.12),.071,.04,steel,(math.pi/2,0,0))
cylinder('Start button',(1.01,-1.009,1.12),.054,.04,orange,(math.pi/2,0,0))
for z in (.69,.76,.83):
    box('Control vent',(1.01,-.965,z),(.18,.018,.019),rubber,.006)
box('Brand plate',(-.16,-.96,2.53),(1.25,.025,.115),teal,.02)
text('Brand lettering','H E A R T H  /  0 4',(-.16,-.98,2.53),.073,ink)
text('Plinth lettering','F O O D   F A C T O R Y',(0,-.914,.325),.065,ink)

# Right-hand service panel and a second, exposed rotating cooling fan.
box('Right service panel',(1.215,.08,1.38),(.04,1.34,1.46),teal,.025)
for y in (-.48,.65):
    for z in (.75,2.01):
        cylinder('Service screw',(1.248,y,z),.026,.02,steel,(0,math.pi/2,0),vertices=12)
cylinder('Side fan surround',(1.258,.12,1.52),.39,.075,dark,(0,math.pi/2,0))
rotor=bpy.data.objects.new('Cooking | side rotor',None); model.objects.link(rotor); rotor.parent=root; rotor.location=(1.307,.12,1.52)
for i in range(6):
    a=i*math.tau/6
    b=box('Side fan blade',(0,math.sin(a)*.21,math.cos(a)*.21),(.035,.13,.25),steel,.03,rotor); b.rotation_euler[0]=-a+.3
cylinder('Side hub',(0,0,0),.09,.065,orange,(0,math.pi/2,0),rotor)
for r in (.17,.28,.365):
    line('Fan protective cage',[(1.358,.12+math.sin(i*math.tau/64)*r,1.52+math.cos(i*math.tau/64)*r) for i in range(65)],.011,dark)
for a in (0,math.pi/2):
    line('Cage cross brace',[(1.365,.12+math.sin(a)*t,1.52+math.cos(a)*t) for t in (-.365,.365)],.014,dark)
box('Service warning label',(1.249,-.05,.90),(.02,.46,.13),orange,.01)
for y in (-.49,-.35,-.21,-.07,.07,.21,.35,.49,.63):
    box('Top extraction louvre',(.14+y,.23,2.621),(.063,.70,.014),dark,.018)
box('Exhaust base',(-.79,.5,2.64),(.36,.40,.09),steel)
cylinder('Exhaust neck',(-.79,.5,2.76),.12,.22,teal)
cylinder('Exhaust hat',(-.79,.5,2.89),.19,.065,steel)

def cycles(idblock):
    action=idblock.animation_data.action
    try:
        curves=action.fcurves
    except AttributeError:
        curves=action.layers[0].strips[0].channelbag(idblock.animation_data.action_slot).fcurves
    for fc in curves:
        fc.modifiers.new('CYCLES')
    return curves

for obj,axis,turns in [(f,1,4) for f in fans]+[(rotor,0,-2)]:
    obj.rotation_mode='XYZ'
    obj.rotation_euler[axis]=0; obj.keyframe_insert(data_path='rotation_euler',index=axis,frame=1)
    obj.rotation_euler[axis]=math.tau*turns; obj.keyframe_insert(data_path='rotation_euler',index=axis,frame=97)
    obj.animation_data.action.name='Cooking | '+obj.name
    for fc in cycles(obj):
        for k in fc.keyframe_points: k.interpolation='LINEAR'

strength=heat.node_tree.nodes.get('Principled BSDF').inputs['Emission Strength']
for frame,value in ((1,3.0),(25,5.0),(49,3.0),(73,5.0),(97,3.0)):
    strength.default_value=value; strength.keyframe_insert(data_path='default_value',frame=frame)
for fc in cycles(heat.node_tree):
    for k in fc.keyframe_points: k.handle_left_type='AUTO_CLAMPED'; k.handle_right_type='AUTO_CLAMPED'

def area(name, loc, energy, color, size, target, collection=studio):
    data=bpy.data.lights.new(name,'AREA'); data.energy=energy; data.color=color; data.shape='DISK'; data.size=size
    obj=bpy.data.objects.new(name,data); collection.objects.link(obj); obj.location=loc; obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    return obj

chamber=area('Cooking | amber chamber light',(-.14,-.32,2.22),45,(1,.30,.045),1.1,(-.14,0,.8),model); chamber.parent=root
for frame,value in ((1,35),(25,60),(49,35),(73,60),(97,35)):
    chamber.data.energy=value; chamber.data.keyframe_insert(data_path='energy',frame=frame)
for fc in cycles(chamber.data):
    for k in fc.keyframe_points: k.handle_left_type='AUTO_CLAMPED'; k.handle_right_type='AUTO_CLAMPED'

floor=mat('Studio | Slate',(.035,.062,.082),.05,.6)
obj=box('Presentation pedestal',(0,0,-.08),(3.35,2.95,.16),dark,.09); move(obj,studio); obj.parent=None
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,0,-.17)); obj=move(bpy.context.object,studio); obj.name='Studio floor'; obj.data.materials.append(floor)
area('Studio | Warm key',(1,-4,6),750,(1,.84,.64),5,(0,0,1.1))
area('Studio | Cool fill',(-4,-2,3),550,(.52,.78,1),4,(0,0,1.2))
area('Studio | Rim',(3,4,5),1000,(.62,.86,1),3,(0,0,1.5))
data=bpy.data.cameras.new('Oven presentation camera'); cam=bpy.data.objects.new('Oven presentation camera',data); studio.objects.link(cam)
cam.location=(4.6,-7,4.4); cam.rotation_euler=(Vector((0,0,1.33))-cam.location).to_track_quat('-Z','Y').to_euler(); data.type='ORTHO'; data.ortho_scale=4.9; data.lens=48; scene.camera=cam
scene.render.engine='CYCLES'; scene.cycles.samples=48
scene.cycles.use_denoising=True
scene.render.resolution_x=1200; scene.render.resolution_y=1200; scene.render.resolution_percentage=100
scene.world.color=(.15,.15,.15)
scene.render.fps=24; scene.frame_start=1; scene.frame_end=96; scene.frame_set(1)
scene.timeline_markers.new('COOKING | loop start',frame=1)
scene.timeline_markers.new('4 seconds | duplicate endpoint at 97',frame=97)
scene.view_settings.view_transform='AgX'
for obj in bpy.context.selected_objects: obj.select_set(False)
root.select_set(True); bpy.context.view_layer.objects.active=root
for screen in bpy.data.screens:
    for area_ui in screen.areas:
        if area_ui.type=='VIEW_3D':
            area_ui.spaces.active.region_3d.view_perspective='CAMERA'
            area_ui.spaces.active.overlay.show_overlays=False
            area_ui.spaces.active.shading.type='MATERIAL'
os.makedirs(OUT,exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=OUT+'/FF_Oven.blend')
scene.render.image_settings.file_format='PNG'; scene.render.filepath=OUT+'/FF_Oven.png'
bpy.ops.render.render(write_still=True)
result={'blend':OUT+'/FF_Oven.blend','preview':OUT+'/FF_Oven.png','model_objects':len(model.objects),'loop':'1-96, 24fps, repeated endpoint 97'}
