"""Machined screen surrounds and operator controls for the cockpit display atlas."""
from mathutils import Vector
from forebody import Surface, child_mesh


def frame_displays(nodes):
    trim = Surface()
    for name in ("FlightDeck_Displays", "FlightDeck_CrewDisplays"):
        obj = nodes[name]
        faces = [p for p in obj.data.polygons
                 if obj.data.materials[p.material_index].name.endswith("Display_Emit")]
        for first,second in zip(faces[::2],faces[1::2]):
            corners = {tuple(obj.matrix_world @ obj.data.vertices[i].co) for p in (first,second) for i in p.vertices}
            if len(corners)!=4:
                continue
            points = list(map(Vector,corners))
            centre = sum(points,Vector())/4
            normal = first.normal.copy()
            across = Vector((0,0,1)).cross(normal).normalized()
            up = normal.cross(across).normalized()
            w=max(abs((p-centre).dot(across)) for p in points)
            h=max(abs((p-centre).dot(up)) for p in points)
            if h < .085 or w < .085:
                continue
            def plane(x,y,z):
                return centre+across*x+up*y+normal*z
            def ring(width,height,depth,bevel):
                return [plane(x,y,depth) for x,y in (
                    (-width+bevel,-height),(width-bevel,-height),(width,-height+bevel),
                    (width,height-bevel),(width-bevel,height),(-width+bevel,height),
                    (-width,height-bevel),(-width,-height+bevel))]
            inner=ring(w+.001,h+.001,.001,.004)
            outer=ring(w+.042,h+.036,.012,.014)
            for i in range(8):
                j=(i+1)%8
                trim.face([outer[i],outer[j],inner[j],inner[i]],"Dark",normal)
            for side in (-1,1):
                for k in range(5):
                    x=side*(w+.024)
                    y=(k-2)*h*.35
                    trim.face([plane(x+dx,y+dy,.016) for dx,dy in
                              ((-.009,-.008),(.009,-.008),(.009,.008),(-.009,.008))],"Mech",normal)
                    if k==0:
                        trim.face([plane(x+dx,y+dy,.017) for dx,dy in
                                  ((-.004,-.001),(.004,-.001),(.004,.001),(-.004,.001))],"Edge",normal)
    if trim.faces:
        trim.install(child_mesh("Int_AvionicsBezels",nodes["Fuselage_Cockpit"]))
