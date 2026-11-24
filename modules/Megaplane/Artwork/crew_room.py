"""Six-station Groza pressure cabin. Local furnishing coordinates are metres, +Y forward.

The pilot pair keeps the native eye and controls. Each specialist's work surface is
dimensioned from the seat pan. A common aisle runs from the pressure door to the pilots.
"""
from math import cos, pi, sin
import bpy
import bmesh
from mathutils import Vector

from bridge_instruments import locator
from crew_graphics import ROLES, assign_print, assign_screen
from finish import new_paint
from forebody import Surface, child_mesh


FLOOR = 4.109
STATIONS = (("Nav", -1, 97.95), ("Comms", 1, 97.95),
            ("Engineering", -1, 94.72), ("Cargo", 1, 94.72))
LAMPS = (("Forward", 99.76, 6.265), ("Mid", 96.50, 6.56), ("Aft", 93.52, 6.61))
LINER, PANEL, EDGE = "CrewLiner", "CrewPanel", "DeckRail"
DARK, RUBBER = "DeckRecess", "DeckPadding"
CLOTH, BOLSTER, STRAP = "CrewUpholstery", "CrewBolster", "CrewWebbing"
CANVAS, ORANGE, PAPER = "CrewOliveCanvas", "CrewRescueWeb", "CrewChartPaper"
FLOOR_MAT, GOLD, LIGHT, IND = "CrewTread", "CrewOchre", "CrewDome_Emit", "DeckWitness_Emit"


def palette():
    for name, color, gloss in (
        (LINER,(.30,.347,.345),.24), (PANEL,(.108,.158,.166),.34),
        (CLOTH,(.102,.157,.205),.13), (BOLSTER,(.058,.092,.120),.19),
        (STRAP,(.29,.34,.30),.18), (FLOOR_MAT,(.107,.125,.128),.22),
        (GOLD,(.48,.306,.112),.32), (LIGHT,(.68,.70,.60),.40),
        (CANVAS,(.165,.191,.097),.14),(ORANGE,(.73,.214,.048),.20),
        (PAPER,(.68,.65,.48),.18)):
        mat = new_paint(name,color,gloss)
        if name == LIGHT:
            node=mat.node_tree.nodes["Principled BSDF"]
            node.inputs["Emission Color"].default_value=(*color,1.)
            node.inputs["Emission Strength"].default_value=.8


class Frame:
    """Batch shapes in the local axes of a cabin module then export them as one part."""
    def __init__(self, origin=(0,0,FLOOR), right=(1,0,0), forward=(0,1,0)):
        self.origin, self.right, self.forward = map(Vector,(origin,right,forward))
        self.surface=Surface()
        self.prints=[]

    def point(self, xyz):
        x,y,z=xyz
        return self.origin+self.right*x+self.forward*y+Vector((0,0,z))

    def face(self, points, material, normal):
        self.surface.face([self.point(p) for p in points], material,
                          self.point(normal)-self.origin)

    def rings(self,rings,material):
        centre=sum((sum((Vector(p) for p in ring),Vector())/len(ring) for ring in rings),Vector())/len(rings)
        for a,b in zip(rings,rings[1:]):
            for i in range(len(a)):
                j=(i+1)%len(a)
                pts=(a[i],a[j],b[j],b[i])
                self.face(pts,material,sum((Vector(p) for p in pts),Vector())/len(pts)-centre)
        for ring in (rings[0],rings[-1]):
            self.face(ring,material,sum((Vector(p) for p in ring),Vector())/len(ring)-centre)

    def slab(self,x,y,z,w,d,h,material,corner=.025):
        c=min(corner,w*.35,d*.35)
        self.rings([[(x+u,y+v,k) for u,v in
                    ((-w/2+c,-d/2),(w/2-c,-d/2),(w/2,-d/2+c),(w/2,d/2-c),
                     (w/2-c,d/2),(-w/2+c,d/2),(-w/2,d/2-c),(-w/2,-d/2+c))]
                    for k in (z-h/2,z+h/2)],material)

    def panel(self,x,y,z,w,h,depth,material,corner=.025):
        """Vertical cartridge; the negative local Y end faces the operator."""
        c=min(corner,w*.3,h*.3)
        self.rings([[(x+u,k,z+v) for u,v in
                   ((-w/2+c,-h/2),(w/2-c,-h/2),(w/2,-h/2+c),(w/2,h/2-c),
                    (w/2-c,h/2),(-w/2+c,h/2),(-w/2,h/2-c),(-w/2,-h/2+c))]
                   for k in (y-depth/2,y+depth/2)],material)

    def rod(self,a,b,r,material,n=8):
        a,b=Vector(a),Vector(b)
        axis=(b-a).normalized()
        u=axis.cross(Vector((0,0,1)) if abs(axis.z)<.94 else Vector((0,1,0))).normalized()
        v=axis.cross(u)
        self.rings([[p+r*(u*cos(2*pi*i/n)+v*sin(2*pi*i/n)) for i in range(n)] for p in (a,b)],material)

    def print_face(self,corners,tile,normal):
        idx=len(self.surface.faces)
        self.face(corners, PANEL, normal)
        ids=[self.surface.index[tuple(round(c,6) for c in self.point(p))] for p in corners]
        self.prints.append((idx,tile,ids))

    def finish(self,name,owner):
        obj=child_mesh(name,owner)
        self.surface.install(obj)
        for idx,tile,indices in self.prints:
            assign_print(obj,tile,idx,indices)
        return obj


