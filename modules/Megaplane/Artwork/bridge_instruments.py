"""Groza instrument bridge: a padded coaming, two recessed displays, pilot lighting controls,
copilot service panels, and the pressed inner faces of the windscreen posts. +Y is forward.
The meter-scale screen apertures share their centres with the game's seated-pilot locators.
"""
from math import cos, pi, sin
from pathlib import Path

import bpy
import bmesh
from mathutils import Vector, kdtree

from finish import new_paint
from forebody import Surface, child_mesh

SKIN = "DeckBinnacle"
RUBBER = "DeckPadding"
TRIM = "DeckRail"
DARK = "DeckRecess"
WITNESS = "DeckWitness_Emit"
LEGEND = "DeckLegend_Emit"
AFT = (0, -1, 0)


def materials():
    for name, rgb, gloss in (
        (SKIN, (.083, .112, .120), .33), (RUBBER, (.023, .034, .038), .21),
        (TRIM, (.18, .218, .219), .49), (DARK, (.017, .029, .030), .32),
        (WITNESS, (.095, .177, .157), .31), (LEGEND, (.55, .61, .58), .27),
    ):
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


def join_rings(surf, first, second, mat, centre):
    for i, a in enumerate(first):
        j=(i+1) % len(first)
        face=[a, first[j], second[j], second[i]]
        outward=sum(face, Vector()) / 4 - centre
        surf.face(face, mat, outward)


def cassette(surf, x, y, z, width, height, depth=.08):
    """Cast housing tapers back into the coaming, with a square display rebate."""
    centre = Vector((x,y,z))
    back = rectangle(x, y+depth, z-.012, width+.026, height+.014, .023)
    shoulder = rectangle(x, y+.023, z, width+.060, height+.064, .027)
    frame = rectangle(x, y-.009, z, width+.044, height+.044, .021)
    inner = rectangle(x, y, z, width+.002, height+.002, .006)
    join_rings(surf, back, shoulder, SKIN, centre)
    join_rings(surf, shoulder, frame, RUBBER, centre)
    for i in range(8):
        j=(i+1)%8
        surf.face((frame[i],frame[j],inner[j],inner[i]), TRIM if i == 0 else SKIN, AFT)
    surf.face(inner, DARK, AFT)


def spindle(surf, x, y, z, radius, indicator=True):
    """A rotary selector with a flange, castellated grip, relieved face and witness line."""
    count = 24
    def disc(ypos, r, knurl=False):
        return [Vector((x + cos(a*2*pi/count)*r*(.88 if knurl and a%2 else 1),
                        ypos,z + sin(a*2*pi/count)*r*(.88 if knurl and a%2 else 1))) for a in range(count)]
    base, collar = disc(y+.012, radius*1.28), disc(y-.002, radius*1.28)
    rear, grip, top = disc(y-.004,radius,True), disc(y-.033,radius,True), disc(y-.039,radius*.78)
    centre = Vector((x,y,z))
    join_rings(surf,base,collar,TRIM,centre)
    surf.face(collar,SKIN,AFT)
    join_rings(surf,rear,grip,RUBBER,centre)
    join_rings(surf,grip,top,SKIN,centre)
    surf.face(top, SKIN,AFT)
    if indicator:
        surf.face([(x+dx,y-.040,z+dz) for dx,dz in
                   ((-.0035,.002),(.0035,.002),(.0035,radius*.68),(-.0035,radius*.68))],WITNESS,AFT)


def face_art(surface, tiles, x,y,z,width,height,tile):
    tiles[len(surface.faces)] = tile
    surface.face([(x-width/2,y,z-height/2), (x+width/2,y,z-height/2),
                  (x+width/2,y,z+height/2), (x-width/2,y,z+height/2)], LEGEND,AFT)


def punched_panel(surf, tiles, x,y,z,width,height,tile, controls):
    cassette(surf,x,y,z,width,height,.060)
    face_art(surf,tiles,x,y-.001,z,width-.006,height-.007,tile)
    for pos in controls:
        spindle(surf,x+pos*width, y-.002,z-height*.275, min(.036,height*.15))


