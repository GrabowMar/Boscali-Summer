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
    /// GLAIVE-2S sim hook (tests/ingame/vg-glaive2s.json, vanguard.json): BoscaliSummer.Vanguard.VanguardAutomation.Glaive2s.
    /// Launch with Step {action:"launch",key:"VG_Glaive2S",at:"ground"}; this adds numeric drone telemetry under
    /// "glaive2s", a second target + designation ("handoff") and a chase camera ("view").
    /// </summary>
    public static partial class VanguardAutomation
    {
        public static Dictionary<string, object> Glaive2s(Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_SIM_SCENARIO")) ||
                GameManager.gameState != GameState.SinglePlayer || !GameAccess.IsServer())
                return Failure("VanguardAutomation", "Glaive2s", "requires a hosted single-player nomodkit scenario");
            Aircraft lead = FindAircraft(Text(args, "faction") ?? "Boscali");
            switch (Text(args, "action"))
            {
                case "handoff": return Glaive2sSim.HandOff(lead, Number(args, "offset", 900f));
                case "view":
                    Glaive2sSim.Chase.Target = Text(args, "angle") == "off" ? null : Glaive2sSim.Spectre()?.GetComponent<Missile>();
                    if (Glaive2sSim.Chase.Target != null && UnityEngine.Object.FindObjectOfType<Glaive2sSim.Chase>() == null)
                        new GameObject("Glaive2sChase").AddComponent<Glaive2sSim.Chase>();
                    return Glaive2sSim.Status(lead);
                default: return Glaive2sSim.Status(lead);
            }
        }

        /// <summary>Everything GLAIVE-2S adds to the sim hook lives here so it cannot collide with other loadouts' partials.</summary>
        private static class Glaive2sSim
        {
            private static Unit handOffTarget;
            private static GlobalPosition firstAt, secondAt;
            private static bool firstSeen, secondSeen;

            public static GlaiveDrone Spectre() =>
                Launched.TryGetValue(VanguardKeys.Glaive2S, out Missile m) && m != null && !m.disabled ? m.GetComponent<GlaiveDrone>() : null;

            public static Dictionary<string, object> Status(Aircraft lead)
            {
                Dictionary<string, object> state = VanguardAutomation.Status(lead);
                var g = new Dictionary<string, object>();
                foreach (string k in new[] { "launch", "orbit", "shots", "handoff", "depart", "winchester", "offstation", "kills" })
                    g[k] = VanguardStats.Get("glaive2s." + k);
                GlaiveDrone drone = Spectre();
                g["alive"] = drone != null ? 1 : 0;
                // A destroyed target's wreck despawns within seconds: the last place each was seen stands in for it.
                if (groundTarget != null) { firstAt = groundTarget.GlobalPosition(); firstSeen = true; }
                if (handOffTarget != null) { secondAt = handOffTarget.GlobalPosition(); secondSeen = true; }
                if (drone != null)
                {
                    Missile m = drone.GetComponent<Missile>();
                    Vector3 radial = m.GlobalPosition() - drone.Centre;
                    g["phase"] = (int)drone.Phase;
                    g["rounds"] = drone.RoundsLeft;
                    g["slide"] = drone.Slide;
                    g["bank"] = drone.BankAngle;
                    g["agl"] = m.radarAlt;
                    g["speed"] = m.speed;
                    g["radius"] = new Vector2(radial.x, radial.z).magnitude;
                    g["height"] = radial.y;
                    if (firstSeen) g["centreToFirst"] = HFlat(drone.Centre, firstAt);
                    if (secondSeen) g["centreToSecond"] = HFlat(drone.Centre, secondAt);
                }
                g["firstAlive"] = groundTarget != null && !groundTarget.disabled ? 1 : 0;
                g["secondAlive"] = handOffTarget != null && !handOffTarget.disabled ? 1 : 0;
                state["glaive2s"] = g;
                return state;
            }

            private static float HFlat(GlobalPosition a, GlobalPosition b)
            {
                Vector3 d = a - b;
                return new Vector2(d.x, d.z).magnitude;
            }

            // A second enemy Hexhound `offset` metres from the first target, then designate it (radial DESIGNATE path).
            public static Dictionary<string, object> HandOff(Aircraft lead, float offset)
            {
                enemyHq ??= FindAircraft("Primeva")?.NetworkHQ;
                GlaiveDrone drone = Spectre();
                if (lead == null || enemyHq == null || (groundTarget == null && drone == null))
                    return Failure("VanguardAutomation", "handoff", "need lead, an enemy HQ and a first ground target or a drone on station");
                if (!Encyclopedia.Lookup.TryGetValue("UGV1_grenade", out UnitDefinition def))
                    return Failure("VanguardAutomation", "handoff", "UGV1_grenade not in Encyclopedia.Lookup");
                // The drone may have destroyed the first target and the game despawned its wreck: the orbit centre stands in.
                Vector3 origin = groundTarget != null ? groundTarget.transform.position : drone.Centre.ToLocalPosition();
                for (int b = 0; b < 360; b += 30)
                {
                    Vector3 dir = Quaternion.Euler(0f, b, 0f) * Vector3.forward;
                    Vector3 desired = origin + dir * offset;
                    desired.y = Datum.LocalSeaY + 1400f;
                    if (!GroundPlacement.TryPlace(def, desired, Quaternion.LookRotation(-dir), out Vector3 point)) continue;
                    handOffTarget = NetworkSceneSingleton<Spawner>.i.SpawnVehicle(def.unitPrefab, point.ToGlobalPosition(),
                        Quaternion.LookRotation(-dir), Vector3.zero, enemyHq, "VG_HandOffTarget", 1f, true, null);
                    DroneOrders.Order(lead, handOffTarget);
                    Dictionary<string, object> state = Status(lead);
                    state["bearing"] = b;
                    return state;
                }
                return Failure("VanguardAutomation", "handoff", "no dry flat ground near the first target");
            }

            /// <summary>Sim-only chase camera outboard of the orbit (starboard, above), looking past the drone at its centre.</summary>
            [DefaultExecutionOrder(2000)]
            public sealed class Chase : MonoBehaviour
            {
                public static Missile Target;

                private void LateUpdate()
                {
                    CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                    if (camera == null || Target == null || Target.disabled) return;
                    Transform t = Target.transform;
                    Vector3 eye = t.position + t.right * 40f + Vector3.up * 14f - t.forward * 18f;
                    Vector3 look = t.position - t.right * 25f - Vector3.up * 12f;
                    camera.SetCameraPosition(eye.ToGlobalPosition(), Quaternion.LookRotation(look - eye, Vector3.up));
                    camera.cameraVelocity = Vector3.zero;
                }
            }
        }
    }
}
