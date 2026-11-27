using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The WING page's rules (HUD v8), pure: the heading-up plot's scale and rim clamp, the state words and tones, the roster layout, the formation skeleton, label placement.</summary>
    internal static class WingBoardMath
    {
        /// <summary>The plot: centre and radius in pixels, and the metres its rim stands for (350 m is the half ring).</summary>
        public const float PlotX = 72f, PlotY = 106f, PlotR = 64f, RimM = 700f;
        /// <summary>A wingman further than this many pixels from the centre sits on the rim (a little inside it) with a range tag.</summary>
        public const float ClampR = PlotR - 8f;
        /// <summary>Slot error beyond which a formation member reads as out of its slot, and below which a tether is not drawn.</summary>
        public const float TetherM = 30f, TetherFarM = 120f;
        /// <summary>
        /// The roster right of the plot (refit 5.3): its left edge and width, the first block's line A (a middle, painter units), the step between blocks and line B under line A. At most <see cref="MaxRows"/> blocks; the rest
        /// are counted under the plot.
        /// </summary>
        public const float RosterX = 152f, RosterW = 288f, BlockTop = 48f, BlockPitch = 48f, LinePitch = 18f;
        public const int MaxRows = 3;

        public static float Scale => PlotR / RimM;

        /// <summary>A wingman's place on the plot (pixels, absolute) from its metres, heading up; <paramref name="clamped"/> when it was past the rim and pulled onto it.</summary>
        public static void Place(float xM, float yM, out float px, out float py, out bool clamped)
        {
            float d = (float)Math.Sqrt(xM * xM + yM * yM) * Scale, k = d > ClampR ? ClampR / d : 1f;
            clamped = k < 1f;
            px = PlotX + xM * Scale * k;
            py = PlotY - yM * Scale * k;
        }

        /// <summary>Unclamped plot position.</summary>
        public static void At(float xM, float yM, out float px, out float py)
        {
            px = PlotX + xM * Scale;
            py = PlotY - yM * Scale;
        }

        /// <summary>Compass bearing of a plot offset (clockwise from the nose).</summary>
        public static float BearingDeg(float xM, float yM) => (float)(Math.Atan2(xM, yM) * 180.0 / Math.PI);

        public static string Word(WingState s)
        {
            switch (s)
            {
                case WingState.Join: return "JOIN";
                case WingState.Engage: return "ENGAGE";
                case WingState.Defend: return "DEFEND";
                case WingState.Winch: return "WINCH";
                case WingState.Rtb: return "RTB";
                case WingState.Refit: return "REFIT";
                case WingState.Damaged: return "DAMAGED";
                case WingState.Ground: return "GND";
                case WingState.Land: return "LAND";
                default: return "FORM";
            }
        }

        /// <summary>Caution for the states that need the pilot's attention (hit, a missile on it, out of weapons); a wingman going home reads quiet.</summary>
        public static ScopeTone ToneOf(WingState s) =>
            s == WingState.Damaged || s == WingState.Defend || s == WingState.Winch ? ScopeTone.Amber
            : s == WingState.Rtb || s == WingState.Refit || s == WingState.Ground || s == WingState.Land ? ScopeTone.Dim
            : ScopeTone.Ink;

        /// <summary>The chevron is hollow while it is joining or out of weapons (not yet / no longer a full member).</summary>
        public static bool Hollow(WingState s) => s == WingState.Join || s == WingState.Winch;

        /// <summary>Members that are out of their slot (the tab's headline reads "n OUT").</summary>
        public static int OutCount(WingBoardMember[] m, int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++) if (m[i].State == WingState.Join) n++;
            return n;
        }

        /// <summary>Formation skeleton: each slot hangs from the nearest of the lead and the slots nearer the lead than it (a finger four reads lead-2, lead-3, 3-4).
        /// <paramref name="parent"/>[i] is the slot index it hangs from, -1 for the lead.</summary>
        public static void Skeleton(float[] sx, float[] sy, int n, int[] parent)
        {
            for (int i = 0; i < n; i++)
            {
                float di = sx[i] * sx[i] + sy[i] * sy[i], best = di;
                parent[i] = -1;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    float dj = sx[j] * sx[j] + sy[j] * sy[j];
                    if (dj > di || (dj == di && j > i)) continue;       // only slots at least as near the lead (ties: the lower index)
                    float dx = sx[i] - sx[j], dy = sy[i] - sy[j], d = dx * dx + dy * dy;
                    if (d < best) { best = d; parent[i] = j; }
                }
            }
        }
    }

    /// <summary>Greedy label placement on the plot (the prototype's <c>placed</c> list): a label keeps its y unless it overlaps one already placed, then it tries 17 px steps down, up, down, up.</summary>
    internal sealed class LabelPlacer
    {
        private static readonly float[] Steps = { 0f, 17f, -17f, 34f, -34f, 51f, -51f };
        /// <summary>Two labels this close sideways read as one run (1.9KM#2 1.8KM): they count as overlapping.</summary>
        private const float Gap = 6f;
        private readonly float[] left = new float[32], right = new float[32], top = new float[32];
        private int n;

        public void Reset() => n = 0;

        /// <summary>Take a spot as placed without moving it: something already drawn there (the digit by a slot diamond) that the labels placed after it keep clear of (LAYOUT.md 12.5).</summary>
        public void Reserve(float x0, float x1, float y)
        {
            if (n < left.Length) { left[n] = x0; right[n] = x1; top[n] = y; n++; }
        }

        /// <summary>Place a label spanning x0..x1 at y; returns the y it takes.</summary>
        public float Place(float x0, float x1, float y)
        {
            float use = y;
            for (int s = 0; s < Steps.Length; s++)
            {
                use = y + Steps[s];
                bool hit = false;
                for (int i = 0; i < n && !hit; i++) hit = x0 < right[i] + Gap && x1 > left[i] - Gap && Math.Abs(use - top[i]) < 17f;
                if (!hit) break;
            }
            if (n < left.Length) { left[n] = x0; right[n] = x1; top[n] = use; n++; }
            return use;
        }
    }
}
