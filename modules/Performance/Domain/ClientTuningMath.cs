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
        public const float LodBiasCap = 1.0f;
        public const float ShadowDistanceCapMeters = 1000f;
        // URP renders the 3D scene at this share of the screen and upscales it (0.8 = 64% of the pixels).
        public const float RenderScaleCap = 0.8f;
        public const float RenderScaleMin = 0.5f;

        // Distance bands of the ported NO_Optimisation client savings (its defaults).
        public const float ReducedMeters = 2500f;
        public const float FarMeters = 7500f;
        public const float SleepMeters = 15000f;
        public const float WakeMeters = 14000f;
        // SLEEP FAR UNITS (a FAST-only saving) hides models past 8 km, where an aircraft is two or three pixels at 1080p;
        // measured 2026-11-19 against the unmodded game: 15 km left FAST behind it. Tracer thinning keeps its own 15 km.
        public const float UnitSleepMeters = 8000f;
        public const float UnitWakeMeters = 7300f;

        /// <summary>Share of another player's visual-only bullets still drawn at this distance.</summary>
        public static float TracerKeep(float distanceSq, bool tracer)
        {
            if (distanceSq >= SleepMeters * SleepMeters) return 0f;
            if (distanceSq >= FarMeters * FarMeters) return tracer ? 0.5f : 0.1f;
            if (distanceSq >= ReducedMeters * ReducedMeters) return tracer ? 1f : 0.5f;
            return 1f;
        }

        /// <summary>A unit sleeps past 8 km and wakes inside 7.3 km, so it never flickers on the edge.</summary>
        public static bool ShouldSleep(float distanceSq, bool sleeping)
        {
            float edge = sleeping ? UnitWakeMeters : UnitSleepMeters;
            return distanceSq >= edge * edge;
        }

        public static float PlanLodBias(float current, bool enabled)
        {
            if (!enabled) return current;
            // Unity: a lower bias swaps to cheaper LODs sooner. The game ships 4.0.
            return current <= 0f ? current : Math.Min(current, LodBiasCap);
        }

        public static float PlanShadowDistance(float current, bool enabled, float cap = ShadowDistanceCapMeters)
        {
            // cap 0 is NO SHADOWS: URP draws no main-light shadows at a zero shadow distance
            if (!enabled || current <= 0f || current <= cap) return current;
            return cap;
        }

        public static float ClampRenderScale(float cap) =>
            float.IsNaN(cap) ? RenderScaleCap : Math.Max(RenderScaleMin, Math.Min(1f, cap));

        public static float PlanRenderScale(float current, bool enabled, float cap = RenderScaleCap)
        {
            cap = ClampRenderScale(cap);
            if (!enabled || current <= 0f || current <= cap) return current;
            return cap;
        }

        // Restore only a value we still own; a new game preset becomes the next baseline.
        public static float RestoreLodBias(float original, float current) =>
            current == PlanLodBias(original, true) ? original : current;

        public static float RestoreShadowDistance(float original, float current, float cap = ShadowDistanceCapMeters) =>
            current == PlanShadowDistance(original, true, cap) ? original : current;

        public static float RestoreRenderScale(float original, float current, float cap = RenderScaleCap) =>
            current == PlanRenderScale(original, true, cap) ? original : current;
    }
}
