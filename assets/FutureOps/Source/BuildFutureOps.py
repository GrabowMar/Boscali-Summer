"""Blender 5.2: blender -b --python BuildFutureOps.py -- <pack folder> [model names].
Metres; forward +Y, up +Z. No game geometry, external textures or dependencies.
LOD0/1/2 and independent static collision meshes are exported with each FBX.
"""
import bpy, bmesh, math, sys, json
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))

OUT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
for d in ('Models', 'Previews'):
    (OUT / d).mkdir(parents=True, exist_ok=True)
COLORS = {
    'Concrete': (.34, .36, .33), 'Olive': (.19, .235, .14),
    'Steel': (.27, .31, .32), 'Dark': (.055, .075, .075),
    'Canvas': (.40, .38, .25), 'Earth': (.30, .22, .13),
    'Safety': (.78, .55, .12), 'Glass': (.055, .16, .20),
    'Red': (.40, .13, .085), 'White': (.68, .70, .63),
}
MATS, PARTS, COLLIDERS = {}, [], []

def mesh(name, verts, faces, mat):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.validate()
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    ob = bpy.data.objects.new(name, me); bpy.context.collection.objects.link(ob)
    if mat: ob.data.materials.append(MATS[mat])
    PARTS.append(ob)
    return ob

def box(name, pos, size, mat='Olive', bevel=0, collision=False):
    verts=[(x*size[0]/2,y*size[1]/2,z*size[2]/2) for x,y,z in
           ((-1,-1,-1),(-1,-1,1),(-1,1,-1),(-1,1,1),(1,-1,-1),(1,-1,1),(1,1,-1),(1,1,1))]
    ob=mesh(name,verts,[(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)],None)
    PARTS.remove(ob); ob.location=pos
    if collision:
        COLLIDERS.append(ob); ob.name = 'COL_' + name; ob.hide_render = True
    else:
        ob.data.materials.append(MATS[mat]); PARTS.append(ob)
        if bevel:
            bpy.context.view_layer.objects.active=ob
            mod = ob.modifiers.new('Edge relief', 'BEVEL'); mod.width = bevel; mod.segments = 1
            bpy.ops.object.modifier_apply(modifier=mod.name)
    return ob

def cylinder(name, pos, radius, height, mat='Steel', n=12, direction=None):
    verts=[(radius*math.cos(i*math.tau/n),radius*math.sin(i*math.tau/n),z) for z in (-height/2,height/2) for i in range(n)]
    faces=[tuple(reversed(range(n))),tuple(range(n,n*2))]+[(i,(i+1)%n,(i+1)%n+n,i+n) for i in range(n)]
    ob=mesh(name,verts,faces,mat); ob.location=pos
    if direction: ob.rotation_euler = Vector(direction).to_track_quat('Z','Y').to_euler()
    return ob

def beam(name, a, b, width=.08, mat='Steel'):
    a, b = Vector(a), Vector(b)
    ob = box(name, (a+b)/2, (width, width, (b-a).length), mat)
    ob.rotation_euler = (b-a).to_track_quat('Z','Y').to_euler()
    return ob

def slab(w, l):
    box('Foundation', (0,0,.18), (w,l,.36), 'Concrete', .12)
    box('Foundation', (0,0,.18), (w,l,.36), collision=True)

def shelter(x,y,w=6,l=12,h=3, mat='Olive'):
    box('Module', (x,y,h/2+.4),(w,l,h),mat,.08)
    box('Module', (x,y,h/2+.4),(w,l,h),collision=True)
    box('Roof coping', (x,y,h+.47),(w+.25,l+.25,.14),'Steel')
    box('Door', (x,y-l/2-.025,1.4), (1.15,.08,2),'Dark')
    box('Door frame', (x,y-l/2-.055,2.45),(1.3,.1,.12),'Steel')
    box('Handle',(x+.4,y-l/2-.09,1.5),(.06,.08,.22),'Safety')
    for sy in (-1,1):
        for yy in range(int(l)):
            box('Wall seam',(x+sy*(w/2+.015),y-l/2+yy+.5,h/2+.4),(.04,.035,h-.15),'Steel')
        for yy in (-l*.27,l*.27):
            box('Window frame',(x+sy*(w/2+.04),y+yy,2.25),(.09,1.5,.8),'Steel')
            box('Window',(x+sy*(w/2+.10),y+yy,2.25),(.03,1.35,.65),'Glass')
    for yy in (-l*.3,l*.3):
        box('Roof HVAC',(x,y+yy,h+.85),(1.5,1.2,.65),'White',.08)
        for a in range(6):
            box('HVAC louver',(x-.6+a*.24,y+yy-.62,h+.86),(.07,.025,.4),'Dark')

