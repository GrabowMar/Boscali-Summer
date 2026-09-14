using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal static class SupportEffectPolicy
    {
        public const float RodCoreRadius = 150f;
        public const float RodBlastRadius = 420f;
        public const float EmpDelay = 3f;
        public const float EmpDuration = 30f;
        public const float EmpBurstAltitude = 30000f;

        public static float RodDamage(float distance)
        {
            if (float.IsNaN(distance) || distance < 0f || distance >= RodBlastRadius) return 0f;
            if (distance <= RodCoreRadius) return 12000f;
            float falloff = (RodBlastRadius - distance) / (RodBlastRadius - RodCoreRadius);
            return 12000f * falloff * falloff * falloff;
        }

        public static string EmpName(string name, float radius) =>
            name + ":r=" + radius.ToString("R", CultureInfo.InvariantCulture);

        public static float EmpRadius(string name)
        {
            int index = name == null ? -1 : name.LastIndexOf(":r=", StringComparison.Ordinal);
            return index >= 0 && float.TryParse(name.Substring(index + 3), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value) && !float.IsNaN(value) &&
                value >= 500f && value <= 60000f ? value : 12000f;
        }
    }
}
