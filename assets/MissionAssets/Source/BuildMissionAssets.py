"""Blender 5.2: original ground mission scenery and four articulated satellite designs.
Reuses the verified FutureOps export/LOD/render path. Metres; +Y forward / +Z up.
"""
import bpy, math, sys, json
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/'FutureOps/Source'))
import BuildFutureOps as a
sys.path.insert(0,str(Path(__file__).resolve().parent))
import SurfaceFinish as finish
a.COLORS.update({'SpaceGold':(.48,.29,.065),'SolarBlue':(.015,.06,.13),'SolarDark':(.012,.025,.055),
                 'ThermalWhite':(.63,.67,.65),'Medical':(.05,.32,.19),'Wreck':(.09,.11,.10)})
ANCHORS={}; RIGS={}

def box(name,pos,size,mat='Olive',bevel=.03,solid=False):
    ob=a.box(name,pos,size,mat,bevel)
    if solid: a.box(name,pos,size,collision=True)
    return ob

def dish(name,pos,radius=1.2,direction=(0,-1,.3),mat='White'):
    # True parabolic reflector, rim and focal feed; not a sphere with a pole.
    verts=[(0,0,0)]; faces=[]; rings=5; n=24
    for j in range(1,rings+1):
        r=radius*j/rings
        verts += [(r*math.cos(i*math.tau/n),r*math.sin(i*math.tau/n),r*r/(radius*2)) for i in range(n)]
    for i in range(n): faces.append((0,1+i,1+(i+1)%n))
    for j in range(rings-1):
        k=1+j*n; q=k+n
        faces += [(k+i,q+i,q+(i+1)%n,k+(i+1)%n) for i in range(n)]
    ob=a.mesh(name,verts,faces,mat); ob.location=pos; ob.rotation_euler=Vector(direction).to_track_quat('Z','Y').to_euler()
    for p in ob.data.polygons: p.use_smooth=True
    mod=ob.modifiers.new('Reflector shell','SOLIDIFY'); mod.thickness=.04
    bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
    rot=ob.rotation_euler.to_matrix(); ctr=Vector(pos); focus=ctr+rot@Vector((0,0,radius*.65))
    for t in (0,math.tau/3,math.tau*2/3):
        a.beam('Feed strut',ctr+rot@Vector((radius*.9*math.cos(t),radius*.9*math.sin(t),radius*.4)),focus,.035,'Steel')
    a.cylinder('Feed horn',focus,.12,.25,'Dark',12,direction)
    return ob

def relay():
    a.slab(14,16); a.shelter(-2,-3,5,7); a.mast(3,3,16)
    for z in (8,12): dish('Microwave relay',(3,3,z),.9,(.4,-1,.1))
    box('Battery cabinets',(-5,4,1.1),(2,3.5,1.8),'Steel',.08,True)
    for yy in (3,4,5): box('Battery louver',(-6.03,yy,1.1),(.025,.5,.6),'Dark',0)
    a.fence(13,15)