def seat(f):
    # Two sliding runners carry a box pedestal and the hinged pan. The back,
    # head pad and five-point belt are visible from the aisle behind each operator.
    for x in (-.218,.218):
        f.slab(x,-.035,.022,.068,.82,.044,EDGE,.012)
        for y in (-.30,.22):
            f.slab(x,y,.065,.086,.09,.058,DARK)
    f.slab(0,-.025,.165,.38,.42,.215,PANEL,.05)
    f.slab(0,.018,.328,.53,.57,.095,EDGE,.07)
    f.slab(0,.030,.405,.52,.53,.095,CLOTH,.075)
    for x in (-.224,.224):
        f.slab(x,.01,.451,.082,.51,.078,BOLSTER,.03)
    # The back rakes six centimetres over its height. Padded shoulders grow
    # out of a moulded structural shell; the harness is anchored at its crown.
    f.rings([[(-w/2,y-.058,z),(w/2,y-.058,z),(w/2,y+.058,z),(-w/2,y+.058,z)]
             for z,y,w in ((.35,-.224,.54),(.69,-.25,.58),(.96,-.29,.53),(1.08,-.30,.36))],PANEL)
    f.panel(0,-.196,.725,.47,.52,.08,BOLSTER,.06)
    for z,w in ((.57,.37),(.68,.40),(.79,.41),(.88,.38)):
        f.panel(0,-.143-(z-.57)*.10,z,w,.093,.031,CLOTH,.024)
    f.panel(0,-.288,1.115,.35,.207,.13,CLOTH,.044)
    for sign in (-1,1):
        f.rod((sign*.253,.026,.36),(sign*.292,.035,.64),.019,EDGE)
        f.slab(sign*.292,.047,.663,.070,.36,.059,BOLSTER,.025)
        belt=[(sign*.154,-.228,1.002),(sign*.146,-.080,.77),
              (sign*.078,-.075,.49),(sign*.038,.035,.453)]
        for a,b in zip(belt,belt[1:]):
            f.face([(a[0]-.018,a[1],a[2]),(a[0]+.018,a[1],a[2]),
                    (b[0]+.018,b[1],b[2]),(b[0]-.018,b[1],b[2])],STRAP,(0,1,.3))
    f.slab(.016,.05,.459,.081,.069,.018,EDGE,.012)
    f.panel(0,-.36,.67,.36,.21,.043,BOLSTER,.025)
    f.panel(0,-.387,.757,.36,.028,.012,STRAP,.006)


def keys(f,x,y,w=.53):
    f.slab(x,y,.803,w,.173,.026,DARK,.014)
    for row in range(3):
        for col in range(9):
            f.slab(x+(col-4)*w*.095,y+(row-1)*.043,.822,w*.080,.034,.016,
                   EDGE if col==8 and row==2 else PANEL,.004)


