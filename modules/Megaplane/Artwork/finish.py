"""Paint and maintenance markings for the KY-44, in the BSMP Blender frame (+Y forward)."""

from math import ceil, cos, hypot, pi, sin

import bpy
from mathutils import Vector


PREFIX = "BS_Megaplane_"


def paint(material, rgb, metallic, smoothness, alpha=1.0):
    material.use_nodes = True
    material.diffuse_color = (*rgb, alpha)
    material["bsmp_values"] = [*rgb, metallic, smoothness, alpha]
    shader = material.node_tree.nodes.get("Principled BSDF")
    shader.inputs["Base Color"].default_value = (*rgb, 1)
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Roughness"].default_value = 1 - smoothness
    shader.inputs["Alpha"].default_value = alpha
    # The tiled preview multiplies its texture by the same linear paint tint as Unity.
    for node in material.node_tree.nodes:
        if node.type == "MIX_RGB" and node.blend_type == "MULTIPLY":
            node.inputs[1].default_value = (*rgb, 1)
    return material


def new_paint(name, rgb, smoothness, source=None):
    material = source.copy() if source else bpy.data.materials.new(PREFIX + name)
    material.name = PREFIX + name
    material["bsmp_tiled"] = bool(source and source["bsmp_tiled"])
    return paint(material, rgb, .025, smoothness)


class PaintedSurface:
    """Project metre-sized strokes onto one damage part and batch them into one mesh."""

    def __init__(self, owner, design_scale, side=0):
        self.owner = owner
        self.inverse = owner.matrix_world.inverted()
        self.design_scale = design_scale
        self.side = side
        self.normal = Vector((side, 0, 0) if side else (0, 0, 1))
        self.vertices, self.faces, self.slots, self.materials = [], [], [], []
        self.hits = {}

    def vertex(self, x, y):
        x *= self.design_scale
        y *= self.design_scale
        key = (round(x, 5), round(y, 5))
        if key not in self.hits:
            origin = Vector((self.side * 250, x * self.side, y) if self.side else (x, y, 150))
            hit, point, normal, _ = self.owner.ray_cast(
                self.inverse @ origin, self.inverse.to_3x3() @ -self.normal
            )
            index = None
            if hit:
                if normal.dot(self.inverse.to_3x3() @ self.normal) < 0:
                    normal.negate()
                index = len(self.vertices)
                self.vertices.append(point + normal * .024)
            self.hits[key] = index
        return self.hits[key]

    def quad(self, corners, material):
        indices = [self.vertex(x, y) for x, y in corners]
        if None in indices or len(set(indices)) < 4:
            return
        if material not in self.materials:
            self.materials.append(material)
        self.faces.append(indices)
        self.slots.append(self.materials.index(material))

    def line(self, start, end, width, material):
        x, y = start
        dx, dy = end[0] - x, end[1] - y
        length = hypot(dx, dy)
        if length < .0001:
            return
        nx, ny = -dy * width / (2 * length), dx * width / (2 * length)
        # Subdivide to follow cambered wings and tapered fins, particularly near the tips.
        count = max(1, ceil(length / .65))
        for step in range(count):
            a, b = step / count, (step + 1) / count
            self.quad([(x+dx*a-nx, y+dy*a-ny), (x+dx*b-nx, y+dy*b-ny),
                       (x+dx*b+nx, y+dy*b+ny), (x+dx*a+nx, y+dy*a+ny)], material)

    def path(self, points, width, material):
        for a, b in zip(points, points[1:]):
            self.line(a, b, width, material)

    def build(self, suffix=""):
        if not self.faces:
            raise ValueError("No markings reached " + self.owner.name)
        name = "Detail_Livery_" + self.owner.name + suffix
        mesh = bpy.data.meshes.new(name)
        mesh.from_pydata(self.vertices, [], self.faces)
        for material in self.materials:
            mesh.materials.append(material)
        for poly, slot in zip(mesh.polygons, self.slots):
            poly.material_index = slot
        mesh.update()
        obj = bpy.data.objects.new(name, mesh)
        self.owner.users_collection[0].objects.link(obj)
        obj.parent = self.owner
        return obj


# Single-stroke industrial lettering. Spaces in the paths form the bridges of a stencil.
GLYPHS = {
    "K": [[(.05, 0), (.05, 1)], [(.62, 1), (.10, .48)], [(.25, .61), (.65, 0)]],
    "Y": [[(.02, 1), (.32, .53), (.62, 1)], [(.32, .45), (.32, 0)]],
    "-": [[(.08, .43), (.52, .43)]],
}


def four(surface, x, y, height, material):
    # Outline the diagonal junctions as adjoining pieces. Overlapping the ends of
    # three separately projected strokes flickers on the curved fin at inspection range.
    bands = [
        (0, 1, .475, .595, .475, .595),
        (.277, .390, .015, .418, .015, .418),
        (.277, .390, .636, .710, .636, .710),
        (.390, .816, .015, .160, .330, .475),
        (.816, 1.0, .330, .475, .466, .475),
    ]
    for lo, hi, left, right, left_top, right_top in bands:
        count = max(1, ceil((hi-lo)*height/.60))
        for k in range(count):
            a, b = k/count, (k+1)/count
            section = lambda t: (x+height*(left+(left_top-left)*t),
                                 x+height*(right+(right_top-right)*t), y+height*(lo+(hi-lo)*t))
            x0, x1, y0 = section(a)
            x3, x2, y1 = section(b)
            surface.quad([(x0,y0), (x1,y0), (x2,y1), (x3,y1)], material)


