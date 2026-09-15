namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Acceptance rule for turning a ground raycast hit into a fire anchor. Unity-free so
    /// tests can compile it: <c>ImpactFireManager</c> owns the probe, this owns the question
    /// of whether the hit may hold a flame.
    /// </summary>
    internal static class FireGroundSnapPolicy
    {
        /// <summary>Land hit below sea level plus this margin is water, not ground.</summary>
        internal const float SeaMargin = 1f;

        /// <summary>
        /// The hit must sit within <paramref name="maxDrop"/> of the reported point. A
        /// projectile that lands on ground, canopy or a vehicle is close to the surface; an
        /// air-to-air interception or proximity air burst hundreds of metres up is not and
        /// must fail closed instead of hanging a flame column in the sky.
        /// </summary>
        internal static bool CanAnchor(float hitY, float requestY, float seaLevel, float maxDrop)
        {
            if (hitY <= seaLevel + SeaMargin) return false;
            float delta = hitY - requestY;
            return delta <= maxDrop && delta >= -maxDrop;
        }
    }
}
