"""External structure of the KY-44, installed on the original damage and animation nodes."""
from math import cos, pi, radians, sin

import bpy
from mathutils import Vector


TAU = 2 * pi


class MeshBuilder:
    def __init__(self, origin=(0, 0, 0), scale=1):
        self.origin = Vector(origin)
        self.scale = scale
        self.vertices = []
        self.faces = []
        self.face_data = []

    def vertex(self, xyz):
        self.vertices.append(tuple(self.origin + Vector(xyz) * self.scale))
        return len(self.vertices) - 1

    def face(self, points, material, smooth=False, uv=None):
        points = tuple(points)
        self.faces.append(points)
        if uv is None:
            # Metre-based projection for panel skins; smooth turned parts supply cylindrical UVs.
            coords = [Vector(self.vertices[i]) for i in points]
            normal = (coords[1] - coords[0]).cross(coords[2] - coords[0])
            omit = max(range(3), key=lambda k: abs(normal[k]))
            u, v = [k for k in range(3) if k != omit]
            uv = [(p[u] / 16, p[v] / 16) for p in coords]
        self.face_data.append((material, smooth, uv))

    def install(self, obj):
        mesh = bpy.data.meshes.new(obj.name + '_Surface')
        mesh.from_pydata(self.vertices, [], self.faces)
        mesh.update()
        slots = {}
        unwrap = mesh.uv_layers.new(name='UVMap')
        for poly, (material, smooth, uvs) in zip(mesh.polygons, self.face_data):
            if material not in slots:
                slots[material] = len(slots)
                mesh.materials.append(bpy.data.materials['BS_Megaplane_' + material])
            poly.material_index = slots[material]
            poly.use_smooth = smooth
            for loop, uv in zip(poly.loop_indices, uvs):
                unwrap.uv[loop].vector = uv
        mesh.set_sharp_from_angle(angle=radians(26))
        old_mesh = obj.data
        obj.data = mesh
        if old_mesh and old_mesh.users == 0:
            bpy.data.meshes.remove(old_mesh)

    def turn(self, profile, segments, material_pattern=None, envelope=None):
        """The material at each (axial station, radius) covers the strip to the next station."""
        rings = []
        for index, (y, radius, _) in enumerate(profile):
            ring = []
            for j in range(segments):
                angle = TAU*j/segments
                r = radius * (envelope(index,angle) if envelope else 1.)
                ring.append(self.vertex((r*cos(angle),y,r*sin(angle))))
            rings.append(ring)
        for row, (front, back) in enumerate(zip(rings, rings[1:])):
            y, r, material = profile[row]
            y2 = profile[row + 1][0]
            repeat = TAU * r * self.scale / 16
            for j in range(segments):
                k = (j + 1) % segments
                u = repeat * j / segments
                u2 = repeat * (j + 1) / segments
                v, v2 = y * self.scale / 16, y2 * self.scale / 16
                mat = material_pattern(row, j, material) if material_pattern else material
                self.face((front[j], front[k], back[k], back[j]), mat, True,
                          ((u,v),(u2,v),(u2,v2),(u,v2)))


COWL = [
    # axial station, nominal radius, fraction of chine profile, material of following strip
    (-.47,.870,0,'Dark'),(-.10,.889,0,'Dark'),(.18,.898,0,'Dark'),(.37,.907,0,'Mech'),
    (.48,.925,0,'Edge'),(.525,.953,0,'Edge'),(.526,.977,.08,'Edge'),
    (.49,.990,.46,'Skin'),(.39,1.004,.85,'Skin'),(.20,1.028,1,'Skin'),
    (.02,1.038,1,'Skin'),(-.035,1.038,1,'Dark'),(-.05,1.034,1,'Skin'),
    (-.066,1.038,1,'Skin'),(-.68,1.038,1,'Skin'),(-1.47,.978,1,'Skin'),
    (-1.596,.966,1,'Dark'),(-1.610,.962,1,'Skin'),(-1.624,.966,1,'Skin'),
    (-1.90,.942,1,'Skin'),(-2.16,.902,.95,'Skin'),(-2.34,.850,.72,'Edge'),
    (-2.37,.842,.64,'Edge'),(-2.37,.811,.15,'Mech'),(-2.28,.805,0,'Dark'),
    (-1.985,.776,0,'Dark'),(-1.90,.721,0,'Mech'),(-1.90,.482,0,'Mech'),
]


