using BoscaliSummer.Core.Contracts;
using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The ink of a bearing caret: the HUD's alert red, caution amber, white, and the ink lifted toward white (the mod has no cyan: the runway, tanker and zones take it).</summary>
    internal enum BrowTone : byte { Red, Amber, White, Bright }

    /// <summary>One caret hanging under the brow rail: where it points (degrees off the nose, clamped to the scale), its word and where on the rail it hangs.</summary>
    internal struct BrowCaret
    {
        /// <summary>Degrees off the nose, + right, -180..180 as given; <see cref="Shown"/> is the clamped one the rail uses.</summary>
        public float Rel;
        public BrowTone Tone;
        public string Label;
        /// <summary>Lower first: what a pilot has to act on soonest.</summary>
        public float Urgency;
        /// <summary>The bearing is past the scale: -1 left, +1 right, 0 on it (the word then carries an arrow).</summary>
        public int Beyond;
        /// <summary>Where the caret hangs on the rail (rail units, centre origin x, y down from the screen's top).</summary>
        public float X, Y;
        /// <summary>The label's centre, kept inside the rail's zone, and its width.</summary>
        public float LabelX, LabelW;
    }

    /// <summary>
    /// The visor's brow rail (2026-11-16, mockup <c>evolved-hud-mockup.html</c> function <c>hud</c>), pure: the curve, its ticks, and which bearing carets hang under it. Reference pixels of a
    /// 1080-high screen, x from the screen's centre line, y down from its top: a quadratic from (-158, 64) through (0, 16) to (158, 64), 37 ticks (every 6th long), the boresight mark
    /// under its middle. A caret hangs at the bearing relative to the aircraft's nose, 0 at the middle and +-90 at the ends; past 90 it sticks to the end with an arrow in its word.
    /// </summary>
    internal static class BrowRail
    {
        public const float Half = 158f, EndY = 64f, ApexY = 16f;
        public const int TickCount = 37, MajorEvery = 6, MaxCarets = 6;
        /// <summary>Degrees from the middle to either end.</summary>
        public const float ScaleDeg = 90f;
        /// <summary>The caret triangle: half width, and the depth from the rail to its apex and its base; the label stands this far under the rail point.</summary>
        public const float CaretHalf = 5f, CaretTop = 7f, CaretBase = 16f, LabelDrop = 29f;
        /// <summary>A label character's advance at the 12 px design size (the HUD face's 0.65 em), plus its padding and the clear gap between two labels.</summary>
        public const float CharW = 7.8f, LabelPad = 4f, LabelGap = 4f, LabelPt = HudType.Label;
        /// <summary>The boresight mark under the rail's middle: apex y, base y, half width.</summary>
        public const float BoreTop = 46f, BoreBase = 54f, BoreHalf = 5f;

        /// <summary>The rail's point at <paramref name="t"/> (0 left end .. 1 right end).</summary>
        public static void Point(float t, out float x, out float y)
        {
            float u = 1f - t;
            x = (u * u - t * t) * -Half;      // (1-t)^2 * -158 + t^2 * 158
            y = u * u * EndY + 2f * u * t * ApexY + t * t * EndY;
        }

        /// <summary>Tick <paramref name="i"/> (0..<see cref="TickCount"/>-1): its foot on the rail and the unit normal that points up and out (ticks stand above the rail, carets hang under it).</summary>
        public static void Tick(int i, out float x, out float y, out float nx, out float ny, out bool major)
        {
            float t = i / (float)(TickCount - 1);
            Point(t, out x, out y);
            Point(Math.Min(1f, t + 0.001f), out float x2, out float y2);
            float dx = x2 - x, dy = y2 - y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6f) { nx = 0f; ny = -1f; }
            else { nx = -dy / len; ny = dx / len; }
            if (ny > 0f) { nx = -nx; ny = -ny; }      // up the screen
            major = i % MajorEvery == 0;
        }

        /// <summary>The rail's t for a bearing: clamped to the scale.</summary>
        public static float T(float relDeg) => (Math.Max(-ScaleDeg, Math.Min(ScaleDeg, relDeg)) + ScaleDeg) / (2f * ScaleDeg);

        /// <summary>Any angle in degrees as -180..180.</summary>
        public static float Wrap180(float deg)
        {
            if (float.IsNaN(deg) || float.IsInfinity(deg)) return 0f;
            deg %= 360f;
            return deg > 180f ? deg - 360f : deg <= -180f ? deg + 360f : deg;
        }

        /// <summary>The bearing of a flat offset (<paramref name="dx"/> east, <paramref name="dz"/> north) off a heading, -180..180 (+ right).</summary>
        public static float RelativeBearing(float dx, float dz, float headingDeg) =>
            Wrap180((float)(Math.Atan2(dx, dz) * 180.0 / Math.PI) - headingDeg);

        /// <summary>A label's width at the design size.</summary>
        public static float LabelWidth(int chars) => chars * CharW + LabelPad;

        // ---- Status caption ----

        /// <summary>
        /// The one status line above the rail, real state only: "VISOR AR · AUTO · NVG OFF · FOV 62". <paramref name="scope"/> 0 AUTO, 1 MANUAL, 2 OFF (the board's page mode);
        /// <paramref name="nvg"/> null: no goggles, the word is left out; <paramref name="fovDeg"/> NaN or 0: left out. Returns the length.
        /// </summary>
        public static int Caption(char[] b, int scope, bool? nvg, float fovDeg)
        {
            int n = AvNumFormat.Append(b, 0, "VISOR AR · ");
            n = AvNumFormat.Append(b, n, scope == 1 ? "MANUAL" : scope == 2 ? "SCOPE OFF" : "AUTO");
            if (nvg.HasValue) n = AvNumFormat.Append(b, AvNumFormat.Append(b, n, " · NVG "), nvg.Value ? "ON" : "OFF");
            if (fovDeg > 1f && fovDeg < 360f) n = AvNumFormat.Write(b, AvNumFormat.Append(b, n, " · FOV "), Math.Round(fovDeg), 0);
            return n;
        }

        // ---- Frame furniture ----

        /// <summary>
        /// The first reference y the right edge's tick ladder may start at: the mockup's 300, or a multiple of 50 under the TAC BOARD's box (<paramref name="boardBottom"/>, 0: none) when that box
        /// reaches the right edge, so no tick crosses the board.
        /// </summary>
        public static float LadderTop(float boardBottom) =>
            boardBottom > 0f ? Math.Max(300f, (float)Math.Ceiling((boardBottom + 8f) / 50f) * 50f) : 300f;
    }

    /// <summary>
    /// The carets of one tick, pure: the caller adds what matters now (<see cref="Add"/>), <see cref="Resolve"/> orders them by urgency, clamps them to the scale, keeps at most
    /// <see cref="BrowRail.MaxCarets"/> and drops the least urgent of any two whose words would overlap. Allocation-free after construction.
    /// </summary>
    internal sealed class BrowCarets
    {
        private const int Capacity = 24;
        private readonly BrowCaret[] candidates = new BrowCaret[Capacity];
        private readonly BrowCaret[] shown = new BrowCaret[BrowRail.MaxCarets];
        private int count;

        public int Count { get; private set; }
        public BrowCaret this[int i] => shown[i];

        public void Clear() { count = 0; Count = 0; }

        /// <summary>Offer a caret: <paramref name="relDeg"/> degrees off the nose (+ right, any angle), the word, the ink and how urgent it is (lower first). Offers past the capacity are ignored.</summary>
        public void Add(float relDeg, BrowTone tone, string label, float urgency)
        {
            if (count >= Capacity || string.IsNullOrEmpty(label) || float.IsNaN(relDeg)) return;
            candidates[count++] = new BrowCaret { Rel = BrowRail.Wrap180(relDeg), Tone = tone, Label = label, Urgency = float.IsNaN(urgency) ? 1000f : urgency };
        }

        public void Resolve()
        {
            // Insertion sort, stable: most urgent first, then the one nearest the nose.
            for (int i = 1; i < count; i++)
            {
                BrowCaret c = candidates[i];
                int j = i - 1;
                while (j >= 0 && (candidates[j].Urgency > c.Urgency || (candidates[j].Urgency == c.Urgency && Math.Abs(candidates[j].Rel) > Math.Abs(c.Rel)))) { candidates[j + 1] = candidates[j]; j--; }
                candidates[j + 1] = c;
            }
            Count = 0;
            for (int i = 0; i < count && Count < BrowRail.MaxCarets; i++)
            {
                BrowCaret c = candidates[i];
                c.Beyond = c.Rel > BrowRail.ScaleDeg ? 1 : c.Rel < -BrowRail.ScaleDeg ? -1 : 0;
                BrowRail.Point(BrowRail.T(c.Rel), out c.X, out c.Y);
                c.LabelW = BrowRail.LabelWidth(c.Label.Length + (c.Beyond != 0 ? 1 : 0));
                float room = BrowRail.Half - c.LabelW * 0.5f;
                c.LabelX = Math.Max(-room, Math.Min(room, c.X));
                bool clash = false;
                for (int k = 0; k < Count && !clash; k++)
                    clash = Math.Abs(shown[k].LabelX - c.LabelX) < (shown[k].LabelW + c.LabelW) * 0.5f + BrowRail.LabelGap;
                if (!clash) shown[Count++] = c;
            }
        }

        /// <summary>A number that changes whenever anything drawn would: the count, and each caret's word, ink, side and whole-degree place.</summary>
        public int Signature()
        {
            int s = Count;
            for (int i = 0; i < Count; i++)
            {
                BrowCaret c = shown[i];
                s = s * 31 + (int)Math.Round(c.Rel);
                s = s * 31 + (int)c.Tone;
                s = s * 31 + c.Label.GetHashCode();
                s = s * 31 + c.Beyond;
            }
            return s;
        }
    }
}