def jammer():
    box('Truck chassis',(0,0,1.0),(2.45,7.4,.5),'Steel',.08,True)
    # Angled cab with separate bonnet, glazing, doors and a visible chassis.
    profile=[(-3.6,1.25),(-3.6,2.1),(-3.05,2.85),(-1.35,2.85),(-1.35,1.25)]
    verts=[(x,y,z) for x in (-1.2,1.2) for y,z in profile]
    cab=a.mesh('Armoured cab',verts,[tuple(reversed(range(5))),tuple(range(5,10))]+[(i,(i+1)%5,(i+1)%5+5,i+5) for i in range(5)],'Olive')
    mod=cab.modifiers.new('Cab edges','BEVEL'); mod.width=.055; mod.segments=2
    bpy.context.view_layer.objects.active=cab; bpy.ops.object.modifier_apply(modifier=mod.name)
    a.box('Cab collision',(0,-2.5,2),(2.4,2.25,1.8),collision=True)
    wind=box('Cab windscreen',(0,-3.32,2.48),(2.05,.04,.53),'Glass',.025); wind.rotation_euler.x=-.63
    a.beam('Windscreen divider',(0,-3.52,2.28),(0,-3.12,2.79),.045,'Olive')
    box('Bonnet',(0,-3.62,1.55),(2.3,.75,.46),'Olive',.08)
    box('Radiator grille',(0,-4.005,1.52),(1.40,.025,.25),'Dark',.01)
    for x in (-.8,.8):
        box('Headlamp',(x,-4.02,1.50),(.29,.045,.18),'White',.025)
        a.beam('Windshield wiper',(x,-3.53,2.29),(x-.23,-3.33,2.53),.025,'Dark')
    for side in (-1,1):
        box('Cab door',(side*1.215,-2.10,1.95),(.045,1.2,1.45),'Olive',.04)
        box('Side glazing',(side*1.247,-2.15,2.48),(.025,.96,.48),'Glass',.025)
        box('Door grip',(side*1.267,-1.70,1.95),(.06,.19,.035),'Steel',.008)
        for z in (1.0,1.25): box('Cab access step',(side*1.27,-1.8,z),(.35,.8,.09),'Steel',.02)
        a.beam('Mirror bracket',(side*1.18,-2.92,2.55),(side*1.53,-3.03,2.55),.035)
        box('Mirror',(side*1.54,-3.03,2.52),(.08,.15,.33),'Dark',.025)
        box('Chassis rail',(side*.83,0,.80),(.16,7.2,.24),'Steel',.02)
    for y in (-2.6,.3,2.6): a.cylinder('Axle',(0,y,.75),.12,2.6,'Steel',12,(1,0,0))
    box('Bumper',(0,-3.7,.95),(2.7,.22,.35),'Steel')
    box('Equipment shelter',(0,1.3,2.15),(2.45,4.6,2.5),'Olive',.10,True)
    for sx in (-1,1):
        for yy in (-2.6,.3,2.6):
            a.cylinder('All-terrain tire',(sx*1.3,yy,.75),.65,.35,'Dark',20,(1,0,0))
            a.cylinder('Wheel hub',(sx*1.5,yy,.75),.30,.08,'Steel',12,(1,0,0))
            for i in range(16):
                t=i*math.tau/16
                tread=box('Tire tread',(sx*1.31,yy+math.cos(t)*.63,.75+math.sin(t)*.63),(.38,.18,.075),'Dark',.01)
                tread.rotation_euler.x=t-math.pi/2
            for i in range(6):
                t=i*math.tau/6
                a.cylinder('Hub bolt',(sx*1.55,yy+math.cos(t)*.19,.75+math.sin(t)*.19),.027,.025,'White',8,(1,0,0))
            box('Wheel mudguard',(sx*1.27,yy,1.45),(.5,1.4,.10),'Olive',.04)
        for y in (0,1,2):
            box('Vent grille',(sx*1.24,y,2.3),(.035,.7,.7),'Dark',0)
            for z in range(6): box('Vent slat',(sx*1.27,y,2.03+z*.105),(.025,.61,.03),'Steel',.008)
        box('Rear compartment rail',(sx*1.235,1.3,3.39),(.055,4.4,.09),'Steel',.015)
        for y in (-.8,3.35):
            for z in (1.1,3.25): box('Shelter lifting eye',(sx*1.27,y,z),(.09,.16,.13),'Steel',.02)
    for y in (0,2.6):
        a.cylinder('Telescopic antenna',(0,y,5.4),.075,4.0,'Steel',12)
        for z in (6.4,7.0): a.beam('Antenna crossarm',(-1.2,y,z),(1.2,y,z),.06)
    dish('Directional emitter',(0,1.3,4.0),.8,(0,1,.2),'Steel')
    for sx in (-1,1):
        box('Outrigger',(sx*2.1,1.2,.65),(1.8,.2,.2),'Steel')
        a.cylinder('Hydraulic stabilizer',(sx*2.9,1.2,.38),.075,.6,'Steel',12)
        box('Stabilizer foot',(sx*2.9,1.2,.07),(.42,.42,.10),'Steel',.025)

