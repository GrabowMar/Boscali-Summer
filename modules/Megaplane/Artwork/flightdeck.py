"""Machined screen surrounds and operator controls for the cockpit display atlas."""
from math import cos, pi, sin

from mathutils import Vector
from bridge_instruments import (AFT, DARK, RUBBER, SKIN, TRIM, WITNESS, bolt,
                                finished_quad, install_finished, join_rings, rectangle,
                                rounded_rectangle)
from forebody import Surface, child_mesh


def profile_box(surface, left, right, profile, skin=SKIN, bevel=0):
    middle = (left + right) / 2
    centre = sum((Vector((middle,y,z)) for y,z in profile), Vector()) / len(profile)
    if bevel:
        shrink = min(.16, bevel / max((Vector((middle,y,z)) - centre).length for y,z in profile))
        rings = [(left, shrink), (left+bevel, 0), (right-bevel, 0), (right, shrink)]
    else:
        rings = [(left, 0), (right, 0)]
    profiles = [[Vector((x, y+(centre.y-y)*shrink, z+(centre.z-z)*shrink)) for y,z in profile]
                for x,shrink in rings]
    for first,second in zip(profiles, profiles[1:]):
        join_rings(surface, first, second, skin, centre)
    surface.face(profiles[0], skin, (-1, 0, 0))
    surface.face(profiles[-1], skin, (1, 0, 0))


def telltale(surface, x, y, z):
    surface.face(rectangle(x, y, z, .021, .025, .004), DARK, AFT)
    surface.face(rectangle(x, y-.001, z, .013, .017, .003), WITNESS, AFT)


def flight_yokes(nodes):
    """Short steering columns with swept padded handles and cast hub covers."""
    controls, finish, smooth_faces = Surface(), [], []
    for x in (-1.15, .92):
        profile_box(controls, x-.062, x+.062, ((101.31, 4.12), (101.27, 4.44), (101.067, 4.64),
                   (101.085, 4.70), (101.374, 4.51), (101.414, 4.12)))
        hub_front = rounded_rectangle(x, 100.97, 4.661, .158, .158, .022)
        hub_lip = rounded_rectangle(x, 100.985, 4.661, .176, .169, .024)
        hub_back = rounded_rectangle(x, 101.067, 4.661, .186, .182, .026)
        centre = Vector((x, 101.021, 4.661))
        join_rings(controls, hub_front, hub_lip, TRIM, centre)
        join_rings(controls, hub_lip, hub_back, SKIN, centre)
        controls.face(hub_front, TRIM, AFT)
        controls.face(hub_back, SKIN, (0, 1, 0))
        finished_quad(controls,finish,"charcoal",[(x+u,100.968,4.661+v) for u,v in
                     ((-.064,-.060),(.064,-.060),(.064,.060),(-.064,.060))],AFT)
        for side in (-1, 1):
            shape = ((.068,101.015,4.632,.021), (.182,101.002,4.676,.019),
                     (.201,100.995,4.710,.023),
                     (.207,100.984,4.744,.029), (.201,100.981,4.776,.026),
                     (.203,100.982,4.808,.030), (.204,100.987,4.823,.023))
            path = [Vector((x+side*dx, y, z)) for dx,y,z,_ in shape]
            rings = []
            for i, point in enumerate(path):
                tangent = (path[min(i+1, len(path)-1)] - path[max(i-1, 0)]).normalized()
                across = tangent.cross(Vector((0, 1, 0))).normalized()
                forward = across.cross(tangent).normalized()
                radius = shape[i][3]
                rings.append([point + radius * (forward*cos(k*pi/4) + across*sin(k*pi/4)*.86)
                              for k in range(8)])
            first_face = len(controls.faces)
            for i, (a,b) in enumerate(zip(rings, rings[1:])):
                join_rings(controls, a, b, RUBBER if i >= 2 else TRIM, (path[i]+path[i+1])*.5)
            smooth_faces.extend(range(first_face, len(controls.faces)))
            controls.face(rings[-1], RUBBER, (0, 0, 1))
            for z in (4.61, 4.711):
                bolt(controls, x+side*.052, 100.967, z, .0055)
            cx, button_y, button_z = x+side*.201, 100.949, 4.785
            seat = rectangle(cx,button_y+.006,button_z,.027,.026,.007)
            collar = rectangle(cx,button_y,button_z,.023,.022,.006)
            join_rings(controls, seat, collar, DARK, Vector((cx,button_y+.004,button_z)))
            controls.face(collar,DARK,AFT)
            controls.face(rectangle(cx,button_y-.002,button_z,.014,.013,.003),
                          WITNESS if side == -1 else SKIN,AFT)
    install_finished(controls, nodes["FlightDeck_Yokes"], finish, smooth_faces)


