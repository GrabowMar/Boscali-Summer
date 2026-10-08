using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// RG-12 LANCE capacitor: holding the trigger charges the bank, releasing fires one slug scaled by
    /// charge. Taps give weak snap shots, a full 2.5 s hold a 3.4 km/s killer; the bank then recharges,
    /// longer after a bigger shot. Pure math; LanceService owns the trigger state.
    /// </summary>
    internal static class LanceCapacitor
    {
        public const float FullChargeSeconds = 2.5f;
        /// <summary>Trigger-up detection: no Fire call for this long means the pilot let go.</summary>
        public const float ReleaseGap = 0.15f;

        /// <summary>0..1 charge after holding the trigger this long.</summary>
        public static float Charge(float holdSeconds) => Mathf.Clamp01(holdSeconds / FullChargeSeconds);

        /// <summary>0..1 shot power from charge, tapered at both ends.</summary>
        public static float Power01(float charge01)
        {
            float c = Mathf.Clamp01(charge01);
            return c * c * (3f - 2f * c);
        }

        public static float Velocity(float power01) => 1400f + 2000f * power01;   // 1400..3400 m/s
        public static float Pierce(float power01) => 1200f + 4800f * power01;     // 1200..6000
        public static float Blast(float power01) => 10f + 30f * power01;          // 10..40
        public static float Tracer(float power01) => .6f + .5f * power01;        // Thin slug streak, no oversized beam.

        /// <summary>Bank recharge after firing at this power, before the next charge can start.</summary>
        public static float RechargeSeconds(float power01) => 1f + 2.5f * power01;
    }
}
