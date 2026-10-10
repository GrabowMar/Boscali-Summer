"""Original 124 m coastal container feeder. Called by BuildFutureOps.cargo_ship.
Hull is a faired loft, never a scaled cube. Metres, +Y bow, +Z up, waterline Z=0.
Native section exports retain shared world-space origins for accurate damage attachment.
"""
import bpy, bmesh, math, json, os, sys
from pathlib import Path
from mathutils import Vector
sys.path.insert(0,os.path.join(os.environ['LOCALAPPDATA'],'nomodkit','blender-pylibs'))
import numpy as np
from PIL import Image, ImageDraw, ImageFont
sys.path.insert(0,str(Path(__file__).resolve().parent))

PALETTE={'MarineHull':(.055,.105,.12),'MarineDeck':(.105,.15,.145),
         'MarineWhite':(.63,.66,.63),'MarineSteel':(.22,.255,.26),
         'CargoRed':(.38,.10,.065),'CargoBlue':(.08,.18,.22),
         'CargoOlive':(.24,.28,.19),'CargoGrey':(.49,.52,.49),
         'Antifoul':(.23,.075,.045),'MarineGlass':(.035,.09,.105),
         'MarineSafety':(.70,.34,.065),'MarineBlack':(.025,.03,.028),
         'NavPort':(.65,.018,.01),'NavStarboard':(.015,.5,.06)}
STATIONS=[(-62,6.8,.6),(-59,8.8,.75),(-54,9.8,.86),(-45,10.3,1),(-30,10.3,1),
          (-10,10.3,1),(12,10.3,1),(30,10.0,.97),(41,8.8,.85),(50,6.6,.64),
          (57,3.5,.38),(61,1.05,.15),(62,.06,.02)]
GROUPS={}
BOX_CACHE={}

