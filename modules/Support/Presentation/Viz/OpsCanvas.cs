using System;
using NOAvionics;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// Colours for the OPS hero parts, always read from the live style sheet roles (never a literal),
    /// so the three themes and the game accent restyle them with the rest of the console.
    /// </summary>
    internal static class OpsInk
    {
        public static Color Role(string role, Color fallback) => AvStyleHost.FuiColor(role, fallback);

        /// <summary>The rail colour a chip/row of this state wears (the state's own hue).</summary>
        public static Color Rail(AvState state) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Rail, AvTheme.RailInert);

        /// <summary>The text colour for a word of this state (inert words read as plain ink).</summary>
        public static Color Word(AvState state) => state == AvState.Inert
            ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
            : AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.TextPrimary);

        public static Color Ink => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        public static Color Muted => Role("ink-muted", AvTheme.Disabled);
        public static Color Hairline => Role("hairline", AvTheme.Hairline);
        public static Color Frame => Role("frame", AvTheme.Frame);
        public static Color Key => Role("key", AvTheme.RailInfo);
        public static Color Select => Role("select", AvTheme.Accent);
        public static Color Sunken => AvStyleHost.Resolve(AvStyleHost.FuiStyle("header").Background, AvTheme.Surface);
        public static Color Inert => AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert);

        public static Color A(Color c, float alpha) => c.WithAlpha(alpha);
    }

    /// <summary>Text helpers shared by the hero parts.</summary>
    internal static class OpsText
    {
        /// <summary>A single-line label that shrinks toward the 11 px floor instead of spilling.</summary>
        public static TMP_Text Line(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align, false);
            AvText.Fit(t, false);
            return t;
        }

        /// <summary>A wrapped block that the owning part measures with <see cref="AvText.Height"/>.</summary>
        public static TMP_Text Block(RectTransform parent, string name, AvTextRole role)
        {
            return AvText.Make(parent, name, role, "", TextAlignmentOptions.TopLeft, true);
        }

        /// <summary>A single-line kit label placed like the plot chrome (top-left origin, Y negative downward).</summary>
        public static TMP_Text Plot(RectTransform parent, string text, Rect area, Color color, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = Line(parent, "Label", role, align);
            t.text = text ?? "";
            t.color = color;
            Chrome.Place(t.rectTransform, area);
            return t;
        }

        /// <summary>Set text only when it changed; returns true when it did.</summary>
        public static bool Set(TMP_Text t, string value)
        {
            string v = value ?? "";
            if (t.text == v) return false;
            t.text = v;
            return true;
        }

        public static void Place(TMP_Text t, float x, float y, float w, float h) => AvLay.Place(t.rectTransform, x, y, w, h);
    }

    /// <summary>
    /// An immediate-mode mesh canvas for the OPS hero visuals (orbit schematic, INFOCON rail, squad
    /// cards, meter bars). Coordinates are local pixels, origin top-left, Y down. Every shape is
    /// vertex-coloured into one mesh, so a hero is one draw call. <see cref="Begin"/> / <see cref="End"/>
    /// bracket a repaint; the mesh only rebuilds when the recorded shapes differ from last time, so a
    /// steady state costs the hash and nothing else. Hard ceiling: <see cref="MaxVerts"/> vertices.
    /// </summary>
    internal sealed class OpsCanvas : MaskableGraphic
    {
        public const int MaxVerts = 8192;

        private Vector3[] pos = new Vector3[512];
        private Color32[] col = new Color32[512];
        private int[] tri = new int[1536];
        private int verts, tris;
        private uint hash = 2166136261u, lastHash;
        private bool everDrawn;

        public static OpsCanvas Add(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var c = go.AddComponent<OpsCanvas>();
            c.raycastTarget = false;
            return c;
        }

        public void Begin()
        {
            verts = 0;
            tris = 0;
            hash = 2166136261u;
        }

        public void End()
        {
            if (everDrawn && hash == lastHash) return;
            everDrawn = true;
            lastHash = hash;
            SetVerticesDirty();
        }

        // ---- shapes ---------------------------------------------------------------------------------

        public void Box(float x, float y, float w, float h, Color c) => Box(x, y, w, h, c, c, c, c);

        /// <summary>Vertical gradient: <paramref name="top"/> at y, <paramref name="bottom"/> at y + h.</summary>
        public void BoxV(float x, float y, float w, float h, Color top, Color bottom) => Box(x, y, w, h, top, top, bottom, bottom);

        public void BoxH(float x, float y, float w, float h, Color left, Color right) => Box(x, y, w, h, left, right, right, left);

        private void Box(float x, float y, float w, float h, Color tl, Color tr, Color br, Color bl)
        {
            if (w <= 0f || h <= 0f || !Room(4, 6)) return;
            Mix(x); Mix(y); Mix(w); Mix(h); Mix(tl); Mix(tr); Mix(br); Mix(bl);
            int b = verts;
            Vert(x, y, tl); Vert(x + w, y, tr); Vert(x + w, y + h, br); Vert(x, y + h, bl);
            Tri(b, b + 1, b + 2); Tri(b, b + 2, b + 3);
        }

        /// <summary>A 1 px (or thicker) box outline drawn inside the rect.</summary>
        public void Outline(float x, float y, float w, float h, float t, Color c)
        {
            Box(x, y, w, t, c); Box(x, y + h - t, w, t, c);
            Box(x, y + t, t, h - 2f * t, c); Box(x + w - t, y + t, t, h - 2f * t, c);
        }

        /// <summary>A straight stroke with square ends.</summary>
        public void Line(float x0, float y0, float x1, float y1, float thick, Color c)
        {
            float dx = x1 - x0, dy = y1 - y0, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f || !Room(4, 6)) return;
            Mix(x0); Mix(y0); Mix(x1); Mix(y1); Mix(thick); Mix(c);
            float nx = -dy / len * thick * 0.5f, ny = dx / len * thick * 0.5f;
            int b = verts;
            Vert(x0 + nx, y0 + ny, c); Vert(x0 - nx, y0 - ny, c); Vert(x1 - nx, y1 - ny, c); Vert(x1 + nx, y1 + ny, c);
            Tri(b, b + 1, b + 2); Tri(b, b + 2, b + 3);
        }

        public void Dashed(float x0, float y0, float x1, float y1, float thick, float dash, float gap, Color c)
        {
            float dx = x1 - x0, dy = y1 - y0, len = Mathf.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) return;
            float ux = dx / len, uy = dy / len, at = 0f;
            int guard = 0;
            while (at < len && guard++ < 256)
            {
                float end = Mathf.Min(len, at + dash);
                Line(x0 + ux * at, y0 + uy * at, x0 + ux * end, y0 + uy * end, thick, c);
                at += dash + gap;
            }
        }

        /// <summary>Annulus segment. Angles in degrees, 0 = +X, counter-clockwise on screen.</summary>
        public void Arc(float cx, float cy, float outer, float inner, float startDeg, float sweepDeg, Color a, Color b)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweepDeg) / 6f), 1, 90);
            if (!Room((steps + 1) * 2, steps * 6)) return;
            Mix(cx); Mix(cy); Mix(outer); Mix(inner); Mix(startDeg); Mix(sweepDeg); Mix(a); Mix(b);
            int first = verts;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, rad = (startDeg + sweepDeg * t) * Mathf.Deg2Rad;
                float cs = Mathf.Cos(rad), sn = Mathf.Sin(rad);
                Color k = Color.Lerp(a, b, t);
                Vert(cx + cs * outer, cy - sn * outer, k);
                Vert(cx + cs * inner, cy - sn * inner, k);
                if (i > 0)
                {
                    int q = first + i * 2;
                    Tri(q - 2, q, q + 1); Tri(q - 2, q + 1, q - 1);
                }
            }
        }

        public void Ring(float cx, float cy, float r, float thick, Color c) => Arc(cx, cy, r, r - thick, 0f, 360f, c, c);

        public void Disc(float cx, float cy, float r, Color c)
        {
            const int steps = 20;
            if (r <= 0f || !Room(steps + 1, steps * 3)) return;
            Mix(cx); Mix(cy); Mix(r); Mix(c);
            int centre = verts;
            Vert(cx, cy, c);
            for (int i = 0; i < steps; i++)
            {
                float rad = i / (float)steps * Mathf.PI * 2f;
                Vert(cx + Mathf.Cos(rad) * r, cy - Mathf.Sin(rad) * r, c);
            }
            for (int i = 0; i < steps; i++) Tri(centre, centre + 1 + i, centre + 1 + (i + 1) % steps);
        }

        public void Diamond(float cx, float cy, float r, Color c)
        {
            if (r <= 0f || !Room(4, 6)) return;
            Mix(cx); Mix(cy); Mix(r); Mix(c);
            int b = verts;
            Vert(cx, cy - r, c); Vert(cx + r, cy, c); Vert(cx, cy + r, c); Vert(cx - r, cy, c);
            Tri(b, b + 1, b + 2); Tri(b, b + 2, b + 3);
        }

        public void DiamondOutline(float cx, float cy, float r, float t, Color c)
        {
            Line(cx, cy - r, cx + r, cy, t, c); Line(cx + r, cy, cx, cy + r, t, c);
            Line(cx, cy + r, cx - r, cy, t, c); Line(cx - r, cy, cx, cy - r, t, c);
        }

        /// <summary>A filled rect with the two diagonal corners cut (the kit's chamfer), as one fan.</summary>
        public void Chamfer(float x, float y, float w, float h, float cut, Color c)
        {
            cut = Mathf.Min(cut, Mathf.Min(w, h) * 0.5f);
            if (w <= 0f || h <= 0f || !Room(6, 12)) return;
            Mix(x); Mix(y); Mix(w); Mix(h); Mix(cut); Mix(c);
            int b = verts;
            Vert(x, y, c); Vert(x + w - cut, y, c); Vert(x + w, y + cut, c); Vert(x + w, y + h, c);
            Vert(x + cut, y + h, c); Vert(x, y + h - cut, c);
            for (int i = 1; i < 5; i++) Tri(b, b + i, b + i + 1);
        }

        // ---- plumbing -------------------------------------------------------------------------------

        private bool Room(int addVerts, int addIdx)
        {
            if (verts + addVerts > MaxVerts) return false;
            if (verts + addVerts > pos.Length)
            {
                int n = Mathf.Min(MaxVerts, Mathf.Max(pos.Length * 2, verts + addVerts));
                Array.Resize(ref pos, n);
                Array.Resize(ref col, n);
            }
            if (tris + addIdx > tri.Length) Array.Resize(ref tri, Mathf.Max(tri.Length * 2, tris + addIdx));
            return true;
        }

        private void Vert(float x, float y, Color c)
        {
            pos[verts] = new Vector3(x, y, 0f);
            col[verts] = c;
            verts++;
        }

        private void Tri(int a, int b, int c) { tri[tris++] = a; tri[tris++] = b; tri[tris++] = c; }

        private void Mix(float v) { hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(v)) * 16777619u; }
        private void Mix(Color c) { Mix(c.r); Mix(c.g); Mix(c.b); Mix(c.a); }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            for (int i = 0; i < verts; i++)
                vh.AddVert(new Vector3(r.xMin + pos[i].x, r.yMax - pos[i].y, 0f), col[i], Vector4.zero);
            for (int i = 0; i + 2 < tris; i += 3)
                if (tri[i] != tri[i + 1] && tri[i + 1] != tri[i + 2]) vh.AddTriangle(tri[i], tri[i + 1], tri[i + 2]);
        }
    }
}
