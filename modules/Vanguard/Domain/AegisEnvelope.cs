using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal static class AegisEnvelope
    {
        public const float DropClearSeconds = .12f;
        public const float MotorDelay = .34f;
        public const float SlewEnd = .40f;
        public const float ArmSeconds = .42f;
        public const float SensorConfirmSeconds = .15f;

        public static float ContactRadius(float width, float height) =>
            Mathf.Clamp(.07f + .5f * Mathf.Max(width,height), .15f, .75f);

        // Relative swept contact catches real high-speed crossings between physics frames.
        public static bool Contact(Vector3 previous, Vector3 current, float radius)
        {
            Vector3 step=current-previous;
            float t=step.sqrMagnitude < 1e-8f ? 0f : Mathf.Clamp01(-Vector3.Dot(previous,step)/step.sqrMagnitude);
            return (previous+step*t).sqrMagnitude <= radius*radius;
        }
    }
}