def chine(angle):
    # Support planes of a chamfered rectangular enclosure. The broad shoulders
    # frame a round bore and provide mounting faces for the service panels.
    x,z = abs(cos(angle)),sin(angle)
    return min(1.075/max(x,1e-6),1.045/max(z,1e-6),.985/max(-z,1e-6),
               1.48/max(.78*x+.96*z,1e-6),1.43/max(.80*x-.98*z,1e-6))


def outer_radius(y,angle):
    for a,b in zip(COWL[9:21],COWL[10:22]):
        if b[0] <= y <= a[0]:
            f = (y-a[0])/(b[0]-a[0])
            radius = a[1]+(b[1]-a[1])*f
            weight = a[2]+(b[2]-a[2])*f
            return radius*(1+weight*(chine(angle)-1))
    raise ValueError(f'Cowl fitting outside panel run at {y}')


def cowling(build, segments):
    def panels(row,j,mat):
        a = TAU*(j+.5)/segments
        if row in (13,14,15) and -.36 < sin(a) < .40:
            return 'Deice'
        if row in (18,19) and -.57 < sin(a) < -.28:
            return 'Deice'
        return mat
    build.turn([(y,r,m) for y,r,_,m in COWL],segments,panels,
               lambda row,a: 1+COWL[row][2]*(chine(a)-1))


def mount_pylon(build, height, small):
    # Sections are airfoils. The aft saddle rises and sweeps into the underside of the wing.
    stations = [
        (.935,.02,-2.56,.52), (1.045,-.12,-2.67,.43),
        (1.145,-.33,-2.81,.236),
        (height - .17, -1.15 if small else -.70, -5.48 if small else -3.82, .130),
        (height, -1.22 if small else -.79, -5.39 if small else -3.76, .096),
    ]
    rows = []
    for z, fore, aft, width in stations:
        flanks = ((0,0),(.040,.51),(.16,.91),(.34,1),(.61,.76),(.84,.39),
                  (1,0),(.84,-.39),(.61,-.76),(.34,-1),(.16,-.91),(.040,-.51))
        rows.append([build.vertex((width * t, fore + (aft - fore) * f, z)) for f,t in flanks])
    for a,b in zip(rows,rows[1:]):
        for i in range(len(a)):
            j = (i + 1) % len(a)
            build.face((a[j],a[i],b[i],b[j]), 'Skin')
    build.face(rows[0], 'Skin')
    build.face(reversed(rows[-1]), 'Skin')


