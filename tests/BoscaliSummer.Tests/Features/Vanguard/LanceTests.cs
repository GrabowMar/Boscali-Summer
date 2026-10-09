using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the RG-12 LANCE OVERCHARGE numbers: capacitor, rail heat, engine draw, through-shot, pose.</summary>
    internal static class LanceTests
    {
        private static void Near(float actual, float expected, float tolerance, string what) =>
            TestAssert.That(Mathf.Abs(actual - expected) <= tolerance, what + " (got " + actual + ", want " + expected + ")");

        public static void Run()
        {
            // ---- capacitor
            Near(LanceCapacitor.Charge(0f), 0f, 1e-6f, "charge 0");
            Near(LanceCapacitor.Charge(1.25f), 0.5f, 1e-6f, "charge half at 1.25 s");
            Near(LanceCapacitor.Charge(2.5f), 1f, 1e-6f, "charge full at 2.5 s");
            Near(LanceCapacitor.Charge(9f), 1f, 1e-6f, "charge clamps");
            Near(LanceCapacitor.Power01(0f), 0f, 1e-6f, "power 0");
            Near(LanceCapacitor.Power01(0.25f), 0.15625f, 1e-5f, "power at 25 %");
            Near(LanceCapacitor.Power01(0.4f), 0.352f, 1e-5f, "power at 40 %");
            Near(LanceCapacitor.Power01(0.5f), 0.5f, 1e-6f, "power at 50 %");
            Near(LanceCapacitor.Power01(0.7f), 0.784f, 1e-5f, "power at 70 %");
            Near(LanceCapacitor.Power01(0.85f), 0.93925f, 1e-5f, "power at the AI release point");
            Near(LanceCapacitor.Power01(1f), 1f, 1e-6f, "power 1");
            TestAssert.That(LanceCapacitor.TierOf(0.24f) == LanceTier.Snap && LanceCapacitor.TierOf(0.25f) == LanceTier.Punch &&
                LanceCapacitor.TierOf(0.69f) == LanceTier.Punch && LanceCapacitor.TierOf(0.70f) == LanceTier.Through &&
                LanceCapacitor.TierOf(0.994f) == LanceTier.Through && LanceCapacitor.TierOf(0.995f) == LanceTier.Full,
                "tier boundaries are 25 / 70 / 99.5 %");
            Near(LanceCapacitor.Pierce(0f), 180f, 1e-3f, "pierce 0");
            Near(LanceCapacitor.Pierce(0.5f), 1593.5f, 1e-2f, "pierce half");
            TestAssert.That(LanceCapacitor.Pierce(1f) == LanceCapacitor.AssetPierce, "full-power pierce equals the asset value");
            Near(LanceCapacitor.Blast(0f), 0.05f, 1e-6f, "blast 0");
            Near(LanceCapacitor.Blast(0.5f), 0.1875f, 1e-6f, "blast half");
            Near(LanceCapacitor.Blast(1f), LanceCapacitor.AssetBlast, 1e-6f, "full-power blast equals the asset value");
            Near(LanceCapacitor.Tracer(0f), 0.6f, 1e-6f, "tracer 0");
            Near(LanceCapacitor.Tracer(1f), 1.1f, 1e-6f, "tracer 1");
            Near(LanceCapacitor.RechargeSeconds(0f), 1f, 1e-6f, "recharge 0");
            Near(LanceCapacitor.RechargeSeconds(0.5f), 2.25f, 1e-6f, "recharge half");
            Near(LanceCapacitor.RechargeSeconds(1f), 3.5f, 1e-6f, "recharge full");
            Near(LanceCapacitor.ServerChargeBound(0f), 0.14f, 1e-5f, "server bound at ready");
            Near(LanceCapacitor.ServerChargeBound(1f), 0.54f, 1e-5f, "server bound 1 s after ready");
            Near(LanceCapacitor.ServerChargeBound(2.15f), 1f, 1e-5f, "server bound reaches full");
            LanceCapacitor.Kick(0f, out float dv0, out float w0);
            LanceCapacitor.Kick(0.5f, out float dv5, out float w5);
            LanceCapacitor.Kick(1f, out float dv1, out float w1);
            TestAssert.That(Mathf.Abs(dv0 - 0.10f) < 1e-5f && Mathf.Abs(w0 - 0.04f) < 1e-5f &&
                Mathf.Abs(dv5 - 0.375f) < 1e-5f && Mathf.Abs(w5 - 0.15f) < 1e-5f &&
                Mathf.Abs(dv1 - 0.65f) < 1e-5f && Mathf.Abs(w1 - 0.26f) < 1e-5f, "kick scales with power");
            LanceCapacitor.Shake(0f, out float lo0, out float hi0);
            LanceCapacitor.Shake(1f, out float lo1, out float hi1);
            TestAssert.That(Mathf.Abs(lo0 - 0.25f) < 1e-5f && Mathf.Abs(hi0 - 0.40f) < 1e-5f &&
                Mathf.Abs(lo1 - 1.15f) < 1e-5f && Mathf.Abs(hi1 - 1.80f) < 1e-5f, "camera shake scales with power");
            TestAssert.That(LanceCapacitor.IsSignature(3007f, 3000f, 0.6f) && !LanceCapacitor.IsSignature(3000f, 3000f, 0.6f) &&
                !LanceCapacitor.IsSignature(3007f, 820f, 0.6f) && !LanceCapacitor.IsSignature(3007f, 3000f, 1f),
                "the ArmorPenetrate signature is exactly the Lance asset triple");

            // ---- rail heat
            Near(LanceHeat.ShotHeat(0f), 0.06f, 1e-6f, "shot heat 0");
            Near(LanceHeat.ShotHeat(0.5f), 0.1825f, 1e-5f, "shot heat half");
            Near(LanceHeat.ShotHeat(1f), 0.55f, 1e-6f, "shot heat full");
            Near(LanceHeat.Cool(0f), 0.018f, 1e-6f, "cooling at rest");
            Near(LanceHeat.Cool(150f), 0.029f, 1e-5f, "cooling at 150 m/s");
            Near(LanceHeat.Cool(300f), 0.040f, 1e-6f, "cooling at 300 m/s");
            Near(LanceHeat.Cool(900f), 0.040f, 1e-6f, "cooling saturates");
            Near(LanceHeat.Step(1.0f, 300f, 10f), 0.6f, 1e-5f, "heat steps down");
            Near(LanceHeat.Step(0.2f, 300f, 10f), 0f, 1e-6f, "heat never goes negative");
            Near(LanceHeat.Derate(0.74f), 1f, 1e-6f, "no derate below 0.75");
            Near(LanceHeat.Derate(0.75f), 1f, 1e-6f, "derate starts at 0.75");
            Near(LanceHeat.Derate(0.9f), 0.7214f, 1e-3f, "derate at 0.9");
            Near(LanceHeat.Derate(0.925f), 0.675f, 1e-3f, "derate at 0.925");
            Near(LanceHeat.Derate(1.0f), 0.5357f, 1e-3f, "derate at 1.0");
            Near(LanceHeat.Derate(1.099f), 0.3518f, 1e-3f, "derate floor");
            Near(LanceHeat.Derate(1.10f), 0.15f, 1e-6f, "fault power at 1.10");
            Near(LanceHeat.Derate(1.5f), 0.15f, 1e-6f, "fault power above");
            TestAssert.That(!LanceHeat.IsFault(1.099f) && LanceHeat.IsFault(1.10f), "fault at 1.10");
            TestAssert.That(LanceHeat.BandOf(0.29f) == HeatBand.Cool && LanceHeat.BandOf(0.30f) == HeatBand.Warm &&
                LanceHeat.BandOf(0.60f) == HeatBand.Hot && LanceHeat.BandOf(0.75f) == HeatBand.Derated &&
                LanceHeat.BandOf(1.10f) == HeatBand.Fault, "heat band boundaries");
            float[] volley = LanceHeat.Simulate(5, 1f, 6f, 250f);
            Near(volley[0], 0f, 0.01f, "volley shot 1");
            Near(volley[1], 0.332f, 0.01f, "volley shot 2");
            Near(volley[2], 0.664f, 0.01f, "volley shot 3");
            Near(volley[3], 0.996f, 0.01f, "volley shot 4 (rails bite)");
            Near(volley[4], 0.982f, 0.01f, "volley shot 5");
            TestAssert.That(LanceHeat.Derate(volley[2]) == 1f && LanceHeat.Derate(volley[3]) < 0.6f, "three full shots, then the fourth derates");

            // ---- engine draw
            Near(LanceDraw.ChargeDraw(0.1f), 0f, 1e-6f, "no draw at 10 %");
            Near(LanceDraw.ChargeDraw(0.2f), 0f, 1e-6f, "draw starts at 20 %");
            Near(LanceDraw.ChargeDraw(0.6f), 0.5f, 1e-5f, "draw at 60 %");
            Near(LanceDraw.ChargeDraw(1f), 1f, 1e-6f, "full draw");
            Near(LanceDraw.RechargeDraw(1f, 0f), 0.45f, 1e-6f, "recharge tail starts at 0.45");
            Near(LanceDraw.RechargeDraw(1f, 0.5f), 0.225f, 1e-6f, "recharge tail halves");
            Near(LanceDraw.RechargeDraw(1f, 1f), 0f, 1e-6f, "recharge tail ends");
            Near(LanceDraw.RechargeDraw(0.5f, 0f), 0.225f, 1e-6f, "recharge tail scales with last power");
            Near(LanceDraw.EnergyGate(60f, 500f), 0f, 1e-6f, "no sag in slow flight");
            Near(LanceDraw.EnergyGate(100f, 500f), 0.5f, 1e-5f, "gate ramps 70..130 m/s");
            Near(LanceDraw.EnergyGate(130f, 500f), 1f, 1e-6f, "gate full at 130 m/s");
            Near(LanceDraw.EnergyGate(200f, 50f), 0.5f, 1e-6f, "low altitude halves the gate");
            Near(LanceDraw.EnergyGate(200f, 500f), 1f, 1e-6f, "cruise gate");
            Near(LanceDraw.Sag(1f, 1f, false), 0.30f, 1e-6f, "max sag 30 %");
            Near(LanceDraw.Sag(1f, 1f, true), 0.15f, 1e-6f, "AI sags half as much");
            Near(LanceDraw.Sag(0.5f, 1f, false), 0.15f, 1e-6f, "sag scales with draw");
            Near(LanceDraw.Sag(1f, 0.5f, false), 0.15f, 1e-6f, "sag scales with gate");
            Near(LanceDraw.JetLossNewtons(40000f, 0.3f), 12000f, 1e-2f, "jet thrust loss");
            Near(LanceDraw.TurbineThrottleFactor(0.3f), 0.7f, 1e-6f, "turbine throttle factor");
            Near(LanceDraw.FlickerLevel(0.35f), 0f, 1e-6f, "flicker starts at 35 %");
            Near(LanceDraw.FlickerLevel(0.675f), 0.5f, 1e-5f, "flicker half");
            Near(LanceDraw.FlickerLevel(1f), 1f, 1e-6f, "flicker full");

            // ---- through-shot
            TestAssert.That(LanceSlug.ThroughEnabled(0.78f) && !LanceSlug.ThroughEnabled(0.779f), "through-shot from 78 % power");
            Near(LanceSlug.ThroughReach(0f), 14f, 1e-4f, "snap reach");
            Near(LanceSlug.ThroughReach(0.78f), 14f, 1e-4f, "reach at the threshold");
            Near(LanceSlug.ThroughReach(0.89f), 617.5f, 0.5f, "reach at 0.89");
            Near(LanceSlug.ThroughReach(0.9f), 689.8f, 0.5f, "reach at 0.9");
            Near(LanceSlug.ThroughReach(1f), 1500f, 1e-2f, "full reach 1.5 km");
            Near(LanceSlug.HitPierce(1f, 3000f, 1f, 0), 3007f, 1e-2f, "first unit takes full pierce");
            Near(LanceSlug.HitPierce(1f, 3000f, 1f, 1), 2255.25f, 1e-2f, "second unit takes 75 %");
            Near(LanceSlug.HitPierce(1f, 3000f, 1f, 2), 1691.44f, 1e-2f, "third unit takes 56 %");
            Near(LanceSlug.HitPierce(1f, 1500f, 0.2f, 0), 751.75f, 1e-2f, "glancing dot clamps to 0.5");
            Near(LanceSlug.Residual(3000f, 3007f, 30f), 2940.1f, 0.2f, "armour eats a little speed");
            Near(LanceSlug.Residual(3000f, 59f, 30f), 0f, 1e-6f, "heavy armour stops the slug");
            Near(LanceSlug.Residual(3000f, 61f, 30f), 49.18f, 0.05f, "marginal pierce leaves a crawl");
            TestAssert.That(LanceSlug.Continues(949f) && !LanceSlug.Continues(948f), "slug stops under 949 m/s");
            // four-unit chain at full power: the last unit's first hit is 0.75^3 of the first, less the speed it lost
            float speed = 3000f, first = 0f, last = 0f;
            for (int unit = 0; unit < 4; unit++)
            {
                float hit = LanceSlug.HitPierce(1f, speed, 1f, unit);
                if (unit == 0) first = hit;
                last = hit;
                speed = LanceSlug.Residual(speed, hit, 30f);
            }
            TestAssert.That(last / first > 0.30f && last / first < 0.45f && LanceSlug.Continues(speed), "four-unit chain keeps ~40 % damage on the last unit");

            // ---- pose
            Near(LancePose.BarrelTravel(0f), 0.10f, 1e-6f, "snap recoil travel");
            Near(LancePose.BarrelTravel(1f), 0.25f, 1e-6f, "full recoil travel");
            Near(LancePose.BarrelOffset(0f, 1f), 0f, 1e-6f, "barrel at rest before the shot");
            Near(LancePose.BarrelOffset(0.07f, 1f), 0.25f, 1e-5f, "barrel peaks at 0.07 s");
            Near(LancePose.BarrelOffset(0.07f, 0f), 0.10f, 1e-5f, "snap peak");
            Near(LancePose.BarrelOffset(0.42f, 1f), 0.25f * Mathf.Exp(-1f), 1e-4f, "barrel eases home with tau 0.35 s");
            TestAssert.That(LancePose.BarrelOffset(9f, 1f) < 0.001f, "barrel is home after the shot");
            Near(LancePose.StripFill(0, 0.5f), 1f, 1e-6f, "strip 0 full at half charge");
            Near(LancePose.StripFill(3, 0.5f), 0.5f, 1e-5f, "strip 3 half lit at half charge");
            Near(LancePose.StripFill(4, 0.5f), 0f, 1e-6f, "strip 4 dark at half charge");
            Near(LancePose.StripFill(6, 1f), 1f, 1e-6f, "last strip lit at full charge");
            Near(LancePose.StripIntensity(0, 0.5f, true, 0f, -1f, 0f, -1f), 3.2f, 1e-5f, "lit strip glows 3.2");
            Near(LancePose.StripIntensity(5, 0.5f, true, 0f, -1f, 0f, -1f), 0f, 1e-6f, "unlit strip stays dark");
            Near(LancePose.StripIntensity(0, 1f, true, 0.25f / LancePose.PulseHz / 1f, -1f, 0f, -1f), 3.2f, 1e-4f, "full bank pulses at its peak");
            Near(LancePose.StripIntensity(2, 0f, false, 0f, 0f, 1f, -1f), 2f, 1e-5f, "release afterglow starts at 2 x power");
            TestAssert.That(LancePose.StripIntensity(2, 0f, false, 0f, 3f, 1f, -1f) < 0.1f, "afterglow fades");
            Near(LancePose.StripIntensity(1, 0f, false, 0f, -1f, 0f, 1f), 0.35f, 1e-5f, "primed bank holds 35 % on refilled strips");
            Near(LancePose.StripIntensity(6, 0f, false, 0f, -1f, 0f, 0.5f), 0f, 1e-5f, "recharge refills aft to fore");
            Near(LancePose.HeatBlend(0.3f), 0f, 1e-6f, "cool strips stay cyan");
            Near(LancePose.HeatBlend(1.1f), 1f, 1e-6f, "overheated strips go orange");
        }
    }
}
