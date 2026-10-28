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

        public static float Sanitize(float value, float fallback) =>
            Scalar.IsFinite(value) ? Scalar.Clamp01(value) : fallback;

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
    }
}
