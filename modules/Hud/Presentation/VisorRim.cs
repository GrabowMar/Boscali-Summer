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
    /// tiny text at the right, the real fields: the pilot's callsign, the airframe's type and the sortie time. 1.2 px strokes at about 0.45 alpha; the glyph edge
    /// and glow come from the shared material. Built like <see cref="TapeHud"/>: a holder under the native HUD canvas scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels, repainted only when
    /// what it draws changed (the sortie clock moves once a second). During the power-on it draws itself in from the corners (<see cref="Boot"/>).
    /// </summary>
    internal sealed class VisorRim
    {
        private const int Quads = 160;
        private const float Stroke = HudStroke.Hairline, Alpha = HudInk.LineLow, TextAlpha = 1f, TextPt = HudType.Label, TextY = 1073f, TextInset = 34f;

        private RectTransform canvasRoot, root;
        /// <summary>The layer's holder (the visor glitch shader's material goes onto the graphics under it; null until built).</summary>
        internal Transform Root => root;
        private AvVector mesh;
        private ScopeText text;
        private readonly char[] etched = new char[64];
        private int etchedLen, drawnSig = int.MinValue;
        private bool on;
        private float lastK, drawnRefW;
        private string callsign = "", airframe = "";
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
            root = CombatAlerts.Holder(BoscaliSummer.Core.Ui.HudLayer.Ensure() ? BoscaliSummer.Core.Ui.HudLayer.Static : canvasRoot, "BoscaliRim");
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
            int sig = step * 31 + callsign.GetHashCode() * 13 + airframe.GetHashCode() * 17;
            if (sig == drawnSig && refW == drawnRefW && ink == drawnInk && flashStep == drawnFlash) return;
            drawnFlash = flashStep;
            Paint(refW, ink, step);
            drawnSig = sig;
            drawnRefW = refW;
            drawnInk = ink;
        }

        private void Paint(float refW, Color ink, int step)
        {
            float p = step > 30 ? 1f : step / 30f;
            float cx = refW * 0.5f;
            mesh.Buffer.Clear();      // no corner arcs (user 2026-11-17: "the corner border things suck"); the rim draws only its words
            mesh.Commit();

            text.Begin();
            if (p >= 0.55f)
            {
                float shown = Mathf.Clamp01((p - 0.55f) / 0.45f);      // the etched line types on at the end of the rim's draw-in
                Color tc = VanillaHudStyle.InkDim.WithAlpha(VisorTone.Alpha(TextAlpha, flashStep));
                etchedLen = Compose(etched);
                int n = Mathf.Min(etchedLen, Mathf.CeilToInt(etchedLen * shown));
                if (n > 0) text.Draw(new System.ReadOnlySpan<char>(etched, 0, n), cx - TextInset, TextY - 540f, TextPt, ScopeAlign.Right, tc, false);
            }
            else etchedLen = 0;
            text.End();
        }

        /// <summary>The right-hand etched line: callsign and airframe; a field that is not known is left out.</summary>
        private int Compose(char[] into)
        {
            int n = 0;
            n = Put(into, n, callsign);
            if (airframe.Length > 0) { n = Put(into, n, " · "); n = Put(into, n, airframe); }
            return n;
        }

        private static int Put(char[] into, int n, string s)
        {
            for (int i = 0; i < s.Length && n < into.Length; i++) into[n++] = s[i];
            return n;
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
