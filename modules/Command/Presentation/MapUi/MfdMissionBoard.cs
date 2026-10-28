using System;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>Lifecycle phase of a mission objective or contract, as the MIS board reads it.</summary>
    internal enum MissionPhase { Offered, Active, Done, Closed }

    /// <summary>
    /// Pure copy rules for the MIS briefing board: distances, ranks and the escalation ladder.
    /// No game types, so the whole surface is checkable offline.
    /// </summary>
    internal static class MfdMissionBoard
    {
        /// <summary>Metres to a short mono readout. An unknown range is a dash, never a confident zero.</summary>
        public static string Distance(float meters)
        {
            if (!float.IsFinite(meters) || meters < 0f) return "—";
            if (meters < 1000f) return AvNum.Fixed(Math.Round(meters / 10f) * 10f, 0) + " M";
            return AvNum.Fixed(meters / 1000f, meters < 100000f ? 1 : 0) + " KM";
        }

        /// <summary>Short mono threshold under a ladder rung; an unset gate never reads as a zero.</summary>
        public static string RungThreshold(int rung, float tactical, float strategic)
        {
            if (rung <= 0) return "BASELINE";
            float value = rung == 1 ? tactical : strategic;
            return value > 0f ? MfdMissionOverview.Whole(value) : "UNSET";
        }

        /// <summary>Fraction of the ladder track that is lit: stage rungs cleared plus progress toward the next gate.</summary>
        public static float LadderFill(int stage, float nextGateProgress)
        {
            if (stage >= 2) return 1f;
            float fill = (stage + Math.Max(0f, Math.Min(1f, nextGateProgress))) / 2f;
            return Math.Max(0f, Math.Min(1f, fill));
        }
    }
}