def ammo():
    a.slab(25,21)
    for x in (-7,7):
        a.shelter(x,2,7,10,3,'Concrete')
        box('Blast cover',(x,2,3.8),(8,11,.7),'Earth',.25)
        box('Warning plate',(x,-3.1,2.8),(1.3,.03,.45),'Safety',0)
    for y in (-3,4): a.container(0,y,0,'Olive',6.1)
    for x in (-2,2):
        for y in (-7,-5): box('Palletized ammunition',(x,y,.7),(1.7,1.3,1.4),'Olive',.07,True)
    for x in (-11,11): a.bags(x,-5,5,math.pi/2,3)

def repair():
    a.slab(20,22)
    for x in (-6,6):
        for y in (-5,5): box('Workshop post',(x,y,2.5),(.2,.2,5),'Steel',0,True)
    box('Service canopy',(0,0,5.1),(13,12,.22),'Olive',.08)
    for x in (-5,5): a.beam('Canopy brace',(x,-5,3.5),(x,-3,5),.10)
    box('Gantry rail',(0,0,4.6),(11,.4,.35),'Steel')
    a.beam('Lift cable',(0,0,4.6),(0,0,2.3),.03,'Dark')
    box('Crane hook',(0,0,2.2),(.2,.3,.3),'Safety')
    a.container(-5,8,0,'Olive',6.1)
    for x in (3,5,7): box('Spares rack',(x,8,1),(1.5,2,2),'Steel',.02,True)
    for x in (-4,4): box('Tool chest',(x,-4,1),(1.3,.65,1),'Red',.07)
    box('Generator',(0,8,1.0),(2.2,3,1.8),'Olive',.1,True)
    for yy in (-5,0,5):
        a.beam('Roof truss chord',(-6,yy,4.85),(6,yy,4.85),.09)
        for x in range(-6,6,2):
            a.beam('Roof truss diagonal',(x,yy,4.85),(x+1,yy,5.02),.04)
            a.beam('Roof truss diagonal',(x+1,yy,5.02),(x+2,yy,4.85),.04)
    for x in (-4,4):
        for z in (.70,.9,1.10,1.3):
            box('Tool drawer seam',(x,-4.34,z),(1.12,.025,.022),'Dark')
            box('Tool drawer pull',(x,-4.36,z+.06),(.48,.03,.025),'Steel',.008)
        for xx in (-.48,.48):
            for yy in (-.20,.20): a.cylinder('Tool chest caster',(x+xx,-4+yy,.44),.09,.08,'Dark',12,(1,0,0))
    for x in (-4,4):
        box('Repair bay safety stripe',(x,0,.371),(.10,8,.012),'Safety')

def aid():
    a.tent(-4,0,5,9); a.tent(4,0,5,9); a.shelter(0,8,6,5)
    for x in (-4,4):
        box('First aid sign',(x,-4.54,2.1),(1,.04,.85),'Medical',0)
        box('White aid mark',(x,-4.57,2.1),(.55,.015,.15),'White',0)
        box('White aid mark',(x,-4.57,2.1),(.15,.015,.55),'White',0)
    for x in (-2,0,2):
        box('Stretcher',(x,-7,.6),(.7,2,.15),'Canvas')
        for sx in (-1,1): a.beam('Stretcher handle',(x+sx*.35,-8.3,.6),(x+sx*.35,-5.7,.6),.045)
    box('Medical supplies',(0,-10,.6),(2.6,1.5,1.2),'White',.07,True)

