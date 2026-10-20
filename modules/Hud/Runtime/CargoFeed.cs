using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// 10 Hz manifest over the native weapon stations of the local pilot's own live aircraft: every load aboard, the focused one (the selected station when it carries cargo, else the first with some
    /// aboard) and, when that load aims at a drop zone and one is set, the zone's distance, bearing off the nose, the height above it, the ground and closing speeds and the reach of a release from here.
    /// </summary>
    internal sealed class CargoFeed
    {
        private CargoScope cache;
        private readonly CargoLoad[] loads = new CargoLoad[CargoManifestRules.MaxLoads];
        private readonly WeaponInfo[] namedFor = new WeaponInfo[CargoManifestRules.MaxLoads];
        private readonly string[] names = new string[CargoManifestRules.MaxLoads];

        public bool Read(out CargoScope data) { data = cache; return CargoManifestRules.Present(data); }

        public void Clear()
        {
            cache = default;
            System.Array.Clear(namedFor, 0, namedFor.Length);
            System.Array.Clear(names, 0, names.Length);
        }

        public void Update(Aircraft aircraft)
        {
            cache = default;
            cache.Loads = loads;
            if (aircraft == null || aircraft.disabled || aircraft.HasEjected() || aircraft.weaponStations == null ||
                !GameManager.GetLocalAircraft(out Aircraft local) || local != aircraft) return;

            WeaponStation current = aircraft.weaponManager != null ? aircraft.weaponManager.currentWeaponStation : null;
            WeaponStation focus = null;
            int focusAmmo = 0, focusNumber = 0;
            for (int i = 0; i < aircraft.weaponStations.Count; i++)
            {
                WeaponStation station = aircraft.weaponStations[i];
                CargoKind kind = CargoStationRead.KindOf(station);
                if (kind == CargoKind.None) continue;
                CargoStationRead.ReadAmmo(station, out int ammo, out int max);
                if (kind == CargoKind.Paratroopers || kind == CargoKind.Troops) cache.Troops += ammo;
                else if (kind == CargoKind.Glider) cache.Gliders += ammo;
                else cache.Cargo += ammo;
                if (cache.LoadCount < loads.Length)
                {
                    int k = cache.LoadCount++;
                    loads[k] = new CargoLoad { Kind = kind, Name = NameOf(k, station.WeaponInfo), Station = i + 1, Ammo = ammo, Max = max, Selected = station == current };
                }
                // Keep the selected load, including an empty one; otherwise prefer something still aboard.
                if (focus == null || station == current || (focus != current && focusAmmo <= 0 && ammo > 0)) { focus = station; focusAmmo = ammo; focusNumber = i + 1; }
            }
            if (focus == null) return;
            cache.Kind = CargoStationRead.KindOf(focus);
            cache.Station = focusNumber;
            CargoStationRead.ReadAmmo(focus, out cache.Ammo, out cache.Max);
            cache.Selected = focus == current;
            cache.Safe = focus.SafetyIsOn(aircraft);
            cache.Reloading = focus.Reloading;
            for (int k = 0; k < cache.LoadCount; k++) if (loads[k].Station == focusNumber) cache.Name = loads[k].Name;
            cache.UsesLandingZone = CargoStationRead.UsesLandingZone(focus);
            cache.ZoneAvailable = ModuleServices.TryGet(out ILandingZone zones);
            if (!cache.UsesLandingZone || !cache.ZoneAvailable || !zones.TryGetLocal(out GlobalPosition zone)) return;

            cache.HasZone = true;
            Vector3 position = aircraft.GlobalPosition().AsVector3();
            Vector2 toZone = new Vector2(zone.x - position.x, zone.z - position.z);
            cache.DistanceM = toZone.magnitude;
            cache.HeightM = position.y - zone.y;
            Vector3 nose = aircraft.transform.forward;
            Vector2 heading = new Vector2(nose.x, nose.z);
            cache.BearingDeg = heading.sqrMagnitude > 1e-4f && cache.DistanceM > 1f ? -Vector2.SignedAngle(heading, toZone) : 0f;
            Vector3 velocity = aircraft.rb != null ? aircraft.rb.velocity : Vector3.zero;
            Vector2 ground = new Vector2(velocity.x, velocity.z);
            cache.GroundMps = ground.magnitude;
            cache.ClosingMps = cache.DistanceM > 1f ? Vector2.Dot(ground, toZone / cache.DistanceM) : 0f;
            cache.HasReach = cache.Kind == CargoKind.Paratroopers || CargoStationRead.IsHamilcar(focus);
            if (cache.HasReach) cache.ReachM = zones.ReachM(cache.Kind == CargoKind.Paratroopers, cache.HeightM);
        }

        /// <summary>The load's short name (the WEAPONS menu's), built once per weapon in each manifest slot.</summary>
        private string NameOf(int slot, WeaponInfo info)
        {
            if (info == namedFor[slot] && names[slot] != null) return names[slot];
            namedFor[slot] = info;
            string name = info == null ? "LOAD" : !string.IsNullOrEmpty(info.shortName) ? info.shortName : info.weaponName ?? "LOAD";
            return names[slot] = name.ToUpperInvariant();
        }
    }
}
