"""Groza instrument bridge: a padded coaming, two recessed displays, pilot lighting controls,
copilot service panels, and the pressed inner faces of the windscreen posts. +Y is forward.
The meter-scale screen apertures share their centres with the game's seated-pilot locators.
"""
from math import cos, pi, sin
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, kdtree

from cabin_finish import assign_finish
from finish import new_paint
from forebody import Surface, child_mesh

SKIN = "DeckBinnacle"
RUBBER = "DeckPadding"
TRIM = "DeckRail"
DARK = "DeckRecess"
WITNESS = "DeckWitness_Emit"
LEGEND = "DeckLegend_Emit"
AFT = (0, -1, 0)
BRIDGE_PALETTE = (
    (SKIN, (.084, .097, .104), .28),
    (RUBBER, (.026, .030, .032), .21),
    (TRIM, (.151, .162, .162), .39),
    (DARK, (.019, .023, .024), .24),
    (WITNESS, (.61, .239, .048), .32),
    (LEGEND, (.55, .61, .58), .27),
)


def materials():
    for name, rgb, gloss in BRIDGE_PALETTE:
        mat = new_paint(name, rgb, gloss)
        if name == TRIM:
            mat["bsmp_values"] = [*rgb, .47, gloss, 1.]
            mat.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = .47
        if name in (WITNESS, LEGEND):
            shader = mat.node_tree.nodes["Principled BSDF"]
            shader.inputs["Emission Color"].default_value = (*rgb, 1.)
            shader.inputs["Emission Strength"].default_value = .8
            if name == LEGEND:
                filename = Path(__file__).resolve().parents[1] / "Assets" / (mat.name + "_Tex.png")
                texture = mat.node_tree.nodes.new("ShaderNodeTexImage")
                texture.image = bpy.data.images.load(str(filename), check_existing=True)
                texture.image.pack()
                for slot in ("Base Color", "Emission Color"):
                    mat.node_tree.links.new(texture.outputs["Color"], shader.inputs[slot])


def rectangle(x, y, z, width, height, clip):
    w, h = width / 2, height / 2
    return [Vector((x+dx, y, z+dz)) for dx, dz in
            ((-w+clip,-h), (w-clip,-h), (w,-h+clip), (w,h-clip),
             (w-clip,h), (-w+clip,h), (-w,h-clip), (-w,-h+clip))]


def rounded_rectangle(x, y, z, width, height, radius):
    """Three samples per corner for visible castings and gasket edges."""
    w, h = width / 2, height / 2
    corners = ((w-radius,-h+radius), (w-radius,h-radius),
               (-w+radius,h-radius), (-w+radius,-h+radius))
    return [Vector((x + u + radius*cos((2*corner+step-2)*pi/4), y,
                    z + v + radius*sin((2*corner+step-2)*pi/4)))
            for corner, (u,v) in enumerate(corners) for step in range(3)]


def join_rings(surf, first, second, mat, centre):
    for i, a in enumerate(first):
        j=(i+1) % len(first)
        face=[a, first[j], second[j], second[i]]
        outward=sum(face, Vector()) / 4 - centre
        surf.face(face, mat, outward)


def finished_quad(surface, finish, tile, points, normal):
    face = len(surface.faces)
    surface.face(points, SKIN, normal)
    corners = [surface.index[tuple(round(float(axis), 6) for axis in point)] for point in points]
    finish.append((face, tile, corners))


def install_finished(surface, obj, finish, smooth_faces=()):
    surface.install(obj)
    for face in smooth_faces:
        obj.data.polygons[face].use_smooth = True
    for face, tile, corners in finish:
        assign_finish(obj, face, tile, corners=corners)


def bolt(surface, x, y, z, radius=.009):
    points = [Vector((x + cos(k*pi/4)*radius, y, z + sin(k*pi/4)*radius)) for k in range(8)]
    surface.face(points, TRIM, AFT)
    surface.face([(x-r*.55,y-.0005,z-r*.13) for r in (-radius,radius)] +
                 [(x-r*.55,y-.0005,z+r*.13) for r in (radius,-radius)], DARK, AFT)


