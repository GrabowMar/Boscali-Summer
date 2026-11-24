"""KY-44 pressure hull, wraparound flight deck, pressure doors and cabin windows.

Axes are X across, +Y towards the nose, +Z up, all dimensions in metres.
The forward pressure shell mates with the surviving forward damage island at its
actual rim. The gear opening, gun sponsons and flight deck move with that shell.
"""
from bisect import bisect_left
from math import atan2, cos, pi, radians, sin
import bpy
from mathutils import Vector


class Surface:
    def __init__(self):
        self.points, self.index, self.faces, self.slots, self.materials = [], {}, [], [], []

    def face(self, points, material, outward):
        ids = []
        for p in points:
            pt = tuple(round(c, 6) for c in p)
            if pt not in self.index:
                self.index[pt] = len(self.points)
                self.points.append(pt)
            i = self.index[pt]
            if i not in ids:
                ids.append(i)
        if len(ids) < 3:
            return
        a, b, c = [Vector(self.points[i]) for i in ids[:3]]
        normal = (b-a).cross(c-a)
        if normal.length_squared < 1e-16:
            return
        if normal.dot(Vector(outward)) < 0:
            ids.reverse()
        if material not in self.materials:
            self.materials.append(material)
        self.faces.append(ids)
        self.slots.append(self.materials.index(material))

    def install(self, obj, smooth=False):
        origin = obj.matrix_world.translation
        mesh = bpy.data.meshes.new(obj.name)
        mesh.from_pydata([Vector(p)-origin for p in self.points], [], self.faces)
        for m in self.materials:
            mesh.materials.append(bpy.data.materials["BS_Megaplane_"+m])
        mesh.polygons.foreach_set("material_index", self.slots)
        mesh.update()
        tex = mesh.uv_layers.new()
        for poly in mesh.polygons:
            poly.use_smooth = smooth
            n = poly.normal
            axis = max(range(3), key=lambda axis: abs(n[axis]))
            for loop in poly.loop_indices:
                x,y,z = self.points[mesh.loops[loop].vertex_index]
                u,v = ((y,z),(x,z),(x,y))[axis]
                tex.uv[loop].vector = u/16, v/16
        old = obj.data
        obj.data = mesh
        if old and not old.users:
            bpy.data.meshes.remove(old)


def child_mesh(name, owner):
    obj = bpy.data.objects.new(name, bpy.data.meshes.new(name))
    bpy.context.collection.objects.link(obj)
    obj.parent = owner
    bpy.context.view_layer.update()
    return obj


def width_at(points, latitude):
    i = max(1, min(len(points)-1, bisect_left(points, (latitude, -1))))
    (a, wa), (b, wb) = points[i-1:i+1]
    return wa + (wb-wa)*(latitude-a)/(b-a)


