using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's BROW RAIL (2026-11-16, mockup <c>evolved-hud-mockup.html</c>): a curved rail at the top centre (reference px x 800..1116, y 12..104) with its tick scale, the
    /// boresight mark, one status caption above it (real state only) and bearing carets hung under it for what matters now, relative to the aircraft's nose, most urgent first (<see cref="BrowCarets"/>):
    /// missiles inbound (red M1, M2), radars locking or painting us (amber LOCK, SPK), the ace on the hunt (red ACE), the objective (white OBJ), the runway or pad (RWY, PAD), the tanker (TKR) and the
    /// landing zone (LZ). The visor's frame (rim, side ticks, etched line) is <see cref="VisorRim"/>'s. During the power-on the rail draws itself from left to right (<see cref="Boot"/>).
    /// Built like <see cref="CombatAlerts"/>: a holder under the native HUD canvas, scaled by canvas height / 1080, centre origin, its own mesh layers and pooled labels (<see cref="ScopeText"/>, so the
    /// glow material applies). Data at the board's 10 Hz (<see cref="Read"/>); a mesh is rebuilt only when what it draws changed, so a steady frame does no work but a compare. Fails open: nothing of
    /// the game's own HUD is touched.
    /// </summary>
    internal sealed class BrowHud
    {
        private const int RailQuads = 96, CurveSegments = 40;
        // Mockup weights: rail and major ticks about 1.5 px at full ink (clear against bright sky and cloud), minor ticks thinner and dimmer.
        private const float CurveStroke = 1.6f, MajorStroke = 1.6f, MinorStroke = 1.2f;
        private const float CaptionY = 11f, CaptionPt = BrowRail.LabelPt;
        private static readonly string[] MissileLabels = { "M0", "M1", "M2", "M3", "M4", "M5", "M6", "M7", "M8", "M9" };

        private RectTransform canvasRoot, root, words;
        /// <summary>The layer's holder (the visor glitch shader's material goes onto the graphics under it; null until built).</summary>
        internal Transform Root => root;
        private AvVector rail;
        private ScopeText text;
        private readonly BrowCarets carets = new BrowCarets();
        private readonly string[] heardLabels = new string[BrowRail.MaxCarets];
        private int heardCount;
        private bool heardPrimed;
        private readonly char[] caption = new char[64], seen = new char[64], scratch = new char[16];
        private int captionLen, seenLen = -1, drawnSig = int.MinValue, drawnBoot = -1;
        private bool on, dirty;
        private float lastK, drawnRefW;
        private Color drawnInk;

        /// <summary>How far the power-on has drawn the rail, 0..1 (1 in flight). Set by the board each frame; the rail repaints when the step (1/40) changes.</summary>
        public float Boot = 1f;

        /// <summary>How much of the caption the power-on has typed, 0..1 (1 in flight): the caption types on, character by character, after the rail is drawn.</summary>
        public float CaptionBoot = 1f;

        private CanvasGroup group;
        private AvVector mark, scan;
        private int flashStep, drawnFlash, drawnTyped = -1;
        private FlashKind flashKind;
        private float shiftX, shiftY, shiftK, scanAt = -1f;

        /// <summary>
        /// The reactive visor's look (<see cref="VisorFx"/>): the layer's brightness and place (the G jitter and a glitch's jump shift the whole root by whole pixels), one flash beat toning the rail, and the idle life: the boresight mark breathes
        /// (its own mesh, alpha only) and a faint tick travels along the rail (a tiny mesh moved as a transform, never rebuilt). No mesh is rebuilt for any of it but the flash's four steps.
        /// </summary>
        public void ApplyFx(float alpha, float x, float y, float flash, FlashKind kind, float breath, float scanPos, float scanAlpha)
        {
            if (root == null) return;
            if (group != null && group.alpha != alpha) group.alpha = alpha;
            if (shiftX != x || shiftY != y || shiftK != lastK)
            {
                shiftX = x; shiftY = y; shiftK = lastK;
                root.anchoredPosition = new Vector2(x, y) * (lastK > 0.01f ? lastK : 1f);      // reference pixels to the canvas's units (the holder is scaled by lastK)
            }
            flashStep = VisorTone.Step(flash);
            flashKind = kind;
            if (mark != null && mark.canvasRenderer.GetAlpha() != breath) mark.canvasRenderer.SetAlpha(breath);
            if (scan == null) return;
            if (scanPos < 0f || scanAlpha <= 0.01f)
            {
                if (scanAt >= 0f) { scanAt = -1f; scan.canvasRenderer.SetAlpha(0f); }
                return;
            }
            if (scanAt != scanPos)
            {
                scanAt = scanPos;
                BrowRail.Point(scanPos, out float px, out float py);
                scan.rectTransform.anchoredPosition = new Vector2(px, Y(py));
            }
            scan.canvasRenderer.SetAlpha(scanAlpha);
        }

        // BROKEN HELMET (2026-11-16): what the jam and the damage do to the rail's words, display only (nothing another system reads changes): the amber EW JAM word beside the caption while the game says an enemy
        // jammer is on us, the caption's letters corrupted for the first part of a heavy damage burst (numbers and the carets' words stay whole), and the carets' bearings jittering by a degree or two while jammed.
        private bool jamWord, drawnJam;
        private int scrambleSeed, drawnScramble, jitterQ, drawnJitter;
        private readonly char[] scrambled = new char[64];
        private const string JamText = "EW JAM", Noise = "#%&@$*/|?!<>=+~";

        /// <summary>The glitch's words (see <see cref="VisorFx"/>): the jam word, a corruption seed for the caption (0: none) and the carets' display jitter in degrees. The rail repaints when one of them steps.</summary>
        public void ApplyGlitch(bool jam, int scramble, float jitterDeg)
        {
            jamWord = jam;
            scrambleSeed = scramble;
            jitterQ = (int)Mathf.Round(jitterDeg * (BrowRail.Half / BrowRail.ScaleDeg) * 2f);      // half-pixel steps
        }

        /// <summary>What the layer shows now (the sim probe reads it).</summary>
        internal bool Shown => root != null && root.gameObject.activeSelf;
        internal int CaretCount => carets.Count;
        internal string CaptionText => captionLen > 0 ? new string(caption, 0, captionLen) : "";

        public bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(canvasRoot, "BoscaliBrow");
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            rail = AvVector.Create(root, "Rail", RailQuads);
            mark = AvVector.Create(root, "Boresight", 2);
            scan = AvVector.Create(root, "Scan", 3);
            scan.canvasRenderer.SetAlpha(0f);
            scanAt = -1f;
            words = CombatAlerts.Holder(root, "Words");
            text = new ScopeText(words, font, fontMaterial);
            root.gameObject.SetActive(false);
            lastK = 0f;
            drawnSig = int.MinValue;
            drawnRefW = 0f;
            seenLen = -1;
            return true;
        }

        /// <summary>
        /// The board's 10 Hz read: this tick's carets and caption. <paramref name="show"/> false (the switch, an external view, a pause, the map) takes the layer down.
        /// <paramref name="scopeMode"/> is the board's page mode (0 AUTO, 1 MANUAL, 2 OFF).
        /// </summary>
        public void Read(bool show, Aircraft aircraft, BoardFrame frame, int scopeMode, IHudBoard board)
        {
            on = show && aircraft != null;
            if (!on) { heardPrimed = false; return; }
            Transform own = aircraft.transform;
            float heading = own.eulerAngles.y;
            carets.Clear();
            if (ScopeFeeds.TryRead(out ThreatScope threat))
            {
                for (int i = 0; i < threat.MissileCount; i++)
                {
                    ThreatMissile m = threat.Missiles[i];
                    if (!m.Live) continue;
                    carets.Add(m.BearingDeg, BrowTone.Red, MissileLabels[Mathf.Clamp(m.Number, 0, 9)], float.IsNaN(m.TtiS) ? 90f : Mathf.Min(m.TtiS, 90f));
                }
                for (int i = 0; i < threat.EmitterCount; i++)
                {
                    ThreatEmitter e = threat.Emitters[i];
                    carets.Add(e.BearingDeg, BrowTone.Amber, e.Lock ? "LOCK" : "SPK", e.Lock ? 100f : 300f + Mathf.Min(e.RangeM * 0.001f, 99f));
                }
            }
            if (ModuleServices.TryGet(out ISquadView squad) && !(squad is Object gone && gone == null) && squad.HuntActive && squad.TryAce(out Aircraft ace, out _) && ace != null)
            {
                Vector3 d = ace.transform.position - own.position;
                carets.Add(BrowRail.RelativeBearing(d.x, d.z, heading), BrowTone.Red, "ACE", 200f);
            }
            if (ScopeFeeds.TryRead(out DockScope dock)) carets.Add(dock.TankerBearingDeg, BrowTone.Bright, "TKR", 400f);
            if (ScopeFeeds.TryRead(out ApproachScope approach) && approach.HasBearing) carets.Add(approach.BearingDeg, BrowTone.Bright, approach.Pad ? "PAD" : "RWY", 410f);
            if (ScopeFeeds.TryRead(out ObjectiveScope objective) && objective.HasMarker)
                carets.Add(BrowRail.RelativeBearing(objective.X - objective.OwnX, objective.Z - objective.OwnZ, objective.OwnHeading), BrowTone.White, "OBJ", 500f);
            if (ModuleServices.TryGet(out ILandingZone zone) && !(zone is Object lost && lost == null) && zone.HasLocal && zone.TryGetLocal(out GlobalPosition at))
            {
                Vector3 d = at - aircraft.GlobalPosition();
                carets.Add(BrowRail.RelativeBearing(d.x, d.z, heading), BrowTone.Bright, "LZ", 520f);
            }
            carets.Resolve();
            HearNewCarets();

            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera cam = state != null ? state.mainCamera : null;
            captionLen = BrowRail.Caption(caption, scopeMode, frame.Systems.NightVision, cam != null ? cam.fieldOfView : float.NaN);
        }

        /// <summary>A tick (HMD-9) when a caret that was not on the rail at the last read appears: a new missile, emitter, ace or objective, never a refresh of one already there, and not while the power-on draws the rail.</summary>
        private void HearNewCarets()
        {
            bool fresh = false;
            int used = 0;
            for (int i = 0; i < carets.Count && !fresh; i++)
            {
                bool found = false;
                for (int j = 0; j < heardCount && !found; j++)
                    if ((used & (1 << j)) == 0 && (object)heardLabels[j] == (object)carets[i].Label) { used |= 1 << j; found = true; }
                fresh = !found;
            }
            for (int i = 0; i < carets.Count; i++) heardLabels[i] = carets[i].Label;
            heardCount = carets.Count;
            if (fresh && heardPrimed && Boot >= 1f) BoscaliSummer.Core.Fx.VisorSound.Play(BoscaliSummer.Core.Fx.VisorClip.Caret);
            heardPrimed = true;
        }

        /// <summary>Every frame: show or hide, follow the screen's size and repaint when what is drawn changed; nothing else.</summary>
        public void Frame()
        {
            if (root == null) return;
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
            if (!on) return;
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (k != lastK)
            {
                root.localScale = new Vector3(k, k, 1f);
                lastK = k;
            }
            float refW = canvasRoot.rect.width / k;
            Color ink = VisorTone.Ink(VanillaHudStyle.Colours.AllClear, flashKind, flashStep);
            int sig = carets.Signature();
            int step = Boot >= 1f ? CurveSegments + 1 : Mathf.Clamp((int)(Boot * CurveSegments), 0, CurveSegments);
            int typed = CaptionBoot >= 1f ? int.MaxValue : Mathf.CeilToInt(captionLen * Mathf.Clamp01(CaptionBoot));
            bool captionChanged = seenLen != captionLen || !SameCaption();
            if (!dirty && !captionChanged && sig == drawnSig && refW == drawnRefW && step == drawnBoot && ink == drawnInk && flashStep == drawnFlash && typed == drawnTyped
                && jamWord == drawnJam && scrambleSeed == drawnScramble && jitterQ == drawnJitter) return;
            drawnJam = jamWord;
            drawnScramble = scrambleSeed;
            drawnJitter = jitterQ;
            drawnFlash = flashStep;
            drawnTyped = typed;
            Paint(refW, ink, step, typed);
            dirty = false;
            drawnSig = sig;
            drawnRefW = refW;
            drawnBoot = step;
            drawnInk = ink;
            System.Array.Copy(caption, seen, captionLen);
            seenLen = captionLen;
        }

        private bool SameCaption()
        {
            for (int i = 0; i < captionLen; i++) if (caption[i] != seen[i]) return false;
            return true;
        }

        // Rail units (x from the centre line, y down from the top) to the holder's (y up from the centre).
        private static float Y(float railY) => 540f - railY;

        /// <summary><paramref name="step"/>: how many of the rail's 40 segments the power-on has drawn so far (<c>CurveSegments + 1</c>: all of it, with its boresight mark, carets and caption).</summary>
        private void Paint(float refW, Color ink, int step, int typed)
        {
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Rgba c = ink.ToRgba();
            bool whole = step > CurveSegments;
            AvQuadBuffer b = rail.Buffer;
            b.Clear();
            BrowRail.Point(0f, out float px, out float py);
            for (int i = 1; i <= CurveSegments && i <= step; i++)
            {
                BrowRail.Point(i / (float)CurveSegments, out float x, out float y);
                AvStrokes.Line(b, px, Y(py), x, Y(y), CurveStroke, c);
                px = x; py = y;
            }
            for (int i = 0; i < BrowRail.TickCount; i++)
            {
                if (!whole && i / (float)(BrowRail.TickCount - 1) * CurveSegments > step) break;      // the ticks follow the line along as it draws
                BrowRail.Tick(i, out float x, out float y, out float nx, out float ny, out bool major);
                float len = major ? 10f : 5f;
                AvStrokes.Line(b, x, Y(y), x + nx * len, Y(y + ny * len), major ? MajorStroke : MinorStroke, c.WithAlpha(major ? 1f : 0.7f));
            }
            // The boresight mark has its own mesh (it breathes by alpha), and the scan tick's is one short line that is only ever moved.
            AvQuadBuffer m = mark.Buffer;
            m.Clear();
            if (whole) Triangle(m, 0f, BrowRail.BoreTop, BrowRail.BoreHalf, BrowRail.BoreBase - BrowRail.BoreTop, c);
            mark.Commit();
            AvQuadBuffer s = scan.Buffer;
            s.Clear();
            AvStrokes.Line(s, 0f, 0f, 0f, 15f, 2f, Color.Lerp(ink, Color.white, 0.5f).ToRgba());
            scan.Commit();
            for (int i = 0; whole && i < carets.Count; i++)
            {
                BrowCaret k = carets[i];
                Triangle(b, k.X + jitterQ * 0.5f, k.Y + BrowRail.CaretTop, BrowRail.CaretHalf, BrowRail.CaretBase - BrowRail.CaretTop, Of(k.Tone, palette).ToRgba());
            }
            rail.Commit();

            text.Begin();
            // Regular weight at the board's small-label size: bold with the glyph edge ran heavy and blobby.
            // The caption types on after the rail (power-on): left-aligned at its final left edge so the letters appear in place, not shifting as it grows.
            if (whole && captionLen > 0)
            {
                int shown = System.Math.Min(captionLen, typed);
                char[] show = caption;
                if (scrambleSeed != 0 && shown >= captionLen)
                {
                    // Corrupted glyphs: three letters of the caption (never a digit, a space or the dots) swapped for noise marks while the burst's first part runs.
                    System.Array.Copy(caption, scrambled, captionLen);
                    for (int n = 0; n < 3; n++)
                    {
                        int at = (int)(VisorFx.Hash(scrambleSeed * 7 + n * 13) * captionLen);
                        if (at < captionLen && scrambled[at] >= 'A' && scrambled[at] <= 'Z') scrambled[at] = Noise[(int)(VisorFx.Hash(scrambleSeed * 3 + n) * Noise.Length) % Noise.Length];
                    }
                    show = scrambled;
                }
                if (shown >= captionLen) text.Draw(new System.ReadOnlySpan<char>(show, 0, captionLen), 0f, CaptionY - 540f, CaptionPt, ScopeAlign.Center, ink.WithAlpha(0.95f), false);
                else if (shown > 0) text.Draw(new System.ReadOnlySpan<char>(caption, 0, shown), -captionLen * CaptionPt * ScopeText.DesignAdvance * 0.5f, CaptionY - 540f, CaptionPt, ScopeAlign.Left, ink.WithAlpha(0.95f), false);
                if (jamWord && shown >= captionLen)
                    text.Draw(System.MemoryExtensions.AsSpan(JamText), -captionLen * CaptionPt * ScopeText.DesignAdvance * 0.5f - 14f, CaptionY - 540f, CaptionPt, ScopeAlign.Right, palette.Warning, true);      // amber, bold, left of the caption (the board's header stands at its right): the one word that says what is wrong
            }
            for (int i = 0; whole && i < carets.Count; i++)
            {
                BrowCaret k = carets[i];
                string label = k.Label;
                label.CopyTo(0, scratch, 0, label.Length);
                int n = label.Length;
                if (k.Beyond != 0) scratch[n++] = k.Beyond > 0 ? '>' : '<';
                text.Draw(new System.ReadOnlySpan<char>(scratch, 0, n), k.LabelX + jitterQ * 0.5f, k.Y + BrowRail.LabelDrop - 540f, BrowRail.LabelPt, ScopeAlign.Center, Of(k.Tone, palette), true);
            }
            text.End();
        }

        private static Color Of(BrowTone tone, VanillaHudStyle.Palette palette) =>
            tone == BrowTone.Red ? palette.Alert : tone == BrowTone.Amber ? palette.Warning : tone == BrowTone.White ? Color.white : Color.Lerp(palette.AllClear, Color.white, ScopePalette.HiLift);

        /// <summary>A filled triangle (apex up on the screen) at rail units: the apex at (<paramref name="x"/>, <paramref name="topY"/>), its base <paramref name="depth"/> lower and <paramref name="half"/> either side.</summary>
        private static void Triangle(AvQuadBuffer b, float x, float topY, float half, float depth, Rgba c) =>
            b.Add(x, Y(topY), x - half, Y(topY + depth), x + half, Y(topY + depth), x + half, Y(topY + depth), c, c, c, c);

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
            mark = scan = null;
            on = false;
        }
    }
}
