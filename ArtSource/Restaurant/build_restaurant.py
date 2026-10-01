"""Build the missing modular restaurant art in a separate Blender scene; export individual metre-scale FBXs.

Run in Blender with RESTAURANT_ROOT set to this checkout's ArtSource/Restaurant.
Existing scenes and objects are preserved. Refuses to overwrite an existing generated scene.
"""
import bpy
import bmesh
import math
import json
from pathlib import Path
from mathutils import Vector

ROOT = Path(globals().get('RESTAURANT_ROOT', 'G:/Unity/FoodFactoryGame/ArtSource/Restaurant'))
OUT = ROOT.parent.parent / 'Assets/Art/Restaurant'
ROOT.mkdir(parents=True, exist_ok=True)
assert 'Restaurant Kit' not in bpy.data.scenes, 'Open a fresh Blender file before regenerating the kit.'
scene = bpy.data.scenes.new('Restaurant Kit')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0
palette = {
    'Cream': ('#eadfc9', .78, 0), 'Teal': ('#285d59', .55, 0),
    'Walnut': ('#67402c', .52, 0), 'Oak': ('#ba8954', .6, 0),
    'Charcoal': ('#293237', .52, .25), 'Brass': ('#ba9150', .3, .7),
    'Steel': ('#adbcbf', .3, .75), 'Terracotta': ('#b86143', .8, 0),
    'Brick': ('#985a44', .85, 0), 'Sage': ('#6c8760', .8, 0),
    'Leaf': ('#315b3e', .8, 0), 'Ivory': ('#f5efdc', .4, 0),
    'Grout': ('#a69984', .9, 0), 'Glow': ('#ffdda1', .3, 0),
    'Glass': ('#91bcb9', .12, 0), 'Chalk': ('#d8d5b9', .85, 0),
}

def linear(v):
    return v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4

