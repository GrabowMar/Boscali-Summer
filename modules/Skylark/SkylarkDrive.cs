using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>
    /// The hybrid drive. The fans are electric; a turbogenerator feeds them and a traction battery can carry them alone.
    /// The drive picks its own mode: at low power with charge in hand the turbine shuts down (EV: no fuel burn, a fifth of the
    /// heat, under half the noise, and heat seekers find nothing to lock), otherwise the turbine runs and recharges the pack.
    /// It runs on every peer from the same replicated engine state, so the host's seekers and each client's sound agree.
    /// </summary>
    internal sealed class SkylarkDrive : MonoBehaviour
    {
        private const float EnterBelow = .55f, LeaveAbove = .62f, EnterCharge = .25f, Floor = .05f;
        private const float Heat = .2f, FanNoise = .45f, TurbineNoise = .15f;
        private static readonly HashSet<Unit> Silent = new HashSet<Unit>();
        private static readonly string[] Percent = new string[101];
        private static Aircraft gaugeAircraft;
        private static SkylarkDrive gaugeDrive;

        private Aircraft aircraft;
        private Turbofan engine;
        private JetNozzle[] nozzles;
        private AudioSource turbine;
        private float[] irMin, irMax, fanVolume;
        private float fuelMin, fuelMax, turbineVolume;

        /// <summary>Traction battery, 0..1. A full pack gives about two minutes of hover on the fans alone.</summary>
        internal float Charge { get; private set; } = 1f;
        internal bool Electric { get; private set; }

        internal static bool IsSilent(Unit unit) => Silent.Contains(unit);

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            engine = GetComponentInChildren<Turbofan>();
            nozzles = SkylarkTemplate.Get<JetNozzle[]>(GetComponent<DuctedThrustSystem>(), "nozzles");
            turbine = SkylarkTemplate.Get<AudioSource>(engine, "turbineAudio");
            turbineVolume = turbine != null ? turbine.volume : 0f;
            fuelMin = SkylarkTemplate.Get<float>(engine, "fuelConsumptionMin");
            fuelMax = SkylarkTemplate.Get<float>(engine, "fuelConsumptionMax");
            irMin = new float[nozzles.Length]; irMax = new float[nozzles.Length]; fanVolume = new float[nozzles.Length];
            for (int i = 0; i < nozzles.Length; i++)
            {
                irMin[i] = SkylarkTemplate.Get<float>(nozzles[i], "IRMin");
                irMax[i] = SkylarkTemplate.Get<float>(nozzles[i], "IRMax");
                fanVolume[i] = SkylarkTemplate.Get<float>(nozzles[i], "thrustMaxVolume");
            }
        }

        private void Update()
        {
            float demand = Mathf.Clamp01(engine.GetThrustRatio());
            bool running = aircraft.Ignition && !aircraft.disabled;
            bool electric = running && (Electric ? demand < LeaveAbove && Charge > Floor : demand < EnterBelow && Charge > EnterCharge);
            if (electric != Electric) Apply(electric);
            float rate = Electric ? -(.003f + .012f * demand) : running ? .012f : 0f;
            Charge = Mathf.Clamp01(Charge + rate * Time.deltaTime);
        }

        // The native nozzles and engine recompute heat, sound and fuel flow every step from these limits,
        // so the mode is applied to the limits once per change, never to the per-step results.
        private void Apply(bool electric)
        {
            Electric = electric;
            if (electric) Silent.Add(aircraft); else Silent.Remove(aircraft);
            for (int i = 0; i < nozzles.Length; i++)
            {
                if (nozzles[i] == null) continue;
                SkylarkTemplate.Set(nozzles[i], "IRMin", irMin[i] * (electric ? Heat : 1f));
                SkylarkTemplate.Set(nozzles[i], "IRMax", irMax[i] * (electric ? Heat : 1f));
                SkylarkTemplate.Set(nozzles[i], "thrustMaxVolume", fanVolume[i] * (electric ? FanNoise : 1f));
            }
            SkylarkTemplate.Set(engine, "fuelConsumptionMin", electric ? 0f : fuelMin);
            SkylarkTemplate.Set(engine, "fuelConsumptionMax", electric ? 0f : fuelMax);
            if (turbine != null) turbine.volume = turbineVolume * (electric ? TurbineNoise : 1f);
        }

        private void OnDestroy() => Silent.Remove(aircraft);

        /// <summary>The TAC BOARD gauge: drive mode over the battery charge, while the local player flies a Skylark.</summary>
        internal static bool ReadGauge(out GaugeSpec gauge)
        {
            gauge = default;
            if (!GameManager.GetLocalAircraft(out Aircraft mine) || mine == null || mine.disabled) return false;
            if (mine != gaugeAircraft) { gaugeAircraft = mine; gaugeDrive = mine.GetComponent<SkylarkDrive>(); }
            if (gaugeDrive == null) return false;
            int percent = Mathf.RoundToInt(gaugeDrive.Charge * 100f);
            bool low = !gaugeDrive.Electric && gaugeDrive.Charge < EnterCharge;
            gauge = new GaugeSpec
            {
                Id = "drive", Label = "DRIVE", Kind = GaugeKind.Mode, Text = gaugeDrive.Electric ? "EV SILENT" : low ? "CHARGING" : "HYBRID",
                Sub = Percent[percent] ?? (Percent[percent] = "BAT " + percent), Value = gaugeDrive.Charge,
                Tone = gaugeDrive.Electric && gaugeDrive.Charge < .15f ? ScopeTone.Amber : ScopeTone.Ink, Rank = 42
            };
            return true;
        }
    }

    // Vanilla seekers and the combat AI only ask whether a unit has a heat signature at all; intensity alone
    // changes nothing but flare rejection. On the battery the Skylark has none to offer a seeker at launch.
    [HarmonyPatch(typeof(Unit), nameof(Unit.HasIRSignature))]
    internal static class SkylarkSilentRunningPatch
    {
        private static void Postfix(Unit __instance, ref bool __result)
        {
            if (__result && SkylarkDrive.IsSilent(__instance)) __result = false;
        }
    }
}
