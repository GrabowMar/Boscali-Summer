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
    /// throttle. The three engine patches read the tables below; swivel-duct airframes keep their own SetParasiticLoss
    /// writes, the patch just adds ours to them.
    /// </summary>
    internal sealed class LanceEngineDraw : MonoBehaviour, ISceneService
    {
        private static readonly Dictionary<object, float> Loss = new Dictionary<object, float>();
        private static readonly Dictionary<TurbineEngine, float> Throttle = new Dictionary<TurbineEngine, float>();
        private static readonly Dictionary<Aircraft, float> Sags = new Dictionary<Aircraft, float>();
        private static readonly Dictionary<Aircraft, bool> Swivel = new Dictionary<Aircraft, bool>();
        private static readonly AccessTools.FieldRef<Turbofan, float> Smoothed = SmoothedRef();

        private readonly Dictionary<Aircraft, float> draw = new Dictionary<Aircraft, float>();
        private readonly List<Aircraft> ended = new List<Aircraft>();

        private static AccessTools.FieldRef<Turbofan, float> SmoothedRef()
        {
            try { return AccessTools.FieldRefAccess<Turbofan, float>("dynamicThrustFactorSmoothed"); }
            catch (System.Exception) { return null; }
        }

        /// <summary>Extra thrust loss (N) the Lance adds to this jet; 0 when it draws nothing.</summary>
        public static float ExtraLoss(object engine) => Loss.TryGetValue(engine, out float newtons) ? newtons : 0f;

        public static float ThrottleFactor(TurbineEngine engine) => Throttle.TryGetValue(engine, out float factor) ? factor : 1f;

        /// <summary>Fraction of dry thrust the Lance is removing from this aircraft right now.</summary>
        public static float SagOf(Aircraft aircraft) => aircraft != null && Sags.TryGetValue(aircraft, out float sag) ? sag : 0f;

        public void ResetForScene()
        {
            Loss.Clear();
            Throttle.Clear();
            Sags.Clear();
            Swivel.Clear();
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
            if (!Swivel.TryGetValue(aircraft, out bool swivel))
                Swivel[aircraft] = swivel = aircraft.GetComponentInChildren<SwivelDuctSystem>(true) != null;
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine is Turbofan fan)
                {
                    float dry = fan.staticThrust * (Smoothed != null ? Smoothed(fan) : 0.6f);
                    Set(fan, LanceDraw.JetLossNewtons(dry, sag));
                    if (!swivel) fan.SetParasiticLoss(0f); // the patch adds our loss to the 0
                }
                else if (engine is Turbojet jet)
                {
                    Set(jet, LanceDraw.JetLossNewtons(jet.maxThrust, sag));
                    if (!swivel) jet.SetParasiticLoss(0f);
                }
                else if (engine is TurbineEngine prop)
                {
                    if (sag > 0f) Throttle[prop] = LanceDraw.TurbineThrottleFactor(sag);
                    else Throttle.Remove(prop);
                }
            }
            VanguardStats.Bump("lance.drawTicks");
        }

        private static void Set(object engine, float newtons)
        {
            if (newtons > 0f) Loss[engine] = newtons;
            else Loss.Remove(engine);
        }

        /// <summary>Sum of the extra jet loss on an aircraft (sim status).</summary>
        public static float TotalLoss(Aircraft aircraft)
        {
            float total = 0f;
            foreach (IEngine engine in aircraft.engineStates) total += ExtraLoss(engine);
            return total;
        }
    }
}
