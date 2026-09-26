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
        public const float MaxEmpRadius = 90000f;

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
                value >= 500f && value <= MaxEmpRadius ? value : 12000f;
        }

        /// <summary>
        /// The flare barrage's radius, duration and flare count ride in the delivery missile's
        /// replicated name, like the EMP radius, so every peer seduces with the host's values.
        /// </summary>
        public static string FlareName(string name, float radius, float duration, int count) =>
            name + ":f=" + radius.ToString("R", CultureInfo.InvariantCulture) + "," +
            duration.ToString("R", CultureInfo.InvariantCulture) + "," + count.ToString(CultureInfo.InvariantCulture);

        /// <summary>Reads <see cref="FlareName"/>; each value outside its setting's range keeps its default.</summary>
        public static void FlareBarrage(string name, out float radius, out float duration, out int count)
        {
            radius = 4000f;
            duration = 15f;
            count = 36;
            int index = name == null ? -1 : name.LastIndexOf(":f=", StringComparison.Ordinal);
            if (index < 0) return;
            string[] parts = name.Substring(index + 3).Split(',');
            if (parts.Length != 3) return;
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                r >= 500f && r <= 15000f) radius = r;
            if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float d) &&
                d >= 5f && d <= 45f) duration = d;
            if (int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                n >= 12 && n <= 64) count = n;
        }
    }
}
