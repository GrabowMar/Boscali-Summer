namespace BoscaliSummer.Core.Math
{
    /// <summary>What the air does to the sun's disc: dimmer and redder through more air, gone below the horizon.</summary>
    internal static class SunDiscMath
    {
        public const float AngularRadiusDeg = 0.27f;

        /// <summary>
        /// Relative air mass on the line to the sun (Kasten and Young 1989: 1 overhead, about 38 at the horizon),
        /// thinned with the observer's altitude on an 8.4 km scale height.
        /// </summary>
        public static float AirMass(float elevationDeg, float altitudeM)
        {
            if (!Scalar.IsFinite(elevationDeg) || !Scalar.IsFinite(altitudeM)) return 1f;
            double h = Scalar.Clamp(elevationDeg, 0f, 90f);
            double mass = 1.0 / (System.Math.Sin(h * Scalar.Deg2Rad) + 0.50572 * System.Math.Pow(h + 6.07995, -1.6364));
            return (float)(mass * System.Math.Exp(-System.Math.Max(0.0, altitudeM) / 8400.0));
        }

        /// <summary>Share of the sun's light that reaches the eye, per colour: blue is scattered away first.</summary>
        public static void Transmission(float elevationDeg, float altitudeM, out float r, out float g, out float b)
        {
            float mass = AirMass(elevationDeg, altitudeM);
            // Clear-air optical depths overhead at sea level (Rayleigh, ozone and a light haze).
            r = (float)System.Math.Exp(-0.09 * mass);
            g = (float)System.Math.Exp(-0.15 * mass);
            b = (float)System.Math.Exp(-0.28 * mass);
        }

        /// <summary>Perceived brightness (0-1) of the disc through the air.</summary>
        public static float Brightness(float elevationDeg, float altitudeM)
        {
            Transmission(elevationDeg, altitudeM, out float r, out float g, out float b);
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// <summary>
        /// 1 with the whole disc above the horizon, 0 once it has set. The game world is flat, so the sea horizon
        /// sits at 0 degrees from any altitude.
        /// </summary>
        // ponytail: the whole disc fades together instead of sinking limb first; a disc shader could clip it per pixel.
        public static float AboveHorizon(float elevationDeg) =>
            Scalar.IsFinite(elevationDeg) ? Scalar.SmoothStep(-AngularRadiusDeg, AngularRadiusDeg, elevationDeg) : 0f;

        /// <summary>
        /// How much a screen-space sun effect shows for a sun at this viewport position (0-1 is on screen):
        /// 1 on screen, fading to 0 half a screen past any edge.
        /// </summary>
        public static float ScreenFade(float u, float v)
        {
            if (!Scalar.IsFinite(u) || !Scalar.IsFinite(v)) return 0f;
            float outside = System.Math.Max(System.Math.Abs(u - 0.5f), System.Math.Abs(v - 0.5f)) - 0.5f;
            return 1f - Scalar.SmoothStep(0f, 0.5f, outside);
        }
    }
}
