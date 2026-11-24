"""Metre-scale maintenance finishes on the two exposed tail-boom crowns."""

from math import cos, hypot, pi, sin

import bpy
from mathutils import Vector

from forebody import Surface, child_mesh


def clipped_rectangle(x, y, width, length, corner):
    left, right = x-width/2, x+width/2
    aft, fore = y-length/2, y+length/2
    return [(left+corner, aft), (right-corner, aft), (right, aft+corner),
            (right, fore-corner), (right-corner, fore), (left+corner, fore),
            (left, fore-corner), (left, aft+corner)]


class BoomCrown:
    """Build fittings in station coordinates on the actual finished pressure skin."""

    def __init__(self, owner, side):
        self.owner, self.side = owner, side
        self.frame = owner.matrix_world
        self.inverse = self.frame.inverted()
        self.height = {}
        self.mesh = Surface()

    def point(self, xy, lift):
        x, y = xy
        key = (x, y)
        if key not in self.height:
            origin = Vector((self.frame.translation.x + self.side*x, y, 55))
            ok, p, _, _ = self.owner.ray_cast(self.inverse @ origin, Vector((0, 0, -1)))
            if not ok:
                raise ValueError(f"{self.owner.name} service detail outside skin at {xy}")
            self.height[key] = self.frame @ p
        return self.height[key] + Vector((0, 0, lift))

    def face(self, outline, lift, paint):
        self.mesh.face([self.point(p, lift) for p in outline], paint, (0, 0, 1))

    def bevel(self, outer, inner, low, high, paint):
        for i, a in enumerate(outer):
            j = (i+1) % len(outer)
            self.mesh.face([self.point(a, low), self.point(outer[j], low),
                            self.point(inner[j], high), self.point(inner[i], high)], paint, (0, 0, 1))

    def stroke(self, a, b, width, z, paint):
        dx, dy = b[0]-a[0], b[1]-a[1]
        k = width / (2*hypot(dx, dy))
        nx, ny = -dy*k, dx*k
        self.face([(a[0]-nx, a[1]-ny), (b[0]-nx, b[1]-ny),
                   (b[0]+nx, b[1]+ny), (a[0]+nx, a[1]+ny)], z, paint)

    def cover(self, x, y, width, length):
        outer = clipped_rectangle(x, y, width, length, .16)
        lip = clipped_rectangle(x, y, width-.092, length-.092, .13)
        self.bevel(outer, lip, .017, .052, "ServiceInk")
        self.face(lip, .052, "FairingPaint")
        # Captive quarter-turn locks at the corners; the larger oval is a fold-flat handle.
        for ex in (-1, 1):
            for ey in (-1, 1):
                px, py = x+ex*(width/2-.17), y+ey*(length/2-.19)
                self.face(clipped_rectangle(px, py, .09, .09, .023), .057, "Mech")
                self.stroke((px-.021, py-.014), (px+.021, py+.014), .012, .061, "Edge")
        xh = x-width*.22
        self.face(clipped_rectangle(xh, y, .16, .34, .055), .061, "ServiceInk")
        self.face(clipped_rectangle(xh-.012, y, .060, .243, .018), .066, "Mech")

    def label(self, x, y, number):
        # H1/H2, read facing outboard from the walkway on either side.
        glyphs = [[(0,0),(0,1)],[(.60,0),(.60,1)],[(0,.50),(.60,.50)]]
        glyphs.extend([[(.94,.77),(1.16,1),(1.16,0)]] if number == 1 else
                      [[(.88,.78),(.99,1),(1.34,1),(1.46,.86),(1.46,.66),(.89,.07),(.89,0),(1.48,0)]])
        for line in glyphs:
            for a, b in zip(line, line[1:]):
                # Glyph right corresponds to aft on the starboard boom.
                p = lambda t: (x+t[1]*.25, y-self.side*t[0]*.25)
                self.stroke(p(a), p(b), .028, .057, "StencilGrey")

    def cooler(self, x, y):
        # Raised detachable shroud with a genuine shallow well and aft-facing slats.
        base = clipped_rectangle(x, y, 1.61, 2.84, .23)
        rim = clipped_rectangle(x, y, 1.37, 2.58, .15)
        opening = clipped_rectangle(x, y, 1.16, 2.33, .10)
        bed = clipped_rectangle(x, y, 1.10, 2.25, .08)
        self.bevel(base, rim, .018, .148, "FairingPaint")
        self.bevel(rim, opening, .148, .148, "GraphiteControl")
        self.bevel(opening, bed, .148, .043, "Mech")
        self.face(bed, .043, "ServiceInk")
        for i in range(7):
            cy = y-.94+i*.31
            # The thin returning face catches a different highlight from the inclined slat.
            a, b = (x-.54, cy-.095), (x+.54, cy-.095)
            c, d = (x+.54, cy+.095), (x-.54, cy+.095)
            self.mesh.face([self.point(a,.129), self.point(b,.129),
                            self.point(c,.052), self.point(d,.052)], "GraphiteControl", (0,0,1))
            self.mesh.face([self.point(a,.098), self.point(b,.098),
                            self.point(b,.129), self.point(a,.129)], "Mech", (0,-1,0))

    def walkway(self):
        # A boot-width strip, interrupted at the expansion seams. The top slopes at
        # each skin station, so each length has its own conformal vertices.
        for a, b in ((-59.2,-52.3), (-52.22,-45.2), (-45.12,-39.2),
                     (-39.12,-32.3), (-32.22,-26.1), (-26.02,-20.8)):
            self.face(clipped_rectangle(-1.13,(a+b)/2,.87,b-a,.095),.024,"GraphiteControl")
        for y in (-56.0,-45.6,-32.7,-21.25):
            self.stroke((-1.49,y),(-1.32,y),.047,.032,"StencilGrey")
            self.stroke((-.94,y),(-.77,y),.047,.032,"StencilGrey")

    def servicing_port(self, x, y):
        def circle(radius):
            return [(x+radius*cos(2*pi*i/16), y+radius*sin(2*pi*i/16)) for i in range(16)]
        self.bevel(circle(.36),circle(.304),.020,.064,"ServiceInk")
        self.face(circle(.304),.064,"GraphiteControl")
        for dx in (-.115,.115):
            self.face(clipped_rectangle(x+dx,y,.070,.25,.023),.069,"Mech")
        self.stroke((x-.49,y-.18),(x-.49,y+.18),.036,.020,"ServiceOchre")


def detail_boom_services(nodes):
    """Run after rebuild_planform, before LOD and runtime export."""
    bpy.context.view_layer.update()
    for suffix, side in (("L",-1),("R",1)):
        owner = nodes["Boom_"+suffix]
        shell = BoomCrown(owner,side)
        shell.walkway()
        for station, number in ((-34.6,1),(-37.24,2)):
            shell.cover(.73,station,1.53,2.46)
            shell.label(.62,station+.35*side,number)
        shell.cooler(.74,-46.30)
        # A separate inspection door and circular servicing port on the outboard chine.
        shell.cover(3.04,-53.22,.94,2.10)
        shell.servicing_port(3.02,-55.08)
        obj = child_mesh("Detail_BoomServices_"+suffix,owner)
        shell.mesh.install(obj)
        nodes[obj.name] = obj
