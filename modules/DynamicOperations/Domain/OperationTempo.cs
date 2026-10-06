using System;

namespace BoscaliSummer.Modules.DynamicOperations.Domain
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
    /// and can never invent one (the ladder is shared with the MIS main tab: Core.Math.EscalationStage). Nonsense
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
            if (!float.IsFinite(current) || !float.IsFinite(tactical) || !float.IsFinite(strategic)) return OperationTempoStage.Conventional;
            return (OperationTempoStage)BoscaliSummer.Core.Math.EscalationStage.Of(current, tactical, strategic);
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


        internal static bool Finite(float current, float tactical, float strategic) =>
            float.IsFinite(current) && float.IsFinite(tactical) && float.IsFinite(strategic);

        /// <summary>Flatten and clamp a scale, the same way the director treats RewardMultiplier.</summary>
        internal static float Scale(float value) => Math.Clamp(float.IsFinite(value) ? value : 1f, MinimumReward, MaximumReward);
    }
}
