using System;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// Escalation stage name used for pacing only.
    /// </summary>
    internal enum OperationTempoStage : byte { Conventional, Tactical, Strategic }

    /// <summary>
    /// Escalation-aware war tempo: how often the director offers contracts and how much a
    /// fresh offer is worth at the moment it is created.
    ///
    /// It reads only the mission's own thresholds, with a tactical or strategic threshold
    /// greater than zero gating that stage. An unset threshold of zero means "no such stage"
    /// and can never invent one, exactly like the MIS main tab's escalation ladder. Nonsense
    /// escalation or thresholds fall back to the conventional stage; every interval and
    /// multiplier is hard-clamped to the bounds declared here. The callers keep the existing
    /// money/XP and RewardMultiplier clamps on top of the tempo multiplier.
    /// </summary>
    internal static class OperationTempo
    {
        internal const float ConventionalInterval = 30f;
        internal const float TacticalInterval = 24f;
        internal const float StrategicInterval = 18f;
        internal const float MinimumInterval = 12f;
        internal const float MaximumInterval = 30f;
        internal const float ConventionalReward = 1f;
        internal const float TacticalReward = 1.15f;
        internal const float StrategicReward = 1.35f;
        internal const float MinimumReward = 0.5f;
        internal const float MaximumReward = 2f;

        internal static OperationTempoStage Stage(float current, float tactical, float strategic)
        {
            if (!Finite(current) || !Finite(tactical) || !Finite(strategic)) return OperationTempoStage.Conventional;
            if (strategic > 0f) return current >= strategic ? OperationTempoStage.Strategic
                : tactical > 0f && current < tactical ? OperationTempoStage.Conventional : OperationTempoStage.Tactical;
            if (tactical > 0f) return current < tactical ? OperationTempoStage.Conventional : OperationTempoStage.Tactical;
            return OperationTempoStage.Strategic;
        }

        internal static float Interval(float current, float tactical, float strategic) =>
            IntervalFor(Stage(current, tactical, strategic));

        internal static float RewardScale(float current, float tactical, float strategic) =>
            RewardFor(Stage(current, tactical, strategic));

        internal static float IntervalFor(OperationTempoStage stage)
        {
            float interval = stage switch
            {
                OperationTempoStage.Strategic => StrategicInterval,
                OperationTempoStage.Tactical => TacticalInterval,
                _ => ConventionalInterval
            };
            return Math.Clamp(interval, MinimumInterval, MaximumInterval);
        }

        internal static float RewardFor(OperationTempoStage stage)
        {
            float scale = stage switch
            {
                OperationTempoStage.Strategic => StrategicReward,
                OperationTempoStage.Tactical => TacticalReward,
                _ => ConventionalReward
            };
            return Math.Clamp(scale, MinimumReward, MaximumReward);
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static bool Finite(float current, float tactical, float strategic) =>
            Finite(current) && Finite(tactical) && Finite(strategic);

        /// <summary>Flatten and clamp a scale, the same way the director treats RewardMultiplier.</summary>
        internal static float Scale(float value) => Math.Clamp(Finite(value) ? value : 1f, MinimumReward, MaximumReward);
    }
}
