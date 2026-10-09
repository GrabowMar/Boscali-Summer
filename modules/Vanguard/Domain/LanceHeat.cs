using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum HeatBand : byte { Cool, Warm, Hot, Derated, Fault }

    /// <summary>
    /// LANCE rail heat. Every shot cooks the rails; about three full-power shots later the barrel derates the
    /// bank, and past 1.10 the next shot is a dud and the pod locks out. Cooling is faster at speed.
    /// </summary>
    internal static class LanceHeat
    {
        public const float WarmAt = 0.30f, HotAt = 0.60f, DerateStart = 0.75f, DerateEnd = 1.10f;
        public const float DerateFloor = 0.35f, FaultPower = 0.15f, FaultLockout = 3.0f;

        public static float ShotHeat(float effectivePower01) => 0.06f + 0.49f * effectivePower01 * effectivePower01;

        /// <summary>Heat shed per second at this true airspeed (m/s).</summary>
        public static float Cool(float tasMps) => 0.018f + 0.022f * Mathf.Clamp01(tasMps / 300f);

        public static float Step(float heat, float tasMps, float dt) => Mathf.Max(0f, heat - Cool(tasMps) * dt);

        /// <summary>Power multiplier from the heat the rails carry BEFORE the shot.</summary>
        public static float Derate(float heatBeforeShot)
        {
            if (heatBeforeShot < DerateStart) return 1f;
            if (heatBeforeShot >= DerateEnd) return FaultPower;
            return 1f - (1f - DerateFloor) * (heatBeforeShot - DerateStart) / (DerateEnd - DerateStart);
        }

        public static bool IsFault(float heat) => heat >= DerateEnd;

        public static HeatBand BandOf(float heat) =>
            heat < WarmAt ? HeatBand.Cool : heat < HotAt ? HeatBand.Warm : heat < DerateStart ? HeatBand.Hot : heat < DerateEnd ? HeatBand.Derated : HeatBand.Fault;

        /// <summary>Heat before each of <paramref name="shots"/> shots fired at a fixed interval, derating each one.</summary>
        public static float[] Simulate(int shots, float requested01, float intervalSeconds, float tasMps)
        {
            var before = new float[shots];
            float heat = 0f;
            for (int i = 0; i < shots; i++)
            {
                before[i] = heat;
                heat += ShotHeat(requested01 * Derate(heat));
                heat = Step(heat, tasMps, intervalSeconds);
            }
            return before;
        }
    }
}