def personal_kit(f, role):
    if role == "Comms":
        # Headset suspended from a short mushroom peg on the seat back.
        f.rod((.050,-.330,.976),(.050,-.426,.976),.022,EDGE,10)
        f.rod((.050,-.426,.976),(.050,-.426,.999),.023,EDGE,10)
        arc=[(.050+.137*cos(i*pi/16),-.423,.908+.119*sin(i*pi/16)) for i in range(17)]
        for i,(a,b) in enumerate(zip(arc,arc[1:])):
            f.rod(a,b,.021 if 3<=i<=12 else .013,BOLSTER if 3<=i<=12 else EDGE,8)
        for s in (-1,1):
            x=.050+s*.138
            f.rod((x,-.422,.912),(x,-.423,.859),.013,EDGE)
            f.panel(x,-.420,.840,.090,.138,.081,DARK,.036)
            f.panel(x,-.464,.840,.078,.111,.025,CANVAS,.032)
            f.panel(x,-.481,.841,.047,.079,.015,STRAP,.020)
        f.rod((.178,-.444,.805),(.122,-.485,.757),.007,EDGE)
        f.rod((.122,-.485,.757),(.042,-.494,.750),.013,DARK)
        lead=[(-.09,-.422,.790),(-.117,-.423,.662),(-.081,-.429,.577),
              (.040,-.424,.545),(.151,-.414,.593),(.175,-.383,.647)]
        for a,b in zip(lead,lead[1:]):
            f.rod(a,b,.007,DARK,6)
    elif role == "Cargo":
        # A lashed soft-sided deck bag: gusset, overlapping flap, two cinches,
        # pull tabs and a carry handle. Its weight sits on the existing pocket.
        f.panel(-.020,-.412,.733,.424,.357,.112,CANVAS,.057)
        f.panel(-.018,-.474,.794,.422,.174,.039,CANVAS,.052)
        f.panel(-.018,-.495,.753,.348,.014,.010,BOLSTER,.004)
        for x in (-.155,.112):
            f.panel(x,-.501,.735,.041,.298,.012,ORANGE,.007)
            f.panel(x,-.511,.690,.051,.042,.011,DARK,.007)
            f.panel(x,-.518,.691,.033,.013,.006,EDGE,.002)
        for x in (-.088,.058):
            f.rod((x,-.408,.889),(x,-.421,.958),.013,STRAP)
        f.rod((-.088,-.421,.958),(.058,-.421,.958),.015,STRAP)
        f.panel(.050,-.497,.847,.085,.026,.009,PAPER,.003)
    elif role == "Nav":
        # A folded, bound plotting wallet tucked into the back pocket.
        f.panel(-.035,-.367,.797,.237,.138,.018,CANVAS,.007)
        f.panel(-.036,-.378,.823,.210,.074,.007,PAPER,.004)
        f.panel(-.112,-.382,.829,.015,.079,.008,ORANGE,.003)


def handset(f,x,y,z):
    # Hung on a cradle with an actual coiled lead dropping to the lower rack.
    f.panel(x,y+.022,z,.095,.28,.081,PANEL)
    f.rod((x,y-.047,z-.105),(x,y-.070,z+.105),.035,DARK,10)
    for dz in (-.112,.112):
        f.panel(x,y-.065,z+dz,.112,.076,.07,RUBBER)
    prev=(x,y-.04,z-.15)
    for i in range(1,39):
        t=i/38
        pt=(x+.018*cos(t*10*pi),y-.025+.017*sin(t*10*pi),z-.15-.29*t)
        f.rod(prev,pt,.006,BOLSTER,5);prev=pt


