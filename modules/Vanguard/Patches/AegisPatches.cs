using System;
using System.Collections;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Presentation;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Vanguard.Patches
{
    // AEGIS-3 CM: attach each pod's MountedMissile cells to their hardpoint and register one AegisCountermeasure
    // per pod with CountermeasureManager instead of creating a WeaponStation in the weapons selector.
    [HarmonyPatch]
    internal static class AegisRegisterPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(WeaponManager), nameof(WeaponManager.RegisterWeapon));
        private static bool Prepare() => TargetMethod() != null && AegisCountermeasure.HasNativeRail;

        private static bool Prefix(Weapon weapon, WeaponMount weaponMount, Hardpoint hardpoint, Aircraft ___aircraft)
        {
            if (weapon == null || !(weapon is MountedMissile cell) || !AegisTrigger.IsAegis(cell.info)) return true;
            try
            {
                if (___aircraft == null || hardpoint == null) return true;
                cell.AttachToHardpoint(___aircraft, hardpoint, weaponMount);
                Transform pod = cell.transform.parent != null ? cell.transform.parent : cell.transform;
                AegisCountermeasure cm = pod.GetComponent<AegisCountermeasure>();
                if (cm == null) cm = pod.gameObject.AddComponent<AegisCountermeasure>();
                cm.AddCell(cell, ___aircraft, hardpoint);
            }
            catch (Exception error)
            {
                PatchGuard.Report("Vanguard.AegisRegister", error);
                return true;
            }
            return false;
        }
    }

    // Keep the AEGIS countermeasure station after vanilla Flares/Jammer so index 0 stays Flares (default selection,
    // PopFlares and GetFlareAmmoProportion). Runs identically on every peer, preserving activeIndex sync.
    [HarmonyPatch]
    internal static class AegisCountermeasureOrderPatch
    {
        internal static readonly FieldInfo StationsField = AccessTools.Field(typeof(CountermeasureManager), "countermeasureStations");
        private static FieldInfo nameField;

        internal static string StationName(object station)
        {
            if (station == null) return null;
            nameField ??= AccessTools.Field(station.GetType(), "displayName");
            return nameField?.GetValue(station) as string;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(CountermeasureManager), nameof(CountermeasureManager.RegisterCountermeasure));
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(CountermeasureManager __instance)
        {
            try
            {
                if (__instance == null || !(StationsField?.GetValue(__instance) is IList stations) || stations.Count <= 1) return;
                int aegisIndex = -1;
                for (int i = 0; i < stations.Count; i++)
                {
                    if (StationName(stations[i]) == AegisIntercept.DisplayName)
                    {
                        aegisIndex = i;
                        break;
                    }
                }
                if (aegisIndex >= 0 && aegisIndex < stations.Count - 1)
                {
                    object aegisStation = stations[aegisIndex];
                    stations.RemoveAt(aegisIndex);
                    stations.Add(aegisStation);
                }
            }
            catch (Exception error)
            {
                PatchGuard.Report("Vanguard.AegisCmOrder", error);
            }
        }
    }

    // Losing the selected pod's last station can leave the game's index beyond the end of the cycle.
    [HarmonyPatch]
    internal static class AegisCountermeasureRemovalPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(CountermeasureManager), "DeregisterCountermeasure");
        private static bool Prepare() => TargetMethod() != null && AegisCountermeasureOrderPatch.StationsField != null;

        private static void Postfix(CountermeasureManager __instance, Countermeasure countermeasure)
        {
            if (!(countermeasure is AegisCountermeasure)) return;
            try
            {
                if (!(AegisCountermeasureOrderPatch.StationsField.GetValue(__instance) is IList stations) || __instance.activeIndex == byte.MaxValue) return;
                bool selectionRemoved = __instance.activeIndex >= stations.Count;
                if (selectionRemoved)
                    __instance.activeIndex = stations.Count > 0 ? (byte)0 : byte.MaxValue;
                if (selectionRemoved || AegisCountermeasureOrderPatch.StationName(stations[__instance.activeIndex]) == AegisIntercept.DisplayName)
                    AegisCombatHudPatch.RefreshSelection(__instance, requirePod: false);
            }
            catch (Exception error)
            {
                PatchGuard.Report("Vanguard.AegisCmRemoval", error);
            }
        }
    }

    // The stock rearm loop paints each station in turn, leaving the last station's data on screen.
    [HarmonyPatch]
    internal static class AegisCountermeasureRearmPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(CountermeasureManager), "Rearm");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(CountermeasureManager __instance)
        {
            try { AegisCombatHudPatch.RefreshSelection(__instance, requirePod: true); }
            catch (Exception error) { PatchGuard.Report("Vanguard.AegisCmRearm", error); }
        }
    }

    // The cockpit app treats every non-flare as a jammer; render AEGIS before it reads the capacitor.
    [HarmonyPatch]
    internal static class AegisIndicatorPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(CountermeasureIndicator), nameof(CountermeasureIndicator.Refresh));
        private static bool Prepare() => TargetMethod() != null &&
            AccessTools.Field(typeof(CountermeasureIndicator), "aircraft")?.FieldType == typeof(Aircraft) &&
            AccessTools.Field(typeof(CountermeasureIndicator), "counterImage")?.FieldType == typeof(Image) &&
            AccessTools.Field(typeof(CountermeasureIndicator), "counterName")?.FieldType == typeof(Text) &&
            AccessTools.Field(typeof(CountermeasureIndicator), "counterAmmo")?.FieldType == typeof(Text);

        private static bool Prefix(CountermeasureIndicator __instance, Aircraft ___aircraft, Image ___counterImage, Text ___counterName, Text ___counterAmmo)
        {
            try
            {
                if (__instance == null || !PlayerSettings.hudWeapons || ___counterImage == null || ___counterName == null || ___counterAmmo == null) return true;
                Countermeasure active = AircraftReadout.ActiveCountermeasure(___aircraft);
                if (!(active is AegisCountermeasure aegis))
                {
                    if (active != null && ___counterName.text == AegisIntercept.DisplayName) ___counterImage.enabled = true;
                    return true;
                }

                int ammo = AegisTrigger.TotalAmmo(___aircraft);
                Color color = ammo > 0 ? Color.green : Color.grey;
                if (!__instance.gameObject.activeSelf) __instance.gameObject.SetActive(true);
                ___counterImage.enabled = aegis.displayImage != null;
                ___counterImage.sprite = aegis.displayImage;
                ___counterImage.color = color;
                ___counterName.text = aegis.displayName;
                ___counterName.color = color;
                ___counterAmmo.text = ammo.ToString();
                ___counterAmmo.color = color;
                return false;
            }
            catch (Exception error)
            {
                PatchGuard.Report("Vanguard.AegisIndicator", error);
                return true;
            }
        }
    }

    // The stock station's final ammo update hides single charges; a dart count needs to show 1 and 0.
    [HarmonyPatch]
    internal static class AegisCombatHudPatch
    {
        internal static void RefreshSelection(CountermeasureManager manager, bool requirePod)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            Aircraft aircraft = hud != null ? hud.aircraft : null;
            if (aircraft == null || aircraft.disabled || aircraft.countermeasureManager != manager ||
                !GameManager.IsLocalAircraft(aircraft) || (requirePod && !AegisTrigger.HasPods(aircraft))) return;

            Countermeasure active = AircraftReadout.ActiveCountermeasure(aircraft);
            if (active == null) return;
            if (AegisCountermeasureOrderPatch.StationsField?.GetValue(manager) is IList stations && manager.activeIndex < stations.Count &&
                AegisCountermeasureOrderPatch.StationName(stations[manager.activeIndex]) != active.displayName)
                active.UpdateHUD();      // a virtual station (such as supplemental chaff) owns its own readout
            else manager.UpdateHUD();
        }

        private static MethodBase TargetMethod() => AccessTools.Method(typeof(CombatHUD), nameof(CombatHUD.DisplayCountermeasureAmmo));
        private static bool Prepare() => TargetMethod() != null &&
            AccessTools.Field(typeof(CombatHUD), "countermeasureName")?.FieldType == typeof(TextMeshProUGUI) &&
            AccessTools.Field(typeof(CombatHUD), "countermeasureAmmo")?.FieldType == typeof(TextMeshProUGUI);

        private static void Postfix(TextMeshProUGUI ___countermeasureName, TextMeshProUGUI ___countermeasureAmmo)
        {
            try
            {
                if (___countermeasureName != null && ___countermeasureName.text == AegisIntercept.DisplayName && ___countermeasureAmmo != null)
                    ___countermeasureAmmo.enabled = true;
            }
            catch (Exception error)
            {
                PatchGuard.Report("Vanguard.AegisCombatHud", error);
            }
        }
    }

    // StartMissile runs on every peer: the released dart's grid fins snap open from its age.
    [HarmonyPatch]
    internal static class AegisFinsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Missile), "StartMissile");
        private static bool Prepare() => TargetMethod() != null;

        private static void Postfix(Missile __instance)
        {
            if (__instance != null && __instance.definition != null) AegisFins.Attach(__instance);
        }
    }
}
