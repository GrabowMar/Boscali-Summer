# AIRBORNE HALO jumper + ram-air canopy (Boscali Summer). Headless build:
#   blender -b --python BuildAirborne.py -- <outdir>
# Rips the vanilla gun-nest soldier (static, seated MG gunner) from resources.assets with UnityPy,
# fits a 16-bone armature to it, skins it by bone heat, stands it up, dresses it as a HALO wingsuit
# trooper and bakes every pose as a blend shape on one mesh: no bones ship, the mod just blends
# shape weights. Writes AirborneJumper.fbx,
# AirborneCanopy.fbx, their .blend files and preview renders.
# Blender frame: forward +Y, up +Z, starboard +X, metres. unity = (x, z, y).
import bpy
import bmesh
import math
import os
import sys
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

sys.path.insert(0, os.environ.get("NOMOD_BLENDER_PYLIBS", os.path.join(
    os.environ.get("LOCALAPPDATA", ""), "nomodkit", "blender-pylibs")))
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

GAME = os.environ.get("NO_GAME_DATA", r"C:\Program Files (x86)\Steam\steamapps\common\Nuclear Option\NuclearOption_Data")
OUT = sys.argv[sys.argv.index("--") + 1] if "--" in sys.argv else os.getcwd()
os.makedirs(OUT, exist_ok=True)


SOURCE_MESH = "emplacement1_MG_crew1"  # the seated MG-nest gunner: helmet, NVG, plate carrier, radio


def u2b(v):
    return Vector((v[0], v[2], v[1]))


# ================================================================ rip

def rip_soldier():
    """Static mesh of the gun-nest soldier (shared `soldier1` material)."""
    env = UnityPy.load(os.path.join(GAME, "resources.assets"))
    for obj in env.objects:
        if obj.type.name != "Mesh":
            continue
        mesh = obj.read()
        if mesh.m_Name != SOURCE_MESH:
            continue
        h = MeshHandler(mesh)
        h.process()
        verts = [u2b(v) for v in h.m_Vertices]
        return {
            "verts": [v - PELVIS for v in verts],
            "uv": [tuple(v) for v in h.m_UV0],
            "tris": [tuple(t) for sm in h.get_triangles() for t in sm],
        }
    raise RuntimeError(SOURCE_MESH + " not found in resources.assets")


# Joints read off orthographic renders of the seated gunner (Blender frame, before re-centring).
PELVIS = Vector((0, -1.17, 0.42))
JOINTS = {
    "pelvis": ((0, -1.17, 0.42), (0, -1.16, 0.62), None),
    "chest": ((0, -1.16, 0.62), (0, -1.12, 0.98), "pelvis"),
    "neck": ((0, -1.12, 0.98), (0, -1.1, 1.07), "chest"),
    "head": ((0, -1.1, 1.07), (0, -1.08, 1.32), "neck"),
    "upperarm_R": ((0.2, -1.1, 0.92), (0.3, -0.98, 0.75), "chest"),
    "forearm_R": ((0.3, -0.98, 0.75), (0.21, -0.73, 0.86), "upperarm_R"),
    "hand_R": ((0.21, -0.73, 0.86), (0.2, -0.62, 0.9), "forearm_R"),
    "thigh_R": ((0.11, -1.17, 0.4), (0.165, -0.76, 0.29), "pelvis"),
    "shin_R": ((0.165, -0.76, 0.29), (0.17, -0.7, -0.08), "thigh_R"),
    "foot_R": ((0.17, -0.7, -0.08), (0.17, -0.52, -0.18), "shin_R"),
}
for _n in [n for n in JOINTS if n.endswith("_R")]:
    _h, _t, _p = JOINTS[_n]
    JOINTS[_n[:-2] + "_L"] = ((-_h[0], _h[1], _h[2]), (-_t[0], _t[1], _t[2]), _p[:-2] + "_L" if _p.endswith("_R") else _p)

# Stands the seated gunner up (thighs down, knees straight, arms hanging) before any pose.
UNSEAT = {"thigh": [("X", -75)], "shin": [("X", 75)], "upperarm": [("X", -35), ("Y", -8)], "forearm": [("X", -79)]}


