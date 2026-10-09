using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>SKYWELL view of a receiver's weapon stations: what a handoff can serve, which rail is next, where it is.</summary>
    internal static class SkywellStations
    {
        // Unboxed accessor (StationPoint runs twice a frame during a handoff): null if a game update renames the field.
        private static readonly AccessTools.FieldRef<MountedMissile, Vector3> MountedPosition = FindMountedPosition();

        // Upper-case HUD names, built once per weapon (the HUD asks every 0.2 s and the radial every frame).
        private static readonly Dictionary<WeaponInfo, string> Names = new Dictionary<WeaponInfo, string>();

        private static AccessTools.FieldRef<MountedMissile, Vector3> FindMountedPosition()
        {
            try { return AccessTools.FieldRefAccess<MountedMissile, Vector3>("mountedPosition"); }
            catch (Exception) { return null; }
        }

        /// <summary>Stations the kit never serves: cargo, nuclear, massless, or not rearmable.</summary>
        public static bool Skip(WeaponStation s)
        {
            WeaponInfo info = s?.WeaponInfo;
            return info == null || info.cargo || info.nuclear || info.massPerRound <= 0f ||
                   s.Weapons == null || s.Weapons.Count == 0 || s.Weapons[0] == null || !s.Weapons[0].Rearmable;
        }

        /// <summary>One entry per <c>r.weaponStations</c> index, from live ammo.</summary>
        public static void Collect(Aircraft r, List<PitStation> into)
        {
            into.Clear();
            if (r == null || r.weaponStations == null) return;
            for (int i = 0; i < r.weaponStations.Count; i++)
            {
                WeaponStation s = r.weaponStations[i];
                WeaponInfo info = s?.WeaponInfo;
                bool skip = Skip(s);
                into.Add(new PitStation
                {
                    Index = i,
                    Missing = s == null ? 0 : Mathf.Max(0, s.FullAmmo - s.GetAmmoTotal()),
                    Full = s == null ? 0 : s.FullAmmo,
                    MassPerRound = info != null ? info.massPerRound : 0f,
                    CostPerRound = info != null ? info.costPerRound : 0f,
                    Skip = skip,
                    // Only missile racks reload one rail per call. Guns, big pods and any other multi-weapon station swap as one
                    // cassette: vanilla Rearm splits the count across the weapons (floor), so a one-round call could load nothing.
                    Belt = !skip && (info.gun || s.FullAmmo > 12 || !(s.Weapons[0] is MountedMissile)),
                });
            }
        }

        /// <summary>The weapon the next vanilla <c>Rearm(1)</c> reloads: missile racks refill from the last empty rail backwards.</summary>
        public static Weapon NextEmpty(WeaponStation s)
        {
            if (s == null || s.Weapons == null || s.Weapons.Count == 0) return null;
            if (s.Weapons[0] is MountedMissile)
                for (int i = s.Weapons.Count - 1; i >= 0; i--)
                    if (s.Weapons[i] != null && s.Weapons[i].GetAmmoLoaded() != 1) return s.Weapons[i];
            return s.Weapons[0];
        }

        /// <summary>World point of the rail the loader reloads. A fired rail slides away, so a missile mount uses its own mounted slot.</summary>
        public static Vector3 StationPoint(Aircraft r, int index)
        {
            if (r == null) return Vector3.zero;
            Weapon w = r.weaponStations != null && index >= 0 && index < r.weaponStations.Count ? NextEmpty(r.weaponStations[index]) : null;
            if (w == null) return SkywellBoard.PylonPoint(r);
            Transform t = w.transform;
            Vector3 p = w is MountedMissile mounted && t.parent != null && MountedPosition != null
                ? t.parent.TransformPoint(MountedPosition(mounted))
                : t.position;
            // A weapon parked at the airframe origin means the anchor is not a real rail: fall back to the bounds heuristic.
            return (p - r.transform.position).sqrMagnitude < 0.25f ? SkywellBoard.PylonPoint(r) : p;
        }

        /// <summary>Upper-case station name for the HUD, at most 14 characters.</summary>
        public static string Describe(WeaponStation s)
        {
            WeaponInfo info = s?.WeaponInfo;
            if (info == null) return "STATION";
            if (Names.TryGetValue(info, out string cached)) return cached;
            string name = (!string.IsNullOrEmpty(info.shortName) ? info.shortName : info.weaponName ?? "STATION").ToUpperInvariant();
            return Names[info] = name.Length <= 14 ? name : name.Substring(0, 14);
        }
    }
}
