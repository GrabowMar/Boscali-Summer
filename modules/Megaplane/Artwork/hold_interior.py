"""KY-44 cargo handling, crew access and hull service spaces. Dimensions are metres."""

from math import cos, pi, sin

import bpy
from mathutils import Vector

from finish import new_paint
from forebody import Surface, child_mesh


class Fittings(Surface):
    """Batch closed solids into the mesh of their shared damage/animation owner."""

    def loft(self, lower, upper, material):
        center = sum(lower+upper, Vector()) / (len(lower)*2)
        for ring in (lower, upper):
            self.face(ring, material, sum(ring, Vector())/len(ring)-center)
        for i, p in enumerate(lower):
            j = (i+1) % len(lower)
            face = [p, lower[j], upper[j], upper[i]]
            self.face(face, material, sum(face, Vector())/4-center)

    def box(self, center, size, material, bevel=0):
        x,y,z = center
        w,d,h = (n/2 for n in size)
        r = min(bevel, .9*w, .9*d)
        profile = [(-w,-d),(w,-d),(w,d),(-w,d)] if not r else [
            (-w+r,-d),(w-r,-d),(w,-d+r),(w,d-r),(w-r,d),(-w+r,d),(-w,d-r),(-w,-d+r)]
        self.loft(*[[Vector((x+a,y+b,z+q)) for a,b in profile] for q in (-h,h)], material)

    def rod(self, start, end, radius, material, sides=8):
        a,b = Vector(start), Vector(end)
        axis = (b-a).normalized()
        normal = axis.cross(Vector((0,0,1) if abs(axis.z)<.9 else (0,1,0))).normalized()
        tangent = axis.cross(normal)
        ring = [radius*(normal*cos(i*2*pi/sides)+tangent*sin(i*2*pi/sides)) for i in range(sides)]
        self.loft([a+t for t in ring], [b+t for t in ring], material)

    def finish(self, name, owner):
        obj = child_mesh("Detail_Bay_" + name, owner)
        self.install(obj)
        return obj


def coatings():
    for name,rgb,gloss in [
        ("BayEnamel", (.188,.238,.244), .39), ("BayIvory", (.49,.515,.488), .35),
        ("BayDeck", (.079,.095,.101), .26), ("BayOchre", (.405,.270,.085), .37),
        ("BayFrame", (.204,.226,.227), .44), ("BayLining", (.234,.242,.228), .26),
        ("BayRubber", (.025,.034,.037), .24), ("BayLamp_Lights", (.97,.78,.49), .48),
    ]:
        if bpy.data.materials.get("BS_Megaplane_"+name):
            continue
        mat = new_paint(name, rgb, gloss)
        if name.endswith("_Lights"):
            shader = mat.node_tree.nodes["Principled BSDF"]
            shader.inputs["Emission Color"].default_value = (*rgb,1)
            shader.inputs["Emission Strength"].default_value = 2


def cargo_deck(nodes):
    b = Fittings()
    # Pallet rollers rise only 7 cm above the existing floor, with tie-downs stowed flush.
    for x in (-1.68,1.68):
        for i in range(12):
            y = -45.53+i*2.43
            b.box((x,y,-3.796), (.66,2.23,.072), "BayRubber", .085)
            for dy in (-.74,0,.74):
                b.rod((x-.225,y+dy,-3.784),(x+.225,y+dy,-3.784),.044,"Edge",10)
    for x in (-4.52,4.52):
        for i in range(12):
            y=-45.1+i*2.37
            b.box((x,y,-3.795),(.32,.49,.058),"BayRubber",.085)
            # A folded D-shaped lashing eye sits inside the socket.
            for q in (-1,1):
                b.rod((x+q*.104,y-.11,-3.755),(x+q*.104,y+.045,-3.755),.018,"Edge",6)
            b.rod((x-.104,y+.045,-3.755),(x+.104,y+.045,-3.755),.019,"Edge",6)
    # Restraint lane ends and bay index marks, stencilled on the edge of the deck.
    for side in (-1,1):
        for i in range(5):
            y=-43+i*5.4
            b.box((side*6.78,y,-3.805),(.075,.86,.042),"BayOchre")
            b.box((side*6.65,y+.39,-3.805),(.27,.078,.042),"BayOchre")
    return b.finish("RollerTracks", nodes["Hold_Floor"])