def textures(out):
    folder=out/'Textures'; folder.mkdir(exist_ok=True)
    n=2048; rng=np.random.default_rng(8210)
    yy,xx=np.mgrid[0:n,0:n]; grit=rng.normal(0,.018,(n,n))
    # An original seamless paint tile: pigment breakup, welded panels, wash streaks.
    tone=.88+grit+.035*np.sin(xx*.023)*np.sin(yy*.012)
    seam=(xx%256<2)|(yy%512<2); tone[seam]*=.57
    for k in range(150):
        x=int(rng.integers(0,n)); y=int(rng.integers(0,n)); length=int(rng.integers(20,200))
        tone[y:min(y+length,n),x:min(x+3,n)]*=.74
    Image.fromarray(np.uint8(np.clip(tone,0,1)*255)).convert('RGB').save(folder/'MarinePaint_Albedo.png')
    h=.008*grit-.008*seam; gy,gx=np.gradient(h)
    normal=np.dstack((.5-gx*30,.5-gy*30,np.full_like(gx,1)))
    Image.fromarray(np.uint8(np.clip(normal,0,1)*255)).save(folder/'MarinePaint_Normal.png')
    # Container sides use real corrugated normals and uneven paint; geometry is reserved for fittings.
    wave=np.sin(xx*math.tau/128); tone=.82+.12*wave+grit
    rusty=(yy>n*.85)*(np.sin(xx*.071)>.65)
    cargo=np.dstack((tone,tone,tone)); cargo[rusty.astype(bool)]*=(.80,.62,.45)
    im=Image.fromarray(np.uint8(np.clip(cargo,0,1)*255)); d=ImageDraw.Draw(im)
    font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',65)
    d.text((160,100),'BSLU  482610  7',font=font,fill=(208,210,198))
    d.text((160,183),'45G1   MAX GROSS  30 480 KG',font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',31),fill=(190,190,177))
    for x in (70,n-70): d.rectangle((x,0,x+15,n),fill=(133,135,127))
    for y in (50,n-50): d.rectangle((0,y,n,y+12),fill=(138,139,130))
    im.save(folder/'Cargo_Albedo.png')
    cn=np.dstack((.5-.23*np.cos(xx*math.tau/128),np.full_like(wave,.5),np.sqrt(1-(.46*np.cos(xx*math.tau/128))**2)))
    Image.fromarray(np.uint8(np.clip(cn,0,1)*255)).save(folder/'Cargo_Normal.png')
    # Masks authored for the native ship damage material, not a decorative damage toggle.
    dmg=np.dstack((tone*.35,tone*.26,tone*.20))
    Image.fromarray(np.uint8(np.clip(dmg,0,1)*255)).save(folder/'MarinePaint_Damage.png')
    Image.fromarray(np.uint8(np.clip(.35+grit*3+.2*np.sin(xx*.011)*np.sin(yy*.008),0,1)*255)).save(folder/'DamageMask.png')
    for name,rgb in PALETTE.items():
        prefix='Cargo' if name.startswith('Cargo') else 'MarinePaint'
        paint=np.asarray(Image.open(folder/(prefix+'_Albedo.png')).convert('RGB'),dtype=float)/255
        tint=np.array(rgb)**(1/2.2)
        Image.fromarray(np.uint8(np.clip(paint*tint,0,1)*255)).save(folder/(name+'_Albedo.png'))
        Image.fromarray(np.uint8(np.clip(paint*tint*.30,0,1)*255)).save(folder/(name+'_Damage.png'))
    mr=np.zeros((n,n,4),dtype=np.uint8); mr[:]=[38,166,0,255]
    Image.fromarray(mr).save(folder/'MarinePaint_MetallicRoughness.png')
    return folder

def materials(a):
    folder=textures(a.OUT)
    for name,rgb in PALETTE.items():
        mat=bpy.data.materials.new(name); mat.use_nodes=True; mat.diffuse_color=(*rgb,1)
        bs=mat.node_tree.nodes['Principled BSDF']; bs.inputs['Base Color'].default_value=(*rgb,1)
        bs.inputs['Roughness'].default_value=.25 if name=='MarineGlass' else .65
        bs.inputs['Metallic'].default_value=.65 if name=='MarineSteel' else .12
        if name!='MarineGlass':
            prefix='Cargo' if name.startswith('Cargo') else 'MarinePaint'
            tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=bpy.data.images.load(str(folder/(prefix+'_Albedo.png')),check_existing=True)
            multiply=mat.node_tree.nodes.new('ShaderNodeMixRGB'); multiply.blend_type='MULTIPLY'; multiply.inputs[0].default_value=1
            multiply.inputs[2].default_value=(*rgb,1); mat.node_tree.links.new(tex.outputs['Color'],multiply.inputs[1]); mat.node_tree.links.new(multiply.outputs[0],bs.inputs['Base Color'])
            normal=mat.node_tree.nodes.new('ShaderNodeTexImage'); normal.image=bpy.data.images.load(str(folder/(prefix+'_Normal.png')),check_existing=True); normal.image.colorspace_settings.name='Non-Color'
            nm=mat.node_tree.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.65
            mat.node_tree.links.new(normal.outputs['Color'],nm.inputs['Color']); mat.node_tree.links.new(nm.outputs['Normal'],bs.inputs['Normal'])
        a.MATS[name]=mat

def smart_uv(ob):
    # Box UVs are explicit: container length runs across the corrugated side map.
    uv=ob.data.uv_layers.new(name='UVMap') if not ob.data.uv_layers else ob.data.uv_layers.active
    pts=[v.co for v in ob.data.vertices]; lo=Vector([min(p[i] for p in pts) for i in range(3)]); hi=Vector([max(p[i] for p in pts) for i in range(3)])
    size=hi-lo
    for poly in ob.data.polygons:
        axes=[i for i in range(3) if i!=max(range(3),key=lambda i:abs(poly.normal[i]))]
        for li in poly.loop_indices:
            p=ob.data.vertices[ob.data.loops[li].vertex_index].co
            uv.data[li].uv=((p[axes[0]]-lo[axes[0]])/max(size[axes[0]],.001),(p[axes[1]]-lo[axes[1]])/max(size[axes[1]],.001))

def box(a,name,pos,size,mat='MarineSteel',bevel=.025):
    key=(tuple(round(float(s),6) for s in size),mat,bevel)
    if key in BOX_CACHE:
        ob=bpy.data.objects.new(name,BOX_CACHE[key]); bpy.context.collection.objects.link(ob)
        ob.location=pos; a.PARTS.append(ob); return ob
    ob=a.box(name,pos,size,mat,bevel); smart_uv(ob)
    if bevel:
        mod=ob.modifiers.new('Weighted corner normals','WEIGHTED_NORMAL'); mod.keep_sharp=True
        bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
    BOX_CACHE[key]=ob.data
    return ob

def beam(a,name,p,q,width=.06,mat='MarineSteel'):
    ob=a.beam(name,p,q,width,mat); smart_uv(ob); return ob

def cyl(a,name,pos,radius,height,mat='MarineSteel',n=16,direction=None):
    ob=a.cylinder(name,pos,radius,height,mat,n,direction)
    for face in ob.data.polygons:
        if len(face.vertices)==4: face.use_smooth=True
    smart_uv(ob); return ob

def profile(y):
    # Cosine interpolated breadth/underwater fullness avoids station kinks.
    for left,right in zip(STATIONS,STATIONS[1:]):
        if left[0]<=y<=right[0]:
            t=(y-left[0])/(right[0]-left[0]); t=.5-.5*math.cos(t*math.pi)
            return left[1]*(1-t)+right[1]*t,left[2]*(1-t)+right[2]*t
    return STATIONS[0][1:] if y<0 else STATIONS[-1][1:]

def sheer(y): return 5.8+max((y-28)/34,0)**2*1.6+max((-y-43)/19,0)**2*.45

def hull(a,start,end,key,sign):
    verts=[]; faces=[]; rings=22; sides=14
    for j in range(rings):
        y=start+(end-start)*j/(rings-1); w,full=profile(y)
        # Port keel -> bilge -> flared topside -> starboard. Positive geometry only.
        section=[]
        for i in range(12):
            t=i/11; z=-5.5+(sheer(y)+5.5)*t
            underwater=math.sin(min(t/.50,1)*math.pi/2)**.70
            breadth=w*(.35*full+.65*underwater)*(.94+.06*t)
            aft=max((-y-46)/16,0)**2
            rake=8*aft*(1-math.exp(min(z,0)/1.2))
            section.append((sign*breadth,y+rake,z))
        aft=max((-y-46)/16,0)**2
        section.extend([(0,y,sheer(y)),(0,y+8*aft*(1-math.exp(-5.5/1.2)),-5.5)])
        verts+=section
    for j in range(rings-1):
        for k in range(sides): faces.append((j*sides+k,j*sides+(k+1)%sides,(j+1)*sides+(k+1)%sides,(j+1)*sides+k))
    faces.extend([tuple(reversed(range(sides))),tuple(range((rings-1)*sides,rings*sides))])
    ob=a.mesh(key,verts,faces,'MarineHull'); ob.data.materials.append(a.MATS['Antifoul'])
    uv=ob.data.uv_layers.new(name='UVMap')
    for p in ob.data.polygons:
        p.use_smooth=len(p.vertices)==4 and p.index%sides<11
        if sum(ob.data.vertices[i].co.z for i in p.vertices)/len(p.vertices)<-.45: p.material_index=1
        for li in p.loop_indices:
            v=ob.data.vertices[ob.data.loops[li].vertex_index].co
            uv.data[li].uv=((v.y+62)/124*6,(v.z+5.5)/13)
    # Use the hull loft itself for the static asset; the native builder subdivides/cooks convex compartments.
    col=ob.copy(); col.data=ob.data.copy(); col.name='COL_'+key; bpy.context.collection.objects.link(col); col.hide_render=True; a.COLLIDERS.append(col)
    return ob

def group(a,key,fn):
    first=len(a.PARTS); fn(); GROUPS[key]=a.PARTS[first:]

def railing(a,points,z,name='Safety rail'):
    for p,q in zip(points,points[1:]):
        p,q=Vector((*p,z)),Vector((*q,z)); count=max(1,math.ceil((q-p).length/2.3))
        for i in range(count+1):
            v=p.lerp(q,i/count); cyl(a,'Rail stanchion',v+Vector((0,0,.55)),.028,1.1,n=8)
        for height in (.52,1.05): beam(a,name,p+Vector((0,0,height)),q+Vector((0,0,height)),.035)

def container(a,x,y,z,variant,length=12.19):
    mat=('CargoBlue','CargoRed','CargoOlive','CargoGrey')[variant%4]
    box(a,'ISO container',(x,y,z+1.295),(2.438,length,2.59),mat,.035)
    a.box('Cargo collision',(x,y,z+1.295),(2.438,length,2.59),collision=True)
    for sy in (-1,1):
        for xx in (-.6,.6):
            box(a,'Container door',(x+xx,y+sy*(length/2+.025),z+1.30),(1.13,.045,2.36),mat,.015)
            for dx in (-.22,.22): cyl(a,'Door lock rod',(x+xx+dx,y+sy*(length/2+.08),z+1.3),.021,2.30,n=8)
            box(a,'Door locking handle',(x+xx,y+sy*(length/2+.11),z+.8),(.38,.045,.04),'MarineSteel',0)
    for xx in (-1.14,1.14):
        for yy in (-length/2+.08,length/2-.08):
            for zz in (.10,2.49): box(a,'ISO casting',(x+xx,y+yy,z+zz),(.16,.20,.18),'MarineSteel',0)

def stairs(a,x,y,z,levels=12,direction=1):
    for i in range(levels): box(a,'Open stair tread',(x,y+i*.26*direction,z+i*.19),(1,.25,.05),'MarineSteel',0)
    for sx in (-1,1):
        beam(a,'Stair stringer',(x+sx*.48,y,z-.08),(x+sx*.48,y+direction*(levels-1)*.26,z+(levels-1)*.19-.08),.08)
        beam(a,'Stair handrail',(x+sx*.53,y,z+1),(x+sx*.53,y+direction*(levels-1)*.26,z+(levels-1)*.19+1),.045)

def accommodation(a):
    # Successively stepped decks, real side passages and projecting enclosed bridge wings.
    for level in range(4):
        z=6.25+level*2.6; width=12.8 if level<3 else 11.2; length=13.2-level*.5
        box(a,'Accommodation deckhouse',(0,-44,z+1.18),(width,length,2.35),'MarineWhite',.08)
        a.box('Accommodation collision',(0,-44,z+1.18),(width,length,2.35),collision=True)
        box(a,'Weather deck',(0,-44,z+2.45),(width+1.6,length+1.1,.18),'MarineSteel',.035)
        for sx in (-1,1):
            for j in range(6):
                yy=-49.1+j*1.85
                box(a,'Accommodation porthole',(sx*(width/2+.025),yy,z+1.55),(.065,.65,.68),'MarineGlass',.06)
                box(a,'Window drip rail',(sx*(width/2+.1),yy,z+1.95),(.16,.82,.08),'MarineWhite',0)
            box(a,'Watertight door',(sx*(width/2+.035),-39,z+1.1),(.09,.83,1.9),'MarineDeck',.04)
            railing(a,[(sx*(width/2+.65),-length/2-44),(sx*(width/2+.65),length/2-44)],z+2.56)
        stairs(a,7.2,-49,z+.15,13,1)
    bridge_z=17.85
    box(a,'Wheelhouse',(0,-42.5,bridge_z),(14.8,6.2,2.8),'MarineWhite',.10)
    box(a,'Bridge roof',(0,-42.5,bridge_z+1.5),(15.5,6.8,.18),'MarineSteel',.04)
    for x in np.arange(-6.65,7,.92):
        window=box(a,'Angled bridge glazing',(float(x),-39.35,bridge_z+.15),(.78,.075,1.30),'MarineGlass',.035)
        window.rotation_euler.x=math.radians(-10)
        beam(a,'Window wiper',(x-.20,-39.24,bridge_z-.3),(x+.20,-39.24,bridge_z+.45),.025,'MarineBlack')
    for sx in (-1,1):
        box(a,'Bridge wing',(sx*8.1,-42.5,bridge_z-.20),(2.2,5.7,2.2),'MarineWhite',.12)
        box(a,'Bridge wing glazing',(sx*9.25,-42.5,bridge_z+.1),(.07,4.5,1.2),'MarineGlass',.02)
        box(a,'Navigation sidelight',(sx*9.25,-39.9,bridge_z+.6),(.12,.30,.28),'NavPort' if sx<0 else 'NavStarboard',.02)
    box(a,'Funnel casing',(3.6,-47,19.79),(3.1,3.6,6.4),'MarineHull',.2)
    for z in (21.2,21.5,21.8): box(a,'Funnel louver',(3.6,-45.17,z),(2.55,.07,.10),'MarineBlack',0)
    for x in (2.8,4.2): cyl(a,'Exhaust uptake',(x,-47,23.54),.4,1.1,'MarineBlack',16)
    for x in (-2.5,0):
        box(a,'Bridge HVAC',(x,-44.5,19.74),(1.5,1.4,.6),'MarineWhite',.05)
        cyl(a,'HVAC fan',(x,-44.5,20.065),.42,.05,'MarineBlack',16)
    # Mainmast, radar scanners, satellite domes, navigation lights and aerials.
    cyl(a,'Navigation mast',(-2,-43,22.7),.12,6.5,n=12)
    for z,length in ((24.9,4.2),(22.4,2.8)):
        beam(a,'Scanner yard',(-2-length/2,-43,z),(-2+length/2,-43,z),.1)
        box(a,'Radar scanner',(-2,-43,z+.3),(length,.30,.25),'MarineWhite',.05)
    for x in (-5,5):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.60,location=(x,-44,20.04))
        ob=bpy.context.object; ob.name='Satellite radome'; ob.data.materials.append(a.MATS['MarineWhite']); a.PARTS.append(ob)
        for p in ob.data.polygons: p.use_smooth=True
    for x in (-3.8,-1,.6): cyl(a,'Whip antenna',(x,-43,22),.025,3.5,n=8)
    # Enclosed lifeboats, davits and embarkation platforms.
    for sx in (-1,1):
        boat=box(a,'Enclosed lifeboat',(sx*8.45,-47.5,11.3),(2.15,6.3,1.8),'MarineSafety',.48)
        box(a,'Lifeboat canopy',(sx*8.45,-47.2,12.3),(1.55,3.6,.85),'MarineSafety',.30)
        box(a,'Lifeboat windscreen',(sx*8.45,-45.34,12.35),(1.2,.05,.35),'MarineGlass',.04)
        for yy in (-49.5,-45.5):
            beam(a,'Davit arm',(sx*7.1,yy,10),(sx*9.9,yy,13),.15)
            beam(a,'Davit arm',(sx*9.9,yy,13),(sx*8.6,yy,13),.15)
            beam(a,'Boat fall',(sx*8.6,yy,13),(sx*8.6,yy,12.1),.025,'MarineBlack')
        box(a,'Embarkation deck',(sx*8.2,-47,9.8),(3,7,.16),'MarineSteel',.035)