def wreck():
    # Original broken utility aircraft; no game model is extracted or copied.
    verts=[]
    for y,r,z in ((-6,.6,.9),(-3,1.1,1.1),(0,1.2,1.3),(3,.8,1.0),(5,.25,.8)):
        verts += [(math.cos(i*math.tau/12)*r,y,z+math.sin(i*math.tau/12)*r*.75) for i in range(12)]
    faces=[tuple(reversed(range(12))),tuple(range(48,60))]
    faces += [(j*12+i,j*12+(i+1)%12,(j+1)*12+(i+1)%12,(j+1)*12+i) for j in range(4) for i in range(12) if not (j==2 and i in (0,1,2))]
    a.mesh('Crushed fuselage',verts,faces,'Wreck'); a.box('Fuselage collision',(0,-.5,1.1),(2.2,11,1.8),collision=True)
    for sx in (-1,1):
        wing=a.mesh('Broken wing',[(sx*1,-2,.9),(sx*6,-3,.15),(sx*5,0,.1),(sx*1,1,.9)],[(0,1,2,3)],'Steel')
        mod=wing.modifiers.new('Wing thickness','SOLIDIFY'); mod.thickness=.16; bpy.context.view_layer.objects.active=wing; bpy.ops.object.modifier_apply(modifier=mod.name)
    for side in (-1,1):
        a.mesh('Cockpit glazing',[(side*.20,3.65,1.3),(side*.60,2.7,2.0),(side*.82,1.35,1.85),(side*.60,1.25,1.4)],[(0,1,2,3)],'Glass')
        for yy in (1.35,2.0,2.65):
            a.beam('Cockpit frame',(side*.65,yy,1.4),(side*.7,yy,1.9),.045,'Steel')
        for yy in (-1.8,-.8,.2):
            box('Cabin window',(side*1.12,yy,1.6),(.035,.53,.34),'Dark',.06)
        a.cylinder('Broken engine nacelle',(side*2.55,-1.0,.7),.45,2.3,'Wreck',20,(0,1,0))
        a.cylinder('Engine intake',(side*2.55,.17,.7),.31,.08,'Dark',20,(0,1,0))
        a.cylinder('Propeller hub',(side*2.55,.28,.7),.13,.20,'Steel',12,(0,1,0))
        for i in range(3):
            t=i*math.tau/3
            a.beam('Bent propeller blade',(side*2.55+math.cos(t)*.16,.4,.7+math.sin(t)*.16),(side*2.55+math.cos(t)*.8,.55,.7+math.sin(t)*.52),.10,'Dark')
        # Exposed wing spars, access panels and sheared surface fragments.
        a.beam('Wing spar',(side*1,-1,.8),(side*5,-1.7,.19),.09,'Steel')
        for span in (1.6,2.3,3.1,4.0):
            a.beam('Wing rib',(side*span,-2.0-span*.12,.65-span*.1),(side*span,.3-span*.15,.65-span*.1),.035,'Steel')
        a.beam('Tailplane',(side*.1,-5.0,1.0),(side*2.3,-5.35,.55),.12,'Wreck')
    for yy in (.2,1.0,1.8,2.5):
        for i in range(3):
            t=i*math.pi/6
            a.beam('Exposed fuselage rib',(math.cos(t)*1.08,yy,1.12+math.sin(t)*.75),(math.cos(t+math.pi/6)*1.08,yy,1.12+math.sin(t+math.pi/6)*.75),.045,'Steel')
    box('Torn interior floor',(0,.9,1.02),(1.7,2.7,.07),'Canvas',.02)
    a.mesh('Tail fin',[(0,-5.5,1),(0,-6.5,3),(0,-4.7,2.6),(0,-4,1)],[(0,1,2,3)],'Steel')
    for i in range(7):
        debris=box('Scattered fragment',(-4+i*1.3,5+(i%3),.12),(.9,.7,.18),'Wreck',.05); debris.rotation_euler.z=i*.7
    box('Flight recorder',(2,4,.28),(.45,.65,.4),'Safety',.08,True)
    a.cylinder('Emergency beacon',(2.6,4,.5),.12,.8,'Safety',12)

def cache():
    for x in (-2,0,2):
        for y in (-1,1):
            box('Supply crate',(x,y,.7),(1.7,1.6,1.4),'Canvas',.05,True)
            for z in (.2,1.2): box('Crate band',(x,y,z),(1.73,1.63,.06),'Dark',0)
    a.tent(0,5,4,5); box('Contraband case',(0,-3,.4),(1.5,.8,.8),'Olive',.05,True)
    a.cylinder('Portable radio',(1.7,-3,.5),.15,.8,'Dark',12); a.cylinder('Whip',(1.7,-3,1.5),.015,1.2,'Steel',8)

