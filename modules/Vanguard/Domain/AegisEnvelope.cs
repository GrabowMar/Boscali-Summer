using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal static class AegisEnvelope
    {
        public const float DropClearSeconds = .12f;
        public const float SlewEnd = .40f;
        public const float ArmSeconds = .42f;
        public const float SensorConfirmSeconds = .15f;

        // Defence darts score on a near pass. In game the closest pass under native steering measured 12-23 m
        // (sim readout aegis.defencePassDm); a body-sized contact sphere never registered one.
        public const float ProximityRadius = 40f;

        // Relative swept contact catches real high-speed crossings between physics frames.
        public static bool Contact(Vector3 previous, Vector3 current, float radius)
        {
            Vector3 step=current-previous;
            float t=step.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(-Vector3.Dot(previous,step)/step.sqrMagnitude);
            return (previous+step*t).sqrMagnitude <= radius*radius;
        }
    }
}
