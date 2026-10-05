using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.C2
{
    /// <summary>The words of the ORBIT page and the full-screen station: track-file ids, button labels, the three bird cells. Pure; no game types.</summary>
    internal static class C2Orbit
    {
        /// <summary>The id a contact wears on the track file and on the CONFIRM button (the wire id, unpadded, so the two always match).</summary>
        public static string TrackId(int id) => "T" + id.ToString(CultureInfo.InvariantCulture);

        public static string ConfirmLabel(int selectedId, bool marksFull) =>
            marksFull ? "MARKS FULL" : selectedId > 0 ? "CONFIRM " + TrackId(selectedId) : "CONFIRM";

        public static string TransmitLabel(int marks) =>
            marks <= 0 ? "TRANSMIT" : "TRANSMIT " + marks.ToString(CultureInfo.InvariantCulture) + (marks == 1 ? " MARK" : " MARKS");

        public static string BirdName(BirdKind bird) =>
            bird == BirdKind.Optical ? "OPTICAL" : bird == BirdKind.Radar ? "RADAR" : "KINETIC";

        // cosmetic: a fixed altitude and inclination per bird type. Never derived from game state.
        public static string Telemetry(BirdKind bird) =>
            bird == BirdKind.Optical ? "512 KM · INC 97.4" : bird == BirdKind.Radar ? "690 KM · INC 98.1" : "540 KM · INC 63.4";

        /// <summary>
        /// The real state of one bird for its constellation cell: NO LINK without a space link, NO BIRD when the faction lacks it,
        /// OFFLINE when the family is dark, UNAVAIL / m:ss for a RADAR scan that cannot or cannot yet be taken, else READY.
        /// </summary>
        public static string BirdState(BirdKind bird, bool linked, bool hasBird, SpaceFamilyState family, int radarSeconds, bool radarUnavailable, out C2Tone tone)
        {
            tone = C2Tone.Danger;
            if (!linked) return "NO LINK";
            if (!hasBird) return "NO BIRD";
            if (family == SpaceFamilyState.Dark) return "OFFLINE";
            tone = C2Tone.Warn;
            if (bird == BirdKind.Radar)
            {
                if (radarUnavailable) return "UNAVAIL";
                if (radarSeconds > 0) return C2Words.Clock(radarSeconds);
            }
            if (family == SpaceFamilyState.Degraded) return "DEGRADED";
            tone = C2Tone.Info;
            return "READY";
        }

        /// <summary>The constellation box meta: uplinks and family, or why there is no link.</summary>
        public static string ConstellationMeta(bool known, bool active, int live, int total, SpaceFamilyState family)
        {
            if (!known) return "NO HOST LINK";
            if (!active) return "NO SPACE LINK";
            return "UPLINKS " + live.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + " · " +
                (family == SpaceFamilyState.Normal ? "NORMAL" : family == SpaceFamilyState.Degraded ? "DEGRADED" : "OFFLINE");
        }

        /// <summary>The sensor frame meta: the source and its zoom (a RADAR picture has none).</summary>
        public static string SensorMeta(BirdKind source, int zoom, bool zoomEnabled) =>
            source == BirdKind.Radar ? "RADAR · SAR" : zoomEnabled ? "OPTICAL · ZOOM " + SpaceFeedRules.ZoomWord(zoom) : "OPTICAL";

        public static string PageMeta(int page, int pages) =>
            "PAGE " + (Math.Max(0, page) + 1).ToString(CultureInfo.InvariantCulture) + "/" + Math.Max(1, pages).ToString(CultureInfo.InvariantCulture);

        /// <summary>The right-hand note of the TASKED box.</summary>
        public static string TaskedMeta(int shown) =>
            shown > 0 ? "CLAIM ARMS · EXECUTE FIRES" : "NOTHING POSTED · MARK, THEN TRANSMIT";
    }
}
