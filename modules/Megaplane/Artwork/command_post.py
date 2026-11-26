"""Chart table and pressure-bulkhead intercom for the Groza command deck."""

from math import cos, hypot, pi, sin

from mathutils import Vector

from finish import new_paint


PANEL, LINER, CHARCOAL = "CrewPanel", "CrewLiner", "DeckRecess"
OLIVE, AMBER, TRACE = "CrewInsulation", "CrewOchre", "CommandPlotInk_Emit"


def octagon(w, d, c):
    return ((-w+c,-d), (w-c,-d), (w,-d+c), (w,d-c),
            (w-c,d), (-w+c,d), (-w,d-c), (-w,-d+c))


def rounded_outline(width, depth, radius):
    corners = ((width-radius, -depth+radius), (width-radius, depth-radius),
               (-width+radius, depth-radius), (-width+radius, -depth+radius))
    points = []
    for index, (x, y) in enumerate(corners):
        for step in range(3):
            angle = (index-1)*pi/2 + step*pi/4
            points.append((x + radius*cos(angle), y + radius*sin(angle)))
    return points


def deck(x, y, lift=0):
    return .73*x, .73*y, 1.022 + .25*y + lift


def chart_line(frame, points, material, width=.004, lift=.011):
    for (ax, ay), (bx, by) in zip(points, points[1:]):
        dx, dy = bx-ax, by-ay
        length = hypot(dx, dy)
        nx, ny = -.5*width*dy/length, .5*width*dx/length
        frame.face((deck(ax+nx, ay+ny, lift), deck(ax-nx, ay-ny, lift),
                    deck(bx-nx, by-ny, lift), deck(bx+nx, by+ny, lift)),
                   material, (0, -.35, 1))


def chart(frame):
    # The plotting surface is a dark insert with a fixed range scale and route card.
    edge = rounded_outline(.514, .303, .033)
    gasket = rounded_outline(.534, .323, .040)
    shoulder = rounded_outline(.552, .341, .047)
    rings = [[deck(x, y, z) for x, y in outline]
             for outline, z in ((edge, .008), (gasket, .013), (shoulder, .006))]
    frame.face(rings[0], CHARCOAL, (0, -.35, 1))
    for inner, outer, material in ((rings[0], rings[1], CHARCOAL), (rings[1], rings[2], OLIVE)):
        for i in range(len(inner)):
            j = (i+1) % len(inner)
            frame.face((inner[i], outer[i], outer[j], inner[j]), material, (0, -.35, 1))
    for radius in (.072, .140, .211):
        circle = [(cos(i*pi/10)*radius, sin(i*pi/10)*radius) for i in range(21)]
        chart_line(frame, circle, TRACE)
    chart_line(frame, ((-.45,0),(.45,0)), TRACE)
    chart_line(frame, ((0,-.252),(0,.251)), TRACE)
    for x in (-.36,-.12,.12,.36):
        chart_line(frame, ((x,-.012),(x,.012)), TRACE)
    for y in (-.24,-.18,.18,.24):
        chart_line(frame, ((-.011,y),(.011,y)), TRACE)
    for coast in (
        ((-.49,.244),(-.442,.227),(-.386,.244),(-.309,.202),(-.261,.192),(-.23,.15)),
        ((-.49,.196),(-.438,.185),(-.389,.202),(-.337,.164),(-.282,.145),(-.253,.124)),
        ((.385,-.273),(.407,-.224),(.38,-.183),(.42,-.146),(.483,-.113)),
        ((.423,-.273),(.454,-.232),(.431,-.191),(.489,-.158)),
    ):
        chart_line(frame, coast, TRACE)
    route = ((-.43,-.183),(-.305,-.121),(-.192,-.104),(-.056,-.024),
             (.124,.038),(.237,.142),(.375,.169))
    chart_line(frame, route, AMBER, .007, .013)
    for x,y in route[::2]:
        chart_line(frame, ((x,y+.014),(x+.014,y),(x,y-.014),(x-.014,y),(x,y+.014)),
                   AMBER, .003, .0135)
    chart_line(frame, ((.367,.222),(.46,.222)), TRACE)
    for x in (.367,.414,.46):
        chart_line(frame, ((x,.216),(x,.228)), TRACE)

    glyphs = (((0,0),(0,1),(.60,1),(.60,.52),(0,.52)),
              ((0,1),(0,0),(.60,0)),
              ((0,0),(0,1),(.60,1),(.60,0),(0,0)))
    for i, glyph in enumerate(glyphs):
        chart_line(frame, [(-.486+i*.025+u*.025,.258+v*.027) for u,v in glyph], AMBER, .003)
    chart_line(frame, ((-.412,.285),(-.395,.285)), AMBER, .003)
    chart_line(frame, ((-.4035,.285),(-.4035,.258)), AMBER, .003)