def catwalk(nodes):
    b=Fittings()
    # An elevated service walk on the port side, with two breaks for crew access.
    for a,e in ((-43.7,-36.0),(-35.05,-28.40),(-27.4,-20.15)):
        mid=(a+e)/2
        b.box((-8.19,mid,-2.87),(1.24,e-a,.17),"BayDeck",.07)
        b.box((-7.60,mid,-2.755),(.074,e-a,.13),"BayOchre")
        b.box((-8.73,mid,-3.27),(.15,e-a,.75),"BayEnamel")
        for i in range(5):
            y=a+.12+i*(e-a-.24)/4
            b.rod((-7.64,y,-2.80),(-7.64,y,-1.73),.037,"Edge")
            b.rod((-8.71,y,-3.65),(-7.68,y,-2.98),.068,"Mech")
        for z in (-2.24,-1.74):
            b.rod((-7.64,a+.12,z),(-7.64,e-.12,z),.042,"BayOchre" if z>-2 else "Edge")
        # Parallel stamped drainage slots are modelled as dark flush strips.
        for i in range(round((e-a)*3)):
            y=a+.15+i*.32
            b.box((-8.15,y,-2.772),(.64,.043,.022),"BayRubber")
    for i in range(4):
        y=-44.65+i*.24
        b.box((-8.19,y,-3.69+i*.245),(1.15,.27,.10),"BayIvory")
    return b.finish("PortWalkway", nodes["Hold_Liner"])


def overhead(nodes):
    b=Fittings()
    # Intakes and the roof-mounted utilities share clamps but occupy separate runs.
    for side in (-1,1):
        for a,e in ((-53.2,-40.),(-39.85,-27.),(-26.85,-17.35)):
            y=(a+e)/2
            b.box((side*6.56,y,5.76),(.72,e-a,.42),"BayEnamel",.07)
        for x,r,coat in ((5.72,.104,"Edge"),(5.41,.077,"BayDeck")):
            b.rod((side*x,-53.1,5.88),(side*x,-17.3,5.88),r,coat,10)
        for i in range(10):
            y=-51.95+i*3.73
            b.box((side*6.53,y,5.77),(.81,.095,.49),"BayIvory")
            b.box((side*5.63,y,6.045),(.72,.11,.14),"Mech")
        for i in range(6):
            y=-48.5+i*5.55
            x=side*3.68
            b.box((x,y,6.31),(.46,1.61,.27),"Mech",.09)
            b.box((x,y,6.14),(.325,1.38,.11),"BayLamp_Lights",.07)
            for q in (-.50,0,.50):
                b.rod((x-.177,y+q,6.065),(x+.177,y+q,6.065),.021,"Mech",6)
    # Open lattice roof carriers above the vehicle envelope; no full-depth false ceiling.
    for y in (-43.4,-35.9,-28.4,-20.9):
        b.box((0,y,6.35),(16.1,.105,.28),"BayEnamel")
        b.box((0,y,6.16),(15.8,.19,.12),"Edge")
        for side in (-1,1):
            b.rod((side*7.84,y,5.1),(side*7.15,y,6.26),.095,"BayEnamel")
    return b.finish("OverheadUtilities",nodes["Hold_Frames"])


