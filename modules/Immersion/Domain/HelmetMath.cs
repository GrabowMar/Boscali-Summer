using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    /// <summary>
    /// Near-future helmet-mounted display (HMD) maths, after the F-35 HMDS class of visor-projected
    /// helmets: the symbology and the glass live on one spherical visor, not on a flat screen.
    /// Pure (no UnityEngine): the visor shader, the canopy reflection and the unit tests all read
    /// the same numbers. UV space is the overlay quad (0..1, v up); centred space is -1..1.
    /// </summary>
    internal static class HelmetMath
    {
        /// <summary>Default visor curvature (0 = legacy flat glass, 1 = strong spherical wrap). Pilot-tunable.</summary>
        public const float DefaultCurvature = .55f;
        /// <summary>Default visor/canopy reflection strength multiplier.</summary>
        public const float DefaultReflection = 1f;
        /// <summary>Coated-visor Fresnel F0 (dielectric glass with an anti-reflective / dichroic coating).</summary>
        public const float FresnelF0 = .04f;

        public static float Sanitize(float value, float fallback) =>
            Scalar.IsFinite(value) ? Scalar.Clamp01(value) : fallback;

        /// <summary>Radial distance in centred space: 0 at boresight, 1 at mid-edge, ~1.41 at a corner.</summary>
        public static float Radial(float u, float v)
        {
            float x = 2f * u - 1f, y = 2f * v - 1f;
            return (float)Math.Sqrt(x * x + y * y);
        }

        /// <summary>
        /// Spherical-visor warp: where a point painted for a flat screen is sampled on the curved
        /// visor. Identity at boresight for any curvature; the periphery is pulled outward, so
        /// straight symbology bows around the eye. Curvature 0 is exactly the legacy flat UV.
        /// </summary>
        public static void Warp(float u, float v, float curvature, out float warpedU, out float warpedV)
        {
            float k = Sanitize(curvature, DefaultCurvature) * .18f;
            float x = 2f * u - 1f, y = 2f * v - 1f;
            float r2 = x * x + y * y;
            float scale = 1f + k * r2;
            warpedU = x * scale * .5f + .5f;
            warpedV = y * scale * .5f + .5f;
        }

        /// <summary>Peripheral vignette of a real visor: 1 in the middle, falling to (1-strength) at the corner.</summary>
        public static float Vignette(float radial, float strength)
        {
            float s = Sanitize(strength, DefaultCurvature);
            float t = Scalar.SmoothStep(.55f, 1.45f, radial);
            return 1f - s * .38f * t * t;
        }

        /// <summary>Schlick Fresnel for the coated visor: near F0 head-on, rising to 1 at grazing angles.</summary>
        public static float Fresnel(float cosTheta)
        {
            float c = Scalar.Clamp01(cosTheta);
            float m = 1f - c;
            float m2 = m * m;
            return FresnelF0 + (1f - FresnelF0) * m2 * m2 * m;
        }

        /// <summary>Fresnel from radial distance alone (overlay proxy: the edge of the quad is the grazing view).</summary>
        public static float EdgeFresnel(float radial) =>
            Fresnel(1f - Scalar.SmoothStep(.35f, 1.42f, radial) * .92f);

        /// <summary>
        /// Electrochromic sun-visor drive: the legacy elevation curve, darkened further when the
        /// pilot actually looks toward the sun and eased back at night so the display stays readable.
        /// </summary>
        public static float ElectroTint(float sunElevationDeg, float sunFacing01, float ambient01)
        {
            float elevation = VisorMath.SunVisor(sunElevationDeg);
            float facing = Scalar.Clamp01(sunFacing01);
            float ambient = Scalar.Clamp01(ambient01);
            return Scalar.Clamp01(elevation * (.45f + .55f * facing) * (.35f + .65f * ambient));
        }

        /// <summary>
        /// Canopy-reflection opacity for the remade pilot reflection: Schlick Fresnel on the glass,
        /// a touch stronger in the dark (a canopy mirrors best at night), faded where the reflected
        /// pilot plane is seen edge-on. Mirrors PilotCanopyReflection's facing term.
        /// </summary>
        public static float ReflectionOpacity(float cosTheta, float ambient01, float facing01, float strength)
        {
            float fresnel = Fresnel(cosTheta);
            float darkBoost = .75f + (1f - Scalar.Clamp01(ambient01)) * .5f;
            return Scalar.Clamp01(fresnel * 3.2f * darkBoost * Scalar.Clamp01(facing01) *
                Sanitize(strength, DefaultReflection) * 1.4f);
        }
    }
}
