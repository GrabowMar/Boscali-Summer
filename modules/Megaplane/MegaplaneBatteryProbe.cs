#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplaneBatteryProbe
    {
        private const string PeerCarrier = "Groza client battery test", PeerTarget = "Groza battery target";

        private sealed class Launches : MonoBehaviour
        {
            internal Aircraft Aircraft;
            internal string MissileKey;
            internal readonly HashSet<uint> Observed = new HashSet<uint>();

            private void LateUpdate()
            {
                foreach (Unit unit in UnitRegistry.allUnits)
                    if (unit is Missile missile && missile.ownerID == Aircraft.persistentID && missile.definition.jsonKey == MissileKey)
                        Observed.Add(missile.persistentID.Id);
            }
        }

        public static object Inspect(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object unit) || !(unit is Aircraft aircraft) || !MegaplaneModule.Is(aircraft))
                return new { ok = false };
            Unit target = args.TryGetValue("idTargetUnit", out object targetValue) ? targetValue as Unit : null;
            return Read(aircraft, target, args.ContainsKey("fire"), args.ContainsKey("emptyFirst"));
        }

        public static string Peer(string command)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Groza peer battery inspection requires a nomodkit loopback sandbox.");
            Aircraft[] aircraft = Object.FindObjectsOfType<Aircraft>();
            Aircraft carrier = aircraft.FirstOrDefault(a => MegaplaneModule.Is(a) && a.NetworkUniqueName == PeerCarrier);
            Aircraft target = aircraft.FirstOrDefault(a => a.NetworkUniqueName == PeerTarget);
            if (command == "spawn" && carrier == null && NetworkManagerNuclearOption.i.Server.Active)
            {
                AircraftDefinition definition = MegaplaneTemplate.Definition;
                AircraftDefinition enemy = Encyclopedia.Lookup["Darkreach"] as AircraftDefinition;
                Player pilot = Object.FindObjectsOfType<Player>().FirstOrDefault(p => !p.IsLocalPlayer);
                FactionHQ[] factions = FactionRegistry.GetSortedHQs().ToArray();
                if (pilot != null && definition != null && enemy != null && factions.Length > 1)
                {
                    carrier = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(pilot, definition.unitPrefab, definition.aircraftParameters.loadouts[0],
                        1f, default, new GlobalPosition(10000f, 4000f, -35000f), Quaternion.identity,
                        Vector3.forward * 130f, null, factions[0], PeerCarrier, .5f, .5f);
                    target = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, enemy.unitPrefab, enemy.aircraftParameters.loadouts[0],
                        1f, default, new GlobalPosition(18000f, 4000f, -35000f), Quaternion.identity,
                        Vector3.forward * 130f, null, factions[1], PeerTarget, .5f, .5f);
                }
            }
            return JsonConvert.SerializeObject(carrier == null || target == null ? new { ok = false } : Read(carrier, target, command == "fire", false));
        }

        private static object Read(Aircraft aircraft, Unit target, bool fire, bool emptyFirst)
        {
            WeaponStation station = aircraft.weaponStations.FirstOrDefault(s => MegaplaneSiloBattery.IsSilo(s) && s.WeaponInfo.weaponName == "NL-98");
            if (station == null || station.Weapons.Count != 2) return new { ok = false };
            if (!aircraft.TryGetComponent(out Launches launches))
            {
                launches = aircraft.gameObject.AddComponent<Launches>();
                launches.Aircraft = aircraft;
                launches.MissileKey = ((MissileLauncher)station.Weapons[0]).missile.jsonKey;
            }
            if (emptyFirst && aircraft.IsServer)
            {
                station.Weapons[0].ammo = 0;
                station.AccountAmmo();
                MegaplaneSiloBattery.NextCell(station) = 0;
            }
            int before = station.Ammo;
            if (fire && aircraft.LocalSim)
            {
                aircraft.SetActiveStation(station.Number);
                station.LaunchMount(aircraft, target, target != null ? target.GlobalPosition() : default);
            }
            var wingMounts = aircraft.GetComponentsInChildren<MegaplaneBatteryMount>()
                .Where(m => m.name.StartsWith("BS_Gun_CIWS_Inner", StringComparison.Ordinal) || m.name.StartsWith("BS_Gun_CIWS_Mid", StringComparison.Ordinal))
                .Select(m => new { m.name, parent = m.transform.parent.name, at = aircraft.transform.InverseTransformPoint(m.transform.position) }).ToArray();
            bool wingPlacement = wingMounts.Length == 4 && wingMounts.All(m => m.at.z < -15f && Math.Abs(m.at.x) > 70f &&
                (m.name.Contains("Inner") ? m.parent.StartsWith("gearbay_") && m.at.y < 4f : m.parent.StartsWith("wing1b_") && m.at.y > 6f));
            MissileDefinition missile = ((MissileLauncher)station.Weapons[0]).missile;
            uint[] missileIds = UnitRegistry.allUnits.Where(u => u is Missile m && !m.disabled && m.definition == missile && m.owner == aircraft &&
                m.targetID == (target != null ? target.persistentID : PersistentID.None)).Select(u => u.persistentID.Id).ToArray();
            return new
            {
                ok = true, local = aircraft.LocalSim, authority = aircraft.HasAuthority, server = aircraft.IsServer,
                wingPlacement, before, after = station.Ammo, left = station.Weapons[0].ammo, right = station.Weapons[1].ammo,
                next = MegaplaneSiloBattery.NextCell(station),
                missiles = missileIds.Length, missileIds = launches.Observed.ToArray(),
            };
        }
    }
}
#endif