def container(x,y,z=0, mat='Olive', l=6.1):
    w,h=2.44,2.6
    box('Container',(x,y,z+h/2),(w,l,h),mat,.035)
    box('Container',(x,y,z+h/2),(w,l,h),collision=True)
    for sy in (-1,1):
        for i in range(int(l/.25)):
            box('Corrugation',(x+sy*1.23,y-l/2+i*.25+.12,z+h/2),(.055,.08,2.4),mat)
    for sy in (-1,1):
        for xx in (-.55,.55):
            box('Container door',(x+xx,y+sy*(l/2+.01),z+h/2),(1.06,.045,2.4),mat)
            cylinder('Lock bar',(x+xx,y+sy*(l/2+.06),z+h/2),.025,2.3,'Steel',8)
    for xx in (-1.15,1.15):
        for yy in (-l/2+.1,l/2-.1):
            for zz in (.12,h-.12): box('Corner casting',(x+xx,y+yy,z+zz),(.18,.22,.2),'Steel')

def tent(x,y,w=5,l=8):
    # Pitched canvas with real thickness, eaves, entrance flaps and tension ribs.
    verts=[(x+a*w/2,y+b*l/2,z) for b in (-1,1) for a,z in ((-1,.25),(-1,2.1),(0,3.5),(1,2.1),(1,.25))]
    faces=[(i,i+5,i+6,i+1) for i in range(4)]+[(4,3,2,1,0),(5,6,7,8,9)]
    mesh('Canvas tent',verts,faces,'Canvas')
    box('Tent volume',(x,y,1.1),(w,l,2.2),collision=True)
    for yy in (y-l/2,y,y+l/2):
        for sign in (-1,1):
            beam('Tent rib',(x,yy,3.52),(x+sign*w/2,yy,2.1),.055)
            beam('Tent rib',(x+sign*w/2,yy,2.1),(x+sign*w/2,yy,.22),.055)
            beam('Guy rope',(x+sign*w/2,yy,2.1),(x+sign*(w/2+1),yy,.12),.018,'Canvas')
    box('Tent entry',(x,y-l/2-.03,1.12),(1.4,.035,1.8),'Dark')
    for sign in (-1,1):
        flap=box('Rolled flap',(x+sign*.82,y-l/2-.08,1.1),(.22,.16,1.8),'Canvas'); flap.rotation_euler.y=sign*.12

def mast(x,y,h=12):
    cylinder('Mast',(x,y,h/2),.10,h)
    for z in (h*.35,h*.7):
        for a in range(3):
            t=a*math.tau/3
            beam('Mast stay',(x,y,z),(x+math.cos(t)*2,y+math.sin(t)*2,.2),.025)
    for z in (h-.4,h-1.4):
        beam('Antenna crossarm',(x-1,y,z),(x+1,y,z))
        for xx in (-1,1): cylinder('Antenna',(x+xx,y,z+.5),.035,1)
    cylinder('Beacon',(x,y,h+.12),.16,.22,'Red')

def fence(w,l):
    for sy in (-1,1):
        for x in range(-int(w/2),int(w/2)+1,4):
            cylinder('Fence post',(x,sy*l/2,1.1),.055,2.2)
        for z in (.45,1.2,2): beam('Fence wire',(-w/2,sy*l/2,z),(w/2,sy*l/2,z),.02)
    for sx in (-1,1):
        for y in range(-int(l/2),int(l/2)+1,4): cylinder('Fence post',(sx*w/2,y,1.1),.055,2.2)
        for z in (.45,1.2,2): beam('Fence wire',(sx*w/2,-l/2,z),(sx*w/2,l/2,z),.02)