def stencil(surface, text, center, baseline, height, material):
    advance = .88 * height
    x = center - ((len(text) - 1) * advance + .67 * height) * .5
    for char in text:
        if char == "4":
            four(surface, x, baseline, height, material)
        else:
            for path in GLYPHS[char]:
                surface.path([(x + px * height, baseline + py * height) for px, py in path],
                             .115 * height, material)
        x += advance


def wing_service(surface, sign, start, end, ink):
    # Human-scale walking surface sits clear of the working lift doors farther outboard.
    def at(span, offset):
        return sign * span, 4.4 - .24 * (span - 36) + offset
    for offset in (0, 1.15):
        surface.line(at(start, offset), at(end, offset), .10, ink)
    for span in range(42, 72, 7):
        if start < span < end:
            surface.line(at(span, .22), at(span, .93), .13, ink)


def filler(surface, x, y, ink, ochre):
    radius = .68
    ring = [(x + radius * cos(i*pi/12), y + radius * sin(i*pi/12)) for i in range(25)]
    surface.path(ring, .085, ink)
    surface.line((x-.28, y), (x+.28, y), .10, ink)
    surface.path([(x+.86, y+.73), (x+.86, y+.38), (x+1.20, y+.38)], .075, ochre)


def finish_surface(nodes):
    """Apply after the shape pass, when all owner meshes are in their final position."""
    bpy.context.view_layer.update()
    mats = bpy.data.materials
    skin = paint(mats[PREFIX+"Skin"], (.084, .108, .132), .045, .57)
    paint(mats[PREFIX+"Radome"], (.042, .052, .065), .0, .37)
    paint(mats[PREFIX+"Dark"], (.037, .048, .057), .03, .38)
    paint(mats[PREFIX+"Edge"], (.22, .246, .264), .68, .65)
    paint(mats[PREFIX+"Mech"], (.058, .067, .070), .56, .53)
    paint(mats[PREFIX+"Fan"], (.19, .203, .205), .68, .58)
    paint(mats[PREFIX+"SpinnerMark"], (.43, .45, .43), .02, .46)
    if (leading_edge := mats.get(PREFIX+"Deice")) is not None:
        paint(leading_edge, (.068, .089, .112), .20, .54)

    control = new_paint("GraphiteControl", (.061, .080, .101), .49, skin)
    fairing = new_paint("FairingPaint", (.076, .098, .115), .50, skin)
    light = new_paint("StencilGrey", (.30, .345, .363), .40)
    ink = new_paint("ServiceInk", (.042, .057, .065), .31)
    ochre = new_paint("ServiceOchre", (.31, .219, .104), .39)
    for name, owner in list(nodes.items()):
        if owner.type != "MESH":
            continue
        replacement = None
        if name.startswith(("Flap_Inner_", "Flap_Outer_", "Aileron_", "Rudder_")) or name == "Elevator":
            replacement = control
        elif name.startswith(("LiftDoor_", "TailCap_", "ActuatorCanoe", "FlapTracks_")):
            replacement = fairing
        if replacement:
            for i, original in enumerate(owner.data.materials):
                if original == skin:
                    owner.data.materials[i] = replacement

    art = []
    # References below are stations on the Breakwater master (190.1 m half-span).
    tip = nodes["Wing_Tip_R"]
    design_scale = max((tip.matrix_world @ Vector(c)).x for c in tip.bound_box) / 190.1
    for suffix, sign in (("L", -1), ("R", 1)):
        cabin = PaintedSurface(nodes["Fuselage_Cockpit"], 1, side=sign)
        stencil(cabin, "KY-44", 72.8*sign, 1.70, 1.12, light)
        art.append(cabin.build("_" + suffix))
        fin = PaintedSurface(nodes["Fin_" + suffix], design_scale, side=sign)
        # Each fin gets a readable outboard face. The horizontal coordinate follows the viewer.
        stencil(fin, "44", -78*sign, 22.1, 6.1, light)
        fin.line((-82*sign, 30.0), (-73*sign, 30.0), .32, ochre)
        fin.line((-82*sign, 30.65), (-75*sign, 30.65), .12, light)
        art.append(fin.build())

        root = PaintedSurface(nodes["Wing_Root_" + suffix], design_scale)
        wing_service(root, sign, 38, 50.8, ink)
        art.append(root.build())
        inner = PaintedSurface(nodes["Wing_Inner_" + suffix], design_scale)
        wing_service(inner, sign, 51.2, 70, ink)
        filler(inner, 64*sign, -.25, ink, ochre)
        art.append(inner.build())
        outer = PaintedSurface(nodes["Wing_Outer_" + suffix], design_scale)
        if sign < 0:
            stencil(outer, "KY-44", -153.5, -22.0, 3.45, light)
        else:
            # Original split-chevron fleet mark, pointed towards the leading edge.
            for offset, width in ((0, .63), (-1.50, .32)):
                outer.path([(149.6, -20.4+offset), (154.1, -17.2+offset),
                            (158.6, -20.4+offset)], width, light)
            outer.line((153.85, -20.8), (154.35, -20.8), .50, ochre)
        art.append(outer.build())
    nodes.update((obj.name, obj) for obj in art)