def sidewalls(nodes):
    b=Fittings()
    # Existing hoop stations are 2.25 m apart. Continue their feet to the load
    # floor, and fit shallow removable insulation cassettes between the webs.
    for side in (-1,1):
        for i in range(14):
            y=-47.25+i*2.25
            b.box((side*8.77,y,-2.12),(.27,.26,3.24),"BayFrame")
            b.box((side*8.62,y,-2.04),(.065,.38,3.03),"BayEnamel")
            if i==13:
                continue
            mid=y+1.125
            for base,top in ((-.46,1.60),(1.75,4.43)):
                # A soft crown catches light across the quilt; the stitching returns
                # to the side rail at all four edges instead of floating off the wall.
                w=.906
                strip=[(-w+.11,base),(w-.11,base),(w,base+.11),(w,top-.11),
                       (w-.11,top),(-w+.11,top),(-w,top-.11),(-w,base+.11)]
                outside=[Vector((side*8.923,mid+dy,z)) for dy,z in strip]
                inside=[Vector((side*8.78,mid+dy*.93,(z-(base+top)/2)*.95+(base+top)/2)) for dy,z in strip]
                for j,a in enumerate(outside):
                    k=(j+1)%8
                    b.face([a,outside[k],inside[k],inside[j]],"BayDeck",(-side,0,0))
                    b.face([inside[j],inside[k],Vector((side*8.72,mid,(base+top)/2))],"BayLining",(-side,0,0))
                for dy in (-.73,.73):
                    b.box((side*8.75,mid+dy,base+.11),(.045,.062,.064),"Edge")
            if i%3==1:
                # Floor-height stowed jump seats behind the port catwalk.
                x=side*8.60
                if side<0:
                    b.box((x,mid,-1.19),(.30,1.45,1.24),"BayDeck",.06)
                    for dy in (-.40,.40):
                        b.box((x+.20,mid+dy,-1.28),(.21,.64,.91),"CabinSeat",.10)
                        b.rod((x+.319,mid+dy-.16,-1.61),(x+.319,mid+dy+.16,-.94),.032,"BayOchre",6)
                else:
                    b.box((side*8.66,mid,2.69),(.25,.89,.57),"BayDeck",.065)
                    for d in range(4):
                        b.box((side*8.52,mid,2.49+d*.096),(.040,.68,.030),"Edge")
        # Local hand lamps over the service walk and the removable cabinet bank.
        for i in range(4):
            mid=-43.8+6.75*i
            b.box((side*8.42,mid,1.70),(.59,1.44,.22),"Mech",.08)
            b.box((side*8.32,mid,1.56),(.32,1.22,.085),"BayLamp_Lights",.055)
            for dy in (-.44,.44):
                b.box((side*8.32,mid+dy,1.50),(.33,.035,.032),"BayDeck")
    for name,old,new in (("Hold_Frames","Mech","BayFrame"),("Hold_Liner","CabinLiner","BayLining")):
        slots=nodes[name].data.materials
        for i,material in enumerate(slots):
            if material.name=="BS_Megaplane_"+old:
                slots[i]=bpy.data.materials["BS_Megaplane_"+new]
    return b.finish("LinerCassettes",nodes["Hold_Liner"])


