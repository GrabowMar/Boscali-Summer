"""Shallow service panels and station markings for the KY-44's fighter cells."""

from mathutils import Vector

from forebody import Surface, child_mesh


def outfit_fighter_bay(nodes):
    liner = nodes['FighterBay_Liner']
    frame = nodes['FighterBay_LinerFrame']
    liner_local = liner.matrix_world.inverted()
    frame_local = frame.matrix_world.inverted()
    art = Surface()

    def wall(y, z, side):
        origin, direction = Vector((0, y, z)), Vector((side, 0, 0))
        hit, point, _, _ = liner.ray_cast(liner_local @ origin, liner_local.to_3x3() @ direction)
        if not hit:
            raise ValueError(f'No fighter bay wall at station {y:g}, height {z:g}')
        return (liner.matrix_world @ point).x

    def stamp(side, station, height, profile, material, proud):
        points = []
        for u, v in profile:
            y, z = station - side*u, height + v
            points.append((wall(y, z, side) - side*proud, y, z))
        art.face(points, material, (-side, 0, 0))

    def panel(side, y, z, width, height, material, proud):
        w, h = width/2, height/2
        stamp(side, y, z, [(-w,-h),(w,-h),(w,h),(-w,h)], material, proud)

    def clear(side, y, z, width, height):
        # The old liner ribs precede the wall. Mount the new trim between ribs.
        direction = frame_local.to_3x3() @ Vector((side, 0, 0))
        for d in (-width/2, -width/4, 0, width/4, width/2):
            for v in (-height/2, 0, height/2):
                x = wall(y+d, z+v, side)
                hit, p, _, _ = frame.ray_cast(frame_local @ Vector((0, y+d, z+v)), direction)
                if hit and side*(frame.matrix_world @ p).x < side*x:
                    return False
        return True

    # Marks are cut from the same opaque palettes as the rest of the hold.
    glyphs = {
        'A': [((-.24,-.34),(-.14,-.34),(.05,.34),(-.05,.34)),
              ((.14,-.34),(.24,-.34),(.05,.34),(-.05,.34)),
              ((-.17,-.13),(.17,-.13),(.17,-.04),(-.17,-.04))],
        'F': [((-.20,-.34),(-.10,-.34),(-.10,.34),(-.20,.34)),
              ((-.10,.24),(.21,.24),(.21,.34),(-.10,.34)),
              ((-.10,-.02),(.14,-.02),(.14,.08),(-.10,.08))],
    }
    for side in (-1, 1):
        for label, starts in (('A', (-12.0, 10.8)), ('F', (19.0, 35.0))):
            for center in starts:
                y = next(y for y in (center, center-.35, center+.35, center-.70, center+.70)
                         if clear(side, y, -7.25, 1.16, 1.20))
                panel(side, y, -7.25, 1.16, 1.20, 'BayDeck', .018)
                panel(side, y, -7.76, 1.16, .18, 'BayOchre', .021)
                for stroke in glyphs[label]:
                    stamp(side, y, -7.25, stroke, 'BayIvory', .021)

        # Low-profile covers break up the tall blank liner without taking space
        # from the suspended fighters or adding another casting light.
        for center in (-8.0, -1.0, 6.5, 24.0, 31.0, 39.6):
            y = next(y for y in (center, center-.35, center+.35, center-.70, center+.70)
                     if clear(side, y, -8.88, 1.28, 1.85))
            stamp(side, y, -8.88, [(-.64,-.82),(-.54,-.92),(.53,-.92),(.64,-.81),
                                    (.64,.81),(.53,.92),(-.53,.92),(-.64,.81)], 'BayEnamel', .037)
            panel(side, y, -8.99, .96, 1.20, 'BayDeck', .040)
            panel(side, y, -8.28, .48, .078, 'BayIvory', .040)
            for step in range(3):
                panel(side, y, -9.17 - step*.11, .66, .039, 'BayEnamel', .043)

    obj = child_mesh('Detail_FighterBay_ServicePanels', liner)
    art.install(obj)
    nodes[obj.name] = obj