def deck_quad(x, y, z, width, depth, rake):
    return [Vector((x+u, y+v, z+v*rake)) for u,v in ((-width/2,-depth/2),(width/2,-depth/2),
                                                     (width/2,depth/2),(-width/2,depth/2))]


def switch_module(surface, finish, x, y, z, width, depth, rake, tile, count):
    normal = (0,-rake,1)
    bezel = deck_quad(x,y,z,width,depth,rake)
    border = deck_quad(x,y,z-.019,width+.032,depth+.032,rake)
    join_rings(surface, bezel, border, TRIM, Vector((x,y,z-.019)))
    finished_quad(surface, finish, tile, bezel, normal)
    pitch = width/(count+.65)
    for i in range(count):
        cx = x+(i-(count-1)/2)*pitch
        for row in (-1, 1):
            cy = y+row*depth*.22
            cz = z+(cy-y)*rake
            live = row == 1 and i%3 == 0
            bed = deck_quad(cx,cy,cz+.002,pitch*.61,depth*.24,rake)
            cap = deck_quad(cx,cy-.003,cz+.015,pitch*.41,depth*.16,rake-.31*row)
            base = Vector((cx,cy,cz-.003))
            surface.face(bed,DARK,normal)
            join_rings(surface, bed, cap, RUBBER, base)
            surface.face(cap,SKIN if live else TRIM,normal)
            if live:
                mark = deck_quad(cx,cy-.003,cz+.0156,pitch*.30,depth*.039,rake-.31*row)
                surface.face(mark,WITNESS,normal)
            if tile == "breakers" and count == 3 and row == -1 and i == 0:
                for side in (-1, 1):
                    guard_x = cx + side*pitch*.42
                    profile_box(surface,guard_x-.0036,guard_x+.0036,
                                ((cy-depth*.135,cz+.002-depth*.135*rake),
                                 (cy-depth*.075,cz+.026-depth*.075*rake),
                                 (cy+depth*.075,cz+.026+depth*.075*rake),
                                 (cy+depth*.135,cz+.002+depth*.135*rake)), TRIM)


def pedestal(nodes):
    """Shared throttle quadrant and trim controls over a short floor-mounted spine."""
    x = -.115
    surface, finish = Surface(), []
    profile_box(surface,x-.171,x+.171,((100.05,4.122),(100.06,4.444),(100.15,4.514),
                (100.62,4.588),(100.96,4.713),(101.094,4.595),(101.114,4.128)),DARK)
    profile_box(surface,x-.150,x+.150,((100.12,4.515),(100.633,4.603),(100.955,4.735),
                (101.068,4.643),(100.94,4.697),(100.61,4.575),(100.14,4.479)))
    switch_module(surface, finish,x,100.285,4.557,.273,.198,.167,"comms_strip",4)
    switch_module(surface, finish,x,100.479,4.589,.273,.158,.167,"power",4)
    # Visible black slots bound the four paired throttles. Each handle has its
    # own lever mesh, giving the bright edges depth in the pilot's low view.
    for i in range(4):
        cx = x+(i-1.5)*.064
        surface.face(deck_quad(cx,100.766,4.668,.038,.209,.402),DARK,(0,-.402,1))
        profile_box(surface,cx-.011,cx+.011,((100.843,4.690),(100.766,4.766),(100.744,4.768),
                    (100.756,4.790),(100.786,4.784),(100.878,4.697)),TRIM)
        profile_box(surface,cx-.028,cx+.028,((100.737,4.771),(100.728,4.793),(100.747,4.810),
                    (100.790,4.805),(100.802,4.786),(100.782,4.767)),RUBBER,bevel=.005)
        surface.face(deck_quad(cx,100.759,4.811,.039,.010,0),TRIM,(0,0,1))
    install_finished(surface,nodes["FlightDeck_Pedestal"],finish)


def fit_pilot_fascia(nodes):
    """Place the original instruments in two knee-clear banks and a centre pedestal."""
    displays = nodes["FlightDeck_Displays"]
    centres = set()
    for face in displays.data.polygons:
        if displays.data.materials[face.material_index].name.endswith("Display_Emit"):
            points = [displays.matrix_world @ displays.data.vertices[i].co for i in face.vertices]
            if min(p.y for p in points) > 101.50:
                centres.update(round(p.x, 4) for p in points)
    edges = sorted(centres)
    assert len(edges) == 14, "Expected seven upright pilot and copilot instruments"
    old_x = [(left + right) / 2 for left, right in zip(edges[::2], edges[1::2])]
    new_x = (-2.05, -1.60, -1.15, -.59, .92, 1.37, 1.82)
    face_y, face_z = 101.30, 4.735
    for obj in (displays, nodes["Int_AvionicsBezels"]):
        inverse = obj.matrix_world.inverted()
        for vertex in obj.data.vertices:
            p = obj.matrix_world @ vertex.co
            if p.y > 101.47:
                i = min(range(len(old_x)), key=lambda k: abs(old_x[k] - p.x))
                p.x = new_x[i] + (p.x - old_x[i]) * .90
                p.y += face_y - 101.5251
                p.z += face_z - 4.6676
                vertex.co = inverse @ p
        obj.data.update()

    sculpt_flightdeck(nodes)


