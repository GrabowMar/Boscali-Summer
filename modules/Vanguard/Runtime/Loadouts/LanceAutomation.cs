using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// LANCE sim hook (tests/ingame/vg-lance.json): BoscaliSummer.Vanguard.VanguardAutomation.LanceStep with
    /// action = equip | select | hold | heat | chain | reset | status. Inert outside a hosted single-player nomodkit run.
    /// The sim cannot press a key, so hold calls WeaponStation.Fire every frame exactly as the pilot state does.
    /// </summary>
    public static partial class VanguardAutomation
    {
        private const string LanceMount = "VG_Lance_Pod";
        private static GameObject lanceProbe;

        public static Dictionary<string, object> LanceStep(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("VanguardAutomation", "LanceStep", "requires a hosted single-player nomodkit scenario");
            Aircraft lead = FindAircraft(Text(args, "faction") ?? "Boscali");
            Aircraft bandit = FindAircraft(Text(args, "enemy") ?? "Primeva");
            if (bandit != null) enemyHq = bandit.NetworkHQ;
            switch (Text(args, "action"))
            {
                case "equip": return LanceEquip(lead, Text(args, "pylon"));
                case "select": return LanceSelect(lead);
                case "hold": return LanceHold(lead, Number(args, "seconds", 1f), !Has(args, "full") || Bool(args, "full"));
                case "heat": return LanceSetHeat(lead, Number(args, "set", 0f));
                case "chain":
                    return LanceChain(lead, (int)Number(args, "count", 4f), Number(args, "spacing", 45f), Number(args, "power", 1f), (int)Number(args, "friend", -1f));
                case "reset": return LanceReset(lead);
                case "status": return LanceStatus(lead);
                default: return Failure("VanguardAutomation", "LanceStep", "unknown action");
            }
        }

        private static LancePod LanceOf(Aircraft lead)
        {
            foreach (LancePod pod in LanceService.Pods.Values)
                if (pod.Gun != null && pod.Aircraft == lead) return pod;
            return null;
        }

        // Gives the lead the pod alone on the first matching set; the station list rebuilds over the next seconds.
        private static Dictionary<string, object> LanceEquip(Aircraft lead, string pylon)
        {
            if (lead == null) return Failure("VanguardAutomation", "lance_equip", "no friendly aircraft");
            WeaponMount mount = null;
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.jsonKey == LanceMount) mount = w;
            if (mount == null) return Failure("VanguardAutomation", "lance_equip", LanceMount + " is not in the Encyclopedia");
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            int set = -1;
            for (int i = 0; i < sets.Length && set < 0; i++)
                if (sets[i].weaponOptions.Contains(mount) && (string.IsNullOrEmpty(pylon) || sets[i].name.IndexOf(pylon, StringComparison.OrdinalIgnoreCase) >= 0)) set = i;
            if (set < 0) return Failure("VanguardAutomation", "lance_equip", "no hardpoint set on " + lead.definition.unitName + " offers the pod for '" + pylon + "'");
            var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
            for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(i == set ? mount : null);
            lead.Networkloadout = loadout;
            if (lanceProbe == null) lanceProbe = new GameObject("LanceProbe", typeof(LanceProbe));
            return new Dictionary<string, object> { ["ok"] = true, ["set"] = set, ["setName"] = sets[set].name, ["mountMassKg"] = mount.mass, ["mountAmmo"] = mount.ammo };
        }

        // Makes the Lance station current. ok even when the swap has not landed yet (found = 0): the scenario asserts it.
        private static Dictionary<string, object> LanceSelect(Aircraft lead)
        {
            LancePod pod = LanceOf(lead);
            WeaponStation station = pod?.ResolveStation();
            var result = new Dictionary<string, object> { ["ok"] = true, ["found"] = station != null ? 1 : 0 };
            if (station == null) return result;
            lead.SetActiveStation(station.Number);
            result["ammo"] = station.Ammo;
            result["station"] = station.Number;
            result["selected"] = lead.weaponManager.currentWeaponStation == station ? 1 : 0;
            return result;
        }

        private static Dictionary<string, object> LanceHold(Aircraft lead, float seconds, bool full)
        {
            LancePod pod = LanceOf(lead);
            WeaponStation station = pod?.ResolveStation();
            if (station == null) return new Dictionary<string, object> { ["ok"] = true, ["started"] = 0 };
            LanceService.AutoRelease = !full;
            var go = new GameObject("LanceHold");
            go.AddComponent<LanceHolder>().Begin(lead, station, seconds);
            return new Dictionary<string, object> { ["ok"] = true, ["started"] = 1 };
        }

        private static Dictionary<string, object> LanceSetHeat(Aircraft lead, float heat)
        {
            LancePod pod = LanceOf(lead);
            if (pod == null) return new Dictionary<string, object> { ["ok"] = true, ["set"] = 0 };
            pod.Heat = heat;
            LanceServer.SetHeat(lead, heat);
            return new Dictionary<string, object> { ["ok"] = true, ["set"] = 1, ["heat"] = heat };
        }

        private static Dictionary<string, object> LanceReset(Aircraft lead)
        {
            LancePod pod = LanceOf(lead);
            if (pod != null)
            {
                pod.Heat = 0f;
                pod.ReadyAt = 0f;
                pod.Phase = LancePhase.Idle;
            }
            LanceServer.Reset();
            LanceProbe.Reset();
            foreach (string key in new List<string>(VanguardStats.Counters.Keys))
                if (key.StartsWith("lance.", StringComparison.Ordinal)) VanguardStats.Counters.Remove(key);
            return new Dictionary<string, object> { ["ok"] = true };
        }

        // `count` enemy Hexhounds on one line `spacing` apart ahead of the lead (`friend` = index spawned as the lead's own
        // faction), then one slug of `power` down that line through LanceServer.Penetrate: the damage path without the net.
        private static Dictionary<string, object> LanceChain(Aircraft lead, int count, float spacing, float power, int friend)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "lance_chain", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue("UGV1_grenade", out UnitDefinition def))
                return Failure("VanguardAutomation", "lance_chain", "UGV1_grenade not in Encyclopedia.Lookup");
            Vector3 forward = lead.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            foreach (float range in new[] { 1200f, 1700f })
                for (int b = -60; b <= 60; b += 15)
                {
                    Vector3 dir = Quaternion.Euler(0f, b, 0f) * forward;
                    var points = new Vector3[count];
                    bool fits = true;
                    for (int k = 0; k < count && fits; k++)
                    {
                        Vector3 desired = lead.transform.position + dir * (range + spacing * k);
                        desired.y = Datum.LocalSeaY + 1400f;
                        fits = GroundPlacement.TryPlace(def, desired, Quaternion.LookRotation(dir), out points[k]) &&
                               (k == 0 || Mathf.Abs(points[k].y - points[0].y) <= 0.4f);
                    }
                    if (!fits) continue;
                    float top = points[0].y;
                    for (int k = 0; k < count; k++)
                    {
                        top = Mathf.Max(top, points[k].y);
                        NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, points[k].ToGlobalPosition(), Quaternion.LookRotation(dir), Vector3.zero,
                            k == friend ? lead.NetworkHQ : enemyHq, "VG_ChainTarget", 1f, true, null);
                    }
                    Physics.SyncTransforms();
                    Vector3 start = points[0] - dir * 5f;
                    start.y = top + 1.1f;
                    LanceServer.Penetrate(start, dir * 2900f, power, lead, lead.persistentID);
                    float first = LanceServer.LastFirstUnit, last = LanceServer.LastLastUnit;
                    return new Dictionary<string, object>
                    {
                        ["ok"] = true, ["placed"] = 1, ["bearing"] = b, ["units"] = LanceServer.LastUnits, ["parts"] = LanceServer.LastParts,
                        ["reach"] = LanceServer.LastReach, ["span"] = LanceServer.LastSpan, ["first"] = first, ["last"] = last,
                        ["ratio"] = first > 0f ? last / first : 0f, ["friendlySkips"] = LanceServer.LastFriendly,
                    };
                }
            return new Dictionary<string, object> { ["ok"] = true, ["placed"] = 0 };
        }

        private static readonly AccessTools.FieldRef<Turbofan, float> FanLoss = Lossref<Turbofan>();
        private static readonly AccessTools.FieldRef<Turbojet, float> JetLoss = Lossref<Turbojet>();

        private static AccessTools.FieldRef<T, float> Lossref<T>()
        {
            try { return AccessTools.FieldRefAccess<T, float>("parasiticThrustLoss"); }
            catch (Exception) { return null; }
        }

        private static float EngineLossNow(Aircraft aircraft)
        {
            float total = 0f;
            foreach (IEngine engine in aircraft.engineStates)
            {
                if (engine is Turbofan fan && FanLoss != null) total += FanLoss(fan);
                else if (engine is Turbojet jet && JetLoss != null) total += JetLoss(jet);
            }
            return total;
        }

        private static Dictionary<string, object> LanceStatus(Aircraft lead)
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["time"] = Time.timeSinceLevelLoad };
            // Every counter is always present (0 until bumped): the sim treats a missing key as a failed check.
            foreach (string name in new[] { "charges", "cancels", "shots", "faults", "pending", "releases", "rejected", "claims", "duplicates", "unmatched",
                                            "chains", "chainUnits", "chainParts", "friendlySkips", "clientDamage", "drawTicks" })
                state["lance_" + name] = VanguardStats.Get("lance." + name);
            state["lanceChainUnits"] = LanceServer.LastUnits;
            state["lanceChainReach"] = LanceServer.LastReach;
            LancePod pod = LanceOf(lead);
            state["lanceFound"] = pod != null ? 1 : 0;
            if (pod == null) return state;
            float now = Time.time;
            LanceLook look = pod.Look(now);
            float heat = pod.Heat;
            state["lanceAmmo"] = pod.Station != null ? pod.Station.Ammo : -1;
            state["lanceState"] = pod.StateCode;
            state["lanceCharge"] = look.Charging ? look.Charge : 0f;
            state["lancePowerLast"] = pod.LastPower;
            state["lanceHeat"] = heat;
            state["lanceDerate"] = LanceHeat.Derate(heat);
            state["lanceDraw"] = pod.Draw(now);
            state["lanceSag"] = LanceEngineDraw.SagOf(lead);
            state["lanceEngineLossN"] = LanceEngineDraw.TotalLoss(lead);
            state["lanceJetLossN"] = EngineLossNow(lead);
            state["lanceAbCut"] = EngineLossNow(lead) > 0f ? 1 : 0;
            state["lanceFlicker"] = look.Charging ? LanceDraw.FlickerLevel(look.Charge) : 0f;
            state["lanceReadyIn"] = pod.Phase == LancePhase.Idle ? 0f : Mathf.Max(0f, pod.ReadyAt - now);
            state["lanceBarrelM"] = LancePose.BarrelOffset(look.SinceShot, look.LastPower);
            for (int i = 0; i < LancePose.Strips; i++)
                state["lanceStrip" + i] = LancePose.StripIntensity(i, look.Charge, look.Charging, now, look.SinceShot, look.LastPower, look.Recharge01);
            state["lanceNoseUpPeakDegS"] = LanceProbe.NoseUpPeak;
            state["lanceNoseDownPeakDegS"] = LanceProbe.NoseDownPeak;
            state["lanceView"] = pod.View != null ? 1 : 0;
            return state;
        }

        // ---- sim helpers

        /// <summary>Calls WeaponStation.Fire every frame for a window, like the player state does while the button is down.</summary>
        private sealed class LanceHolder : MonoBehaviour
        {
            private Aircraft aircraft;
            private WeaponStation station;
            private float until;

            public void Begin(Aircraft owner, WeaponStation target, float seconds)
            {
                aircraft = owner;
                station = target;
                until = Time.time + seconds;
            }

            private void Update()
            {
                if (aircraft == null || aircraft.disabled || Time.time >= until)
                {
                    Destroy(gameObject);
                    return;
                }
                if (station.Ready() && !station.SafetyIsOn(aircraft)) station.Fire(aircraft, null);
            }
        }

        /// <summary>Records the largest pitch rate (deg/s) in the 0.3 s after a shot: the sign proves the nose-up kick.</summary>
        private sealed class LanceProbe : MonoBehaviour
        {
            public static float NoseUpPeak, NoseDownPeak;

            public static void Reset() => NoseUpPeak = NoseDownPeak = 0f;

            private void FixedUpdate()
            {
                float now = Time.time;
                foreach (LancePod pod in LanceService.Pods.Values)
                {
                    if (pod.Gun == null || !pod.Owner || now - pod.ShotAt > 0.3f || pod.Aircraft.rb == null) continue;
                    float rate = pod.Aircraft.transform.InverseTransformDirection(pod.Aircraft.rb.angularVelocity).x * Mathf.Rad2Deg;
                    NoseUpPeak = Mathf.Max(NoseUpPeak, -rate); // positive spin about +right pitches the nose DOWN
                    NoseDownPeak = Mathf.Max(NoseDownPeak, rate);
                }
            }
        }
    }
}
