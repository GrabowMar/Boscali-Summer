using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Presentation envelopes, not a physical radiation/damage model. Source and eye adaptation share the two pulses.</summary>
    internal static class Sa74Flash
    {
        public static float Pulse(float age)
        {
            if (age < 0f) return 0f;
            float prompt = (float)Math.Exp(-age / .06f);
            float thermal = .86f * Smooth(.22f, 1.1f, age) * (float)Math.Exp(-Math.Max(0f, age - 1.1f) / 8.5f);
            return Math.Max(prompt, thermal);
        }

        public static float Winter(float age) => Smooth(20f, 95f, age);
        public static float Range(float distance)
        {
            float radius = Math.Max(0f, distance) / 32000f;
            return 1f / (1f + radius * radius);
        }
        public static float Facing(float dot)
        {
            float facing = Math.Max(0f, Math.Min(1f, (dot + .05f) / 1.05f));
            return .12f + .88f * facing * facing;
        }
        public static float GlareGain(float age) => age < .22f ? 18f :
            10f * (float)Math.Exp(-Math.Max(0f, age - 1.1f) / 2.6f);
        private static float Smooth(float from, float to, float value)
        {
            float t = Math.Max(0f, Math.Min(1f, (value - from) / (to - from)));
            return t * t * (3f - 2f * t);
        }
    }
}
