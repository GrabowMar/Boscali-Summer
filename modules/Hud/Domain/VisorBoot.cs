using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>How the visor boots: a full power-on (the first aircraft of the session, about 3.4 s) or a short re-sync (a respawn or an aircraft change, 1 s).</summary>
    internal enum BootMode : byte { None, Full, Resync }

    /// <summary>The visor's zones in the order they come alive.</summary>
    internal enum BootGroup : byte { Rim, Brow, Left, Board, Arcs }

    /// <summary>The ink of a self-test result: the HUD's green, the lifted green, amber (abnormal) and red (failed).</summary>
    internal enum BootTone : byte { Ok, Info, Warn, Bad }

    /// <summary>One line of the self-test: what is checked, what was found, and when it types on and resolves (seconds on the boot clock). Built once per boot.</summary>
    internal sealed class BootLine
    {
        public string Tag, Detail, Result, Head;
        public BootTone Tone;
        public float StartS, ResolveS;
    }

    /// <summary>The real state the self-test reports. A part whose datum is not available (<c>Has...</c> false) gets no line: nothing is made up.</summary>
    internal struct BootInputs
    {
        /// <summary>The HUD is bound to an aircraft: the visor is synced to it.</summary>
        public bool Hmd;
        /// <summary>The session is hosted solo or online (both known).</summary>
        public bool HasNet, Online;
        /// <summary>The aircraft has a radar and it is emitting.</summary>
        public bool HasRadar, RadarOn;
        /// <summary>Weapon stations the stores feed read, and whether the selected one is safe (<see cref="HasSafety"/>: one is selected).</summary>
        public int Stations;
        public bool HasSafety, Safe;
        /// <summary>The fuel level 0..1, and BINGO.</summary>
        public bool HasFuel, Bingo;
        public float Fuel;
        /// <summary>The nearest friendly field and the metres to it (the SYSTEMS strip's way home).</summary>
        public string Field;
        public float FieldM;
        public bool Metric;
        /// <summary>The countermeasure stations.</summary>
        public CmStation[] Cm;
        public int CmCount;
    }

    /// <summary>
    /// The visor's power-on (2026-11-16, "HELMET ON"), pure: the timeline of both boots, the reveal order and its delays, the self-test lines for a set of real inputs and how a line reads at a given time. Seconds on the
    /// boot clock (<see cref="VisorBootClock"/>), reference pixels where a length is given. The presentation (<c>VisorBootHud</c>) only draws what this says.
    /// <para>FULL: 0.0-0.4 the visor is dark, a line sweeps open from the centre and the rim draws in from the corners; 0.3-1.8 the SELF-TEST types on, each line ending in a state that ticks from a spinner to its result;
    /// 1.2-2.6 the zones come alive one after another (rim, brow, left column, board, arcs) and an ALIGN bracket converges on the boresight; 2.6-3.4 the list collapses to VISOR ONLINE and the callsign, holds and fades.
    /// RESYNC: the same in 1 s with a one-line VISOR RESYNC and no list.</para>
    /// </summary>
    internal static class VisorBoot
    {
        public const float FullS = 3.4f, ResyncS = 1.0f;
        /// <summary>The self-test list's first line starts at this, its last resolves by <see cref="ListEndS"/>; a line types for <see cref="TypeS"/> and resolves <see cref="ResolveAfterS"/> after it starts.</summary>
        public const float ListStartS = 0.3f, ListEndS = 1.8f, TypeS = 0.15f, ResolveAfterS = 0.45f, MaxStepS = 0.2f;
        /// <summary>When the list gives way to VISOR ONLINE and when that is gone.</summary>
        public const float CollapseS = 2.6f, OnlineFadeS = 0.3f;
        public const int MaxLines = 7, Cols = 32, TagWidth = 6, NameMax = 16;

        public static float Duration(BootMode mode) => mode == BootMode.Full ? FullS : mode == BootMode.Resync ? ResyncS : 0f;

        /// <summary>The first aircraft of the session boots in full, every later one re-syncs.</summary>
        public static BootMode ModeFor(bool seenBefore) => seenBefore ? BootMode.Resync : BootMode.Full;

        // ---- reveal order ----

        private static void Window(BootMode mode, BootGroup group, out float start, out float length)
        {
            if (mode == BootMode.Resync)
            {
                switch (group)
                {
                    case BootGroup.Rim: start = 0f; length = 0.35f; return;
                    case BootGroup.Brow: start = 0.15f; length = 0.3f; return;
                    case BootGroup.Left: start = 0.23f; length = 0.3f; return;
                    case BootGroup.Board: start = 0.31f; length = 0.3f; return;
                    default: start = 0.4f; length = 0.45f; return;
                }
            }
            switch (group)
            {
                case BootGroup.Rim: start = 0.05f; length = 0.6f; return;
                case BootGroup.Brow: start = 1.2f; length = 0.5f; return;
                case BootGroup.Left: start = 1.3f; length = 0.5f; return;      // 100 ms behind the brow, the board 120 behind that, the arcs 130 after
                case BootGroup.Board: start = 1.42f; length = 0.5f; return;
                default: start = 1.55f; length = 0.7f; return;
            }
        }

        /// <summary>How far a zone has come alive, 0 (not yet) to 1 (as it is in flight). 1 with no boot running.</summary>
        public static float Reveal(BootMode mode, BootGroup group, float t)
        {
            if (mode == BootMode.None) return 1f;
            Window(mode, group, out float start, out float length);
            return Clamp01((t - start) / length);
        }

        /// <summary>The visibility of a block that is coming alive: 0 before it starts, 1 when done, and in between a stutter that settles (a display powering up), never fully off after its first frame.</summary>
        public static float Flicker(float reveal, float t)
        {
            if (!(reveal > 0f)) return 0f;
            if (reveal >= 1f) return 1f;
            bool on = (((int)(t * 26f) + (int)(reveal * 9f)) & 1) == 0;
            return on ? 0.45f + 0.55f * reveal : 0.1f + 0.2f * reveal;
        }

        // ---- the sweep line, the ALIGN bracket, the closing line ----

        /// <summary>The CRT line opening from the centre: its half length as a share of the screen's half width.</summary>
        public static float SweepWidth(BootMode mode, float t)
        {
            float open = mode == BootMode.Resync ? 0.25f : 0.4f;
            return Ease(Clamp01(t / open));
        }

        /// <summary>The sweep line's alpha: full while it opens, gone a moment after.</summary>
        public static float SweepAlpha(BootMode mode, float t)
        {
            float open = mode == BootMode.Resync ? 0.25f : 0.4f;
            return t < open ? 1f : Clamp01(1f - (t - open) / 0.2f);
        }

        /// <summary>The ALIGN bracket: its half size (from 150 down to 26 px, converging on the boresight) and its alpha (steady while it converges, then it blinks off). Alpha 0 outside its window.</summary>
        public static void Align(BootMode mode, float t, out float half, out float alpha)
        {
            half = 26f; alpha = 0f;
            if (mode == BootMode.None) return;
            float start = mode == BootMode.Full ? 1.9f : 0.5f, converge = mode == BootMode.Full ? 0.5f : 0.3f;
            if (t < start) return;
            float k = Clamp01((t - start) / converge);
            half = 26f + 124f * (1f - Ease(k));
            if (k < 1f) { alpha = 0.9f; return; }
            float since = t - start - converge;
            if (mode == BootMode.Resync || since >= 0.4f) return;      // a short resync just converges; the full one blinks, then is gone
            alpha = (((int)(since * 12f)) & 1) == 0 ? 0.9f : 0f;
        }

        /// <summary>The closing line (VISOR ONLINE / VISOR RESYNC): its alpha. Full: from the collapse to the end, fading over the last <see cref="OnlineFadeS"/>; resync: 0.2 to the end, fading over the last 0.25.</summary>
        public static float OnlineAlpha(BootMode mode, float t)
        {
            if (mode == BootMode.Full)
                return t < CollapseS ? 0f : Clamp01((FullS - t) / OnlineFadeS);
            if (mode == BootMode.Resync)
                return t < 0.2f ? 0f : Clamp01((ResyncS - t) / 0.25f);
            return 0f;
        }

        /// <summary>The self-test list is on the screen (typing or resolved), before it collapses.</summary>
        public static bool ListShown(BootMode mode, float t) => mode == BootMode.Full && t >= ListStartS && t < CollapseS;

        // ---- self-test lines ----

        /// <summary>Fills <paramref name="into"/> with the lines the real state supports, in display order, and times them; returns how many. A part with no datum is left out.</summary>
        public static int BuildLines(in BootInputs i, BootLine[] into)
        {
            int n = 0;
            if (i.Hmd) n = Add(into, n, "HMD", "SYNC", "OK", BootTone.Ok);
            if (i.HasNet) n = Add(into, n, "NET", i.Online ? "ONLINE" : "SOLO", "OK", BootTone.Ok);
            if (i.HasRadar) n = Add(into, n, "RDR", "RADAR", i.RadarOn ? "EMIT" : "STBY", i.RadarOn ? BootTone.Warn : BootTone.Ok);
            if (i.Stations > 0 && i.HasSafety) n = Add(into, n, "STORES", i.Stations + " STN", i.Safe ? "SAFE" : "ARMED", i.Safe ? BootTone.Warn : BootTone.Ok);
            if (i.HasFuel)
            {
                int pct = (int)Math.Round(Clamp01(i.Fuel) * 100f);
                BootTone tone = i.Bingo || i.Fuel <= FuelEndurance.WarningLevel ? BootTone.Bad : i.Fuel <= FuelEndurance.CautionLevel ? BootTone.Warn : BootTone.Ok;
                n = Add(into, n, "FUEL", pct + "%", i.Bingo ? "BINGO" : tone == BootTone.Ok ? "OK" : "LOW", tone);
            }
            if (!string.IsNullOrEmpty(i.Field) && !float.IsNaN(i.FieldM) && !float.IsInfinity(i.FieldM))
            {
                n = Add(into, n, "NAV", ShortName(i.Field), Distance(i.FieldM, i.Metric), BootTone.Ok);
            }
            if (i.Cm != null && i.CmCount > 0) n = AddEw(into, n, i);
            Time(into, n);
            return n;
        }

        /// <summary>The EW line: the rounds left in each dispenser (chaff, flares), two at most so the line fits its column; a jammer has no rounds to count and is left out.</summary>
        private static int AddEw(BootLine[] into, int n, in BootInputs i)
        {
            if (n >= MaxLines) return n;
            var words = new System.Text.StringBuilder(24);
            BootTone tone = BootTone.Ok;
            string result = "OK";
            int shown = 0;
            for (int k = 0; k < i.CmCount && shown < 2; k++)
            {
                CmStation s = i.Cm[k];
                if (s.Chargeable) continue;
                if (words.Length > 0) words.Append(' ');
                words.Append(HudCm.Word(s)).Append(' ').Append(s.Ammo);
                shown++;
                CmLevel level = HudCm.Level(s);
                if (level == CmLevel.Empty) { tone = BootTone.Bad; result = "EMPTY"; }
                else if (level == CmLevel.Low && tone != BootTone.Bad) { tone = BootTone.Warn; result = "LOW"; }
            }
            return shown == 0 ? n : Add(into, n, "EW", words.ToString(), result, tone);
        }

        /// <summary>A field's name that fits its column: whole words when it can ("K92 HIGHWAY STRIP" is "K92 HIGHWAY"), else cut at <see cref="NameMax"/>.</summary>
        public static string ShortName(string name)
        {
            if (name.Length <= NameMax) return name;
            int cut = name.LastIndexOf(' ', NameMax);
            return cut >= 5 ? name.Substring(0, cut) : name.Substring(0, NameMax);
        }

        private static int Add(BootLine[] into, int n, string tag, string detail, string result, BootTone tone)
        {
            if (n >= MaxLines || n >= into.Length) return n;
            BootLine line = into[n] ??= new BootLine();
            line.Tag = tag; line.Detail = detail; line.Result = result; line.Tone = tone;
            line.Head = tag.PadRight(TagWidth) + (tag.Length >= TagWidth ? " " : "") + detail;
            return n + 1;
        }

        private static void Time(BootLine[] into, int n)
        {
            float step = n > 1 ? Math.Min(MaxStepS, (ListEndS - ListStartS - ResolveAfterS) / (n - 1)) : 0f;
            for (int k = 0; k < n; k++)
            {
                into[k].StartS = ListStartS + k * step;
                into[k].ResolveS = into[k].StartS + ResolveAfterS;
            }
        }

        /// <summary>The distance as the SYSTEMS strip words it: whole kilometres, or nautical miles.</summary>
        public static string Distance(float metres, bool metric) =>
            metric ? (int)Math.Round(metres / 1000f) + "KM" : (int)Math.Round(metres / 1852f) + "NM";

        /// <summary>The line is resolved (its result shows) at <paramref name="t"/>.</summary>
        public static bool Resolved(BootLine line, float t) => t >= line.ResolveS;

        /// <summary>The line's left part at <paramref name="t"/>: its head typing on, then the dots that fill toward the result column while the check runs; written to <paramref name="buf"/> (at least <see cref="Cols"/> long), its length returned. Nothing before the line starts.</summary>
        public static int Left(char[] buf, BootLine line, float t)
        {
            if (t < line.StartS) return 0;
            string head = line.Head;
            int typed = (int)((t - line.StartS) / TypeS * head.Length);
            if (typed < head.Length)
            {
                for (int k = 0; k < typed; k++) buf[k] = head[k];
                return typed;
            }
            int n = head.Length;
            for (int k = 0; k < n; k++) buf[k] = head[k];
            int room = Math.Max(2, Cols - head.Length - 2 - line.Result.Length);
            float run = Clamp01((t - line.StartS - TypeS) / Math.Max(0.01f, line.ResolveS - line.StartS - TypeS));
            int dots = Resolved(line, t) ? room : (int)(room * run);
            if (dots > 0)
            {
                buf[n++] = ' ';
                for (int k = 0; k < dots; k++) buf[n++] = '.';
            }
            return n;
        }

        /// <summary>The spinner's character at <paramref name="t"/> (a line that has typed and not resolved).</summary>
        public static char Spin(float t) => "|/-\\"[((int)(t * 14f)) & 3];

        /// <summary>The line has finished typing (it may still be checking).</summary>
        public static bool Typed(BootLine line, float t) => t >= line.StartS + TypeS;

        // ---- small maths ----

        public static float Ease(float k) => 1f - (1f - k) * (1f - k);
        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }

    /// <summary>
    /// The boot's clock and its one rule, pure: it starts at a time, runs to the mode's duration, and is cut short at once when it is not allowed (an external view, a pause, the option, reduced motion) or when a real warning
    /// is up; a hold (the sim's) freezes it at a time so each phase can be captured still. <see cref="Active"/> says the layers below should be in their boot state; once it is false everything is as in flight.
    /// </summary>
    internal sealed class VisorBootClock
    {
        private float start, hold = float.NaN;

        public BootMode Mode { get; private set; }
        public bool Active => Mode != BootMode.None;
        public bool Held => !float.IsNaN(hold);

        /// <summary>Seconds into the boot at <paramref name="now"/>.</summary>
        public float Age(float now) => float.IsNaN(hold) ? now - start : hold;

        public void Start(BootMode mode, float now) { Mode = mode; start = now; }

        /// <summary>Freeze the clock at <paramref name="seconds"/> (a negative number lets it run on from there).</summary>
        public void Hold(float seconds, float now)
        {
            if (seconds < 0f) { if (!float.IsNaN(hold)) start = now - hold; hold = float.NaN; return; }
            hold = seconds;
        }

        /// <summary>The boot is over at once: everything snaps to its flying state.</summary>
        public void Cut() { Mode = BootMode.None; hold = float.NaN; }

        /// <summary>One frame: cut when not <paramref name="allowed"/> or a <paramref name="warning"/> is up, finish at the mode's duration (never while held). Returns <see cref="Active"/>.</summary>
        public bool Step(float now, bool allowed, bool warning)
        {
            if (Mode == BootMode.None) return false;
            if (!allowed || warning) { Cut(); return false; }
            if (float.IsNaN(hold) && Age(now) >= VisorBoot.Duration(Mode)) Cut();
            return Active;
        }
    }
}
