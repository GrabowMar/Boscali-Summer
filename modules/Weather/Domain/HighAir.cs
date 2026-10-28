namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The air seen from very high up (2026-10-28). Vanilla fogs by distance with a floor of 3e-5 per metre that it reaches near 16 km, so from 50 km
    /// everything below sat behind 80-90 % fog: a flat cyan sea under a black sky. Above <see cref="ThinFrom"/> the fog thins as (12 km / height)^1.5, so a
    /// ray down to the sea crosses a column of air instead of a haze that grows with the height: fog density x height, the column's optical depth, eases from
    /// vanilla's 0.36 at 12 km to about 0.18 at 50 km (clear air is 0.15-0.25). The cloud composite paints the curved Earth and its limb past the far plane
    /// with that same column, so the two meet at the far plane.
    /// </summary>
    internal static class HighAir
    {
        internal const float ThinFrom = 12000f;
        internal const float EarthRadius = 6371000f;

        /// <summary>The factor on the fog density at a camera height (m above the sea): 1 up to 12 km, then (12 km / height)^1.5.</summary>
        internal static float FogScale(float height) => height <= ThinFrom ? 1f : (float)System.Math.Pow(ThinFrom / height, 1.5);

        /// <summary>0..1, how much of the curved Earth, its limb and the cloud horizon cut show: none below 12 km, all from 30 km.</summary>
        internal static float EarthFade(float height) => WeatherMath.Smoothstep(ThinFrom, 30000f, height);
    }
}
