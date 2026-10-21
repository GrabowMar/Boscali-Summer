using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Runtime;
using HarmonyLib;
using NuclearOption;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Patches
{
    /// <summary>The CUE stack's one ledger (client-local, presentation only): every message the game puts on one of its grey boxes lands here from the patches below, with the game's own time for it.</summary>
    internal static class CueFeed
    {
        public static readonly CueLog Log = new CueLog();

        /// <summary>The sources whose cues are being drawn right now (bit <see cref="CueSource"/>): written every frame by the board's corners, read by <see cref="TakeNotice"/>.</summary>
        public static int Charge;

        public static Cue Capture(CueSource source, string rich, CueSeverity severity, float seconds) => Log.Add(source, rich, severity, Time.timeSinceLevelLoad, seconds);

        /// <summary>
        /// A notice the TAC BOARD's ticker was about to take from the "sys" channel (the radio module sends the game's centre notices there, in capitals, and keeps them off the game's own box): while the action report's cues are
        /// drawn they take it instead, so a message is shown once. The tone says what the game's colour did (its alert colour arrives as a caution). False leaves it to the ticker.
        /// </summary>
        /// <summary>How long a mission's task line stays up.</summary>
        public const float TaskSeconds = 12f;

        /// <summary>
        /// A mission script's hint (the board's "task" notice channel), as an AMBER cue under the class word MSN, the way the game's ARMED and DISARMED come up. Only while the action report's cues are drawn; false leaves it to
        /// the ticker.
        /// </summary>
        public static bool TakeTask(string text)
        {
            if ((Charge >> (int)CueSource.Action & 1) == 0 || string.IsNullOrEmpty(text)) return false;
            return Log.Add(CueSource.Action, text, CueSeverity.Caution, Time.timeSinceLevelLoad, TaskSeconds, CueClass.Msn) != null;
        }

        public static bool TakeNotice(string text, HudTone tone)
        {
            if ((Charge >> (int)CueSource.Action & 1) == 0 || string.IsNullOrEmpty(text)) return false;
            return Capture(CueSource.Action, text, tone == HudTone.Info ? CueSeverity.Normal : CueSeverity.Alert, CueLog.DefaultSeconds) != null;
        }

        /// <summary>The words of a reserve notice: the aircraft and what became of its reservation. Null for an outcome the game shows no notice for.</summary>
        public static string ReserveLine(ReserveEvent outcome, string aircraft, out CueSeverity severity)
        {
            string name = string.IsNullOrEmpty(aircraft) ? "AIRCRAFT" : aircraft;
            severity = CueSeverity.Normal;
            switch (outcome)
            {
                case ReserveEvent.granted: return name + " RESERVE GRANTED";
                case ReserveEvent.cancelledAfford: severity = CueSeverity.Caution; return name + " RESERVE CANCELLED (FUNDS)";
                case ReserveEvent.cancelledRank: severity = CueSeverity.Alert; return name + " RESERVE CANCELLED (RANK)";
                default: return null;
            }
        }
    }

    /// <summary>
    /// Captures what <c>AircraftActionsReport.ReportText</c> puts in the game's action box (weapon safety, flight assist, radar, gear, damage failures: every caller, the mod's own included). A postfix: it reads the game's text
    /// after the call and keeps the message only when the game's box took it. A notice the radio or the ticker took instead (the radio module's prefix skips the original) never reached the box and is not a cue (it comes
    /// through <see cref="CueFeed.TakeNotice"/> when it went to the ticker). The alert colour on the game's text marks a failure. Read-only: nothing the game does is changed.
    /// </summary>
    [HarmonyPatch(typeof(AircraftActionsReport), nameof(AircraftActionsReport.ReportText))]
    internal static class CueReportPatch
    {
        private static readonly AccessTools.FieldRef<AircraftActionsReport, TextMeshProUGUI> TextRef = NativeRefs.Field<AircraftActionsReport, TextMeshProUGUI>("actionsText");

        private static void Postfix(AircraftActionsReport __instance, string report, float displayTime)
        {
            try
            {
                if (string.IsNullOrEmpty(report)) return;
                TextMeshProUGUI box = NativeRefs.Get(TextRef, __instance);
                if (box == null || !box.text.EndsWith(report, StringComparison.Ordinal)) return;
                CueFeed.Capture(CueSource.Action, report, CueText.Coloured(report) ? CueSeverity.Alert : CueSeverity.Normal, displayTime);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.CueFeed.Action", e);
            }
        }
    }

    /// <summary>The reserve notice (<c>ReserveReport</c>: an aircraft icon and "+1", "+$" or "x" over five seconds). A postfix on its handler, after the game has shown it (the notice object is active); only the three outcomes the game shows.</summary>
    [HarmonyPatch(typeof(ReserveReport), "ReserveReport_OnReserveNotice")]
    internal static class CueReservePatch
    {
        private static void Postfix(ReserveReport __instance, ReserveNotice reserveNotice)
        {
            try
            {
                if (__instance == null || !__instance.gameObject.activeSelf) return;
                string line = CueFeed.ReserveLine(reserveNotice.outcome, reserveNotice.aircraftDefinition != null ? reserveNotice.aircraftDefinition.unitName : null, out CueSeverity severity);
                if (line != null) CueFeed.Capture(CueSource.Reserve, line, severity, 5f);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.CueFeed.Reserve", e);
            }
        }
    }

    /// <summary>The funds line (<c>AllocationDisplay.Show</c>: the balance and what changed, six seconds). The game fills its readout whatever happens and shows it only while the display is visible, so the cue is kept only when the game's object came up.</summary>
    [HarmonyPatch(typeof(AllocationDisplay), nameof(AllocationDisplay.Show))]
    internal static class CueAllocationPatch
    {
        private static readonly AccessTools.FieldRef<AllocationDisplay, TMP_Text> ReadoutRef = NativeRefs.Field<AllocationDisplay, TMP_Text>("allocationReadout");

        private static void Postfix(AllocationDisplay __instance, float change)
        {
            try
            {
                if (__instance == null || !__instance.enabled || !__instance.gameObject.activeSelf) return;
                TMP_Text readout = NativeRefs.Get(ReadoutRef, __instance);
                if (readout != null && !string.IsNullOrEmpty(readout.text)) CueFeed.Capture(CueSource.Allocation, readout.text, change < 0f ? CueSeverity.Caution : CueSeverity.Normal, 6f);
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.CueFeed.Allocation", e);
            }
        }
    }

    /// <summary>The score line (<c>KillDisplay.killText</c>: "Detected units +1,0", a kill's credit, five seconds), set by <c>DisplayKill</c> and <c>DisplayBonus</c>. A postfix on each reads the game's text after the call.</summary>
    [HarmonyPatch(typeof(KillDisplay), nameof(KillDisplay.DisplayKill))]
    internal static class CueScorePatch
    {
        internal static readonly AccessTools.FieldRef<KillDisplay, TextMeshProUGUI> TextRef = NativeRefs.Field<KillDisplay, TextMeshProUGUI>("killText");

        internal static void Keep(KillDisplay display)
        {
            TextMeshProUGUI text = NativeRefs.Get(TextRef, display);
            if (text != null && text.enabled && !string.IsNullOrEmpty(text.text)) CueFeed.Capture(CueSource.Score, text.text, CueSeverity.Normal, 5f);
        }

        private static void Postfix(KillDisplay __instance)
        {
            try { Keep(__instance); }
            catch (Exception e) { PatchGuard.Report("Hud.CueFeed.Score", e); }
        }
    }

    [HarmonyPatch(typeof(KillDisplay), nameof(KillDisplay.DisplayBonus))]
    internal static class CueBonusPatch
    {
        private static void Postfix(KillDisplay __instance, float scoreAwarded)
        {
            try { if (scoreAwarded != 0f) CueScorePatch.Keep(__instance); }
            catch (Exception e) { PatchGuard.Report("Hud.CueFeed.Bonus", e); }
        }
    }
}
