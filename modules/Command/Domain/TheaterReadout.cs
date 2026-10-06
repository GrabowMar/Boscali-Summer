using System;
using System.Globalization;
using BoscaliSummer.Core.Contracts;
using NOAvionics;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The arithmetic and copy behind the strategic readouts, kept free of Unity so it can
    /// be tested without a game install.
    ///
    /// <para>Formatting lives here rather than at each label because the same figure is
    /// written from several pages, and "58%" and "58.0%" appearing on adjacent rows is the
    /// kind of thing that makes an instrument look approximate.</para>
    /// </summary>
    internal static class TheaterReadout
    {
        /// <summary>
        /// The share of the board each side holds, as three fractions that sum to at most 1.
        ///
        /// <para>Contested ground is its own band rather than a shortfall in someone's bar.
        /// A single friendly-versus-total fill cannot distinguish "we hold 40%, they hold
        /// 60%" from "we hold 40%, they hold 40%, and 20% is being fought over" — which are
        /// opposite situations to fly into.</para>
        /// </summary>
        public static void Shares(
            int friendly, int contested, int hostile, int neutral,
            out float friendlyShare, out float contestedShare, out float hostileShare)
        {
            int total = Math.Max(0, friendly) + Math.Max(0, contested)
                      + Math.Max(0, hostile) + Math.Max(0, neutral);

            if (total <= 0)
            {
                friendlyShare = contestedShare = hostileShare = 0f;
                return;
            }

            friendlyShare = Math.Max(0, friendly) / (float)total;
            contestedShare = Math.Max(0, contested) / (float)total;
            hostileShare = Math.Max(0, hostile) / (float)total;
        }

        /// <summary>A ratio as whole percent. NaN and infinity read as an unknown dash.</summary>
        public static string Percent(float ratio) => AvNum.PercentWhole(ratio);

        /// <summary>A length in metres as kilometres, one decimal. The front's honest figure.</summary>
        public static string Kilometres(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return "—";
            return (Math.Max(0f, metres) / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>
        /// Seconds since an event as a short age stamp. The log needs "how long ago" at a
        /// glance, so the unit changes with the magnitude rather than printing 738 seconds.
        /// </summary>
        public static string Age(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "—";
            int value = (int)Math.Round(Math.Max(0f, seconds), MidpointRounding.AwayFromZero);
            if (value < 60) return value.ToString(CultureInfo.InvariantCulture) + "s";
            if (value < 3600) return (value / 60).ToString(CultureInfo.InvariantCulture) + "m";
            return (value / 3600).ToString(CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>
        /// The SA page's known enemy air defence in one line: distinct known sites, how many
        /// carry a radar SAM, how many are still only pre-war intel, and how many have gone
        /// stale. A picture that is not ready reads as a dash; a ready one with nothing in it
        /// says so in words rather than printing a row of confident zeroes.
        /// </summary>
        public static string KnownAirDefence(bool ready, AirDefenceRing[] rings, int count)
        {
            if (!ready || rings == null) return "—";
            if (count > rings.Length) count = rings.Length;
            int sites = 0, radar = 0, preWar = 0, stale = 0;
            for (int i = 0; i < count; i++)
            {
                int site = rings[i].SiteHash;
                bool counted = false;
                for (int j = 0; j < i && !counted; j++) counted = rings[j].SiteHash == site;
                if (counted) continue;
                bool anyRadar = false, allPreWar = true, allStale = true;
                for (int j = i; j < count; j++)
                {
                    if (rings[j].SiteHash != site) continue;
                    anyRadar |= rings[j].Kind == AirDefenceKind.RadarSam;
                    allPreWar &= rings[j].Source == RingSource.PreWar && !rings[j].Confirmed;
                    allStale &= rings[j].Stale;
                }
                sites++;
                if (anyRadar) radar++;
                if (allPreWar) preWar++;
                if (allStale) stale++;
            }
            if (sites == 0) return "NONE KNOWN";
            return sites.ToString(CultureInfo.InvariantCulture) + (sites == 1 ? " SITE · " : " SITES · ") +
                   radar.ToString(CultureInfo.InvariantCulture) + " RADAR (" +
                   preWar.ToString(CultureInfo.InvariantCulture) + " PRE-WAR, " +
                   stale.ToString(CultureInfo.InvariantCulture) + " STALE)";
        }

        /// <summary>
        /// Pressure as a word, for rows whose figure column already carries the number.
        /// Below the noise floor it is not pressure, it is a rounding artefact, and calling
        /// it pressure would overstate what is known.
        /// </summary>
        public static string PressureState(float captureProgress)
        {
            if (float.IsNaN(captureProgress) || float.IsInfinity(captureProgress)) return "UNKNOWN";
            if (captureProgress < 0.05f) return "HOLDING";
            if (captureProgress >= 0.75f) return "FALLING";
            return "UNDER PRESSURE";
        }
    }
}