def console(f,role):
    idx=ROLES.index(role)
    accent=(EDGE,IND,GOLD,STRAP)[idx]
    width=(1.99,1.91,2.14,2.04)[idx]
    # The leg opening is deliberately uninterrupted in the centre. Side cabinets
    # and a rear cable chase support a taper-edged worktop and a separate display.
    for x in (-1,1):
        f.slab(x*(width/2-.173),1.02,.36,.29,.61,.67,PANEL,.05)
        f.slab(x*(width/2-.173),.728,.36,.235,.035,.46,DARK,.012)
        for level in range(4):
            f.panel(x*(width/2-.173),.700,.213+.099*level,.20,.068,.025,PANEL,.012)
            f.panel(x*(width/2-.173)+.067,.681,.215+.099*level,.033,.010,.012,accent,.002)
    f.slab(0,1.03,.735,width,1.13,.080,EDGE,.095)
    f.slab(0,1.02,.773,width-.02,1.12,.047,PANEL,.09)
    f.slab(0,.479,.764,width-.26,.055,.053,RUBBER,.02)
    f.slab(0,1.35,.39,width-.45,.085,.59,LINER,.02)
    f.slab(0,1.087,.864,.24,.165,.175,EDGE,.023)
    f.panel(0,1.083,1.071,.900,.525,.105,DARK,.044)
    f.panel(0,1.036,1.071,.856,.485,.042,EDGE,.028)
    f.panel(0,1.022,1.071,.835,.457,.026,RUBBER,.014)
    f.panel(.326,.997,.822,.056,.010,.010,IND,.001)
    # The sill lip and overhang bracket the panel, leaving its 0.82 x 0.45 m face clear.
    f.slab(0,1.010,1.328,.902,.13,.025,DARK,.012)
    # An enamel identity plate is bolted onto the sidecar facing the seat.
    sign=-1 if idx in (0,2) else 1
    tagx=sign*.666
    f.panel(tagx,1.125,1.157,.376,.194,.062,PANEL,.023)
    f.print_face([(tagx-.171,1.092,1.072),(tagx+.171,1.092,1.072),
                  (tagx+.171,1.092,1.242),(tagx-.171,1.092,1.242)],idx,(0,-1,0))
    # The card sits under a keeper and is readable from the chair.
    px,py=-sign*.64,.80
    f.slab(px,py,.803,.349,.20,.012,DARK,.009)
    f.print_face([(px-.163,py-.081,.811),(px+.163,py-.081,.811),
                  (px+.163,py+.081,.811),(px-.163,py+.081,.811)],4+idx,(0,0,1))
    f.slab(px,py+.084,.818,.09,.017,.016,EDGE,.004)
    if role=="Nav":
        keys(f,.08,.693,.43)
        # A rolled route sheet is held by two spanning hoops on the chart table.
        f.rod((-.56,.935,.851),(.15,.935,.851),.045,LINER,12)
        for x in (-.51,.103):
            f.rod((x,.935,.851),(x+.021,.935,.851),.049,GOLD,12)
        f.slab(-.53,.677,.806,.22,.174,.016,EDGE,.024)
        f.rod((-.53,.68,.812),(-.53,.68,.846),.050,STRAP,12)
    elif role=="Comms":
        keys(f,-.061,.7,.47)
        handset(f,.68,1.06,.997)
        # Channel selector bank and push-to-talk paddle next to the handset.
        for j in range(4):
            x=-.83+j*.091
            f.rod((x,.765,.795),(x,.765,.849),.027,RUBBER,10)
            f.slab(x,.78,.852,.009,.023,.006,IND)
        f.slab(.43,.60,.824,.15,.082,.048,GOLD)
    elif role=="Engineering":
        for j in range(5):
            x=-.42+j*.177
            f.slab(x,.745,.813,.125,.207,.028,DARK,.017)
            for k in range(3):
                f.slab(x,.68+k*.058,.838,.074,.027,.024,accent if j==4 else EDGE,.005)
        for x in (.6,.73,.86):
            f.rod((x,.96,.797),(x,.96,.85),.037,RUBBER,10)
            f.slab(x,.978,.852,.008,.027,.005,IND)
        for j in range(3):
            f.panel(-.68,1.086,.873+j*.055,.37,.036,.036,DARK,.006)
    else:
        keys(f,-.13,.70,.43)
        for j in range(2):
            x=.28+j*.126
            f.slab(x,.706,.808,.093,.211,.024,DARK)
            f.rod((x,.70,.816),(x,.75,.895),.014,EDGE)
            f.rod((x-.029,.75,.897),(x+.029,.75,.897),.020,GOLD)
        for j in range(3):
            f.slab(.68+j*.050,.87,.819,.029,.155,.021,BOLSTER,.009)


def build_screen(role,frame,owner):
    screen=Frame(frame.origin,frame.right,frame.forward)
    # The normal points towards the seated operator, and the same orientation
    # defines the UVs in Blender and the per-instance game atlas.
    corners=[(-.41,1.004,1.071-.225),(.41,1.004,1.071-.225),
             (.41,1.004,1.071+.225),(-.41,1.004,1.071+.225)]
    screen.face(corners,DARK,(0,-1,0))
    obj=screen.finish("FlightDeck_StationScreen_"+role,owner)
    assign_screen(obj,role,corners=[screen.surface.index[tuple(round(c,6) for c in screen.point(p))] for p in corners])
    return obj