def bags(x,y,length=6,angle=0,rows=3):
    for row in range(rows):
        for i in range(int(length/.68)):
            xx=(i+.5)*.68-length/2+(.20 if row%2 else 0)
            bpy.ops.mesh.primitive_uv_sphere_add(segments=8, ring_count=4, radius=1,
                location=(x+xx*math.cos(angle),y+xx*math.sin(angle),.22+row*.35))
            ob=bpy.context.object; ob.name='Sandbag'; ob.scale=(.37,.23,.21); ob.rotation_euler.z=angle
            bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
            ob.data.materials.append(MATS['Canvas']); PARTS.append(ob)
    wall=box('Sandbag wall',(x,y,rows*.175), (length,.5,rows*.35),collision=True)
    wall.rotation_euler.z=angle

def trench_segment(length=8):
    # Above-ground embankment kit: floor at grade, 1.8 m walls; no terrain cutting assumed.
    for sign in (-1,1):
        pts=[(sign*x,y,z) for y in (-length/2,length/2) for x,z in ((1.1,0),(1.1,1.55),(1.7,1.8),(3.2,0))]
        mesh('Earth bank',pts,[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)],'Earth')
        box('Bank collision',(sign*2.1,0,.75),(2,length,1.5),collision=True)
        for j in range(int(length/.4)):
            box('Revetment plank',(sign*1.08,-length/2+j*.4+.2,.76),(.12,.34,1.52),'Canvas')
        for yy in (-length/2+.2,0,length/2-.2):
            box('Retaining post',(sign*.98,yy,.8),(.16,.18,1.6),'Steel')
        bags(sign*1.35,0,length,math.pi/2,2)
        # Lift parapet bags above wall.
        for ob in PARTS[-int(length/.68)*2:]: ob.location.z+=1.5
        COLLIDERS[-1].location.z+=1.5
    for yy in range(int(length/.35)):
        box('Duckboard',(0,-length/2+yy*.35+.17,.04),(1.85,.28,.08),'Canvas')

def launch_site():
    slab(38,48)
    cylinder('Launch plinth',(0,7,.65),8,.6,'Concrete',32)
    cylinder('Steel launch ring',(0,7,1.02),3,.15,'Steel',24)
    for a in range(12):
        t=a*math.tau/12
        ob=box('Pad radial stripe',(math.cos(t)*6,7+math.sin(t)*6,1.0),(1.1,.18,.015),'Safety'); ob.rotation_euler.z=t
    box('Flame trench',(0,0,.55),(3,10,.16),'Dark')
    for sx in (-1,1): box('Flame trench lip',(sx*1.65,0,.9),(.3,10,.9),'Concrete')
    for x in (7,10):
        for y in (5,8): beam('Tower chord',(x,y,.4),(x,y,22),.22)
    for z in range(1,22,3):
        for y in (5,8):
            beam('Tower brace',(7,y,z),(10,y,z+3),.12)
            beam('Tower brace',(10,y,z),(7,y,z+3),.12)
        for x in (7,10): beam('Tower brace',(x,5,z),(x,8,z+3),.12)
        box('Service deck',(8.5,6.5,z),(3.6,3.6,.14),'Steel')
    beam('Umbilical arm',(8,6.5,18),(1,6.5,18),.28)
    box('Gantry collision',(8.5,6.5,11),(3,3,22),collision=True)
    shelter(-10,-12,7,10)
    for x in (-11,-6):
        cylinder('Propellant tank',(x,9,3.5),1.75,6,'White',20)
        box('Propellant tank',(x,9,3.5),(3.5,3.5,6),collision=True)
        cylinder('Tank rim',(x,9,6.55),1.85,.12)
        beam('Transfer pipe',(x,9,.9),(x,-1,.9),.15)
    fence(37,47); mast(13,-15,10)

def field_camp():
    for x in (-7,7):
        for y in (-7,5): tent(x,y)
    shelter(0,16,6,8)
    for x in (-7,7): container(x,17)
    for x in (-2,2): box('Supply crate',(x,-13,.65),(1.5,1.3,1.3),'Olive',.06)
    bags(0,-17,8,0,3); mast(0,10,9)

