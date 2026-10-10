"""VGKIT - texture-detail toolkit for Vanguard props (numpy + Pillow only, no bpy).

BuildVanguard.compose() calls apply() after the legacy layers (colour/bands/patches/surface hook/stencils) whenever a
spec uses one of the OPTIONAL kit keys below. Every key is opt-in; a spec without them bakes exactly as before.

    relief      [dict]  baked height relief that reads in the normal map AND as baked crevice shading:
                        panel (flush / recessed / raised doubler + fastener border), hatch (recess + latches + hinge),
                        grille (louvre or slot array), perforation (hex hole grid), naca (flush intake), seam (straight
                        line + rivet rows), rivets (row), ring (circumferential seam + rivet ring)
    decals      [dict]  paint projected per surface: text, stripes, placard, arrow, triangle, nostep, rbf, rect, band
    weathering  dict    curvature edge wear, cavity grime, smoothness grunge, streaks, dust (needs the masks bake pass)
    heat        [dict]  temper tint (straw -> bronze -> blue -> soot) around hot hardware
    max_slope   float   normal-map slope clip (default 0.65 legacy, 1.1 once a kit key is present)

Placement ("frames"): a feature/decal sits in a local surface frame. `at` is a point near the surface, `dir` the
outward direction ("+x", "-z", or a vector); the frame snaps to the outermost skin texel on that ray. Local axes:
u = up x n (text reads left-to-right as seen from outside), v = n x u. `up` defaults to +Z (or +Y on top/bottom faces).
`wrap: True` measures the frame around a body of revolution (axis +Y through `axis_at` (x, z), default (0, 0)) so
large decals and hatches follow the curvature instead of smearing. `mirror: True` adds the -x twin, still readable.
Directional options (hinge, point, flow, along) are 3D directions, so mirrored twins stay geometrically correct.
Sizes are metres in the frame, (along u, along v). See MODELING_GUIDE.md. `python vgkit.py` runs the self-check.
"""
import functools
import math
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SIDES = {"+x": (1, 0, 0), "-x": (-1, 0, 0), "+y": (0, 1, 0), "-y": (0, -1, 0), "+z": (0, 0, 1), "-z": (0, 0, -1)}
KIT_KEYS = ("relief", "decals", "weathering", "heat")
POINT_KEYS = ("at",)
VECTOR_KEYS = ("dir", "up", "hinge", "point", "flow", "along", "latch")
FONT = os.path.join(os.environ.get("WINDIR", "C:/Windows"), "Fonts", "bahnschrift.ttf")
# Generic placard copy (fictional maintenance lines; at prop scale they read as tiny text texture).
PLACARD_LINES = ("SERVICE WITH GSE-14 ONLY", "TORQUE LATCHES 12 NM", "GROUND POINT  BOND HERE", "28 VDC  40 A MAX",
                 "INSPECT SEAL EACH SORTIE", "LOT 7741-B  REV C", "CAGE HEAD BEFORE TOW", "DESICCANT  CHANGE 90 D",
                 "REF TM-VG-0311 SEC 4", "STATIC SENSITIVE PARTS", "DO NOT PAINT THIS AREA", "LIFT AT MARKED POINTS")


def active(spec):
    return any(spec.get(k) for k in KIT_KEYS)


def wants_masks(spec):
    """Weathering needs the extra edge/cavity bake pass (Cycles bevel + AO nodes)."""
    return bool(spec.get("weathering"))


def max_slope(spec):
    """Normal-map slope clip. Capped at 1.2: steeper normals (tangent z < 0.5) fail ValidateVanguard."""
    return min(1.2, spec.get("max_slope", 1.1 if active(spec) else 0.65))


def unit(v):
    v = np.asarray(SIDES[v] if isinstance(v, str) else v, np.float64)
    return v / np.linalg.norm(v)


def mirrored(item):
    """The -x twin of a feature or decal: points and directions get x negated (text stays readable)."""
    out = {k: v for k, v in item.items() if k != "mirror"}
    for key in POINT_KEYS + VECTOR_KEYS + ("axis_at",):
        if out.get(key) is None:
            continue
        val = list(SIDES[out[key]] if isinstance(out[key], str) else out[key])
        val[0] = -val[0]
        out[key] = tuple(val)
    return out


def expand(items):
    for item in items or ():
        yield item
        if item.get("mirror"):
            yield mirrored(item)


def texel_size(pos, covered):
    """Texel edge in metres: sqrt of the median texel footprint (smart_project keeps islands at one density)."""
    steps = []
    for axis in (0, 1):
        a = np.moveaxis(pos, axis, 0)
        c = np.moveaxis(covered, axis, 0)
        d = np.linalg.norm(a[1:] - a[:-1], axis=-1)
        ok = c[1:] & c[:-1] & (d > 0)
        if not ok.any():
            return 0.002
        ok &= d < np.median(d[ok]) * 3
        steps.append(float(np.median(d[ok])))
    return math.sqrt(steps[0] * steps[1])


def neighbours(pos, covered, px):
    """Same-surface masks for the 4 texel neighbours (rejects UV island jumps and the roll wrap-around)."""
    out = []
    for axis in (0, 1):
        for sh in (1, -1):
            ok = covered & np.roll(covered, sh, axis)
            ok &= np.linalg.norm(np.roll(pos, sh, axis) - pos, axis=-1) < px * 2.5
            edge = 0 if sh == 1 else -1
            if axis == 0:
                ok[edge, :] = False
            else:
                ok[:, edge] = False
            out.append((axis, sh, ok.astype(np.float32)))
    return out


def blur(img, nb, iters):
    """Island-aware [1 2 1] blur repeated `iters` times (sigma ~ sqrt(iters / 2) texels)."""
    a = np.array(img, np.float32, copy=True)
    for _ in range(iters):
        for axis in (0, 1):
            acc, w = a * 2.0, np.full(a.shape[:2], 2.0, np.float32)
            for ax, sh, ok in nb:
                if ax == axis:
                    acc += np.roll(a, sh, ax) * (ok if a.ndim == 2 else ok[..., None])
                    w += ok
            a = acc / (w if a.ndim == 2 else w[..., None])
    return a


# ================================================================ frames