# Each cabin station defines the deck width, width at the top of the sidewall,
# width under the ceiling shoulder, and clear height above the floor.
BODY=((92.22,4.04,4.04,3.44,2.67),(93.52,4.04,4.04,3.44,2.65),
      (96.50,4.04,3.96,3.10,2.61),(97.95,4.00,3.61,2.63,2.48),
      (99.12,3.89,3.23,2.17,2.35),(99.76,3.71,2.90,1.78,2.31),
      (100.74,3.47,2.45,1.06,2.15),(101.76,3.05,1.84,.65,1.86),
      (102.54,2.65,1.30,.46,1.68))


def envelope(y):
    for a,b in zip(BODY,BODY[1:]):
        if a[0]-.001<=y<=b[0]+.001:
            t=(y-a[0])/(b[0]-a[0])
            return [p+(q-p)*t for p,q in zip(a[1:],b[1:])]
    raise ValueError(y)


def lining_section(y):
    floor,mid,top,h=envelope(y)
    return [(0,y,0),(floor,y,0),(floor,y,.25),(mid,y,1.37),
            (top,y,h-.19),(.69*top,y,h),(0,y,h)]


def interior(f):
    # The sidewalls, shoulder, headliner, floor and pressure bulkhead share all
    # perimeter vertices. The forward side openings coincide with the windshield.
    for a,b in zip(BODY,BODY[1:]):
        left,right=lining_section(a[0]),lining_section(b[0])
        for i in range(6):
            mat=(FLOOR_MAT,DARK,PANEL,LINER,LINER,LINER)[i]
            for s in (-1,1):
                if a[0]>=99.12 and i>=3:
                    # Leave the forward pilots' original glazing exposed through
                    # the full upper aperture. Their original roof closes above.
                    continue
                coords=[(s*x,y,z) for x,y,z in (left[i],left[i+1],right[i+1],right[i])]
                f.face(coords,mat,(0,0,1) if i==0 else (-s,0,-.02 if i<4 else -4))
    # Close the shell aft of the hold and underneath the pilot fascia. End caps
    # extend to the same edges as the adjoining floor and liner.
    for y in (BODY[0][0], BODY[-1][0]):
        row=lining_section(y)
        if y==BODY[0][0]:
            outline=row[1:]+[(-x,ry,z) for x,ry,z in reversed(row[1:-1])]
            for a,b in zip(outline,outline[1:]+outline[:1]):
                f.face((a,b,(0,y,1.32)),LINER,(0,1,0))
        else:
            f.face([(-row[1][0],y,0),(row[1][0],y,0),(row[2][0],y,.25),
                    (row[3][0],y,.90),(-row[3][0],y,.90),(-row[2][0],y,.25)],DARK,(0,-1,0))
    # The ceiling above the aft room ends in an upholstered header. Its front
    # face tapers upward *behind* the pilots; no lip crosses their eye line.
    y=99.12
    row=lining_section(y)
    for s in (-1,1):
        p=[(s*x,y,z) for x,_,z in row[3:]]
        for a,b in zip(p,p[1:]):
            f.face((a,b,(b[0],y,b[2]+.048),(a[0],y,a[2]+.048)),DARK,(0,1,0))
    # Longitudinal aisle tiles with true shadow gaps over a continuous floor.
    for n in range(9):
        y=92.78+n*.92
        if y>100.35: break
        f.slab(0,y,.009,1.14,.905,.016,FLOOR_MAT,.047)
        for s in (-1,1):
            f.slab(s*.604,y,.012,.025,.899,.014,EDGE,.007)
            if n in (0,3,6):
                for k in range(3):
                    f.slab(s*.513,y-.21+k*.087,.022,.065,.026,.008,GOLD,.005)
    # Full-width shallow floor service plates sit behind the working chairs.
    for s in (-1,1):
        for j in range(5):
            y=92.99+j*1.45
            f.slab(s*.99,y,.009,.69,1.37,.014,FLOOR_MAT,.045)
            f.slab(s*1.245,y+.47,.019,.048,.13,.012,DARK,.012)
    # Repeated overhead pressure frames have tapered shoulders, an insulated
    # centre panel and gasket. Hardware is concentrated near service apertures.
    for y in (92.45,93.72,95.15,96.56,97.75,98.85):
        row=lining_section(y)
        for s in (-1,1):
            for a,b in zip(row[3:],row[4:]):
                x1,_,z1=a;x2,_,z2=b
                x1*=s;x2*=s
                v=((x1,y-.035,z1-.017),(x1,y+.035,z1-.017),
                   (x2,y+.035,z2-.017),(x2,y-.035,z2-.017))
                f.face(v,PANEL,(-s,0,-1))
            # Downlights sit in a recessed perforated air-distribution panel.
            if y<98.8:
                _,_,_,h=envelope(y)
                for j in range(7):
                    f.slab(s*(.76+j*.09),y+.12,h-.034,.051,.069,.024,DARK,.012)
    # The lamp surround and its narrow diffuser are distinct from the dynamic
    # light component at the corresponding locator. Forward unit mounts to
    # the original roof through a short central fairing.
    for name,y,z in LAMPS:
        h=z-FLOOR
        f.slab(0,y,h+.082,.70,.19,.111,PANEL,.047)
        f.slab(0,y,h+.042,.57,.117,.037,DARK,.026)
        f.slab(0,y,h+.026,.50,.076,.012,LIGHT,.023)
        if name=="Forward":
            f.slab(0,y+.02,h+.188,.63,.27,.15,LINER,.049)
    # Shoulder-mounted low-level aisle guidance and deep side return skirtings.
    for s in (-1,1):
        for y in (93.15,95.92,98.83):
            floor,_,_,_=envelope(y)
            f.rod((s*(floor-.060),y-.24,.145),(s*(floor-.060),y+.24,.145),.028,EDGE)
            f.rod((s*(floor-.086),y-.13,.150),(s*(floor-.086),y+.13,.150),.013,IND)