def fob():
    slab(28,30); shelter(-6,0,7,12); shelter(6,0,7,12)
    container(-6,11); container(6,11,0,'White')
    for sy in (-1,1):
        for x in range(-12,13,2):
            if sy == -1 and abs(x) <= 2: continue  # Six-metre vehicle entrance.
            box('Gabion',(x,sy*14,1),(1.95,1.25,2),'Earth',.08)
            box('Gabion',(x,sy*14,1),(1.95,1.25,2),collision=True)
            for z in (.1,.7,1.3,1.9): box('Gabion wire',(x,sy*14-.64,z),(1.95,.025,.025),'Steel')
    for sx in (-1,1):
        for y in range(-12,13,2):
            box('Gabion',(sx*13,y,1),(1.25,1.95,2),'Earth',.08)
            box('Gabion',(sx*13,y,1),(1.25,1.95,2),collision=True)
    mast(0,6,15); box('Command sign',(0,-6,2),(3,.1,.55),'Safety')
    for x in (-3,3): box('Entry post',(x,-14,1),(.25,.25,2),'Steel')
    box('Raised gate arm',(-3,-14,3.4),(.14,.14,4.8),'White')

def data_center():
    slab(26,26)
    shelter(0,2,16,16,6,'Concrete')
    for x in (-5,0,5):
        box('Cooling bank',(x,2,7.2),(3,9,1.6),'Steel',.1)
        for y in (-1,2,5):
            cylinder('Cooling fan',(x,y,8.03),1,.05,'Dark',16)
            for a in range(6):
                t=a*math.tau/6; beam('Fan grille',(x-math.cos(t),y-math.sin(t),8.08),(x+math.cos(t),y+math.sin(t),8.08),.028)
    container(-7,-9,0,'White'); container(7,-9,0,'Olive')
    for x in (-10,10):
        box('Cable trunk',(x,1,.7),(.6,14,.5),'Dark')
        cylinder('Backup fuel',(x,8,1.6),1.1,2.4,'White')
    mast(-10,-3,11); fence(25,25)

def cargo_ship():
    from BuildCargoShip import build_geometry
    build_geometry(sys.modules[__name__])


def trench_corner():
    # Centreline turns at origin; 2.2 m passage, outlets (0,-4) and (4,0).
    for a,b,outward in (((-1.1,-4),(-1.1,1.1),(-1,0)),
                         ((-1.1,1.1),(4,1.1),(0,1)),
                         ((1.1,-4),(1.1,-1.1),(1,0)),
                         ((1.1,-1.1),(4,-1.1),(0,-1))):
        a,b=Vector((*a,0)),Vector((*b,0)); mid=(a+b)/2
        length=(b-a).length; angle=math.atan2(b.y-a.y,b.x-a.x)
        for i in range(int(length/.4)):
            p=a+(b-a)*(i+.5)/int(length/.4)
            ob=box('Corner revetment',(p.x,p.y,.75),(.34,.12,1.5),'Canvas'); ob.rotation_euler.z=angle
        bank=mid+Vector((*outward,0))*.9
        for coll in (False,True):
            ob=box('Corner earth bank',(bank.x,bank.y,.8),(length,1.6,1.6),'Earth',collision=coll); ob.rotation_euler.z=angle
        before=len(PARTS); bags(mid.x,mid.y,length,angle,2)
        for ob in PARTS[before:]: ob.location.z+=1.5
        COLLIDERS[-1].location.z+=1.5
    for yy in range(15): box('Duckboard',(0,-3.85+yy*.3,.04),(1.85,.25,.08),'Canvas')
    for xx in range(4): box('Duckboard',(1.5+xx*.7,0,.04),(.65,1.85,.08),'Canvas')

