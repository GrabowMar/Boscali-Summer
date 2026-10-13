#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// LANCE sim hook (tests/ingame/vg-lance.json): BoscaliSummer.Vanguard.VanguardAutomation.LanceStep with
    /// action = equip | select | hold | heat | chain | twin | reset | status. Inert outside a hosted single-player nomodkit run.
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
                case "twin": return LanceTwin(lead, (int)Number(args, "count", 2f));
                case "reset": return LanceReset(lead);
                case "status": return LanceStatus(lead);
                default: return Failure("VanguardAutomation", "LanceStep", "unknown action");
            }
        }

        // The station lead when the aircraft has one (the pod that runs the charge cycle), else any pod of the aircraft.
        private static LancePod LanceOf(Aircraft lead)
        {
            LancePod any = null;
            foreach (LancePod pod in LanceService.Pods.Values)
            {
                if (pod.Gun == null || pod.Aircraft != lead) continue;
                any ??= pod;
                WeaponStation station = pod.ResolveStation();
                if (station != null && LanceService.LeadOf(station) == pod) return pod;
            }
            return any;
        }

        // Blueprinter adds the carrier options to the aircraft PREFAB once its bundles are loaded. Under -autoHost the mission
        // is ready first, so a sim aircraft can predate that: give its sets whatever its prefab's sets offer by now.
        private static void AdoptPrefabOptions(Aircraft lead)
        {
            WeaponManager prefab = lead.definition.unitPrefab.GetComponentInChildren<WeaponManager>(true);
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            for (int i = 0; prefab != null && i < sets.Length && i < prefab.hardpointSets.Length; i++)
                foreach (WeaponMount option in prefab.hardpointSets[i].weaponOptions)
                    if (!sets[i].weaponOptions.Contains(option)) sets[i].weaponOptions.Add(option);
        }

        // Gives the lead the pod alone on the first matching set; the station list rebuilds over the next seconds.
        private static Dictionary<string, object> LanceEquip(Aircraft lead, string pylon)
        {
            if (lead == null) return Failure("VanguardAutomation", "lance_equip", "no friendly aircraft");
            WeaponMount mount = null;
            foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                if (w != null && w.jsonKey == LanceMount) mount = w;
            if (mount == null) return Failure("VanguardAutomation", "lance_equip", LanceMount + " is not in the Encyclopedia");
            AdoptPrefabOptions(lead);
            HardpointSet[] sets = lead.weaponManager.hardpointSets;
            int set = -1;
            for (int i = 0; i < sets.Length && set < 0; i++)
                if (sets[i].weaponOptions.Contains(mount) && (string.IsNullOrEmpty(pylon) || sets[i].name.IndexOf(pylon, StringComparison.OrdinalIgnoreCase) >= 0)) set = i;
            if (set < 0)
            {
                var seen = new List<string>();
                for (int i = 0; i < sets.Length; i++) seen.Add(i + ":" + sets[i].name + (sets[i].weaponOptions.Contains(mount) ? "*" : ""));
                return Failure("VanguardAutomation", "lance_equip", "no hardpoint set on " + lead.definition.unitName + " offers the pod for '" + pylon
                    + "' (sets, * = offers the pod: " + string.Join(", ", seen) + ")");
            }
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
            // The sim lead is usually over the sea: sweep outward until a level stretch of land turns up (as `ground` does).
            for (float range = 1200f; range <= 20000f; range += 400f)
                for (int b = -60; b <= 60; b += 15)
                {
                    if (!PlaceLine(lead, def, b, range, count, spacing, out Vector3[] points, out Vector3 dir)) continue;
                    var spawned = new Unit[count];
                    for (int k = 0; k < count; k++)
                    {
                        spawned[k] = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, points[k].ToGlobalPosition(), Quaternion.LookRotation(dir), Vector3.zero,
                            k == friend ? lead.NetworkHQ : enemyHq, "VG_ChainTarget", 1f, true, null);
                    }
                    Physics.SyncTransforms();
                    // Through the spawned hulls themselves, first to last: a guessed height skims over or under a low hull.
                    Vector3 first0 = HullCentre(spawned[0], points[0]),
                        shot = count > 1 ? (HullCentre(spawned[count - 1], points[count - 1]) - first0).normalized : dir;
                    LanceServer.Penetrate(first0 - shot * 5f, shot * 2900f, power, lead, lead.persistentID);
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

        // Centre of the unit's solid colliders (its placement point while it has none).
        private static Vector3 HullCentre(Unit unit, Vector3 placed)
        {
            bool any = false;
            Bounds bounds = default;
            if (unit != null)
                foreach (Collider c in unit.GetComponentsInChildren<Collider>())
                {
                    if (c.isTrigger || !c.enabled) continue;
                    if (any) bounds.Encapsulate(c.bounds);
                    else bounds = c.bounds;
                    any = true;
                }
            return any ? bounds.center : placed;
        }

        // `count` targets on one level line `spacing` apart, `range` out along `bearing` degrees off the lead's heading.
        private static bool PlaceLine(Aircraft lead, UnitDefinition def, float bearing, float range, int count, float spacing, out Vector3[] points, out Vector3 dir)
        {
            Vector3 forward = lead.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            dir = Quaternion.Euler(0f, bearing, 0f) * forward;
            points = new Vector3[count];
            for (int k = 0; k < count; k++)
            {
                Vector3 desired = lead.transform.position + dir * (range + spacing * k);
                desired.y = Datum.LocalSeaY + 1400f;
                if (!GroundPlacement.TryPlace(def, desired, Quaternion.LookRotation(dir), out points[k])) return false;
            }
            // The slug flies first hull to last hull: an even slope serves as well as level ground, a hump or a dip does not.
            for (int k = 1; k < count - 1; k++)
                if (Mathf.Abs(points[k].y - Mathf.Lerp(points[0].y, points[count - 1].y, k / (count - 1f))) > 0.4f) return false;
            return true;
        }

        // The twin-pod volley of a CLIENT-owned aircraft, minus the net: ONE server release for `count` slugs
        // (LanceServer.Release, exactly what LanceNet.ReceiveIntent calls), then one hit claim at each of two separated
        // targets (LanceServer.Resolve, what the ArmorPenetrate patch calls), then a third claim that must read as a ricochet.
        private static Dictionary<string, object> LanceTwin(Aircraft lead, int count)
        {
            if (lead == null || enemyHq == null) return Failure("VanguardAutomation", "lance_twin", "need lead and an enemy HQ");
            if (!Encyclopedia.Lookup.TryGetValue("UGV1_grenade", out UnitDefinition def))
                return Failure("VanguardAutomation", "lance_twin", "UGV1_grenade not in Encyclopedia.Lookup");
            WeaponStation station = LanceOf(lead)?.ResolveStation();
            if (station == null) return new Dictionary<string, object> { ["ok"] = true, ["placed"] = 0, ["found"] = 0 };
            var lines = new List<Vector3[]>();
            var dirs = new List<Vector3>();
            var bearings = new List<float>();
            for (float range = 900f; range <= 20000f && lines.Count < 2; range += 400f) // outward until two targets fit on land
            {
                lines.Clear();
                dirs.Clear();
                bearings.Clear();
                for (int b = -60; b <= 60 && lines.Count < 2; b += 15)
                {
                    if (bearings.Count > 0 && b - bearings[0] < 30f) continue; // two lines at least 30 degrees apart: 470 m or more between the targets
                    if (!PlaceLine(lead, def, b, range, 1, 0f, out Vector3[] points, out Vector3 dir)) continue;
                    lines.Add(points);
                    dirs.Add(dir);
                    bearings.Add(b);
                }
            }
            if (lines.Count < 2) return new Dictionary<string, object> { ["ok"] = true, ["placed"] = 0, ["found"] = 1 };
            var twins = new Unit[2];
            for (int i = 0; i < 2; i++)
                twins[i] = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, lines[i][0].ToGlobalPosition(), Quaternion.LookRotation(dirs[i]), Vector3.zero,
                    enemyHq, "VG_TwinTarget", 1f, true, null);
            Physics.SyncTransforms();
            int unmatched0 = VanguardStats.Get("lance.unmatched"), duplicates0 = VanguardStats.Get("lance.duplicates"),
                chains0 = VanguardStats.Get("lance.chains"), pending0 = VanguardStats.Get("lance.pending");
            bool released = LanceServer.Release(lead, station.Number, station.Ammo, 1f, count, Time.time, out float power, out float _);
            int pending = VanguardStats.Get("lance.pending") - pending0;
            var result = new Dictionary<string, object> { ["ok"] = true, ["placed"] = 1, ["found"] = 1, ["released"] = released ? 1 : 0, ["power"] = power, ["pending"] = pending };
            for (int i = 0; i < 2; i++)
            {
                LanceServer.Resolve(HullCentre(twins[i], lines[i][0]) - dirs[i] * 5f, dirs[i] * 2900f, lead.persistentID);
                result["line" + i + "Units"] = LanceServer.LastUnits;
                result["line" + i + "First"] = LanceServer.LastFirstUnit; // damage the first part of that line's target took
            }
            result["unmatched"] = VanguardStats.Get("lance.unmatched") - unmatched0;
            result["duplicates"] = VanguardStats.Get("lance.duplicates") - duplicates0;
            result["chains"] = VanguardStats.Get("lance.chains") - chains0;
            LanceServer.Resolve(HullCentre(twins[0], lines[0][0]) - dirs[0] * 5f, dirs[0] * 2900f, lead.persistentID); // nothing left: a ricochet
            result["duplicatesAfterExtra"] = VanguardStats.Get("lance.duplicates") - duplicates0;
            return result;
        }

        private static Dictionary<string, object> LanceStatus(Aircraft lead)
        {
            var state = new Dictionary<string, object> { ["ok"] = true, ["time"] = Time.timeSinceLevelLoad };
            // Every counter is always present (0 until bumped): the sim treats a missing key as a failed check.
            foreach (string name in new[] { "charges", "cancels", "shots", "faults", "pending", "releases", "rejected", "claims", "duplicates", "unmatched",
                                            "chains", "chainUnits", "chainParts", "friendlySkips", "clientDamage", "drawTicks", "volleys", "errors", "castOverflow" })
                state["lance_" + name] = VanguardStats.Get("lance." + name);
            state["lanceChainUnits"] = LanceServer.LastUnits;
            state["lanceChainReach"] = LanceServer.LastReach;
            state["lanceHudLines"] = LanceService.HudLines;
            state["lanceJam"] = SceneSingleton<CombatHUD>.i != null ? SceneSingleton<CombatHUD>.i.jamAccumulation : -1f;
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
            float fieldLoss = LanceEngineDraw.FieldLoss(lead);
            state["lanceJetLossN"] = fieldLoss; // read back from the engines' own field, not from the Lance's tables
            state["lanceAbCut"] = fieldLoss > 0f ? 1 : 0;
            state["lanceFlicker"] = look.Charging ? LanceDraw.FlickerLevel(look.Charge) : 0f;
            state["lanceReadyIn"] = pod.Phase == LancePhase.Idle ? 0f : Mathf.Max(0f, pod.ReadyAt - now);
            state["lanceBarrelM"] = LancePose.BarrelOffset(look.SinceShot, look.LastPower);
            for (int i = 0; i < LancePose.Strips; i++)
                state["lanceStrip" + i] = LancePose.StripIntensity(i, look.Charge, look.Charging, now, look.SinceShot, look.LastPower, look.Recharge01);
            state["lanceNoseUpPeakDegS"] = LanceProbe.NoseUpPeak;
            state["lanceNoseDownPeakDegS"] = LanceProbe.NoseDownPeak;
            state["lanceView"] = pod.View != null ? 1 : 0;
            // Per pod of the station, in station order: both pods of a twin station must load, fire and flash together.
            int pods = 0;
            if (pod.Station != null && LanceAccess.Ensure())
                foreach (Weapon weapon in pod.Station.Weapons)
                    if (weapon is Gun gun && LanceService.Pods.TryGetValue(gun, out LancePod member))
                    {
                        state["lancePod" + pods + "Ammo"] = gun.ammo;
                        state["lancePod" + pods + "Loaded"] = LanceAccess.BulletsLoaded(gun);
                        state["lancePod" + pods + "Fired"] = member.ShotAt > 0f ? 1 : 0;
                        pods++;
                    }
            state["lancePods"] = pods;
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
#endif