def coast():
    a.slab(12,14); a.shelter(-2,-3,5,6)
    box('Sensor tower',(2,3,2.8),(1.2,1.2,5.6),'Steel',.08,True)
    a.cylinder('EO turret',(2,3,5.9),.45,.45,'White',16)
    a.cylinder('Camera aperture',(2,2.6,5.9),.18,.10,'Glass',16,(0,1,0))
    dish('Surface radar',(2,3,7),1.25,(0,-1,0),'White')
    for x in (-3,-1,1): box('Power cabinet',(x,5,1.2),(1,1.2,2),'Olive',.08,True)

def drone():
    a.slab(16,17); a.container(-4,0,0,'Olive',6.1)
    dish('SATCOM terminal',(-4,0,3.4),1.1,(.3,.2,1))
    box('Launch rail',(3,0,1.2),(.6,8,.35),'Steel',.06)
    for y in (-3,3): a.beam('Rail leg',(3,y,1),(2,y,.15),.12); a.beam('Rail leg',(3,y,1),(4,y,.15),.12)
    box('Drone body',(3,-1,1.7),(.4,2,.35),'White',.15)
    box('Drone wing',(3,-1,1.7),(3.4,.45,.07),'White')
    for x in (2,4): box('Battery case',(x,6,.65),(1.2,1.4,1.3),'Olive',.08,True)

def power():
    a.slab(22,19)
    for x in (-6,0,6):
        box('Transformer',(x,1,2),(3.2,4,3.2),'Steel',.1,True)
        for xx in (-1,1):
            for y in range(-1,4): box('Cooling fin',(x+xx*1.65,y,2),(.10,.2,2.5),'Steel',0)
        for yy in (-.7,1.7):
            a.cylinder('Bushing',(x,yy,4),.20,1.2,'White',12)
            for z in (3.6,3.8,4,4.2,4.4): a.cylinder('Insulator disk',(x,yy,z),.30,.08,'White',12)
    a.shelter(0,-6,7,4); a.fence(21,18)

def demolition():
    box('Charge pallet',(0,0,.12),(2.4,2.4,.24),'Canvas',.04,True)
    for x in (-.7,.7):
        for y in (-.7,.7): box('Demolition case',(x,y,.6),(1.2,1.1,.8),'Olive',.06,True)
    box('Control unit',(0,-2,.5),(.75,.55,1),'Steel',.05)
    a.cylinder('Arming antenna',(0,-2,1.45),.012,.95,'Steel',8)
    for x in (-3,3):
        a.bags(x,1,4,math.pi/2,2)
    for x in (-1,1): box('Warning board',(x,3,1.1),(.8,.06,.55),'Safety',.02)

def lz():
    a.slab(18,18)
    for x in (-1.8,1.8): box('LZ marking',(x,0,.365),(.35,5,.01),'White',0)
    box('LZ marking',(0,0,.365),(3.6,.35,.01),'White',0)
    for x in (-7,7):
        for y in (-7,7):
            box('Landing beacon',(x,y,.5),(.3,.3,.3),'Safety',.05)
            a.cylinder('Beacon lens',(x,y,.74),.1,.15,'Glass',12)
    a.bags(-7,0,6,math.pi/2,3); a.bags(7,0,6,math.pi/2,3)
    box('Extraction supplies',(0,7,.9),(3,1.3,1.5),'Olive',.08,True)

