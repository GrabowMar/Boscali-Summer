"""Vanguard replacement designs: clean manufactured assemblies, not dress-up geometry."""
import math


def replacements(g):
    skin, dark, glass, glow = g.SKIN, g.DARK, g.GLASS, g.GLOW

    def bar(name, start, end, radius, mat, sides=8):
        start, end = g.Vector(start), g.Vector(end)
        direction = end - start
        g.bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=radius, depth=direction.length,
                                             location=(start+end)*0.5)
        obj = g.bpy.context.object
        obj.name = name
        obj.rotation_euler = direction.to_track_quat("Z", "Y").to_euler()
        obj.data.materials.append(mat)
        return obj

    def base(color, lines=(), **extra):
        return dict(color=color, metal=0, smooth=0.33, grime=0.035,
                    lines=[g.plane_y(y) for y in lines], line_width=0.003, **extra)

    def remora(m):
        g.loft("CleanAirframe", [
            (-2.50,.25,.09,.07,.025,2.5,2.5,1.3), (-1.85,.42,.23,.12,.025,2.5,2.5,1.3),
            (-.60,.49,.29,.15,0,2.5,2.5,1.3), (.65,.44,.26,.15,-.01,2.5,2.5,1.3),
            (1.60,.30,.18,.12,-.025,2.3,2.3,1.4), (2.30,.12,.08,.06,-.03,2,2,1.6),
            (2.70,.008,.008,.006,-.03,2,2,2)], m[skin], rings=38,n=28,cap_tail=True)
        # A duct mouth has an actual thick lip and a recessed back wall.
        g.loft("DorsalIntake", [(-1.05,.06,.025,.018,.25,2.6,2.6,1.7),
               (-.25,.19,.075,.018,.28,2.8,2.8,1.7),(.50,.19,.075,.018,.28,2.8,2.8,1.7)],
               m[skin],rings=14,n=20,tip=False,cap_tail=True)
        g.box("IntakeMouth",(.30,.014,.064),(0,.506,.301),m[dark],chamfer=.005)
        g.box("IntakeSplitter",(.018,.08,.061),(0,.48,.301),m[skin],chamfer=.004)
        g.box("ExhaustLip",(.48,.07,.10),(0,-2.51,.025),m[skin],chamfer=.012)
        g.box("ExhaustRecess",(.39,.009,.052),(0,-2.55,.025),m[dark],chamfer=.006)
        rows=[(.38,.66,0,2.34,.045),(1.20,-.10,.015,1.54,.04),(2.20,-.94,.055,.52,.035),
              (2.34,-1.10,.075,.27,.03)]
        g.surface("ContinuousWing",[(x,y,z,c*.74,t) for x,y,z,c,t in rows],m[skin],mirror="x")
        for suffix,side in (("L",-1),("R",1)):
            with g.seg("Elevon"+suffix,pivot=(side*1.2,-1.17,-.01)):
                g.surface("ElevonPanel"+suffix,[(side*x,y-c*.74-.006,z,c*.26-.006,t)
                          for x,y,z,c,t in rows],m[skin])
            g.fin("Tail"+suffix,(-1.43,0,.78,.045),(-2.02,.66,.33,.04),m[skin],
                  cant=side*34,x=side*.30,z=.13)
        # Conformal sensors and aerials replace buried / intersecting cheek fins.
        g.box("EotsMount",(.19,.28,.046),(0,1.28,-.157),m[skin],chamfer=.010)
        g.box("EotsWindow",(.12,.16,.008),(0,1.33,-.184),m[glass],chamfer=.003)
        g.box("SatcomFairing",(.17,.36,.035),(0,-.67,.297),m[skin],chamfer=.009)
        for side in (-1,1):
            g.box("FormationLight"+str(side),(.006,.13,.008),(side*.472,-.6,.035),m[glow],chamfer=.002)
        g.lugs(-.35,.35,.29,m[dark])
        return base((.22,.28,.31),(-1.8,-.7,.65,1.8),
                    hatches=[(-1.4,-.95,-.17,.17,True),(.6,1,-.13,.13,True)],
                    stencils=[("REMORA / XQ-58V",-.75,.28,-.03,.015,.39,(.67,.72,.74))],
                    view=(1,.8,.5))

    def aegis(m):
        # Three downward-pointing cold-launch darts, in tandem along a narrow spine.
        g.loft("AerodynamicSpine",[(-.78,.025,.02,.02,.04,3,3,2),(-.61,.105,.050,.036,.04,3,3,2),
               (.52,.105,.050,.036,.04,3,3,2),(.74,.028,.027,.020,.04,3,3,2)],
               m[skin],rings=26,n=16,tip=True)
        g.box("MountBeam",(.095,.84,.035),(0,-.025,.106),m[skin],chamfer=.007)
        g.lugs(-.28,.28,.128,m[dark])
        cells=[(0,-.38,-.54),(0,0,-.54),(0,.38,-.54)]
        for i,(x,y,z) in enumerate(cells):
            g.cylinder("EjectorCollar"+str(i),.047,.057,(0,y,-.028),m[skin],verts=16)
            for side in (-1,1):
                g.box("EjectorArm",(.021,.042,.13),(side*.050,y,-.082),m[skin],chamfer=.004)
            g.box("ReleaseShoe",(.066,.066,.023),(0,y,-.13),m[dark],chamfer=.004)
        for side in (-1,1):
            g.box("ApproachSensor",(.008,.12,.027),(side*.106,.26,.043),m[dark],chamfer=.004)
            g.box("SensorWindow",(.003,.077,.014),(side*.111,.26,.043),m[glass],chamfer=.002)
            g.box("ReadyLamp",(.003,.035,.011),(side*.108,-.21,.043),m[glow],chamfer=.002)
        return base((.43,.46,.48),(-.54,.49),hatches=[(-.2,.2,-.065,.065,True)],
                    stencils=[("AEGIS-3",-.20,.15,.044,.068,.085,(.12,.15,.17))],
                    preview_with=[("AegisInterceptor",p,(math.pi/2,0,0)) for p in cells],view=(1,.65,.12))

    def muzzle_frame(m):
        # Octagonal annulus around a real open recessed rail channel.
        def ring(w,h,c):
            return [(-w+c,-h),(w-c,-h),(w,-h+c),(w,h-c),(w-c,h),(-w+c,h),(-w,h-c),(-w,-h+c)]
        verts=[]
        for y,shape in ((2.42,ring(.133,.108,.023)),(2.62,ring(.115,.09,.018)),
                        (2.62,ring(.067,.041,.009)),(2.42,ring(.067,.041,.009))):
            verts += [(x,y,z) for x,z in shape]
        faces=[]
        for k in range(4):
            for i in range(8):
                a,b=k*8,((k+1)%4)*8
                faces.append((a+i,a+(i+1)%8,b+(i+1)%8,b+i))
        g.mesh_object("MachinedMuzzle",verts,faces,m[skin])
        g.box("BoreDepth",(.17,.012,.13),(0,2.23,0),m[dark],chamfer=.008)
        for side in (-1,1):
            g.box("InnerRail",(.014,.35,.046),(side*.052,2.445,0),m[skin],chamfer=.002)

    def lance(m):
        # The user liked the original exposed bank/frame. Rebuild only its bullet exit.
        spec=g.lance(m)
        for name in ("Muzzle","Bore","BoreThroat"):
            obj=g.bpy.data.objects.get(name)
            if obj is not None: g.bpy.data.objects.remove(obj,do_unlink=True)
        beam=g.bpy.data.objects["Beam"]
        bm=g.bmesh.new(); bm.from_mesh(beam.data)
        front=[f for f in bm.faces if f.calc_center_median().y + beam.location.y > 2.42 and f.normal.y > .8]
        g.bmesh.ops.delete(bm,geom=front,context="FACES")
        bm.to_mesh(beam.data); bm.free()
        muzzle_frame(m)
        spec.update(exposed_bank=True,grime=.055,line_width=.0035,view=(1,1,.34))
        return spec

    def canopy(m):
        # Twelve-gore canopy with a vent, seam ribs and eight real suspension lines.
        sectors,rings=48,13
        verts=[]
        for j in range(rings):
            phi=.055+(math.pi/2-.055)*j/(rings-1)
            radius=5.0*math.sin(phi)
            z=8.2-2.5*(1-math.cos(phi))
            for i in range(sectors):
                a=i*2*math.pi/sectors
                verts.append((radius*math.cos(a),radius*math.sin(a),z))
        faces=[]
        for j in range(rings-1):
            for i in range(sectors):
                a,b=j*sectors+i,j*sectors+(i+1)%sectors
                faces.append((a,a+sectors,b+sectors,b))
        g.mesh_object("ContinuousFabric",verts,faces,m[skin])
        for i in range(8):
            a=i*2*math.pi/8
            bar("SuspensionLine"+str(i),(.28*math.cos(a),.48*math.sin(a),.23),
                (5*math.cos(a),5*math.sin(a),5.7),.012,m[skin],sides=6)
        return base((.12,.20,.15),smooth_angle=50,view=(1,.7,.25),
                    patches=[((-6,-6,-1),(6,6,5.65),(.042,.054,.049))])

    def glaive(m):
        g.loft("ArmoredFlightBody",[(-1.48,.10,.10,.10,.03,3,3,2),(-1.13,.27,.23,.18,.03,3,3,2),
               (.53,.28,.22,.18,.03,3,3,2),(1.07,.22,.17,.14,.03,3,3,2),
               (1.42,.04,.04,.04,.03,2,2,2)],m[skin],rings=28,n=20,tip=True)
        g.box("ParachutePack",(.36,.80,.085),(0,-.14,.292),m[skin],chamfer=.019)
        g.box("PackLatch",(.19,.05,.022),(0,.24,.341),m[dark],chamfer=.006)
        g.lugs(-.5,.38,.354,m[dark])
        for suffix,side in (("R",1),("L",-1)):
            with g.seg("Wing"+suffix,pivot=(side*.25,-.18,.16)):
                g.surface("WingPanel"+suffix,[(side*.25,.21,.16,.79,.055),(side*1.45,-.3,.18,.33,.045)],m[skin])
                g.cylinder("WingHinge",.045,.03,(side*.26,-.18,.185),m[skin],verts=12)
            g.fin("Tail"+suffix,(-.94,0,.40,.06),(-1.18,.29,.18,.045),m[skin],cant=side*28,x=side*.19,z=.10)
        g.sleeve("TailNozzle",-1.50,-1.39,.098,.063,m[skin],z=.03,n=16)
        g.tube("NozzleThroat",[(-1.40,.062,0,.03),(-1.28,.062,0,.03)],m[dark],n=16)
        with g.seg("GunYaw",pivot=(0,-.20,-.22)):
            g.cylinder("TurretRace",.18,.075,(0,-.20,-.22),m[skin],verts=24)
            for side in (-1,1):
                g.box("GunYoke",(.046,.24,.17),(side*.143,-.13,-.32),m[skin],chamfer=.011)
        with g.seg("GunPitch","GunYaw",pivot=(0,-.12,-.33)):
            g.box("GunBreech",(.22,.38,.19),(0,-.28,-.33),m[skin],chamfer=.021)
            g.tube("CannonJacket",[(-.08,.058,0,-.33),(.65,.058,0,-.33)],m[skin],n=16)
            g.sleeve("CannonMuzzle",.65,.94,.048,.019,m[skin],z=-.33,n=16)
            g.tube("CannonBore",[(.63,.018,0,-.33),(.65,.018,0,-.33)],m[dark],n=12)
            for side in (-1,1):
                g.box("FeedCassette",(.10,.31,.15),(side*.16,-.33,-.31),m[skin],chamfer=.015)
        g.box("TargetingBezel",(.13,.07,.09),(0,1.27,-.043),m[dark],chamfer=.010)
        g.box("TargetingWindow",(.08,.008,.05),(0,1.308,-.043),m[glass],chamfer=.004)

        def pose(stow=False):
            canopy_root=g.bpy.data.objects.get("GlaiveCanopy")
            if canopy_root:
                for obj in canopy_root.children_recursive:
                    if obj.type == "MESH" and not obj.name.endswith(("_LOD1","_LOD2")):
                        obj.hide_render=stow
            for name,angle in (("WingL",90),("WingR",-90)):
                g.bpy.data.objects[name].rotation_euler.z=0 if stow else math.radians(angle)
            g.bpy.data.objects["GunPitch"].rotation_euler.x=0 if stow else math.radians(-70)
            g.bpy.context.view_layer.update()

        return base((.29,.34,.30),(-.94,.48,1.10),
                    hatches=[(-.81,-.40,-.12,.12,True)],
                    stencils=[("GLAIVE / SKY TURRET",-.82,.40,-.018,.043,.24,(.72,.76,.64))],
                    anchors=[("GlaiveMuzzle","GunPitch",(0,.947,-.33))],
                    preview_with=[("GlaiveCanopy",(0,0,0))],pose=pose,
                    view=(1,.9,-.25),pose_view=(1,.8,.25))

    return {"Remora":remora,"AegisPod":aegis,"Lance":lance,"GlaiveCanopy":canopy,"Glaive":glaive}