def sculpt_flightdeck(nodes):
    new_x = (-2.05, -1.60, -1.15, -.59, .92, 1.37, 1.82)
    face_y, face_z = 101.30, 4.735
    flight_yokes(nodes)
    pedestal(nodes)
    fascia, finish = Surface(), []
    for x in new_x:
        centre = Vector((x, 101.412, face_z))
        lip = rounded_rectangle(x, face_y + .006, face_z, .408, .433, .022)
        shoulder = rounded_rectangle(x, face_y + .031, face_z, .419, .444, .028)
        back = rounded_rectangle(x, 101.51, face_z - .016, .386, .411, .026)
        fascia.face(lip, DARK, AFT)
        join_rings(fascia, lip, shoulder, TRIM, centre)
        join_rings(fascia, shoulder, back, SKIN, centre)
        fascia.face(back, SKIN, (0, 1, 0))
        for z in (face_z+.102, face_z+.073):
            telltale(fascia, x+.187, face_y-.022, z)
        for u in (-.191,.191):
            for v in (-.195,.195):
                bolt(fascia,x+u,face_y-.007,face_z+v,.0045)

    # The bevelled apron sits behind the cassettes and well forward of both seats.
    # It supports the exposed lower display cases without filling either knee well.
    for left, right in ((-2.27, -.365), (.70, 2.04)):
        profile_box(fascia, left, right, ((101.37, 4.43), (101.38, 4.51), (101.415, 4.85),
                    (101.49, 4.972), (101.755, 4.918), (101.78, 4.56), (101.59, 4.426)),DARK)
        # The joining deck angles back towards the upper live instruments and
        # leaves the lower cartridges projecting only by their slim bezels.
        profile_box(fascia,left+.028,right-.028,((101.464,4.921),(101.495,4.982),
                    (102.002,4.941),(101.99,4.904),(101.53,4.899)))
        segment = (right-left)/3
        for i in range(3):
            x = left+(i+.5)*segment
            finished_quad(fascia,finish,"charcoal",deck_quad(x,101.742,4.964,segment-.032,.471,-.081),(0,.081,1))
            switch_module(fascia,finish,x,101.77,4.969,min(.42,segment-.035),.183,-.081,
                          ("power","breakers","thermal")[i],3)

    profile_box(fascia,-.341,.676,((101.58,4.824),(101.59,4.925),(101.984,4.915),
                (102.005,4.852),(101.763,4.800)),DARK)
    switch_module(fascia,finish,.272,101.76,4.934,.627,.169,-.025,"comms_strip",5)

    # Stepped switch boxes lie outboard of both knee wells with the controls on the tilted deck.
    for x in (-1.88, 1.65):
        profile_box(fascia, x-.182, x+.182, ((100.66,4.124),(100.65,4.390),(100.73,4.433),
                    (100.85,4.462),(101.308,4.556),(101.49,4.445),(101.469,4.124)),DARK)
        profile_box(fascia, x-.169, x+.169, ((100.733,4.438),(100.773,4.465),(101.294,4.570),
                    (101.45,4.466),(101.299,4.543),(100.806,4.447)))
        switch_module(fascia,finish,x,100.894,4.497,.282,.206,.202,"breakers",4)
        switch_module(fascia,finish,x,101.125,4.543,.282,.216,.202,"power",4)

    # The remaining slanted display stays at its authored angle and atlas coordinates.
    # Give it a separate pedestal with a floor mount and an uninterrupted apron at each seat.
    profile_box(fascia, -.295, .065, ((101.106, 4.60), (101.387, 4.783), (101.575, 4.783),
                (101.639, 4.68), (101.619, 4.12), (101.25, 4.12), (101.18, 4.26)))
    for z in (4.375, 4.418, 4.461, 4.504):
        y = 101.18-(z-4.26)*.2176
        fascia.face(rectangle(-.115, y-.007, z, .203, .014, .007), DARK, AFT)
    install_finished(fascia,nodes["FlightDeck_Panel"],finish)


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
        bezel = child_mesh("Int_AvionicsBezels", nodes["Fuselage_Cockpit"])
        trim.install(bezel)
        nodes[bezel.name] = bezel