def foredeck(a):
    # Raised forecastle follows the bow; bulwarks are capped and pierced by fairleads.
    ys=np.linspace(43,61.8,38)
    verts=[]
    for y in ys:
        w,_=profile(float(y)); z=sheer(float(y))+.08
        verts.extend([(-w*.97,float(y),z),(0,float(y),z+.15),(w*.97,float(y),z)])
    a.mesh('Fair forecastle deck',verts,[(j*3+k,j*3+k+1,(j+1)*3+k+1,(j+1)*3+k) for j in range(len(ys)-1) for k in (0,1)],'MarineDeck')
    for sx in (-1,1):
        wall=[]
        for y in ys:
            w,_=profile(float(y)); z=sheer(float(y))+.08
            wall.extend([(sx*w*.97,float(y),z),(sx*w*.97,float(y),z+1.1)])
        ob=a.mesh('Continuous bow bulwark',wall,[(j*2,j*2+1,(j+1)*2+1,(j+1)*2) for j in range(len(ys)-1)],'MarineHull')
        mod=ob.modifiers.new('Bulwark thickness','SOLIDIFY'); mod.thickness=.12; bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)
        for j in range(len(ys)-1): beam(a,'Bulwark capping',wall[j*2+1],wall[(j+1)*2+1],.06)
    for sx in (-1,1):
        deck=sheer(48)+.08
        cyl(a,'Windlass drum',(sx*3.2,48,deck+1.25),.65,1.5,'MarineSteel',16,(1,0,0))
        box(a,'Windlass foundation',(sx*3.2,48,deck+.325),(2,2,.65),'MarineSteel',.07)
        cyl(a,'Capstan',(sx*5,45,sheer(45)+.68),.35,1.2,'MarineSteel',16)
        for j in range(12):
            yy=49+j*.40
            cyl(a,'Anchor chain link',(sx*3.2,yy,sheer(yy)+.17),.075,.10,'MarineBlack',8,(1,0,0) if j%2 else (0,1,0))
        box(a,'Chain stopper',(sx*3.2,53.6,sheer(53.6)+.28),(1.0,1.0,.4),'MarineSteel',.03)
        beam(a,'Anchor shank',(sx*6.4,52,3.6),(sx*6.4,52,5.1),.18)
        beam(a,'Anchor fluke',(sx*6.4,52,3.6),(sx*6.4,51.2,4.15),.23)
        beam(a,'Anchor fluke',(sx*6.4,52,3.6),(sx*6.4,52.8,4.15),.23)
    cyl(a,'Forward jackstaff',(0,58,9.2),.045,3,n=8)
    for y in (45,54):
        w,_=profile(y)
        for sx in (-1,1):
            for yy in (y-.3,y+.3): cyl(a,'Bitt',(sx*(w-1.1),yy,sheer(y)+.5),.17,.8,n=12)
            box(a,'Bitt cap',(sx*(w-1.1),y,sheer(y)+.95),(.45,1,.14),'MarineSteel',.04)

