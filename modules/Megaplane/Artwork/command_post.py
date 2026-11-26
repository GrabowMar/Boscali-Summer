"""Chart table and pressure-bulkhead intercom for the Groza command deck."""

from math import cos, hypot, pi, sin

from finish import new_paint


PANEL, LINER, CHARCOAL = "CrewPanel", "CrewLiner", "DeckRecess"
OLIVE, AMBER, TRACE = "CrewInsulation", "CrewOchre", "CommandPlotInk_Emit"


def octagon(w, d, c):
    return ((-w+c,-d), (w-c,-d), (w,-d+c), (w,d-c),
            (w-c,d), (-w+c,d), (-w,d-c), (-w,-d+c))


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
    frame.face([deck(x,y,.008) for x,y in octagon(.514,.303,.029)], CHARCOAL, (0,-.35,1))
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


def build_command_post(frame_factory, owner, floor):
    """Build the aft-wall plot shelf and its recessed interphone panel."""
    ink = (.118, .209, .075)
    shader = new_paint(TRACE, ink, .28).node_tree.nodes["Principled BSDF"]
    shader.inputs["Emission Color"].default_value = (*ink, 1.)
    shader.inputs["Emission Strength"].default_value = .58
    frame = frame_factory((-1.14, 96.02, floor), (-1, 0, 0), (0, -1, 0))
    frame.slab(0, .185, .459, .854, .177, .833, PANEL, .057)
    frame.panel(0, .070, .442, .762, .634, .073, CHARCOAL, .032)
    frame.finish_face(((-.348, .030, .153), (.348, .030, .153),
                       (.348, .030, .716), (-.348, .030, .716)), "vent", (0,-1,0))
    frame.rings([[deck(x,y,offset) for x,y in octagon(.602,.387,.056)]
                 for offset in (-.095, 0)], LINER)
    for x in (-.332, .332):
        frame.rod((x,-.217,.834), (x,-.261,.807), .024, OLIVE, 8)
    frame.panel(0,-.258,.869,.716,.083,.048,PANEL,.020)
    for i in range(5):
        x = -.243+i*.121
        frame.rod((x,-.276,.869),(x,-.295,.869),.022,AMBER if i==1 else CHARCOAL,8)
    frame.panel(0,.223,1.253,.659,.283,.060,PANEL,.027)
    frame.finish_face(((-.303,.191,1.131),(.303,.191,1.131),(.303,.191,1.373),(-.303,.191,1.373)),
                      "comms_strip", (0,-1,0))
    chart(frame)
    return frame.finish("FlightDeck_CommandPost", owner)