def build_rig(data):
    """Armature fitted to the gunner, plus the body skinned by bone heat."""
    arm_data = bpy.data.armatures.new("JumperRig")
    rig = bpy.data.objects.new("JumperRig", arm_data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    for name, (head, tail, _) in JOINTS.items():
        eb = arm_data.edit_bones.new(name)
        eb.head = Vector(head) - PELVIS
        eb.tail = Vector(tail) - PELVIS
    for name, (_, _, parent) in JOINTS.items():
        if parent:
            arm_data.edit_bones[name].parent = arm_data.edit_bones[parent]
            arm_data.edit_bones[name].use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")

    me = bpy.data.meshes.new("Body")
    # Swapping two axes mirrors handedness, so reverse each triangle to keep it outward.
    me.from_pydata([tuple(v) for v in data["verts"]], [], [(t[0], t[2], t[1]) for t in data["tris"]])
    me.validate()
    uv = me.uv_layers.new(name="UVMap")
    for loop in me.loops:
        uv.data[loop.index].uv = data["uv"][loop.vertex_index]
    body = bpy.data.objects.new("Body", me)
    bpy.context.collection.objects.link(body)

    # Bone heat on a welded copy (UV seams split the ripped mesh into islands), then copy the
    # weights back by position so seam twins deform together.
    weld = bpy.data.objects.new("Weld", me.copy())
    bpy.context.collection.objects.link(weld)
    bm = bmesh.new()
    bm.from_mesh(weld.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bm.to_mesh(weld.data)
    bm.free()
    bpy.ops.object.select_all(action="DESELECT")
    weld.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    names = {g.index: g.name for g in weld.vertex_groups}
    tree = KDTree(len(weld.data.vertices))
    for v in weld.data.vertices:
        tree.insert(v.co, v.index)
    tree.balance()
    groups = {n: body.vertex_groups.new(name=n) for n in JOINTS}
    missing = 0
    for v in me.vertices:
        # Bone heat leaves a few isolated verts unweighted; borrow the nearest weighted neighbour.
        ws = []
        for _, wi, dist in tree.find_n(v.co, 16):
            ws = [(names[g.group], g.weight) for g in weld.data.vertices[wi].groups]
            if ws:
                break
        if not ws:
            missing += 1
        for n, w in ws:
            groups[n].add([v.index], w, "REPLACE")
    print("WEIGHTS welded=%d body=%d unmatched=%d" % (len(weld.data.vertices), len(me.vertices), missing))
    bpy.data.objects.remove(weld)
    body.parent = rig
    body.modifiers.new("Armature", "ARMATURE").object = rig
    return rig, body


# ================================================================ materials

def material(name, rgb, metal=0.0, rough=0.7):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Metallic"].default_value = metal
    bsdf.inputs["Roughness"].default_value = rough
    mat.diffuse_color = (*rgb, 1.0)
    return mat


# Names are the contract with AirborneBuilder.cs (Body takes the vanilla pilot textures).
BODY, GEAR, DARK, WING = "AB_Body", "AB_Gear", "AB_Dark", "AB_Wing"
CANOPY, CANOPY_DARK, LINES = "AB_Canopy", "AB_CanopyDark", "AB_Lines"


def materials():
    return {
        BODY: material(BODY, (0.22, 0.27, 0.2)),
        GEAR: material(GEAR, (0.36, 0.33, 0.24)),
        DARK: material(DARK, (0.045, 0.045, 0.05), 0.3, 0.55),
        WING: material(WING, (0.11, 0.13, 0.11), 0.0, 0.6),
        CANOPY: material(CANOPY, (0.27, 0.3, 0.2), 0.0, 0.8),
        CANOPY_DARK: material(CANOPY_DARK, (0.06, 0.065, 0.06), 0.0, 0.9),
        LINES: material(LINES, (0.6, 0.6, 0.55), 0.0, 0.8),
    }


# ================================================================ gear

class Part:
    """Accumulates geometry with per-vertex bone weights and per-face material."""

    def __init__(self, name):
        self.name, self.verts, self.faces, self.mats, self.weights = name, [], [], [], []

    def add(self, verts, faces, mat, weights):
        base = len(self.verts)
        self.verts += [Vector(v) for v in verts]
        self.weights += weights if isinstance(weights, list) else [weights] * len(verts)
        for f in faces:
            self.faces.append(tuple(base + i for i in f))
            self.mats.append(mat)

    def box(self, center, size, mat, bone, rot=None, bevel=0.0):
        sx, sy, sz = (s * 0.5 for s in size)
        corners = [Vector((x * sx, y * sy, z * sz)) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
        if bevel > 0:
            # Chamfered edges read as padded webbing, not crates.
            bm = bmesh.new()
            vs = [bm.verts.new(c) for c in corners]
            for f in faces:
                bm.faces.new([vs[i] for i in f])
            bmesh.ops.bevel(bm, geom=bm.verts[:] + bm.edges[:], offset=bevel, segments=1, affect="EDGES")
            bm.verts.index_update()
            corners = [v.co.copy() for v in bm.verts]
            faces = [tuple(v.index for v in f.verts) for f in bm.faces]
            bm.free()
        rm = rot.to_matrix() if rot else Matrix.Identity(3)
        self.add([Vector(center) + rm @ c for c in corners], faces, mat, {bone: 1.0})

    def cyl(self, a, b, r, mat, bone, n=8, cap=True):
        a, b = Vector(a), Vector(b)
        axis = (b - a).normalized()
        side = axis.orthogonal().normalized()
        up = axis.cross(side)
        ring = [side * math.cos(2 * math.pi * k / n) * r + up * math.sin(2 * math.pi * k / n) * r for k in range(n)]
        faces = [(k, (k + 1) % n, n + (k + 1) % n, n + k) for k in range(n)]
        if cap:
            faces += [tuple(reversed(range(n))), tuple(range(n, 2 * n))]
        self.add([a + o for o in ring] + [b + o for o in ring], faces, mat, {bone: 1.0})

    def membrane(self, edge_a, edge_b, wa, wb, mat, cols=4, thick=0.012, push=Vector()):
        """Closed thin sheet between two polylines; weights blend from wa[i] to wb[i] across it."""
        rows = len(edge_a)
        verts, weights = [], []
        for side in (-1, 1):
            for i in range(rows):
                for j in range(cols + 1):
                    t = j / cols
                    verts.append(edge_a[i].lerp(edge_b[i], t) + push * math.sin(math.pi * t) + Vector((0, side * thick * 0.5, 0)))
                    w = {}
                    for k, v in wa[i].items():
                        w[k] = w.get(k, 0) + v * (1 - t)
                    for k, v in wb[i].items():
                        w[k] = w.get(k, 0) + v * t
                    weights.append(w)
        n = rows * (cols + 1)
        idx = lambda s, i, j: s * n + i * (cols + 1) + j
        faces = []
        for i in range(rows - 1):
            for j in range(cols):
                faces.append((idx(0, i, j), idx(0, i + 1, j), idx(0, i + 1, j + 1), idx(0, i, j + 1)))
                faces.append((idx(1, i, j), idx(1, i, j + 1), idx(1, i + 1, j + 1), idx(1, i + 1, j)))
        for i in range(rows - 1):
            faces.append((idx(0, i, 0), idx(1, i, 0), idx(1, i + 1, 0), idx(0, i + 1, 0)))
            faces.append((idx(0, i, cols), idx(0, i + 1, cols), idx(1, i + 1, cols), idx(1, i, cols)))
        self.add(verts, faces, mat, weights)

    def to_object(self, mats):
        me = bpy.data.meshes.new(self.name)
        me.from_pydata([tuple(v) for v in self.verts], [], self.faces)
        names = list(dict.fromkeys(self.mats))
        for n in names:
            me.materials.append(mats[n])
        for poly, m in zip(me.polygons, self.mats):
            poly.material_index = names.index(m)
        me.validate()
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.collection.objects.link(obj)
        groups = {}
        for vi, w in enumerate(self.weights):
            total = sum(w.values()) or 1.0
            for bone, wt in w.items():
                if bone not in groups:
                    groups[bone] = obj.vertex_groups.new(name=bone)
                groups[bone].add([vi], wt / total, "REPLACE")
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.to_mesh(me)
        bm.free()
        return obj


def measure(body, lo_z, hi_z, x_max=0.12):
    """Front/back y of the torso slab between two heights."""
    ys = [v.co.y for v in body.data.vertices if lo_z < v.co.z < hi_z and abs(v.co.x) < x_max]
    return max(ys), min(ys)


def head_of(rig, bone):
    return rig.data.bones[bone].head_local.copy()


def dress(rig, body, kit):
    g = Part("Gear")
    # The gunner already wears helmet, NVG, plate carrier, belt pouches and a back radio.
    front, back = measure(body, 0.2, 0.45)
    hip_front, _ = measure(body, -0.15, 0.05, 0.15)
    print("MEASURE chest front %.3f back %.3f hip front %.3f" % (front, back, hip_front))
    up = Vector((0, 0, 1))
    if kit == "assault":
        # Fast-rope/rappel trooper: carbine slung diagonally across the back, rappel harness.
        a, b = Vector((0.2, back - 0.07, 0.5)), Vector((-0.22, back - 0.07, -0.05))
        d = (b - a).normalized()
        q = up.rotation_difference(d)
        g.box(a.lerp(b, 0.45), (0.04, 0.06, 0.42), DARK, "chest", rot=q)
        g.box(a.lerp(b, 0.05), (0.035, 0.08, 0.2), DARK, "chest", rot=q)
        g.cyl(a.lerp(b, 0.62), b + d * 0.08, 0.011, DARK, "chest", n=6)
        g.box(a.lerp(b, 0.4) + Vector((0, -0.05, 0)), (0.03, 0.09, 0.07), DARK, "chest", rot=q)
        g.cyl(Vector((0.2, front + 0.02, 0.5)), a, 0.008, GEAR, "chest", n=4)
        for side in (-1, 1):
            g.cyl((side * 0.1, hip_front - 0.02, -0.08), (side * 0.17, hip_front - 0.12, -0.12), 0.016, DARK, "pelvis", n=6)
        g.cyl((0, hip_front + 0.01, -0.02), (0, hip_front + 0.05, -0.06), 0.012, DARK, "pelvis", n=6)
        return g

    # Parachute rig: container on the back, risers over the shoulders, leg straps, ripcord.
    rig_back = back - 0.05
    g.box((0, rig_back - 0.09, 0.27), (0.36, 0.18, 0.5), WING, "chest", bevel=0.03)
    g.box((0, rig_back - 0.185, 0.36), (0.3, 0.02, 0.22), GEAR, "chest", bevel=0.006)
    g.box((0, rig_back - 0.06, 0.55), (0.3, 0.12, 0.08), DARK, "chest", bevel=0.02)
    for side in (-1, 1):
        g.box((side * 0.11, (front + back) * 0.5 - 0.01, 0.52), (0.045, front - back + 0.03, 0.025), DARK, "chest")
        g.cyl((side * 0.12, back - 0.08, 0.05), (side * 0.12, rig_back - 0.15, 0.06), 0.018, DARK, "pelvis", n=6)
    g.cyl((0.16, front + 0.02, 0.15), (0.16, front + 0.06, 0.15), 0.022, DARK, "chest", n=6)
    g.cyl((-0.21, rig_back - 0.06, 0.12), (-0.21, rig_back - 0.06, 0.42), 0.045, DARK, "chest", n=10)
    g.cyl((-0.21, rig_back - 0.06, 0.42), (-0.21, rig_back - 0.06, 0.46), 0.018, GEAR, "chest", n=6)

    # Carbine strapped muzzle-down along the left flank.
    a, b = Vector((-0.235, front - 0.02, 0.43)), Vector((-0.215, front + 0.03, -0.32))
    d = (b - a).normalized()
    q = up.rotation_difference(d)
    g.box(a.lerp(b, 0.45), (0.04, 0.06, 0.42), DARK, "chest", rot=q)
    g.box(a.lerp(b, 0.05), (0.035, 0.08, 0.2), DARK, "chest", rot=q)
    g.cyl(a.lerp(b, 0.62), b + d * 0.08, 0.011, DARK, "chest", n=6)
    g.box(a.lerp(b, 0.4) + Vector((0, 0.06, 0)), (0.03, 0.09, 0.07), DARK, "chest", rot=q)
    g.box(a.lerp(b, 0.42), (0.028, 0.03, 0.16), GEAR, "chest", rot=q)

    # Combat ruck front-mounted under the reserve, hanging on the thighs.
    g.box((0, hip_front + 0.13, -0.24), (0.36, 0.2, 0.32), GEAR, "pelvis", bevel=0.035)
    g.box((0, hip_front + 0.235, -0.2), (0.28, 0.03, 0.16), GEAR, "pelvis", bevel=0.01)
    for side in (-1, 1):
        g.box((side * 0.12, hip_front + 0.06, -0.06), (0.03, 0.12, 0.03), DARK, "pelvis")

    # Jump strobe on the helmet crown.
    head = head_of(rig, "head")
    top = max(v.co.z for v in body.data.vertices)
    g.box((0, head.y - 0.03, top + 0.005), (0.03, 0.05, 0.025), DARK, "head", bevel=0.005)

    # Wrist altimeter.
    fl, hl = head_of(rig, "forearm_L"), head_of(rig, "hand_L")
    g.box(fl.lerp(hl, 0.75) + Vector((-0.03, 0.02, 0)), (0.04, 0.05, 0.05), DARK, "forearm_L", bevel=0.008)

    # Ankle smoke canisters: each fireteam's glide leaves a coloured streak.
    for side, shin, foot in ((-1, "shin_L", "foot_L"), (1, "shin_R", "foot_R")):
        c = head_of(rig, foot) + Vector((side * 0.075, -0.02, 0.12))
        g.cyl(c - Vector((0, 0, 0.08)), c + Vector((0, 0, 0.08)), 0.03, DARK, shin, n=8)
        g.cyl(c - Vector((0, 0, 0.1)), c - Vector((0, 0, 0.08)), 0.018, GEAR, shin, n=6)

    # Wingsuit: arm wings from wrist to hip, tail wing between the legs.
    for side, s in (("L", -1), ("R", 1)):
        sh, el, wr = head_of(rig, "upperarm_" + side), head_of(rig, "forearm_" + side), head_of(rig, "hand_" + side)
        arm = [sh.lerp(el, t) for t in (0.25, 0.55, 0.85)] + [el.lerp(wr, t) for t in (0.2, 0.55, 0.95)]
        torso = [Vector((s * 0.16, 0.0, z)) for z in (0.42, 0.34, 0.25, 0.16, 0.06, -0.06)]
        wa = [{"upperarm_" + side: 1.0}] * 3 + [{"forearm_" + side: 1.0}] * 3
        wb = [{"chest": 1.0}] * 4 + [{"chest": 0.5, "pelvis": 0.5}, {"pelvis": 1.0}]
        g.membrane([p + Vector((0, -0.02, 0)) for p in arm], [p + Vector((0, -0.02, 0)) for p in torso], wa, wb, WING,
                   cols=4, push=Vector((0, -0.03, 0)))
    legs = {}
    for side in ("L", "R"):
        th, sn, ft = head_of(rig, "thigh_" + side), head_of(rig, "shin_" + side), head_of(rig, "foot_" + side)
        legs[side] = [th.lerp(sn, t) for t in (0.2, 0.55, 0.9)] + [sn.lerp(ft, t) for t in (0.3, 0.65, 0.95)]
    ws = lambda side: [{"thigh_" + side: 1.0}] * 3 + [{"shin_" + side: 1.0}] * 3
    inset = lambda pts, s: [p + Vector((-s * 0.06, -0.035, 0)) for p in pts]
    g.membrane(inset(legs["L"], -1), inset(legs["R"], 1), ws("L"), ws("R"), WING, cols=6, push=Vector((0, -0.04, 0)))
    return g


# ================================================================ poses

# bone (or side-less base name) -> [(axis, degrees)] in armature space, written for the right side;
# left bones mirror Y/Z. Upright frame: the mod pitches the root flat for the glide.
POSES = {
    # Wingsuit track: arms swept out and back, legs spread to tension the tail wing, head up.
    "Track": {"upperarm": [("Y", 58), ("X", -14)], "forearm": [("Y", 4)], "hand": [("X", -10)],
              "thigh": [("Y", 13), ("X", -6)], "shin": [("X", -4)], "foot": [("X", -55)],
              "chest": [("X", 6)], "head": [("X", 62)]},
    # Ramp exit: hard arch, arms in a W, knees bent.
    "Exit": {"upperarm": [("Y", 88), ("X", -8)], "forearm": [("Y", 75)], "hand": [("Y", 10)],
             "thigh": [("Y", 10), ("X", -18)], "shin": [("X", -80)], "foot": [("X", -30)],
             "chest": [("X", 18)], "head": [("X", 40)]},
    # Under canopy, hands up on the toggles.
    "Hang": {"upperarm": [("Y", 155), ("X", 8)], "forearm": [("Y", 18)], "hand": [("X", 10)],
             "thigh": [("X", 12), ("Y", 3)], "shin": [("X", -22)], "foot": [("X", -25)],
             "head": [("X", 8)]},
    # Turns: inside toggle buried at the hip, outside hand up, legs swinging into the turn.
    "SteerL": {"upperarm_L": [("Y", 25), ("X", 15)], "forearm_L": [("X", 35)],
               "upperarm_R": [("Y", 150), ("X", 8)], "forearm_R": [("Y", 15)],
               "thigh": [("X", 10), ("Z", 8)], "shin": [("X", -25)], "foot": [("X", -25)],
               "chest": [("Z", 6)], "head": [("Z", 15)]},
    "SteerR": {"upperarm_R": [("Y", 25), ("X", 15)], "forearm_R": [("X", 35)],
               "upperarm_L": [("Y", 150), ("X", 8)], "forearm_L": [("Y", 15)],
               "thigh": [("X", 10), ("Z", -8)], "shin": [("X", -25)], "foot": [("X", -25)],
               "chest": [("Z", -6)], "head": [("Z", -15)]},
    # Flare: both toggles to the hips, knees together, legs out front.
    "Flare": {"upperarm": [("Y", 22), ("X", -12)], "forearm": [("X", 40)],
              "thigh": [("X", 38)], "shin": [("X", -38)], "foot": [("X", 10)],
              "chest": [("X", -6)], "head": [("X", -8)]},
    # Touchdown crouch, weapon hand coming up.
    "Land": {"upperarm": [("Y", 20), ("X", 38)], "forearm": [("X", 50)],
             "thigh": [("X", 62), ("Y", 6)], "shin": [("X", -100)], "foot": [("X", 38)],
             "chest": [("X", -24)], "head": [("X", 18)]},
}
HALO_POSES = POSES
ROPE_POSES = {
    # Fast-rope slide: gloved hands stacked on the rope at the face, boots clamping it.
    "Rope": {"upperarm": [("X", 125), ("Y", -12)], "forearm": [("X", 25)], "hand": [("X", 10)],
             "thigh": [("X", 28), ("Y", -4)], "shin": [("X", -45)], "foot": [("X", -20)],
             "head": [("X", -25)]},
    # Rappel: sat back in the harness, guide hand high, brake hand low behind the hip.
    "Rappel": {"upperarm_L": [("X", 110), ("Y", -10)], "forearm_L": [("X", 20)],
               "upperarm_R": [("X", -25), ("Y", 18)], "forearm_R": [("X", 30)],
               "thigh": [("X", 70), ("Y", 8)], "shin": [("X", -75)], "foot": [("X", 15)],
               "chest": [("X", 12)], "head": [("X", -20)]},
    # Security halt: right knee down, left foot planted, hands up at the weapon.
    "Kneel": {"upperarm_R": [("X", 60)], "upperarm_L": [("X", 60), ("Y", -22)], "forearm": [("X", 45)],
              "thigh_L": [("X", 85)], "shin_L": [("X", -88)],
              "thigh_R": [("X", -8)], "shin_R": [("X", -98)], "foot_R": [("X", -40)],
              "chest": [("X", -12)], "head": [("X", 8)]},
    # Two stride frames; the mod blends between them while the squad moves.
    "WalkA": {"upperarm": [("X", 40)], "forearm": [("X", 55)],
              "thigh_L": [("X", 28)], "shin_L": [("X", -12)], "thigh_R": [("X", -18)], "shin_R": [("X", -22)],
              "chest": [("X", -8)]},
    "WalkB": {"upperarm": [("X", 40)], "forearm": [("X", 55)],
              "thigh_R": [("X", 28)], "shin_R": [("X", -12)], "thigh_L": [("X", -18)], "shin_L": [("X", -22)],
              "chest": [("X", -8)]},
    # SPIES extraction: clipped to the rope by the harness, hands up on the line, legs hanging.
    "Ride": {"upperarm": [("X", 165), ("Y", -8)], "forearm": [("X", 10)],
             "thigh": [("X", 12), ("Y", 6)], "shin": [("X", -18)], "foot": [("X", -30)],
             "head": [("X", 12)]},
    "Land": POSES["Land"],
}
GROUNDED = {"Land", "Kneel", "WalkA", "WalkB"}


def steps_for(spec, name):
    if name in spec:
        return spec[name]
    base = name[:-2] if name[-2:] in ("_L", "_R") else name
    return spec.get(base)


def apply_pose(rig, spec):
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    order = []
    def walk(b):
        order.append(b)
        for c in b.children:
            walk(c)
    for b in rig.pose.bones:
        if b.parent is None:
            walk(b)
    for pb in order:
        steps = steps_for(spec, pb.name)
        if not steps:
            continue
        sgn = -1 if pb.name.endswith("_L") else 1
        rot = Matrix.Identity(4)
        for axis, deg in steps:
            a = math.radians(deg) * (sgn if axis in "YZ" else 1)
            # Y abduction runs the other way: the right arm swings out with a negative Y turn.
            if axis == "Y":
                a = -a
            rot = Matrix.Rotation(a, 4, axis) @ rot
        head = pb.head.copy()
        pb.matrix = Matrix.Translation(head) @ rot @ Matrix.Translation(-head) @ pb.matrix
        bpy.context.view_layer.update()


def bake_shapes(rig, obj, poses):
    """Basis = rest; one shape per pose, read from the evaluated armature deform."""
    obj.shape_key_add(name="Basis", from_mix=False)
    rest_low = min(v.co.z for v in obj.data.vertices)
    for name, spec in poses.items():
        for k in obj.data.shape_keys.key_blocks:
            k.value = 0.0
        obj.active_shape_key_index = 0
        apply_pose(rig, spec)
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        me = ev.to_mesh()
        co = [v.co.copy() for v in me.vertices]
        ev.to_mesh_clear()
        if name in GROUNDED:
            drop = min(c.z for c in co) - rest_low
            co = [c - Vector((0, 0, drop)) for c in co]
        key = obj.shape_key_add(name=name, from_mix=False)
        for v, c in zip(key.data, co):
            v.co = c
    for k in obj.data.shape_keys.key_blocks:
        k.value = 0.0
    apply_pose(rig, {})


# ================================================================ canopy

SPAN, CHORD, CELLS, HEIGHT = 7.4, 2.9, 9, 4.6
ARC = math.radians(38)  # half-angle of the spanwise arc


def airfoil(t):
    """Upper/lower offsets (fraction of chord) at chordwise t (0 = nose)."""
    t = min(max(t, 1e-6), 1.0)
    th = 0.15 * (1.4845 * math.sqrt(t) - 0.63 * t - 1.758 * t * t + 1.4215 * t ** 3 - 0.5075 * t ** 4)
    camber = 0.035 * math.sin(math.pi * t)
    return camber + th * 0.5, camber - th * 0.5


def canopy_point(s, t, upper, pillow):
    """s in [-1, 1] spanwise, t in [0, 1] chordwise -> position above the riser confluence."""
    up, lo = airfoil(t)
    off = (up if upper else lo) * CHORD
    cell = (s + 1) * 0.5 * CELLS
    off += math.sin(math.pi * (cell - math.floor(cell))) * pillow * (1 if upper else -0.6)
    ang = s * ARC
    radius = SPAN * 0.5 / ARC
    n = Vector((math.sin(ang), 0, math.cos(ang)))
    centre = Vector((0, 0, HEIGHT - radius))
    p = centre + n * radius + Vector((0, (0.42 - t) * CHORD, 0))
    return p + n * off


def build_canopy():
    p = Part("Canopy")
    ns, nt = CELLS * 4, 12
    ss = [-1 + 2 * i / ns for i in range(ns + 1)]
    ts = [0.5 - 0.5 * math.cos(math.pi * j / nt) for j in range(nt + 1)]
    ts[0] = 0.025  # cell mouths stay open at the nose
    for upper in (True, False):
        grid = [canopy_point(s, t, upper, 0.07) for s in ss for t in ts]
        faces = []
        for i in range(ns):
            for j in range(nt):
                a, b = i * (nt + 1) + j, (i + 1) * (nt + 1) + j
                faces.append((a, b, b + 1, a + 1) if upper else (a, a + 1, b + 1, b))
        p.add(grid, faces, CANOPY if upper else CANOPY_DARK, {"root": 1.0})
    for i in range(0, ns + 1, 4):  # ribs: dark walls visible inside each cell mouth
        s = ss[i]
        ring = [canopy_point(s, t, True, 0.0) for t in ts] + [canopy_point(s, t, False, 0.0) for t in ts]
        faces = [(j, j + 1, nt + 2 + j, nt + 1 + j) for j in range(nt)]
        p.add(ring, faces + [tuple(reversed(f)) for f in faces], CANOPY_DARK, {"root": 1.0})
    for i in range(ns):  # trailing edge
        a0, a1 = canopy_point(ss[i], 1.0, True, 0.07), canopy_point(ss[i + 1], 1.0, True, 0.07)
        b0, b1 = canopy_point(ss[i], 1.0, False, 0.07), canopy_point(ss[i + 1], 1.0, False, 0.07)
        p.add([a0, a1, b1, b0], [(0, 1, 2, 3), (3, 2, 1, 0)], CANOPY, {"root": 1.0})
    # Suspension lines A-D from every rib to the two riser confluences above the shoulders.
    risers = {-1: Vector((-0.2, 0.0, 0.95)), 1: Vector((0.2, 0.0, 0.95))}
    for i in range(0, ns + 1, 4):
        side = -1 if ss[i] < 0 else 1
        for t in (0.08, 0.32, 0.6, 0.88):
            p.cyl(risers[side], canopy_point(ss[i], t, False, 0.0), 0.009, LINES, "root", n=3, cap=False)
    # Slider and risers down to the harness (shoulders at z~0.55).
    p.box((0, 0.0, 1.35), (0.7, 0.55, 0.012), CANOPY_DARK, "root")
    for side in (-1, 1):
        p.cyl(risers[side], Vector((side * 0.12, -0.02, 0.5)), 0.016, DARK, "root", n=4)
    return p


def bake_canopy_shapes(obj):
    """Snivel (slider-held, half-open) and Packed (in the container) for the deployment blend."""
    obj.shape_key_add(name="Basis", from_mix=False)
    sn = obj.shape_key_add(name="Snivel", from_mix=False)
    for v, k in zip(obj.data.vertices, sn.data):
        c = v.co
        k.co = Vector((c.x * 0.3, c.y * 0.5 - abs(c.x) * 0.08, 0.95 + max(c.z - 0.95, 0) * 0.9 + min(c.z - 0.95, 0)))
    pack = Vector((0, -0.32, 0.4))
    pk = obj.shape_key_add(name="Packed", from_mix=False)
    for v, k in zip(obj.data.vertices, pk.data):
        k.co = pack + (v.co - pack) * 0.025
    # Blender 5 adds keys at weight 1; the FBX carries these as Unity's default blend weights.
    for k in obj.data.shape_keys.key_blocks:
        k.value = 0.0


# ================================================================ export + preview

def export_fbx(obj, name):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, name + ".fbx"), use_selection=True,
        axis_forward="-Z", axis_up="Y", apply_scale_options="FBX_SCALE_NONE",
        bake_space_transform=False, object_types={"MESH"}, use_mesh_modifiers=False,
        mesh_smooth_type="EDGE", use_tspace=False, use_triangles=False,
        use_custom_props=False, add_leaf_bones=False, bake_anim=False)


def preview(objs, tag, views, res=(1800, 700)):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sh = sc.display.shading
    sh.light, sh.studio_light, sh.color_type = "STUDIO", "Default", "MATERIAL"
    sh.show_cavity, sh.cavity_type = True, "BOTH"
    sh.show_object_outline = True
    sc.display.render_aa = "16"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.view_settings.view_transform = "Standard"
    sc.world = sc.world or bpy.data.worlds.new("W")
    sc.world.color = (0.72, 0.77, 0.82)
    for o in sc.objects:
        o.hide_render = o not in objs
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in objs for c in o.bound_box]
    lo = Vector([min(p[i] for p in pts) for i in range(3)])
    hi = Vector([max(p[i] for p in pts) for i in range(3)])
    cam_data = bpy.data.cameras.new("PCam")
    cam_data.type = "ORTHO"
    cam = bpy.data.objects.new("PCam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam
    ctr = (lo + hi) * 0.5
    for view, d in views.items():
        d = Vector(d).normalized()
        cam.location = ctr + d * 60
        el = math.asin(max(-1.0, min(1.0, d.z)))
        cam.rotation_euler = (math.pi / 2 - el, 0.0, math.atan2(d.x, -d.y))
        rot = cam.rotation_euler.to_matrix()
        right, upv = rot @ Vector((1, 0, 0)), rot @ Vector((0, 1, 0))
        ew = max(abs((p - ctr).dot(right)) for p in pts)
        eh = max(abs((p - ctr).dot(upv)) for p in pts)
        cam_data.ortho_scale = max(ew, eh * res[0] / res[1]) * 2.15
        sc.render.filepath = os.path.join(OUT, "%s_%s.png" % (tag, view))
        bpy.ops.render.render(write_still=True)
        print("RENDER", sc.render.filepath)
    bpy.data.objects.remove(cam)


def pose_copy(obj, shape, offset):
    me = obj.data.copy()
    d = bpy.data.objects.new("Sheet_" + shape, me)
    bpy.context.collection.objects.link(d)
    for k in me.shape_keys.key_blocks:
        k.value = 1.0 if k.name == shape else 0.0
    d.location = offset
    return d


def make_soldier(mats, name, kit, poses):
    """Rip, rig and stand up the gunner, dress him in `kit`, bake `poses`, export `name`.fbx."""
    data = rip_soldier()
    rig, body = build_rig(data)
    body.data.materials.append(mats[BODY])
    print("RIP_OK verts=%d tris=%d" % (len(data["verts"]), len(data["tris"])))
    # Stand the gunner up and make that the rest pose, so gear and poses work from standing.
    apply_pose(rig, UNSEAT)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier="Armature")
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="POSE")
    bpy.ops.pose.armature_apply()
    bpy.ops.object.mode_set(mode="OBJECT")
    body.modifiers.new("Armature", "ARMATURE").object = rig
    gear = dress(rig, body, kit).to_object(mats)
    gear.parent = rig
    gear.modifiers.new("Armature", "ARMATURE").object = rig
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    gear.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    soldier = body
    soldier.name = soldier.data.name = name
    bake_shapes(rig, soldier, poses)
    soldier.modifiers.clear()
    soldier.parent = None
    soldier.vertex_groups.clear()
    bpy.data.objects.remove(rig)
    for poly in soldier.data.polygons:
        poly.use_smooth = True
    print("SOLDIER_OK %s tris=%d shapes=%s mats=%s" % (name,
        sum(len(p.vertices) - 2 for p in soldier.data.polygons),
        [k.name for k in soldier.data.shape_keys.key_blocks], [m.name for m in soldier.data.materials]))
    export_fbx(soldier, name)
    return soldier


