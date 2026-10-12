using System;

namespace BoscaliSummer.Modules.Performance.Domain
{
    /// <summary>
    /// Pure planning for the explicit opt-in base-game tunings. Each knob only ever moves
    /// a Unity setting in the cheaper direction and the runtime restores the captured
    /// game value exactly when the toggle goes off, so these compose with any graphics
    /// preset instead of replacing it. Values follow the community's proven picks: the
    /// 1.5 LOD bias cap is what MpClientOpt ships, and a 1 km shadow cap (the game ships 2 km) keeps near-field
    /// shadows while dropping the far cascade work pilots never see from the cockpit.
    /// </summary>
    internal static class ClientTuningMath
    {
        public const float LodBiasCap = 1.5f;
        public const float ShadowDistanceCapMeters = 1000f;
        public const int FrameRateCapFps = 60;

        public static float PlanLodBias(float current, bool enabled)
        {
            if (!enabled) return current;
            // Unity: a lower bias swaps to cheaper LODs sooner. The game ships 4.0.
            return current <= 0f ? current : Math.Min(current, LodBiasCap);
        }

        public static float PlanShadowDistance(float current, bool enabled)
        {
            if (!enabled || current <= 0f || current <= ShadowDistanceCapMeters) return current;
            return ShadowDistanceCapMeters;
        }

        public static int PlanFrameRate(int current, bool enabled)
        {
            if (!enabled) return current;
            return current > 0 ? Math.Min(current, FrameRateCapFps) : FrameRateCapFps;
        }

        // Restore only a value we still own; a new game preset becomes the next baseline.
        public static float RestoreLodBias(float original, float current) =>
            current == PlanLodBias(original, true) ? original : current;

        public static float RestoreShadowDistance(float original, float current) =>
            current == PlanShadowDistance(original, true) ? original : current;

        public static int RestoreFrameRate(int original, int current) =>
            current == PlanFrameRate(original, true) ? original : current;
    }
}
