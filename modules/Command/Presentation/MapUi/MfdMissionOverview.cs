using System;
using System.Globalization;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure escalation readout math for the MIS main tab.
    ///
    /// The game raises escalation to the highest faction score reached and clears
    /// tactical / strategic employment at the mission's own thresholds. The ladder names
    /// each rung, its threshold and its state; <see cref="Fraction"/> keeps the equal-thirds
    /// position for any caller that still draws the old single track. A zero threshold
    /// means the mission never gated that stage.
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

        /// <summary>The rungs in order, as the ladder names them.</summary>
        public static string StageName(int stage) =>
            stage <= 0 ? "CONVENTIONAL" : stage == 1 ? "TACTICAL NUCLEAR" : "STRATEGIC NUCLEAR";

        /// <summary>One rung's threshold; an unset gate never reads as a confident zero.</summary>
        public static string Threshold(int stage, float tactical, float strategic)
        {
            if (stage <= 0) return "BASELINE — ALWAYS ACTIVE";
            float value = stage == 1 ? tactical : strategic;
            return value > 0f ? "THRESHOLD " + Whole(value) : "NO THRESHOLD SET";
        }

        /// <summary>
        /// One rung's state against the current stage. The word and the rung's rail both
        /// carry it, so a colour-blind player still reads which gate is holding.
        /// </summary>
        public static string StageState(int stage, int currentStage, bool thresholdSet) =>
            stage == currentStage ? "CURRENT"
            : stage < currentStage ? "CLEARED"
            : thresholdSet ? "PENDING" : "NOT SET";

        public static string Caption(float current, float tactical, float strategic)
        {
            current = Math.Max(0f, current);
            if (tactical <= 0f && strategic <= 0f) return "NO ESCALATION GATES";
            if (tactical > 0f && current < tactical)
                return "CUR " + Whole(current) + " · TAC IN " + Whole(tactical - current);
            if (strategic <= 0f) return "CUR " + Whole(current) + " · TAC ACTIVE";
            if (current < strategic)
                return "CUR " + Whole(current) + " · TAC ACTIVE · STR IN " + Whole(strategic - current);
            return "CUR " + Whole(current) + " · STR ACTIVE";
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, value));

        internal static string Whole(float value) =>
            Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
    }
}
