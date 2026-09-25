using System;

namespace BoscaliSummer.Features.Visuals.Domain
{
    /// <summary>
    /// Pure curves behind the visual layer: negative-G redout, G-strain fringing, tree sway and
    /// grass wind. Nothing here touches Unity, so the tests can pin the shapes.
    /// </summary>
    public static class VisualsMath
    {
        public const float GForceRedoutThreshold = -1.5f;
        public const float GForceRedoutMax = -4.0f;
        public const float StrainOnsetG = 5.0f;
        public const float StrainMaxG = 9.0f;

        /// <summary>Redout strength 0..1 under negative G (push-overs, outside loops).</summary>
        public static float CalculateGForceRedout(float gForce)
        {
            if (gForce >= GForceRedoutThreshold) return 0f;
            float t = (GForceRedoutThreshold - gForce) / (GForceRedoutThreshold - GForceRedoutMax);
            return Clamp01(t);
        }

        /// <summary>
        /// Lens-fringe strength 0..1 under heavy positive G. The game's own G-LOC model already
        /// darkens and desaturates the view, so this only adds the edge smear on top.
        /// </summary>
        public static float CalculateGStrain(float gForce)
        {
            if (gForce <= StrainOnsetG) return 0f;
            return Clamp01((gForce - StrainOnsetG) / (StrainMaxG - StrainOnsetG));
        }

        /// <summary>
        /// Canopy-top sway amplitude in metres for a wind speed in m/s, before the player's
        /// strength dial. Calm air still breathes a little; a gale is capped so crowns do not
        /// detach from trunks.
        /// </summary>
        public static float TreeSwayAmplitude(float windSpeed)
        {
            float w = Math.Max(0f, windSpeed);
            return Math.Min(1.8f, 0.25f + w * 0.07f);
        }

        /// <summary>
        /// Object-space offset for one vertex of the shared tree-clump mesh. <paramref name="height"/>
        /// is the tallest vertex; anything at or below the ground plane (y &lt;= 0) stays put, and the
        /// bend grows with height squared so trunks pivot at the root. Phase comes from the vertex's
        /// horizontal position, so the separate crowns inside one clump move out of step.
        /// <paramref name="windX"/>/<paramref name="windZ"/> are a unit direction.
        /// </summary>
        public static (float dx, float dy, float dz) TreeSway(
            float time, float x, float y, float z, float height,
            float windX, float windZ, float amplitude)
        {
            if (y <= 0f || height <= 0.001f || amplitude <= 0f) return (0f, 0f, 0f);

            float h = Clamp01(y / height);
            float bend = h * h;

            // Phase: a travelling wave downwind plus a smooth spatial offset, so neighbouring
            // crowns drift out of step while every vertex of one crown moves together (a hash
            // with hard cell edges would tear crowns that straddle a cell boundary).
            float along = x * windX + z * windZ;
            float crown = 1.6f * (float)Math.Sin(x * 0.07f + 1.7f) + 1.6f * (float)Math.Sin(z * 0.08f + 0.4f);
            float phase = along * 0.09f + crown;

            // Slow gust envelope shared by the whole map, so the forest leans and relaxes together.
            float gust = 0.7f + 0.3f * (float)Math.Sin(time * 0.31f + 1.3f) * (float)Math.Sin(time * 0.13f);

            float sway = (float)Math.Sin(time * 1.15f - phase) * 0.6f
                         + (float)Math.Sin(time * 2.45f - phase * 1.7f) * 0.22f
                         + 0.35f; // standing lean downwind
            float main = amplitude * bend * sway * gust;
            float cross = amplitude * bend * 0.18f * (float)Math.Sin(time * 0.93f + crown);

            // Leaf flutter: small, fast, all axes, scales with height (not squared) so the
            // whole crown shimmers rather than only its tip.
            float flutterPhase = time * 6.3f + (x + y * 1.3f + z) * 2.1f;
            float flutter = amplitude * 0.06f * h * (float)Math.Sin(flutterPhase);

            float dx = windX * main - windZ * cross + flutter;
            float dz = windZ * main + windX * cross + flutter * 0.7f;
            // Keep branch length roughly constant: a bent tip also drops a little.
            float dy = -(dx * dx + dz * dz) / (2f * Math.Max(y, 1f)) + flutter * 0.4f;
            return (dx, dy, dz);
        }

        /// <summary>Vanilla grass wind strength scaled by map wind (m/s) and the player's dial.</summary>
        public static float GrassWindStrength(float vanillaStrength, float windSpeed, float dial)
        {
            float windFactor = Math.Max(0.6f, Math.Min(2.5f, 0.6f + Math.Max(0f, windSpeed) * 0.12f));
            return vanillaStrength * windFactor * Math.Max(0f, dial);
        }

        /// <summary>Vanilla grass wind speed, a little faster in strong wind.</summary>
        public static float GrassWindSpeed(float vanillaSpeed, float windSpeed)
        {
            return vanillaSpeed * Math.Max(0.8f, Math.Min(1.8f, 0.8f + Math.Max(0f, windSpeed) * 0.05f));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
