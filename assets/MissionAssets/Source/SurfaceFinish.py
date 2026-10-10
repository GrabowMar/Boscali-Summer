"""Original tileable PBR surfaces and metre-scaled UVs; no extracted game art."""
import bpy, bmesh, math, os, sys
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(os.environ['LOCALAPPDATA'])/'nomodkit/blender-pylibs'))
from PIL import Image, ImageDraw

def generate(out, colors):
    folder=out/'Textures'; folder.mkdir(exist_ok=True)
    n=512; y,x=np.mgrid[:n,:n]; rng=np.random.default_rng(81624)
    for key,rgb in colors.items():
        if key.startswith(('Marine','Cargo','Nav')) or key=='Antifoul': continue
        noise=rng.normal(0,1,(n,n)); broad=(np.sin(x*.041)+np.cos(y*.027)+np.sin((x+y)*.014))/3
        height=noise*.0008; variation=broad*.018+noise*.008
        metal=.45 if key=='Steel' else .75 if key=='SpaceGold' else 0
        smooth=.76 if key=='Glass' else .24
        if key in ('Canvas','Earth'):
            weave=((x%4==0)|(y%4==0)).astype(float); height+=weave*.009; variation-=weave*.025
        elif key=='SpaceGold':
            # Irregular restrained foil folds, rather than a regular waffle pattern.
            crease=sum(np.sin((x*math.cos(t)+y*math.sin(t))*f+np.sin(y*.013)*2+p)
                       for t,f,p in ((.3,.051,1.2),(1.1,.077,2.8),(2.4,.12,.4),(2.9,.033,4.2)))/4
            height+=crease*.012; variation+=crease*.055; smooth=.42
        elif key.startswith('Solar'):
            column=x*12//n; row=y*6//n
            grid=((x*12%n<20)|(y*6%n<14)); fingers=x%7==0
            height+=grid*.003; variation-=grid*.09; variation+=fingers*.014
            variation+=np.sin(column*2.1+row*.8)*.010; smooth=.56; metal=.3
        elif key=='Concrete':
            height+=noise*.003; variation+=noise*.025
            variation-=((x%256<2)|(y%256<2))*.035
        else:
            # Sparse worn scratches, restrained mottling and exposed-metal flecks.
            scratch=(np.sin(x*.4+y*.06)>.997)&(broad>.1)
            height-=scratch*.005; variation+=scratch*.05
        srgb=np.where(np.array(rgb)<=.0031308,np.array(rgb)*12.92,1.055*np.array(rgb)**(1/2.4)-.055)
        albedo=np.clip(srgb[None,None,:]+variation[:,:,None],0,1)
        if key in ('Olive','Steel','White','Safety','Red','Wreck'):
            # Tiny paint chips: not a repeating camouflage pattern.
            chip=(noise>2.8)&(broad>.15); albedo[chip]=(.30,.32,.29)
        Image.fromarray((albedo*255).astype('uint8')).save(folder/(key+'_Albedo.png'))
        dx=(np.roll(height,-1,1)-np.roll(height,1,1))*24
        dy=(np.roll(height,-1,0)-np.roll(height,1,0))*24
        normal=np.stack((-dx,-dy,np.ones_like(dx)),axis=-1); normal/=np.linalg.norm(normal,axis=-1)[:,:,None]
        Image.fromarray(((normal*.5+.5)*255).astype('uint8')).save(folder/(key+'_Normal.png'))
        gloss=np.zeros((n,n,4),dtype='uint8'); gloss[:,:,:3]=round(metal*255); gloss[:,:,3]=(np.clip(smooth+noise*.015,0,1)*255).astype('uint8')
        Image.fromarray(gloss).save(folder/(key+'_MetalGloss.png'))

def configure(a):
    for key,mat in a.MATS.items():
        path=a.OUT/'Textures'/(key+'_Albedo.png')
        if not path.exists(): continue
        nodes=mat.node_tree.nodes; links=mat.node_tree.links; bs=nodes['Principled BSDF']
        for suffix,socket in (('Albedo','Base Color'),('Normal','Normal'),('MetalGloss',None)):
            img=bpy.data.images.load(str(a.OUT/'Textures'/(key+'_'+suffix+'.png')),check_existing=True)
            if suffix!='Albedo': img.colorspace_settings.name='Non-Color'
            img.pack(); node=nodes.new('ShaderNodeTexImage'); node.image=img
            if suffix=='Normal':
                normal=nodes.new('ShaderNodeNormalMap'); normal.inputs['Strength'].default_value=.45
                links.new(node.outputs['Color'],normal.inputs['Color']); links.new(normal.outputs['Normal'],bs.inputs[socket])
            elif suffix=='Albedo': links.new(node.outputs['Color'],bs.inputs[socket])
            else:
                separate=nodes.new('ShaderNodeSeparateColor'); links.new(node.outputs['Color'],separate.inputs[0]); links.new(separate.outputs['Red'],bs.inputs['Metallic'])
                invert=nodes.new('ShaderNodeMath'); invert.operation='SUBTRACT'; invert.inputs[0].default_value=1
                links.new(node.outputs['Alpha'],invert.inputs[1]); links.new(invert.outputs[0],bs.inputs['Roughness'])

