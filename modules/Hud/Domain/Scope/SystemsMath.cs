using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the SYSTEMS tab headline says, most urgent first.</summary>
    internal enum SystemsHeadline : byte { Nominal, Structure, Joker, FuelLeak, EngineOut, Bingo, Fire }

    /// <summary>The rules of the SYSTEMS page, pure: tones, the fuel marks, the headline, the dispenser grids and the words they use.</summary>
    internal static class SystemsMath
    {
        /// <summary>The fuel marks: BINGO goes red within this share of the tanks of the level, JOKER amber within this (the prototype's 6 / 21 points).</summary>
        public const float BingoNear = 0.06f, JokerNear = 0.21f;
        /// <summary>The endurance reads red under this many minutes, amber under <see cref="EndCautionMin"/>.</summary>
        public const float EndWarningMin = 5f, EndCautionMin = 10f;
        /// <summary>A margin at or under this many points of the tanks cautions; at or under zero it is gone.</summary>
        public const int MarginCaution = 15;

        public static ScopeTone ToneOf(HudTone tone) => tone == HudTone.Warning ? ScopeTone.Red : tone == HudTone.Caution ? ScopeTone.Amber : ScopeTone.Ink;

        public static ScopeTone ToneOf(LampState state) => state == LampState.Warning ? ScopeTone.Red : state == LampState.Caution ? ScopeTone.Amber : ScopeTone.Ink;

        public static int Percent(float level01) => (int)Math.Round(Math.Max(0f, Math.Min(1f, level01)) * 100f, MidpointRounding.AwayFromZero);

        /// <summary>Whole minutes of endurance, or false when it is unknown (a parked aircraft, no burn rate yet).</summary>
        public static bool EnduranceMinutes(float seconds, out int minutes)
        {
            minutes = 0;
            if (!float.IsFinite(seconds) || seconds < 0f || seconds >= 360000f) return false;
            minutes = (int)(seconds / 60f);
            return true;
        }

        public static ScopeTone EndTone(float seconds) =>
            !EnduranceMinutes(seconds, out int _) ? ScopeTone.Dim : seconds < EndWarningMin * 60f ? ScopeTone.Red : seconds < EndCautionMin * 60f ? ScopeTone.Amber : ScopeTone.Dim;

        /// <summary>Margin over BINGO in points of the tanks (negative: past it).</summary>
        public static int MarginPoints(in FuelState f) => (int)Math.Round((f.Level - f.Need) * 100f, MidpointRounding.AwayFromZero);

        public static ScopeTone MarginTone(int points) => points <= 0 ? ScopeTone.Red : points <= MarginCaution ? ScopeTone.Amber : ScopeTone.Ink;

        /// <summary>Reference marks stay dim until they matter.</summary>
        public static ScopeTone BingoMarkTone(in FuelState f) => f.Level <= f.Need + BingoNear ? ScopeTone.Red : ScopeTone.Dim;

        public static ScopeTone JokerMarkTone(in FuelState f) => f.Level <= f.Need + JokerNear ? ScopeTone.Amber : ScopeTone.Dim;

        /// <summary>The burn rate in points of the tanks per minute (0 when no endurance is known).</summary>
        public static float BurnPointsPerMinute(in FuelState f) =>
            EnduranceMinutes(f.EnduranceS, out int _) && f.EnduranceS > 0f ? f.Level / f.EnduranceS * 6000f : 0f;

        /// <summary>The page's headline and its tone for this damage and fuel state.</summary>
        public static SystemsHeadline Headline(in DamageState d, in FuelState f)
        {
            if ((d.Flags & DamageFlags.Fire) != 0) return SystemsHeadline.Fire;
            if (f.Valid && f.Bingo) return SystemsHeadline.Bingo;
            if (d.EnginesOut > 0) return SystemsHeadline.EngineOut;
            if ((d.Flags & DamageFlags.FuelLeak) != 0) return SystemsHeadline.FuelLeak;
            if (f.Valid && f.Joker) return SystemsHeadline.Joker;
            if (d.DetachedRatio >= HudDamage.StructureCaution) return SystemsHeadline.Structure;
            return SystemsHeadline.Nominal;
        }

        public static ScopeTone HeadlineTone(SystemsHeadline h, in DamageState d) =>
            h == SystemsHeadline.Fire || h == SystemsHeadline.Bingo || h == SystemsHeadline.EngineOut ? ScopeTone.Red
            : h == SystemsHeadline.Nominal ? ScopeTone.Dim
            : h == SystemsHeadline.Structure && d.DetachedRatio >= HudDamage.StructureWarning ? ScopeTone.Red
            : ScopeTone.Amber;

        private static readonly string[] EngineOut = { "ENG OUT", "ENG 1 OUT", "ENG 2 OUT", "ENG 3 OUT", "ENG 4 OUT" };

        public static string HeadlineText(SystemsHeadline h, in DamageState d)
        {
            switch (h)
            {
                case SystemsHeadline.Fire: return "FIRE";
                case SystemsHeadline.Bingo: return "BINGO";
                case SystemsHeadline.EngineOut: return d.Engines > 1 && d.EnginesOut < d.Engines && d.EnginesOut < EngineOut.Length ? EngineOut[d.EnginesOut] : EngineOut[0];
                case SystemsHeadline.FuelLeak: return "FUEL LEAK";
                case SystemsHeadline.Joker: return "JOKER";
                case SystemsHeadline.Structure: return "STR " + (100 - Percent(d.DetachedRatio)) + "%";
                default: return "NOMINAL";
            }
        }

        /// <summary>Whether two damage readings would draw the same rows and zones.</summary>
        public static bool SameDamage(in DamageState a, in DamageState b) =>
            a.Flags == b.Flags && a.Engines == b.Engines && a.EnginesOut == b.EnginesOut && Percent(a.DetachedRatio) == Percent(b.DetachedRatio) &&
            a.EngineSlots == b.EngineSlots && a.EngineOut == b.EngineOut && a.Leak == b.Leak && a.Fire == b.Fire && a.WingLoss == b.WingLoss;

        /// <summary>Which side of the airframe a part sits on: its lateral offset (metres, + = right) against a dead band for the centreline.</summary>
        public static byte SideOf(float lateralM, float centreBand) =>
            (byte)(lateralM < -centreBand ? ZoneSide.Left : lateralM > centreBand ? ZoneSide.Right : ZoneSide.Centre);

        /// <summary>"RDY" ready, "ON" while jamming, "CHG" while the capacitor recharges.</summary>
        public static string JamWord(in CmStation s) => s.Active ? "ON" : s.Charge >= 0.98f ? "RDY" : "CHG";

        /// <summary>The G gauge is live from this absolute load (G) and for this many seconds after the load last was that high: level cruise has no G gauge.</summary>
        public const float GaugeLiveG = 2.5f, GaugeHoldSeconds = 3f;

        /// <summary>
        /// Whether the G gauge is live at <paramref name="now"/>: the absolute load is at least <see cref="GaugeLiveG"/>, or was within <see cref="GaugeHoldSeconds"/>. <paramref name="lastAt"/> is the
        /// clock of the last tick it was (start it at negative infinity, so a quiet airframe is not live).
        /// </summary>
        public static bool GaugeLive(float g, float now, ref float lastAt)
        {
            if (Math.Abs(g) >= GaugeLiveG) lastAt = now;
            return now - lastAt <= GaugeHoldSeconds;
        }

        /// <summary>
        /// The margin over BINGO as the base line of the SYSTEMS strip says it: "+90%", or "NO MARGIN" once it is gone. <paramref name="shortForm"/> is the one that gives way when the base name needs the room:
        /// "+90" and "NO MGN". Composed in the shared scratch (see <see cref="ScopeStr"/>): use it at once.
        /// </summary>
        public static ReadOnlySpan<char> MarginWords(int points, bool shortForm) =>
            points > 0 ? ScopeStr.Cat("+", points, shortForm ? null : "%") : shortForm ? "NO MGN" : "NO MARGIN";

        /// <summary>The wind's compass-from degrees as 1..360 (a calm north wind reads 360, never 000).</summary>
        public static int WindFromDegrees(float deg)
        {
            int d = (int)Math.Round(((deg % 360f) + 360f) % 360f, MidpointRounding.AwayFromZero) % 360;
            return d == 0 ? 360 : d;
        }
    }
}