materials = {}
for key, (hexcode, rough, metal) in palette.items():
    rgb = [int(hexcode[i:i+2], 16) / 255 for i in (1, 3, 5)]
    m = bpy.data.materials.new('RT_' + key)
    m.diffuse_color = (*[linear(c) for c in rgb], 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = m.diffuse_color
    bsdf.inputs['Roughness'].default_value = rough
    bsdf.inputs['Metallic'].default_value = metal
    if key == 'Glass':
        bsdf.inputs['Alpha'].default_value = .22
        m.surface_render_method = 'DITHERED'
    if key == 'Glow':
        bsdf.inputs['Emission Color'].default_value = m.diffuse_color
        bsdf.inputs['Emission Strength'].default_value = .4
    materials[key] = m

assets = []
manifest = []
collections = {}

class Mesh:
    def __init__(self, name, category, note=''):
        self.name, self.category, self.note = 'RT_' + name, category, note
        self.vertices, self.faces, self.indices, self.slots = [], [], [], []

    def face(self, points, mat):
        if mat not in self.slots:
            self.slots.append(mat)
        start = len(self.vertices)
        self.vertices.extend(points)
        self.faces.append(tuple(range(start, start + len(points))))
        self.indices.append(self.slots.index(mat))

    def box(self, center, size, mat, angle=0):
        x, y, z = center
        a, b, c = [v / 2 for v in size]
        points = []
        for px, py, pz in [(-a,-b,-c),(a,-b,-c),(a,b,-c),(-a,b,-c),(-a,-b,c),(a,-b,c),(a,b,c),(-a,b,c)]:
            points.append((x+px*math.cos(angle)-py*math.sin(angle), y+px*math.sin(angle)+py*math.cos(angle), z+pz))
        for ids in [(0,3,2,1),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7),(4,5,6,7)]:
            self.face([points[i] for i in ids], mat)

    def rod(self, start, end, radius, mat, n=12, r2=None):
        a, b = Vector(start), Vector(end)
        axis = (b-a).normalized()
        u = axis.cross(Vector((0,0,1)) if abs(axis.z) < .95 else Vector((0,1,0))).normalized()
        v = axis.cross(u)
        rings = [[tuple(p + r*(u*math.cos(i*2*math.pi/n)+v*math.sin(i*2*math.pi/n))) for i in range(n)] for p, r in [(a,radius),(b,radius if r2 is None else r2)]]
        self.face(list(reversed(rings[0])), mat)
        self.face(rings[1], mat)
        for i in range(n):
            j=(i+1)%n
            self.face([rings[0][i],rings[0][j],rings[1][j],rings[1][i]],mat)

    def cylinder(self, x, y, z, height, radius, mat, n=24, r2=None):
        self.rod((x,y,z),(x,y,z+height),radius,mat,n,r2)

    def leaf(self, start, end, width, mat):
        a, b = Vector(start), Vector(end)
        axis = b-a
        side = axis.cross(Vector((0,0,1))).normalized()*width
        mid = a+axis*.55
        ridge=mid+Vector((0,0,width*.3))
        for pts in [(a,mid-side,ridge),(a,ridge,mid+side),(b,ridge,mid-side),(b,mid+side,ridge)]:
            self.face([tuple(p) for p in pts],mat)
            self.face([tuple(p-Vector((0,0,.004))) for p in reversed(pts)],mat)

    def finish(self, bevel=.006, pivot=(0,0,0)):
        mesh = bpy.data.meshes.new(self.name)
        mesh.from_pydata(self.vertices, [], self.faces)
        for key in self.slots:
            mesh.materials.append(materials[key])
        for p, idx in zip(mesh.polygons,self.indices):
            p.material_index=idx
        bm=bmesh.new(); bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.00001)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(mesh); bm.free()
        uv=mesh.uv_layers.new(name='UVMap')
        for poly in mesh.polygons:
            major=max(range(3),key=lambda i:abs(poly.normal[i]))
            axes=[i for i in range(3) if i != major]
            for li in poly.loop_indices:
                co=mesh.vertices[mesh.loops[li].vertex_index].co
                uv.data[li].uv=(co[axes[0]],co[axes[1]])
        if self.category not in collections:
            col=bpy.data.collections.new(self.category); scene.collection.children.link(col); collections[self.category]=col
        obj=bpy.data.objects.new(self.name,mesh); collections[self.category].objects.link(obj)
        for other in bpy.context.selected_objects: other.select_set(False)
        obj.select_set(True); bpy.context.view_layer.objects.active=obj
        if bevel:
            mod=obj.modifiers.new('Soft manufactured edges','BEVEL'); mod.width=bevel; mod.segments=2
            bpy.ops.object.modifier_apply(modifier=mod.name)
            mod=obj.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True; mod.weight=40
            bpy.ops.object.modifier_apply(modifier=mod.name)
        for vertex in obj.data.vertices: vertex.co-=Vector(pivot)
        # Extend the feet to the placement plane without changing seat/worktop heights.
        if self.category in ('Furniture', 'Service'):
            low=min(v.co.z for v in obj.data.vertices)
            if 0 < low < .1:
                for vertex in obj.data.vertices:
                    if vertex.co.z < .1:
                        vertex.co.z=(vertex.co.z-low)*.1/(.1-low)
                obj.data.update()
        # Pin triangulation across Blender/FBX/Unity and discard collapsed bevel slivers.
        bm=bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.triangulate(bm, faces=list(bm.faces))
        slivers=[f for f in bm.faces if f.calc_area()<1e-8]
        bmesh.ops.delete(bm, geom=slivers, context='FACES_ONLY')
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
        obj.data.calc_loop_triangles()
        bpy.context.view_layer.update()
        path=OUT/'Models'/self.category/(self.name+'.fbx'); path.parent.mkdir(parents=True,exist_ok=True)
        bpy.ops.export_scene.fbx(filepath=str(path),use_selection=True,object_types={'MESH'},axis_forward='-Z',axis_up='Y',global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_UNITS',bake_space_transform=True,use_mesh_modifiers=True,mesh_smooth_type='FACE',add_leaf_bones=False,bake_anim=False,path_mode='AUTO')
        assets.append(obj)
        manifest.append({'name':self.name,'category':self.category,'path':path.relative_to(ROOT.parent.parent).as_posix(),'dimensions_blender_m':[round(v,4) for v in obj.dimensions],'triangles':len(obj.data.loop_triangles),'materials':[m.name for m in obj.data.materials],'pivot_blender_m':pivot,'note':self.note})
        return obj

def wall_surface(m,w,h,style):
    m.box((0,0,h/2),(w,.18,h),'Cream' if style != 'Brick' else 'Grout')
    if style=='Brick':
        for row in range(round(h/.2)):
            z=row*.2+.1
            offset=.25 if row%2 else 0
            for k in range(-1,round(w/.5)+1):
                lo=max(-w/2,-w/2+k*.5+offset); hi=min(w/2,-w/2+(k+1)*.5+offset)
                if hi-lo>.02:
                    for y in (-.099,.099): m.box(((lo+hi)/2,y,z),(hi-lo-.012,.025,.186),'Brick')
    if style=='Wainscot':
        for y in (-.102,.102):
            m.box((0,y,.55),(w,.035,1.1),'Teal')
            for x in [-w/2+.055+i*.32 for i in range(math.ceil(w/.32))]:
                if x<w/2-.02: m.box((x,y*1.2,.53),(.032,.025,.94),'Teal')
            m.box((0,y*1.23,1.1),(w,.05,.06),'Oak')
    if style=='Tile':
        for y in (-.101,.101):
            for row in range(6):
                for col in range(round(w/.25)):
                    m.box((-w/2+.125+col*.25,y,.125+row*.25),(.24,.03,.24),'Ivory' if row!=5 else 'Teal')
    for y in (-.111,.111): m.box((0,y,.055),(w,.045,.11),'Walnut' if style=='Wainscot' else 'Cream')

for style in ('Plaster','Brick','Wainscot','Tile'):
    for width in (1,2):
        m=Mesh(f'Wall_{style}_{width}m','Architecture','3 m high; centerline pivot. Visual thickness only, no occupancy rule.')
        wall_surface(m,width,3,style); m.finish(.002)
    m=Mesh(f'WallEnd_{style}','Architecture','0.24 m junction post conceals straight/corner/T joints; base pivot.')
    m.box((0,0,1.5),(.24,.24,3),'Brick' if style=='Brick' else 'Cream')
    if style=='Wainscot': m.box((0,0,.55),(.26,.26,1.1),'Teal')
    m.finish()

m=Mesh('Partition_HalfHeight_2m','Architecture'); wall_surface(m,2,1.2,'Wainscot'); m.box((0,0,1.23),(2,.29,.06),'Oak'); m.finish()
m=Mesh('Partition_Slatted_1m','Architecture')
for z in (.06,2.44): m.box((0,0,z),(1,.16,.12),'Walnut')
for i in range(9): m.box((-.44+i*.11,0,1.25),(.045,.1,2.4),'Oak')
m.finish()

for kind,width,hole,bottom,top in [('Doorway_Single_2m',2,1.04,0,2.24),('Doorway_Double_3m',3,2.04,0,2.24),('WindowOpening_2m',2,1.6,.85,2.5),('ServingHatch_2m',2,1.6,1.0,2.2)]:
    m=Mesh(kind,'Architecture','Real empty opening; frame/leaf supplied separately.')
    side=(width-hole)/2
    for x in (-(hole+side)/2,(hole+side)/2): m.box((x,0,1.5),(side,.2,3),'Cream')
    m.box((0,0,(3+top)/2),(hole,.2,3-top),'Cream')
    if bottom: m.box((0,0,bottom/2),(hole,.2,bottom),'Teal')
    if 'Hatch' in kind: m.box((0,-.14,1.01),(1.7,.55,.06),'Oak')
    m.finish(.003)

for name,width in [('DoorFrame_Single',1.04),('DoorFrame_Double',2.04)]:
    m=Mesh(name,'Architecture','Opening match: outer width; inner clear width is 0.08 m less.')
    for x in (-width/2+.02,width/2-.02): m.box((x,0,1.12),(.04,.26,2.24),'Walnut')
    m.box((0,0,2.22),(width,.26,.04),'Walnut'); m.finish(.003)
for style in ('Panel','Glazed','Kitchen'):
    m=Mesh('DoorLeaf_'+style,'Architecture','Hinge pivot at left edge, floor height. 0.95 x 2.18 m; fits single frame or paired in double.')
    if style=='Glazed':
        for x in (.04,.91): m.box((x,0,1.09),(.08,.06,2.18),'Teal')
        for z in (.1,1,2.12): m.box((.475,0,z),(.87,.06,.12),'Teal')
        m.box((.475,0,1.57),(.79,.016,.98),'Glass'); m.box((.475,0,.53),(.79,.016,.74),'Glass')
    else:
        m.box((.475,0,1.09),(.95,.055,2.18),'Walnut' if style=='Panel' else 'Steel')
        if style=='Panel':
            for z,h in [(.5,.7),(1.5,.95)]: m.box((.475,-.034,z),(.73,.025,h),'Oak')
        else:
            m.box((.475,-.04,1.65),(.42,.025,.5),'Charcoal'); m.box((.475,-.056,1.65),(.34,.012,.42),'Glass')
            m.box((.475,-.034,.18),(.84,.018,.28),'Charcoal')
    for y in (-.065,.065): m.rod((.78,y,1.02),(.88,y,1.02),.014,'Brass')
    for z in (.22,1.96): m.cylinder(.012,0,z,.13,.022,'Brass',12)
    m.finish(.004)

for divided in (False,True):
    m=Mesh('Window_'+('Mullioned' if divided else 'Picture'),'Architecture','1.6 x 1.65 m; pivot bottom center. Place at opening sill height 0.85 m.')
    for x in (-.775,.775): m.box((x,0,.825),(.05,.24,1.65),'Teal')
    for z in (.025,1.625): m.box((0,0,z),(1.6,.24,.05),'Teal')
    m.box((0,0,.825),(1.5,.016,1.55),'Glass')
    if divided:
        m.box((0,-.02,.825),(.04,.08,1.55),'Teal'); m.box((0,-.02,.825),(1.5,.08,.04),'Teal')
    m.box((0,-.05,0),(1.7,.34,.045),'Oak'); m.finish(.002)

for style in ('Checker','Terracotta','OakPlank','KitchenTile'):
    m=Mesh('Floor_'+style+'_1m','Surfaces','1 m repeat; top surface at Z=0; thickness below pivot.')
    m.box((0,0,-.045),(1,1,.05),'Grout')
    if style=='OakPlank':
        for i in range(5): m.box((-.4+i*.2,0,-.009),(.196,.996,.018),'Oak' if i%2 else 'Walnut')
    else:
        n=4 if style=='KitchenTile' else 2
        for i in range(n):
            for j in range(n): m.box((-.5+(i+.5)/n,-.5+(j+.5)/n,-.009),(1/n-.008,1/n-.008,.018),('Ivory' if (i+j)%2 else 'Charcoal') if style=='Checker' else ('Terracotta' if style=='Terracotta' else 'Ivory'))
    m.finish(.001)
m=Mesh('Ceiling_Panel_1m','Surfaces','1 m panel; underside at pivot, extends upwards.')
m.box((0,0,.035),(1,1,.07),'Cream'); m.finish(.002)
m=Mesh('Cornice_1m','Architecture','Wall attachment pivot at lower rear edge; projects towards -Y.')
for z,d,h in [(0,.12,.05),(.065,.19,.08),(.13,.25,.05)]: m.box((0,-d/2,z+h/2),(1,d,h),'Cream')
m.finish(.002)

def legs(m,w,d,h,mat='Charcoal'):
    for x in (-w/2,w/2):
        for y in (-d/2,d/2): m.rod((x*1.12,y*1.12,.025),(x,y,h),.025,mat)

for kind in ('BistroRound','Communal','HighRound'):
    m=Mesh('Table_'+kind,'Furniture','Standalone table; seating authored separately. Distinct from existing four-seat Table prefab.')
    h=1.05 if kind=='HighRound' else .75
    if kind=='Communal':
        for i in range(5): m.box((0,-.36+i*.18,h-.035),(2.4,.174,.07),'Oak')
        for x in (-.85,.85):
            m.box((x,0,h/2),(.08,.65,h-.08),'Charcoal')
        m.box((0,0,.22),(1.7,.07,.07),'Charcoal')
    else:
        r=.38 if kind=='HighRound' else .48
        m.cylinder(0,0,h-.055,.055,r,'Ivory' if kind=='BistroRound' else 'Oak',48)
        m.cylinder(0,0,.03,h-.08,.045,'Brass',20)
        m.cylinder(0,0,0,.04,.29,'Charcoal',32)
    m.finish()
for kind in ('Spindle','Upholstered','BarStool'):
    m=Mesh('Chair_'+kind,'Furniture','Front faces -Y; ground-center pivot. Seats are separate from tables.')
    h=.75 if kind=='BarStool' else .46
    legs(m,.34,.34,h,'Walnut')
    if kind=='BarStool':
        m.cylinder(0,0,h-.04,.07,.23,'Teal',32)
        for y in (-.18,.18): m.rod((-.18,y,.3),(.18,y,.3),.017,'Brass')
    else:
        m.box((0,0,h),(.47,.46,.075),'Teal' if kind=='Upholstered' else 'Oak')
        for x in (-.2,.2): m.rod((x,.19,.35),(x,.24,.93),.023,'Walnut')
        if kind=='Upholstered': m.box((0,.23,.75),(.46,.09,.31),'Teal')
        else:
            for i in range(5): m.rod((-.16+i*.08,.2,.5),(-.16+i*.08,.24,.87),.012,'Oak')
            m.box((0,.24,.9),(.47,.055,.085),'Walnut')
    m.finish(.009)
m=Mesh('Booth_Straight_2m','Furniture','Two-person banquette module; front -Y.')
m.box((0,0,.2),(2,.65,.4),'Walnut'); m.box((0,-.04,.46),(1.96,.68,.13),'Teal')
m.box((0,.31,.74),(2,.17,1.02),'Walnut')
for i in range(10): m.box((-.9+i*.2,.195,.86),(.19,.16,.7),'Teal')
m.box((0,-.34,.17),(1.95,.035,.035),'Brass'); m.finish(.012)
m=Mesh('Bench_Slatted_1_6m','Furniture')
for i in range(4): m.box((0,-.18+i*.12,.45),(1.6,.105,.045),'Oak')
legs(m,1.2,.3,.43); m.finish()

def pot(m,x,y,z,scale=1,tall=False):
    h=.38*scale
    m.cylinder(x,y,z,h,.17*scale,'Terracotta',20,r2=.24*scale)
    m.cylinder(x,y,z+h-.025*scale,.018*scale,.218*scale,'Walnut',20)
    for i in range(11):
        a=i*2.39996; r=(.34 if tall else .24)*scale
        end=(x+math.cos(a)*r,y+math.sin(a)*r,z+h+(.55+(i%4)*.13 if tall else .18+(i%3)*.09)*scale)
        start=(x,y,z+h-.015)
        m.rod(start,end,.007*scale,'Leaf',6)
        m.leaf((x,y,z+h+.07*scale),end,.08*scale,'Sage' if i%3 else 'Leaf')

m=Mesh('Plant_Floor','Decor'); pot(m,0,0,0,1.4,True); m.finish(.002)
m=Mesh('Plant_Table','Decor','Pivot sits on tabletop.'); pot(m,0,0,0,.45); m.finish(.001)
m=Mesh('Planter_Divider_1_6m','Decor')
m.box((0,0,.27),(1.6,.44,.54),'Teal'); m.box((0,0,.54),(1.65,.48,.05),'Oak')
for x in (-.55,0,.55): pot(m,x,0,.4,.7,True)
m.finish()
m=Mesh('WallArt_Abstract','Decor','Rear attachment plane Y=0; projects towards -Y; pivot bottom center.')
m.box((0,-.02,.5),(.72,.04,1),'Walnut'); m.box((0,-.047,.5),(.64,.018,.92),'Cream')
for x,z,w,h,col in [(-.12,.36,.22,.42,'Terracotta'),(.14,.63,.22,.5,'Teal'),(.12,.22,.26,.12,'Oak')]: m.box((x,-.061,z),(w,.01,h),col)
m.finish(.002)
m=Mesh('MenuBoard_Wall','Decor','Blank decorative menu; attachment at rear bottom.')
m.box((0,-.025,.4),(1.1,.05,.8),'Oak'); m.box((0,-.056,.4),(1,.02,.7),'Charcoal')
for z in (.2,.3,.4,.5,.6):
    m.box((-.13,-.069,z),(.5,.005,.014),'Chalk'); m.box((.34,-.069,z),(.1,.005,.014),'Chalk')
m.finish(.003)
m=Mesh('MenuBoard_AFrame','Decor')
for y in (-.24,.24):
    for x in (-.3,.3): m.rod((x,y,0),(x,0,1.12),.023,'Oak')
m.box((0,-.09,.67),(.64,.065,.78),'Oak'); m.box((0,-.126,.67),(.55,.018,.67),'Charcoal')
for z in (.48,.6,.72,.84): m.box((0,-.139,z),(.39,.005,.015),'Chalk')
m.finish(.003)
m=Mesh('WallClock','Decor','Pivot lower rear; face points -Y.')
m.rod((0,0,.25),(0,-.055,.25),.25,'Brass',48); m.rod((0,-.056,.25),(0,-.062,.25),.225,'Cream',48)
for i in range(12):
    a=i*math.pi/6; m.box((math.sin(a)*.19,-.068,.25+math.cos(a)*.19),(.018,.008,.018),'Charcoal')
m.rod((0,-.08,.25),(.12,-.08,.32),.01,'Charcoal',8); m.rod((0,-.08,.25),(-.04,-.08,.4),.008,'Charcoal',8); m.finish(.001)
m=Mesh('CoatStand','Decor'); m.cylinder(0,0,0,.045,.24,'Charcoal'); m.cylinder(0,0,.045,1.65,.025,'Walnut')
for i in range(6):
    a=i*math.pi/3; m.rod((0,0,1.45),(.23*math.cos(a),.23*math.sin(a),1.7),.012,'Brass')
m.finish(.003)
m=Mesh('WasteBin','Decor'); m.cylinder(0,0,0,.62,.22,'Teal',24); m.cylinder(0,0,.62,.04,.23,'Charcoal',24)
m.box((0,-.195,.53),(.22,.09,.09),'Charcoal'); m.finish(.005)
m=Mesh('Table_Caddy','Decor','Tabletop prop: napkin holder and salt/pepper shakers.')
m.box((0,0,.025),(.3,.2,.05),'Walnut'); m.box((-.055,0,.11),(.1,.12,.17),'Steel'); m.box((-.055,0,.18),(.075,.09,.06),'Ivory')
for y in (-.055,.055): m.cylinder(.075,y,.05,.1,.029,'Ivory' if y<0 else 'Charcoal',12); m.cylinder(.075,y,.15,.02,.031,'Steel',12)
m.finish(.002)
m=Mesh('Vase_Ceramic','Decor','Tabletop prop.')
m.cylinder(0,0,0,.16,.07,'Teal',24,r2=.095); m.cylinder(0,0,.16,.1,.095,'Teal',24,r2=.035)
for i in range(5):
    a=i*2.4; end=(.1*math.cos(a),.1*math.sin(a),.5-(i%2)*.07); m.rod((0,0,.23),end,.004,'Leaf',6); m.leaf((0,0,.32),end,.04,'Sage')
m.finish(.002)
m=Mesh('Rug_Bordered_2x3m','Decor')
m.box((0,0,.008),(2,3,.016),'Terracotta'); m.box((0,0,.018),(1.8,2.8,.007),'Cream'); m.box((0,0,.023),(1.7,2.7,.004),'Teal'); m.finish(.001)

for kind in ('Pendant','Sconce','FloorLamp','CeilingStrip'):
    m=Mesh('Light_'+kind,'Lighting','Mesh only; no light component. Pendant/strip pivot at ceiling; sconce at rear lower edge.')
    if kind=='Pendant':
        m.cylinder(0,0,-.04,.04,.11,'Charcoal'); m.cylinder(0,0,-.6,.56,.009,'Charcoal',8)
        m.cylinder(0,0,-.82,.22,.28,'Teal',32,r2=.055); m.cylinder(0,0,-.825,.009,.25,'Glow',32)
    elif kind=='Sconce':
        m.box((0,-.025,.17),(.13,.05,.34),'Brass'); m.rod((0,-.05,.14),(0,-.24,.14),.017,'Brass')
        m.cylinder(0,-.24,.14,.26,.115,'Ivory',24,r2=.075); m.cylinder(0,-.24,.13,.015,.1,'Glow',24)
    elif kind=='FloorLamp':
        m.cylinder(0,0,0,.045,.23,'Charcoal'); m.cylinder(0,0,.045,1.4,.018,'Brass',16)
        m.cylinder(0,0,1.25,.36,.27,'Ivory',32,r2=.16); m.cylinder(0,0,1.24,.01,.24,'Glow',32)
    else:
        m.box((0,0,-.04),(1.2,.2,.08),'Steel'); m.box((0,0,-.085),(1.1,.15,.02),'Glow')
    m.finish(.003)

m=Mesh('PrepTable_1_5m','Service','Unpowered stainless preparation furniture.')
legs(m,1.3,.5,.86,'Steel'); m.box((0,0,.87),(1.5,.7,.06),'Steel'); m.box((0,0,.22),(1.38,.58,.04),'Steel'); m.box((0,.33,.94),(1.5,.04,.16),'Steel'); m.finish()
m=Mesh('Sink_Single','Service','Open basin built from rim, inner floor, and sides; no working plumbing.')
legs(m,1,.5,.86,'Steel')
for x in (-.48,.48): m.box((x,0,.87),(.24,.7,.055),'Steel')
for y in (-.29,.29): m.box((0,y,.87),(.72,.12,.055),'Steel')
m.box((0,0,.63),(.72,.48,.035),'Steel')
for x in (-.35,.35): m.box((x,0,.75),(.035,.48,.25),'Steel')
for y in (-.225,.225): m.box((0,y,.75),(.7,.035,.25),'Steel')
m.cylinder(0,0,.65,.006,.045,'Charcoal',16)
m.rod((0,.27,.88),(0,.27,1.17),.018,'Steel'); m.rod((0,.27,1.17),(0,.04,1.17),.018,'Steel'); m.rod((0,.04,1.17),(0,.04,1.11),.018,'Steel'); m.finish(.003)
m=Mesh('StorageShelf_1_2m','Service')
for x in (-.56,.56):
    for y in (-.23,.23): m.rod((x,y,0),(x,y,1.85),.02,'Steel')
for z in (.12,.66,1.2,1.78): m.box((0,0,z),(1.2,.5,.04),'Steel')
m.finish(.004)
m=Mesh('Trolley_TwoShelf','Service')
for x in (-.36,.36):
    for y in (-.22,.22):
        m.rod((x,y-.025,.07),(x,y+.025,.07),.065,'Charcoal',16); m.rod((x,y,.12),(x,y,.9),.017,'Steel')
for z in (.22,.75): m.box((0,0,z),(.82,.53,.045),'Steel')
m.rod((-.36,-.22,.92),(-.36,.22,.92),.023,'Teal'); m.finish(.003)

# Save the authoring file with an organized contact-sheet layout. FBXs above remain at their local origins.
for i,obj in enumerate(assets): obj.location=((i%8)*3.6,(i//8)*4.4,0)
stage=bpy.data.collections.new('Preview only'); scene.collection.children.link(stage)
def stage_obj(name,data):
    obj=bpy.data.objects.new(name,data); stage.objects.link(obj); return obj
camera=stage_obj('Kit Camera',bpy.data.cameras.new('Kit Camera')); scene.camera=camera
camera.data.type='ORTHO'; camera.data.ortho_scale=37
target=Vector((12.6,15.4,0)); camera.location=target+Vector((26,-38,42)); camera.rotation_euler=(target-camera.location).to_track_quat('-Z','Y').to_euler()
for name,pos,power,size in [('Key',(6,-4,22),4500,18),('Fill',(24,20,18),3500,15)]:
    light=stage_obj(name,bpy.data.lights.new(name,'AREA')); light.location=pos; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; light.rotation_euler=(target-light.location).to_track_quat('-Z','Y').to_euler()
scene.world=bpy.data.worlds.new('Restaurant Studio'); scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.32,.32,.32,1); scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
scene.render.engine='CYCLES'; scene.cycles.samples=32
scene.render.resolution_x=1800; scene.render.resolution_y=1800; scene.render.resolution_percentage=100
scene.view_settings.view_transform='AgX'
scene.render.image_settings.file_format='PNG'
scene.render.filepath=str(ROOT/'Restaurant_Kit_Preview.png')
(ROOT/'manifest.json').write_text(json.dumps({'units':'metres','front':'Blender -Y / Unity +Z','assets':manifest,'palette':palette},indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'Restaurant_Kit.blend'))
result={'assets':len(assets),'triangles':sum(x['triangles'] for x in manifest),'blend':str(ROOT/'Restaurant_Kit.blend')}