def thermal_bus(size=(2,2.8,1.7)):
    ob=box('MLI spacecraft bus',(0,0,0),size,'SpaceGold',.10,True)
    bs=a.MATS['SpaceGold'].node_tree.nodes['Principled BSDF']; bs.inputs['Metallic'].default_value=.75; bs.inputs['Roughness'].default_value=.30
    for sx in (-1,1):
        for y in (-.8,0,.8): box('Blanket seams',(sx*(size[0]/2+.01),y,0),(.02,.045,size[2]-.1),'Steel',0)
    box('Radiator',(0,0,size[2]/2+.015),(size[0]*.8,size[1]*.8,.045),'ThermalWhite',.015)
    for y in (-size[1]/2+.3,size[1]/2-.3): box('Bus end rail',(0,y,size[2]/2+.1),(size[0]+.1,.09,.1),'Steel',0)

def thrusters(size=(2,2.8,1.7)):
    for sx in (-1,1):
        for sy in (-1,1):
            pos=(sx*size[0]/2,sy*size[1]/2,0)
            a.cylinder('RCS pod',pos,.15,.3,'Steel',12,(sx,0,0))
            a.cylinder('RCS nozzle',(sx*(size[0]/2+.19),sy*size[1]/2,0),.10,.15,'Dark',12,(sx,0,0))
    a.cylinder('Propellant tank',(0,.25,0),.45,1.25,'Steel',16,(0,1,0))

def solar(side,length=4.2,width=2.3):
    start=len(a.PARTS)
    a.beam('Solar yoke',(side*1.0,0,0),(side*1.8,0,0),.09,'Steel')
    for panel in range(2):
        x=side*(1.8+(panel+.5)*length/2)
        box('Panel frame',(x,0,0),(length/2,width,.07),'Steel',0)
        laminate=box('Solar cell laminate',(x,0,.042),(length/2-.12,width-.12,.014),'SolarBlue',0)
        laminate['solar_panel_uv']=True; laminate['panel_length']=length/2-.12; laminate['panel_width']=width-.12
        for y in (-width/2,width/2): a.beam('Panel spar',(x-length/4,y,.04),(x+length/4,y,.04),.045,'ThermalWhite')
    for y in (-width*.35,width*.35):
        a.cylinder('Solar fold hinge',(side*(1.8+length/2),y,.025),.055,.23,'Steel',12,(0,1,0))
    return a.PARTS[start:]

def export_rig(name,groups):
    # Rig pivots survive FBX. Only copies are exported, leaving the master static model untouched.
    bpy.context.view_layer.update()
    exported=[]
    root=bpy.data.objects.new(name+'_Rig',None); bpy.context.collection.objects.link(root); exported.append(root)
    for key,(objects,pivot) in groups.items():
        hinge=bpy.data.objects.new(key,None); bpy.context.collection.objects.link(hinge); hinge.location=pivot; hinge.parent=root; exported.append(hinge)
        for ob in objects:
            copy=ob.copy(); copy.data=ob.data.copy(); bpy.context.collection.objects.link(copy)
            matrix=ob.matrix_world.copy(); copy.parent=hinge
            copy.matrix_parent_inverse=Matrix.Identity(4)
            copy.matrix_basis=Matrix.Translation(-Vector(pivot))@matrix
            copy.hide_render=True; exported.append(copy)
    # One mesh renderer per moving pivot, retaining each material as a submesh.
    # The original editable parts remain separate outside the exported copy hierarchy.
    bpy.context.view_layer.update()
    for hinge in root.children:
        copies=[ob for ob in hinge.children if ob.type=='MESH']
        if not copies: continue
        bpy.ops.object.select_all(action='DESELECT')
        for ob in copies: ob.select_set(True)
        bpy.context.view_layer.objects.active=copies[0]; bpy.ops.object.join()
        bpy.context.object.name=hinge.name+'_RigMesh'
        # Bake the child's transform into its vertices, retaining the parent's hinge.
        # This also keeps Unity's renderer bounds aligned with the static LOD model.
        bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    exported=[root]+list(root.children_recursive)
    bpy.ops.object.select_all(action='DESELECT')
    for o in exported: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(a.OUT/'Models'/(name+'Rig.fbx')),use_selection=True,
        axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_NONE',bake_space_transform=False,
        object_types={'MESH','EMPTY'},mesh_smooth_type='EDGE',use_tspace=False,add_leaf_bones=False,bake_anim=False)
    RIGS[name]=name+'Rig'
    bpy.ops.wm.save_as_mainfile(filepath=str(a.OUT/'Models'/(name+'Authoring.blend')))
    for o in reversed(exported): bpy.data.objects.remove(o,do_unlink=True)

