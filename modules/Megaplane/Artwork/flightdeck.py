"""Machined screen surrounds and operator controls for the cockpit display atlas."""
import bmesh
from mathutils import Vector
from forebody import Surface, child_mesh


def seat_lower_displays(nodes):
    """Fit the seven upright MFDs in front of the lower fascia and behind the yokes."""
    from bridge_instruments import AFT, DARK, SKIN, join_rings, rectangle

    panel=nodes["FlightDeck_Panel"]
    dark=panel.data.materials.find("BS_Megaplane_Dark")
    editable=bmesh.new()
    editable.from_mesh(panel.data)
    # These are the old rectangular mounting rails. Their upper edges cross the compass
    # cues and extend through the new faceplates. The sloping centre pedestal is separate.
    rails=[face for face in editable.faces if face.material_index==dark and
           all(101.51 < (panel.matrix_world@v.co).y < 101.58 for v in face.verts)]
    assert rails, "The original lower screen rails are missing"
    bmesh.ops.delete(editable,geom=rails,context="FACES")
    editable.to_mesh(panel.data)
    editable.free()

    # The side windows narrow at the windscreen. Bring the outboard cassettes
    # inboard of the coaming cheek, and give the centre map its own bay left of the pedestal.
    glass=nodes["FlightDeck_Displays"]
    centres=set()
    for polygon in glass.data.polygons:
        if not glass.data.materials[polygon.material_index].name.endswith("Display_Emit"):
            continue
        points=[glass.matrix_world@glass.data.vertices[i].co for i in polygon.vertices]
        if min(p.y for p in points)<101.50:
            continue
        centres.update(round(p.x,4) for p in points)
    edges=sorted(centres)
    assert len(edges)==14, "Expected seven independent lower screen apertures"
    old_x=[(left+right)/2 for left,right in zip(edges[::2],edges[1::2])]
    new_x=(-2.095,-1.639,-1.15,-.52,.92,1.409,1.865)
    depth_shift=101.295-101.525
    for obj in (glass,nodes["Int_AvionicsBezels"]):
        inverse=obj.matrix_world.inverted()
        for vertex in obj.data.vertices:
            p=obj.matrix_world@vertex.co
            if p.y<101.50:
                continue
            i=min(range(len(old_x)),key=lambda k:abs(old_x[k]-p.x))
            vertex.co=inverse@(p+Vector((new_x[i]-old_x[i],depth_shift,.070)))
        obj.data.update()

    cases=Surface()
    for x in new_x:
        centre=Vector((x,101.412,4.737))
        lip=rectangle(x,101.301,4.737,.441,.436,.015)
        side=rectangle(x,101.323,4.737,.453,.446,.018)
        back=rectangle(x,101.584,4.717,.420,.412,.018)
        cases.face(lip,DARK,AFT)
        join_rings(cases,lip,side,SKIN,centre)
        join_rings(cases,side,back,SKIN,centre)
        cases.face(back,DARK,(0,1,0))
    obj=child_mesh("FlightDeck_LowerDisplayCases",nodes["Fuselage_Cockpit"])
    cases.install(obj)
    nodes[obj.name]=obj


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
        bezel=child_mesh("Int_AvionicsBezels",nodes["Fuselage_Cockpit"])
        trim.install(bezel)
        nodes[bezel.name]=bezel
