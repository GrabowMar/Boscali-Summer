using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    /// <summary>
    /// The visor's POWER-ON (2026-11-16, "HELMET ON"): what the pilot sees for the first seconds in the seat. This class owns the boot's clock (<see cref="VisorBootClock"/>) and the three things only the boot draws:
    /// the CRT line that opens from the centre, the SELF-TEST block (terse avionics shorthand, every line a real check, each ticking from a spinner to its result; <see cref="VisorBoot.BuildLines"/>) which collapses
    /// to VISOR ONLINE and the callsign, and the ALIGN bracket that converges on the boresight and blinks off. The other zones (rim, brow, left column, board, arcs) ask it how far they have come alive
    /// (<see cref="Progress"/>, <see cref="Alpha"/>); it draws none of them. Purely visual: it takes no input and hides nothing of the game's. The owner cuts it short (<see cref="Step"/>) when it is not allowed or a real warning is up.
    /// Built like <see cref="BrowHud"/>: a holder under the native HUD canvas scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels; once the boot is over the holder is hidden and costs nothing.
    /// The self-test block stands LEFT of the screen centre (x 330..580, y 470..590), clear of the reticle, the arcs and the cockpit's dashboard; only the sweep line and the ALIGN bracket cross the middle, for a moment.
    /// </summary>
    internal sealed class VisorBootHud
    {
        private const int Quads = 64;
        private const float Pt = 12f, OnlinePt = 14f, Row = 16f, TitleY = 462f, FirstY = 484f, Advance = 7.8f, BlockMinX = 296f, BlockFromCentre = 690f;

        private readonly VisorBootClock clock = new VisorBootClock();
        private readonly BootLine[] lines = new BootLine[VisorBoot.MaxLines];
        private readonly char[] buf = new char[VisorBoot.Cols + 8];
        private readonly char[] online = new char[48];
        private RectTransform canvasRoot, root;
        private AvVector mesh;
        private ScopeText text;
        private int lineCount, onlineLen, resolvedHeard;
        private float lastK, t;
        private bool collapsedHeard;

        /// <summary>The boot is running (the zones below are in their boot state).</summary>
        public bool Active => clock.Active;
        public BootMode Mode => clock.Mode;
        /// <summary>Seconds into the boot (0 when none runs).</summary>
        public float Age => clock.Active ? t : 0f;
        internal int LineCount => lineCount;
        internal bool Shown => root != null && root.gameObject.activeSelf;

        /// <summary>How far a zone has come alive, 0..1; 1 when no boot runs.</summary>
        public float Progress(BootGroup group) => clock.Active ? VisorBoot.Reveal(clock.Mode, group, t) : 1f;

        /// <summary>The zone's visibility now: its reveal with the power-up stutter; 1 when no boot runs.</summary>
        public float Alpha(BootGroup group) => clock.Active ? VisorBoot.Flicker(VisorBoot.Reveal(clock.Mode, group, t), t) : 1f;

        /// <summary>Start a boot with the real state it will report. <paramref name="callsign"/> goes on the closing line. One allocation here, none per frame.</summary>
        public void Begin(BootMode mode, float time, in BootInputs inputs, string callsign)
        {
            lineCount = mode == BootMode.Full ? VisorBoot.BuildLines(inputs, lines) : 0;
            string tail = mode == BootMode.Full ? "VISOR ONLINE" : "VISOR RESYNC";
            string closing = string.IsNullOrEmpty(callsign) ? tail : tail + " · " + callsign;
            onlineLen = System.Math.Min(closing.Length, online.Length);
            closing.CopyTo(0, online, 0, onlineLen);
            resolvedHeard = 0;
            collapsedHeard = false;
            clock.Start(mode, time);
            t = 0f;
            PlaySound(AvUiCue.Open);
        }

        /// <summary>The sim's hold: freeze the boot clock at <paramref name="seconds"/>, or (negative) let it run on.</summary>
        public void Hold(float seconds, float time) => clock.Hold(seconds, time);

        /// <summary>One frame: cut the boot when it is not <paramref name="allowed"/> or a real <paramref name="warning"/> is up, else advance it and draw. Returns whether it still runs; false takes the holder down.</summary>
        public bool Step(float time, bool allowed, bool warning)
        {
            if (!clock.Step(time, allowed, warning)) { Hide(); return false; }
            t = clock.Age(time);
            if (Ensure()) Paint();
            return true;
        }

        public void Abort()
        {
            clock.Cut();
            Hide();
        }

        public void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        private bool Ensure()
        {
            if (root != null && canvasRoot != null) return true;
            root = null;
            if (!NativeRefs.HudCanvas(out _, out Canvas canvas)) return false;
            canvasRoot = (RectTransform)canvas.transform;
            VanillaHudStyle.TryHudFont(out TMPro.TMP_FontAsset font, out Material fontMaterial);
            root = CombatAlerts.Holder(canvasRoot, "BoscaliBoot");
            mesh = AvVector.Create(root, "Boot", Quads);
            text = new ScopeText(CombatAlerts.Holder(root, "Words"), font, fontMaterial);
            lastK = 0f;
            return true;
        }

        private void Paint()
        {
            float k = canvasRoot.rect.height / 1080f;
            if (!(k > 0.01f)) return;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (k != lastK) { root.localScale = new Vector3(k, k, 1f); lastK = k; }
            float refW = canvasRoot.rect.width / k;
            VanillaHudStyle.Palette palette = VanillaHudStyle.Colours;
            Color ink = palette.AllClear;
            Color bright = Color.Lerp(ink, Color.white, ScopePalette.HiLift);
            BootMode mode = clock.Mode;

            AvQuadBuffer b = mesh.Buffer;
            b.Clear();
            // The CRT line: opens from the centre, a thin glow line either side of the bright core.
            float sweepAlpha = VisorBoot.SweepAlpha(mode, t);
            if (sweepAlpha > 0f)
            {
                float half = VisorBoot.SweepWidth(mode, t) * refW * 0.5f;
                AvStrokes.Line(b, -half, 0f, half, 0f, 2f, bright.ToRgba().WithAlpha(sweepAlpha));
                AvStrokes.Line(b, -half * 0.8f, 4f, half * 0.8f, 4f, 1f, ink.ToRgba().WithAlpha(sweepAlpha * 0.4f));
                AvStrokes.Line(b, -half * 0.8f, -4f, half * 0.8f, -4f, 1f, ink.ToRgba().WithAlpha(sweepAlpha * 0.4f));
            }
            // ALIGN: four corner brackets converging on the boresight.
            VisorBoot.Align(mode, t, out float box, out float alignAlpha);
            if (alignAlpha > 0f)
            {
                Rgba c = bright.ToRgba().WithAlpha(alignAlpha);
                const float arm = 12f;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        float cx = sx * box, cy = sy * box;
                        AvStrokes.Line(b, cx, cy, cx - sx * arm, cy, 1.5f, c);
                        AvStrokes.Line(b, cx, cy, cx, cy - sy * arm, 1.5f, c);
                    }
            }

            float left = System.Math.Max(BlockMinX, refW * 0.5f - BlockFromCentre) - refW * 0.5f;      // text x: from the centre line
            text.Begin();
            if (VisorBoot.ListShown(mode, t)) PaintList(b, left, ink, bright, palette);
            float onlineAlpha = VisorBoot.OnlineAlpha(mode, t);
            if (onlineAlpha > 0f)
            {
                if (!collapsedHeard && mode == BootMode.Full) { collapsedHeard = true; PlaySound(AvUiCue.Confirm); }
                // The closing line is the one thing left on the screen: a size up and full ink so it reads on bright sky.
                text.Draw(new System.ReadOnlySpan<char>(online, 0, onlineLen), left, FirstY - 540f, OnlinePt, ScopeAlign.Left, ink.WithAlpha(onlineAlpha), false);
                float w = onlineLen * OnlinePt * ScopeText.DesignAdvance;
                AvStrokes.Line(b, left, Y(FirstY + 11f), left + w, Y(FirstY + 11f), 1.2f, ink.ToRgba().WithAlpha(onlineAlpha * 0.8f));
            }
            if (alignAlpha > 0f)
                text.Draw("ALIGN", 0f, -(box + 12f), 10f, ScopeAlign.Center, bright.WithAlpha(alignAlpha), false);
            text.End();
            mesh.Commit();
        }

        private void PaintList(AvQuadBuffer b, float left, Color ink, Color bright, VanillaHudStyle.Palette palette)
        {
            float width = VisorBoot.Cols * Advance, rows = lineCount * Row;
            // The block's frame: the title, a rule under it, and corner ticks, so it reads as a window of the visor and not loose text.
            float typedTitle = Mathf.Clamp01((t - VisorBoot.ListStartS + 0.1f) / 0.2f);
            if (typedTitle > 0f)
            {
                const string title = "VISOR SELF-TEST";
                int n = Mathf.Min(title.Length, Mathf.CeilToInt(title.Length * typedTitle));
                text.Draw(System.MemoryExtensions.AsSpan(title, 0, n), left, TitleY - 540f, Pt, ScopeAlign.Left, bright, false);
                Rgba rule = ink.ToRgba().WithAlpha(0.6f);
                AvStrokes.Line(b, left, Y(TitleY + 9f), left + width, Y(TitleY + 9f), 1f, rule);
                float bottom = FirstY + rows - 8f;
                AvStrokes.Line(b, left - 6f, Y(FirstY - 12f), left - 6f, Y(FirstY - 4f), 1f, rule);
                AvStrokes.Line(b, left - 6f, Y(bottom), left - 6f, Y(bottom + 8f), 1f, rule);
                AvStrokes.Line(b, left - 6f, Y(bottom + 8f), left + 2f, Y(bottom + 8f), 1f, rule);
            }
            int resolved = 0;
            for (int i = 0; i < lineCount; i++)
            {
                BootLine line = lines[i];
                int len = VisorBoot.Left(buf, line, t);
                if (len == 0) continue;
                float y = FirstY + i * Row - 540f;
                text.Draw(new System.ReadOnlySpan<char>(buf, 0, len), left, y, Pt, ScopeAlign.Left, ink.WithAlpha(0.92f), false);
                if (VisorBoot.Resolved(line, t))
                {
                    resolved++;
                    text.Draw(line.Result, left + (VisorBoot.Cols - line.Result.Length) * Advance, y, Pt, ScopeAlign.Left, ToneColour(line.Tone, ink, bright, palette), line.Tone != BootTone.Ok);
                }
                else if (VisorBoot.Typed(line, t))
                {
                    buf[0] = VisorBoot.Spin(t);
                    text.Draw(new System.ReadOnlySpan<char>(buf, 0, 1), left + (VisorBoot.Cols - 1) * Advance, y, Pt, ScopeAlign.Left, bright, false);
                }
            }
            // A soft tick as each line resolves; a caution's tone for an abnormal one.
            while (resolvedHeard < resolved)
            {
                BootLine line = lines[resolvedHeard];
                PlaySound(line.Tone == BootTone.Ok || line.Tone == BootTone.Info ? AvUiCue.Navigate : AvUiCue.Caution);
                resolvedHeard++;
            }
        }

        private static Color ToneColour(BootTone tone, Color ink, Color bright, VanillaHudStyle.Palette p) =>
            tone == BootTone.Bad ? p.Alert : tone == BootTone.Warn ? p.Warning : tone == BootTone.Info ? bright : ink;

        // Holder units: y up from the centre; the layout above is in screen px from the top.
        private static float Y(float fromTop) => 540f - fromTop;

        // The pilot's UI sound volume rules (AvUiSound.Volume, 0 mutes); the recorded clips are the console's own (ui-open, ui-tab, ui-caution, ui-confirm).
        private static void PlaySound(AvUiCue cue)
        {
            try { AvUiSound.Play(cue); }
            catch (System.Exception) { /* a missing audio device must never touch the HUD */ }
        }

        public void Destroy()
        {
            clock.Cut();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
        }
    }
}
