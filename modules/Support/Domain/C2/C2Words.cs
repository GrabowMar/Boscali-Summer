using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    internal enum C2Area : byte { Orbital, Cyber, Sof }
    internal enum C2Tone : byte { Info, Warn, Danger }
    internal enum C2Tab : byte { Cap = 1, Orbit = 2, Net = 3, Sof = 4, Board = 5 }
    /// <summary>The cockpit HUD notices that wear a C2 strip: a new TASKED post, an inbound warning, the enemy intent.</summary>
    internal enum C2HudKind : byte { Tasked, Inbound, Intent, CyberHeld, CyberTraced, SofPinned, SofLost, SofDone, OpsExecute, OpsBroken, OpsDone, OpsPing }

    /// <summary>Words for the SATCOM C2 terminal. Session, key rotation and auth codes are cosmetic and deterministic.</summary>
    internal static class C2Words
    {
        public static string Banner(string faction, C2Area area)
        {
            string f = string.IsNullOrEmpty(faction) ? "FACTION" : faction.ToUpperInvariant();
            string tail = area == C2Area.Cyber ? "CYBER-EW" : area == C2Area.Sof ? "SOF-JTAC" : "ORBITAL SUPPORT C2";
            return "TOP SECRET // " + f + " EYES ONLY // " + tail;
        }

        // cosmetic: 24-bit FNV-1a over the player id and scene generation.
        public static string Session(ulong playerId, int sceneGeneration)
        {
            uint h = 2166136261u;
            for (int i = 0; i < 8; i++) h = (h ^ (byte)(playerId >> (8 * i))) * 16777619u;
            for (int i = 0; i < 4; i++) h = (h ^ (byte)((uint)sceneGeneration >> (8 * i))) * 16777619u;
            return ((h >> 16) & 0xFF).ToString("X2") + "-" + ((h >> 8) & 0xFF).ToString("X2") + "-" + (h & 0xFF).ToString("X2");
        }

        // cosmetic: counts down 300 s and wraps.
        public static string KeyRotation(float missionSeconds)
        {
            if (float.IsNaN(missionSeconds) || float.IsInfinity(missionSeconds)) missionSeconds = 0f;
            int s = (int)Math.Floor(missionSeconds);
            int left = 300 - (((s % 300) + 300) % 300);
            return (left / 60).ToString(CultureInfo.InvariantCulture) + ":" + (left % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        // cosmetic
        public static string AuthCode(int actionId, char tierInitial) =>
            ((actionId * 7919 + 4471) & 0xFFFF).ToString("X4") + "-" + tierInitial;

        public static string TabLabel(C2Tab tab, int boardCount)
        {
            string name = tab == C2Tab.Cap ? "CAP" : tab == C2Tab.Orbit ? "ORBIT" : tab == C2Tab.Net ? "NET" : tab == C2Tab.Sof ? "SOF" : "BOARD";
            string s = "[" + ((int)tab).ToString(CultureInfo.InvariantCulture) + "] " + name;
            if (tab == C2Tab.Board && boardCount > 0) s += "\u00B7" + Math.Min(boardCount, 99).ToString(CultureInfo.InvariantCulture);
            return s;
        }

        /// <summary>The warning class words of a threat strip with no distances: <c>BANDIT 12 KM</c> reads <c>BANDIT</c>.</summary>
        public static string ThreatClasses(string strip)
        {
            if (string.IsNullOrEmpty(strip)) return "";
            string[] parts = strip.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].StartsWith("BANDIT", StringComparison.Ordinal)) parts[i] = "BANDIT";
            return string.Join(" · ", parts);
        }

        public static string Fit(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return "";
            if (text.Length <= maxChars) return text;
            return maxChars == 1 ? "\u2026" : text.Substring(0, maxChars - 1) + "\u2026";
        }

        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return "\u2014";
            int s = (int)Math.Floor(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>The 14 px strip word above a HUD notice: <c>C2 // TASKED // NEW POST</c>, <c>C2 // INBOUND</c>, <c>C2 // INT</c>.</summary>
        public static string HudStrip(C2HudKind kind) =>
            kind == C2HudKind.Tasked ? "C2 // TASKED // NEW POST" : kind == C2HudKind.Inbound ? "C2 // INBOUND" :
            kind == C2HudKind.CyberHeld ? "C2 // CYBER // NODE HELD" : kind == C2HudKind.CyberTraced ? "C2 // CYBER // TRACED" :
            kind == C2HudKind.SofPinned ? "C2 // SOF // TEAM PINNED" : kind == C2HudKind.SofLost ? "C2 // SOF // TEAM LOST" : kind == C2HudKind.SofDone ? "C2 // SOF // MISSION COMPLETE" :
            kind == C2HudKind.OpsExecute ? "C2 // OPERATION // EXECUTE T-60" : kind == C2HudKind.OpsBroken ? "C2 // OPERATION // BROKEN" :
            kind == C2HudKind.OpsDone ? "C2 // OPERATION // EXECUTED" : kind == C2HudKind.OpsPing ? "C2 // OPERATION // ENEMY" : "C2 // INT";

        /// <summary>The tone of that strip: caution for a post, danger for an inbound warning, info for intent.</summary>
        public static C2Tone HudStripTone(C2HudKind kind) =>
            kind == C2HudKind.Tasked || kind == C2HudKind.CyberHeld || kind == C2HudKind.SofPinned || kind == C2HudKind.OpsExecute ? C2Tone.Warn :
            kind == C2HudKind.Inbound || kind == C2HudKind.CyberTraced || kind == C2HudKind.SofLost || kind == C2HudKind.OpsBroken || kind == C2HudKind.OpsPing ? C2Tone.Danger : C2Tone.Info;
    }
}
