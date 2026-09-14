using System;
using System.Globalization;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure escalation readout math for the MIS main tab.
    ///
    /// The game raises escalation to the highest faction score reached and clears
    /// tactical / strategic employment at the mission's own thresholds, so the ladder
    /// gives each stage an equal third of the track and states the distance to the next
    /// threshold. A zero threshold means the mission never gated that stage.
    /// </summary>
    internal static class MfdMissionOverview
    {
        public static int Stage(float current, float tactical, float strategic)
        {
            if (strategic > 0f) return current >= strategic ? 2 : tactical > 0f && current < tactical ? 0 : 1;
            if (tactical > 0f) return current < tactical ? 0 : 1;
            return 2;
        }

        public static float Fraction(float current, float tactical, float strategic)
        {
            current = Math.Max(0f, current);
            if (strategic > 0f)
            {
                if (current < tactical) return Unit(current / Math.Max(tactical, 0.001f)) / 3f;
                if (current < strategic)
                    return 1f / 3f + Unit((current - tactical) / Math.Max(strategic - tactical, 0.001f)) / 3f;
                return 2f / 3f + Unit((current - strategic) / strategic) / 3f;
            }
            if (tactical > 0f) return current < tactical ? Unit(current / tactical) / 3f : 2f / 3f;
            return 1f;
        }

        public static string Caption(float current, float tactical, float strategic)
        {
            current = Math.Max(0f, current);
            if (tactical <= 0f && strategic <= 0f) return "NO ESCALATION THRESHOLDS SET";
            if (tactical > 0f && current < tactical)
                return "CURRENT " + Whole(current) + "  ·  " + Whole(tactical - current) + " TO TACTICAL NUCLEAR";
            if (strategic <= 0f) return "CURRENT " + Whole(current) + "  ·  TACTICAL NUCLEAR CLEARED";
            if (current < strategic)
                return "CURRENT " + Whole(current) + "  ·  TACTICAL CLEARED  ·  " + Whole(strategic - current) + " TO STRATEGIC NUCLEAR";
            return "CURRENT " + Whole(current) + "  ·  STRATEGIC NUCLEAR CLEARED";
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, value));

        private static string Whole(float value) =>
            Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
    }
}
