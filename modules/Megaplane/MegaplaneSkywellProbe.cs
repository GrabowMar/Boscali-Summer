#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Exercise the same weapon selection and trigger as the occupied Groza in solo and loopback scenarios.</summary>
    public static class MegaplaneSkywellProbe
    {
        private const string PeerName = "Groza client Skywell test";

        public static object Read(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object unit) || unit is not Aircraft a || !MegaplaneModule.Is(a))
                return new { ok = false };
            string action = args.TryGetValue("action", out object value) ? value as string : null;
            Snapshot result = Inspect(a, action);
            if (args.ContainsKey("until")) return result.state;
            return result;
        }

        public static string Peer(string action)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Groza peer Skywell inspection requires a nomodkit loopback sandbox.");
            Aircraft[] aircraft = Object.FindObjectsOfType<Aircraft>();
            Aircraft carrier = aircraft.FirstOrDefault(a => MegaplaneModule.Is(a) && a.NetworkUniqueName == PeerName);
            if (carrier == null && action == "spawn" && NetworkManagerNuclearOption.i.Server.Active)
            {
                AircraftDefinition definition = MegaplaneTemplate.Definition;
                Player pilot = Object.FindObjectsOfType<Player>().FirstOrDefault(p => !p.IsLocalPlayer);
                if (definition != null && pilot != null)
                {
                    if (pilot.HQ == null)
                    {
                        FactionHQ faction = FactionRegistry.GetSortedHQs().FirstOrDefault(hq => hq != null && !hq.preventJoin);
                        if (faction != null) pilot.SetFaction(faction);
                    }
                    if (pilot.HQ != null)
                        carrier = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(pilot, definition.unitPrefab,
                            definition.aircraftParameters.loadouts[1], 1f, default, new GlobalPosition(12000f, 4500f, -39000f),
                            Quaternion.identity, Vector3.forward * 130f, null, pilot.HQ, PeerName, .5f, .5f);
                }
            }
            if (carrier != null && (action == "wing-read" || action == "wing-fire"))
                return JsonConvert.SerializeObject(MegaplaneWingBayProbe.Read(new Dictionary<string, object>
                    { ["idUnit"] = carrier, ["fire"] = action == "wing-fire" }));
            return JsonConvert.SerializeObject(carrier != null ? Inspect(carrier, action) : new Snapshot());
        }

        private static Snapshot Inspect(Aircraft aircraft, string action)
        {
            WeaponManager manager = aircraft.weaponManager;
            HardpointSet cargo = manager.hardpointSets.FirstOrDefault(set => set.name == "CARGO HOLD");
            HardpointSet kit = manager.hardpointSets.FirstOrDefault(set => set.name == "SKYWELL TENDER");
            WeaponStation cargoStation = FindStation(aircraft, cargo);
            WeaponStation kitStation = FindStation(aircraft, kit);
            WeaponStation fire = action == "trigger" ? kitStation : action == "cargo" ? cargoStation : null;
            bool triggered = fire != null && aircraft.HasAuthority && GameManager.GetLocalAircraft(out Aircraft local) && local == aircraft;
            if (triggered)
            {
                aircraft.SetActiveStation(fire.Number);
                manager.Fire();
            }

            var snapshot = new Snapshot
            {
                ok = kitStation != null, server = aircraft.IsServer, local = aircraft.LocalSim, authority = aircraft.HasAuthority,
                triggered = triggered, speed = aircraft.rb.velocity.magnitude,
                seated = aircraft.Player != null && aircraft.Player.Aircraft == aircraft,
                ready = kitStation != null && kitStation.Ready() && !kitStation.SafetyIsOn(aircraft),
                gearUp = aircraft.gearState == LandingGear.GearState.LockedRetracted,
                kitAmmo = kitStation?.GetAmmoTotal() ?? -1,
                cargoAmmo = cargoStation?.GetAmmoTotal() ?? -1,
                ramp = aircraft.TryGetComponent(out MegaplaneRamp ramp) ? ramp.Open : -1f,
                kitKey = kit?.weaponMount == null ? "" : kit.weaponMount.jsonKey,
                cargoKey = cargo?.weaponMount == null ? "" : cargo.weaponMount.jsonKey,
                separated = kitStation != null && cargoStation != null && kitStation != cargoStation &&
                    CargoStationRead.KindOf(kitStation) == CargoKind.None && CargoStationRead.KindOf(cargoStation) == CargoKind.Cargo &&
                    !kit.weaponMount.Cargo && kit.hardpoints.All(h => h.bayDoors == null || h.bayDoors.Length == 0) &&
                    cargo.weaponOptions.All(m => m == null || !m.jsonKey.StartsWith("VG_Skywell", StringComparison.Ordinal)),
            };
            if (ModuleServices.TryGet(out ISkywellTenderView tenders))
            {
                snapshot.state = tenders.TenderState(aircraft);
                if (tenders.TryGetPosition(aircraft, out Vector3 world))
                {
                    snapshot.visible = true;
                    Vector3 at = aircraft.transform.InverseTransformPoint(world);
                    snapshot.tenderY = at.y;
                    snapshot.tenderZ = at.z;
                }
            }
            if (kit != null && kit.hardpoints.Count > 0)
            {
                Vector3 at = aircraft.transform.InverseTransformPoint(kit.hardpoints[0].transform.position);
                snapshot.mountY = at.y;
                snapshot.mountZ = at.z;
            }
            return snapshot;
        }

        private static WeaponStation FindStation(Aircraft aircraft, HardpointSet set) => set?.weaponMount == null ? null :
            aircraft.weaponStations.FirstOrDefault(s => s.WeaponInfo == set.weaponMount.info);

        private sealed class Snapshot
        {
            public bool ok, server, local, authority, triggered, seated, ready, gearUp, separated, visible;
            public float speed, ramp, mountY, mountZ, tenderY, tenderZ;
            public int kitAmmo, cargoAmmo;
            public string state = "none", kitKey, cargoKey;
        }
    }
}
#endif
