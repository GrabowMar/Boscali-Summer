"""GLAIVE-2A: fixed-boom-frame gun drone. Symmetric twin tail booms, aft-hinged fold-out wings, a pusher
propeller and a supported two-axis (yaw/pitch) belly gun with feed box. Named joints are runtime API
(GunYaw, GunPitch, WingL/R, Propeller; anchors GlaiveMuzzle/GlaiveBreech); see ValidateGlaiveAssembly.py."""
import math

NAME = "Glaive2A"
TEX = 2048  # atlas side in px
CLASS = "drone"  # munition | pod | drone | service: LOD0 triangle budget (models/__init__.py)
# joint mesh -> (parent joint or None, rest pivot in Blender metres or None); checked by ValidateVanguard
JOINTS = {
    "WingL": (None, (-0.255, -0.16, 0.175)),
    "WingR": (None, (0.255, -0.16, 0.175)),
    "GunYaw": (None, (0, 0.13, -0.102)),
    "GunPitch": ("GunYaw", (0, 0.13, -0.26)),
    "Propeller": (None, (0, -1.152, 0.055)),
}
UV_SHAPE = "AABB"  # pack: CONCAVE (default) | AABB (convex bounds, faster for many small islands)
DEPENDS = ()  # models whose .blend must be built first


def surface(S):
    """Surface-response hook (VanguardSurface.surface_layers): composite airframe, rubber seals and coated
    steel hardware have separate responses."""
    np, x, y, z, nrm = S.np, S.x, S.y, S.z, S.nrm
    base, metallic, smoothness, height, fine = S.base, S.metallic, S.smoothness, S.height, S.fine
    metal_region = S.metal_region
    wing=(np.abs(x)>.23)&(z>.095)&(z<.255)&(y>-.4)&(y<.5)
    smoothness[wing]=.27+fine[wing]*.025
    prop=(y<-1.105)&(y>-1.195)&(np.abs(x)<.35)&(z>-.035)&(z<.15)
    base[prop]=(.09,.11,.115); smoothness[prop]=.34
    tips=prop&(np.abs(x)>.30)
    base[tips]=(.64,.62,.47)
    gun=(z<-.173)&(np.abs(x)<.14)&(y>.02)&(y<.72)
    metal_region(gun,(.22,.245,.25),.43)
    barrel=gun&(y>.28)&(np.abs(x)<.02)
    metal_region(barrel,(.13,.155,.17),.49)
    seal=(z>-.144)&(z<-.133)&((x*x+(y-.13)**2)<.083**2)
    base[seal]=(.055,.07,.075); metallic[seal]=0; smoothness[seal]=.23
    pin=((np.abs(x)-.255)**2+(y+.16)**2<.0125**2)&(z>.18)
    metal_region(pin,(.13,.155,.17),.40)
    jacket=barrel&(y>.28)&(y<.415)&(np.abs(z+.26)<.008)&(np.abs(nrm[...,0])>.6)&(((y-.28)%.045)<.027)
    base[jacket]=(.025,.035,.040); height[jacket]-=.0007
    cooling=(np.abs(x)>.12)&(np.abs(x)<.18)&(y>-.67)&(y<-.43)&(z>.035)&(z<.075)&(np.abs(nrm[...,0])>.7)&(((y+.67)%.052)<.032)
    base[cooling]=(.035,.045,.05); height[cooling]-=.0006
    # Short top-only control-surface seams; avoid arbitrary rings across the wings.
    span=np.abs(x)
    edge=np.where(span<.97,.34-(span-.255)*(.16/.715),.18-(span-.97)*(.24/.75))
    chord=np.where(span<.97,.50-(span-.255)*(.16/.715),.34-(span-.97)*(.14/.75))
    hinge=edge-chord*.74
    seam=wing&(np.abs(x)>.82)&(np.abs(x)<1.59)&(nrm[...,2]>.6)&(np.abs(y-hinge)<.0013)
    height[seam]-=.0003; base[seam]*=.65
    fin=(np.abs(x)>.36)&(np.abs(x)<.55)&(y<-1.71)&(z>.10)&(np.abs(nrm[...,0])>.6)
    fin_le=-1.75-(z-.07)*(.19/.31)
    fin_chord=.31-(z-.07)*(.14/.31)
    rudder=fin&(np.abs(y-(fin_le-fin_chord*.75))<.0013)
    elevator=(np.abs(x)<.57)&(y<-1.71)&(z>.072)&(z<.088)&(nrm[...,2]>.7)&(np.abs(y+1.86)<.0013)
    tail_seam=rudder|elevator
    height[tail_seam]-=.00025; base[tail_seam]*=.65
    cover=(y>.35)&(y<.73)&(np.abs(x)<.066)&(z>.161)
    base[cover]*=.92; smoothness[cover]=.31


def build(g):
    m = g.mats(NAME)
    skin, dark, glass = g.SKIN, g.DARK, g.GLASS
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

    return {
        "color": (.43, .46, .43), "smooth": 0.33, "grime": 0.035,
        "lines": [g.plane_y(y) for y in (-.58, .86)], "line_width": 0.003,
        "hatches": [(-.52, -.12, -.095, .095, True)],
        "stencils": [("GLAIVE / GD-7", -.48, .35, -.010, .026, .13, (.12, .15, .16))],
        "anchors": [("GlaiveMuzzle", "GunPitch", (0, .716, gun_z)),
                    ("GlaiveBreech", "GunPitch", (0, .215, gun_z))],
        "pose": pose, "surface": surface,
        "view": (1, .8, .40), "pose_view": (1, .8, .34),
    }