def satellite(kind):
    name={'optical':'OpticalSatellite','sar':'SarSatellite','relay':'RelaySatellite','tug':'OrbitalServiceTug'}[kind]
    size={'optical':(2,2.8,1.7),'sar':(1.8,3.4,1.5),'relay':(2.1,2.4,2.1),'tug':(2.3,3.7,2)}[kind]
    thermal_bus(size); thrusters(size)
    groups={}
    if kind=='optical':
        a.cylinder('Telescope barrel',(0,-1.1,-.8),.65,2.0,'ThermalWhite',24,(0,0,1))
        a.cylinder('Optical aperture',(0,-1.1,-1.83),.57,.05,'Glass',24)
        a.cylinder('Baffle ring',(0,-1.1,-1.87),.65,.045,'Dark',24)
        for z in (-1.40,-1.05,-.7): a.cylinder('Optical barrel stiffener',(0,-1.1,z),.68,.055,'Steel',24)
        for t in range(4):
            x=.82*math.cos(t*math.pi/2); y=-1.1+.82*math.sin(t*math.pi/2)
            a.beam('Optical support',(x,y,-.4),(x*.7,y,-1.2),.06)
    elif kind=='sar':
        start=len(a.PARTS)
        box('Deployable SAR array',(0,1.95,-1.1),(1.4,7.6,.13),'Steel',.04)
        for i in range(4):
            box('AESA radar panel',(0,-.85+i*1.85,-1.185),(1.25,1.65,.02),'Canvas',.015)
            for x in (-.55,.55): a.beam('Radar panel hinge',(x,-1.4+i*1.85,-1.1),(x,-1.1+i*1.85,-1.1),.07)
        for x in (-.5,.5): a.beam('Radar deployment bracket',(x,1.3,-.85),(x,1.3,-1.1),.08)
        groups['RadarBoom']=(a.PARTS[start:],(0,1.35,-1.1))
    elif kind=='relay':
        for sy in (-1,1):
            start=len(a.PARTS); dish('Relay reflector',(0,sy*1.8,-.5),1.25,(0,sy*.4,-1),'ThermalWhite')
            groups['DishFore' if sy<0 else 'DishAft']=(a.PARTS[start:],(0,sy*1.4,-.4))
    else:
        a.cylinder('Capture collar',(0,-2.25,0),.65,.35,'ThermalWhite',24,(0,1,0))
        a.cylinder('Docking aperture',(0,-2.45,0),.47,.055,'Dark',24,(0,1,0))
        for sx in (-1,1):
            a.beam('Capture arm',(sx*1.2,-1.8,.5),(sx*.75,-2.8,.4),.12)
            box('Capture claw',(sx*.75,-2.8,.4),(.2,.55,.22),'Steel',.06)
        a.cylinder('Main engine',(0,2.15,0),.4,.65,'Dark',20,(0,1,0))
        for side in (-1,1):
            a.cylinder('External propellant vessel',(side*1.36,.65,.45),.31,1.6,'Steel',24,(0,1,0))
            for yy in (.05,1.2): a.cylinder('Tank retaining band',(side*1.36,yy,.45),.325,.065,'ThermalWhite',24,(0,1,0))
            for yy in (-1.4,-.8):
                a.beam('Manipulator elbow',(side*1.2,-1.8,.5),(side*1.40,yy,1.0),.06,'Steel')
        for i in range(12):
            t=i*math.tau/12
            a.cylinder('Docking ring bolt',(.57*math.cos(t),-2.46,.57*math.sin(t)),.035,.03,'Steel',8,(0,1,0))
    port=solar(-1,3.5 if kind=='tug' else 4.2); starboard=solar(1,3.5 if kind=='tug' else 4.2)
    groups['SolarPort']=(port,(-1,0,0)); groups['SolarStarboard']=(starboard,(1,0,0))
    finish.finish(a)
    # Assign the remaining spacecraft geometry to the body root.
    moving={o for objects,pivot in groups.values() for o in objects}
    groups['Bus']=([o for o in a.PARTS if o not in moving],(0,0,0))
    export_rig(name,groups)