def coaming(surface):
    # Sections turn toward each seat at the shoulders, then taper into the original sill.
    sections=((-2.57,101.43,5.015),(-2.29,101.72,5.145),(-1.92,102.06,5.243),
              (-1.75,102.15,5.314),(-1.23,102.15,5.314),(-1.13,102.15,5.261),
              (-.41,102.15,5.261),(-.30,102.15,5.191),(.27,102.15,5.191),
              (.41,102.15,5.25),(1.56,102.15,5.25),(1.90,102.04,5.21),
              (2.22,101.76,5.12),(2.52,101.43,5.015))
    rings = []
    for x,y,z in sections:
        # The rear shelf descends onto the existing dashboard; the rubber brow projects aft.
        rings.append([Vector((x,y+dy,h)) for dy,h in
                      ((-.49,4.79),(-.39,4.825),(.083,4.851),(.083,z-.055),
                       (-.038,z-.041),(-.057,z-.020),(-.037,z),
                       (.185,z+.002),(.258,z-.027),(.281,4.82))])
    cover=(SKIN,SKIN,SKIN,SKIN,RUBBER,RUBBER,RUBBER,RUBBER,SKIN,DARK)
    for a,b in zip(rings,rings[1:]):
        for i,mat in enumerate(cover):
            j=(i+1)%len(a)
            pts=[a[i],b[i],b[j],a[j]]
            # Follow the cross-section of the padding. The inset front wall is
            # concave, so orienting it away from the average centre flips it.
            surface.face(pts,mat,(b[i]-a[i]).cross(a[j]-a[i]))
    surface.face(rings[0],SKIN,(-1,0,0))
    surface.face(rings[-1],SKIN,(1,0,0))


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
        _,i,d=tree.find(old)
        assert d<.002, "Windscreen surround and pressure liner have lost registration"
        delta=Vector(old)-p
        inner.data.vertices[i].co=inv@(p+delta.normalized()*.012)
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
    owner=nodes["Fuselage_Cockpit"]
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
    housing, hardware, tiles = Surface(), Surface(), {}
    coaming(housing)
    for name,x,z,w,h in (("TacticalScreen",-1.49,5.075,.372,.360),
                         ("SystemsScreen",-.83,5.09,.630,.215)):
        cassette(hardware,x,102.18,z,w,h)
        nodes[name]=locator(name,(x,102.18,z),owner)
    # Instrument lighting and engraved maker's plate flank the pilot. On the other
    # station the windscreen reveals the environmental and bus-distribution selectors.
    punched_panel(hardware,tiles,-1.932,102.055,5.109,.268,.176,1,(-.29,.29))
    punched_panel(hardware,tiles,-.116,102.163,5.073,.45,.166,0,())
    punched_panel(hardware,tiles,.53,102.16,5.074,.54,.212,4,(-.34,0,.34))
    punched_panel(hardware,tiles,1.17,102.16,5.074,.56,.212,5,(-.34,0,.34))
    punched_panel(hardware,tiles,1.71,102.07,5.047,.34,.165,6,(-.29,.29))
    # The local brightness knob on the chart bezel stays clear of the live pixel area.
    spindle(hardware,-1.23,102.145,5.032,.028)
    # The demisting duct mouths sit under the cowl, below the sightline. Each has
    # vertical vanes and a pair of high-contrast witness marks on the outer flange.
    for x in (-2.28,2.115):
        y,z=101.67,5.052
        cassette(hardware,x,y,z,.27,.074,.05)
        for off in range(-4,5):
            ox=x+off*.027
            hardware.face([(ox-.003,y-.009,z-.030),(ox+.004,y-.009,z-.030),
                           (ox+.014,y-.009,z+.030),(ox+.007,y-.009,z+.030)],TRIM,AFT)
    hnode=child_mesh("FlightDeck_Glareshield",owner)
    housing.install(hnode)
    hwear=child_mesh("FlightDeck_InstrumentBridge",owner)
    hardware.install(hwear)
    registration(hwear,tiles)
    nodes[hnode.name]=hnode
    nodes[hwear.name]=hwear
    bpy.context.view_layer.update()
