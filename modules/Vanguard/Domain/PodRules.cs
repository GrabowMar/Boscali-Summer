using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// Numbers and decisions of the two hidden pylon pods: PCU-9 STORMCELL (capacitor) and ASQ-X WATCHTOWER (IRST, ESM, MAWS, relay).
    /// </summary>
    internal static class PodRules
    {
        /// <summary>STORMCELL: one pod adds as much capacity as the aircraft has, within [<see cref="MinExtra"/>, <see cref="MaxExtra"/>] (vanilla ECM pod: 100).</summary>
        public const float CapacityShare = 1f, MinExtra = 50f, MaxExtra = 300f;
        /// <summary>STORMCELL: charge per engine RPM scale while any pod is fitted (pods do not stack it).</summary>
        public const float ChargeRateScale = 1.5f;

        /// <summary>WATCHTOWER: IRST sees aircraft within <see cref="IrstRangeM"/> inside <see cref="IrstConeDeg"/> (full angle, forward).</summary>
        public const float IrstRangeM = 28000f, IrstConeDeg = 150f, IrstPeriodS = 1f;
        public const float EsmRangeM = 90000f, EsmPeriodS = 2f;
        /// <summary>WATCHTOWER: missile warning range (vanilla 5 km) and the drone link scale of its relay.</summary>
        public const float MawsRangeM = 12000f, RelayScale = 2.5f;

        public static float ExtraCapacity(float baseMax) => Mathf.Clamp(baseMax * CapacityShare, MinExtra, MaxExtra);

        public static bool InIrst(Vector3 forward, Vector3 toTarget, float rangeM) =>
            toTarget.sqrMagnitude <= rangeM * rangeM && Vector3.Angle(forward, toTarget) <= IrstConeDeg * 0.5f;

        /// <summary>Warning range with the pod: never below what the airframe already has.</summary>
        public static float MawsRange(float baseM) => Mathf.Max(baseM, MawsRangeM);

        public static float LinkScale(bool relay) => relay ? RelayScale : 1f;
    }
}