BUILDERS={'RadioRelay':relay,'MobileJammer':jammer,'AmmunitionDepot':ammo,'RepairStation':repair,
          'ForwardAidStation':aid,'RescueCrashSite':wreck,'SmugglerCache':cache,'CoastalSensorPost':coast,
          'DroneGroundStation':drone,'PowerSubstation':power,'BridgeDemolitionKit':demolition,'EvacuationLandingZone':lz,
          'OpticalSatellite':lambda:satellite('optical'),'SarSatellite':lambda:satellite('sar'),
          'RelaySatellite':lambda:satellite('relay'),'OrbitalServiceTug':lambda:satellite('tug')}
ANCHORS={
 'RadioRelay':{'Interact':(-2,1.2,-6.6),'Uplink':(3,16,3)},
 'MobileJammer':{'Emitter':(0,7.4,1.3),'Service':(2,1.2,1.3)},
 'AmmunitionDepot':{'CargoPickup':(0,0,-8),'Target':(0,1,1)},
 'RepairStation':{'RepairBay':(0,0,0),'Service':(0,1,8)},
 'ForwardAidStation':{'Triage':(0,0,-7),'Dropoff':(0,0,-12)},
 'RescueCrashSite':{'Recorder':(2,.3,4),'Survivor':(-2,0,4)},
 'SmugglerCache':{'Inspect':(0,0,-3)},
 'CoastalSensorPost':{'Sensor':(2,7,3),'Service':(-2,0,-6)},
 'DroneGroundStation':{'LaunchRail':(3,1.4,-1),'DataTerminal':(-4,1,0)},
 'PowerSubstation':{'Breaker':(0,1,-8),'Transformer':(0,2,1)},
 'BridgeDemolitionKit':{'ChargePlacement':(0,0,0),'Control':(0,.5,-2)},
 'EvacuationLandingZone':{'LandingPoint':(0,.37,0),'Extraction':(0,0,7)},
 'OpticalSatellite':{'NadirSensor':(0,-1.85,-1.1)},
 'SarSatellite':{'RadarOrigin':(0,-1.185,1.95)},
 'RelaySatellite':{'Downlink':(0,-1,0)},
 'OrbitalServiceTug':{'CapturePort':(0,0,-2.45),'Engine':(0,0,2.5)}}

if __name__=='__main__':
    finish.generate(a.OUT,a.COLORS)
    def dressed(fn):
        def run(): fn(); finish.finish(a)
        return run
    a.BUILDERS={name:(fn if 'Satellite' in name or name=='OrbitalServiceTug' else dressed(fn)) for name,fn in BUILDERS.items()}
    names=sys.argv[sys.argv.index('--')+2:]
    names=names[0].split(',') if names else list(BUILDERS)
    records=[]
    for name in names:
        record=a.build(name); record['key']='BS_Mission_'+name
        record['category']='orbital' if name in RIGS else 'ground'
        record['anchors']=[{'name':n,'position':{'x':p[0],'y':p[1],'z':p[2]}} for n,p in ANCHORS[name].items()]
        if name in RIGS: record['rig_model']=RIGS[name]
        records.append(record)
    if len(names)<len(BUILDERS) and (a.OUT/'catalog.json').exists():
        previous=json.loads((a.OUT/'catalog.json').read_text())['assets']; by_name={e['model']:e for e in previous}; by_name.update({e['model']:e for e in records}); records=[by_name[n] for n in BUILDERS if n in by_name]
    (a.OUT/'catalog.json').write_text(json.dumps({'units':'metres','frame':'Blender +Y forward, +Z up; anchors Unity +Y up','assets':records},indent=2))