class Frame:
    """Local surface frame snapped onto the skin along a ray; see module docstring."""

    def __init__(self, K, item, facing=0.45):
        self.ok = False
        d = unit(item.get("dir") or _radial(item["at"]))
        at = np.asarray(item["at"], np.float64)
        tol = max(2.5 * K.px, item.get("snap", 0.0))
        # Ray search near `at` first (a Y slab: cheap on long bodies), the whole skin only if that finds nothing.
        for idx in (K.slab(at[1], 0.25 + tol), None):
            P = K.P if idx is None else K.P[idx]
            rel = P - at.astype(np.float32)
            t = rel @ d.astype(np.float32)
            off = rel - t[:, None] * d.astype(np.float32)
            perp2 = np.einsum("ij,ij->i", off, off)
            cand = (perp2 < tol * tol) & ((K.N if idx is None else K.N[idx]) @ d.astype(np.float32) > 0.2)
            if K.real is not None:  # never anchor on a bake-margin texel: between islands they hold blended positions
                cand &= K.real if idx is None else K.real[idx]
            if cand.any():
                break
        else:
            return
        k = int(np.argmax(np.where(cand, t, -np.inf)))
        dk = P - P[k]
        near = cand & (np.einsum("ij,ij->i", dk, dk) < 9 * tol * tol)
        self.o = at + d * float(t[k])  # centre stays on the authored ray even when `snap` widens the search
        N = K.N if idx is None else K.N[idx]
        n = N[near].mean(0).astype(np.float64) if perp2[k] < 9 * K.px ** 2 else d  # far snap: not our normal
        n = n / np.linalg.norm(n)
        # Default "up": +Z on flanks; on top/bottom faces text runs along the body (+Y), upright for a reader on
        # the starboard side (top) or seen from below with the nose to the right (bottom).
        up = unit(item["up"]) if item.get("up") is not None else             unit((0, 0, 1) if abs(n[2]) <= 0.8 else (-1, 0, 0) if n[2] > 0 else (1, 0, 0))
        u = np.cross(up, n)
        if np.linalg.norm(u) < 1e-3:  # up parallel to n: fall back to the body axis
            u = np.cross((0, 1, 0), n)
        u /= np.linalg.norm(u)
        v = np.cross(n, u)
        ang = math.radians(item.get("angle", 0.0))
        self.u, self.v = u * math.cos(ang) + v * math.sin(ang), -u * math.sin(ang) + v * math.cos(ang)
        self.n = n
        self.wrap = bool(item.get("wrap"))
        if self.wrap:
            ax, az = item.get("axis_at", (0.0, 0.0))[:2] if len(item.get("axis_at", (0, 0))) == 2 else \
                (item["axis_at"][0], item["axis_at"][2])
            self.c = np.array((ax, 0.0, az))
            self.ax = np.array((0.0, 1.0, 0.0))
            r0 = self.o - self.c
            self.s0 = r0 @ self.ax
            r0 = r0 - self.s0 * self.ax
            self.R0 = float(np.linalg.norm(r0))
            self.e1 = r0 / self.R0
            self.e2 = np.cross(self.ax, self.e1)
        self.depth = item.get("depth", 0.02 if self.wrap else 0.25 * max(item.get("size", (0.05, 0.05))) + 0.01)
        self.facing = item.get("facing", facing)  # cos of the steepest surface the projection may paint
        self.ok = True

    def dir2(self, D):
        """A 3D direction expressed in the frame's (u, v) plane, normalised."""
        D = unit(D)
        f = np.array((D @ self.u, D @ self.v))
        n = np.linalg.norm(f)
        return f / n if n > 1e-6 else np.array((1.0, 0.0))

    def select(self, K, radius):
        """Compact texel indices within `radius` of the anchor that face the frame, plus local (a, b) metres."""
        span = K.slab(self.o[1], radius)
        rel = K.P[span] - self.o.astype(np.float32)
        sel = np.flatnonzero(np.einsum("ij,ij->i", rel, rel) < radius * radius) + span.start
        rel, N = (K.P[sel] - self.o.astype(np.float32)).astype(np.float64), K.N[sel]
        if self.wrap:
            p = rel + (self.o - self.c)
            s = p @ self.ax
            r = p - s[:, None] * self.ax
            R = np.linalg.norm(r, axis=1)
            radial = r / np.maximum(R, 1e-9)[:, None]
            theta = np.arctan2(r @ self.e2, r @ self.e1)
            axial, arc = s - self.s0, theta * self.R0
            a = axial * (self.u @ self.ax) + arc * (self.u @ self.e2)
            b = axial * (self.v @ self.ax) + arc * (self.v @ self.e2)
            h = R - self.R0
            face = np.einsum("ij,ij->i", N, radial)
        else:
            a, b, h = rel @ self.u, rel @ self.v, rel @ self.n
            face = N @ self.n
        keep = (np.abs(h) < self.depth) & (face > self.facing)
        sel, a, b, h, face = sel[keep], a[keep], b[keep], h[keep], face[keep]
        if len(sel):
            # Depth test, like a projected decal: only the outermost surface under each (a, b) cell takes the
            # stamp, so nothing lands on a fairing, flank or underside lying beneath it within the depth window.
            cs = 1.5 * K.px
            ia, ib = np.floor(a / cs).astype(np.int64), np.floor(b / cs).astype(np.int64)
            ia -= ia.min()
            ib -= ib.min()
            key = ia * (int(ib.max()) + 1) + ib
            top = np.full(int(key.max()) + 1, -np.inf)
            real = K.real[sel] if K.real is not None else slice(None)  # margin texels never occlude the skin
            np.maximum.at(top, key[real], h[real])
            slack = 0.0015 + cs * 1.5 * np.sqrt(np.clip(1 - face ** 2, 0, 1)) / np.maximum(face, 0.2)
            vis = h >= top[key] - slack
            sel, a, b = sel[vis], a[vis], b[vis]
        return sel, a, b


def _radial(at):
    x, _, z = at
    return (x, 0.0, z) if abs(x) + abs(z) > 1e-6 else (0.0, 0.0, 1.0)


# ================================================================ kit state