def cassette(surf, x, y, z, width, height, depth=.08, rounded=False):
    """Cast housing tapers back into the coaming, with a square display rebate."""
    centre = Vector((x,y,z))
    contour = rounded_rectangle if rounded else rectangle
    back = contour(x, y+depth, z-.012, width+.026, height+.014, .023)
    shoulder = contour(x, y+.023, z, width+.060, height+.064, .027)
    frame = contour(x, y-.009, z, width+.044, height+.044, .021)
    inner = contour(x, y, z, width+.002, height+.002, .006)
    join_rings(surf, back, shoulder, SKIN, centre)
    join_rings(surf, shoulder, frame, RUBBER, centre)
    for i in range(len(frame)):
        j = (i+1) % len(frame)
        finish = TRIM if frame[i].z <= z-height/2 and frame[j].z <= z-height/2 else SKIN
        surf.face((frame[i],frame[j],inner[j],inner[i]), finish, AFT)
    surf.face(inner, DARK, AFT)
    for side in (-1, 1):
        for stack in (-1, 1):
            bolt(surf, x+side*(width/2+.013), y-.009, z+stack*(height/2-.035), .007)


def spindle(surf, smooth_faces, x, y, z, radius):
    """A rotary selector with a flange, castellated grip, relieved face and witness line."""
    count = 16
    def disc(ypos, r, knurl=False):
        return [Vector((x + cos(a*2*pi/count)*r*(.88 if knurl and a%2 else 1),
                        ypos,z + sin(a*2*pi/count)*r*(.88 if knurl and a%2 else 1))) for a in range(count)]
    base, collar = disc(y+.012, radius*1.28), disc(y-.002, radius*1.28)
    rear, grip, top = disc(y-.004,radius,True), disc(y-.029,radius,True), disc(y-.038,radius*.73)
    centre = Vector((x,y,z))
    start = len(surf.faces)
    join_rings(surf,base,collar,TRIM,centre)
    smooth_faces.extend(range(start, len(surf.faces)))
    surf.face(collar,SKIN,AFT)
    join_rings(surf,rear,grip,RUBBER,centre)
    start = len(surf.faces)
    join_rings(surf,grip,top,SKIN,centre)
    smooth_faces.extend(range(start, len(surf.faces)))
    surf.face(top, SKIN,AFT)
    mark = radius*.136
    surf.face([(x+dx,y-.0385,z+dz) for dx,dz in
               ((-mark,.003),(mark,.003),(mark*.74,radius*.67),(-mark*.74,radius*.67))],WITNESS,AFT)


def face_art(surface, tiles, x,y,z,width,height,tile):
    tiles[len(surface.faces)] = tile
    surface.face([(x-width/2,y,z-height/2), (x+width/2,y,z-height/2),
                  (x+width/2,y,z+height/2), (x-width/2,y,z+height/2)], LEGEND,AFT)


def punched_panel(surf, tiles, smooth_faces, x,y,z,width,height,tile, controls):
    cassette(surf,x,y,z,width,height,.060)
    face_art(surf,tiles,x,y-.001,z,width-.006,height-.007,tile)
    for pos in controls:
        spindle(surf,smooth_faces,x+pos*width, y-.002,z-height*.275, min(.036,height*.15))


def coaming(surface, finish):
    # Sections turn toward each seat at the shoulders, then taper into the original sill.
    sections=((-2.57,101.43,5.015),(-2.29,101.72,5.145),(-1.92,102.06,5.243),
              (-1.75,102.15,5.314),(-1.23,102.15,5.314),(-1.13,102.15,5.261),
              (-.41,102.15,5.261),(-.30,102.15,5.191),(.27,102.15,5.191),
              (.41,102.15,5.25),(1.56,102.15,5.25),(1.90,102.04,5.21),
              (2.22,101.76,5.12),(2.52,101.43,5.015))
    rings = []
    for x,y,z in sections:
        # A slim metal instrument panel wraps around the recessed live displays.
        # The dark cap descends in three facets like folded padded sheet.
        rings.append([Vector((x,y+dy,h)) for dy,h in
                      ((-.38,4.816),(-.29,4.851),(.083,4.860),(.083,z-.057),
                       (-.040,z-.045),(-.075,z-.026),(-.069,z-.004),
                       (-.011,z+.016),(.186,z+.003),(.252,z-.027),(.281,4.82))])
    cover=(SKIN,SKIN,SKIN,SKIN,RUBBER,RUBBER,RUBBER,RUBBER,SKIN,SKIN,DARK)
    for a,b in zip(rings,rings[1:]):
        for i,mat in enumerate(cover):
            j=(i+1)%len(a)
            pts=[a[i],b[i],b[j],a[j]]
            # Follow the cross-section of the padding. The inset front wall is
            # concave, so orienting it away from the average centre flips it.
            normal=(b[i]-a[i]).cross(a[j]-a[i])
            if i in (1, 2, 7, 8):
                finished_quad(surface, finish, "fabric" if i == 7 else "charcoal", pts, normal)
            else:
                surface.face(pts,mat,normal)
    surface.face(rings[0],SKIN,(-1,0,0))
    surface.face(rings[-1],SKIN,(1,0,0))


