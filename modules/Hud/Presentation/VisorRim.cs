using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's RIM (2026-11-16, "HELMET ON"): the screen as glass you look through. Thin curved lines hug the four corners (arcs of 16-22 px that stay inside the 12 px margin the HUD zones leave, so nothing crosses a
    /// zone or the minimised map) and run a short way along the edges, a double line sits at the top centre above the brow, a few registration ticks close the line ends, and the tick ladders and cross marks of the old
    /// "frame furniture" run down both sides (the right ladder starts under the board when its box reaches that edge). Along the bottom rim, in the corners the cockpit's dashboard art does not cover, one ETCHED LINE of
    /// tiny text: left a constant helmet designation (flavour, claims nothing), right the real fields: the pilot's callsign, the airframe's type and the sortie time. 1.2 px strokes at about 0.45 alpha; the glyph edge
    /// and glow come from the shared material. Built like <see cref="BrowHud"/>: a holder under the native HUD canvas scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels, repainted only when
    /// what it draws changed (the sortie clock moves once a second). During the power-on it draws itself in from the corners (<see cref="Boot"/>).
    /// </summary>
    internal sealed class VisorRim
    {
        private const int Quads = 160;
        private const float Stroke = 1.2f, Alpha = 0.45f, TextAlpha = 0.6f, TextPt = 10f, TextY = 1073f, TextInset = 34f;
        private const string Designation = "HMD-9 // BOSCALI AEROSPACE";

        private RectTransform canvasRoot, root;
        private AvVector mesh;
        private ScopeText text;
        private readonly char[] etched = new char[64];
        private int etchedLen, drawnSig = int.MinValue;
        private bool on;
        private float lastK, drawnRefW, ladderTop = 300f;
        private string callsign = "", airframe = "";
        private int sortieSeconds = -1;
        private Color drawnInk;

        /// <summary>How far the power-on has drawn the rim in, 0..1 (1 in flight). Set by the board each frame; the rim repaints when its step (1/30) changes.</summary>
        public float Boot = 1f;

        private CanvasGroup group;
        private int flashStep, drawnFlash;
        private FlashKind flashKind;

        /// <summary>The reactive visor's look (<see cref="VisorFx"/>): the layer's brightness (the G dim, a glitch's flicker, an EMP's stutter, the idle breathing) and one flash beat. The rim itself does not move: it is the glass, the symbols move behind it.</summary>
        public void ApplyFx(float alpha, float flash, FlashKind kind)
        {
            if (group != null && group.alpha != alpha) group.alpha = alpha;
            flashStep = VisorTone.Step(flash);
            flashKind = kind;
        }

        /// <summary>What the layer shows now (the sim probe reads it).</summary>
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal string EtchedText => etchedLen > 0 ? new string(etched, 0, etchedLen) : "";

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(canvasRoot, "BoscaliRim");
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            mesh = AvVector.Create(root, "Rim", Quads);
            text = new ScopeText(CombatAlerts.Holder(root, "Words"), font, fontMaterial);
            root.gameObject.SetActive(false);
            lastK = 0f;
            drawnSig = int.MinValue;
            drawnRefW = 0f;
            return true;
        }

        /// <summary>The board's 10 Hz read: whether the rim is wanted, who sits in the seat and how long, and where the right ladder starts.</summary>
        public void Read(bool show, string pilot, string type, float sortieS, IHudBoard board)
        {
            on = show;
            if (!on) return;
            callsign = pilot ?? "";
            airframe = type ?? "";
            sortieSeconds = sortieS < 0f ? -1 : (int)sortieS;
            // The right edge's ladder starts under the board when its box reaches that edge.
            float bottom = 0f;
            if (board != null && board.TryGetScreenBox(out float l, out _, out float r, out float b) && r > l && Screen.height > 0 && r >= Screen.width - 40f)
                bottom = b * 1080f / Screen.height;
            ladderTop = BrowRail.LadderTop(bottom);
        }

        /// <summary>Every frame: show or hide, follow the screen's size and repaint when what is drawn changed; nothing else.</summary>
        public void Frame()
        {
            if (root == null) return;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (k != lastK) { root.localScale = new Vector3(k, k, 1f); lastK = k; }
            float refW = canvasRoot.rect.width / k;
            Color ink = VisorTone.Ink(VanillaHudStyle.Colours.AllClear, flashKind, flashStep);
            int step = Boot >= 1f ? 31 : Mathf.Clamp((int)(Boot * 30f), 0, 30);
            int sig = step * 31 + sortieSeconds * 7 + callsign.GetHashCode() * 13 + airframe.GetHashCode() * 17;
            if (sig == drawnSig && refW == drawnRefW && ladderTop == drawnLadder && ink == drawnInk && flashStep == drawnFlash) return;
            drawnFlash = flashStep;
            Paint(refW, ink, step);
            drawnSig = sig;
            drawnRefW = refW;
            drawnLadder = ladderTop;
            drawnInk = ink;
        }

        private float drawnLadder = -1f;

        // Screen px (x from the left, y from the top) to the holder's (x from the centre, y up from the centre).
        private static float Y(float fromTop) => 540f - fromTop;

        private void Paint(float refW, Color ink, int step)
        {
            float p = step > 30 ? 1f : step / 30f, e = VisorBoot.Ease(p);
            float cx = refW * 0.5f;
            Rgba c = ink.ToRgba().WithAlpha(VisorTone.Alpha(Alpha, flashStep));
            AvQuadBuffer b = mesh.Buffer;
            b.Clear();
            if (p > 0.04f)
            {
                // The four corners: an arc hugging each and a line a little way along both edges (the top ones; the bottom ones leave the edge to the etched line).
                float lenTop = 140f * e, lenSide = 120f * e;
                Corner(b, cx, 0f, 0f, 22f, 3f, lenTop, lenSide, true, c);
                Corner(b, cx, 1f, 0f, 16f, 2f, lenTop, lenSide, true, c);
                Corner(b, cx, 0f, 1f, 22f, 3f, 0f, lenSide, false, c);
                Corner(b, cx, 1f, 1f, 22f, 3f, 0f, lenSide, false, c);
            }
            if (p >= 0.5f)
            {
                // The double line at the top centre, above the brow caption.
                AvStrokes.Line(b, -80f, Y(2.5f), 80f, Y(2.5f), Stroke, c);
                AvStrokes.Line(b, -50f, Y(5.5f), 50f, Y(5.5f), Stroke, c);
                AvStrokes.Line(b, -80f, Y(2.5f), -50f, Y(5.5f), Stroke, c.WithAlpha(c.A * 0.8f));
                AvStrokes.Line(b, 80f, Y(2.5f), 50f, Y(5.5f), Stroke, c.WithAlpha(c.A * 0.8f));
            }
            if (p >= 0.65f)
            {
                // The glass's ladders: ticks every 50 from 300 to 800 down both sides, longer every 100, a small cross at the middle of each side. The left ones stay in the 12 px margin left of the column zones.
                float left = -cx + 2f, right = cx - 2f;
                Rgba t = ink.ToRgba().WithAlpha(VisorTone.Alpha(0.4f, flashStep));
                for (int y = 300; y <= 800; y += 50)
                {
                    float len = y % 100 == 0 ? 10f : 6f;
                    AvStrokes.Line(b, left, Y(y), left + len, Y(y), 1f, t);
                    if (y >= ladderTop) AvStrokes.Line(b, right, Y(y), right - len, Y(y), 1f, t);
                }
                AvStrokes.Line(b, left, Y(540f), left + 10f, Y(540f), 1f, t);
                AvStrokes.Line(b, left + 5f, Y(533f), left + 5f, Y(547f), 1f, t);
                AvStrokes.Line(b, right, Y(540f), right - 10f, Y(540f), 1f, t);
                AvStrokes.Line(b, right - 5f, Y(533f), right - 5f, Y(547f), 1f, t);
            }
            mesh.Commit();

            text.Begin();
            if (p >= 0.55f)
            {
                float shown = Mathf.Clamp01((p - 0.55f) / 0.45f);      // the etched line types on at the end of the rim's draw-in
                Color tc = ink.WithAlpha(VisorTone.Alpha(TextAlpha, flashStep));
                DrawEtched(Designation, -cx + TextInset, ScopeAlign.Left, shown, tc);
                etchedLen = Compose(etched);
                int n = Mathf.Min(etchedLen, Mathf.CeilToInt(etchedLen * shown));
                if (n > 0) text.Draw(new System.ReadOnlySpan<char>(etched, 0, n), cx - TextInset, TextY - 540f, TextPt, ScopeAlign.Right, tc, false);
            }
            else etchedLen = 0;
            text.End();
        }

        private void DrawEtched(string s, float x, ScopeAlign align, float shown, Color c)
        {
            int n = Mathf.Min(s.Length, Mathf.CeilToInt(s.Length * shown));
            if (n > 0) text.Draw(System.MemoryExtensions.AsSpan(s, 0, n), x, TextY - 540f, TextPt, align, c, false);
        }

        /// <summary>The right-hand etched line: callsign, airframe and sortie time (the same "T+h:mm:ss" the SYSTEMS strip writes); a field that is not known is left out.</summary>
        private int Compose(char[] into)
        {
            int n = 0;
            n = Put(into, n, callsign);
            if (airframe.Length > 0) { n = Put(into, n, " · "); n = Put(into, n, airframe); }
            if (sortieSeconds >= 0)
            {
                int s = sortieSeconds, h = s / 3600, m = s / 60 % 60;
                n = Put(into, n, n > 0 ? " · T+" : "T+");
                if (n < into.Length) into[n++] = (char)('0' + Mathf.Min(h, 9));
                if (n < into.Length) into[n++] = ':';
                if (n < into.Length) into[n++] = (char)('0' + m / 10);
                if (n < into.Length) into[n++] = (char)('0' + m % 10);
                if (n < into.Length) into[n++] = ':';
                if (n < into.Length) into[n++] = (char)('0' + s % 60 / 10);
                if (n < into.Length) into[n++] = (char)('0' + s % 60 % 10);
            }
            return n;
        }

        private static int Put(char[] into, int n, string s)
        {
            for (int i = 0; i < s.Length && n < into.Length; i++) into[n++] = s[i];
            return n;
        }

        /// <summary>One corner. <paramref name="fx"/> / <paramref name="fy"/> 0 or 1: right / bottom. The arc has radius <paramref name="r"/> at <paramref name="inset"/> from both edges (it stays inside the margin the zones leave: at 16-22 px
        /// it never reaches the 12 px zone line). The line continues along the vertical edge <paramref name="side"/> px and, with <paramref name="horizontal"/>, along the horizontal one <paramref name="top"/> px, each ending in a small registration tick.</summary>
        private static void Corner(AvQuadBuffer b, float cx, float fx, float fy, float r, float inset, float top, float side, bool horizontal, Rgba c)
        {
            float ux = fx < 0.5f ? 1f : -1f, uy = fy < 0.5f ? 1f : -1f;
            float ox = fx < 0.5f ? -cx : cx, oy = fy < 0.5f ? 0f : 1080f;      // the corner point, holder x and screen y
            float ccx = ox + ux * (inset + r), ccy = oy + uy * (inset + r);
            const int segments = 6;
            float px = ccx - ux * r, py = ccy;
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 0.5f;
                float x = ccx - ux * r * Mathf.Cos(a), y = ccy - uy * r * Mathf.Sin(a);
                AvStrokes.Line(b, px, Y(py), x, Y(y), Stroke, c);
                px = x; py = y;
            }
            float edgeX = ox + ux * inset, edgeY = oy + uy * inset;
            if (side > 0f)
            {
                AvStrokes.Line(b, edgeX, Y(ccy), edgeX, Y(ccy + uy * side), Stroke, c);
                AvStrokes.Line(b, edgeX, Y(ccy + uy * side), edgeX + ux * 5f, Y(ccy + uy * side), Stroke, c);
            }
            if (horizontal && top > 0f)
            {
                AvStrokes.Line(b, ccx, Y(edgeY), ccx + ux * top, Y(edgeY), Stroke, c);
                AvStrokes.Line(b, ccx + ux * top, Y(edgeY), ccx + ux * top, Y(edgeY + uy * 4f), Stroke, c);
            }
        }

        public void Hide()
        {
            on = false;
            if (root != null) root.gameObject.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
            group = null;
            on = false;
        }
    }
}
