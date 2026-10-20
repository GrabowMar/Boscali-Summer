using System;
using System.Reflection;
using BoscaliSummer.Core.Contracts;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>Read-only cargo identity and passenger counts from the actual fitted weapons, including mounts outside the airframe hierarchy.</summary>
    internal static class CargoStationRead
    {
        private static readonly FieldInfo MountField = AccessTools.Field(typeof(Weapon), "mount");

        private static string Key(Weapon weapon) =>
            weapon != null && MountField?.GetValue(weapon) is WeaponMount mount ? mount.jsonKey : null;

        private static bool Is(string key, string root) => key != null &&
            (key == root || key.StartsWith(root + "_", StringComparison.Ordinal));

        public static CargoKind KindOf(WeaponStation station)
        {
            if (station == null) return CargoKind.None;
            CargoKind kind = station.Cargo || station.WeaponInfo != null &&
                (station.WeaponInfo.cargo || station.WeaponInfo.sling) ? CargoKind.Cargo : CargoKind.None;
            if (station.WeaponInfo != null && station.WeaponInfo.troops) kind = CargoKind.Troops;
            foreach (Weapon weapon in station.Weapons)
            {
                string key = Key(weapon);
                if (Is(key, "VG_Hamilcar") || Is(key, "VG_HawcX") || Is(key, "VG_Kestrel")) return CargoKind.Glider;
                if (key == "AB_Paratroopers_x16") return CargoKind.Paratroopers;
                if (weapon is MountedTroops) kind = CargoKind.Troops;
            }
            return kind;
        }

        /// <summary>KESTREL follows its designation; HALO, HAMILCAR and HAWC-X read the standing zone once at release.</summary>
        public static bool UsesLandingZone(WeaponStation station)
        {
            if (station == null) return false;
            foreach (Weapon weapon in station.Weapons)
            {
                string key = Key(weapon);
                if (key == "AB_Paratroopers_x16" || Is(key, "VG_Hamilcar") || Is(key, "VG_HawcX")) return true;
            }
            return false;
        }

        public static bool IsHamilcar(WeaponStation station)
        {
            if (station == null) return false;
            foreach (Weapon weapon in station.Weapons) if (Is(Key(weapon), "VG_Hamilcar")) return true;
            return false;
        }

        /// <summary>Native troop GetFullAmmo returns one bench and GetAmmoTotal reads host capture strength. The local replayed passenger ammo and mount capacity are the display counts.</summary>
        public static void ReadAmmo(WeaponStation station, out int ammo, out int max)
        {
            ammo = station != null ? Mathf.Max(0, station.Ammo) : 0;
            max = station != null ? Mathf.Max(ammo, station.FullAmmo) : 0;
            if (station == null) return;
            int aboard = 0, capacity = 0;
            bool troops = false;
            foreach (Weapon weapon in station.Weapons)
            {
                if (!(weapon is MountedTroops passengers)) continue;
                troops = true;
                if (passengers.IsAttached()) aboard += Mathf.Max(0, passengers.ammo);
                capacity += Mathf.Max(0, MountField?.GetValue(passengers) is WeaponMount mount ? mount.ammo : passengers.ammo);
            }
            if (!troops) return;
            ammo = aboard;
            max = Mathf.Max(aboard, capacity);
        }
    }
}
