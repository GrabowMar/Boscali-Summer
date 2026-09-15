using System;
using System.Globalization;

namespace BoscaliSummer.Features.Command.Domain
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
        public static string Percent(float ratio)
        {
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) return "—";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero))
                   .ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A length in metres as kilometres, one decimal. The front's honest figure.</summary>
        public static string Kilometres(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres)) return "—";
            return (Math.Max(0f, metres) / 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " km";
        }

        /// <summary>
        /// How a DEFCON level reads as a rail state, so severity is carried by position on
        /// the scale rather than by a colour the caller picked.
        /// </summary>
        public static string DefconRail(int level)
        {
            if (level <= 1) return "danger";
            if (level == 2) return "contested";
            if (level == 3) return "armed";
            return "ready";
        }

        /// <summary>
        /// A node's rail: who holds it, and whether that hold is currently being argued with.
        /// </summary>
        public static string NodeRail(bool friendly, bool contested)
        {
            if (contested) return "contested";
            return friendly ? "ready" : "hostile";
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
