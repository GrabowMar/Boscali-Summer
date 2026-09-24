using System;
using System.Globalization;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.TheaterOps.Domain
{
    /// <summary>
    /// The theater widget's copy, pure so the test project links it directly. It names the main
    /// effort and the offensive the host is running; it never claims a directive the host has
    /// not set.
    /// </summary>
    internal static class TheaterOpsHudCopy
    {
        /// <summary>"MAIN EFFORT · HILL 402", or the honest none when nothing is set.</summary>
        public static string Effort(bool hasPriority, string label) =>
            hasPriority && !string.IsNullOrEmpty(label) ? "MAIN EFFORT · " + label : "MAIN EFFORT · NONE";

        public static string PhaseWord(TheaterOperationPhase phase)
        {
            switch (phase)
            {
                case TheaterOperationPhase.Mustering: return "MUSTERING";
                case TheaterOperationPhase.Planning: return "PLANNING";
                case TheaterOperationPhase.AwaitingTarget: return "AWAITING TARGET";
                case TheaterOperationPhase.Launching: return "LAUNCHING";
                case TheaterOperationPhase.Assault: return "ASSAULT";
                case TheaterOperationPhase.Holding: return "HOLDING";
                case TheaterOperationPhase.Concluded: return "CONCLUDED";
                default: return "UNKNOWN";
            }
        }

        /// <summary>"OP BREAKWATER · LAUNCHING · T-1:30"; null when there is no operation.</summary>
        public static string Detail(string operationName, TheaterOperationPhase phase, float countdown)
        {
            if (string.IsNullOrEmpty(operationName)) return null;
            string text = "OP " + operationName + " · " + PhaseWord(phase);
            string clock = Clock(countdown);
            return string.IsNullOrEmpty(clock) ? text : text + " · " + clock;
        }

        /// <summary>"T-90s" / "T-1:30"; empty when no countdown is running.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return "";
            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total + "s";
            return "T-" + (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static float Bar(float progress)
        {
            if (float.IsNaN(progress) || float.IsInfinity(progress)) return 0f;
            return progress < 0f ? 0f : progress > 1f ? 1f : progress;
        }
    }
}
