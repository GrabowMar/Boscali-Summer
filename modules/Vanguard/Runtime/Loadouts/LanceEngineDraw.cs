using System.Collections.Generic;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// LANCE power draw on the engines of aircraft this peer simulates (owner peer; AI pods on the server). While the bank
    /// charges and primes, jets lose a share of their DRY thrust (any loss also cuts the afterburner) and turboprops lose
    /// throttle. The jets' loss is written straight into the private parasiticThrustLoss field every physics step: the
    /// SetParasiticLoss setters are one-line methods the JIT inlines into their callers, so a Harmony prefix on them would
    /// silently never run. This component runs after every other FixedUpdate (order 1000), so on swivel-duct airframes,
    /// which rewrite their own loss each step, the Lance share lands on top of the swivel's. The turboprop throttle patch
    /// stays a Harmony patch (Transmission calls Throttle through an interface, which is not inlined).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    internal sealed class LanceEngineDraw : MonoBehaviour, ISceneService
    {
        private struct Mark
        {
            public float Written, Base;
        }

        private static readonly Dictionary<object, Mark> Marks = new Dictionary<object, Mark>();
        private static readonly Dictionary<TurbineEngine, float> Throttle = new Dictionary<TurbineEngine, float>();
        private static readonly Dictionary<Aircraft, float> Sags = new Dictionary<Aircraft, float>();
        private static readonly AccessTools.FieldRef<Turbofan, float> Smoothed = Ref<Turbofan>("dynamicThrustFactorSmoothed");
        private static readonly AccessTools.FieldRef<Turbofan, float> FanLoss = Ref<Turbofan>("parasiticThrustLoss");
        private static readonly AccessTools.FieldRef<Turbojet, float> JetLoss = Ref<Turbojet>("parasiticThrustLoss");

        private readonly Dictionary<Aircraft, float> draw = new Dictionary<Aircraft, float>();
        private readonly List<Aircraft> ended = new List<Aircraft>();

        private static AccessTools.FieldRef<T, float> Ref<T>(string field)
        {
            try { return AccessTools.FieldRefAccess<T, float>(field); }
            catch (System.Exception)
            {
                Plugin.Logger?.LogWarning("LANCE engine draw: " + typeof(T).Name + "." + field + " not found; that part of the draw is off.");
                return null;
            }
        }

        public static float ThrottleFactor(TurbineEngine engine) => Throttle.TryGetValue(engine, out float factor) ? factor : 1f;

        /// <summary>Fraction of dry thrust the Lance is removing from this aircraft right now.</summary>
        public static float SagOf(Aircraft aircraft) => aircraft != null && Sags.TryGetValue(aircraft, out float sag) ? sag : 0f;

        /// <summary>The loss (N) the engines of this aircraft really carry in their parasiticThrustLoss field (sim status).</summary>
        public static float FieldLoss(Aircraft aircraft)
        {
            float total = 0f;
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine is Turbofan fan && FanLoss != null) total += FanLoss(fan);
                else if (engine is Turbojet jet && JetLoss != null) total += JetLoss(jet);
            }
            return total;
        }

        /// <summary>The part of that loss the Lance added (sim status: proves the field write took).</summary>
        public static float TotalLoss(Aircraft aircraft)
        {
            float total = 0f;
            foreach (IEngine engine in aircraft.engineStates)
                if (Marks.TryGetValue(engine, out Mark mark)) total += mark.Written - mark.Base;
            return total;
        }

        public void ResetForScene()
        {
            Marks.Clear();
            Throttle.Clear();
            Sags.Clear();
            draw.Clear();
        }

        private void OnDestroy() => ResetForScene();

        private void FixedUpdate()
        {
            if (LanceService.Pods.Count == 0 && Sags.Count == 0) return;
            float now = Time.time;
            draw.Clear();
            foreach (LancePod pod in LanceService.Pods.Values)
            {
                if (pod.Gun == null || pod.Aircraft == null || pod.Aircraft.disabled || !pod.Owner) continue;
                float level = pod.Draw(now);
                if (!draw.TryGetValue(pod.Aircraft, out float best) || level > best) draw[pod.Aircraft] = level;
            }
            foreach (KeyValuePair<Aircraft, float> pair in draw)
            {
                Aircraft aircraft = pair.Key;
                float sag = LanceDraw.Sag(pair.Value, LanceDraw.EnergyGate(aircraft.speed, aircraft.radarAlt), aircraft.Player == null);
                if (sag > 0f || Sags.ContainsKey(aircraft)) Apply(aircraft, sag);
            }
            ended.Clear();
            foreach (Aircraft aircraft in Sags.Keys)
                if (aircraft == null || aircraft.disabled || !draw.ContainsKey(aircraft)) ended.Add(aircraft);
            foreach (Aircraft aircraft in ended)
            {
                if (aircraft != null) Apply(aircraft, 0f);
                Sags.Remove(aircraft);
            }
        }

        private static void Apply(Aircraft aircraft, float sag)
        {
            if (sag > 0f) Sags[aircraft] = sag;
            else Sags.Remove(aircraft);
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine is Turbofan fan)
                {
                    if (FanLoss == null) continue;
                    float dry = fan.staticThrust * (Smoothed != null ? Smoothed(fan) : 0.6f);
                    Overlay(fan, ref FanLoss(fan), LanceDraw.JetLossNewtons(dry, sag));
                }
                else if (engine is Turbojet jet)
                {
                    if (JetLoss == null) continue;
                    Overlay(jet, ref JetLoss(jet), LanceDraw.JetLossNewtons(jet.maxThrust, sag));
                }
                else if (engine is TurbineEngine prop)
                {
                    if (sag > 0f) Throttle[prop] = LanceDraw.TurbineThrottleFactor(sag);
                    else Throttle.Remove(prop);
                }
            }
            VanguardStats.Bump("lance.drawTicks");
        }

        // Puts `extra` newtons of Lance loss on top of whatever else sets this engine's loss. If the field still holds what
        // this method wrote last step nobody else touched it, so the other share is the remembered base; otherwise the
        // swivel-duct system rewrote it this step and the field IS the base.
        private static void Overlay(object engine, ref float loss, float extra)
        {
            float baseLoss = Marks.TryGetValue(engine, out Mark mark) && loss == mark.Written ? mark.Base : loss;
            loss = baseLoss + extra;
            if (extra > 0f) Marks[engine] = new Mark { Written = loss, Base = baseLoss };
            else Marks.Remove(engine);
        }
    }
}
