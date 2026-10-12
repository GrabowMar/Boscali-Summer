using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The WING page's rules (HUD v8), pure: the heading-up plot's scale and rim clamp, the state words and tones, the roster layout, the formation skeleton, the order glyph's scale, label placement.</summary>
    internal static class WingBoardMath
    {
        /// <summary>The plot: centre and radius in pixels, and the metres its rim stands for (350 m is the half ring).</summary>
        public const float PlotX = 124f, PlotY = 172f, PlotR = 104f, RimM = 700f;
        /// <summary>A wingman further than this many pixels from the centre sits on the rim (a little inside it) with a range tag.</summary>
        public const float ClampR = PlotR - 8f;
        /// <summary>Slot error beyond which a formation member reads as out of its slot, and below which a tether is not drawn.</summary>
        public const float OutM = 60f, TetherM = 30f, TetherFarM = 120f;
        /// <summary>The roster's block: first row's top, a full row's height and the step between full rows, and the room the rows may use.</summary>
        public const float RowsTop = 34f, FullRowH = 62f, FullStep = 66f, RowsRoom = 260f, RowGap = 3f, CompactMinH = 34f, FullTaskMinH = 56f;
        public const int MaxRows = 7;

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

        /// <summary>The page has something to show on its own account: someone is out of slot, fighting or has a missile on it. (A hit, running dry and turning for home are
        /// events: the page holds for a few seconds after each, it does not stay up for the rest of the sortie.)</summary>
        public static bool NeedsAttention(WingBoardMember[] m, int count)
        {
            for (int i = 0; i < count; i++)
                if (m[i].State == WingState.Join || m[i].State == WingState.Engage || m[i].State == WingState.Defend) return true;
            return false;
        }

        /// <summary>How many rows to draw, how tall, how far apart; whether a row has its task line and whether the ack log has room (three or fewer wingmen).</summary>
        public static void Rows(int count, out int shown, out float rowH, out float step, out bool taskLine, out bool acks)
        {
            shown = Math.Min(Math.Max(count, 0), MaxRows);
            if (shown <= 3)
            {
                rowH = FullRowH; step = FullStep; taskLine = true; acks = true;
                return;
            }
            rowH = (float)Math.Floor((RowsRoom - RowGap * (shown - 1)) / shown);
            if (rowH > FullRowH) rowH = FullRowH;
            if (rowH < CompactMinH) rowH = CompactMinH;
            step = rowH + RowGap;
            taskLine = rowH >= FullTaskMinH;
            acks = false;
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

        /// <summary>Metres to glyph pixels for the order pictogram: the formation fits an 18 x 15 px field either side of and behind the lead.</summary>
        public static float GlyphScale(float[] sx, float[] sy, int n)
        {
            float mx = 1f, my = 1f;
            for (int i = 0; i < n; i++) { mx = Math.Max(mx, Math.Abs(sx[i])); my = Math.Max(my, Math.Abs(sy[i])); }
            return Math.Min(18f / mx, 15f / my);
        }
    }

    /// <summary>Greedy label placement on the plot (the prototype's <c>placed</c> list): a label keeps its y unless it overlaps one already placed, then it tries 15 px steps down, up, down, up.</summary>
    internal sealed class LabelPlacer
    {
        private static readonly float[] Steps = { 0f, 15f, -15f, 30f, -30f };
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
                for (int i = 0; i < n && !hit; i++) hit = x0 < right[i] + Gap && x1 > left[i] - Gap && Math.Abs(use - top[i]) < 15f;
                if (!hit) break;
            }
            if (n < left.Length) { left[n] = x0; right[n] = x1; top[n] = use; n++; }
            return use;
        }
    }
}