def forward_bulkhead(nodes):
    b=Fittings()
    # Pressure partition between the load deck and the machinery space. It sits forward
    # of the cargo anchors. The service door and station face the open ramp.
    profile=[(-8.83,-3.82),(8.83,-3.82),(8.83,5.13),(7.62,6.48),(-7.62,6.48),(-8.83,5.13)]
    b.loft(*[[Vector((x,y,z)) for x,z in profile] for y in (-16.78,-16.56)],"BayEnamel")
    for x in (-7.35,-4.77,-.96,2.78,6.90):
        b.box((x,-16.89,1.22),(.145,.24,9.92),"BayIvory")
    b.box((0,-16.88,3.05),(17.35,.21,.17),"BayIvory")
    for x,w in ((-5.9,1.55),(-2.85,2.74),(.90,2.86),(4.8,3.1)):
        b.box((x,-16.943,.92),(w,.105,1.75),"BayFrame",.052)
        for edge in (-1,1):
            for z in (.22,1.62):
                b.box((x+edge*(w/2-.085),-17.01,z),(.08,.028,.08),"Edge")
    # Ventilated equipment plenum above the crew exit and communications terminal.
    b.box((-4.35,-17.02,4.10),(4.67,.28,1.03),"BayDeck",.09)
    for i in range(6):
        b.box((-4.35,-17.19,3.74+i*.145),(4.34,.07,.065),"BayFrame")
    # A human-size pressure door and sill on the raised walkway.
    x=-8.13
    b.box((x,-16.93,-1.49),(1.48,.15,2.65),"BayRubber",.04)
    b.box((x,-17.04,-1.49),(1.27,.13,2.47),"BayIvory",.03)
    b.box((x,-17.115,-.92),(.54,.06,.42),"Dark")
    b.box((x,-17.16,-.92),(.42,.035,.30),"Window")
    b.rod((x+.27,-17.24,-1.59),(x+.51,-17.24,-1.59),.043,"BayOchre")
    b.box((x,-17.74,-2.87),(1.26,1.91,.16),"BayDeck",.07)
    for i in range(4):
        b.box((x,-18.84-i*.24,-3.02-i*.21),(1.21,.27,.095),"BayIvory")
    # A compact wall-mounted loadmaster terminal, with a folding seat and interphone.
    x=-3.52
    b.box((x,-17.00,-2.35),(2.02,.30,2.68),"BayDeck",.08)
    b.box((x,-17.20,-1.76),(1.50,.19,.75),"BayRubber",.07)
    b.box((x,-17.31,-1.74),(1.30,.055,.55),"Window")
    for i in range(6):
        b.box((x-.47,-17.348,-1.56-i*.067),(.25+.06*(i%3),.008,.021),"BayLamp_Lights")
    for i in range(3):
        b.box((x+.27+i*.105,-17.35,-1.83),(.05,.01,.135+.05*i),"Formation_Emit")
    b.box((x,-17.34,-2.26),(1.85,.63,.09),"BayIvory",.065)
    for i in range(9):
        b.box((x-.52+i*.13,-17.46,-2.196),(.075,.085,.027),"Dark")
    b.box((x,-17.97,-3.17),(.57,.54,.15),"CabinSeat",.08)
    b.rod((x,-17.97,-3.73),(x,-17.97,-3.21),.10,"Mech")
    b.box((x,-18.14,-2.85),(.55,.15,.55),"CabinSeat",.06)
    # An original bay ID, made from inset, chamfered white stencil bars.
    def mark(x1,z1,x2,z2):
        b.rod((x1,-16.94,z1),(x2,-16.94,z2),.083,"BayIvory",4)
    for x in (3.83,4.93):
        for z in (3.72,5.13):
            mark(x+.09,z,x+.66,z)
    for x,z0,z1 in ((3.83,3.80,5.06),(4.58,3.80,5.06),(5.68,4.49,5.06),(4.93,3.80,4.36)):
        mark(x,z0,x,z1)
    mark(5.02,4.43,5.60,4.43)
    # Hinged access panels with louvres and square latches.
    for x in (.20,1.50,4.02,5.43):
        b.box((x,-17.00,-1.40),(1.14,.18,3.83),"BayIvory",.045)
        for i in range(8):
            b.box((x,-17.112,-.16-i*.077),(.76,.027,.032),"BayRubber")
        b.box((x+.39,-17.14,-1.69),(.074,.06,.34),"Mech")
    return b.finish("ForwardStation",nodes["Hold_Liner"])


def service_bays(nodes):
    b=Fittings()
    # Replaceable cabinets on the starboard lining. Equipment stands clear of x=7.45.
    for i,y in enumerate((-41.8,-39.5,-32.8,-30.5,-24.4,-22.1)):
        b.box((8.58,y,-2.58),(.68,1.93,2.40),"BayDeck",.10)
        b.box((8.20,y,-2.56),(.105,1.73,2.15),"BayEnamel",.04)
        for j in range(8):
            b.box((8.13,y,-1.78-j*.069),(.034,1.09,.033),"BayRubber")
        for dy in (-.66,.66):
            b.rod((8.11,y+dy,-2.81),(8.11,y+dy,-2.55),.034,"Edge")
        b.box((8.13,y+.39,-2.28),(.025,.24,.075),"BayOchre" if i%3==0 else "BayIvory")
    # An exposed pump-and-accumulator rack midway along the starboard wall.
    for i in range(2):
        y=-36.70+i*1.25
        b.box((8.54,y,-3.54),(.78,1.05,.37),"Mech",.06)
        b.rod((8.46,y,-3.32),(8.46,y,-1.32),.30,"BayEnamel",14)
        for z in (-3.07,-2.02):
            b.rod((8.46,y,z),(8.46,y,z+.085),.33,"Edge",14)
        b.rod((8.46,y,-1.32),(8.46,y,-1.12),.115,"BayOchre",8)
        b.rod((8.46,y,-1.12),(8.46,y+.35,-.85),.068,"BayRubber")
    b.rod((8.46,-38.1,-.85),(8.46,-28.9,-.85),.078,"Mech")
    for y in (-37.9,-34.0,-30.4):
        b.rod((8.46,y,-.85),(8.79,y,.42),.065,"BayEnamel")
    return b.finish("StarboardService",nodes["Hold_Liner"])


