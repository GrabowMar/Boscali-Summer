using System;

namespace BoscaliSummer.Features.QoL.Runtime
{
    internal static class GunAimAssistPolicy
    {
        public const float ConeDegrees = 2.5f;

        public static float Correction(float error, float totalError, float input, float angularRate, float strength)
        {
            if (!Finite(error) || !Finite(totalError) || !Finite(input) || !Finite(angularRate) ||
                !Finite(strength) || totalError < 0f || totalError >= ConeDegrees ||
                Math.Abs(input) >= 0.35f || input * error < -0.005f) return 0f;
            float edge = 1f - totalError / ConeDegrees;
            float limit = Math.Max(0f, Math.Min(strength, 0.08f));
            float correction = Math.Max(-limit, Math.Min(limit, error * 0.04f - angularRate * 0.025f));
            return correction * edge * edge * (1f - Math.Abs(input) / 0.35f);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