def uv(objects):
    # Local planar projection keeps every part at 1 texture repeat / 2 metres.
    # UVs are also assigned to rig copies before export, so static and moving skins agree.
    for ob in objects:
        layer=ob.data.uv_layers.get('UVMap') or ob.data.uv_layers.new(name='UVMap')
        for p in ob.data.polygons:
            axis=max(range(3),key=lambda i:abs(p.normal[i])); axes=[i for i in range(3) if i!=axis]
            for li in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[li].vertex_index].co
                if ob.get('solar_panel_uv'):
                    layer.data[li].uv=(v.x/ob['panel_length']+.5,v.y/ob['panel_width']+.5)
                else: layer.data[li].uv=(v[axes[0]]*.5,v[axes[1]]*.5)

def fittings(a):
    """Dress authored structural parts; bounded detail without changing collision."""
    for ob in list(a.PARTS):
        name=ob.name.split('.')[0]; p=ob.location; s=ob.dimensions
        if name in ('Supply crate','Contraband case','Demolition case','Medical supplies','Battery case','Palletized ammunition','Extraction supplies'):
            for side in (-1,1):
                a.box('Case protective rail',(p.x+side*s.x*.44,p.y,p.z+s.z*.51),(.06,s.y*.9,.05),'Steel',.012)
                a.box('Recessed case handle',(p.x+side*s.x*.505,p.y,p.z),(.035,s.y*.25,s.z*.16),'Dark',.01)
            for x in (-.32,.32): a.box('Latch',(p.x+x*s.x,p.y-s.y*.505,p.z+s.z*.25),(.11,.045,.15),'Steel',.01)
        if name in ('Module','Equipment shelter','Battery cabinets','Power cabinet','Generator','Transformer'):
            front=p.y-s.y/2-.012
            for x in (-.40,.40):
                a.box('Access panel trim',(p.x+x*s.x,front,p.z),(.035,.025,s.z*.75),'Steel',.008)
            a.box('Equipment ID plate',(p.x+s.x*.22,front-.025,p.z+s.z*.25),(min(.65,s.x*.25),.02,.20),'White',.006)
            for i in range(5): a.box('ID stencil',(p.x+s.x*.22-.20+i*.09,front-.038,p.z+s.z*.25),(.04,.005,.085),'Dark')
        if name=='Canvas tent':
            # Seams and ventilation mesh read at normal mission camera distances.
            verts=[ob.matrix_world@v.co for v in ob.data.vertices]; lo=[min(v[i] for v in verts) for i in range(3)]; hi=[max(v[i] for v in verts) for i in range(3)]
            center=(lo[0]+hi[0])/2
            for yy in (lo[1]+.6,hi[1]-.6):
                for side in (-1,1):
                    a.beam('Stitched canvas seam',(center,yy,3.515),(center+side*(hi[0]-lo[0])/2,yy,2.115),.025,'Canvas')
            for side in (-1,1):
                a.box('Tent mesh vent',(center+side*(hi[0]-lo[0])/2, (lo[1]+hi[1])/2,1.5),(.03,1.6,.5),'Dark',.02)
        if name=='MLI spacecraft bus':
            for side in (-1,1):
                for yy in (-s.y*.38,0,s.y*.38):
                    a.box('Blanket fastener tab',(p.x+side*(s.x/2+.02),yy,s.z*.32),(.035,.10,.10),'ThermalWhite',.008)
                a.box('Avionics access panel',(p.x+side*(s.x/2+.035),0,0),(.045,s.y*.48,s.z*.52),'ThermalWhite',.02)
                for yy in (-s.y*.17,s.y*.17):
                    for zz in (-s.z*.16,s.z*.16): a.cylinder('Captive panel screw',(p.x+side*(s.x/2+.065),yy,zz),.018,.012,'Dark',8,(1,0,0))
            for i in range(9): a.box('Radiator coolant channel',(-s.x*.34+i*s.x*.085,0,s.z/2+.045),(.015,s.y*.72,.018),'Steel')
            for side in (-1,1):
                a.cylinder('Star tracker housing',(side*.55,-s.y/2-.08,s.z*.25),.14,.20,'Steel',16,(0,1,0))
                a.cylinder('Star tracker lens',(side*.55,-s.y/2-.195,s.z*.25),.10,.025,'Glass',16,(0,1,0))
        if name=='Roof HVAC':
            for side in (-1,1): a.cylinder('Fan grille',(p.x+side*.38,p.y,p.z+s.z/2+.012),.28,.025,'Dark',16)

def finish(a):
    bpy.context.view_layer.update(); fittings(a)
    for ob in a.PARTS: clean(ob.data)
    uv(a.PARTS)
    for ob in a.PARTS:
        if ob.name.startswith('Sandbag'):
            for p in ob.data.polygons: p.use_smooth=True
        elif any(len(p.vertices)>4 for p in ob.data.polygons) and len(ob.data.polygons)>=10:
            # Cylinders/lofted shells: curved sides with flat end caps; no extra vertices.
            for p in ob.data.polygons: p.use_smooth=len(p.vertices)==4
    configure(a)

def clean(mesh):
    bm=bmesh.new(); bm.from_mesh(mesh)
    bmesh.ops.dissolve_degenerate(bm,dist=1e-6,edges=list(bm.edges))
    collapsed=[f for f in bm.faces if f.calc_area()<1e-12]
    if collapsed: bmesh.ops.delete(bm,geom=collapsed,context='FACES_ONLY')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
    bm.to_mesh(mesh); bm.free(); mesh.update()