def blades(build, count):
    # One closed, cambered blade per sector. Section stagger and sweep both vary with span.
    # The shroud stays parked on the pod. Only the rotor and spiral turn on the original axis.
    blade_section = ((0,0),(.09,.04),(.41,.073),(.79,.049),(1,0),(.73,.008),(.32,.012),(.07,.005))
    for blade in range(count):
        angle = TAU * blade / count
        rows = []
        for radius, sweep, chord, stagger in (
            (.208,-.15,.48,.18), (.35,-.19,.44,.15), (.55,-.12,.355,.115),
            (.76,.00,.293,.083), (.878,.105,.263,.057),
        ):
            section = []
            for fraction,camber in blade_section:
                a = angle + sweep + fraction * chord
                y = (.47 - fraction) * stagger + camber - .085 * radius
                section.append(build.vertex((radius*cos(a),y,radius*sin(a))))
            rows.append(section)
        for a,b in zip(rows,rows[1:]):
            for i in range(len(a)):
                j = (i+1) % len(a)
                build.face((a[i],a[j],b[j],b[i]), 'Fan' if i < 4 else 'Mech', True)
        build.face(rows[-1], 'Fan')

    dome = [(.405,.006),(.40,.039),(.375,.086),(.315,.143),(.21,.195),
            (.09,.223),(.015,.239),(-.10,.239)]
    build.turn([(y,r,'Spinner') for y,r in dome], 32)
    # Preserve a readable speed cue without giving the hub an aircraft-sized white disk.
    row = []
    steps = 6
    for sample in range((len(dome)-2)*steps + 1):
        i, sub = divmod(sample, steps)
        f = sub / steps
        y = dome[i][0] * (1-f) + dome[i+1][0] * f
        r = dome[i][1] * (1-f) + dome[i+1][1] * f
        t = sample / ((len(dome)-2)*steps)
        angle = .7 + 4.6 * t
        half = .25 - .115 * t
        row.append([build.vertex(((r+.002)*cos(angle+s),y+.002,(r+.002)*sin(angle+s)))
                    for s in (-half,half)])
    for a,b in zip(row,row[1:]):
        build.face((a[0],a[1],b[1],b[0]), 'SpinnerMark')


def machinery(build, segments):
    # Recessed fixed vanes and a shadow bulkhead prevent daylight shining through the fan.
    for j in range(15):
        angle = TAU*j/15
        root = .27
        tip = .88
        verts = [build.vertex((r*cos(angle+a),y,r*sin(angle+a)))
                 for r,a,y in ((root,-.11,-.29),(root,.07,-.39),(tip,.15,-.43),(tip,.08,-.32))]
        build.face(verts,'Mech')
    build.turn([(-.485,.005,'Mech'),(-.485,.88,'Mech')], segments)
    # Cowling service covers sit tangent to the shell. Recessed louvers face backwards.
    for mirror in (-1,1):
        def panel_vertex(y, angle, radius):
            return build.vertex((mirror*radius*cos(angle),y,radius*sin(angle)))
        for side_angle in (-.10,.15):
            for k in range(7):
                y = -1.22 - k*.067
                angle0 = side_angle - .048
                angle1 = side_angle + .048
                lip = [panel_vertex(y,angle0,outer_radius(y,angle0)+.004),
                       panel_vertex(y,angle1,outer_radius(y,angle1)+.004),
                       panel_vertex(y-.046,angle1,outer_radius(y-.046,angle1)+.009),
                       panel_vertex(y-.046,angle0,outer_radius(y-.046,angle0)+.009)]
                if mirror < 0: lip.reverse()
                build.face(lip, 'Mech')
        # Flush locking handles along the fan-cowl split. Their scale stays human-readable.
        for y in (-.18, -.36, -.54):
            upper = -.60
            lower = -.66
            coords = [panel_vertex(yp,a,outer_radius(yp,a)+.005) for yp,a in
                      ((y,upper),(y-.082,upper),(y-.082,lower),(y,lower))]
            if mirror < 0: coords.reverse()
            build.face(coords,'Mech')
        # A removable hydraulic access door with a recessed perimeter, lifting
        # catches and hinge knuckles. It follows the flat cowl facet in model space.
        center_y,center_a = -.52,-.01
        outline = [(-.32,-.19),(-.36,-.14),(-.36,.12),(-.29,.20),
                   (.27,.20),(.34,.13),(.34,-.12),(.28,-.19)]
        rows=[]
        for shrink in (1,.974):
            ring=[]
            for dy,da in outline:
                y,a = center_y+dy*shrink,center_a+da*shrink
                ring.append(panel_vertex(y,a,outer_radius(y,a)+.004))
            rows.append(ring)
        for j in range(len(outline)):
            k=(j+1)%len(outline)
            face=(rows[0][j],rows[0][k],rows[1][k],rows[1][j])
            build.face(tuple(reversed(face)) if mirror>0 else face,'Dark')
        for y in (-.73,-.32):
            for a,mat in ((-.155,'Mech'),(.165,'Edge')):
                plate=[panel_vertex(yp,ap,outer_radius(yp,ap)+.009)
                       for yp,ap in ((y-.035,a-.012),(y+.035,a-.012),
                                     (y+.035,a+.012),(y-.035,a+.012))]
                build.face(plate if mirror>0 else reversed(plate),mat)


