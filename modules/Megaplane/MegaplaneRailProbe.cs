#if !PUBLIC_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NuclearOption.Networking;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplaneRailProbe
    {
        private const string PeerName = "Groza client rail test";

        public static string Peer(string command)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NOMODKIT_PEER_PORT")))
                throw new InvalidOperationException("Groza peer rail inspection requires a nomodkit loopback sandbox.");
            Aircraft carrier = Object.FindObjectsOfType<Aircraft>().FirstOrDefault(a => MegaplaneModule.Is(a) && a.NetworkUniqueName == PeerName);
            if (carrier == null && command == "spawn" && NetworkManagerNuclearOption.i.Server.Active && MegaplaneSettings.Railgun)
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
                            definition.aircraftParameters.loadouts[1], 1f, default, new GlobalPosition(57000f, 5500f, -60000f),
                            Quaternion.identity, Vector3.forward * 130f, null, pilot.HQ, PeerName, .5f, .5f);
                }
            }
            if (carrier == null || !carrier.TryGetComponent(out MegaplaneRailgun rail) || rail.Turret == null)
                return JsonConvert.SerializeObject(new { ok = false });
            WeaponStation station = rail.Turret.GetWeaponStation();
            if (station == null) return JsonConvert.SerializeObject(new { ok = false });
            if (carrier.HasAuthority && GameManager.GetLocalAircraft(out Aircraft local) && local == carrier)
            {
                switch (command)
                {
                    case "select":
                        carrier.SetActiveStation(station.Number);
                        carrier.weaponManager.SetGunsLinked(false);
                        break;
                    case "stow":
                        WeaponStation other = carrier.weaponStations.FirstOrDefault(s => s != station);
                        if (other != null) carrier.SetActiveStation(other.Number);
                        break;
                    case "aim":
                        CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
                        if (camera != null && camera.currentState == camera.cockpitState) camera.SwitchState(camera.orbitState);
                        Vector3 direction = carrier.transform.TransformDirection(Quaternion.Euler(14f, 52f, 0f) * Vector3.forward);
                        rail.Turret.SetVector(direction);
                        carrier.SetTurretVector(station.Number, direction);
                        break;
                    case "fire":
                        rail.StartCoroutine(PullTrigger(carrier, rail, station));
                        break;
                }
            }
            Vector3 aim = rail.NativeAim;
            return JsonConvert.SerializeObject(new
            {
                ok = true, server = carrier.IsServer, local = carrier.LocalSim, authority = carrier.HasAuthority,
                phase = rail.Phase.ToString(), selected = rail.Selected, remoteSelected = rail.RemoteSelected, wanted = rail.Wanted,
                live = rail.Live, ready = rail.Live && station.Ready(), ammo = station.Ammo, shots = rail.Shots, aimX = aim.x, aimY = aim.y, aimZ = aim.z,
            });
        }

        private static IEnumerator PullTrigger(Aircraft carrier, MegaplaneRailgun rail, WeaponStation station)
        {
            int startShots = rail.Shots;
            float stopAt = Time.time + .35f;
            while (Time.time < stopAt && rail.Live && rail.Shots == startShots && carrier.weaponManager.currentWeaponStation == station)
            {
                carrier.weaponManager.Fire();
                yield return null;
            }
        }

        public static object Cycle(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object value) || !(value is Aircraft aircraft) ||
                !aircraft.TryGetComponent(out MegaplaneRailgun rail) || rail.Turret == null) return new { ok = false };
            WeaponStation station = rail.Turret.GetWeaponStation();
            if (station == null) return new { ok = false };
            if (args.TryGetValue("down", out object down)) rail.Command = Convert.ToBoolean(down);
            if (args.TryGetValue("bearing", out object bearing))
            {
                aircraft.SetActiveStation(station.Number);
                rail.Turret.SetVector(aircraft.transform.TransformDirection(Quaternion.Euler(14f, Convert.ToSingle(bearing), 0f) * Vector3.forward));
            }
            // Hit the native trigger even while cycling to verify the mount rejects it mechanically.
            if (args.ContainsKey("fire")) station.Fire(aircraft, rail.Turret.GetTarget());
            Vector3 aim = rail.NativeAim;
            return new
            {
                ok = true, phase = rail.Phase.ToString(), door = rail.Door, lower = rail.Lower, live = rail.Live, selected = rail.Selected,
                progress = rail.CycleProgress, ammo = station.Ammo, recoil = rail.Recoil, flash = rail.Flash, charge = rail.Charge,
                shots = rail.Shots, aimX = aim.x, aimY = aim.y, muzzleError = rail.MuzzleError, muzzleAngle = rail.MuzzleAngle,
                clearance = DoorClearance(aircraft),
            };
        }

        private static float DoorClearance(Aircraft aircraft)
        {
            float lowerDoor = float.PositiveInfinity, upperBarrel = float.NegativeInfinity;
            foreach (MeshFilter mesh in aircraft.GetComponentsInChildren<MeshFilter>())
            {
                bool door = mesh.name.StartsWith("FighterDoor_", StringComparison.Ordinal);
                if (!door && mesh.name != "Railgun_Pitch") continue;
                Matrix4x4 local = aircraft.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                foreach (Vector3 vertex in mesh.sharedMesh.vertices)
                {
                    float y = local.MultiplyPoint3x4(vertex).y;
                    if (door) lowerDoor = Mathf.Min(lowerDoor, y);
                    else upperBarrel = Mathf.Max(upperBarrel, y);
                }
            }
            return lowerDoor - upperBarrel;
        }
    }
}
#endif
