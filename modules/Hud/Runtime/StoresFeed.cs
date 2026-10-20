using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Presentation;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Hud's own provider of the STORES HEADER's feed (<see cref="StoresScope"/>): every weapon station of the aircraft the native HUD shows (class, name, rounds, reload, last shot), the one
    /// selected (<c>weaponManager.currentWeaponStation</c>) with its safety, the guns' LINK and the turrets' auto flag, the countermeasure stations of the board frame, and the power supply's
    /// charge with its measured rate. Read-only: it never selects, arms or fires anything. Refreshed at 10 Hz by <c>HudBoard</c>; the header reads the cache through <see cref="Read"/>.
    /// A part the aircraft does not have stays empty and the header draws nothing for it. Client-local: every peer reads its own aircraft.
    /// </summary>
    internal sealed class StoresFeed
    {
        private static readonly AccessTools.FieldRef<WeaponManager, bool> GunsLinked = NativeRefs.Field<WeaponManager, bool>("gunsLinked");
        private static readonly AccessTools.FieldRef<Gun, float> GunReload = NativeRefs.Field<Gun, float>("reloadTime");
        private static readonly AccessTools.FieldRef<MissileLauncher, float> LauncherReload = NativeRefs.Field<MissileLauncher, float>("reloadTime");
        private static readonly AccessTools.FieldRef<PowerSupply, float> MaxCharge = NativeRefs.Field<PowerSupply, float>("maxCharge");

        private readonly StoreStation[] stations = new StoreStation[StoresScope.MaxStations];
        private readonly string[] names = new string[StoresScope.MaxStations];
        private readonly int[] icons = new int[StoresScope.MaxStations];
        private readonly WeaponInfo[] namedFor = new WeaponInfo[StoresScope.MaxStations];
        private readonly bool[] namedCargo = new bool[StoresScope.MaxStations];
        private readonly CapacitorTrend trend = new CapacitorTrend();
        private StoresScope cache;
        private bool valid;

        /// <summary>The <see cref="ScopeProvider{T}"/> the board registers.</summary>
        public bool Read(out StoresScope data)
        {
            data = cache;
            return valid;
        }

        public void Update(Aircraft aircraft, BoardFrame frame, CombatHUD combat)
        {
            cache = default;
            valid = false;
            if (aircraft == null || aircraft.disabled) { trend.Reset(); return; }
            cache.Stations = stations;
            cache.Selected = -1;
            cache.Cm = frame.Cm;
            cache.CmCount = frame.CmCount;
            if (aircraft.weaponManager != null && aircraft.weaponStations != null) Stations(aircraft, aircraft.weaponManager, combat);
            Capacitor(aircraft, frame.Now);
            valid = cache.Selected >= 0 || cache.CmCount > 0 || cache.HasCap;
        }

        private void Stations(Aircraft aircraft, WeaponManager manager, CombatHUD combat)
        {
            List<WeaponStation> list = aircraft.weaponStations;
            WeaponStation current = manager.currentWeaponStation;
            float level = Time.timeSinceLevelLoad;
            int count = 0, slot = 0;
            // ponytail: a list past MaxStations is cut (the rail has no room for more); the selected station past the cut shows no weapon cell.
            for (int i = 0; i < list.Count && count < stations.Length; i++)
            {
                WeaponStation s = list[i];
                WeaponInfo info = s != null ? s.WeaponInfo : null;
                if (info == null) continue;
                var flags = new WeaponFlags
                {
                    Gun = info.gun, Energy = info.energy, Bomb = info.bomb || info.glideBomb, Missile = info.missile, Jammer = info.jammer,
                    Cargo = info.cargo || info.troops || info.sling, Launcher = s.Weapons.Count > 0 && s.Weapons[0] is MissileLauncher,
                    AntiAir = info.effectiveness.antiAir, AntiSurface = info.effectiveness.antiSurface,
                };
                StoreClass cls = StoresMath.ClassOf(flags);
                CargoStationRead.ReadAmmo(s, out int ammo, out int max);
                var q = new StoreStation
                {
                    Class = cls, Name = NameOf(count, s, info), Ammo = ammo, Max = max, Slot = cls == StoreClass.Gun ? 0 : ++slot,
                    Reloading = s.Reloading, ReloadElapsed = float.NaN, ReloadLeftS = float.NaN, FiredAgeS = level - s.LastFiredTime, Icon = icons[count],
                };
                if (s.Reloading) Reload(s, ref q);
                stations[count] = q;
                if (ReferenceEquals(s, current))
                {
                    cache.Selected = count;
                    cache.Safe = s.SafetyIsOn(aircraft);
                    cache.Link = manager.HasMultipleGuns() && NativeRefs.Get(GunsLinked, manager);
                    cache.TurretAuto = s.HasTurret() && combat != null && combat.turretAutoControl;
                }
                count++;
            }
            cache.StationCount = count;
        }

        /// <summary>The weapon's own name in capitals, as the native panel words it (cargo stations say so), and the handle of its picture (<see cref="StoreIcons"/>), both made only when the station's weapon changed.</summary>
        private string NameOf(int index, WeaponStation s, WeaponInfo info)
        {
            if (!ReferenceEquals(namedFor[index], info) || namedCargo[index] != s.Cargo || names[index] == null)
            {
                namedFor[index] = info;
                namedCargo[index] = s.Cargo;
                icons[index] = StoreIcons.Id(info.weaponIcon);
                names[index] = (s.Cargo ? "Cargo (" + info.weaponName + ")" : info.weaponName ?? "").ToUpperInvariant();
            }
            return names[index];
        }

        /// <summary>How far through its reload a station is and how many seconds are left. A gun's reload progress counts the time still to run; a launcher's the time spent; any other weapon says nothing.</summary>
        private static void Reload(WeaponStation s, ref StoreStation q)
        {
            if (s.Weapons.Count == 0) return;
            float progress = Mathf.Clamp01(s.GetReloadStatusMax());
            float total;
            if (s.Weapons[0] is Gun gun)
            {
                total = NativeRefs.Get(GunReload, gun);
                q.ReloadElapsed = 1f - progress;
                if (total > 0f) q.ReloadLeftS = progress * total;
            }
            else if (s.Weapons[0] is MissileLauncher launcher)
            {
                total = NativeRefs.Get(LauncherReload, launcher);
                q.ReloadElapsed = progress;
                if (total > 0f) q.ReloadLeftS = (1f - progress) * total;
            }
        }

        /// <summary>The power supply as the native capacitor bar shows it: only while something uses it, with the full charge from the supply's own capacity.</summary>
        private void Capacitor(Aircraft aircraft, float now)
        {
            PowerSupply supply = aircraft.GetPowerSupply();
            if (supply == null || supply.Users <= 0) { trend.Reset(); return; }
            float kj = supply.GetChargeKJ(), max = NativeRefs.Get(MaxCharge, supply);
            if (!(max > 0f))
            {
                float share = supply.GetCharge();
                max = share > 0.001f ? kj / share : kj;
            }
            cache.HasCap = true;
            cache.CapKj = kj;
            cache.CapMaxKj = max;
            cache.CapRate = trend.Push(now, kj);
        }

        public void Clear()
        {
            valid = false;
            trend.Reset();
        }
    }
}
