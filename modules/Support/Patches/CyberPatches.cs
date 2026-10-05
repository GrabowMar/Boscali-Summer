using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Support.Patches
{
    /// <summary>
    /// CYBER HOLD effects that need a vanilla seam (the radar range scaler needs none: it writes <c>Radar.RadarParameters</c> directly). Every
    /// body is guarded: a failure logs once and the vanilla path runs. Host only; clients never block anything.
    /// </summary>
    /// <remarks>
    /// Verified seams (nomodkit + decompile): <c>WeaponStation.LaunchMount(Unit owner, Unit target, GlobalPosition aimpoint)</c> and
    /// <c>WeaponStation.Fire(Unit owner, Unit target)</c> are what a station-driven shooter calls to launch; <c>TargetDetector.DetectTarget(Unit)</c>
    /// ends in <c>attachedUnit.NetworkHQ.RpcUpdateTrackingInfo(target.persistentID)</c>, which is how a sighting reaches the whole faction.
    /// Turret-only guns that fire through their own fire control are NOT covered: SPOOF IFF and SAM BLOCK stop what goes through a station.
    /// </remarks>
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.LaunchMount))]
    internal static class CyberLaunchMountPatch
    {
        private static bool Prefix(Unit owner)
        {
            try { return !(GameAccess.IsServer() && CyberService.Active != null && CyberService.Active.LaunchBlocked(owner)); }
            catch (Exception e) { PatchGuard.Report("Support.CyberLaunchMount", e); return true; }
        }
    }

    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.Fire))]
    internal static class CyberLaunchFirePatch
    {
        private static bool Prefix(Unit owner)
        {
            try { return !(GameAccess.IsServer() && CyberService.Active != null && CyberService.Active.LaunchBlocked(owner)); }
            catch (Exception e) { PatchGuard.Report("Support.CyberLaunchFire", e); return true; }
        }
    }

    /// <summary>The unit whose detector is inside <c>DetectTarget</c> right now (null outside it): lets the share gate know whose sighting it is.</summary>
    internal static class CyberShareContext
    {
        [ThreadStatic] internal static Unit Detecting;
    }

    [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
    internal static class CyberDetectScopePatch
    {
        private static void Prefix(TargetDetector __instance)
        {
            try { CyberShareContext.Detecting = __instance != null ? __instance.GetAttachedUnit() : null; }
            catch (Exception e) { CyberShareContext.Detecting = null; PatchGuard.Report("Support.CyberDetectScope", e); }
        }

        /// <summary>Always clears the scope, including when vanilla throws, and never swallows the exception.</summary>
        private static Exception Finalizer(Exception __exception)
        {
            CyberShareContext.Detecting = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RpcUpdateTrackingInfo))]
    internal static class CyberShareBlockPatch
    {
        private static bool Prefix()
        {
            try
            {
                Unit detecting = CyberShareContext.Detecting;
                return detecting == null || !GameAccess.IsServer() || CyberService.Active == null || !CyberService.Active.ShareBlocked(detecting);
            }
            catch (Exception e) { PatchGuard.Report("Support.CyberShareBlock", e); return true; }
        }
    }
}