def overhead(surface, finish):
    """Tapered overhead breaker and services console on the central canopy rib."""
    outline = ((-.615,101.558),(.462,101.558),(.537,101.616),(.537,101.933),
               (.500,102.029),(-.659,102.029),(-.703,101.933),(-.703,101.616))
    def z(y):
        return 5.921 - (y-101.558)*.263
    low = [Vector((x,y,z(y))) for x,y in outline]
    shoulder = [Vector((x*1.05,y,z(y)+.026)) for x,y in outline]
    mounts = [Vector((x*.93,y,z(y)+.117)) for x,y in outline]
    centre = Vector((-.08,101.790,5.918))
    join_rings(surface, low, shoulder, TRIM, centre)
    join_rings(surface, shoulder, mounts, SKIN, centre)
    surface.face(low,DARK,(0,-.26,-1))
    surface.face(mounts,SKIN,(0,.26,1))
    # Label carriers are recessed amid rows of guarded selectors. Position the
    # large markings on the stepped underside where the native pilot can see them.
    for x,tile in ((-.468,"power"),(-.199,"breakers"),(.065,"thermal"),(.326,"comms_strip")):
        aft, nose = 101.629, 101.903
        left, right = x-.123, x+.123
        finished_quad(surface, finish, tile, [(left,nose,z(nose)-.002),(right,nose,z(nose)-.002),
                       (right,aft,z(aft)-.002),(left,aft,z(aft)-.002)], (0,-.26,-1))
        for dx in (-.081,0,.081):
            for row, y in enumerate((101.714,101.820)):
                clip = [(x+dx+u,y+v,z(y+v)-.011) for u,v in
                        ((-.015,-.020),(.015,-.020),(.015,.020),(-.015,.020))]
                surface.face(clip,TRIM,(0,-.26,-1))
                surface.face([(a,b+.013,c-.012) for a,b,c in clip],
                             WITNESS if dx == -.081 and row == 0 else RUBBER,(0,-.26,-1))


def registration(obj,tiles):
    """Each printed module owns one atlas rectangle, including a two-pixel bleed margin."""
    uv=obj.data.uv_layers.active
    for face,tile in tiles.items():
        c,r = tile%4, tile//4
        for loop, (u,v) in zip(obj.data.polygons[face].loop_indices, ((0,0),(1,0),(1,1),(0,1))):
            uv.uv[loop].vector = ((c+.006+u*.988)/4,(1-r+.014+v*.972)/2)


def locator(name, point, owner):
    obj=bpy.data.objects.new(name,None)
    bpy.context.collection.objects.link(obj)
    obj.parent=owner
    obj.location=Vector(point)-owner.matrix_world.translation
    obj.empty_display_size=.065
    return obj


def line_posts():
    # The pressure liner was uniformly inset from the airframe. At eye distance its
    # displaced mullions read as a second shadow frame. Press this lining against
    # the actual metal surround while retaining the stitched vertices at its edge.
    frame = bpy.data.objects["Detail_WindscreenFrames"]
    inner = bpy.data.objects["Int_BridgeLining"]
    tree = kdtree.KDTree(len(inner.data.vertices))
    for v in inner.data.vertices:
        tree.insert(inner.matrix_world@v.co,v.index)
    tree.balance()
    inv=inner.matrix_world.inverted()
    matched=set()
    for v in frame.data.vertices:
        p=frame.matrix_world@v.co
        old=(p.x*.988,p.y-.15,p.z*.986)
        copies=tree.find_range(old,.00002)
        assert copies, "Windscreen surround and pressure liner have lost registration"
        delta=Vector(old)-p
        pressed=inv@(p+delta.normalized()*.012)
        # The imported pressure liner has separate vertices for adjacent triangles
        # at shading and UV seams. All of them share this physical press line.
        for _,i,_ in copies:
            inner.data.vertices[i].co=pressed
            matched.add(i)
    inner.data.materials.append(bpy.data.materials["BS_Megaplane_"+SKIN])
    slot=len(inner.data.materials)-1
    for p in inner.data.polygons:
        if all(i in matched for i in p.vertices):
            p.material_index=slot
            p.use_smooth=False
    inner.data.update()


