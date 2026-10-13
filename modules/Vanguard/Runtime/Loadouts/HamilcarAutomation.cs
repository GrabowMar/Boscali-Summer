#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Vanguard
{
    /// <summary>
    /// HAMILCAR sim hook (tests/ingame/vg-hamilcar.json, vg-hamilcar-hold.json): Step {action:"hamilcar", do:...}.
    ///   do=status              the generic status plus numeric glider telemetry under "hamilcar" (the default)
    ///   do=tune [husk, hold]   husk lifetime in seconds; hold=1 makes an AI launcher's vehicle hold position like a player's
    ///   do=fit                 the lead carries the hold mount alone on the first set that offers it; the stations rebuild over the next seconds
    ///   do=fire                releases it from the hold at the `ground` target through the station's own launch path
    ///   do=view [angle]        sim camera: chase (default) or front on the last glider, inside or ramp on the carrier, off
    /// A direct launch is Step {action:"launch", key:"VG_Hamilcar", at:"ground"}, or at:"point" with x, z (map metres) for a bare map point.
    /// </summary>
    public static partial class VanguardAutomation
    {
        private static Dictionary<string, object> Hamilcar(Dictionary<string, object> args, Aircraft lead)
        {
            switch (Text(args, "do") ?? "status")
            {
                case "tune":
                    VanguardFlight.HamilcarHusk = Number(args, "husk", VanguardFlight.HamilcarHusk);
                    if (Has(args, "hold")) VanguardFlight.HamilcarHold = Number(args, "hold", 0f) > 0f;
                    return HamilcarSim.Status(lead);
                case "fit": return HamilcarSim.Fit(lead);
                case "fire": return HamilcarSim.Fire(lead);
                case "view": return HamilcarSim.View(lead, Text(args, "angle") ?? "chase");
                case "status": return HamilcarSim.Status(lead);
                default: return Failure("VanguardAutomation", "hamilcar", "do must be status, tune, fit, fire or view");
            }
        }

        // launch at:"point": no unit; a glider is re-aimed at the bare map point (pointAimed 1), any other missile flies its no-lock route.
        private static Dictionary<string, object> LaunchAtPoint(string key, Aircraft lead, float x, float z)
        {
            Launched.TryGetValue(key ?? "", out Missile before);
            Dictionary<string, object> state = Launch(key, lead, null);
            bool aimed = Launched.TryGetValue(key ?? "", out Missile shot) && shot != null && shot != before &&
                VanguardRegistry.TryGet(shot, out VanguardFlight flight) && flight.HamilcarAimAt(new GlobalPosition(x, 0f, z));
            state["pointAimed"] = aimed ? 1 : 0;
            return state;
        }

        /// <summary>Everything HAMILCAR adds to the sim hook lives here so it cannot collide with other loadouts' partials.</summary>
        private static class HamilcarSim
        {
            private const string HoldMount = "VG_Hamilcar_AGM_heavy_single";

            public static Dictionary<string, object> Status(Aircraft lead)
            {
                Dictionary<string, object> state = VanguardAutomation.Status(lead);
                var h = new Dictionary<string, object>();
                VanguardFlight.HamilcarReport(h, lead);
                // The cradle Hexhound as this peer draws it: while the glider stands, its visuals answer instead of the bare hierarchy count.
                Missile glider = VanguardFlight.HamilcarLast?.Missile;
                HamilcarVisuals visuals = glider != null ? glider.GetComponent<HamilcarVisuals>() : null;
                if (visuals != null)
                {
                    h["payloadRenderers"] = visuals.PayloadRenderers;
                    h["payloadHidden"] = visuals.PayloadHidden ? 1 : 0;
                }
                state["hamilcar"] = h;
                return state;
            }

            public static Dictionary<string, object> Fit(Aircraft lead)
            {
                if (lead == null) return Failure("VanguardAutomation", "hamilcar", "no friendly aircraft");
                WeaponMount mount = null;
                foreach (WeaponMount w in Encyclopedia.i.weaponMounts)
                    if (w != null && w.jsonKey == HoldMount) mount = w;
                if (mount == null) return Failure("VanguardAutomation", "hamilcar", HoldMount + " is not in the Encyclopedia");
                AdoptPrefabOptions(lead);
                HardpointSet[] sets = lead.weaponManager.hardpointSets;
                int set = Array.FindIndex(sets, s => s.weaponOptions.Contains(mount));
                if (set < 0) return Failure("VanguardAutomation", "hamilcar", "no hardpoint set on " + lead.definition.unitName + " offers the hold mount");
                var loadout = new NuclearOption.SavedMission.Loadout { weapons = new List<WeaponMount>() };
                for (int i = 0; i < sets.Length; i++) loadout.weapons.Add(i == set ? mount : null);
                lead.Networkloadout = loadout;
                return new Dictionary<string, object> { ["ok"] = true, ["set"] = set, ["setName"] = sets[set].name };
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

            // The station's own launch (MountedMissile.Fire: ramp, rail, spawn), the way Drain fires a station. fired 0 = no loaded glider station yet.
            public static Dictionary<string, object> Fire(Aircraft lead)
            {
                if (lead == null || groundTarget == null) return Failure("VanguardAutomation", "hamilcar", "need lead and a ground target");
                foreach (WeaponStation station in lead.weaponStations)
                {
                    GameObject round = station?.WeaponInfo != null ? station.WeaponInfo.weaponPrefab : null;
                    Missile glider = round != null ? round.GetComponent<Missile>() : null;
                    if (glider == null || glider.definition == null || glider.definition.jsonKey != VanguardKeys.Hamilcar || station.Ammo <= 0) continue;
                    station.LaunchMount(lead, groundTarget, groundTarget.GlobalPosition());
                    return new Dictionary<string, object> { ["ok"] = true, ["fired"] = 1, ["station"] = (int)station.Number };
                }
                return new Dictionary<string, object> { ["ok"] = true, ["fired"] = 0 };
            }

            public static Dictionary<string, object> View(Aircraft lead, string angle)
            {
                Missile glider = VanguardFlight.HamilcarLast?.Missile;
                bool carrier = angle == "inside" || angle == "ramp";
                Cam.Angle = angle;
                Cam.Target = carrier ? (lead != null ? lead.transform : null) : angle == "off" || glider == null ? null : glider.transform;
                if (Cam.Target != null && UnityEngine.Object.FindObjectOfType<Cam>() == null) new GameObject("HamilcarCam").AddComponent<Cam>();
                return Status(lead);
            }

            /// <summary>Capture camera. On the glider: chase (starboard quarter) or front (low off the port bow, where the nose opens).
            /// On the carrier: inside (its hold, looking aft) or ramp (behind the cargo ramp).</summary>
            public sealed class Cam : MonoBehaviour
            {
                public static Transform Target;
                public static string Angle;

                private void LateUpdate()
                {
                    CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                    if (camera == null || Target == null) return;
                    Transform t = Target;
                    Vector3 eye, look;
                    switch (Angle)
                    {
                        case "front": eye = t.position + Vector3.up * 3.5f + t.forward * 13f - t.right * 9f; look = t.position + Vector3.up * 1.4f + t.forward * 1.5f; break;
                        case "inside": eye = t.TransformPoint(1.6f, -1f, 3.2f); look = t.TransformPoint(0f, -3.2f, -3f); break;
                        case "ramp": eye = t.TransformPoint(7f, -3f, -24f); look = t.TransformPoint(0f, -3.6f, -8f); break;
                        default: eye = t.position + Vector3.up * 6f + t.right * 12f - t.forward * 15f; look = t.position + Vector3.up * 1.4f; break;
                    }
                    camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(look - eye, Vector3.up));
                    camera.cameraVelocity = Vector3.zero;
                }
            }
        }
    }
}
#endif
