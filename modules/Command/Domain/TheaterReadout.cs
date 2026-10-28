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
        /// The one allied-share definition: friendly / (friendly + hostile), the same number as <c>TerritoryControlRatio</c>.
        /// NaN unless both counts are known (not negative) and their sum is above zero, so an empty board never reads as a confident 0 or 100.
        /// </summary>
        public static float AlliedShare(int friendly, int hostile)
        {
            if (friendly < 0 || hostile < 0) return float.NaN;
            long total = (long)friendly + hostile;
            return total == 0 ? float.NaN : friendly / (float)total;
        }

        /// <summary>A ratio as whole percent. NaN and infinity read as an unknown dash.</summary>
        public static string Percent(float ratio) => AvNum.PercentWhole(ratio);

        /// <summary>A length in metres as kilometres, one decimal. The front's honest figure.</summary>
        public static string Kilometres(float metres)
        {
            if (!float.IsFinite(metres)) return "—";
            return (Math.Max(0f, metres) / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>
        /// Seconds since an event as a short age stamp. The log needs "how long ago" at a
        /// glance, so the unit changes with the magnitude rather than printing 738 seconds.
        /// </summary>
        public static string Age(float seconds)
        {
            if (!float.IsFinite(seconds)) return "—";
            int value = (int)Math.Round(Math.Max(0f, seconds), MidpointRounding.AwayFromZero);
            if (value < 60) return value.ToString(CultureInfo.InvariantCulture) + "s";
            if (value < 3600) return (value / 60).ToString(CultureInfo.InvariantCulture) + "m";
            return (value / 3600).ToString(CultureInfo.InvariantCulture) + "h";
        }

        /// <summary>Distinct known enemy air-defence sites among the first count rings (two cones at one site are one site); -1 while the picture is not ready, so the board prints a dash, never a zero.</summary>
        public static int KnownSites(bool ready, AirDefenceRing[] rings, int count)
        {
            if (!ready || rings == null) return -1;
            if (count > rings.Length) count = rings.Length;
            int sites = 0;
            for (int i = 0; i < count; i++)
            {
                bool counted = false;
                for (int j = 0; j < i && !counted; j++) counted = rings[j].SiteHash == rings[i].SiteHash;
                if (!counted) sites++;
            }
            return sites;
        }
    }
}