class Kit:
    """Compact per-texel layers for the covered texels (M of them)."""

    def __init__(self, pos, nrm, covered, base, metal, smooth, inside=None):
        self.shape = covered.shape
        self.idx = np.flatnonzero(covered.ravel())
        # Compact texels ordered by Y: a feature's neighbourhood is then a contiguous slab (views, no gathers).
        self.idx = self.idx[np.argsort(pos.reshape(-1, 3)[self.idx, 1], kind="stable")]
        self.P = pos.reshape(-1, 3)[self.idx].astype(np.float32)
        self.N = nrm.reshape(-1, 3)[self.idx].astype(np.float32)
        self.px = texel_size(pos, covered)
        du, dv = np.roll(pos, -1, 1) - pos, np.roll(pos, -1, 0) - pos
        area = np.linalg.norm(np.cross(du, dv), axis=-1)
        ok = covered & np.roll(covered, -1, 1) & np.roll(covered, -1, 0)
        ok &= (area > 0.05 * self.px ** 2) & (area < 6 * self.px ** 2)
        ok[-1, :] = ok[:, -1] = False
        self.area = np.where(ok, area, self.px ** 2).reshape(-1)[self.idx].astype(np.float32)
        self.real = None  # texels inside UV triangles (no bake margin); None = unknown, every texel counts
        if inside is not None:  # bake-margin texels duplicate surface elsewhere: keep them out of the fit metric
            self.real = inside.reshape(-1)[self.idx]
            self.area *= self.real
        self.base = base.reshape(-1, 3)[self.idx].copy()
        self.metal = metal.reshape(-1)[self.idx].copy()
        self.smooth = smooth.reshape(-1)[self.idx].copy()
        self.h = np.zeros(len(self.idx), np.float32)
        self.dark = np.zeros(len(self.idx), np.float32)
        self.warnings = []
        self.decal_fit = []
        self.ys = np.ascontiguousarray(self.P[:, 1])

    def slab(self, y, r):
        """Slice of the (Y-sorted) compact texels with |Y - y| < r: features touch a small part of a long prop."""
        lo, hi = np.searchsorted(self.ys, (y - r, y + r))
        return slice(int(lo), int(hi))

    def frame(self, item, radius, facing=0.45):
        F = Frame(self, item, facing)
        if not F.ok:
            self.warnings.append(f"{item.get('kind')} at {tuple(item['at'])}: ray along {item.get('dir')} hits no skin")
            return None
        sel, a, b = F.select(self, radius)
        return (F, sel, a, b) if len(sel) else None

    def full(self, values, fill=0.0):
        out = np.full(self.shape[0] * self.shape[1], fill, np.float32)
        out[self.idx] = values
        return out.reshape(self.shape)

    def paint(self, sel, weight, colour):
        w = np.clip(weight, 0, 1)[:, None]
        self.base[sel] = self.base[sel] * (1 - w) + np.asarray(colour, np.float32) * w

    def tone(self, sel, weight, factor):
        self.base[sel] *= (1 + (factor - 1) * np.clip(weight, 0, 1))[:, None]


# ================================================================ relief primitives (all lengths metres)

def sd_rect(a, b, hw, hh, r=0.0):
    """Signed distance to a rounded rectangle centred at the origin (negative inside)."""
    r = min(r, hw, hh)
    qa, qb = np.abs(a) - (hw - r), np.abs(b) - (hh - r)
    return np.hypot(np.maximum(qa, 0), np.maximum(qb, 0)) + np.minimum(np.maximum(qa, qb), 0) - r


def vgroove(sd, hw):
    """V-section groove: 1 on the line, 0 at half-width hw (a constant slope reads as a crisp machined edge)."""
    return np.clip(1 - np.abs(sd) / hw, 0, 1)


def fill(sd, w):
    """0 outside, 1 inside, with a linear chamfer ramp of width w."""
    return np.clip(-sd / w, 0, 1)


def studs(K, a, b, pts, r, kind="rivet"):
    """Height/dark for rivet domes, flush screws (cross slot) or hex bolts at local points."""
    h, dark = np.zeros(len(a), np.float32), np.zeros(len(a), np.float32)
    if not len(pts) or not len(a):
        return h, dark
    r = max(r, 1.6 * K.px)  # a dome under ~3 texels across turns into a '+' in the normal map
    P = np.asarray(pts, np.float64)
    d2 = np.full(len(a), np.inf)
    dx, dy = np.zeros(len(a)), np.zeros(len(a))
    for chunk in range(0, len(P), 64):
        Q = P[chunk:chunk + 64]
        ddx, ddy = a[:, None] - Q[:, 0], b[:, None] - Q[:, 1]
        dd = ddx * ddx + ddy * ddy
        k = dd.argmin(1)
        best = dd[np.arange(len(a)), k]
        better = best < d2
        d2 = np.where(better, best, d2)
        dx = np.where(better, ddx[np.arange(len(a)), k], dx)
        dy = np.where(better, ddy[np.arange(len(a)), k], dy)
    d = np.sqrt(d2)
    ring = np.clip(1 - np.abs(d - r) / (0.8 * K.px), 0, 1)
    if kind == "rivet":
        h += r * 0.45 * np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))
        dark = np.maximum(dark, ring * 0.45)
    elif kind == "screw":
        h -= 0.0004 * ring
        slot = (d < r * 0.78) & ((np.abs(dx) < max(0.18 * r, 0.5 * K.px)) | (np.abs(dy) < max(0.18 * r, 0.5 * K.px)))
        h -= 0.0005 * slot
        dark = np.maximum(dark, np.maximum(ring * 0.55, slot * 0.6))
    else:  # hex bolt head
        hexd = np.maximum.reduce([np.abs(dx * math.cos(t) + dy * math.sin(t)) for t in (0, math.pi / 3, 2 * math.pi / 3)])
        sd = hexd - r * 0.866
        h += 0.0018 * fill(sd, K.px)
        dark = np.maximum(dark, vgroove(sd, 0.9 * K.px) * 0.5)
    return h.astype(np.float32), dark.astype(np.float32)


def border_points(hw, hh, inset, pitch):
    """Fastener positions along the inside of a rectangle's border."""
    ax, ay = hw - inset, hh - inset
    if ax <= 0 or ay <= 0:
        return []
    nx, ny = max(2, int(round(2 * ax / pitch)) + 1), max(2, int(round(2 * ay / pitch)) + 1)
    xs, ys = np.linspace(-ax, ax, nx), np.linspace(-ay, ay, ny)
    pts = [(x, y) for x in xs for y in (-ay, ay)] + [(x, y) for y in ys[1:-1] for x in (-ax, ax)]
    return pts


def _edge(F, D, hw, hh):
    """Which rectangle edge a 3D direction points at: (local axis, sign)."""
    f = F.dir2(D)
    return ("a", math.copysign(1, f[0])) if abs(f[0]) >= abs(f[1]) else ("b", math.copysign(1, f[1]))


def _edge_coords(edge, a, b, hw, hh):
    """(across, along, edge length): across = 0 on the edge, positive outward."""
    axis, s = edge
    return (s * a - hw, b, 2 * hh) if axis == "a" else (s * b - hh, a, 2 * hw)