def fit_bridge(nodes):
    materials()
    # Replace the old solid yellow warning-light blocks. The recessed live strip and
    # printed lighting station now occupy the coaming above these mounting surfaces.
    panel=nodes["FlightDeck_Panel"]
    idx=panel.data.materials.find("BS_Megaplane_Annunciator_Emit")
    assert idx >= 0
    editable = bmesh.new()
    editable.from_mesh(panel.data)
    lights = [f for f in editable.faces if f.material_index == idx]
    assert lights, "Missing first-generation pilot and copilot light blocks"
    bmesh.ops.delete(editable, geom=lights, context="FACES")
    editable.to_mesh(panel.data)
    editable.free()
    # Press the former flat shelf into a mounting well. Its top otherwise cuts
    # through the lower quarter of the new chart display from the pilot's eye.
    for vertex in panel.data.vertices:
        p=panel.matrix_world@vertex.co
        across=max(0.,min(1.,(2.6-abs(p.x))/.42))
        depth=max(0.,min(1.,(p.y-101.34)/.35))
        height=max(0.,min(1.,(p.z-4.71)/.19))
        vertex.co.z -= .145 * across * depth * height
    panel.data.update()

    line_posts()
    sculpt_bridge(nodes)


def sculpt_bridge(nodes):
    owner = nodes["Fuselage_Cockpit"]
    housing, hardware, tiles = Surface(), Surface(), {}
    trim, switch_tiles, smooth_faces = [], [], []
    coaming(housing, trim)
    overhead(hardware, switch_tiles)
    for name,x,z,w,h in (("TacticalScreen",-1.49,5.075,.372,.360),
                         ("SystemsScreen",-.83,5.09,.630,.215)):
        cassette(hardware,x,102.18,z,w,h,rounded=True)
        if name not in nodes:
            nodes[name]=locator(name,(x,102.18,z),owner)
    # Instrument lighting and engraved maker's plate flank the pilot. On the other
    # station the windscreen reveals the environmental and bus-distribution selectors.
    # Keep the port lighting legend below the sloping cowl from the pilot eye.
    punched_panel(hardware,tiles,smooth_faces,-1.892,102.055,5.074,.268,.176,1,(-.29,.29))
    punched_panel(hardware,tiles,smooth_faces,-.116,102.163,5.073,.45,.166,0,())
    punched_panel(hardware,tiles,smooth_faces,.53,102.16,5.074,.54,.212,4,(-.34,0,.34))
    punched_panel(hardware,tiles,smooth_faces,1.17,102.16,5.074,.56,.212,5,(-.34,0,.34))
    punched_panel(hardware,tiles,smooth_faces,1.71,102.07,5.047,.34,.165,6,(-.29,.29))
    # The local brightness knob on the chart bezel stays clear of the live pixel area.
    spindle(hardware,smooth_faces,-1.23,102.145,5.032,.028)
    # The demisting duct mouths sit under the cowl, below the sightline. Each has
    # vertical vanes and a pair of high-contrast witness marks on the outer flange.
    for x in (-2.28,2.115):
        y,z=101.67,5.052
        cassette(hardware,x,y,z,.27,.074,.05)
        for off in range(-4,5):
            ox=x+off*.027
            hardware.face([(ox-.003,y-.009,z-.030),(ox+.004,y-.009,z-.030),
                           (ox+.014,y-.009,z+.030),(ox+.007,y-.009,z+.030)],TRIM,AFT)
    hnode = nodes.get("FlightDeck_Glareshield") or child_mesh("FlightDeck_Glareshield",owner)
    install_finished(housing, hnode, trim)
    hwear = nodes.get("FlightDeck_InstrumentBridge") or child_mesh("FlightDeck_InstrumentBridge",owner)
    install_finished(hardware, hwear, switch_tiles, smooth_faces)
    registration(hwear,tiles)
    nodes[hnode.name]=hnode
    nodes[hwear.name]=hwear
    bpy.context.view_layer.update()
