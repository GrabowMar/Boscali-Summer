using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The ink of a mark on the heading tape: WARNING (an inbound missile: a filled chevron), CAUTION (a lock, a radar spike, the ace: an open amber diamond) and INK-HI (the navigation marks: an open caret, the word in regular Ink). A threat word is bold in its tone.</summary>
    internal enum TapeTone : byte { Warning, Caution, Hi }

    /// <summary>
    /// The game's own heading tape as measured this frame, reference pixels of a 1080-high screen with x from the screen's centre line and y UP from its centre (the holder's units): where its middle is, where its tick line is,
    /// how many pixels one degree is and how many degrees the tape shows either side of the nose. Not valid until it was measured.
    /// </summary>
    internal struct TapeFrame
    {
        public float CentreX, TickY, PxPerDeg, HalfRangeDeg;
        public bool Valid => PxPerDeg > 0.01f && HalfRangeDeg > 1f;
    }

    /// <summary>One mark hanging under the tape: its true bearing, its word, and where it hangs (x on the tape, the label's sideways nudge and row).</summary>
    internal struct TapeMark
    {
        /// <summary>Compass degrees as given (the tape scrolls under the nose, so the mark's place follows the heading every frame, <see cref="Rel"/>).</summary>
        public float Abs;
        public TapeTone Tone;
        public string Label;
        /// <summary>Lower first: what a pilot has to act on soonest.</summary>
        public float Urgency;
        /// <summary>Past the tape's end: -1 left, +1 right, 0 on it (the mark then stands at that end with a chevron pointing out).</summary>
        public int Beyond;
        /// <summary>Where the mark hangs now (holder x) and the label's centre, sideways, from the mark; <see cref="Low"/>: the label stands on the lower row, clear of the game's heading readout.</summary>
        public float X, LabelDx, LabelW;
        public bool Low;
    }

    /// <summary>Bearings and the tape's scale, pure.</summary>
    internal static class TapeBearings
    {
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

        /// <summary>The compass bearing of a flat offset, 0..360.</summary>
        public static float CompassBearing(float dx, float dz) =>
            (float)((Math.Atan2(dx, dz) * 180.0 / Math.PI % 360.0 + 360.0) % 360.0);

        /// <summary>
        /// Where a bearing <paramref name="relDeg"/> off the nose stands on the tape: its x, pinned to the tape's end when it is past it (<paramref name="beyond"/> -1 left, +1 right). The tape's middle is the nose.
        /// </summary>
        public static float X(float relDeg, in TapeFrame tape, out int beyond)
        {
            float rel = Wrap180(relDeg);
            beyond = rel > tape.HalfRangeDeg ? 1 : rel < -tape.HalfRangeDeg ? -1 : 0;
            float shown = beyond != 0 ? beyond * tape.HalfRangeDeg : rel;
            return tape.CentreX + shown * tape.PxPerDeg;
        }
    }

    /// <summary>The marks' geometry in reference pixels: the triangle hangs just under the tape's tick line, the word under it.</summary>
    internal static class TapeLayout
    {
        public const int MaxMarks = 6;
        /// <summary>The triangle: gap under the tick line, depth, half width; the chevron past an end stands this far outside the triangle.</summary>
        public const float TriGap = 3f, TriDepth = 7f, TriHalf = 4f, ChevronOut = 12f, ChevronSize = 7f;
        /// <summary>A label's centre under the tick line on its row (clear of the triangle), and the lower row that clears the game's heading readout and its boresight triangle under the tape's middle.</summary>
        public const float LabelDrop = 19f, LowLabelDrop = 58f;
        /// <summary>A mark this close to the middle (px) puts its word on the lower row.</summary>
        public const float CentreClear = 48f;
        /// <summary>A label character's advance at LABEL (the HUD face's 0.65 em), its padding and the clear gap between two labels.</summary>
        public const float CharW = 7.8f, LabelPad = 4f, LabelGap = 12f;

        public static float LabelWidth(int chars) => chars * CharW + LabelPad;
    }

    /// <summary>
    /// The marks of one tick, pure: the caller adds what matters now (<see cref="Add"/>, a compass bearing), <see cref="Resolve"/> orders them by urgency, keeps at most <see cref="TapeLayout.MaxMarks"/> and, where two
    /// words on the same row would overlap, nudges the less urgent one sideways or drops it. Allocation-free after construction.
    /// </summary>
    internal sealed class TapeMarkSet
    {
        private const int Capacity = 24;
        private readonly TapeMark[] candidates = new TapeMark[Capacity];
        private readonly TapeMark[] shown = new TapeMark[TapeLayout.MaxMarks];
        private int count;

        public int Count { get; private set; }
        public TapeMark this[int i] => shown[i];

        public void Clear() { count = 0; Count = 0; }

        /// <summary>Offer a mark: its compass bearing, the ink, the word and how urgent it is (lower first). Offers past the capacity are ignored.</summary>
        public void Add(float compassDeg, TapeTone tone, string label, float urgency)
        {
            if (count >= Capacity || string.IsNullOrEmpty(label) || float.IsNaN(compassDeg) || float.IsInfinity(compassDeg)) return;
            candidates[count++] = new TapeMark { Abs = compassDeg, Tone = tone, Label = label, Urgency = float.IsNaN(urgency) ? 1000f : urgency };
        }

        public void Resolve(float headingDeg, in TapeFrame tape)
        {
            // Insertion sort, stable: most urgent first, then the one nearest the nose.
            for (int i = 1; i < count; i++)
            {
                TapeMark c = candidates[i];
                float rc = Math.Abs(TapeBearings.Wrap180(c.Abs - headingDeg));
                int j = i - 1;
                while (j >= 0 && (candidates[j].Urgency > c.Urgency || (candidates[j].Urgency == c.Urgency && Math.Abs(TapeBearings.Wrap180(candidates[j].Abs - headingDeg)) > rc))) { candidates[j + 1] = candidates[j]; j--; }
                candidates[j + 1] = c;
            }
            Count = 0;
            for (int i = 0; i < count && Count < TapeLayout.MaxMarks; i++)
            {
                TapeMark c = candidates[i];
                c.X = TapeBearings.X(c.Abs - headingDeg, tape, out c.Beyond);
                c.LabelW = TapeLayout.LabelWidth(c.Label.Length);
                c.Low = Math.Abs(c.X - tape.CentreX) < TapeLayout.CentreClear;
                if (c.Beyond != 0 && PinnedTwin(c)) continue;      // two radars off the same end are one SPK there, not "SPK SPK"
                if (!Place(ref c, tape)) continue;
                shown[Count++] = c;
            }
        }

        private bool PinnedTwin(in TapeMark c)
        {
            for (int k = 0; k < Count; k++)
                if (shown[k].Beyond == c.Beyond && shown[k].Label == c.Label) return true;
            return false;
        }

        /// <summary>Tries the label where it is, then nudged right, then left, past the words already placed on its row; false: no room, the mark is dropped.</summary>
        private bool Place(ref TapeMark c, in TapeFrame tape)
        {
            float reach = tape.HalfRangeDeg * tape.PxPerDeg + 40f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                float dx = 0f;
                if (attempt > 0)
                {
                    // Nudge by the overlap with the first word in the way.
                    float shift = 0f;
                    for (int k = 0; k < Count; k++)
                    {
                        if (shown[k].Low != c.Low) continue;
                        float need = (shown[k].LabelW + c.LabelW) * 0.5f + TapeLayout.LabelGap - Math.Abs(shown[k].X + shown[k].LabelDx - c.X);
                        if (need > 0f) { shift = need; break; }
                    }
                    dx = attempt == 1 ? shift : -shift;
                    if (shift <= 0f) return false;
                }
                float centre = c.X + dx;
                if (Math.Abs(centre - tape.CentreX) > reach) continue;
                bool clash = false;
                for (int k = 0; k < Count && !clash; k++)
                    clash = shown[k].Low == c.Low && Math.Abs(shown[k].X + shown[k].LabelDx - centre) < (shown[k].LabelW + c.LabelW) * 0.5f + TapeLayout.LabelGap;
                if (clash) continue;
                c.LabelDx = dx;
                return true;
            }
            return false;
        }

        /// <summary>A number that changes whenever anything drawn but the marks' x would: the count and each mark's word, ink, side, row and nudge.</summary>
        public int Signature()
        {
            int s = Count;
            for (int i = 0; i < Count; i++)
            {
                TapeMark c = shown[i];
                s = s * 31 + (int)c.Tone;
                s = s * 31 + c.Label.GetHashCode();
                s = s * 31 + c.Beyond;
                s = s * 31 + (c.Low ? 1 : 0);
                s = s * 31 + (int)Math.Round(c.LabelDx);
            }
            return s;
        }
    }

    /// <summary>The marks' words that carry a number, built once per value (a mark's seconds change once a second; nothing is allocated twice).</summary>
    internal static class TapeLabels
    {
        private static readonly string[] Missiles = new string[10];

        /// <summary>"M1": the missile's index (the seconds live on the THREAT time bar and the banner).</summary>
        public static string Missile(int number)
        {
            int n = Math.Max(0, Math.Min(9, number));
            return Missiles[n] ??= "M" + n;
        }
    }

    /// <summary>The visor frame's pure helpers.</summary>
    internal static class VisorFrameMath
    {
        /// <summary>
        /// The first reference y the right edge's tick ladder may start at: 300, or a multiple of 50 under the TAC BOARD's box (<paramref name="boardBottom"/>, 0: none) when that box
        /// reaches the right edge, so no tick crosses the board.
        /// </summary>
        public static float LadderTop(float boardBottom) =>
            boardBottom > 0f ? Math.Max(300f, (float)Math.Ceiling((boardBottom + 8f) / 50f) * 50f) : 300f;
    }
}