def dugout():
    for sx in (-1,1):
        box('Dugout wall',(sx*2.2,0,1.1),(.5,6,2.2),'Concrete')
        box('Dugout wall',(sx*2.2,0,1.1),(.5,6,2.2),collision=True)
    box('Rear wall',(0,2.8,1.1),(4.8,.4,2.2),'Concrete')
    box('Rear wall',(0,2.8,1.1),(4.8,.4,2.2),collision=True)
    box('Roof',(0,0,2.3),(5.2,6.5,.5),'Concrete')
    box('Roof',(0,0,2.3),(5.2,6.5,.5),collision=True)
    box('Earth cover',(0,0,2.7),(5.5,6.8,.5),'Earth',.2)
    for x in (-1.65,1.65): bags(x,-3,1.8,0,4)
    for y in (-1.4,1.4): box('Interior bench',(-1.7,y,.45),(.6,1.9,.3),'Canvas')

def radar():
    slab(16,18); shelter(0,-3,6,8); cylinder('Radar pedestal',(0,3,4),.8,7)
    verts=[(0,3.0,8)]; faces=[]
    for ring in range(1,5):
        r=ring*.85
        for j in range(24):
            t=j*math.tau/24; verts.append((r*math.cos(t),3+r*r*.13,8+r*math.sin(t)))
    for j in range(24): faces.append((0,1+j,1+(j+1)%24))
    for ring in range(3):
        a=1+ring*24; b=a+24
        for j in range(24): faces.append((a+j,b+j,b+(j+1)%24,a+(j+1)%24))
    ob=mesh('Parabolic reflector',verts,faces,'White')
    mod=ob.modifiers.new('Reflector thickness','SOLIDIFY'); mod.thickness=.06
    bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
    for angle in (0,math.tau/3,2*math.tau/3): beam('Feed support',(math.cos(angle)*3,4.3,8+math.sin(angle)*3),(0,.4,8),.09)
    cylinder('Feed horn',(0,.4,8),.25,.6,'Dark',12,(0,1,0)); mast(6,-3,10)

def fuel_depot():
    slab(28,24)
    for x in (-7,7):
        cylinder('Storage tank',(x,4,3.4),4,6,'White',32)
        cylinder('Tank roof',(x,4,6.45),4.05,.15,'Steel',32)
        for z in (1.4,3.5,5.6): cylinder('Tank hoop',(x,4,z),4.035,.08,'Steel',32)
        for sx in (-1,1): beam('Tank ladder',(x+sx*.28,-.1,.5),(x+sx*.28,-.1,6.6),.045)
        for z in range(1,7): beam('Ladder rung',(x-.28,-.13,z),(x+.28,-.13,z),.04)
        box('Tank collision',(x,4,3.4),(8,8,6),collision=True)
        beam('Fuel manifold',(x,0,1),(x,-7,1),.18)
    for y in (-1,10): box('Containment bund',(0,y,.65),(25,.5,.6),'Concrete')
    for x in (-12.5,12.5): box('Containment bund',(x,4.5,.65),(.5,11,.6),'Concrete')
    shelter(0,-9,5,4); fence(27,23)

def checkpoint():
    shelter(-5,0,3,4); bags(5,0,6,0,4)
    for x in (-1.5,1.5):
        box('Gate pillar',(x,0,.9),(.3,.3,1.8),'Steel')
    box('Gate arm',(0,0,1.6),(3,.15,.15),'White')
    for x in (-1,0,1): box('Gate stripe',(x,-.085,1.6),(.35,.02,.16),'Red')
    for x in (-7,6):
        for y in (-3,3):
            for d in ((1,0,1),(1,0,-1),(0,1,0)):
                v=Vector(d).normalized()*1.2; beam('Steel obstacle',Vector((x,y,.9))-v,Vector((x,y,.9))+v,.16)
    mast(-5,0,7)

def helipad():
    slab(24,24)
    cylinder('Landing surface',(0,0,.39),11,.06,'Dark',32)
    for x in (-1.8,1.8): box('H marking',(x,0,.425),(.55,6,.015),'White')
    box('H marking',(0,0,.425),(3.6,.55,.015),'White')
    for a in range(24):
        t=a*math.tau/24
        ob=box('Pad perimeter',(10*math.cos(t),10*math.sin(t),.425),(1.6,.22,.015),'Safety'); ob.rotation_euler.z=t+math.pi/2
        if a%3==0: cylinder('Pad lamp',(11*math.cos(t),11*math.sin(t),.48),.12,.13,'Glass')