if __name__ == "__main__":
    bpy.ops.wm.read_factory_settings(use_empty=True)
    mats = materials()
    jumper = make_soldier(mats, "AirborneJumper", "halo", HALO_POSES)
    trooper = make_soldier(mats, "AirborneTrooper", "assault", ROPE_POSES)

    canopy = build_canopy().to_object(mats)
    canopy.name = canopy.data.name = "AirborneCanopy"
    canopy.vertex_groups.clear()
    bake_canopy_shapes(canopy)
    print("CANOPY_OK tris=%d" % sum(len(p.vertices) - 2 for p in canopy.data.polygons))
    export_fbx(canopy, "AirborneCanopy")
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "Airborne.blend"))

    if os.environ.get("AB_PREVIEW"):
        for model, tag in ((jumper, "Poses"), (trooper, "RopePoses")):
            shapes = [k.name for k in model.data.shape_keys.key_blocks[1:]]
            sheet = [pose_copy(model, s, Vector((i * 1.7, 0, 0))) for i, s in enumerate(shapes)]
            preview(sheet, tag, {"front": (0.25, 1, 0.15), "back": (-0.3, -1, 0.2)})
        canopy.hide_render = False
        hang = pose_copy(jumper, "Hang", Vector())
        preview([canopy, hang], "Canopy", {"front": (0.6, 1, 0.35), "side": (1, 0, 0), "under": (-0.4, 0.5, -0.6)}, res=(1400, 1000))
        for k in canopy.data.shape_keys.key_blocks[1:]:
            k.value = 1.0
            preview([canopy, hang], "Canopy" + k.name, {"front": (0.6, 1, 0.35)}, res=(900, 900))
            k.value = 0.0
