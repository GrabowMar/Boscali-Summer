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
            (2.70,.008,.008,.006,-.03,2,2,2)], m[skin], rings=30,n=24,cap_tail=True)
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
            g.box("FormationLight"+str(side),(.006,.13,.008),(side*.472,-.6,.035),m[skin],chamfer=.002)
        g.lugs(-.35,.35,.29,m[dark])
        lamps=[((.4689,-.6651,.0309),(.4751,-.5349,.0391),(.09,.44,.50)),
               ((-.4751,-.6651,.0309),(-.4689,-.5349,.0391),(.09,.44,.50))]
        return base((.22,.28,.31),(-1.8,-.7,.65,1.8),
                    patches=lamps,
                    hatches=[(-1.4,-.95,-.17,.17,True),(.6,1,-.13,.13,True)],
                    stencils=[("REMORA / XQ-58V",-.75,.28,-.03,.015,.39,(.67,.72,.74))],
                    view=(1,.8,.5))

    def aegis(m):
        # Low-profile horizontal carriage; each round drops clear before pitching upright.
        g.loft("AerodynamicSpine",[(-.60,.035,.020,.016,.04,3,3,2),(-.41,.18,.043,.029,.04,3,3,2),
               (.27,.18,.043,.029,.04,3,3,2),(.62,.014,.014,.012,.04,3,3,2)],
               m[skin],rings=26,n=16,tip=True)
        g.box("MountBeam",(.095,.67,.029),(0,-.025,.091),m[skin],chamfer=.006)
        g.lugs(-.23,.23,.11,m[dark])
        cells=[(-.145,0,-.13),(0,0,-.13),(.145,0,-.13)]
        for i,(x,y,z) in enumerate(cells):
            for yy in (-.24,.24):
                g.box("EjectorMount",(.030,.06,.063),(x,yy,-.038),m[skin],chamfer=.005)
                g.box("ReleaseShoe",(.045,.066,.020),(x,yy,-.079),m[dark],chamfer=.004)
        for side in (-1,1):
            g.box("ApproachSensor",(.008,.09,.024),(side*.181,.16,.043),m[dark],chamfer=.004)
            g.box("SensorWindow",(.003,.060,.012),(side*.186,.16,.043),m[dark],chamfer=.002)
            g.box("ReadyLamp",(.003,.025,.009),(side*.182,-.20,.043),m[skin],chamfer=.002)
        lamps=[((.1805,-.213,.038),(.1845,-.187,.048),(.09,.44,.50)),
               ((-.1845,-.213,.038),(-.1805,-.187,.048),(.09,.44,.50))]
        return base((.43,.46,.48),(-.54,.49),hatches=[(-.2,.2,-.065,.065,True)],
                    patches=lamps,
                    stencils=[("AEGIS-3",-.18,.11,.037,.062,.15,(.12,.15,.17))],
                    preview_with=[("AegisInterceptor",p) for p in cells],view=(1,.75,-.30))

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

    def glaive(m):
        """Symmetric fixed boom frame, aft-hinged wings and a supported two-axis belly gun."""
        wing_z=.175; hinge_y=-.16; hinge_x=.255
        gun_y=.13; gun_z=-.26
        g.loft("FlightKeel",[(-1.05,.048,.054,.048,.025,2.8,3,2),(-.73,.13,.10,.08,.025,2.8,3,2),
               (-.12,.18,.13,.105,.025,2.8,3,2),(.55,.155,.12,.085,.025,2.8,3,2),
               (.93,.090,.076,.055,.025,2.4,2.5,2),(1.16,.006,.006,.006,.025,2,2,2)],
               m[skin],rings=24,n=20,tip=True)
        # This fixed cross-member joins the booms to the hull independently of the folding panels.
        g.surface("BoomCarryThrough",[(-.47,.17,.055,.24,.12),(-.17,.20,.07,.29,.12),
                  (.17,.20,.07,.29,.12),(.47,.17,.055,.24,.12)],m[skin])
        g.lugs(-.22,.22,.146,m[dark])
        for suffix,side in (("R",1),("L",-1)):
            g.surface("FixedWingRoot"+suffix,[(side*.10,.35,.130,.51,.15),
                      (side*.25,.34,wing_z,.50,.08)],m[skin])
            g.cylinder("FixedHingeBearing"+suffix,.029,.034,(side*hinge_x,hinge_y,.159),m[skin],verts=16)
            with g.seg("Wing"+suffix,pivot=(side*hinge_x,hinge_y,wing_z)):
                g.surface("WingPanel"+suffix,[(side*hinge_x,.34,wing_z,.50,.08),
                    (side*.97,.18,.185,.34,.065),(side*1.72,-.06,.195,.20,.055)],m[skin])
                g.cylinder("MovingHinge"+suffix,.032,.016,(side*hinge_x,hinge_y,.179),m[skin],verts=16)
                g.cylinder("HingePin"+suffix,.012,.029,(side*hinge_x,hinge_y,.182),m[skin],verts=10)
            g.tube("TailBoom"+suffix,[(-2.05,.020,side*.43,.055),(-1.75,.025,side*.43,.055),
                   (.10,.033,side*.43,.055)],m[skin],n=12)
            g.tube("BoomRootFairing"+suffix,[(-.22,.034,side*.43,.055),
                   (-.04,.043,side*.43,.055),(.10,.033,side*.43,.055),(.17,.007,side*.43,.055)],m[skin],n=12)
            g.tube("TailSocket"+suffix,[(-2.05,.024,side*.43,.055),(-1.95,.030,side*.43,.055),
                   (-1.72,.030,side*.43,.055)],m[skin],n=12)
            g.fin("TailFin"+suffix,(-1.75,0,.31,.07),(-1.94,.31,.17,.05),m[skin],
                  cant=side*10,x=side*.43,z=.07)
        g.surface("TailPlane",[(-.58,-1.71,.080,.20,.07),(.58,-1.71,.080,.20,.07)],m[skin])
        g.tube("PusherHub",[(-1.125,.045,0,.055),(-1.075,.052,0,.055),(-1.00,.058,0,.055)],m[skin],n=14)
        with g.seg("Propeller",pivot=(0,-1.152,.055)):
            g.tube("Spinner",[(-1.185,.012,0,.055),(-1.16,.048,0,.055),(-1.125,.045,0,.055)],m[skin],n=14)
            # Swept, tapered, twisted airfoil blades, not flat rectangular paddles.
            for side in (-1,1):
                verts=[]; faces=[]; n=12
                for radius,chord,sweep,pitch in ((.027,.033,0,32),(.075,.063,-.003,29),
                    (.15,.074,-.010,25),(.24,.057,-.027,19),(.31,.037,-.045,14),(.335,.010,-.048,12)):
                    angle=math.radians(pitch)
                    for i in range(n):
                        t=math.tau*i/n
                        c=math.cos(t)*chord*.5; h=math.sin(t)*chord*.075
                        verts.append((side*radius,-1.152+c*math.sin(angle)+h*math.cos(angle),
                                      .055+side*(sweep+c*math.cos(angle)-h*math.sin(angle))))
                for ring in range(5):
                    for i in range(n):
                        j=(i+1)%n; a=ring*n; b=(ring+1)*n
                        faces.append((a+i,a+j,b+j,b+i))
                faces.extend((tuple(reversed(range(n))),tuple(range(5*n,6*n))))
                g.mesh_object("PusherBlade",verts,faces,m[skin])
        g.sleeve("LaunchAssistExit",-1.07,-.94,.026,.016,m[skin],z=-.055,n=12)
        g.tube("LaunchAssistThroat",[(-.97,.015,0,-.055),(-.94,.015,0,-.055)],m[dark],n=12)
        g.loft("AvionicsCover",[(.32,.027,.008,.006,.144,2.8,2.8,2),(.46,.065,.027,.014,.142,2.8,2.8,2),
               (.66,.061,.022,.014,.123,2.8,2.8,2),(.76,.018,.004,.007,.120,2,2,2)],m[skin],rings=8,n=12)
        g.box("SensorBezel",(.085,.105,.030),(0,.88,-.047),m[dark],chamfer=.007)
        g.box("EoWindow",(.056,.07,.004),(0,.88,-.0635),m[glass],chamfer=.002)
        # Fixed body plinth reaches the yaw race; neither race nor yoke floats below the hull.
        g.box("GunBodyMount",(.123,.16,.052),(0,gun_y,-.079),m[skin],chamfer=.010)
        with g.seg("GunYaw",pivot=(0,gun_y,-.102)):
            g.cylinder("GunRace",.090,.034,(0,gun_y,-.118),m[skin],verts=16)
            g.cylinder("RaceSeal",.082,.010,(0,gun_y,-.138),m[skin],verts=16)
            for side in (-1,1):
                g.box("GimbalArm",(.024,.085,.146),(side*.082,gun_y,-.190),m[skin],chamfer=.006)
                g.xcyl("PitchBearing",.031,.024,(side*.082,gun_y,gun_z),m[skin],verts=14)
        with g.seg("GunPitch","GunYaw",pivot=(0,gun_y,gun_z)):
            g.xcyl("PitchAxle",.016,.204,(0,gun_y,gun_z),m[skin],verts=12)
            for side in (-1,1):
                g.xcyl("BearingCap",.019,.009,(side*.100,gun_y,gun_z),m[dark],verts=12)
            g.box("GunReceiver",(.090,.20,.066),(0,gun_y,gun_z),m[skin],chamfer=.010)
            g.sleeve("GunJacket",.215,.62,.017,.0038,m[skin],z=gun_z,n=12)
            g.sleeve("BarrelCollar",.245,.279,.024,.016,m[skin],z=gun_z,n=12)
            g.sleeve("BarrelCollar",.414,.440,.022,.016,m[skin],z=gun_z,n=12)
            g.sleeve("GunMuzzle",.62,.71,.018,.007,m[skin],z=gun_z,n=12)
            g.tube("BoreRecess",[(.585,.0038,0,gun_z),(.589,.0038,0,gun_z)],m[dark],n=10)
            g.box("FeedBox",(.056,.14,.058),(.096,.32,gun_z),m[skin],chamfer=.008)
            g.box("FeedCover",(.058,.070,.005),(.096,.315,gun_z+.030),m[skin],chamfer=.002)
            g.box("FeedBridge",(.036,.111,.026),(.0655,.2465,gun_z+.005),m[dark],
                  rot=(0,0,-math.atan2(.049,.071)),chamfer=.004)

        def pose(stow=False):
            for name,angle in (("WingL",90),("WingR",-90)):
                g.bpy.data.objects[name].rotation_euler.z=math.radians(angle) if stow else 0
            g.bpy.data.objects["GunYaw"].rotation_euler.z=0 if stow else math.radians(65)
            g.bpy.data.objects["GunPitch"].rotation_euler.x=0 if stow else math.radians(-35)
            g.bpy.data.objects["Propeller"].rotation_euler.y=0 if stow else math.radians(35)
            g.bpy.context.view_layer.update()

        return base((.43,.46,.43),(-.58,.86),
                    hatches=[(-.52,-.12,-.095,.095,True)],
                    stencils=[("GLAIVE / GD-7",-.48,.35,-.010,.026,.13,(.12,.15,.16))],
                    anchors=[("GlaiveMuzzle","GunPitch",(0,.716,gun_z)),
                             ("GlaiveBreech","GunPitch",(0,.215,gun_z))],pose=pose,
                    view=(1,.8,.40),pose_view=(1,.8,.34),glaive_polish=True)

    return {"Remora":remora,"AegisPod":aegis,"Lance":lance,"Glaive":glaive}