BUILDERS={'LaunchSite':launch_site,'FieldCamp':field_camp,'FOB':fob,'DataCenter':data_center,
          'CargoShip':cargo_ship,'TrenchStraight':trench_segment,'TrenchCorner':trench_corner,
          'TrenchDugout':dugout,'SandbagWall':lambda:bags(0,0,8,0,4),
          'RadarPost':radar,'FuelDepot':fuel_depot,'Checkpoint':checkpoint,'Helipad':helipad}

def join(objects,name):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in objects: ob.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]; bpy.ops.object.join()
    ob=bpy.context.object; ob.name=ob.data.name=name
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    return ob

def render(ob,name):
    sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=16
    prefs=bpy.context.preferences.addons['cycles'].preferences
    try:
        prefs.compute_device_type='OPTIX'; prefs.get_devices()
        gpu=False
        for device in prefs.devices:
            device.use=device.type=='OPTIX'; gpu |= device.use
        if gpu: sc.cycles.device='GPU'
    except (TypeError,RuntimeError):
        pass  # CPU rendering is sufficient on machines without OptiX.
    sc.render.resolution_x=1200; sc.render.resolution_y=900; sc.render.resolution_percentage=100
    sc.world=bpy.data.worlds.new('Studio'); sc.world.use_nodes=True
    sc.world.node_tree.nodes['Background'].inputs[0].default_value=(.22,.27,.32,1)
    sc.world.node_tree.nodes['Background'].inputs[1].default_value=.6
    pts=[ob.matrix_world@Vector(v) for v in ob.bound_box]; lo=Vector([min(p[i] for p in pts) for i in range(3)]); hi=Vector([max(p[i] for p in pts) for i in range(3)])
    ctr=(hi+lo)/2; extent=max(hi-lo)
    bpy.ops.object.light_add(type='AREA',location=ctr+Vector((-extent*.6,-extent*.8,extent*1.4)))
    light=bpy.context.object; light.data.energy=extent**2*110; light.data.shape='DISK'; light.data.size=extent*.8
    light.rotation_euler=(ctr-light.location).to_track_quat('-Z','Y').to_euler()
    bpy.ops.object.camera_add(); cam=bpy.context.object
    cam.location=ctr+Vector((1.15,-1.5,1.15)).normalized()*extent*2
    cam.rotation_euler=(ctr-cam.location).to_track_quat('-Z','Y').to_euler(); cam.data.type='ORTHO'
    sc.camera=cam
    inv=cam.rotation_euler.to_matrix().transposed(); q=[inv@(p-ctr) for p in pts]
    cam.data.ortho_scale=max(max(abs(p.x) for p in q)*2.25,max(abs(p.y) for p in q)*3)
    # Neutral ground below the model; ship remains visible down to its keel.
    ground=box('Preview ground',(0,0,lo.z-.13),(extent*200,extent*200,.1),'Concrete')
    ground.data.materials.clear(); m=bpy.data.materials.new('Backdrop'); m.diffuse_color=(.12,.15,.18,1); m.use_nodes=True
    m.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.12,.15,.18,1); ground.data.materials.append(m)
    sc.render.filepath=str(OUT/'Previews'/f'{name}.png'); bpy.ops.render.render(write_still=True)
    for o in (ground,light,cam): bpy.data.objects.remove(o,do_unlink=True)