def bulkhead():
    # Face the door from inside the room, so the positive local Y coordinate is aft.
    f=Frame((0,92.22,FLOOR),(-1,0,0),(0,-1,0))
    f.panel(0,-.028,1.043,1.397,2.08,.066,DARK,.16)
    f.panel(0,-.077,1.039,1.294,2.01,.051,EDGE,.14)
    f.panel(0,-.115,1.040,1.208,1.959,.043,PANEL,.13)
    f.panel(0,-.143,1.041,1.108,1.865,.021,LINER,.10)
    f.panel(-.02,-.167,1.56,.439,.402,.033,DARK,.08)
    f.panel(-.02,-.191,1.56,.387,.350,.021,EDGE,.06)
    f.panel(-.02,-.207,1.56,.336,.294,.021,"DeckBinnacle",.045)
    # A stainless grab rail and a two-stage dog latch.
    for x in (-.19,.19):
        f.rod((x,-.147,.94),(x,-.249,.94),.028,EDGE)
    f.rod((-.19,-.249,.94),(.19,-.249,.94),.031,EDGE)
    f.panel(.367,-.174,1.135,.114,.207,.038,RUBBER)
    f.rod((.367,-.226,1.15),(.244,-.244,1.21),.021,GOLD)
    f.panel(0,-.16,2.175,.52,.135,.04,PANEL)
    for x in (-.122,0,.122):
        f.panel(x,-.188,2.174,.086,.028,.015,IND,.006)
    for s in (-1,1):
        f.panel(s*.811,-.015,1.05,.055,2.00,.040,EDGE,.021)
        # Segmented access-panel seams run across the cabin's aft face.
        for x in (1.12,2.40,3.48):
            f.panel(s*x,-.015,1.26,.024,2.36,.028,PANEL,.008)
    # Twin service cabinets have countersunk vent fins, retaining latches and
    # a side-mounted flashlight. The equipment pack uses crossing web straps.
    for x in (-2.53,-1.65):
        f.panel(x,-.135,1.13,.78,1.55,.18,PANEL,.057)
        f.panel(x,-.244,1.17,.683,1.39,.040,LINER,.039)
        f.panel(x+.227,-.275,1.17,.089,.178,.037,DARK)
        for j in range(7):
            f.panel(x,-.271,1.50+j*.032,.50,.011,.015,PANEL,.004)
        f.panel(x,-.270,.58,.48,.062,.020,EDGE)
    f.panel(1.83,-.196,.94,.91,1.04,.28,DARK,.077)
    f.panel(1.83,-.361,.95,.82,.91,.15,"CrewBolster",.090)
    for x in (1.58,2.08):
        f.panel(x,-.446,.96,.070,.91,.03,STRAP,.01)
        f.panel(x,-.468,.95,.084,.094,.026,EDGE,.012)
    # Crew duty board mounted above the restrained bag. The route strip and
    # shift sheet are separate paper leaves under clips on the same pin board.
    f.panel(1.88,-.039,1.925,1.16,.69,.062,DARK,.033)
    f.panel(1.88,-.075,1.925,1.113,.645,.025,CANVAS,.020)
    for x in (1.435,2.325):
        for z in (1.664,2.186):
            f.rod((x,-.092,z),(x,-.105,z),.013,EDGE,8)
    for x,z,tile in ((1.66,1.959,4),(2.13,1.902,7)):
        f.panel(x-.009,-.095,z-.014,.474,.293,.009,LINER,.007)
        f.panel(x,-.105,z,.461,.278,.010,PAPER,.005)
        f.print_face([(x-.215,-.112,z-.114),(x+.215,-.112,z-.114),
                      (x+.215,-.112,z+.114),(x-.215,-.112,z+.114)],tile,(0,-1,0))
        f.panel(x,-.123,z+.129,.081,.038,.018,EDGE,.005)
    f.panel(1.88,-.100,2.211,.32,.032,.014,ORANGE,.005)
    f.rod((2.77,-.161,.48),(2.77,-.161,1.46),.124,GOLD,14)
    f.rod((2.77,-.16,1.43),(2.77,-.16,1.56),.075,PANEL,12)
    for z in (.68,1.18):
        f.rod((2.77,-.161,z-.025),(2.77,-.161,z+.025),.134,EDGE,14)
    return f


