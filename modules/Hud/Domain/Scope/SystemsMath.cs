using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>What the SYSTEMS tab headline says, most urgent first.</summary>
    internal enum SystemsHeadline : byte { Nominal, Structure, Joker, FuelLeak, EngineOut, Bingo, Fire }

    /// <summary>The rules of the SYSTEMS page, pure: tones, the fuel marks, the headline, the dispenser grids and the words they use.</summary>
    internal static class SystemsMath
    {
        public static ScopeTone ToneOf(HudTone tone) => tone == HudTone.Warning ? ScopeTone.Red : tone == HudTone.Caution ? ScopeTone.Amber : ScopeTone.Ink;

        public static ScopeTone ToneOf(LampState state) => state == LampState.Warning ? ScopeTone.Red : state == LampState.Caution ? ScopeTone.Amber : ScopeTone.Ink;

        public static int Percent(float level01) => (int)Math.Round(Math.Max(0f, Math.Min(1f, level01)) * 100f, MidpointRounding.AwayFromZero);

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

        /// <summary>The sky-cover word of a 0..1 cover: CLR below 0.1, FEW below 0.3, SCT below 0.6, BKN below 0.9, else OVC.</summary>
        public static string SkyWord(float cover) => cover < 0.1f ? "CLR" : cover < 0.3f ? "FEW" : cover < 0.6f ? "SCT" : cover < 0.9f ? "BKN" : "OVC";

        /// <summary>The wind's compass-from degrees as 1..360 (a calm north wind reads 360, never 000).</summary>
        public static int WindFromDegrees(float deg)
        {
            int d = (int)Math.Round(((deg % 360f) + 360f) % 360f, MidpointRounding.AwayFromZero) % 360;
            return d == 0 ? 360 : d;
        }
    }
}