def build(name):
    global MATS,PARTS,COLLIDERS
    bpy.ops.wm.read_factory_settings(use_empty=True); PARTS=[]; COLLIDERS=[]; MATS={}
    bpy.context.preferences.filepaths.save_version=0
    for key,rgb in COLORS.items():
        m=bpy.data.materials.new(key); m.diffuse_color=(*rgb,1); m.use_nodes=True
        bs=m.node_tree.nodes['Principled BSDF']; bs.inputs['Base Color'].default_value=(*rgb,1)
        bs.inputs['Roughness'].default_value=.28 if key=='Glass' else .78
        bs.inputs['Metallic'].default_value=.45 if key=='Steel' else 0
        # Fine surface breakup in the editable source; FBX uses the authored base palette.
        noise=m.node_tree.nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=18
        bump=m.node_tree.nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.15; bump.inputs['Distance'].default_value=.035
        m.node_tree.links.new(noise.outputs['Fac'],bump.inputs['Height']); m.node_tree.links.new(bump.outputs['Normal'],bs.inputs['Normal'])
        MATS[key]=m
    BUILDERS[name]()
    lod0=join(PARTS,'LOD0'); lod0['asset_key']='BS_Future_'+name
    bpy.context.view_layer.objects.active=lod0; lod0.select_set(True)
    if name != 'CargoShip' and not lod0.data.uv_layers:
        bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.uv.smart_project(island_margin=.02); bpy.ops.object.mode_set(mode='OBJECT')
    exported=[lod0]; counts=[]
    for level,ratio in ((1,.4),(2,.12)):
        ob=lod0.copy(); ob.data=lod0.data.copy(); ob.name=ob.data.name=f'LOD{level}'; bpy.context.collection.objects.link(ob)
        bpy.context.view_layer.objects.active=ob
        mod=ob.modifiers.new('LOD reduction','DECIMATE'); mod.ratio=ratio
        bpy.ops.object.modifier_apply(modifier=mod.name)
        if name != 'CargoShip':
            # Decimation can collapse narrow bevels. Keep exported lower LODs clean too.
            from SurfaceFinish import clean
            clean(ob.data)
        ob.hide_render=True; exported.append(ob)
    if COLLIDERS:
        # Merge collision boxes into one non-convex static mesh, preserving passage openings.
        col=join(COLLIDERS,'COL_Static'); col.hide_render=True; exported.append(col)
    for ob in exported:
        ob.data.calc_loop_triangles(); counts.append((ob.name,len(ob.data.loop_triangles)))
        assert all(v.co.length<2000 and all(math.isfinite(a) for a in v.co) for v in ob.data.vertices)
    assert counts[0][1]>0 and counts[1][1]<counts[0][1] and counts[2][1]<counts[1][1],counts
    bpy.ops.object.select_all(action='DESELECT')
    for ob in exported: ob.select_set(True)
    bpy.context.view_layer.objects.active=lod0
    bpy.ops.export_scene.fbx(filepath=str(OUT/'Models'/f'{name}.fbx'),use_selection=True,
        axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_NONE',bake_space_transform=False,
        object_types={'MESH'},mesh_smooth_type='EDGE',use_tspace=False,use_custom_props=True,
        add_leaf_bones=False,bake_anim=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'Models'/f'{name}.blend'))
    render(lod0,name)
    lo=[min(v.co[i] for v in lod0.data.vertices) for i in range(3)]; hi=[max(v.co[i] for v in lod0.data.vertices) for i in range(3)]
    record={'key':'BS_Future_'+name,'model':name,'bounds_blender':[lo,hi],'lod_triangles':dict(counts),'materials':[m.name for m in lod0.data.materials]}
    print('ASSET_OK',json.dumps(record),flush=True)
    return record

if __name__=='__main__':
    # Ground facilities share the original mission pack's physical surface finish.
    # The native cargo ship retains its separately authored marine texture/UV pipeline.
    sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'MissionAssets/Source'))
    import SurfaceFinish as finish
    finish.generate(OUT,COLORS)
    def dressed(fn):
        def run(): fn(); finish.finish(sys.modules[__name__])
        return run
    BUILDERS={name:(fn if name=='CargoShip' else dressed(fn)) for name,fn in BUILDERS.items()}
    args=sys.argv[sys.argv.index('--')+2:]
    names=args[0].split(',') if args else list(BUILDERS)
    records=[build(name) for name in names]
    if args and (OUT/'catalog.json').exists():
        previous=json.loads((OUT/'catalog.json').read_text())['assets']
        by_name={e['model']:e for e in previous}
        by_name.update({e['model']:e for e in records})
        records=[by_name[n] for n in BUILDERS if n in by_name]
    (OUT/'catalog.json').write_text(json.dumps({'units':'metres','frame':'Blender +Y forward, +Z up','assets':records},indent=2))
