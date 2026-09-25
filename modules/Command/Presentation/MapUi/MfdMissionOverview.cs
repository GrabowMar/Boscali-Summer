using System;
using System.Globalization;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Pure escalation readout math for the MIS main tab.
    ///
    /// The game raises escalation to the highest faction score reached and clears
    /// tactical / strategic employment at the mission's own thresholds. The ladder names
    /// each rung, its threshold and its state. A zero threshold means the mission never
    /// gated that stage.
    /// </summary>
    internal static class MfdMissionOverview
    {
        public static int Stage(float current, float tactical, float strategic)
        {
            if (strategic > 0f) return current >= strategic ? 2 : tactical > 0f && current < tactical ? 0 : 1;
            if (tactical > 0f) return current < tactical ? 0 : 1;
            return 2;
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

        // The segmented tape is a score gate, not a timer. An unset or malformed gate has no
        // trustworthy fraction to draw.
        public static float NextGateProgress(float current, float tactical, float strategic)
        {
            if (float.IsNaN(current) || float.IsInfinity(current) ||
                float.IsNaN(tactical) || float.IsInfinity(tactical) ||
                float.IsNaN(strategic) || float.IsInfinity(strategic)) return 0f;
            current = Math.Max(0f, current);
            if (tactical <= 0f && strategic <= 0f) return 0f;
            if (tactical > 0f && current < tactical) return Unit(current / tactical);
            if (strategic > tactical && current < strategic)
                return Unit((current - Math.Max(0f, tactical)) / (strategic - Math.Max(0f, tactical)));
            return 1f;
        }

        public static string NextGateLabel(float current, float tactical, float strategic)
        {
            if (float.IsNaN(current) || float.IsInfinity(current) ||
                float.IsNaN(tactical) || float.IsInfinity(tactical) ||
                float.IsNaN(strategic) || float.IsInfinity(strategic)) return "SCORE LINK UNAVAILABLE";
            current = Math.Max(0f, current);
            if (tactical <= 0f && strategic <= 0f) return "NO ESCALATION GATES";
            if (tactical > 0f && current < tactical)
                return "SCORE " + Whole(current) + "  /  +" + Whole(tactical - current) + " TO TACTICAL";
            if (strategic > tactical && current < strategic)
                return "SCORE " + Whole(current) + "  /  +" + Whole(strategic - current) + " TO STRATEGIC";
            return "SCORE " + Whole(current) + "  /  ALL GATES CLEARED";
        }

        private static float Unit(float value) => Math.Max(0f, Math.Min(1f, value));

        internal static string Whole(float value) =>
            Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);
    }
}