def remove_old_furnishings(nodes):
    for name in ("FlightDeck_Consoles","FlightDeck_CrewDisplays","FlightDeck_CrewSeats",
                 "FlightDeck_Floor","FlightDeck_JumpSeat","FlightDeck_Overhead","FlightDeck_Seats"):
        obj=nodes.pop(name)
        assert not obj.children, f"Unexpected original cockpit rig on {name}"
        bpy.data.objects.remove(obj,do_unlink=True)
    # Old rear display bezels were part of the same mesh as the pilot MFD surrounds.
    obj=nodes["Int_AvionicsBezels"]
    mesh=bmesh.new()
    mesh.from_mesh(obj.data)
    bmesh.ops.delete(mesh,geom=[v for v in mesh.verts if (obj.matrix_world@v.co).y<99],context="VERTS")
    mesh.to_mesh(obj.data)
    mesh.free()


def fit_crew_room(nodes):
    owner=nodes["Fuselage_Cockpit"]
    remove_old_furnishings(nodes)
    palette()
    cabin=Frame()
    interior(cabin)
    modules=[cabin.finish("FlightDeck_CabinLiner",owner),bulkhead().finish("FlightDeck_AftPressureDoor",owner)]
    for label,x in (("Pilot",-1.15),("Copilot",.92)):
        f=Frame((x,100.70,FLOOR))
        seat(f)
        modules.append(f.finish("FlightDeck_Seat_"+label,owner))
        modules.append(locator("FlightDeck_SeatMount_"+label,(x,100.725,4.725),owner))
    for role,s,y in STATIONS:
        axes=((0,-s,0),(s,0,0))
        f=Frame((s*2.336,y,FLOOR),*axes)
        seat(f)
        personal_kit(f,role)
        modules.append(f.finish("FlightDeck_Seat_"+role,owner))
        work=Frame(f.origin,*axes)
        console(work,role)
        modules.append(work.finish("FlightDeck_Station_"+role,owner))
        modules.append(build_screen(role,f,owner))
    for name,y,z in LAMPS:
        modules.append(locator("FlightDeck_Lamp"+name,(0,y,z),owner))
    nodes.update({o.name:o for o in modules})
    bpy.context.view_layer.update()