def make_forebody(nodes):
    front = nodes["Fuselage_Forward"]
    nose = nodes["Fuselage_Cockpit"]
    start = 57.32 * .9
    gear_end = 67.47 * .9
    cap_start = 80.0
    tip = 118.0

    # Broaden the shoulders gently while keeping the fighter bay lips and doors seated.
    for vertex in front.data.vertices:
        p = front.matrix_world @ vertex.co
        t = max(0, min(1, (p.y-26)/(start-26)))
        weight = t*t*(3-2*t) * max(0, min(1, (p.z+11)/7))
        vertex.co.x += p.x*.19*weight
        vertex.co.z += 1.8*weight
    front.data.update()
    seam = set()
    for face in front.data.polygons:
        if not front.data.materials[face.material_index].name.endswith("_Skin"):
            continue
        for i in face.vertices:
            p = front.matrix_world @ front.data.vertices[i].co
            if abs(p.y-start)<.01 and p.x>=-.001:
                seam.add((round(p.z,5), round(max(p.x,0),5)))
    rim = sorted(seam)
    assert len(rim) >= 28, "forward/cockpit mating rim is missing"
    bottom = rim[0][0]-.22
    height = rim[-1][0]-bottom
    profile = [(-1,0)] + [(2*(z-bottom)/height-1,x) for z,x in rim]
    gear_edge = profile[1][0]
    low, high = .656, .773
    # The forward keel stays narrow well ahead of the shoulders. Upper and lower
    # chine lines remain continuous from the pressure barrel through the apex.
    sections = [(-1.,0.),(-.92,.51),(-.75,.74),(-.43,.91),(-.10,1.),
                (.10,.992),(.53,.84),(.78,.68),(.93,.47),(1.,0.)]
    reach = [(-1.,0.),(-.92,.30),(-.75,.57),(-.43,.82),(-.10,1.),
             (.10,.956),(.53,.74),(.78,.60),(.93,.42),(1.,0.)]
    plan_x = [(0.,0.),(.15,.20),(.42,.51),(.77,.84),(1.,1.)]
    plan_y = [(0.,1.),(.15,.943),(.42,.736),(.77,.342),(1.,0.)]
    latitudes = sorted(set([z for z,_ in profile]+[v for v,_ in sections]+[low,high]))
    def strip(y,v,s):
        t = (y-start)/(cap_start-start)
        # The aft lip still exactly matches the detachable pressure section.
        amount = t*t*(3-2*t)
        original = width_at(profile,v)
        width = original+(13.1*width_at(sections,v)-original)*amount
        bottom_z = bottom+1.68*min(1,(y-start)/(gear_end-start))
        roof_z = bottom+height-1.05*amount
        return Vector((s*width,y,bottom_z+(roof_z-bottom_z)*(v+1)*.5))
    def cap(phi,v):
        t=abs(phi)/(pi/2)
        z=strip(cap_start,v,1).z
        longitudinal=width_at(reach,v)*width_at(plan_y,t)
        return Vector(((1 if phi>=0 else -1)*13.1*width_at(sections,v)*width_at(plan_x,t),
                       cap_start+(tip-cap_start)*longitudinal,z-2.05*longitudinal))
    shell, glass, frame = Surface(), Surface(), Surface()
    for s in (-1,1):
        for v0,v1 in zip(latitudes,latitudes[1:]):
            stations=(start,(start+gear_end)/2,gear_end,66.,72.,cap_start)
            for y0,y1 in zip(stations,stations[1:]):
                if y0<gear_end and v0<gear_edge-.00001:
                    continue
                shell.face([strip(y0,v0,s),strip(y1,v0,s),strip(y1,v1,s),strip(y0,v1,s)],
                           "Skin",(s*.8,0,v0))
    limit = radians(60)
    pane_count = 23
    degrees = sorted(set([-pi/2, pi/2, -radians(82),-radians(72),radians(72),radians(82)]
                         + [-limit+2*limit*i/pane_count for i in range(pane_count+1)]))
    for a,b in zip(degrees,degrees[1:]):
        for v0,v1 in zip(latitudes,latitudes[1:]):
            middle=(a+b)/2
            direction=(sin(middle),cos(middle),(v0+v1)/2)
            corners=[cap(a,v0),cap(b,v0),cap(b,v1),cap(a,v1)]
            if abs(middle)<limit and low<=v0 and v1<=high:
                # A separate glazing surface spans an actual opening. Narrow aluminium
                # mullions and rubber seals have thickness and are visible from the seats.
                if v0 != low:
                    continue
                v1=high
                corners=[cap(a,v0),cap(b,v0),cap(b,v1),cap(a,v1)]
                margin=.047 if abs(middle)>.25 else .060
                u,w=a+(b-a)*margin,b-(b-a)*margin
                i,j=v0+(v1-v0)*.075,v1-(v1-v0)*.065
                lip=[cap(u,i),cap(w,i),cap(w,j),cap(u,j)]
                centre=sum(lip,Vector())/4
                inward=Vector((sin(middle),cos(middle),.9)).normalized()*.055
                pane=[(p-centre)*.974+centre-inward for p in lip]
                for k in range(4):
                    n=(k+1)%4
                    frame.face([corners[k],corners[n],lip[n],lip[k]],"Skin",direction)
                    frame.face([lip[k],lip[n],pane[n],pane[k]],"Dark",direction)
                glass.face(pane,"Canopy",direction)
            else:
                shell.face(corners,"Skin",direction)
    # Close the nose-wheel opening's leading sill. The doors occupy the open bottom aft of it.
    for s in (-1,1):
        p=strip(gear_end,gear_edge,s)
        q=strip(gear_end,-1,s)
        shell.face([p,q,q+Vector((0,0,.24)),p+Vector((0,0,.24))],"Mech",(0,-1,0))
    # The rear of this detachable pressure section remains visible as wreckage.
    # The original forward island owns the complementary fracture face.
    bulkhead=Surface()
    contour=[strip(start,v,1) for v in latitudes]+[strip(start,v,-1) for v in reversed(latitudes[1:-1])]
    centre=Vector((0,start+.13,-.5))
    edges=[]
    for factor,depth in ((.996,.10),(.982,.14),(.966,.30),(.947,.30),(.925,.18)):
        edges.append([Vector((p.x*factor,start+depth,centre.z+(p.z-centre.z)*factor)) for p in contour])
    for row,(outside,inside) in enumerate(zip(edges,edges[1:])):
        for k in range(len(contour)):
            j=(k+1)%len(contour)
            bulkhead.face([outside[k],outside[j],inside[j],inside[k]],"Primer" if row%2==0 else "Mech",(0,-1,0))
    for k in range(len(contour)):
        bulkhead.face([edges[-1][k],edges[-1][(k+1)%len(contour)],centre],"Primer",(0,-1,0))
    for v in (-.59,-.08,.52):
        pt=strip(start,v,1)
        extent=pt.x*.92
        bulkhead.face([(x,start+.07,pt.z+h) for x,h in
                      ((-extent,-.14),(extent,-.14),(extent,.14),(-extent,.14))],"Mech",(0,-1,0))
    bulkhead.install(child_mesh("Int_ForwardPressureBulkhead",nose))
    # In Unity the coated shell is single-sided. The pressure lining closes the
    # bridge around the glazing so the pilot sees sky only through the panes.
    lining=Surface()
    for surf in (shell,frame):
        for face in surf.faces:
            pts=[Vector(surf.points[i]) for i in reversed(face)]
            normal=(pts[1]-pts[0]).cross(pts[2]-pts[0]).normalized()
            liner_pts=[Vector((p.x*.988,p.y-.15,p.z*.986)) for p in pts]
            lining.face(liner_pts,"CabinLiner",normal)
    lining.install(child_mesh("Int_BridgeLining",nose),True)
    shell.install(nose,True)
    nose.data.set_sharp_from_angle(angle=radians(9))
    glass.install(nodes["Glass_Canopy"])
    frame.install(child_mesh("Detail_WindscreenFrames",nose))
    bpy.context.view_layer.update()

    # Seat the original fitted flight deck just behind the centre panes. Preserve
    # human dimensions after sizing the larger airframe, including the eye anchor.
    eye = cap(0,(high+low)*.5)
    pilot = nodes["pilot"]
    desired = Vector((-1.15,eye.y-3.8,eye.z-.02))
    shift = desired-pilot.matrix_world.translation
    for name,obj in nodes.items():
        if name.startswith("FlightDeck_") or name=="pilot":
            obj.location += shift
    bpy.context.view_layer.update()
    # The original transport used tall side liners under a different canopy.
    # Tuck the liners below the new sill and check furniture against the skin.
    consoles=nodes["FlightDeck_Consoles"]
    for vert in consoles.data.vertices:
        pos=consoles.matrix_world @ vert.co
        sill_z=eye.z-.95
        if pos.z>sill_z:
            vert.co.z-=.97*(pos.z-sill_z)
    # The outer operator stations sit behind the inclined side panes. Move whole
    # stations so the displays, seats and their new bezels retain their shape.
    for name in ("FlightDeck_CrewDisplays", "FlightDeck_CrewSeats"):
        nodes[name].location.y -= 3.55
    bpy.context.view_layer.update()
    for name in ("FlightDeck_Consoles","FlightDeck_Panel","FlightDeck_Floor"):
        furniture=nodes[name]
        for vert in furniture.data.vertices:
            pos=furniture.matrix_world @ vert.co
            origin=Vector((pos.x,tip+5,pos.z))
            limits=[]
            for wall in (nose,nodes["Glass_Canopy"],bpy.data.objects["Detail_WindscreenFrames"]):
                ok, point, _, _ = wall.ray_cast(origin-wall.matrix_world.translation,(0,-1,0))
                if ok: limits.append((wall.matrix_world @ point).y)
            if limits:
                vert.co.y-=max(0,pos.y-max(limits)+.28)
        furniture.data.update()
    # Match the lower cheek-mounted installations to the expanded pressure shell.
    for s,side in ((-1,"L"),(1,"R")):
        obj=nodes["GunFairing_F_"+side]
        obj.location.x += s*3.42
    for i in (5,6):
        obj=nodes["DefenceBarbette_"+str(i)]
        obj.location.z -= .95
    bpy.context.view_layer.update()
    add_cabin_details(nodes)
    # Split the actual skin on a single station to give the removable composite
    # cap a clean perimeter. The panel seam follows the same plane on both cheeks.
    import bmesh
    from finish import new_paint
    composite = new_paint("NoseComposite", (.083, .103, .119), .42)
    mesh = nose.data
    mesh.materials.append(composite)
    cut_y = tip - 4.4 - nose.matrix_world.translation.y
    editable = bmesh.new()
    editable.from_mesh(mesh)
    bmesh.ops.bisect_plane(editable, geom=[*editable.verts, *editable.edges, *editable.faces],
                           plane_co=(0, cut_y, 0), plane_no=(0, 1, 0), dist=.00001)
    for face in editable.faces:
        if face.calc_center_median().y > cut_y:
            face.material_index = len(mesh.materials) - 1
    editable.to_mesh(mesh)
    editable.free()
    mesh.update()


