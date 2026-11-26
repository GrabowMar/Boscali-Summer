#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Reads the forward bay slot for the scripted recovery proof without steering or repositioning its fighter.</summary>
    public static class MegaplaneRecoveryProbe
    {
        private const string PeerName = "Groza client recovery test";

        public static string Peer(string command)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Groza peer recovery inspection requires a nomodkit loopback sandbox.");
            Aircraft carrier = Object.FindObjectsOfType<Aircraft>()
                .FirstOrDefault(a => MegaplaneModule.Is(a) && a.NetworkUniqueName == PeerName);
            if (carrier == null && command == "spawn" && NetworkManagerNuclearOption.i.Server.Active)
            {
                AircraftDefinition definition = MegaplaneTemplate.Definition;
                Player pilot = Object.FindObjectsOfType<Player>().FirstOrDefault(p => !p.IsLocalPlayer);
                if (definition != null && pilot != null)
                {
                    if (pilot.HQ == null)
                    {
                        FactionHQ joinable = FactionRegistry.GetSortedHQs().FirstOrDefault(hq => hq != null && !hq.preventJoin);
                        if (joinable != null) pilot.SetFaction(joinable);
                    }
                    if (pilot.HQ != null)
                        carrier = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(pilot, definition.unitPrefab,
                            definition.aircraftParameters.loadouts[1], 1f, default, new GlobalPosition(12000f, 4500f, -39000f),
                            Quaternion.identity, Vector3.forward * 130f, null, pilot.HQ, PeerName, .5f, .5f);
                }
            }
            if (!MegaplaneBayControls.TryBay(carrier, out var bay, out var operations)) return JsonConvert.SerializeObject(new { ok = false });
            CarrierBayAction? action = command switch
            {
                "launch" => CarrierBayAction.Launch,
                "recall" => CarrierBayAction.Recall,
                "waveoff" => CarrierBayAction.WaveOff,
                _ => null,
            };
            bool accepted = action.HasValue && carrier.HasAuthority && GameManager.GetLocalAircraft(out Aircraft local) && local == carrier &&
                MegaplaneBayControls.Request(carrier, action.Value);
            bool stowedAfterburner = carrier.GetComponentsInChildren<Transform>(true)
                .Where(t => t.name == "BayFighter_F" || t.name == "BayFighter_A")
                .Any(t => t.GetComponentsInChildren<MeshRenderer>(true).Any(BurnerFx));
            Aircraft fighter = bay.Released[0];
            MegaplaneCapture capture = fighter != null ? fighter.GetComponent<MegaplaneCapture>() : null;
            Vector3 offset = Vector3.zero, filteredOffset = Vector3.zero;
            float filteredRelativeSpeed = 0f;
            if (fighter != null && fighter.rb != null && carrier.rb != null)
            {
                Vector3 position = fighter.rb.position;
                offset = Quaternion.Inverse(carrier.rb.rotation) * (position - carrier.rb.position);
                filteredOffset = bay.DeckLocal(position);
                filteredRelativeSpeed = (fighter.rb.velocity - bay.DeckVelocity(position)).magnitude;
            }
            return JsonConvert.SerializeObject(new
            {
                ok = true, server = carrier.IsServer, local = carrier.LocalSim, authority = carrier.HasAuthority, deckReady = bay.DeckReady,
                front = operations.Phase(bay, 0).ToString(), aft = operations.Phase(bay, 1).ToString(), accepted, stowedAfterburner,
                hold = bay.DeckHoldReason, time = Time.time, state = bay.State[0], arm = bay.Arm[0], door = bay.Door,
                alive = fighter != null && !fighter.disabled, departing = capture != null && capture.Departing,
                belowLane = capture != null && capture.BelowLaunchLane, offsetX = offset.x, offsetY = offset.y, offsetZ = offset.z,
                filteredOffsetX = filteredOffset.x, filteredOffsetY = filteredOffset.y, filteredOffsetZ = filteredOffset.z, filteredRelativeSpeed,
                deckSpeed = carrier.rb == null ? 0f : carrier.rb.velocity.magnitude,
            });
        }

        public static object Read(Dictionary<string, object> args)
        {
            if (!(args.TryGetValue("idUnit", out object unit) && unit is Aircraft carrier) || !MegaplaneModule.Is(carrier) ||
                !carrier.TryGetComponent(out MegaplaneBay bay)) return false;

            const int slot = 0;
            Aircraft fighter = bay.Released[slot];
            bool alive = fighter != null && !fighter.disabled;
            MegaplaneCapture capture = alive ? fighter.GetComponent<MegaplaneCapture>() : null;
            int phase = capture == null ? -1 : capture.Phase;
            int state = bay.State[slot];
            Vector3 local = alive && fighter.rb != null ? carrier.transform.InverseTransformPoint(fighter.rb.position) : Vector3.zero;
            float service = Mathf.Max(0f, bay.ReadyAt[slot] - Time.time);
            if (args.TryGetValue("until", out object condition))
                return (condition as string) switch
                {
                    "deck" => bay.DeckReady,
                    "released" => state == 2 && alive,
                    "recallable" => state == 2 && alive && capture == null,
                    // At the marshal gate the fighter can already be clear of the bay. Bank once it has flown inside the gate.
                    "lineup" => phase == 1 && local.z > -165f,
                    "bolter" => phase == 4,
                    "captured" => state == 4 && !alive ? "captured" :
                        $"slot {state}, phase {phase}, deck ready {bay.DeckReady}, fighter at {local:F2} in deck coordinates",
                    "secured" => state == 0 && bay.Door == 0f,
                    "ready" => state == 0 && service == 0f && bay.DeckReady,
                    _ => false,
                };

            Transform model = carrier.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "BayFighter_F");
            MeshRenderer[] modelRenderers = model != null ? model.GetComponentsInChildren<MeshRenderer>() : Array.Empty<MeshRenderer>();
            return new
            {
                state, phase, alive, service, deckZ = local.z, deckReady = bay.DeckReady, door = bay.Door, arm = bay.Arm[slot],
                rollCommand = Mathf.Abs(carrier.GetInputs().roll),
                friendly = alive && fighter.NetworkHQ == carrier.NetworkHQ,
                hookedModel = model != null,
                nozzle = modelRenderers.Any(r => r.name == "nozzle"),
                burner = modelRenderers.Any(BurnerFx),
            };
        }

        private static bool BurnerFx(MeshRenderer renderer) => renderer.name is "afterburner_flame" or "afterburner_nozzle_glow";
    }
}
#endif
