using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Runtime
{
    internal static class SupportEffectPolicy
    {
        public const float RodCoreRadius = 150f;
        public const float RodBlastRadius = 420f;
        public const float EmpDelay = 3f;
        public const float EmpDuration = 30f;
        public const float EmpBurstAltitude = 30000f;
        public const float MaxEmpRadius = 90000f;

        /// <summary>
        /// A rod's blast damage at <paramref name="distance"/>: the core and the reach shrink with <paramref name="radiusScale"/> (STARFALL's rods are about 35 %), the damage itself scales with
        /// <paramref name="damageScale"/> (the front's POWER on an ORBITAL ROD).
        /// </summary>
        public static float RodDamage(float distance, float damageScale, float radiusScale)
        {
            float reach = RodBlastRadius * radiusScale, core = RodCoreRadius * radiusScale;
            if (float.IsNaN(distance) || distance < 0f || distance >= reach) return 0f;
            if (distance <= core) return 12000f * damageScale;
            float falloff = (reach - distance) / (reach - core);
            return 12000f * damageScale * falloff * falloff * falloff;
        }

        /// <summary>
        /// A rod's damage and blast-radius scales ride in the delivery missile's replicated name (<c>:k=damage%,radius%</c>), like the EMP radius, so the host's blast and every peer's picture agree.
        /// The radius scale never goes above 1: the friendly stand-off is judged against the full 420 m blast.
        /// </summary>
        public static string RodName(string name, float damageScale, float radiusScale) =>
            name + ":k=" + Math.Round(damageScale * 100f).ToString(CultureInfo.InvariantCulture) + "," + Math.Round(radiusScale * 100f).ToString(CultureInfo.InvariantCulture);

        /// <summary>Reads <see cref="RodName"/>: damage 30 .. 150 %, radius 30 .. 100 %; a name without it (or an unreadable one) is a plain rod, 1 and 1.</summary>
        public static void RodScales(string name, out float damage, out float radius)
        {
            damage = 1f; radius = 1f;
            int index = name == null ? -1 : name.LastIndexOf(":k=", StringComparison.Ordinal);
            if (index < 0) return;
            string[] parts = name.Substring(index + 3).Split(',');
            if (parts.Length != 2) return;
            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) && d >= 30 && d <= 150) damage = d / 100f;
            if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) && r >= 30 && r <= 100) radius = r / 100f;
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