def add_cabin_details(nodes):
    # Project every trim vertex onto the pressure skin. The lower and upper decks
    # have different window spacing; structural frames and door bays interrupt the rows.
    doors=[29.0,49.0,72.0,87.0]
    for label,ymin,ymax in [("Fuselage_Center",3,22.42),("Fuselage_Forward",22.43,51.58),("Fuselage_Cockpit",51.59,94.0)]:
        owner=nodes[label]
        batch=Surface()
        for side in (-1,1):
            def point(y,z,lift):
                local=Vector((side*38,y,z))-owner.matrix_world.translation
                hit,p,normal,_=owner.ray_cast(local,(-side,0,0))
                if not hit:
                    raise ValueError(f"Missing pressure skin on {label} at {y:.2f}, {z:.2f}")
                return p+owner.matrix_world.translation+normal*lift
            def rounded(y,z,width,height,radius,lift):
                pts=[]
                for cy,cz,a in ((1,1,0),(-1,1,90),(-1,-1,180),(1,-1,270)):
                    for k in range(4):
                        theta=radians(a+k*30)
                        pts.append(point(y+cy*(width/2-radius)+radius*cos(theta),
                                         z+cz*(height/2-radius)+radius*sin(theta),lift))
                return pts
            def inset(y,z,w,h,r,inner,mat):
                back=rounded(y,z,w,h,r,.038)
                face=rounded(y,z,w-inner*2,h-inner*2,max(.025,r-inner),.045)
                for k in range(len(face)):
                    j=(k+1)%len(face)
                    batch.face([back[k],back[j],face[j],face[k]],"Mech",(side,0,0))
                batch.face(face,mat,(side,0,0))
            def hatch(y,z,w,h):
                # Service cover, recessed camlocks and two protected latches.
                inset(y,z,w,h,min(.14,h*.22),.028,"Radome")
                for dy in (-w*.36,w*.36):
                    for dz in (-h*.32,h*.32):
                        inset(y+dy,z+dz,.075,.075,.032,.012,"Dark")
            def louver(y,z,w,h):
                inset(y,z,w,h,.10,.062,"Mech")
                count=max(4,round(w/.17))
                for k in range(count):
                    v=y-w*.5+(k+.5)*w/count
                    lo,hi=z-h*.35,z+h*.35
                    points=[point(v-.045,lo,.065),point(v+.035,lo,.10),
                            point(v+.035,hi,.10),point(v-.045,hi,.065)]
                    batch.face(points,"Radome",(side,0,0))
            for y,z,w,h in ((12.,-4.4,2.3,.94),(36.5,-3.95,2.9,1.25),
                            (40.05,-3.95,2.45,1.25),(63.2,-2.35,1.95,.88),
                            (72.0,-4.35,1.38,.76),(45.,6.35,1.1,.48)):
                if ymin+w/2<y<ymax-w/2: hatch(y,z,w,h)
            for y,z,w,h in ((32.8,-3.9,1.9,.75),(54.1,-3.65,2.3,.86),
                            (56.8,-3.65,2.3,.86),(69.8,-4.35,1.38,.68)):
                if ymin+w/2<y<ymax-w/2: louver(y,z,w,h)
            for deck,z,spacing,begin in ((0,-2.35,2.20,5.20),(1,.73,2.43,4.05)):
                y=begin
                while y<=90:
                    if ymin+.5<y<ymax-.5 and not (9<y<16 or 34<y<40 or 75<y<79) and all(abs(y-door)>1.50 for door in doors):
                        inset(y,z,.60,.45,.11,.047,"Window")
                    y+=spacing
            for door in doors:
                if ymin+1<door<ymax-1:
                    inset(door,-.65,1.14,2.08,.16,.048,"Radome")
                    inset(door,.06,.40,.27,.066,.032,"Window")
                    # Capsule handle with a black finger recess at elbow height.
                    inset(door-.27,-.74,.27,.115,.05,.033,"Edge")
        if batch.faces:
            batch.install(child_mesh("Detail_CrewAccess_"+label,owner))
