using System;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.CombatAdjust.Runtime;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Patches
{
    /// <summary>Thread-static scopes the patches below share.</summary>
    internal static class CombatScope
    {
        /// <summary>The detector inside <c>DetectTarget</c> right now (host, link discipline on); null outside it.</summary>
        [ThreadStatic] internal static TargetDetector Detecting;

        /// <summary>True inside <c>IsTargetPositionAccurate</c>: the accuracy gates compare against the unfogged position.</summary>
        [ThreadStatic] internal static bool FogBypass;
    }

    /// <summary>
    /// Opens the "who is detecting" scope for the share gate. Same shape as Support's CyberDetectScopePatch, kept separate so neither module
    /// imports the other; it touches the thread-static only on the host with link discipline on.
    /// </summary>
    [HarmonyPatch(typeof(TargetDetector), nameof(TargetDetector.DetectTarget))]
    internal static class CombatDetectScopePatch
    {
        private static void Prefix(TargetDetector __instance, out TargetDetector __state)
        {
            __state = null;
            CombatAdjustService s = CombatAdjustService.Active;
            if (s == null || !s.LinkOn || !GameAccess.IsServer()) return;
            __state = CombatScope.Detecting;
            CombatScope.Detecting = __instance;
        }

        private static Exception Finalizer(Exception __exception, TargetDetector __state)
        {
            CombatAdjustService s = CombatAdjustService.Active;
            if (s != null && s.LinkOn && GameAccess.IsServer()) CombatScope.Detecting = __state;
            return __exception;
        }
    }

    /// <summary>Link discipline: a sighting inside a detector's scope reaches the faction picture only by <c>LinkRules</c>. Every other report passes.</summary>
    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.RpcUpdateTrackingInfo))]
    internal static class CombatShareGatePatch
    {
        private static bool Prefix(PersistentID id)
        {
            TargetDetector detector = CombatScope.Detecting;
            if (detector == null) return true;
            CombatAdjustService s = CombatAdjustService.Active;
            if (s == null || !s.LinkOn) return true;
            try { return s.LinkShares(detector, id); }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.ShareGate", e); return true; }
        }
    }

    /// <summary>Track fog: a contact nobody is reporting drifts from its last point. Live contacts and the accuracy gates see vanilla.</summary>
    [HarmonyPatch(typeof(TrackingInfo), nameof(TrackingInfo.GetPosition))]
    internal static class CombatFogPatch
    {
        private static void Postfix(TrackingInfo __instance, ref GlobalPosition __result)
        {
            if (CombatScope.FogBypass) return;
            CombatAdjustService s = CombatAdjustService.Active;
            if (s == null || !s.FogOn) return;
            try { __result += s.FogOffset(__instance); }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.Fog", e); }
        }
    }

    [HarmonyPatch(typeof(FactionHQ), nameof(FactionHQ.IsTargetPositionAccurate))]
    internal static class CombatAccuracyScopePatch
    {
        private static void Prefix(out bool __state)
        {
            __state = CombatScope.FogBypass;
            CombatScope.FogBypass = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            CombatScope.FogBypass = __state;
            return __exception;
        }
    }

    /// <summary>Every peer: the missile joins the book (smoke and decoy visuals are judged from it everywhere).</summary>
    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class CombatMissileStartPatch
    {
        private static void Postfix(Missile __instance)
        {
            try { CombatAdjustService.Active?.OnMissileStart(__instance); }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.MissileStart", e); }
        }
    }

    /// <summary>Host (LocalStart runs where the missile is simulated): faults are rolled before the seeker initializes, so a dud's fuse is never set.</summary>
    [HarmonyPatch(typeof(Missile), "LocalStart")]
    internal static class CombatMissileLocalStartPatch
    {
        private static void Prefix(Missile __instance)
        {
            try { CombatAdjustService.Active?.OnLocalStart(__instance); }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.LocalStart", e); }
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.SetProxyFuse))]
    internal static class CombatProxyFusePatch
    {
        private static bool Prefix(Missile __instance, Transform targetTransform)
        {
            if (targetTransform == null) return true;
            CombatAdjustService s = CombatAdjustService.Active;
            return s == null || !s.IsDud(__instance);
        }
    }

    /// <summary>The one seam every seeker steers through: seeker failure, smoke and decoys add their offset to the aim point (host).</summary>
    [HarmonyPatch(typeof(Missile), nameof(Missile.SetAimpoint))]
    internal static class CombatAimPatch
    {
        private static void Prefix(Missile __instance, ref GlobalPosition aimPoint)
        {
            CombatAdjustService s = CombatAdjustService.Active;
            if (s != null && s.TryAimOffset(__instance, out Vector3 offset)) aimPoint += offset;
        }
    }

    /// <summary>Smoke hides a vehicle from eyes: no line-of-sight check is queued through an active cloud. Radar uses RequestRadarCheck and is unaffected.</summary>
    [HarmonyPatch(typeof(DetectorManager), nameof(DetectorManager.RequestLoSCheck))]
    internal static class CombatSmokeLosPatch
    {
        private static bool Prefix(TargetDetector detector, Unit target)
        {
            CombatAdjustService s = CombatAdjustService.Active;
            if (s == null || !s.SmokeOn || !s.AnySmoke) return true;
            try { return !s.SmokeBlocksSight(detector, target); }
            catch (Exception e) { PatchGuard.Report("CombatAdjust.SmokeLoS", e); return true; }
        }
    }
}