def props(a):
    for sx in (-1,1):
        cyl(a,'Propeller shaft',(sx*3.3,-55,-3.4),.20,8,'MarineSteel',16,(0,1,0))
        box(a,'Balanced rudder',(sx*3.3,-60.25,-3),( .30,2.3,3.5),'MarineHull',.13)

def propeller(a,sx):
        cyl(a,'Propeller hub',(sx*3.3,-58.5,-3.4),.48,1.15,'MarineSteel',20,(0,1,0))
        for i in range(4):
            t=i*math.pi/2; verts=[]
            for r,angle,yy in ((.45,t,-58.3),(1.7,t+.5,-58.35),(1.95,t+.8,-58.6),(1.0,t+1.0,-58.75)):
                verts.append((sx*3.3+r*math.cos(angle),yy,-3.4+r*math.sin(angle)))
            ob=a.mesh('Skewed propeller blade',verts,[(0,1,2,3)],'MarineSteel')
            mod=ob.modifiers.new('Blade thickness','SOLIDIFY'); mod.thickness=.06; bpy.context.view_layer.objects.active=ob; bpy.ops.object.modifier_apply(modifier=mod.name)

def native_export(a):
    export=[]; specs=[]
    for key,objects in GROUPS.items():
        copies=[]
        for obj in objects:
            c=obj.copy(); c.data=obj.data.copy(); bpy.context.collection.objects.link(c); c.hide_render=True; copies.append(c)
        ob=a.join(copies,key+'_LOD0'); ob.hide_render=True; export.append(ob)
        for level,ratio in ((1,.36),(2,.09)):
            c=ob.copy(); c.data=ob.data.copy(); c.name=c.data.name=key+'_LOD'+str(level); bpy.context.collection.objects.link(c)
            bpy.context.view_layer.objects.active=c; mod=c.modifiers.new('Native LOD','DECIMATE'); mod.ratio=ratio
            bpy.ops.object.modifier_apply(modifier=mod.name); c.hide_render=True; export.append(c)
        bounds=[Vector(v) for v in ob.bound_box]; lo=Vector([min(v[i] for v in bounds) for i in range(3)]); hi=Vector([max(v[i] for v in bounds) for i in range(3)])
        center={'x':(lo.x+hi.x)/2,'y':(lo.z+hi.z)/2,'z':(lo.y+hi.y)/2}
        if key.startswith('Propeller'): center={'x':-3.3 if key.endswith('Port') else 3.3,'y':-3.4,'z':-58.5}
        specs.append({'name':key,'center_unity':center,'hull':key.startswith('Hull'),'rotor':key.startswith('Propeller')})
    bpy.ops.object.select_all(action='DESELECT')
    for o in export: o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(a.OUT/'Models/CargoShipNative.fbx'),use_selection=True,
        axis_forward='-Z',axis_up='Y',apply_scale_options='FBX_SCALE_NONE',bake_space_transform=False,
        object_types={'MESH'},mesh_smooth_type='EDGE',use_tspace=False,add_leaf_bones=False,bake_anim=False)
    (a.OUT/'cargo-sections.json').write_text(json.dumps({'parts':specs,'waterline':0,'length':124,'beam':20.6},indent=2))
    # Remove export copies before the static asset's master Blender file is saved.
    for o in export: bpy.data.objects.remove(o,do_unlink=True)

