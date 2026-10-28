using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    internal enum C2Tone : byte { Info, Warn, Danger }
    /// <summary>The cockpit HUD notices that wear a C2 strip: a new TASKED post, an inbound warning, the enemy intent.</summary>
    internal enum C2HudKind : byte { Tasked, Inbound, Intent, CyberHeld, CyberTraced, SofPinned, SofLost, SofDone, OpsExecute, OpsBroken, OpsDone, OpsPing, FrontUp, FrontFailed, FrontLost }

    /// <summary>Words for the SATCOM C2 terminal. Session, key rotation and auth codes are cosmetic and deterministic.</summary>
    internal static class C2Words
    {

        public static string Fit(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return "";
            if (text.Length <= maxChars) return text;
            return maxChars == 1 ? "\u2026" : text.Substring(0, maxChars - 1) + "\u2026";
        }

        public static string Clock(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0f) return "\u2014";
            int s = (int)Math.Floor(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The 14 px strip word above a HUD notice: <c>C2 // TASKED // NEW POST</c>, <c>C2 // INBOUND</c>, <c>C2 // INT</c>.</summary>
        public static string HudStrip(C2HudKind kind) =>
            kind == C2HudKind.Tasked ? "C2 // TASKED // NEW POST" : kind == C2HudKind.Inbound ? "C2 // INBOUND" :
            kind == C2HudKind.CyberHeld ? "C2 // CYBER // NODE HELD" : kind == C2HudKind.CyberTraced ? "C2 // CYBER // TRACED" :
            kind == C2HudKind.SofPinned ? "C2 // SOF // TEAM PINNED" : kind == C2HudKind.SofLost ? "C2 // SOF // TEAM LOST" : kind == C2HudKind.SofDone ? "C2 // SOF // MISSION COMPLETE" :
            kind == C2HudKind.OpsExecute ? "C2 // OPERATION // EXECUTE T-60" : kind == C2HudKind.OpsBroken ? "C2 // OPERATION // BROKEN" :
            kind == C2HudKind.OpsDone ? "C2 // OPERATION // EXECUTED" : kind == C2HudKind.OpsPing ? "C2 // OPERATION // ENEMY" :
            kind == C2HudKind.FrontUp ? "C2 // FRONT // DONE" : kind == C2HudKind.FrontFailed ? "C2 // FRONT // FAILED" : kind == C2HudKind.FrontLost ? "C2 // FRONT // ASSET LOST" : "C2 // INT";

        /// <summary>The tone of that strip: caution for a post, danger for an inbound warning, info for intent.</summary>
        public static C2Tone HudStripTone(C2HudKind kind) =>
            kind == C2HudKind.Tasked || kind == C2HudKind.CyberHeld || kind == C2HudKind.SofPinned || kind == C2HudKind.OpsExecute || kind == C2HudKind.FrontFailed ? C2Tone.Warn :
            kind == C2HudKind.Inbound || kind == C2HudKind.CyberTraced || kind == C2HudKind.SofLost || kind == C2HudKind.OpsBroken || kind == C2HudKind.OpsPing || kind == C2HudKind.FrontLost ? C2Tone.Danger : C2Tone.Info;
    }
}