def exhaust(build, segments):
    # Reverser tail cone, separate contoured core casing, and the dark rolled outlet.
    build.turn([(-1.8,.55,'Dark'),(-2.17,.548,'Mech'),(-2.26,.547,'Mech'),
                (-2.33,.532,'Fan'),(-2.53,.529,'Fan'),(-2.74,.515,'Mech'),
                (-2.78,.518,'Fan'),(-2.89,.501,'Fan'),(-3.08,.473,'Mech'),
                (-3.12,.477,'Fan'),(-3.31,.439,'Fan'),(-3.36,.419,'Edge'),
                (-3.36,.392,'Mech'),(-3.13,.396,'Mech'),(-3.07,.268,'Mech')], segments,
               lambda row,col,m: 'Mech' if row in (7,9,10) and col % 4 == 0 else m)
    build.turn([(-2.96,.29,'Mech'),(-3.22,.278,'Dark'),(-3.46,.224,'Mech'),
                (-3.63,.169,'Mech'),(-3.80,.074,'Mech'),(-3.86,.003,'Mech')], segments//2)
    # Rear vanes span the bypass outlet; visible obliquely, as on the game's engine art.
    for i in range(12):
        a = TAU*i/12
        row = [build.vertex((r*cos(a+b),y,r*sin(a+b))) for r,b,y in
               ((.524,-.045,-2.19),(.524,.035,-2.29),(.825,.11,-2.32),(.814,.054,-2.15))]
        build.face(tuple(reversed(row)),'Fan')
    for i in range(24):
        a = TAU*i/24
        liner = [build.vertex((r*cos(a+b),y,r*sin(a+b))) for r,b,y in
                 ((.270,0,-3.09),(.270,.14,-3.14),(.396,.21,-3.16),(.396,.11,-3.09))]
        build.face(tuple(reversed(liner)),'Fan')


def detail_engine(nodes, side, number):
    pod = nodes[f'Pod_{side}{number}']
    fan = nodes[f'Fan_Pod_{side}{number}']
    nozzle = nodes[f'Pod_{side}{number}_Nozzle']
    fittings = nodes[f'Pods_Nacelle{number}_{side}']
    small = number > 2
    radius = 3.50 if small else 6.80
    segments = 40 if small else 56
    height = (max(v.co.z for v in pod.data.vertices) - fan.location.z) / radius

    shell = MeshBuilder(fan.location, radius)
    cowling(shell, segments)
    mount_pylon(shell,height,small)
    shell.install(pod)
    rotor = MeshBuilder(scale=radius)
    blades(rotor,16 if small else 20)
    rotor.install(fan)
    fixed = MeshBuilder(fan.location - fittings.location, radius)
    machinery(fixed,segments)
    fixed.install(fittings)
    tail = MeshBuilder(fan.location - nozzle.location, radius)
    exhaust(tail,segments)
    tail.install(nozzle)


def fin_panels(fin, target, side, heights):
    # A near-flush, clipped-corner cover replaces the original long external actuators.
    # Sample each corner directly on the fin airfoil; the inboard and outboard skins differ.
    build = MeshBuilder(fin.matrix_world.translation - target.matrix_world.translation)
    offset = .025 * side
    for z, half_height in heights:
        vertices = []
        for dy,dz in ((-.54,-.72),(-.42,-1),(.42,-1),(.54,-.72),
                      (.54,.72),(.42,1),(-.42,1),(-.54,.72)):
            y = -26.35 + dy
            h = z + dz * half_height
            hit, point, _, _ = fin.ray_cast((side*8,y,h),(-side,0,0))
            if not hit:
                break
            point.x += offset
            vertices.append(build.vertex(point))
        if len(vertices) == 8:
            if side < 0:
                vertices.reverse()
            build.face(vertices,'Radome')
    build.install(target)


def chined_boom(boom):
    # Use the authored end stations and load path, but loft a deep polygonal beam
    # from the wing shoulder to the fin. Hard longitudinal creases define the chine.
    sections = {}
    for v in boom.data.vertices:
        sections.setdefault(round(v.co.y,3),[]).append(v.co)
    ys = sorted(sections)
    start, end = ys[0], ys[-1]
    build = MeshBuilder()
    rings = []
    contour = [(-.45,1),(.45,1),(.78,.78),(1,.31),(.99,-.16),
               (.54,-.85),(.28,-1),(-.28,-1),(-.54,-.85),(-.99,-.16),(-1,.31),(-.78,.78)]
    for fraction in (0,.025,.065,.14,.27,.42,.55,.69,.78,.84,.91,.97,1):
        y = min(ys,key=lambda s: abs(s-(start+fraction*(end-start))))
        section = sections[y]
        low, high = min(v.z for v in section),max(v.z for v in section)
        center = (low+high)*.5
        height = max(.12,(high-low)*.51)
        root = max(0.,min(1.,(fraction-.15)/.55))
        width = max(.14,max(abs(v.x) for v in section)*(.94+.86*root))
        rings.append([(x*width,y,center+z*height) for x,z in contour])
    # Separate adjacent facets, keeping their lengthwise shading continuous.
    for i in range(len(contour)):
        j = (i+1)%len(contour)
        rails = [(build.vertex(row[i]),build.vertex(row[j])) for row in rings]
        for a,b in zip(rails,rails[1:]):
            build.face((a[0],a[1],b[1],b[0]),'Deice' if i in (4,5,6,7,8) else 'Skin',True)
    build.face([build.vertex(p) for p in reversed(rings[0])],'Deice')
    build.face([build.vertex(p) for p in rings[-1]],'Skin')
    build.install(boom)


def swept_fin(fin):
    # The two shoulders break into a swept, narrow fin with a vertical hinge at
    # y=-29.04. The shallow slot clears the rudder through its full sweep.
    build = MeshBuilder()
    rings = []
    section = [(0,0),(.038,.42),(.14,.79),(.34,1),(.59,.89),(.88,.44),
               (1,.29),(1,-.29),(.88,-.44),(.59,-.89),(.34,-1),(.14,-.79),(.038,-.42)]
    for z,nose,half_width in ((-2.2,11.5,2.75),(-.75,9.7,2.42),(.68,7.7,1.88),
                              (2.55,4.25,1.68),(9.4,-4.7,1.25),(15.0,-12.4,.93),
                              (15.8,-14.0,.83)):
        rings.append([(half_width*w,nose+(-29.04-nose)*f,z) for f,w in section])
    for i in range(len(section)):
        j = (i+1)%len(section)
        rails = [(build.vertex(row[i]),build.vertex(row[j])) for row in rings]
        for a,b in zip(rails,rails[1:]):
            build.face((a[1],a[0],b[0],b[1]),'Deice' if i in (0,12) else 'Skin',True)
    build.face([build.vertex(p) for p in rings[0]],'Skin')
    build.face([build.vertex(p) for p in reversed(rings[-1])],'Skin')
    build.install(fin)


def lean_tail(nodes, side):
    sign = 1 if side == 'R' else -1
    fin = nodes['Fin_'+side]
    chined_boom(nodes['Boom_'+side])
    swept_fin(fin)
    # Both root transitions are part of their new lofts; retain the attachment
    # names as empties in the stream rather than rendering overlapping old shells.
    for name in ('BoomFillet_','FinFillet_'):
        nodes[name+side].data.clear_geometry()
    # The original stabilizer had two longitudinal bullet fairings perched over the
    # fins. The new fin tops seat directly in the fixed stabilizer.
    cap = nodes['TailCap_'+side]
    for v in cap.data.vertices:
        v.co.z *= .62
    strobe = nodes['Det_lights_tail'+side+'_strobe']
    aft = min(v.co.y for v in cap.data.vertices)
    strobe.location = cap.location + Vector((0,aft+.07,0))
    rudder = nodes['Rudder_'+side]
    for v in rudder.data.vertices:
        v.co.x *= .68
    rudder.data.update()
    bpy.context.view_layer.update()
    for name, direction, heights in (
        ('ActuatorCanoe_', sign, ((4.0,.45),(10.2,.45))),
        ('ActuatorCanoeIn_', -sign, ((4.0,.45),(10.2,.45))),
        ('HingeBarrel_', sign, ((2.4,.24),(8.5,.24),(12.7,.24))),
        ('HingeBarrelIn_', -sign, ((2.4,.24),(8.5,.24),(12.7,.24))),
    ):
        fin_panels(fin,nodes[name+side],direction,heights)


def leading_edge_material():
    name = 'BS_Megaplane_Deice'
    mat = bpy.data.materials.get(name)
    if mat is None:
        mat = bpy.data.materials['BS_Megaplane_Skin'].copy()
        mat.name = name
        red, green, blue, _, _, alpha = mat['bsmp_values']
        rgb = (red*.83, green*.83, blue*.90)
        mat['bsmp_values'] = (*rgb,.20,.44,alpha)
        mat.diffuse_color = (*rgb,alpha)
        bsdf = mat.node_tree.nodes.get('Principled BSDF')
        bsdf.inputs['Metallic'].default_value = .20
        bsdf.inputs['Roughness'].default_value = .56
        for node in mat.node_tree.nodes:
            if node.type == 'MIX_RGB':
                node.inputs[1].default_value = (*rgb,1)
    return mat


def leading_edge(wing, coat):
    # These airfoil sections have a straight swept nose. Carry the erosion-resistant
    # finish around the leading edge, including on the lower skin. Faces and UVs stay
    # with their source damage part, avoiding coplanar overlays on grazing approaches.
    mesh = wing.data
    skin_index = mesh.materials.find('BS_Megaplane_Skin')
    slot = len(mesh.materials)
    mesh.materials.append(coat)
    sections = {}
    for vertex in mesh.vertices:
        x, y, _ = vertex.co
        key = round(x, 2)
        if key not in sections:
            sections[key] = [y,y]
        sections[key][0] = min(y,sections[key][0])
        sections[key][1] = max(y,sections[key][1])
    x0, x1 = min(sections), max(sections)
    aft0, nose0 = sections[x0]
    aft1, nose1 = sections[x1]
    for poly in mesh.polygons:
        if poly.material_index != skin_index or abs(poly.normal.x) > .95:
            continue
        coords = [mesh.vertices[i].co for i in poly.vertices]
        depths = []
        for x,y,_ in coords:
            f = (x-x0)/(x1-x0)
            nose = nose0+(nose1-nose0)*f
            aft = aft0+(aft1-aft0)*f
            depths.append((nose-y)/(nose-aft))
        if max(depths) < .070:
            poly.material_index = slot


def wheel_well(well, nose=False):
    # Retain the curved rim at each door. Above the rim the six main bogies share
    # one uninterrupted sponson cavity; the forward end has a pocket for the toe
    # of the stowed bogie. Individual closed bulkheads cut through the moving tires.
    roof = max(v.co.z for v in well.data.vertices)
    stations = {}
    for v in well.data.vertices:
        x,y,z = v.co
        if z < roof - .15:
            stations.setdefault(round(y, 3),set()).add((round(x,4),y,z))
    ys = sorted(stations)
    first,last = ys[0],ys[-1]
    rim = sorted(stations[first])
    rim += [max(stations[y]) for y in ys[1:-1]]
    rim += sorted(stations[last],reverse=True)
    rim += [min(stations[y]) for y in reversed(ys[1:-1])]
    build = MeshBuilder()
    bottom, ledge, upper = [],[],[]
    for x,y,z in rim:
        t = (y-first)/(last-first)
        if not nose:
            y += 3.05*max(0.,(t-.87)/.13)
        # Zooming out through the nearly flush aperture reveals the roof recess.
        widen = (1 if x>0 else -1) * (.90 if nose else .29)
        pocket = (3.7 if nose else 4.6)*max(0., (t-.78)/.22) - 1.5*max(0.,(.18-t)/.18)
        bottom.append(build.vertex((x,y,z)))
        ledge.append(build.vertex((x+.80*widen,y+.86*pocket,z+.85)))
        upper.append(build.vertex((x+widen,y+pocket,roof+(2.7 if nose else 3.2))))
    for a,b,mat in ((bottom,ledge,'Dark'),(ledge,upper,'Primer')):
        for i in range(len(a)):
            j=(i+1)%len(a)
            build.face((a[j],a[i],b[i],b[j]),mat)
    build.face(reversed(upper),'Dark')
    build.install(well)


def gear_socket(leg, shift):
    # Reposition the bearing and deform the upper strut into it. Tire and bogie
    # vertices retain their exact deployed world positions and wheel spacing.
    shift = Vector(shift)
    leg.location += shift
    for v in leg.data.vertices:
        attached = max(0.,min(1.,(v.co.z+4.)/3.))
        v.co -= shift*(1-attached)
    leg.data.update()


def fit_gear(nodes,side):
    wheel_well(nodes['GearWell_'+side])
    gear_socket(nodes['Gear_Nose_'+side], (0,-1.08,-.72))
    nose_door = nodes['GearDoor_N_'+side]
    offset = Vector((-.24 if side=='L' else .24,0,0))
    nose_door.location += offset
    for v in nose_door.data.vertices:
        v.co -= offset
    terminal_door = nodes['GearDoor_M6_'+side]
    end = max(v.co.y for v in terminal_door.data.vertices)
    for v in terminal_door.data.vertices:
        v.co.y += 3.05*max(0.,(v.co.y+.5)/(end+.5))
    for number in range(1,7):
        gear_socket(nodes[f'Gear_Main_{side}{number}'], (0,-.68,.68))


def cargo_closures(nodes):
    ramp=nodes['Ramp']
    aft=min(v.co.y for v in ramp.data.vertices)
    for v in ramp.data.vertices:
        v.co.y += .15*max(0.,min(1.,(aft+.95-v.co.y)/.95))
    # Chamfer the meeting edges to their actual swept arcs. The recessed corners
    # can now clear the opposite leaf without a visible straight gap at rest.
    for stem in ('AftDoor_','BayDoor_'):
        for side,sign in (('L',-1),('R',1)):
            door=nodes[stem+side]
            radius=abs(door.matrix_world.translation.x)-.028
            for v in door.data.vertices:
                x=sign*v.co.x
                if x<0:
                    inward=-max(0.,radius**2-max(0.,v.co.z)**2)**.5
                    v.co.x=sign*max(x,inward)
            door.data.update()
    ramp.data.update()


def detail_structure(nodes):
    """Run after putting the source in its final metric frame, before projecting decals."""
    bpy.context.view_layer.update()
    deice = leading_edge_material()
    cargo_closures(nodes)
    nodes['BulletCollar'].data.clear_geometry()
    stabilizer = nodes['Tailplane'].data
    aft = min(v.co.y for v in stabilizer.vertices)
    for v in stabilizer.vertices:
        relief = max(0.,min(1.,(aft+.8-v.co.y)/.8))
        v.co.y += .14*relief
    stabilizer.update()
    wheel_well(nodes['GearWell_N'],nose=True)
    for side in ('L','R'):
        for number in range(1,5):
            detail_engine(nodes,side,number)
        lean_tail(nodes,side)
        for part in ('Root','Inner','Mid','Outer','Tip'):
            leading_edge(nodes[f'Wing_{part}_{side}'],deice)
        fit_gear(nodes,side)
