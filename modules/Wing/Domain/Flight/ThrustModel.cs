namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Approximate throttle/speed feed-forward using thrust proportional to throttle and drag
    /// proportional to speed squared. Anticipating leader lever changes reduces acceleration lag;
    /// closed-loop speed correction handles model error.</summary>
    internal static class ThrustModel
    {

        private static float Clamp01(float value)
        {
            if (float.IsNaN(value)) return 0f;
            return value < 0f ? 0f : (value > 1f ? 1f : value);
        }
    }
}
