namespace BoscaliSummer.Fire
{
    internal enum BuildingDamageStage
    {
        Intact = 0,
        Minor = 1,
        Major = 2,
        Critical = 3
    }

    internal static class BuildingDamagePolicy
    {
        internal const float MinimumEstimatedHitPoints = 100f;

        internal static BuildingDamageStage FromHitPoints(float hitPoints, float observedPeakHitPoints)
        {
            if (hitPoints <= 0f) return BuildingDamageStage.Intact;
            float baseline = observedPeakHitPoints > MinimumEstimatedHitPoints
                ? observedPeakHitPoints
                : MinimumEstimatedHitPoints;
            float remaining = hitPoints / baseline;
            if (remaining <= 0.15f) return BuildingDamageStage.Critical;
            if (remaining <= 0.40f) return BuildingDamageStage.Major;
            if (remaining <= 0.70f) return BuildingDamageStage.Minor;
            return BuildingDamageStage.Intact;
        }

        internal static BuildingDamageStage FromFireProgress(float progress)
        {
            if (progress >= 0.66f) return BuildingDamageStage.Critical;
            if (progress >= 0.33f) return BuildingDamageStage.Major;
            return BuildingDamageStage.Minor;
        }

        internal static BuildingDamageStage FromSeverity(float severity)
        {
            if (severity >= 0.78f) return BuildingDamageStage.Critical;
            if (severity >= 0.58f) return BuildingDamageStage.Major;
            if (severity > 0f) return BuildingDamageStage.Minor;
            return BuildingDamageStage.Intact;
        }

        internal static float Severity(BuildingDamageStage stage)
        {
            switch (stage)
            {
                case BuildingDamageStage.Critical: return 0.92f;
                case BuildingDamageStage.Major: return 0.70f;
                case BuildingDamageStage.Minor: return 0.46f;
                default: return 0f;
            }
        }
    }
}