def crash_grip(frame):
    # A continuous bent tube sits below the controls with clearance for a gloved hand.
    path = [Vector(p) for p in ((-.327,-.221,.862), (-.327,-.263,.781), (-.307,-.292,.768),
                              (-.273,-.304,.764), (.273,-.304,.764), (.307,-.292,.768),
                              (.327,-.263,.781), (.327,-.221,.862))]
    rings, axes = [], []
    for i, center in enumerate(path):
        axis = (path[min(i+1,len(path)-1)] - path[max(i-1,0)]).normalized()
        across = axis.cross(Vector((0,0,1))).normalized()
        up = across.cross(axis).normalized()
        rings.append([center + .020*(across*cos(j*pi/3) + up*sin(j*pi/3)) for j in range(6)])
        axes.append(axis)
    for i, (a, b) in enumerate(zip(rings, rings[1:])):
        center = (path[i]+path[i+1])*.5
        for j in range(6):
            k = (j+1) % 6
            points = (a[j], a[k], b[k], b[j])
            normal = sum(points, Vector())*.25 - center
            frame.face(points, CHARCOAL, normal)
    frame.face(rings[0], LINER, -axes[0])
    frame.face(rings[-1], LINER, axes[-1])


def interphone(frame):
    frame.panel(0, .251, 1.253, .665, .288, .096, CHARCOAL, .035)
    frame.panel(0, .214, 1.253, .644, .268, .042, PANEL, .033)
    frame.finish_face(((-.303,.191,1.131),(.303,.191,1.131),(.303,.191,1.373),(-.303,.191,1.373)),
                      "comms_strip", (0,-1,0))
    # Register the keys and telltales over the atlas's channel legends.
    for index, x in enumerate((-.171, -.059, .056, .166)):
        frame.rod((x,.191,1.252),(x,.184,1.252),.015,AMBER if index in (0,3) else OLIVE,5)
        frame.rod((x,.191,1.214),(x,.160,1.228 if index == 1 else 1.218),.014,LINER,6)


def build_command_post(frame_factory, owner, floor):
    """Build the aft-wall plot shelf and its recessed interphone panel."""
    ink = (.118, .209, .075)
    shader = new_paint(TRACE, ink, .28).node_tree.nodes["Principled BSDF"]
    shader.inputs["Emission Color"].default_value = (*ink, 1.)
    shader.inputs["Emission Strength"].default_value = .58
    frame = frame_factory((-1.14, 96.02, floor), (-1, 0, 0), (0, -1, 0))
    frame.rings([[(x,y+.196,z) for x,y in octagon(width,depth,.046)]
                 for width, depth, z in ((.393,.085,.043), (.427,.103,.109), (.421,.103,.862))], PANEL)
    frame.panel(0, .070, .442, .762, .634, .073, CHARCOAL, .032)
    frame.finish_face(((-.348, .030, .153), (.348, .030, .153),
                       (.348, .030, .716), (-.348, .030, .716)), "vent", (0,-1,0))
    for pixel in range(65,191,17):
        z = .716 - (pixel-4)*.563/248
        frame.face(((-.186,.029,z),(.186,.029,z),(.177,.015,z-.010),(-.177,.015,z-.010)),
                   LINER, (0,-1,1))
        frame.face(((-.177,.015,z-.010),(.177,.015,z-.010),(.186,.029,z-.022),(-.186,.029,z-.022)),
                   CHARCOAL, (0,-1,-1))
    frame.rings([[deck(x,y,offset) for x,y in rounded_outline(width, depth, radius)]
                 for width, depth, radius, offset in ((.517,.350,.056,-.095),
                    (.602,.387,.062,-.028), (.590,.375,.060,0))], LINER)
    crash_grip(frame)
    frame.panel(0,-.258,.869,.716,.083,.048,PANEL,.020)
    for i in range(5):
        x = -.243+i*.121
        frame.rod((x,-.276,.869),(x,-.295,.869),.022,AMBER if i==1 else CHARCOAL,8)
    interphone(frame)
    chart(frame)
    return frame.finish("FlightDeck_CommandPost", owner)