def build_geometry(a):
    global GROUPS,BOX_CACHE
    GROUPS={}; BOX_CACHE={}; materials(a)
    for stem,lo,hi in (('HullStern',-62,-35),('HullAft',-35,-9),('HullMid',-9,19),('HullBow',19,62)):
        for sign,suffix in ((-1,'Port'),(1,'Starboard')):
            key=stem+suffix
            group(a,key,lambda lo=lo,hi=hi,key=key,sign=sign:hull(a,lo,hi,key,sign))
    # Original bulbous bow, below the waterline, blended into the stem.
    def bulb():
        bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=12,radius=1,location=(0,59.6,-3.0))
        ob=bpy.context.object; ob.name='Bulbous bow'; ob.scale=(1.7,5,1.65); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
        ob.data.materials.append(a.MATS['Antifoul']); a.PARTS.append(ob)
        for p in ob.data.polygons: p.use_smooth=True
        smart_uv(ob)
    first=len(a.PARTS); bulb(); GROUPS['HullBowPort']+=a.PARTS[first:]
    def deck():
        for y in np.linspace(-59,43,103):
            w,_=profile(float(y)); box(a,'Cambered weather deck',(0,float(y),sheer(float(y))+.06),(w*1.94,1.02,.12),'MarineDeck',0)
        for sx in (-1,1):
            points=[(sx*(profile(y)[0]-.35),y) for y in range(-59,44,4)]
            railing(a,points,6.0)
            for y in range(-31,42,8): box(a,'Scupper',(sx*10.25,y,5.78),(.04,.45,.16),'MarineBlack',0)
    group(a,'WeatherDeck',deck)
    for bay,yy in enumerate((-22,-7,8,23,36)):
        def cargo(bay=bay,yy=yy):
            length=12.19 if bay<4 else 6.06
            box(a,'Hatch coaming',(0,yy,6.4),(15.2,length+1,.8),'MarineHull',.07)
            box(a,'Pontoon hatch cover',(0,yy,6.84),(15.0,length+.7,.12),'MarineDeck',.025)
            for xx in np.arange(-6.3,6.4,2.52):
                layers=3 if bay<3 else 2
                if bay==0 and abs(xx)>5: layers=2
                for tier in range(layers): container(a,float(xx),yy,6.95+tier*2.62,bay*3+int(xx*2)+tier,length)
            # Lashing bridge: walkway, vertical guides, diagonal tie rods.
            for sx in (-1,1):
                for x in (sx*7.4,sx*5.1):
                    beam(a,'Cargo guide',(x,yy-length/2-.5,6.9),(x,yy-length/2-.5,12.0),.10)
                beam(a,'Lashing brace',(sx*7.4,yy-length/2-.5,7),(sx*5.1,yy-length/2-.5,11.8),.045)
            box(a,'Lashing walkway',(0,yy-length/2-.55,9.5),(15.2,.75,.10),'MarineSteel',0)
            railing(a,[(-7.4,yy-length/2-.85),(7.4,yy-length/2-.85)],9.6)
        group(a,'CargoBay'+str(bay),cargo)
    group(a,'Accommodation',lambda:accommodation(a))
    group(a,'Forecastle',lambda:foredeck(a))
    group(a,'PropulsionVisuals',lambda:props(a))
    group(a,'PropellerPort',lambda:propeller(a,-1))
    group(a,'PropellerStarboard',lambda:propeller(a,1))
    # Stern working gear and aft access, readable when viewed close from aircraft.
    def stern():
        for sx in (-1,1):
            cyl(a,'Aft mooring winch',(sx*4.7,-56,7.2),.50,1.3,'MarineSteel',16,(1,0,0))
            for yy in (-57,-54): cyl(a,'Aft bollard',(sx*6.1,yy,6.9),.2,.7,n=12)
        box(a,'Stern hatch',(0,-56,6.6),(2.4,3,.20),'MarineWhite',.05)
        cyl(a,'Stern staff',(0,-60,7.4),.04,2.8,n=8)
    group(a,'SternEquipment',stern)
    for ob in a.PARTS:
        if not ob.data.uv_layers: smart_uv(ob)
    bpy.ops.wm.save_as_mainfile(filepath=str(a.OUT/'Models/CargoShipAuthoring.blend'))
    native_export(a)

if __name__=='__main__':
    import BuildFutureOps as base
    record=base.build('CargoShip')
    old=json.loads((base.OUT/'catalog.json').read_text())
    old['assets']=[record if e['model']=='CargoShip' else e for e in old['assets']]
    (base.OUT/'catalog.json').write_text(json.dumps(old,indent=2))
