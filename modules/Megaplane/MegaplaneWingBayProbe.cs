#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplaneWingBayProbe
    {
        private sealed class DoorHistory : MonoBehaviour
        {
            internal MegaplaneWingCell[] Port, Starboard;
            internal float PeakPort, PeakStarboard;

            private void LateUpdate()
            {
                foreach (MegaplaneWingCell door in Port)
                    if (door != null) PeakPort = Mathf.Max(PeakPort, door.Open);
                foreach (MegaplaneWingCell door in Starboard)
                    if (door != null) PeakStarboard = Mathf.Max(PeakStarboard, door.Open);
            }
        }

        public static object Read(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object unit) || unit is not Aircraft aircraft || !MegaplaneModule.Is(aircraft))
                return new { ok = false };
            WeaponManager manager = aircraft.weaponManager;
            Hardpoint[] hardpoints = manager.hardpointSets.SelectMany(set => set.hardpoints)
                .Where(p => p.transform != null && p.transform.name.StartsWith(MegaplaneWingBay.HardpointPrefix, StringComparison.Ordinal))
                .ToArray();
            if (!aircraft.TryGetComponent(out DoorHistory history))
            {
                history = aircraft.gameObject.AddComponent<DoorHistory>();
                MegaplaneWingCell[] doors = aircraft.GetComponentsInChildren<MegaplaneWingCell>(true);
                history.Port = doors.Where(door => door.name.EndsWith("_L", StringComparison.Ordinal)).ToArray();
                history.Starboard = doors.Where(door => door.name.EndsWith("_R", StringComparison.Ordinal)).ToArray();
            }
            bool Remora(WeaponStation station) => station.WeaponInfo != null && station.WeaponInfo.weaponPrefab != null &&
                station.WeaponInfo.weaponPrefab.TryGetComponent(out Missile missile) && missile.definition != null && missile.definition.jsonKey == "VG_Remora";
            WeaponStation bayStation = aircraft.weaponStations.FirstOrDefault(s => Remora(s) &&
                s.Weapons.Any(w => w != null && w.GetComponentInParent<MegaplaneWingCell>() != null));
            WeaponStation external = aircraft.weaponStations.FirstOrDefault(s => Remora(s) && s != bayStation);
            if (args.TryGetValue("until", out object until) && until is string condition && condition == "ready")
                return bayStation != null && !bayStation.SafetyIsOn(aircraft) && bayStation.Ready();
            int before = bayStation != null ? bayStation.GetAmmoTotal() : 0;
            if (args.TryGetValue("fire", out object fire) && Convert.ToBoolean(fire) && bayStation != null &&
                GameManager.IsLocalAircraft(aircraft) && aircraft.LocalSim)
            {
                aircraft.SetActiveStation(bayStation.Number);
                manager.Fire();
            }
            int bayAmmo = bayStation != null ? bayStation.GetAmmoTotal() : 0;
            var releases = UnitRegistry.allUnits.OfType<Missile>()
                .Where(m => m != null && m.ownerID == aircraft.persistentID && m.definition != null && m.definition.jsonKey == "VG_Remora")
                .Select(m => new { id = m.NetId, at = aircraft.transform.InverseTransformDirection(m.NetworkstartOffsetFromOwner) })
                .ToArray();
            var fromBay = releases.Where(release => release.at.y < -4.5f && hardpoints.Any(p =>
                Mathf.Abs(aircraft.transform.InverseTransformPoint(p.transform.position).x - release.at.x) < 3.5f)).ToArray();
            var cells = hardpoints.Select(hardpoint =>
            {
                Transform point = hardpoint.transform;
                var door = point.GetComponentInParent<MegaplaneWingCell>();
                MountedMissile round = point.GetComponentInChildren<MountedMissile>(true);
                Vector3 from = round != null && round.ammo > 0 ? round.transform.position : point.position;
                bool clear = !Physics.Linecast(from, from - point.up * 6.4f, out _, ~(int)PhysicsLayers.ExclusionZonesMask,
                    QueryTriggerInteraction.Ignore);
                return new
                {
                    name = point.name, open = door != null ? door.Open : -1f, installed = round != null,
                    loaded = round != null && round.ammo > 0, fit = round == null || round.ammo <= 0 || Fits(door, point),
                    correctRail = round != null && MegaplaneWingBay.HasDropRail(round), clear,
                };
            }).ToArray();
            MegaplaneRamp cargo = aircraft.GetComponent<MegaplaneRamp>();
            return new
            {
                ok = true, configured = cells.Length, installed = cells.Count(cell => cell.installed),
                bayAmmo, externalAmmo = external != null ? external.GetAmmoTotal() : 0, triggered = bayAmmo < before,
                ramp = cargo != null ? cargo.Open : -1f,
                released = fromBay.Length, portReleased = fromBay.Count(release => release.at.x < 0f),
                starboardReleased = fromBay.Count(release => release.at.x > 0f),
                missileIds = releases.Select(release => release.id).ToArray(),
                peakPort = history.PeakPort, peakStarboard = history.PeakStarboard,
                loaded = cells.Count(cell => cell.loaded), closed = cells.All(cell => cell.open == 0f),
                fits = cells.All(cell => cell.fit), rails = cells.Count(cell => cell.correctRail),
                clear = cells.All(cell => cell.clear),
                port = cells.Where(cell => cell.name.EndsWith("_L", StringComparison.Ordinal)).Select(cell => cell.open).DefaultIfEmpty(0f).Max(),
                starboard = cells.Where(cell => cell.name.EndsWith("_R", StringComparison.Ordinal)).Select(cell => cell.open).DefaultIfEmpty(0f).Max(),
                cells,
            };
        }

        private static bool Fits(MegaplaneWingCell door, Transform point)
        {
            if (door == null) return false;
            foreach (MeshFilter mesh in point.GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.sharedMesh == null || !mesh.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
                Bounds bounds = mesh.sharedMesh.bounds;
                Matrix4x4 local = door.transform.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = local.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1)));
                    if (Mathf.Abs(p.x) > 2.88f || Mathf.Abs(p.z) > 4.25f || p.y < -3.12f || p.y > 2.45f) return false;
                }
            }
            return true;
        }
    }
}
#endif
