using System;

namespace BoscaliSummer.Features.Autopilot.Domain
{
    internal enum CarrierLeg
    {
        Upwind = 0,
        Crosswind = 1,
        Downwind = 2,
        Base = 3,
        Final = 4
    }

    /// <summary>
    /// Rectangular carrier traffic: four gates before the existing winged final.
    /// Distances are metres in runway-right coordinates. Pure.
    /// </summary>
    internal static class CarrierPattern
    {
        public const float GateRadius = 250f;
        public const float LegTimeout = 45f;
        public const float HeadingAlign = 40f;
        public const float DirectFinalRange = 2500f;
        public const float DirectFinalHeading = 25f;

        public static string Word(CarrierLeg leg)
        {
            switch (leg)
            {
                case CarrierLeg.Upwind: return "UPWIND";
                case CarrierLeg.Crosswind: return "CROSSWIND";
                case CarrierLeg.Downwind: return "DOWNWIND";
                case CarrierLeg.Base: return "BASE";
                default: return "FINAL";
            }
        }

        public static CarrierLeg Next(CarrierLeg leg) =>
            leg >= CarrierLeg.Final ? CarrierLeg.Final : (CarrierLeg)((int)leg + 1);

        public static float PatternSide(float rightOffset) => rightOffset >= 0f ? 1f : -1f;

        public static bool DirectFinal(float distanceToTouchdown, float headingErrorDeg) =>
            distanceToTouchdown < DirectFinalRange && headingErrorDeg < DirectFinalHeading;

        public static bool ShouldAdvance(float distanceToGate, float headingErrorDeg, float timeOnLeg) =>
            timeOnLeg >= LegTimeout ||
            (distanceToGate < GateRadius && headingErrorDeg < HeadingAlign);

        /// <summary>
        /// Gate in world metres relative to touchdown. <paramref name="landingDx"/> / Dz is the
        /// landing-direction unit; <paramref name="side"/> is +1 right-hand / −1 left-hand.
        /// Y is AGL above the deck.
        /// </summary>
        public static void Gate(CarrierLeg leg, float landingDx, float landingDz, float side,
            out float x, out float y, out float z)
        {
            float len = (float)Math.Sqrt(landingDx * landingDx + landingDz * landingDz);
            if (len < 0.001f) { x = 0f; y = 80f; z = 0f; return; }
            float dx = landingDx / len;
            float dz = landingDz / len;
            float rx = dz * side;
            float rz = -dx * side;
            switch (leg)
            {
                case CarrierLeg.Upwind:
                    x = dx * 800f; y = 300f; z = dz * 800f; return;
                case CarrierLeg.Crosswind:
                    x = dx * 800f + rx * 1200f; y = 300f; z = dz * 800f + rz * 1200f; return;
                case CarrierLeg.Downwind:
                    x = dx * -1500f + rx * 1200f; y = 300f; z = dz * -1500f + rz * 1200f; return;
                case CarrierLeg.Base:
                    x = dx * -1500f + rx * 400f; y = 200f; z = dz * -1500f + rz * 400f; return;
                default:
                    x = 0f; y = 80f; z = 0f; return;
            }
        }
    }
}
