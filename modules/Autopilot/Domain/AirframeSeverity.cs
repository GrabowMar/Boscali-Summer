namespace BoscaliSummer.Features.Autopilot.Domain
{
    /// <summary>Read-only 0..1 damage from vanilla part HP. Never writes wear.</summary>
    internal static class AirframeSeverity
    {
        public static float FromParts(int count, float hitPoints, float startHitPoints, int detached)
        {
            if (count <= 0 || startHitPoints <= 0f || float.IsNaN(hitPoints) || float.IsNaN(startHitPoints))
                return 0f;
            float lost = hitPoints / startHitPoints;
            if (lost < 0f) lost = 0f;
            else if (lost > 1f) lost = 1f;
            lost = 1f - lost;
            float det = detached / (float)count;
            if (det < 0f) det = 0f;
            else if (det > 1f) det = 1f;
            float value = lost * 0.7f + det * 0.3f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }
}