def relief_panel(K, item, hatch=False):
    w, hgt = item["size"]
    hw, hh = w / 2, hgt / 2
    got = K.frame(item, math.hypot(hw, hh) + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    px = K.px
    sd = sd_rect(a, b, hw, hh, item.get("radius", min(hw, hh) * 0.15))
    inside = fill(sd, max(item.get("chamfer", 0.0022), px))
    h = (item.get("raised", 0.0) - item.get("inset", 0.0015 if hatch else 0.0)) * inside
    groove = vgroove(sd, max(item.get("seam", 0.0032) / 2, 0.9 * px))
    h -= item.get("seam_depth", 0.0016) * groove
    dark = groove * 0.3
    if "tone" in item:
        K.tone(sel, inside, item["tone"])
    if "color" in item:
        K.paint(sel, inside, item["color"])
    if item.get("rivets"):
        hh_, dk = studs(K, a, b, border_points(hw, hh, item.get("rivet_inset", 0.010), item["rivets"]),
                        item.get("rivet_radius", 0.0032), "rivet")
        h, dark = h + hh_, np.maximum(dark, dk)
    if item.get("screws"):
        inset = item.get("screw_inset", 0.012)
        pitch = item["screws"] if not isinstance(item["screws"], bool) else 9.0
        hh_, dk = studs(K, a, b, border_points(hw, hh, inset, pitch), item.get("screw_radius", 0.0045), "screw")
        h, dark = h + hh_, np.maximum(dark, dk)
    if hatch:
        hinge = item.get("hinge")
        if hinge is not None:
            e, t, L = _edge_coords(_edge(F, hinge, hw, hh), a, b, hw, hh)
            rk = item.get("hinge_radius", 0.0045)
            prof = np.sqrt(np.clip(1 - ((e - rk * 0.15) / rk) ** 2, 0, 1)) * (np.abs(t) < L * 0.42)
            gap = (np.mod(t + L, item.get("knuckle", 0.032)) < max(0.0026, 1.1 * px)) * prof
            h += rk * 0.75 * prof * (1 - gap)
            dark = np.maximum(dark, gap * 0.55)
            K.tone(sel, prof, 0.86)
        n_latch = item.get("latches", 2)
        if n_latch:
            latch_dir = item.get("latch") or (tuple(-np.asarray(unit(hinge))) if hinge is not None else None)
            edge = _edge(F, latch_dir, hw, hh) if latch_dir is not None else ("b", 1.0)
            e, t, L = _edge_coords(edge, a, b, hw, hh)
            ll, lw = item.get("latch_size", (0.034, 0.014))
            for k in range(n_latch):
                tk = ((k + 1) / (n_latch + 1) - 0.5) * L
                sdl = sd_rect(e + ll * 0.22, t - tk, ll / 2, lw / 2, lw * 0.3)
                body = fill(sdl, px)
                slot = fill(sd_rect(e + ll * 0.22 - ll * 0.18, t - tk, ll * 0.17, lw * 0.26, lw * 0.13), px)
                h += 0.0017 * body - 0.0024 * slot - 0.0008 * vgroove(sdl, 0.9 * px)
                dark = np.maximum.reduce([dark, slot * 0.75, vgroove(sdl, 0.9 * px) * 0.45])
                K.tone(sel, body, 0.80)
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


def relief_grille(K, item):
    w, hgt = item["size"]
    hw, hh = w / 2, hgt / 2
    got = K.frame(item, math.hypot(hw, hh) + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    px = K.px
    f = F.dir2(item.get("along", "+y"))
    along_a = abs(f[0]) >= abs(f[1])
    Lc, Cc, Lh, Ch = (a, b, hw, hh) if along_a else (b, a, hh, hw)
    frame_w = item.get("frame", 0.007)
    n = item.get("slots", max(3, int(2 * (Ch - frame_w) / 0.016)))
    pitch = 2 * (Ch - frame_w) / n
    sw = pitch * item.get("open", 0.62)
    sd_out = sd_rect(a, b, hw, hh, item.get("radius", 0.006))
    h = item.get("frame_raise", 0.0012) * fill(sd_out, px)
    groove = vgroove(sd_out, max(0.0016, 0.9 * px))
    h -= 0.0015 * groove
    k = np.clip(np.floor((Cc + Ch - frame_w) / pitch), 0, n - 1)
    cc = Cc - (-Ch + frame_w + (k + 0.5) * pitch)
    sds = sd_rect(Lc, cc, Lh - frame_w, sw / 2, sw * 0.45)
    inside = fill(sds, px)
    s = np.clip((cc + sw / 2) / sw, 0, 1)
    if item.get("flip"):
        s = 1 - s
    depth = item.get("depth", 0.004)
    floor = -depth * (0.15 + 0.85 * s) if item.get("style", "louvre") == "louvre" else -depth
    h += floor * inside
    dark = np.maximum(groove * 0.3, inside * (0.40 + 0.55 * s))
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


def relief_perforation(K, item):
    w, hgt = item["size"]
    hw, hh = w / 2, hgt / 2
    got = K.frame(item, math.hypot(hw, hh) + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    px = K.px
    p = item.get("pitch", 0.012)
    rh = max(item.get("hole", p * 0.33), 0.9 * px)
    sd_out = sd_rect(a, b, hw, hh, item.get("radius", 0.008))
    rowh = p * 0.8660254
    row = np.round(b / rowh)
    off = np.mod(row, 2) * p / 2
    col = np.round((a - off) / p)
    d = np.hypot(a - (col * p + off), b - row * rowh)
    holes = fill(d - rh, px) * (sd_out < -item.get("margin", 0.006))
    groove = vgroove(sd_out, max(0.0016, 0.9 * px))
    h = -item.get("depth", 0.003) * holes - 0.0014 * groove + item.get("frame_raise", 0.0008) * fill(sd_out, px)
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], np.maximum(holes * 0.85, groove * 0.3))


def relief_naca(K, item):
    L, W = item["size"]
    got = K.frame(item, math.hypot(L, W) / 2 + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    px = K.px
    f = F.dir2(item.get("flow", "-y"))
    t = a * f[0] + b * f[1] + L / 2  # 0 at the upstream tip, L at the mouth
    c = -a * f[1] + b * f[0]
    tt = np.clip(t / L, 0, 1)
    half = W / 2 * (0.14 + 0.86 * tt ** 1.35)
    sd = np.maximum.reduce([np.abs(c) - half, -t, t - L])
    inside = fill(sd, px)
    D = item.get("depth", 0.014)
    h = -D * tt ** 1.15 * inside
    lip = (t > L) & (t < L + max(0.006, 2 * px)) & (np.abs(c) < W / 2 + 0.004)
    h += 0.0009 * lip - 0.0012 * vgroove(sd, 0.9 * px) * (t < L)
    dark = np.maximum(inside * (0.12 + 0.8 * tt ** 2.2), vgroove(sd, 0.9 * px) * 0.3)
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


def relief_seam(K, item):
    """Straight seam along local u (length size[0]) with optional rivet rows at +-offset."""
    L = item["size"][0]
    got = K.frame(item, L / 2 + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    px = K.px
    d = np.hypot(np.maximum(np.abs(a) - L / 2, 0), b)
    g = vgroove(d, max(item.get("seam", 0.0032) / 2, 0.9 * px))
    h = -item.get("seam_depth", 0.0016) * g
    dark = g * 0.3
    if item.get("rivets"):
        off, pitch = item.get("offset", 0.011), item["rivets"]
        n = max(2, int(round(L / pitch)) + 1)
        sides = {"both": (-1, 1), "+": (1,), "-": (-1,)}[item.get("rows", "both")]
        pts = [(x, s * off) for x in np.linspace(-L / 2 + pitch / 2, L / 2 - pitch / 2, n - 1) for s in sides]
        hh_, dk = studs(K, a, b, pts, item.get("rivet_radius", 0.0032), item.get("fastener", "rivet"))
        h, dark = h + hh_, np.maximum(dark, dk)
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


def relief_rivets(K, item):
    """A row of fasteners along local u (length size[0]); rows=2 adds a staggered second row `spacing` below."""
    L = item["size"][0]
    got = K.frame(item, L / 2 + 0.03)
    if got is None:
        return
    F, sel, a, b = got
    pitch = item.get("pitch", 0.03)
    n = max(2, int(round(L / pitch)) + 1)
    xs = np.linspace(-L / 2, L / 2, n)
    pts = [(x, 0.0) for x in xs]
    if item.get("rows", 1) == 2:
        pts += [(x + pitch / 2, -item.get("spacing", 0.012)) for x in xs[:-1]]
    h, dark = studs(K, a, b, pts, item.get("radius", 0.0032), item.get("fastener", "rivet"))
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


def _ring_coords(K, item):
    """(compact selection, axial offset from y, arc angle, radius) for texels of a body of revolution about +Y."""
    cx, cz = item.get("axis_at", (0.0, 0.0))
    reach = item.get("reach", 0.03)
    y0, y1 = (item["y"], item["y"]) if np.isscalar(item["y"]) else item["y"]
    s = K.P[:, 1]
    sel = np.flatnonzero((s > y0 - reach) & (s < y1 + reach))
    x, z = K.P[sel, 0] - cx, K.P[sel, 2] - cz
    R = np.hypot(x, z)
    face = (K.N[sel, 0] * x + K.N[sel, 2] * z) / np.maximum(R, 1e-9)
    keep = (R < item.get("rmax", 9.0)) & (R > item.get("rmin", 0.0)) & (face > item.get("facing", 0.75))
    ang = np.degrees(np.arctan2(z, x))
    for a0, a1 in item.get("skip", ()):  # angular sectors (deg, atan2(z, x)) left alone, e.g. under a fairing
        keep &= ~((ang > a0) & (ang < a1))
    sel, x, z, R = sel[keep], x[keep], z[keep], R[keep]
    return sel, s[sel], np.arctan2(z, x), R


def relief_ring(K, item):
    """Circumferential seam at y with optional rivet rings on both sides (pitch along the arc)."""
    sel, s, th, R = _ring_coords(K, item)
    if not len(sel):
        K.warnings.append(f"ring at y={item['y']}: no skin")
        return
    px = K.px
    e = s - item["y"]
    g = vgroove(e, max(item.get("seam", 0.0032) / 2, 0.9 * px))
    h = -item.get("seam_depth", 0.0016) * g
    dark = g * 0.3
    if item.get("rivets"):
        n = max(8, int(round(2 * math.pi * float(np.median(R)) / item["rivets"])))
        k = np.round(th / (2 * math.pi / n))
        arc = (th - k * 2 * math.pi / n) * R
        off = item.get("offset", 0.011)
        r = max(item.get("rivet_radius", 0.0032), 1.6 * px)
        for side in {"both": (-1, 1), "+": (1,), "-": (-1,)}[item.get("rows", "both")]:
            d = np.hypot(arc, e - side * off)
            h += r * 0.45 * np.sqrt(np.clip(1 - (d / r) ** 2, 0, 1))
            dark = np.maximum(dark, np.clip(1 - np.abs(d - r) / (0.8 * px), 0, 1) * 0.45)
    K.h[sel] += h
    K.dark[sel] = np.maximum(K.dark[sel], dark)


RELIEF = {"panel": relief_panel, "hatch": lambda K, it: relief_panel(K, it, hatch=True), "grille": relief_grille,
          "perforation": relief_perforation, "naca": relief_naca, "seam": relief_seam, "rivets": relief_rivets,
          "ring": relief_ring}


# ================================================================ decals (Pillow rasters sampled in the frame)

@functools.lru_cache(maxsize=64)
def font(size, style="Bold Condensed"):
    try:
        f = ImageFont.truetype(FONT, max(4, int(size)))
        try:
            f.set_variation_by_name(style)
        except Exception:
            pass
        return f
    except OSError:
        return ImageFont.load_default(size=max(4, int(size)))


def text_fit(draw, box, text, fill_rgba, style="Bold Condensed", align="center"):
    """Largest text that fits `box` (x0, y0, x1, y1) without stretching."""
    x0, y0, x1, y1 = box
    bw, bh = x1 - x0, y1 - y0
    size = max(4, int(bh * 1.3))
    for _ in range(3):
        f = font(size, style)
        l, t, r, b = draw.textbbox((0, 0), text, font=f)
        scale = min(bw / max(r - l, 1), bh / max(b - t, 1))
        if 0.97 < scale <= 1.0:
            break
        size = max(4, int(size * scale * 0.99))
    f = font(size, style)
    l, t, r, b = draw.textbbox((0, 0), text, font=f)
    x = {"left": x0, "right": x1 - (r - l)}.get(align, x0 + (bw - (r - l)) / 2)
    draw.text((x - l, y0 + (bh - (b - t)) / 2 - t), text, font=f, fill=fill_rgba)


def rgba(c, a=1.0):
    return tuple(int(round(255 * max(0.0, min(1.0, v)))) for v in tuple(c)[:3]) + (int(round(255 * a)),)


def raster(item, Wr, Hr, F):
    """RGBA Pillow image for one decal kind."""
    img = Image.new("RGBA", (Wr, Hr), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    kind = item["kind"]
    ink = item.get("color", (0.1, 0.11, 0.12))
    m = max(1, int(min(Wr, Hr) * 0.06))
    if kind == "text":
        if item.get("bg") is not None:
            d.rounded_rectangle((0, 0, Wr - 1, Hr - 1), radius=m, fill=rgba(item["bg"]))
            text_fit(d, (m * 2, m, Wr - m * 2, Hr - m), item["text"], rgba(ink), item.get("style", "Bold Condensed"),
                     item.get("align", "center"))
        else:
            text_fit(d, (0, 0, Wr, Hr), item["text"], rgba(ink), item.get("style", "Bold Condensed"),
                     item.get("align", "center"))
    elif kind == "stripes":
        c1, c2 = item.get("colors", ((0.86, 0.66, 0.10), (0.08, 0.08, 0.08)))
        d.rectangle((0, 0, Wr, Hr), fill=rgba(c1))
        p = max(2, int(round(item.get("pitch", 0.025) * Wr / item["size"][0])))
        for x in range(-Hr - p, Wr + Hr, p * 2) if item.get("style") != "chevron" else ():
            d.polygon([(x, Hr), (x + p, Hr), (x + p + Hr, 0), (x + Hr, 0)], fill=rgba(c2))
        if item.get("style") == "chevron":
            for y in range(-Wr - p, Hr + Wr, p * 2):
                d.polygon([(0, y + Wr / 2), (Wr / 2, y), (Wr, y + Wr / 2), (Wr, y + Wr / 2 + p), (Wr / 2, y + p),
                           (0, y + Wr / 2 + p)], fill=rgba(c2))
        if item.get("border") is not None:
            d.rectangle((0, 0, Wr - 1, Hr - 1), outline=rgba(item["border"]), width=max(1, m // 2))
    elif kind == "placard":
        bg = item.get("bg", (0.84, 0.83, 0.78))
        d.rectangle((0, 0, Wr - 1, Hr - 1), fill=rgba(bg), outline=rgba(ink), width=max(1, m // 2))
        th = int(Hr * item.get("title_height", 0.28))
        if item.get("title"):
            d.rectangle((0, 0, Wr - 1, th), fill=rgba(item.get("title_bg", (0.72, 0.12, 0.08))))
            text_fit(d, (m, m // 2, Wr - m, th - m // 2), item["title"], rgba(item.get("title_color", (0.95, 0.94, 0.9))),
                     "Bold")
        lines = item.get("lines", 3)
        texts = lines if isinstance(lines, (list, tuple)) else \
            [PLACARD_LINES[(hash(item.get("title", "")) + i * 5) % len(PLACARD_LINES)] for i in range(lines)]
        y0 = th + m
        lh = (Hr - y0 - m) / max(1, len(texts))
        for i, t in enumerate(texts):
            text_fit(d, (m * 2, int(y0 + i * lh + lh * 0.12), Wr - m * 2, int(y0 + (i + 1) * lh - lh * 0.12)), t,
                     rgba(ink), "SemiBold SemiCondensed", "left")
    elif kind == "triangle":
        bw = max(1, m)
        d.polygon([(Wr / 2, bw), (Wr - bw, Hr - bw), (bw, Hr - bw)], fill=rgba(item.get("bg", (0.88, 0.70, 0.10))),
                  outline=rgba(ink), width=bw)
        text_fit(d, (Wr * 0.35, Hr * 0.36, Wr * 0.65, Hr * 0.88), "!", rgba(ink), "Bold")
    elif kind == "nostep":
        bw = max(1, m // 2)
        d.rectangle((bw, bw, Wr - bw - 1, Hr - bw - 1), outline=rgba(ink), width=bw)
        text_fit(d, (m * 2, m * 2, Wr - m * 2, Hr - m * 2), item.get("text", "NO STEP"), rgba(ink))
    elif kind == "rbf":
        d.rounded_rectangle((0, 0, Wr - 1, Hr - 1), radius=m, fill=rgba(item.get("bg", (0.72, 0.08, 0.06))))
        text_fit(d, (m * 2, m, Wr - m * 2, Hr - m), item.get("text", "REMOVE BEFORE FLIGHT"),
                 rgba(item.get("color", (0.96, 0.95, 0.92))), "Bold Condensed")
    elif kind == "rect":
        d.rounded_rectangle((0, 0, Wr - 1, Hr - 1), radius=int(item.get("radius", 0) * Wr / item["size"][0]),
                            fill=rgba(ink) if item.get("fill", True) else None,
                            outline=rgba(item.get("outline", ink)) if item.get("outline") or not item.get("fill", True)
                            else None, width=max(1, m // 2))
    elif kind == "arrow":
        fdir = F.dir2(item.get("point", "+y"))  # (u, v): raster x = u, raster y = -v
        horizontal = abs(fdir[0]) >= abs(fdir[1])
        label = item.get("text")
        if horizontal:
            ab = (0, Hr * (0.5 if label else 0), Wr, Hr)
            tb = (0, 0, Wr, Hr * 0.46)
        else:
            ab = (0, 0, Wr * (0.4 if label else 1), Hr)
            tb = (Wr * 0.46, 0, Wr, Hr)
        x0, y0, x1, y1 = ab
        cx, cy, L, T = (x0 + x1) / 2, (y0 + y1) / 2, (x1 - x0) if horizontal else (y1 - y0), (y1 - y0) if horizontal else (x1 - x0)
        s = 1 if (fdir[0] > 0 if horizontal else fdir[1] < 0) else -1
        head, shaft = min(L * 0.35, T * 1.0), T * 0.28

        def P(along, across):
            return (cx + s * along, cy + across) if horizontal else (cx + across, cy + s * along)
        pts = [P(-L / 2, -shaft / 2), P(L / 2 - head, -shaft / 2), P(L / 2 - head, -T / 2), P(L / 2, 0),
               P(L / 2 - head, T / 2), P(L / 2 - head, shaft / 2), P(-L / 2, shaft / 2)]
        d.polygon(pts, fill=rgba(ink))
        if label:
            text_fit(d, tb, label, rgba(ink), "Bold Condensed", "center")
    else:
        raise ValueError(f"unknown decal kind {kind!r}")
    return img


def stamp(K, item):
    w, hgt = item["size"]
    got = K.frame(item, math.hypot(w, hgt) / 2 + 2 * K.px, facing=0.6)
    if got is None:
        return
    F, sel, a, b = got
    # Rasterise at ~6 px per texel, box-filter down to ~1.25 px per texel, then bilinear: crisp, not aliased.
    Wf, Hf = [int(min(4096, max(8, math.ceil(s / K.px * 6)))) for s in (w, hgt)]
    img = raster(item, Wf, Hf, F)
    Wr, Hr = [int(max(4, math.ceil(s / K.px * 1.25))) for s in (w, hgt)]
    img = img.resize((Wr, Hr), Image.Resampling.BOX)
    arr = np.asarray(img, np.float32) / 255.0
    arr[..., :3] *= arr[..., 3:4]  # premultiplied for filtering
    x = (a / w + 0.5) * Wr - 0.5
    y = (0.5 - b / hgt) * Hr - 0.5
    inb = (x > -1) & (x < Wr) & (y > -1) & (y < Hr)
    x0 = np.floor(x).astype(int)
    y0 = np.floor(y).astype(int)
    fx, fy = (x - x0)[:, None], (y - y0)[:, None]
    pad = np.pad(arr, ((1, 1), (1, 1), (0, 0)))
    xi, yi = np.clip(x0 + 1, 0, Wr), np.clip(y0 + 1, 0, Hr)
    xj, yj = np.clip(x0 + 2, 0, Wr + 1), np.clip(y0 + 2, 0, Hr + 1)
    s = (pad[yi, xi] * (1 - fx) * (1 - fy) + pad[yi, xj] * fx * (1 - fy) +
         pad[yj, xi] * (1 - fx) * fy + pad[yj, xj] * fx * fy) * inb[:, None]
    op = item.get("opacity", 1.0)
    al = np.clip(s[:, 3] * op, 0, 1)
    K.base[sel] = K.base[sel] * (1 - al[:, None]) + s[:, :3] * op
    K.metal[sel] *= 1 - al
    K.smooth[sel] += al * item.get("gloss", 0.04)
    expected = float(arr[..., 3].sum()) * (w * hgt) / (Wr * Hr)  # ink area, m2
    landed = float((al * K.area[sel]).sum()) / max(op, 1e-6)
    fit = landed / max(expected, 1e-6)
    label = item.get("text") or item.get("title") or item["kind"]
    K.decal_fit.append((label, round(fit, 2)))
    if fit < 0.75 or fit > 1.4:  # smart_project islands stretch ~+-25 %
        K.warnings.append(f"decal {label!r} at {tuple(item['at'])}: {fit:.0%} of its ink landed on the skin "
                          "(clipped, wrapped or doubled; try wrap=True, a smaller size or depth)")


def band(K, item):
    """Paint ring around a body of revolution between y0..y1 with optional pinstripe edges."""
    sel, s, th, R = _ring_coords(K, item)
    if not len(sel):
        K.warnings.append(f"band at y={item['y']}: no skin")
        return
    y0, y1 = item["y"]
    px = K.px
    w = np.clip(np.minimum(s - y0, y1 - s) / px + 0.5, 0, 1)
    K.paint(sel, w, item.get("color", (0.86, 0.66, 0.10)))
    K.metal[sel] *= 1 - w
    if item.get("edge"):
        col, ew = item["edge"]
        for e in (y0, y1):
            K.paint(sel, np.clip(1 - np.abs(s - e) / max(ew / 2, 0.6 * px), 0, 1), col)


def heat(K, item, noise):
    """Temper colours around hot hardware: straw -> bronze -> purple-blue -> soot at the centre."""
    at = np.asarray(item["at"], np.float32)
    r = item.get("radius", 0.2)
    if item.get("axis") is not None:  # distance along an axis (rings of tint down a nozzle)
        ax = unit(item["axis"]).astype(np.float32)
        dist = np.abs((K.P - at) @ ax)
    else:
        dist = np.linalg.norm(K.P - at, axis=1)
    sel = np.flatnonzero(dist < r)
    if not len(sel):
        return
    t = np.clip(1 - dist[sel] / r, 0, 1) * item.get("strength", 1.0)
    t = np.clip(t + (np.asarray(noise(K.P[sel], 30.0, 47), np.float32) - 0.5) * 0.14, 0, 1)
    stops = np.array([0.0, 0.25, 0.45, 0.62, 0.8, 1.0], np.float32)
    cols = np.array([(0, 0, 0), (0.66, 0.54, 0.30), (0.50, 0.32, 0.20), (0.34, 0.24, 0.40), (0.20, 0.27, 0.44),
                     (0.10, 0.10, 0.12)], np.float32)
    rgb = np.stack([np.interp(t, stops, cols[:, c]) for c in range(3)], -1)
    mix = np.clip(t / 0.2, 0, 1)[:, None] * item.get("opacity", 0.9)
    if item.get("metal", True):
        K.base[sel] = K.base[sel] * (1 - mix) + rgb * mix
        K.metal[sel] = np.maximum(K.metal[sel], np.clip(t * 3, 0, 1) * 0.9)
        K.smooth[sel] = K.smooth[sel] * (1 - mix[:, 0]) + (0.42 - 0.2 * t) * mix[:, 0]
    else:  # scorched paint: browns and blackens, goes matte
        K.base[sel] *= (1 - 0.75 * t)[:, None]
        K.base[sel] += np.array((0.05, 0.03, 0.01), np.float32) * t[:, None]
        K.smooth[sel] -= 0.2 * t


DECALS = {k: stamp for k in ("text", "stripes", "placard", "triangle", "nostep", "rbf", "rect", "arrow")}
DECALS["band"] = band


# ================================================================ apply

def apply(spec, pos, nrm, covered, base, metal, smooth, height, noise, masks=None, inside=None):
    """Run every kit layer. Returns (base, metal, smooth, height, shade, info); shade multiplies the final albedo
    (relief crevices = the baked-AO look). `masks` = (H, W, 3) from the masks bake pass: R = bevel-normal . normal,
    G = short-range AO (outside), B = short-range AO (inside, convexity). `inside` = texels inside UV triangles
    (no bake margin): the decal fit check counts only those, and only those anchor a frame or occlude in its depth test."""
    K = Kit(pos, nrm, covered, base, metal, smooth, inside)
    for item in expand(spec.get("relief")):
        RELIEF[item["kind"]](K, item)
    for item in expand(spec.get("decals")):
        DECALS[item["kind"]](K, item)
    for item in expand(spec.get("heat")):
        heat(K, item, noise)
    nb = neighbours(pos, covered, K.px)
    Hk = K.full(K.h)
    hb = blur(Hk, nb, 6)
    cav_h = np.clip((hb - Hk) / 0.0007, 0, 1)
    cvx_h = np.clip((Hk - hb) / 0.0007, 0, 1)
    dark = K.full(K.dark)
    base = base.copy()
    base.reshape(-1, 3)[K.idx] = K.base
    base *= (1 - 0.82 * dark)[..., None]
    metal = K.full(K.metal)
    smooth = K.full(K.smooth, 0.3)
    smooth -= dark * 0.18
    shade = 1 - 0.5 * cav_h
    W = spec.get("weathering")
    info = {"texel_m": K.px, "warnings": K.warnings, "decal_fit": K.decal_fit}
    if W:
        base, metal, smooth, shade = weather(W, pos, nrm, covered, base, metal, smooth, shade, cav_h, cvx_h, masks,
                                            noise, nb, K.px)
    smooth = np.where(covered, smooth, 0.3)
    return (np.clip(base, 0, 1).astype(np.float32), np.clip(metal, 0, 1).astype(np.float32),
            np.clip(smooth, 0.06, 0.92).astype(np.float32), (height + Hk).astype(np.float32), shade, info)


def weather(W, pos, nrm, covered, base, metal, smooth, shade, cav_h, cvx_h, masks, noise, nb, px):
    # Weathering noise has >= 9 mm wavelengths: on fine atlases (< 1.5 mm/texel) evaluate it at half resolution.
    f = 2 if px < 0.0015 else 1

    def noise(p, scale, seed, _noise=noise):
        if f == 1:
            return np.asarray(_noise(p, scale, seed), np.float32)
        small = np.asarray(_noise(p[::f, ::f], scale, seed), np.float32)
        return np.repeat(np.repeat(small, f, 0), f, 1)[:p.shape[0], :p.shape[1]]
    n1 = noise(pos, 6.0, 41)
    n2 = noise(pos, 28.0, 42)
    n3 = noise(pos, 110.0, 43)
    if masks is not None:
        mk = blur(masks, nb, 2)
        bevel = np.clip((1 - mk[..., 0]) * 9, 0, 1)
        cav_g = np.clip((1 - mk[..., 1]) * 1.5, 0, 1)
        convex = np.clip((1 - mk[..., 2]) * 2.5, 0, 1)
        edge_g = bevel * convex
    else:
        cav_g = edge_g = np.zeros(covered.shape, np.float32)
    # Edge wear: geometric convex edges + raised relief edges, broken up into chips.
    edge = np.maximum(edge_g, cvx_h * 0.85)
    amt = W.get("edge", 0.5)
    breakup = n2 * 0.55 + n3 * 0.45
    wear = np.clip((edge * (0.55 + 0.9 * breakup) - (1 - amt) * 0.7) * 2.2, 0, 1) * covered
    col = np.asarray(W.get("edge_color", (0.62, 0.62, 0.60)), np.float32)
    base = base * (1 - wear[..., None] * 0.8) + col * wear[..., None] * 0.8
    metal = np.maximum(metal, wear * W.get("edge_metal", 0.35))
    smooth = smooth * (1 - wear * 0.6) + 0.55 * wear * 0.6
    # Cavity grime: concave geometry (short AO) + relief crevices.
    cav = np.clip(np.maximum(cav_g, cav_h) * (0.6 + 0.6 * n2), 0, 1) * W.get("cavity", 0.5)
    grime = np.asarray(W.get("grime_color", (0.10, 0.09, 0.08)), np.float32)
    base = base * (1 - cav[..., None] * 0.5) + grime * cav[..., None] * 0.5
    smooth -= cav * 0.22
    # Smoothness grunge: blotches, rain streaks down the flanks, airflow streaks aft.
    g = W.get("grunge", 0.5)
    blot = n1 * 0.55 + n2 * 0.3 + n3 * 0.15 - 0.5
    rain = np.asarray(noise(pos * np.array((1.0, 1.0, 0.07), np.float32), 34.0, 44), np.float32)
    flow = np.asarray(noise(pos * np.array((1.0, 0.06, 1.0), np.float32), 30.0, 45), np.float32)
    side = np.clip(1 - np.abs(nrm[..., 2]) * 1.2, 0, 1)
    streak = np.clip((rain - 0.55) * 3, 0, 1) * side * W.get("streaks", 0.4) + \
        np.clip((flow - 0.6) * 3, 0, 1) * W.get("streaks", 0.4) * 0.6
    smooth += blot * g * 0.32 - streak * 0.18
    base *= (1 - streak * 0.10)[..., None]
    base *= (1 + blot * g * 0.06)[..., None]
    # Dust settles on up-facing surfaces and in their crevices.
    dust = np.clip((nrm[..., 2] - 0.35) / 0.65, 0, 1) * (0.35 + 0.65 * n1) * W.get("dust", 0.0)
    dust = np.clip(dust + cav * W.get("dust", 0.0) * 0.6, 0, 1)
    dcol = np.asarray(W.get("dust_color", (0.50, 0.47, 0.42)), np.float32)
    base = base * (1 - dust[..., None] * 0.45) + dcol * dust[..., None] * 0.45
    smooth -= dust * 0.25
    shade = shade * (1 - 0.35 * cav_g * W.get("cavity", 0.5))
    return base, metal, smooth, shade


# ================================================================ self-check (numpy + Pillow only)

def self_check():
    """Synthetic 'bake' of a 0.2 m cylinder plus a buried 0.19 m sleeve in the lower half of the atlas: readable
    decals on both flanks, decal fit, depth test (nothing paints the buried sleeve), relief normal energy."""
    import json
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    from VanguardSurface import detail_normal
    H, Wd = 640, 1024  # ~square texels: 2 m / 1024 along Y, 2 pi 0.2 m / 640 around
    ys = np.linspace(-1.0, 1.0, Wd, dtype=np.float32)
    th = np.linspace(-math.pi, math.pi, H, endpoint=False, dtype=np.float32)
    Y, T = np.meshgrid(ys, th)
    rad = np.full(T.shape, 0.2, np.float32)
    Y, T, rad = np.concatenate([Y, Y]), np.concatenate([T, T]), np.concatenate([rad, rad * 0.95])
    H *= 2
    pos = np.stack((rad * np.cos(T), Y, rad * np.sin(T)), -1).astype(np.float32)
    nrm = np.stack((np.cos(T), np.zeros_like(T), np.sin(T)), -1).astype(np.float32)
    covered = np.ones((H, Wd), bool)
    outer = np.arange(H) < H // 2

    def noise(p, scale, seed):
        return (np.sin((p * np.array((1.7, 0.9, 1.3), np.float32)).sum(-1) * scale + seed) + 1) * 0.5
    text = {"kind": "text", "text": "VG TEST 7", "at": (0.3, 0.1, 0.0), "dir": "+x", "size": (0.40, 0.08),
            "color": (0, 0, 0), "mirror": True, "wrap": True}
    spec = {"decals": [text], "relief": [{"kind": "hatch", "at": (0, -0.5, 0.3), "dir": "+z", "size": (0.2, 0.14),
                                          "hinge": "-y", "wrap": True},
                                         {"kind": "grille", "at": (0, 0.5, -0.3), "dir": "-z", "size": (0.16, 0.1),
                                          "wrap": True}]}
    base = np.full((H, Wd, 3), 0.8, np.float32)
    out = apply(spec, pos, nrm, covered, base, np.zeros((H, Wd), np.float32), np.full((H, Wd), 0.4, np.float32),
                np.zeros((H, Wd), np.float32), noise)
    alb, _, _, height, _, info = out
    assert not info["warnings"], info["warnings"]
    fits = [f for _, f in info["decal_fit"]]
    assert len(fits) == 2 and all(0.85 < f < 1.15 for f in fits), fits
    ink = alb[..., 0] < 0.4
    assert ink[outer].sum() > 1000 and ink[~outer].sum() == 0, "decal leaked onto the buried sleeve"
    assert np.abs(height[~outer]).max() == 0, "relief leaked onto the buried sleeve"
    ink, T = ink[outer], T[outer]
    height, pos, nrm, covered = height[outer], pos[outer], nrm[outer], covered[outer]

    # Seen from outside each flank, the text must read the same way: starboard viewer's right = +Y (nose),
    # port viewer's right = -Y. Compare ink column profiles along each viewer's reading axis from the text centre.
    reading = np.linspace(-0.22, 0.22, 400)

    def view(side):
        profile = (ink & (np.cos(T) * side > 0.5)).sum(0).astype(np.float64)
        return np.interp(0.1 + side * reading, ys, profile)
    s, p = view(1), view(-1)
    assert s.sum() > 500 and np.corrcoef(s, p)[0, 1] > 0.95, "text not readable on both flanks"
    assert np.corrcoef(s, p[::-1])[0, 1] < 0.8, "check cannot tell mirrored text apart"
    nmap = detail_normal(pos, nrm, height, covered, max_slope=max_slope(spec)) * 2 - 1
    relief = np.abs(height) > 1e-6
    energy = float(nmap[..., :2][relief].std())
    assert energy > 0.15, energy
    print(json.dumps({"vgkit_check": "PASS", "decal_fit": fits, "relief_normal_xy_std": round(energy, 3)}))


if __name__ == "__main__":
    self_check()
