using System;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Presentation envelopes, not a damage model. Source and eye adaptation share the two pulses.
    /// Pulse timing follows Glasstone and Dolan's thermal scaling for 74 Mt: minimum near 0.0025*sqrt(kt) = 0.7 s,
    /// second maximum near 0.0417*kt^0.44 = 5.8 s, most of the heat within a minute.</summary>
    internal static class Sa74Flash
    {
        public static float Pulse(float age)
        {
            if (age < 0f) return 0f;
            float prompt = (float)Math.Exp(-age / .06f);
            float thermal = .86f * (.04f + .96f * Smooth(.45f, 5.8f, age)) * (float)Math.Exp(-Math.Max(0f, age - 5.8f) / 12f);
            return Math.Max(prompt, thermal);
        }

        // The cloud first sheds fallout (ash, radiation), and only as it spreads into the sky does the cold set in.
        public static float Fallout(float age) => Smooth(90f, 420f, age);
        public static float Winter(float age) => Smooth(600f, 2100f, age);
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
        public static float GlareGain(float age) => age < .45f ? 18f :
            10f * (float)Math.Exp(-Math.Max(0f, age - 5.8f) / 4f);
        private static float Smooth(float from, float to, float value)
        {
            float t = Math.Max(0f, Math.Min(1f, (value - from) / (to - from)));
            return t * t * (3f - 2f * t);
        }
    }
}
