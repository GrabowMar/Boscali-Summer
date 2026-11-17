using BoscaliSummer.Core.Fx;
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
    /// Built like <see cref="TapeHud"/>: a holder under the native HUD canvas scaled by canvas height / 1080, one quad mesh and pooled <see cref="ScopeText"/> labels; once the boot is over the holder is hidden and costs nothing.
    /// The self-test block is centred just under the flight symbology (x about 796..1124, y 586..770 of a 1080 screen): under the reticle and the speed and altitude numbers, between the arcs' captions, over the cockpit's coaming; it is transient (1.5 s, then VISOR ONLINE in its place) and only the sweep line and the ALIGN bracket cross the middle.
    /// </summary>
    internal sealed class VisorBootHud
    {
        private const int Quads = 96;
        // The self-test is the star of its 1.5 s: 1.4x the board's small-label size, centred just under the flight symbology and over the dashboard (reference px from the top of a 1080 screen).
        private const float Pt = 16.8f, TitlePt = 14f, OnlinePt = 19f, Row = 21f, TitleY = 600f, FirstY = 632f, Advance = Pt * ScopeText.DesignAdvance;
        private const string Title = "VISOR SELF-TEST · HMD-9";

        private readonly VisorBootClock clock = new VisorBootClock();
        private readonly BootLine[] lines = new BootLine[VisorBoot.MaxLines];
        private readonly char[] buf = new char[VisorBoot.Cols + 8];
        private readonly char[] online = new char[48];
        private RectTransform canvasRoot, root;
        private AvVector mesh;
        private ScopeText text;
        private int lineCount, onlineLen, resolvedHeard, startedHeard;
        private float lastK, t;
        private bool collapsedHeard, onlineBeat, alignHeard;

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

        /// <summary>The closing line came up since the last call (once per boot): the owner flashes the rim and brow for the end beat.</summary>
        public bool TakeOnlineBeat() { bool b = onlineBeat; onlineBeat = false; return b; }

        /// <summary>Start a boot with the real state it will report. <paramref name="callsign"/> goes on the closing line. One allocation here, none per frame.</summary>
        public void Begin(BootMode mode, float time, in BootInputs inputs, string callsign)
        {
            lineCount = mode == BootMode.Full ? VisorBoot.BuildLines(inputs, lines) : 0;
            string tail = mode == BootMode.Full ? "VISOR ONLINE" : "VISOR RESYNC";
            string closing = string.IsNullOrEmpty(callsign) ? tail : tail + " · " + callsign;
            onlineLen = System.Math.Min(closing.Length, online.Length);
            closing.CopyTo(0, online, 0, onlineLen);
            resolvedHeard = startedHeard = 0;
            collapsedHeard = onlineBeat = alignHeard = false;
            clock.Start(mode, time);
            t = 0f;
            PlaySound(mode == BootMode.Full ? VisorClip.PowerUp : VisorClip.Resync);      // the capacitor whine, a relay and the settling chirp, at the moment the line opens
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
            root = CombatAlerts.Holder(BoscaliSummer.Core.Ui.HudLayer.Ensure() ? BoscaliSummer.Core.Ui.HudLayer.Static : canvasRoot, "BoscaliBoot");
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
            if (!alignHeard && t >= VisorBoot.AlignLockS(mode)) { alignHeard = true; PlaySound(VisorClip.Align); }      // the bracket has closed on the boresight
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

            float left = -VisorBoot.Cols * Advance * 0.5f;      // text x: from the centre line, the block centred on the reticle's axis
            text.Begin();
            if (VisorBoot.ListShown(mode, t)) PaintList(b, left, ink, bright, palette);
            float onlineAlpha = VisorBoot.OnlineAlpha(mode, t);
            if (onlineAlpha > 0f)
            {
                if (!collapsedHeard)
                {
                    collapsedHeard = true;
                    onlineBeat = true;      // the end beat (rim and brow flash), once
                    if (mode == BootMode.Full) PlaySound(VisorClip.Online);
                }
                // The closing line is the one thing left on the screen: centred where the list was, a size up and full ink so it reads on bright sky.
                text.Draw(new System.ReadOnlySpan<char>(online, 0, onlineLen), 0f, FirstY - 540f, OnlinePt, ScopeAlign.Center, ink.WithAlpha(onlineAlpha), false);
                float w = onlineLen * OnlinePt * ScopeText.DesignAdvance;
                AvStrokes.Line(b, -w * 0.5f, Y(FirstY + 13f), w * 0.5f, Y(FirstY + 13f), 1.2f, ink.ToRgba().WithAlpha(onlineAlpha * 0.8f));
            }
            if (alignAlpha > 0f)
                text.Draw("ALIGN", 0f, -(box + 12f), 10f, ScopeAlign.Center, bright.WithAlpha(alignAlpha), false);
            text.End();
            mesh.Commit();
        }

        private void PaintList(AvQuadBuffer b, float left, Color ink, Color bright, VanillaHudStyle.Palette palette)
        {
            float width = VisorBoot.Cols * Advance;
            // The block's frame: the header, a thin progress rule under it that fills as the lines resolve, and corner ticks (no plate), so it reads as a window of the visor and not loose text.
            float typedTitle = Mathf.Clamp01((t - VisorBoot.ListStartS + 0.1f) / 0.2f);
            if (typedTitle > 0f)
            {
                int n = Mathf.Min(Title.Length, Mathf.CeilToInt(Title.Length * typedTitle));
                text.Draw(System.MemoryExtensions.AsSpan(Title, 0, n), left, TitleY - 540f, TitlePt, ScopeAlign.Left, bright, false);
                Rgba rule = ink.ToRgba().WithAlpha(0.6f);
                float ruleY = Y(TitleY + 13f), fill = VisorBoot.Progress(lines, lineCount, t) * width;
                AvStrokes.Line(b, left, ruleY, left + width, ruleY, 1f, ink.ToRgba().WithAlpha(0.3f));
                if (fill > 0f) AvStrokes.Line(b, left, ruleY, left + fill, ruleY, 2f, bright.ToRgba());
                // Corner ticks round the whole block: above the header and under the last row.
                float x0 = left - 9f, x1 = left + width + 9f, top = TitleY - 14f, bottom = FirstY + (lineCount - 1) * Row + 13f;
                const float arm = 9f;
                AvStrokes.Line(b, x0, Y(top), x0 + arm, Y(top), 1f, rule); AvStrokes.Line(b, x0, Y(top), x0, Y(top + arm), 1f, rule);
                AvStrokes.Line(b, x1, Y(top), x1 - arm, Y(top), 1f, rule); AvStrokes.Line(b, x1, Y(top), x1, Y(top + arm), 1f, rule);
                AvStrokes.Line(b, x0, Y(bottom), x0 + arm, Y(bottom), 1f, rule); AvStrokes.Line(b, x0, Y(bottom), x0, Y(bottom - arm), 1f, rule);
                AvStrokes.Line(b, x1, Y(bottom), x1 - arm, Y(bottom), 1f, rule); AvStrokes.Line(b, x1, Y(bottom), x1, Y(bottom - arm), 1f, rule);
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
                    // The state word snaps in bright (white-lifted, bold) and settles to its tone.
                    bool snap = t - line.ResolveS < VisorBoot.SnapS;
                    Color word = snap ? Color.Lerp(bright, Color.white, 0.6f) : ToneColour(line.Tone, ink, bright, palette);
                    text.Draw(line.Result, left + (VisorBoot.Cols - line.Result.Length) * Advance, y, Pt, ScopeAlign.Left, word, snap || line.Tone != BootTone.Ok);
                }
                else if (VisorBoot.Typed(line, t))
                {
                    buf[0] = VisorBoot.Spin(t);
                    text.Draw(new System.ReadOnlySpan<char>(buf, 0, 1), left + (VisorBoot.Cols - 1) * Advance, y, Pt, ScopeAlign.Left, bright, false);
                }
            }
            // A tick as each line starts typing; the two-note OK as it resolves, the sour double blip for an amber or red result.
            while (startedHeard < lineCount && t >= lines[startedHeard].StartS) { PlaySound(VisorClip.Tick); startedHeard++; }
            while (resolvedHeard < resolved)
            {
                BootLine line = lines[resolvedHeard];
                PlaySound(line.Tone == BootTone.Ok || line.Tone == BootTone.Info ? VisorClip.Ok : VisorClip.Fault);
                resolvedHeard++;
            }
        }

        private static Color ToneColour(BootTone tone, Color ink, Color bright, VanillaHudStyle.Palette p) =>
            tone == BootTone.Bad ? p.Alert : tone == BootTone.Warn ? p.Warning : tone == BootTone.Info ? bright : ink;

        // Holder units: y up from the centre; the layout above is in screen px from the top.
        private static float Y(float fromTop) => 540f - fromTop;

        // The HMD-9 visor sounds (Core/Fx/VisorSound: the option VisorSound and the pilot's UI volume rule them; 0 is silent).
        private static void PlaySound(VisorClip clip) => VisorSound.Play(clip);

        public void Destroy()
        {
            clock.Cut();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            canvasRoot = null;
        }
    }
}
