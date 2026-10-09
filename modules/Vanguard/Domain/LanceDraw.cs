using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// LANCE power draw: the bank feeds off the engines while it charges (afterburner drops out, dry thrust sags up to
    /// 30 %) and the recharge leaves a decaying tail. Slow flight, hover and the ground are exempt so a charging pod can
    /// never stall a carrier.
    /// </summary>
    internal static class LanceDraw
    {
        public const float MaxSag = 0.30f, ChargeOnset = 0.20f, TailDraw = 0.45f, AiScale = 0.5f;
        public const float SpeedLo = 70f, SpeedHi = 130f, LowAgl = 80f;

        /// <summary>0..1 draw while charging; nothing below 20 % charge.</summary>
        public static float ChargeDraw(float charge01) => LanceCapacitor.Smooth(ChargeOnset, 1f, charge01);

        /// <summary>Draw tail after a shot: elapsed01 runs 0..1 over the recharge.</summary>
        public static float RechargeDraw(float lastPower01, float elapsed01) => TailDraw * lastPower01 * (1f - Mathf.Clamp01(elapsed01));

        public static float EnergyGate(float speedMps, float aglMeters) =>
            Mathf.Clamp01((speedMps - SpeedLo) / (SpeedHi - SpeedLo)) * (aglMeters < LowAgl ? 0.5f : 1f);

        /// <summary>Fraction of DRY thrust removed.</summary>
        public static float Sag(float draw01, float gate01, bool ai) => MaxSag * draw01 * gate01 * (ai ? AiScale : 1f);

        public static float JetLossNewtons(float dryThrustN, float sag) => sag * dryThrustN;

        /// <summary>Turboprop throttle multiplier.</summary>
        public static float TurbineThrottleFactor(float sag) => 1f - sag;

        /// <summary>HUD/jam disruption level while charging: starts at 35 % charge.</summary>
        public static float FlickerLevel(float charge01) => LanceCapacitor.Smooth(0.35f, 1f, charge01);
    }
}