def split_space(nodes):
    # Parts visible through a lost upper shell or forward damage island. The two
    # cable galleries end on their own side of the structural break.
    art=[]
    for owner, start, end in (("Fuselage_Center",.9,21.2),("Fuselage_Forward",22.8,34.4)):
        b=Fittings()
        for side in (-1,1):
            x=side*7.96
            b.box((x,(start+end)/2,-2.08),(.85,end-start,.22),"BayDeck",.06)
            for i in range(int((end-start)/2.2)):
                y=start+1.28+i*2.2
                b.box((x,y,-.80),(.75,1.65,2.36),"BayEnamel",.12)
                b.box((x-side*.39,y,-.8),(.065,1.42,2.1),"BayIvory",.04)
                for k in (-.60,.60):
                    b.box((x-side*.43,y+k,-.55),(.045,.08,.31),"Mech")
                b.rod((x,y+.55,.37),(x,y+.55,1.44),.073,"Mech")
            for dz in (0,.36):
                b.rod((x,start,1.45+dz),(x,end,1.45+dz),.12,"Edge",10)
        art.append(b.finish(owner+"_Galleries",nodes[owner]))
    return art


def aft_services(nodes):
    b=Fittings()
    # The pressure liner is a distinct shell. Paired air-handling units sit in
    # the attic between its 6.6 m crown and the outer back at 11.3–12.3 m.
    # Keep the central belly-door well open beneath the deck.
    for side in (-1,1):
        x=side*4.65
        b.box((x,-34.0,6.95),(3.0,24.1,.22),"BayFrame",.10)
        b.box((x+side*.68,-34,9.55),(.76,24.1,.63),"BayEnamel",.08)
        for i in range(4):
            y=-43.0+i*5.95
            b.box((x,y,7.99),(2.46,4.04,1.80),"BayEnamel",.18)
            b.box((x,y,8.96),(2.58,4.18,.18),"BayFrame",.10)
            # Reinforced removable filter trays, visible from a detached roof.
            for j in range(7):
                b.box((x,y-1.52+j*.48,9.095),(1.80,.24,.12),"BayDeck",.03)
            b.rod((x-side*.97,y,8.06),(x-side*2.05,y+.71,8.06),.26,"Mech",10)
            b.rod((x+side*.68,y+1.54,8.91),(x+side*.68,y+2.15,9.49),.21,"Edge",10)
        # The auxiliary racks and cross-braced floor supports flank the cargo door.
        x=side*7.9
        b.box((x,-33.9,-6.83),(1.54,23.1,.26),"BayFrame",.08)
        for i in range(5):
            y=-44.3+i*4.98
            b.box((x,y,-5.66),(1.18,3.19,1.98),"BayDeck",.15)
            b.box((x-side*.61,y,-5.59),(.075,2.85,1.53),"BayEnamel")
            for dy in (-1.08,0,1.08):
                b.box((x-side*.66,y+dy,-5.59),(.06,.087,1.21),"Edge")
            b.rod((x-side*.52,y+1.72,-6.72),(x+side*.52,y+2.43,-4.23),.10,"BayFrame")
    return b.finish("AftPlant", nodes["Fuselage_Aft"])


def work_lights(nodes):
    owner=nodes["Hold_Frames"]
    lights=[]
    # The lamp object also supplies the position of its matching URP spotlight.
    for i,(x,y) in enumerate(((-3.68,-42.95),(3.68,-37.40),(-3.68,-26.30),(3.68,-20.75))):
        name=f"Detail_Bay_WorkLight_{i}"
        lamp=bpy.data.lights.new(name,"AREA")
        lamp.shape="RECTANGLE"
        lamp.size,lamp.size_y=.32,1.25
        lamp.energy=1050
        lamp.color=(1,.85,.72)
        obj=bpy.data.objects.new(name,lamp)
        owner.users_collection[0].objects.link(obj)
        obj.parent=owner
        obj.location=Vector((x,y,6.015))-owner.matrix_world.translation
        lights.append(obj)
    return lights


def outfit_hold(nodes):
    coatings()
    art=[cargo_deck(nodes),catwalk(nodes),overhead(nodes),sidewalls(nodes),forward_bulkhead(nodes),
         service_bays(nodes),aft_services(nodes)]
    art.extend(split_space(nodes))
    art.extend(work_lights(nodes))
    nodes.update((obj.name,obj) for obj in art)
