using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Modules;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// ASQ-X WATCHTOWER: a hidden recon pod cloned from the jamming pod. While one hangs on a live aircraft it gives that aircraft
    /// IRST (aircraft ahead, host), ESM (radars that emit, host), MAWS (early IR missile warning, on the pilot's own peer) and a
    /// drone relay (<see cref="LinkScale"/>, every peer). Detections go to the owner's FactionHQ tracking, so they show on the map as is.
    /// </summary>
    internal static class WatchtowerPod
    {
        public static readonly Type[] PatchTypes = new Type[0];

        private static readonly AccessTools.FieldRef<MissileWarning, float> DetectionRange = Ref();
        /// <summary>Aircraft carrying at least one pod, with the pod count; filled on every peer by the pods themselves.</summary>
        private static readonly Dictionary<Aircraft, int> Fitted = new Dictionary<Aircraft, int>();
        /// <summary>Warning systems whose range a pod raised, with the range to give back.</summary>
        private static readonly Dictionary<MissileWarning, float> Raised = new Dictionary<MissileWarning, float>();

        public static void Register(ModuleContext context)
        {
            RuntimeStores.Add(new StoreRecipe
            {
                Key = VanguardKeys.WatchtowerMount, Donor = "JammingPod1", Carriers = StormcellPod.Carriers,
                Build = mount => StormcellPod.HidePod<WatchtowerUnit>(mount, "ASQ-X WATCHTOWER", 150f, 0.5f, StoreArt.Watchtower)
            });
            context.AddSceneService<WatchtowerService>(60);
        }

        /// <summary>The launcher's drone link scale: <see cref="PodRules.RelayScale"/> when a hardpoint set holds the WATCHTOWER, else 1.</summary>
        public static float LinkScale(Unit launcher)
        {
            WeaponMount pod = RuntimeStores.Mount(VanguardKeys.WatchtowerMount);
            HardpointSet[] sets = pod != null && launcher is Aircraft aircraft && aircraft.weaponManager != null ? aircraft.weaponManager.hardpointSets : null;
            if (sets != null)
                foreach (HardpointSet set in sets)
                    if (set != null && set.weaponMount == pod) return PodRules.LinkScale(true);
            return PodRules.LinkScale(false);
        }

        internal static void Add(Aircraft aircraft)
        {
            Fitted.TryGetValue(aircraft, out int n);
            Fitted[aircraft] = n + 1;
        }

        internal static void Remove(Aircraft aircraft)
        {
            if (!Fitted.TryGetValue(aircraft, out int n)) return;
            if (n > 1) { Fitted[aircraft] = n - 1; return; }
            Fitted.Remove(aircraft);
            MissileWarning warning = aircraft != null ? aircraft.GetMissileWarningSystem() : null;
            if (warning != null && Raised.TryGetValue(warning, out float original))
            {
                if (DetectionRange != null) DetectionRange(warning) = original;
                Raised.Remove(warning);
            }
        }

        internal static void Reset()
        {
            Fitted.Clear();
            Raised.Clear();
        }

        internal static void Snapshot(List<Aircraft> into)
        {
            into.Clear();
            foreach (Aircraft aircraft in Fitted.Keys) into.Add(aircraft);
        }

        /// <summary>MAWS: the pilot's own aircraft hears IR missiles from <see cref="PodRules.MawsRangeM"/> (the vanilla list admits them inside detectionRange with a clear line).</summary>
        internal static void RaiseWarning(Aircraft aircraft)
        {
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            if (DetectionRange == null || warning == null || Raised.ContainsKey(warning) || !GameManager.GetLocalAircraft(out Aircraft me) || me != aircraft) return;
            float original = DetectionRange(warning);
            Raised[warning] = original;
            DetectionRange(warning) = PodRules.MawsRange(original);
            Debug.Log("[Vanguard] WATCHTOWER MAWS: detectionRange " + original + " -> " + DetectionRange(warning));
        }

        private static AccessTools.FieldRef<MissileWarning, float> Ref()
        {
            try { return AccessTools.Field(typeof(MissileWarning), "detectionRange") != null ? AccessTools.FieldRefAccess<MissileWarning, float>("detectionRange") : null; }
            catch (Exception) { return null; }
        }
    }

    /// <summary>Runs the pods' senses: MAWS arming on every peer (1 Hz), IRST and ESM on the host.</summary>
    internal sealed class WatchtowerService : MonoBehaviour, ISceneService
    {
        private readonly List<Aircraft> carriers = new List<Aircraft>();
        private readonly List<Unit> scratch = new List<Unit>();
        private float nextMaws, nextIrst, nextEsm;

        public void ResetForScene()
        {
            WatchtowerPod.Reset();
            nextMaws = nextIrst = nextEsm = 0f;
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextMaws && now < nextIrst && now < nextEsm) return;
            WatchtowerPod.Snapshot(carriers);
            if (carriers.Count == 0) return;
            try
            {
                if (now >= nextMaws)
                {
                    nextMaws = now + 1f;
                    foreach (Aircraft aircraft in carriers) if (aircraft != null && !aircraft.disabled) WatchtowerPod.RaiseWarning(aircraft);
                }
                bool irst = now >= nextIrst, esm = now >= nextEsm;
                if (irst) nextIrst = now + PodRules.IrstPeriodS;
                if (esm) nextEsm = now + PodRules.EsmPeriodS;
                if (!GameAccess.IsServer()) return;
                foreach (Aircraft aircraft in carriers)
                {
                    if (aircraft == null || aircraft.disabled || aircraft.NetworkHQ == null) continue;
                    if (irst) Irst(aircraft);
                    if (esm) Esm(aircraft);
                }
            }
            catch (Exception error) { PatchGuard.Report("Vanguard.Watchtower", error); }
        }

        /// <summary>Enemy aircraft ahead of the nose inside IRST range with a clear line: the owner's faction tracks them.</summary>
        private void Irst(Aircraft aircraft)
        {
            Vector3 pos = aircraft.transform.position, forward = aircraft.transform.forward;
            FactionHQ hq = aircraft.NetworkHQ;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(aircraft.GlobalPosition(), PodRules.IrstRangeM, scratch);
            foreach (Unit unit in scratch)
                if (unit is Aircraft && !unit.disabled && unit.NetworkHQ != null && unit.NetworkHQ != hq &&
                    PodRules.InIrst(forward, unit.transform.position - pos, PodRules.IrstRangeM) && unit.LineOfSight(pos, 1000f))
                    hq.RpcUpdateTrackingInfo(unit.persistentID);
        }

        /// <summary>Enemy units with a radar on inside ESM range: the owner's faction tracks them, no line needed.</summary>
        private void Esm(Aircraft aircraft)
        {
            FactionHQ hq = aircraft.NetworkHQ;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(aircraft.GlobalPosition(), PodRules.EsmRangeM, scratch);
            foreach (Unit unit in scratch)
                if (!unit.disabled && unit.NetworkHQ != null && unit.NetworkHQ != hq && unit.HasRadarEmission())
                    hq.RpcUpdateTrackingInfo(unit.persistentID);
        }
    }

    /// <summary>The WATCHTOWER on one hardpoint. A Weapon only so the game spawns and attaches it with the mount; it never gets a station.</summary>
    internal sealed class WatchtowerUnit : Weapon, IHiddenPod
    {
        private Aircraft carrier;
        private bool counted;

        public override void AttachToHardpoint(Aircraft aircraft, Hardpoint hardpoint, WeaponMount weaponMount)
        {
            base.AttachToHardpoint(aircraft, hardpoint, weaponMount);
            if (counted) return;
            counted = true;
            carrier = aircraft;
            WatchtowerPod.Add(aircraft);
        }

        public override float GetMass() => 0f; // info is null on this mount

        private void OnDestroy()
        {
            if (counted) WatchtowerPod.Remove(carrier);
            counted = false;
        }
    }
}
