using System;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Chatter.Runtime;
using HarmonyLib;

namespace BoscaliSummer.Modules.Chatter.Patches
{
    // Harmony calls the prefixes by name.
#pragma warning disable IDE0051
    /// <summary>Every kill message (host and received) passes the killer and the victim to the radio: the player's kill is confirmed, the other side reacts.</summary>
    [HarmonyPatch]
    internal static class ChatterKillPatch
    {
        private static MethodBase TargetMethod() => KillMessageRpc.Target();

        [HarmonyPrefix]
        private static void Prefix(PersistentID killerID, PersistentID killedID)
        {
            try { ChatterHost.Instance?.Flight?.OnKill(killerID, killedID); }
            catch (Exception error) { PatchGuard.Report("Chatter.Kill", error); }
        }
    }

    /// <summary>
    /// The game's centre notice box ("Cleared to taxi to Runway 21", "Stability Assist Enabled"). The radio takes a clearance as the tower's line and hands the rest to the
    /// TAC BOARD's ticker, and the native box, which sits on the heading tape, stays empty. Fails open: whatever is not taken, or throws, shows the game's own way.
    /// </summary>
    [HarmonyPatch(typeof(AircraftActionsReport), nameof(AircraftActionsReport.ReportText))]
    internal static class ChatterNoticePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(string report)
        {
            try { return !(ChatterHost.Instance?.Flight?.TakeNotice(report) ?? false); }
            catch (Exception error) { PatchGuard.Report("Chatter.Notice", error); return true; }
        }
    }
#pragma warning restore IDE0051
}
